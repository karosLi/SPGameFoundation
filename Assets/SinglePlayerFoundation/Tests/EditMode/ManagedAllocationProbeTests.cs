using System;
using System.Threading;
using NUnit.Framework;
using SPF.Testing;

namespace SPF.Tests.EditMode
{
    public class ManagedAllocationProbeTests
    {
        static byte[] s_Retained;
        static int s_Value;
        static void Empty() { s_Value++; }
        static void Allocate() { s_Retained = new byte[1024]; }
        static void Throw() { throw new InvalidOperationException("Intentional probe test exception."); }

#if SPF_DOTNET_HARNESS
        // A cooperative managed busy loop, with no object creation or framework/gameplay calls.
        // SpinWait is unsuitable here: its native transition did not reproduce the counter issue.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void BusyWithoutAllocating()
        {
            for (int i = 0; i < 1000000; i++) s_Value = unchecked(s_Value * 1664525 + 1013904223);
        }

        [Test]
        public void HarnessThreadByteAccountingRemainsExactDuringCollections()
        {
            // This is a testhost setting, never a Unity/player or machine-wide GC change.
            Assert.AreEqual(System.Runtime.GCLatencyMode.Batch, System.Runtime.GCSettings.LatencyMode,
                "The .NET allocation harness requires blocking GC; regenerate its test projects.");
            var samples = new ManagedAllocationSample[64];
            int stop = 0, requests = 0;
            using var ready = new ManualResetEventSlim();
            var worker = new Thread(() =>
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    GC.Collect(2, GCCollectionMode.Forced, blocking: false, compacting: false);
                    Interlocked.Increment(ref requests);
                    ready.Set();
                    Thread.Sleep(1);
                }
            }) { IsBackground = true };
            Action operation = BusyWithoutAllocating;
            operation();
            using var probe = new ManagedAllocationProbe();
            var before = probe.Calibrate();
            worker.Start();
            try
            {
                Assert.IsTrue(ready.Wait(5000), "The GC diagnostic worker must start.");
                for (int i = 0; i < samples.Length; i++) samples[i] = probe.Measure(operation);
            }
            finally
            {
                Volatile.Write(ref stop, 1);
                Assert.IsTrue(worker.Join(5000), "The bounded GC diagnostic worker must terminate.");
            }
            var after = probe.Calibrate();
            AssertCalibration(before, probe.Metric);
            AssertCalibration(after, probe.Metric);
            Assert.Greater(requests, 0, "The diagnostic must exercise collections.");
            foreach (var sample in samples) Assert.AreEqual(0, sample.Value);
            Allocate();
            Assert.GreaterOrEqual(probe.Measure(Allocate).Value, 1024,
                "Blocking GC must not hide actual managed allocations.");
            s_Retained = null;
            TestContext.WriteLine($".NET thread-byte accounting: {samples.Length} nonallocating busy windows, {requests} collection requests, runtime={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, latency={System.Runtime.GCSettings.LatencyMode}; actual retained allocation still detected.");
        }
#endif

        static void AssertCalibration(ManagedAllocationCalibration calibration, ManagedAllocationMetric metric)
        {
            long minimum = metric == ManagedAllocationMetric.ManagedBytes
                ? ManagedAllocationProbe.CalibrationArrayCount * ManagedAllocationProbe.CalibrationArrayBytes
                : ManagedAllocationProbe.CalibrationArrayCount;
            Assert.AreEqual(metric, calibration.RetainedArrays.Metric);
            Assert.AreEqual(metric, calibration.Empty.Metric);
            Assert.GreaterOrEqual(calibration.RetainedArrays.Value, minimum);
            Assert.AreEqual(0, calibration.Empty.Value);
            Assert.GreaterOrEqual(calibration.RetainedArrays.Collections, 0);
        }

        [Test]
        public void RetainedPositiveAndEmptyControlsCalibrateBothEnds()
        {
            using var probe = new ManagedAllocationProbe();
            var before = probe.Calibrate();
            Empty();
            Allocate(); // Explicitly warm these operations, outside the measured windows.
            var empty = probe.Measure(Empty);
            var allocated = probe.Measure(Allocate);
            var after = probe.Calibrate();
            s_Retained = null;
            AssertCalibration(before, probe.Metric);
            AssertCalibration(after, probe.Metric);
            Assert.AreEqual(0, empty.Value);
            Assert.AreEqual(probe.Metric, empty.Metric);
            Assert.GreaterOrEqual(allocated.Value, probe.Metric == ManagedAllocationMetric.ManagedBytes ? 1024 : 1);
            TestContext.WriteLine($"Managed allocation probe: {probe.Metric}; controls before={before.RetainedArrays.Value}/{before.Empty.Value}, after={after.RetainedArrays.Value}/{after.Empty.Value}; empty={empty.Value}, retained1024={allocated.Value}; independent process-wide gen0 collections={allocated.Collections}.");
        }

        [Test]
        public void RequiresCalibrationAndRejectsDisposedOrNullUse()
        {
            var probe = new ManagedAllocationProbe();
            try
            {
                Assert.Throws<InvalidOperationException>(() => probe.Measure(Empty));
                AssertCalibration(probe.Calibrate(), probe.Metric);
                Assert.Throws<ArgumentNullException>(() => probe.Measure(null));
            }
            finally { probe.Dispose(); }
            Assert.Throws<ObjectDisposedException>(() => probe.Measure(Empty));
            Assert.Throws<ObjectDisposedException>(() => probe.Calibrate());
            Assert.DoesNotThrow(() => probe.Dispose());
        }

        [Test]
        public void OperationExceptionStopsRecorderAndPermitsRecalibration()
        {
            using var probe = new ManagedAllocationProbe();
            probe.Calibrate();
            Assert.Throws<InvalidOperationException>(() => probe.Measure(Throw));
            AssertCalibration(probe.Calibrate(), probe.Metric);
            Empty();
            Assert.AreEqual(0, probe.Measure(Empty).Value);
        }

        [Test]
        public void NestedMeasurementIsRejectedWithoutLeavingRecorderRunning()
        {
            using var probe = new ManagedAllocationProbe();
            probe.Calibrate();
            Assert.Throws<InvalidOperationException>(() => probe.Measure(() => probe.Measure(Empty)));
            AssertCalibration(probe.Calibrate(), probe.Metric);
            Empty();
            Assert.AreEqual(0, probe.Measure(Empty).Value);
        }

        [Test]
        public void CrossThreadMeasurementIsRejectedBeforeAccessingRecorder()
        {
            using var probe = new ManagedAllocationProbe();
            probe.Calibrate();
            Exception caught = null;
            var thread = new Thread(() =>
            {
                try { probe.Measure(Empty); }
                catch (Exception error) { caught = error; }
            });
            thread.Start();
            Assert.IsTrue(thread.Join(5000), "The rejected worker operation must terminate.");
            Assert.IsInstanceOf<InvalidOperationException>(caught);
            AssertCalibration(probe.Calibrate(), probe.Metric);
            Empty();
            Assert.AreEqual(0, probe.Measure(Empty).Value);
        }
    }
}
