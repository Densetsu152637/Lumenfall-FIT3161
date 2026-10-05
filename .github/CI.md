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

## One-time Unity license setup

The Unity job deliberately fails rather than silently skipping tests when licensing is
not configured. Follow the GameCI activation process for a Unity Personal license, then
add these encrypted repository secrets under **Settings > Secrets and variables >
Actions**:

- `UNITY_LICENSE`: the contents of the activated Unity license file
- `UNITY_EMAIL`: the Unity account email used to activate it
- `UNITY_PASSWORD`: the Unity account password used to activate it

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

## Downloadable game releases

[`Release game bundle`](workflows/release-build.yml) builds a **Windows x64 Mono**
player with the Unity version in `Lumenfall/ProjectSettings/ProjectVersion.txt`.
It uses the enabled scenes in `EditorBuildSettings.asset` (currently
`Assets/_Project/Scenes/SampleScene.unity`) and the existing player settings.
It requires the same three Unity secrets described above. The Windows build cache
is separate from the EditMode cache.

After this workflow is merged, either:

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
platform. Player version settings are preserved; changing the release tag does
not change `PlayerSettings.bundleVersion`. Windows is the only bundled platform.

Packaging fails before publication if the executable, engine DLL, scene data,
managed assemblies, or Mono runtime are missing or empty. Successful bundles and
available build logs are retained as workflow artifacts for 14 days. Releases keep
their attached assets independently of that retention period. A failed build
does not create a release on a tag push; an already-published release remains
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

After the workflow has run once, protect `main` and require these status checks before
merge:

- `Repository integrity`
- `Unity EditMode tests`

Also require pull requests and at least one approving review if that matches the team's
review process.
