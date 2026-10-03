# Accessibility settings implementation

Branch: `codex/feature-accessibility-settings`; independent base: `8ceebfa`.

## Full required scope and acceptance contract

Persist text scale, dialogue read speed (baseline 42 characters per second with existing punctuation pauses), instant reveal, reduced motion, high contrast, and relevant input/audio options with validated defaults. Apply settings live. Maintain keyboard/gamepad focus and close/cancel operation, with scrolling for large text and many choices. Reuse existing UI and audio authority. Introduce stable localization IDs with English fallbacks using current dialogue IDs/assets; no invented translations, packages or media. Verify reset and malformed stored values. Record rendering/device/translation coverage honestly.

## Plan

1. Add immutable, validated accessibility preferences and a persistence boundary over PlayerPrefs, with a Changed event and default reset. Keep preferences independent of story save slots.
2. Add a reusable scrollable IMGUI settings view (text size, read speed, instant reveal, reduced motion, contrast, hold/toggle sprint), local focus navigation, cancel handling, and optional audio controls callback. Startup hosts this view; the baseline master-volume owner remains Startup until the audio branch is merged.
3. Apply preferences in DialogueOverlay: live text scaling/contrast/read speed/instant reveal, scrollable choice focus and keyboard/gamepad advance. Preserve rich text, Nunito and speaker art at defaults. Add stable localization ID/fallback lookup for UI and dialogue text.
4. Add a live in-game settings entry point with scoped pause/input ownership, and integrate hold/toggle sprint through existing GridPlayerInput actions. Extend current camera smoothing for reduced-motion policy without renaming serialized fields.
5. Add behavioral tests for clamps/nonfinite/invalid persisted values, defaults/reset/persistence, notifications, toggle-sprint state, and localization fallbacks. Compile first-party runtime and tests offline against installed Unity 6000.4.10f1 assemblies with no stale imported first-party references; run deterministic settings tests offline where possible.
6. Review committed diff, document merge hunks and remaining acceptance, commit/push implementation.

## Ownership and integration

`Lumenfall.Accessibility.AccessibilitySettings.Current` owns text/input/motion/contrast only. Proposed immutable snapshot fields: TextScale, CharactersPerSecond, InstantReveal, ReducedMotion, HighContrast, HoldToSprint. `Changed`, `Update(snapshot)`, `ResetDefaults()` are the consumer boundary. Audio branch owns `Lumenfall.Audio.AudioMixSettings`; do not duplicate this authority. Startup's existing `lumenfall.master-volume` control remains independently functional; at merge supply the audio branch's controls to the settings view.

Shared merge files: StartupMenu, DialogueOverlay, GridPlayerInput, CameraFollow2_5D. Merge exact accessibility hooks with production UI overlay/focus/lifecycle changes and camera profiles. Never replace a sibling's complete file with this baseline version.

## Validation boundary

No shared Unity Editor, Unity MCP/CLI, Play Mode or build launch. Read-only reference project: `D:/Git Repositories/Lumenfall-FIT3161/Lumenfall`. Response files and compiler outputs stay in this worktree's Temp. Static/offline compilation does not establish visual, audible, device or release acceptance. Assets need matching metadata; serialized fields, existing GUIDs/file IDs remain stable.

## Current status

Plan committed before implementation. All implementation and runtime acceptance still pending.
