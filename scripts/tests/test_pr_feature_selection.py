"""Exercise narrow matrix selection and conservative failure behavior."""

import json
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import select_pr_feature_suites as selector


class FeatureSelectionTests(unittest.TestCase):
    def test_single_catalog_matrix_limits_feature_login_pressure(self):
        root = Path(__file__).resolve().parents[2]
        callers = list((root / ".github/workflows").glob("feature-*.yaml"))
        self.assertEqual([path.name for path in callers], ["feature-acceptance.yaml"])
        workflow = callers[0].read_text()
        self.assertIn("      fail-fast: false\n      max-parallel: 6\n", workflow)
        self.assertEqual(workflow.count("name: Select affected suites"), 1)
        self.assertIn("scripts/select_pr_feature_suites.py --group all", workflow)
        self.assertIn("name: PR ${{ matrix.pr }} - ${{ matrix.feature }}", workflow)
        self.assertIn("uses: ./.github/workflows/pr-feature-suite.yaml", workflow)

    def test_readiness_transition_does_not_repeat_draft_checks(self):
        root = Path(__file__).resolve().parents[2]
        workflows = [root / ".github/workflows/pr-validation.yml"]
        workflows.append(root / ".github/workflows/feature-acceptance.yaml")
        for path in workflows:
            with self.subTest(workflow=path.name):
                self.assertIn("types: [opened, synchronize, reopened]", path.read_text())
                self.assertNotIn("ready_for_review", path.read_text())

    def test_group_mapping_covers_manifest_exactly_once(self):
        numbers = [number for suites in selector.GROUPS.values() for number, _ in suites]
        manifest = json.loads((Path(__file__).resolve().parents[2] / "tests/pr_features/pr_manifest.json").read_text())
        self.assertEqual(sorted(numbers), sorted(entry["number"] for entry in manifest["pull_requests"]))
        self.assertEqual(len(numbers), len(set(numbers)))
        selected = selector.select_matrix("all", "workflow_dispatch", {}, Path.cwd())["include"]
        self.assertEqual(sorted(item["pr"] for item in selected), sorted(numbers))
        self.assertEqual(len(selected), len(numbers))

    def test_single_or_multiple_suite_edits_select_only_affected_suites(self):
        self.assertEqual(selector.select_numbers("presentation-ui", ["tests/pr_features/pr_18/EditMode/Tests.cs"]), {18})
        self.assertEqual(selector.select_numbers("presentation-ui", ["tests/pr_features/pr_18/EditMode/Tests.cs", "tests/pr_features/pr_13/PlayMode/Tests.cs"]), {13, 18})

    def test_other_groups_and_catalog_documentation_do_not_select_suites(self):
        self.assertEqual(selector.select_numbers("audio", ["tests/pr_features/pr_18/README.md", ".github/workflows/feature-world-story.yaml", "tests/pr_features/README.md"]), set())
        self.assertEqual(selector.select_numbers("audio", ["tests/pr_features/pr_09/README.md", "tests/pr_features/VALIDATION.md"]), set())

    def test_union_path_filters_keep_shared_helpers_and_new_suites(self):
        root = Path(__file__).resolve().parents[2]
        workflow = (root / ".github/workflows/feature-acceptance.yaml").read_text()
        for path in ["scripts/select_pr_feature_suites.py", "scripts/select_pr_checks.py", "scripts/free_unity_disk_space.py",
                     ".github/workflows/feature-*.yaml", ".github/workflows/pr-feature-suite.yaml",
                     "tests/pr_features/pr_*/**", "tests/pr_features/pr_manifest.json",
                     "scripts/run_pr_feature_tests.py", "scripts/pr_feature_ci.py",
                     "scripts/tests/test_select_pr_checks.py", "scripts/tests/test_free_unity_disk_space.py",
                     "scripts/tests/test_*pr_feature*.py"]:
            with self.subTest(path=path):
                self.assertEqual(workflow.count(f"- '{path}'"), 2)

    def test_shared_inputs_workflows_and_unknown_paths_select_full_group(self):
        for path in ["scripts/run_pr_feature_tests.py", "scripts/pr_feature_ci.py", "scripts/select_pr_feature_suites.py",
                     "scripts/select_pr_checks.py", "scripts/free_unity_disk_space.py", "scripts/tests/test_pr_feature_selection.py",
                     "tests/pr_features/pr_manifest.json", ".github/workflows/pr-feature-suite.yaml",
                     ".github/workflows/feature-audio.yaml", "unknown.file", "tests/pr_features/pr_99/Test.cs"]:
            with self.subTest(path=path):
                self.assertEqual(selector.select_numbers("audio", [path]), {9, 22})

    def test_renames_include_source_and_destination_suites(self):
        self.assertEqual(selector.select_numbers("audio", ["tests/pr_features/pr_09/Tests.cs", "tests/pr_features/pr_22/Tests.cs"]), {9, 22})

    def test_manual_main_missing_event_and_failed_diff_select_full_group(self):
        for event_name in ["workflow_dispatch", "push", "pull_request"]:
            with self.subTest(event_name=event_name):
                self.assertEqual([item["pr"] for item in selector.select_matrix("audio", event_name, {}, Path.cwd())["include"]], [9, 22])
        with patch.object(selector, "changed_paths", side_effect=subprocess.CalledProcessError(128, "git")):
            self.assertEqual(len(selector.select_matrix("audio", "pull_request", {}, Path.cwd())["include"]), 2)

    def test_valid_draft_diff_selects_single_suite(self):
        with patch.object(selector, "changed_paths", return_value=["tests/pr_features/pr_09/Tests.cs"]):
            result = selector.select_matrix("audio", "pull_request", {"pull_request": {"draft": True}}, Path.cwd())
        self.assertEqual(result, {"include": [{"pr": 9, "feature": "Event audio"}]})

    def test_all_group_selects_mixed_group_changes_once(self):
        paths = ["tests/pr_features/pr_09/Tests.cs", "tests/pr_features/pr_18/EditMode/Tests.cs",
                 "tests/pr_features/pr_03/Tests.cs", "tests/pr_features/pr_09/Tests.cs"]
        with patch.object(selector, "changed_paths", return_value=paths):
            matrix = selector.select_matrix("all", "pull_request", {}, Path.cwd())
        self.assertEqual({item["pr"] for item in matrix["include"]}, {3, 9, 18})
        self.assertEqual(len(matrix["include"]), 3)

    def test_all_group_documentation_only_selects_empty_matrix(self):
        paths = [".github/CI.md", "tests/pr_features/README.md", "tests/pr_features/VALIDATION.md",
                 "tests/pr_features/pr_09/README.md", "tests/pr_features/pr_18/README.md"]
        with patch.object(selector, "changed_paths", return_value=paths):
            self.assertEqual(selector.select_matrix("all", "pull_request", {}, Path.cwd()), {"include": []})

    def test_all_group_shared_inputs_unknown_suite_and_uncertain_diff_select_all(self):
        for path in ("scripts/pr_feature_ci.py", ".github/workflows/feature-acceptance.yaml",
                     "tests/pr_features/pr_99/EditMode/Tests.cs", "unknown.file"):
            with self.subTest(path=path), patch.object(selector, "changed_paths", return_value=[path]):
                self.assertEqual(len(selector.select_matrix("all", "pull_request", {}, Path.cwd())["include"]), 22)
        for event_name in ("workflow_dispatch", "push", "pull_request"):
            with self.subTest(event_name=event_name):
                self.assertEqual(len(selector.select_matrix("all", event_name, {}, Path.cwd())["include"]), 22)
        for error in (ValueError("invalid event"), subprocess.CalledProcessError(128, "git")):
            with self.subTest(error=error), patch.object(selector, "changed_paths", side_effect=error):
                self.assertEqual(len(selector.select_matrix("all", "pull_request", {}, Path.cwd())["include"]), 22)

    def test_migrated_group_workflow_change_preserves_group_selection(self):
        with patch.object(selector, "changed_paths", return_value=[".github/workflows/feature-audio.yaml"]):
            matrix = selector.select_matrix("all", "pull_request", {}, Path.cwd())
        self.assertEqual([item["pr"] for item in matrix["include"]], [9, 22])


if __name__ == "__main__":
    unittest.main()
