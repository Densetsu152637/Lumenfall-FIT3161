"""Validate the authored quest and ambient dialogue package."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "Lumenfall/Assets/Resources/GameEvents/quest_npc_dialogue.json"
NPC_DATA = ROOT / "Lumenfall/Assets/Resources/NPCs/npcs.json"
doc = json.loads(DATA.read_text(encoding="utf-8"))
npcs = json.loads(NPC_DATA.read_text(encoding="utf-8"))["npcs"]
assert doc["schemaVersion"] == 1
quests = {q["id"]: q for q in doc["quests"]}
dialogues = {d["id"]: d for d in doc["dialogues"]}
events = {e["id"]: e for e in doc["events"]}
assert len(quests) == 6 and len(dialogues) >= 15 and len(events) == 10
assert len(quests) == len(doc["quests"]) and len(dialogues) == len(doc["dialogues"])

for qid in quests:
    d = dialogues.get(qid + "_dialogue")
    assert d and d["startNodeId"] in {n["id"] for n in d["nodes"]}
    nodes = {n["id"]: n for n in d["nodes"]}
    start = nodes[d["startNodeId"]]
    choices = {c["id"]: c for c in start.get("choices", [])}
    assert {"accept", "active", "done"} <= choices.keys()
    assert any(a.get("type") == "StartQuest" and a.get("key") == qid for a in choices["accept"].get("actions", []))
    assert any(c.get("conditions", [{}])[0].get("value") == "Active" for c in [choices["active"]])
    assert any(c.get("conditions", [{}])[0].get("value") == "Completed" for c in [choices["done"]])
    ev = events["complete_" + qid]
    expected = [{"type": "QuestStatus", "key": qid, "value": "Active"}]
    if qid == "orio_patch_notes":
        expected.append({"type": "OrioMechanismAligned", "key": "mechanism.orio.rotate"})
    assert ev["conditions"] == expected
    assert ev["actions"] == [{"type": "CompleteQuest", "key": qid}]

# Katrina's keepsake is carried back: the reveal only marks it found, and returning it completes the quest.
found = events["found_katrina_keepsake"]
assert found["conditions"] == [{"type": "QuestStatus", "key": "katrina_something_borrowed", "value": "Active"}]
assert found["actions"] == [{"type": "SetFlag", "key": "inventory.keyitem.katrina-keepsake", "boolValue": True}]
katrina_choices = {c["id"]: c for c in dialogues["katrina_something_borrowed_dialogue"]["nodes"][0]["choices"]}
assert katrina_choices["report"]["actions"] == [{"type": "TriggerEvent", "key": "complete_katrina_something_borrowed"}]

assert dialogues["stella_string_them_together_dialogue"]["nodes"][0]["choices"][0]["conditions"][0] == {"type": "QuestStatus", "key": "stella_first_light", "value": "Completed"}
assert dialogues["cinna_cold_feet_warm_idea_dialogue"]["nodes"][0]["choices"][0]["conditions"][0] == {"type": "QuestStatus", "key": "cinna_out_of_alignment", "value": "Completed"}

for d in dialogues.values():
    if d.get("metadata", [{}])[0].get("key") == "ambient":
        for n in d["nodes"]:
            for c in n.get("choices", []):
                assert not c.get("actions"), d["id"]
            assert not n.get("onEnterActions"), d["id"]

registry = {n["id"]: n for n in npcs}
assert {"stella", "cinna_cirra_orientation", "cinna_cirra_trial", "orio", "campus_student", "forest_student"} <= registry.keys()
for nid in ("stella", "cinna_cirra_orientation", "cinna_cirra_trial", "orio"):
    entries = registry[nid]["dialogue"]
    assert len(entries) == (3 if nid in {"orio", "cinna_cirra_trial"} else 6)
    for entry in entries:
        assert entry["dialogueId"] in dialogues and entry["requiredQuestStatus"] in {"NotStarted", "Active", "Completed"}
for nid, prefix in (("campus_student", "ambient_courtyard_student"), ("forest_student", "ambient_forest_student")):
    entries = registry[nid]["dialogue"]
    assert [e["dialogueId"] for e in entries] == [prefix, prefix + "_2", prefix + "_3"]
    assert {e["weight"] for e in entries} == {1}
assert len(registry) >= 5

# Every authored layer marker must resolve to the record's declared symbol and a non-blocking source cell.
for npc in npcs:
    if not npc.get("layerSymbol"):
        continue
    layer = next(x for x in json.loads(NPC_DATA.read_text(encoding="utf-8"))["layers"] if x["mapId"] == npc["mapId"])
    rows = (ROOT / "Lumenfall/Assets/Resources/NPCs" / layer["tileFile"]).read_text(encoding="utf-8").splitlines()
    matches = [(x, y) for y, row in enumerate(rows) for x, glyph in enumerate(row) if glyph == npc["layerSymbol"]]
    assert matches, f"{npc['id']} layer symbol is absent"
    marker_x, marker_y = matches[0]
    map_rows = ROOT / "Lumenfall/Assets/Resources/Maps" / (layer["mapId"] + ".txt")
    source = map_rows.read_text(encoding="utf-8").splitlines()
    assert source[marker_y][marker_x] not in "WBT", f"{npc['id']} is on a blocking source cell"
print(f"validated {len(quests)} quests, {len(dialogues)} dialogues, {len(events)} events")
