# Gameplay test suites

The current Unity checkout is the test subject. `Gameplay regression tests` runs
all batch-capable EditMode and PlayMode tests against `Lumenfall` in one GameCI
action. It shares one project and Library across both modes. No historical source
fetches, extracted snapshots or per-feature import matrix are required.

## Behavior inventory

[`gameplay_suites.json`](gameplay_suites.json) records fixture names and case counts,
not source revisions or individual method names. The Python harness compares this
inventory with the current test sources, including parameterized cases. Intentional
test additions or removals must update the inventory in the same change.

The integrated suite contains 302 EditMode, sixteen batch PlayMode and two graphical PlayMode cases:

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
| Save slot selection policy | GameSaveSlotPolicyTests | 3 |
| Borrowed Light progression and spell rewards | KatrinaBorrowedProgressionTests | 8 |
| Borrowed Light world state and restoration | KatrinaBorrowedWorldTests | 8 |
| Rotara alignment, normalization, retry and save migration | OrioPatchNotesTests | 25 |
| Authored quest dialogue references | QuestDialogueContentTests | 3 |
| Quest monitor lifecycle and completion requirements | QuestDialogueLifecycleTests | 9 |
| Quest hint visibility and dialogue input gating | QuestHintOverlayTests | 10 |
| Spell state and world save round trips | SpellWorldStateTests | 4 |
| Weave quest prerequisites, authored board and completion | StringThemTogetherTests | 6 |
| Live cable weakening/recovery and minimum-power save restoration | WeaveNetworkTests | 2 |
| Pending effect deduplication, acknowledgment and requeue | WorldEffectQueueTests | 1 |
| Accessibility preferences and localization | AccessibilityPreferencesTests | 16 |
| Audio buses and feedback | AudioFeedbackTests | 15 |
| Transactional save recovery and rollback | GameSaveRecoveryTests | 19 |
| Overlay ownership and focus policy | ProductionUiPolicyTests | 4 |
| Graphics preference persistence | GraphicsPreferencesTests | 13 |
| Hardware recommendation thresholds | GraphicsRecommendationsTests | 17 |
| Ambient casts and single-frame input | AmbientCastTests | 7 |
| Cutscene sequencing and blocking | CutsceneSequenceTests | 15 |
| Opening forest story | LateToOrientationTests | 4 |
| Campus introduction | LayOfTheLandTests | 6 |
| Collectible discovery | MiscCollectiblesTests | 3 |
| NPC introductions | NpcIntroductionsTests | 7 |
| Player name and dialogue substitution | PlayerProfileTests | 5 |
| Player rendering visibility | PlayerVisibilityTests | 4 |
| Dormitory onboarding | SettlingInTests | 6 |
| **Gameplay rules / EditMode** | | **302** |
| Scene boot/references, saved-state refresh, forest gate and campus portals | AuthoredWorldPlayModeTests | 7 |
| Sibling and legacy movement locks | ProductionUiLockPlayModeTests | 1 |
| Graphics bootstrap, VSync, shading/AO, later-camera AA | GraphicsOptionsPlayModeTests | 4 |
| Settings keyboard navigation and preset isolation | OptionsPanelPlayModeTests | 2 |
| Dialogue pointer gating, runtime replacement and owned cleanup | DialogueSettingsUiPlayModeTests | 1 |
| Wheel casts, sibling ownership and closing-frame input consumption | SpellWheelCastingPlayModeTests | 1 |
| **Batch PlayMode** | | **16** |
| Pointer ribbon interaction and screenshots (graphical only) | OptionsPanelPlayModeTests | **1** |
| Localized dialogue, reveal speed, large text and high-contrast screenshots (graphical only) | DialogueSettingsUiPlayModeTests | **1** |

Tests live alongside their systems in four assemblies: `Lumenfall.GameEvents.Tests`,
`Lumenfall.Startup.Tests`, `Lumenfall.World.PlayModeTests` and
`Lumenfall.Startup.PlayModeTests`. The source inventory discovers all C# files owned
by test assembly definitions, including nested helpers and files with other names.
No test uses Ignore or Explicit. Both rendering tests have the explicit
`RequiresGraphics` category and fails if accidentally selected in batch mode.

## Running and verifying

Open `Lumenfall` with the version in `ProjectSettings/ProjectVersion.txt`. Run all
tests in both tabs of **Window > General > Test Runner**. EditMode fixtures that
exercise MonoBehaviour lifecycle must initialize the required services explicitly;
use the existing `EditModeLifecycle` helper when applicable, restore previous world
instances, and unsubscribe fixture-owned event handlers during cleanup. Quest
monitors must load their definitions before evaluation. Preserve production reward
and completion requirements.

Authored-world PlayMode cases load their own `SampleScene` additively while
retaining the runner and an empty fixture host. Teardown unloads only that owned
world, checks loader/listener cleanup, then restores the runner and removes the
host. Tests wait for any startup menu to finish before establishing a fresh
authored world. Portal expectations use fixed current prefab geometry: all authored
campus forest-gate cells and B02 entrance cells must transfer,
and the B02 landing must reach Stella's actual prefab position and return portal.

CI uses `testMode: all` with GameCI CLI `v0.1.72` and
`customParameters: -testCategory !RequiresGraphics`. The Unity checkout enables
Git LFS so terrain and other binary assets are materialized before import. The CLI runs both modes against
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

## Graphical settings acceptance and local isolation

Run both rendering cases separately in a graphical Editor, without `-batchmode`, using
`-runTests -testPlatform PlayMode -testCategory RequiresGraphics` and
`-testResults <fresh-graphical-directory>/playmode-results.xml`. After the Editor
exits successfully, verify the separate manifest and exact current revision:

```sh
python scripts/verify_gameplay_tests.py --graphical --artifacts <fresh-graphical-directory> --outcome success --revision <full-current-checkout-sha>
```

The graphical inventory has exactly two cases; the batch inventory contains the
other fifteen PlayMode cases. Both verifications reject any failed or skipped case.
Graphical acceptance also requires inspecting the generated settings and dialogue screenshots
under `Lumenfall/Logs/graphics-validation`. Batch results alone do not establish
pointer repaint or visible layout quality.

A checkout does not isolate PlayerPrefs or `Application.persistentDataPath`.
Local validation must use a dedicated temporary company/product identity in the
task-owned checkout, preserve the original ProjectSettings bytes, and restore them
after all Editor processes exit. Report this identity-only configuration difference.
Never run recovery tests or startup backdrops against a developer's normal saves.
Hosted Linux jobs have disposable user profiles and validate the committed identity.

## Consolidation and coverage limits

The historical snapshot matrix remains retired. All tests exercise the current
checkout; graphics, settings, audio, accessibility, save recovery and overlay
coverage now ship with their corresponding implementations. Physical controller
behavior, visual AA/AO quality, audio output, standalone player compatibility and
performance still require their own acceptance evidence. Source inventory and local
checks do not substitute for successful hosted checks on the final revision.

PR #32 integration preserves transactional save restoration and adds successful New Game autosave resumption, manual-save keyboard dispatch, the authored deep-forest dais route, and save-only exits from the retired Orio hall and Katrina recess. Legacy interiors have no active replacement entrance in the new world.
