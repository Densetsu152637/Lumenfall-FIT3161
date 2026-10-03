using UnityEngine;

namespace Lumenfall.Diagnostics
{
    internal static class RuntimePerformanceDiagnostics
    {
        // SubsystemRegistration also runs when entering Play Mode with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            PerformanceDiagnostics.Enabled = false;
            PerformanceDiagnostics.Reset();
        }
    }
}