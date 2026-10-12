# PR 03 feature acceptance

Target revision: 927f2184fd80c7900d553a3002f28529b34df36b

4 methods / 4 cases. Category: PR03. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Leaving initial dialogue cannot activate lesson; active reminder survives save; completed callback remains consumed across save; reset restores both First Light and next-lesson gates.

Existing coverage and limits: Existing FirstLightIntegrationTests covers Ampere teaching/casting and monitor lifecycle. This suite uses fresh catalog/runtime instances to add authored dialogue/save progression composition; no live caster, UI or map traversal.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
