# Settings

Store project-owned Unity configuration assets here. Examples include render-pipeline
assets, renderer data, volume profiles, lighting settings, presets, and shared quality
profiles.

Do not move files from the `ProjectSettings` directory into this folder; Unity manages
those settings separately at the project root. System-specific ScriptableObject data
belongs with its owning folder under `Systems`, while configuration shipped with an
imported asset stays in `ThirdParty`.

Explain platform, rendering, or performance implications when changing these assets.

The startup [graphics options](../Systems/Startup/README.md#graphics-options) adjust the
active URP pipeline through a transient runtime copy and toggle its existing SSAO feature.
`PC_Renderer` uses deferred rendering and includes SSAO; `Mobile_Renderer` uses forward
rendering without SSAO. Camera FXAA/SMAA support both paths.

`UniversalRenderPipelineGlobalSettings.asset` disables **Strip Unused Variants** to retain
both sides of runtime ambient-occlusion and shadow-cascade switches in player builds. This can
increase shader compilation time and build size. Post-processing variant stripping remains
enabled. If unused-variant stripping is re-enabled, ship authored configurations covering all
runtime combinations and verify the options in a player build.
