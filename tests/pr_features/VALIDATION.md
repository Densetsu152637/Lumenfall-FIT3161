# Feature acceptance execution report

Snapshot: 9 October 2026. Test integration branch: `codex/pr-feature-acceptance`.
Repository baseline: `7eb73ba75eead65c9e16851941a6288bdb463c2d`.
Environment: Windows, Python 3.13.2, Unity Editor 6000.4.10f1, batch mode with
`-nographics`. Each PR was tested independently against the exact full SHA in
[the manifest](pr_manifest.json), using the filter `Lumenfall.FeatureAcceptance`.

Status: **Ready with limitations** for the additional suites. All **101/101**
feature cases passed with zero failures or skips. All 22 latest per-PR reports
match the pinned target commits and current suite hashes.

## Executed feature coverage

Counts are discovered cases, including parameterized cases. A dash means that
platform has no cases in this additional suite. Existing tests on the PR branches
were outside the acceptance filter and are not included in these totals.

| PR | Target SHA | Implementation vector | EditMode | PlayMode | Python | Result |
| --- | --- | --- | ---: | ---: | ---: | --- |
| 1 | `de7745631b77` | Hint feedback and reset event composition | 4 | — | — | Passed |
| 2 | `1b9c39dd2f53` | Authored-content validator mutations | — | — | 3 | Passed |
| 3 | `927f2184fd80` | First Light progression and reset | 4 | — | — | Passed |
| 4 | `446ae475d462` | Weave graph connectivity | 4 | — | — | Passed |
| 5 | `c3fbad214b29` | Vis cast boundaries and alignment reset | 8 | — | — | Passed |
| 6 | `174ec2e983b3` | Chapter 1 permission composition | 3 | — | — | Passed |
| 7 | `03d775a18cef` | Save payload and recovery boundaries | 7 | — | — | Passed |
| 8 | `0a6df7af4eb0` | Rotara angle normalization and rejection | 13 | — | — | Passed |
| 9 | `dd4c0787612f` | Audio signal coalescing and rearming | 3 | — | — | Passed |
| 10 | `42fc903761d1` | Optional power state isolation | 3 | — | — | Passed |
| 11 | `401cd79ac51d` | Accessibility preference isolation | 3 | — | — | Passed |
| 12 | `732094f85573` | Reserved-region obstacle lifecycle | — | 3 | — | Passed |
| 13 | `d9dc0b9184b4` | Presentation identity, assets and texture ownership | 8 | 1 | — | Passed |
| 14 | `2ea5ab6b1e23` | Lost Echoes reveal and collection boundaries | 4 | — | — | Passed |
| 15 | `29a175e83e54` | UI overlay ownership and focus | 3 | — | — | Passed |
| 16 | `bccf69e725d2` | NPC travel regions and saved flags | 4 | — | — | Passed |
| 17 | `cccc452bb641` | Diagnostics reset and generation | 3 | — | — | Passed |
| 18 | `36dc7c3d523d` | Camera geometry, pause and occlusion cleanup | 3 | 2 | — | Passed |
| 19 | `fcd8bf62adf5` | Optional recovery and saved progress | 3 | — | — | Passed |
| 20 | `1a395c931720` | Glaciate lane footprint and completion proof | 5 | — | — | Passed |
| 22 | `686ebe99dcab` | Area audio gain, fades and native disposal | — | 4 | — | Passed |
| 23 | `784c10745949` | Graphics defaults and partial preference combinations | 3 | — | — | Passed |
| **Total** | **22 PRs** | | **88** | **10** | **3** | **101 cases** |

The fourteen partial graphics preference combinations are exercised within one
discovered case. Each PR's README explains its additional vectors and manual limits.

## Commands and retained evidence

From the repository root, execution was split into these selections:

```powershell
python scripts/run_pr_feature_tests.py --pr 1 --mode editmode --output Lumenfall/Temp/pr-feature-validation
python scripts/run_pr_feature_tests.py --pr 2 --mode python --output Lumenfall/Temp/pr-feature-validation
python scripts/run_pr_feature_tests.py --pr 3 --mode all --output Lumenfall/Temp/pr-feature-validation --seed-library "D:/Git Repositories/Lumenfall-FIT3161/Lumenfall"
python scripts/run_pr_feature_tests.py --pr 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20 22 23 --mode all --output Lumenfall/Temp/pr-feature-validation --shared-snapshot Temp/pr-target --seed-library "D:/Git Repositories/Lumenfall-FIT3161/Lumenfall" --timeout 900
python scripts/run_pr_feature_tests.py --pr 13 --mode all --output Lumenfall/Temp/pr-feature-validation --shared-snapshot Temp/pr-target --timeout 900
python -m unittest discover -s scripts/tests -q
python scripts/validate_unity_project.py Lumenfall
git diff --check
```

The main multi-PR invocation exited 1 because of the initial PR 13 fixture failure
described below; unaffected selections continued and passed. Final acceptance uses
each PR's latest report and XML, rather than that historical aggregate report.

Local evidence is retained under `Lumenfall/Temp/pr-feature-validation/pr_XX/`:
`report.json`, mode-specific reports where available, `EditMode.xml` / `PlayMode.xml`,
corresponding Editor and launcher logs, and `python.log` for PR 2. Reports identify
the target commit, suite SHA-256, process exit codes and counts. The final audit
compares each suite hash to the catalog and every Unity fixture/count to the
manifest; Python requires exactly three executed cases with no skips or failures.
These generated files are ignored by Git. Source snapshots and caches can be
regenerated from the pinned Git objects; obsolete task copies are removed while
the logs and reports are retained.
`Lumenfall/Temp/pr-feature-validation/final-audit.json` records the complete
22-PR hash, revision, exit-code and result-count audit across the separate runs.

All 21 Unity-targeted suites also passed source compilation against their pinned
first-party sources. Compilation evidence alone was not counted as execution.
The repository Python harness passed **47/47** checks, including **18** new runner
regressions for source restoration, archive ownership and incomplete discovery.
Repository integrity and whitespace checks passed.

## Corrections and observed limitations

- The initial PR 13 suite passed 7/8 EditMode cases. Its final assertion incorrectly
  expected a normal MonoBehaviour destruction callback in EditMode. Replacement
  and explicit clearing remain EditMode assertions; native destruction has a
  separate PlayMode case. The original failing XML/report are retained as
  `EditMode-initial-fixture-failure.xml` and `report-initial-fixture-failure.json`.
  The corrected rerun passed 8/8 EditMode and 1/1 PlayMode cases. No production
  code was changed to resolve this fixture issue.
- PR 17/23 initially used an NUnit attribute unavailable in Unity's bundled NUnit;
  it was removed before successful compilation and execution. Runs are sequential.
- Deep Windows snapshot paths caused an importer file lookup failure. A short
  shared snapshot path resolved it for subsequent runs. This was an environment
  issue; the runner now defaults to a shorter output path and documents the limit.
- Logs retain existing PR/package deprecation warnings, licensing refresh messages
  despite successful licensed execution, and importer diagnostics for third-party
  rig/terrain assets in early imports. Passing the filtered tests does not establish
  a warning-free project or approve those assets. No warnings were suppressed.
- Tests use isolated extracted source and temporary fixtures. Preference fixtures
  restore the keys they touch. The PR 2 fixture uses the snapshot working directory
  directly so an ambient target-path environment variable cannot redirect it.

This report validates the additional acceptance suites. It does not establish
that sibling PRs work together, that all existing branch tests pass, or that a
player build succeeds. Visual/audio quality, complete gameplay journeys, target
device performance and art approval remain manual or separately scoped checks.
The results above were obtained locally before publication; they do not establish
GitHub-hosted runner execution. No merge or production implementation was performed.

## Actions integration follow-up

The initial Actions integration added six workflows grouped by feature area and a shared GameCI
workflow, with a separate named matrix job for every one of the 22 PR suites.
The CI adapter adds strict action-outcome, source-revision, suite-hash and result
verification, plus per-platform job summaries and diagnostic artifacts. The local
Python harness passes **56/56** checks, including nine CI adapter regressions in
addition to the original 18 runner regressions. All seven feature YAML workflow documents
pass actionlint 1.7.12; the existing release workflow's `concurrency.queue` field
is outside that linter's supported schema and was left unchanged. Repository
integrity and whitespace validation pass. See the PR's live checks for hosted
execution results; the 101 local feature-case results above remain independent
evidence against the same pinned revisions.

The first hosted Unity invocation exposed an upstream GameCI CLI incompatibility:
`coverageEnabled: false` produced an unsupported `--no-coverageEnabled` argument
and stopped before launching Unity. The shared workflow now uses GameCI's default
coverage setting, matching the existing repository workflow. The suite summary and
diagnostic artifact correctly retained this setup failure; it was not classified
as a failed feature assertion. Hosted results must be read from the latest PR head.

The first hosted PR 2 Python run also exposed a missing dependency setup step:
its pinned validator fixture imports PyYAML. The workflow now installs the exact
target's `scripts/requirements-validation.txt` before executing mutations, and
the per-suite README documents the same setup for a fresh local environment.
The complete prepare/install/run/verify sequence passed all three cases in a
fresh task-local virtual environment. That follow-up's report and Python log
are retained under `Temp/pr-tests/pr_02/`; the final audit uses this current
suite hash after the setup documentation update.

Local cleanup/reuse verification also found that a retained snapshot marker
could prevent re-extraction after its source directory was removed. Preparation
now recreates missing snapshots, with that case covered by the existing source
restoration regression. The full 56-check Python harness passed after this fix.

The initial grouped workflow catalog covered all 22 manifest entries exactly once. The
October 12 efficiency update adds precise PR matrix selection within each group,
conservative full-group fallback, Ubuntu 24.04 runners, sparse catalog checkout,
early license validation, conditional Python setup/pip caching, and shared bounded
incremental disk cleanup. Independent outcomes and unique suite names remain.
The initial update removed the group cap; recurrent hosted Unity
`143.002 maximumsameuser` login failures then required one suite at a time per
group. This contained activation pressure while retaining six concurrent groups;
it did not establish a global account-level queue.
Acceptance assertions, pinned revisions, expected counts and strict result
verification are unchanged. These workflow changes require their own latest-head
hosted evidence; the 101-case local execution report above remains historical.
Local checks for the efficiency update passed all 76 Python harness tests, the
Unity repository validator, and actionlint 1.7.12 with ShellCheck 0.11.0 against
all eight PR/feature workflows. The adapter's existing revision, suite-hash,
platform-discovery and complete-result regressions remain in that harness.

The shared-cache follow-up replaces per-PR/target/suite Library keys with immutable
seeds keyed by OS, Unity version and SHA-256 of extracted Packages/ProjectSettings
file names and bytes. All 21 Unity targets use Unity 6000.4.10f1 and the same package
tree; their four settings configurations cover 15, 3, 2 and 1 suites. At the observed
roughly 1.27 GiB per Library, four seeds would use about 5.1 GiB rather than 21
copies, excluding other workflows and branch scopes. This is an estimate; changed
assets/suites still require incremental import and compilation. The fixed Actions
cache path is `Temp/pr-feature-unity-cache/Library` in every job. Guarded adapter
commands move it into a prepared owned project and stage a new seed only after
strict verification passes on an exact-key miss. Cache hits are not overwritten.
No target pins, assertions, fixtures, expected counts or verification rules changed.

Local cache regressions cover sharing across different assets/suites, package and
settings name/content invalidation, prepare outputs, cold absence, safe moves,
occupied destinations, linked paths, ownership/preparation rejection and failed or
stale verification evidence. These checks do not establish hosted cross-suite
cache reuse or faster Unity execution. The first hosted seed restore followed by
complete strict verification must be checked on the latest PR head; the historical
101-case local result above remains separate evidence.

The follow-up passes all 88 local Python harness tests (including ten new cache
regressions), the Unity repository validator, whitespace validation and actionlint
1.7.12 with ShellCheck 0.11.0 on the changed shared workflow. Local tests use a
checkout-local temporary directory because the Windows sandbox's default temporary
directory denies nested writes; this does not change the hosted workflow.

The scheduling follow-up consolidates the six entry workflows into
`feature-acceptance.yaml`, with one **Select affected suites** job and one matrix
capped at six concurrent feature suites (`fail-fast: false`). Logical areas and
their individual selector CLI options remain, while `--group all` unions the same
area-selection rules. Every one of the 22 caller PR/feature names and reusable
acceptance names remains unchanged, as do the required repository-integrity and
Unity EditMode checks. Positive path filters cover the union of the old triggers,
new suites and entry-workflow migration. Known documentation-only diffs yield an
empty matrix; uncertain/shared diffs select all suites. The single queue allows
available slots to serve any area and removes five selector jobs. This is a
scheduling projection, not a measured speedup or an account-wide login queue.
Hosted cache reuse, activation behavior and final-head strict results still need
verification after publication. No acceptance pins, assertions or counts changed.

The consolidated revision passes all 92 local Python harness tests, the Unity
repository validator, whitespace validation and actionlint 1.7.12 with ShellCheck
0.11.0 on all three PR/feature workflows. Selector regressions cover mixed-area
changes, empty documentation matrices, shared/uncertain inputs, unknown new suites,
legacy workflow migration and complete manifest coverage without duplicate jobs.

The first consolidated hosted run passed strict acceptance verification for its
first five Unity suites, then failed cache staging with `Errno 13: Permission
denied`. A Linux ownership regression reproduces the same guarded rename failure
with a runner-owned prepared snapshot and a root-created Library directory;
returning the project to the runner's UID:GID makes that unchanged move pass.
The shared workflow now captures the actual runner UID:GID during preparation and
passes it to GameCI's supported `chownFilesTo` input in both EditMode and PlayMode.
CLI v0.1.72 returns ownership of the project, artifacts and coverage through this
input. Cache guards, strict verification, pinned sources and expected counts are
unchanged. The permission regression executes under Linux root with an
unprivileged child and is skipped on Windows; hosted seed upload/reuse still
requires successful evidence on the repaired head.

The ownership repair subsequently saved seeds after strict verification passed.
A hosted PR 15 warm restore from PR 19 then failed with
`DirectoryNotFoundException` referring to PR 19's previous absolute
`Library/Bee/artifacts/.../Lumenfall.GameEvents.dll.mvfrm` path (run 38239852946,
job 114782006746). Restore now clears only `Library/Bee` and
`Library/ScriptAssemblies`, the same compiler-state pair cleared by
[GameCI's fallback-cache handling](https://game.ci/docs/github-orchestrator/advanced-topics/build-services/#fallback-keys).
Imported artifacts, package/shader caches and existing v1 seed keys are retained.
Regressions demonstrate that stale absolute compiler markers survive the old
restore and are removed by the repair while imported data and snapshot evidence
remain unchanged. Cleanup rejects linked roots/descendants and malformed compiler
directories before deleting either tree; a Linux regression protects real outside
link targets. Strict source/fixture/count checks and runner ownership repair remain
unchanged. Successful hosted cross-suite verification on the repaired head remains
the acceptance gate; local filesystem tests do not prove Unity's warm runtime.
