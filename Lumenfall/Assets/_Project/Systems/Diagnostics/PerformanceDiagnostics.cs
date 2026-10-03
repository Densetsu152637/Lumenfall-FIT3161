using System;
using System.Diagnostics;

namespace Lumenfall.Diagnostics
{
    /// <summary>Fixed operation names only; no map, object, save or player identifiers are retained.</summary>
    public enum PerformanceMetric
    {
        WorldLoad,
        WorldInstantiate,
        WorldActivate,
        WorldBeginRebuild,
        WorldEndRebuild,
        WorldObjectProperty,
        WorldObjectMove,
        WorldConnection,
        WorldEffectQueue,
        NpcBuildPath
    }

    public readonly struct PerformanceSnapshot
    {
        public PerformanceMetric Metric { get; }
        public long SampleCount { get; }
        public long TotalElapsedTicks { get; }
        public long MaxElapsedTicks { get; }
        public long TotalWorkUnits { get; }
        public double TotalElapsedMilliseconds => TotalElapsedTicks * (1000.0 / Stopwatch.Frequency);
        public double MeanElapsedMilliseconds => SampleCount == 0 ? 0 : TotalElapsedMilliseconds / SampleCount;
        public double MaxElapsedMilliseconds => MaxElapsedTicks * (1000.0 / Stopwatch.Frequency);

        internal PerformanceSnapshot(PerformanceMetric metric, long samples, long total, long maximum, long workUnits)
        {
            Metric = metric;
            SampleCount = samples;
            TotalElapsedTicks = total;
            MaxElapsedTicks = maximum;
            TotalWorkUnits = workUnits;
        }
    }

    /// <summary>
    /// Opt-in, process-local aggregate timings. Stores one bucket per fixed metric, with no sample history.
    /// Enable explicitly, exercise a scenario, read snapshots, then disable. Nested durations are inclusive
    /// and must not be added together. Samples count attempts, including rejected operations and exceptions.
    /// Reset and enable/disable transitions discard scopes from the previous collection session.
    /// </summary>
    public static class PerformanceDiagnostics
    {
        private const int MetricCount = (int)PerformanceMetric.NpcBuildPath + 1;
        private static readonly object Gate = new object();
        private static readonly long[] Samples = new long[MetricCount];
        private static readonly long[] Totals = new long[MetricCount];
        private static readonly long[] Maximums = new long[MetricCount];
        private static readonly long[] WorkUnits = new long[MetricCount];
        private static volatile bool enabled;
        private static long generation;

        public static bool Enabled
        {
            get => enabled;
            set
            {
                lock (Gate)
                {
                    if (enabled == value) return;
                    generation++;
                    enabled = value;
                }
            }
        }

        /// <summary>Disabled scopes do not read the clock, allocate or acquire a lock.</summary>
        public static Measurement Measure(PerformanceMetric metric, long workUnits = 1)
        {
            if (!enabled) return default;
            Validate(metric, 0, workUnits);
            lock (Gate)
            {
                if (!enabled) return default;
                return new Measurement(metric, Stopwatch.GetTimestamp(), generation, workUnits);
            }
        }

        /// <summary>Allows deterministic records from offline probes. Ticks use Stopwatch.Frequency.</summary>
        public static void Record(PerformanceMetric metric, long elapsedTicks, long workUnits = 1)
        {
            if (!enabled) return;
            Validate(metric, elapsedTicks, workUnits);
            lock (Gate)
            {
                if (enabled) Add(metric, elapsedTicks, workUnits);
            }
        }

        public static PerformanceSnapshot GetSnapshot(PerformanceMetric metric)
        {
            Validate(metric, 0, 0);
            lock (Gate)
            {
                int index = (int)metric;
                return new PerformanceSnapshot(metric, Samples[index], Totals[index], Maximums[index], WorkUnits[index]);
            }
        }

        /// <summary>Only an explicit read allocates the returned snapshot array.</summary>
        public static PerformanceSnapshot[] GetSnapshots()
        {
            lock (Gate)
            {
                var result = new PerformanceSnapshot[MetricCount];
                for (int i = 0; i < MetricCount; i++)
                    result[i] = new PerformanceSnapshot((PerformanceMetric)i, Samples[i], Totals[i], Maximums[i], WorkUnits[i]);
                return result;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                generation++;
                Array.Clear(Samples, 0, MetricCount);
                Array.Clear(Totals, 0, MetricCount);
                Array.Clear(Maximums, 0, MetricCount);
                Array.Clear(WorkUnits, 0, MetricCount);
            }
        }

        private static void Complete(PerformanceMetric metric, long start, long session, long workUnits)
        {
            if (!enabled) return;
            long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - start);
            lock (Gate)
            {
                if (enabled && generation == session) Add(metric, elapsed, workUnits);
            }
        }

        private static void Add(PerformanceMetric metric, long elapsedTicks, long workUnits)
        {
            int index = (int)metric;
            Samples[index] = SaturatingAdd(Samples[index], 1);
            Totals[index] = SaturatingAdd(Totals[index], elapsedTicks);
            Maximums[index] = Math.Max(Maximums[index], elapsedTicks);
            WorkUnits[index] = SaturatingAdd(WorkUnits[index], workUnits);
        }

        private static long SaturatingAdd(long value, long addition) => long.MaxValue - value < addition ? long.MaxValue : value + addition;
        private static void Validate(PerformanceMetric metric, long elapsedTicks, long workUnits)
        {
            if ((int)metric < 0 || (int)metric >= MetricCount) throw new ArgumentOutOfRangeException(nameof(metric));
            if (elapsedTicks < 0) throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
            if (workUnits < 0) throw new ArgumentOutOfRangeException(nameof(workUnits));
        }

        /// <summary>Use in one using scope; like other value-type scopes it must not be copied or disposed twice.</summary>
        public readonly struct Measurement : IDisposable
        {
            private readonly PerformanceMetric metric;
            private readonly long start;
            private readonly long session;
            private readonly long workUnits;
            private readonly bool active;

            internal Measurement(PerformanceMetric metric, long start, long session, long workUnits)
            {
                this.metric = metric;
                this.start = start;
                this.session = session;
                this.workUnits = workUnits;
                active = true;
            }

            public void Dispose()
            {
                if (active) Complete(metric, start, session, workUnits);
            }
        }
    }
}