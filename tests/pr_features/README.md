# PR feature acceptance suites

These tests exercise the implementation vectors of the 22 open Lumenfall PRs
captured on 9 October 2026. `pr_manifest.json` pins each exact source revision.
The suites complement the tests already on those branches; they do not merge
the branches or establish that all sibling features work together.

See [the execution report](VALIDATION.md) for per-PR results, case counts and
retained evidence from the local validation run.

Six **Feature tests - <area>** GitHub Actions workflows group the suites by target
area, with one named matrix job per PR suite. See [CI instructions](../../.github/CI.md#feature-acceptance-workflows)
for the group mapping, triggers, pinned revisions, summaries and result artifacts.
On PRs, each group selects only suites with changed inputs. Shared execution changes
or uncertain change detection run the whole applicable group; catalog README/report
edits skip suite execution. Main and manual runs select all suites in each triggered
group. Drafts use the same rules; readiness transitions do not repeat unchanged
checks. Jobs retain independent outcomes without an extra
per-group parallelism cap, subject to GitHub's account scheduling limits.

Tests live outside `Lumenfall/Assets` because main does not yet contain the APIs
under test. The runner extracts the selected PR's tracked project into its own
ignored snapshot, installs only that PR's acceptance suite with deterministic
Unity metadata, and runs it there. Existing checkouts and authored assets are
not edited. Each PR directory explains its behavioral coverage and limits.

## Run

From the repository root, with Python 3.12+ and Unity 6000.4.10f1:

```powershell
python scripts/run_pr_feature_tests.py --list
python scripts/run_pr_feature_tests.py --pr 3 --mode all
python scripts/run_pr_feature_tests.py --pr 2 --mode python
python scripts/run_pr_feature_tests.py --pr 18 22 --mode playmode
python scripts/run_pr_feature_tests.py --pr all --mode all
```

Use `--unity-editor <absolute-path-to-Unity.exe>` to override the default Windows
Editor path. Unity licensing and package access must be available. Missing Git
objects fail explicitly; fetch the pinned revision from the repository before
running if it is not already local. The runner does not fetch or publish.

PR 2's Python fixture requires the target's pinned validator dependencies. On a
fresh Python environment, follow [its setup instructions](pr_02/README.md) before
running Python or all-mode selection. Its Actions workflow installs them automatically.

For source compilation without launching the Editor, an already imported
**separate** Unity project can supply read-only dependency references:

```powershell
python scripts/run_pr_feature_tests.py --pr 3 --mode compile --reference-project "D:/Git Repositories/Lumenfall-FIT3161/Lumenfall"
```

The compiler reads current snapshot sources and excludes stale imported
first-party references. This checks compilation only. `--mode prepare` creates
the snapshot and installs tests without compiling or executing them.

For repeated local runs, `--seed-library <separate-imported-project>` copies its
Library into snapshots without an existing cache. No links are used and the
reference project is read-only. Unity still imports and compiles the selected
PR source. This trades disk space for faster initial imports.

For a large sequential run, `--shared-snapshot <owned-output-directory>` retains
one import cache between PRs. Each revision change removes and re-extracts all
source trees and replaces the installed suite before Unity runs. Results and
logs remain under each PR's output directory; the shared source directory ends
at the last selected PR. Never run two processes against the same shared snapshot.
On Windows, keep this path short to avoid legacy importer path limits. For example:

```powershell
python scripts/run_pr_feature_tests.py --pr all --mode all --shared-snapshot Temp/pr-target
```

## Evidence and isolation

The default output is `Temp/pr-tests/pr_XX/`. Each PR gets
its own project snapshot, compiler logs, Unity log, NUnit XML and `report.json`.
The aggregate report covers the PRs selected in that invocation. Reusing the
same output requires the same pinned commit; the selected suite is refreshed
to remove stale test sources. Source fingerprints detect edited or injected
snapshot files and restore tracked source before reuse while retaining the
import cache. Suite hashes identify the tested catalog content.
Old NUnit XML is removed before execution, and missing, empty, failed, skipped
or inconclusive results fail the run. Expected fixture and case counts prevent
partial Unity discovery from passing; Python discovery likewise requires the
expected positive count with zero skips, errors or failures. A nonzero Unity
exit also fails it.

Run suites sequentially: Unity, user preference stores and some native services
are shared process resources. Tests preserve preferences they touch and clean
up their own objects. Do not run this catalog concurrently with an interactive
gameplay session using the same OS account. `--timeout` limits each process in
seconds (default 1800).

The snapshots are temporary validation artifacts, not Git worktrees. Inspect
their logs before deleting a resolved output directory. Existing user
worktrees are never removed by this runner.

Automated assertions do not establish visual quality, audio quality, game feel,
device performance, player-build readiness, or combined sibling-PR behavior.
The per-PR notes retain the relevant manual acceptance boundaries.
