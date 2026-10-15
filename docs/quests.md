# Quest outline

The opening quest set introduces the principal characters and teaches the player the interaction spells used by grid puzzles. Quest names are working titles; dialogue, object names, rewards, and final prerequisites remain to be written.

## Stella: First Light

Stella has read ahead in Magical Engineering Smart Systems (MEMG1013) and begins an assignment to recreate a relic that stores electrical energy and transmits it to other objects. Her research is not enough to make the prototype work, so she asks the player to diagnose the problem.

The player identifies a practical solution, gives Stella confidence to accept help, and uses Ampere to power the repaired relic. This quest introduces electrical powering and establishes Stella's interest in magical engineering.

## Stella: String Them Together

Stella asks the player to complete the next stage of her assignment by connecting special magic circuit relics. The task reveals her insecurity about electrical magic despite her ambition to become a magical engineer. The player learns Weave and completes a wire-routing puzzle inspired from puzzles found in gerudo desert in Legend of Zelda: Breath of the Wild.

## Cinna Cirra: Out of Alignment

Cinna Cirra registers for an orientation duty without realising that the activity is intended for more experienced students. They are assigned to align objects whose operation they have not yet learned. After overhearing an explanation of Vis, the player helps Cinna Cirra reposition the objects and completes the alignment puzzle. In the implemented version, an orientation relic beside Cinna Cirra on the C02 terrace belongs in the ice trial. The player learns Vis, lifts the relic, carries it through the trial portal and sets it on the empty pedestal inside, then reports back to Cinna. Cinna goes ahead and asks to meet at the trial; the scene fades to black and back with Cinna gone, and the player walks to the trial to find them there.

## Cinna Cirra: Cold Feet, Warm Idea

Cinna Cirra receives another orientation task: triggering a sequence of traps in the required order. They suggest freezing the ground and sliding through the area. The player learns Glaciate and uses its movement rules to solve the sequence puzzle. In the implemented version the trial is an ice-slide room: Glaciate freezes the relic the player delivered during Out of Alignment, which freezes the pool, and the player slides onto nine switches, around crystal pillars and snow patches and without sliding off the open edges, and then into a seal.

## Orio: Patch Notes

Orio is trying to optimise a mechanism but refuses to proceed until its sequence is perfect. He explains the puzzle through game and turn-order analogies, then teaches the player Rotara. The player uses trial and error to rotate the components into the correct configuration.

## Katrina: Something Borrowed

Katrina has lost an item with personal significance but minimises its importance and searches without telling the group. The player learns Lost Echoes, reveals relevant points of interest, and helps recover the item. The item's identity and its connection to Katrina's family are still to be decided.

## Optional activities

Potential side content includes restoring power to the dormitory grid and locating an item through a recovery quest. The scope, reset behaviour, and any replayable minigame are open production decisions.

## Runtime puzzle targets

The runtime target registry maps the Stella Weave endpoints `relic.stella.power` and `circuit.stella.link` to `B02.Ground` (W02); First Light uses five additional IDs generated and registered by its runtime board. These are distinct sets of objects: String Them Together uses the two stable map-authored endpoints, while First Light uses its five generated toggle relics. Cinna’s ice-trial switches are in `C03.IceTrial`, Katrina’s keepsake is in `C04.Recess`, and Orio’s rune dais is in `deepForest`, reached through the west forest portal `P.F01` beside his F04 glade. First Light requires all five generated Stella toggle relics to be powered; String Them Together requires `relic.stella.power` to be powered and connected to `circuit.stella.link`; Cold Feet, Warm Idea requires the nine `switch.cinna.ice.*` switches and the seal `switch.cinna.ice.final` to be powered; Patch Notes requires the three rune discs of the dais to point north at rotation 0, turned only through its four sockets, and completes through Orio’s report conversation rather than a monitor (see [`quest-orio-patch-notes.md`](implementation/quest-orio-patch-notes.md)); and Something Borrowed requires `keepsake.katrina.hidden` to be revealed at grid cell `(42,60)`. Out of Alignment has no monitor target: it completes when the player reports back after the relic is on its stand and Cinna leaves for the ice trial (`CinnaTrialDeparture`). The quest-puzzle monitor evaluates its target only while that quest is Active; First Light’s custom board also emits the shared completion event when all five generated relics are powered.

## Implementation trackers

Detailed implementation and completion assessments are maintained separately for each quest:

- [Stella: First Light](quest-implementation/stella-first-light.md)
- [Stella: String Them Together](quest-implementation/stella-string-them-together.md)
- [Cinna Cirra: Out of Alignment](quest-implementation/cinna-out-of-alignment.md)
- [Cinna Cirra: Cold Feet, Warm Idea](quest-implementation/cinna-cold-feet-warm-idea.md)
- [Orio: Patch Notes](quest-implementation/orio-patch-notes.md)
- [Katrina: Something Borrowed](quest-implementation/katrina-something-borrowed.md)

Legacy save compatibility: the retired `orioDaisRoom` and `C04.Recess` interiors remain loadable for old saves and provide an exit to the authored forest or campus. Their old entrances stay retired; current Orio progression uses the deep-forest dais and Katrina uses the three memory anchors.

Integration checks preserve terminal lesson rewards and explicit Stella hand-in. The circuit target requires one connected board network; individually lit, disconnected components cannot enable reporting. Orio's quest hint uses the deep-forest route, and taught Katrina search hints keep the recovered anchors awaiting her report.
