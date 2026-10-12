from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from select_pr_checks import requires_unity, select_checks  # noqa: E402


def pr_event(base: str = "a" * 40, head: str = "b" * 40) -> dict:
    return {"pull_request": {"base": {"sha": base}, "head": {"sha": head}}}


class CheckSelectionTests(unittest.TestCase):
    def test_documentation_only_can_skip_unity(self) -> None:
        self.assertFalse(requires_unity(["README.md", "LICENSE", "docs/architecture/design.md", ".github/CI.md"]))
        self.assertFalse(requires_unity(["tests/README.md"]))

    def test_affected_inputs_and_unknown_paths_require_unity(self) -> None:
        for path in [
            "Lumenfall/Assets/Script.cs", "Lumenfall/Assets/Tests/Test.cs",
            "Lumenfall/Assets/README.md", "Lumenfall/Assets/picture.png.meta",
            "Lumenfall/Packages/packages-lock.json", "Lumenfall/ProjectSettings/ProjectVersion.txt",
            ".github/workflows/pr-validation.yml", ".github/workflows/release-build.yml",
            "scripts/check.py", "tools/test.sh", ".gitattributes", "new-input.json", "tests/gameplay_suites.json",
        ]:
            with self.subTest(path=path):
                self.assertTrue(requires_unity(["README.md", path]))

    def test_empty_or_noncanonical_paths_fail_closed(self) -> None:
        for paths in [[], [""], ["/README.md"], ["docs/../README.md"], ["docs//file.md"], ["docs\\file.md"]]:
            with self.subTest(paths=paths):
                self.assertTrue(requires_unity(paths))

    def test_manual_main_and_unknown_events_always_run(self) -> None:
        for name in ["push", "workflow_dispatch", "merge_group", ""]:
            with self.subTest(name=name), patch("select_pr_checks.subprocess.run") as run:
                self.assertTrue(select_checks(name, {}, Path.cwd())[0])
                run.assert_not_called()

    def test_missing_or_invalid_event_data_fails_closed(self) -> None:
        for event in [None, [], {}, {"pull_request": {}}, pr_event("main"), pr_event(head=None), pr_event(head="a" * 40)]:
            with self.subTest(event=event), patch("select_pr_checks.subprocess.run") as run:
                self.assertTrue(select_checks("pull_request", event, Path.cwd())[0])
                run.assert_not_called()

    def test_unavailable_history_and_timeout_fail_closed(self) -> None:
        for error in [subprocess.CalledProcessError(128, "git"), subprocess.TimeoutExpired("git", 30), OSError("git unavailable")]:
            with self.subTest(error=error), patch("select_pr_checks.subprocess.run", side_effect=error):
                self.assertTrue(select_checks("pull_request", pr_event(), Path.cwd())[0])

    def test_missing_or_malformed_diff_data_fails_closed(self) -> None:
        for data in [b"", b"README.md", b"docs/\xff.md\0", b"README.md\0\0"]:
            with self.subTest(data=data), patch("select_pr_checks.subprocess.run", return_value=subprocess.CompletedProcess([], 0, data)):
                self.assertTrue(select_checks("pull_request", pr_event(), Path.cwd())[0])

    def test_draft_pr_uses_same_file_rules(self) -> None:
        event = pr_event()
        event["pull_request"]["draft"] = True
        for data, required in [(b"README.md\0", False), (b"scripts/check.py\0", True)]:
            with self.subTest(data=data), patch("select_pr_checks.subprocess.run", return_value=subprocess.CompletedProcess([], 0, data)):
                self.assertEqual(required, select_checks("pull_request", event, Path.cwd())[0])

    def test_real_git_diff_retains_deleted_and_renamed_source(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            repository = Path(directory)

            def git(*args: str) -> str:
                return subprocess.run(["git", *args], cwd=repository, check=True, capture_output=True, text=True).stdout.strip()

            git("init", "--quiet")
            git("config", "user.email", "ci-test@example.invalid")
            git("config", "user.name", "CI test")
            (repository / "source.cs").write_text("class Source {}\n", encoding="utf-8")
            git("add", "source.cs")
            git("commit", "--quiet", "-m", "source")
            base = git("rev-parse", "HEAD")
            (repository / "source.cs").rename(repository / "README.md")
            git("add", "--all")
            git("commit", "--quiet", "-m", "move into documentation")
            self.assertTrue(select_checks("pull_request", pr_event(base, git("rev-parse", "HEAD")), repository)[0])
            (repository / "README.md").unlink()
            git("add", "--all")
            git("commit", "--quiet", "-m", "remove")
            self.assertTrue(select_checks("pull_request", pr_event(base, git("rev-parse", "HEAD")), repository)[0])
            self.assertFalse(select_checks("pull_request", pr_event(git("rev-parse", "HEAD^"), git("rev-parse", "HEAD")), repository)[0])


if __name__ == "__main__":
    unittest.main()
