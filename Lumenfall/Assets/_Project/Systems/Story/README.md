# Story

Scripted story scenes: what starts them, and any characters that only exist for them. The scenes are cutscenes (see [the Cutscenes README](../Cutscenes/README.md)). Their data lives in this folder's `Resources/Cutscenes` and `Resources/GameEvents`.

## Late to orientation (forest to campus)

`LateToOrientationScene` runs the walk from the forest to the academy after the opening.

- **Kepler waits at the gate.** Until the scene has played, Prof. Kepler stands in `westForest` at `(19,46)`, beside the gate to campus (portal `P.F00` at `(17,48)`). Kepler is the `prof_kepler` record in `NpcCatalog.asset`, with the `Teacher_01` prefab and no dialogue of their own. They are not baked into the area prefab. The scene spawns them through `StoryNpc` (see below).
- **Trigger.** The first time the player stands on row y 22 of the forest or beyond it (around `(18,22)`), with no cutscene, dialogue or menu open, the cutscene `late_to_orientation` plays. The step onto the row counts as reaching it, so the scene starts at once rather than when the player stops. Every cutscene locks movement the moment it starts, so the player halts on that cell. It is play-once and unskippable. The gate is further on, so the row cannot be walked around.
- **The scene:**
  1. Kepler shouts from the gate (speaker "Distant voice", since Kepler is far off and the two have not met): orientation started ages ago, and the player should come over quickly. The player's inner voice wonders whether the afterlife has an orientation, and decides it is probably best to do what they say.
  2. The game walks the player to two cells in front of Kepler (`(19,44)`).
  3. Kepler says they are already late to orientation, introduces themselves and asks for the player's name. The name prompt opens: type a name, or leave it blank for Sol (see [the Player README](../Player/README.md)).
  4. Kepler marks them down by name ("{player}. There, you're on the list.") and tells them to get in quickly. The inner voice agrees: through the gate it is.
  5. The flag `story.orientation.kepler_met` is set. The game walks the player onto the gate. The screen fades to black, they go through to campus (arriving at `P.C00`), and it fades back in. Control returns on campus.
- **Afterwards.** Once the flag is set, this Kepler is removed as soon as the cutscene ends and is never spawned again. Kepler is meant to return elsewhere later with their own placement.
- **Saves.** Autosave waits during the scene, and the flag is saved with the GameEvents state. A save made in the forest beyond row 22 before this scene existed plays it as soon as it loads. Kepler and their blocker are removed while the startup menu is open, because saves load then, and a blocker on a saved player cell made the world provider reject the whole save. If a save leaves the player on Kepler's cell, Kepler appears once the player steps off it.

The shout has no portrait, because Kepler is far away when it plays. Once the player reaches Kepler and they introduce themselves, the speaker is "Prof. Kepler". That matches the catalog name, so the dialogue box shows Kepler's portrait.

## Lay of the land (arriving on campus)

`LayOfTheLandScene` runs the arrival on campus, straight after Kepler's scene.

- **Kit.** Kit is the student at the campus entrance: `campus_student`, renamed from "Campus student" in the catalog, the campus prefab, `npcs.json` and the journal. Kit stands at `(24,13)` in the campus prefab; the catalog's `(32,21)` is out of date, so the scene uses Kit's live position.
- **The call.** On campus, once `story.orientation.kepler_met` is set and `story.orientation.kit_called` is not, with no cutscene, dialogue or menu open, the cutscene `kit_call` plays. In practice that is the moment the player comes through the forest gate. Kit calls the player over, the player's inner voice notes that at least this stranger is smiling, and the flag `story.orientation.kit_called` is set. Control returns, and the player walks over themselves. Nothing walks them.
- **The tour.** Once the call has played, the first time the player talks to Kit (the talk key, `E` by default, within Kit's usual talk range), the cutscene `kit_lay_of_the_land` plays instead of Kit's usual conversation. The scene runs before NPC input, so it takes that press first. After the tour, talking to Kit opens Kit's usual choice-based courtyard conversation again. Both cutscenes are play-once and unskippable.
- **The tour's content:**
  1. Kit introduces themselves and gives the lay of the land: the courtyard with its fountain in the middle (the place to head back to if lost), the Main Academy Building (reception, the main portal, the staff), the Arcane Workshop (relics and machines), the Dormitory (student rooms and common spaces), the Library, building signs by every door, and the forest back through the gate. The directions are landmarks rather than left and right, because the camera's diagonal makes screen directions differ from the map, and older map data disagree about which side each building is on.
  2. Kit says the player missed the orientation ceremony, and that the headmistress, Professor Bann, is still by the fountain: go and speak to her right away. The flag `story.orientation.kit_met` is set and control returns. There is no hint card or marker.
- **Prof. Bann** is the `prof_bann` record (`Teacher_03`, campus `(26,42)`, by the fountain). She is spawned through `StoryNpc` from the moment `story.orientation.kepler_met` is set, and the player talks to her with E.
  - Her conversation `bann_orientation`: the player has missed orientation entirely, and she will organise a student guide to go over it with them tomorrow. For now they should report to their dorm and get their room sorted. Today is for exploring and speaking to fellow students, so she suggests the room first, then whatever they please. The player's inner voice decides that, for the afterlife, this isn't half bad. Her last line sets `story.orientation.bann_met`. She does not say where the dorm is; there is no hint card or marker.
  - Talking to her again gives the short `bann_reminder` ("Your dorm first, {player}…") instead. Her first node triggers the repeatable event `bann_remind_if_met`, which swaps in the reminder once the flag is set.
- **Kit's everyday conversation** (`ambient_courtyard_student`, after the tour): "Hey, {player}! Settling in all right?", with two choices. "Can you remind me of the campus buildings and areas?" repeats the landmark tour: the fountain, the Main Academy Building, the Arcane Workshop, the Dormitory and Library, the forest back through the gate, and the building signs. "Bye!" gets "See you around!". Kit's two one-line courtyard chats (`_2`, `_3`) are unchanged, apart from the speaker now being Kit.

## Ambient cast

`AmbientCast` keeps background students and staff on the maps, so the academy feels lived in until the dorm and later story content exist. Each is an NPC catalog record, spawned through `StoryNpc` whenever the player is on their map, with a short placeholder conversation in `Resources/GameEvents/ambient_cast.json` to be rewritten later.

| Character | Catalog id | Prefab | Map and cell |
| --- | --- | --- | --- |
| Prof. Voltaire | `prof_voltaire` | `Teacher_02` | Library, `B04.Ground` `(7,17)` |
| Vex | `vex` | `Student_04` | Library, `B04.Ground` `(2,11)` |
| Luna | `luna` | `Student_01` | Campus, `(22,77)` |
| Juno | `juno` | `Student_03` | Campus, `(43,42)` |
| Moss | `moss` | `Student_05` | West forest, `(25,32)` |

Cells were set by hand on 2026-10-14. Odette (`Student_06`) is kept back for later. The older baked forest student (`forest_student`, at around `(16,39)` in the West Forest prefab) is now called Grace, in the catalog, the prefab, `npcs.json`, the journal and their three forest conversations. Moss stands beyond the forest's gate-scene trigger row, so the player meets Moss on a return visit. Each cast member has a journal entry.

### Dormitory

The dormitory's people are placed in `Dormitory.prefab` (`B01.Ground`) rather than spawned. Each is their catalog record's model with a `DynamicNpc` holding the record, plus a one-cell blocker. The room is edited by hand: to move someone, move them in the prefab and change their cell in `NpcCatalog.asset` to match (`AmbientCastTests` checks the two agree).

| Character | Catalog id | Prefab | Cell |
| --- | --- | --- | --- |
| Marigold | `marigold` | `Receptionist_01` | Reception, behind the counter, `(8,8)` |
| Thistle | `thistle` | `Receptionist_02` | Reception, behind the counter, `(12,8)` |
| Rowan | `rowan` | `Student_02` | Lobby, in front of the counter, `(12,6)` |

Marigold and Thistle staff the reception, ready for the story's key scene. Their lines (`Resources/GameEvents/dormitory.json`) are placeholders marked as such, until that scene is written. Rowan's lines (`ambient_cast.json`) were moved from the old Training Centre to the dormitory lobby, which `B01.Ground` now is.

## Meeting people

`NpcIntroductions` keeps track of whom the player has met. Until someone has introduced themselves, the dialogue name ribbon and the journal show them as `???`. Their frame, colour and portrait still show.

- **First conversation.** The first time the player talks to an NPC, the NPC plays their introduction before anything else. The introduction is `intro_<person>` in `Resources/GameEvents/introductions.json`. They give their name, ask the player's, and the player answers through a choice, "I'm {player}.". They then go straight on to the conversation they would otherwise have had, such as a quest offer, without the dialogue box closing.
- **The name shows from the line where they give it.** That line's `SetFlag` sets `npc.met.<person>`, which is saved with the game. Later conversations skip the introduction and use the player's name as usual.
- **People with several records** count as one person. The three Cinna records are all `cinna_cirra`, and the speaker name "Cinna Cirra" is matched to that person too.
- **Scripted first meetings** set the flag in their own dialogue. Prof. Kepler is `???` while calling the player over, and is named on "I'm Professor Kepler" (`kepler_late`/`late`); the gate scene's name prompt is how the player first gives their name. Kit is `???` while shouting (`kit_call`). The tour (`kit_lay_of_the_land`) now opens with Kit's name and a question for the player's, before the lay of the land. Saves made before this existed count Kepler and Kit as met through the scenes' own flags.
- **Prof. Bann, the headmistress,** is known to everyone from the start, and needs no introduction.
- **General conversation once met.** After the first meeting, talking to someone opens their `chat_<person>` dialogue (`Resources/GameEvents/conversations.json`). They greet the player by name and offer "How's your day going?", "Tell me about yourself.", one topic of their own and "Bye!". Each answer returns to "Anything else?" with the same topics, until the player says goodbye. Their quest dialogue still comes first when it is what they need to say: a quest to offer (its prerequisite met), a task done but not yet handed in (its "report" choice is available), or a quest finished whose thanks the player has not heard yet (`npc.quest-thanked.<quest>`, set once any of their quest conversations ends with that quest complete). While a quest is simply under way, their chat adds "About what you asked me to do...", which hands over to the quest dialogue for a reminder. Kit keeps their own chat (`ambient_courtyard_student`). Prof. Bann keeps her story lines.
- **An NPC with no introduction** counts as met from their first conversation, so a new NPC is never stuck as `???`.
- **An unnamed player** (only possible on saves from before the name system) is asked for their name by the `ask_player_name` cutscene before an introduction.

The introductions' and conversations' lines are placeholders, to be rewritten.

## Settling in (end of Chapter 1)

`SettlingInScene` runs the rest of Chapter 1, from the dormitory to the end of the chapter.

1. **Room not ready.** Once Prof. Bann has spoken to the player (`story.orientation.bann_met`), talking to Marigold at the dorm reception plays `dorm_room_not_ready` instead of Marigold's usual chat, after Marigold's introduction if this is their first meeting. The player's room isn't ready, so the player should get familiar with the campus and meet the other students. It sets `story.dorm.room_not_ready`. Until then Bann keeps her orientation lines and her reminder about the dorm; from then on she chats like anyone else (`chat_prof_bann`).
2. **Exploring.** The player explores and solves the puzzles. Nothing guides them. The first time the player talks to Rowan or Thistle, a short scene plays at the desk first (`dorm_no_rooms`, setting `story.dorm.no_rooms_scene`): Rowan pleads for a room, Thistle says tough luck, and Marigold asks Thistle not to laugh. Then whoever the player spoke to carries on as usual, introduction included. The dorm's room doors all say the devs are sleeping and to visit later. Once the room-ready thought has played (step 3), the joke pays off and stays that way: Rowan never got a room, and has moved onto the lounge floor at `(27,7)` (moved in code each time the dorm is placed, with his obstacle), where he says so (`dorm_rowan_homeless`). Talking to Thistle then gives only the player's thought that he is about to burst into tears of laughter, and Marigold telling him not to laugh (`dorm_thistle_laughing`). The desk scene no longer plays by then.
3. **The room should be ready.** Once every Chapter 1 puzzle is solved (the six quests in `SettlingInScene.ChapterQuestIds`) and nothing else is on screen, the cutscene `room_ready_thought` plays the player's inner voice: their dorm room should be ready by now. It sets `story.dorm.room_ready_thought`. The player then walks back themselves.
4. **The key.** Talking to Marigold then plays `chapter1_room_ready`. Marigold hands over the key (`dorm_room_ready`, setting `story.dorm.key_received`), the player is walked to the third room door at `(17,17)`, the screen fades to black and "End of Chapter 1" appears, `story.chapter1.complete` is set, and the screen fades back on the player at their door. The game then saves and returns to the main menu.
5. **Afterwards.** Loading that save puts the player back at their door, with no further story. Marigold's chat offers "Thanks for the room key!" in place of "When will my room be ready?".

The story lines (`Resources/GameEvents/settling_in.json`) are placeholders, to be rewritten.

## Campus doors that go nowhere

Three blocked campus portals answer with a joke instead of "The door is locked". Each one's text lives on its Portal
Content asset in `World/Areas/Portals/CampusPortals`.

| Portal | Where | Notice |
| --- | --- | --- |
| `P.C03` | The Main Academy Building's door | "Unfortunately, we ran out of cruxite" |
| `P.C06` | The door in the west wall, at `(2,45)` | "Ngl, I forgot about this door lmaooo" |
| `P.C07` | The north gate, at `(26,88)` | "A dog obstructs the way." |

The north gate also has `AnnoyingDogGate`. The first time the player tries it in a save, a fake "Loading next area..."
bar creeps through nonsense file names until `annoying_dog.exe` loads and the bar drops back to zero. Then the notice
shows. The flag `joke.annoying_dog.seen` is saved as soon as it starts, so after that the gate only shows the notice.

## Hidden curios

`MiscCollectibles` hides ten curios around the maps for the player to find. Each is a hidden pickup: Lost Echoes reveals it with a gold glow, and the talk key picks it up from up to one cell away (it can also be picked up blind). Picking one up opens its found dialogue, "Found a <name>." (or "an") then its description, with no speaker name on either line, and adds it to the journal's **Miscellaneous** list, between key items and spells. There it shows by name, and clicking the name shows or hides the description.

| Curio | Map | Cell |
| --- | --- | --- |
| Worn Bandana | Dormitory (`B01.Ground`) | `(25,6)` |
| Straw Hat | Dormitory (`B01.Ground`) | `(4,16)` |
| Bouncy Plush | Dormitory (`B01.Ground`) | `(3,4)` |
| Rubber Ducky | Ice trial (`C03.IceTrial`) | `(5,6)` |
| Ticking Pocket Watch | Arcane Workshop (`B02.Ground`) | `(23,20)` |
| Picnic Basket | Campus | `(38,42)` |
| Tea Set for Three | Campus | `(17,74)` |
| Piece of Parchment | Library (`B04.Ground`) | `(2,5)` |
| Good Witch Novel | Library (`B04.Ground`) | `(0,14)` |
| Faded Polaroid | West forest | `(24,6)` |

- **Text:** each curio's name and description live in its `found_misc_<key>` dialogue in `Resources/GameEvents/misc_items.json` (name in the `itemName` metadata, description in the `description` node). The journal reads them from there.
- **Pickup:** the matching `found_misc_<key>` event sets `inventory.misc.<key>` and opens the dialogue.
- **To add one:** add its map and cell to `MiscCollectibles.Items`, and its dialogue and event to `misc_items.json`. `MiscCollectiblesTests` checks the two match.
- **Gifts:** four more curios are handed over by the secrets below rather than hidden (`MiscCollectibles.SecretRewards`). They use the same dialogue and event, triggered from a choice in the giver's dialogue, and the journal lists them after the hidden ones.

## Secrets

Deltarune-style secrets: odd actions, hinted at in dialogue, that lead somewhere pointless and charming. Each step is a saved flag (`secret.*`), so a secret half done stays half done. Three of them open a small room built in code by `SecretRooms`, like Orio's legacy hall: the loader resolves it by map id, so a save made inside restores. Each room has one person to talk to, whose first conversation hands over a curio, and a glowing ring by the door that takes the player back to where they came from. After that, their `chat_<id>` offers the curio again until it has been taken. Their dialogue is in `Resources/GameEvents/secrets.json`; the furniture and the janitor's model are referenced by `Resources/SecretRoomArt.asset`. Each room can be entered once: `SecretRooms.Enter` sets `secret.visited.<room>`, and after that its way in answers like a locked door (the doors are only doors, the dog is back in the way, the STAFF ONLY door is locked), so a curio not taken on that visit is gone.

| Secret | How | Hint | Leads to | Gift |
| --- | --- | --- | --- | --- |
| The devs' room (`DormKnockSecret`) | Check dorm room doors 3, 1 and 4 in a row (pi). Door 4 answers, and closing its card goes in. | Stella's dormmate line: someone keeps "knocking out pi" at night. | `secret.devRoom`: three devs at their desks, styled after the team (`DevChibis`: background NPC models recoloured, with a beard, curls, collar or trim added on the head and neck bones). Nick snores at his; the other two are working, and all three can be talked to. Back to the hallway at `(23,17)`. | Cold Cup of Coffee |
| The Lantern Bearer (`LanternBearerSecret`) | From `(19,9)`, four tiles up and two left of where the player wakes in the forest, walk up, up, right, up, right, right, then stand still for 10 seconds of play. Each right step pings a note higher; a wrong one thuds and the walk starts over. The tones are built in code until clips are placed in `Resources/Audio/LanternBearer` (`Ping1` to `Ping6`, `Wrong`). | Luna traces the constellation; Moss says the lanterns hum loudest a few steps up the path from where you woke, just to the left, and to stand very still; the pings confirm the way. | A lantern spirit appears beside the player, and stays until it has given its gift. | Slightly Warm Pebble |
| The dog (`AnnoyingDogGate`) | After the north gate's loading joke, try the gate 10 more times: the dog becomes aware. Cast Vis within two cells of the gate, then Rotara. The next try goes through. | The notices change as the player persists (an ear twitches, it stirs, it looks light enough to lift, if only it would turn aside). | `secret.dogRoom`: a white room, the dog asleep on a pile of scrolls. Back to `(26,87)`. | annoying_dog.exe (corrupted) |
| The staff closet (`StaffClosetSecret`) | In any order: Glaciate by the campus fountain, Rotara by the round table at `(17,74)`, beside the garden pavilion, Lost Echoes near Prof. Voltaire's desk in the Library. Then try the main hall door. | Juno's "Got any gossip?" gives one hint each time she is asked, in turn (fountain, table, Library, then round again; counter `juno.gossip`); Voltaire's crackling shelf. | `secret.staffCloset`: the janitor (who also works on the game). Back to `(18,44)`. | Mop for Your Troubles |

- **Spells:** the spell secrets listen to `SpellCaster.CastAttempted`, raised for every cast from input before the spell looks for a target. A handler that answers it sets `Handled`, and the spell does nothing else (Lost Echoes still searches).
- **Doors:** the knock listens to `BuildingSignInteraction.Checked` and `Closed`, and swaps door 4's text for that visit with `ShowInstead`. The staff closet adds itself to the main hall door (`P.C03`) when the campus loads, as an `IBlockedPortalHandler`.
- **Out of order (`SequenceBreakEnding`):** solving any Chapter 1 puzzle before Marigold has said the room is not ready breaks the story. Once the puzzle's wrap-up is over, a glitching voice notices (`secret_sequence_break`), then the screen cuts to black, all sound pauses, and a voice that speaks in hands (a W. D. Gaster nod) types out that the story cannot continue. The run is then reset as New Game does, saved over the autosave and its linked manual slot, and the game returns to the main menu. Loading that file plays the New Game opening again (`secret.sequence_break.opening_pending`). The hand lines use symbols from the system's fallback fonts.

## Story-only characters

`StoryNpc` spawns a character from their NPC catalog record (prefab, map and cell) instead of baking them into an area prefab, in the way the ice trial spawns its Cinna. Each frame, the owning scene says whether the story wants them present.

- They appear on their map as a child of its area object, facing south, with a `DynamicNpc` and a one-cell `GridObstacle`. They switch off with the area, and are removed when they leave their map or are no longer wanted.
- They are removed while the startup menu is open, because saves load then, and a blocker on a saved player cell made the world provider reject the save.
- If the player stands on their cell, they wait until the player steps off.
- If their authored cell is blocked (a tree, a wall, the fountain) or is a doorway, they stand on the nearest free cell within four cells instead. The interior and forest layouts only exist in the area prefabs, so authored cells cannot all be checked ahead of time.

## Verification

`LateToOrientationTests`, `LayOfTheLandTests` and `AmbientCastTests` (EditMode) check the cast's prefabs, maps and conversations, Kit's and Bann's catalog records, when Kit's scene plays, its step order and its conversations. `LateToOrientationTests` also check the trigger row, Kepler's catalog record, the scene's step order (call, walk beside Kepler, ask for a name, mark down, flag, walk onto the gate, enter it) and the three conversations. They compile but have not yet been run in Unity.

`SecretsTests` (EditMode) check the knock sequence, the constellation's shape against Luna's line, the dog's steps, the staff closet's three locks, when a puzzle breaks the story's order, each secret room's arrival, exit and return map, and that every secret gift is a curio offered again only until it is taken. They compile but have not yet been run in Unity, and the rooms' furniture placement, the black screen and the reset-to-opening have not been played.

Manual validation (not yet executed): check that the shout plays at row 22 and the walk finds a way through the real forest. A walk that is blocked for three seconds, or has no path, places the player at its destination instead. Also check that Kepler's model, facing and portrait look right; that the name prompt works with the keyboard; that the player arrives on campus; and that Kepler is gone on returning to the forest.
