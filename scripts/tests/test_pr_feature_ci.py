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


class FeatureCacheTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name).resolve()
        self.output = self.root / "Temp/pr-tests"
        self.location = self.output / "pr_03"
        self.project = self.location / "snapshot/Lumenfall"
        self.library = self.project / "Library"
        self.seed = self.root / ci.CACHE_LIBRARY
        self.catalog = self.root / "catalog"
        suite = self.catalog / "pr_03"
        suite.mkdir(parents=True)
        (suite / "acceptance.cs").write_text("fixture source")
        for name, value in (("ROOT", self.root), ("CATALOG", self.catalog)):
            patcher = patch.object(ci.runner, name, value)
            patcher.start()
            self.addCleanup(patcher.stop)
        self.entry = {"number": 3, "suite": "pr_03", "commit": "a" * 40,
                      "expected_cases": {"EditMode": {"Feature.Edit": 2}}}
        self.configuration(self.project)
        self.prepared = {"status": "prepared", "commit": self.entry["commit"],
                         "suite_sha256": ci.runner.suite_hash(suite)}
        self.write_json("report-prepare.json", self.prepared)
        self.marker = {"commit": self.entry["commit"], "pr": 3}
        self.write_json("snapshot.json", self.marker)

    def configuration(self, project):
        for name in ("Packages", "ProjectSettings", "Assets"):
            (project / name).mkdir(parents=True)
        (project / "Packages/manifest.json").write_text('{"dependencies": {}}')
        (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.4.10f1\n")

    def write_json(self, name, value):
        (self.location / name).write_text(json.dumps(value))

    def seed_content(self, path):
        path.mkdir(parents=True)
        (path / "imported.bin").write_bytes(b"imported data")

    def successful_verification(self):
        artifacts = self.root / "artifacts"
        directory = artifacts / "editmode"
        directory.mkdir(parents=True)
        (directory / "results.xml").write_text(
            '<test-run result="Passed">' + '<test-case classname="Feature.Edit" result="Passed" />' * 2
            + '</test-run>')
        report = ci.verify(self.entry, self.output, artifacts, {"EditMode": "success"})
        self.assertEqual(report["status"], "passed")
        self.write_json("ci-report.json", report)

    def test_configuration_key_is_shared_across_assets_targets_and_suites(self):
        other = self.root / "Temp/pr-tests/pr_09/snapshot/Lumenfall"
        self.configuration(other)
        (self.project / "Assets/first.cs").write_text("first target and suite")
        (other / "Assets/second.cs").write_text("different target and suite")
        self.assertEqual(ci.config_hash(self.project), ci.config_hash(other))

    def test_configuration_key_tracks_package_settings_names_and_bytes(self):
        original = ci.config_hash(self.project)
        for relative in ("Packages/manifest.json", "ProjectSettings/ProjectVersion.txt"):
            with self.subTest(relative=relative):
                path = self.project / relative
                content = path.read_bytes()
                path.write_bytes(content + b"changed")
                self.assertNotEqual(original, ci.config_hash(self.project))
                path.write_bytes(content)
                renamed = path.with_suffix(".renamed")
                path.rename(renamed)
                self.assertNotEqual(original, ci.config_hash(self.project))
                renamed.rename(path)

    def test_prepare_emits_extracted_configuration_hash(self):
        with patch.object(ci.runner, "main", return_value=0):
            outputs = ci.prepare(self.entry, self.output)
        self.assertEqual(outputs["config-hash"], ci.config_hash(self.project))
        self.assertEqual(outputs["unity-version"], "6000.4.10f1")
        self.assertEqual(outputs["suite-hash"], self.prepared["suite_sha256"])

    def test_cold_restore_is_a_noop(self):
        self.assertFalse(ci.cache_restore(self.entry, self.output))
        self.assertFalse(self.library.exists())
        self.assertFalse(self.seed.exists())

    def test_restore_and_validated_save_move_only_library(self):
        self.seed_content(self.seed)
        self.assertTrue(ci.cache_restore(self.entry, self.output))
        self.assertFalse(self.seed.exists())
        self.assertEqual((self.library / "imported.bin").read_bytes(), b"imported data")
        self.successful_verification()
        ci.cache_save(self.entry, self.output)
        self.assertFalse(self.library.exists())
        self.assertEqual((self.seed / "imported.bin").read_bytes(), b"imported data")
        self.assertEqual(json.loads((self.location / "snapshot.json").read_text()), self.marker)
        self.assertTrue((self.project / "Packages/manifest.json").is_file())

    def test_restore_refuses_occupied_destination_even_when_cold(self):
        self.seed_content(self.library)
        with self.assertRaisesRegex(ValueError, "occupied"):
            ci.cache_restore(self.entry, self.output)
        self.seed_content(self.seed)
        with self.assertRaisesRegex(ValueError, "occupied"):
            ci.cache_restore(self.entry, self.output)
        self.assertTrue((self.seed / "imported.bin").exists())
        self.assertTrue((self.library / "imported.bin").exists())

    def test_save_refuses_missing_failed_stale_and_occupied_evidence(self):
        self.seed_content(self.library)
        with self.assertRaises(OSError):
            ci.cache_save(self.entry, self.output)
        self.successful_verification()
        report = json.loads((self.location / "ci-report.json").read_text())
        for changes in ({"status": "failed"}, {"errors": ["missing tests"]}, {"commit": "b" * 40},
                        {"pr": 9}, {"suite_sha256": "stale"}):
            with self.subTest(changes=changes):
                self.write_json("ci-report.json", dict(report, **changes))
                with self.assertRaisesRegex(ValueError, "successful verification"):
                    ci.cache_save(self.entry, self.output)
                self.assertTrue(self.library.exists())
        self.write_json("ci-report.json", report)
        self.seed_content(self.seed)
        with self.assertRaisesRegex(ValueError, "occupied"):
            ci.cache_save(self.entry, self.output)

    def test_cache_operations_require_owned_current_preparation(self):
        for changes in ({"pr": 9}, {"commit": "b" * 40}):
            with self.subTest(changes=changes):
                self.write_json("snapshot.json", dict(self.marker, **changes))
                with self.assertRaisesRegex(ValueError, "owned prepared"):
                    ci.cache_restore(self.entry, self.output)
        self.write_json("snapshot.json", self.marker)
        self.write_json("report-prepare.json", dict(self.prepared, suite_sha256="stale"))
        with self.assertRaisesRegex(ValueError, "owned prepared"):
            ci.cache_restore(self.entry, self.output)
        for output in (self.root / "outside", self.root / "Temp/../outside"):
            with self.subTest(output=output), self.assertRaisesRegex(ValueError, "escapes owned"):
                ci.cache_restore(self.entry, output)

    def test_links_in_cache_paths_and_ancestors_are_rejected(self):
        # Mock link detection so this guard is also exercised on Windows without symlink privilege.
        original = Path.is_symlink
        for linked in (self.seed, self.seed.parent, self.library, self.project,
                       self.location / "snapshot.json"):
            with self.subTest(linked=linked):
                with patch.object(Path, "is_symlink", lambda path: path == linked or original(path)):
                    with self.assertRaisesRegex(ValueError, "contains a link"):
                        ci.cache_restore(self.entry, self.output)

    def test_configuration_links_are_rejected(self):
        original = Path.is_symlink
        for linked in (self.project / "Packages", self.project / "Packages/manifest.json"):
            with self.subTest(linked=linked):
                with patch.object(Path, "is_symlink", lambda path: path == linked or original(path)):
                    with self.assertRaisesRegex(ValueError, "[Ll]inked"):
                        ci.config_hash(self.project)


if __name__ == "__main__":
    unittest.main()
