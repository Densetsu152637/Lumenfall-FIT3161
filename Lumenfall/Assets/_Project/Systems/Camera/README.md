# Camera system

Store runtime camera behaviour and camera-specific tests or configuration here. This
includes following, framing, bounds, transitions, shake, and coordination with player or
scene state.

`PlayerOcclusionReveal` applies the player-centered screen-space cutout to the patched Polytope
vegetation shaders. It also makes runtime material copies for opaque URP Lit / Simple Lit / Standard
materials on the campus `Pavilion`, `sakura+tree`, and `Old+Tree` models and on the authored Library
model, using `Resources/PlayerOcclusionLit.shader`. The copies are restored when the camera component
is disabled or an area no longer contains a supported occluder; imported models and shared material
assets are not modified. Transparent materials are left alone.

`CameraFollow2_5D` exposes `TryGetFollowPose` (where the camera rests when caught up with its
target) and `ResetSmoothing`. Overlays that move the camera themselves, such as the startup menu's
drift, use these to hand the camera back without a jump.

The camera rests due south of the player (offset x = 0), so the grid lines up with the screen and
W/A/S/D move up/left/down/right on screen. Holding the left mouse button and dragging, or tilting the
gamepad right stick, swings the view up to 35° either way and a little up or down to peek past walls and
through doorways; it eases back on release. Movement is never remapped, which is why the swing stays
under 45°. Peeking is off whenever the player cannot move (menus, dialogue, cutscenes, the spell wheel,
the journal), and Reduced Motion snaps it back instead of easing. The limits and speeds are on the
component under **Peek**.

`PlayerOcclusionReveal.SetRevealZone` widens the see-through cutout over an area. While the player
stands in it, the cutout becomes an ellipse centred on the area, sized to hold all of it on screen,
and reaching back to its far edge. It shrinks back to the usual circle over a falloff just outside.
The blend follows the player's position rather than time. The Deep Forest rune dais
(`OrioRuneDiscPuzzle.RevealDais`) uses it so trees never hide part of the puzzle.

With **Reduce camera motion** on, conversation framing cuts instead of easing. A dialogue line with
`cameraFocus` metadata (Kit's tour) cuts to that place and back, and cutscene camera steps jump
straight to their framing. The usual zoom onto the speakers stays off.

**Options > Gameplay > Camera angle** switches between **Straight** (the default, above) and
**Diagonal**, the original corner view from the south-east, where W moves up and to the right.
`CameraAngleSetting` saves the choice in PlayerPrefs (`lumenfall.camera-angle`), outside story save
slots. Diagonal turns the serialized offset 45° around the player, so tune the offset for Straight
only. Switching swings the camera round over the conversation blend time, or snaps with Reduced Motion.

General rendering configuration belongs in `_Project/Settings`; camera animation and
visual assets belong in `_Project/Art`. If the system grows, separate runtime and tests
with `Runtime` and `Tests/EditMode` or `Tests/PlayMode` subfolders and add assembly
definitions only when a real compilation boundary is useful.
