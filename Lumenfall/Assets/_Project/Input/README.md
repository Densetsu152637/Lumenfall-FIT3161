# Input

Store shared Unity Input System assets here, including action maps, control schemes,
binding overrides, and input-related configuration used across multiple systems.

Add gameplay code that consumes input to its owning folder under `Systems`; for example,
grid movement input handling belongs in `Systems/GridMovement`. Device-specific UI art
belongs in `Art`, not here.

When changing an action name or map, search for serialized `InputActionReference` usage and
update the affected documentation and manual test plan in the pull request.
