#!/usr/bin/env python3
"""Skip Unity only for a positively identified documentation-only PR."""

from __future__ import annotations

import json
import os
import re
import subprocess
from pathlib import Path


DOCUMENTATION_FILES = {"README.md", "LICENSE", ".github/CI.md", ".github/PULL_REQUEST_TEMPLATE.md", "tests/README.md"}
COMMIT_PATTERN = re.compile(r"[0-9a-fA-F]{40}")


def requires_unity(paths: list[str]) -> bool:
    """Unknown, empty, or noncanonical paths require the expensive check."""
    if not paths:
        return True
    for path in paths:
        if not path or "\\" in path or any(part in {"", ".", ".."} for part in path.split("/")):
            return True
        if path in DOCUMENTATION_FILES:
            continue
        if path.startswith("docs/") and path.endswith(".md"):
            continue
        return True
    return False


def changed_paths(event: object, repository: Path) -> list[str]:
    """Read both sides of moves from exact event commits; reject ambiguous data."""
    pull_request = event["pull_request"]
    base = pull_request["base"]["sha"]
    head = pull_request["head"]["sha"]
    if not all(isinstance(sha, str) and COMMIT_PATTERN.fullmatch(sha) for sha in (base, head)):
        raise ValueError("Invalid event commit")
    if base == head:
        raise ValueError("No distinct PR commits")
    # Comparing endpoints conservatively includes base changes since the PR forked.
    result = subprocess.run(
        ["git", "diff", "--no-ext-diff", "--no-renames", "--name-only", "-z", base, head, "--"],
        cwd=repository, capture_output=True, check=True, timeout=30,
    )
    if not result.stdout.endswith(b"\0"):
        raise ValueError("Missing or incomplete changed-path data")
    paths = result.stdout[:-1].decode("utf-8").split("\0")
    if any(not path or "\\" in path or any(part in {"", ".", ".."} for part in path.split("/")) for path in paths):
        raise ValueError("Noncanonical changed path")
    return paths


def select_checks(event_name: str, event: object, repository: Path) -> tuple[bool, str]:
    """Use event commits, never branch names or PR-provided shell fragments."""
    if event_name != "pull_request":
        return True, "Unity is required for manual, main, and other non-PR runs."
    try:
        paths = changed_paths(event, repository)
    except (KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError):
        return True, "Changed paths could not be established; Unity is required."
    if requires_unity(paths):
        return True, "The PR changes files outside the documentation allowlist."
    return False, "Only allowlisted documentation changed; Unity is not needed."


def main() -> None:
    try:
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    except (KeyError, OSError, ValueError):
        event = None
    required, reason = select_checks(os.environ.get("GITHUB_EVENT_NAME", ""), event, Path.cwd())
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        output.write(f"unity_required={str(required).lower()}\n")
    print(reason)


if __name__ == "__main__":
    main()
