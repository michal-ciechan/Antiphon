# CARD-1286 routing-pointer attribution

The existing regression is correct. Commit
`85f17adaa099c366bc8ee91300d8d7f42cc213fb` replaced the Role policy paragraph
and removed its create-time routing owner pointer. Its direct parent
`bf33417a15f79c15aee7c203cf48c62f9b9ae3d3` passes both argument cases; fetched
master `a30b7f4297400f010ce70188ac99ebe55be51931` passes antiphon-api but fails
ops-http with `pointers.Count should be 1 but was 0`. The test file is identical
between those two tested commits. This is inherited from CARD-1250, not introduced
by CARD-1281. The test was introduced in
`8e444106d302de0ef51430f8c82397252e4ae877`.

The repair adds back the pointer to the still-existing matrix heading under
`orchestration-loop.md#2-tiers`, preserving every existing byte of the Role policy
guidance. No test/assertion changes. No server or runner restart is needed.

Both historical executions used clean detached checkouts, the exact method filter,
fresh TRX, verified isolated builds and granted slots with zero wait. The parent
receipt passes `validate-checkpoint-receipt.ps1`; current master is intentionally
red and is not a green certificate. Fresh TRX confirms both parameter identities.
Reported run walls: master 6m18s; parent 5m49s (cleanup finished later).
The checkpoint bootstrap build was additionally slot-gated: 9s held, 0s waited,
2 warnings, 0 errors. No whole Unit or assembly run was commissioned.

```text
CHECKPOINT CP-1 commit=a30b7f4297400f010ce70188ac99ebe55be51931 build=ok filter=/*/*/OrchestratorInstructionsGuidanceTests/Routing_cross_references_resolve_to_an_existing_owner_heading executed=2 passed=1 failed=1 skipped=0 trx=/tmp/c1286-current-e17a0f89/.antiphon/checkpoints/20261015-163533-69fa/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a30b7f4297400f010ce70188ac99ebe55be51931 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=bf33417a15f79c15aee7c203cf48c62f9b9ae3d3 build=ok filter=/*/*/OrchestratorInstructionsGuidanceTests/Routing_cross_references_resolve_to_an_existing_owner_heading executed=2 passed=2 failed=0 skipped=0 trx=/tmp/c1286-before-e17a0f89/.antiphon/checkpoints/20261015-164214-ce69/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=bf33417a15f79c15aee7c203cf48c62f9b9ae3d3 sourceState=clean buildSource=verified
```

The [closed verification manifest](../superpowers/plans/2026-10-15-card-1286-ops-http-pointer.md)
defines final V-1/V-2/R-1/R-2. This note is frozen before the repaired-source run;
its results belong in `.antiphon/task-e17a0f89.md`, with unedited CHECKPOINT lines,
per-class counts, actual tested SHA and receipt validation. Original Code/landing
owner: `e17a0f89-75cd-4ed3-8776-5a10d5129723`. Zero declared PCs; SourceLanding
Mutation still owns missing-control discovery after ordinary Review and landing.
