# Asset placement guide

Use this directory as a routing point rather than placing loose assets at its root.

## Where should a file go?

1. If the team created it specifically for Lumenfall, put it in `_Project`.
2. If it came from the Asset Store, another repository, or an external package, put the
   complete package in `ThirdParty`.
3. If it is tutorial, demo, or starter content that can be removed later, put it in
   `Samples`.

Within `_Project`:

- Player-facing art or audio belongs in `Art/<Discipline>`.
- Shared Input System assets belong in `Input`.
- Playable scenes belong in `Scenes`.
- Unity configuration assets belong in `Settings`.
- Runtime behaviour, feature tests, and feature-owned data belong in `Systems/<Feature>`.

Do not commit `Library`, `Temp`, `Logs`, generated IDE files, or build output. Always move
an asset together with its `.meta` file so scene, prefab, and ScriptableObject references
retain their GUIDs.
