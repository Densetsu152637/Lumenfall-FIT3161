# PR23 graphics recommendation acceptance

Target: `784c1074594927e27536921061e5732f0e6c6fa0`. Category: `PR23`. EditMode: 3 cases.

New vectors:
- All fourteen nonempty partial preference combinations preserve existing explicit values and fill only absent keys from the recommended defaults.
- A later stronger hardware recommendation cannot overwrite a previously persisted Low choice.
- Recommendation is side-effect free with respect to PlayerPrefs, even for extreme capacity; mobile defaults remain stable when hardware/device values are unknown.

Fixture preserves existence and integer values for the four documented graphics keys and restores them in teardown. It never uses PlayerPrefs.DeleteAll and touches no gameplay saves.

Manual limits: recommendations are capacity heuristics, not measured frame-rate claims. Actual renderer setting application, restart/startup UI, visual quality, and heterogeneous device performance need PlayMode/manual validation.
