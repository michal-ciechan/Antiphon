# CARD-1603: stop capacity grants cycling after their consumer disappears

Plan task `2cebeca1`. Source inspected at
`f8bddb6efda7b0e8f50f3b45744087e52188bc66` on 2026-10-29.
No product code is changed by this Plan task. Verification is designed here for
execution after independent plan Review; this is not a claim of passing tests.

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
| A canceled selected message leaves a reusable grant | `SessionMessageQueueService.cs:5567-5573` stamps `SelectedMessageId` before delivery. `:2204-2222` cancels an expired brief without ending its wait; `:2231-2243` also handles an expiry after the first Sent claim. |
| The orphan sweep catches that cancellation | `CapacityRecoveryService.cs:665-680` checks terminal/missing session and task owners only. It does not inspect `SelectedMessageId` or queued-message status. A live session with a canceled selected message passes through. |
| Expiry eventually exhausts the leak | `CapacityRecoveryPolicy.cs:92-96` treats the selected message as an execution receipt. `CapacityRecoveryService.cs:1347-1355` requests revalidation; `:1208-1222` preserves the admission count and schedules another grant after expiry. The 120-second default bounds a grant, not the number of grants. |
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

Rejected: only adding callbacks to human Start and brief expiry. That misses
existing bad rows, other queue cancellation writers, and a crash after cancellation
commits but before the callback. Rejected: cancel on age, runner absence or an
agent's `Running` flag. Those are not proof that this wait has lost its consumer.

**D-2 - A canceled selected queue message ends its obsolete queue action.** A
candidate must be unfinished, be `LiveSession` or `PendingQueue`, have a
`SelectedMessageId` that resolves to an actual `Canceled` row, and have matching
message `CapacityWaitId` and `CapacityRecoveryActionKey`. Missing rows, an expired
deadline without committed cancellation, `Pending`, and `Sent` are not evidence.
Keep a wait that has a launch/dispatch receipt (`LaunchSessionId`, `LaunchReceipt`
or `DispatchAttemptId`): it still represents admitted execution. Also keep a live
task consumer that can independently redeem: a linked `Queued` task or a
`Working && CapacityWaitRetained` task. The existing terminal-task sweep still
owns ended tasks; do not turn canceling an unrelated optional brief into canceling
its parent's recovery. No wait registered for `QueuedTask`/`RoutingBlockedTask`
is ended by this new queue predicate.

Under the queue gate and provider lock (D-4), recheck that the exact selected row
is still canceled and still selected. Then set `State/Outcome = Canceled`,
`OutcomeReason = selected-message-canceled`, increment `Version` once and update
`UpdatedAt`. Keep the selected message, action key/ordinal, receipt fields and
`AdmissionCount` as history. Clear `NeedsRevalidationGrant`; clear the provider
grant only when it names this wait. Preserve `NextAdmissionAt` and wave fields.
A previously spent admission is not refunded. Once the provider clock is due,
another eligible waiter can receive the grant in the same reconciliation call.

If the queue selected a different pending row before cleanup acquired its gate,
the old cancellation is stale evidence: preserve the wait and that newer selection.
Do not cancel messages, move a task, type input or alter session status here.
This choice preserves real post-Sent delivery retries and S3's retained-return
consumer. Rejected: `!Pending` as the predicate (`Sent` is legitimate delivery),
clearing the receipt/rearming the same canceled action, or spending an admission
to make the attempt cap eventually close it.

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
and perform the same counter/history/grant handling as D-2. Do not stamp
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
scanned/finished/deferred counts and elapsed time, with no message bodies.

| Path | Lock and transaction order | Reason |
|---|---|---|
| Canceled selection | Resolve the existing singleton `SessionMessageQueueService` lazily from the fresh scope; try `GetLock(message.AgentSessionId).WaitAsync(0, ct)`. If busy, skip. Only after the gate, open a fresh DB transaction, try provider advisory (rank 7), reload wait/message/task facts, conditionally finish capacity rows (8), commit, release gate in `finally`. | Queue selection and cancellation use that same semaphore (`SessionMessageQueueService.cs:57,6047`). It prevents cleanup from stranding a replacement selection. Resolve lazily because the queue already depends on capacity recovery; do not introduce a constructor DI cycle or a second lock dictionary. No production queue is resolved when the discovery query has no queue candidates. |
| Standing replacement | Fresh transaction; current session `FOR UPDATE NOWAIT` (4), then agent `FOR UPDATE NOWAIT` (5). Reload pointer; if it differs from discovery, abandon this candidate. Prove ownership. Try provider advisory (7), reload wait, conditionally finish capacity rows (8), commit. | Same session-before-agent order as `AgentControlService.cs:543-549`, same connection throughout. S3 capacity start commits pointer and wait together. This pass writes no session or agent row. |

Use `pg_try_advisory_xact_lock` for the new pass's provider acquisition; false is
a deferred candidate, not cancellation. A row `NOWAIT` 55P03 rolls back that
candidate. Do not retry in the same pass. Existing grant/consume acquisition and
timeouts are unchanged. No queue lock is acquired while holding a database lock.
Cancellation disposes the transaction and releases the semaphore.

Finish using a conditional update over the just-read unfinished state, `Version`,
`ActionKey` and selection identity. `Version` is not an EF concurrency token
(`server/Infrastructure/Data/AppDbContext.cs:2903-2924`), so a tracked save alone
is not the compare-and-set. Zero affected rows means skip; never clear a grant
after losing that compare-and-set. Provider clearance and wait termination commit
together. One fresh context per candidate avoids stale tracked entities after
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

Commit and push both slices' test witnesses before either production fix, then
commit/push S1 and S2 separately. The two product slices form one final checkpoint
group, so their final green verification shares one build. Tests call existing
production entry points at the red commit; an absent new helper or build failure
is never the RED witness.

| Slice | Files | Change and tests |
|---|---|---|
| S1 | `server/Application/Services/CapacityRecoveryService.cs`; new `server/Application/Services/CapacityRecoveryService.ObsoleteConsumers.cs`; new `tests/Antiphon.Tests/Application/CapacityObsoleteQueueTests.cs` | D-1/D-2/D-4 queue path, shared conditional finish helper, integration/selection-race/rollback guards V-1 through V-5. Existing orphan and live-grant expiry suites stay unchanged. |
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
| CP-1 test execution (13 new queue results) | About 2 minutes | 4 minutes, excluding its build |
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
| V-1 `C1603_canceled_selected_brief_stops_regranting` | `before-claim`, `after-claim` / **2** | Real queue fixture, live session, wait `TaskId = null` (the optional brief's `ExecutionTaskId` is distinct), and a selected stamped message. Expire through production brief cancellation: already expired before the claim, or a one-shot DB interceptor advances fake time only when this message is claimed Sent (adapt `OutputDistillationDeadlineTests.ClaimClock`). First prove actual `Canceled` message, canceled optional task, zero typed bytes, live session. Reconcile; expect wait `Canceled/selected-message-canceled`, preserved key/receipt and count (0 before claim; 1 after claim), no revalidation, other eligible wait gets provider once its clock is due. Advance 121 seconds and reconcile three times; old wait never holds the grant again. **RED before fix:** the selected wait stays unfinished and is granted/revalidated again; no-op cleanup fails the terminal-state and ownership assertions. |
| V-2 `C1603_queue_cleanup_preserves_real_consumers` | `pending`, `sent`, `missing-selected-row`, `unselected-canceled-row`, `retained-task`, `launch-receipt` / **6** | Seed a matching held grant, with the named legitimate/unknown consumer state. Reconcile without advancing its due/expiry clock. Wait state/count/key and provider ownership stay intact. `retained-task` uses a linked Working retained task; `launch-receipt` uses a still-live admitted execution. Guard PC: remove the selected-message Canceled predicate; pending/sent become terminal and the method fails. Missing selected IDs used by existing grant-liveness tests must stay valid. |
| V-3 `C1603_queue_cleanup_rechecks_selection_and_grant` | `replacement-selection`, `other-grant`, `already-progressed` / **3** | At the discovery boundary, change the selected row to a new Pending message under the queue gate, or move provider ownership to an unrelated wait, or mark the candidate Progressed. Resume reconciliation. New selection stays deliverable; other grant is untouched while the obsolete wait ends; Progressed stays terminal with no extra version bump. Assert exact selected ID, state and grant owner. Guard PC: use the discovery snapshot without reload/conditional recheck; replacement/progress variants fail. |
| V-4 `C1603_queue_cleanup_rolls_back_and_releases_gate` | none / **1** | One-shot transaction interceptor throws before the cleanup commit. Read from a separate context: original wait and its grant remain, no count change. Fault removed, a second call cancels once and releases the gate (another acquisition succeeds). Stub fails the second call; dropping the transaction fails persisted rollback. |
| V-5 `C1603_queue_cleanup_is_bounded_and_idempotent` | none / **1** | Batch 2, three eligible canceled selections plus live controls; one shared fixture, separate rows. First/second/third calls end 2/1/0 new waits. Finished version increments exactly once; a caller-created future `NextAdmissionAt` and unrelated provider grant remain unchanged. Stub ends zero. PC removing `Take(batch)` ends three on the first call. |
| V-6 `C1603_human_start_supersedes_standing_wait` | `fresh-null-generation`, `resume-null-generation`, `resume-stamped-generation`, `fresh-consumer-key-fallback` / **4** | Real Start via existing fake-adapter supervision harness, a granted standing wait, and a second eligible waiter. First/fourth start Fresh; second/third resume the existing failed row. Null cases reproduce `RecordStartFailureAsync`'s actual registration; fallback omits AgentId. Stamped case has one prior admission and a session carrying the old capacity key, to prove full-generation comparison wins. Observe committed new pointer/StartedAt and exactly one human launch. Reconcile; expect Superseded/reason, unchanged admission count, provider released when due, no extra launch/kill and no repeat grant after three fake expiry windows. **RED before fix:** the old wait remains unfinished. No-op/stub fails the terminal-state/provider assertions. |
| V-7 `C1603_standing_cleanup_keeps_current_or_unproven_consumer` | `same-running-generation`, `original-stopped-generation`, `capacity-fresh`, `capacity-resume`, `legacy-episode-key`, `missing-pointer`, `conflicting-owner` / **7** | Preserve exact wait and grant accounting. Capacity variants actually run S3-A supervisor starts; assert one admission, current wait tuple, and successful ordinary D8-style re-arm after a failed start. Original stopped is still the restart consumer. Legacy key uses an older action ordinal of the same wait, not just current ActionKey. Missing/conflicting identity is unknown, never replacement. Guard PC: replace the generation/replacement predicate with agent-exists; current/stopped cases fail. |
| V-8 `C1603_standing_cleanup_serializes_with_acceptance` | `capacity-commit`, `human-rollback`, `session-busy`, `agent-busy` / **4** | Use a second connection and existing start reservation interceptors. Capacity commit completes before locked recheck: updated wait tuple is retained. Human rollback leaves old tuple/grant; no supersede. Hold the session or agent row: sweep defers promptly with no provider lock and no mutation, then a subsequent pass after release ends a genuinely replaced wait. A DbCommandInterceptor records zero provider-acquisition commands on busy-row calls, and successful session/agent lock acquisition preceding that command otherwise; post-rollback pg_locks alone cannot prove the order. Assert DB outcomes and completed/joined calls. PC moving provider acquisition ahead of row acquisition fails the command-order assertion; PC dropping the pointer/tuple reread fails the capacity-commit case. |
| V-9 `C1603_standing_cleanup_is_bounded_and_provider_busy_safe` | `free`, `provider-busy` / **2** | Batch 2 with three positively replaced standing consumers; finish 2/1/0, one version bump each, all admission counts unchanged. Busy arm holds the provider on a second connection: pass returns promptly with no changes; after release it obeys the same batch bound. Stub finishes zero; blocking provider acquisition fails the prompt-return assertion. |
| V-10 `C1603_obsolete_cleanup_loses_compare_and_set_safely` | none / **1** | At before-finish boundary, a separate context advances the wait to Progressed/version+1 while leaving the grant for its normal reconciler. Finish must affect zero rows and leave that grant intact; inspect before the later ordinary grant pass, using an interceptor or boundary snapshot, then assert normal reconciliation revokes it. No dirty tracker replays the old state. PC removing the state/version CAS overwrites Progressed and fails. |

Totals: **13 queue + 18 standing = 31 new results**. V-2/V-3/V-7/V-8/V-10 are
guards with the named method-scoped positive controls; V-4/V-5/V-9 also have
the named fault/batch/lock controls. Mutation is a separate commissioned stage:
one precise `/*/*/ClassName/ExactTestMethod` per control, record the assertion
failure, restore, and rerun that same method green. No class-wide mutation cycles.

### RED execution before product changes

One leased isolated build of the committed test-only source, reused for exactly:

1. `/*/Antiphon.Tests.Application/CapacityObsoleteQueueTests/C1603_canceled_selected_brief_stops_regranting` — 2 executed, 2 expected terminal-state failures.
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
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1603/` | c1603-queue | `/*/Antiphon.Tests.Application/CapacityObsoleteQueueTests/*` | V-1..V-5 | all 13 new results, 0 failed/skipped | 13 | 9 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
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
6. **Compatibility may create a genuinely new episode.** It runs before this
   cleanup and excludes finished rows when finding an unfinished episode. The
   old canceled/superseded wait must never return; do not suppress a new wall or
   new pending input merely because an earlier episode finished. V-1/V-6 repeat
   reconciliation and assert the obsolete ID never regains ownership. A later
   empty episode created solely from historical wall evidence is distinct from
   resurrecting this ID; report any such observation rather than silently
   widening compatibility behavior in this card.

This plan changes provider wait lifecycle, not a session that waits for input.
There is no new session release/parking action or deadline. With parking disabled,
an input-waiting session still has no automatic release deadline (CARD-1083).

## Completion and handoff

Plan Review must judge the positive-evidence predicates, null-generation fallback,
queue lock/DI lifetime, CAS and lock ordering, independent retained consumer, and
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
`selected-message-canceled`/`standing-consumer-replaced` terminal outcome, no
subsequent grants to that wait ID across at least three configured expiry
intervals, and another eligible waiter advancing when its provider clock permits.
Do not generate production traffic or cancel a live message just for this check.
