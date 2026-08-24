# Camera system

Store runtime camera behaviour and camera-specific tests or configuration here. This
includes following, framing, bounds, transitions, shake, and coordination with player or
scene state.

General rendering configuration belongs in `_Project/Settings`; camera animation and
visual assets belong in `_Project/Art`. If the system grows, separate runtime and tests
with `Runtime` and `Tests/EditMode` or `Tests/PlayMode` subfolders and add assembly
definitions only when a real compilation boundary is useful.
