# NPC dialogue content contract

`quest_npc_dialogue.json` is an independent schema-version 1 GameEvents package. The runtime merges JSON files beneath `Assets/**/Resources/GameEvents`; it does not replace `sample_events.json`.

## Presentation

`DialogueOverlay` presents dialogue through a UI Toolkit panel (`DialogueView`). It is laid out against a 1920x1080 reference and scales with screen height. Styling lives in `Resources/Dialogue/DialogueBox.uss`, with the panel theme in `Resources/Dialogue/DialogueTheme.tss`.

- **Box and frames:** the box slides and fades in over a light dim of the world. Stella, Cinna Cirra, Orio and Katrina have their own accent colours and generated 3:1 frames from `Resources/Dialogue/{Stella,CinnaCirra,Orio,Katrina}`. Students, the Narrator and any other unframed speaker use the neutral `Random NPC` parchment frame. Text is inset from the illustrated border.
- **Speaker:** the name sits on a ribbon in the speaker's colour on the frame's top edge.
- **Portrait:** a live portrait of the speaking NPC sits over the frame's top-left corner. `DialoguePortrait` moves that NPC's renderers to layer 31 while it speaks and renders its face, located by its eye mesh, into a texture; the world cameras still draw it. The speaker is the nearby NPC whose display name matches the node speaker, otherwise the NPC being talked to. The Narrator has no portrait.
- **Pages:** `DialoguePaging` splits a node's text into pages of at most about 130 visible characters, roughly three lines. Pages break after sentences, or between words inside a very long sentence. Tags open across a break are closed and reopened, so effects carry over. Continuing shows the next page; choices appear only after the last page.
- **Box size:** the box keeps the frames' 3:1 shape and scales as a whole to 72%, 80%, 88% or 100%. It uses the smallest size whose text area fits the page and, on the last page, its choices, so short remarks get a smaller box. The text size never changes.
- **Reveal:** text reveals at a frame-rate-independent pace with a short punctuation pause. Choices appear once the last page is fully visible; the continue arrow shows whenever another page or line follows.
- **Voice blips:** `DialogueBlips` plays a soft generated tone every other letter, pitched per speaker. The Narrator is silent.
- **Camera:** while a conversation is open, `CameraFollow2_5D.FrameConversation` eases the camera in to frame the player and the NPC, and releases it when the dialogue ends.

**Input.** Talk (`E`, rebindable), Space, Return or the gamepad south button reveals the line, then continues. Clicking the box also reveals or continues. Choices are parchment rows: select them with up/down (`W`/`S`, the arrow keys or the D-pad) and confirm, or click a row. The number keys 1–9 also pick a choice directly, in list order. The opening interaction frame is ignored so `E` cannot skip the first line, and the movement lock stays active throughout.

**Player token.** `{player}` as a node speaker means the player character: the ribbon shows their name, or `???` before they have one, and no portrait is shown. In node and choice text it is replaced with the same name. See the [Player README](../Lumenfall/Assets/_Project/Systems/Player/README.md).

**Writer tags.** Standard rich-text tags (`<b>`, `<i>`, `<color>`) pass through, and `DialogueTextEffects` adds three more:

| Tag | Effect |
| --- | --- |
| `<wave>…</wave>` | Letters bob gently |
| `<shake>…</shake>` | Letters jitter |
| `<spell>…</spell>` | Spell styling (bold purple) |

The spell names Ampere, Weave, Vis, Glaciate, Rotara and Lost Echoes are styled automatically as whole words. The word before an exclamation mark gets a small wave.

**Font.** The dialogue box uses Fredoka, a rounded sans serif licensed under SIL Open Font License 1.1. Body text and choices use `Fredoka-Medium`; speaker names use `Fredoka-SemiBold`. Both are static weight-500 and weight-600 instances, at width 100, cut with fontTools from the variable `Fredoka[wdth,wght].ttf` in the official [Google Fonts repository](https://github.com/google/fonts/tree/main/ofl/fredoka). The variable font's default weight is Light. The licence is copied to `Resources/Dialogue/Fredoka-OFL.txt`. Nunito Regular, with its licence in `Resources/Dialogue/OFL.txt`, remains in use by the ice-trial and Lost Echoes guide overlays. Asset generation provenance is recorded in [`dialogue-art-provenance.md`](dialogue-art-provenance.md).

## Content

Principal dialogue IDs and placements are: `stella_first_light_dialogue` at W02, `stella_string_them_together_dialogue` at W02 (W03 remains optional), `cinna_out_of_alignment_dialogue` at C02, `cinna_cold_feet_warm_idea_dialogue` at C03, `orio_patch_notes_dialogue` at F04, and `katrina_something_borrowed_dialogue` at C04. Registry records should select each dialogue using its quest ID and `NotStarted`/`Active`/`Completed` status. Stella's second quest additionally requires `stella_first_light:Completed`; Cinna's second requires `cinna_out_of_alignment:Completed`.

Conversation choices only start quests. Puzzle systems must call `CompleteQuest` externally after a successful puzzle: `stella_first_light`, `stella_string_them_together`, `cinna_out_of_alignment`, `cinna_cold_feet_warm_idea`, `orio_patch_notes`, and `katrina_something_borrowed`. Two report choices finish quests: Orio's `report` choice triggers `complete_orio_patch_notes`, and Cinna's `report` choice (shown once `vis.delivered.relic.cinna.orientation` is set) sets `cinna.trial.departure`, which `CinnaTrialDeparture` turns into `complete_cinna_out_of_alignment` behind a fade while Cinna leaves for the ice trial. The package intentionally has no lifecycle event definitions; the runtime owner may bind completion events to post-completion dialogue if desired.

The dialogue keeps unresolved design details conceptual. It does not define Stella's exact relic fault, circuit topology, Cinna's object category or trap numbering, Orio's final mechanism configuration, or Katrina's keepsake identity/family history. Katrina's item is consistently described as a keepsake. The package does not include optional dormitory power or recovery side quests.

Ambient conversations should be added as separate weighted, quest-neutral dialogue records when anonymous campus/forest NPCs are registered. They should cover academy routines, spell practice, forest paths, dining/library life, orientation, and student game chatter without impersonating Stella, Cinna Cirra, Orio, or Katrina and without actions or quest state changes.

The active registry contains Stella, two Cinna Cirra records, Orio, Katrina, and two anonymous ambient students. The trial Cinna Cirra (`cinna_cirra_trial`) stands at `(8,1)` in the ice trial `C03.IceTrial`, entered from campus `(47,45)`; the trial room spawns them from their catalog record with the `Cinna Cirra.fbx` model, and they appear once Out of Alignment is complete. Katrina is placed on campus at `(29,70)`; her keepsake is hidden at `(12,9)` in the garden recess `C04.Recess`, entered from campus `(39,49)`. NPC layer files are parallel resources for `campus`, `westForest`, and `B02.Ground`; their symbols are `c`/`d`/`a`/`k`, `o`/`f`, and `s` respectively. The academy west gate is resolved with two active campus cells and four active forest footprint cells; three additional campus X cells remain explicitly reserved.

All six active registry records intentionally use the shared existing placeholder prefab `NPCs/Elara.prefab`. The display name, map position, layer symbol, and dialogue identity remain data-driven; individual character meshes can replace this asset per record later without changing quest content or placement data. Katrina already uses the `Katarina.fbx` model: the campus prefab instance has `ChibiCharacter_Controler` and her `DynamicNpc`, and her catalog record points to the same model.
