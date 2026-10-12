"""Select touched acceptance suites without weakening shared-code validation."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
from pathlib import Path

from select_pr_checks import changed_paths, requires_unity


GROUPS = {
    "audio": [(9, "Event audio"), (22, "Area music")],
    "persistence-recovery": [(7, "Save recovery"), (19, "Optional recovery")],
    "presentation-ui": [(11, "Accessibility and localization"), (13, "World presentation"),
                        (15, "Production UI"), (18, "Camera"), (23, "Graphics defaults")],
    "quests-spells": [(1, "Hint feedback and reset"), (3, "First Light"), (4, "Weave network"),
                      (5, "Vis alignment"), (8, "Rotara rotation"), (14, "Lost Echoes"), (20, "Glaciate lanes")],
    "validation-diagnostics": [(2, "Content validators"), (17, "Diagnostics")],
    "world-story": [(6, "Chapter 1 story"), (10, "Optional dormitory power"),
                    (12, "Reserved regions"), (16, "Ambient NPC travel")],
}
SUITE_PATH = re.compile(r"tests/pr_features/pr_(\d{2})/.+")


def select_numbers(group: str, paths: list[str] | None) -> set[int]:
    """None/unknown means all; known other suites and documentation mean none."""
    applicable = {number for number, _ in GROUPS[group]}
    if not paths:
        return applicable
    known = {number for suites in GROUPS.values() for number, _ in suites}
    selected = set()
    for path in paths:
        if not requires_unity([path]):
            continue
        if match := SUITE_PATH.fullmatch(path):
            number = int(match[1])
            if number not in known:
                return applicable
            selected.update({number} & applicable)
        elif path in {f".github/workflows/feature-{name}.yaml" for name in GROUPS}:
            if path == f".github/workflows/feature-{group}.yaml":
                return applicable
        else:
            return applicable
    return selected


def select_matrix(group: str, event_name: str, event: object, repository: Path) -> dict:
    paths = None
    if event_name == "pull_request":
        try:
            paths = changed_paths(event, repository)
        except (KeyError, TypeError, ValueError, OSError, subprocess.SubprocessError):
            pass  # Fail closed to all suites in this group.
    selected = select_numbers(group, paths)
    return {"include": [{"pr": number, "feature": feature} for number, feature in GROUPS[group] if number in selected]}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--group", required=True, choices=GROUPS)
    args = parser.parse_args()
    try:
        event = json.loads(Path(os.environ["GITHUB_EVENT_PATH"]).read_text(encoding="utf-8"))
    except (KeyError, OSError, ValueError):
        event = None
    matrix = select_matrix(args.group, os.environ.get("GITHUB_EVENT_NAME", ""), event, Path.cwd())
    values = {"matrix": json.dumps(matrix, separators=(",", ":")), "has_suites": str(bool(matrix["include"])).lower()}
    with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
        for key, value in values.items():
            output.write(f"{key}={value}\n")
    print(f"Selected {len(matrix['include'])} suites in {args.group}.")


if __name__ == "__main__":
    main()
