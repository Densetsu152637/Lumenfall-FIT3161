# Third-party assets

Store externally sourced Unity asset packs here, one self-contained folder per package.
Keep each package's original internal layout and `.meta` files intact so upgrades and
license audits remain manageable.

For every new package, record its name, source URL, version, licence, and acquisition date
in the pull request or the team's dependency register. Do not add paid source files unless
the repository's access rules permit redistribution.

Avoid editing vendor files directly. Prefer an adapter, prefab variant, material variant,
or wrapper under `Assets/_Project`. If a compatibility fix must change a vendor file,
document the exact change so it can be reapplied after an upgrade.
