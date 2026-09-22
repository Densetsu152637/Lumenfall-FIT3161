# Lumenfall Unity project

Open this directory as the project root in Unity `6000.4.10f1`.

## Asset layout

- `Assets/_Project` contains first-party game content. Add new Lumenfall work here.
- `Assets/_Project/Systems` is feature-oriented: runtime code, tests, and system-owned
  resources stay together.
- `Assets/ThirdParty` contains imported asset packs. Avoid modifying these files unless
  an import or compatibility fix requires it.
- `Assets/Samples` contains removable Unity starter/sample content.

See [`Assets/_Project/README.md`](Assets/_Project/README.md) for the detailed conventions.
The tile-grid movement system is documented in
[`Assets/_Project/Systems/GridMovement/README.md`](Assets/_Project/Systems/GridMovement/README.md).
The startup flow is documented in
[`Assets/_Project/Systems/Startup/README.md`](Assets/_Project/Systems/Startup/README.md).
