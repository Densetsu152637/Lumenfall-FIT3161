using System;
using System.Linq;
using System.Reflection;
using Lumenfall.Diagnostics;
using Lumenfall.GameEvents.Tests;
using NUnit.Framework;

public static class CollectorHarness
{
    public static int Main()
    {
        if (PerformanceDiagnostics.Enabled) throw new Exception("Collector must default to disabled.");
        var fixture = new PerformanceDiagnosticsTests();
        var tests = typeof(PerformanceDiagnosticsTests).GetMethods().Where(m => m.GetCustomAttribute<TestAttribute>() != null).OrderBy(m => m.Name);
        int passed = 0;
        foreach (var test in tests)
        {
            fixture.SetUp();
            try
            {
                test.Invoke(fixture, null);
                Console.WriteLine("PASS " + test.Name);
                passed++;
            }
            catch (TargetInvocationException ex)
            {
                Console.WriteLine("FAIL " + test.Name + ": " + ex.InnerException);
                return 1;
            }
            finally { fixture.TearDown(); }
        }
        for (int i = 0; i < 1000; i++) { using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad)) { } }
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) { using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad)) { } }
        long disabledBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (disabledBytes != 0) throw new Exception("Disabled measurement allocated " + disabledBytes + " bytes.");
        PerformanceDiagnostics.Enabled = true;
        for (int i = 0; i < 1000; i++) { using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad)) { } }
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100000; i++) { using (PerformanceDiagnostics.Measure(PerformanceMetric.WorldLoad)) { } }
        long enabledBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (enabledBytes != 0) throw new Exception("Enabled measurement allocated " + enabledBytes + " bytes.");
        Console.WriteLine("Passed " + passed + " repository NUnit tests. Disabled and enabled measurements each allocated 0 bytes over 100000 warmed scopes on .NET 8 (not Unity runtime evidence).");
        return 0;
    }
}