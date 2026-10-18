# Stella: String Them Together implementation scope

Branch `codex/quest-string-them-together`, based on `origin/main` (8ceebfa).

## Required scope

- Teach Weave through a completed lesson while this quest is Active and First Light is Completed; opening, accepting, abandoning, or leaving alone must not grant it.
- Select Stella's next lesson deterministically after First Light, prioritizing an active assignment while retaining completed conversations.
- Require the exact authored power relic/receiver cable, a powered source, and live effective strength at least 0.2 using tile distance.
- Recompute strength when objects move or power changes; provide cable removal/retry without losing save persistence.
- Give understandable lesson, hints, cable and completion feedback using existing presentation.
- Cover lesson/progression, exact topology, power/distance boundaries, retry and persistence with focused tests.

## Baseline and design limits

Both existing B02 endpoints are at (18,14)/(20,14), in W11 (the authored region at x=18,y=12,width=6,height=3) according to the map regions. This branch describes the existing engineering workbench without inventing relocation into W02. Connection attenuation is 1/(1+Euclidean tile distance); links below 0.2 are rejected at creation but stored strength needs live reevaluation.

## Remaining manual work

Approved cable/character/prop art, audio and animations; a dedicated Unity Play Mode pass for teaching, targeting, portal/map reload, cable retries and save/load; final room/layout approval. Shared Unity Editor/MCP/CLI access is excluded during parallel implementation.

## Merge coordination

First Light independently changes SpellCaster/QuestPuzzleMonitor subscription lifecycle and success dialogue. This branch changes scoped Weave reward handling, cable APIs, NPC selection and monitor target conditions. Reconcile those distinct sections when merging; both branches start main independently.

## Implemented and validation

- Completed terminal Weave lesson uses `spellRewardQuestId=stella_string_them_together`, requires Active quest and Completed First Light. Shared handler supports comma/semicolon spell lists and preserves Ampere fallback.
- Structured NPC dialogue prioritizes Active, then NotStarted, then Completed; ambient weighting remains within equal priority.
- Monitor requires powered source separately, exact relic/receiver link, and live effective power >=0.2. B02/campus authored target assets and parallel JSON declare the same threshold.
- Cable strength follows current cells; recasting removes even dark/overlong cables and allows a valid retry. World save data keeps existing connections/provider format; live values derive from restored positions/power.
- Four focused EditMode tests cover lesson abandonment/save flags, authored source-only power, exact topology/power/distance/retry/persistence, source-endpoint casting, removal feedback with stray cables, restored legacy creation strength, Stella selection, and durable monitor completion. These sources compile offline; NUnit engine execution and Play Mode are not run.
- Repository runtime-world validator passes (8 maps/7 NPCs), dialogue validator passes (6 quests/15 dialogues/9 events), and Python validator harness passes all 4 tests. Isolated runtime/GameEvents/EditMode compilation succeeds against shared read-only engine references, excluding baseline Lumenfall assemblies. Existing obsolete object-search/unassigned serialized-field compiler warnings remain.

Final independent review corrected map target/source-power parity and cable-removal feedback when unrelated links remain. B02/campus authored map targets now require the power relic itself to be powered, plus the exact live pair. GameEvents, runtime and all existing EditMode sources were recompiled offline; validators and all four Python harness tests passed again. The four Unity NUnit tests remain unexecuted.

Completion feedback currently uses the existing spell cast status box and completed Stella conversation. Final cable artwork/animation, automatic completion celebration, and runtime visual acceptance remain manual presentation work.

## Live gyroscope power after completion

The golden gyroscope animates only while the five obelisks, ten relays and gyroscope are powered in one connected live network. Reporting String Them Together preserves its completed quest history; it does not supply permanent power. Cutting a required cable stops and rewinds the animation, and reconnecting the live circuit resumes it. Dark nodes remain valid Weave endpoints so the player can wire a circuit before powering it; the existing restriction against directly connecting a small relay to the gyroscope remains unchanged.

`AuthoredWorldPlayModeTests.CompletedGyroscopeStopsWhenItsLiveCircuitBreaksAndRestartsWhenReconnected` exercises the authored model's animated child rotations while powered, after disconnecting every gyroscope feed and after reconnection. It requires timed PlayMode execution but no rendered-pixel inspection. Runtime execution of this new regression is pending integrated validation.
