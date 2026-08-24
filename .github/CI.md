# Pull request validation

`Pull Request Validation` runs when a pull request is opened, updated, reopened, or
marked ready for review. With no pull-request branch filter, it applies to PRs into
`main` and every other branch. It also runs after changes are pushed to `main`, and
can be started manually from the Actions tab.

The workflow contains two checks:

1. `Repository integrity` runs the Python harness tests and validates Unity metadata,
   JSON files, build scenes, case-safe paths, merge markers, and generated-file hygiene.
   It requires no third-party packages or secrets.
2. `Unity EditMode tests` runs the NUnit tests under `Lumenfall/Assets/Tests` with
   GameCI and uploads the XML test results for 14 days.

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

## Recommended branch protection

After the workflow has run once, protect `main` and require these status checks before
merge:

- `Repository integrity`
- `Unity EditMode tests`

Also require pull requests and at least one approving review if that matches the team's
review process.
