#!/usr/bin/env python3
"""Static validation for Lumenfall's shipped data-driven world assets."""
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Lumenfall" / "Assets"
MAPS = ROOT / "Resources" / "Maps"
NPCS = ROOT / "Resources" / "NPCs" / "npcs.json"
EVENTS_DIRS = [ROOT / "Resources" / "GameEvents", ROOT / "_Project" / "Systems" / "GameEvents" / "Resources" / "GameEvents"]
CATALOG = Path(__file__).resolve().parents[1] / "docs" / "maps" / "layouts" / "index.json"
errors = []
pending_portals = []
maps = {}
row_error_count = {}

for path in sorted(MAPS.glob("*.json")):
    data = json.loads(path.read_text(encoding="utf-8"))
    key = data.get("id", path.stem)
    # Prefer the filename matching the id when duplicate authoring assets exist
    # (e.g. map.json and campus.json both declare id=campus).
    if key not in maps or path.stem == key:
        maps[key] = (path, data)

for map_id, (path, data) in maps.items():
    tile = data.get("tileFile")
    if not tile:
        continue
    tile_path = MAPS / tile
    if not tile_path.exists():
        errors.append(f"{map_id}: missing tileFile {tile}")
        continue
    rows = tile_path.read_text(encoding="utf-8").replace("\r", "").split("\n")
    while rows and rows[-1] == "": rows.pop()
    if len(rows) != data.get("height"):
        errors.append(f"{map_id}: rows={len(rows)} declared height={data.get('height')}")
    for y, row in enumerate(rows):
        if len(row) != data.get("width"):
            row_error_count[map_id] = row_error_count.get(map_id, 0) + 1
        for x, glyph in enumerate(row[:data.get("width", 0)]):
            if glyph not in ".WBgGpTX":
                row_error_count[map_id] = row_error_count.get(map_id, 0) + 1
                break
    for portal in data.get("portals", []):
        if portal.get("reserved"):
            continue
        dest = portal.get("destinationScene")
        target = portal.get("destinationPortalId")
        if dest not in maps:
            errors.append(f"{map_id}/{portal.get('id')}: destination map {dest!r} missing")
            continue
        target_map = maps[dest][1]
        targets = target_map.get("portals", [])
        if not any(p.get("id") == target or p.get("id", "").endswith("." + str(target)) for p in targets):
            errors.append(f"{map_id}/{portal.get('id')}: target alias {target!r} not found in {dest}")
        offset = target_map.get("coordinateOffset") or {}
        x = portal.get("arrivalOffsetX", 0) - offset.get("x", 0)
        y = portal.get("arrivalOffsetY", 0) - offset.get("y", 0)
        if not (0 <= x < target_map.get("width", 0) and 0 <= y < target_map.get("height", 0)):
            pending_portals.append(f"{map_id}/{portal.get('id')} -> {dest}@{x},{y}")

# WorldStateService.AddConnection rejects cables weaker than 0.2 (strength = 1 / (1 + tile distance)),
# so every authored weave-with pair must sit within four tiles or the quest cannot be completed.
def check_weave_pairs(source, cells, partners):
    for object_id, partner in partners.items():
        if partner not in cells or object_id not in cells:
            errors.append(f"{source}: {object_id} weaves with missing object {partner!r}")
            continue
        (ax, ay), (bx, by) = cells[object_id], cells[partner]
        if 1 / (1 + math.hypot(ax - bx, ay - by)) < 0.2:
            errors.append(f"{source}: weave pair {object_id}|{partner} is too far apart to connect")

for map_id, (path, data) in maps.items():
    objects = data.get("objects", [])
    check_weave_pairs(path.name, {o.get("id"): (o.get("x", 0), o.get("y", 0)) for o in objects},
                      {o.get("id"): t.split(":", 1)[1] for o in objects for t in o.get("tags", []) if t.startswith("weave-with:")})

REGISTRATION = re.compile(r"objectId: (\S+)\r?\n  cell: \{x: (-?\d+), y: (-?\d+)\}(.*?)(?=\r?\n--- |\Z)", re.S)
for path in sorted((ROOT / "_Project" / "World" / "Areas" / "Prefabs").glob("*.prefab")):
    cells, partners = {}, {}
    for object_id, x, y, rest in REGISTRATION.findall(path.read_text(encoding="utf-8")):
        cells[object_id] = (int(x), int(y))
        partner = re.search(r"- weave-with:(\S+)", rest)
        if partner: partners[object_id] = partner.group(1)
    check_weave_pairs(path.name, cells, partners)

npcs = json.loads(NPCS.read_text(encoding="utf-8"))
event_docs = [json.loads(path.read_text(encoding="utf-8")) for directory in EVENTS_DIRS for path in sorted(directory.glob("*.json"))]
event_ids = {d.get("id") for doc in event_docs for d in doc.get("dialogues", [])}
quest_ids = {q.get("id") for doc in event_docs for q in doc.get("quests", [])}
for npc in npcs.get("npcs", []):
    if not npc.get("mapId"):
        errors.append(f"NPC {npc.get('id')}: missing mapId")
    asset_path = (ROOT / "Resources" / npc["asset"]) if npc.get("asset") else None
    if asset_path and not (asset_path.exists() or asset_path.with_suffix(".prefab").exists()):
        errors.append(f"NPC {npc.get('id')}: missing prefab/resource {npc['asset']}")
    for dialogue in npc.get("dialogue", []):
        if dialogue.get("dialogueId") not in event_ids:
            errors.append(f"NPC {npc.get('id')}: dialogue {dialogue.get('dialogueId')!r} missing")
        if dialogue.get("questId") and dialogue["questId"] not in quest_ids:
            errors.append(f"NPC {npc.get('id')}: quest {dialogue['questId']!r} missing")

# Every catalog portal destination should have a runtime portal at its declared cell.
catalog = json.loads(CATALOG.read_text(encoding="utf-8"))
for area in catalog.get("areas", []):
    source_map = area.get("mapId")
    if source_map not in maps:
        continue
    for portal in area.get("portals", []):
        if portal.get("state") == "reserved":
            continue
        dest = portal.get("destination") or {}
        dest_map = dest.get("mapId")
        cell = dest.get("cell") or {}
        runtime = maps.get(dest_map)
        if not runtime:
            continue
        offset = runtime[1].get("coordinateOffset") or {}
        expected_x = cell.get("x", 0) - offset.get("x", 0)
        expected_y = cell.get("y", 0) - offset.get("y", 0)
        found = any(p.get("x") == expected_x and p.get("y") == expected_y for p in runtime[1].get("portals", []))
        if not found:
            pending_portals.append(f"catalog {portal.get('id')} -> {dest_map}@{expected_x},{expected_y}")

print(f"checked {len(maps)} maps and {len(npcs.get('npcs', []))} NPCs")
for map_id, count in sorted(row_error_count.items()):
    errors.append(f"{map_id}: {count} malformed/unsupported tile rows or glyphs")
if pending_portals:
    print("PENDING legacy portal arrivals (awaiting user-approved X endpoints):")
    for item in pending_portals: print("PENDING: " + item)
for error in errors: print("FAIL: " + error)
raise SystemExit(1 if errors else 0)
