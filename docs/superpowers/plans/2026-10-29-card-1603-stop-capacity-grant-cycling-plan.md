# CARD-1603: retire canceled or parked queue selections and supersede replaced standing consumers

Plan round 3, task `71d5ca7f`, revises round 2 (`c404bdc3`) in place after
re-review `8db4c0fe`. Task base: `169583fecc77d15ac020de20c9e4daad725013f1`.
Round 2 followed design Review `b7762633`; its confirmed standing design, lock
order, S3-A dependency, classification and performance obligations are retained.
Product source inspected at
`f8bddb6efda7b0e8f50f3b45744087e52188bc66` on 2026-10-29.
No product code is changed by this Plan task. Verification is designed here for
execution after independent plan Review; this is not a claim of passing tests.
S3-A inspected separately at `6564c5757dd0ec7c347fe50275f1334498f9e073`;
its presence on a remote task ref is not a landed-prerequisite claim.
Code (task `1d5d34a3`) re-anchored the service line citations below to master
`e65343132e877ce20bd26540d440eff3867d9798`, where CARD-1593 S3-A is present.
The inspected-source sentences above stay as the plan-time record.

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
S3-A must first receive its approving Full Review; the inspected remote tip is
neither that approval nor a land. This round-3 plan revision also needs approval
before the Code handoff is actionable.
That dependency has since landed: master `e65343132` contains S3-A, and this
Code task implements against that base. The pre-start `RedeemAsync` sentence
above is the plan-time base, not the tree this Code task compiles.

## Ground truth

All paths below are relative to `server/Application/Services/` unless stated otherwise.

| Card assumption | Verified behavior at the inspected source |
|---|---|
| Expiring an optional brief cancels a capacity-selected row | **False.** ApplyCapacityHoldAsync selects only Channel/Scheduled (SessionMessageQueueService.cs:5534-5573); brief expiry requires ExecutionTaskId (:2206-2209, :2235-2236). Production execution briefs are Delegation (SessionMessageQueueService.DispatchBrief.cs:228-230; AgentTaskDispatcher.cs:5372-5380,5455-5461,8996-8998; AgentSessionService.cs:1132-1134). Delivery batches never mix origins (:2601-2625). StampQueueActionAsync has only test callers; it cannot establish this alleged production cause. |
| A canceled selected Channel/Scheduled message leaves a reusable grant | **True.** Human Drop CancelAsync (:1205-1208), schedule supersession CancelPendingBySourceScheduleAsync (:1360-1384), and RemoteSpillCourier.CancelUndeliverableAsync / MarkUndeliverable (:262-286) can cancel such rows. ScheduleService.FireAsync's prompt arm (:298-309) automatically cancels the previous Pending, never-typed Scheduled copy through CancelPendingIfUntypedAsync, then enqueues its replacement. Drop also works after a redeemed delivery attempt reverts to Pending. None detaches the selected row from its wait. |
| The stale-park sweep automatically cancels Channel/Scheduled | **False; corrects both earlier plan rounds and round-1 Review.** CancelParkedIfStaleAsync has one production caller: ParkedMessageSweepService.ScanAsync (:49-102), restricted to Delegation/System/Check/Supervision, SourceTaskId null and no active task. Calling it on a Channel row in V-1 was a test-only transition. |
| A parked or future-held Pending row is demand | **False.** Flush excludes DeliveryAttempts >= Math.Max(1, DeliveryVerification.MaxDeliveryAttempts), future HoldUntil and LegacyUnclassified maintenance (:2417-2424). A parked selected row remains Pending/keyed; the selected-ID receipt plus free revalidation repeats indefinitely unless selection is retired. Delivery-attempt and capacity-episode caps are different settings. |
| Losing demand can use the generic grant-revoke transition | **False.** RecordRevokedGrantAsync (CapacityRecoveryService.cs:1282-1303 on master e65343132) turns an unexpired ActionPending/Ready/Admitted wait Deferred, preserving any earlier reason via ??=. Ordinary grant candidates exclude Deferred, and no general demand-arrival transition readies it. An unselected dropped Y can therefore strand later Z if the new demand check uses that branch. |
| The orphan sweep catches that cancellation | CapacityRecoveryService.cs:767-782 checks terminal/missing session and task owners only. It does not inspect SelectedMessageId or queued-message status. A live session with a canceled selected message passes through. |
| Expiry eventually exhausts the leak | Before redemption, D-12 calls RearmRevokedGrantOnAsync / PrepareNextAttemptOnAsync (:1305-1325,1480-1518): canceled rows are re-keyed without a status filter and count stays 0. After redemption, CapacityRecoveryPolicy.HasExecutionReceipt treats the selected row as a receipt; :1457-1465 requests free revalidation, count stays 1. The default 120 seconds bounds each grant, not repetition. A later successful report-boundary turn **can** end a LiveSession wait via ObserveTranscriptAsync (:981-1007); the reproducer must not emit that progress. |
| The wait belongs only to its selected row | **False.** It serves the whole held session queue. While no grant is held, a new Pending Channel/Scheduled row can be Held and unselected (SessionMessageQueueService.cs:5544-5573). Canceling the wait strands that row and later held input. |
| Compatibility will recreate a finished wait for later input | **False.** ReconcileCompatibilityAsync (:1090-1234) filters keys after its persisted cursor and keeps the cursor when empty; it does not revisit an already-scanned session. Keep the existing wait as the consumer. |
| A human start progresses a standing wait | `AgentControlService.cs:951-980` commits the accepted session pointer and resets human-start supervision state. The wait is redeemed only when that start carries a capacity admission (`ConsumeAdmittedStartAsync`, `:843-859`). Transcript progress looks up the wait by `SessionId` (`CapacityRecoveryService.cs:987-992`). |
| A running agent will eventually consume the old standing grant | `AgentSupervisorService.cs:171-212` handles the live session and returns before the capacity branch at `:218`. `OrphanedWaits` excludes `StandingStart` entirely. |
| Every standing wait includes the old session generation | False: `AgentSupervisorService.cs:434-445` creates model-hold waits with `AgentId` and `BlockedAt`, but no `SessionId` or `SessionStartedAt`. The design must handle that production shape, not just a fully stamped test row. |
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

**D-2 - Retire a canceled or parked selection, keep the session's wait.** A candidate
must be unfinished, be LiveSession or PendingQueue, have SelectedMessageId
resolving to an actual Channel/Scheduled row that is either Canceled or Pending
at the delivery-attempt cap, and have matching message CapacityWaitId,
CapacityRecoveryActionKey and session identity. Use
`Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts)`, exactly the
queue's MaxAttempts, never CapacityRecoveryService.MaxAttempts (the episode cap).
Missing rows, uncommitted expiry, Pending below that cap and Sent are not evidence.
Keep a wait with a launch/dispatch receipt
(LaunchSessionId, LaunchReceipt, DispatchAttemptId) or
confirmed prompt: it still represents admitted execution. Also keep a linked
Queued task or Working task with CapacityWaitRetained that can independently
redeem. The terminal-task sweep still owns ended tasks. No QueuedTask or
RoutingBlockedTask wait is changed by this queue cleanup.

Under D-4's gate/transaction, conditionally detach only that obsolete message's
CapacityWaitId, CapacityWaitVersion and CapacityRecoveryActionKey, then clear the
wait's SelectedMessageId and NeedsRevalidationGrant. Retire its action: increment
ActionOrdinal, derive a new ActionKey, increment Version exactly once, and stamp
UpdatedAt / OutcomeReason = selected-message-canceled or selected-message-parked.
The parked row stays Pending at its existing attempts cap: do not cancel it,
reset attempts, erase delivery/transcript facts or make it automatically retry.
Send-now and late confirmation retain their queue semantics; a status/attempt
change that makes the obsolete predicate false at the locked recheck prevents
cleanup. For ActionPending,
Admitted or Ready, use State/Outcome = Ready and a due time no earlier than the
old due time or normal jitter eligibility for this reset. Deferred with reason
grant-revoked may return to Ready; preserve other deferrals and WaitingForHold /
Reheld so cleanup cannot bypass an unrelated prerequisite. No new state/migration.
Do **not** call PrepareNextAttemptOnAsync here: at this base it would rewrite
queue rows under the provider lock. Only the exact obsolete row is detached.

Clear the provider grant only if it still names this wait and old action. Keep
AdmissionCount, provider LastActionKey/LastActionAt, NextAdmissionAt, wave fields,
and the message's delivery/cancellation facts. A spent admission is not
refunded. Keep provider ExpectedWaitVersion/ExpectedOwnerId handling consistent
with ClearGrant; no stale expected stamp constitutes a held grant. Log old wait/action/message IDs and disposition without body text.
Normal attempt caps still apply when new work is admitted; an already exhausted
episode is outside the new delivery guarantee. No admission is spent merely to
retire a selection.

Add one shared **queue-demand predicate** to GrantReadyAsync, both its candidate
query **before Take(batch)** and outstanding-grant validation. For a resolved
LiveSession/PendingQueue consumer without an independent execution receipt/task,
demand is a Pending Channel/Scheduled row eligible for this wait (unkeyed, or
linked to this wait/current action), with **DeliveryAttempts below the delivery
cap, HoldUntil null or <= the same captured now, and MaintenanceKind !=
LegacyUnclassified**. Mirror the flush's deferred-run generation eligibility if
those fields are present, using the current session generation. Do not use the
display-only NoteHeader == Held, treat Send-now's explicit hold override as
automatic demand, or count a selected ID alone as deliverable demand.

Known selected Pending rows obey these rules; a future-held selected row keeps
its selection but cannot alone reserve capacity before due. A positively
canceled/parked selection must pass through the detach above before another
queue grant, even if Y is already waiting (direct GrantReadyAsync must not reuse
X's obsolete receipt for Y). Selected Sent, missing/unknown selection identity,
launch/dispatch receipts, confirmed prompts and independently redeemable tasks
keep their conservative existing semantics. Resolve a legacy missing SessionId
through the exact session consumer key; unresolved identity stays conservative.
Use SQL existence reads, never a queue semaphore under the provider lock.
Recheck the winner before writing; a later cancellation is handled next tick.
An empty/parked-only/future-held-only wait is skipped without repeated
version/ordinal changes. This covers direct GrantReadyAsync callers too.

**Outstanding grant loses demand:** handle this as a distinct cause, not a new
false return routed into RecordRevokedGrantAsync's Deferred branch. Under the
existing provider transaction, reread the matching wait/action and demand.
For an otherwise grant-candidate queue wait with proven absent demand and no
independent/unknown receipt, clear only its own provider grant and set
State/Outcome = Ready, OutcomeReason = queue-demand-lost, UpdatedAt = now and
Version += 1. Keep ActionKey/ActionOrdinal, selection/linkage, AdmissionCount,
NeedsRevalidationGrant, DueAt and all provider admission-clock/wave/history
fields. A receipt-backed revalidation flag stays set until obsolete-selection
cleanup retires it; a future-held retry is not charged a second admission merely
because its grant was released. Do not call PrepareNextAttemptOnAsync, re-key
queue rows or acquire their semaphore/row locks under the provider lock.
Use the same conditional state/version/action write discipline; if it loses,
leave the grant for the next reconciler. The reason is stamped explicitly even
when the previous reason was grant-expired or new-wall-wave.

Review A-2, recorded at Code. A selected Pending row whose HoldUntil is still
in the future is a seeded guard in V-12: production sets HoldUntil only at
enqueue (SessionMessageQueueService.cs:795) and later only clears it, so that
arm cannot occur as a live transition. An Admitted consumer with
NeedsRevalidationGrant and proven-absent demand still becomes Ready with
OutcomeReason queue-demand-lost. ConfirmPromptOnAsync
(CapacityRecoveryService.cs:1023-1037 on master e65343132) returns immediately
for Ready and does not stamp PromptConfirmed. NeedsRevalidationGrant stays set,
so a later due retry is not a second admission. RedeemOnAsync refuses Ready
(:354-356); GrantReady sets Ready back to ActionPending and leaves the flag
(:222-223), so that consume does not increment AdmissionCount.

This cause takes precedence over expiry for a matching, otherwise eligible wait
whose demand is proven absent, so expiry does not rotate an empty action first.
Other invalidations, independent live consumers and uncertain evidence keep the
existing handling; never turn a genuine Deferred/Reheld/WaitingForHold wait into
Ready via this new branch. The normal candidate filter immediately skips the
empty wait, allows an eligible competitor at the unchanged provider clock, and
later Z uses the same wait without a human prompt or cursor rewind. With no
held grant, repeated empty ticks make no further reset. If a known obsolete X
still needs detaching, the maintenance pass does that once under D-4; this
grant-only transition does not bypass its lock order.

An unselected held row Y already present when X is canceled remains eligible;
a later held row wakes the **same** wait on the ordinary grant tick. No enqueue
registration or compatibility-cursor rewind is required. While empty, another
waiter gets the provider once its unchanged clock permits. If selection moved
to Y before cleanup rechecks, preserve Y. Delivery still obeys normal dialog,
composer and transcript guards: cancellation does not prove an attempted body
absent from the composer. Keep genuine post-Sent retries and retained returns.

Rejected: ending the wait (drops existing/later held work); finishing plus a new
enqueue-time registration path (larger lifecycle change); !Pending (Sent is valid);
resetting the episode count; age-based cleanup; retaining a canceled/parked
selection as a receipt (perpetuates free revalidation); and generic Deferred on
demand loss (strands later input). Letting D-12 expire one last empty grant is a
safe fallback but rejected here because a distinct Ready-preserving release is
small, directly testable and frees proven unused capacity on the next tick.

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
(`AgentControlService.cs:694-732`); a preflight that merely tried to start changes
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
Include canceled versus parked detach reasons and the grant-only demand-loss
release in structured logs, without counting a busy lock as a lifecycle deferral.

| Path | Lock and transaction order | Reason |
|---|---|---|
| Canceled/parked selection | Lazily resolve the existing singleton queue; try GetLock(message.AgentSessionId).WaitAsync(0, ct). Open a fresh transaction only after the gate. Lock the exact message row FOR UPDATE NOWAIT, recheck (Canceled OR Pending at delivery cap)/origin/session/wait/key and conditionally clear its linkage **before** trying provider advisory (7). Reload the wait, recheck selection/state/version/key/independent consumer, CAS capacity rows (8), commit. Any failed check or busy provider rolls back the message detach. Release gate in finally. | Same semaphore as selection/cancellation (SessionMessageQueueService.cs:57,6047), without a new dictionary or constructor cycle. Prelocking the message and writing its linkage before rank 7 avoids reversing a queue-row -> provider order if S3-B is present; all post-provider writes remain capacity-only. No queue is resolved with zero queue candidates. |
| Standing replacement | Fresh transaction; current session `FOR UPDATE NOWAIT` (4), then agent `FOR UPDATE NOWAIT` (5). Reload pointer; if it differs from discovery, abandon this candidate. Prove ownership. Try provider advisory (7), reload wait, conditionally finish capacity rows (8), commit. | Same session-before-agent order as `AgentControlService.cs:575-581`, same connection throughout. S3 capacity start commits pointer and wait together. This pass writes no session or agent row. |

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
together. The message write includes the exact canceled/parked predicate and linkage; its
row lock stays held to commit. DeferResumeAbsenceAsync currently can reset rows
to Pending without a status predicate (SessionMessageQueueService.ResumeReceipt.cs:122-127).
If such a writer won first, cleanup skips unless the row still meets the parked
predicate; a later Pending write is fresh demand only below the delivery cap and
when due, never authority to restore a retired action. Do not rely solely on the local
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

**D-7 - Classify slow work explicitly and honor the commissioned verification profile.**
Both new classes use Integration + Slow and get exact fully qualified entries with
adjacent CARD-1603 reason comments in tests/Antiphon.Tests/slow-tests-allowlist.txt.
Their DB/real queue/start/barrier work is provisionally 3-30 seconds per result;
this avoids treating a known >=5-second path as unexpectedly fast. Retain the
actual duration-tripwire results and review classification after measurements;
Slow does not exempt any named checkpoint. No NativeLinuxGate: there is no
OS-selected behavior. tests/linux-test-roster.json needs no entry: it is the
frozen CARD-0590 roster, not an enforced admission list (current CapacityLockOrderTests
is already absent). Do not edit it or add a native gate to manufacture coverage.

## Slices

Commit and push both slices' test witnesses (including V-11/V-12) before either production fix, then
commit/push S1 and S2 separately. The two product slices form one final checkpoint
group, so their final affected-integration verification shares one build. Tests call existing
production entry points at the red commit; an absent new helper or build failure
is never the RED witness.

| Slice | Files | Change and tests |
|---|---|---|
| S1 | `server/Application/Services/CapacityRecoveryService.cs`; new `server/Application/Services/CapacityRecoveryService.ObsoleteConsumers.cs`; new `tests/Antiphon.Tests/Application/CapacityObsoleteQueueTests.cs` | D-1/D-2/D-4 canceled/parked selection path, deliverable-demand predicate, Ready-preserving demand-loss revocation, conditional reset helper; production schedule supersession, selection-race/rollback and later-input guards V-1 through V-5 and V-11/V-12. Existing orphan and live-grant expiry suites retain their assertions; CP-3 adds WallRerouteDispatchTests for the independent-task exemption. |
| S2 | Same production files; new `tests/Antiphon.Tests/Application/CapacityObsoleteStandingTests.cs` | D-3/D-4 standing path; real human Start, null generation, legitimate admitted start, replaced generation and row-lock/refusal guards V-6 through V-10. No change to AgentControlService, queue cancellation semantics or live process ownership. |

Both test commits also update `tests/Antiphon.Tests/slow-tests-allowlist.txt`
with the D-7 classification. S1 may adjust only the affected existing capacity
fixtures / `tests/Antiphon.Tests/TestHelpers/CapacityRecoveryTestSupport.cs` when
a grant witness previously seeded a real live queue owner with no demand: add a
real Pending held row for that invariant, retain its assertions and result count,
and report the changed setup. Never bypass the demand predicate in production
or add fictitious selected receipts to make old grant assertions pass.

Test support stays local to these two classes unless an existing helper needs a
small optional interceptor argument. Use `CapacityRecoveryTestSupport`,
`ClaudeDialogFixture`/`BridgeQueueHarness`, `AgentSupervisionTests.BuildHarness`
and `CapacityLockProbe`; compose ScheduleService/claim queue using the existing
ScheduleSweepTests pattern inside the queue's isolated schema for V-1. Do not
reuse its global-schema sweep or add another test host implementation.

## Performance budget

Apply [the plan/review requirements](../../testing-and-build.md#performance-standards)
and [R1-R9](../../test-performance-standards.md). This Plan task runs no tests or
builds. All following estimates are for Code, not measurements made by this task.

**Measurement provenance.** The S3 plan at `733f413acc93b79b324ea38972a78aa33bccf117`
reports S1 Linux capacity methods at 1.2-11.4 seconds, first-test shared DB startup
about 10-11 seconds, and leased test builds at 217-399 seconds (220-520 including
slot wait). It supplies no comparable sample count, load or raw per-row p95 for
these new selections. Therefore **measured p95 is unavailable** for CP-1..CP-8;
none of the estimates below is labeled measured. CARD-1603 owns collection in
the commissioned performance Debug and first Code round; CARD-1404/1563 own broader budget tooling. Preserve these
historical ranges rather than treating them as evidence for a new timeout.

**Setup and category.** These are `Antiphon.Tests`, namespace
`Antiphon.Tests.Application`, `[Category("Integration")]` plus `[Category("Slow")]`
for both new classes, server/capacity and
in-process queue-delivery batches. Both new classes are `[NotInParallel]`.
Use TestDbFixture's shared Postgres/template once per TUnit test-host session
(each checkpoint invocation has its own host; record each startup); clone the template
per test with `TestDbFixture.CreateIsolatedSchemaAsync`. No container/migration
per class or per test. Build each per-test fake service harness once; this is
cheap DI/runtime state, not an AppHost/web host. Queue cases use the real queue
with a fake adapter; standing cases use `AgentSupervisionTests.BuildHarness`.
No provider CLI, production runner, real broker, browser or native Pty process
for the new witnesses. Existing lifecycle/lock-order regressions retain their
scratch workspace/dispatcher fixtures; record those IO phases as well.
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
| One isolated Antiphon.Tests build | 4-7 minutes | 25 minutes derived from CP-5; tooling-imposed CARD-1603 exception described below, not a measured 2x allowance |
| Slot admission | Unknown; load dependent, outside execution | `build.slotWaitMinutes = 10`; report busy/HTTP time separately; exit 4 is not a test failure or permission to run unleased |
| Postgres start/readiness + template/migrations | About 11 seconds once per test host from the S3 report | 30 seconds target; if existing shared-fixture guards are larger, record that inherited exception and actual phase under CARD-1603; do not silently change the shared fixture |
| Clone + seed | 1-3 seconds per test, provisional | 10-second operation target; retain actual DB spans and inherited Npgsql command deadline |
| New simple queue/DB test incl. clone and teardown | 3-15 seconds | 30 seconds per test |
| New real-start or concurrent-boundary test incl. clone and teardown | 10-30 seconds | 60 seconds per test; retain the existing 10-second launch-queue/barrier waits rather than increasing them |
| Teardown, fake adapter/runtime and isolated DB | 1-5 seconds per test | 10-second join/cleanup target; failure leaves diagnostic evidence |
| CP-1 test execution (52 queue results) | 6-10 minutes | 20 minutes |
| CP-2 test execution (18 new standing results) | 5 minutes central; 7.5-9 minutes at the 30-second per-result upper estimate | **10 minutes**, replacing the invalid 6-minute cap; provisional 2x central estimate, narrow headroom at the upper estimate must be measured |
| CP-3 test execution (47 existing results, including 15 WallRerouteDispatch results) | 4-8 minutes | 16 minutes |
| CP-4 test execution (24 S3-A supervision results) | 3-6 minutes | 12 minutes |
| CP-5 test execution (52 complete standing restart results) | 8-16 minutes | 25 minutes; provisional, measure nested four-combination loops separately |
| CP-6 test execution (28 acceptance/persistence/transcript results) | 2-5 minutes | 10 minutes |
| CP-7 test execution (28 complete lifecycle results) | 5-9 minutes | 18 minutes |
| CP-8 test execution (29 compatibility/refusal/attention/lock results) | 3-6 minutes | 12 minutes |

The expanded integration selection is **278 results** (fix round 2 added 17 queue results to the prior 261), provisionally **36-69
minutes of tests + 4-7 minutes for one build = 40-76 minutes execution**. Round 2's
238 results / 38-72 minutes and round 1's 21-minute claim are historical estimates,
not execution evidence. This round adds eight new guard results and fifteen
existing dispatcher results; all durations remain provisional. Individual
rows stay below 30 minutes; the complete selection does not. **R1 therefore
requires the caller to commission a performance Debug for this expanded selection
under CARD-1603 before Code executes it**, using this estimate and the S3 timings
as the initial evidence. Debug must decide measured packing/setup repair and
retain its task ID/findings with Code's report. Splitting rows alone does not
waive the over-30-minute selection obligation. Do not run or repeat a long sweep
without that obligation being commissioned; never omit affected classes to fit
the old cap. No performance Debug or test execution is claimed by this Plan task.

Initial collection ceilings (subject to that Debug's findings, not measured p95):
import to ignored YAML; set row timeoutMinutes to **20, 10, 16, 12, 25, 10, 18, 12**
for CP-1..CP-8, build.slotWaitMinutes = 10, and timeouts.rowMinutes = 15. The tool
derives its shared build deadline from the largest dependent row, hence **25
minutes**, not 15; there is no build.timeoutMinutes field. That tooling-imposed
build ceiling is a named provisional CARD-1603 exception to R5 (historical build
range 217-399 seconds; 25 minutes is not claimed to be 2x p95). Debug should avoid
repeating this allowance without a measured repair. Do not use --row-timeout to
widen every row. Use --total-timeout 90m --hard-wall-timeout 100m as finite first
collection ceilings after commissioning Debug. The global ceiling need not equal
the sum of row ceilings. Report execution and admission separately; exit 4 is a
slot timeout, not a failed test and never permission to run unleased.

The commissioned Code/Review profile remains authoritative. A **Final** sweep
also requires the Unit lane; CP-1..CP-8 are the complete affected Integration list,
not a Full ordinary-scope claim. The caller must bind the current Unit manifest
and its independent build/provenance and cost before execution. This plan does
not waive it, silently credit a partial shard set, or authorize an unbounded full
assembly. The existing Unit-lane >30-minute / incomplete-shard issue is documented
under testing-and-build.md#unit-lane-checkpoint-shards-card-1573; commission its
required Debug/complete selection under that owner policy. Unit counts and costs
are **unknown here**, excluded from 278/40-76, and must be reported separately.
Any absent required Unit execution keeps the Final gate incomplete. No native
Windows run or desktop evidence is part of these portable service changes.

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
and `capacity-obsolete-before-finish` (after the fresh reread, before the
conditional update). Supply wait ID and selected-message ID, or `Guid.Empty`; no payload.
The pre-fix service simply never calls those points, so the two mandatory RED
tests do not depend on them. All callbacks/blocked transactions are joined and
released in `finally`; no sleeps or orphaned test work.

### New witnesses and guards

All queue methods below belong to new `CapacityObsoleteQueueTests`; all standing
methods to new `CapacityObsoleteStandingTests`. Each argument produces one TUnit
result; loops/extra assertions do not inflate the checkpoint floor.

| ID / method | Arguments / results | Setup, oracle and why a stub fails |
|---|---|---|
| V-1 C1603_canceled_selected_message_stops_regranting | human-drop-before-redeem, human-drop-after-redeem, schedule-supersession, spill-undeliverable / **4** | Real queue, live session, terminal wall, Channel/Scheduled X with no ExecutionTaskId. Obtain selection through FlushIfIdleAsync -> ApplyCapacityHoldAsync, never StampQueueActionAsync. Before-redeem uses the natural non-held Ui head: X is selected behind it, but pending[0] has no capacity key, so redeem is skipped; let Ui delivery fail without a UserPrompt/TurnEnd, join flush, then real CancelAsync(X). Assert count 0; no selection-save interceptor. After-redeem lets the production failed-delivery path return X to Pending before Drop; count 1. Schedule arm drives actual due claims/FireAsync for the same recurring schedule: first copy X gets selected but remains never-typed via the non-held-head path, then the next fire calls CancelPendingBySourceScheduleAsync -> CancelPendingIfUntypedAsync and enqueues Y. Keep enqueue's opportunistic flush withheld by the real working guard until cleanup, so Y cannot replace X's selection first; assert X Canceled, attempts 0, same SourceScheduleId on fresh Pending Y, and count 0. Spill arm uses a persisted legacy owned pointer with null RemoteSpillBody after loss of process-local staging on restart; neither a complete transcript receipt nor recoverable source bytes exist. This is a recovery-state seed, not a claim the current successful enqueue loses bytes. Real flush selects then calls HoldSpillBodyForDeliveryAsync -> CancelUndeliverableAsync; never direct MarkUndeliverable. Count is 1 at S3-A, 0 if S3-B's pre-Sent boundary is already present; bind to inspected base, not either value. Suppress automatic successful TurnEnd. Reconcile before next grant due; assert exact X detach, SelectedMessageId null, Ready/new action, preserved count/clock/wave and no revalidation. For the three no-demand arms, advance three 121-second windows: no old-action grant/repeated reset and a due competitor advances. Schedule arm instead proves live Y is retained, later selected under the new action on the SAME wait and delivered with correlated UserPrompt; no empty-queue assertion while Y exists. **RED:** current code retains X's linkage/selection at the cleanup boundary in all four arms; no-op fails those exact assertions before later delivery. |
| V-2 C1603_queue_cleanup_preserves_real_consumers | pending-below-cap-and-due, sent, missing-selected-row, unselected-canceled-row, retained-task, queued-task, launch-receipt, confirmed-prompt, dropped-launch-receipt, parked-launch-receipt, dropped-dispatch-receipt, parked-dispatch-receipt, dropped-confirmed-prompt, parked-confirmed-prompt, dropped-queued-task, parked-queued-task, dropped-retained-task, parked-retained-task / **18** | Seed the named legitimate/unknown lifecycle state, with a matching held grant only where grantable; clocks are not due/expired. Confirmed-prompt uses PromptConfirmed with no held grant. Reconcile: keep state/count/key/selection; preserve ownership where grantable and assert the ordinary no-grant outcome for PromptConfirmed. Retained-task is Working + retained; queued-task is independently redeemable; launch/confirmed-prompt represent active work. PC: broaden the obsolete-status predicate or remove independent-receipt exemptions; real consumer data is detached and the method fails. Missing selected IDs used by grant-liveness tests stay conservative. Dropped and parked arms key a canceled or delivery-cap Pending selection to a LiveSession wait that also has a launch receipt, a dispatch attempt, a confirmed prompt, a Queued task, or a Working retained task. State, version, action, selection and the revalidation flag stay unchanged. |
| V-3 C1603_queue_cleanup_rechecks_selection_and_grant | replacement-selection, other-grant, already-progressed, resurrected-pending, message-busy, parked-send-now, parked-late-confirm / **7** | At discovery, replace selection with Y under the real gate, move ownership to another wait, finish Progressed, or change canceled X back to Pending below the delivery cap from a separate context. Reread must retain Y/Progressed/deliverable Pending; other-grant permits detach but never clears that grant. Busy arm holds X's row from another connection: NOWAIT defers promptly and a later call after release succeeds. Parked arms begin with V-11's selected parked X: after discovery/outside all locks, real SendNowAsync succeeds, or a complete matching transcript prompt plus real FlushIfIdleAsync late-confirms X before the deliverability filter. Assert Sent/live receipt wins; cleanup retains its linkage and does not retire that action or stop the session. Assert exact row linkage, selected ID, state, version and owner. PC: remove reload/CAS or the conditional obsolete-message predicate; replacement/progress/resurrection and parked-to-Sent arms fail. PC blocking row acquisition fails busy deadline. |
| V-4 C1603_queue_cleanup_rolls_back_and_releases_gate | none / **1** | Inject a transaction-commit failure after both tentative message detach and wait reset. A separate context must see original selection/message key/wait/grant and unchanged count. Remove fault; another call detaches exactly once and the real gate can be acquired. Record queue-gate hold duration. No-op fails the retry; dropping the transaction fails persisted rollback. |
| V-5 C1603_queue_cleanup_is_bounded_and_idempotent | none / **1** | Batch 2, three canceled selections plus live controls. First/second/third calls detach 2/1/0 selections, leaving all three waits unfinished. Each reset increments version/ordinal once; no repeated empty grant or re-key. Preserve future NextAdmissionAt and unrelated grant. Removing Take(batch) detaches three first time; stub detaches zero. |
| V-6 `C1603_human_start_supersedes_standing_wait` | `fresh-null-generation`, `resume-null-generation`, `resume-stamped-generation`, `fresh-consumer-key-fallback` / **4** | Real Start via existing fake-adapter supervision harness, a granted standing wait, and a second eligible waiter. First/fourth start Fresh; second/third resume the existing failed row. Null cases reproduce `RecordStartFailureAsync`'s actual registration; fallback omits AgentId. Stamped case has one prior admission and a session carrying the old capacity key, to prove full-generation comparison wins. Observe committed new pointer/StartedAt and exactly one human launch. Reconcile; expect Superseded/reason, unchanged admission count, provider released when due, no extra launch/kill and no repeat grant after three fake expiry windows. **RED before fix:** the old wait remains unfinished. No-op/stub fails the terminal-state/provider assertions. A busy cluster-wide provider try-lock leaves the granted wait ActionPending; the witness reconciles again, at most five times, until the supersede commits. |
| V-7 `C1603_standing_cleanup_keeps_current_or_unproven_consumer` | `same-running-generation`, `original-stopped-generation`, `capacity-fresh`, `capacity-resume`, `legacy-episode-key`, `missing-pointer`, `conflicting-owner` / **7** | Preserve exact wait and grant accounting. Capacity variants actually run S3-A supervisor starts; assert one admission, current wait tuple, and successful ordinary D8-style re-arm after a failed start. Original stopped is still the restart consumer. Legacy key uses an older action ordinal of the same wait, not just current ActionKey. Missing/conflicting identity is unknown, never replacement. Guard PC: replace the generation/replacement predicate with agent-exists; current/stopped cases fail. |
| V-8 `C1603_standing_cleanup_serializes_with_acceptance` | `capacity-commit`, `human-rollback`, `session-busy`, `agent-busy` / **4** | Use a second connection and existing start reservation interceptors. Capacity commit completes before locked recheck: updated wait tuple is retained. Human rollback leaves old tuple/grant; no supersede. Hold the session or agent row: sweep defers promptly with no provider lock and no mutation, then a subsequent pass after release ends a genuinely replaced wait. A DbCommandInterceptor records zero provider-acquisition commands on busy-row calls, and successful session/agent lock acquisition preceding that command otherwise; post-rollback pg_locks alone cannot prove the order. Assert DB outcomes and completed/joined calls. PC moving provider acquisition ahead of row acquisition fails the command-order assertion; PC dropping the pointer/tuple reread fails the capacity-commit case. |
| V-9 `C1603_standing_cleanup_is_bounded_and_provider_busy_safe` | `free`, `provider-busy` / **2** | Batch 2 with three positively replaced standing consumers; finish 2/1/0, one version bump each, all admission counts unchanged. Busy arm holds the provider on a second connection: pass returns promptly with no changes; after release it obeys the same batch bound. Stub finishes zero; blocking provider acquisition fails the prompt-return assertion. |
| V-10 `C1603_obsolete_cleanup_loses_compare_and_set_safely` | none / **1** | The before-finish pause is after the fresh reread. A separate context advances the wait to Progressed/version+1 while leaving the grant for its normal reconciler. Finish must affect zero rows and leave that grant intact; inspect before the later ordinary grant pass, using an interceptor or boundary snapshot, then assert normal reconciliation revokes it. No dirty tracker replays the old state. PC removing the state/version CAS overwrites Progressed and fails. Queue twin `C1603_queue_cleanup_loses_compare_and_set_safely` (CP-1, one result) calls ObserveTranscriptAsync at that same pause. |
| V-11 C1603_cleanup_keeps_held_input_deliverable | unselected-held-pending-channel, unselected-held-pending-scheduled, later-held-input-after-cleanup-channel, later-held-input-after-cleanup-scheduled, parked-selected-channel, parked-selected-scheduled / **6** | First four arms start from V-1's real selected X/Drop with admission below cap. Advance compatibility cursor beyond this session. Existing-input arms first expire the unredeemed grant while X is still Pending, reaching the normal ungranted Ready/rearm delay, then Drop X and enqueue Y via the real queue before cleanup; prove Y Pending/Held/unkeyed. Later-input arms first clean X with no other rows, reconcile over three expiry windows (no grant), then enqueue Y. Parked arms use real failed delivery to the configured delivery cap (cap 1 is sufficient; episode cap stays higher), with a prior transcript baseline to avoid pre-first-turn attempt refunds; never CancelParkedIfStaleAsync or a direct status-cancel write. Assert Pending/keyed/selected at cap first; cleanup detaches X, preserves Pending/attempts/verdict/body, clears selection and revalidation, rotates once and keeps count. Across three empty expiry windows, no grant or mutation repeats; then enqueue Y. Grant when due, flush through real selection and fake adapter correlated UserPrompt. Assert SAME wait ID, new action on Y, actual delivered body/receipt and no manual prompt/compatibility rewind; X remains Canceled or parked Pending as applicable, never automatically retried. PC restoring wait cancellation strands Y; PC blanket suppression strands later Y; PC retaining parked X as a receipt fails parked disposition. The original four delivery-only guards pass at base; parked-detach assertions are new failing behavior, not claimed base-green. |
| V-12 C1603_empty_queue_demand_does_not_reserve_provider | never-selected-empty, detached-empty, independent-task, attempt-cap, parked-only, future-held-only, granted-then-demand-lost-grant-expired, granted-then-demand-lost-new-wall-wave, legacy-null-session-empty, legacy-null-session-held / **10** | Exercise direct GrantReadyAsync plus ReconcileAsync. Put more than B empty/parked-only/future-held-only live queue waits before an eligible competitor; grant the competitor, proving exclusion precedes Take. Parked-only uses unselected Pending Channel/Scheduled rows at the delivery cap; across three expiry windows no reservation/count/version changes. Future-held-only includes a selected Pending row below cap as well as an unselected held row: preserve selection/receipt, grant neither before HoldUntil, then grant/deliver when due (no manual prompt). Independent task grants without queue input. Episode-at-cap cleaned wait retains count, never grants free revalidation; new demand reaches Exhausted. Demand-lost arms grant W for unselected due Y with no independent receipt; seed the named earlier OutcomeReason, real Drop Y **before expiry**, then direct GrantReadyAsync. Assert immediate own-grant release, Ready/queue-demand-lost, one version bump, unchanged action/count/due/clock/wave and no queue re-key; second empty tick is stable and a competitor is admitted when its clock allows. Enqueue Z after the cursor has passed W's session, grant/flush SAME wait, assert actual correlated UserPrompt without human input. PC route demand loss through existing RecordRevokedGrantAsync: Deferred or missing Z delivery fails. PC drop attempts/HoldUntil filters or put demand after Take: provider reservation/starvation fails; blanket filtering fails independent-task. Legacy null-SessionId arms keep consumer key `session:<N>` and a null SessionId: the empty wait is not granted, and the same wait is granted when that session has held Pending demand. Removing the in-loop demand check grants the empty legacy wait. |

Totals: **52 queue + 18 standing = 70 new results**. V-2/V-3/V-7/V-8/V-10/V-11/V-12 are
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

**Lane: Linux, PostgreSQL Integration; portable server services.** CP-1..CP-8
share S1-S2 and one isolated build. Run every named affected class in full,
including all partial files. CP-5 includes Refusal (1), Transient (2), Capacity
(2), CapacityResume (1) and the remaining 46 results: **52**, not only the old
three capacity methods. CP-4 is **6 + 18 = 24** at S3-A tip
`6564c5757dd0ec7c347fe50275f1334498f9e073`. CP-7 is **18 + 2 Recovery + 6 Remote +
2 S3 = 28**: the review/brief's 26 omitted Recovery's two tests. CP-3 now adds all
**15 WallRerouteDispatchTests results** (13 methods, two with two Arguments),
including the production LiveSession + Queued TaskId dispatcher path that must
remain independently redeemable without queue demand. Counts are a
source audit, not executed results; recheck the actual landed S3-A base and fresh
TRX, including later S3 tests if present. Never lower floors to conceal omissions.

Import this plan with the built checkpoint tool (`import --plan <this-file> --out
<ignored-manifest.yaml>`); apply the performance/timing pins, then run the
positional manifest with `--expected-source-sha <full-tip> --total-timeout 90m
--hard-wall-timeout 100m` after the R1 Debug commissioning described above. Preserve
membership. Use one run for this committed group and `wait --max-wait 45s` repeatedly
until exit is not 75, remaining responsible for the run and regular status updates.
Bootstrap any missing tool binary through `scripts/build-slot.ps1` and account for
it separately; subsequent control commands use the built binary. Every checkpoint
build/test takes its own host slot. No unleased/tool-refusal fallback. Retain all
unedited CHECKPOINT lines, exact tested SHA, roster, TRX/hash and source/build
provenance; validate with `scripts/validate-checkpoint-receipt.ps1` and run the
duration tripwire on each fresh TRX. The Final Unit lane is additionally bound by
the commissioning profile as specified in Performance budget.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Tests -> bin-c1603/` | c1603-queue | `/*/Antiphon.Tests.Application/CapacityObsoleteQueueTests/*` | V-1..V-5, V-11/V-12, finished-grant release, exempted obsolete selections, legacy null session, queue compare-and-set | 52 results, 0 failed/skipped | 52 | 17 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-2 | S1-S2 | CP-1 | c1603-standing | `/*/Antiphon.Tests.Application/CapacityObsoleteStandingTests/*` | V-6..V-10 | 18 new results, 0 failed/skipped | 18 | 9 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-3 | S1-S2 | CP-1 | c1603-existing-capacity | `/*/Antiphon.Tests.Application/(CapacityWaitOrphanSweepTests*)\|(CapacityRecoveryGrantLivenessTests*)\|(CapacityRecoveryQueueTests*)\|(CapacityRecoveryTaskTests*)\|(WallRerouteDispatchTests*)/*` | terminal owners, D-12 fairness, genuine queue delivery, retained consumers and independently redeemable LiveSession dispatcher waits | 6 + 12 + 8 + 6 + 15 = 47, each class present, 0 failed/skipped | 47 | 8 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-4 | S1-S2 | CP-1 | c1603-s3-supervision | `/*/Antiphon.Tests.Application/CapacityRecoverySupervisionTests/*` | S3-A atomic consumption/exit guards, D8 | 6 + 18 = 24, 0 failed/skipped | 24 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-5 | S1-S2 | CP-1 | c1603-standing-reservation | `/*/Antiphon.Tests.Application/StandingRestartEscalationTests/*` | full class: Refusal/Transient, Fresh/resume, acceptance/generation boundaries and guards | 52, all partials included, 0 failed/skipped | 52 | 16 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-6 | S1-S2 | CP-1 | c1603-acceptance-persistence | `/*/Antiphon.Tests.Application/(CapacityRecoveryAcceptanceTests*)\|(CapacityRecoveryPersistenceTests*)\|(CapacityRecoveryTranscriptTests*)/*` | direct grant callers, persistence, real session progress | 15 + 12 + 1 = 28, each class present, 0 failed/skipped | 28 | 5 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-7 | S1-S2 | CP-1 | c1603-admission-lifecycle | `/*/Antiphon.Tests.Application/CapacityAdmissionLifecycleTests/*` | complete lifecycle including Recovery, Remote and S3 partials | 18 + 2 + 6 + 2 = 28, 0 failed/skipped | 28 | 9 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |
| CP-8 | S1-S2 | CP-1 | c1603-compatibility-locks | `/*/Antiphon.Tests.Application/(CapacityRecoveryCompatibilityTests*)\|(CapacityRecoveryRefusalTests*)\|(CapacityRecoveryAttentionTests*)\|(CapacityLockOrderTests*)/*` | forward cursor, other consumer kinds, exhaustion, provider/owner lock order | 5 + 4 + 3 + 17 = 29, each class present, 0 failed/skipped | 29 | 6 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp |

Integration total: **70 new + 208 existing = 278**. The required RED diagnostics target 8
results (4 queue + 4 standing), separately from the green roster and its counts.
The estimated column totals **76 minutes**, including a 7-minute shared build only
in CP-1 (7 + 10 test minutes); the other rows charge test execution. Explicit row timeout pins replace
the importer's estimate-derived defaults; the table itself does not set p95.

Suffix audit at the inspected source/S3-A tip: each of the twelve class prefixes
in CP-3/CP-6/CP-8 resolves to exactly its intended class; partial files are one
class, not suffix collisions. CP-4/CP-5/CP-7 use exact classes. New exact class
names do not yet exist and are reserved here. Recheck implementation and landed
base before execution. Every OR operand is parenthesized with its own trailing
asterisk; table pipes are escaped. Require fresh TRX membership and nonzero
counts; list-tests does not establish scoped execution.

## Placement

Reread `GET /api/runner-defaults` and `GET /api/session-runners` during round 3 on
2026-10-29 at approximately 12:33 UTC.
Defaults revision 2 has no per-kind override; an eligible Linux lane is available.
Use current defaults rather than embedding a fleet location; omit `-Runner` and
`-Platform` for portable work. Checkpoints will name the Linux integration lane.
No desktop files or native Windows runs are needed for this plan.

## Risks and limits

1. **S3-A is a real prerequisite.** Running the standing predicate against a
   pre-S3 supervisor can misclassify its separately committed start. S3-A is on
   master `e65343132` before this Code task. Code must confirm atomic consume
   plus wait generation stamps on both resume and fresh arms, and keep CP-4/CP-5
   green. This plan does not amend S3's card-spawn post-commit consume window;
   card-bound sessions are excluded by ownership.
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
   should reserve a provider repeatedly after the canceled/parked selection is detached.
   Parked or future-held rows are not automatic demand; a demand-loss revoke
   leaves Ready even if the old reason was grant-expired/new-wall-wave.
7. **Cross-process selection remains a pre-existing race.** ApplyCapacityHoldAsync
   stamps SelectedMessageId without the provider lock or Version increment, and
   the semaphore is local to one server process. Conditional exact selection/key
   writes and the message row lock protect this cleanup; they do not establish
   fleet-wide serialization against a later stale tracker save during overlap.
   Preserve this limit for staged rollout; do not claim the semaphore solves it.
   A broader selector protocol belongs in separately commissioned work if observed.
8. **Resume absence can change message status.** The cleanup's conditional message
   write and row lock are mandatory (D-4), not an assumption that Canceled or
   parked Pending is immutable. A racing deliverable Pending, successful Send-now
   or late-confirmed Sent winner must be preserved; no unconditional tracker save
   may clear its linkage. Measure queue semaphore hold time across the DB
   transaction, including busy/rollback paths. This is separate from S3 Risk 8's
   cancellation producer correction below.

This plan changes provider wait lifecycle, not a session that waits for input.
There is no new session release/parking action or deadline. With parking disabled,
an input-waiting session still has no automatic release deadline (CARD-1083).

## Card and S3 Risk 8 wording to amend

The caller should replace CARD-1603's Risk 8 paragraph and its queue fix direction,
and make the same correction to S3 plan Risk 8, with this text (no generated
files under docs/cards are edited by this task):

> A capacity-selected Channel/Scheduled message can be canceled by human Drop
> (CancelAsync), schedule supersession (CancelPendingBySourceScheduleAsync), or a missing
> remote spill body (RemoteSpillCourier.MarkUndeliverable). Before redemption,
> expiry re-keys the canceled row without spending an admission; after redemption,
> SelectedMessageId is treated as an execution receipt and revalidation can repeat
> without another admission. A later successful session turn can end the wait.
> Brief-expiry cancellation concerns Delegation rows and cannot cause this case.
> Detach the canceled selection and suppress grants without deliverable demand while retaining
> the session's wait, so already-held and later Channel/Scheduled input remain
> deliverable. Also clear a selected Pending row's selection/linkage once it is
> parked at the delivery-attempt cap; preserve its Pending status and attempts,
> and do not count it or a future HoldUntil as automatic demand. If a held grant
> loses its demand, release it while leaving the wait Ready, never stranded in
> Deferred. Human-started standing waits are a separate supersede path.

The title and Risk 9 premise remain valid. Code applied this wording to the S3
plan copy and did not edit the generated card file or the live card.

## Completion and handoff

Plan Review must judge the positive-evidence predicates, null-generation fallback,
deliverable demand/parked disposition, Ready-preserving demand-loss revocation,
schedule supersession/later-input delivery, queue lock/DI lifetime, CAS and lock
ordering, independent retained consumer (including WallRerouteDispatch), and
performance exceptions. Ask a different company's model to perform that Review.
No extra user decision is required to produce this plan under D-1..D-7.
After approving Review and S3-A landing, commission Code with the verification
profile/Unit manifest and the R1 performance Debug obligation attached.

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
selected-message-canceled or selected-message-parked **selection detach** (same
unfinished wait, no grant without deliverable demand), a queue-demand-lost
**Ready** grant release, or standing-consumer-replaced **Superseded** outcome. Across at
least three configured expiry intervals the retired action must not regain a
grant; another eligible waiter advances when its provider clock permits. A new
held message may correctly grant the retained queue wait under a new action.

Do not generate production traffic or cancel a live message just for this check.
