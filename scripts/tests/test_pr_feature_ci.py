"""Ensure Actions summaries cannot turn missing or stale tests into success."""

import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import pr_feature_ci as ci


class FeatureCiTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.catalog = self.root / "catalog"
        suite = self.catalog / "pr_03"
        suite.mkdir(parents=True)
        (suite / "acceptance.cs").write_text("fixture source")
        self.output = self.root / "output"
        self.location = self.output / "pr_03"
        self.location.mkdir(parents=True)
        self.artifacts = self.root / "artifacts"
        self.entry = {"number": 3, "suite": "pr_03", "commit": "a" * 40,
                      "expected_cases": {"EditMode": {"Feature.Edit": 2}, "PlayMode": {"Feature.Play": 1}}}
        catalog_patch = patch.object(ci.runner, "CATALOG", self.catalog)
        catalog_patch.start()
        self.addCleanup(catalog_patch.stop)
        self.hash = ci.runner.suite_hash(suite)
        self.prepared = {"status": "prepared", "commit": self.entry["commit"], "suite_sha256": self.hash}
        self.write_json("report-prepare.json", self.prepared)
        self.write_json("snapshot.json", {"commit": self.entry["commit"]})

    def write_json(self, name, value):
        (self.location / name).write_text(json.dumps(value))

    def xml(self, platform, classname, count):
        directory = self.artifacts / platform.lower()
        directory.mkdir(parents=True, exist_ok=True)
        cases = "".join(f'<test-case classname="{classname}" result="Passed" />' for _ in range(count))
        path = directory / "results.xml"
        path.write_text(f'<test-run result="Passed">{cases}</test-run>')
        return path

    def verify(self, **outcomes):
        return ci.verify(self.entry, self.output, self.artifacts, outcomes)

    def test_describe_python_only_suite_needs_no_unity(self):
        entry = dict(self.entry, expected_cases={}, expected_python_cases=3)
        outputs = ci.describe(entry)
        self.assertEqual(outputs["python"], "true")
        self.assertEqual(outputs["unity"], "false")
        self.assertEqual(outputs["editmode"], "false")
        self.assertEqual(outputs["playmode"], "false")

    def test_both_platforms_require_success_and_complete_cases(self):
        self.xml("EditMode", "Feature.Edit", 2)
        self.xml("PlayMode", "Feature.Play", 1)
        (self.artifacts / "editmode/coverage.xml").write_text("<coverage />")
        report = self.verify(EditMode="success", PlayMode="success")
        self.assertEqual(report["status"], "passed")
        self.assertEqual([result["passed"] for result in report["platforms"]], [2, 1])
        self.assertEqual(report["commit"], self.entry["commit"])

    def test_partial_discovery_fails_even_when_gameci_succeeds(self):
        self.xml("EditMode", "Feature.Edit", 1)
        self.xml("PlayMode", "Feature.Play", 1)
        report = self.verify(EditMode="success", PlayMode="success")
        self.assertEqual(report["status"], "failed")
        self.assertIn("discovery differs", report["errors"][0])

    def test_missing_platform_and_failed_action_cannot_reuse_xml(self):
        self.xml("EditMode", "Feature.Edit", 2)
        self.xml("PlayMode", "Feature.Play", 1)
        for outcome in ("failure", "skipped", "not_run"):
            with self.subTest(outcome=outcome):
                report = self.verify(EditMode="success", PlayMode=outcome)
                self.assertEqual(report["status"], "failed")
                self.assertIn("GameCI step outcome", report["errors"][0])
        (self.artifacts / "playmode/results.xml").unlink()
        self.assertEqual(self.verify(EditMode="success", PlayMode="success")["status"], "failed")

    def test_revision_or_suite_change_invalidates_preparation(self):
        self.xml("EditMode", "Feature.Edit", 2)
        self.xml("PlayMode", "Feature.Play", 1)
        for changes in ({"commit": "b" * 40}, {"suite_sha256": "stale"}):
            with self.subTest(changes=changes):
                self.write_json("report-prepare.json", dict(self.prepared, **changes))
                report = self.verify(EditMode="success", PlayMode="success")
                self.assertEqual(report["status"], "failed")
                self.assertIn("Preparation:", report["errors"][0])

    def test_duplicate_or_malformed_xml_fails_closed(self):
        path = self.xml("EditMode", "Feature.Edit", 2)
        duplicate = path.parent / "duplicate.xml"
        duplicate.write_bytes(path.read_bytes())
        with self.assertRaisesRegex(ValueError, "found 2"):
            ci.find_results(path.parent)
        duplicate.write_text("<invalid")
        report = self.verify(EditMode="success", PlayMode="skipped")
        self.assertEqual(report["status"], "failed")

    def python_evidence(self, **counts):
        self.entry.update(expected_cases={}, expected_python_cases=3)
        self.write_json("report-python.json", dict(self.prepared, status="passed", python={"exit_code": 0}))
        result = dict(executed=3, failed=0, errors=0, skipped=0, **counts)
        (self.location / "python.log").write_text("PR_FEATURE_PYTHON_RESULT=" + json.dumps(result))

    def test_python_evidence_requires_exact_count_and_no_skips(self):
        self.python_evidence()
        self.assertEqual(self.verify(Python="success")["status"], "passed")
        for key, value in (("executed", 0), ("executed", 2), ("skipped", 1), ("failed", 1)):
            with self.subTest(key=key, value=value):
                counts = dict(executed=3, failed=0, errors=0, skipped=0)
                counts[key] = value
                (self.location / "python.log").write_text("PR_FEATURE_PYTHON_RESULT=" + json.dumps(counts))
                self.assertEqual(self.verify(Python="success")["status"], "failed")

    def test_python_action_failure_cannot_pass_with_prior_success(self):
        self.python_evidence()
        self.assertEqual(self.verify(Python="failure")["status"], "failed")
        self.assertEqual(self.verify(Python="skipped")["status"], "failed")

    def test_missing_preparation_is_visible_even_with_passing_xml(self):
        self.xml("EditMode", "Feature.Edit", 2)
        self.xml("PlayMode", "Feature.Play", 1)
        (self.location / "report-prepare.json").unlink()
        report = self.verify(EditMode="success", PlayMode="success")
        self.assertEqual(report["status"], "failed")
        self.assertTrue(report["errors"][0].startswith("Preparation:"))


if __name__ == "__main__":
    unittest.main()
