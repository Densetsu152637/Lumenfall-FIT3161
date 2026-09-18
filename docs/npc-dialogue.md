# NPC dialogue content contract

`quest_npc_dialogue.json` is an independent schema-version 1 GameEvents package. The runtime merges JSON files beneath `Assets/**/Resources/GameEvents`; it does not replace `sample_events.json`.

Principal dialogue IDs and placements are: `stella_first_light_dialogue` at W02, `stella_string_them_together_dialogue` at W02 (W03 remains optional), `cinna_out_of_alignment_dialogue` at C02, `cinna_cold_feet_warm_idea_dialogue` at C03, `orio_patch_notes_dialogue` at F04, and `katrina_something_borrowed_dialogue` at C04. Registry records should select each dialogue using its quest ID and `NotStarted`/`Active`/`Completed` status. Stella's second quest additionally requires `stella_first_light:Completed`; Cinna's second requires `cinna_out_of_alignment:Completed`.

Conversation choices only start quests. Puzzle systems must call `CompleteQuest` externally after a successful puzzle: `stella_first_light`, `stella_string_them_together`, `cinna_out_of_alignment`, `cinna_cold_feet_warm_idea`, `orio_patch_notes`, and `katrina_something_borrowed`. No dialogue choice completes a quest. The package intentionally has no lifecycle event definitions; the runtime owner may bind completion events to post-completion dialogue if desired.

The dialogue keeps unresolved design details conceptual. It does not define Stella's exact relic fault, circuit topology, Cinna's object category or trap numbering, Orio's final mechanism configuration, or Katrina's keepsake identity/family history. Katrina's item is consistently described as a keepsake. The package does not include optional dormitory power or recovery side quests.

Ambient conversations should be added as separate weighted, quest-neutral dialogue records when anonymous campus/forest NPCs are registered. They should cover academy routines, spell practice, forest paths, dining/library life, orientation, and student game chatter without impersonating Stella, Cinna Cirra, Orio, or Katrina and without actions or quest state changes.

The active registry contains Stella, two Cinna Cirra records, Orio, Katrina, and two anonymous ambient students. Katrina is placed at campus `(23,40)` in C04, with the keepsake alcove at `(30,42)`. NPC layer files are parallel resources for `campus`, `eastForest`, and `B02.Ground`; their symbols are `c`/`d`/`a`/`k`, `o`/`f`, and `s` respectively. The academy east gate is resolved with two active campus cells and four active forest footprint cells; three additional campus X cells remain explicitly reserved.

All six active registry records intentionally use the shared existing placeholder prefab `NPCs/Elara.prefab`. The display name, map position, layer symbol, and dialogue identity remain data-driven; individual character meshes can replace this asset per record later without changing quest content or placement data.
