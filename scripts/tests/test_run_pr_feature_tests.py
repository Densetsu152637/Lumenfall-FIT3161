"""Behavioral checks for fail-closed, isolated PR test execution."""

import importlib.util
import io
import json
import shutil
import tarfile
import tempfile
import unittest
import contextlib
from pathlib import Path
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("feature_runner", Path(__file__).parents[1] / "run_pr_feature_tests.py")
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class FeatureRunnerTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def manifest(self, entries):
        path = self.root / "manifest.json"
        path.write_text(json.dumps({"schema_version": 1, "pull_requests": entries}))
        return path

    def entry(self, **changes):
        return dict({"number": 3, "commit": "a" * 40, "suite": "pr_03"}, **changes)

    def xml(self, text):
        path = self.root / "results.xml"
        path.write_text(text)
        return path

    def test_manifest_accepts_pinned_unique_prs(self):
        entries = [self.entry(), self.entry(number=23, suite="pr_23", commit="b" * 40)]
        self.assertEqual(runner.read_manifest(self.manifest(entries)), entries)

    def test_manifest_rejects_duplicate_numbers_short_refs_and_traversal(self):
        invalid = [[self.entry(), self.entry()], [self.entry(commit="main")],
                   [self.entry(suite="../../pr_03")], [], [self.entry(number=0)]]
        for entries in invalid:
            with self.subTest(entries=entries), self.assertRaises(ValueError):
                runner.read_manifest(self.manifest(entries))

    def archive(self, members):
        path = self.root / "archive.tar"
        with tarfile.open(path, "w") as archive:
            for name, kind in members:
                info = tarfile.TarInfo(name)
                if kind == "file":
                    content = b"fixture"
                    info.size = len(content)
                    archive.addfile(info, io.BytesIO(content))
                else:
                    info.type = tarfile.SYMTYPE if kind == "link" else tarfile.FIFOTYPE
                    info.linkname = "../../outside"
                    archive.addfile(info)
        return path

    def test_extract_validates_whole_archive_before_writing(self):
        destination = self.root / "snapshot"
        destination.mkdir()
        for name, kind in [("../outside", "file"), ("/absolute", "file"),
                           ("C:/escape", "file"), ("folder\\escape", "file"),
                           ("link", "link"), ("pipe", "pipe")]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                runner.extract_archive(self.archive([("safe", "file"), (name, kind)]), destination)
            self.assertFalse((destination / "safe").exists())

    def test_extract_keeps_files_inside_snapshot(self):
        destination = self.root / "snapshot"
        destination.mkdir()
        runner.extract_archive(self.archive([("Lumenfall/Assets/fixture.txt", "file")]), destination)
        self.assertEqual((destination / "Lumenfall/Assets/fixture.txt").read_text(), "fixture")

    def test_prepare_refuses_existing_unowned_or_different_revision(self):
        output = self.root / "output"
        (output / "snapshot").mkdir(parents=True)
        with self.assertRaisesRegex(ValueError, "Refusing to overwrite"):
            runner.prepare(self.entry(), output)
        (output / "snapshot.json").write_text(json.dumps({"commit": "b" * 40}))
        with self.assertRaisesRegex(ValueError, "Refusing to overwrite"):
            runner.prepare(self.entry(), output)

    def test_stage_only_selected_suite_and_remove_stale_test_sources(self):
        catalog = self.root / "catalog"
        source = catalog / "pr_03/EditMode"
        source.mkdir(parents=True)
        (source / "First.cs").write_text("// first")
        other = catalog / "pr_04/EditMode"
        other.mkdir(parents=True)
        (other / "Other.cs").write_text("// other")
        project = self.root / "project"
        runner.stage_suite(self.entry(), project, catalog)
        target = project / "Assets/_Project/FeatureAcceptance/Tests/EditMode"
        first_meta = (target / "First.cs.meta").read_text()
        self.assertFalse((target / "Other.cs").exists())
        (source / "First.cs").unlink()
        (source / "Second.cs").write_text("// second")
        runner.stage_suite(self.entry(), project, catalog)
        self.assertFalse((target / "First.cs").exists())
        self.assertFalse((target / "First.cs.meta").exists())
        self.assertTrue((target / "Second.cs.meta").is_file())
        self.assertIn("guid:", first_meta)

    def test_stage_requires_executable_tests_not_only_readme(self):
        catalog = self.root / "catalog"
        suite = catalog / "pr_03"
        suite.mkdir(parents=True)
        (suite / "README.md").write_text("coverage")
        with self.assertRaisesRegex(ValueError, "no executable tests"):
            runner.stage_suite(self.entry(), self.root / "project", catalog)

    def test_assembly_membership_excludes_nested_test_assembly(self):
        folder = self.root / "Assets/Systems"
        child = folder / "Tests"
        child.mkdir(parents=True)
        definition = folder / "Runtime.asmdef"
        definition.write_text("{}")
        (folder / "Runtime.cs").write_text("// runtime")
        (child / "Tests.asmdef").write_text("{}")
        (child / "Test.cs").write_text("// test")
        self.assertEqual(runner.assembly_sources(self.root, definition), [folder / "Runtime.cs"])

    def test_xml_requires_actual_cases_and_complete_success(self):
        invalid = ['<test-run result="Passed" />',
                   '<test-run result="Passed"><test-case result="Skipped" /></test-run>',
                   '<test-run result="Passed"><test-case result="Inconclusive" /></test-run>',
                   '<test-run result="Failed"><test-case result="Passed" /></test-run>',
                   '<test-run result="Passed"><test-case result="Failed" /></test-run>']
        for text in invalid:
            with self.subTest(text=text), self.assertRaises(ValueError):
                runner.validate_results(self.xml(text))
        self.assertEqual(runner.validate_results(self.xml(
            '<test-run result="Passed"><test-case result="Passed" /><test-case result="Passed" /></test-run>')),
            {"passed": 2, "failed": 0, "skipped": 0})

    def test_missing_xml_is_failure(self):
        with self.assertRaisesRegex(ValueError, "did not produce"):
            runner.validate_results(self.root / "missing.xml")

    def test_unity_stale_success_cannot_mask_missing_new_results(self):
        xml = self.root / "EditMode.xml"
        xml.write_text('<test-run result="Passed"><test-case result="Passed" /></test-run>')
        with patch.object(runner, "command", return_value=0):
            result = runner.run_unity(self.root, Path("Unity.exe"), "EditMode", self.root, 10)
        self.assertEqual(result["status"], "failed")
        self.assertFalse(xml.exists())

    def test_unity_nonzero_exit_fails_even_with_passing_xml(self):
        def write_result(*args):
            (self.root / "EditMode.xml").write_text('<test-run result="Passed"><test-case result="Passed" /></test-run>')
            return 1
        with patch.object(runner, "command", side_effect=write_result):
            result = runner.run_unity(self.root, Path("Unity.exe"), "EditMode", self.root, 10)
        self.assertEqual(result["status"], "failed")
        self.assertEqual(result["passed"], 1)

    def test_unity_requires_every_expected_fixture_and_case(self):
        expected = {"Feature.First": 2, "Feature.Second": 1}
        for xml in [
            '<test-run result="Passed"><test-case classname="Unrelated" result="Passed" /></test-run>',
            '<test-run result="Passed"><test-case classname="Feature.First" result="Passed" /><test-case classname="Feature.First" result="Passed" /></test-run>',
            '<test-run result="Passed"><test-case classname="Feature.First" result="Passed" /><test-case classname="Feature.Second" result="Passed" /></test-run>',
        ]:
            with self.subTest(xml=xml), self.assertRaisesRegex(ValueError, "discovery differs"):
                runner.validate_results(self.xml(xml), expected)
        complete = '<test-run result="Passed"><test-case classname="Feature.First" result="Passed" /><test-case classname="Feature.First" result="Passed" /><test-case classname="Feature.Second" result="Passed" /></test-run>'
        self.assertEqual(runner.validate_results(self.xml(complete), expected)["passed"], 3)

    def test_python_empty_skipped_and_partial_discovery_fail(self):
        for index, (source, expected, code) in enumerate([
            ("", 3, 1),
            ("import unittest\nclass Cases(unittest.TestCase):\n @unittest.skip('fixture')\n def test_skip(self): pass\n", 1, 1),
            ("import unittest\nclass Cases(unittest.TestCase):\n def test_pass(self): pass\n", 3, 1),
            ("import unittest\nclass Cases(unittest.TestCase):\n def test_pass(self): pass\n", 1, 0),
        ]):
            directory = self.root / f"python_{index}"
            directory.mkdir()
            (directory / f"test_fixture_{index}.py").write_text(source)
            with self.subTest(index=index), contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(runner.execute_python_suite(directory, expected), code)

    def test_snapshot_hash_detects_modified_and_injected_source_but_ignores_cache(self):
        source = self.root / "Lumenfall/Assets/Runtime.cs"
        source.parent.mkdir(parents=True)
        source.write_text("original")
        original = runner.snapshot_hash(self.root)
        cache = self.root / "Lumenfall/Library/cache.bin"
        cache.parent.mkdir()
        cache.write_text("cache")
        self.assertEqual(runner.snapshot_hash(self.root), original)
        source.write_text("modified")
        self.assertNotEqual(runner.snapshot_hash(self.root), original)
        source.write_text("original")
        (source.parent / "Injected.cs").write_text("injected")
        self.assertNotEqual(runner.snapshot_hash(self.root), original)

    def test_preparation_restores_modified_source_and_removes_injected_source(self):
        output = self.root / "output"
        archives = []
        def fake_git(args, **kwargs):
            if args[1] == "archive":
                archives.append(args[2])
                with tarfile.open(fileobj=kwargs["stdout"], mode="w") as archive:
                    info = tarfile.TarInfo("Lumenfall/Assets/Runtime.cs")
                    content = b"authoritative source"
                    info.size = len(content)
                    archive.addfile(info, io.BytesIO(content))
        with patch.object(runner.subprocess, "run", side_effect=fake_git), \
                patch.object(runner, "stage_suite", return_value={}):
            project, _ = runner.prepare(self.entry(), output)
            source = project / "Assets/Runtime.cs"
            source.write_text("modified")
            (project / "Assets/Injected.cs").write_text("injected")
            cache = project / "Library/cache.bin"
            cache.parent.mkdir()
            cache.write_text("keep cache")
            runner.prepare(self.entry(), output)
            self.assertEqual(source.read_text(), "authoritative source")
            self.assertFalse((project / "Assets/Injected.cs").exists())
            self.assertEqual(cache.read_text(), "keep cache")
            runner.prepare(self.entry(commit="b" * 40), output, allow_retarget=True)
            self.assertEqual(json.loads((output / "snapshot.json").read_text())["commit"], "b" * 40)
            self.assertEqual(archives, ["a" * 40, "a" * 40, "b" * 40])
            self.assertEqual(cache.read_text(), "keep cache")
            # Cleanup may retire source/cache while retaining the ownership marker
            # and reports. The next invocation must recreate the pinned source.
            shutil.rmtree(project.parent)
            runner.prepare(self.entry(commit="b" * 40), output, allow_retarget=True)
            self.assertEqual(source.read_text(), "authoritative source")
            self.assertEqual(archives, ["a" * 40, "a" * 40, "b" * 40, "b" * 40])
    def test_python_only_all_does_not_require_unity(self):
        suite = self.root / "catalog/pr_03"
        suite.mkdir(parents=True)
        (suite / "test_case.py").write_text("fixture")
        entry = self.entry(expected_python_cases=3, expected_cases={})
        output = self.root / "output"
        (output / "pr_03").mkdir(parents=True)
        with patch.object(runner, "CATALOG", suite.parent), patch.object(runner, "read_manifest", return_value=[entry]), \
                patch.object(runner, "prepare", return_value=(self.root / "project", {})), \
                patch.object(runner, "command", return_value=0):
            self.assertEqual(runner.main(["--pr", "3", "--mode", "all", "--output", str(output),
                                          "--unity-editor", str(self.root / "missing-editor")]), 0)

    def test_missing_whole_platform_cannot_pass_all_mode(self):
        entry = self.entry(expected_cases={"EditMode": {"Feature.Edit": 1}, "PlayMode": {"Feature.Play": 1}})
        output = self.root / "output"
        with patch.object(runner, "read_manifest", return_value=[entry]), \
                patch.object(runner, "prepare", return_value=(self.root / "project", {"EditMode": 1})), \
                patch.object(runner, "command") as command:
            self.assertEqual(runner.main(["--pr", "3", "--mode", "all", "--output", str(output)]), 1)
            command.assert_not_called()
        report = json.loads((output / "pr_03/report.json").read_text())
        self.assertIn("Staged test platforms differ", report["error"])


if __name__ == "__main__":
    unittest.main()
