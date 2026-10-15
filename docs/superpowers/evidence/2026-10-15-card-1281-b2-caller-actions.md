# CARD-1281 B-2 caller actions — Code 27d33f88

Status: partial implementation, not Review-ready. Original Code/landing owner: 27d33f88-e1c4-4846-9d15-e6c7c67468c1. Branch: feat/card-task-27d33f88. Worktree: /work/worktrees/task-27d33f88. Base: 5dd289dbc37a3eeb82583899c4cb3fbd647f02b2.

Implemented in the component scope: explicit RecordAsync, scoped GET, authenticated actor, workflow/attempt/settlement and observation fences, append-only history, idempotent lost-response replay, verified completion receipt association, independent nullable clocks and actual-child clock readback. Routes remain unregistered until S6. No migration, shared entity/admission/Program edit, policy/bundle change, dispatcher, transport change, restart or deployment.

## Blocking shared boundary

The approved B-2 binding requires blocker, overlap, quota, stage/host cap, placement and tracker/spend conditions revalidated inside AgentTaskService.CreateAsync's child/link transaction. At the assigned S0 base, its action hook validates identity and writes the link atomically, but FindScopeOverlapsAsync is explicitly advisory and runs after commit; it returns ScopeOverlaps rather than refusing. CardBlockers is not called from CreateAsync. Tracker activation/spend is not part of its handoff request contract. Adding only a component preflight would leave the promised transaction guard absent. AgentTaskService.cs is exclusively owned by sequential integration and forbidden by this brief. A scope decision was requested; no expansion is assumed.

RequestChildAsync and its HTTP request form are not implemented. V-9/V-11, refusal recovery and the complete V-8 request/admission proof remain pending, not passed. Do not register this component as the completed B-2 protocol. Commission the shared admission repair, then finish B-2 and its ordinary tests before Review. A missing method is reported as missing; there is no constant-success admission stub.

## Verification selection

Plans read in full at base:

- docs/superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md; SHA-256 07a7042f81b2c264c93883e84bba6778e907b902de7256c37ab9d79d7a3ee6e8.
- docs/superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md; SHA-256 1b3b355eeb2b5ae819343fc0dbc11cb30ac252e07ba3d897d9c61bf8eb213059.

The brief explicitly waives CP-3 whole Unit (CARD-1287). The appended table actually assigns CP-14..16 to S0 and CP-17..23 to S6, with no B-2 component rows. The existing CP-4 component class is selected, taking its own isolated build because CP-3 is waived. The ignored task manifest preserves that filter and floor. Counts alone cannot complete CP-4: C1281_AdmissionHolds and C1281_CurrentHandoffOnce are still absent.

The additional exact Unit row uses PipelineHandoffParseTests, ExpectationPipelineTests and CompletionNoteWorkQueueTests (R-2). Grep found no existing tests that pin the three new production files; these are the named adjacent Unit classes, and the parser is directly reused. No owner prose or existing pinned sentence was edited. No assembly sweep or whole Unit is claimed.

The component harness maps the actual endpoint extension with migrated isolated PostgreSQL and production service classes. It does not boot real Program; S6 owns registration. Queue recovery reuses the unchanged CompletionWarningDeliveryTests.C788_ProgressWarningRecovery body, which exercises all nine cuts, busy/eligible callers and raw/distilled-spilled bodies. The terminal adapter records submitted bytes; it does not prove native ingestion or caller reasoning. The clock projection test seeds a child as a projection fixture and makes no admission claim.

## Red-first

Test commit b80ee1ae12ac8eb02587e3bb0be1eb4cf4a0f4cb: intended HTTP 404 versus 200 assertion, explicit-consumption-is-durable. Fresh TRX: one execution, one assertion failure, zero skips. Build and fixture succeeded. The row's final floor remains 12; only the initial acceptance method existed in this red commit.

```text
CHECKPOINT CP-4 commit=b80ee1ae12ac8eb02587e3bb0be1eb4cf4a0f4cb build=ok filter=/*/*/PipelineHandoffPilotTests/* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-27d33f88/.antiphon/checkpoints/20261015-125512-9245/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=b80ee1ae12ac8eb02587e3bb0be1eb4cf4a0f4cb sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=505.9473s startup=51.8948989s testsWall=6.171643s teardown=3.0916291s hostWall=61.1581679s
```

The checkpoint-tool bootstrap was an additional leased dotnet run with its own bin-c1281-b2-tool/ output, necessary to invoke the mandated checkpoint front door. Its lease was granted, waited=0s. All row/build leases were granted, waited=0s. No Mutation or deliberately weakened predicate was run.

## Pending obligations

- V-8: writer/history/clocks covered by the new test; actual child-request timing remains pending.
- V-9, V-11: pending shared admission repair and component implementation.
- V-10: selected ordinary reused recovery; final outcome recorded in the task report after execution. Negative receipt associations are additionally tested by C1281_WriterReceipt; full clipped-text/old-generation recovery matrix remains owed.
- R-2: selected adjacent Unit row and component class; incomplete until V-9/V-11 finish.
- V-30/V-31/V-32, V-36/V-37 and R-7: S6's real Program/HTTP composition remains pending. Component tests are not those IDs' full discharge.
- Q-5: operating cohort remains pending; no pilot, spend, external message or timing improvement claimed.
- All PCs remain pending for Mutation. B-2 inherited PCs: PC-13..26, PC-48..51, PC-53..57; shared B-2 PCs: PC-67..73, PC-99..104, PC-112, PC-113, PC-117. All variants pending. The entire plan's PC-1..121 census remains pending, with missing-control discovery still owned by Mutation. No PC-clean claim.

Restart: none. Next: Code for the shared admission contract and remaining ordinary verification, then Review. Landing belongs to the original Code task after Review, followed by separately commissioned SourceLanding Mutation.
