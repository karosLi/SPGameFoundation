using UnityEngine;

namespace SPF.Runtime.Diagnostics
{
    /// <summary>
    /// GPU (and CPU) frame time from <see cref="FrameTimingManager"/>: call <see cref="Sample"/> once per
    /// frame, read the averages since the last <see cref="Reset"/>. GPU timings arrive a few frames late
    /// and are only reported where the platform supports them (Metal, Vulkan, D3D, most GLES 3 drivers
    /// with Frame Timing Stats enabled in Player settings); <see cref="GpuSamples"/> stays 0 otherwise.
    /// Allocation-free after construction.
    /// </summary>
    public sealed class GpuFrameTimer
    {
        readonly FrameTiming[] m_Timings = new FrameTiming[1];
        double m_GpuSum, m_CpuSum;

        public int GpuSamples { get; private set; }
        public int CpuSamples { get; private set; }
        public double GpuMs => GpuSamples > 0 ? m_GpuSum / GpuSamples : 0.0;
        public double CpuMs => CpuSamples > 0 ? m_CpuSum / CpuSamples : 0.0;

        /// <summary>True when the frame timing feature is enabled for this build / platform.</summary>
        public static bool Enabled => FrameTimingManager.IsFeatureEnabled();

        public void Reset()
        {
            m_GpuSum = m_CpuSum = 0.0;
            GpuSamples = CpuSamples = 0;
        }

        public void Sample()
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, m_Timings) == 0)
                return;
            var t = m_Timings[0];
            if (t.gpuFrameTime > 0.0)
            {
                m_GpuSum += t.gpuFrameTime;
                GpuSamples++;
            }
            if (t.cpuFrameTime > 0.0)
            {
                m_CpuSum += t.cpuFrameTime;
                CpuSamples++;
            }
        }
    }
}
