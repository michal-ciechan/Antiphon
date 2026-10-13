# CARD-1205 S5 recipient acceptance

Code/landing owner: c0e86d50-00e6-4942-b303-7bc44122c640.
Branch: feat/card-task-c0e86d50. Base: 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2.
S5 only; the explicit brief excludes the Unit lane. No production or shared-fixture
changes are authorized. The frozen ordinary census is CP-5=30 and CP-6=4.

The first test-only group implements T37 in both cache modes. Actual adapter bytes
enter runner custody and HTTP/runtime persistence. Its expected defect is PC-84
(suppressed Sync recovered-boundary wakeup), owned by later SourceLanding Mutation.
Run the witness on unchanged pre-S5 production first; record its actual outcome.
The S1/S2 repair already landed, so baseline red is not assumed.

Extra setup: one build-slot gated checkpoint-tool bootstrap into
bin-c1205-s5-tool/, UseAppHost=false. No full-assembly, deployment or native PTY run.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| S5-BASE | Tests | `tests/Antiphon.Tests -> bin-c1205-s5-base/` | five-note-baseline | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once*` | T37,V-20,R-5 | Two executed on unchanged pre-S5 production; report actual outcome | 2 | 6 |
| S5-A | Resume | `tests/Antiphon.Tests -> bin-c1205-s5-resume/` | recovery-restart | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/*` | T37..42,T44,T46 | Ten cases before adding T43/T45 | 10 | 12 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1205-s5/` | restart-receipts | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/*` | V-20..24,V-36..40,R-5 | Exactly 30 executed, all named cases green | 30 | 16 |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c1205-receipts/` | receipt-compatibility | `/*/*/(VerificationRoundDeliveryTests*)\|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)\|(C544_StampIsNotReceipt*)\|(Registry_matches_compiled_metadata*)` | R-8,R-9 | Exactly four named methods green | 4 | 7 |

All PC-84..100 variants remain pending. No deliberate mutant is authorized here.

## Partial Code handoff, 2026-10-13

**S5 is incomplete and must not land.** The first two T37 cases and CP-6 pass.
T38..T46 (28 executions) are not authored. CP-5's full 30-case selection was not
run. This is not a completed Final verification round or a Review-ready change.

The binding brief at `.antiphon/inbox/31705a5b-7dce-445b-b5b4-4a2575311915.md`
says every new test “must really go red on the pre-slice code.” This is a test-only
S5 dispatch on already-landed S1..S4; the correct composed T37 witness passes on
unchanged pre-S5 production. Deliberate mutants/red-restore-green cycles belong
to SourceLanding Mutation. The caller must clarify whether to continue the
frozen acceptance matrix with those defect proofs pending for Mutation. No
fixture failure below is offered as a guarded-defect red. No missing production
dependency was established. No source repair outside S5 was attempted.

### Implemented witness and limits

`TurnEndGapRestartDeliveryTests.C1205_Restart_delivers_five_gap_held_completions_once`
runs with cache enabled and disabled. Real C544World task creation/reply settlement
creates five independent completion obligations. Busy phases retain five Pending
queue rows with zero attempts and submissions. A fresh provider reconstructs the
runtime, store, queue, notifier and flush queue. The actual completion worker's
boot sweep is observed while busy before the pump begins the captured gap.

The real HTTP client/pump and real non-replaying hub recover the ordinary end.
Actual adapter bytes enter runner custody and are persisted through HTTP/runtime,
including the real queue's receipt grace pull when that serial pump is occupied.
The finite observation checks processed live frames, an empty coalesced flush
directory, no recovery hints and the actual registered 15-minute backstop timer.
The guard budget is teardown protection, not an absence verdict. Queue timers
advance only at their registered 50 ms poll and 20 ms Enter barriers.

Each case requires five submissions, five notifier confirmations, complete wire
text/digest matching destination UserPrompts above their attempt floors, and stable
TaskId/SourceEventId/notification/queue identities. A second fresh-provider restart
and two notifier reconciliations require the same receipts without additional
submissions. Epoch adapters dispose only the old server output subscription;
the simulated provider, composer and submission ledger survive. SSE disposal
cancels its own hub subscription. Worker/pump lifetimes are stopped and awaited.

These are service-restart and controlled-provider witnesses, not native terminal,
JSONL capture, Kestrel scheduling, model or AppHost deployment acceptance. T37
holds the independent watchdog without ticking it; T38..T46 remain necessary.

### Actual outcomes and source custody

Actual tested source for both green runs:
`4139dec15f3421f29923f5a7790e22b1dbd0a49e`.
Production under `server/` and `src/` is byte-identical to task base
`6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2`.
Both green report.json receipts passed `validate-checkpoint-receipt.ps1` with
that exact SHA. Fresh TRX inspection found the intended True/False argument
rows and all four CP-6 method names, with nonzero counts and no skips.

| ID | Actual outcome |
|---|---|
| V-20, R-5 / T37 | Two passed, zero failed/skipped in S5-BASE; full CP-5 not run |
| V-21, V-22, V-23, V-24 | Not implemented/run; deferred to completing S5 |
| V-36, V-37, V-38, V-39, V-40 | Not implemented/run; deferred to completing S5 |
| R-8 | Three named receipt methods passed in CP-6 |
| R-9 | Named metadata method passed in CP-6 |
| CP-5 | Not run; 28 of its 30 cases remain unimplemented |
| CP-6 | Four executed/passed, zero failed/skipped |
| Unit lane | Omitted as explicitly required by the S5 brief; only CP-6's named metadata method ran |
| Live manual work / restart | None required for this controlled fixture; server=none, runner=none |

Essential tool lines, unedited:

```text
CHECKPOINT S5-BASE commit=4139dec15f3421f29923f5a7790e22b1dbd0a49e build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-103814-b4df/rows/S5-BASE/run.trx slot=granted waited=0s dirty=0 source=4139dec15f3421f29923f5a7790e22b1dbd0a49e sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=4139dec15f3421f29923f5a7790e22b1dbd0a49e build=ok filter=/*/*/(VerificationRoundDeliveryTests*)|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)|(C544_StampIsNotReceipt*)|(Registry_matches_compiled_metadata*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-104522-b189/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=4139dec15f3421f29923f5a7790e22b1dbd0a49e sourceState=clean buildSource=verified
```

T37 True: 20.9556507 seconds; False: 6.6803158 seconds. CP-6 methods:
Registry_matches_compiled_metadata (1.9397044), C544_CompletionReceipt
(20.9506376), C544_CompletionReceiptWholeWire (17.0993491),
C544_StampIsNotReceipt (2.1921669). Counts are executions, not internal loops.
Measured run wall times: S5-BASE 4m45s and CP-6 4m52s, including isolated builds.
CP-6 ran independently while the full CP-5 matrix was held at the proof-policy
question; it does not satisfy CP-5 or the requested CP-5-then-CP-6 final sequence.
No full assembly or broad Unit selection was run.

### Test-first/fixture repair history (not defect proof)

Each source slice was committed and pushed before its row. The only extra setup
build was the leased checkpoint-tool bootstrap (18 seconds, slot=granted,
waited=0s, zero errors and the existing TaskOwnerGuard CS8602 warning). All row
build/test drivers used the gate. No timeout, assertion or retry was loosened.
An initial no-build tool launcher omitted UseAppHost=false and failed before
owner admission; it launched no build/test. It was corrected without rebuilding.

| Source / run | Actual outcome and classification |
|---|---|
| 73313df9eedcc5fff21e866c184ce104dd263b11 / 20261013-100539-7b82 | Build failed: missing loader import and ambiguous settings; zero tests; corrected |
| 1705124e8e5570d1f564143bc07e6eb30392380a / 20261013-100859-231a | Build passed (slot=granted, waited=15s); test row still waiting for slot when explicitly stopped; no TRX/certificate; tool stop and wait confirmed executor dead |
| e6aeb198979267fae47bcb4f339c0604f459314c / 20261013-101550-a247 | Two assertion failures, expected five submissions/actual zero; simulated end lacked EndTurn reason; fixture red |
| b4b3e0941a547f08ec1331aac5d52b261aba1cf4 / 20261013-102232-d432 | Two same fixture reds; diagnostics proved stored end/SQL idle, no queue action |
| 489ac1606b59005eea76cbc7a02e4a12a9dc8aca / 20261013-102706-79b9 | Two assertion failures, expected five/actual one; real completion recovery worker missing from composition; incomplete-fixture red |
| 4139dec15f3421f29923f5a7790e22b1dbd0a49e / 20261013-103814-b4df | Correct composed witness, two green; no ordinary repetition after green |

Earlier unedited diagnostic lines:

```text
CHECKPOINT S5-BASE commit=73313df9eedcc5fff21e866c184ce104dd263b11 build=failed filter=/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=73313df9eedcc5fff21e866c184ce104dd263b11 sourceState=clean buildSource=unknown
CHECKPOINT S5-BASE commit=e6aeb198979267fae47bcb4f339c0604f459314c build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-101550-a247/rows/S5-BASE/run.trx slot=granted waited=0s dirty=0 source=e6aeb198979267fae47bcb4f339c0604f459314c sourceState=clean buildSource=verified
CHECKPOINT S5-BASE commit=b4b3e0941a547f08ec1331aac5d52b261aba1cf4 build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-102232-d432/rows/S5-BASE/run.trx slot=granted waited=0s dirty=0 source=b4b3e0941a547f08ec1331aac5d52b261aba1cf4 sourceState=clean buildSource=verified
CHECKPOINT S5-BASE commit=489ac1606b59005eea76cbc7a02e4a12a9dc8aca build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_delivers_five_gap_held_completions_once* executed=2 passed=0 failed=2 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-102706-79b9/rows/S5-BASE/run.trx slot=granted waited=0s dirty=0 source=489ac1606b59005eea76cbc7a02e4a12a9dc8aca sourceState=clean buildSource=verified
```

All deliberate controls remain pending in the
[unbound partial candidate](../mutations/2026-10-13-card-1205-s5-candidate.md).
That candidate cannot be commissioned until S5 ordinary work and Review complete.
Continuation: use the published branch; resolve the baseline gate, implement
T38..T46 without broadening production scope, commit/push, then run CP-5 (30)
followed by CP-6 (4) through the tool at exact committed HEAD. Any necessary
unlanded production dependency must be returned to the caller, not repaired here.

--- next stage ---
next: decide
handoff: Reconcile pre-S5 defect-red requirements with test-only S5 on already repaired production and Mutation ownership; then resume Code for T38..T46 and full CP-5=30 followed by CP-6=4. T37=2 and CP-6=4 pass, but S5 and Final verification remain incomplete. Do not land this partial branch.
artifact: docs/superpowers/plans/2026-10-13-card-1205-s5-checkpoints.md

## Caller continuation decision

The 2026-10-13 reply in
`.antiphon/inbox/f9ff4934-a3c3-4405-9365-39000956e63d.md` authorizes continuing
S5: green on pre-S5 production by construction; defect-red proof by Mutation:
PC-84..100. This supersedes the earlier baseline-gate hold. The binding Code
stage contract assigns deliberate scratch-copy mutations to Mutation too; no
Code-stage mutation is claimed. Family controls remain explicit Mutation work.
Resume owns T38..T46 and full CP-5 then CP-6, no broad Unit lane. Stop 20 minutes
before the original 240-minute task ceiling if incomplete.

S5-A is a disclosed committed test-first group: T37/T38/T39/T40/T41/T42/T44/T46,
ten executions, on unchanged production. T43/T45 follow in the next test slice.
The extra leased bootstrap rebuilds the tool output removed at the previous hold.
