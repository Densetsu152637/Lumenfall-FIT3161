# Quest outline

The opening quest set introduces the principal characters and teaches the player the interaction spells used by grid puzzles. Quest names are working titles; dialogue, object names, rewards, and final prerequisites remain to be written.

## Stella: First Light

Stella has read ahead in Magical Engineering Smart Systems (MEMG1013) and begins an assignment to recreate a relic that stores electrical energy and transmits it to other objects. Her research is not enough to make the prototype work, so she asks the player to diagnose the problem.

The player identifies a practical solution, gives Stella confidence to accept help, and uses Ampere to power the repaired relic. This quest introduces electrical powering and establishes Stella's interest in magical engineering.

## Stella: String Them Together

Stella asks the player to complete the next stage of her assignment by connecting special magic circuit relics. The task reveals her insecurity about electrical magic despite her ambition to become a magical engineer. The player learns Weave and completes a wire-routing puzzle inspired from puzzles found in gerudo desert in Legend of Zelda: Breath of the Wild.

## Cinna Cirra: Out of Alignment

Cinna Cirra registers for an orientation duty without realising that the activity is intended for more experienced students. They are assigned to align objects whose operation they have not yet learned. After overhearing an explanation of Vis, the player helps Cinna Cirra reposition the objects and completes the alignment puzzle. The object type and exact alignment rules are open design decisions.

## Cinna Cirra: Cold Feet, Warm Idea

Cinna Cirra receives another orientation task: triggering a sequence of traps in the required order. They suggest freezing the ground and sliding through the area. The player learns Glaciate and uses its movement rules to solve the sequence puzzle.

## Orio: Patch Notes

Orio is trying to optimise a mechanism but refuses to proceed until its sequence is perfect. He explains the puzzle through game and turn-order analogies, then teaches the player Rotara. The player uses trial and error to rotate the components into the correct configuration.

## Katrina: Something Borrowed

Katrina has lost an item with personal significance but minimises its importance and searches without telling the group. The player learns Lost Echoes, reveals relevant points of interest, and helps recover the item. The item's identity and its connection to Katrina's family are still to be decided.

## Optional activities

Potential side content includes restoring power to the dormitory grid and locating an item through a recovery quest. The scope, reset behaviour, and any replayable minigame are open production decisions.

## Runtime puzzle targets

First Light uses five runtime toggle relics in `B02.Ground`; all five must be powered. After First Light, accepting String Them Together teaches Weave immediately while the new quest remains Active. Its five board relics take power only through a woven chain from `relic.stella.power`, using `circuit.stella.link` and the authored relays as needed. All five must be lit for completion.

Cinna's alignment target is on `campus`: `orientation.cinna.align` must be aligned. The ice trial requires `trial.cinna.ice` to be frozen at `(34,67)`. Katrina's keepsake is in `C04.Recess` at `(12,9)`; revealing it there grants the key item and starts the recovery conversation, and returning it to Katrina completes the quest.

Orio's `westForest` mechanism starts at 180 degrees. Finish his Rotara lesson, cast three clockwise quarter turns to reach 90 degrees, then report to Orio. Completion requires the learned spell and the exact live mechanism; the automatic quest monitor deliberately does not complete this quest. Orio teaches Rotara only, and Gravitate stays locked. The quest hint directs the player to the west forest, the unfinished lesson, the required turns, or the final confirmation according to live state.

## Implementation trackers

Detailed implementation and completion assessments are maintained separately for each quest:

- [Stella: First Light](quest-implementation/stella-first-light.md)
- [Stella: String Them Together](quest-implementation/stella-string-them-together.md)
- [Cinna Cirra: Out of Alignment](quest-implementation/cinna-out-of-alignment.md)
- [Cinna Cirra: Cold Feet, Warm Idea](quest-implementation/cinna-cold-feet-warm-idea.md)
- [Orio: Patch Notes](quest-implementation/orio-patch-notes.md)
- [Katrina: Something Borrowed](quest-implementation/katrina-something-borrowed.md)
