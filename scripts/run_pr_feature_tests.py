"""Run branch-specific acceptance suites without changing an existing checkout.

The manifest pins PR revisions. Source is extracted from Git into an owned,
ignored snapshot; imported Unity dependencies may be read from another project
for offline compilation. Compilation never counts as test execution.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
import tarfile
import unittest
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / "tests" / "pr_features"
MANIFEST = CATALOG / "pr_manifest.json"
NAMESPACE = uuid.UUID("2df907fc-9779-40b1-9bcd-716cd180d5fa")


def read_manifest(path: Path = MANIFEST) -> list[dict]:
    data = json.loads(path.read_text(encoding="utf-8"))
    if data.get("schema_version") != 1:
        raise ValueError("Unsupported feature manifest version")
    entries = data["pull_requests"]
    seen = set()
    for entry in entries:
        number = entry["number"]
        if not isinstance(number, int) or number <= 0 or number in seen:
            raise ValueError("PR numbers must be unique positive integers")
        if not re.fullmatch(r"[a-f0-9]{40}", entry["commit"]):
            raise ValueError(f"PR {number}: expected full Git commit SHA")
        if entry["suite"] != f"pr_{number:02d}":
            raise ValueError(f"PR {number}: invalid suite directory")
        seen.add(number)
    if not entries:
        raise ValueError("Feature manifest has no pull requests")
    return entries


def extract_archive(archive: Path, destination: Path) -> None:
    """Reject links, special files and traversal before extracting any member."""
    destination = destination.resolve()
    with tarfile.open(archive) as source:
        members = source.getmembers()
        for member in members:
            path = PurePosixPath(member.name)
            if (path.is_absolute() or ".." in path.parts or "\\" in member.name
                    or ":" in member.name or not (member.isdir() or member.isfile())):
                raise ValueError(f"Unsafe archive member: {member.name}")
            resolved = (destination / member.name).resolve()
            if not resolved.is_relative_to(destination):
                raise ValueError(f"Archive member escapes snapshot: {member.name}")
        source.extractall(destination, members=members, filter="data")


def meta(path: Path, logical_name: str, folder: bool = False) -> None:
    guid = uuid.uuid5(NAMESPACE, logical_name).hex
    content = f"fileFormatVersion: 2\nguid: {guid}\n"
    if folder:
        content += "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    elif path.suffix == ".asmdef":
        content += "AssemblyDefinitionImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    else:
        content += "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    Path(str(path) + ".meta").write_text(content, encoding="utf-8")


def stage_suite(entry: dict, project: Path, catalog: Path = CATALOG) -> dict:
    suite = catalog / entry["suite"]
    if not suite.is_dir():
        raise ValueError(f"Missing suite: {suite}")
    target = project / "Assets" / "_Project" / "FeatureAcceptance"
    # The destination belongs solely to this runner's owned snapshot.
    if target.exists():
        if not target.resolve().is_relative_to(project.resolve()):
            raise ValueError("Acceptance directory escapes snapshot")
        shutil.rmtree(target)
    tests = target / "Tests"
    tests.mkdir(parents=True)
    meta(target, "FeatureAcceptance", True)
    meta(tests, "FeatureAcceptance/Tests", True)
    staged = {}
    for platform in ("EditMode", "PlayMode"):
        sources = sorted((suite / platform).glob("*.cs"))
        if not sources:
            continue
        folder = tests / platform
        folder.mkdir()
        meta(folder, f"FeatureAcceptance/Tests/{platform}", True)
        assembly = f"Lumenfall.FeatureAcceptance.{platform}"
        definition = {
            "name": assembly,
            "rootNamespace": "Lumenfall.FeatureAcceptance",
            "references": ["Lumenfall.Runtime", "Lumenfall.GameEvents"],
            "optionalUnityReferences": ["TestAssemblies"],
        }
        if platform == "EditMode":
            definition["includePlatforms"] = ["Editor"]
        asmdef = folder / (assembly + ".asmdef")
        asmdef.write_text(json.dumps(definition, indent=2) + "\n", encoding="utf-8")
        meta(asmdef, assembly)
        for source in sources:
            copied = folder / source.name
            shutil.copyfile(source, copied)
            meta(copied, f"{entry['suite']}/{platform}/{source.name}")
        staged[platform] = len(sources)
    python_sources = sorted(suite.glob("test_*.py"))
    if not staged and not python_sources:
        raise ValueError(f"PR {entry['number']}: suite contains no executable tests")
    return staged


def prepare(entry: dict, output: Path, allow_retarget: bool = False) -> tuple[Path, dict]:
    output.mkdir(parents=True, exist_ok=True)
    snapshot = output / "snapshot"
    marker = output / "snapshot.json"
    if snapshot.is_symlink() or (snapshot.exists() and snapshot.resolve().parent != output.resolve()):
        raise ValueError("Snapshot must be an owned direct child of the output directory")
    if snapshot.exists():
        if not marker.exists() or (json.loads(marker.read_text())["commit"] != entry["commit"] and not allow_retarget):
            raise ValueError(f"Refusing to overwrite an unowned or different snapshot: {snapshot}")
        saved = json.loads(marker.read_text())
        recorded = saved.get("source_sha256")
        if saved["commit"] != entry["commit"] or not recorded or snapshot_hash(snapshot) != recorded:
            # Restore authoritative source while retaining this snapshot's import cache.
            for relative in ("Lumenfall/Assets", "Lumenfall/Packages", "Lumenfall/ProjectSettings", "scripts", "docs"):
                path = snapshot / relative
                if not path.resolve().is_relative_to(snapshot.resolve()):
                    raise ValueError(f"Snapshot source directory escapes ownership: {path}")
                if path.is_dir():
                    shutil.rmtree(path)
                elif path.exists():
                    path.unlink()
            marker.unlink()
    if not marker.exists() or not snapshot.exists():
        subprocess.run(["git", "cat-file", "-e", entry["commit"] + "^{commit}"], cwd=ROOT, check=True)
        archive = output / "source.tar"
        try:
            with archive.open("wb") as stream:
                subprocess.run(["git", "archive", entry["commit"], "Lumenfall", "scripts", "docs"],
                               cwd=ROOT, stdout=stream, check=True)
            snapshot.mkdir(exist_ok=True)
            extract_archive(archive, snapshot)
            marker.write_text(json.dumps({"commit": entry["commit"], "pr": entry["number"],
                                          "source_sha256": snapshot_hash(snapshot)}), encoding="utf-8")
        finally:
            archive.unlink(missing_ok=True)
    project = snapshot / "Lumenfall"
    return project, stage_suite(entry, project)


def snapshot_hash(snapshot: Path) -> str:
    digest = hashlib.sha256()
    for relative in ("Lumenfall/Assets", "Lumenfall/Packages", "Lumenfall/ProjectSettings", "scripts", "docs"):
        directory = snapshot / relative
        if directory.is_symlink() or not directory.resolve().is_relative_to(snapshot.resolve()):
            raise ValueError(f"Snapshot source directory escapes ownership: {directory}")
        for source in sorted(directory.rglob("*")):
            logical = source.relative_to(snapshot).as_posix()
            if "/FeatureAcceptance/" in logical or logical.endswith("/FeatureAcceptance.meta"):
                continue
            if "__pycache__" in source.parts:
                continue
            if source.is_symlink() or not source.resolve().is_relative_to(snapshot.resolve()):
                raise ValueError(f"Snapshot contains a source link: {source}")
            if source.is_file():
                digest.update(logical.encode())
                digest.update(source.read_bytes())
    return digest.hexdigest()


def command(args: list[str], cwd: Path, log: Path, timeout: int = 1800) -> int:
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8") as stream:
        stream.write(json.dumps(args) + "\n")
        stream.flush()
        try:
            return subprocess.run(args, cwd=cwd, stdout=stream, stderr=subprocess.STDOUT,
                                  timeout=timeout, check=False).returncode
        except subprocess.TimeoutExpired:
            stream.write(f"\nTimed out after {timeout} seconds\n")
            return 124


def assembly_sources(project: Path, definition: Path) -> list[Path]:
    result = []
    for source in sorted(definition.parent.rglob("*.cs")):
        parent = source.parent
        while parent != definition.parent and not list(parent.glob("*.asmdef")):
            parent = parent.parent
        if parent == definition.parent:
            result.append(source)
    return result


def compile_project(project: Path, reference: Path, editor: Path, output: Path) -> list[dict]:
    """Compile current source, using imported templates for external references only."""
    if project.resolve().is_relative_to(reference.resolve()) or reference.resolve().is_relative_to(project.resolve()):
        raise ValueError("Current source and read-only dependency project must be separate")
    artifacts = reference / "Library" / "Bee" / "artifacts"
    compiler = editor.parent / "Data" / "DotNetSdkRoslyn" / "csc.dll"
    dotnet = editor.parent / "Data" / "NetCoreRuntime" / ("dotnet.exe" if sys.platform == "win32" else "dotnet")
    if not artifacts.is_dir() or not compiler.is_file() or not dotnet.is_file():
        raise ValueError("Offline compile requires imported reference project and Unity Roslyn runtime")
    assemblies = {}
    for asmdef in sorted((project / "Assets").rglob("*.asmdef")):
        data = json.loads(asmdef.read_text(encoding="utf-8-sig"))
        name = data["name"]
        if not name.startswith("Lumenfall."):
            continue
        is_test = "TestAssemblies" in data.get("optionalUnityReferences", [])
        if is_test and not name.startswith("Lumenfall.FeatureAcceptance."):
            continue
        if data.get("defineConstraints") or data.get("excludePlatforms"):
            raise ValueError(f"Offline compilation does not support constraints on {name}")
        assemblies[name] = (asmdef, data)
    compiled = set()
    results = []
    output.mkdir(parents=True, exist_ok=True)
    while len(compiled) < len(assemblies):
        progress = False
        for name, (asmdef, data) in assemblies.items():
            if name in compiled:
                continue
            dependencies = data.get("references", [])
            if any(dep in assemblies and dep not in compiled for dep in dependencies):
                continue
            template_name = name
            if name.startswith("Lumenfall.FeatureAcceptance."):
                template_name = ("Lumenfall.GameEvents.Tests" if name.endswith("EditMode")
                                 else "Lumenfall.World.PlayModeTests")
            templates = list(artifacts.rglob(template_name + ".rsp"))
            if len(templates) != 1:
                raise ValueError(f"Expected one imported template for {template_name}, found {len(templates)}")
            lines = ["-target:library", "-nologo", "-deterministic", f'-out:"{output / (name + ".dll")}"']
            references = set()
            for line in templates[0].read_text(encoding="utf-8-sig").splitlines():
                match = re.match(r'^[-/]r:"?(.+?)"?$', line)
                if match:
                    raw = match[1].replace("\\", "/")
                    filename = raw.rsplit("/", 1)[-1]
                    if filename.startswith(("Lumenfall.", "Assembly-CSharp")):
                        continue
                    if "/Editor/Data/" in raw:
                        path = editor.parent / "Data" / raw.split("/Editor/Data/", 1)[1]
                    else:
                        path = Path(raw)
                        if not path.is_absolute():
                            path = reference / path
                    if not path.is_file():
                        raise ValueError(f"Missing dependency: {path}")
                    references.add(path.resolve())
                elif re.match(r"^[-/](define:|langversion:|unsafe|nostdlib|noconfig|nullable:|nowarn:)", line):
                    lines.append(line)
            for dep in dependencies:
                if dep in assemblies:
                    references.add(output / (dep + ".dll"))
                elif not dep.startswith("GUID:"):
                    matches = list(artifacts.rglob(dep + ".ref.dll"))
                    if len(matches) == 1:
                        references.add(matches[0])
            lines.extend(f'-r:"{path}"' for path in sorted(references))
            sources = assembly_sources(project, asmdef)
            if not sources:
                raise ValueError(f"No current sources for {name}")
            lines.extend(f'"{source}"' for source in sources)
            rsp = output / (name + ".rsp")
            rsp.write_text("\n".join(lines) + "\n", encoding="utf-8")
            log = output / (name + ".log")
            code = command([str(dotnet), str(compiler), "@" + str(rsp)], project, log)
            results.append({"assembly": name, "exit_code": code, "sources": len(sources), "log": str(log)})
            if code:
                return results
            compiled.add(name)
            progress = True
        if not progress:
            raise ValueError("Unresolved first-party assembly dependency cycle")
    return results


def validate_results(path: Path, expected: dict | None = None) -> dict:
    if not path.is_file():
        raise ValueError("Unity did not produce test results")
    root = ET.parse(path).getroot()
    cases = list(root.iter("test-case"))
    if not cases:
        raise ValueError("Unity results contain no test cases")
    outcomes = {"passed": 0, "failed": 0, "skipped": 0}
    for case in cases:
        result = case.get("result", "").lower()
        if result == "passed":
            outcomes["passed"] += 1
        elif result == "failed":
            outcomes["failed"] += 1
        else:
            outcomes["skipped"] += 1
    if outcomes["failed"] or outcomes["skipped"] or root.get("result", "").lower() != "passed":
        raise ValueError(f"Unity suite did not pass completely: {outcomes}")
    if expected is not None:
        discovered = {}
        for case in cases:
            fixture = case.get("classname", "")
            discovered[fixture] = discovered.get(fixture, 0) + 1
        if discovered != expected:
            raise ValueError(f"Unity discovery differs from expected fixtures/cases: expected={expected}, actual={discovered}")
    return outcomes


def run_unity(project: Path, editor: Path, platform: str, output: Path, timeout: int,
              expected: dict | None = None) -> dict:
    xml = output / (platform + ".xml")
    xml.unlink(missing_ok=True)  # A previous success must never mask this invocation.
    log = output / (platform + ".log")
    launcher_log = output / (platform + "-launcher.log")
    args = [str(editor), "-batchmode", "-nographics", "-projectPath", str(project),
            "-runTests", "-testPlatform", platform, "-testFilter", "Lumenfall.FeatureAcceptance",
            "-testResults", str(xml), "-logFile", str(log)]
    code = command(args, project, launcher_log, timeout)
    result = {"platform": platform, "exit_code": code, "xml": str(xml), "log": str(log)}
    try:
        result.update(validate_results(xml, expected))
        result["status"] = "passed" if code == 0 else "failed"
    except (ValueError, ET.ParseError) as error:
        result.update(status="failed", error=str(error))
    return result


def execute_python_suite(directory: Path, expected: int) -> int:
    suite = unittest.TestLoader().discover(str(directory), pattern="test_*.py")
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    counts = {"executed": result.testsRun, "failed": len(result.failures),
              "errors": len(result.errors), "skipped": len(result.skipped)}
    print("PR_FEATURE_PYTHON_RESULT=" + json.dumps(counts))
    return int(not result.wasSuccessful() or result.testsRun != expected or expected <= 0 or bool(result.skipped))


def suite_hash(path: Path) -> str:
    digest = hashlib.sha256()
    for source in sorted(path.rglob("*")):
        if source.is_file() and "__pycache__" not in source.parts:
            digest.update(source.relative_to(path).as_posix().encode())
            digest.update(source.read_bytes())
    return digest.hexdigest()


def seed_library(project: Path, reference: Path) -> bool:
    """Copy, never link, an import cache; Unity still imports the target source."""
    destination = project / "Library"
    source = reference / "Library"
    if destination.exists():
        return False
    if not source.is_dir() or project.resolve().is_relative_to(reference.resolve()):
        raise ValueError("Cache seed must be a separate imported Unity project")
    if sys.platform == "win32" and shutil.which("robocopy"):
        # A Library contains many small immutable cache files; bounded parallel
        # copying avoids serial per-file latency without sharing writable links.
        code = subprocess.run(["robocopy", str(source), str(destination), "/E", "/MT:16",
                               "/R:1", "/W:1", "/NFL", "/NDL", "/NJH", "/NJS", "/NP",
                               "/XF", "*.lock", "*lockfile*", "*-lock"],
                              stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                              timeout=900, check=False).returncode
        if code >= 8:
            raise ValueError(f"Unity cache copy failed (robocopy exit {code})")
    else:
        shutil.copytree(source, destination, ignore=shutil.ignore_patterns("*.lock", "*lockfile*", "*-lock"))
    return True


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--pr", nargs="+", default=["all"], help="PR numbers or all")
    parser.add_argument("--mode", choices=["prepare", "python", "compile", "editmode", "playmode", "all"], default="all")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--output", type=Path, default=ROOT / "Temp" / "pr-tests")
    parser.add_argument("--unity-editor", type=Path, default=Path("C:/Program Files/Unity/Hub/Editor/6000.4.10f1/Editor/Unity.exe"))
    parser.add_argument("--reference-project", type=Path)
    parser.add_argument("--seed-library", type=Path, help="Separate imported project whose Library is copied into new snapshots")
    parser.add_argument("--shared-snapshot", type=Path, help="Owned snapshot output reused sequentially across selected PRs; retains only its import cache")
    parser.add_argument("--timeout", type=int, default=1800)
    parser.add_argument("--python-suite", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--expected-python-count", type=int, help=argparse.SUPPRESS)
    args = parser.parse_args(argv)
    if args.python_suite is not None:
        if args.expected_python_count is None:
            parser.error("Internal Python suite execution requires an expected case count")
        return execute_python_suite(args.python_suite, args.expected_python_count)
    entries = read_manifest()
    if args.list:
        for entry in entries:
            print(f"PR {entry['number']:2}: {entry['title']} [{entry['commit'][:12]}]")
        return 0
    selected = {entry["number"] for entry in entries} if args.pr == ["all"] else {int(value) for value in args.pr}
    if not selected.issubset({entry["number"] for entry in entries}):
        parser.error("Requested PR is not in the pinned manifest")
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    reports = []
    for entry in entries:
        if entry["number"] not in selected:
            continue
        report = {"pr": entry["number"], "commit": entry["commit"], "mode": args.mode,
                  "suite_sha256": suite_hash(CATALOG / entry["suite"]), "status": "not_run"}
        reports.append(report)
        print(f"PR {entry['number']}: {args.mode}", flush=True)
        location = output / entry["suite"]
        location.mkdir(parents=True, exist_ok=True)
        try:
            snapshot_location = args.shared_snapshot.resolve() if args.shared_snapshot else location
            project, staged = prepare(entry, snapshot_location, allow_retarget=args.shared_snapshot is not None)
            report["project"] = str(project)
            report["staged_sources"] = staged
            if set(staged) != set(entry["expected_cases"]):
                raise ValueError(f"Staged test platforms differ from manifest: expected={sorted(entry['expected_cases'])}, actual={sorted(staged)}")
            if args.seed_library is not None and staged and args.mode in ("editmode", "playmode", "all"):
                report["cache_seeded"] = seed_library(project, args.seed_library.resolve())
            python_tests = list((CATALOG / entry["suite"]).glob("test_*.py"))
            if args.mode in ("python", "all") and python_tests:
                log = location / "python.log"
                code = command([sys.executable, str(Path(__file__).resolve()), "--python-suite",
                                str(CATALOG / entry["suite"]), "--expected-python-count",
                                str(entry["expected_python_cases"])],
                               project.parent, log, args.timeout)
                report["python"] = {"exit_code": code, "log": str(log)}
                if code:
                    raise ValueError("Python feature tests failed")
            elif args.mode == "python":
                raise ValueError("No Python feature tests selected")
            if args.mode == "compile":
                if args.reference_project is None:
                    raise ValueError("--reference-project is required for offline compilation")
                report["compilation"] = compile_project(project, args.reference_project.resolve(),
                                                        args.unity_editor.resolve(), location / "compile")
                if not report["compilation"] or report["compilation"][-1]["exit_code"]:
                    raise ValueError("Offline compilation failed")
            if args.mode in ("editmode", "playmode", "all"):
                platforms = [platform for platform in ("EditMode", "PlayMode")
                             if platform in staged and args.mode in ("all", platform.lower())]
                if platforms and not args.unity_editor.is_file():
                    raise ValueError("Unity Editor executable is unavailable")
                report["unity"] = []
                for platform in platforms:
                    result = run_unity(project, args.unity_editor.resolve(), platform, location, args.timeout,
                                       entry["expected_cases"][platform])
                    report["unity"].append(result)
                if not report["unity"] and (args.mode != "all" or not python_tests):
                    raise ValueError("No tests selected for requested platform")
                if any(result["status"] != "passed" for result in report["unity"]):
                    raise ValueError("Unity acceptance tests failed")
            report["status"] = "prepared" if args.mode == "prepare" else "compiled" if args.mode == "compile" else "passed"
        except (ValueError, OSError, subprocess.SubprocessError, ET.ParseError) as error:
            report.update(status="failed", error=str(error))
        (location / "report.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        (location / f"report-{args.mode}.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        (output / "report.json").write_text(json.dumps(reports, indent=2) + "\n", encoding="utf-8")
        print(f"PR {entry['number']}: {report['status']}" + (": " + report["error"] if "error" in report else ""), flush=True)
    return int(any(report["status"] == "failed" for report in reports))


if __name__ == "__main__":
    raise SystemExit(main())
