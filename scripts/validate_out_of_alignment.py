"""Read-only source/baked parity checks for Cinna's provisional alignment quest."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Lumenfall/Assets"
MARKER = "orientation.cinna.align"
QUEST = "cinna_out_of_alignment"
TRIAL = "cinna_cold_feet_warm_idea_dialogue"

def read(path):
    return (ASSETS / path).read_text(encoding="utf-8-sig")

def id_block(text, key):
    match = re.search(r"(?m)^( +)-? ?id: " + re.escape(key) + r"$", text)
    assert match, key
    tail = text[match.start():]
    next_id = re.search(r"(?m)^" + re.escape(match.group(1)) + r"-? ?id: ", tail[1:])
    return tail if next_id is None else tail[:next_id.start() + 1]

def value(block, key):
    match = re.search(r"(?m)^ +" + re.escape(key) + r": (.*)$", block)
    assert match, key
    return match.group(1)

campus = json.loads(read("Resources/Maps/campus.json"))
marker = next(o for o in campus["objects"] if o["id"] == MARKER)
assert (marker["x"], marker["y"], marker.get("rotation", 0)) == (40, 22, 0)
assert marker["tags"] == ["vis", "vis-target:90"]
target = next(t for t in campus["puzzleTargets"] if t["id"] == "quest.cinna.out-of-alignment")
monitor = next(p for p in json.loads(read("Resources/Quests/quest_puzzle_definitions.json"))["puzzles"] if p["questId"] == QUEST)
assert monitor["mapId"] == "campus"
for predicate in [target, monitor["targets"][0]]:
    assert predicate["objectIds"] == [MARKER] and predicate["requiredAligned"] and predicate["requiredRotation"] == 90
area = read("_Project/World/Areas/campus.asset")
marker_block = id_block(area, MARKER)
assert (value(marker_block, "x"), value(marker_block, "y"), value(marker_block, "rotation")) == ("40", "22", "0")
assert "      - vis\n      - vis-target:90" in marker_block
target_block = id_block(area, "quest.cinna.out-of-alignment")
assert value(target_block, "requiredAligned") == "1" and value(target_block, "requiredRotation") == "90"
assert re.findall(r"(?m)^      - (orientation[^\n]*)$", target_block) == [MARKER]
prefab = read("_Project/World/Areas/Prefabs/campus.prefab")
documents = re.split(r"(?m)^--- !u!", prefab)
registration = next(d for d in documents if "  objectId: " + MARKER + "\n" in d)
assert value(registration, "cell") == "{x: 40, y: 22}"
assert value(registration, "spellCompatible") == "1"
assert "  - vis\n  - vis-target:90" in registration
game_object = value(registration, "m_GameObject")
transform = next(d for d in documents if d.startswith("4 &") and "  m_GameObject: " + game_object + "\n" in d)
assert value(transform, "m_LocalPosition") == "{x: 40, y: 0, z: 22}"
assert value(transform, "m_LocalRotation") == "{x: 0, y: 0, z: 0, w: 1}"
npcs = json.loads(read("Resources/NPCs/npcs.json"))["npcs"]
trial = next(n for n in npcs if n["id"] == "cinna_cirra_trial")
orientation = next(n for n in npcs if n["id"] == "cinna_cirra_orientation")
assert orientation["hideWhenQuestCompleted"] == trial["showWhenQuestCompleted"] == QUEST
assert len(trial["dialogue"]) == 3 and not trial.get("dialogueIds")
assert {r["requiredQuestStatus"] for r in trial["dialogue"]} == {"NotStarted", "Active", "Completed"}
assert all(r["dialogueId"] == TRIAL and r["prerequisiteQuestId"] == QUEST and r["prerequisiteQuestStatus"] == "Completed" for r in trial["dialogue"])
catalog_trial = id_block(read("_Project/World/Areas/NpcCatalog.asset"), "cinna_cirra_trial")
prefab_trial = next(d for d in documents if "    id: cinna_cirra_trial\n" in d)
for block in [catalog_trial, prefab_trial]:
    assert value(block, "showWhenQuestCompleted") == QUEST
    assert value(block, "dialogueIds") == "[]"
    rules = re.findall(r"(?m)^    - dialogueId: (.*)$", block)
    assert rules == [TRIAL] * 3
    assert re.findall(r"(?m)^      prerequisiteQuestId: (.*)$", block) == [QUEST] * 3
    assert re.findall(r"(?m)^      prerequisiteQuestStatus: (.*)$", block) == ["Completed"] * 3
    assert set(re.findall(r"(?m)^      requiredQuestStatus: (.*)$", block)) == {"NotStarted", "Active", "Completed"}
print("validated alignment marker/target and deterministic Cinna handoff source/baked parity")
