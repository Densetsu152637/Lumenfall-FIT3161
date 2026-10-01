# Pull request validation

`Pull Request Validation` runs on every pull request (opened, updated, reopened,
or marked ready for review), pushes to `main`, and manual dispatches. Every PR
runs the full suite, including documentation and workflow changes, so required
checks are never left pending by path filters.

## Checks

| Check | What it verifies |
| --- | --- |
| `Workflow lint` | actionlint 1.7.12 checks workflow syntax, expressions, action inputs, and shell scripts. The downloaded binary is verified against the official release checksums. |
| `Repository integrity` | Python harness regression tests, Unity metadata/GUIDs, JSON, enabled build scenes, case-safe paths, merge conflicts, and tracked generated files. |
| `Content / Runtime world` | Shipped map dimensions/glyphs, portal targets, NPC assets, and dialogue/quest references. |
| `Content / Quest and NPC dialogue` | Authored quest lifecycle branches, prerequisites, completion events, ambient dialogue, and NPC markers. |
| `Content / NPC layers` | Shapes and exact mirrors of the six authoring/runtime NPC layers. |
| `Content / Garden and portals` | Garden bounds, walkability, tree blocks, Katrina placement, reserved markers, and reciprocal east-gate portals. |
| `Unity EditMode tests` | `Lumenfall.GameEvents.Tests`: event execution, dialogue, quest lifecycle, spell state, save-slot policy, and text reveal. |
| `Unity PlayMode tests` | `Lumenfall.World.PlayModeTests`: SampleScene startup, all active authored routes, world save/restore, and quest-gated NPC refresh. |

The first six checks need no Unity editor, license, or Python dependencies. Python
is fixed to 3.12 in CI. All four content checks run independently, and both Unity
modes run after the fast checks pass. A failure in one matrix job does not cancel
its siblings.

Unity uses the version in `Lumenfall/ProjectSettings/ProjectVersion.txt` (currently
`6000.4.10f1`) through GameCI. Each mode selects its first-party test assembly;
third-party sample tests do not belong to the game acceptance suite. The NUnit
result validator fails missing, malformed, empty, failed, or entirely skipped
results, and requires at least one passing test from the intended assembly.
Results and logs are uploaded for 14 days even when the test step fails, and
passing-test counts appear in the job summary. Coverage instrumentation is disabled
because this project does not configure the Unity Code Coverage package.

Library caches are separated by OS, test mode, editor/package hash, and commit.
Restore fallback stays within the same mode and dependency versions. Workflows
use read-only repository permissions; checkout credentials are not persisted.
GameCI check-run publishing is disabled explicitly because the job status,
summary, and uploaded artifacts already report the verdict.

## Unity license setup

Unity jobs fail explicitly if licensing is not configured. Follow the
[GameCI activation guide](https://game.ci/docs/github/activation/), then add these
encrypted repository secrets under **Settings > Secrets and variables > Actions**:

- `UNITY_LICENSE`: the activated Unity license file contents
- `UNITY_EMAIL`: the Unity account email used to activate it
- `UNITY_PASSWORD`: that account's password

GitHub withholds repository secrets from fork pull requests. Fast checks still
run, but Unity jobs report missing licensing secrets. For a trusted fork
contribution, a maintainer can place the reviewed changes on an in-repository
branch. Keep the normal `pull_request` trigger; do not expose secrets with
`pull_request_target`.

## Running locally

From the repository root:

```sh
python -m unittest discover --start-directory scripts/tests --verbose
python scripts/validate_unity_project.py Lumenfall
python scripts/validate_runtime_world.py
python scripts/validate_npc_dialogue.py
python scripts/validate_npc_layers.py
python scripts/validate_garden_portals.py
```

With [actionlint](https://github.com/rhysd/actionlint) installed, run `actionlint`.
In Unity, run the first-party assemblies from **Window > General > Test Runner**
in both **EditMode** and **PlayMode**. After exporting NUnit XML results, verify
that tests actually ran:

```sh
python scripts/validate_unity_results.py artifacts/editmode --assembly Lumenfall.GameEvents.Tests
python scripts/validate_unity_results.py artifacts/playmode --assembly Lumenfall.World.PlayModeTests
```

`validate_layouts.py` is a legacy split-manifest validator and assumes unnumbered
source maps and fully connected planned regions. Current authoring maps include
rulers/row labels and reserved future areas, so it is not a required CI check.
`validate_runtime_world.py` prints pending legacy portal arrivals as diagnostics;
the PlayMode suite checks the active routes in the shipped prefabs.

The garden validator checks gameplay content invariants; the original task's
temporary recovery snapshot and frozen forest byte hash are removed so legitimate
map edits and CRLF checkouts do not fail CI. Integrity validation accepts BOM-marked
UTF-16 `.txt`/`.md` vendor documentation and ignores
long decorative divider lines while retaining real conflict-marker checks.

## Recommended branch protection

Require all eight check names listed above on `main`, plus pull requests and any
reviews required by the team. The Unity checks require configured license secrets;
do not treat a missing license as a passing test run.
