#!/usr/bin/env python3
"""Static validation for Lumenfall's shipped data-driven world assets."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / "Lumenfall" / "Assets"
MAPS = ROOT / "Resources" / "Maps"
NPCS = ROOT / "Resources" / "NPCs" / "npcs.json"
EVENTS = ROOT / "_Project" / "Systems" / "GameEvents" / "Resources" / "GameEvents" / "sample_events.json"
CATALOG = Path(__file__).resolve().parents[1] / "docs" / "maps" / "layouts" / "index.json"
errors = []
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
            if glyph not in ". WBgG pT".replace(" ", ""):
                row_error_count[map_id] = row_error_count.get(map_id, 0) + 1
                break
    for portal in data.get("portals", []):
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
            errors.append(f"{map_id}/{portal.get('id')}: arrival {x},{y} outside {dest}")

npcs = json.loads(NPCS.read_text(encoding="utf-8"))
events = json.loads(EVENTS.read_text(encoding="utf-8"))
event_ids = {d.get("id") for d in events.get("dialogues", [])}
quest_ids = {q.get("id") for q in events.get("quests", [])}
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
            errors.append(f"catalog {portal.get('id')}: destination {dest_map}@{expected_x},{expected_y} has no runtime portal")

print(f"checked {len(maps)} maps and {len(npcs.get('npcs', []))} NPCs")
for map_id, count in sorted(row_error_count.items()):
    errors.append(f"{map_id}: {count} malformed/unsupported tile rows or glyphs")
for error in errors: print("FAIL: " + error)
raise SystemExit(1 if errors else 0)
