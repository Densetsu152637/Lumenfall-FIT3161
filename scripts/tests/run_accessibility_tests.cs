using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Lumenfall.GameEvents.Tests;

internal static class AccessibilityTestRunner
{
    private static int Main()
    {
        int passed = 0, failed = 0;
        object fixture = new AccessibilityPreferencesTests();
        foreach (MethodInfo method in fixture.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            TestCaseAttribute[] cases = method.GetCustomAttributes<TestCaseAttribute>().ToArray();
            object[][] arguments = cases.Length > 0 ? cases.Select(test => test.Arguments).ToArray() :
                method.GetCustomAttribute<TestAttribute>() != null ? new[] { Array.Empty<object>() } : Array.Empty<object[]>();
            foreach (object[] values in arguments)
            {
                try { method.Invoke(fixture, values); passed++; Console.WriteLine("PASS " + method.Name); }
                catch (Exception error) { failed++; Console.WriteLine("FAIL " + method.Name + ": " + (error.InnerException ?? error)); }
            }
        }
        Console.WriteLine($"Accessibility tests: {passed} passed, {failed} failed.");
        return failed == 0 && passed > 0 ? 0 : 1;
    }
}
