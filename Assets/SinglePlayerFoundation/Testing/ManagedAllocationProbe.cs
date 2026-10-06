using System;
using System.Threading;
#if !SPF_DOTNET_HARNESS
using UnityEngine.Profiling;
#endif

namespace SPF.Testing
{
    public enum ManagedAllocationMetric { ManagedBytes, AllocationSamples }

    public readonly struct ManagedAllocationSample
    {
        public readonly long Value;
        public readonly ManagedAllocationMetric Metric;
        /// <summary>Independent process-wide generation-0 collection delta, not allocation events.</summary>
        public readonly int Collections;

        internal ManagedAllocationSample(long value, ManagedAllocationMetric metric, int collections)
        {
            Value = value;
            Metric = metric;
            Collections = collections;
        }
    }

    public readonly struct ManagedAllocationCalibration
    {
        public readonly ManagedAllocationSample RetainedArrays, Empty;

        internal ManagedAllocationCalibration(ManagedAllocationSample retainedArrays, ManagedAllocationSample empty)
        {
            RetainedArrays = retainedArrays;
            Empty = empty;
        }
    }

    /// <summary>
    /// Testing-only synchronous managed-allocation probe. Construct/delegate-bind outside the
    /// measured region, call Calibrate before and after retained measurements, and explicitly warm
    /// the tested operation at the call site. Calibration failure throws; unavailable never means zero.
    /// Unity measures current-thread GC.Alloc samples using the installed test framework's recorder
    /// stop/flush pattern; the .NET harness measures current-thread bytes. Neither covers deferred
    /// frames, other threads, native allocations or attribution of process-wide collections.
    /// </summary>
    public sealed class ManagedAllocationProbe : IDisposable
    {
        public const int CalibrationArrayCount = 32;
        public const int CalibrationArrayBytes = 1024;
        static readonly Action EmptyAction = () => { };
        readonly byte[][] m_Retained = new byte[CalibrationArrayCount][];
        readonly Action m_AllocateControl;
        readonly int m_OwnerThread = Thread.CurrentThread.ManagedThreadId;
        bool m_Calibrated, m_Disposed, m_Measuring;
#if !SPF_DOTNET_HARNESS
        // Recorder.Get shares the sampler. These probes must not overlap each other or another
        // GC.Alloc recorder/AllocatingGCMemoryConstraint. No global Profiler setting is changed.
        static int s_RecorderLease;
        readonly Recorder m_Recorder;
#endif

        public ManagedAllocationMetric Metric
        {
            get
            {
#if SPF_DOTNET_HARNESS
                return ManagedAllocationMetric.ManagedBytes;
#else
                return ManagedAllocationMetric.AllocationSamples;
#endif
            }
        }

        public ManagedAllocationProbe()
        {
            m_AllocateControl = AllocateControl;
#if !SPF_DOTNET_HARNESS
            if (Interlocked.CompareExchange(ref s_RecorderLease, 1, 0) != 0)
                throw new InvalidOperationException("GC.Alloc probes cannot overlap.");
            try
            {
                m_Recorder = Recorder.Get("GC.Alloc");
                if (!m_Recorder.isValid)
                    throw new InvalidOperationException("GC.Alloc recorder unavailable; allocation measurement cannot be established.");
                m_Recorder.enabled = false;
                m_Recorder.FilterToCurrentThread();
            }
            catch
            {
                if (m_Recorder != null)
                {
                    m_Recorder.enabled = false;
                    m_Recorder.CollectFromAllThreads();
                }
                Interlocked.Exchange(ref s_RecorderLease, 0);
                throw;
            }
#endif
        }

        void CheckReady()
        {
            if (m_Disposed) throw new ObjectDisposedException(nameof(ManagedAllocationProbe));
            if (Thread.CurrentThread.ManagedThreadId != m_OwnerThread)
                throw new InvalidOperationException("Use the probe on its creating thread.");
            if (m_Measuring) throw new InvalidOperationException("Allocation measurements cannot nest.");
        }

        void AllocateControl()
        {
            for (int i = 0; i < m_Retained.Length; i++)
                m_Retained[i] = new byte[CalibrationArrayBytes];
        }

        /// <summary>Known retained allocations followed by an empty window; no implicit operation warm-up.</summary>
        public ManagedAllocationCalibration Calibrate()
        {
            CheckReady();
            m_Calibrated = false;
            try
            {
                var positive = MeasureCore(m_AllocateControl);
                var empty = MeasureCore(EmptyAction);
                long minimum = Metric == ManagedAllocationMetric.ManagedBytes
                    ? CalibrationArrayCount * CalibrationArrayBytes : CalibrationArrayCount;
                if (positive.Value < minimum || empty.Value != 0)
                    throw new InvalidOperationException($"Allocation measurement unavailable: retained-array control={positive.Value} {Metric} (minimum {minimum}), empty={empty.Value}. Do not interpret zero as allocation-free.");
                m_Calibrated = true;
                return new ManagedAllocationCalibration(positive, empty);
            }
            finally { Array.Clear(m_Retained, 0, m_Retained.Length); }
        }

        public ManagedAllocationSample Measure(Action operation)
        {
            CheckReady();
            if (!m_Calibrated)
                throw new InvalidOperationException("Calibrate before measuring allocations.");
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            return MeasureCore(operation);
        }

        ManagedAllocationSample MeasureCore(Action operation)
        {
            int collections = GC.CollectionCount(0);
            m_Measuring = true;
#if SPF_DOTNET_HARNESS
            long before = GC.GetAllocatedBytesForCurrentThread();
#else
            m_Recorder.enabled = true;
#endif
            try { operation(); }
            finally
            {
#if !SPF_DOTNET_HARNESS
                m_Recorder.enabled = false;
#endif
                m_Measuring = false;
            }
#if SPF_DOTNET_HARNESS
            long value = GC.GetAllocatedBytesForCurrentThread() - before;
#else
            long value = m_Recorder.sampleBlockCount;
#endif
            return new ManagedAllocationSample(value, Metric, GC.CollectionCount(0) - collections);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            CheckReady();
#if !SPF_DOTNET_HARNESS
            m_Recorder.enabled = false;
            m_Recorder.CollectFromAllThreads();
            Interlocked.Exchange(ref s_RecorderLease, 0);
#endif
            Array.Clear(m_Retained, 0, m_Retained.Length);
            m_Calibrated = false;
            m_Disposed = true;
        }
    }
}
