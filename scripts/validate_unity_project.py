#!/usr/bin/env python3
"""Fast, license-free checks for a Unity repository."""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from collections import defaultdict
from pathlib import Path


GUID_PATTERN = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.MULTILINE)
VERSION_PATTERN = re.compile(r"^m_EditorVersion:\s*(\S+)\s*$", re.MULTILINE)
BUILD_SCENE_PATTERN = re.compile(
    r"- enabled:\s*1\s*\r?\n\s*path:\s*(\S.*?)\s*\r?$", re.MULTILINE
)
TEXT_SUFFIXES = {
    ".asmdef",
    ".asmref",
    ".cginc",
    ".compute",
    ".controller",
    ".cs",
    ".gitattributes",
    ".gitignore",
    ".hlsl",
    ".inputactions",
    ".json",
    ".md",
    ".meta",
    ".prefab",
    ".shader",
    ".txt",
    ".unity",
    ".uss",
    ".uxml",
    ".yaml",
    ".yml",
}
JSON_SUFFIXES = {".asmdef", ".asmref", ".inputactions", ".json"}
GENERATED_DIRECTORY_NAMES = {
    ".idea",
    ".vs",
    "build",
    "builds",
    "library",
    "logs",
    "memorycaptures",
    "obj",
    "temp",
    "usersettings",
}


def _iter_source_paths(project: Path):
    """Yield source-controlled project paths while ignoring Unity's local output."""
    for child in sorted(project.iterdir()):
        if child.name.casefold() in GENERATED_DIRECTORY_NAMES:
            continue
        yield child
        if child.is_dir():
            yield from sorted(child.rglob("*"))


def _relative(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def _read_text(path: Path, errors: list[str], root: Path) -> str | None:
    try:
        data = path.read_bytes()
        # Some vendor readmes ship as UTF-16; its byte-order mark makes the encoding unambiguous.
        if data.startswith((b"\xff\xfe", b"\xfe\xff")):
            return data.decode("utf-16")
        return data.decode("utf-8-sig")
    except (OSError, UnicodeDecodeError) as exception:
        errors.append(f"Cannot read {_relative(path, root)} as UTF-8: {exception}")
        return None


def _is_merge_conflict_marker(line: str) -> bool:
    # Git writes the separator as exactly seven '=' characters, so longer decorative rules are not markers.
    return line.startswith(("<<<<<<< ", ">>>>>>> ")) or line.rstrip() == "======="


def _validate_required_structure(project: Path, errors: list[str]) -> None:
    required = (
        project / "Assets",
        project / "Packages" / "manifest.json",
        project / "Packages" / "packages-lock.json",
        project / "ProjectSettings" / "ProjectVersion.txt",
    )
    for path in required:
        if not path.exists():
            errors.append(f"Missing required Unity path: {_relative(path, project)}")


def _validate_version(project: Path, errors: list[str]) -> None:
    version_file = project / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.is_file():
        return
    text = _read_text(version_file, errors, project)
    if text is not None and VERSION_PATTERN.search(text) is None:
        errors.append("ProjectSettings/ProjectVersion.txt has no m_EditorVersion value")


def _validate_json(project: Path, errors: list[str]) -> None:
    roots = (project / "Assets", project / "Packages")
    for root in roots:
        if not root.is_dir():
            continue
        for path in sorted(item for item in root.rglob("*") if item.suffix.lower() in JSON_SUFFIXES):
            text = _read_text(path, errors, project)
            if text is None:
                continue
            try:
                json.loads(text)
            except json.JSONDecodeError as exception:
                errors.append(
                    f"Invalid JSON in {_relative(path, project)} at "
                    f"line {exception.lineno}, column {exception.colno}: {exception.msg}"
                )


def _validate_asset_metadata(project: Path, errors: list[str]) -> None:
    assets = project / "Assets"
    if not assets.is_dir():
        return

    guid_owners: dict[str, list[Path]] = defaultdict(list)
    for path in sorted(assets.rglob("*")):
        relative = _relative(path, project)
        if path.name.endswith(".meta"):
            target = path.with_name(path.name[:-5])
            if not target.exists():
                errors.append(f"Orphaned Unity metadata: {relative}")

            text = _read_text(path, errors, project)
            if text is None:
                continue
            match = GUID_PATTERN.search(text)
            if match is None:
                errors.append(f"Unity metadata has no valid 32-character GUID: {relative}")
            else:
                guid_owners[match.group(1).lower()].append(path)
            continue

        metadata = path.with_name(f"{path.name}.meta")
        if not metadata.is_file():
            kind = "directory" if path.is_dir() else "asset"
            errors.append(f"Unity {kind} is missing metadata: {relative}.meta")

    for guid, paths in sorted(guid_owners.items()):
        if len(paths) > 1:
            owners = ", ".join(_relative(path, project) for path in paths)
            errors.append(f"Duplicate Unity GUID {guid}: {owners}")


def _validate_build_scenes(project: Path, errors: list[str]) -> None:
    settings = project / "ProjectSettings" / "EditorBuildSettings.asset"
    if not settings.is_file():
        errors.append("Missing ProjectSettings/EditorBuildSettings.asset")
        return
    text = _read_text(settings, errors, project)
    if text is None:
        return
    for scene_path in BUILD_SCENE_PATTERN.findall(text):
        if not (project / scene_path).is_file():
            errors.append(f"Enabled build scene does not exist: {scene_path}")


def _validate_text_files(project: Path, errors: list[str]) -> None:
    for path in (item for item in _iter_source_paths(project) if item.is_file()):
        if path.suffix.lower() not in TEXT_SUFFIXES and path.name not in {"LICENSE"}:
            continue
        text = _read_text(path, errors, project)
        if text is None:
            continue
        if any(_is_merge_conflict_marker(line) for line in text.splitlines()):
            errors.append(f"Unresolved merge-conflict marker in {_relative(path, project)}")


def _validate_case_collisions(project: Path, errors: list[str]) -> None:
    seen: dict[str, Path] = {}
    for path in _iter_source_paths(project):
        relative = Path(_relative(path, project))
        key = relative.as_posix().casefold()
        previous = seen.get(key)
        if previous is not None and previous != relative:
            errors.append(f"Case-colliding paths: {previous.as_posix()} and {relative.as_posix()}")
        else:
            seen[key] = relative


def _validate_tracked_generated_files(repository: Path, project: Path, errors: list[str]) -> None:
    try:
        result = subprocess.run(
            ["git", "-C", str(repository), "ls-files", "-z"],
            check=True,
            capture_output=True,
        )
    except (FileNotFoundError, subprocess.CalledProcessError):
        return

    try:
        project_parts = tuple(part.casefold() for part in project.relative_to(repository).parts)
    except ValueError:
        return

    for raw_path in result.stdout.decode("utf-8", errors="replace").split("\0"):
        if not raw_path:
            continue
        parts = Path(raw_path).parts
        folded_parts = tuple(part.casefold() for part in parts)
        generated = None
        if folded_parts and folded_parts[0] in {".idea", ".vs"}:
            generated = parts[0]
        elif (
            folded_parts[: len(project_parts)] == project_parts
            and len(folded_parts) > len(project_parts)
            and folded_parts[len(project_parts)] in GENERATED_DIRECTORY_NAMES
        ):
            generated = parts[len(project_parts)]
        if generated is not None:
            errors.append(f"Generated directory is tracked by Git: {raw_path} ({generated})")


def validate_project(project: Path, repository: Path | None = None) -> list[str]:
    """Return all validation errors for *project*."""
    project = project.resolve()
    errors: list[str] = []
    if not project.is_dir():
        return [f"Unity project directory does not exist: {project}"]

    _validate_required_structure(project, errors)
    _validate_version(project, errors)
    _validate_json(project, errors)
    _validate_asset_metadata(project, errors)
    _validate_build_scenes(project, errors)
    _validate_text_files(project, errors)
    _validate_case_collisions(project, errors)
    if repository is not None:
        _validate_tracked_generated_files(repository.resolve(), project, errors)
    return errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("project", type=Path, help="Path to the Unity project")
    arguments = parser.parse_args(argv)

    repository = Path(__file__).resolve().parent.parent
    errors = validate_project(arguments.project, repository)
    if errors:
        print(f"Unity repository validation failed with {len(errors)} error(s):", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print(f"Unity repository validation passed: {arguments.project.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
