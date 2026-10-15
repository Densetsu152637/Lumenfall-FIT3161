# Pull request validation

`Pull Request Validation` runs when a pull request is opened, updated, or reopened.
Drafts execute the same checks; marking an unchanged PR ready for review does not
repeat them. It applies to PRs into every branch, pushes to `main`, and manual runs.

The workflow always reports two stable required checks on Ubuntu 24.04:

1. `Repository integrity` runs the Python harness and validates Unity metadata,
   JSON, build scenes, case-safe paths, merge markers and generated-file hygiene.
   It also selects the needed checks and validates the PR workflow with actionlint
   1.7.12, downloaded from its official release with a pinned SHA256. These checks
   require no third-party Python packages or secrets.
2. `Unity EditMode tests` is the compatibility gate for branch protection. It
   requires repository integrity and, whenever Unity is required, the separate
   `Gameplay regression tests` job to succeed. That job verifies both EditMode and
   PlayMode. Failed, cancelled, missing or unexpected results fail the gate.

Unity runs for every manual and `main` push and for relevant PR changes. Only PRs
changing exclusively `README.md`, `LICENSE`, `.github/CI.md`,
`.github/PULL_REQUEST_TEMPLATE.md`, `tests/README.md` or Markdown under `docs/` skip
Unity. `scripts/select_pr_checks.py` compares the event's exact base and head
commits from the depth-two merge checkout. Renames are deletion plus addition.
Source, assets (including their documentation), tests, packages, project settings,
workflows, shared scripts and unknown paths require Unity. Missing commits,
malformed events, empty diffs and detection failures also require Unity. Comparing
commit endpoints may conservatively include base changes since a PR forked. The
workflow has no path filter, so documentation-only PRs still report both checks.

## Current-project gameplay regression

One job checks out the current event revision with depth one and runs `Lumenfall`
through one GameCI action, using `testMode: all` and
`customParameters: -testCategory !RequiresGraphics`. Its checkout enables Git LFS
so terrain and other binary assets are available to Unity. The integrated inventory
contains 303 EditMode and sixteen batch-capable PlayMode cases. Two additional rendering
cases are explicitly categorized `RequiresGraphics` and must run separately
in a graphical Editor; it is never accepted as a skipped CI test. The
[behavior inventory and graphical procedure](../tests/README.md) cover all 321 cases.
There are no historical source fetches or snapshot matrices.

The supported `cliVersion` input pins
[GameCI CLI v0.1.72](https://github.com/game-ci/cli/releases/tag/v0.1.72).
The [action's all-mode mapping](https://github.com/game-ci/unity-test-runner/blob/32e57712352b500e17974b245a6dce9e11a73213/src/test-args.ts#L49)
selects PlayMode, EditMode and result combination. Unity's
[category argument](https://docs.unity3d.com/Packages/com.unity.test-framework@1.4/manual/reference-command-line.html#testcategory)
supports `!RequiresGraphics`. The pinned CLI expands custom parameters as separate
arguments without eval; the category needs no embedded shell quotes. The
[CLI platform loop](https://github.com/game-ci/cli/blob/v0.1.72/dist/platforms/ubuntu/steps/test.sh#L172)
uses the same project/Library while launching Unity for each phase; one action does
not mean one Editor process. The image matches `m_EditorVersion` in
`ProjectVersion.txt` and uses the Ubuntu base image without unnecessary build modules.

`scripts/verify_gameplay_tests.py` requires the exact `editmode-results.xml` and
`playmode-results.xml` files and their expected fixture counts. Combined output is
never accepted as platform evidence. Nonzero complete discovery, successful action
outcome, unique case identities, no failed/skipped cases and the actual checked-out
revision matching `GITHUB_SHA` are required. A source-inventory harness keeps the
manifest aligned with current fixtures. The job refuses an existing results
directory before Unity, writes a current-revision/per-mode summary and
`ci-report.json`, and uploads `gameplay-test-results` for 14 days after execution,
including failures. The GitHub Checks reporter is disabled; the token needs only
`contents: read`.

The job checks license secrets before cleanup, checkout or cache downloads. Relevant
fork PRs fail quickly if secrets are unavailable. Checkout credentials are not
persisted. GameCI's supported `chownFilesTo` input receives the runner's numeric
UID:GID, returning ownership of the project and result paths after the container
runs. See the [CLI permissions block](https://github.com/game-ci/cli/blob/v0.1.72/dist/platforms/ubuntu/steps/test.sh#L303).

The existing `unity-editmode-v2` cache namespace and `Lumenfall/Library` path are
retained. Keys include OS, Unity version, packages, project settings and asset
content. Asset changes can restore a compatible prefix for incremental import; a
cold cache imports normally. Both modes share this Library. A missing exact key is
saved only after strict verification of both suites succeeds. Cache branch scope
follows GitHub isolation, and the Library contains no license secrets. Hosted
cache/import timings are needed to measure the improvement.

The job prints disk space and, only below 30 GiB available, removes unrelated
preinstalled .NET, Android, GHC, ghcup and CodeQL SDKs one at a time, stopping at
sufficient space. Cleanup is limited to disposable GitHub-hosted runners and six
minutes; the Unity job has a 30-minute limit. It does not prune Docker images.
Workspace and Library remain intact. Release cleanup is configured independently.

Concurrency includes workflow, event and PR number or ref. New commits cancel
obsolete runs for that PR; independent PRs, main pushes and manual runs do not
cancel each other. The Windows player build checkout also enables Git LFS; publication behavior is unchanged.

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
actionlint .github/workflows/pr-validation.yml
```

Use actionlint 1.7.12 with ShellCheck 0.11 to match the local validation tools.
The harness covers conservative selection, renamed/deleted inputs, missing history,
malformed events, complete gameplay discovery and strict XML verification.
`GITHUB_EVENT_NAME`, `GITHUB_EVENT_PATH` and `GITHUB_OUTPUT` are the selector's
standard Actions inputs/output; rehearse with sanitized event JSON and existing
commit SHAs. The release workflow uses `concurrency.queue`, which actionlint 1.7.12
does not recognize; the syntax command targets the maintained PR workflow.

See [gameplay test instructions](../tests/README.md) for both Unity Test Runner modes,
fixture lifecycle requirements and verification of fresh GameCI results. The retired
snapshot catalog, its two workflows, three execution/selection adapters and three
adapter harnesses have been replaced by the current-project fixture inventory and
strict result verifier. Tests of APIs absent from this checkout are documented as
coverage gaps rather than represented as validation of this revision.

## Downloadable game releases

[`Release game bundle`](workflows/release-build.yml) builds a **Windows x64 Mono**
player with the Unity version in `Lumenfall/ProjectSettings/ProjectVersion.txt`.
It uses the enabled scenes in `EditorBuildSettings.asset` (currently
`Assets/_Project/Scenes/SampleScene.unity`) and the existing player settings.
It requires the same three Unity secrets described above. The Windows build cache
is separate from the EditMode cache.

The build checkout explicitly downloads Git LFS content (`lfs: true`), including
textures, models, audio and tracked terrain data. Before restoring the Library or
starting Unity, `validate_unity_project.py Lumenfall --require-lfs-assets` rejects
any remaining pointer placeholders. The lightweight repository-integrity checks
keep their default metadata-only validation and do not download LFS objects.
The FastMesh `SceneViewText` component retains its serialized toggle in players;
its scene-view callbacks and UnityEditor references compile only with `UNITY_EDITOR`.

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
