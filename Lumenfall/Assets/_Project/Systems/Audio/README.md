# Area audio

`AreaAudioController` lives beside `DataDrivenWorldLoader` on SampleScene's World Content
object. Each `WorldAreaAsset.audioProfile` selects an `AreaAudioProfile` containing optional
music and ambience loops, independent gains, optional mixer groups, and a fade duration.
The shipped forest, campus, and four interiors have ambience assigned; music is optional
and can be added to these same profiles in the Inspector.

The loader publishes `AreaChanged` after an authored area is activated and its spawn is
applied, and publishes null when cleared. Portal travel, new-game reset, direct loads,
failed-travel rollback, and save restoration therefore all update the audio through the
same path. Audio uses the current **World Area asset** (the whole loaded map), rather than
the rectangular regions within that map. Audio state is derived from the world and does
not need another save provider or save schema change.

Music and ambience each use two non-spatial, looping AudioSources for crossfades. Reloading
an area or switching between profiles with the same clip, mixer route, and gain preserves
playback position. Interrupted transitions fade from current gains; introducing a third
clip replaces the quieter source so the system never exceeds four sources. Completed
outgoing sources stop and release their clip. A missing profile/clip fades its channel to
silence, and a zero fade duration switches immediately. Disabling the controller stops
all sources and unsubscribes; enabling it resynchronizes with the loader's current area.

Fades use unscaled time so the startup menu's restored backdrop finishes its mix while
gameplay is paused. The loops continue during that menu. `AudioListener.volume` remains
owned by StartupMenu's persisted master-volume setting. This system does not change it.
No mixer exists in the current project; optional routes permit future mixer integration.

Starter audio is under `Assets/_Project/Art/Audio`; see that folder's README for provenance
and regeneration. To add an area, create an Audio Profile through **Assets > Create >
Lumenfall > Area Audio Profile**, assign imported looping clips and gains, then assign it
to the World Area asset. Keep one controller on the loader; no area prefab wiring is needed.

Tests in `Tests/EditMode` check imported clips and the six shipped profile assignments.
`Tests/PlayMode` cover channel independence, reload continuity, pause, rapid switches,
silence, master-volume preservation, and disable/enable. The authored world PlayMode test
also checks profiles after actual portal transitions and save restoration.

Manual listening check: open SampleScene, start a slot, travel campus -> east forest ->
campus -> an interior, and listen for a smooth change between loops. Load a forest save
from another area, test master-volume mute/unmute in Settings, and revisit an interior.
The four interiors intentionally share a quiet room-tone profile. Starter loops are
synthetic placeholders; final music and environmental recording choices remain an art task.

Validation on 2026-10-05 with Unity 6000.4.10f1: all six audio EditMode cases and all nine
PlayMode tests passed, including actual playback start, controller destruction, all ten
authored portal edges, and save restoration. The full EditMode suite had 25 passes and
eight failures; unchanged main at `8ceebfa` reproduced exactly the same eight dialogue,
quest, and spell failures (19 passes). The four Python validator harness tests passed.
Repository integrity still reports two unchanged third-party README problems: UTF-8
decoding in `FantasyCastle/ReadMe.txt` and conflict-marker text in
`Low Poly Tarvern Pack/README.txt`. These can block CI before Unity tests run. Listening
quality and a target player build were not verified; use the manual listening check above.
