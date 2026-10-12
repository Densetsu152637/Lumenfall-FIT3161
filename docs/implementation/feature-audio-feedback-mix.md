# Audio feedback and mix implementation contract

Branch: `codex/feature-audio-feedback-mix`; independent baseline `8ceebfa64c20d0f016dcd693c410bb64178d0dd5`. Draft PR: https://github.com/Densetsu152637/Lumenfall-FIT3161/pull/9.

## Required scope and delivered behavior

Event-driven forest/campus/interior/dialogue/mystery/sleep-wake contexts, spell/player-step/UI/quest feedback, saved clamped master/music/SFX/ambience controls, balanced runtime replacement/map restore lifecycle and safely reusable UI/accessibility contracts are implemented in code. No audio clips or mixer assets exist on the baseline; the 30 required cue entries explicitly remain `missing_approved_media`. This is a runtime/configuration delivery and media handoff, not an audible acceptance claim.

`AudioContextPolicy` provides deterministic Sleep > Dialogue > Mystery > map priority and pause/ducking rules. `GameAudioSignals` captures/detaches the existing event runtime, derives saved story contexts from `chapter1.key_handover`, `chapter1.sleep`, `chapter1.complete` plus optional audio flags, and emits live quest/sleep/wake cues once. Binding, replacement and StateRestored seed derived edges without replaying historical reward sounds. Map changes during dialogue return to the latest base context.

`AudioFeedbackService` bootstraps automatically and owns two music/two ambience loop sources, unscaled 0.6-second fades, dialogue/sleep ducking and eight bounded transient sources. Map rebuild and scene events bind the player/mover/caster; MoveCompleted emits surface steps, while teleport/blocked movement does not. Baseline CastCompleted emits applied spell/no-change cues. Gameplay pause leaves music/UI available, pauses ambience/gameplay voices and rejects new gameplay cues; application pause pauses all channels. Empty paused voices remain available for startup UI. No per-cue objects or repeated clip loads are created.

`AudioClipCatalog` validates approval, IDs, bus/loop/gain configuration and loads approved resources once. Missing/invalid/unknown cues expose explicit status and safe silent behavior; initialization reports all missing media once. No media was generated, borrowed, or imported, and no AudioMixer assets were modified.

`AudioMixSettings` is the single Master/Music/Sfx/Ambience preference authority. It preserves `lumenfall.master-volume`, adds category keys, repairs non-finite/out-of-range values, persists only changed values and exposes parameterless Changed. The service applies master only through AudioListener.volume; source gain never multiplies master twice. Startup delegates all four sliders and emits confirmed button feedback. Preferences are independent of save slots.

## Integration contract

Namespace `Lumenfall.Audio`: enum AudioBus { Master, Music, Sfx, Ambience }; static AudioMixSettings.GetVolume(AudioBus), SetVolume(AudioBus,float), ResetDefaults(), readonly Master/Music/Sfx/Ambience and event System.Action Changed. UI uses static AudioFeedbackService.PlayUi(AudioUiCue.Confirm/Cancel/Open/Error). Preference keys: lumenfall.master-volume, lumenfall.music-volume, lumenfall.sfx-volume, lumenfall.ambience-volume. Defaults 1 / 0.7 / 0.85 / 0.65.

Optional flags audio.mystery/audio.sleeping/audio.woke and event metadata audioCue are supported. audioContext metadata is not supported. Exact media authoring, pause/spatial policy and cue IDs are documented in Assets/_Project/Systems/Audio/README.md and Resources/Audio/audio-manifest.json. New Unity files have unique .meta GUIDs; no scene/prefab/map changes.

## Validation and manager review

- Worker `scripts/validate_audio.ps1` exit 0: source-built Lumenfall.GameEvents, Lumenfall.Runtime and complete Lumenfall.GameEvents.Tests compile against read-only Unity 6000.4.10f1/shared reference libraries. All imported Lumenfall.* and Assembly-CSharp refs excluded. Response/compiler outputs remain under this worktree Lumenfall/Temp/audio-validation.
- 13 portable audio NUnit methods executed through reflection runner and passed: mix boundaries/independence, context priority/ducking/pause, exact story flags/aliases, late sleep rejection, runtime replacement, repeated binding/disposal, map change during dialogue, reset/restore signal, metadata cue and missing/invalid media. UnityJsonRestore and UnitySettings tests compile but were not executed.
- Manager baseline runtime-world validator passed: 8 maps/7 NPCs. Dialogue validator passed: 6 quests/15 dialogues/9 events. Python validator harness passed all 4 tests.
- Manager project integrity scan reports only the 3 known baseline ThirdParty errors: orphan URP.meta, FantasyCastle UTF-16 ReadMe misread as UTF-8, Tarvern README conflict marker. No new audio metadata/config errors.
- Incremental manager review covered context/mix/signals, service/source ownership, startup layout and final tests/handoff. Review found and fixed startup-paused empty voice pool reservation. Unstaged diff whitespace check and committed `git diff --check origin/main...HEAD` passed; working tree clean after publication.

## Remaining media and manual acceptance

All 30 approved clips remain to be supplied with provenance/license/owner; configure resourcePath/approvalStatus without changing cue IDs. Listen to loop seams, transitions, ducking, pause/unpause, bus persistence and provisional spatialBlend 0.2/default attenuation in Unity Play Mode on target devices. Validate service bootstrap, scene/domain reload, actual player replacement and JSON/PlayerPrefs tests with Unity engine. Production keyboard/gamepad audio controls and additional confirmed UI actions remain integration work in production UI/accessibility branches. No shared Unity Editor/MCP/CLI/Play Mode/build launched. Offline checks do not establish audible, visual or platform acceptance.

## Merge overlaps

- First Light also adds GameEventManager.RuntimeChanged; preserve one replacement notification path and captured subscriptions, never overwrite the shared file from baseline.
- StartupMenu overlaps production UI/accessibility. Preserve AudioMixSettings delegation, four bus controls and single AudioListener owner; accessibility adapter should delegate to the exact contract above, not duplicate volume authority.
- Story flags must preserve exact key_handover/sleep/complete IDs for automatic context.
- Spell-feedback branch adds SpellCaster.CastFeedback Action<SpellCastFeedback> and expands CastCompleted to all attempts. After merge replace audio CastCompleted binding with typed CastFeedback: Applied => spell.<lowercase>, NoChange => spell.no-change, other rejection outcomes => ui.error; do not subscribe to both. Audio branch intentionally does not modify SpellCaster or duplicate its validation rules.
- Merge code incrementally; do not copy entire shared files. Root owns remote verification and eventual archival/removal of this managed worktree.

## PR #23 integration

The shared **Main** settings tab exposes Master, Music, Sound effects and Ambience controls in
startup, pause and journal. `GameSettingsPanel.MasterVolume` delegates to `AudioMixSettings`;
`AudioFeedbackService` receives runtime replacement notifications after New Game. This scoped import
adds the manifest/backend and tests, without importing unrelated area-audio or audio binaries.
