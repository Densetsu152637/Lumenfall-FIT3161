# PR 20 feature acceptance

Target revision: 1a395c93172058cd77610c0d186fec1325c8ee86

5 methods / 5 cases. Category: PR20. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Ordered trap enumeration excludes start; maximum lane length boundary and duplicate authoring; entire footprint fits lane; count/position/frozen proof agreement; interrupted progress requires zero proof inside lane.

Existing coverage and limits: Existing ColdFeetTrialTests covers casting, occupancy, reset and saves. This suite exercises public authored lane/proof validation in isolation; no additional live lifecycle or PlayMode movement checks.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
