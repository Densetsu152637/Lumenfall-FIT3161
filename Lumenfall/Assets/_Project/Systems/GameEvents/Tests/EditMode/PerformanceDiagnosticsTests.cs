using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NUnit.Framework;
using Lumenfall.Diagnostics;

namespace Lumenfall.GameEvents.Tests
{
    public sealed class PerformanceDiagnosticsTests
    {
        [SetUp]
        public void SetUp()
        {
            PerformanceDiagnostics.Enabled = false;
            PerformanceDiagnostics.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            PerformanceDiagnostics.Enabled = false;
            PerformanceDiagnostics.Reset();
        }

        [Test]
        public void DisabledCollectionIgnoresRecordsAndScopes()
        {
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 100, 3);
            using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad)) { }
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.Zero);
        }

        [Test]
        public void RecordsAggregateCountsDurationsAndWorkUnits()
        {
            PerformanceDiagnostics.Enabled = true;
            PerformanceDiagnostics.Record(PerformanceMetric.WorldEndRebuild, 10, 4);
            PerformanceDiagnostics.Record(PerformanceMetric.WorldEndRebuild, 25, 7);
            var snapshot = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldEndRebuild);
            Assert.That(snapshot.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.TotalElapsedTicks, Is.EqualTo(35));
            Assert.That(snapshot.MaxElapsedTicks, Is.EqualTo(25));
            Assert.That(snapshot.TotalWorkUnits, Is.EqualTo(11));
            Assert.That(snapshot.TotalElapsedMilliseconds, Is.EqualTo(35 * (1000.0 / Stopwatch.Frequency)));
        }

        [Test]
        public void MetricsAreIndependentAndSnapshotsAreDetached()
        {
            PerformanceDiagnostics.Enabled = true;
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 8);
            var snapshot = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad);
            PerformanceDiagnostics.Record(PerformanceMetric.NpcBuildPath, 12);
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 3);
            Assert.That(snapshot.SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).TotalElapsedTicks, Is.EqualTo(11));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.NpcBuildPath).TotalElapsedTicks, Is.EqualTo(12));
        }

        [Test]
        public void LargeSampleVolumeKeepsFixedBucketCount()
        {
            PerformanceDiagnostics.Enabled = true;
            for (int i = 0; i < 100000; i++) PerformanceDiagnostics.Record(PerformanceMetric.NpcBuildPath, 1);
            var snapshots = PerformanceDiagnostics.GetSnapshots();
            Assert.That(snapshots.Length, Is.EqualTo(Enum.GetValues(typeof(PerformanceMetric)).Length));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.NpcBuildPath).SampleCount, Is.EqualTo(100000));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.NpcBuildPath).TotalElapsedTicks, Is.EqualTo(100000));
        }

        [Test]
        public void InvalidMetricAndNegativeMeasurementsAreRejectedWithoutRecording()
        {
            PerformanceDiagnostics.Enabled = true;
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceDiagnostics.Record((PerformanceMetric)(-1), 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceDiagnostics.GetSnapshot((PerformanceMetric)999));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad, -1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.Zero);
        }

        [Test]
        public void AggregateCountersSaturateInsteadOfWrapping()
        {
            PerformanceDiagnostics.Enabled = true;
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, long.MaxValue, long.MaxValue);
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 1, 1);
            var snapshot = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad);
            Assert.That(snapshot.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.TotalElapsedTicks, Is.EqualTo(long.MaxValue));
            Assert.That(snapshot.MaxElapsedTicks, Is.EqualTo(long.MaxValue));
            Assert.That(snapshot.TotalWorkUnits, Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void ResetClearsAllBucketsWhilePreservingOptIn()
        {
            PerformanceDiagnostics.Enabled = true;
            foreach (PerformanceMetric metric in Enum.GetValues(typeof(PerformanceMetric))) PerformanceDiagnostics.Record(metric, 15, 3);
            PerformanceDiagnostics.Reset();
            Assert.That(PerformanceDiagnostics.Enabled, Is.True);
            foreach (var snapshot in PerformanceDiagnostics.GetSnapshots())
            {
                Assert.That(snapshot.SampleCount, Is.Zero);
                Assert.That(snapshot.TotalElapsedTicks, Is.Zero);
                Assert.That(snapshot.MaxElapsedTicks, Is.Zero);
                Assert.That(snapshot.TotalWorkUnits, Is.Zero);
            }
        }

        [Test]
        public void ResetDiscardsMeasurementsStartedInPreviousSession()
        {
            PerformanceDiagnostics.Enabled = true;
            var oldScope = PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad);
            PerformanceDiagnostics.Reset();
            oldScope.Dispose();
            using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad, 2)) { }
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).TotalWorkUnits, Is.EqualTo(2));
        }

        [Test]
        public void DisabledScopeDoesNotRecordWhenCollectionIsEnabledLater()
        {
            var scope = PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad);
            PerformanceDiagnostics.Enabled = true;
            scope.Dispose();
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.Zero);
        }

        [Test]
        public void DisableAndReenableDiscardsOldActiveScopesButRetainsAggregates()
        {
            PerformanceDiagnostics.Enabled = true;
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 8);
            var oldScope = PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad);
            PerformanceDiagnostics.Enabled = false;
            PerformanceDiagnostics.Enabled = true;
            oldScope.Dispose();
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).TotalElapsedTicks, Is.EqualTo(8));
        }

        [Test]
        public void EnabledScopeRecordsOnExceptionWithoutSuppressingException()
        {
            PerformanceDiagnostics.Enabled = true;
            Assert.Throws<InvalidOperationException>(() =>
            {
                using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldObjectProperty, 3))
                    throw new InvalidOperationException("probe");
            });
            var snapshot = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldObjectProperty);
            Assert.That(snapshot.SampleCount, Is.EqualTo(1));
            Assert.That(snapshot.TotalWorkUnits, Is.EqualTo(3));
            Assert.That(snapshot.TotalElapsedTicks, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ReapplyingEnabledStateDoesNotInvalidateActiveMeasurement()
        {
            PerformanceDiagnostics.Enabled = true;
            using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldActivate))
                PerformanceDiagnostics.Enabled = true;
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldActivate).SampleCount, Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentRecordingDoesNotLoseSamples()
        {
            PerformanceDiagnostics.Enabled = true;
            Parallel.For(0, 4000, _ => PerformanceDiagnostics.Record(PerformanceMetric.WorldEffectQueue, 2, 3));
            var snapshot = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldEffectQueue);
            Assert.That(snapshot.SampleCount, Is.EqualTo(4000));
            Assert.That(snapshot.TotalElapsedTicks, Is.EqualTo(8000));
            Assert.That(snapshot.TotalWorkUnits, Is.EqualTo(12000));
        }
    }
}