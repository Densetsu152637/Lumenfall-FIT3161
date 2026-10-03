# Stella: First Light implementation scope

Branch: `codex/quest-first-light`, based on `origin/main` (8ceebfa).

## Required player loop

- Reach Stella in W02 on B02.Ground and accept First Light.
- Diagnose the broken electrical relic through Stella's lesson and receive Ampere before attempting the puzzle.
- Make lesson and active hints match available actions and the authored target; avoid inventing an unsupported repair interaction or receiver.
- Cast Ampere on `relic.stella.power`; completion requires an active quest, the correct map, and authoritative powered state.
- Give clear success feedback and preserve the completed conversation before the String Them Together handoff.
- Keep reward flags, completion state and monitor lifecycle reliable across reload/reset/save import.
- Add meaningful automated coverage for the implemented progression and edge cases.

## Existing baseline

Main already awards Ampere through First Light's terminal teaching dialogue and defines the powered relic target. Extend that path; do not add duplicate reward sources or replace existing managers.

## Work plan

Inspect dialogue, NPC priority, monitor and save lifecycle; implement the most concrete gaps, run repository validators and isolated compilation where possible, record results here, and update the draft PR.

## Outstanding manual work

- Approved NPC/relic artwork, final animations, audio and scene presentation.
- Play Mode traversal from reception to W02, spell targeting and completed-dialogue handoff.
- Unity Editor/build verification in a dedicated project session after parallel branches are reviewed.

Shared Unity Editor and Unity MCP/CLI access are deliberately excluded from this parallel work.
