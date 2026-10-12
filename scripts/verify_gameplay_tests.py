"""Require complete current-checkout gameplay results from both Unity test modes."""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "tests/gameplay_suites.json"


def read_manifest(path: Path = MANIFEST) -> dict:
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(manifest, dict) or manifest.get("schema_version") != 1:
        raise ValueError("Unsupported gameplay manifest version")
    platforms = manifest.get("platforms", {})
    if not isinstance(platforms, dict) or set(platforms) != {"editmode", "playmode"}:
        raise ValueError("Gameplay manifest must include exactly EditMode and PlayMode")
    for suite in platforms.values():
        if not isinstance(suite, dict):
            raise ValueError("Gameplay suites must be objects")
        fixtures = suite.get("fixtures")
        if (not isinstance(suite.get("name"), str) or not suite["name"].strip()
                or not isinstance(fixtures, dict) or not fixtures
                or any(not isinstance(name, str) or not name.strip()
                       or type(count) is not int or count <= 0 for name, count in fixtures.items())):
            raise ValueError("Gameplay suites require a name and positive fixture case counts")
    return platforms


def validate_results(path: Path, expected: dict[str, int]) -> int:
    root = ET.parse(path).getroot()
    if root.tag != "test-run" or root.get("result", "").lower() != "passed":
        raise ValueError("Unity test-run did not pass")
    for attribute in ("failed", "skipped", "inconclusive"):
        if attribute in root.attrib and root.get(attribute) != "0":
            raise ValueError(f"Unity test-run reports {attribute} cases")
    cases = list(root.iter("test-case"))
    if not cases:
        raise ValueError("Unity results contain no test cases")
    for attribute in ("total", "passed", "testcasecount"):
        if attribute in root.attrib and int(root.attrib[attribute]) != len(cases):
            raise ValueError(f"Unity test-run {attribute} differs from actual cases")
    if any(suite.get("result", "").lower() != "passed" for suite in root.iter("test-suite")):
        raise ValueError("Unity results contain a failed or skipped suite")
    discovered = {}
    seen = set()
    for case in cases:
        fixture, name = case.get("classname", ""), case.get("fullname", "")
        if not fixture or not name or not case.get("name"):
            raise ValueError("Unity test case is missing its fixture or identity")
        if name in seen:
            raise ValueError(f"Duplicate Unity test case: {name}")
        seen.add(name)
        if case.get("result", "").lower() != "passed":
            raise ValueError(f"Unity test case failed or was skipped: {name}")
        discovered[fixture] = discovered.get(fixture, 0) + 1
    if discovered != expected:
        raise ValueError(f"Unity discovery differs: expected={expected}, actual={discovered}")
    return len(cases)


def verify(artifacts: Path, platforms: dict, outcome: str, revision: str) -> dict:
    report = {"revision": revision, "status": "passed", "platforms": [], "errors": []}
    if not re.fullmatch(r"[a-fA-F0-9]{40}", revision):
        report["errors"].append("Current checkout revision must be a full Git SHA")
    if outcome != "success":
        report["errors"].append(f"GameCI step outcome was {outcome!r}")
    for platform, suite in platforms.items():
        # testMode=all also generates combined output; it is never platform evidence.
        path = artifacts / f"{platform}-results.xml"
        result = {"platform": platform, "name": suite["name"], "status": "failed", "xml": str(path)}
        try:
            result["passed"] = validate_results(path, suite["fixtures"])
            result["status"] = "passed"
        except (OSError, ValueError, ET.ParseError) as error:
            result["error"] = str(error)
            report["errors"].append(f"{suite['name']}: {error}")
        report["platforms"].append(result)
    if report["errors"]:
        report["status"] = "failed"
    return report


def write_report(report: dict, artifacts: Path) -> None:
    artifacts.mkdir(parents=True, exist_ok=True)
    (artifacts / "ci-report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    lines = [f"## Gameplay tests: {report['status']}", "", f"Current checkout: `{report['revision']}`", "",
             "| Suite | Passed | Status |", "| --- | ---: | --- |"]
    lines.extend(f"| {suite['name']} | {suite.get('passed', 0)} | {suite['status']} |"
                 for suite in report["platforms"])
    for error in report["errors"]:
        escaped = error.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error::{escaped}")
        lines.extend(["", "```text", error.replace("```", "'''"), "```"])
    summary = "\n".join(lines) + "\n"
    print(summary)
    if destination := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(destination, "a", encoding="utf-8") as stream:
            stream.write(summary)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, default=ROOT / "artifacts/gameplay")
    parser.add_argument("--outcome", required=True)
    parser.add_argument("--revision", required=True)
    args = parser.parse_args(argv)
    try:
        report = verify(args.artifacts, read_manifest(), args.outcome, args.revision)
        actual = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT,
                                check=True, capture_output=True, text=True, timeout=30).stdout.strip()
        if actual != args.revision:
            report["status"] = "failed"
            report["errors"].append(f"Checkout revision differs from workflow revision: {actual}")
        write_report(report, args.artifacts)
        return int(report["status"] != "passed")
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"Gameplay verification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
