# PR 14 feature acceptance

Target revision: 2ea5ab6b1e23b87ebea2905f64752b3e3cbbc73c

4 methods / 4 cases. Category: PR14. Namespace: Lumenfall.FeatureAcceptance.

Coverage: Inclusive Manhattan reveal boundary and negative radius rejection; zero-radius pickup emits reveal before collect once; unknown/negative sonar and pickup cannot mutate; same pickup id on two maps keeps collection independent.

Existing coverage and limits: Existing LostEchoesRevealEventTests and Katrina tests cover hooks and quest progression. These cases use untagged-event pickups and do not alter live runtime flags. No sonar audio/visual animation or UI/input.

Run through the repository feature-acceptance runner against this exact PR revision, copying this EditMode directory to the isolated Unity project's feature-acceptance test assembly. These source files deliberately live outside Assets in the integration checkout.

Isolation: world service hosts are inactive and destroyed after each case; no PlayerPrefs, save files, provider registrations, or DeleteAll calls. Fresh runtimes are used where possible. Reflection in PR05 is limited to fixture isolation and never used as the behavior under test.

Validation at author handoff: APIs and assertions checked against git show of the target revision; diff whitespace check passed. Unity compilation and execution remain assigned to the root runner and are not claimed passed here.
