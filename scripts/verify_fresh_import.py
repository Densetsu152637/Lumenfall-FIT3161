"""Strictly verify one cold Unity import fixture and its native import diagnostics."""

from __future__ import annotations

import argparse
import importlib.util
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MANIFEST = ROOT / "tests/gameplay_suites.json"
FIXTURE = "Lumenfall.Tests.GameEvents.SerializedAssetImportTests"
HELPER = ROOT / "scripts/verify_gameplay_tests.py"
IMPORT_ERROR_PATTERNS = (
    re.compile(r"An infinite import loop has been detected", re.IGNORECASE),
    re.compile(r"Failed to load .*New Terrain\.asset", re.IGNORECASE),
    re.compile(r"Failed to load .*TerrainData_ef2baf1a-e0c2-47af-8b8d-c8ecc7214609\.asset", re.IGNORECASE),
    re.compile(r"Prefab 'Elara\.prefab' has unexpected file IDs", re.IGNORECASE),
)


def load_validation_helpers():
    spec = importlib.util.spec_from_file_location("gameplay_verifier", HELPER)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Cannot load shared NUnit verifier: {HELPER}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.read_manifest, module.validate_results


def expected_fixture(path: Path = MANIFEST) -> dict[str, int]:
    read_manifest, _ = load_validation_helpers()
    platforms = read_manifest(path)
    fixtures = platforms["editmode"]["fixtures"]
    if FIXTURE not in fixtures or type(fixtures[FIXTURE]) is not int or fixtures[FIXTURE] <= 0:
        raise ValueError(f"Manifest must include a positive case count for {FIXTURE}")
    return {FIXTURE: fixtures[FIXTURE]}


def find_import_errors(log: str) -> list[str]:
    lines = log.splitlines()
    return [line.strip() for line in lines if any(pattern.search(line) for pattern in IMPORT_ERROR_PATTERNS)]


def verify(artifacts: Path, outcome: str, expected_revision: str, actual_revision: str,
           manifest: Path = MANIFEST) -> dict:
    report = {"status": "passed", "expected_revision": expected_revision,
              "actual_revision": actual_revision, "case_count": None, "errors": []}
    if not re.fullmatch(r"[a-fA-F0-9]{40}", expected_revision or ""):
        report["errors"].append("Expected workflow revision must be a full Git SHA")
    if actual_revision != expected_revision:
        report["errors"].append(f"Checkout revision differs from workflow revision: {actual_revision}")
    if outcome != "success":
        report["errors"].append(f"GameCI step outcome was {outcome!r}")

    xml_path = artifacts / "editmode-results.xml"
    log_path = artifacts / "editmode.log"
    try:
        expected = expected_fixture(manifest)
        if not xml_path.is_file() or xml_path.stat().st_size == 0:
            raise ValueError("EditMode NUnit XML is missing or empty")
        _, validate_results = load_validation_helpers()
        report["case_count"] = validate_results(xml_path, expected)
    except (OSError, ValueError, ET.ParseError, RuntimeError) as error:
        report["errors"].append(f"EditMode results: {error}")

    try:
        if not log_path.is_file() or log_path.stat().st_size == 0:
            raise ValueError("Unity Editor log is missing or empty")
        log = log_path.read_text(encoding="utf-8", errors="replace")
        failures = find_import_errors(log)
        report["import_diagnostics"] = failures
        if failures:
            report["errors"].append(f"Unity Editor log contains {len(failures)} known native import failure line(s)")
    except (OSError, ValueError) as error:
        report["errors"].append(f"Unity Editor log: {error}")

    if report["errors"]:
        report["status"] = "failed"
    return report


def write_report(report: dict, artifacts: Path) -> None:
    artifacts.mkdir(parents=True, exist_ok=True)
    path = artifacts / "fresh-import-report.json"
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Fresh asset import audit: {report['status']} (cases={report['case_count']})")
    for error in report["errors"]:
        escaped = error.replace("%", "%25").replace("\r", "%0D").replace("\n", "%0A")
        print(f"::error::{escaped}")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artifacts", type=Path, required=True)
    parser.add_argument("--outcome", required=True)
    parser.add_argument("--expected-revision", required=True)
    parser.add_argument("--manifest", type=Path, default=MANIFEST)
    args = parser.parse_args(argv)
    try:
        actual = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT,
                                check=True, capture_output=True, text=True, timeout=30).stdout.strip()
        report = verify(args.artifacts, args.outcome, args.expected_revision, actual, args.manifest)
        write_report(report, args.artifacts)
        return int(report["status"] != "passed")
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"Fresh import verification failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
