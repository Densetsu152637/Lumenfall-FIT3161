# Scenes

Store first-party playable scenes here. Use descriptive subfolders once scene roles become
distinct, such as `Gameplay`, `UI`, `Testing`, or `Lighting`.

- Add production scenes to `ProjectSettings/EditorBuildSettings.asset` through Unity's
  Build Profiles/Build Settings window.
- Keep vendor demo scenes inside their package under `Assets/ThirdParty`.
- Keep tutorial and disposable example scenes under `Assets/Samples`.
- Put reusable objects in prefabs owned by the relevant system or art area instead of
  duplicating them across scenes.

Avoid unrelated scene saves in a pull request because Unity scene YAML is difficult to
review and merge.
