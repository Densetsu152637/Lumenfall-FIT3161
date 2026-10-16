# Pull request validation

`Pull Request Validation` runs when a pull request is opened, updated, or reopened.
Drafts execute the same checks; marking an unchanged PR ready for review does not
repeat them. It applies to PRs into every branch, pushes to `main`, and manual runs.

The workflow always reports two stable required checks on Ubuntu 24.04:

1. `Repository integrity` runs the Python harness and validates Unity metadata,
   JSON, build scenes, case-safe paths, merge markers and generated-file hygiene.
   It also selects the needed checks and validates every workflow YAML file with
   actionlint 1.7.12, downloaded from its official release with a pinned SHA256.
   That version does not recognize the release workflow's `concurrency.queue` key,
   so the syntax check ignores only that diagnostic while still linting the rest
   of all three workflows. These checks require no third-party Python packages or
   secrets.
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
contains 346 EditMode and sixteen batch-capable PlayMode cases. Five additional rendering
cases are explicitly categorized `RequiresGraphics` and must run separately
in a graphical Editor; it is never accepted as a skipped CI test. The
[behavior inventory](../tests/gameplay_suites.json) and [graphical procedure](../tests/README.md)
cover all 346 EditMode cases, including the three shader-stripping contracts and
four serialized asset compatibility cases.
There are no historical source fetches or snapshot matrices.

`FountainFinal.fbx` and the campus prefab depend on the decorative `diffuse_cube`
texture. Reimporting the original IHV DDS in that chain produced an import loop.
The FBX stores the cubemap name and its metadata remaps it by GUID and file ID; the
native Cubemap keeps that name and identity while changing the asset extension. Its six
serialized face payloads were verified byte-for-byte against the source DDS, with
the original dimensions, sRGB format, sampler settings and unreadable state retained.
The asset compatibility test force-imports the cubemap, FBX and campus prefab and
checks that both dependency chains resolve to the native asset.

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
actionlint -ignore 'unexpected key "queue" for "concurrency" section' .github/workflows/*.yml
```

Use actionlint 1.7.12 with ShellCheck 0.11 to match the local validation tools.
The harness covers conservative selection, renamed/deleted inputs, missing history,
malformed events, complete gameplay discovery and strict XML verification.
`GITHUB_EVENT_NAME`, `GITHUB_EVENT_PATH` and `GITHUB_OUTPUT` are the selector's
standard Actions inputs/output; rehearse with sanitized event JSON and existing
commit SHAs. The exact ignore is needed because actionlint 1.7.12 does not yet
recognize `concurrency.queue`; removing that queue would change serialized release
publication behavior. The same command lints the PR, release, and build-test workflows.

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
It requires the same three Unity secrets described above. Release and build-test
jobs use the pinned `ubuntu-24.04` runner label instead of the moving
`ubuntu-latest` label. The Unity builder is pinned to upstream GameCI v5.0.1 commit
`adaf7cd8f42fd48fbbf2d4f5f67ca30d87f819f1`: [current v4 metadata](https://raw.githubusercontent.com/game-ci/unity-builder/v4/action.yml)
still declares Node 20, while [the pinned v5.0.1 metadata](https://raw.githubusercontent.com/game-ci/unity-builder/adaf7cd8f42fd48fbbf2d4f5f67ca30d87f819f1/action.yml)
declares Node 24. The pinned action retains the workflow's Unity version, project
path, target platform, build name, output path, and `versioning: None` inputs. Its
[release notes](https://github.com/game-ci/unity-builder/releases/tag/v5.0.1)
document the `/dev/shm` setting needed for newer Unity editors. The Windows build
cache is separate from the EditMode cache.

### Build time and cache behavior

The Windows build jobs in the release and build-test workflows have a 120-minute
timeout. This leaves about 25 minutes beyond the 95-minute cold main build observed
on 2026-10-16 while preventing an unbounded six-hour run. The timeout applies only
to player builds; the release publisher keeps its separate serialized queue.
License secrets are checked before the LFS checkout. Cleanup reuses
`scripts/free_unity_disk_space.py`: below 30 GiB free it removes unrelated SDKs
until sufficient space is available, with a six-minute cleanup step limit.
It leaves Docker images intact and skips deletion when space is sufficient.

The Library key includes runner OS, Unity version, packages, project settings and
asset content instead of the commit SHA, so documentation-only commits reuse an
exact cache. Restore first tries compatible project settings, then the previous
Unity/package prefix, including existing caches from the old key format. Unity
updates restored state for changed inputs. An explicit save uses the original
restore key only after the builder and player packaging validation succeed, before
artifact uploads; an upload failure therefore does not discard a completed build's
cache. Failed/cancelled builds never save partial Library state. GameCI's
[Linux build script](https://github.com/game-ci/unity-builder/blob/adaf7cd8f42fd48fbbf2d4f5f67ca30d87f819f1/dist/platforms/ubuntu/steps/build.sh)
makes the project readable to the runner before cache compression. Cache availability
still depends on GitHub's branch scope, storage limits and eviction.

Investigation on 2026-10-16 found the dominant delay inside Unity shader compilation:

- [Run 38828490133, build job 116741521728](https://github.com/Densetsu152637/Lumenfall-FIT3161/actions/runs/38828490133/job/116741521728)
  at `113d818` spent 1m58s cleaning a runner that already had 86 GiB free,
  49s checking out LFS assets, and 57m11s in the builder before the 60-minute
  job limit cancelled it. The Library cache missed. The image pull took 2m41s
  and the main asset refresh 658s. `ClusterDeferred` took about 12 minutes,
  terrain `BillboardWavingDoublePass` about 7m42s, and the `FinalPost` fragment
  pass compiled 1,280 variants in 693s with zero local cache hits. `FinalPostXR`
  was still compiling when the job was cancelled, preventing a cache save.
- [Run 38262584442, build job 114856680632](https://github.com/Densetsu152637/Lumenfall-FIT3161/actions/runs/38262584442/job/114856680632)
  restored a 1,881 MB Library in 35s and its main asset refresh took 50s, but
  still hit the job limit while compiling `FinalPost`. Repository integrity
  took nine seconds in each run and remains required. The cache did not remove
  the shader bottleneck for those changed settings/assets.

The main-branch regression coincides with [graphics-options merge e68b601](https://github.com/Densetsu152637/Lumenfall-FIT3161/commit/e68b601420073928cf31a0c9c08850d75e44ed09):
relative to its first parent `cf55a024`, it changed all three stored general
unused-variant flags from 1 to 0, while keeping post-processing stripping enabled.
The PC pipeline, PC renderer and quality settings did not change across that merge.
The preceding [cf55a024 release build](https://github.com/Densetsu152637/Lumenfall-FIT3161/actions/runs/38252685910/job/114822818699)
completed its builder step in 22m43s; the following `e68b601` build hit the one-hour
limit. Together with the package behavior below, this is strong evidence that
disabled general stripping contributed to the regression. The timings are not a
controlled comparison: the merge also changed gameplay/UI source and assets.

Both **Strip Unused Post Processing Variants** and **Strip Unused Variants** are now
enabled in the active Unity 6000.4.10f1 / URP 17.4.0 API and
[serialized settings](../Lumenfall/Assets/_Project/Settings/UniversalRenderPipelineGlobalSettings.asset).
Simply enabling the general flag with the original renderer list would be unsafe:
[PC_Renderer](../Lumenfall/Assets/_Project/Settings/PC_Renderer.asset) has active SSAO
before opaque rendering (`AfterOpaque: 0`), and URP's
`Editor/ShaderBuildPreprocessor.cs`, lines 1109-1118, removes the SSAO-off variant
when every included renderer has SSAO.
[GraphicsOptions](../Lumenfall/Assets/_Project/Systems/Startup/GraphicsOptions.cs)
toggles SSAO at runtime; Mobile quality is excluded from Standalone and cannot
supply that missing state. The PC pipeline therefore also includes
[PC_Renderer_AOOffVariants](../Lumenfall/Assets/_Project/Settings/PC_Renderer_AOOffVariants.asset),
an equivalent ForwardPlus renderer with an empty feature list. The original remains
the default renderer at index 0. URP collects both feature sets and retains a variant
if either needs it (`ShaderBuildPreprocessor.cs`, 638-659;
`ShaderScriptableStripper.cs`, 1253-1278), preserving both AO states while pruning
unrelated features. Quality presets and graphics APIs are unchanged. URP eagerly
initializes the extra renderer, adding some initialization/allocation cost; cameras
continue to render once with the original default renderer.
See Unity's [stripping guidance](https://docs.unity3d.com/6000.4/Documentation/Manual/urp/shader-stripping-features.html).
Diagnostics retain exported shader-stripping JSON from `Lumenfall/Temp` when available.

With general stripping off, URP 17.4.0's build preprocessor uses permissive feature
and prefilter masks and does not remove unused XR shader passes, even without an XR
loader. Post-processing stripping works independently: it tests component presence
in every `VolumeProfile` under `Assets`, without filtering by scene use or component
activity. The project's `DefaultVolumeProfile` already contains all of the effect
types tested by that collector. Removing only the third-party example profile would
therefore not resolve the broad variant set. The observed UberPost fragment passes
still retain 13,824 variants each after scriptable stripping, down from 23,040 after
settings filtering; enabled post-processing stripping alone does not make this a
small shader build.

The native Windows diagnostic build at `99f784b` was **cancelled by the user after
97m57.3s**, still compiling UberPostXR (last log: 11,251/13,824 variants ready).
No executable or completed BuildReport was produced. Existing local Library state
was retained and refreshed; this was neither a clean-cache benchmark nor a completed
warm-cache run. The first sandboxed license-start attempt (57.6s) is excluded.

The initial asset refresh took 326.256s, and the three successful Tundra script-build
steps logged 9.70s, 2.01s and 7.57s. Shader compilation dominated: terrain ForwardLit
compiled 6,912 variants with zero local hits in 1,615.86s; UberPost compiled 13,822
variants with two local hits in 1,625.53s. Their raw variant spaces were 35,389,440
and 92,160 respectively; Unity filtered these before compiling. UberPostXR retained
another 13,824 variants, with no final cache-hit/compile summary before cancellation.
On the i7-13700KF (16 cores/24 threads), one five-second sample showed 24 shader
workers consuming the equivalent of 18.38 busy logical cores, about 33 GiB of free
RAM and 3.74% total disk activity. This supports CPU-heavy shader compilation as the
delay, rather than a stalled C# compiler or license check. Native Windows timings do
not establish the duration of the Ubuntu-hosted build. Partial logs, API inputs,
activity sample and cancellation evidence remain locally under
`artifacts/unity-stripping-20261016/`; temporary probe and generated source changes
were removed/restored. The later optimization validation below was separately authorized.

The optimized native Windows probe retained that Library and the same shipping
scene, Mono backend, ForwardPlus path and graphics APIs. Its actual build collector
confirmed two feature sets differing only in SSAO, AO prefilter `Select` (ON and OFF),
and unused-XR stripping enabled. Three focused EditMode contracts exercise the
installed URP collector, including a negative control showing that removing the
alternate renderer loses the AO-off state; all three passed. The four existing
`GraphicsOptionsPlayModeTests` also passed on the RTX 5080 with D3D11, covering
repeated shading/AO changes and later-camera antialiasing. The Python harness
passed all 62 tests. Original graphics preferences and incidental Editor settings
were restored, and the temporary build probes were removed. The observed fragment
counts (d3d11) were:

| Shader/pass | General stripping off | Optimized | Optimized cache/compile evidence |
| --- | ---: | ---: | --- |
| Terrain `WavingDoublePass` / ForwardLit | 6,912 | 16 | 16 new, zero hits; 6.11s vs baseline 1,615.86s |
| UberPost / UberPost | 13,824 | 432 | 432 local hits, zero new; 3.85s |
| UberPost / UberPostXR | 13,824 | 0 | Pass stripped |

All `ClusterDeferred` passes and the unused `FinalPostXR` passes were also stripped;
the original ForwardPlus renderer remains selected. Terrain's raw fragment space
was unchanged at 35,389,440; settings filtering reduced it to 6,144, built-in
stripping to 48, and scriptable stripping to 16. UberPost's raw 92,160 became 960
after settings filtering and 432 after scriptable stripping (0.01s preparation).
These counts establish reduced build work. The retained cache explains UberPost's
short time and the initial refresh of 21.719s; the timings are not a clean-cache
comparison or a hosted-build estimate. Evidence is retained locally under
`artifacts/unity-stripping-optimisation-20261016/`.

The improved probe was stopped at the agreed profiling checkpoint after **15m16.3s**,
still compiling URP/Lit ForwardLit (126/1,344 fragment variants ready in the last
progress message). No executable or final BuildReport was produced. Cold third-party
material shaders remain a cost: glTF-pbrMetallicRoughness's Forward fragment pass
compiled 288 new variants with no hits in 157.28s. A complete optimized player build,
strict shader-variant matching and standalone visual checks remain unverified.

Measure the next successful manual branch build and a repeat with an exact cache
hit before claiming an overall speedup. Manual runs produce workflow artifacts
without publishing a release. Local workflow/tooling checks cannot establish
hosted cache behavior, successful player compilation or the new build duration.

### Shader API compatibility

The 2026-10-16 shader audit found URP 17.4.0 deprecation warnings in
`IceTrialFrost.shader` and five locally maintained Polytope shader copies. Those
sources now use `_CLUSTER_LIGHT_LOOP`, `USE_CLUSTER_LIGHT_LOOP`, and
`CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK`; the two Polytope deferred passes
also use `GBufferOutput.hlsl` and `PackGBuffersBRDFData` while retaining their
`UniversalGBuffer` passes. Five non-batch graphical tests passed on an NVIDIA
GeForce RTX 5080 with Direct3D 12. The shader cases render into 128-by-128 targets
and compare the central 80-by-80 pixels while exercising configured Forward, ForwardPlus and Deferred
renderer paths. Deferred assertions cover the configured renderer and GBuffer
pass;
they do not observe URP's internal actual render mode. Frost is checked for its
main-light response only in Forward and ForwardPlus. These API updates do not
establish a build-time improvement.

The initial main build also emitted 5,475 warning annotations. The largest
remaining content families were 4,747 self-intersecting polygons discarded across
four FBXs, 100 obsolete external-material-location notices across 16 tree FBXs,
24 tree shader/renderer compatibility warnings, and 23 FBX rig/animation import
warnings. These are asset-specific content and rendering risks; no broad geometry
rewrite, material migration, or warning suppression is included. Review affected
models and materials in the Editor before claiming visual correction.

Package diagnostics included 460 unsupported Sentis transpose variants, 33 other
Sentis shader messages, 46 unsupported ray-tracing variants, and 2,806 backend
diagnostic records in compiler logs. Those records do not represent 2,806 failed
shaders; the successful build log has no corresponding `ok=0` failure count.
Sentis resources and unsupported platform variants are not broadly stripped based
on these messages. The initial eight project-owned URP deprecation warnings and
32 C# warnings were addressed in source; later branch results reported no old
project C# or shader deprecations. One Node 20 warning came from the former
unity-builder v4 reference; the build workflows now pin its Node 24-capable v5.0.1
commit. This taxonomy describes the captured initial main run, not a claim that
every third-party warning has been eliminated.

The build checkout explicitly downloads Git LFS content (`lfs: true`), including
textures, models, audio and tracked terrain data. Before restoring the Library or
starting Unity, `validate_unity_project.py Lumenfall --require-lfs-assets` rejects
any remaining pointer placeholders. The lightweight repository-integrity checks
keep their default metadata-only validation and do not download LFS objects.
The FastMesh `SceneViewText` component retains its serialized toggle in players;
its scene-view callbacks and UnityEditor references compile only with `UNITY_EDITOR`.

The release workflow runs automatically on every push to `main`. A successful build of the
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

Push to `CI/build-test` to run **Build test game bundle**. It runs the same source,
LFS, Windows player, packaging, checksum, downloadable artifact, and diagnostic steps
as the release build, using `build-test-<workflow-run-id>` as the bundle version.
Download its `Lumenfall-windows-x64` artifact from the run; this workflow does not
create or modify GitHub Releases. It only runs for pushes to `CI/build-test`.
The Windows build job is capped at 120 minutes. New pushes to this branch cancel
obsolete runs of this workflow; the release workflow's publication queue is
independent.

To run a one-time cold asset-import audit on a pushed branch head, include
`[fresh-import-audit]` in that commit's message. This adds a Linux EditMode run
against an uncached Library using the Unity version from `ProjectVersion.txt` and
the Ubuntu Windows-Mono editor image. It first checks license secrets, checks out
LFS, frees disk space, validates materialized assets, and refuses an existing
Library. The test runner is pinned to GameCI's Node 24 v4.4.0 commit and CLI
v0.1.72; `-buildTarget win64` and the `SerializedAssetImportTests` filter limit the
job to the four current terrain, prefab and cubemap cases without a player build.
The strict verifier derives the case count from the live manifest, requires the
current checkout SHA and successful action, rejects the four known native import
error signatures, and requires nonempty NUnit XML and Editor log files. Results
and available Unity logs are uploaded on success or failure. This marker-gated job
does not run on ordinary pushes, and a marked commit still runs the normal
nonpublishing player build on the same head.

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
`build-info.json` with the bundle version, source commit, Unity version, and
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
