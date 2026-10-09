# PR19 optional Lost Echoes preview acceptance

Target: `fcd8bf62adf50647beebb4e3cc56064536cbcf93`. Category: `PR19`. EditMode: 3 cases.

New vectors:
- Suspending a partial search keeps observed progress, prevents completion while suspended, and resumes to exactly one completion after both targets are revealed.
- Saved progress overrides stale reveal state in both directions while preserving unrelated power state; restoration alone grants no receipt.
- Missing the optional-recovery tag prevents starting and rewarding even when both targets are already revealed and active flags were set.

Uses a local GameEventRuntime and inactive WorldStateService host to avoid Awake, singleton replacement, and save-provider registration. Targets have no scene objects and no native reveal spell call is used.

Manual limits: actual spell targeting, marker cleanup across scene/map lifecycles, preview UI, and authored Katrina quest integration require the existing PlayMode/manual workflow. This preview remains optional and disabled by default.
