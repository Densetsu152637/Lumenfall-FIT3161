using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    /// <summary>
    /// Play should leave the Console quiet: game code reports problems with warnings and errors only, never routine
    /// <c>Debug.Log</c> lines (such as the player's position on every step). Editor tools and tests may log.
    /// </summary>
    public sealed class RuntimeLoggingTests
    {
        private static readonly Regex PlainLog = new Regex(@"\bDebug\.Log(Format)?\s*\(");

        [Test]
        public void GameCodeDoesNotWriteRoutineConsoleLines()
        {
            string systems = Path.Combine(Application.dataPath, "_Project", "Systems");
            string[] offenders = Directory.GetFiles(systems, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsUnder(path, "Tests") && !IsUnder(path, "Editor"))
                .SelectMany(path => File.ReadAllLines(path).Select((line, index) => (path, line, number: index + 1)))
                .Where(entry => !entry.line.TrimStart().StartsWith("//") && PlainLog.IsMatch(entry.line))
                .Select(entry => $"{Path.GetFileName(entry.path)}:{entry.number}: {entry.line.Trim()}")
                .ToArray();
            Assert.That(offenders, Is.Empty, "Use Debug.LogWarning or Debug.LogError for problems, or remove the log.");
        }

        private static bool IsUnder(string path, string folder) =>
            path.Replace('\\', '/').Split('/').Contains(folder);
    }
}
