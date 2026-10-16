"""Verify bounded SDK cleanup without touching the host filesystem."""

import os
import sys
import unittest
from collections import namedtuple
from pathlib import Path
from unittest.mock import call, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import free_unity_disk_space as cleanup

DiskUsage = namedtuple("DiskUsage", "total used free")


class UnityDiskCleanupTests(unittest.TestCase):
    def test_other_hosts_are_rejected_before_any_command(self):
        for environment, platform in [("self-hosted", "linux"), ("github-hosted", "win32"), ("", "linux")]:
            with self.subTest(environment=environment, platform=platform), patch.dict(os.environ, RUNNER_ENVIRONMENT=environment), patch.object(cleanup.sys, "platform", platform), patch.object(cleanup.subprocess, "run") as run:
                with self.assertRaises(ValueError):
                    cleanup.free_space()
                run.assert_not_called()

    def test_sufficient_space_does_not_delete_anything(self):
        with patch.dict(os.environ, RUNNER_ENVIRONMENT="github-hosted"), patch.object(cleanup.sys, "platform", "linux"), patch.object(cleanup.shutil, "disk_usage", return_value=DiskUsage(100, 0, cleanup.MINIMUM_FREE_BYTES)), patch.object(cleanup.subprocess, "run") as run:
            cleanup.free_space()
        self.assertEqual(run.call_args_list, [call(["df", "-h", "/"], check=True)] * 2)

    def test_cleanup_stops_after_enough_space_is_available(self):
        with patch.dict(os.environ, RUNNER_ENVIRONMENT="github-hosted"), patch.object(cleanup.sys, "platform", "linux"), patch.object(cleanup.shutil, "disk_usage", side_effect=[DiskUsage(100, 0, 0), DiskUsage(100, 0, cleanup.MINIMUM_FREE_BYTES)]), patch.object(cleanup.subprocess, "run") as run:
            cleanup.free_space()
        self.assertEqual(run.call_args_list[1], call(["sudo", "rm", "-rf", "--", "/usr/share/dotnet"], check=True, timeout=300))
        self.assertEqual(run.call_count, 3)


if __name__ == "__main__":
    unittest.main()
