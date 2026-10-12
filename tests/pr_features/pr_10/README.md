# PR10 optional dormitory power acceptance
Target: `42fc903761d17306fce2d463312b08ca0cd1a11a`.

Three EditMode cases cover reconfiguration cancelling only transient sessions, effective-feedback notification coalescing, and rejection/recovery of an incompatible sink endpoint. Uses the actual caster and dedicated world endpoints. The fixture preserves the original event runtime object, time scale, overlay state, and world singleton; its inactive isolated world never registers a save provider. Reflection is restricted to fixture lifecycle/static ownership setup inherited from the target's test pattern.

Unity compilation/execution is delegated to the catalog runner. Manual limits: no production scene enables this optional activity; approved authoring, UI presentation, and physical interaction need explicit opt-in PlayMode checks.
