# Lumenfall

Lumenfall is a Unity game project developed for FIT3161. The repository contains the Unity project, project-authored design notes, and first-party runtime assets.

## Getting started

Open [Lumenfall](Lumenfall) as the Unity project root with Unity `6000.4.10f1`. First-party content is under [Lumenfall/Assets/_Project](Lumenfall/Assets/_Project); imported packages and Unity sample content are kept separately under `Assets/ThirdParty` and `Assets/Samples`.

## Repository guide

- [Design notes](docs) cover the cast, quests, spells, and world.
- [First-party systems](Lumenfall/Assets/_Project/Systems) contain feature-oriented runtime code, tests, documentation, and owned data.
- [Unity project guide](Lumenfall/README.md) describes the project layout.
- [Grid movement guide](Lumenfall/Assets/_Project/Systems/GridMovement/README.md) documents the implemented tile-grid movement system.
- [Game events guide](Lumenfall/Assets/_Project/Systems/GameEvents/README.md) documents the implemented event, dialogue, and quest-state system.

The files in `docs/` describe intended game content. System READMEs document repository conventions and behaviour present in the project; design notes should not be treated as an implementation status report.
