# Pull request validation

`Pull Request Validation` runs when a pull request is opened, updated, reopened, or
marked ready for review. With no pull-request branch filter, it applies to PRs into
`main` and every other branch. It also runs after changes are pushed to `main`, and
can be started manually from the Actions tab.

The workflow contains two checks:

1. `Repository integrity` runs the Python harness tests and validates Unity metadata,
   JSON files, build scenes, case-safe paths, merge markers, and generated-file hygiene.
   It requires no third-party packages or secrets.
2. `Unity EditMode tests` runs the NUnit tests colocated with their systems under
   `Lumenfall/Assets/_Project/Systems/*/Tests` with GameCI and uploads the XML test
   results for 14 days.

Both Unity jobs first print the runner's available disk space, then reclaim space on
the disposable GitHub-hosted Ubuntu runner by removing unrelated preinstalled .NET,
Android, GHC, ghcup, and CodeQL SDKs and unused Docker images. They fail closed on
self-hosted runners. The step leaves the workspace and cached Unity `Library` intact;
the Unity toolchain is provided by GameCI. If extraction runs out of space again,
compare the before and after `df -h /` diagnostics in the job log. Unity tests still
run and continue to fail when the required license secrets are missing.

The EditMode job reads and validates `m_EditorVersion` from `ProjectVersion.txt` and
uses the matching GameCI Ubuntu base image, which omits build modules the tests do not
need. Its Unity `Library` cache is keyed by Unity version, packages, and commit; it
restores only caches for the same Unity/package set and saves a new cache after an
EditMode XML result exists. The test runner's GitHub Checks reporter is disabled
because the artifact upload is the supported result record and the reporter requires
Checks API write access. Test failures still fail the job, and XML results remain
available as artifacts.

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
```

Run the game tests from **Window > General > Test Runner > EditMode** in Unity.

Branch-specific acceptance tests for the active PR snapshot are documented in
[`tests/pr_features/README.md`](../tests/pr_features/README.md). Run
`python scripts/run_pr_feature_tests.py --pr <number> --mode all` to materialize
the pinned PR project and execute its added suite without changing a checkout.
These catalog tests live outside `Assets` and therefore require this runner;
the existing EditMode CI job does not automatically discover them on main.

## Feature acceptance workflows

Six `.yaml` workflows group the catalog by feature area in the Actions overview:

| Workflow | Target PR suites |
| --- | --- |
| **Feature tests - Quests and spells** | 1, 3, 4, 5, 8, 14, 20 |
| **Feature tests - World and story** | 6, 10, 12, 16 |
| **Feature tests - Presentation and UI** | 11, 13, 15, 18, 23 |
| **Feature tests - Audio** | 9, 22 |
| **Feature tests - Persistence and recovery** | 7, 19 |
| **Feature tests - Validation and diagnostics** | 2, 17 |

Each workflow calls `pr-feature-suite.yaml` through a matrix with one named job
per suite, such as **PR 18 - Camera**. Individual job status, rerun controls,
summaries and artifacts still identify the precise failing suite. A suite failure
does not cancel sibling jobs (`fail-fast: false`), and each group runs at most
three suites concurrently. Within a job, EditMode and PlayMode have separate named steps; both
are attempted when applicable, even if EditMode fails. PR 2 installs the validator's
pinned requirements from the extracted target and runs Python mutations without a
Unity image or license.

They run on pull requests and pushes to `main` that change a suite in their area, its workflow,
the shared workflow, manifest, runner or CI adapter. Updating shared execution code
or the manifest starts all six groups covering all 22 suites. A suite-only change
starts its feature group. Each
workflow also supports **Run workflow** from Actions after it is on the default
branch. These are catalog validation workflows: they fetch the exact manifest SHA,
not the current feature-branch head. Refresh the pinned SHA and any expectations
when adopting a later implementation; ordinary gameplay changes alone do not
trigger these historical snapshots.

Every job prepares its own extracted project, uses the snapshot's Unity version,
and filters to `Lumenfall.FeatureAcceptance`. Unity jobs use the same three license
secrets as the existing EditMode workflow and fail visibly if they are absent.
The workflows use `contents: read`; GameCI's optional Checks reporter is disabled.
Library caches are scoped to PR, OS, Unity version, target SHA and suite hash, and
saved only after complete result verification. Concurrent suites run on separate
hosted runners; their source, preferences and caches are isolated.

`scripts/pr_feature_ci.py` verifies each action outcome, pinned revision, suite
hash and every expected fixture/case count. Missing, duplicate, partial, failed or
skipped test results fail the workflow. The job summary lists the target commit
and results by platform; annotations identify the failing platform or preparation
step. Download `pr-N-feature-acceptance` for the NUnit XML, Editor diagnostics,
Python log, preparation metadata and `ci-report.json` (retained for 14 days).
Artifacts are uploaded after failures as well. The original repository integrity
and colocated EditMode checks continue unchanged.

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
access releases; this repository is currently private.

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
