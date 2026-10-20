# CARD-1307: cancellation and partial tool removal

2026-10-20. Scope: A1/A2 only, master-only. Source inspected:
`6ec71d427574f3a1b74bfbe2042e0ea9a819d191`. Card read through `card.ps1`;
investigation read in full from commit
`eb0dca02693cf58f54aa2118afee59c1160c5cf0`, path
`docs/investigations/2026-10-20-card-1302-1307-card-0987-advisories.md`
(fetch `feat/card-task-602939aa` if needed). No tests or builds run in Plan.
Verification design is included for the commissioned next stage, Code.

## Ground truth

| Card assumption | Current code | Consequence |
|---|---|---|
| A1: cancellation can strand a partial tool copy | `ToolCopyAuthority.Remove` verifies/unlinks files sequentially; its next read can fail after an unlink. `Check` still requires the original complete inventory. `ToolCopyCleanup.Remove` retries and then refuses. | Confirmed; retain refusal and explain the incomplete removal durably. A three-second read deadline can produce the same residue. |
| A2: no named cancellation result | `Program.Main` supplies a default runtime; its token is unset. `RunAsync` catches I/O errors, not OCE. Wait can throw OCE; `ReceiptFile` wraps it in IOException; Execute's broad catch emits exit 6. | Activate caller cancellation and classify at command/executor boundaries, including returned failures. |
| Wiring Main alone activates all work | `Program.Row` explicitly passes `CancellationToken.None` to slot probe/acquire and row execution; internal `hold` and smoke helpers do not observe Runtime. | Thread the row token; leave helper signal behavior alone. |
| A3: file-only delete uses global records | `CheckpointDelete.SafeWrite` is an instance constructed with its context. | No repair. |
| A4: named evidence base fails | CARD-1290 now diagnoses a non-ancestor base explicitly. | No repair. |
| Native mkdir duplication can be removed | `scripts/lib/checkpoint-owned-output.ps1` compiles `ExclusiveDirectory.cs` alone with Add-Type. | Preserve the BCL-only file and PowerShell caller unchanged. |

## Decisions

- **D-1 — Record an attempt, retain ambiguous residue.** For sealed tool copies,
  create `<run>/tool-removal-started.json` under the existing cleanup lock, after
  dead-owner and full authority admission and before calling `ApplyToolCopy`.
  Use the injected `CheckpointWrite` with ordinary ownership, never raw writes or
  replaceable authority. Include version, run ID, tool path and authority digest
  as diagnostics. A write/refusal failure permits zero tool deletions. Keep the
  marker permanently as evidence; successful removal is still determined by the
  existing delete result. Marker publication is a process-interruption record,
  not a power-loss durability guarantee.
- **D-2 — Marker presence never grants custody.** If the attempt has begun and
  removal cannot finish, stop retrying the original inventory. Return a non-clean
  `Refused` cleanup receipt with `tool-removal-incomplete`, original failure
  reason, tool/marker paths and manual-inspection guidance. A subsequent invocation
  with a remaining tool and marker returns the same conservative disposition,
  with zero tool unlinks; invalid/linked marker paths remain ordinary refusals.
  A marker may precede the first unlink, so say “may be partially removed,” never
  assert an exact deleted count. When the tool is absent, keep `AlreadyAbsent`.
  The old unsealed/current-process ledger branch stays unchanged.
- **D-3 — Manual recovery is deliberate.** Operator recovery establishes executor
  death and inspects containment, links and the remaining tool before removing
  that specific residue. Keep run receipts, ownership seal and marker. Re-running
  `wait --run <run>` after verified manual removal may observe absence;
  `clean` remains a manual-only refusal, as today.
  Deleting the marker, editing/resealing the inventory or importing it into the
  creation registry is not recovery. Reject rollback/atomic directory replacement
  and resumable-delete journals: both enlarge custody authority and cannot make
  sequential unlinks transactional. The card explicitly admits a clear marker.
- **D-4 — Caller cancellation is `ExitCodes.Canceled = 130`.** Use the familiar
  interrupt status without adding it to row-result precedence. At the command
  boundary, check the caller token before execution, on cancellation/I/O failure,
  and after a returned result; checking only OCE misses swallowed read failures.
  Emit `canceled: caller-requested`. A requested caller cancellation observed
  before command completion wins over generic invalid/crash classification.
  Without it, receipt deadlines/refusals remain 2, executor crashes 6, owner-ended
  7, row/total timeouts 5 and max-wait 75. Keep `ReceiptFile`'s existing bounded
  IOException contract and row-code aggregation unchanged. Reject matching
  exception text or mapping every OCE to 130: internal deadlines are different.
- **D-5 — Activate only the invoking CLI's cancellation.** For work verbs, Main owns a disposable
  CTS, subscribes to `Console.CancelKeyPress`, suppresses default termination and
  cancels that CTS; always unsubscribe in finally. Repeated interrupts remain
  idempotent. Keep internal `hold`, `smoke-write` and `smoke-detach` outside this
  handler so suppressing default termination cannot strand an uncancelable helper.
  Pass the token through `Runtime`, including Row's slot probe/acquire and driver.
  Canceling `run`/`wait` stops the
  observer, leaves the detached executor running, and retains the printed run ID
  for a later wait or explicit stop. A directly canceled executor joins its
  existing scheduler/driver cleanup and persists a non-green terminal 130 result.
  No new SIGTERM/SIGKILL, force-stop, session parking or automatic release policy.
  Existing bounded synchronous custody operations may delay cooperative exit.

These are implementation decisions within the brief, not pending operator choices.
No session waiting for input is added or changed (CARD-1083 does not add a deadline).

## Code slices

| Slice | Time | Files and change | Tests |
|---|---|---|---|
| S1 — A1 report/recovery | 45–60 min | `src/Antiphon.HostCleanup/Checkpoints/ToolCopyCleanup.cs`: marker and non-clean incomplete receipt; leave `ToolCopyAuthority` and `SafeDelete` custody checks intact. `docs/testing-and-build.md`: describe marker and manual recovery. | New `tests/Antiphon.Tests/Infrastructure/C1307ToolRemovalTests.cs`; reuse `HostCleanupToolFixture`/`VirtualCheckpointIo`. |
| S2 — A2 command result | 30–45 min | `tools/Antiphon.Checkpoints/Program.cs`, `Execution/ExitCodes.cs`: normalize the runtime once, check token at the boundary and map raw/wrapped/swallowed cancellation. `Commands/WaitCommand.cs`: after printing an incomplete cleanup receipt, honor cancellation before reading/printing an earlier green report. | New `tests/Antiphon.Tests/Infrastructure/C1307CancellationTests.cs`; reuse virtual receipt/tool fixtures and entered-stream barriers. |
| S3 — A2 activation/execute | 45–60 min | `tools/Antiphon.Checkpoints/Program.cs`, small `ConsoleCancellation.cs` adapter if needed, `CheckpointApp.cs`: register/unregister the console handler for work verbs, thread Row's three omitted tokens, classify executor cancellation before broad crash handling. Reuse terminal persistence with exit 130/reason `caller-requested`; check after scheduler joins and before terminal publication so swallowed scheduler cancellation cannot become timeout/owner-ended. Keep safe receipt writes and diagnostic persistence refusals; do not claim successful persistence on refusal. Update `docs/testing-and-build.md` with exit/observer semantics. | New `tests/Antiphon.Tests/Checkpoints/C1307CliCancellationTests.cs`; reuse `CheckpointTestBase`, `FakeDriver`, fixed slot client, and executor-log/ownership fixture patterns. |

S1 precedes S2; S2 precedes S3. Each slice commits/pushes its compiling witness
before the fix, runs its red row once, then commits/pushes the implementation and
runs its narrow green row. No production change to `ExclusiveDirectory.cs`, its
PowerShell caller, receipt validation, process ownership or scheduler kill logic.

## Verification design

### Inspection and proof boundaries

Read `ToolCopyCleanup`, `ToolCopyAuthority`, `SafeDelete`, `SafeWrite`, `ReceiptFile`,
`CheckpointFileReader`, `Program`, `CheckpointApp`, `WaitCommand`, `ExitCodes`, and
the scheduler cancellation branches. Read the existing authority/receipt tests,
virtual fixtures, `CheckpointAppTests`, `CheckpointExecutorLogTests`,
`CheckpointTaskOwnershipTests`, `WaitCommandTests` and standalone script witness.
New tests use real production entry points and observable I/O, not source-string
checks. Virtual I/O proves ordering/custody decisions, not native filesystem races.
An injected console-event subscription proves Main's composition and token flow,
not operating-system signal delivery. No new asynchronous message delivery path.

### Witnesses and narrow green rosters

- **V-1/R-1, S1:** `C1307ToolRemovalTests.Mid_removal_cancellation_leaves_marker`
  creates/seals two files. Cancel on the next file read only after the event log
  proves one actual tool unlink and that file is absent. Assert marker publication
  preceded the unlink, remaining bytes unchanged, non-clean incomplete receipt,
  and a fresh-context retry performs zero tool deletions with the named guidance.
  Baseline red is the missing marker (not a fixture error). Five green methods:
  this witness; `Read_deadline_leaves_marker` (advance fake time after entered
  second read); `Marker_failure_prevents_tool_unlink` (write failure/existing
  foreign marker); `Custody_refusal_preserves_sentinels` (changed/added/linked
  files, live/unknown owner); `Complete_removal_and_manual_recovery_are_absent`
  (normal success, marker retained, manually removed residue observed absent).
- **V-2/R-2, S2:** `C1307CancellationTests.Canceled_wait_returns_130` passes a
  canceled Runtime token to real `Program.RunAsync` with a valid virtual run.
  Capture exception/result and assert numeric 130, so the witness compiles before
  the constant exists; baseline throws OCE. Five green methods: this witness;
  `Entered_receipt_cancellation_returns_130` (status/report entered-read cases);
  `Partial_cleanup_prints_residue_before_canceled_exit` (real Wait, no green
  report after cancellation); `Deadline_without_caller_cancel_stays_invalid`;
  `Canceling_wait_leaves_executor_and_receipts_unchanged` (live executor,
  process-control spy, zero stop calls). Barriers prove cancellation location;
  every blocked stream is released and every task awaited in finally.
- **V-3/R-3, S3:** `C1307CliCancellationTests.Executor_cancel_persists_130` uses
  a real fixture run and entered FakeDriver, cancels the supplied executor token,
  and asserts return/state/report are 130 rather than 6, 5 or 7, with RED verdict,
  unchanged custody and completed driver/lease cleanup. Baseline red is that
  terminal classification. Five green methods: this witness;
  `Main_interrupt_reaches_command_and_unsubscribes` (inject only subscription,
  execute real RunAsync, assert token cancelability, event suppression, repeated
  signal, helper exclusion, and unsubscribe on success/error); `Cancel_before_request_read_is_not_crash`;
  `Direct_row_cancel_reaches_slot_and_driver` (isolated fake broker/driver,
  entered probe/acquire/driver barriers and released lease, no live broker);
  `No_caller_cancel_preserves_existing_results` (owner-ended, deadline, crash,
  max-wait controls, including unchanged state/report where applicable).

Guard checks are concentrated in these rosters: durable marker before mutation,
zero unlinks on marker/custody refusal, marker never becoming deletion authority,
caller-token-only classification, subscription lifetime, observer non-interference,
and executor cleanup before terminal completion. No blanket cancellation retry or
timeout increase. Assertions on the marker must count **tool** unlinks separately
from temporary marker-file cleanup. Standalone compilation is preserved by leaving
both consumer/source files untouched; its existing witness is not rerun here.

### Execution and cost

Read `GET /api/runner-defaults` and `GET /api/session-runners` again at dispatch.
Both were read in Plan: the configured default was unavailable, and eligible
Linux and Windows runners existed. Use live placement; omit `-Runner` and
`-Platform` (Any work). All rows use the **portable focused lane**; this names
placement/compatibility, not a request to run a whole test lane.

The table is a closed list: one expected red plus one narrow green per slice,
no full Unit/assembly/native lane runs. Select only that row with the checkpoint
tool (`run --plan <this-plan> --rows CP-n --expected-source-sha <committed-sha>`),
using the documented build-slot gate and isolated tool bootstrap; await completion
and continue `wait` on 75. A red row deliberately returns test failure; it is
accepted only with its one named assertion failure, never zero tests or build error.
Its green partner rebuilds at the fix SHA. Do not rerun red rows on repaired source.
Preserve exact CHECKPOINT lines, SHAs, counts and ignored receipts. Run the required
evidence-diff guard over recorded base..HEAD; remove owned alternate outputs safely.

Estimated verification floor: six isolated build/filter rows × 4 minutes = **24
minutes** (red 12, green 12), plus implementation above; estimates are not measured.
This commissioning includes red-first witnesses, not a post-land mutation campaign.
Native signal qualification and unrelated suites are excluded because no native
binding or process-kill implementation changes. Compare protected standalone files
to base in review. This avoids a roughly 25.5-minute assembly run per slice.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-red | `tests/Antiphon.Tests -> bin-c1307-s1-red/` | portable-removal-red | `/*/*/C1307ToolRemovalTests/Mid_removal_cancellation_leaves_marker*` | R-1 | 1 executed, 1 expected assertion failure, 0 skipped | 1 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1307-s1-green/` | portable-removal-green | `/*/*/C1307ToolRemovalTests/*` | V-1, R-1 | all 5 listed methods, 0 failed/skipped | 5 | 4 |
| CP-3 | S2-red | `tests/Antiphon.Tests -> bin-c1307-s2-red/` | portable-result-red | `/*/*/C1307CancellationTests/Canceled_wait_returns_130*` | R-2 | 1 executed, 1 expected assertion failure, 0 skipped | 1 | 4 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1307-s2-green/` | portable-result-green | `/*/*/C1307CancellationTests/*` | V-2, R-2 | all 5 listed methods, 0 failed/skipped | 5 | 4 |
| CP-5 | S3-red | `tests/Antiphon.Tests -> bin-c1307-s3-red/` | portable-activation-red | `/*/*/C1307CliCancellationTests/Executor_cancel_persists_130*` | R-3 | 1 executed, 1 expected assertion failure, 0 skipped | 1 | 4 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c1307-s3-green/` | portable-activation-green | `/*/*/C1307CliCancellationTests/*` | V-3, R-3 | all 5 listed methods, 0 failed/skipped | 5 | 4 |
