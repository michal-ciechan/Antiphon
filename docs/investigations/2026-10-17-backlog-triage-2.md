# Backlog triage round 2 (2026-10-17)

Read-only board pass for the orchestrator. No card or product edit. Snapshot `GET /api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` at 2026-10-17T14:13:00Z, plus `scripts/card.ps1 list -Board Antiphon -Status Backlog -All -Json` (356 rows, every page). Relationships for the named cards came from `GET /api/cards/{id}/relationships`. None of the eight below has an open blocker.

## Capacity at the snapshot

Project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` is at its parallel cap: at 14:03 open 6, parallel 5, queued 1, `parallelRemaining` 0. Investigate 2/3, Plan 0 parallel plus 1 queued, Code 2/5, Review 0/5, Deploy 1/1. At 14:13 the queued Plan row for CARD-1311 had become Dispatched (`Plan release branch model amendment`), so the cap is still full.

Hosts: `local` 2/2 remaining 0; `server2-temp` 4/10 remaining 6; `server2` in flight 0, effective limit unknown. Windows cards wait for a desktop seat. The other six can take `server2-temp` when a project slot frees (this triage is one of the two Investigate rows).

`investigateBacklog.total` is 342. The five published items are CARD-1263, CARD-0500, CARD-0548, CARD-0870, CARD-0900, all rank 8. That matches a full rank sort of the Backlog list after dropping 8 archived rows and 5 `post-land-verification` rows (343). One of those 343 has a succeeded or open stage task and is not a fresh candidate. No live Backlog card has any task since 2026-09-01, and the missing card is not in the published top five.

Rank is `13 - (3*importance + 2*effectiveUrgency)`, lower first. Formal ready rows use the same key. Mutation ready rows are omitted below because Mutation is paused.

## Already in flight or named as handled

Not recommended. Seen live: CARD-1315 Investigate, CARD-1318 Code, CARD-1320 Code (plus this triage and a blocked CARD-1320 S3), CARD-1311 Deploy and now Plan. Ready but skipped by the brief: CARD-0242 Plan, CARD-1305 Plan, CARD-1306 Plan, CARD-1010 Code, CARD-1031 Review. Mutation ready and paused: CARD-0812 (rank 5), CARD-0698 (rank 6), CARD-0701 (rank 8).

## Close

### CARD-0900 — already fixed on rc and master

Title: Flaky under load: `RunCheckpointScriptTests.C578_FailedBuildKeepsLogAndExit` fixed 10 s readiness wait. Rank 8, Backlog, no label, no blocker.

The card's mechanism is a 10 s wall deadline in `Wait-C578Ready` that rejects a short failed-build child after exit even when both stream markers are in the log (`docs/investigations/2026-10-02-flaky-test-root-causes.md`). On this branch (`HEAD` `3adbe0d5efe214075b1325cd55081e3524cd148f`, the rc base) that deadline is gone.

- `scripts/test-run-checkpoint.ps1:808-828` `Wait-C578Ready` waits on a directory-watch signal (`Wait-C889Signal`). It does not sleep 10 s.
- The failed-build shim writes `phase.ready` and then holds while `C578_HOLD` is `build` (`scripts/test-run-checkpoint.ps1:591-599`). `Test-C578_FailedBuildKeepsLogAndExit` starts that hold (`-Hold 'build' -FailBuild`) at line 1155, asserts both markers at line 1162, then expects exit 37 at line 1170.
- Those lines are from `fa14fa73ff24e19dcba957f489aebe55e3f1e050` and `0f5a31c9c045a6653c6c1ccc4ea633a560c6c5e1` (`test(CARD-0889): hold C578...`, 2026-10-09). Both commits are ancestors of `HEAD` and of `origin/master`.

Recommend close. The fixed-10 s race the card describes is not in the current script.

### CARD-0548 — premise withdrawn, do not dispatch

Title starts `[CORRECTED] Lesson...`. Rank 8, labels `orchestration,process`. The 2026-09-18 correction on the card says there was no misbinding. CARD-0527 is Done; its close text records the land at `6ab13481e72e10fa4c99985fa30364974bc7bb66`. The two leftover asks are optional ideas (echo the resolved title at dispatch). Recommend close as a corrected lesson, not a bug.

## Hold

| Card | Why it is not in the eight |
|---|---|
| CARD-1263 | Rank 8 and first in `investigateBacklog`, but the body says do the cleanup on 2026-10-21 (due 2026-10-21 09:00). Desktop worktrees and remote `rescue/*` deletes. Dispatch on that date, and list branches for the operator before any delete whose recommendation is not discard. |
| CARD-0972, CARD-0979 | Rank 8. Both are SourceLanding Mutation PCs plus the Windows `AgentTaskLandHalfResetWindowsTests` row. Mutation is paused, and the two cards share that Windows class and the land half-reset footprint. Do not dispatch either while Mutation is paused. |
| CARD-1287 | Formal next stage is Investigate (ready since 2026-10-15T18:58:59Z). The handoff re-runs the full Unit lane. That lane is the one CARD-1298's two reds block. Review CARD-1298 first. |
| CARD-1322 | Fresh Backlog, master-only, rank 10. Follow-ups from CARD-0242 include `RunnerCodexAdapter`. Soft collision with CARD-0500. Dispatch only if CARD-0500 is not in flight. |
| CARD-1323 | Fresh Backlog, master-only. It is the CARD-1305 D-3/D-4/D-5 leftover, and CARD-1305 is still Plan-ready. Same policy JSON and `LandDeliveryFixture`. Leave it with CARD-1305. |
| CARD-1326 | Fresh Backlog, label `rc-2026-10-15`. Test vectors for the CARD-1324 census. CARD-1324's Review `e43b55d8` succeeded at 13:37Z and there is no ready stage row, so the next action on 1324 is land, not another stage. Write 1326 after that land. |
| CARD-0416 | TestDesign ready, rank 13, live Grok compaction on an authenticated Windows host. Below this cut. |
| CARD-0081 | Label `parked`. The body says do not dispatch without asking. |

## Top 8

Ordered by rank, then created time. `rc-fix` means the card label is `rc-2026-10-15`. No such label is recorded as master-only. `requiredPlatform` is Any on every row; the Windows column is the reproduction host.

| # | Card | Next stage | Train | Windows | Source area | Blockers | Goal |
|---|---|---|---|---|---|---|---|
| 1 | CARD-0500 Interactive Codex submit-confirm flake | Investigate | master-only | yes (desktop PTY canary) | `tests/Antiphon.Tests/Agents/CodexCommandLengthSessionTests.cs` (`RunInteractiveAsync`) | none. CARD-0108 and CARD-0055 are Done; do not reopen those adapters | See whether the interactive canary still flakes after those lands, and fix only a remaining race. |
| 2 | CARD-0870 GrokDelegateEndToEndTests `IX_Agents_Slug` retry | Investigate | master-only | yes (Windows evidence) | `GrokDelegateEndToEndTests` and the stalled-task agent retry save | none | Find why the retry inserts a duplicate `IX_Agents_Slug` and make the two named tests pass without a weaker assertion. |
| 3 | CARD-1259 Live transcript ingestion lags 28 s | Review | rc-fix | no | transcript ingestion pump / delivery verifier (D-1 on task `8a60a8e2`) | none. Review `259aa3b9` was created before that Code task completed, so the ready row is still open | Review the pushed D-1 repair. Task `8a60a8e2` stays the landing owner. |
| 4 | CARD-1281 Pipeline speed (D-7 / D-8..12 slice) | Plan | master-only | no | E2E JSON contracts and the inherited Linux fixture from task `c2c02264` | none. Not CARD-1305 (D-2..D-6) and not CARD-1306 | Plan the confirmed Linux fixture failure and five stale JSON contracts. Leave the release-train policy for a later decision. |
| 5 | CARD-1286 ops-http routing-pointer test | Review | master-only | no | `docs/ops-http.md` and `OrchestratorInstructionsGuidanceTests` | none | Review `829b0cd9f6452b27d93722b1a68003fb62b52f99`. Code task `e17a0f89` stays the landing owner. |
| 6 | CARD-1296 D-2 policy doc guard | Review | master-only | no | `docs/orchestration-loop.md` and `AdvisoryChangePolicyTests` | none | Review the pushed D-2 section guard (Code `a8d016eb`). Different file from CARD-1286. |
| 7 | CARD-1298 Two master reds (census helper, untagged fixture string) | Review | master-only | no | `DelegationHarnessCensusTests` and `TestLaneCategoryGuardTests` | none | Review `a16263459c89bdf1183090eef7c69771499f0166`. These two reds are why a full Unit rerun (CARD-1287) should wait. |
| 8 | CARD-1321 Closed-card Merge/Commit binding witnesses | Investigate | master-only | no | `AgentTaskService` CreateMerge/CreateCommit and `PipelineSpeedIntegrationTests` admission | none. CARD-1288 is already handled | Document the helper exception and add the missing follow-up-bind and Canceled witness arms. |

No two of these eight share a source area. CARD-1286 and CARD-1296 both sit under the broad `docs` area and touch different files; review them in parallel and land them as separate publications. CARD-0500 and CARD-0870 both need the desktop, and `local` remaining is 0, so they are first in rank and second in time. The six Linux rows can start as soon as a project slot frees.

## Next if a further slot frees

CARD-1325 (master-only, Backlog, Investigate): nightly diagnostic follow-ups in `scripts/lib/nightly-coverage.ps1` and `NightlyExecutionDiagnosticTests`. CARD-1306 is already published, so this does not collide with it. Then CARD-1322 only when CARD-0500 is not in flight.
