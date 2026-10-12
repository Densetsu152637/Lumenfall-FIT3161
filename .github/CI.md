# Pull request validation

`Pull Request Validation` runs when a pull request is opened, updated, or reopened.
Drafts execute the same checks; marking an unchanged PR ready for review does not
repeat them. With no pull-request branch filter, it applies to PRs into
`main` and every other branch. It also runs after changes are pushed to `main`, and
can be started manually from the Actions tab.

The workflow always reports the same two required checks on Ubuntu 24.04:

1. `Repository integrity` runs the Python harness tests and validates Unity metadata,
   JSON files, build scenes, case-safe paths, merge markers, and generated-file hygiene.
   It also selects the needed checks and validates the three PR/feature workflows with actionlint
   1.7.12, downloaded from its official release and verified with a pinned SHA256.
   The Python checks require no third-party packages or secrets.
2. `Unity EditMode tests` is an always-run gate. It requires repository integrity to
   succeed and the separate `Run Unity EditMode tests` job to pass whenever Unity is
   required. Failed, cancelled, missing, or unexpected results fail the gate.

Unity runs for every manual and `main` push, and for draft PRs with relevant changes.
Only PRs changing exclusively `README.md`, `LICENSE`, `.github/CI.md`,
`.github/PULL_REQUEST_TEMPLATE.md`, Markdown files under `docs/`, or the acceptance
catalog's `README.md`, `VALIDATION.md`, and `pr_XX/README.md` files skip the expensive
job. `scripts/select_pr_checks.py` compares the event's exact base and head commits
from the depth-two merge checkout. Renames are treated as deletion plus addition.
Source, assets (including their documentation), tests, packages, project settings,
workflows, shared scripts, and unknown paths all require Unity. Missing commits,
malformed event data, empty diffs, and other detection failures also require Unity.
Comparing commit endpoints may conservatively include changes made on the base since
a PR forked; this can run extra tests but cannot hide source changes. The required
workflow itself has no path filter, so documentation-only PRs still report both checks.

The Unity job checks for license secrets before cleanup, checkout, or cache downloads.
Fork PRs with relevant changes therefore fail quickly when secrets are unavailable;
they do not execute untrusted code with a privileged token. Checkout credentials are
not persisted, and the workflow token has only `contents: read` access.

The EditMode job prints disk space and, only when less than 30 GiB is available,
removes unrelated preinstalled .NET, Android, GHC, ghcup, and CodeQL SDKs one at a time,
stopping when enough space is free. It does not prune Docker images. Cleanup is limited
to disposable GitHub-hosted runners and six minutes; the whole Unity job is limited
to 30 minutes. The workspace and cached Unity `Library` remain intact. Release-build
cleanup is configured independently. Compare the before/after `df -h /` diagnostics
if extraction runs out of space.

The EditMode job reads and validates `m_EditorVersion` from `ProjectVersion.txt` and
uses the matching GameCI Ubuntu base image, which omits build modules the tests do not
need. All three PR/feature GameCI test steps pin the supported `cliVersion` input to
[v0.1.72](https://github.com/game-ci/cli/releases/tag/v0.1.72), already used by this
repository. This makes test setup reproducible and avoids resolving the latest CLI
release separately on every runner.

The EditMode Unity `Library` cache is keyed by OS, Unity version, packages, project
settings, and asset content, so repeated runs with unchanged Unity inputs reuse an
exact cache without uploading another copy. Changes to assets can restore an older
cache with the same Unity/package/settings prefix for incremental import. A cold
cache imports normally. A new cache is saved only after an EditMode XML result exists;
test failures still fail the job. Cache scope follows GitHub's branch isolation and
does not contain license secrets. The test runner's GitHub Checks reporter is disabled
because the artifact upload is the supported result record and the reporter requires
Checks API write access. Test failures still fail the job, and XML results remain
available as artifacts.

Concurrency includes the workflow, event, and PR number or ref. New PR commits cancel
obsolete runs for that PR; independent PRs, main pushes, and manual runs do not cancel
one another. No deployment or release behavior is changed by PR validation.

## One-time Unity license setup

The Unity job deliberately fails rather than silently skipping tests when licensing is
not configured. Follow the GameCI activation process for a Unity Personal license, then
add these encrypted repository secrets under **Settings > Secrets and variables >
Actions**:

- `UNITY_LICENSE`: the contents of the activated Unity license file
- `UNITY_EMAIL`: the Unity account email used to activate it
- `UNITY_PASSWORD`: the Unity account password used to activate it

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

## Running the fast checks locally

From the repository root:

```sh
python -m unittest discover --start-directory scripts/tests --verbose
python scripts/validate_unity_project.py Lumenfall
actionlint .github/workflows/pr-validation.yml .github/workflows/feature-*.yaml .github/workflows/pr-feature-suite.yaml
```

Use actionlint 1.7.12 to match CI. The harness covers the selector's documentation,
source, workflow, lockfile, draft, rename/deletion, missing-history, malformed-event,
and fail-closed cases. `GITHUB_EVENT_NAME`, `GITHUB_EVENT_PATH`, and `GITHUB_OUTPUT`
are the selector's standard Actions inputs/output; it can be rehearsed with sanitized
event JSON and existing local commit SHAs. The release workflow currently uses
`concurrency.queue`, which actionlint 1.7.12 does not recognize; the syntax check above
intentionally targets the three PR and feature workflows maintained here.

Run the game tests from **Window > General > Test Runner > EditMode** in Unity.
Synchronous EditMode fixtures must explicitly provide the world-service singleton and
invoke the lifecycle callbacks they exercise: ordinary MonoBehaviours do not receive
play-mode startup callbacks here. Use the existing `EditModeLifecycle` helper, restore
any previous world instance, and unsubscribe fixture-owned spell casters during cleanup.
Load a quest monitor's definitions through its `Awake` callback before calling
`EvaluatePuzzles`; tests should not depend on `Update` timing. Preserve the production
lesson, puzzle and report requirements rather than bypassing them to grant rewards.

Branch-specific acceptance tests for the active PR snapshot are documented in
[`tests/pr_features/README.md`](../tests/pr_features/README.md). Run
`python scripts/run_pr_feature_tests.py --pr <number> --mode all` to materialize
the pinned PR project and execute its added suite without changing a checkout.
These catalog tests live outside `Assets` and therefore require this runner;
the existing EditMode CI job does not automatically discover them on main.

## Feature acceptance workflows

**Feature acceptance tests** (`feature-acceptance.yaml`) selects and runs the catalog
through one shared matrix. The six logical feature areas remain available through
`scripts/select_pr_feature_suites.py --group <area>`:

| Logical area / CLI group | Target PR suites |
| --- | --- |
| Quests and spells / `quests-spells` | 1, 3, 4, 5, 8, 14, 20 |
| World and story / `world-story` | 6, 10, 12, 16 |
| Presentation and UI / `presentation-ui` | 11, 13, 15, 18, 23 |
| Audio / `audio` | 9, 22 |
| Persistence and recovery / `persistence-recovery` | 7, 19 |
| Validation and diagnostics / `validation-diagnostics` | 2, 17 |

The workflow calls `pr-feature-suite.yaml` through a matrix with one named job
per suite, such as **PR 18 - Camera**. Individual job status, rerun controls,
summaries and artifacts still identify the precise failing suite. A suite failure
does not cancel sibling jobs (`fail-fast: false`). A single `max-parallel: 6` cap
bounds feature jobs across all areas in this workflow run. This replaces six
selectors and per-area queues with one selector and a shared pool of six slots;
finished areas no longer leave slots idle while another area still has suites
queued. The cap follows recurrent hosted Unity `143.002 maximumsameuser` account
login failures. It does not serialize the separate basic EditMode job, other PRs
or other workflow runs against the same account. Full-catalog wall time remains
constrained by activation capacity, cache/import work and runner availability;
latest hosted timings are needed to measure the scheduling improvement.
Within a job, EditMode and PlayMode have separate named steps; both
are attempted when applicable, even if EditMode fails. PR 2 installs the validator's
pinned requirements from the extracted target and runs Python mutations without a
Unity image or license.

It runs on pull requests and pushes to `main` that change a suite, an entry workflow,
the shared workflow, manifest, runner or CI adapter. Updating shared execution code
or the manifest selects all 22 suites. Shared selectors, disk-cleanup code and their
regression tests are included in the positive path filters. The `feature-*.yaml`
filter covers the new entry workflow and deletion of the six old entry workflows;
`pr-feature-suite.yaml` remains explicit. The suite glob also covers newly added
suite directories. `scripts/select_pr_feature_suites.py --group all` builds a
matrix containing only suites whose inputs changed. Known acceptance documentation
does not select a suite, so a triggered documentation-only run has an empty matrix.
Renames consider both old and new paths. Legacy group-workflow changes retain their
logical-area selection during migration; new entry/shared execution changes,
unknown suites or paths, malformed events and missing history select all 22 suites.
Main and manual runs select every suite. Draft PRs follow the same rules, so
marking an unchanged PR ready for review does not start another suite run. The
workflow also supports **Run workflow** from Actions after it is on the default
branch. This catalog validation workflow fetches the exact manifest SHA,
not the current feature-branch head. Refresh the pinned SHA and any expectations
when adopting a later implementation; ordinary gameplay changes alone do not
trigger these historical snapshots.

Every job uses Ubuntu 24.04, checks out only the catalog and scripts with a partial
clone, and prepares its own extracted project. It avoids downloading a duplicate
copy of the current gameplay assets before fetching the pinned target. It uses the snapshot's Unity version,
and filters to `Lumenfall.FeatureAcceptance`. Unity jobs use the same three license
secrets as the existing EditMode workflow and check them immediately after reading
the manifest, before target fetching, preparation, cleanup or cache transfer. Python
3.13 setup and pip download caching apply only to Python suites; the target's pinned
requirements are installed even on a cache hit. Other adapter steps use Ubuntu
24.04's Python 3.12. Both PR Unity workflows use the guarded, incremental 30 GiB
cleanup helper, with a six-minute step limit and no Docker image pruning.
The workflows use `contents: read`; GameCI's optional Checks reporter is disabled.
Feature execution retains read-only checkout authentication for private exact-target
fetches and partial-clone archive reads; selection jobs do not persist credentials.
Feature Library seeds use the versioned `feature-unity-seed-v1` namespace, keyed by
OS, Unity version and a SHA-256 of every extracted `Packages/**` and
`ProjectSettings/**` file's relative name and bytes. Target SHA, suite and Assets
are deliberately excluded from this import seed key. Unity still imports changed
assets and compiles the pinned target and installed suite; source-revision,
suite-hash, fixture and case-count verification remain independent of the cache.
There are four matching configurations among the current 21 Unity suites (15, 3,
2 and 1 suites), so four seeds at the observed roughly 1.27 GiB each would use about
5.1 GiB instead of 21 copies. This is a budget estimate, not a fixed size or runtime
guarantee: other workflows, branch scopes and future configurations also consume
the repository cache budget. No paid cache limit change is required.

Every feature job restores and saves the identical path
`Temp/pr-feature-unity-cache/Library`, which also keeps the Actions cache version
compatible across suite paths. The adapter's `cache-restore` command moves a seed
into the owned prepared snapshot before Unity; cold absence is normal. A restore
or move failure prevents Unity execution and fails strict verification. After
complete verification passes, `cache-save` moves the Library back to the fixed path
only when the exact key was missing. Existing seeds remain immutable; the first
successful writer wins if jobs race to save the same new configuration. The moves
reject occupied destinations, linked directories/ancestors and paths outside the
checkout's `Temp` tree, and require current preparation/verification evidence.
The cached Library contains no license or checkout credentials. Each suite still
runs on a separate hosted runner with its own source and preferences. A shared
seed can require incremental reimport; compare latest hosted cache/import timings
and strict suite results before claiming a speedup.

`scripts/pr_feature_ci.py` verifies each action outcome, pinned revision, suite
hash and every expected fixture/case count. Missing, duplicate, partial, failed or
skipped test results fail the workflow. The job summary lists the target commit
and results by platform; annotations identify the failing platform or preparation
step. Download `pr-N-feature-acceptance` for the NUnit XML, Editor diagnostics,
Python log, preparation metadata and `ci-report.json` (retained for 14 days).
Artifacts are uploaded after failures as well. The original repository integrity
and colocated EditMode checks remain separate required checks.

## Downloadable game releases

[`Release game bundle`](workflows/release-build.yml) builds a **Windows x64 Mono**
player with the Unity version in `Lumenfall/ProjectSettings/ProjectVersion.txt`.
It uses the enabled scenes in `EditorBuildSettings.asset` (currently
`Assets/_Project/Scenes/SampleScene.unity`) and the existing player settings.
It requires the same three Unity secrets described above. The Windows build cache
is separate from the EditMode cache.

The workflow runs automatically on every push to `main`. A successful build of the
current `main` head publishes a `main-<workflow-run-id>` release with the runnable
ZIP and checksum and marks it **Latest**. Its tag points to the exact pushed commit.
After the assets are uploaded and the replacement is published, the workflow deletes
all previous game bundle releases and their attached assets, including older tagged
bundles. It recognizes bundle releases by their `main-<workflow-run-id>` tag or a
`Lumenfall-*-windows-x64.zip` / `.zip.sha256` asset. Git tags and unrelated releases
are preserved. Deleted bundle download links stop working; use the
[latest release page](https://github.com/Densetsu152637/Lumenfall-FIT3161/releases/latest).

Each main push builds independently. Publication and cleanup share a serialized
queue, and `scripts/publish_main_bundle.py` checks the live `main` head before staging
and again after uploading. Superseded builds and old reruns cannot publish or prune
releases. The last successful public bundle remains available while a newer build
runs or fails. New releases are staged as drafts until both assets have uploaded;
a failed or superseded upload can leave a draft that is retried by a rerun or removed
by the next successful publication. Reruns reuse the same release and asset names.

Select **Actions > Release game bundle > Run workflow** to build the selected branch
or tag and download a test bundle from the workflow's artifacts. Manual runs do not
create or modify GitHub Releases. Tag pushes and manually publishing releases no
longer trigger the bundler; automatic game releases follow `main` only.

The release assets are `Lumenfall-<tag>-windows-x64.zip` and its `.zip.sha256`
checksum. Download the game ZIP from the release's **Assets**, extract the entire
archive, and run **Lumenfall.exe**. Keep the data directory, DLLs, Mono runtime,
and all other included files beside the executable. Unity Editor is not required.
GitHub's automatically generated **Source code** archives contain the project
sources, not a runnable player. The repository's visibility also governs who can
access releases; this repository is currently public.

Every ZIP includes launch instructions, the repository license, and
`build-info.json` with the source commit, triggering tag/ref, Unity version, and
platform. Main builds use their `main-<workflow-run-id>` identifier as the bundle
version. All builds check out the exact event commit, even if the
branch advances while the workflow is queued. Player version settings are
preserved; changing the release tag does not change `PlayerSettings.bundleVersion`.
Windows is the only bundled platform.

Packaging fails before publication if the executable, engine DLL, scene data,
managed assemblies, or Mono runtime are missing or empty. Successful bundles and
available build logs are retained as workflow artifacts for 14 days. Releases keep
their attached assets independently of that retention period, until replaced by
the next successful main bundle. A failed build does not create a release or remove
the previous bundle. Upload/publication failures prevent cleanup; cleanup failures
fail the job after the new bundle is available and can be retried with a rerun.
Reruns replace the same named ZIP/checksum assets and preserve existing release notes.

To package a local Windows Mono player and verify its checksum:

```sh
python scripts/package_game.py build/StandaloneWindows64 dist --version v0.1.0 --commit <source-commit-sha>
cd dist
sha256sum --check Lumenfall-v0.1.0-windows-x64.zip.sha256
```

Only the publication job receives `contents: write`; build jobs have read access.
Release publication and cleanup are serialized across all main builds. GitHub's built-in
token does not trigger another workflow when it creates a release. No release or tag is
created by pull-request validation.

## Recommended branch protection

After the workflow has run once, protect `main` and require these status checks before
merge:

- `Repository integrity`
- `Unity EditMode tests`

Also require pull requests and at least one approving review if that matches the team's
review process.
