# Camera system

Store runtime camera behaviour and camera-specific tests or configuration here. This
includes following, framing, bounds, transitions, shake, and coordination with player or
scene state.

`PlayerOcclusionReveal` applies the player-centered screen-space cutout to the patched Polytope
vegetation shaders. In the campus `Garden Area`, it also makes runtime material copies for opaque
URP Lit / Simple Lit / Standard materials on the `Pavilion`, `sakura+tree`, and `Old+Tree` models,
using `Resources/PlayerOcclusionLit.shader`. The copies are restored when the camera component is
disabled; imported model and shared material assets are not modified. Transparent pavilion materials
are left alone.

`CameraFollow2_5D` exposes `TryGetFollowPose` (where the camera rests when caught up with its
target) and `ResetSmoothing`. Overlays that move the camera themselves, such as the startup menu's
drift, use these to hand the camera back without a jump.

General rendering configuration belongs in `_Project/Settings`; camera animation and
visual assets belong in `_Project/Art`. If the system grows, separate runtime and tests
with `Runtime` and `Tests/EditMode` or `Tests/PlayMode` subfolders and add assembly
definitions only when a real compilation boundary is useful.
