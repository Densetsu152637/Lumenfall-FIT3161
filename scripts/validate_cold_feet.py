"""Validate Cold Feet's bounded provisional lane, lesson and authored copies without Unity."""
import json
import re
from pathlib import Path

TRIAL = "trial.cinna.ice"
QUEST = "cinna_cold_feet_warm_idea"

def validate_documents(campus, npcs, events, monitor, rows):
    errors = []
    trial = next((x for x in campus.get("objects", []) if x.get("id") == TRIAL), {})
    if (trial.get("x"), trial.get("y"), trial.get("width", 1), trial.get("height", 1)) != (20,34,1,1):
        errors.append("trial must start at (20,34) with a one-cell footprint")
    if trial.get("blocks", True) or "slide-lane:20,34,24,34" not in trial.get("tags", []):
        errors.append("trial must be nonblocking with the bounded rightward lane")
    lane_tags = [t for t in trial.get("tags", []) if t.startswith("slide-lane:")]
    if len(lane_tags) != 1:
        errors.append("trial needs exactly one authored slide lane")
    if any(len(rows) <= y or len(rows[y]) <= x or rows[y][x] in "WBT" for y in (34,35) for x in range(20,25)):
        errors.append("trial lane and walking return row must be walkable")
    map_target = next((x for x in campus.get("puzzleTargets", []) if x.get("id") == "quest.cinna.cold-feet-warm-idea"), {})
    definition = next((x for x in monitor.get("puzzles", []) if x.get("questId") == QUEST), {})
    target = next(iter(definition.get("targets", [])), {})
    required = dict(objectIds=[TRIAL], requiredFrozen=True, requiredVisitedTraps=4, requiredUninterruptedSlide=True, requiredPositionX=24, requiredPositionY=34)
    for name, actual in (("map",map_target),("monitor",target)):
        if any(actual.get(k) != v for k,v in required.items()): errors.append(name+" target must require genuine uninterrupted four-trap proof")
    if definition.get("mapId") != "campus" or definition.get("successDialogueId") != "cinna_cold_feet_success_dialogue":
        errors.append("monitor must stay campus-scoped with success feedback")
    lesson = next((x for x in events.get("dialogues", []) if x.get("id") == QUEST+"_dialogue"), {})
    metadata = {x["key"]:x["value"] for x in lesson.get("metadata", [])}
    expected = dict(spellReward="Glaciate", spellRewardQuestId=QUEST, spellRewardPrerequisiteQuestId="cinna_out_of_alignment", spellRewardNodeIds="accept,active")
    if any(metadata.get(k) != v for k,v in expected.items()): errors.append("lesson must guard terminal teaching, Active quest and completed prerequisite")
    npc = next((x for x in npcs.get("npcs", []) if x.get("id") == "cinna_cirra_trial"), {})
    rules = npc.get("dialogue", [])
    if len(rules) != 3 or {r.get("requiredQuestStatus") for r in rules} != {"NotStarted","Active","Completed"} or any(r.get("questId") != QUEST or r.get("dialogueId") != QUEST+"_dialogue" or r.get("prerequisiteQuestId") != "cinna_out_of_alignment" or r.get("prerequisiteQuestStatus") != "Completed" for r in rules):
        errors.append("trial NPC must offer exactly its three prerequisite-gated quest states")
    return errors

def validate(root):
    resources = root / "Lumenfall/Assets/Resources"
    load = lambda p: json.loads((resources/p).read_text(encoding="utf-8"))
    campus,npcs,events,monitor = [load(p) for p in ("Maps/campus.json","NPCs/npcs.json","GameEvents/quest_npc_dialogue.json","Quests/quest_puzzle_definitions.json")]
    rows = (resources/"Maps/campus.txt").read_text(encoding="utf-8").splitlines()
    errors = validate_documents(campus,npcs,events,monitor,rows)
    area = (root/"Lumenfall/Assets/_Project/World/Areas/campus.asset").read_text(encoding="utf-8")
    prefab = (root/"Lumenfall/Assets/_Project/World/Areas/Prefabs/campus.prefab").read_text(encoding="utf-8")
    catalog = (root/"Lumenfall/Assets/_Project/World/Areas/NpcCatalog.asset").read_text(encoding="utf-8")
    trial_asset = area.split("    - id: "+TRIAL+"\n",1)[1].split("    - id:",1)[0]
    trial_prefab = prefab.split("  objectId: "+TRIAL+"\n",1)[1].split("--- !u!",1)[0]
    for name, block in (("asset",trial_asset),("prefab",trial_prefab)):
        tags = re.findall(r"^\s+- (glaciate|slide-[^\n]+)$",block,re.M)
        if tags != next(x["tags"] for x in campus["objects"] if x["id"] == TRIAL): errors.append(name+" trial tags differ from source JSON")
    asset_target = area.split("    - id: quest.cinna.cold-feet-warm-idea\n",1)[1].split("    - id:",1)[0]
    for key, val in dict(requiredFrozen=1,requiredVisitedTraps=4,requiredUninterruptedSlide=1,requiredPositionX=24,requiredPositionY=34).items():
        if not re.search(r"^\s+"+key+r": "+str(val)+r"$",asset_target,re.M): errors.append("asset target differs: "+key)
    source = next(x["dialogue"] for x in npcs["npcs"] if x["id"] == "cinna_cirra_trial")
    for name, text in (("catalog",catalog),("prefab",prefab)):
        block = text.split("id: cinna_cirra_trial\n",1)[1]
        block = block.split("\n  - id:",1)[0] if name == "catalog" else block.split("\n--- !u!",1)[0]
        rules = []
        for part in block.split("    - dialogueId: ")[1:]:
            item = {"dialogueId":part.splitlines()[0]}
            item.update(dict(re.findall(r"^      (\w+): (.*)$",part,re.M)))
            if "weight" in item: item["weight"] = int(item["weight"])
            rules.append(item)
        if rules != source: errors.append(name+" trial NPC dialogue differs from source JSON")
    return errors

if __name__ == "__main__":
    errors = validate(Path(__file__).resolve().parents[1])
    for error in errors: print(error)
    print("Cold Feet authored scope "+("FAIL" if errors else "PASS"))
    raise SystemExit(bool(errors))
