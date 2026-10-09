# PR02 content validation acceptance
Target: `1b9c39dd2f532a00fbd9db9b0bf0b316b8f9db95`.

Three behavioral mutation cases complement the target's existing fixture tests: parity-consistent invalid tiles, accumulation of independent diagnostics, and repair of parity without hiding invalid geometry. Each starts from an observed valid synthetic project and mutates temporary files only. Reuses the target's `ContentFixture`, not copied validator logic.

Run from the extracted target repository root: `python -m unittest discover -s <catalog>/tests/pr_features/pr_02 -v`. Optional `LUMENFALL_TARGET_REPO` selects an explicit target root. Requires the target's validation dependencies. Local run: all 3 passed against the named target.

Manual limits: real GitHub Actions wiring, uploaded artifacts, and Unity import/build remain runner/CI checks.
