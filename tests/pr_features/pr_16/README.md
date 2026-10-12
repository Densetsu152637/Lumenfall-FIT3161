# PR16 ambient NPC and optional travel acceptance

Target: `bccf69e725d22075ce0dd3b5a5c0b3dd7ee6867b`. Category: `PR16`. EditMode: 4 cases.

New vectors:
- Required travel flags reject blank entries, missing runtimes, and missing flags; duplicated satisfied flags remain safe.
- Safe alcove geometry includes lower edges and excludes upper edges at a negative origin; unwalkable regions reject arrival.
- A selected null ambient stop fails closed without advancing phase or silently selecting another destination.
- Choice conditions and future unapproved metadata cannot enter ambient dialogue; approved passive portrait metadata remains allowed.

Manual limits: path navigation retries, real portal transfers and rollback, idle timing, and ambient authored dialogue presentation remain PlayMode/manual concerns (covered partly by the PR's existing PlayMode suite). No singleton runtime, save, or scene content is changed here.
