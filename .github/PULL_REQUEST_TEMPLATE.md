## Summary

<!-- What changed, and why is this the right change? Keep this player-focused where possible. -->

## Related work

<!-- Use "Closes #123" when merging this PR should close an issue. -->

- Closes #
- Design/task link:

## Change type

<!-- Check every category that applies. -->

- [ ] Gameplay or game systems
- [ ] Level, narrative, art, animation, or audio content
- [ ] UI/UX, input, accessibility, or localisation
- [ ] Bug fix
- [ ] Editor tooling or developer workflow
- [ ] Tests, build, CI, or infrastructure
- [ ] Documentation or maintenance

## Player and design impact

<!-- Describe the intended player experience. Note balance, difficulty, controls, pacing, or save-data changes. Use "None" if this is not player-facing. -->

## Implementation notes

<!-- Call out important architecture choices, new dependencies, scene/prefab changes, data migrations, or non-obvious constraints. -->

## Verification

### Automated tests

<!-- List tests added/updated and relevant CI checks. -->

- [ ] EditMode tests
- [ ] PlayMode tests
- [ ] Repository validation
- [ ] Not applicable (explain below)

### Manual test plan

<!-- Give reviewers reproducible steps, expected results, and any setup/seed data. -->

1.

### Test coverage

<!-- List what you actually tested. Delete entries that do not apply. -->

- Unity editor version: `6000.4.10f1`
- Scenes/levels:
- Platforms/build targets:
- Input methods/controllers:
- Save/load or upgrade path:
- Performance-sensitive scenario:

## Evidence

<!-- Add before/after screenshots, GIFs, video, profiler captures, logs, or test output for visual/gameplay changes. -->

## Risks and rollback

<!-- What could regress? Note dependencies, known limitations, feature flags, and how to revert or disable the change. -->

## Reviewer focus

<!-- Point reviewers to the decisions or files where feedback is most valuable. -->

## Author checklist

- [ ] The PR is focused and its title clearly describes the change.
- [ ] I tested the affected player flow and relevant edge cases.
- [ ] New or changed behaviour has appropriate automated tests, or I explained why not.
- [ ] Scenes, prefabs, ScriptableObjects, and `.meta` files are included where required.
- [ ] I avoided unrelated Unity asset reserialization and large binary changes.
- [ ] I checked console output and introduced no unexpected errors or warnings.
- [ ] I considered frame time, allocations, loading, and build size where relevant.
- [ ] I considered save compatibility, accessibility, localisation, and input rebinding where relevant.
- [ ] Documentation and setup instructions are updated where needed.
- [ ] CI is passing, or each failing check is explained.
