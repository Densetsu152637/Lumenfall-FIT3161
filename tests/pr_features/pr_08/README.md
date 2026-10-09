# PR 08 feature acceptance

Target revision: 0a6df7af4eb0fe301c492e06f81f2938a72b4608

5 methods / 13 cases. Category: PR08. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Quarter turns normalize negative/overflow revolutions; imported off-axis rotation snaps to nearest quarter; solved angle accepts equivalent revolutions; nonfinite angles reject; four turns form a complete cycle.

Existing coverage and limits: Existing OrioPatchNotesTests covers lesson and rune-room interaction. This suite tests public shared gear-angle contracts only; no gear mesh/animation, room teleport, or live spell lifecycle.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
