#if !SPF_DOTNET_HARNESS
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SPF.Testing
{
    /// <summary>Test-only bounded readback buffer. PNG encoding and file writes happen after acquisition.
    /// Captures the supplied already-rendered target; it does not advance simulation or manufacture frames.</summary>
    public sealed class BufferedFrameCapture : IDisposable
    {
        public const long MaximumBufferBytes = 128L * 1024 * 1024;
        readonly RenderTexture m_Target;
        readonly Texture2D m_Read;
        readonly byte[][] m_Frames;
        readonly double[] m_Times, m_SimulationTimes;
        bool m_Disposed;
        public int Count { get; private set; }
        public int Capacity => m_Frames.Length;
        public int Width { get; }
        public int Height { get; }
        public long BufferBytes { get; }

        public BufferedFrameCapture(RenderTexture target, int capacity)
        {
            if (target == null || !target.IsCreated()) throw new ArgumentException("A created capture target is required.", nameof(target));
            if (capacity < 2 || capacity > 512) throw new ArgumentOutOfRangeException(nameof(capacity));
            Width = target.width; Height = target.height;
            BufferBytes = checked((long)Width * Height * 4 * capacity);
            if (Width < 1 || Height < 1 || BufferBytes > MaximumBufferBytes)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Raw capture storage must fit the explicit 128 MiB bound.");
            m_Target = target;
            m_Read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            m_Frames = new byte[capacity][]; m_Times = new double[capacity]; m_SimulationTimes = new double[capacity];
            int bytesPerFrame = checked(Width * Height * 4);
            for (int i = 0; i < capacity; i++) m_Frames[i] = new byte[bytesPerFrame];
        }

        /// <summary>Call after normal rendering. Simulation time is a caller annotation at readback,
        /// not a promise that the preceding camera render occurred at that exact simulation instant.</summary>
        public bool Capture(double simulationSeconds)
        {
            ThrowIfDisposed();
            if (Count == Capacity) return false;
            if (double.IsNaN(simulationSeconds) || double.IsInfinity(simulationSeconds))
                throw new ArgumentOutOfRangeException(nameof(simulationSeconds));
            var previous = RenderTexture.active;
            double at = Time.realtimeSinceStartupAsDouble;
            try
            {
                RenderTexture.active = m_Target;
                m_Read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                // ReadPixels changed CPU texture storage; no redundant upload is needed for this copy.
                m_Read.GetRawTextureData<byte>().CopyTo(m_Frames[Count]);
            }
            finally { RenderTexture.active = previous; }
            m_Times[Count] = at; m_SimulationTimes[Count] = simulationSeconds; Count++;
            return true;
        }

        public Color32 ReadPixel(int frame, int x, int y)
        {
            ThrowIfDisposed();
            if (frame < 0 || frame >= Count || x < 0 || x >= Width || y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException(nameof(frame));
            var p = m_Frames[frame]; int at = (y * Width + x) * 4;
            return new Color32(p[at], p[at + 1], p[at + 2], p[at + 3]);
        }

        /// <summary>Flush only after capture. The ffconcat preserves measured acquisition intervals;
        /// its final repeated image retains display duration and is explicitly documented.</summary>
        public string Write(string name, string scenario)
        {
            ThrowIfDisposed();
            if (Count < 2) throw new InvalidOperationException("At least two captured frames are needed to report cadence.");
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A capture name is required.", nameof(name));
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) throw new ArgumentException("Use a filename-safe capture name.", nameof(name));
            string directory = Path.Combine(Application.dataPath, "..", "Artifacts", "Screenshots", "WeaponMotion", name);
            Directory.CreateDirectory(directory);
            var csv = new StringBuilder("frame,acquisition_seconds,simulation_seconds_at_readback\n");
            var concat = new StringBuilder("ffconcat version 1.0\n");
            int clampedIntervals = 0;
            for (int i = 0; i < Count; i++)
            {
                string filename = "frame-" + i.ToString("D3", CultureInfo.InvariantCulture) + ".png";
                m_Read.LoadRawTextureData(m_Frames[i]);
                File.WriteAllBytes(Path.Combine(directory, filename), m_Read.EncodeToPNG());
                csv.Append(i).Append(',').Append((m_Times[i] - m_Times[0]).ToString("F9", CultureInfo.InvariantCulture))
                    .Append(',').Append(m_SimulationTimes[i].ToString("F9", CultureInfo.InvariantCulture)).Append('\n');
                double duration = i + 1 < Count ? m_Times[i + 1] - m_Times[i] : m_Times[i] - m_Times[i - 1];
                // Retain every raw timestamp. A timer-resolution collision is disclosed, never relabeled 30/60fps.
                if (duration < .000001) { duration = .000001; clampedIntervals++; }
                concat.Append("file '").Append(filename).Append("'\nduration ").Append(duration.ToString("F9", CultureInfo.InvariantCulture)).Append('\n');
            }
            concat.Append("file 'frame-").Append((Count - 1).ToString("D3", CultureInfo.InvariantCulture)).Append(".png'\n");
            File.WriteAllText(Path.Combine(directory, "acquisition.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(directory, "acquisition.ffconcat"), concat.ToString());
            File.WriteAllText(Path.Combine(directory, "README.txt"),
                "Unmodified GPU target readbacks; no synthesized or interpolated image frames.\n" +
                "Scenario: " + scenario + "\n" +
                "Capture: " + Count + " frames, " + Width + "x" + Height + "; raw storage " + BufferBytes + " bytes.\n" +
                "PNG encoding/file writes occurred after acquisition. Synchronous readback can still affect cadence.\n" +
                "CSV records actual acquisition times and a separate simulation-clock annotation. This is not a device frame-pacing benchmark.\n" +
                "ffconcat uses measured intervals, with one final repeated image to retain display duration. Timer-resolution intervals clamped to 1 microsecond: " + clampedIntervals + ".\n");
            return directory;
        }

        void ThrowIfDisposed() { if (m_Disposed) throw new ObjectDisposedException(nameof(BufferedFrameCapture)); }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            Array.Clear(m_Frames, 0, m_Frames.Length);
            Object.Destroy(m_Read);
        }
    }
}
#endif
