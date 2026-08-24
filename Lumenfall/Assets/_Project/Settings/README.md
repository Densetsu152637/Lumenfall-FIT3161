# Settings

Store project-owned Unity configuration assets here. Examples include render-pipeline
assets, renderer data, volume profiles, lighting settings, presets, and shared quality
profiles.

Do not move files from the `ProjectSettings` directory into this folder; Unity manages
those settings separately at the project root. System-specific ScriptableObject data
belongs with its owning folder under `Systems`, while configuration shipped with an
imported asset stays in `ThirdParty`.

Explain platform, rendering, or performance implications when changing these assets.
