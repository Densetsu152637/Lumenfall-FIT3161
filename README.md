# Lumenfall

Lumenfall is a Unity game project developed for FIT3161. The repository contains the Unity project, project-authored design notes, and first-party runtime assets.

## Getting started

Open [Lumenfall](Lumenfall) as the Unity project root with Unity `6000.4.10f1`. First-party content is under [Lumenfall/Assets/_Project](Lumenfall/Assets/_Project); imported packages and Unity sample content are kept separately under `Assets/ThirdParty` and `Assets/Samples`.

## Repository guide

- [Download a Windows game bundle](https://github.com/Densetsu152637/Lumenfall-FIT3161/releases): extract the entire game ZIP and run `Lumenfall.exe`. See [CI and release instructions](.github/CI.md) for publishing builds.

- [Design notes](docs) cover the cast, quests, spells, and world.
- [First-party systems](Lumenfall/Assets/_Project/Systems) contain feature-oriented runtime code, tests, documentation, and owned data.
- [Unity project guide](Lumenfall/README.md) describes the project layout.
- [Grid movement guide](Lumenfall/Assets/_Project/Systems/GridMovement/README.md) documents the implemented tile-grid movement system.
- [Game events guide](Lumenfall/Assets/_Project/Systems/GameEvents/README.md) documents the implemented event, dialogue, and quest-state system.
- [Startup menu guide](Lumenfall/Assets/_Project/Systems/Startup/README.md) documents save-slot selection, backdrop restoration, settings, and input gating.
- [Area audio guide](Lumenfall/Assets/_Project/Systems/Audio/README.md) documents area profiles, looping ambience, transitions, and authoring.
- Run `powershell -ExecutionPolicy Bypass -File scripts/compile_runtime.ps1` followed by `scripts/compile_editmode_tests.ps1` for licensed-editor-independent runtime and EditMode test assembly compilation.

The files in `docs/` describe intended game content. System READMEs document repository conventions and behaviour present in the project; design notes should not be treated as an implementation status report.
