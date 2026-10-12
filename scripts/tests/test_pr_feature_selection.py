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
    def test_readiness_transition_does_not_repeat_draft_checks(self):
        root = Path(__file__).resolve().parents[2]
        workflows = [root / ".github/workflows/pr-validation.yml"]
        workflows.extend(root / f".github/workflows/feature-{group}.yaml" for group in selector.GROUPS)
        for path in workflows:
            with self.subTest(workflow=path.name):
                self.assertIn("types: [opened, synchronize, reopened]", path.read_text())
                self.assertNotIn("ready_for_review", path.read_text())

    def test_group_mapping_covers_manifest_exactly_once(self):
        numbers = [number for suites in selector.GROUPS.values() for number, _ in suites]
        manifest = json.loads((Path(__file__).resolve().parents[2] / "tests/pr_features/pr_manifest.json").read_text())
        self.assertEqual(sorted(numbers), sorted(entry["number"] for entry in manifest["pull_requests"]))
        self.assertEqual(len(numbers), len(set(numbers)))

    def test_single_or_multiple_suite_edits_select_only_affected_suites(self):
        self.assertEqual(selector.select_numbers("presentation-ui", ["tests/pr_features/pr_18/EditMode/Tests.cs"]), {18})
        self.assertEqual(selector.select_numbers("presentation-ui", ["tests/pr_features/pr_18/EditMode/Tests.cs", "tests/pr_features/pr_13/PlayMode/Tests.cs"]), {13, 18})

    def test_other_groups_and_catalog_documentation_do_not_select_suites(self):
        self.assertEqual(selector.select_numbers("audio", ["tests/pr_features/pr_18/README.md", ".github/workflows/feature-world-story.yaml", "tests/pr_features/README.md"]), set())
        self.assertEqual(selector.select_numbers("audio", ["tests/pr_features/pr_09/README.md", "tests/pr_features/VALIDATION.md"]), set())

    def test_shared_helpers_trigger_every_group(self):
        root = Path(__file__).resolve().parents[2]
        for group in selector.GROUPS:
            workflow = (root / f".github/workflows/feature-{group}.yaml").read_text()
            for path in ["scripts/select_pr_feature_suites.py", "scripts/select_pr_checks.py", "scripts/free_unity_disk_space.py"]:
                with self.subTest(group=group, path=path):
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


if __name__ == "__main__":
    unittest.main()
