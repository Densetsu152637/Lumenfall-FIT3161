"""Keep missing, incomplete or stale Unity evidence from passing the required gate."""

import json
import io
import re
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from contextlib import redirect_stdout
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import verify_gameplay_tests as gameplay


class GameplayVerificationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.artifacts = Path(temporary.name)
        self.revision = "a" * 40
        self.platforms = {
            "editmode": {"name": "Gameplay rules", "fixtures": {"Game.RulesTests": 2}},
            "playmode": {"name": "World integration", "fixtures": {"Game.WorldTests": 1}},
        }

    def results(self, platform, *, fixture=None, count=None, result="Passed"):
        expected = self.platforms[platform]["fixtures"]
        fixture = fixture or next(iter(expected))
        count = sum(expected.values()) if count is None else count
        root = gameplay.ET.Element("test-run", result="Passed", total=str(count), passed=str(count))
        for index in range(count):
            gameplay.ET.SubElement(root, "test-case", classname=fixture, name=f"Case{index}",
                                   fullname=f"{fixture}.Case{index}", result=result)
        path = self.artifacts / f"{platform}-results.xml"
        gameplay.ET.ElementTree(root).write(path)
        return path

    def both_results(self):
        for platform in self.platforms:
            self.results(platform)

    def verify(self, outcome="success", revision=None):
        return gameplay.verify(self.artifacts, self.platforms, outcome, revision or self.revision)

    def test_both_modes_require_complete_fixture_discovery(self):
        self.both_results()
        report = self.verify()
        self.assertEqual(report["status"], "passed")
        self.assertEqual([suite["passed"] for suite in report["platforms"]], [2, 1])
        self.assertEqual(report["revision"], self.revision)

    def test_combined_result_cannot_replace_missing_platform_results(self):
        path = self.results("editmode")
        path.rename(self.artifacts / "combined-results.xml")
        self.results("playmode")
        report = self.verify()
        self.assertEqual(report["status"], "failed")
        self.assertIn("Gameplay rules", report["errors"][0])

    def test_partial_extra_and_wrong_fixture_discovery_fail(self):
        self.both_results()
        for count, fixture in [(1, None), (3, None), (2, "Game.UnexpectedTests")]:
            with self.subTest(count=count, fixture=fixture):
                self.results("editmode", count=count, fixture=fixture)
                self.assertEqual(self.verify()["status"], "failed")

    def test_duplicate_identity_cannot_fill_missing_case_count(self):
        self.both_results()
        path = self.artifacts / "editmode-results.xml"
        tree = gameplay.ET.parse(path)
        cases = list(tree.getroot().iter("test-case"))
        cases[1].set("fullname", cases[0].get("fullname"))
        tree.write(path)
        self.assertIn("Duplicate", self.verify()["errors"][0])

    def test_failed_skipped_and_unknown_case_results_fail(self):
        self.both_results()
        for result in ["Failed", "Skipped", "Inconclusive", "Ignored", ""]:
            with self.subTest(result=result):
                self.results("playmode", result=result)
                self.assertEqual(self.verify()["status"], "failed")

    def test_failed_missing_or_skipped_action_rejects_passing_xml(self):
        self.both_results()
        for outcome in ["failure", "cancelled", "skipped", ""]:
            with self.subTest(outcome=outcome):
                self.assertEqual(self.verify(outcome)["status"], "failed")

    def test_case_identity_is_required(self):
        self.both_results()
        for attribute in ["classname", "fullname", "name"]:
            with self.subTest(attribute=attribute):
                path = self.results("playmode")
                tree = gameplay.ET.parse(path)
                del next(tree.getroot().iter("test-case")).attrib[attribute]
                tree.write(path)
                self.assertEqual(self.verify()["status"], "failed")

    def test_root_and_suite_failures_or_inconsistent_counts_fail(self):
        self.both_results()
        for attribute, value in [("result", "Failed"), ("total", "4"), ("passed", "0"),
                                 ("testcasecount", "9"), ("skipped", "1"), ("failed", "1"),
                                 ("inconclusive", "1")]:
            with self.subTest(attribute=attribute):
                path = self.results("playmode")
                tree = gameplay.ET.parse(path)
                tree.getroot().set(attribute, value)
                tree.write(path)
                self.assertEqual(self.verify()["status"], "failed")
        path = self.results("playmode")
        tree = gameplay.ET.parse(path)
        gameplay.ET.SubElement(tree.getroot(), "test-suite", result="Skipped")
        tree.write(path)
        self.assertEqual(self.verify()["status"], "failed")

    def test_empty_malformed_and_non_test_xml_fail(self):
        self.both_results()
        for content in ['<test-run result="Passed" />', "<invalid", '<coverage result="Passed" />']:
            with self.subTest(content=content):
                (self.artifacts / "playmode-results.xml").write_text(content)
                self.assertEqual(self.verify()["status"], "failed")

    def test_revision_must_be_full_sha(self):
        self.both_results()
        for revision in ["main", "12345", "a" * 39, "a" * 40 + "\n"]:
            with self.subTest(revision=revision):
                self.assertEqual(self.verify(revision=revision)["status"], "failed")

    def test_cli_checks_actual_checkout_revision(self):
        self.both_results()
        args = ["--artifacts", str(self.artifacts), "--outcome", "success", "--revision", self.revision]
        with patch.object(gameplay, "read_manifest", return_value=self.platforms), redirect_stdout(io.StringIO()):
            for actual, expected_exit in [(self.revision, 0), ("b" * 40, 1)]:
                with self.subTest(actual=actual), patch.object(gameplay.subprocess, "run", return_value=
                        subprocess.CompletedProcess([], 0, actual + "\n")):
                    self.assertEqual(gameplay.main(args), expected_exit)
        report = json.loads((self.artifacts / "ci-report.json").read_text())
        self.assertIn("Checkout revision differs", report["errors"][0])

    def test_report_preserves_failed_platform_and_summary(self):
        self.results("editmode")
        report = self.verify()
        summary = self.artifacts / "summary.md"
        with patch.dict(gameplay.os.environ, {"GITHUB_STEP_SUMMARY": str(summary)}), redirect_stdout(io.StringIO()):
            gameplay.write_report(report, self.artifacts)
        self.assertEqual(json.loads((self.artifacts / "ci-report.json").read_text()), report)
        self.assertIn("World integration | 0 | failed", summary.read_text())
        self.assertIn(self.revision, summary.read_text())

    def test_manifest_requires_both_modes_and_positive_case_contracts(self):
        path = self.artifacts / "manifest.json"
        for manifest in [[], {}, {"schema_version": 2}, {"schema_version": 1, "platforms": {}},
                         {"schema_version": 1, "platforms": {"editmode": {}, "playmode": {}}}]:
            with self.subTest(manifest=manifest):
                path.write_text(json.dumps(manifest))
                with self.assertRaises(ValueError):
                    gameplay.read_manifest(path)
        for count in [0, -1, True, "1"]:
            manifest = json.loads(json.dumps({"schema_version": 1, "platforms": self.platforms}))
            manifest["platforms"]["playmode"]["fixtures"]["Game.WorldTests"] = count
            path.write_text(json.dumps(manifest))
            with self.subTest(count=count), self.assertRaises(ValueError):
                gameplay.read_manifest(path)

    def test_graphical_manifest_is_separate_and_requires_explicit_category(self):
        path = self.artifacts / "manifest.json"
        manifest = {"schema_version": 1, "platforms": self.platforms,
                    "graphical": {"name": "Pointer interaction", "category": "RequiresGraphics",
                                  "fixtures": {"Game.PointerTests": 1}}}
        path.write_text(json.dumps(manifest))
        self.assertEqual(gameplay.read_manifest(path), self.platforms)
        self.assertEqual(gameplay.read_manifest(path, graphical=True),
                         {"playmode": manifest["graphical"]})
        for category in [None, "", "OtherCategory"]:
            manifest["graphical"]["category"] = category
            path.write_text(json.dumps(manifest))
            with self.subTest(category=category), self.assertRaises(ValueError):
                gameplay.read_manifest(path, graphical=True)


class GameplayContractTests(unittest.TestCase):
    def test_manifest_matches_current_test_sources_and_expected_totals(self):
        platforms = gameplay.read_manifest()
        actual = {"editmode": {}, "playmode": {}}
        graphical = {}
        assets = gameplay.ROOT / "Lumenfall/Assets"
        # Discover every current test assembly and every C# file within it, including
        # fixture files whose names do not end in Tests.cs. Nested asmdefs own their files.
        assemblies = {path.parent: json.loads(path.read_text(encoding="utf-8-sig"))
                      for path in assets.rglob("*.asmdef")}
        seen = set()
        for path in assets.rglob("*.cs"):
            owner = next((parent for parent in path.parents if parent in assemblies), None)
            if owner is None or "TestAssemblies" not in assemblies[owner].get("optionalUnityReferences", []):
                continue
            seen.add(assemblies[owner]["name"])
            platform = "editmode" if "Editor" in assemblies[owner].get("includePlatforms", []) else "playmode"
            source = path.read_text(encoding="utf-8-sig")
            self.assertNotRegex(source, r"\bAssert\.Ignore\s*\(|\[(?:Ignore|Explicit)\b")
            namespace = re.search(r"namespace\s+([\w.]+)", source)
            # Helper classes can precede methods inside their owning fixture.
            # Mask comments and strings so their braces cannot change class scope.
            masked = re.sub(r'@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|//[^\n]*|/\*.*?\*/',
                            lambda match: " " * len(match.group()), source, flags=re.S)
            classes = []
            for declaration in re.finditer(r"\bclass\s+(\w+)[^{]*\{", masked):
                start = declaration.end() - 1
                depth = 1
                end = start + 1
                while depth and end < len(masked):
                    depth += (masked[end] == "{") - (masked[end] == "}")
                    end += 1
                classes.append((start, end, declaration.group(1)))
            methods = re.finditer(r"((?:\s*\[[^\]]+\])+\s*)public\s+(?:[\w<>,.?\[\]]+\s+)+(\w+)\s*\(", source)
            for method in methods:
                attributes = method.group(1)
                cases = len(re.findall(r"\bTestCase\s*\(", attributes))
                if not cases and re.search(r"\b(?:Test|UnityTest)\b", attributes):
                    cases = 1
                if not cases:
                    continue
                self.assertIsNotNone(namespace, str(path))
                fixture = next(name for start, end, name in reversed(classes) if start < method.start() < end)
                identity = f"{namespace.group(1)}.{fixture}"
                is_graphical = 'Category("RequiresGraphics")' in attributes
                self.assertFalse(is_graphical and platform != "playmode", str(path))
                destination = graphical if is_graphical else actual[platform]
                destination[identity] = destination.get(identity, 0) + cases
        self.assertEqual(seen, {"Lumenfall.GameEvents.Tests", "Lumenfall.Startup.Tests",
                                "Lumenfall.World.PlayModeTests", "Lumenfall.Startup.PlayModeTests"})
        for platform, expected_total in [("editmode", 359), ("playmode", 17)]:
            self.assertEqual(actual[platform], platforms[platform]["fixtures"])
            self.assertEqual(sum(actual[platform].values()), expected_total)
        self.assertEqual(graphical, gameplay.read_manifest(graphical=True)["playmode"]["fixtures"])
        self.assertEqual(sum(graphical.values()), 5)

    def test_workflow_uses_one_current_checkout_action_and_verified_cache_save(self):
        workflow = (gameplay.ROOT / ".github/workflows/pr-validation.yml").read_text()
        self.assertEqual(workflow.count("uses: game-ci/unity-test-runner@32e57712352b500e17974b245a6dce9e11a73213"), 1)
        self.assertIn("testMode: all", workflow)
        self.assertIn("customParameters: -testCategory !RequiresGraphics", workflow)
        unity_job = workflow.split("  run-gameplay-tests:", 1)[1]
        self.assertIn("lfs: true", unity_job.split("      - name: Free disk space", 1)[0])
        self.assertIn("projectPath: Lumenfall", workflow)
        self.assertIn("chownFilesTo: ${{ steps.unity-version.outputs.runner-owner }}", workflow)
        self.assertIn("steps.verify-gameplay.outcome == 'success'", workflow)
        self.assertIn("name: Unity EditMode tests", workflow)
        self.assertIn("name: Gameplay regression tests", workflow)
        self.assertIn("if [[ -e artifacts/gameplay || -L artifacts/gameplay ]]", workflow)
        self.assertNotIn("git fetch", workflow)
        self.assertNotIn("matrix:", workflow)


if __name__ == "__main__":
    unittest.main()
