# Player Occlusion Reveal prototype

SampleScene's Main Camera has PlayerOcclusionReveal, targeting the existing Player.
During that camera's URP render, it publishes a player-centered screen-space circle.
Foreground vegetation pixels inside the circle are dither-clipped; the center is
clear and the edge fades smoothly in coverage. It is not alpha-blended glass.

## Controls

- Radius: outer radius in fractions of camera height (default 0.16).
- Feather: gradient width in the same units (default 0.07).
- Center Opacity: retained coverage at the center (0 = fully clear).
- Target Offset: world-space offset above the Player root (default Y = 0.8).
- Depth Padding: vegetation must be in front of the focus plane by this distance.
- View Offset: camera-right / camera-up offset in world units. SampleScene uses
  (0, 0), centering the reveal on the Player plus Target Offset without a right/up shift.
- SampleScene overrides Radius to 0.4 and Feather to 0.175 (2.5 times the original).
- Disable the component to restore normal rendering. No gameplay state changes.

## Scope and limitations

The Polytope PT_Vegetation_Foliage_Shader and PT_Vegetation_Opaque_Shader
have small integration hooks into the project-owned PlayerOcclusionReveal.hlsl.
Forward, DepthOnly, DepthNormals, and opaque GBuffer passes use the same clipping.
ShadowCaster and Meta are unchanged: tree shadows remain, including inside the hole.
Existing wind, materials, colliders, grid occupancy, portals, and save data are unchanged.
Other vegetation using these same shaders is affected too, not just trees.

Terrain Paint Trees works when the rendered tree mesh uses these shaders. Billboard
and other shaders are not covered. Keep nearby trees in mesh LOD for this prototype.
This is a non-XR, single-game-camera prototype; motion-vector/TAA artifacts and
third-party shader re-generation/upgrades need review before production use.
Amplify regeneration or a vendor update can overwrite the integration hooks.

## Manual validation (not yet executed)

1. Open SampleScene, enter Play Mode, select START.
2. Walk behind a tree using one of the supported shaders: the center clears and
   its circular edge restores foliage progressively; the Player and ground stay solid.
3. Walk past the tree: vegetation behind the focus plane remains opaque.
4. Test wide and tall Game views; the reveal should remain circular.
5. Compare Radius 0.1/0.2, Feather 0.02/0.08, Center Opacity 0/0.25.
6. Disable the component: original vegetation rendering returns immediately.
7. Check Terrain-painted trees near the camera as well as manually placed prefabs.
8. Check Console for shader compilation errors and inspect depth/SSAO edge artifacts.

Source integration and scene references were checked statically. Unity compilation
and visual verification must still be performed in the Editor.
