from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from publish_main_bundle import is_bundle_release, publish_bundle  # noqa: E402


class MainBundlePublicationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.directory = Path(self.temporary_directory.name)
        self.tag = "main-200"
        for suffix in (".zip", ".zip.sha256"):
            (self.directory / f"Lumenfall-{self.tag}-windows-x64{suffix}").write_bytes(b"bundle")
        self.releases = [
            {"tag_name": "main-100", "assets": []},
            {"tag_name": "v0.1.0", "assets": [{"name": "Lumenfall-v0.1.0-windows-x64.zip"}]},
            {"tag_name": "tools-v1", "assets": [{"name": "tools.zip"}]},
        ]
        self.commands = []
        self.heads = ["current", "current"]
        self.fail_operation = None
        patcher = patch("publish_main_bundle.gh", side_effect=self.run_gh)
        self.mock_gh = patcher.start()
        self.addCleanup(patcher.stop)

    def run_gh(self, *arguments: str) -> str:
        self.commands.append(arguments)
        if arguments[:2] == self.fail_operation:
            raise subprocess.CalledProcessError(1, ["gh", *arguments])
        if arguments[0] == "api":
            if arguments[1].endswith("git/ref/heads/main"):
                return json.dumps({"object": {"sha": self.heads.pop(0)}})
            self.assertIn("--paginate", arguments)
            self.assertIn("--slurp", arguments)
            # The tagged bundle and unrelated release are on a second API page.
            return json.dumps([self.releases[:1], self.releases[1:]])
        return ""

    def publish(self) -> bool:
        return publish_bundle(self.directory, "owner/repo", self.tag, "current")

    def operations(self, operation: str) -> list:
        return [command for command in self.commands if command[:2] == ("release", operation)]

    def test_publishes_before_pruning_all_pages_and_preserves_unrelated_releases(self) -> None:
        self.assertTrue(self.publish())
        create = self.operations("create")[0]
        self.assertIn("--draft", create)
        self.assertEqual("current", create[create.index("--target") + 1])
        upload = self.operations("upload")[0]
        self.assertIn("--clobber", upload)
        self.assertEqual(2, len([argument for argument in upload if argument.startswith(str(self.directory))]))
        publish = self.operations("edit")[0]
        self.assertIn("--draft=false", publish)
        self.assertIn("--prerelease=false", publish)
        self.assertIn("--latest", publish)
        deleted = self.operations("delete")
        self.assertEqual(["main-100", "v0.1.0"], [command[2] for command in deleted])
        self.assertLess(self.commands.index(upload), self.commands.index(publish))
        self.assertLess(self.commands.index(publish), self.commands.index(deleted[0]))
        self.assertTrue(all("--cleanup-tag" not in command for command in deleted))

    def test_rerun_reuses_current_release_and_never_deletes_it(self) -> None:
        self.releases.append({"tag_name": self.tag, "assets": []})
        self.assertTrue(self.publish())
        self.assertEqual([], self.operations("create"))
        self.assertNotIn(self.tag, [command[2] for command in self.operations("delete")])

    def test_superseded_run_cannot_publish_or_delete_newer_release(self) -> None:
        self.heads = ["newer"]
        self.assertFalse(self.publish())
        self.assertEqual(1, len(self.commands))

    def test_main_advancing_during_upload_keeps_previous_public_release(self) -> None:
        self.heads = ["current", "newer"]
        self.assertFalse(self.publish())
        self.assertEqual(1, len(self.operations("upload")))
        self.assertEqual([], self.operations("edit"))
        self.assertEqual([], self.operations("delete"))

    def test_api_create_upload_or_publish_failure_never_deletes_previous_releases(self) -> None:
        for operation in ("api", "create", "upload", "edit"):
            with self.subTest(operation=operation):
                self.commands.clear()
                self.heads = ["current", "current"]
                self.fail_operation = (
                    ("api", "repos/owner/repo/releases?per_page=100")
                    if operation == "api" else ("release", operation)
                )
                with self.assertRaises(subprocess.CalledProcessError):
                    self.publish()
                self.assertEqual([], self.operations("delete"))

    def test_cleanup_failure_is_reported_after_replacement_is_published(self) -> None:
        self.fail_operation = ("release", "delete")
        with self.assertRaises(subprocess.CalledProcessError):
            self.publish()
        self.assertEqual(1, len(self.operations("edit")))
        self.assertEqual(1, len(self.operations("delete")))

    def test_missing_or_empty_assets_prevent_release_changes(self) -> None:
        archive = self.directory / f"Lumenfall-{self.tag}-windows-x64.zip"
        for content in (None, b""):
            with self.subTest(content=content):
                if content is None:
                    archive.unlink()
                else:
                    archive.write_bytes(content)
                self.heads = ["current"]
                with self.assertRaises(ValueError):
                    self.publish()
                self.assertEqual([], self.operations("create"))

    def test_invalid_tag_is_rejected_before_any_api_call(self) -> None:
        self.tag = "v0.1.0"
        with self.assertRaises(ValueError):
            self.publish()
        self.mock_gh.assert_not_called()

    def test_release_recognition_requires_main_run_tag_or_game_asset(self) -> None:
        for tag, assets, expected in (
            ("main-123", [], True),
            ("main-not-a-run", [], False),
            ("v1", ["Lumenfall-v1-windows-x64.zip.sha256"], True),
            ("v1", ["another-game.zip"], False),
        ):
            with self.subTest(tag=tag, assets=assets):
                self.assertEqual(expected, is_bundle_release({
                    "tag_name": tag, "assets": [{"name": name} for name in assets],
                }))


if __name__ == "__main__":
    unittest.main()
