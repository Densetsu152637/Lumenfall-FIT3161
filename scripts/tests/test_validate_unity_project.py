from __future__ import annotations

import json
import sys
import tempfile
import unittest
from pathlib import Path


SCRIPTS_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS_DIRECTORY))

from validate_unity_project import validate_project  # noqa: E402


class UnityProjectValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.project = Path(self.temporary_directory.name) / "Game"
        self.assets = self.project / "Assets"
        (self.project / "Packages").mkdir(parents=True)
        (self.project / "ProjectSettings").mkdir()
        self.assets.mkdir()

        (self.project / "Packages" / "manifest.json").write_text("{}", encoding="utf-8")
        (self.project / "Packages" / "packages-lock.json").write_text("{}", encoding="utf-8")
        (self.project / "ProjectSettings" / "ProjectVersion.txt").write_text(
            "m_EditorVersion: 6000.4.10f1\n", encoding="utf-8"
        )
        (self.project / "ProjectSettings" / "EditorBuildSettings.asset").write_text(
            "EditorBuildSettings:\n  m_Scenes: []\n", encoding="utf-8"
        )

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def create_asset(self, relative_path: str, guid: str) -> Path:
        asset = self.assets / relative_path
        asset.parent.mkdir(parents=True, exist_ok=True)

        current = asset.parent
        while current != self.assets:
            metadata = current.with_name(f"{current.name}.meta")
            if not metadata.exists():
                directory_guid = f"{abs(hash(current.as_posix())):032x}"[-32:]
                metadata.write_text(f"guid: {directory_guid}\n", encoding="utf-8")
            current = current.parent

        asset.write_text(json.dumps({"name": relative_path}), encoding="utf-8")
        asset.with_name(f"{asset.name}.meta").write_text(f"guid: {guid}\n", encoding="utf-8")
        return asset

    def test_valid_minimal_project_passes(self) -> None:
        self.create_asset("Data/config.json", "1" * 32)

        self.assertEqual([], validate_project(self.project))

    def test_missing_metadata_is_reported(self) -> None:
        asset = self.create_asset("Data/config.json", "2" * 32)
        asset.with_name(f"{asset.name}.meta").unlink()

        errors = validate_project(self.project)

        self.assertTrue(any("missing metadata" in error and "config.json.meta" in error for error in errors))

    def test_duplicate_guids_are_reported(self) -> None:
        duplicate_guid = "3" * 32
        self.create_asset("first.json", duplicate_guid)
        self.create_asset("second.json", duplicate_guid)

        errors = validate_project(self.project)

        self.assertTrue(any("Duplicate Unity GUID" in error for error in errors))

    def test_invalid_json_is_reported(self) -> None:
        asset = self.create_asset("broken.json", "4" * 32)
        asset.write_text("{not-json}", encoding="utf-8")

        errors = validate_project(self.project)

        self.assertTrue(any("Invalid JSON" in error and "broken.json" in error for error in errors))

    def test_vendor_utf16_documentation_is_accepted(self) -> None:
        asset = self.create_asset("ReadMe.txt", "5" * 32)
        asset.write_text("Vendor documentation: café\n", encoding="utf-16")
        self.assertEqual([], validate_project(self.project))

    def test_non_utf8_source_is_still_rejected(self) -> None:
        asset = self.create_asset("broken.cs", "6" * 32)
        asset.write_text("class Example {}", encoding="utf-16")
        self.assertTrue(any("Cannot read" in error for error in validate_project(self.project)))

    def test_decorative_dividers_are_not_conflicts(self) -> None:
        asset = self.create_asset("ReadMe.txt", "7" * 32)
        asset.write_text("=" * 72 + "\nVendor readme\n", encoding="utf-8")
        self.assertEqual([], validate_project(self.project))

    def test_each_conflict_marker_is_reported(self) -> None:
        asset = self.create_asset("broken.cs", "8" * 32)
        for marker in ("<<<<<<< HEAD", "=======", ">>>>>>> main"):
            with self.subTest(marker=marker):
                asset.write_text(marker + "\n", encoding="utf-8")
                self.assertTrue(any("merge-conflict" in error for error in validate_project(self.project)))

    def test_orphaned_metadata_is_reported(self) -> None:
        (self.assets / "missing.prefab.meta").write_text("guid: " + "9" * 32 + "\n", encoding="utf-8")
        self.assertTrue(any("Orphaned" in error for error in validate_project(self.project)))

    def test_missing_enabled_build_scene_is_reported(self) -> None:
        (self.project / "ProjectSettings" / "EditorBuildSettings.asset").write_text(
            "EditorBuildSettings:\n  m_Scenes:\n  - enabled: 1\n    path: Assets/Missing.unity\n",
            encoding="utf-8",
        )
        self.assertTrue(any("Enabled build scene" in error for error in validate_project(self.project)))


if __name__ == "__main__":
    unittest.main()
