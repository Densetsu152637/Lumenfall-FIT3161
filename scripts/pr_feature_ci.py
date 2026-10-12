"""Bridge the pinned acceptance catalog to GameCI and GitHub job summaries."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import run_pr_feature_tests as runner


def entry_for(number: int) -> dict:
    return next(entry for entry in runner.read_manifest() if entry["number"] == number)


def describe(entry: dict) -> dict[str, str]:
    return {
        "commit": entry["commit"],
        "suite": entry["suite"],
        "editmode": str("EditMode" in entry["expected_cases"]).lower(),
        "playmode": str("PlayMode" in entry["expected_cases"]).lower(),
        "python": str(bool(entry.get("expected_python_cases"))).lower(),
        "unity": str(bool(entry["expected_cases"])).lower(),
    }


def emit_outputs(values: dict[str, str]) -> None:
    for key, value in values.items():
        if "\n" in value or "\r" in value:
            raise ValueError("GitHub output values must be single-line")
        print(f"{key}={value}")
    if destination := os.environ.get("GITHUB_OUTPUT"):
        with open(destination, "a", encoding="utf-8") as stream:
            for key, value in values.items():
                stream.write(f"{key}={value}\n")


def prepare(entry: dict, output: Path) -> dict[str, str]:
    if runner.main(["--pr", str(entry["number"]), "--mode", "prepare", "--output", str(output)]):
        raise ValueError("Pinned acceptance project preparation failed")
    project = output / entry["suite"] / "snapshot" / "Lumenfall"
    values = describe(entry)
    values["project"] = project.relative_to(runner.ROOT).as_posix()
    values["suite-hash"] = runner.suite_hash(runner.CATALOG / entry["suite"])
    if entry["expected_cases"]:
        text = (project / "ProjectSettings" / "ProjectVersion.txt").read_text()
        versions = re.findall(r"^m_EditorVersion:\s*(\S+)\s*$", text, re.MULTILINE)
        if len(versions) != 1 or not re.fullmatch(r"\d+\.\d+\.\d+[abfp]\d+", versions[0]):
            raise ValueError("Snapshot must specify exactly one valid Unity Editor version")
        values["unity-version"] = versions[0]
    return values


def find_results(directory: Path) -> Path:
    """Select a single NUnit run, ignoring unrelated coverage XML documents."""
    candidates = []
    for path in sorted(directory.rglob("*.xml")):
        root = ET.parse(path).getroot()
        if root.tag == "test-run":
            candidates.append(path)
    if len(candidates) != 1:
        raise ValueError(f"Expected exactly one NUnit test-run XML under {directory}; found {len(candidates)}")
    return candidates[0]


def verify(entry: dict, output: Path, artifacts: Path, outcomes: dict[str, str]) -> dict:
    location = output / entry["suite"]
    report = {"pr": entry["number"], "commit": entry["commit"],
              "suite_sha256": runner.suite_hash(runner.CATALOG / entry["suite"]),
              "status": "passed", "platforms": [], "errors": []}
    try:
        prepared = json.loads((location / "report-prepare.json").read_text())
        marker = json.loads((location / "snapshot.json").read_text())
        if (prepared["status"] != "prepared" or prepared["commit"] != entry["commit"]
                or marker["commit"] != entry["commit"]
                or prepared["suite_sha256"] != report["suite_sha256"]):
            raise ValueError("Prepared source revision or suite hash differs from the catalog")
    except (OSError, ValueError, KeyError) as error:
        report["errors"].append(f"Preparation: {error}")
    for platform, expected in entry["expected_cases"].items():
        result = {"platform": platform, "outcome": outcomes.get(platform), "status": "failed"}
        try:
            if result["outcome"] != "success":
                raise ValueError(f"GameCI step outcome was {result['outcome']!r}")
            xml = find_results(artifacts / platform.lower())
            result.update(runner.validate_results(xml, expected), xml=str(xml), status="passed")
        except (OSError, ValueError, ET.ParseError) as error:
            result["error"] = str(error)
            report["errors"].append(f"{platform}: {error}")
        report["platforms"].append(result)
    if entry.get("expected_python_cases"):
        result = {"platform": "Python", "outcome": outcomes.get("Python"), "status": "failed"}
        try:
            python = json.loads((location / "report-python.json").read_text())
            log = (location / "python.log").read_text()
            counts = [json.loads(line.split("=", 1)[1]) for line in log.splitlines()
                      if line.startswith("PR_FEATURE_PYTHON_RESULT=")]
            expected = dict(executed=entry["expected_python_cases"], failed=0, errors=0, skipped=0)
            if (result["outcome"] != "success" or python["status"] != "passed"
                    or python["commit"] != entry["commit"]
                    or python["suite_sha256"] != report["suite_sha256"]
                    or python["python"]["exit_code"] != 0 or counts != [expected]):
                raise ValueError("Python step, revision, suite hash or executed counts did not pass completely")
            result.update(status="passed", passed=expected["executed"], failed=0, skipped=0)
        except (OSError, ValueError, KeyError) as error:
            result["error"] = str(error)
            report["errors"].append(f"Python: {error}")
        report["platforms"].append(result)
    if not report["platforms"]:
        report["errors"].append("No expected test platforms")
    if report["errors"]:
        report["status"] = "failed"
    return report


def write_report(report: dict, location: Path) -> None:
    location.mkdir(parents=True, exist_ok=True)
    (location / "ci-report.json").write_text(json.dumps(report, indent=2) + "\n")
    lines = [f"## PR {report['pr']} feature acceptance: {report['status']}", "",
             f"Target: `{report['commit']}`", f"Suite SHA-256: `{report['suite_sha256']}`", "",
             "| Platform | Passed | Status |", "| --- | ---: | --- |"]
    lines.extend(f"| {item['platform']} | {item.get('passed', '—')} | {item['status']} |"
                 for item in report["platforms"])
    for error in report["errors"]:
        escaped = error.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error::PR {report['pr']} {escaped}")
        lines.extend(["", "```text", error.replace("```", "'''"), "```"])
    summary = "\n".join(lines) + "\n"
    print(summary)
    if destination := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(destination, "a", encoding="utf-8") as stream:
            stream.write(summary)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=["describe", "prepare", "verify"])
    parser.add_argument("--pr", required=True, type=int)
    parser.add_argument("--output", type=Path, default=runner.ROOT / "Temp" / "pr-tests")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--editmode-outcome", default="not_run")
    parser.add_argument("--playmode-outcome", default="not_run")
    parser.add_argument("--python-outcome", default="not_run")
    args = parser.parse_args(argv)
    try:
        entry = entry_for(args.pr)
        output = args.output.resolve()
        if args.operation == "describe":
            emit_outputs(describe(entry))
        elif args.operation == "prepare":
            emit_outputs(prepare(entry, output))
        else:
            artifacts = (args.artifacts or runner.ROOT / "artifacts" / f"pr-{args.pr}").resolve()
            report = verify(entry, output, artifacts, {"EditMode": args.editmode_outcome,
                            "PlayMode": args.playmode_outcome, "Python": args.python_outcome})
            write_report(report, output / entry["suite"])
            return int(report["status"] != "passed")
    except (OSError, ValueError, StopIteration) as error:
        print(f"PR {args.pr}: {error or 'PR is not in the manifest'}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
