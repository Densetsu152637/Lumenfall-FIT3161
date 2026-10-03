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

## Implemented

- Immutable `AccessibilitySnapshot`: text scale 100–175%, read speed 10–100 characters/second (default 42), instant reveal, reduced camera motion, high contrast, hold/toggle sprint. Numeric nonfinite values use defaults, finite values clamp. Invalid individual stored fields fall back independently. Preferences use invariant-culture per-field keys under `lumenfall.accessibility.` and remain outside story save slots.
- `AccessibilityPreferences` validates and persists one effective update once, then emits one `Changed` notification. No-op updates do not write or notify. Reset restores all accessibility defaults and repairs malformed storage even when effective settings already match defaults; unrelated story/audio keys remain intact. PlayerPrefs is the runtime storage boundary.
- Startup settings and bootstrapped in-game settings reuse `AccessibilitySettingsView`. The view supplies scrollable wrapped labels/controls, a fixed Close button, visible focus, arrow/Tab/Enter/Space input, controller d-pad/stick/A navigation, Escape/controller B close, and reset. Mouse scrolling remains usable; automatic focus scrolling occurs only when navigation changes focus. Startup main/load pages now scroll for short viewports and focus changes bring the selected row into view.
- F10/controller Start opens settings during play or an active dialogue. The launcher pauses time, suspends only previously enabled movement/inventory/save-input components, preserves the active dialogue and its movement lock, and restores only captured components. Dialogue and movement honor a same-frame close guard. It refuses opening during another paused menu or journal. Existing Time.timeScale guards stop NPC/spell frame input while paused; production UI must consolidate this launcher into its central gate at merge.
- Dialogue reads live text size/read speed/instant reveal/contrast preferences. It preserves Nunito, speaker frames/colors at defaults and existing punctuation-aware/rich-text reveal behavior. Instant reveal completes the current text; disabling it does not hide text already revealed. Enlarged text uses a taller scrollable panel. Controller/keyboard focus selects among all choices, reveals the focused choice by scrolling, and cancel ends the session without claiming completion. The opening frame remains blocked. High contrast uses an opaque dark frame with white text and strips authored rich-text color tags while preserving emphasis.
- Existing movement actions implement hold or toggle sprint; a new press toggles once, the move-completion callback uses the cached decision, and disabling/blocked input or settings changes clear latched state. Existing bindings and serialized fields remain unchanged.
- Camera has a small `SetReducedMotion(bool)` API and balanced settings subscription. Reduced motion clears smoothing velocity and follows directly; the default preserves existing smoothing. This is a provisional comfort policy requiring visual acceptance.
- Localization lookup uses stable IDs plus authored English fallback. Missing/blank/throwing resolvers fall back safely. Dialogue text IDs are `dialogue.<dialogueId>.<nodeId>.text`; choices are `dialogue.<dialogueId>.<nodeId>.choice.<choiceId>`; speaker IDs are `speaker.<authored speaker>`; controls use `settings.*` / `startup.*`. No translations or packages were invented. The resolver is reset during runtime subsystem initialization to avoid stale providers with domain reload disabled. Current-node text resolves on entering a node; swapping locale mid-node is pending, while control/choice labels resolve during drawing.
- Startup remains the independent legacy master-volume owner and preserves `lumenfall.master-volume` / AudioListener.volume. Its single baseline adapter is reused by both views, clamps invalid/nonfinite values, and supports pointer/keyboard/controller adjustment. Accessibility preferences do not own audio values.

## Exact integration API

```csharp
AccessibilitySettings.Current; // immutable AccessibilitySnapshot
AccessibilitySettings.Changed += OnSettingsChanged; // Action<AccessibilitySnapshot>
AccessibilitySettings.Update(snapshot);
AccessibilitySettings.ResetDefaults();

var view = new AccessibilitySettingsView();
view.ResetFocus();
view.HandleNavigation(vertical, horizontal, submit);
bool close = view.Draw(panel, drawAudioControls); // Rect, optional Action<GUIStyle>
view.AudioAdjustRequested += AdjustAudio; // Action<int>, left/right on audio region
view.AudioResetRequested += ResetAudio; // Action

AccessibilitySettingsOverlay.IsOpen;
AccessibilitySettingsOverlay.InputBlocked;
settingsOverlay.Open();
settingsOverlay.Close();
camera.SetReducedMotion(settings.ReducedMotion);
Localization.Resolver = id => translatedTextOrNull;
Localization.Resolve(id, authoredEnglishFallback);
```

The host owns input sampling/close handling. `AccessibilityMenuInput` is the independent Input System adapter; production UI may forward its own actions to `HandleNavigation`. The callback audio region currently fits the baseline master controls; multi-bus controls need their own layout/scroll/focus adapter when merged.

## Pending scope and merge obligations

- Production UI: preserve its captured-runtime/replacement subscriptions, input lock ownership, slot/overwrite semantics, journal/objective/save feedback and focus logic. This branch retains baseline DialogueOverlay subscription/boolean mover-lock lifecycle; its accessibility hunks must combine with that branch's fixes. Replace independent settings pause/suspend launcher ownership with the production overlay gate and preserve F10/controller Start and live settings during dialogue. Baseline journal, quest/save feedback and spell-wheel text do not yet consume scale/contrast/localization; these broader production UI hooks remain pending and were forwarded to the production UI manager. This branch does not claim complete UI text coverage.
- Audio: replace Startup's `DrawMasterVolume` / `AdjustMasterVolume` / `ResetMasterVolume` baseline adapter with `Lumenfall.Audio.AudioMixSettings.GetVolume(AudioBus)`, `SetVolume(AudioBus,float)`, `ResetDefaults()` and its `Action Changed` event. `AudioBus` is Master/Music/Sfx/Ambience. The audio runtime alone applies AudioListener.volume after merge. Music/SFX/ambience controls are pending that coordinated merge, not duplicated here.
- Camera: keep the camera branch's richer `SetReducedMotion(bool)` implementation/profile/bounds/reseed behavior. Retain one balanced accessibility subscription or a separate consumer adapter; do not merge both duplicate methods. Serialized target/offset/focusOffset/smoothTime stay compatible.
- GridPlayerInput: preserve world-route collision/input refinements and production overlay gate. Merge only the sprint preference/cached decision/suspension guard hooks, leaving authored input references unchanged.
- Localization: this is an ID/fallback boundary, not translated content. Full journal/story/save localization, a provider/catalog, translated font/layout acceptance and live locale switching are pending. English is the only authored fallback.
- No scene/prefab/ProjectSettings/package/content asset edits were needed. New scripts/folder have unique matching `.meta` files; existing GUIDs/file IDs are untouched.

## Validation evidence

Ran `scripts/validate_accessibility.ps1 -ReferenceProject 'D:/Git Repositories/Lumenfall-FIT3161/Lumenfall'` with installed Unity **6000.4.10f1** compiler/references, read-only. It compiled the branch GameEvents/runtime assemblies and both existing EditMode and World.PlayMode test assemblies. Imported references matching `Lumenfall.*` and Assembly-CSharp were excluded; branch outputs were the only first-party references. All compiler response files/output stayed in `Lumenfall/Temp/accessibility-validation`.

Executed **16 pure accessibility NUnit cases: 16 passed, 0 failed**, using the same checked-in test source and actual NUnit assertions through the offline reflection runner. Cases cover missing defaults, NaN/infinity, finite boundaries, isolated invalid storage, effective update/write/notification counts, reload/persistence, reset repair/unrelated-key preservation, invariant culture, null rejection, sprint toggle/hold/blocked transitions, localization missing/blank/throwing providers/stable IDs/rich text, and high-contrast color-tag handling. These tests do not invoke Unity native methods.

The manager independently reran that compiler/test script successfully. Manager static checks also passed runtime-world validation (8 maps/7 NPCs), dialogue validation (6 quests/15 dialogues/9 events), and repository Python harness (4/4). Unity integrity validation found exactly three existing unrelated ThirdParty faults: orphan `URP.meta`, UTF-16 FantasyCastle ReadMe, and Tavern README marker. Those assets remain untouched. Existing serialized-field, obsolete baseline FindObjects API and compiler response warnings remain; no compile errors.

Committed-range `git diff --check 8ceebfa...HEAD` is required after the implementation commit; final result is reported to the manager with exact HEAD. The working diff is checked before commit.

## Unexecuted acceptance cases

No Unity Editor/MCP/CLI, Play Mode, platform build, rendering, audio playback or physical controller tests were run. Offline compilation of test assemblies does not mean those Unity tests executed. Verify in a licensed isolated acceptance session:

1. Startup/main/load/settings remain reachable at small supported window sizes; 175% text and long translated labels retain wrapped controls and scrolling. Mouse scrolling is not forced back toward focus.
2. F10/controller Start during a revealed/partially revealed dialogue preserves node/session/scroll and the dialogue lock. Escape/B/Close restores only the settings gate and cannot cast/advance/move in the close frame.
3. Many long choices at 175% text all remain reachable via arrows/Tab/d-pad/stick/A; focused choices scroll into view. Speaker, hint, final Continue/Close controls remain readable.
4. Change read speed/instant reveal/contrast live, reset, close/reopen and restart the application. Confirm rich text, punctuation and English fallback behavior at default settings and nonfinite legacy master volume safety.
5. Toggle sprint once per press; close a settings/journal/dialogue gate or change preference without leaving a sprint latch. Test actual action references and keyboard/gamepad devices.
6. Evaluate reduced-motion comfort while moving/teleporting and after the camera-profile merge; direct follow is provisional and not a claim of comfort acceptance.
7. After coordinated production UI/audio merge, validate all overlay text coverage, bus controls, one master listener owner and lifecycle restoration across scene/runtime/save replacement.

Implementation is substantive and independently compilable; the remaining coverage above is explicit and required before full integrated accessibility/release acceptance. PR: https://github.com/Densetsu152637/Lumenfall-FIT3161/pull/11.
