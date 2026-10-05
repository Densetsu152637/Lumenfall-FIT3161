from __future__ import annotations

import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from package_game import package_game  # noqa: E402


class GameBundleTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.build = self.root / "build"
        self.output = self.root / "dist"
        self.files = {
            "Lumenfall.exe": b"player",
            "UnityPlayer.dll": b"engine",
            "Lumenfall_Data/globalgamemanagers": b"scene settings",
            "Lumenfall_Data/Managed/Game.dll": b"game scripts",
            "Lumenfall_Data/StreamingAssets/Maps/start.json": b'{"map": "start"}',
            "MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll": b"runtime",
        }
        for relative, content in self.files.items():
            path = self.build / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(content)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_bundle_extracts_with_every_player_file_and_matching_checksum(self) -> None:
        archive = package_game(self.build, self.output, "v1.2.3", "a" * 40)
        extracted = self.root / "extracted"
        with ZipFile(archive) as bundle:
            self.assertIsNone(bundle.testzip())
            bundle.extractall(extracted)
        for relative, content in self.files.items():
            self.assertEqual(content, (extracted / relative).read_bytes())
        metadata = json.loads((extracted / "build-info.json").read_text())
        self.assertEqual("v1.2.3", metadata["version"])
        self.assertEqual("a" * 40, metadata["commit"])
        self.assertEqual("windows-x64", metadata["platform"])
        self.assertIn("run Lumenfall.exe", (extracted / "README.txt").read_text())
        self.assertTrue((extracted / "LICENSE").is_file())
        checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
        self.assertEqual(
            f"{checksum}  {archive.name}\n",
            archive.with_suffix(".zip.sha256").read_text(),
        )

    def test_incomplete_or_empty_player_is_rejected_before_creating_bundle(self) -> None:
        for relative in self.files:
            with self.subTest(file=relative):
                path = self.build / relative
                content = path.read_bytes()
                path.unlink()
                # StreamingAssets are optional; they must be retained when present.
                if "StreamingAssets" not in relative:
                    with self.assertRaises(ValueError):
                        package_game(self.build, self.output, "v1.0.0", "abc")
                    self.assertFalse(self.output.exists())
                path.write_bytes(content)
        (self.build / "Lumenfall.exe").write_bytes(b"")
        with self.assertRaises(ValueError):
            package_game(self.build, self.output, "v1.0.0", "abc")

    def test_output_inside_build_is_rejected(self) -> None:
        for output in (self.build, self.build / "dist"):
            with self.subTest(output=output), self.assertRaises(ValueError):
                package_game(self.build, output, "v1.0.0", "abc")

    def test_refs_cannot_escape_output_directory(self) -> None:
        archive = package_game(self.build, self.output, "../../release/v1", "abc")
        self.assertEqual(self.output, archive.parent)
        self.assertEqual("Lumenfall-release-v1-windows-x64.zip", archive.name)

    def test_empty_version_is_rejected(self) -> None:
        with self.assertRaises(ValueError):
            package_game(self.build, self.output, "...", "abc")


if __name__ == "__main__":
    unittest.main()
