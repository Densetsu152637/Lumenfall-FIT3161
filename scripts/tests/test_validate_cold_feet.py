import json
import sys
import unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from validate_cold_feet import validate, validate_documents

class ColdFeetContentValidationTests(unittest.TestCase):
    def setUp(self):
        self.root=Path(__file__).resolve().parents[2]
        resources=self.root/"Lumenfall/Assets/Resources"
        self.documents=[json.loads((resources/p).read_text(encoding="utf-8")) for p in ("Maps/campus.json","NPCs/npcs.json","GameEvents/quest_npc_dialogue.json","Quests/quest_puzzle_definitions.json")]
        self.rows=(resources/"Maps/campus.txt").read_text(encoding="utf-8").splitlines()
    def errors(self): return validate_documents(*self.documents,self.rows)
    def test_authored_json_and_baked_copies_match(self): self.assertEqual(validate(self.root),[])
    def test_frozen_goal_without_sequence_is_rejected(self):
        target=next(x for x in self.documents[3]["puzzles"] if x["questId"]=="cinna_cold_feet_warm_idea")["targets"][0]
        target.pop("requiredVisitedTraps"); self.assertTrue(self.errors())
    def test_blocking_prop_is_rejected(self):
        next(x for x in self.documents[0]["objects"] if x["id"]=="trial.cinna.ice")["blocks"]=True; self.assertTrue(self.errors())
    def test_wrong_lane_is_rejected(self):
        next(x for x in self.documents[0]["objects"] if x["id"]=="trial.cinna.ice")["tags"][2]="slide-lane:20,34,24,35"; self.assertTrue(self.errors())
    def test_missing_lesson_guard_is_rejected(self):
        lesson=next(x for x in self.documents[2]["dialogues"] if x["id"]=="cinna_cold_feet_warm_idea_dialogue")
        lesson["metadata"]=[x for x in lesson["metadata"] if x["key"]!="spellRewardQuestId"]; self.assertTrue(self.errors())
    def test_old_completed_alignment_handoff_is_rejected(self):
        npc=next(x for x in self.documents[1]["npcs"] if x["id"]=="cinna_cirra_trial")
        npc["dialogue"].append(dict(dialogueId="cinna_out_of_alignment_dialogue",questId="cinna_out_of_alignment",requiredQuestStatus="Completed",weight=5)); self.assertTrue(self.errors())
    def test_blocked_return_cell_is_rejected(self):
        row=list(self.rows[35]); row[22]="W"; self.rows[35]="".join(row); self.assertTrue(self.errors())
