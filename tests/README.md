# Gameplay test suites

The current Unity checkout is the test subject. `Gameplay regression tests` runs
the complete EditMode and PlayMode assemblies against `Lumenfall` in one GameCI
action. It shares one project and Library across both modes. No historical source
fetches, extracted snapshots or per-feature import matrix are required.

## Behavior inventory

[`gameplay_suites.json`](gameplay_suites.json) records fixture names and case counts,
not source revisions or individual method names. The Python harness compares this
inventory with the current test sources, including parameterized cases. Intentional
test additions or removals must update the inventory in the same change.

The integrated suite contains 156 EditMode cases and six PlayMode cases:

| Behavior | Fixture | Cases |
| --- | --- | ---: |
| Dialogue page splitting and rich-text balancing | DialoguePagingTests | 6 |
| Dialogue text effects and spell highlighting | DialogueTextEffectsTests | 8 |
| Cinna ice switches and seal | CinnaIceTrialTests | 5 |
| First Light completion and runtime replacement | FirstLightIntegrationTests | 7 |
| Lost Echoes reveals, collection and progression events | LostEchoesRevealEventTests | 8 |
| Vis carry, placement, reset and Cinna hand-off | OutOfAlignmentTests | 33 |
| Dialogue reveal and rich text | DialogueTextRevealTests | 8 |
| Event dispatch, dialogue and quest runtime | GameEventRuntimeTests | 7 |
| Save slot selection policy | GameSaveSlotPolicyTests | 2 |
| Borrowed Light progression and spell rewards | KatrinaBorrowedProgressionTests | 8 |
| Borrowed Light world state and restoration | KatrinaBorrowedWorldTests | 8 |
| Rotara alignment, normalization, retry and save migration | OrioPatchNotesTests | 25 |
| Authored quest dialogue references | QuestDialogueContentTests | 2 |
| Quest monitor lifecycle and completion requirements | QuestDialogueLifecycleTests | 9 |
| Quest hint visibility and dialogue input gating | QuestHintOverlayTests | 8 |
| Spell state and world save round trips | SpellWorldStateTests | 4 |
| Weave quest prerequisites, authored board and completion | StringThemTogetherTests | 5 |
| Live cable weakening/recovery and minimum-power save restoration | WeaveNetworkTests | 2 |
| Pending effect deduplication, acknowledgment and requeue | WorldEffectQueueTests | 1 |
| **Gameplay rules / EditMode** | | **156** |
| Scene boot/references, saved-state refresh, forest gate, campus/Cinna routes and rune-hall portal save restoration | AuthoredWorldPlayModeTests | **6** |

EditMode sources are under
[`GameEvents/Tests/EditMode`](../Lumenfall/Assets/_Project/Systems/GameEvents/Tests/EditMode)
in `Lumenfall.GameEvents.Tests`. PlayMode sources are under
[`DataDriven/Tests/PlayMode`](../Lumenfall/Assets/_Project/Systems/DataDriven/Tests/PlayMode)
in `Lumenfall.World.PlayModeTests`. No tests are ignored or explicit-only.

## Running and verifying

Open `Lumenfall` with the version in `ProjectSettings/ProjectVersion.txt`. Run all
tests in both tabs of **Window > General > Test Runner**. EditMode fixtures that
exercise MonoBehaviour lifecycle must initialize the required services explicitly;
use the existing `EditModeLifecycle` helper when applicable, restore previous world
instances, and unsubscribe fixture-owned event handlers during cleanup. Quest
monitors must load their definitions before evaluation. Preserve production reward
and completion requirements.

CI uses `testMode: all` with GameCI CLI `v0.1.72`. The CLI runs both modes against
the same project, then combines results; it launches Unity separately for each
phase. The verifier requires the separate `editmode-results.xml` and
`playmode-results.xml`. Combined XML cannot stand in for either mode. It rejects
missing or partial discovery, duplicate identities, failed or skipped cases,
unsuccessful action outcomes and a checkout revision mismatch. It writes
`ci-report.json` and a per-mode job summary. Results and diagnostics are uploaded as
`gameplay-test-results` for 14 days, including after test failures. The Library is
saved only after both modes pass strict verification.

For equivalent GameCI-generated results, run from the repository root after the
Unity action has completed (supply its actual outcome and checked-out commit):

```sh
python scripts/verify_gameplay_tests.py --artifacts artifacts/gameplay --outcome success --revision <full-current-checkout-sha>
```

CI refuses an already-existing output directory before execution. Local results
must likewise come from a fresh run of this checkout; copying old XML is not a
validation run. The verifier harness and static source inventory run without Unity:

```sh
python -m unittest discover --start-directory scripts/tests --verbose
python scripts/validate_unity_project.py Lumenfall
```

## Consolidation and coverage limits

The retired catalog contained 101 cases across historical pinned implementations
(98 Unity cases and three Python mutations). Those counts described other source
snapshots, not tests of the current project. The replacement retains all existing
current-project coverage and migrates only the three critical effect/cable cases
above. Existing quest/world/save-state tests already cover the overlapping
progression scenarios, and the Rotara fixture covers alignment and invalid-angle
behavior without another boundary table. Fast useful assertions remain; removing
the historical snapshot matrix and repeated Unity imports addresses the main
execution cost.

Historical tests of retry/reset extensions, chapter transitions, audio signals and
area audio, optional power/recovery activities, accessibility/localization,
reserved regions, presentation catalogs, overlay/focus
indexes, travel/ambient policies, performance diagnostics, camera framing,
occlusion, ice-slide lanes and graphics preferences depend on APIs absent from this
checkout. Their snapshot infrastructure and validator mutation tests were removed.
Passing the current suite does not validate those branch-only features; add focused
behavior tests alongside their eventual implementation.

Save coverage here exercises state round trips and slot policy. Public disk
save/load failure rollback remains a gap; these tests do not access or change a
user's persistent save directory. The PlayMode smoke checks do not establish player
build compatibility, visual quality, audio output or performance. Hosted results
and timings are required before claiming a measured speedup.
