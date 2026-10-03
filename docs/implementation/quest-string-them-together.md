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

Both existing B02 endpoints are at (18,14)/(20,14), in W10 according to the map regions. This branch describes the existing engineering workbench without inventing relocation into W02. Connection attenuation is 1/(1+Euclidean tile distance); links below 0.2 are rejected at creation but stored strength needs live reevaluation.

## Remaining manual work

Approved cable/character/prop art, audio and animations; a dedicated Unity Play Mode pass for teaching, targeting, portal/map reload, cable retries and save/load; final room/layout approval. Shared Unity Editor/MCP/CLI access is excluded during parallel implementation.

## Merge coordination

First Light independently changes SpellCaster/QuestPuzzleMonitor subscription lifecycle and success dialogue. This branch changes scoped Weave reward handling, cable APIs, NPC selection and monitor target conditions. Reconcile those distinct sections when merging; both branches start main independently.
