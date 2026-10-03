# Production UI implementation

Branch: codex/feature-production-ui. Independent baseline: 8ceebfa64c20d0f016dcd693c410bb64178d0dd5.

## Scope and review contract
Materially improve existing startup/slots/save feedback/dialogue/quest objectives/inventory controls using existing owners. Provide keyboard/gamepad focus, cancel, scrolling, long choices/text and responsive sizing; explicitly separate offline checks from device/render acceptance. One owned overlay input gate must block dialogue/menu/journal/wheel leaks and survive disabling/reloading without releasing another owner. Opening-frame E must not skip dialogue. Preserve Nunito licensing, speaker frames, rich-text DialogueTextReveal, inventory flags, current-slot behavior and explicit overwrite confirmation.

## Plan
1. Inspect existing presenter, event runtime, movement and save contracts; retain IMGUI to keep this code-only work bounded and scene references stable.
2. Establish shared owner-based overlay gating in GameEvents (no Runtime dependency), wire gameplay inputs and existing overlay owners, and guard opening-frame dialogue continuation.
3. Improve dialogue choice navigation and scrolling; add journal objective/status presentation; expose focused startup, save and inventory controls and actionable errors.
4. Add behavior tests for navigation/gate boundaries and overlay lifecycle where safe; offline compile all affected assemblies using read-only installed Unity references, excluding imported Lumenfall assemblies. Review full committed diff.

## Risk, compatibility and merge coordination
Runtime input/UI changes are medium risk. Existing serialized fields and asset identities remain stable; rollback is the independent branch. Accessibility independently edits StartupMenu and DialogueOverlay for text/reveal/contrast/localization and focused choices. Merge those hunks deliberately; do not import unmerged accessibility APIs. Proposed merged API is Lumenfall.Accessibility.AccessibilitySettings with immutable Current (TextScale, CharactersPerSecond, InstantReveal, ReducedMotion, HighContrast, HoldToSprint), Changed/Update/ResetDefaults. Audio owns AudioMixSettings and AudioFeedbackService; UI will keep a narrow integration seam and no competing audio preferences owner. World-route and spell branches can overlap input owners; preserve their lifecycle and targeting improvements.

## Validation and pending acceptance
No Unity Editor/MCP/CLI, Play Mode, build, or shared write operations. Offline compilation/static checks and authored tests do not prove rendering, controller hardware, scene lifecycle or platform acceptance. Required manual acceptance: startup/new/load/full-slot cancellation, dialogue choices/reveal/open-frame interaction, journal objective states, inventory scrolling and cancel, spell wheel sibling locks, save/load errors, small resolution and controller focus. Final evidence and remaining setup will replace this section after implementation.
