# CARD-1574 Code reader checkpoint — partial, next Code

Portable reader readiness and transport carriage are implemented and pushed. The final reader pack passes 3/3, and the server transport build has zero errors. S2 launch/queue protection and Final verification are incomplete under the commissioned 60-minute budget. This is not Review-ready and does not fix the running incident by itself.

Original Code / landing owner: `7c1cf5bb-2f0a-4bc7-983f-d2bab596da72`. Branch: `feat/card-task-7c1cf5bb`. Worktree: `/work/worktrees/task-7c1cf5bb`. Base: `fda635c6641fccc84c97dd299aeae205f14eec88` (fetched master already contained the landed plan). Plan: [docs/superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md](../superpowers/plans/2026-10-24-card-1574-resume-receipt-readiness-plan.md). Latest tested source: `420c32d40f92814f633afab5ef847f2647885c8c`; the report-only commit follows it. Restart target: **none**. CARD-1259 attribution and CARD-0510 activation remain outside this task.

## Delivered source

- Optional live readiness DTOs with null/unknown compatibility defaults. Existing accepted-generation and terminal-completion fields remain carried.
- Reader snapshots report an opaque binding ID, parsed extent, fresh observed length, Ready/Behind/Unknown and a reason. Progress publishes after complete chunk processing. Bind/fork/revocation/in-flight reads, partial/malformed records, physical replacement and observed shortening prevent false Ready. Observed shortening remains invalid through regrowth until a real rebind; strict UTF-8 validation refuses replacement-decoded input for readiness while retaining legacy normalization. Replay starts at zero with the existing pacing/order/UUID semantics. Native identity helpers were reused unchanged.
- Both server transport mappings carry the optional record. Their server project compiles; HTTP/runtime behavioral verification V-3 is pending.
- Real-tailer tests use small temporary files and owned barriers. The added third method reproduced both truncation-regrowth and invalid UTF-8 defects before repair.

Source commits, in cherry-pick order (these are only the partial reader slice; the caller still needs the complete reviewed S2 fix for rc/2026-10-24):

```text
c7dc923efee268f06ea2b7dae779a7c237819bc5 test(CARD-1574): witness missing live transcript readiness
7ce076ab619dbd5f2f57af9af4822e3aa8e990d0 feat(CARD-1574): publish current-file transcript ingestion readiness
8d2a4d5a7c3b31544db5ea1a30c0a5c6287190e7 test(CARD-1574): give fork fixture qualifying ownership evidence
c489fc8e79ce7e0ea07b9f8c1512d65a741e9904 test(CARD-1574): expose regrowth and malformed-byte readiness holes
420c32d40f92814f633afab5ef847f2647885c8c fix(CARD-1574): retain invalidation after truncation and malformed bytes
```

## Ordinary outcomes and incomplete Final scope

| ID | Actual outcome |
|---|---|
| V-1 | Passed at 420c32d40f92814f633afab5ef847f2647885c8c: complete-current-file, held processing boundary, append invalidation, stable sequence/UUID checks |
| V-2 | Passed at 420c32d40f92814f633afab5ef847f2647885c8c: partial, malformed, revoked claim, fork, physical replacement, truncation, valid ignored record; separate third method also passes observed truncation/regrowth and invalid UTF-8 |
| V-3 | Not run; mappings compile, runtime readiness observer and test pack not implemented |
| V-4 | Not run; durable real-launch resume producer and delivery test not implemented |
| V-5 | Not run; delayed-receipt queue/retry/failure fences and test not implemented |
| V-6 | Not run; durable acknowledgement/claim/acceptance reconstruction test not implemented |
| V-7 | Not run; caught-up genuine-absence recovery/guard matrix not implemented |
| R-1 | Reader selection passed at 420c32d40f92814f633afab5ef847f2647885c8c; no server/delivery claim |
| R-2 | Not run (V-3 remains) |
| R-3 | Not run (V-4 remains) |
| R-4 | Not run (V-5/V-6 remain) |
| R-5 | Not run (V-7 and CP-5 remain) |

CP-3, CP-4 and CP-5 were not run. The whole Unit lane and every required named affected server integration selection remain unexecuted; the Final profile is **Incomplete**, not Full. These IDs are deferred to the continuation's Final work, never marked passed. No live-provider/manual activation run was commissioned or performed. Ordinary Review remains required after implementation and ordinary V/R finish.

## Checkpoints and provenance

Every run supplied its exact committed expected SHA. All executed rows below had clean source (dirty=0), verified producing build and nonzero fresh TRX rosters. The final receipt independently validates with `validate --evidence .antiphon/checkpoints/20261025-002143-cb37/report.json --expected-source-sha 420c32d40f92814f633afab5ef847f2647885c8c --rows CP-2`: CHECKPOINT SOURCE VALID. Fresh XML inspection retained 12 executions across these five historical runs, with each class/method and outcome present; latest is exactly three intended methods, all Passed.

### 20261024-234929-df07

Expected red: both original methods fail because Ready is absent. Build slot=granted, waited=225s. Evidence: `.antiphon/checkpoints/20261024-234929-df07/report.json`.

```text
CHECKPOINT CP-1 commit=c7dc923efee268f06ea2b7dae779a7c237819bc5 build=ok filter=/*/*/ResumeReceiptReadinessTests/* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-7c1cf5bb/.antiphon/checkpoints/20261024-234929-df07/rows/CP-1/run.trx slot=granted waited=75s dirty=0 source=c7dc923efee268f06ea2b7dae779a7c237819bc5 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=75s executed=20.816s wall=95.900s
PHASES CP-1 slotWait=300s build=47.7512586s startup=2.2059669s testsWall=16.7216327s teardown=0.6216804s hostWall=19.5529148s
BUDGET wall=385.590s execution=85.574s admissionOnly=300.016s
```

### 20261024-235738-aeb9

Historical failure: V-1 passes; V-2 times out at the fork because the authored fixture used a too-short C4 input. The fixture was repaired with distinctive input; no timeout/assertion/ownership guard was loosened. Build slot=granted, waited=225s. Evidence: `.antiphon/checkpoints/20261024-235738-aeb9/report.json`.

```text
CHECKPOINT CP-2 commit=7ce076ab619dbd5f2f57af9af4822e3aa8e990d0 build=ok filter=/*/*/ResumeReceiptReadinessTests/* executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/task-7c1cf5bb/.antiphon/checkpoints/20261024-235738-aeb9/rows/CP-2/run.trx slot=granted waited=450s dirty=0 source=7ce076ab619dbd5f2f57af9af4822e3aa8e990d0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=450s executed=13.683s wall=463.825s
PHASES CP-2 slotWait=675s build=66.4880094s startup=2.495911s testsWall=9.9327259s teardown=0.6141017s hostWall=13.042749s
BUDGET wall=769.973s execution=94.818s admissionOnly=675.155s
```

### 20261025-001106-1255

Corrected two-method green, before the new inspection witness. Build slot=granted, waited=300s. Evidence: `.antiphon/checkpoints/20261025-001106-1255/report.json`.

```text
CHECKPOINT CP-2 commit=8d2a4d5a7c3b31544db5ea1a30c0a5c6287190e7 build=ok filter=/*/*/ResumeReceiptReadinessTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-7c1cf5bb/.antiphon/checkpoints/20261025-001106-1255/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=8d2a4d5a7c3b31544db5ea1a30c0a5c6287190e7 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=30s executed=5.978s wall=36.070s
PHASES CP-2 slotWait=330s build=53.6087945s startup=2.1621126s testsWall=2.2752847s teardown=0.5824449s hostWall=5.0198398s
BUDGET wall=413.773s execution=83.715s admissionOnly=330.058s
```

### 20261025-001934-cb18

Expected red of the changed three-method selection: the new assertion names both truncation-regrowth and invalid-utf8. These were introduced readiness defects, now repaired. Build slot=granted, waited=15s. Evidence: `.antiphon/checkpoints/20261025-001934-cb18/report.json`.

```text
CHECKPOINT CP-2 commit=c489fc8e79ce7e0ea07b9f8c1512d65a741e9904 build=ok filter=/*/*/ResumeReceiptReadinessTests/* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-7c1cf5bb/.antiphon/checkpoints/20261025-001934-cb18/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=c489fc8e79ce7e0ea07b9f8c1512d65a741e9904 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=9.163s wall=9.323s
PHASES CP-2 slotWait=15s build=44.9478973s startup=3.4169674s testsWall=2.9593428s teardown=0.7695512s hostWall=7.1458593s
BUDGET wall=85.001s execution=70.059s admissionOnly=14.942s
```

### 20261025-002143-cb37

Final three-method green after the ordinary repair. Build slot=granted, waited=45s. Evidence: `.antiphon/checkpoints/20261025-002143-cb37/report.json`.

```text
CHECKPOINT CP-2 commit=420c32d40f92814f633afab5ef847f2647885c8c build=ok filter=/*/*/ResumeReceiptReadinessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-7c1cf5bb/.antiphon/checkpoints/20261025-002143-cb37/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=420c32d40f92814f633afab5ef847f2647885c8c sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=30s executed=10.186s wall=40.266s
PHASES CP-2 slotWait=75s build=48.4982332s startup=4.6819466s testsWall=3.7385081s teardown=0.6392587s hostWall=9.0597123s
BUDGET wall=162.393s execution=87.417s admissionOnly=74.976s
```

Latest method durations (one source-qualified sample each, not a measured p95):

- `C1574_Reader_progress_tracks_complete_current_file`: 00:00:01.9771047.
- `C1574_Invalid_binding_or_partial_tail_is_unknown`: 00:00:01.3035979.
- `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: 00:00:00.4040800.

The historical two-method fixture repair and the added three-method proof selection are separate changed proofs, not unchanged green repeats. No loaded/repeat-proof battery or deliberate mutant was run. Existing confirmation/grace settings and reader polling/barrier bounds were not widened.

## Additional builds and cost

The only unlisted builds were:

- Checkpoint CLI bootstrap through scripts/build-slot.ps1, required to execute the checkpoint tool: slot=granted, waited=60s, held=59s. An earlier incorrectly transcribed expected-SHA invocation was stopped while still queued, before any build/driver/executor; no evidence is claimed from it. Later CLI invocations used the existing DLL with no build.
- Server transport compilation at 420c32d40f92814f633afab5ef847f2647885c8c, because the reader project does not compile the changed server mapper/client: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1574-transport-compile -SlotWaitMinutes 5 -- dotnet build server/Antiphon.Server.csproj --property:OutputPath=bin-c1574-transport/ --property:UseAppHost=false --verbosity minimal`. Exit 0, 23 warnings, zero errors, MSBuild elapsed 53.04s; slot=granted, waited=90s, held=54s. Clean source was captured before and checked after. This is compilation only, not a server behavior/receipt qualification. Log: `.antiphon/c1574-transport-build.log`.

No unlisted test selection or full-assembly test was run. Supported duration tokens 45s/4m were used: this checkout's parser does not support the plan's 00:00:45 syntax, and a bare 4 means seconds. Admission was reported separately; no unleased retry or slot bypass occurred. No individual checkpoint run exceeded 30 minutes (largest was 12m50s, mainly admission). Raw source receipts retain historical costs; later green does not erase red. Detailed per-call native IO/HTTP timings not supplied by the tooling are unknown, not zero. Shared DB, web-host and provider-process setup are not applicable to the reader pack; no such setup was run.

Live placement reads GET /api/runner-defaults (revision 2) and GET /api/session-runners succeeded; no Runner/Platform pin was supplied. The static literal ProjectReference graph (53 projects, 16 changed-root/dependent projects, no unresolved literal expressions) is retained in `.antiphon/c1574-project-graph.json`; imports/conditions were not evaluated, so it is not scoped rc qualification. Categories are Claude runner/shared runner contracts and server/session messaging.

## Pending Mutation inventory

Every row and variant below remains **Pending** for method-scoped SourceLanding Mutation, including discovery. Ordinary red/green witnesses do not discharge a PC. PC-1..PC-7 and PC-27/PC-28 have reader implementation/test sites; PC-8..PC-26 remain unbound because S2 is not implemented. The plan amendment adds two independently bypassable guards and increases the provisional Mutation floor from 78 to 84 minutes (28 controls, four batches of seven).

| PC | Status | Compiling defect / variant | Exact method and decisive red oracle |
|---|---|---|---|
| PC-1 | Pending | Publish progress before processing the chunk | V-1 method: Ready remains false at the held processing boundary |
| PC-2 | Pending | Use remembered length after an append | V-1 method: old EOF is not Ready |
| PC-3 | Pending | Bypass revoked-claim check | V-2 method: revoked binding refuses Ready |
| PC-4 | Pending | Preserve progress across fork | V-2 method: new binding refuses old readiness |
| PC-5 | Pending | Bypass physical-file comparison | V-2 method: same-length replacement refuses Ready |
| PC-6 | Pending | Ignore pending bytes | V-2 method: partial line refuses Ready |
| PC-7 | Pending | Ignore parse failure | V-2 method: malformed line refuses Ready |
| PC-8 | Pending | Accept wrong snapshot session | V-3 method: wrong-session observation is Unknown |
| PC-9 | Pending | Accept wrong accepted/current generation | V-3 method: changed-generation observation is Unknown |
| PC-10 | Pending | Ignore a changed runner route | V-3 method: route-race observation is Unknown |
| PC-11 | Pending | Ignore a changed runner store | V-3 method: store-race observation is Unknown |
| PC-12 | Pending | Return Ready before awaiting persistence | V-3 method: held commit keeps observation incomplete |
| PC-13 | Pending | Treat failed/NeedsReload persistence as success | V-3 method: faulted commit cannot return Ready |
| PC-14 | Pending | Use direct Now for resumed note | V-4 method: durable keyed row exists before first input |
| PC-15 | Pending | Mint a fallback key after commit/response failure | V-6 method: exactly one original row and recipient prompt |
| PC-16 | Pending | Bypass flush readiness gate | V-5 method: no additional body/attempt while Behind |
| PC-17 | Pending | Bypass automated Now readiness gate | V-5 method: blocked automated Now writes no input |
| PC-18 | Pending | Bypass SendNow readiness gate | V-5 method: blocked SendNow writes no input |
| PC-19 | Pending | Bypass interrupted recovery readiness gate | V-6 method: no Enter or body before readiness/receipt |
| PC-20 | Pending | Allow destructive failure on non-Ready evidence | V-5 method: zero conditional kills beyond the virtual deadline |
| PC-21 | Pending | Accept a clipped body as complete | V-7 method: clipped receipt never confirms note |
| PC-22 | Pending | Ignore attempt floor | V-4 method: old identical restart note cannot confirm new row |
| PC-23 | Pending | Accept queued-command/screen evidence as receipt | V-7 method: neither surrogate confirms note |
| PC-24 | Pending | Retry before checking late receipt | V-5 method: exactly one submitted body after catch-up |
| PC-25 | Pending | Refund/clear the attempt record on defer | V-6 method: original attempt fields and row ID remain equal |
| PC-26 | Pending | Propagate observation timeout into launch cleanup | V-4 method: timed-out pull leaves completed live launch and retained note |
| PC-27 | Pending | Forget observed truncation when the same file regrows | `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: truncation-regrowth is never Ready without replay |
| PC-28 | Pending | Remove strict byte validation before replacement decoding | `C1574_Truncation_regrowth_and_invalid_utf8_stay_unknown`: invalid-utf8 refuses Ready |

Concrete reader binding sites at tested source: progress publication in TranscriptTailer.RunAsync after ProcessPending; fresh extent/identity/claim/partial/parse decisions in ObserveReadiness (around line 205); binding reset in TryBind; observed-shrink latch at lines 219 and 452 (PC-27 snapshot variant bypasses the line-219 latch); strict complete-line byte validation in ProcessPending at line 555 (PC-28). No concrete S2 mutation recipe is asserted.

## Exact continuation

1. Implement S2-red's five real launch/queue/runtime tests using the shared DB fixture with per-test isolation and real ingestion/DB transitions, plus Unknown-only compilation scaffolding if needed; commit/push and run CP-3 with expected red assertions.
2. Implement durable standing-resume key production, bounded generation/owner/route/store/commit-qualified observation, shared launch receipt fences, strict UserPrompt correlation, preservation/recovery of the same attempted row, caught-up negative-verdict recovery, and the session-runtime invariant paragraph. Commit/push before CP-4/CP-5.
3. Complete every ordinary V/R and required Final Unit/named integration selection. Run no unnecessary unchanged green repeats. Review next only after implementation and ordinary scope finish. Caller later lands the original Code task, commissions SourceLanding Mutation and separately qualifies matching server+runner activation on rc.

Important implementation entry points: flush/enqueue Working checks currently precede DeliverNextLockedAsync (enqueue around 910, OnTurnEndCoreAsync around 1631, stranded around 1785, FlushSessionReportingDeliveryAsync around 1834, FlushIfIdleAsync around 1888). Reconcile attempted notes before those checks; cover immediate Now, SendNow, interrupted Sent recovery and confirm-loop re-Enter. A Ready empty persisted snapshot needs an observable zero baseline. General existing matchers admit queued prompts/tool-result evidence and screen fallbacks; narrow the note contract. Existing checked recovery is local-only: reuse its lease/SQL fencing patterns without silently widening that old policy or rejecting all remote receipt probes. Never hold a lease across a helper that reacquires it.

The two reader concerns noted earlier are now **resolved by ordinary red/green proof**; do not carry them forward as unresolved findings. S2 implementation and trustworthy Final coverage remain the substantive incomplete gates.

All test executors and the explicit server build were joined. Eight task-owned alternate output roots were removed; receipts/logs remain ignored. Initial merge-tree against fetched origin/master 0d51344da28bcb5dea4f37a86c7434bc4c606da4 was clean (tree 427ddedcd84bf1b0c4dddea8d7aa7e8f4d11c522); the final report message carries the post-report publication/audit result. No restart, rollout, land or deployment occurred.
