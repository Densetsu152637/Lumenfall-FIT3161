# PR15 production UI acceptance

Target: `29a175e83e54736fbeeb7bf45b2883e7eb3addd7`. Category: `PR15`. EditMode: 3 cases.

New vectors:
- Two overlay owners block each other; releasing one allows only its sibling after the closing frame; releasing the last consumes exactly its close frame.
- Reacquiring an already open owner on a later frame does not create a new input transition. Clear removes ownership and stale transition state.
- Focus recovers from out-of-range saved indices, respects the first enabled choice, and terminates on a disabled single-slot menu.

Manual limits: gamepad/keyboard input devices, screen layout, dialogue/wheel handoff, actual click focus, and accessibility need interactive review. This suite uses isolated policy instances; it does not clear or mutate the global overlay gate.
