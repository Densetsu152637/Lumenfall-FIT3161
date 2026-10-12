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


class GameplayContractTests(unittest.TestCase):
    def test_manifest_matches_current_test_sources_and_expected_totals(self):
        platforms = gameplay.read_manifest()
        for platform, expected_total in [("editmode", 156), ("playmode", 6)]:
            actual = {}
            for path in (gameplay.ROOT / "Lumenfall/Assets").rglob("*Tests.cs"):
                if platform not in [part.lower() for part in path.parts]:
                    continue
                source = path.read_text(encoding="utf-8")
                namespace = re.search(r"namespace\s+([\w.]+)", source).group(1)
                fixture = re.search(r"public\s+(?:sealed\s+)?class\s+(\w+)", source).group(1)
                self.assertNotRegex(source, r"\[(?:Ignore|Explicit)\b")
                actual[f"{namespace}.{fixture}"] = len(re.findall(r"\[(?:Test|UnityTest)\]|\[TestCase\(", source))
            self.assertEqual(actual, platforms[platform]["fixtures"])
            self.assertEqual(sum(actual.values()), expected_total)

    def test_workflow_uses_one_current_checkout_action_and_verified_cache_save(self):
        workflow = (gameplay.ROOT / ".github/workflows/pr-validation.yml").read_text()
        self.assertEqual(workflow.count("uses: game-ci/unity-test-runner@v4"), 1)
        self.assertIn("testMode: all", workflow)
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
