# PR06 Chapter One acceptance
Target: `174ec2e983b326a2a05e7a6ff2a2433fd6083460`.

Three EditMode cases cover independent dialogue permission owners, idempotent transition permission ownership and authorization cleanup, and isolation of story routing from ordinary NPC conversations. Uses authored resource catalogs and local runtimes; no singleton or save mutations.

Unity compilation/execution is delegated to the catalog runner. Manual limits: in-world physical position, NPC staging, and the authored end-to-end sleep journey remain PlayMode/manual checks; existing target tests cover stage progression and save restore.
