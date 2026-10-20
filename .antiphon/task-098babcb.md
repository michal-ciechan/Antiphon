# CARD-1284 CP-5: reproduced and isolated to the CARD-1259 S2 fixture contract

Task: 098babcb. Linux/server2; master-only. No production or test edits.

The current-master failure reproduced at e8bd995b0481e838c0110217dc4f680fe4de8374. It is a native recovery fixture dependency mismatch: CARD-1259 S2 (dd039a405c9425d7cffc83b64d60a1032d940636) made transcript ingestion require SessionStateStore and deferred transcript effects through SessionRuntimeActionCoordinator, while ChannelOutboundRecoveryTests.BuildNativeRecoveryProvider still registers neither. The caller's provisional missing-ready-wait hypothesis is contradicted by a successful native submit and completed native transcript before the delivery exception.

## Evidence from current master

Run 20261020-153910-7a1e: CP-5 executed 1, passed 0, failed 1, skipped 0. Build slot granted for build and row; dirty=0, sourceState=clean, buildSource=verified. TRX failure is the same ConflictException reported by prior verification: `the submitting Enter produced no output`, raised through SessionMessageQueueService.cs:1566/1573 and awaited at ChannelOutboundRecoveryTests.cs:878. The test ended 2026-10-20T15:46:53.5301818Z.

The read-only observer copied only task-associated fixture files, identified by the fake wrapper's executable path pointing inside this task's checkout. Its 150ms observations are approximate arrival times, not original file-write timestamps. The native `.timing` sidecar provides the writer timestamps below. Captures are ignored under `.antiphon/debug-098babcb/native/antiphon-outbound-admission-2bc55bec36a3488a8dfe17db78bf580d/`.

- ANSI log: `Fake Grok ready`, `SUBMITTED:`, and `Converted 1 frozen Markdown sources in the running fixture.` are all present.
- `native-input-shape.txt`: `bytes=676 cr=0 lf=0 paste=False`, followed by `bytes=1 cr=1 lf=0 paste=False` (and two later Enter retries). The body and submitting CR arrived separately.
- Observer: first CR at 15:46:21.823Z; gate held at 15:46:21.973Z; gate released at 15:46:30.534Z. The gate was held about 8.56 seconds, below the 30-second confirm deadline.
- Native `updates.jsonl` lines 1-3 contain `user_message_chunk` with the complete marker-bearing task brief, `agent_message_chunk` with the conversion reply and done token, and `turn_completed` with `stop_reason=end_turn`.
- Native `updates.jsonl.timing` lines 2-4 stamp those writes at 15:46:30.5415377Z, 15:46:30.5500882Z, and 15:46:30.5683665Z: almost 23 seconds before the failing test finished.
- The runner transcript-binding file points to that exact updates.jsonl with `how=deterministic` and a bound session matching the CLI's session id.

The fake's SubmitTurn emits its responding title and SUBMITTED text before OutboundRequestMode.TryRun. That converter sets `.held`, waits for `.release`, then returns; only afterwards does SubmitTurn append the native prompt/reply/turn records. The test waits for `.held`, kills the dispatcher, verifies durable ownership, runs the admission probe, releases the gate and awaits nativeBriefDelivery. Current master completed this sequence well inside the queue's deadline. The queue's catch-up cannot persist the resulting native entries because this fixture has no SessionStateStore.

## Source cause and repair handoff

`server/Application/Services/AgentSessionRuntime.cs:1064` requires the store; its catch at 1078-1082 records a persist failure and returns empty. Pull/catch-up consequently never creates the durable UserPrompt used by delivery verification. At an initially unobservable baseline, SessionMessageQueueService can return NoSubmitOutput when no durable confirming prompt arrives (around 4123-4158), including its echoed-body fallback. The captured verdict is NoSubmitOutput; no console logger exposed which fallback branch fired. This verdict does not prove that the real Enter produced no bytes; the native evidence shows otherwise. The persist exception text is established from source/DI, not a captured console exception: this provider uses AddLogging without a console sink.

`tests/Antiphon.Tests/Application/ChannelOutboundRecoveryTests.cs:1431` is the stale provider. Its file SHA-256 is identical at the parent, S2 and current master: `2c3dd9e885c087eeb4ac4d3e3d70b7a567c4de48418dfae15c41b54c72ca89e3`. Neither this file nor FakeGrok changed across S2.

Next Code: repair this test fixture, not production delivery or fake stdin. Register IOptions<SessionStateSettings>, SessionStateCommandMetrics, ISessionStateLoader/SessionStateLoader, SessionStateStore and SessionRuntimeActionCoordinator in BuildNativeRecoveryProvider (follow the existing BridgeQueueHarness registrations). Update ReconcileNativeWorkerAsync to drive the coordinator's session-scoped DrainPageAsync after SyncTranscriptAsync so persisted actions actually settle the task; the old inline-effect assumption is obsolete. Preserve the converter gate and all existing prompt/publication assertions. Use the same exact CP-5 selection for red/green evidence. Do not widen timeouts, loosen assertions, disable transcript confirmation or add sleeps/retries. This graph/dispatch repair exceeds the brief's narrow ready-wait exception, so no fix was attempted here.

Native completeness cross-check: the ANSI `SUBMITTED:` body exactly equals the native UserPrompt text (672 characters / 676 UTF-8 bytes). The input-shape record says the body burst contained 676 bytes, so the submitted body is accounted for without truncation.

## Historical comparison

- Immediate pre-S2 parent `ee5e0fede35da4685b37b9f0deca3f251e3250b2`: run `20261020-155303-e135`, CP-5 1 passed / 0 failed / 0 skipped. The tool deleted its outputs and source validation exited 0: `CHECKPOINT SOURCE VALID source=ee5e0fede35da4685b37b9f0deca3f251e3250b2 rows=1`.
- Exact S2 code commit `dd039a405c9425d7cffc83b64d60a1032d940636`: run `20261020-154729-d224`, build passed, but BeforeAssembly failed with EF Core PendingModelChangesWarning. Its TRX reports one failed test; the body did not execute, so this is an incomplete diagnostic and is not counted as reproducing the delivery failure. This WIP commit adds the model before the immediately following migration commit.
- The next commit `4e58e05c6102ecf70b8fff3e3908c00a6db8370f` changes only the S2 migration, its designer, and the model snapshot. Its runtime, fake and test code are identical to S2. Run `20261020-155940-9880` at that schema-complete point reproduced the same ConflictException at the same test await: 1 executed, 0 passed, 1 failed, 0 skipped. Its native capture also contains the submitted UserPrompt, conversion AssistantText and TurnEnd after a roughly 9.76-second gate hold. This closes the behavioral boundary: the parent passes; S2 plus its migration fails. The runtime code causing the fixture dependency mismatch was introduced in dd039a405; the first runnable bad point is 4e58e05c.

These are manually created clean detached comparison checkouts, run with the same current checkpoint binary. They do not change the assigned branch. The original plan was extracted from `22e6d1dae4681e729d61186d150df1647ee13bae` into ignored `.antiphon/debug-098babcb/source-plan.md`; only CP-5 was selected. The separate `.antiphon/task-cd5febb7.md` named by the brief is absent from that commit, but the plan contains the full preceding verification and attribution evidence.

## Execution census and costs

Three test bodies ran: one passed and two failed with the target delivery exception. A fourth checkpoint reported one BeforeAssembly failure and never entered the body. Four isolated checkpoint builds succeeded; one additional gated checkpoint-tool bootstrap succeeded (4 warnings, 0 errors, compiler time 20.13 seconds). No class, lane, or namespace run was executed. Three historical probes were used, below the cap of four.

| Source | Run | Executed / passed / failed / skipped (TRX) | Disposition | Reported run wall |
|---|---|---|---|---|
| e8bd995b0481e838c0110217dc4f680fe4de8374 | 20261020-153910-7a1e | 1 / 0 / 1 / 0 | Target ConflictException reproduced | 459.78s |
| dd039a405c9425d7cffc83b64d60a1032d940636 | 20261020-154729-d224 | 1 / 0 / 1 / 0 | BeforeAssembly model/schema refusal; body not executed | 299.50s |
| ee5e0fede35da4685b37b9f0deca3f251e3250b2 | 20261020-155303-e135 | 1 / 1 / 0 / 0 | Passed; source receipt validated | 314.97s |
| 4e58e05c6102ecf70b8fff3e3908c00a6db8370f | 20261020-155940-9880 | 1 / 0 / 1 / 0 | Target ConflictException reproduced | 307.02s |

All four reports record sourceState=clean and buildSource=verified, and each build and row held a granted host build slot. The setup-refused WIP result is not behavioral evidence. The three red checkpoints retained outputs; those exact task-created roots were removed manually after executor death. Their historical cleanup refusal fields were not rewritten, and they are not green qualifications. The parent green receipt passed deterministic validation before its checkout was removed. The CHECKPOINT/TRX paths below preserve the original worktree names; the complete run folders were copied unchanged into this checkout’s ignored `.antiphon/checkpoints/<run-id>/` before all three temporary worktrees were removed.

## Unedited checkpoint lines

```text
CHECKPOINT CP-5 commit=e8bd995b0481e838c0110217dc4f680fe4de8374 build=ok filter=/*/*/ChannelOutboundRecoveryTests*/Running_converter_reconciles_after_dispatcher_death* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-098babcb/.antiphon/checkpoints/20261020-153910-7a1e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=e8bd995b0481e838c0110217dc4f680fe4de8374 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=dd039a405c9425d7cffc83b64d60a1032d940636 build=ok filter=/*/*/ChannelOutboundRecoveryTests*/Running_converter_reconciles_after_dispatcher_death* executed=1 passed=0 failed=1 skipped=0 trx=/tmp/antiphon-c1284-098babcb/after-s2/.antiphon/checkpoints/20261020-154729-d224/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=dd039a405c9425d7cffc83b64d60a1032d940636 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=ee5e0fede35da4685b37b9f0deca3f251e3250b2 build=ok filter=/*/*/ChannelOutboundRecoveryTests*/Running_converter_reconciles_after_dispatcher_death* executed=1 passed=1 failed=0 skipped=0 trx=/tmp/antiphon-c1284-098babcb/before-s2/.antiphon/checkpoints/20261020-155303-e135/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=ee5e0fede35da4685b37b9f0deca3f251e3250b2 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4e58e05c6102ecf70b8fff3e3908c00a6db8370f build=ok filter=/*/*/ChannelOutboundRecoveryTests*/Running_converter_reconciles_after_dispatcher_death* executed=1 passed=0 failed=1 skipped=0 trx=/tmp/antiphon-c1284-098babcb/after-s2-schema/.antiphon/checkpoints/20261020-155940-9880/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=4e58e05c6102ecf70b8fff3e3908c00a6db8370f sourceState=clean buildSource=verified
```

## TRX identities and digests

| Run | TestRun ID | TRX SHA-256 |
|---|---|---|
| 20261020-153910-7a1e | c4aef290-c873-4cb0-b46c-674eeedaf259 | f13abf0d4089afcb65fda00e2bf283c04f5284e6c09cf753b59e4a1a65d7083e |
| 20261020-154729-d224 | 3f0a563a-ea5b-4871-b6e7-22c45a3d669a | 1b5b312944c5e7fc4328acb95e99f5eb982a7cdbb851fd745cb8c06095f6657a |
| 20261020-155303-e135 | 293923c1-7fce-43c9-ac44-b64006923ef5 | 2366ce30615ad05e7cd56750a983638262d3fc1500faa3afd3852470801fd85e |
| 20261020-155940-9880 | b70a6d54-cf7e-40a2-b188-10f8f01aa426 | 9455296b481f96640a3aeabf55bdaf05993bc62c49a4300828344b4c3cfd4ad5 |

## Reproduction commands

Use the immutable verification plan, since it is not present on the tested master. Bootstrap and all rows are gated; the checkpoint tool takes its own row/build leases. Run only CP-5.

```sh
mkdir -p .antiphon/debug-098babcb
git show 22e6d1dae4681e729d61186d150df1647ee13bae:docs/superpowers/plans/2026-10-20-card-1284-linux-fake-cli-regression-plan.md > .antiphon/debug-098babcb/source-plan.md
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1284-debug-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1284-debug-tool/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1284-debug-tool/Antiphon.Checkpoints.dll run --plan .antiphon/debug-098babcb/source-plan.md --rows CP-5 --serial --row-timeout 40m --expected-source-sha e8bd995b0481e838c0110217dc4f680fe4de8374 --max-wait 45s
```

After exit 75, call `wait --run <run-id> --max-wait 45s` through the same DLL until terminal. Historical probes used this same binary, `--repo-root <clean-detached-checkout>`, the plan copied beneath that checkout, and the corresponding full expected SHA. The plan supplies `TUNIT_MAX_PARALLEL_TESTS=1` and the trailing-wildcard method filter. The ignored Node observer wraps only these tool commands and reads/copies task-owned fixture logs; it neither writes to the fixture nor launches another test. No owner, slot, timeout, environment, platform guard or assertion was bypassed.

## Settlement

No production or test source changed. Only this Markdown report is delivered on the assigned branch. Raw receipts, logs, TRX, native captures and observer scripts stay ignored. All executors and observers exited, the three detached comparison checkouts were removed, and all task-created alternate compiler outputs were removed. There was no RC operation, restart, deployment or activation. The current CP-5 acceptance gate remains red; the earlier 17 passing results and both CARD-1277 comparison passes retain their original source attribution and were not rerun.

--- next stage ---
next: code
handoff: Repair ChannelOutboundRecoveryTests.BuildNativeRecoveryProvider to register the session-state and runtime-action graph; drive session-scoped runtime action draining in ReconcileNativeWorkerAsync after transcript sync. Preserve the converter gate and assertions, and prove the repair with CP-5 only. CARD-1284 remains open.
artifact: /work/worktrees/task-098babcb/.antiphon/task-098babcb.md
