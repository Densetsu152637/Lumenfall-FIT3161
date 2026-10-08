#!/usr/bin/env python3
"""Package a complete Windows Mono player for GitHub Releases."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]


def package_game(build: Path, output: Path, version: str, commit: str) -> Path:
    build = build.resolve()
    output = output.resolve()
    if output == build or build in output.parents:
        raise ValueError("Bundle output must be outside the player build directory")

    # Catch successful-looking but incomplete builds before publishing them.
    for relative in (
        "Lumenfall.exe",
        "UnityPlayer.dll",
        "Lumenfall_Data/globalgamemanagers",
    ):
        path = build / relative
        if not path.is_file() or path.stat().st_size == 0:
            raise ValueError(f"Missing or empty player file: {relative}")
    for relative in ("Lumenfall_Data/Managed", "MonoBleedingEdge"):
        path = build / relative
        if not path.is_dir() or not any(file.is_file() for file in path.rglob("*")):
            raise ValueError(f"Missing or empty player runtime directory: {relative}")

    safe_version = re.sub(r"[^A-Za-z0-9._-]", "-", version).strip(".-")
    if not safe_version:
        raise ValueError("Version must contain a letter or number")

    unity_version_file = REPOSITORY_ROOT / "Lumenfall/ProjectSettings/ProjectVersion.txt"
    unity_version = re.search(
        r"^m_EditorVersion:\s*(\S+)",
        unity_version_file.read_text(encoding="utf-8"),
        re.MULTILINE,
    ).group(1)
    metadata = {
        "version": version,
        "commit": commit,
        "unityVersion": unity_version,
        "platform": "windows-x64",
    }
    readme = (
        f"Lumenfall - {version} (Windows 64-bit)\n\n"
        "Extract the entire ZIP into a folder, then run Lumenfall.exe.\n"
        "Keep Lumenfall_Data, UnityPlayer.dll, MonoBleedingEdge, and all other\n"
        "included files beside the executable. Unity Editor is not required.\n\n"
        f"Build commit: {commit}\nUnity version: {unity_version}\n"
    )

    output.mkdir(parents=True, exist_ok=True)
    archive = output / f"Lumenfall-{safe_version}-windows-x64.zip"
    with ZipFile(archive, "w", compression=ZIP_DEFLATED) as bundle:
        for path in sorted(build.rglob("*")):
            if path.is_file():
                bundle.write(path, path.relative_to(build).as_posix())
        bundle.writestr("README.txt", readme)
        bundle.writestr("build-info.json", json.dumps(metadata, indent=2) + "\n")
        bundle.write(REPOSITORY_ROOT / "LICENSE", "LICENSE")

    with archive.open("rb") as stream:
        checksum = hashlib.file_digest(stream, "sha256").hexdigest()
    archive.with_suffix(".zip.sha256").write_text(
        f"{checksum}  {archive.name}\n", encoding="utf-8"
    )
    return archive


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("build", type=Path, help="Windows player output directory")
    parser.add_argument("output", type=Path, help="Directory for the ZIP and checksum")
    parser.add_argument("--version", required=True, help="Release tag or manual build ref")
    parser.add_argument("--commit", required=True, help="Source commit SHA")
    arguments = parser.parse_args()
    try:
        archive = package_game(
            arguments.build, arguments.output, arguments.version, arguments.commit
        )
    except (ValueError, OSError) as exception:
        parser.exit(1, f"Cannot package game: {exception}\n")
    print(f"Created {archive} and its SHA-256 checksum")


if __name__ == "__main__":
    main()
