#!/usr/bin/env python3
"""Upload a Unity license file and local account credentials to GitHub Actions."""

from __future__ import annotations

import argparse
import re
import shutil
import subprocess
import xml.etree.ElementTree as ET
from pathlib import Path


DEFAULT_REPOSITORY = "Densetsu152637/Lumenfall-FIT3161"
DEFAULT_LICENSE = Path("C:/ProgramData/Unity/Unity_lic.ulf")


def read_env(path: Path) -> dict[str, str]:
    """Read literal single-line values without executing or expanding them."""
    values = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        match = re.match(r"^\s*(?:export\s+)?(UNITY_EMAIL|UNITY_PASSWORD|UNITY_LICENSE_FILE)\s*=(.*)$", line)
        if match is None:
            continue
        key, value = match.groups()
        if key in values:
            raise ValueError(f"Duplicate {key} in the environment file")
        value = value.strip()
        if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
            value = value[1:-1]
        values[key] = value
    return values


def prepare_secrets(env_file: Path, license_file: Path | None) -> dict[str, bytes]:
    values = read_env(env_file)
    for key in ("UNITY_EMAIL", "UNITY_PASSWORD"):
        if not values.get(key):
            raise ValueError(f"Fill {key} in the environment file before uploading")
    if license_file is None:
        configured_path = values.get("UNITY_LICENSE_FILE")
        license_file = Path(configured_path) if configured_path else DEFAULT_LICENSE
        if not license_file.is_absolute():
            license_file = env_file.resolve().parent / license_file
    license_content = license_file.read_bytes()
    if len(license_content) > 48 * 1024:
        raise ValueError("License exceeds GitHub's 48 KB secret size limit")
    try:
        license_xml = ET.fromstring(license_content)
    except ET.ParseError:
        raise ValueError("License file must contain valid Unity .ulf XML") from None
    if license_xml.tag != "License" and license_xml.find(".//License") is None:
        raise ValueError("License file contains no Unity License element")
    return {
        "UNITY_LICENSE": license_content,
        "UNITY_EMAIL": values["UNITY_EMAIL"].encode("utf-8"),
        "UNITY_PASSWORD": values["UNITY_PASSWORD"].encode("utf-8"),
    }


def upload_secrets(secrets: dict[str, bytes], repository: str, gh: str) -> None:
    for name, value in secrets.items():
        result = subprocess.run(
            [gh, "secret", "set", name, "--repo", repository],
            input=value,
            capture_output=True,
        )
        if result.returncode:
            # Do not echo CLI output: credentials must never reach setup logs.
            raise RuntimeError(
                f"Could not upload {name}. Check gh auth status and repository secret permissions. "
                "Any earlier secrets may already have been updated; rerunning is safe."
            )
        print(f"Updated {name} in {repository}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, default=Path(".env"))
    parser.add_argument("--license-file", type=Path, help="Unity .ulf file; overrides UNITY_LICENSE_FILE")
    parser.add_argument("--repo", default=DEFAULT_REPOSITORY)
    parser.add_argument("--check", action="store_true", help="Validate local inputs without uploading")
    arguments = parser.parse_args()
    try:
        secrets = prepare_secrets(arguments.env_file, arguments.license_file)
        if arguments.check:
            print("Unity license file and account credential entries are valid; nothing uploaded.")
            return
        gh = shutil.which("gh")
        if gh is None:
            raise ValueError("Install GitHub CLI from https://cli.github.com/ and run gh auth login first")
        upload_secrets(secrets, arguments.repo, gh)
    except (OSError, ValueError, RuntimeError) as exception:
        parser.exit(1, f"Setup failed: {exception}\n")


if __name__ == "__main__":
    main()
