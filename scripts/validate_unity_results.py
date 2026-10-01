#!/usr/bin/env python3
"""Require passing NUnit results from the intended first-party Unity assembly."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def validate_results(directory: Path, assembly: str) -> tuple[int, list[str]]:
    errors: list[str] = []
    passed = 0
    reports = 0
    for path in sorted(directory.rglob("*.xml")):
        try:
            root = ET.parse(path).getroot()
        except (OSError, ET.ParseError) as exception:
            errors.append(f"Cannot read {path.name}: {exception}")
            continue
        if root.tag != "test-run":
            continue  # Coverage and other XML artifacts are not NUnit reports.
        reports += 1
        if root.get("result") != "Passed":
            errors.append(f"{path.name}: test run result is {root.get('result')!r}")
        for case in root.iter("test-case"):
            if case.get("result") not in {"Passed", "Skipped", "Ignored"}:
                errors.append(f"{path.name}: {case.get('fullname', case.get('name'))}: {case.get('result')}")
        for suite in root.iter("test-suite"):
            if suite.get("type") != "Assembly":
                continue
            # Unity emits either the assembly name or the DLL filename.
            name = Path(suite.get("name", "").replace("\\", "/")).name.removesuffix(".dll")
            if name == assembly:
                passed += sum(case.get("result") == "Passed" for case in suite.iter("test-case"))
    if reports == 0:
        errors.append(f"No NUnit test-run XML reports found in {directory}")
    if passed == 0:
        errors.append(f"No passing tests were executed from {assembly}")
    return passed, errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
    parser.add_argument("--assembly", required=True)
    parser.add_argument("--summary", type=Path)
    args = parser.parse_args()
    passed, errors = validate_results(args.directory, args.assembly)
    summary = f"{args.assembly}: {passed} passing tests; {len(errors)} validation errors."
    print(summary)
    for error in errors:
        print(f"ERROR: {error}", file=sys.stderr)
    if args.summary:
        with args.summary.open("a", encoding="utf-8") as output:
            output.write(f"\n### {args.assembly}\n\n{summary}\n")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
