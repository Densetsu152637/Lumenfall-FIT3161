# PR 01 feature acceptance

Target revision: de7745631b7776d16ce1d7c66d0f6eb879448887

4 methods / 4 cases. Category: PR01. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Reset completion observes committed progress and restored feedback; unknown reset preserves effects; effect acknowledgement permits replay; invalid progress is silent.

Existing coverage and limits: Existing SpellFeedbackResetTests covers caster rejection and occupied-grid reset. These cases add public event/progress composition; no input/UI animation or occupied scene test.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
