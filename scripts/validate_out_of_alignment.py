"""Read-only source/live parity checks for Cinna's Out of Alignment relic and its hand-off to the ice trial."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Lumenfall/Assets"
RELIC = "relic.cinna.orientation"
STAND = "relic.cinna.frost"
START = (23, 53)
STAND_CELL = (3, 1)
TRIAL_MAP = "C03.IceTrial"
TAGS = ["vis", "vis-lift", "vis-destination:" + TRIAL_MAP]
QUEST = "cinna_out_of_alignment"
DELIVERED = "vis.delivered." + RELIC
DEPARTURE = "cinna.trial.departure"
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

# The campus relic is lifted with Vis and carried to the ice trial; no campus target or monitor completes the quest.
campus = json.loads(read("Resources/Maps/campus.json"))
relic = next(o for o in campus["objects"] if o["id"] == RELIC)
assert (relic["x"], relic["y"], relic.get("rotation", 0)) == START + (0,)
assert relic["tags"] == TAGS and relic.get("blocks") is True
assert all(o["id"] != "orientation.cinna.align" for o in campus["objects"])
assert all(t["id"] != "quest.cinna.out-of-alignment" for t in campus["puzzleTargets"])
assert all(p["questId"] != QUEST for p in json.loads(read("Resources/Quests/quest_puzzle_definitions.json"))["puzzles"])
# The source layout must leave the relic's start tile and its neighbours open so it can be lifted and set down.
rows = read("Resources/Maps/campus.txt").splitlines()
legend = json.loads(read("Resources/Maps/legend.json"))
for x, y in [START, (START[0], START[1] + 1), (START[0] + 1, START[1])]:
    glyph = rows[y][x]
    assert not legend[glyph].get("collision"), f"cell ({x},{y}) is {legend[glyph]['name']}"
area = read("_Project/World/Areas/campus.asset")
relic_block = id_block(area, RELIC)
assert (value(relic_block, "x"), value(relic_block, "y"), value(relic_block, "rotation")) == (str(START[0]), str(START[1]), "0")
assert value(relic_block, "blocks") == "1" and value(relic_block, "compatibleWithSpells") == "1"
assert "\n".join("      - " + tag for tag in TAGS) in relic_block
assert "orientation.cinna.align" not in area and "quest.cinna.out-of-alignment" not in area
# The relic is built at runtime by CinnaAlignmentRelic; the old baked marker stays inactive and never registers.
prefab = read("_Project/World/Areas/Prefabs/campus.prefab")
documents = re.split(r"(?m)^--- !u!", prefab)
assert not any("  objectId: " + RELIC + "\n" in d for d in documents)
for registration in (d for d in documents if "  objectId: orientation.cinna.align\n" in d):
    owner = re.search(r"fileID: (-?\d+)", value(registration, "m_GameObject")).group(1)
    game_object = next(d for d in documents if d.startswith("1 &" + owner + "\n"))
    assert value(game_object, "m_IsActive") == "0", "the legacy marker must not register beside the relic"

# The ice trial's pedestal is the relic's stand.
stand_block = id_block(read("_Project/World/Areas/C03.IceTrial.asset"), STAND)
assert (value(stand_block, "x"), value(stand_block, "y")) == tuple(map(str, STAND_CELL))
assert "      - vis-stand:" + RELIC in stand_block

# Cinna takes the report only once the relic is delivered; the report sends Cinna ahead to the trial.
events = json.loads(read("Resources/GameEvents/quest_npc_dialogue.json"))
dialogue = next(d for d in events["dialogues"] if d["id"] == "cinna_out_of_alignment_dialogue")
nodes = {n["id"]: n for n in dialogue["nodes"]}
choices = {c["id"]: c for c in nodes["greeting"]["choices"]}
assert {"type": "Flag", "key": DELIVERED, "boolValue": True} in choices["report"]["conditions"]
assert {"type": "Flag", "key": DELIVERED, "boolValue": True, "negate": True} in choices["active"]["conditions"]
assert {"type": "SetFlag", "key": DEPARTURE, "boolValue": True} in nodes["report"]["onEnterActions"]
assert any(e["id"] == "complete_cinna_out_of_alignment" for e in events["events"])

npcs = json.loads(read("Resources/NPCs/npcs.json"))["npcs"]
trial = next(n for n in npcs if n["id"] == "cinna_cirra_trial")
orientation = next(n for n in npcs if n["id"] == "cinna_cirra_orientation")
assert orientation["hideWhenQuestCompleted"] == trial["showWhenQuestCompleted"] == QUEST
assert len(trial["dialogue"]) == 3 and not trial.get("dialogueIds")
assert {r["requiredQuestStatus"] for r in trial["dialogue"]} == {"NotStarted", "Active", "Completed"}
assert all(r["dialogueId"] == TRIAL and r["prerequisiteQuestId"] == QUEST and r["prerequisiteQuestStatus"] == "Completed" for r in trial["dialogue"])
catalog_trial = id_block(read("_Project/World/Areas/NpcCatalog.asset"), "cinna_cirra_trial")
# The trial Cinna stands in the ice trial (C03.IceTrial), spawned at runtime from the catalog, so campus bakes no copy.
assert trial["mapId"] == value(catalog_trial, "mapId") == TRIAL_MAP
assert not any("    id: cinna_cirra_trial\n" in d for d in documents)
for block in [catalog_trial]:
    assert value(block, "showWhenQuestCompleted") == QUEST
    assert value(block, "dialogueIds") == "[]"
    rules = re.findall(r"(?m)^    - dialogueId: (.*)$", block)
    assert rules == [TRIAL] * 3
    assert re.findall(r"(?m)^      prerequisiteQuestId: (.*)$", block) == [QUEST] * 3
    assert re.findall(r"(?m)^      prerequisiteQuestStatus: (.*)$", block) == ["Completed"] * 3
    assert set(re.findall(r"(?m)^      requiredQuestStatus: (.*)$", block)) == {"NotStarted", "Active", "Completed"}
print("validated the Out of Alignment relic, its trial stand, Cinna's report and departure, and the trial hand-off")
