#!/usr/bin/env python3
"""Acceptance checks for the campus garden, NPC layers, and east gate portals."""
from __future__ import annotations

import json
import sys
from pathlib import Path
from collections import deque

ROOT = Path(__file__).resolve().parents[1]
RES = ROOT / "Lumenfall" / "Assets" / "Resources"
MAPS = RES / "Maps"
NPCS = RES / "NPCs"
FAILS: list[str] = []


def fail(msg: str) -> None:
    FAILS.append(msg)


def load(name: Path):
    try:
        return json.loads(name.read_text(encoding="utf-8"))
    except Exception as exc:
        fail(f"cannot parse {name}: {exc}")
        return {}


def normalize(path: Path, width: int, height: int) -> list[str]:
    raw = path.read_text(encoding="utf-8").replace("\r", "").splitlines()
    if raw and raw[0] == "".join(str(i % 10) for i in range(1, width + 1)):
        raw = raw[1:]
    rows = []
    for row in raw:
        # Source maps retain a one-digit row label after the glyph cells.
        rows.append(row[:width] if len(row) >= width else row)
    if len(rows) != height or any(len(row) != width for row in rows):
        fail(f"{path.name}: normalized shape is {len(rows)} rows, widths {sorted(set(map(len, rows)))}; expected {width}x{height}")
    return rows[:height]


def walkable(rows: list[str], x: int, y: int) -> bool:
    return 0 <= y < len(rows) and 0 <= x < len(rows[y]) and rows[y][x] in "gXp"


def main() -> int:
    # Authoring sources retain the numbered ruler and row suffixes, while runtime
    # TextAssets are normalized. Check content invariants, not a recovery snapshot
    # or byte hash from the original garden-editing task.
    source_campus = ROOT / "docs" / "maps" / "layouts" / "campus.txt"
    if source_campus.exists():
        raw = source_campus.read_text(encoding="utf-8-sig").splitlines()
        if not raw or not raw[0].startswith("1234567890") or len(raw) <= 54:
            fail(f"authoring campus source lost numeric ruler/row-label rows: {source_campus}")
    campus_meta = load(MAPS / "campus.json")
    forest_meta = load(MAPS / "eastForest.json")
    cw, ch = campus_meta.get("width", 0), campus_meta.get("height", 0)
    fw, fh = forest_meta.get("width", 0), forest_meta.get("height", 0)
    campus = normalize(MAPS / "campus.txt", cw, ch)
    forest = normalize(MAPS / "eastForest.txt", fw, fh)

    garden = (10, 32, 35, 49)
    for region in campus_meta.get("regions", []):
        if region.get("id") == "C04":
            r = region.get("rect", {})
            if not (r.get("x", -1) >= garden[0] and r.get("y", -1) >= garden[1]
                    and r.get("x", -1) + r.get("width", 0) - 1 <= garden[2]
                    and r.get("y", -1) + r.get("height", 0) - 1 <= garden[3]):
                fail(f"C04 metadata rect is not inside garden: {r}")
    npc_db = load(NPCS / "npcs.json")
    assets = {n.get("asset") for n in npc_db.get("npcs", [])}
    if assets != {"NPCs/Elara.prefab"} or len(npc_db.get("npcs", [])) != 7:
        fail(f"NPC shared asset/count mismatch: count={len(npc_db.get('npcs', []))}, assets={sorted(assets)}")
    kat = next((n for n in npc_db.get("npcs", []) if n.get("id") == "katrina"), None)
    if not kat or kat.get("mapId") != "campus" or (kat.get("x"), kat.get("y")) != (23, 40):
        fail(f"Katrina marker mismatch: {kat}")
    elif not walkable(campus, 23, 40) or campus[40][23] in "TX":
        fail("Katrina is not on a walkable non-tree/non-portal cell")

    # Interior 2x2 tree blocks are checked; the perimeter T planting border is metadata.
    covered = set()
    for y in range(garden[1], garden[3]):
        for x in range(garden[0], garden[2]):
            if campus[y][x] == "T" and (x, y) not in covered:
                if all(campus[y + dy][x + dx] == "T" for dy in (0, 1) for dx in (0, 1)):
                    covered.update((x + dx, y + dy) for dy in (0, 1) for dx in (0, 1))
                elif garden[0] < x < garden[2] and garden[1] < y < garden[3]:
                    fail(f"garden T at ({x},{y}) cannot be coalesced as parser-compatible 2x2")
    for y in range(garden[1] + 1, garden[3]):
        for x in range(garden[0] + 1, garden[2]):
            if campus[y][x] == "T" and (x, y) not in covered:
                fail(f"garden T at ({x},{y}) is not part of a complete 2x2 block")
    if not (walkable(campus, 22, 49) and walkable(campus, 23, 49)):
        fail("garden entrance (22,49)-(23,49) is not walkable")

    # Exact active east-gate X cells and reserved future markers.
    expected = {"campus": {(86, 25), (86, 26)}, "eastForest": {(0, 34), (1, 34), (0, 35), (1, 35)}}
    for name, rows, cells in (("campus", campus, expected["campus"]), ("eastForest", forest, expected["eastForest"])):
        actual = {(x, y) for y, row in enumerate(rows) for x, c in enumerate(row) if c == "X"}
        if name == "campus":
            actual -= {(45, 19), (46, 19), (68, 23)}
        if actual != cells:
            fail(f"{name} active X cells {sorted(actual)} != {sorted(cells)}")
    reserved = {(45, 19), (46, 19), (68, 23)}
    actual_reserved = {(x, y) for x, y in reserved if campus[y][x] == "X"}
    if actual_reserved != reserved:
        fail(f"reserved future campus X cells mismatch: {sorted(actual_reserved)}")
    manifest_path = ROOT / "docs" / "maps" / "layouts" / "portal-bindings.json"
    if manifest_path.exists():
        manifest = load(manifest_path)
        maps = manifest.get("maps", {})
        if {tuple(v) for v in maps.get("campus", [])} != reserved | expected["campus"]:
            fail(f"portal manifest campus cells disagree with source: {maps.get('campus')}")
        if {tuple(v) for v in maps.get("eastForest", [])} != expected["eastForest"]:
            fail(f"portal manifest forest cells disagree with source: {maps.get('eastForest')}")
    portals = campus_meta.get("portals", []) + forest_meta.get("portals", [])
    ids = [p.get("id") for p in portals]
    if len(ids) != len(set(ids)):
        fail("portal IDs are duplicated")
    for p in portals:
        if p.get("id", "").startswith("P.A01.F03") and p.get("x") not in (86,):
            fail(f"legacy/noncanonical campus east portal remains: {p}")
        dest = p.get("destinationScene")
        if dest not in ("campus", "eastForest"):
            continue
        target_rows = campus if dest == "campus" else forest
        ax, ay = p.get("arrivalOffsetX", -1), p.get("arrivalOffsetY", -1)
        if p.get("id", "").startswith(("P.A01.F03", "P.F03.A01")) and not walkable(target_rows, ax, ay):
            fail(f"portal {p.get('id')} arrival ({ax},{ay}) is not walkable")
    for p in portals:
        if p.get("id") in {"P.A01.F03", "P.F03.A01"} and p.get("destinationPortalId") not in ids:
            fail(f"portal {p.get('id')} has missing canonical reciprocal {p.get('destinationPortalId')}")
    for msg in FAILS:
        print("FAIL:", msg)
    if not FAILS:
        print("PASS: garden, NPC, and east-gate acceptance checks")
    return 1 if FAILS else 0


if __name__ == "__main__":
    raise SystemExit(main())
