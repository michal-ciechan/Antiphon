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
retain it, log the bounded diagnostic, and report the row for follow-up rather
than guessing. Fresh human starts and initial unspent model-hold resumes do not
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

Each slice commits and pushes its failing witnesses first, then its production
fix. Tests call existing production entry points at the red commit; an absent
new helper or build failure is never the RED witness.

| Slice | Files | Change and tests |
|---|---|---|
| S1 | `server/Application/Services/CapacityRecoveryService.cs`; new `server/Application/Services/CapacityRecoveryService.ObsoleteConsumers.cs`; new `tests/Antiphon.Tests/Application/CapacityObsoleteQueueTests.cs` | D-1/D-2/D-4 queue path, shared conditional finish helper, integration/selection-race/rollback guards V-1 through V-5. Existing orphan and live-grant expiry suites stay unchanged. |
| S2 | Same production files; new `tests/Antiphon.Tests/Application/CapacityObsoleteStandingTests.cs` | D-3/D-4 standing path; real human Start, null generation, legitimate admitted start, replaced generation and row-lock/refusal guards V-6 through V-10. No change to AgentControlService, queue cancellation semantics or live process ownership. |

Test support stays local to these two classes unless an existing helper needs a
small optional interceptor argument. Use `CapacityRecoveryTestSupport`,
`ClaudeDialogFixture`/`BridgeQueueHarness`, `AgentSupervisionTests.BuildHarness`
and `CapacityLockProbe`; do not add another test host implementation.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and
`docs/test-performance-standards.md`. This Plan task runs no tests or builds.
The Code budget will use shared Postgres/template setup, isolated test databases,
fake time for grant expiry, and explicit build/slot/fixture/body/teardown timings.
The S3 plan reports 1.2-11.4-second individual capacity methods and 217-399-second
builds, but those ranges are not measured p95 for this change. New budgets must
remain provisional until the bounded Code sample supplies comparable timings.

## Placement

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-29.
Defaults revision 2 has no per-kind override; an eligible Linux lane is available.
Use current defaults rather than embedding a fleet location; omit `-Runner` and
`-Platform` for portable work. Checkpoints will name the Linux integration lane.
No desktop files or native Windows runs are needed for this plan.
