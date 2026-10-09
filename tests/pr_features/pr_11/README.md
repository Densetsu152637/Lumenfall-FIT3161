# PR11 accessibility/localization acceptance
Target: `401cd79ac51d23d3aead282eaffb41f7763fc3c2`.

Three EditMode cases cover immutable one-option changes preserving siblings, malformed stored values retaining valid siblings without writes, and localization recovery after transient provider failure while skipping blank IDs. Uses in-memory settings and restores the global localization resolver in `finally`.

Unity compilation/execution is delegated to the catalog runner. Manual limits: keyboard/controller focus, readable layout at maximum scale, color contrast, and reduced-motion comfort require UI/visual checks.
