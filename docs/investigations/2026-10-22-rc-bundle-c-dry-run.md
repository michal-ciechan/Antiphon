# rc bundle C dry-run onto rc/2026-10-20 (2026-10-22)

Task 2a42be76 (Docs/dry-run). Work in progress: source assembly is complete;
compilation and narrow checks are pending. No rc branch or tag is published.

Pinned rc base: `0714a9a89e4bff65aae7e0b4b94098694aeb931c`.
Master snapshot: `79341d09664267c7ccce40175815022b7e7c9c61`.

CARD-1495 depends on CARD-0167 provider-endpoint types and schema, absent from rc.
It is blocked pending the caller's decision on that separate feature port. All other
currently landed requested card code has applied without source conflicts in the
first scratch tree. Unconsumed plan changes are restored to the preceding rc state.

CARD-1506's requested `A1EBB3D0B4C0DF9BB31E417D0AFD7607CDC4BD5A404DACE986CC7CD8EC274DD6`
with count 57 is the inventory before CARD-1483 S1, which adds another build project.
The final inventory must be generated again after S1, using the audit tool.

Final recipe and measured outcomes will replace this preliminary record.
