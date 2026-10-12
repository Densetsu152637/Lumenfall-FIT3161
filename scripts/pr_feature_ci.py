"""Bridge the pinned acceptance catalog to GameCI and GitHub job summaries."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

import run_pr_feature_tests as runner


CACHE_LIBRARY = Path("Temp/pr-feature-unity-cache/Library")


def config_hash(project: Path) -> str:
    """Fingerprint names and bytes of extracted Unity configuration, excluding Assets."""
    digest = hashlib.sha256()
    for name in ("Packages", "ProjectSettings"):
        directory = project / name
        if directory.is_symlink() or directory.is_junction() or not directory.is_dir():
            raise ValueError(f"Missing or linked Unity configuration: {directory}")
        for source in sorted(directory.rglob("*")):
            if (source.is_symlink() or source.is_junction()
                    or not source.resolve().is_relative_to(directory.resolve())):
                raise ValueError(f"Linked Unity configuration: {source}")
            if source.is_file():
                logical = source.relative_to(project).as_posix().encode("utf-8")
                content = source.read_bytes()
                digest.update(len(logical).to_bytes(8, "big"))
                digest.update(logical)
                digest.update(len(content).to_bytes(8, "big"))
                digest.update(content)
    return digest.hexdigest()


def owned_cache_path(path: Path) -> Path:
    """Require a literal, link-free path inside this checkout's ignored Temp tree."""
    root = runner.ROOT.resolve()
    path = path.absolute()
    if ".." in path.parts or not path.is_relative_to(root / "Temp"):
        raise ValueError(f"Cache path escapes owned Temp directory: {path}")
    current = root
    for part in path.relative_to(root).parts:
        current /= part
        if current.is_symlink() or current.is_junction():
            raise ValueError(f"Cache path contains a link: {current}")
    if not path.resolve().is_relative_to(root / "Temp"):
        raise ValueError(f"Cache path escapes owned Temp directory: {path}")
    return path


def cache_paths(entry: dict, output: Path) -> tuple[Path, Path]:
    if not entry["expected_cases"]:
        raise ValueError("Unity cache operations require a Unity suite")
    location = owned_cache_path(output / entry["suite"])
    project = owned_cache_path(location / "snapshot" / "Lumenfall")
    prepared = json.loads(owned_cache_path(location / "report-prepare.json").read_text())
    marker = json.loads(owned_cache_path(location / "snapshot.json").read_text())
    if (not project.is_dir() or prepared.get("status") != "prepared"
            or prepared.get("commit") != entry["commit"]
            or prepared.get("suite_sha256") != runner.suite_hash(runner.CATALOG / entry["suite"])
            or marker.get("commit") != entry["commit"] or marker.get("pr") != entry["number"]):
        raise ValueError("Unity cache requires an owned prepared target and current suite")
    return owned_cache_path(project / "Library"), owned_cache_path(runner.ROOT / CACHE_LIBRARY)


def move_library(source: Path, destination: Path) -> None:
    """Rename a guarded Library directory without overwriting, merging or copying."""
    if destination.exists():
        raise ValueError(f"Cache destination is occupied: {destination}")
    if not source.is_dir():
        raise ValueError(f"Unity Library is missing or not a directory: {source}")
    destination.parent.mkdir(parents=True, exist_ok=True)
    source.rename(destination)


def cache_restore(entry: dict, output: Path) -> bool:
    library, seed = cache_paths(entry, output)
    if library.exists():
        raise ValueError(f"Cache destination is occupied: {library}")
    if not seed.exists():
        return False  # A cold cache is normal; Unity imports from the pinned source.
    move_library(seed, library)
    clear_compilation_cache(library)
    return True


def clear_compilation_cache(library: Path) -> None:
    """Rebuild project-specific compiler state while retaining imported asset data."""
    def fail_walk(error: OSError) -> None:
        raise error

    directories = []
    for name in ("Bee", "ScriptAssemblies"):
        directory = owned_cache_path(library / name)
        if not directory.exists():
            continue
        if not directory.is_dir():
            raise ValueError(f"Unity compilation cache is not a directory: {directory}")
        # Validate only these two generated trees, never the rest of Library.
        # Check child directories before os.walk descends into them (including junctions).
        for current, children, files in os.walk(directory, followlinks=False, onerror=fail_walk):
            for child in children + files:
                owned_cache_path(Path(current) / child)
        directories.append(directory)
    # Validate both trees before deleting either; never follow cached links outside them.
    for directory in directories:
        shutil.rmtree(directory)


def cache_save(entry: dict, output: Path) -> None:
    library, seed = cache_paths(entry, output)
    report = json.loads(owned_cache_path(output / entry["suite"] / "ci-report.json").read_text())
    if (report.get("status") != "passed" or report.get("errors") != []
            or report.get("pr") != entry["number"] or report.get("commit") != entry["commit"]
            or report.get("suite_sha256") != runner.suite_hash(runner.CATALOG / entry["suite"])):
        raise ValueError("Unity cache save requires complete successful verification")
    move_library(library, seed)


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
        values["config-hash"] = config_hash(project)
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
    parser.add_argument("operation", choices=["describe", "prepare", "verify", "cache-restore", "cache-save"])
    parser.add_argument("--pr", required=True, type=int)
    parser.add_argument("--output", type=Path, default=runner.ROOT / "Temp" / "pr-tests")
    parser.add_argument("--artifacts", type=Path)
    parser.add_argument("--editmode-outcome", default="not_run")
    parser.add_argument("--playmode-outcome", default="not_run")
    parser.add_argument("--python-outcome", default="not_run")
    args = parser.parse_args(argv)
    try:
        entry = entry_for(args.pr)
        output = args.output.absolute() if args.operation.startswith("cache-") else args.output.resolve()
        if args.operation == "describe":
            emit_outputs(describe(entry))
        elif args.operation == "prepare":
            emit_outputs(prepare(entry, output))
        elif args.operation == "cache-restore":
            print("Unity Library seed restored" if cache_restore(entry, output) else "Cold Unity Library cache")
        elif args.operation == "cache-save":
            cache_save(entry, output)
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
