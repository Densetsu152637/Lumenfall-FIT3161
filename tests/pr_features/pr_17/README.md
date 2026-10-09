# PR17 diagnostics acceptance

Target: `cccc452bb64176be9e1e5391a45c2242ffa4cb95`. Category: `PR17`. EditMode: 3 cases.

New vectors:
- Reset nested inside an active outer scope discards the old scope while retaining a new inner measurement.
- Zero-work and zero-duration attempts still count and produce a valid arithmetic timing mean.
- Clearing a returned snapshot array does not clear collector aggregates or affect later snapshots.

The nonparallel fixture snapshots and restores the collector's private aggregate arrays, enabled setting, and collection generation. It preserves pre-existing diagnostics instead of leaving a reset collector behind; reflection is confined to state preservation.

Manual limits: actual frame rates, release packaging, hardware benchmarks, and timing overhead need the release/performance workflow. Deterministic aggregation checks do not establish any performance target. Do not run concurrently with external diagnostic collection.
