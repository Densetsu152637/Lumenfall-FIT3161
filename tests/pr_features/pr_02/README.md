# PR02 content validation acceptance
Target: `1b9c39dd2f532a00fbd9db9b0bf0b316b8f9db95`.

Three behavioral mutation cases complement the target's existing fixture tests: parity-consistent invalid tiles, accumulation of independent diagnostics, and repair of parity without hiding invalid geometry. Each starts from an observed valid synthetic project and mutates temporary files only. Reuses the target's `ContentFixture`, not copied validator logic.

The repository runner sets the working directory to the extracted target root.
Prepare the suite first, then install the target's pinned validation requirements
before execution (Actions does this automatically):

```powershell
python scripts/run_pr_feature_tests.py --pr 2 --mode prepare
python -m pip install -r Temp/pr-tests/pr_02/snapshot/scripts/requirements-validation.txt
python scripts/run_pr_feature_tests.py --pr 2 --mode python
```

The fixture always uses the pinned snapshot working directory. Local run: all 3
passed against the named target.

Manual limits: real GitHub Actions wiring, uploaded artifacts, and Unity import/build remain runner/CI checks.
