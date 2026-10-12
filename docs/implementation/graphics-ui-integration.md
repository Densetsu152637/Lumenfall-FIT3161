# Graphics and menu branch integration

PR #23 combines the basic graphics settings with the UI work available on the repository's remote branches. Production UI and accessibility branches are merged. Broader gameplay/content branches contribute only their UI files and the runtime dependencies needed to make those menus function; unrelated maps, puzzles, quest logic and large art imports are excluded.

## Sources

| Source branch / reviewed commit | Integrated content |
| --- | --- |
| `codex/feature-production-ui` / `29a175e` | Keyboard/controller navigation and owned overlay/movement locks. |
| `codex/feature-accessibility-settings` / `401cd79` | Text scaling, dialogue reveal/contrast, sprint preference, reduced camera motion and localization resolver. The F10/controller shortcut opens the shared settings panel. |
| `feat/options-menu` / `65e413c` (includes `feat/main-menu` / `a67baeb`, `2e7cc18`) | Shared theme, runtime menu art, journal restyle, pause/settings presentation and grouped key rebinding. Its five sections are consolidated into Main and Graphics. |
| `campus-updates` / `20ff50a` | Dedicated autosave, manual Save page, slot summaries/thumbnails and loading snapshots into autosave. The later garden/occlusion content is excluded. |
| `feat/spell-wheel` / `aa70dae`, `176fae8` | Animated spell wheel, scroll feedback and selected-spell HUD chip. References to newer puzzle-specific panels are excluded. |
| `codex/feature-audio-feedback-mix` / `7040692`, `dd4c078` | Four bus controls, feedback service/catalog and runtime-change notifications. No audio binaries or unrelated area-audio feature are imported. |
| `codex/feature-save-recovery-migrations` / `2b78b5a`, `03d775a` | Explicit recovery actions and their validated, transactional save/provider backend, with recovery tests. |
| `feat/game-systems` / `0e74db7` | Main-menu readiness/singleton guards and fresh New Game behavior; spell subscriptions follow replacement event runtimes. |
| `feat/hint-system`, `Week-10-Sign-off` (in options-menu ancestry) | Hint toggle/presentation and interface bindings through the shared menu sources. Hints use the current base's puzzle model; newer quest/puzzle gameplay is excluded. |
| `codex/quest-katrina-something-borrowed` / `a157a5d`, `73aa466` | Journal learned-spell presentation. Existing quest-completion keepsake rewards are retained and covered by lifecycle tests. |
| `codex/feature-optional-recovery-activity` / `9da1650` | Journal label for the optional recovered item; the activity gameplay remains on its branch. |

Other remote branches either inherit these UI changes or modify gameplay, levels, assets, infrastructure or documentation outside this integration. This is content integration for broad branches, not a claim that their complete histories are merged. A full options-menu merge would bring over 1,600 unrelated files; scoped imports keep this PR reviewable.

### Full menu-branch merge

`feat/options-menu` (`30df69c`, which contains all of `feat/main-menu`) was later merged into this branch in full, after both had been merged into `main` through PRs #26 and #27. The merge brings in the newer gameplay and quest content those branches carry, so the scoped exclusions above no longer apply to them. Conflicts were resolved as follows:

- **Kept from this branch:** the consolidated Main/Graphics `OptionsPanel`, `MenuSkin`, `GameSettingsPanel`, graphics options, accessibility, audio buses, `OverlayInputGate` ownership, keyboard/controller navigation, transactional save recovery (`GameSaveManager`) and owner-scoped movement locks.
- **Taken from the menu branches:** the New Game slot picker with linked manual slots (`ManualSaveSlot`), journal-footer Save/Main menu/Quit, deliberate autosaving (`GameAutosave.HasPlayedSinceLoad`, no save on load), the four-option main ring (the startup Save page is removed), quest hints for Stella and Orio, the `eastForest` save-id migration, NPC talk prompts and the dialogue text-colour fixes.
- **Combined:** `SpellCaster` follows `main` (Weave threads, Lost Echoes area search, mechanism casts and the animated wheel) and adds gamepad/arrow wheel navigation and the input gate. `GridPlayerInput` keeps the hold/toggle sprint preference and ignores rebound keyboard keys in the input actions. `WorldStateService` and `DataDrivenWorldLoader` validate saves after normalising legacy map ids.

## Result

One `OptionsPanel` is used in startup Settings, pause Settings and journal Settings:

- **Main:** hints, audio buses, accessibility, reduced menu motion and key rebinding.
- **Graphics:** fullscreen, VSync, AA, AO, shading and Use Recommended.

Each opening selects Main. Keyboard/controller focus and pointer controls operate the same underlying preference services. Only graphics keys missing on first launch receive hardware recommendations; existing preferences survive. Preset thresholds and runtime rendering behavior are documented in the [startup README](../../Lumenfall/Assets/_Project/Systems/Startup/README.md).

Save/load controls use dedicated autosave and three manual slots. Invalid saves offer explicit recovery candidates. Transactional reset/restore protects current progress, retains unknown providers and prevents autosaving after a failed rollback. Audio controls use the imported bus owner rather than a second master-volume store. Accessibility shortcuts route to the same panel rather than opening a competing settings screen.

## Verification

Unity version: `6000.4.10f1`, URP `17.4`. Local results are retained in `Lumenfall/Logs/graphics-validation` (ignored build artifacts).

- EditMode covers graphics recommendations/persistence, audio buses, accessibility, save recovery and existing quest/world behavior.
- PlayMode covers live URP settings, tab navigation, recommended preset isolation, overlay ownership and authored-world routes/quest refresh.
- The graphical-only UI test paints the compact shared panel, sends pointer events to both tabs and captures screenshots. Batch-mode CI explicitly skips this test because IMGUI does not repaint there; keyboard/controller state tests continue in batch mode.
- The authored-world fixture loads additively, resets quest definitions after the menu backdrop loads, and unloads its scene afterward, avoiding test-runner destruction and dependence on a developer's saved quest progress.
- Repository checks: `python scripts/validate_unity_project.py Lumenfall` and `python -m unittest discover -s scripts/tests`.

Physical controller testing, the full animated title/menu flow and a standalone player build require reviewer checks. Hardware presets are heuristics and have not been benchmarked across machines. The audio manifest may reference clips absent from the base branch; missing clips are tolerated and the volume preferences remain functional. Broad branch integrations intentionally exclude their unrelated content.

## Review and rollback

Review the shared settings/input ownership, manual-save/autosave transitions and recovery flow together. No gameplay map/prefab edits or package additions are required. Revert the integration commit and production-UI merge to remove the combined menus; the earlier graphics implementation can remain independently. Do not downgrade/write newer saves with an older executable without retaining a backup.
