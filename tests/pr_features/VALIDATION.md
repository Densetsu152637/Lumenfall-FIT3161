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

The branch now includes 22 named suite workflows and a shared GameCI workflow.
The CI adapter adds strict action-outcome, source-revision, suite-hash and result
verification, plus per-platform job summaries and diagnostic artifacts. The local
Python harness passes **56/56** checks, including nine CI adapter regressions in
addition to the original 18 runner regressions. All 23 new YAML workflow documents
pass actionlint 1.7.12; the existing release workflow's `concurrency.queue` field
is outside that linter's supported schema and was left unchanged. Repository
integrity and whitespace validation pass. See the PR's live checks for hosted
execution results; the 101 local feature-case results above remain independent
evidence against the same pinned revisions.
