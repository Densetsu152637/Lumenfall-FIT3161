# PR18 camera and interiors acceptance

Target: `36dc7c3d523d1340e9b47a4a87842eb3574bb1f5`. Category: `PR18`. EditMode: 3 cases. PlayMode: 2 cases.

New vectors:
- Fractional grid insets preserve scaled cell-center bounds and repeated clamping is stable.
- Translating a room and anchor by a whole-cell offset translates the final framing coordinate.
- Snap detection rejects nonfinite thresholds/distances and negative squared distance.
- Reduced-motion following tracks small movement during a gameplay pause without moving the actor.
- Clearing the camera target releases an actual physics-hit occlusion override immediately and preserves camera pose.

PlayMode fixtures own their camera, actor, occluder, and restore timeScale. They do not load scenes or alter loader/provider state.

Manual limits: interior composition, camera comfort, all map transitions, and visually important objects' occluder authoring still require interactive review. Physics/native PlayMode execution is required for lifecycle evidence.
