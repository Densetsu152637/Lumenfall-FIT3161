#!/usr/bin/env python3
"""Publish the current main bundle before deleting previous bundle releases.

Call only from the serialized release publication job, with GH_TOKEN configured.
Git tags and releases unrelated to game bundles are preserved.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
from pathlib import Path


def gh(*arguments: str) -> str:
    return subprocess.run(
        ["gh", *arguments], check=True, capture_output=True, text=True,
    ).stdout


def is_bundle_release(release: dict) -> bool:
    """Recognize automatic main releases and earlier tagged game bundles."""
    return re.fullmatch(r"main-\d+", release["tag_name"]) is not None or any(
        re.fullmatch(r"Lumenfall-.+-windows-x64\.zip(?:\.sha256)?", asset["name"])
        for asset in release.get("assets", [])
    )


def is_current_main(repository: str, commit: str) -> bool:
    head = json.loads(gh("api", f"repos/{repository}/git/ref/heads/main"))
    return head["object"]["sha"] == commit


def publish_bundle(directory: Path, repository: str, tag: str, commit: str) -> bool:
    """Return False for a superseded build; propagate API and upload failures."""
    if re.fullmatch(r"main-\d+", tag) is None:
        raise ValueError("Main bundle tags must use main-<workflow-run-id>")
    if not is_current_main(repository, commit):
        print("Skipping superseded build: its commit is no longer the main head.")
        return False

    archive = directory / f"Lumenfall-{tag}-windows-x64.zip"
    assets = [archive, archive.with_suffix(".zip.sha256")]
    for asset in assets:
        if not asset.is_file() or asset.stat().st_size == 0:
            raise ValueError(f"Missing or empty bundle asset: {asset}")

    # Read every page before deleting anything so pagination cannot skip releases.
    pages = json.loads(gh(
        "api", f"repos/{repository}/releases?per_page=100", "--paginate", "--slurp",
    ))
    releases = [release for page in pages for release in page]
    current = next((release for release in releases if release["tag_name"] == tag), None)
    if current is None:
        gh(
            "release", "create", tag, "--repo", repository, "--target", commit,
            "--draft", "--title", "Lumenfall - latest main build",
            "--notes", f"Windows x64 game bundle from main commit {commit}. "
            "Extract the entire ZIP and run Lumenfall.exe.",
        )

    gh("release", "upload", tag, *(str(asset) for asset in assets),
       "--repo", repository, "--clobber")
    # A push may have arrived during the upload. Keep the last good public bundle.
    if not is_current_main(repository, commit):
        print("Main advanced during upload; skipping publication and cleanup.")
        return False

    gh("release", "edit", tag, "--repo", repository,
       "--draft=false", "--prerelease=false", "--latest")
    for release in releases:
        if release["tag_name"] != tag and is_bundle_release(release):
            gh("release", "delete", release["tag_name"], "--repo", repository, "--yes")
            print(f"Removed previous bundle release: {release['tag_name']}")
    print(f"Published {tag}; only the latest game bundle release remains.")
    return True


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--repo", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--commit", required=True)
    arguments = parser.parse_args()
    try:
        publish_bundle(arguments.directory, arguments.repo, arguments.tag, arguments.commit)
    except (OSError, ValueError, subprocess.CalledProcessError) as exception:
        parser.exit(1, f"Bundle publication failed: {exception}\n")


if __name__ == "__main__":
    main()
