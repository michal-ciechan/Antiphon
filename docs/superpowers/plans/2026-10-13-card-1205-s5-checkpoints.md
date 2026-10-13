# CARD-1205 S5 recipient acceptance

**Ordinary S5 implementation and Final slice verification are complete:**
CP-5=30/30 and CP-6=4/4, zero failures/skips, on
`52e9463282afe6c1c782a061753a1b044800c60e`. The later artifact commit updates
documentation only. The historical holds below are superseded by the final
results at the end. Every deliberate PC/family control remains pending Mutation.
The caller has assigned the scratch-family proofs to a separate probe task;
they remain pending and no longer block this ordinary Code handoff to Review.

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
| S5-A | Resume | `tests/Antiphon.Tests -> bin-c1205-s5-resume/` | recovery-restart | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/(C1205_Restart_delivers*)\|(C1205_Watchdog_delivers*)\|(C1205_Restart_after_transcript*)\|(C1205_Receipt_restart*)\|(C1205_Busy_tail*)\|(C1205_Already_eligible*)\|(C1205_Unknown_probe*)\|(C1205_Flush_rechecks*)` | T37..42,T44,T46 | Exactly ten cases, excluding T43/T45 | 10 | 12 |
| CP-5 | S5 | `tests/Antiphon.Tests -> bin-c1205-s5/` | restart-receipts | `/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/*` | V-20..24,V-36..40,R-5,F1-guard | Exactly 31 executed, all named cases green | 31 | 16 |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c1205-receipts/` | receipt-compatibility | `/*/*/(VerificationRoundDeliveryTests*)\|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)\|(C544_StampIsNotReceipt*)\|(Registry_matches_compiled_metadata*)` | R-8,R-9 | Exactly four named methods green | 4 | 7 |

All PC-84..100 variants remain pending. The caller authorizes scratch-family
mutants, but the binding Code-stage contract reserves their execution to Mutation.

## Historical first Code hold, 2026-10-13 (superseded)

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

## Final ordinary verification, 2026-10-13

T37..T46 are all implemented in the card-specific harness/class. All witnesses
are green on pre-S5 production by construction; defect-red proof by Mutation:
PC-84..100. No production/shared-fixture/checkpoint-infrastructure edit was made.
The named metadata entry is updated for the full delivery matrix.

| Witness | Ordinary IDs | Executions / actual outcome | Pending defect-red proof |
|---|---|---|---|
| T37 five-note restart | V-20, R-5 | 2 passed (cache enabled/disabled) | PC-84, both variants |
| T38 healthy-stream watchdog | V-21 | 1 passed | PC-85 |
| T39 committed transcript/lost wakeup | V-22 | 1 passed | PC-86 |
| T40 receipt restart/no retyping | V-23 | 1 passed | PC-87, PC-88 |
| T41 busy authoritative tail | V-24 | 1 passed | PC-89 |
| T42 eligible producer-to-recipient | V-36 | 1 passed | PC-90 |
| T43 handoff cuts | V-37 | 16 passed: eight cuts, busy/eligible | PC-91..95, all independent variants |
| T44 uncertain probe/valid recovery | V-38 | 2 passed: wrong generation/pull fault | PC-96, both variants |
| T45 invalid receipts | V-39 | 4 passed: header/truncated/destination/floor | PC-97..99, all variants |
| T46 newer turn before wake consumption | V-40 | 1 passed | PC-100 |
| Named existing receipt compatibility | R-8 | 3 passed | Existing defect controls remain Mutation work |
| Named metadata guard | R-9 | 1 passed | Registry compatibility, no commissioned systematic PC |

There are no deferred ordinary S5 IDs. Unit lane is excluded by the explicit
brief; CP-6 includes only the single named metadata case. No full assembly,
native terminal, live broker, AppHost deployment or manual live acceptance was
run. Restart: server=none, runner=none. Fresh providers prove service reconstruction.

T38 drives real checked probes with acknowledged healthy keepalives. T39 holds
the owned successful probe before base TryEnqueue, observes committed SQL idle,
cancels/joins the old service, and requires the real fresh completion-scan producer.
T40/T43 observe actual committed prompts before confirmation faults. T41/T44
retain immutable obligations without attempts/receipts while Working/Unknown.
T46 consumes the real wake channel only after a new prompt commits, then invokes
the normal worker's concrete idle-recheck primitive.

T43 reaches each armed cut once before restart, keeps committed keys/frozen wires,
replays rolled-back settlement, observes actual notification scanner recovery,
and invokes the established stranded primitive after the existing 36-second
interrupted-attempt age. The full-channel rows first prove a same-epoch requested
sweep with four blocked real worker scopes and a full 128-entry channel, then
use a distinct obligation for crash/full-channel restart recovery. T45 holds the
actual accepted provider response for an independent explicit flusher; the
notifier scans real HTTP/runtime-ingested invalid evidence before retry effects.
Wrong-destination evidence is above the attempt floor; below-floor evidence is
actual earlier provider input, not direct SQL receipt insertion.

The fixture's controlled continuation queue observes the real production
ExecuteAsync loops between continuations so a synchronous coalesced
remove/re-enqueue cannot look quiescent. It owns cancellation/join for those loops.
Hosting.Abstractions 10.0.2 is resolved in project.assets.json; its launcher
[runs ExecuteAsync on a background task](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/backgroundservice-executeasync-task).
This fixture substitute does not certify framework launcher scheduling. The
full-channel barrier uses normal framework launch to permit four independently
blocked scopes; all gates are released and workers joined at teardown.

Fresh TRX roster inspection verified every intended method and argument count.
Both rows passed `validate-checkpoint-receipt.ps1` with exact committed source
`52e9463282afe6c1c782a061753a1b044800c60e`. Essential unedited lines:

```text
CHECKPOINT CP-5 commit=52e9463282afe6c1c782a061753a1b044800c60e build=ok filter=/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-121816-d02e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=52e9463282afe6c1c782a061753a1b044800c60e sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=52e9463282afe6c1c782a061753a1b044800c60e build=ok filter=/*/*/(VerificationRoundDeliveryTests*)|(TestClassificationGuardTests*)/(C544_CompletionReceipt*)|(C544_StampIsNotReceipt*)|(Registry_matches_compiled_metadata*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-c0e86d50/.antiphon/checkpoints/20261013-121816-d02e/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=52e9463282afe6c1c782a061753a1b044800c60e sourceState=clean buildSource=verified
```

Measured final run: 10m04s; one isolated build per row, serial CP-5 then CP-6.
CP-5: build 207.53s, startup 53.95s, tests wall 123.47s, teardown 11.44s.
CP-6: build 124.47s, startup 41.95s, tests wall 37.52s, teardown 1.49s.
Every selected build/row had slot=granted, waited=0s. The sole extra resumed setup
build was the disclosed leased tool bootstrap (4s, same pre-existing CS8602 warning).
No green full-class proof was repeated; no flake or Review repetition exemption
is claimed. Source fixes were pushed before changed-fixture runs.

### Continuation repair evidence, not production-defect reds

| Run / tested source | Outcome |
|---|---|
| 20261013-111105-878c / a1cbc929da800aeb2047bfa4141df9a8e134143a | Build failed before tests: two invalid property declarations; corrected |
| 20261013-111537-2df7 / 6c7c874cf81027a258d5c784a0ef228fd5a25e9c | S5-A 10 executed: 8 passed, 2 Unknown-variant receipt-count reds; transient quiescence observation corrected |
| 20261013-112719-f3e3 / 9e989836e7574cd5b9c29f1684670aea91ae56af | Build failed before tests: TUnit rejected async-void launcher; corrected |
| 20261013-113333-62fb / 73c0218544368fec9faaaf1eb76b987988a803bd | Explicitly stopped after fixture progress wait stalled; no final certificate; stop/wait confirmed executor dead |
| 20261013-115129-0395 / 1df9d20a7a33a347f0db78c94352122961316ec2 | CP-5=30: 24 passed, 6 fixture reds; CP-6=4 green |
| 20261013-120749-2b92 / ed59461984dcdc4deb8e1633c03b2dad2f8f34f6 | Explicitly stopped after invalid receipt fixture waited on delivered-only hook; no final certificate; executor dead confirmed |
| 20261013-121816-d02e / 52e9463282afe6c1c782a061753a1b044800c60e | CP-5=30 and CP-6=4 green, clean/verified receipts |

No assertion or timeout was widened. Invalid attempt assertions remain exactly
one: the response gate prevents unrelated automatic truncation retries from
changing the observation. A backstop timer allocated for a ready signal is not
an idle-pass oracle; the repaired control requires it live and debt-free.
Owner reads had transient 502/timeouts but the bound runs completed; no unbound
bypass was used. Placement revision 2 and eligible Any capacity were refreshed;
no runner/platform pin was added. Original task CreatedAt=09:54:57.099636Z gives
a conservative requested stop point of 13:34:57Z; verification finished earlier.

### Mutation ownership

The caller's continuation also requests one scratch-copy deliberate mutation per
witness family before systematic SourceLanding variants. **Those family cycles
were not run in Code.** The binding stage contract states: “Mutation owns
deliberate mutants, red/restore/green and missing-control discovery.” This includes
uncommitted scratch-copy mutations. The unbound candidate names the family
selections for caller commission; it makes no red-capability/PC-clean claim.
All PC-84..100/variants and inherited S1..S4 controls stay pending for Mutation.

### Refinement disposition, 2026-10-13

The refinement in
`.antiphon/inbox/3f1bc8b8-7cc6-41e0-a18e-d8fbf7898783.md` reaffirms that one
throw-away scratch mutation per witness family is required and says Mutation is
paused. It changes the requested acceptance status: **ordinary S5 verification
is complete, but the newly reaffirmed family-proof requirement is not complete**.
FC-1..FC-6 remain unexecuted; there is no failing output for any family and no
claim that the acceptance witnesses have demonstrated defect sensitivity.

The active developer-provided `stage-code` bundle explicitly says: “Mutation
owns deliberate mutants, red/restore/green and missing-control discovery.” This
is a binding instruction, not a default in a repository document. An uncommitted
scratch-copy edit disabling production behavior is still a deliberate mutant.
The caller refinement does not change that higher-priority stage instruction.
No scratch source was created or modified and no additional build/test was run.
The final source receipt remains the exact tested SHA above; this amendment is
documentation only and does not require repeating the green ordinary selections.

Continuation needs either a higher-priority stage-contract amendment permitting
these six Code-stage family cycles, or caller commission of their execution to
Mutation. This delegate cannot change its role or sub-delegate. The complete
ordinary implementation remains available for Review, with all systematic
PC-84..100 variants pending after original-owner landing as before.

### Caller closure decision

The caller confirmed that a separate probe task will execute the scratch-family
proofs and requested clean closure. This resolves the Code handoff hold without
claiming any family red or systematic PC as passed. Ordinary S5 is complete;
all existing tested-source receipts remain unchanged. The previously reported
infrastructure sync delay requires no decision from this delegate.

--- next stage ---
next: review
handoff: Review ordinary S5 acceptance: CP-5 30/30 and CP-6 4/4 with clean/verified receipts. Caller assigned FC-1..FC-6 scratch-family proofs to a separate probe task; none are claimed passed here. All systematic PC-84..100 variants remain pending for Mutation after original Code owner landing.
artifact: docs/superpowers/plans/2026-10-13-card-1205-s5-checkpoints.md

## S5 F1 repair, task 3280a044

Repair base: 4114cd5fc43df17a22d5d84c85b34f8077f89799. New landing owner:
3280a044-57d9-40e5-a1dc-c3c72ed31db1; prior Code task
c0e86d50-00e6-4942-b303-7bc44122c640. Branch feat/card-task-3280a044.

The repair adds one oracle-adequacy execution:
`/*/Antiphon.Tests.Application/TurnEndGapRestartDeliveryTests*/C1205_Restart_recovery_worker_executes_queue_cycle*`.
CP-5's full-class selection includes it: 31 executions; CP-6 remains four.
Run only CP-5/CP-6, serial, against committed HEAD. No broad Unit lane,
production change, timeout increase, full-assembly or live/manual run.

T37's second restart and T40 now enqueue the recipient into the fresh real
CompletionNoteWorkHostedService, advance only the fake clock beyond the established
36-second interrupted-attempt boundary, and join its real queue cycles. T40 cuts
the existing verdict save after complete prompt ingestion, asserts Sent/null
verdict before restart, and requires LateConfirmed after recovery. Both preserve
submission bodies, note/queue identities and the attempt identity fields
(DeliveryAttempts, LastDeliveryStartedAt, generation and baseline sequence).
The new guard requires at least one completed post-restart delivery scope; boot
recovery scopes alone do not increment it.

Audit: T38, T42, T45, T46 have no restart/reconnect; T39 runs the fresh boot
worker; T41 runs the worker plus explicit queue flush; T44 runs the worker
before its negative assertions and later actual delivery. T43's attempt-committed
and receipt-before-save cuts also had stopped delivery workers after restart;
all eight cuts in both modes now finish with a fresh queue recovery cycle, stable
submitted bodies and attempt identities. Render-cut still invokes the real
stranded primitive first. Full-channel restart already runs its fresh worker.
Earlier T1..T36/T47..T62 witnesses cover stream/recovery/watchdog semantics;
reconnect methods assert markers/ingested event identities, not no-retyping,
and do not share this oracle.

Verification results are pending in the task report until CP-5/CP-6 finish.
All PC-84..100 variants and inherited controls remain pending SourceLanding
Mutation. The requested throw-away retyping mutant is also pending: the binding
Code-stage developer contract reserves deliberate mutants and red/restore/green
to Mutation. Ordinary green does not discharge that proof.
