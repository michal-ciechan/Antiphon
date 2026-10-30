# Aggregate-run Debug record for CARD-1618 S-F

Status: commissioned execution in progress; no passing aggregate claimed yet.

Source: `985034e0aa541a4fb48519a48e5811dad9d37d11`, Linux/server2, task `a59a5491`, branch `feat/card-task-a59a5491`.

The closed selection is CP-41 (17 shape cases), CP-42 (13 baseline cases), CP-43 (5 confinement cases), serial in one checkpoint invocation with one shared build. This record is separate from native Windows qualification, R-2, and S-F-cost. No product changes are authorized here.

Execution uses the plan import with only its documented timeout and timing metadata overlay: CP-41 28m, CP-42 48m, CP-43 20m, shared-build floor 20m and per-acquisition slot allowance 10m. Bootstrap uses an owned alternate output, a build slot, TEMP=/tmp and UseAppHost=false. Generated manifests, JSONL, logs and receipts remain ignored under `.antiphon/`; final evidence will retain hashes and essential CHECKPOINT provenance here, following git-common.

No synthetic repeat will be run solely to estimate p95. Each case has at most one naturally required sample; percentile summaries across different cases are descriptive distributions, not a per-case measured p95. Missing phase spans will be unknown, not zero. AppHost, HTTP, Docker, database, migration and seed are not applicable to this selection.
