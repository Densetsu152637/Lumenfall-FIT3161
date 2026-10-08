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
| `Content / Authored runtime assets` | Runtime WorldArea/NpcCatalog/prefab parity, transforms, active GUID portal graph/arrivals, monitor references, lesson gates and supported optional metadata gates. |
| `Content / Runtime world` | Shipped map dimensions/glyphs, portal targets, NPC assets, and dialogue/quest references. |
| `Content / Quest and NPC dialogue` | Authored quest lifecycle branches, prerequisites, completion events, ambient dialogue, and NPC markers. |
| `Content / NPC layers` | Shapes and exact mirrors of authoring/runtime NPC layers. |
| `Content / Garden and portals` | Garden bounds, walkability, tree blocks, Katrina placement, reserved markers, and reciprocal east-gate portals. |
| `Unity EditMode tests` | `Lumenfall.GameEvents.Tests`: event execution, dialogue, quest lifecycle, spell state, save-slot policy, and text reveal. |
| `Unity PlayMode tests` | `Lumenfall.World.PlayModeTests`: SampleScene startup, all active authored routes, world save/restore, and quest-gated NPC refresh. |

The workflow lint, integrity and content checks need no Unity Editor or license. Python is fixed to 3.12 in CI; `scripts/requirements-validation.txt` pins PyYAML for Unity YAML inspection. The harness also runs PowerShell portability fixtures when PowerShell is available (hosted Ubuntu runners provide `pwsh`). Content checks run independently, and both Unity modes run after the fast checks pass. A failure in one matrix job does not cancel its siblings.

Both Unity jobs first print the runner's available disk space, then reclaim space on
the disposable GitHub-hosted Ubuntu runner by removing unrelated preinstalled .NET,
Android, GHC, ghcup, and CodeQL SDKs and unused Docker images. They fail closed on
self-hosted runners. The step leaves the workspace and cached Unity `Library` intact;
the Unity toolchain is provided by GameCI. If extraction runs out of space again,
compare the before and after `df -h /` diagnostics in the job log. Unity tests still
run and continue to fail when the required license secrets are missing.

Each Unity job reads and validates `m_EditorVersion` from `ProjectVersion.txt` and
uses the matching GameCI Ubuntu base image, which omits build modules the tests do not
need. Its Unity `Library` cache is keyed by Unity version, packages, and commit; it
restores only caches for the same Unity/package set and saves a new cache after a
mode-specific XML result exists. The test runner's GitHub Checks reporter is disabled
because the artifact upload is the supported result record and the reporter requires
Checks API write access. Test failures still fail the job, and XML results remain
available as artifacts.


Unity uses the version in `Lumenfall/ProjectSettings/ProjectVersion.txt` (currently
`6000.4.10f1`) through GameCI. Each mode selects its first-party test assembly;
third-party sample tests do not belong to the game acceptance suite. The NUnit
result validator fails missing, malformed, empty, failed, or entirely skipped
results, and requires at least one passing test from the intended assembly.
Results and logs are uploaded for 14 days even when the test step fails, and
passing-test counts appear in the job summary. The GameCI CLI is pinned to
`v0.1.72` to avoid per-run GitHub latest-release discovery. The runner uses its
supported default coverage arguments; no coverage threshold is an acceptance gate.

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

### Upload the license file directly

You can use the whole `.ulf` file without opening it or copying its XML. Install
[GitHub CLI](https://cli.github.com/) and run `gh auth login`, then put these local
values in the ignored repository-root `.env`:

```dotenv
UNITY_LICENSE_FILE='C:/ProgramData/Unity/Unity_lic.ulf'
UNITY_EMAIL='your Unity account email'
UNITY_PASSWORD='your Unity account password'
```

From the repository root, run:

```sh
python scripts/configure_unity_secrets.py --check
python scripts/configure_unity_secrets.py
```

The helper reads the complete license file and uploads it as `UNITY_LICENSE`,
along with the two account secrets, using GitHub CLI's encrypted secret storage.
Secret values are passed through stdin and are never printed or put into command
arguments. GitHub Actions still uses the same three secrets; it does not read your
local `.env` or local license path. `.ulf` is the Unity license extension.

Use `--env-file <path>` for a `.env` in another checkout, `--license-file <path>`
to override the license file location, or `--repo owner/name` for another repository.
Relative `UNITY_LICENSE_FILE` paths are resolved beside `.env`.

GitHub does not pass repository secrets to workflows created from forks. Contributors
who need the Unity job to run should use a branch in this repository, or a maintainer
can rerun a trusted contribution from an in-repository branch.

## Running locally

From the repository root:

```sh
python -m pip install -r scripts/requirements-validation.txt
python -m unittest discover --start-directory scripts/tests --verbose
python scripts/validate_unity_project.py Lumenfall
python scripts/validate_authored_content.py Lumenfall
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
the authored runtime validator checks the shipped prefab/component graph. The licensed PlayMode suite exercises travel when run.

The garden validator checks gameplay content invariants; the original task's
temporary recovery snapshot and frozen forest byte hash are removed so legitimate
map edits and CRLF checkouts do not fail CI. Integrity validation accepts BOM-marked
UTF-16 `.txt`/`.md` vendor documentation and ignores
long decorative divider lines while retaining real conflict-marker checks.

## Downloadable game releases

[`Release game bundle`](workflows/release-build.yml) builds a **Windows x64 Mono**
player with the Unity version in `Lumenfall/ProjectSettings/ProjectVersion.txt`.
It uses the enabled scenes in `EditorBuildSettings.asset` (currently
`Assets/_Project/Scenes/SampleScene.unity`) and the existing player settings.
It requires the same three Unity secrets described above. The Windows build cache
is separate from the EditMode cache.

After this workflow is merged, it runs automatically on every push to `main`,
including the merge that adds the workflow. A successful build creates a
`main-<workflow-run-id>` prerelease with the runnable ZIP and checksum. Its tag
points to the exact pushed commit. These prereleases do not replace the latest
stable release, and rerunning the same workflow updates the same release assets.
Each main push builds independently, so a newer push does not cancel an older one.

You can also:

- Push a version tag such as `v0.1.0` to build the tagged commit and automatically
  create a GitHub Release with generated notes. Tags containing a hyphen, such as
  `v0.1.0-rc.1`, create prereleases.
- Publish a release through GitHub's **Releases > Draft a new release** UI. Publishing
  a release or prerelease builds its tag and attaches the bundle to that release.
- Select **Actions > Release game bundle > Run workflow** to build the selected
  branch or tag and download a test bundle from the workflow's artifacts. Manual
  runs do not create or modify GitHub Releases.

For example, from an up-to-date `main` checkout:

```sh
git tag v0.1.0
git push origin v0.1.0
```

The release assets are `Lumenfall-<tag>-windows-x64.zip` and its `.zip.sha256`
checksum. Download the game ZIP from the release's **Assets**, extract the entire
archive, and run **Lumenfall.exe**. Keep the data directory, DLLs, Mono runtime,
and all other included files beside the executable. Unity Editor is not required.
GitHub's automatically generated **Source code** archives contain the project
sources, not a runnable player. The repository's visibility also governs who can
access releases; this repository is currently private.

Every ZIP includes launch instructions, the repository license, and
`build-info.json` with the source commit, triggering tag/ref, Unity version, and
platform. Main builds use their `main-<workflow-run-id>` identifier as the bundle
version. All builds check out the exact event commit or release tag, even if the
branch advances while the workflow is queued. Player version settings are
preserved; changing the release tag does not change `PlayerSettings.bundleVersion`.
Windows is the only bundled platform.

Packaging fails before publication if the executable, engine DLL, scene data,
managed assemblies, or Mono runtime are missing or empty. Successful bundles and
available build logs are retained as workflow artifacts for 14 days. Releases keep
their attached assets independently of that retention period. A failed build
does not create a release on a main or tag push; an already-published release remains
visible without the bundle until a successful rerun. Reruns replace the same
named ZIP/checksum assets and preserve existing release notes.

To package a local Windows Mono player and verify its checksum:

```sh
python scripts/package_game.py build/StandaloneWindows64 dist --version v0.1.0 --commit <source-commit-sha>
cd dist
sha256sum --check Lumenfall-v0.1.0-windows-x64.zip.sha256
```

Only the publication job receives `contents: write`; build jobs have read access.
Tag pushes and release publication are serialized per tag. GitHub's built-in token
does not trigger another workflow when it creates a release. No release or tag is
created by pull-request validation.

## Recommended branch protection

Require all check names listed above on `main`, plus pull requests and any
reviews required by the team. The Unity checks require configured license secrets;
do not treat a missing license as a passing test run.

## Runtime content baseline and offline compilation

The source/baked parity, active interior routes, NPC resource paths, and lesson gates now pass the authored runtime check. The original findings and their repairs are recorded in `docs/implementation/feature-content-validation-ci.md`; a failing check must not be treated as a successful gameplay audit. Planned glyph-ineligible and reserved portals are excluded from live-route checks unless a live component references them. Custom condition/action types remain extensible; built-in quest/event/dialogue references are checked with runtime-compatible case handling.

For clean-worktree offline compilation without launching Unity, see `docs/runtime-data-contract.md`. Supply an explicit read-only imported reference project; the helper compiles current asmdef membership and filters all stale first-party references. `-PrepareOnly` is used by portability fixtures and does not claim a successful C# compile.
