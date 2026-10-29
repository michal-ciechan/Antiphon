# CARD-1603: retire canceled queue selections and supersede replaced standing consumers

Plan round 2, task `c404bdc3`, revises round 1 (`2cebeca1`) in place after design
Review `b7762633`. Task base: `d553e70106c92d9e4e7b92df7d1c12d81e200c84`.
Product source inspected at
`f8bddb6efda7b0e8f50f3b45744087e52188bc66` on 2026-10-29.
No product code is changed by this Plan task. Verification is designed here for
execution after independent plan Review; this is not a claim of passing tests.
S3-A inspected separately at `6564c5757dd0ec7c347fe50275f1334498f9e073`;
its presence on a remote task ref is not a landed-prerequisite claim.

## Inputs and dependency

- CARD-1603 on the Antiphon board, read with `scripts/card.ps1 get`.
- CARD-1593 S3 plan, Risks 8 and 9:
  `docs/superpowers/plans/2026-10-28-card-1593-s3-atomic-consume-plan.md`
  on `origin/feat/card-task-f264f937`, full tip
  `733f413acc93b79b324ea38972a78aa33bccf117`.
- Its supporting investigation:
  `docs/investigations/2026-10-28-card-1593-capacity-leaks.md`
  on `origin/feat/card-task-bcb5bc45`.
- The S1 lock contract in
  `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md`
  on `origin/feat/card-task-d94003bc`.

Code is commissioned only after S3 round A lands on master. This base still uses
the pre-start `RedeemAsync` in the supervisor. A plan may be reviewed before that
dependency lands; it is not evidence that the dependency is already present.

## Ground truth

All paths below are relative to `server/Application/Services/` unless stated otherwise.

| Card assumption | Verified behavior at the inspected source |
|---|---|
| Expiring an optional brief cancels a capacity-selected row | **False.** ApplyCapacityHoldAsync selects only Channel/Scheduled (SessionMessageQueueService.cs:5534-5573); brief expiry requires ExecutionTaskId (:2206-2209, :2235-2236). Production execution briefs are Delegation (SessionMessageQueueService.DispatchBrief.cs:228-230; AgentTaskDispatcher.cs:5372-5380,5455-5461,8996-8998; AgentSessionService.cs:1132-1134). Delivery batches never mix origins (:2601-2625). StampQueueActionAsync has only test callers; it cannot establish this alleged production cause. |
| A canceled selected Channel/Scheduled message leaves a reusable grant | **True.** Human Drop CancelAsync (:1205-1208), CancelParkedIfStaleAsync (:1389-1392), and RemoteSpillCourier.CancelUndeliverableAsync / MarkUndeliverable (:253-282) can cancel such rows. Drop works before redemption or after a redeemed delivery attempt has reverted to Pending. None detaches the selected row from its wait. |
| The orphan sweep catches that cancellation | CapacityRecoveryService.cs:665-680 checks terminal/missing session and task owners only. It does not inspect SelectedMessageId or queued-message status. A live session with a canceled selected message passes through. |
| Expiry eventually exhausts the leak | Before redemption, D-12 calls RearmRevokedGrantOnAsync / PrepareNextAttemptOnAsync (:1203-1223,1370-1408): canceled rows are re-keyed without a status filter and count stays 0. After redemption, CapacityRecoveryPolicy.HasExecutionReceipt treats the selected row as a receipt; :1347-1355 requests free revalidation, count stays 1. The default 120 seconds bounds each grant, not repetition. A later successful report-boundary turn **can** end a LiveSession wait via ObserveTranscriptAsync (:879-905); the reproducer must not emit that progress. |
| The wait belongs only to its selected row | **False.** It serves the whole held session queue. While no grant is held, a new Pending Channel/Scheduled row can be Held and unselected (SessionMessageQueueService.cs:5544-5573). Canceling the wait strands that row and later held input. |
| Compatibility will recreate a finished wait for later input | **False.** ReconcileCompatibilityAsync (:988-1132) filters keys after its persisted cursor and keeps the cursor when empty; it does not revisit an already-scanned session. Keep the existing wait as the consumer. |
| A human start progresses a standing wait | `AgentControlService.cs:833-863` commits the accepted session pointer and resets human-start supervision state. The wait moves only in the capacity-specific branch at `:804-816`. Transcript progress looks up the wait by `SessionId` (`CapacityRecoveryService.cs:885-890`). |
| A running agent will eventually consume the old standing grant | `AgentSupervisorService.cs:175-207` handles the live session and returns before the capacity branch at `:213`. `OrphanedWaits` excludes `StandingStart` entirely. |
| Every standing wait includes the old session generation | False: `AgentSupervisorService.cs:393-404` creates model-hold waits with `AgentId` and `BlockedAt`, but no `SessionId` or `SessionStartedAt`. The design must handle that production shape, not just a fully stamped test row. |
| Canceling a capacity wait releases a process seat | False. These are provider-admission rows. Finishing one must not kill, park, or detach a session. |

## Decisions

**D-1 - Reconcile positive evidence of an obsolete consumer.** Add a bounded
`ReconcileObsoleteConsumersAsync` pass to `CapacityRecoveryService.ReconcileAsync`,
after compatibility reconciliation and the existing terminal-owner sweep, before
re-arm and grant selection. Keep the terminal-owner sweep's existing semantics.
Put the two new paths in `CapacityRecoveryService.ObsoleteConsumers.cs`; make the
existing class partial. No migration, new timer, expiry setting or API is needed.

This repairs already persisted waits as well as new ones. The hosted supervisor
already calls reconciliation before its start tick
(`server/Infrastructure/Supervision/AgentSupervisorHostedService.cs:132-148`).
Normal cadence is `SupervisionSettings.TickSeconds`, default 10 seconds, plus
bounded-batch/lock deferral; this is not a hard wall-clock SLA when the host or
database is unavailable. With recovery disabled the pass, like granting, is off.

Rejected: only adding callbacks to human Start and queue cancellation. That misses
existing bad rows, other queue cancellation writers, and a crash after cancellation
commits but before the callback. Rejected: cancel on age, runner absence or an
agent's `Running` flag. Those are not proof that this wait has lost its consumer.

**D-2 - Retire the stale selection, keep the session's wait.** A candidate must
be unfinished, be LiveSession or PendingQueue, have SelectedMessageId resolving
to an actual Canceled Channel/Scheduled row, and have matching message
CapacityWaitId, CapacityRecoveryActionKey and session identity. Missing rows,
uncommitted expiry, Pending and Sent are not evidence. Keep a wait with a
launch/dispatch receipt (LaunchSessionId, LaunchReceipt, DispatchAttemptId) or
confirmed prompt: it still represents admitted execution. Also keep a linked
Queued task or Working task with CapacityWaitRetained that can independently
redeem. The terminal-task sweep still owns ended tasks. No QueuedTask or
RoutingBlockedTask wait is changed by this queue cleanup.

Under D-4's gate/transaction, conditionally detach only the canceled message's
CapacityWaitId, CapacityWaitVersion and CapacityRecoveryActionKey, then clear the
wait's SelectedMessageId and NeedsRevalidationGrant. Retire its action: increment
ActionOrdinal, derive a new ActionKey, increment Version exactly once, and stamp
UpdatedAt / OutcomeReason = selected-message-canceled. For ActionPending,
Admitted or Ready, use State/Outcome = Ready and a due time no earlier than the
old due time or normal jitter eligibility for this reset. Deferred with reason
grant-revoked may return to Ready; preserve other deferrals and WaitingForHold /
Reheld so cleanup cannot bypass an unrelated prerequisite. No new state/migration.
Do **not** call PrepareNextAttemptOnAsync here: at this base it would rewrite
queue rows under the provider lock. Only the exact canceled row is detached.

Clear the provider grant only if it still names this wait and old action. Keep
AdmissionCount, provider LastActionKey/LastActionAt, NextAdmissionAt, wave fields,
and the canceled row's delivery/cancellation facts. A spent admission is not
refunded. Log old wait/action/message IDs and disposition without body text.
Normal attempt caps still apply when new work is admitted; an already exhausted
episode is outside the new delivery guarantee. No admission is spent merely to
retire a selection.

Add one shared **queue-demand predicate** to GrantReadyAsync, both its candidate
query **before Take(batch)** and outstanding-grant validation. A LiveSession or
PendingQueue wait without an independent execution receipt/task can receive or
retain a grant only if its session has a Pending Channel/Scheduled row eligible
for that wait (unkeyed, or linked to this wait/current action). Do not use the
display-only NoteHeader == Held. Existing selected Pending/Sent/unknown receipt
shapes, launch/dispatch receipts and independently redeemable tasks keep their
semantics. Resolve a legacy missing SessionId through the exact session consumer
key; unresolved identity stays conservative. Use SQL existence reads, never a
queue semaphore under the provider lock. Recheck the winner before writing;
a later cancellation is handled next tick. An empty wait remains Ready but is
skipped without repeated version/ordinal changes. This also covers direct
GrantReadyAsync callers, not only ReconcileAsync.

An unselected held row Y already present when X is canceled remains eligible;
a later held row wakes the **same** wait on the ordinary grant tick. No enqueue
registration or compatibility-cursor rewind is required. While empty, another
waiter gets the provider once its unchanged clock permits. If selection moved
to Y before cleanup rechecks, preserve Y. Delivery still obeys normal dialog,
composer and transcript guards: cancellation does not prove an attempted body
absent from the composer. Keep genuine post-Sent retries and retained returns.

Rejected: ending the wait (drops existing/later held work); finishing plus a new
enqueue-time registration path (larger lifecycle change); !Pending (Sent is valid);
resetting the episode count; age-based cleanup; and retaining the canceled
selection as a receipt (perpetuates free revalidation).

**D-3 - Supersede a standing wait when a different accepted generation replaced
its consumer.** Restrict this path to unfinished `StandingStart` waits with no
execution receipt. Resolve the standing agent by `AgentId`, or the exact
`agent:<N-format-id>` consumer key when the ID is absent. Do not classify a missing
agent, malformed/missing pointer, or ambiguous ownership as replacement.

Lock and reread the pointer and current session as D-4 specifies. Use
`StandingSessionOwnership.ResolveAsync` before taking the provider lock to prove
the current cardless session belongs to this non-pool standing agent. This uses
the existing stamped/unique-legacy ownership rules, not a name or working path.
After the provider lock, reload the wait; it may have been consumed while this
pass waited. Apply this table to the persisted data:

| Wait evidence | Replacement evidence required |
|---|---|
| Both `SessionId` and `SessionStartedAt` present | The accepted current `(Id, StartedAt)` differs, and current `StartedAt > wait.BlockedAt`. The same ID with a later generation counts. An exact tuple match never counts, even while Running. |
| Either generation field absent (the production model-hold shape) | Current `StartedAt > wait.BlockedAt`, and the session's `CapacityRecoveryActionKey` is not any action of this wait (`<wait-id:N>:` prefix). An old accepted action from this episode is conservative evidence to retain the wait, even after re-arm changed its ordinal. |

`StartedAt` changes inside the accepted reservation on resume
(`AgentControlService.cs:652-687`); a preflight that merely tried to start changes
nothing. S3 round A stamps both wait generation fields in that same reservation
for capacity starts, so a legitimately admitted replacement is retained. Do not
require the new session still to be Running: a committed human start that quickly
fails still replaced the old consumer. Current identity/generation, not activity
or a guessed caller, is the evidence; a committed external replacement has the
same effect as a human replacement.

Set `State/Outcome = Superseded`, `OutcomeReason = standing-consumer-replaced`,
and increment Version once, preserve action/receipt history and admission count,
clear NeedsRevalidationGrant, and clear only its own grant without changing the
provider clock/wave. Do not apply queue action rotation to standing waits. Do not stamp
`Progressed`, change the agent's pointer, launch again, or stop either session.
An uncommitted start/rollback and the original stopped generation keep their wait.

Rejected: supersede every standing wait with a live agent (that would discard
S3's valid admitted starts and D8 re-arm); compare session IDs only (misses human
resume); require a wait session ID (misses production model-hold registrations);
or add a new human-start column/migration. For a legacy incomplete wait whose
human resume inherited an old capacity action key, the evidence is ambiguous:
retain it and report any such row found during activation for follow-up rather
than guessing. Do not scan all ambiguous rows on every tick just to log them.
Fresh human starts and initial unspent model-hold resumes do not
have that ambiguity. S3-stamped waits compare the full generation and do not rely
on the inherited key.

**D-4 - Serialize with the consumer before the provider; never wait indefinitely
in the maintenance pass.** Discover at most `ReconciliationBatchSize` candidates
per path, ordered by `BlockedAt`, then `Id`. Discovery applies the positive
predicate, rather than taking the first batch of all standing/live waits. Every
candidate is revalidated; one stale/busy candidate does not end the loop. Record
scanned/detached/superseded/deferred counts and elapsed time, with no message bodies.

| Path | Lock and transaction order | Reason |
|---|---|---|
| Canceled selection | Lazily resolve the existing singleton queue; try GetLock(message.AgentSessionId).WaitAsync(0, ct). Open a fresh transaction only after the gate. Lock the exact message row FOR UPDATE NOWAIT, recheck Canceled/origin/session/wait/key and conditionally clear its linkage **before** trying provider advisory (7). Reload the wait, recheck selection/state/version/key/independent consumer, CAS capacity rows (8), commit. Any failed check or busy provider rolls back the message detach. Release gate in finally. | Same semaphore as selection/cancellation (SessionMessageQueueService.cs:57,6047), without a new dictionary or constructor cycle. Prelocking the message and writing its linkage before rank 7 avoids reversing a queue-row -> provider order if S3-B is present; all post-provider writes remain capacity-only. No queue is resolved with zero queue candidates. |
| Standing replacement | Fresh transaction; current session `FOR UPDATE NOWAIT` (4), then agent `FOR UPDATE NOWAIT` (5). Reload pointer; if it differs from discovery, abandon this candidate. Prove ownership. Try provider advisory (7), reload wait, conditionally finish capacity rows (8), commit. | Same session-before-agent order as `AgentControlService.cs:543-549`, same connection throughout. S3 capacity start commits pointer and wait together. This pass writes no session or agent row. |

Use `pg_try_advisory_xact_lock` for the new pass's provider acquisition; false is
a deferred candidate, not cancellation. A row `NOWAIT` 55P03 rolls back that
candidate. Do not retry in the same pass. Existing grant/consume acquisition and
timeouts are unchanged. No queue lock is acquired while holding a database lock.
Cancellation disposes the transaction and releases the semaphore.

Update using a conditional write over the just-read unfinished state, `Version`,
`ActionKey` and selection identity. `Version` is not an EF concurrency token
(`server/Infrastructure/Data/AppDbContext.cs:2903-2924`), so a tracked save alone
is not the compare-and-set. Zero affected rows means skip; never clear a grant
after losing that compare-and-set. Message detach, selection reset and its grant
clearance commit together; standing supersede and its grant clearance commit
together. The message write includes Status == Canceled and exact linkage; its
row lock stays held to commit. DeferResumeAbsenceAsync currently can reset rows
to Pending without a status predicate (SessionMessageQueueService.ResumeReceipt.cs:122-127).
If such a writer won first, cleanup skips; a later Pending write is fresh demand,
not authority to restore a canceled action. Do not rely solely on the local
semaphore as proof against that writer. One fresh context per candidate avoids stale tracked entities after
rollback. S1 Rule P is preserved: all writes after provider acquisition are rank 8.

The batch limits are explicit: at most B queue candidates plus B standing
candidates per reconciliation, in addition to the existing B terminal-owner
candidates. No unbounded drain loop. Busy candidates can defer across ticks;
positive cancellation/replacement is acted on when its locks are available.

**D-5 - Both branches eventually; master first.** This is a production capacity
leak fix, so it must reach master and the active rc before being credited as
active in the product. Implement on master after S3 A and an approving plan
Review. Port only reviewed product/test commits to the then-current rc, after
confirming S3 A is present there (or commission its reviewed prerequisite port).
The rc port needs its own verification and Review. No rc rebase, tags, publishing
or deployment is authorized by this Plan task. The orchestrator schedules the
port and activation; do not hard-code an rc name or infer activation from a land.

S3 B/C are not prerequisites: this cleanup works for a queue admission spent at
either the old pre-delivery point or S3's Sent boundary. If B/C is already on the
Code base, retain its tests and recount the affected regression roster. Avoid
overlapping Code edits to `CapacityRecoveryService` with S3 rounds.

**D-6 - Plan review first.** The immediate next stage is Review by a different
company's model, as commissioned. After approval, the verification section and
checkpoint table make this plan executable by Code without another design stage.
These D-n choices are the plan's stated defaults, not additional permission asks.

## Slices

Commit and push both slices' test witnesses (including V-11/V-12) before either production fix, then
commit/push S1 and S2 separately. The two product slices form one final checkpoint
group, so their final green verification shares one build. Tests call existing
production entry points at the red commit; an absent new helper or build failure
is never the RED witness.

| Slice | Files | Change and tests |
|---|---|---|
| S1 | `server/Application/Services/CapacityRecoveryService.cs`; new `server/Application/Services/CapacityRecoveryService.ObsoleteConsumers.cs`; new `tests/Antiphon.Tests/Application/CapacityObsoleteQueueTests.cs` | D-1/D-2/D-4 queue path, queue-demand predicate, conditional reset helper, integration/selection-race/rollback guards V-1 through V-5 and V-11/V-12. Existing orphan and live-grant expiry suites stay unchanged. |
| S2 | Same production files; new `tests/Antiphon.Tests/Application/CapacityObsoleteStandingTests.cs` | D-3/D-4 standing path; real human Start, null generation, legitimate admitted start, replaced generation and row-lock/refusal guards V-6 through V-10. No change to AgentControlService, queue cancellation semantics or live process ownership. |

Test support stays local to these two classes unless an existing helper needs a
small optional interceptor argument. Use `CapacityRecoveryTestSupport`,
`ClaudeDialogFixture`/`BridgeQueueHarness`, `AgentSupervisionTests.BuildHarness`
and `CapacityLockProbe`; do not add another test host implementation.

## Performance budget

Apply [the plan/review requirements](../../testing-and-build.md#performance-standards)
and [R1-R9](../../test-performance-standards.md). This Plan task runs no tests or
builds. All following estimates are for Code, not measurements made by this task.

**Measurement provenance.** The S3 plan at `733f413acc93b79b324ea38972a78aa33bccf117`
reports S1 Linux capacity methods at 1.2-11.4 seconds, first-test shared DB startup
about 10-11 seconds, and leased test builds at 217-399 seconds (220-520 including
slot wait). It supplies no comparable sample count, load or raw per-row p95 for
these new selections. Therefore **measured p95 is unavailable** for CP-1..CP-5;
none of the estimates below is labeled measured. CARD-1603 owns collection in
the first Code round; CARD-1404/1563 own broader budget tooling. Preserve these
historical ranges rather than treating them as evidence for a new timeout.

**Setup and category.** These are `Antiphon.Tests`, namespace
`Antiphon.Tests.Application`, `[Category("Integration")]`, server/capacity and
in-process queue-delivery batches. Both new classes are `[NotInParallel]`.
Use the assembly's shared Postgres/template once per host; clone the template
per test with `TestDbFixture.CreateIsolatedSchemaAsync`. No container/migration
per class or per test. Build each per-test fake service harness once; this is
cheap DI/runtime state, not an AppHost/web host. Queue cases use the real queue
with a fake adapter; standing cases use `AgentSupervisionTests.BuildHarness`.
No provider CLI, production runner, real broker, browser or native Pty process.
Advance `FakeTimeProvider` across 121-second expiry windows; no real-time sleeps.

The direct production subject is `server/Antiphon.Server.csproj`, compiled by
`tests/Antiphon.Tests/Antiphon.Tests.csproj`'s direct project reference. Capture
the resolved ProjectReference graph and downstream test membership with the
run. Select the affected server reconciliation, queue and supervision tests
below; no runner protocol, messaging transport, client or git implementation
changes. Ordinary scoped evidence grants no scoped rc qualification credit:
that remains CARD-1545/release policy. Component extraction adds no value to
this small service-only change (R2 considered).

| Phase/selection | Provisional expected duration | Initial bounded allowance |
|---|---|---|
| Tool bootstrap, if its built executable is unavailable | 1-2 minutes, separately accounted | 4 minutes execution through the build slot |
| One isolated Antiphon.Tests build | 4-7 minutes | 15 minutes: existing tool floor, about twice the reported upper range; provisional CARD-1603 exception described below |
| Slot admission | Unknown; load dependent, outside execution | `build.slotWaitMinutes = 10`; report busy/HTTP time separately; exit 4 is not a test failure or permission to run unleased |
| Postgres start/readiness + template/migrations | About 11 seconds once per test host from the S3 report | 30 seconds target; if existing shared-fixture guards are larger, record that inherited exception and actual phase under CARD-1603; do not silently change the shared fixture |
| Clone + seed | 1-3 seconds per test, provisional | 10-second operation target; retain actual DB spans and inherited Npgsql command deadline |
| New simple queue/DB test incl. clone and teardown | 3-15 seconds | 30 seconds per test |
| New real-start or concurrent-boundary test incl. clone and teardown | 10-30 seconds | 60 seconds per test; retain the existing 10-second launch-queue/barrier waits rather than increasing them |
| Teardown, fake adapter/runtime and isolated DB | 1-5 seconds per test | 10-second join/cleanup target; failure leaves diagnostic evidence |
| CP-1 test execution (27 new queue results) | About 2 minutes | 4 minutes, excluding its build |
| CP-2 test execution (18 new standing results) | About 3 minutes | 6 minutes |
| CP-3 test execution (32 existing results) | About 5 minutes | 10 minutes |
| CP-4 test execution (20 S3-A supervision results) | About 3 minutes | 6 minutes |
| CP-5 test execution (3 standing capacity results) | About 1 minute | 2 minutes |

Final green selection: about **21 minutes execution including one 7-minute
build**, excluding admission and optional bootstrap. Set a 29-minute charged
execution cap and 39-minute independent wall cap (29 + one 10-minute admission
allowance). At 30 minutes expected or observed for any selection/batch/run,
preserve timings and commission Debug; do not repeat or raise a timeout. A busy
slot may end the bounded run without any failed test. Importer defaults
`max(15, 3 * estimate)` are not this budget: import the plan to ignored YAML and
pin each checkpoint's `timeoutMinutes` to 4, 6, 10, 6, 2 respectively, with
`build.slotWaitMinutes: 10` and `timeouts.rowMinutes: 15`. Rows' timeout pins cover
test execution. The tool derives the build deadline as the largest row/default
with a 15-minute floor (`RunScheduler.BuildDeadlineMinutes`, CARD-1356); there is
no `build.timeoutMinutes` field. Accept that 15-minute build-only exception under
CARD-1603 pending measured timings; do not use `--row-timeout` to widen every test
row. Pass `--total-timeout 29m --hard-wall-timeout 39m`. No schema changes.

Bounded sample collection: reuse the required two RED methods, one final green
checkpoint run, and later method-scoped positive controls. Retain per-test and
per-row durations and sample counts; compute observed p95 for each comparable
selection using the recorded platform, source and load. Small samples stay
provisional. Set subsequent allowances near rounded-up 2x observed p95, with a
named CARD-1603 exception if inherited harness overhead requires more. Do not
repeat the suite merely to manufacture a statistical sample.

Keep ignored artifacts under `.antiphon/checkpoints/<run-id>/` and per-test timing
files referenced by its report. Record exact executed membership/category, SHA,
platform/host/load, slot/admission, build, discovery/host startup, shared DB
readiness, template/migrations, clone/seed, body and teardown. No AppHost/web-host
phase applies; mark that as not applicable, and missing instrumentation as
unknown, not zero. Record DB calls and test-owned filesystem/cleanup operations
separately from wall time; fake runner calls are in-process. External HTTP/CLI
belongs only to build/slot tooling in these tests. Use the existing opt-in
`TestTiming` facilities and TRX. Set `ANTIPHON_TEST_PHASE_ROOT`,
`ANTIPHON_TEST_PHASE_RUN_ID`, `ANTIPHON_TEST_PHASE_GROUP_ID`,
`ANTIPHON_TEST_PHASE_ATTEMPT` and `ANTIPHON_TEST_PHASE_SUITE` in the imported
manifest's row-only environments (unique run/group, attempt 1, suite
Antiphon.Tests); do not inject them into build/discovery children. Retain
unattributed remainder, exceptions and timeouts. Do not report unavailable
per-phase instrumentation as complete.

## Verification design

RED means an executed outcome assertion against the pre-fix production behavior,
not a build error, missing fixture, or zero discovery. The new test classes and
seams compile on the S3-A Code base. Tests call `ReconcileAsync`, real queue
`FlushIfIdleAsync`, and real `AgentControlService.StartAsync`; none merely invokes
a new helper whose stub could return success.

Use the existing optional `LandDeliveryBoundary` for two named deterministic
points in the new pass: `capacity-obsolete-after-discovery` (outside all locks)
and `capacity-obsolete-before-finish` (after revalidation, before conditional
update). Supply wait ID and selected-message ID, or `Guid.Empty`; no payload.
The pre-fix service simply never calls those points, so the two mandatory RED
tests do not depend on them. All callbacks/blocked transactions are joined and
released in `finally`; no sleeps or orphaned test work.

### New witnesses and guards

All queue methods below belong to new `CapacityObsoleteQueueTests`; all standing
methods to new `CapacityObsoleteStandingTests`. Each argument produces one TUnit
result; loops/extra assertions do not inflate the checkpoint floor.

| ID / method | Arguments / results | Setup, oracle and why a stub fails |
|---|---|---|
| V-1 C1603_canceled_selected_message_stops_regranting | human-drop-before-redeem, human-drop-after-redeem, parked-stale, spill-undeliverable / **4** | Real queue, live session, terminal wall, Channel/Scheduled row with no ExecutionTaskId. Obtain selection through ApplyCapacityHoldAsync reached by FlushIfIdleAsync, never StampQueueActionAsync. Before-redeem arm uses a one-shot SaveChanges interceptor that stops the flush **after the committed selection save** (a crash/cancellation boundary), joins the flush/releases its gate, then calls real CancelAsync; assert count 0. After-redeem arm lets the production failed-delivery path revert to Pending before Drop; assert count 1. Parked arm drives failed attempts to the existing cap then calls CancelParkedIfStaleAsync; spill arm uses a selected owned pointer with missing durable/staged bytes and real FlushIfIdleAsync -> CancelUndeliverableAsync, not a direct MarkUndeliverable call. Prove each production cancellation, actual original selection/key and unchanged live session. Do not emit a successful report-boundary turn (disable the fake adapter's automatic TurnEnd); no successful submission is claimed. Reconcile; assert canceled row detached, SelectedMessageId null, unfinished Ready wait/new action, preserved count/clock/wave, no revalidation, and another eligible waiter advancing when due. Advance three 121-second windows with empty queue: old action is never granted again, same wait remains usable, no repeated reset. **RED:** current code keeps the canceled selection and cycles its action; no-op cleanup fails exact detach/provider assertions. |
| V-2 C1603_queue_cleanup_preserves_real_consumers | pending, sent, missing-selected-row, unselected-canceled-row, retained-task, queued-task, launch-receipt, confirmed-prompt / **8** | Seed a matching held grant and the named legitimate/unknown consumer state; clocks are not due/expired. Reconcile: keep state/count/key/selection and ownership. Retained-task is Working + retained; queued-task is independently redeemable; launch/confirmed-prompt represent active work. PC: remove the Canceled/independent-receipt predicates; real consumer data is detached and the method fails. Missing selected IDs used by grant-liveness tests stay conservative. |
| V-3 C1603_queue_cleanup_rechecks_selection_and_grant | replacement-selection, other-grant, already-progressed, resurrected-pending, message-busy / **5** | At discovery, replace selection with Y under the real gate, move ownership to another wait, finish Progressed, or change X back to Pending from a separate context. Reread must retain Y/Progressed/Pending; other-grant permits detach but never clears that grant. Busy arm holds X's row from another connection: NOWAIT defers promptly and a later call after release succeeds. Assert exact row linkage, selected ID, state, version and owner. PC: remove reload/CAS or the conditional Canceled message write; replacement/progress/resurrection arms fail. PC blocking row acquisition fails busy deadline. |
| V-4 C1603_queue_cleanup_rolls_back_and_releases_gate | none / **1** | Inject a transaction-commit failure after both tentative message detach and wait reset. A separate context must see original selection/message key/wait/grant and unchanged count. Remove fault; another call detaches exactly once and the real gate can be acquired. Record queue-gate hold duration. No-op fails the retry; dropping the transaction fails persisted rollback. |
| V-5 C1603_queue_cleanup_is_bounded_and_idempotent | none / **1** | Batch 2, three canceled selections plus live controls. First/second/third calls detach 2/1/0 selections, leaving all three waits unfinished. Each reset increments version/ordinal once; no repeated empty grant or re-key. Preserve future NextAdmissionAt and unrelated grant. Removing Take(batch) detaches three first time; stub detaches zero. |
| V-6 `C1603_human_start_supersedes_standing_wait` | `fresh-null-generation`, `resume-null-generation`, `resume-stamped-generation`, `fresh-consumer-key-fallback` / **4** | Real Start via existing fake-adapter supervision harness, a granted standing wait, and a second eligible waiter. First/fourth start Fresh; second/third resume the existing failed row. Null cases reproduce `RecordStartFailureAsync`'s actual registration; fallback omits AgentId. Stamped case has one prior admission and a session carrying the old capacity key, to prove full-generation comparison wins. Observe committed new pointer/StartedAt and exactly one human launch. Reconcile; expect Superseded/reason, unchanged admission count, provider released when due, no extra launch/kill and no repeat grant after three fake expiry windows. **RED before fix:** the old wait remains unfinished. No-op/stub fails the terminal-state/provider assertions. |
| V-7 `C1603_standing_cleanup_keeps_current_or_unproven_consumer` | `same-running-generation`, `original-stopped-generation`, `capacity-fresh`, `capacity-resume`, `legacy-episode-key`, `missing-pointer`, `conflicting-owner` / **7** | Preserve exact wait and grant accounting. Capacity variants actually run S3-A supervisor starts; assert one admission, current wait tuple, and successful ordinary D8-style re-arm after a failed start. Original stopped is still the restart consumer. Legacy key uses an older action ordinal of the same wait, not just current ActionKey. Missing/conflicting identity is unknown, never replacement. Guard PC: replace the generation/replacement predicate with agent-exists; current/stopped cases fail. |
| V-8 `C1603_standing_cleanup_serializes_with_acceptance` | `capacity-commit`, `human-rollback`, `session-busy`, `agent-busy` / **4** | Use a second connection and existing start reservation interceptors. Capacity commit completes before locked recheck: updated wait tuple is retained. Human rollback leaves old tuple/grant; no supersede. Hold the session or agent row: sweep defers promptly with no provider lock and no mutation, then a subsequent pass after release ends a genuinely replaced wait. A DbCommandInterceptor records zero provider-acquisition commands on busy-row calls, and successful session/agent lock acquisition preceding that command otherwise; post-rollback pg_locks alone cannot prove the order. Assert DB outcomes and completed/joined calls. PC moving provider acquisition ahead of row acquisition fails the command-order assertion; PC dropping the pointer/tuple reread fails the capacity-commit case. |
| V-9 `C1603_standing_cleanup_is_bounded_and_provider_busy_safe` | `free`, `provider-busy` / **2** | Batch 2 with three positively replaced standing consumers; finish 2/1/0, one version bump each, all admission counts unchanged. Busy arm holds the provider on a second connection: pass returns promptly with no changes; after release it obeys the same batch bound. Stub finishes zero; blocking provider acquisition fails the prompt-return assertion. |
| V-10 `C1603_obsolete_cleanup_loses_compare_and_set_safely` | none / **1** | At before-finish boundary, a separate context advances the wait to Progressed/version+1 while leaving the grant for its normal reconciler. Finish must affect zero rows and leave that grant intact; inspect before the later ordinary grant pass, using an interceptor or boundary snapshot, then assert normal reconciliation revokes it. No dirty tracker replays the old state. PC removing the state/version CAS overwrites Progressed and fails. |
| V-11 C1603_cleanup_keeps_held_input_deliverable | unselected-held-pending-channel, unselected-held-pending-scheduled, later-held-input-after-cleanup-channel, later-held-input-after-cleanup-scheduled / **4** | Start from V-1's real selected X/Drop with admission below cap. Advance compatibility cursor beyond this session. Existing-input arms put the wait in ungranted Ready and enqueue Y via the real queue before cleanup; prove Y Pending/Held/unkeyed. Later-input arms first clean X with no other rows, reconcile over three expiry windows (no grant), then enqueue Y. Grant when due, flush through real selection and fake adapter correlated UserPrompt. Assert SAME wait ID, new action selected on Y, actual delivered body/receipt and no manual prompt/compatibility rewind; X remains Canceled. PC restoring round-1 wait cancellation strands Y; PC unconditional empty-demand suppression strands later Y. These guards pass at base and must not be mislabeled RED witnesses. |
| V-12 C1603_empty_queue_demand_does_not_reserve_provider | never-selected-empty, detached-empty, independent-task, attempt-cap / **4** | Exercise direct GrantReadyAsync plus ReconcileAsync. Put more than B real live empty queue waits before an eligible competitor; competitor must grant, proving demand exclusion precedes Take. Empty waits keep their state/key/version after cleanup. Independently redeemable task still grants with no queue input. At-cap cleaned wait retains count, never grants free revalidation; new demand reaches the existing Exhausted rule. PC removing demand filter/putting it after Take monopolizes/starves the competitor; blanket no-queue filtering fails independent-task. |

Totals: **27 queue + 18 standing = 45 new results**. V-2/V-3/V-7/V-8/V-10/V-11/V-12 are
guards with the named method-scoped positive controls; V-4/V-5/V-9 also have
the named fault/batch/lock controls. Mutation is a separate commissioned stage:
one precise `/*/*/ClassName/ExactTestMethod` per control, record the assertion
failure, restore, and rerun that same method green. No class-wide mutation cycles.

### RED execution before product changes

One leased isolated build of the committed test-only source, reused for exactly:

1. `/*/Antiphon.Tests.Application/CapacityObsoleteQueueTests/C1603_canceled_selected_message_stops_regranting` — 4 executed, 4 expected selection-detach failures.
2. `/*/Antiphon.Tests.Application/CapacityObsoleteStandingTests/C1603_human_start_supersedes_standing_wait` — 4 executed, 4 expected terminal-state failures.

These two diagnostic runs and their build are authorized additions to the final
green table, reported with their own SHA, counts and failed assertions. Run TUnit
through the build-slot wrapper using the isolated output and `--no-build` on the
second driver; never `dotnet test`. Use separate ignored RED evidence and do not
overwrite it with green receipts. If S3-A fails one of its pre-existing assertions,
confirm only that failing method at the pinned Code base before calling it inherited.

### Checkpoints

**Lane: Linux, PostgreSQL Integration, fake in-process adapters.** All rows share
the `S1-S2` committed group and a single isolated build. They are the closed final
green list; no full Unit/assembly run or native Windows tests. The expected S3-A
base has 20 `CapacityRecoverySupervisionTests` results (6 existing + 14 specified
in S3 A). Verify the landed prerequisite and recount before Code: additional
landed S3 tests must be named and included, never hidden by lowering a floor.

Use the checkpoint tool's plan workflow: `import --plan <this-file> --out
<ignored-manifest.yaml>`, then apply only the budget/timing pins above and execute
`run <ignored-manifest.yaml> --expected-source-sha <full-tip> --total-timeout 29m
--hard-wall-timeout 39m`. YAML is a positional argument; `--plan` parses Markdown.
Preserve the imported membership exactly. One run, then `wait --max-wait 570s`
until exit is not 75; remain responsible for joining it. Bootstrap any unavailable
tool binary through `scripts/build-slot.ps1` and account for that separately.
Use the built tool for subsequent control commands so no unlisted compilation
occurs. Checkpoint builds take their own slots. No reader-refusal fallback or
unleased driver is authorized here; stop with the evidence if tooling refuses.
Retain every unedited CHECKPOINT line, full tested SHA, roster, TRX/hash and
source/build provenance. Validate receipts with `scripts/validate-checkpoint-receipt.ps1`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1603/` | c1603-queue | `/*/Antiphon.Tests.Application/CapacityObsoleteQueueTests/*` | V-1..V-5, V-11/V-12 | all 27 new results, 0 failed/skipped | 27 | 9 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S1-S2 | CP-1 | c1603-standing | `/*/Antiphon.Tests.Application/CapacityObsoleteStandingTests/*` | V-6..V-10 | all 18 new results, 0 failed/skipped | 18 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S1-S2 | CP-1 | c1603-existing-capacity | `/*/Antiphon.Tests.Application/(CapacityWaitOrphanSweepTests*)\|(CapacityRecoveryGrantLivenessTests*)\|(CapacityRecoveryQueueTests*)\|(CapacityRecoveryTaskTests*)/*` | terminal owners, D-12 fairness, genuine queue delivery, retained consumers | 6 + 12 + 8 + 6 = 32 results, each class present, 0 failed/skipped | 32 | 5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S1-S2 | CP-1 | c1603-s3-supervision | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` | S3-A atomic consumption/exit guards, D8, V16-V18 | all 20 S3-A results, 0 failed/skipped | 20 | 3 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-5 | S1-S2 | CP-1 | c1603-standing-reservation | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/(C510_S2_capacity_grant_is_required_and_follows_fresh*)\|(C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick*)` | existing automatic Fresh/resume after a model hold | 2 + 1 = 3 results, both methods present, 0 failed/skipped | 3 | 1 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Suffix audit at the inspected source: CP-3's four class prefixes each resolve to
one distinct class; CP-5's two method prefixes each resolve to one method (Fresh
has ClaudeCode/Grok arguments). There are no suffix collisions. The new class
names do not yet exist and are reserved by this plan; recheck after implementation
and after moving to the landed S3 base. Every OR operand is parenthesized with
its own trailing `*`, and table pipes are escaped. Confirm actual fresh TRX names;
`--list-tests` is not scoped-execution evidence.

## Placement

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-29.
Defaults revision 2 has no per-kind override; an eligible Linux lane is available.
Use current defaults rather than embedding a fleet location; omit `-Runner` and
`-Platform` for portable work. Checkpoints will name the Linux integration lane.
No desktop files or native Windows runs are needed for this plan.

## Risks and limits

1. **S3-A is a real prerequisite.** Running the standing predicate against a
   pre-S3 supervisor can misclassify its separately committed start. Code must
   confirm atomic consume plus wait generation stamps on both resume and fresh
   arms, and keep CP-4/CP-5 green. This plan does not amend S3's card-spawn
   post-commit consume window; card-bound sessions are excluded by ownership.
2. **Unknown is retained.** A missing selected message is not a canceled message;
   a missing/ambiguous standing owner is not an accepted replacement. The legacy
   incomplete-generation/inherited-key case in D-3 remains diagnostic debt.
   Do not turn cleanup into a general age-based orphan reaper.
3. **Queue reselection races.** The queue gate is required, with fresh reads
   after acquisition. A lazy resolve of the actual singleton must not create a
   new semaphore dictionary or DI cycle. An integration test must use production
   registration; a fake private gate cannot establish this guarantee.
4. **Owner sweeps compete with valid capacity work.** Never clear another wait's
   provider grant, reset the provider clock, spend/refund an admission or cancel
   a retained task's independently redeemable wait. The compare-and-set plus
   the guard roster covers these boundaries. No claim/dispatcher method changes.
5. **Busy/disabled/offline hosts delay cleanup.** The pass defers busy locks and
   is bounded; it provides no hard completion deadline during outage, continuous
   contention or an arrival rate above its batch capacity. D-12 still bounds
   individual stale grants while reconciliation runs. Measure per-pass duration,
   candidates, deferrals and oldest eligible age during activation.
6. **Compatibility is not a re-registration service.** Its cursor only moves forward.
   Queue cleanup therefore leaves the same wait unfinished, excludes empty demand
   before the grant candidate batch limit, and tests existing unselected and later
   input with the cursor already beyond this session. No historical wall alone
   should reserve a provider repeatedly after the canceled selection is detached.
7. **Cross-process selection remains a pre-existing race.** ApplyCapacityHoldAsync
   stamps SelectedMessageId without the provider lock or Version increment, and
   the semaphore is local to one server process. Conditional exact selection/key
   writes and the message row lock protect this cleanup; they do not establish
   fleet-wide serialization against a later stale tracker save during overlap.
   Preserve this limit for staged rollout; do not claim the semaphore solves it.
   A broader selector protocol belongs in separately commissioned work if observed.
8. **Resume absence can change message status.** The cleanup's conditional message
   write and row lock are mandatory (D-4), not an assumption that Canceled is
   immutable. A racing Pending winner must be preserved; no unconditional tracker
   save may clear its linkage. Measure queue semaphore hold time across the DB
   transaction, including busy/rollback paths.

This plan changes provider wait lifecycle, not a session that waits for input.
There is no new session release/parking action or deadline. With parking disabled,
an input-waiting session still has no automatic release deadline (CARD-1083).

## Card and S3 Risk 8 wording to amend

The caller should replace CARD-1603's Risk 8 paragraph and its queue fix direction,
and make the same correction to S3 plan Risk 8, with this text (no generated
files under docs/cards are edited by this task):

> A capacity-selected Channel/Scheduled message can be canceled by human Drop
> (CancelAsync), parked-stale cancellation (CancelParkedIfStaleAsync), or a missing
> remote spill body (RemoteSpillCourier.MarkUndeliverable). Before redemption,
> expiry re-keys the canceled row without spending an admission; after redemption,
> SelectedMessageId is treated as an execution receipt and revalidation can repeat
> without another admission. A later successful session turn can end the wait.
> Brief-expiry cancellation concerns Delegation rows and cannot cause this case.
> Detach the canceled selection and suppress empty-queue grants while retaining
> the session's wait, so already-held and later Channel/Scheduled input remain
> deliverable. Human-started standing waits are a separate supersede path.

The title and Risk 9 premise remain valid. This is a precise handoff amendment,
not a claim that the live card or the S3 branch was changed.

## Completion and handoff

Plan Review must judge the positive-evidence predicates, null-generation fallback,
queue demand/later-input delivery, queue lock/DI lifetime, CAS and lock ordering,
independent retained consumer, and
performance exceptions. Ask a different company's model to perform that Review.
No extra user decision is required to produce this plan under D-1..D-6.

Before Code/Review calls a product branch landable, fetch master and run
`git merge-tree --write-tree origin/master HEAD` without merging; conflicts need
their own resolution work. Run `scripts/check-evidence-diff.ps1 -BaseRef
<task-base-sha> -HeadRef <full-tip>` and validate the checkpoint source receipts.
Keep generated evidence ignored. Remove only tool-owned alternate `bin-c1603/`
outputs using documented custody after all children have joined. Never delete
other worktrees, desktop evidence or unrelated files.

After master land, commission the reviewed rc port and its same checkpoint roster
at the new rc SHA (adjust only for proven prerequisite roster differences), then
separately activate under the release/runbook owners. Confirm loaded server SHA,
not merely healthy status. Read-only activation evidence should show a
selected-message-canceled **selection detach** (same unfinished wait, no grant
while empty), or standing-consumer-replaced **Superseded** outcome. Across at
least three configured expiry intervals the canceled action must not regain a
grant; another eligible waiter advances when its provider clock permits. A new
held message may correctly grant the retained queue wait under a new action.


Do not generate production traffic or cancel a live message just for this check.
