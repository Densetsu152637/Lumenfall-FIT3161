using System;
using System.Collections.Generic;
using System.Reflection;
using Lumenfall.Diagnostics;
using NUnit.Framework;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR17"), NonParallelizable]
    public sealed class Pr17DiagnosticsAcceptanceTests
    {
        private readonly Dictionary<FieldInfo, object> prior = new Dictionary<FieldInfo, object>();
        [SetUp] public void PreserveCollector()
        {
            foreach (var field in typeof(PerformanceDiagnostics).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
                if (field.FieldType == typeof(long[]) || field.Name == "enabled" || field.Name == "generation")
                { object value = field.GetValue(null); prior[field] = value is Array array ? array.Clone() : value; }
            PerformanceDiagnostics.Enabled = true; PerformanceDiagnostics.Reset();
        }
        [TearDown] public void RestoreCollector()
        {
            foreach (var item in prior)
                if (item.Value is Array array) Array.Copy(array, (Array)item.Key.GetValue(null), array.Length);
                else item.Key.SetValue(null, item.Value);
            prior.Clear();
        }
        [Test] public void NestedResetDiscardsOuterScopeButRetainsMeasurementsStartedAfterReset()
        {
            using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad, 9))
            {
                PerformanceDiagnostics.Reset();
                using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldInstantiate, 4)) { }
            }
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad).SampleCount, Is.Zero);
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldInstantiate).SampleCount, Is.EqualTo(1));
            Assert.That(PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldInstantiate).TotalWorkUnits, Is.EqualTo(4));
        }
        [Test] public void ZeroWorkAttemptStillCountsAndParticipatesInTimingMean()
        {
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 10, 0);
            PerformanceDiagnostics.Record(PerformanceMetric.WorldLoad, 0, 0);
            var snapshot = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldLoad);
            Assert.That(snapshot.SampleCount, Is.EqualTo(2));
            Assert.That(snapshot.TotalWorkUnits, Is.Zero);
            Assert.That(snapshot.MeanElapsedMilliseconds, Is.EqualTo(snapshot.TotalElapsedMilliseconds / 2));
            Assert.That(snapshot.MaxElapsedTicks, Is.EqualTo(10));
        }
        [Test] public void MutatingReturnedSnapshotArrayCannotAlterCollectionOrLaterReads()
        {
            PerformanceDiagnostics.Record(PerformanceMetric.WorldActivate, 20, 3);
            var snapshots = PerformanceDiagnostics.GetSnapshots();
            Array.Clear(snapshots, 0, snapshots.Length);
            var actual = PerformanceDiagnostics.GetSnapshot(PerformanceMetric.WorldActivate);
            Assert.That(actual.SampleCount, Is.EqualTo(1)); Assert.That(actual.TotalElapsedTicks, Is.EqualTo(20));
            Assert.That(actual.TotalWorkUnits, Is.EqualTo(3));
        }
    }
}
