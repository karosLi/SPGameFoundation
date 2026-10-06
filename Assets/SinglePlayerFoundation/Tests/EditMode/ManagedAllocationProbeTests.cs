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
