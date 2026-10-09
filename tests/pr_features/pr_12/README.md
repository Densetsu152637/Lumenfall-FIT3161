# PR12 production world acceptance
Target: `732094f85573563507b667f89d101d06906be90a`.

Three PlayMode cases exercise the production reserved-region component with an isolated authored area: complete rectangular blocking while walkable regions remain open, disable/re-enable/destroy obstacle registration, and invalid-footprint diagnostics without discarding valid siblings. Reflection sets the private authored area before the real OnEnable lifecycle; assertions use the public GridWorld occupancy contract. All created objects/assets are destroyed after each case.

Unity compilation/execution is delegated to the catalog runner. Manual limits: production startup/scene authority behavior is covered by the target's existing production tests; this added suite focuses on closed-region enforcement. Real route travel, reserved-area labels, and scene composition remain integration/manual checks.
