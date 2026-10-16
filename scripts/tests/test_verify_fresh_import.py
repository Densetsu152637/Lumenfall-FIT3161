"""Exercise cold-import evidence gates without Unity or hosted services."""

import json
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

SCRIPTS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SCRIPTS))
import verify_fresh_import as audit


class FreshImportVerificationTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(dir=SCRIPTS)
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.artifacts = self.root / "artifacts"
        self.artifacts.mkdir()
        self.manifest = self.root / "manifest.json"
        self.manifest.write_text(json.dumps({
            "schema_version": 1,
            "platforms": {
                "editmode": {"name": "EditMode", "fixtures": {audit.FIXTURE: 4}},
                "playmode": {"name": "PlayMode", "fixtures": {"Game.PlayTests": 1}},
            },
        }), encoding="utf-8")
        self.revision = "a" * 40
        self.write_xml()
        self.write_log("Asset Pipeline Refresh completed")

    def write_xml(self, cases=None, *, root_result="Passed"):
        cases = cases if cases is not None else [f"Case{i}" for i in range(4)]
        root = ET.Element("test-run", result=root_result, total=str(len(cases)),
                          passed=str(len(cases)), failed="0", skipped="0", inconclusive="0")
        for name in cases:
            ET.SubElement(root, "test-case", classname=audit.FIXTURE,
                          name=name, fullname=f"{audit.FIXTURE}.{name}", result="Passed")
        ET.ElementTree(root).write(self.artifacts / "editmode-results.xml")

    def write_log(self, text):
        (self.artifacts / "editmode.log").write_text(text, encoding="utf-8")

    def verify(self, **kwargs):
        arguments = {"artifacts": self.artifacts, "outcome": "success",
                     "expected_revision": self.revision, "actual_revision": self.revision,
                     "manifest": self.manifest}
        arguments.update(kwargs)
        return audit.verify(**arguments)

    def test_current_manifest_count_and_complete_results_pass(self):
        report = self.verify()
        self.assertEqual(report["status"], "passed")
        self.assertEqual(report["case_count"], 4)

    def test_empty_and_missing_xml_or_editor_log_fail_closed(self):
        for filename in ("editmode-results.xml", "editmode.log"):
            path = self.artifacts / filename
            saved = path.read_bytes()
            for state in ("missing", "empty"):
                with self.subTest(filename=filename, state=state):
                    if state == "missing":
                        path.unlink()
                    else:
                        path.write_bytes(b"")
                    self.assertEqual(self.verify()["status"], "failed")
                    path.write_bytes(saved)

    def test_manifest_count_is_live_and_unexpected_or_zero_cases_fail(self):
        self.manifest.write_text(json.dumps({
            "schema_version": 1,
            "platforms": {
                "editmode": {"name": "EditMode", "fixtures": {audit.FIXTURE: 5}},
                "playmode": {"name": "PlayMode", "fixtures": {"Game.PlayTests": 1}},
            },
        }), encoding="utf-8")
        self.assertEqual(self.verify()["status"], "failed")
        self.manifest.write_text(json.dumps({
            "schema_version": 1,
            "platforms": {
                "editmode": {"name": "EditMode", "fixtures": {audit.FIXTURE: 0}},
                "playmode": {"name": "PlayMode", "fixtures": {"Game.PlayTests": 1}},
            },
        }), encoding="utf-8")
        self.assertEqual(self.verify()["status"], "failed")
        self.write_xml(cases=[])
        self.assertEqual(self.verify()["status"], "failed")

    def test_duplicate_and_foreign_cases_fail(self):
        self.write_xml(cases=["Case0", "Case0", "Case2", "Case3"])
        self.assertIn("Duplicate", " ".join(self.verify()["errors"]))
        self.write_xml()
        tree = ET.parse(self.artifacts / "editmode-results.xml")
        next(tree.getroot().iter("test-case")).set("classname", "Other.UnexpectedFixture")
        tree.write(self.artifacts / "editmode-results.xml")
        self.assertEqual(self.verify()["status"], "failed")

    def test_nonpassed_case_and_root_result_fail(self):
        self.write_xml()
        tree = ET.parse(self.artifacts / "editmode-results.xml")
        next(tree.getroot().iter("test-case")).set("result", "Skipped")
        tree.write(self.artifacts / "editmode-results.xml")
        self.assertEqual(self.verify()["status"], "failed")
        self.write_xml(root_result="Failed")
        self.assertEqual(self.verify()["status"], "failed")

    def test_known_native_import_diagnostics_fail_without_broad_error_grep(self):
        self.write_log("Shader compiler backend: error-record count 2806 (unrelated)\n"
                       "An infinite import loop has been detected when processing post process all assets callbacks:\n"
                       "Assets/_Project/Art/Environment/Decorations/Textures/diffuse_cube.dds")
        report = self.verify()
        self.assertEqual(report["status"], "failed")
        self.assertTrue(any("known native import" in error for error in report["errors"]))
        self.write_log("backend error records are unrelated")
        self.assertEqual(self.verify()["status"], "passed")

    def test_captured_terrain_and_elara_signatures_fail(self):
        for signature in [
            "Failed to load '/github/workspace/Lumenfall/Assets/Third Party/New Terrain.asset'. File may be corrupted",
            "Failed to load 'Assets/Third Party/TerrainData_ef2baf1a-e0c2-47af-8b8d-c8ecc7214609.asset'. File may be corrupted",
            "Prefab 'Elara.prefab' has unexpected file IDs and is likely to be corrupt",
        ]:
            with self.subTest(signature=signature):
                self.write_log(signature)
                self.assertEqual(self.verify()["status"], "failed")

    def test_action_failure_and_revision_mismatch_fail_even_with_valid_xml(self):
        self.assertEqual(self.verify(outcome="failure")["status"], "failed")
        self.assertEqual(self.verify(actual_revision="b" * 40)["status"], "failed")
        self.assertEqual(self.verify(expected_revision="not-a-sha")["status"], "failed")


if __name__ == "__main__":
    unittest.main()
