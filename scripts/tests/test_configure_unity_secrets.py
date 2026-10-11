from __future__ import annotations

import contextlib
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from configure_unity_secrets import prepare_secrets, upload_secrets  # noqa: E402


class UnitySecretSetupTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name)
        self.env_file = self.root / ".env"
        self.env_file.write_text(
            "UNITY_EMAIL='ci@example.com'\nUNITY_PASSWORD='literal # $value'\n"
            "UNITY_LICENSE_FILE='Unity_lic.ulf'\n", encoding="utf-8"
        )
        self.license_file = self.root / "Unity_lic.ulf"
        self.license_content = b'<?xml version="1.0"?>\r\n<root><License><Serial>test</Serial></License></root>\r\n'
        self.license_file.write_bytes(self.license_content)

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def test_reads_entire_file_and_preserves_literal_credentials(self) -> None:
        secrets = prepare_secrets(self.env_file, None)
        self.assertEqual(self.license_content, secrets["UNITY_LICENSE"])
        self.assertEqual(b"literal # $value", secrets["UNITY_PASSWORD"])
        self.assertEqual(b"ci@example.com", secrets["UNITY_EMAIL"])

    def test_explicit_license_file_overrides_env_path(self) -> None:
        other = self.root / "other.ulf"
        other.write_bytes(b"<License />")
        self.assertEqual(b"<License />", prepare_secrets(self.env_file, other)["UNITY_LICENSE"])

    def test_missing_credentials_and_duplicate_entries_are_rejected(self) -> None:
        for content in ("UNITY_EMAIL=ci@example.com\n", "UNITY_EMAIL=a\nUNITY_EMAIL=b\n"):
            with self.subTest(content=content):
                self.env_file.write_text(content, encoding="utf-8")
                with self.assertRaises(ValueError):
                    prepare_secrets(self.env_file, self.license_file)

    def test_invalid_or_oversized_license_is_rejected(self) -> None:
        for content in (b"", b"not XML", b"<unrelated />", b"x" * (48 * 1024 + 1)):
            with self.subTest(content=content[:20]):
                self.license_file.write_bytes(content)
                with self.assertRaises(ValueError):
                    prepare_secrets(self.env_file, self.license_file)

    @patch("configure_unity_secrets.subprocess.run")
    def test_upload_uses_stdin_and_never_logs_secret_values(self, run) -> None:
        run.return_value = subprocess.CompletedProcess([], 0)
        secrets = prepare_secrets(self.env_file, None)
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            upload_secrets(secrets, "owner/repo", "gh")
        self.assertEqual(3, run.call_count)
        for call, (name, value) in zip(run.call_args_list, secrets.items()):
            self.assertEqual(["gh", "secret", "set", name, "--repo", "owner/repo"], call.args[0])
            self.assertEqual(value, call.kwargs["input"])
            self.assertTrue(call.kwargs["capture_output"])
            self.assertNotIn(value.decode("utf-8"), output.getvalue())

    @patch("configure_unity_secrets.subprocess.run")
    def test_upload_failure_stops_and_redacts_cli_output(self, run) -> None:
        run.return_value = subprocess.CompletedProcess([], 1, stderr=b"sensitive output")
        with self.assertRaises(RuntimeError) as error:
            upload_secrets({"UNITY_LICENSE": b"license", "UNITY_EMAIL": b"email"}, "owner/repo", "gh")
        self.assertNotIn("sensitive output", str(error.exception))
        self.assertEqual(1, run.call_count)


if __name__ == "__main__":
    unittest.main()
