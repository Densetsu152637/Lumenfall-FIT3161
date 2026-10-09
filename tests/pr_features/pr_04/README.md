# PR 04 feature acceptance

Target revision: 446ae475d4621c3c54df3b2e549f77b23d02b3d8

4 methods / 4 cases. Category: PR04. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Upstream disconnection depowers downstream chain; live stretched endpoint stops and recovers propagation; ordinary dark relic breaks conductor relay; required connection power threshold follows save restore.

Existing coverage and limits: Existing StringThemTogetherTests covers board interaction. These tests exercise public network authority without board presentation, keyboard input, cable mesh, or scene movement.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
