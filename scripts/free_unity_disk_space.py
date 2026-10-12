"""Reclaim only the space Unity needs on disposable GitHub Linux runners."""

from __future__ import annotations

import os
import shutil
import subprocess
import sys


MINIMUM_FREE_BYTES = 30 * 1024**3
UNRELATED_SDKS = (
    "/usr/share/dotnet", "/usr/local/lib/android", "/opt/ghc",
    "/usr/local/.ghcup", "/opt/hostedtoolcache/CodeQL",
)


def free_space() -> None:
    if os.environ.get("RUNNER_ENVIRONMENT") != "github-hosted" or sys.platform != "linux":
        raise ValueError("Disk cleanup is restricted to disposable GitHub-hosted Linux runners.")
    print("Disk space before cleanup:", flush=True)
    subprocess.run(["df", "-h", "/"], check=True)
    for sdk in UNRELATED_SDKS:
        if shutil.disk_usage("/").free >= MINIMUM_FREE_BYTES:
            break
        subprocess.run(["sudo", "rm", "-rf", "--", sdk], check=True, timeout=300)
    print("Disk space after cleanup:", flush=True)
    subprocess.run(["df", "-h", "/"], check=True)


if __name__ == "__main__":
    free_space()
