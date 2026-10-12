# PR 05 feature acceptance

Target revision: c3fbad214b29a397257e66d94d10a1b7c5423f41

6 methods / 8 cases. Category: PR05. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Vis normalizes authored negative/overflow quarter turns and repeated casts; conflicting targets reject silently; marker reset preserves other markers and unrelated properties; save retains alignment but authored reset angle; no-op reset notifications; map-specific reset baselines.

Existing coverage and limits: Existing OutOfAlignmentTests covers quest/handoff gates. Direct casts temporarily substitute isolated manager/runtime and world singletons via reflection, restoring exact prior references and inventory state; hosts remain inactive to avoid lifecycle/save-provider registration. No UI/input or PlayMode rendering.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
