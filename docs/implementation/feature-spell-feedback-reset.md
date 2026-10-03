# Spell targeting, object feedback and puzzle reset/retry

## Contract and baseline
Continue existing feat/hint-system at 374d2bc (PR 1), preserving its hints/settings, Stella circuit board and map rename history. This is a substantial inherited branch; no cherry-pick, reset or merge from main. No tracked AGENTS.md was found. Repository coding, Git, container and validation instructions and Unity feature/build validation skills apply.

Required scope: distinct locked spell, no target, unknown target, incompatible target, range, blocked input and no-op cast outcomes through existing SpellCaster/WorldStateService owners, including direct Cast calls; authoritative idle/valid/invalid/solved/restored presentation; generic retry restoring authored object baselines and removing related connections/effects/puzzle progress while preserving quest completion and unrelated maps. Keep IDs/provider keys and new metadata stable. No shared Unity Editor/MCP/CLI/Play Mode/build operations.

## Plan before implementation
1. Add typed cast feedback alongside the compatible bool Cast/CastCompleted API. Validate nearest targeting and direct calls against current map readiness, overlay state, target metadata and Manhattan footprint range. Existing callers without a GridMover retain a documented service integration exception. Tag categories: relic/circuit, mechanism, movable/ice, orientation, hidden and authored explicit spell tags.
2. WorldStateService retains authored baselines at registration before applying saves. Add feedback query/event surface deriving solved from live target evaluation, plus transactional reset of declared puzzle objects, incident cables and object effects. Reset must hold IsReady false until all objects/progress restored and publish one final state. Per-puzzle saved progress lives with this existing provider, never quest flags.
3. Present textual cast/object status and a nearby puzzle retry action through existing SpellCaster UI; no duplicate settings/hint overlay. Rebuild/load derives restored feedback without firing quest completion effects. Reset generic map targets; inherited Stella board custom input remains its own owner.
4. Add behavior-focused EditMode cases for rejected/direct casts, no-op, authored reset/restore, event atomicity, cables/effects, durable unrelated state and progress. Include metadata for new files.
5. Offline compile runtime + tests using read-only Unity reference assemblies, excluding all stale Lumenfall references; run available Python validators and full origin/main...HEAD diff --check, report inherited failures separately. Commit/push and manager handles existing draft PR update.

## Merge and validation risks
SpellCaster Cast/selection/teaching/lifecycle overlaps First Light, quest lessons, audio, accessibility and UI. WorldStateService overlaps exact Weave power, ice sequence, reveal, optional activities and saves; integrate individual hunks and preserve quest rules. QuestPuzzleMonitor success/lifecycle work must remain intact. This inherited branch changes eastForest to westForest and has a custom five-relic First Light board, so main's single-rel ic contracts cannot simply replace its data. Generic reset does not undo completed quests or remove unlock/reward flags. Custom board baseline progress must be registered with the existing authority or explicitly reported pending.

Unity runtime/rendering/build execution remains intentionally unverified; offline compile cannot establish gameplay or visual acceptance.
