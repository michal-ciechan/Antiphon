# CARD-1108/1124 follow-ups: Held re-stamp, sweep counters, parked-continuation guidance, and the review-reliability pins

Date: 2026-10-07. Plan task: `cd4e71ed-27c8-4c83-827d-e49ef118cb51`.
Inspected source: `9975d163e34501e0fe66bf1f8486ef1a6413b4d2` (`origin/master` at planning time;
CARD-1108 S1-S4 and CARD-1124 S1-S3 landed). The assigned branch was fast-forwarded from
`5b713f6855ee417737ff7d7e47cb340d66e3d9b8` to that SHA before any citation below was taken.
Assigned branch: `feat/card-task-cd4e71ed`; fast-forward-only, never rebased.
Status: Plan and verification design complete under the defaults in `## Decisions`; the brief
asked for the Checkpoints table, negative controls and regression classes in this artifact, so
test design is folded here and **next: code** (S1).
Platform read before this plan: `GET /api/runner-defaults` revision 2, global runner `server2`,
no kind defaults; `GET /api/session-runners` shows `desktop` (Windows, 0/2) accepting, `server2`
(Linux, 0/10) draining and not accepting, `server2-temp` (Linux, 3/10) accepting. No runner pin
and no platform pin: every change is server-side C#, test code or Markdown, and every checkpoint
row runs on the Linux lane.

## Outcome and scope

Ten Backlog cards from the CARD-1065, CARD-1108 and CARD-1124 reviews. Each was re-derived on
`9975d163e`; every one is still true there. None is a regression or a fail-open while parking is
default-off, so the bundle changes nothing that parking authorises: no release happens earlier, no
check is skipped, no setting defaults on, no migration.

| Card | Still true on master | Decision | Slice |
|---|---|---|---|
| CARD-1135 | yes (ground truth 4, 5) | small production hardening: a refused Held row is re-stamped with its reason; the stamp carries the reason | S1 |
| CARD-1137 | yes (6) | fixture fix: the seat fixture's loopback client carries one stated budget instead of the production 3 s list budget | S1 |
| CARD-1129 | yes (1, 2, 3) | small production hardening: `Released` counts this run's confirmations and a run never revisits a row; test-only 119 s arm | S2 |
| CARD-1141 | yes (10, 11) | plan amendments (items 1-2) and an owner-doc sentence with a pin (item 3) | S2, S6 |
| CARD-1103 | yes, a parking residual (14, 15) | small production hardening: attempt-scoped confirmed-park guidance; the 422 names Reply; owner sentences flip | S3 |
| CARD-1097 | yes, a parking residual (16) | item 1 small production hardening: one `park_resume_refused` warning per reason; items 2-5 stay on the card | S4 |
| CARD-1138 | yes (7, 8) | test-only pins: sampler tick at fifteen reader statements; cross-task park arm | S5 |
| CARD-1140 | yes (9) | plan amendments naming PC-1 and PC-2 as PC-184's executed guards | S5 |
| CARD-1104 | yes (12) | test-only pointer-path arm at the production chunk size; owner sentence flips | S6 |
| CARD-1106 | yes (13) | test-only: doc pins bind to the code constants | S6 |

No card closes as not needed without work. After the bundle lands, CARD-1129, CARD-1135,
CARD-1137, CARD-1138, CARD-1140, CARD-1141, CARD-1103, CARD-1104 and CARD-1106 close; CARD-1097
stays open for its item 2 (a resume-time runner identity read) and its items 3-5 are documented,
pinned, fail-closed facts that this plan recommends closing as not needed (see `### Out of scope`).

Constraints carried through every slice: parking and the reclaim sweep stay default-off and inert
when disabled; a Working session is never touched; the CARD-1065 D-3 publication gate
(`PrepareAsync` to `VerifyAsync` to the `TryReserveAsync` CAS) is not edited; every production
change is fail-closed or cost-reducing; no new per-tick statement on the desktop (counts in D-10).
No production session, card, runner budget or deployment setting was changed during this Plan; no
test or build ran except the checkpoint importer's table validation described under
`### Checkpoints`.

## Ground truth

Every citation is at `9975d163e`.

| # | Card | The card assumes | What the code does | Where |
|---|---|---|---|---|
| 1 | CARD-1129 item 1 | A later sweep recounts already-parked rows as released. | Confirmed. `released++` fires whenever `IsConfirmed(FindAttemptReleaseAsync(...))` is true after the visit; a Parked task is still `Blocked`, so the page keeps returning it, `RegisterLegacyAsync` returns the existing episode (`registered++` too) and `TryHandleTaskAsync` returns at `Parked`. The job adds `.Released` on every run. V-23 asserts `Released == 2` on the releasing run only. The ledger stamps `ConfirmedAt` from the server clock. | `server/Application/Services/TerminalRunnerSeatReleaseService.cs:147-168` (loop, `:156-160`), `:82` (Parked return), `:923` (`ConfirmedAt`); `server/Application/Services/BlockedTaskParkingService.cs:115-127` (reuse), `:130-143` (page is `Status == Blocked`); `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs:47`; `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs:141-147` |
| 2 | CARD-1129 item 2 | The once-per-run bound assumes the Blocked set does not shrink mid-run. | Confirmed. `cap` is read once before the loop; the page wraps whenever the tail after the cursor is short; nothing records the ids visited in the run. Worked example in D-2: six rows with the cursor after the second, page size 2, budget 3, two rows leaving Blocked after the first visit: six visits of four rows. | `TerminalRunnerSeatReleaseService.cs:140-147`; `BlockedTaskParkingService.cs:137-142` |
| 3 | CARD-1129 item 3 | The 119 s boundary is proven by reading only. | Confirmed. The gate is `now < due`; the test calls at the same clock and then advances 120 s. | `TerminalRunnerSeatReleaseService.cs:194`; `BlockedTaskParkReclaimTests.cs:302-314` |
| 4 | CARD-1135 item 1 | A Held park refused before `PersistIntentAsync` is not re-stamped and is prepared again on every sweep after its first backoff. | Confirmed. `HoldAsync` passes `Requested` as the expected state; the CAS is `p.State == expected`; `Allowed` has no Held to Held. Every refusal before the intent persist (`RefusalAsync`, `BindingRefusal`, `park_repository_unknown`, `park_endpoint_unknown`, `park_source_intent_changed`, and `CaptureSourceIdentityAsync`'s `RefuseAsync`) goes through `HoldAsync`. The `TryHandleTaskAsync` gate admits a Held row once `NextAttemptAt` is due. After a successful `PersistIntentAsync`, `PrepareAsync` bumps only the in-memory `Revision`; the loaded `State` stays what it was. | `server/Application/Services/TaskParkPublicationService.cs:348-355` (`HoldAsync`), `:35-42` and `:105-116` (refusals), `:118-121` (in-memory state after intent), `:289-318` (`PersistIntentAsync`); `BlockedTaskParkingService.cs:97` (CAS), `:211-220` (`Allowed`); `TerminalRunnerSeatReleaseService.cs:84-87` (gate) |
| 5 | CARD-1135 item 2 | `StampHeldAttemptAsync` keeps the stale reason code. | Confirmed. The stamp writes `NextAttemptAt` and `UpdatedAt` only; both callers hold a reason they do not pass. | `BlockedTaskParkingService.cs:164-171`; `TerminalRunnerSeatReleaseService.cs:88-106` |
| 6 | CARD-1137 | `Discovery_request_uses_runner_owned_delivery_evidence` is load-sensitive because its fixture inventory read cancels under the 56-result class run. | Confirmed, and the budget is named: the method boots six `LiveSeat` fixtures in sequence, each an in-process Kestrel app (plus a `PhoneHomeTestHost` on the phone-home arm); the first `AcquireAsync` reads inventory through the fixture's loopback `SessionRunnerHttpClient`, which is built with `new SessionRunnerSettings { BaseUrl }`, so `ListAsync` applies the production `ListTimeoutSeconds` default of 3 s through `CancelAfter` while the fixture's own `HttpClient.Timeout` says 10 s. The class carries no concurrency attribute; 12 of its 17 methods create a `LiveSeat`; no process is spawned, so `ParallelLimiter<ProcessSpawnLimit>` (limit 1) is the wrong tool. The two red runs were the HTTP arm (stack through `SessionRunnerHttpClient.ListAsync`). | `tests/Antiphon.Tests/Application/RunnerSeatOrphanSweepTests.cs:17-18`, `:344-349`; `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs:105-108`, `:275`, `:757`, `:784-818`, `:910`; `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:556-563`; `server/Application/Settings/SessionRunnerSettings.cs:13`; `tests/Antiphon.Tests/TestHelpers/ProcessSpawnLimit.cs:15` |
| 7 | CARD-1138 item 1 | No committed test pins the sampler tick's statement count. | Confirmed. `Rig.SampleAsync` builds its context from `DbOptions` with no interceptor; `TestDbFixture.CreateDbContextOptions` has no interceptor overload; the rig's tasks carry no `CardId`, so the join skips its card query and the tick is 14 reader statements. With a card it is 15: one `HostBudgets` read, the local count, three runner queries, nine join statements, one sample insert. The prune `ExecuteDeleteAsync` is a non-reader command the interceptor does not capture. | `tests/Antiphon.Tests/Application/SeatOccupancySamplerTests.cs:434-443`, `:484-486`, `:565-577`; `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs:53-58`; `tests/Antiphon.Tests/TestHelpers/TestDbFixtureLifecycle.cs:358-365`; `server/Application/Services/HostBudgetService.cs:22-26`; `server/Application/Services/SeatOccupancySampler.cs:45-48`, `:54-57`, `:68-78`, `:101`; `server/Application/Services/SeatDesktopJoin.cs:80-93`; `tests/Antiphon.Tests/TestHelpers/CountingCommandInterceptor.cs:14-27` |
| 8 | CARD-1138 item 2 | The cross-task park arm is untested. | Confirmed. `CurrentPark` filters on the owner's task id and attempt; `hasReceipt` is keyed on the latest task; the park query loads rows for latest and owner ids. The four join tests never seed an owner older than a settled latest task that carries a park. | `SeatDesktopJoin.cs:116-132`, `:171-178`, `:238-252`; `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs:18-19`, `:181-182`, `:275-276`, `:387-388` |
| 9 | CARD-1140 | PC-184's row and the CARD-1124 Mutation handoff do not name PC-1 and PC-2 as the executed guard. | Confirmed. The G-184 and PC-184 amendments point at the CARD-1124 plan without naming a control; the CARD-1124 Mutation handoff still says "the CARD-1065 plan amendment in S3 says so"; its docs row 10 still carries the dropped clause. CARD-1124 is in Review and its Mutation has not run. | `docs/superpowers/plans/2026-10-05-card-1065-blocked-task-parking-plan.md:1155`, `:1405`; `docs/superpowers/plans/2026-10-07-card-1124-occupancy-park-projection-plan.md:270`, `:408-412`; `card.ps1 get CARD-1124` (Review) |
| 10 | CARD-1141 items 1-2 | Plan V-6, D-6 and PC-11 say 2 and 3 verifies; CP-28 expects 13. | Confirmed. The committed test pins 3 without a boundary and 4 with one; `RegisterAndReserveAsync` verifies at the entry gate and after `BeforeReservation`; `RunnerSlotEndpointTests` has ten methods. The CARD-1108 plan has one commit and no amendment. | `docs/superpowers/plans/2026-10-06-card-1108-park-reclaim-follow-ups-plan.md:176-191`, `:267`, `:320`, `:379`; `BlockedTaskParkReclaimTests.cs:603-616`; `tests/Antiphon.Tests/Application/RunnerSlotEndpointTests.cs:378`, `:447`; `git log -- <plan>` (`ce1c9e80b` only) |
| 11 | CARD-1141 item 3 | The Known-limits sentence omits CARD-1129 and CARD-1135 and is not pinned. | Confirmed. The sentence names CARD-1097, CARD-1103 and CARD-1104; `BlockedTaskParkProjectionTests` has no "Known limits" phrase. | `docs/session-runtime-invariants.md:117`; `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs` (grep) |
| 12 | CARD-1104 | V-27 proves the caller receipt on the inline path only. | Confirmed. `ReviewWorld.StartAsync` passes `completionSingleWriteBytes: 86_400`, which the fixture maps to `DelegationSettings.PtySingleChunkBytes` (production default 1,024); `AssertCallerReceiptAsync` requires the UserPrompt text to contain the evidence id. The pointer path exists in production: the land-notification reconcile reads an owned pointer's retained spill. The owner doc states the limit and it is pinned. | `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs:359-385`, `:500-527`, `:592-596`; `RunnerSeatReleaseFixture.cs:134`, `:157-158`; `server/Application/Settings/DelegationSettings.cs:190`; `server/Application/Services/AgentTaskLandNotificationService.cs:199-225`; `server/Application/Services/TypedBodySpill.cs:21`, `:104`; `docs/session-runtime-invariants.md:109`; `BlockedTaskParkProjectionTests.cs:72` |
| 13 | CARD-1106 | V-15 pins literals, not code constants. | Confirmed. `C1076_remote_prep_push_contract_is_documented` pins eight literals. Constants exist for five of them: `DelegationSettings.RemotePrepPushBudgetMinutes`, `ILandingGit.RemotePrepPush`, `AgentTaskPipelineStatusService.QueueReasonRepositoryLease` and `QueueReasonRemotePrep`, `RemotePrepProgress.BehindTaskId`; `QueueReasonHostBudget` exists and `hostBudget` appears twice in `docs/orchestration-loop.md` unpinned. | `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs:28-43`; `DelegationSettings.cs:632`; `server/Application/Interfaces/ILandingGit.cs:14`; `server/Application/Services/AgentTaskPipelineStatusService.cs:33-42`; `server/Application/Services/RemoteWorkspacePreparer.cs:23` |
| 14 | CARD-1103 item 1 | `HasConfirmedPublishedParkAsync` is not scoped to the attempt and accepts Resumed. | Confirmed. The predicate filters task id, receipt, release id, state Parked/ResumePending/Resumed and a Confirmed ledger row, with no attempt term. Callers: the follow-up refusal and task detail (`BlockedContextBuilder`); both hold the `AgentTask`. A Resumed park's attempt is always one below the task's current attempt. | `server/Application/Services/AgentTaskService.cs:3272-3288`, `:495-499`, `:520`, `:2478-2481`; `server/Application/Services/AgentTaskDispatcher.cs:6380-6383` |
| 15 | CARD-1103 item 2 | The 422 for a remote pool predecessor never names Reply. | Confirmed. `RefuseRemotePoolFollowUp` runs before the Blocked branch and its message names only the fresh-task route; the order is pinned (`c1065-remote-before-blocked`) and PC-170 expects the 422. The owner doc and `docs/orchestration-loop.md` state the limit. | `AgentTaskService.cs:511`, `:3257-3270`; `BlockedTaskParkProjectionTests.cs:66`, `:92`, `:130-134`; `docs/session-runtime-invariants.md:100-101` |
| 16 | CARD-1097 | Per-tick `park_resume_refused` warnings with no dedupe; resume inspects the desktop only; lease, confirm-path and prune notes. | Confirmed for item 1: every refused tick calls `RemoteWarnAsync`, which inserts a Warning event with no lookup; no dedupe exists in the dispatcher for this detail. Items 2-5 are documented sentences, each pinned. | `AgentTaskDispatcher.cs:4679-4685`, `:6377-6396`, `:6403-6428`; `docs/session-runtime-invariants.md:91-95`; `BlockedTaskParkProjectionTests.cs:61-65` |

Also inspected: `BlockedTaskParkReclaimTests`, `BlockedTaskParkReleaseTests`,
`BlockedTaskParkResumeTests` and `BlockedTaskParkDeliveryTests` are Slow and allowlisted
(`tests/Antiphon.Tests/slow-tests-allowlist.txt:3`, `:5`, `:7`, `:9`); every new Slow method in
this plan lands in one of them, so no new class and no registry change, and the registry guard
runs once at the final SHA. The checkpoint tool's `import` verb exists
(`tools/Antiphon.Checkpoints/Program.cs:48`). `AgentTaskServiceIntegrationTests` has three plain
`[Test]` methods that assert the follow-up codes (`:1613`, `:1654`, `:1691`);
`RemotePoolFollowUpAdmissionTests` has three. Rosters at `9975d163e` are listed under
`### Checkpoints`.

## Decisions

Defaults, not questions. None needs a human call.

### D-1. A refused Held row is re-stamped through its real state, and the stamp records the reason (CARD-1135)

`TaskParkPublicationService.HoldAsync(park, reason, ...)` branches on the loaded row: when
`park.State == Held` it calls `StampHeldAttemptAsync(park.Id, park.Revision, backoff, reason, ct)`;
otherwise it calls `PersistStateAsync(park.Id, park.Revision, park.State, Held, reason, ct,
backoff)`, so Requested to Held and Published to Held stay the admitted transitions and any other
state writes nothing, as today. `StampHeldAttemptAsync` gains a `reasonCode` parameter, validated
like `PersistStateAsync`'s, and sets `ReasonCode` beside `NextAttemptAt` and `UpdatedAt`; the CAS
stays `(Id, Revision, State == Held)` and `Revision` is not bumped (the S3 review's note that a
concurrent `PersistIntentAsync` at the same revision still wins stands). The two
`TryHandleTaskAsync` callers pass `park_binding_missing` and `park_ownership_ambiguous`.
`CaptureSourceIdentityAsync`'s `RefuseAsync` uses the same `HoldAsync`, so its refusals get the
same treatment. `HeldBackoff` is unchanged: `park_workspace_reserved` still gets no backoff.

One consequence is load-bearing: after `PersistIntentAsync` returns null in `PrepareAsync` the
database row is `Requested` with `NextAttemptAt` null, so beside the existing `park.Revision++`
the in-memory `park` must be set to `State = Requested`, `ReasonCode =
"park_publication_requested"`, `HeldFromState = null`, `NextAttemptAt = null`. Without that, a
Held row re-prepared at its due time would reach the holds after admission (`park_workspace_reserved`,
the final `RefusalAsync`, the inspection result) with `State == Held` in memory, the new branch
would stamp a row that is no longer Held, and the row would stay Requested with no backoff: a
regression of CARD-1108 G-8, caught by that plan's V-5 run-3 arm (R-1 here, PC-3 below).

Rejected: (a) admitting Held to Held in `Allowed` (a transition that is not one; `HeldFromState`
would read Held); (b) re-requesting the row (Held to Requested) before preparing it (a write per
due visit even when the refusal repeats, and `PersistIntentAsync` clears the backoff by design);
(c) never re-preparing a Held row (the backoff exists to retry). Statement cost: none per tick; on
the refused due visit one update replaces one CAS that matched nothing.

### D-2. `Released` counts this run's confirmations, a run never revisits a row, and the 119 s boundary has an arm (CARD-1129)

`ReclaimLegacyAsync` reads `runStart = clock.GetUtcNow().UtcDateTime` before the loop and
increments `released` only when `IsConfirmed(release) && release.ConfirmedAt >= runStart`. The
ledger stamps `ConfirmedAt` from the same server clock
(`TerminalRunnerSeatReleaseService.cs:923`), so a row confirmed in this run counts once and a row
Parked before the run never counts again; it is still visited (its `TryHandleTaskAsync` returns at
Parked) and still counted in `Visited` and `Registered`, which the CARD-1108 plan's D-3 defines as
visits with an episode. Zero added statements: the post-visit ledger read already exists.

A `HashSet<Guid>` of ids visited in this run is kept; when a page yields an id already in it, the
run stops without committing the cursor for that id, because the cursor has wrapped past the
run's start. `Visited` counts distinct rows. With six rows A to F, the cursor after B, page size 2
and budget 3, and E and F leaving Blocked after C's visit, today's visits are C, D, A, B, C, D; with
D-2 they are C, D, A, B and the run stops. The pinned owner sentence "One run visits each eligible
Blocked row at most once" becomes unconditionally true.

Test-only: `C1108_ScheduledSweepIsGatedAndBoundedPerRun` gains `Advance(119 s)` asserting
`LegacyReclaimResult.None` and an unchanged cursor, then `Advance(1 s)` into the existing 120 s arm.

Rejected: (a) a pre-visit `FindAttemptReleaseAsync` read (one more statement per visit on an
enabled sweep; `ConfirmedAt` carries the same fact); (b) excluding Parked rows from the page (a
parks join in the page query and in `Eligible`, and a new meaning for "eligible" in the pinned
sentence; a Parked row's visit is four reads, so defer until an operator measures them); (c)
counting a park that moved to Parked during the run (park state is not an exit certificate; the
ledger row is, per CARD-1108 D-3).

### D-3. Confirmed-park guidance is attempt-scoped and the remote-pool 422 names Reply (CARD-1103)

`HasConfirmedPublishedParkAsync(AgentTask task, ct)` requires `p.TaskId == task.Id && p.Attempt ==
task.Attempt`, a receipt id, a release id, `p.State` Parked or ResumePending, and the Confirmed
ledger row. Resumed is dropped: a Resumed park's attempt is always `task.Attempt - 1`
(`AgentTaskDispatcher.cs:6380-6383`), so with the attempt term it can never match, and listing it
would name a reachable arm that is not. Both callers already hold the task.

In the follow-up branch, `confirmedPark` is computed before `RefuseRemotePoolFollowUp` and passed
in; when true, the 422 message appends: "Blocked task <short> has a confirmed published park for
its current attempt; reply to it (delegate.ps1 -Reply <short> "...") to continue that task instead
of creating a fresh one." The code `follow_up_remote_pool_unsupported` and the order before the
Blocked branch are unchanged (PC-170 and `c1065-remote-before-blocked` keep holding). One extra
query runs only on the remote-pool 422 request path, never on a tick.

Fail-closed: the only behaviour changes are fewer "The published seat was released. Reply to
continue" verdicts (and `CanAnswer` true) and a longer 422 message. Rejected: reordering the 422
after the Blocked branch (CARD-1037's restriction is independent, CARD-1065 G-170); a new error
code (clients switch on the code).

### D-4. The seat fixture's loopback client carries one stated budget (CARD-1137)

What makes the method load-sensitive: it boots six in-process Kestrel hosts in sequence and each
host's first inventory read is cold (JIT and thread-pool ramp), and that read runs under the
production `SessionRunnerSettings.ListTimeoutSeconds` default of 3 s, a budget written for the
resilience read pipeline against a remote runner. Alone, the whole method takes 10.8 s to 18.5 s
and every cold read fits; with 56 results started in one 65 ms window on a host at load 9-15 with
other build leases, one cold loopback read exceeds 3 s and `ListAsync` cancels it. The fixture
already declares a 10 s loopback budget on `HttpClient.Timeout` and on `LoopbackClientFactory`; the
3 s production default silently undercuts it.

Fix: `RunnerSeatReleaseFixture.LiveSeat` gets `private const int LoopbackBudgetSeconds = 10`,
used for `HttpClient.Timeout`, `LoopbackClientFactory` and `new SessionRunnerSettings { BaseUrl,
ListTimeoutSeconds = LoopbackBudgetSeconds, RequestTimeoutSeconds = LoopbackBudgetSeconds }`, with
a comment naming CARD-1137 and the cause. No production timeout changes. No new test: a pin on a
fixture constant cannot go red against a production line (manifest rule 4); the evidence is the
56-result row (the failing shape) at S1 and at the final SHA, and the Code task reports the
method's TRX duration from both.

Rejected: (a) `[NotInParallel]` on the method (about 20 s of serial tail per row, and host load
from other build leases remains, so the 3 s budget still binds a cold read); (b)
`ParallelLimiter<ProcessSpawnLimit>` (no process is spawned; the limiter caps spawning tests at
one); (c) six `[Arguments]` rows (changes a roster four landed plans expect and leaves the per-read
budget alone); (d) widening `SessionRunnerSettings.ListTimeoutSeconds` (a production default).

### D-5. The sampler tick is pinned at fifteen reader statements and the cross-task arm is tested (CARD-1138)

`TestDbFixture.CreateDbContextOptions(string connectionString, params IInterceptor[]
interceptors)` is an additive overload (`TestDbFixtureLifecycle.BuildOptions` gains the same);
`SeatDesktopJoinTests.CountingOptions` moves onto it. `SeatOccupancySamplerTests.Rig.CreateAsync`
takes an optional `CountingCommandInterceptor` and `SampleAsync` builds its context from the
counting options when one is present. The new sampler test adds a Project, Board, BoardColumn and
Card itself and sets the Blocked task's `CardId`, so the join reaches nine statements and the tick
is fifteen: `HostBudgets` (1), local count (1), three runner queries (3), the join (9), the sample
insert (1). The prune `ExecuteDeleteAsync` is a non-reader command the interceptor does not see, so
the pin also asserts that exactly one captured text contains `INSERT` and it names
`HostOccupancySamples`, and that none contains `UPDATE` or `DELETE`. The negative arm samples the
same rig without the card and measures fourteen, proving the pin counts the join's card statement
rather than a constant.

The new join test seeds a session whose Blocked task is older than a Succeeded task that carries a
Parked park with a receipt: `OpenTaskId` is the Blocked task, `LatestTask` the Succeeded one, `Park`
null, `PublicationReceipt` true, nine statements. Its negative arm is a second session whose Blocked
owner is also the latest task and has a Held park: `Park` set, `PublicationReceipt` false.

Rejected: seeding a card in the shared rig (changes `BoardId` facts for every sampler test);
pinning fourteen (the production join with a card is nine).

### D-6. V-27 gets a pointer-path arm at the production chunk size (CARD-1104)

`ReviewWorld.StartAsync` gains `int? completionSingleWriteBytes = 86_400`, so V-27 is unchanged.
The new Delivery method starts a world with `null` (the fixture then leaves
`DelegationSettings.PtySingleChunkBytes` at its production 1,024), runs V-27's first arm
(`busyCaller: false`) to the settled Clean/Full report, and asserts the pointer shape: among the
parent session's UserPrompt transcript entries exactly one carries `TypedBodySpill.PointerHeadline`
and `TypedBodySpill.InboxRelativePath(<completion row id>)`; no UserPrompt text contains the
evidence id (the inline assertion would fail here, which is the gap the card names); the
completion row's `RemoteSpillBody`, or the inbox file under the parent cwd when the column is
null, contains `review-evidence=<id>` and `reviewed-sha=<sha>`; the land notification reconciles
with no `queue_pointer_content_mismatch`. The production lines it guards are the typed-spill fit
(`SessionMessageQueueService.cs:373-394`) and the pointer reconcile
(`AgentTaskLandNotificationService.cs:199-225`).

Rejected: documenting inline-only as sufficient (the pointer line is the production default and
the retained spill is the only place the id lives; nothing committed proved it).

### D-7. CARD-1076 doc pins bind to the code constants where one exists (CARD-1106)

`C1076_remote_prep_push_contract_is_documented` pins `nameof(DelegationSettings.RemotePrepPushBudgetMinutes)`,
`ILandingGit.RemotePrepPush`, `AgentTaskPipelineStatusService.QueueReasonRepositoryLease`,
`QueueReasonRemotePrep` and `nameof(RemotePrepProgress.BehindTaskId)` in place of the five
literals, and adds `QueueReasonHostBudget` for `docs/orchestration-loop.md` only. The three prose
pins stay literal. A code-side rename now fails the test until the docs follow. Rejected: pinning
the docs against a reflection dump of every constant (no reader can tell which drift it reports).

### D-8. One `park_resume_refused` warning per reason (CARD-1097 item 1)

Before `RemoteWarnAsync` in the parked-resume refusal, the dispatcher reads the task's newest
Warning event (`Type == Warning`, newest `At` then `Id`) and skips the insert when its `Detail`
equals the refusal string. One indexed read per refused tick replaces one insert per refused tick;
a changed reason inserts again; the dispatch outcome stays `NotClaimed`. Items 2-5 of the card are
out of scope (below). Rejected: an in-memory dedupe (lost on restart; the event row is the durable
memory); a time-based dedupe (a repeated refusal is the same fact).

### D-9. Plan text gets dated amendment lines, never rewrites (CARD-1140, CARD-1141)

- CARD-1065 plan G-184 (`:1155`) and PC-184 (`:1405`): append "Amendment 2026-10-07 (CARD-1140):
  PC-184's executed guards are CARD-1124 PC-1 and PC-2 on
  `SeatDesktopJoinTests.C1124_Join_owner_task_is_queued_dispatched_working_or_blocked`; the
  doc-pin filter named here cannot detect the mutation."
- CARD-1124 plan Mutation handoff (`:408-412`): append "Amendment 2026-10-07 (CARD-1140): the
  Mutation evidence for PC-1 and PC-2 together closes CARD-1065 G-184/PC-184; the S3 amendment
  names this plan but not the controls, and this line replaces the sentence 'the CARD-1065 plan
  amendment in S3 says so'." Docs row 10 (`:270`): append "Amendment 2026-10-07 (CARD-1140): S3
  dropped the clause 'PC-184's guard is this plan's PC-1' as inexact; PC-1 and PC-2 together are
  the guard."
- CARD-1108 plan V-6 (`:267`), D-6 (`:176-191`) and PC-11 (`:320`): append "Amendment 2026-10-07
  (CARD-1141): the code measures 3 verifies without a boundary and 4 with one, because
  `RegisterAndReserveAsync` verifies at the entry gate and again after `BeforeReservation`; the
  committed pins are 3 and 4, and PC-11's red is `VerifyCalls` 4 against the pin 3." CP-28
  (`:379`): append "Amendment 2026-10-07 (CARD-1141): Expect exact 15 (10 + 3 + 2) and Min 15,
  since CARD-1124 added two `RunnerSlotEndpointTests` methods before the S4 base."
- The owner doc's Known-limits sentence is rewritten in S6 and pinned (`## Docs sentences and pins`).
- The orchestrator's CARD-1124 Mutation brief carries the PC-184 note (`## Mutation handoff`).

### D-10. Order, restart, statement counts, defaults

Slices run in safety order. S1 first: a Held row re-prepared on every sweep is the one item that
repeatedly touches a seat's park without bound, and the fixture budget makes every later
56-result row trustworthy. S2 bounds the sweep and its counters; S3 fixes guidance an operator
acts on against a Blocked task; S4 stops a warning flood; S5 and S6 are pins and plan text.

AppHost restart: S1, S2, S3 and S4 change server code and activate on the next AppHost restart
after landing (`scripts/restart-apphost.ps1` from the main checkout; confirm `GET /api/version`).
S5 and S6 need none. The Code tasks restart nothing. With parking off the S1 and S2 paths are
unreachable; S3's attempt scope and message and S4's dedupe are reachable only with a confirmed or
refused park, which requires parking to have been on at some point.

Per-tick statements on the desktop: dispatcher tick unchanged (S4 trades an insert for a read on
the refused-resume path only); sampler tick unchanged (S5 is test-only; the pin is fifteen with a
card, fourteen without); sweep unchanged (S2 adds none; S1 replaces one no-op CAS with one update
on a refused due visit). Request paths: S3 adds one query on the remote-pool 422 path.

Defaults: `BlockedTaskParking:Enabled`, `ReclaimExisting` and
`TerminalRunnerSeatRelease:AutomaticEnabled` stay false; no new setting; no migration. The D-3
publication gate is not edited: S1 edits `HoldAsync`, the in-memory state after
`PersistIntentAsync`, and `StampHeldAttemptAsync`; nothing in `VerifyAsync`, `AcceptAsync`,
`TryReserveAsync`, `RegisterAndReserveAsync` or `AdvanceAsync` changes.

## Implementation slices

Each slice is 30-60 minutes including its checkpoint allowance, commits its docs and pins with its
code, and ends with its rows green at that commit. S1 and S2 both edit
`TerminalRunnerSeatReleaseService.cs` and `BlockedTaskParkReclaimTests.cs`; S3 and S6 both edit
`BlockedTaskParkDeliveryTests.cs`; S4 and S6 both edit `BlockedTaskParkProjectionTests.cs` and the
owner doc. Run them in order, one Code task at a time, or one Code task for S1-S6 in order. Red
first: every new method or arm must fail against the inspected source before its production edit.

| Slice | Files and change | Tests, rows, minutes |
|---|---|---|
| S1 (D-1, D-4) | `server/Application/Services/TaskParkPublicationService.cs`: `HoldAsync` (`:348-355`) branches on `park.State`; `PrepareAsync` (`:118-121`) sets the in-memory Requested state after a successful intent. `server/Application/Services/BlockedTaskParkingService.cs`: `StampHeldAttemptAsync` (`:164-171`) takes and writes `reasonCode`. `server/Application/Services/TerminalRunnerSeatReleaseService.cs:95`, `:105`: pass the reason. `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs:816-818`, `:910`: `LoopbackBudgetSeconds` with the CARD-1137 comment. Docs sentence 1 and its pin. | New `BlockedTaskParkReclaimTests.C1135_HeldRefusalsRestampAndRecordTheirReason` (V-1, V-2). CP-1..CP-5. 30 author + 29 check = 59 min. |
| S2 (D-2, D-9 CARD-1141 items 1-2) | `TerminalRunnerSeatReleaseService.cs:131-176`: `runStart`, `ConfirmedAt >= runStart`, the per-run `seen` set and stop. CARD-1108 plan amendments (V-6, D-6, PC-11, CP-28). Docs sentence 2 and its pin. | New `BlockedTaskParkReclaimTests.C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce` (V-3, V-4); the 119 s arm in `C1108_ScheduledSweepIsGatedAndBoundedPerRun` (V-5). CP-6..CP-11. 32 + 23 = 55 min. |
| S3 (D-3) | `server/Application/Services/AgentTaskService.cs:3272-3288` (signature and predicate), `:505-530` (compute `confirmedPark` first; pass it to `RefuseRemotePoolFollowUp`), `:2478-2481` (caller), `:3257-3270` (message). Docs sentences 3, 4, 5 and pins. | New `BlockedTaskParkDeliveryTests.C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply` (V-6, V-7). CP-12..CP-15. 36 + 17 = 53 min. |
| S4 (D-8) | `server/Application/Services/AgentTaskDispatcher.cs:4679-4685`: newest-Warning read before `RemoteWarnAsync`. Docs sentence 6 and pin. | New `BlockedTaskParkResumeTests.C1097_RefusedParkedResumeWarnsOncePerReason` (V-8). CP-16..CP-19. 26 + 17 = 43 min. |
| S5 (D-5, D-9 CARD-1140) | `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`, `TestDbFixtureLifecycle.cs`: interceptor overload. `tests/Antiphon.Tests/Application/SeatOccupancySamplerTests.cs`: rig seam, new method. `tests/Antiphon.Tests/Application/SeatDesktopJoinTests.cs`: `CountingOptions` onto the overload, new method. CARD-1065 and CARD-1124 plan amendments. | New `SeatOccupancySamplerTests.C1138_Sampler_tick_is_fifteen_reader_statements_per_runner_host` (V-9), `SeatDesktopJoinTests.C1138_Join_park_follows_the_owner_and_receipt_follows_the_latest_task` (V-10). CP-20..CP-23. 34 + 19 = 53 min. |
| S6 (D-6, D-7, D-9 CARD-1141 item 3) | `tests/Antiphon.Tests/Application/BlockedTaskParkDeliveryTests.cs`: `ReviewWorld.StartAsync` parameter, new method. `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs:28-43`: bound pins and the `hostBudget` pin. Docs sentences 7 and 8; pin flip and new pin in `BlockedTaskParkProjectionTests.cs`. | New `BlockedTaskParkDeliveryTests.C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize` (V-11); V-12, V-13. CP-24..CP-26, then the final set CP-27..CP-34 at the last commit. 32 + 7 = 39 min, plus 43 min final check time. |

Scope for the Code tasks: `server/Application/Services/TaskParkPublicationService.cs,
server/Application/Services/BlockedTaskParkingService.cs,
server/Application/Services/TerminalRunnerSeatReleaseService.cs,
server/Application/Services/AgentTaskService.cs, server/Application/Services/AgentTaskDispatcher.cs,
tests/Antiphon.Tests/**, docs/**`. No `antiphon.areas.json` change; no migration; no client change;
no `AGENTS.md` change (the safety core already says parking defaults off and never stops a Working
session).

## Docs sentences and pins

"Old" is the text at `9975d163e`; "Pin" names the label in `BlockedTaskParkProjectionTests`
(`Require` is a case-sensitive `ShouldContain` on the real file) and whether it is flipped or new.

| # | Slice | File:line | Old | New | Pin |
|---|---|---|---|---|---|
| 1 | S1 | `docs/session-runtime-invariants.md:116` (new sentence after the Held-backoff sentence) | (none) | A refusal on a Held row re-stamps NextAttemptAt from the same backoff and records the refusal reason, so a Held row is never prepared on consecutive sweeps while its backoff runs (CARD-1135). | new `c1135-held-restamp` |
| 2 | S2 | `docs/session-runtime-invariants.md:114` (new sentence after the released-total sentence) | (none; the existing sentence stays) | A sweep's Released counts only ledger rows confirmed during that run; a row Parked before the run is visited and counted in Visited and Registered but not in Released, and a run stops at the first row it has already visited (CARD-1129). | new `c1129-released-this-run`; `c1065-reclaim-count` and `c1065-reclaim-page` unchanged |
| 3 | S3 | `docs/session-runtime-invariants.md:100` | The 422 follow_up_remote_pool_unsupported fires before the Blocked branch, so a confirmed park on a remote pool agent does not hear Reply. | The 422 follow_up_remote_pool_unsupported fires before the Blocked branch and, when the Blocked task has a confirmed published park for its current attempt, names that task and Reply (CARD-1103). | `c1065-remote-422` flipped; new `c1103-no-silent-422` = `ShouldNotContain("does not hear Reply")` |
| 4 | S3 | `docs/session-runtime-invariants.md:101` | HasConfirmedPublishedParkAsync is not scoped to the current attempt and accepts Resumed. | HasConfirmedPublishedParkAsync is scoped to the task's current attempt and accepts Parked or ResumePending only (CARD-1103). | `c1065-park-identity` flipped |
| 5 | S3 | `docs/orchestration-loop.md` (the sentence `c1065-loop-422` pins) | (same as old 3) | (same as new 3) | `c1065-loop-422` flipped; new `c1103-loop-no-silent-422` = `ShouldNotContain("does not hear Reply")` |
| 6 | S4 | `docs/session-runtime-invariants.md:92` | park_resume_refused is written on each refused dispatch tick with no dedupe. | park_resume_refused is written once per refusal reason; a tick that refuses again for the same reason adds no event, and a changed reason is written again (CARD-1097). | `c1065-resume-warn` flipped |
| 7 | S6 | `docs/session-runtime-invariants.md:109` | The caller UserPrompt receipt is proven on the inline path only. The fixture sets completionSingleWriteBytes to 86400 while production PtySingleChunkBytes stays 1024, so a spilled completion note's transcript line is the pointer. | The caller UserPrompt receipt is proven on both paths: inline at completionSingleWriteBytes 86400, and at the production PtySingleChunkBytes 1024, where the transcript line is the pointer and the retained spill carries review-evidence and reviewed-sha (CARD-1104). | `c1065-v27-inline` flipped |
| 8 | S6 | `docs/session-runtime-invariants.md:117` | Known limits stay on CARD-1097, CARD-1103, and CARD-1104. | Known limits stay on CARD-1097 items 2-5 (resume reads the desktop checkout, the inspection lease window, confirm-path bookkeeping, and whole-repository prune); CARD-1103, CARD-1104, CARD-1129 and CARD-1135 are closed by the CARD-1108/1124 follow-up plan. | new `c1141-known-limits` |
| 9 | S2, S5 | CARD-1108, CARD-1065 and CARD-1124 plans | (see D-9) | dated amendment lines | not pinned |

## Verification design

### Inspection

| Read | Why |
|---|---|
| `TaskParkPublicationService.cs:27-141` (capture, prepare, every `HoldAsync` call), `:286-318` (`PersistIntentAsync`), `:348-355`; `BlockedTaskParkingService.cs:72-110` (`PersistStateAsync`), `:164-180` (stamp, backoff), `:211-220` (`Allowed`) | Every S1 production line, the CAS the fix must match, and the intent persist whose in-memory consequence D-1 names. |
| `TerminalRunnerSeatReleaseService.cs:68-125` (`TryHandleTaskAsync`), `:131-210` (sweep and gate), `:513-531` (ledger read, `IsConfirmed`), `:900-930` (`ConfirmedAt` stamp) | S1's stamp callers; every S2 line; the clock the counter compares against. |
| `BlockedTaskParkingService.cs:115-163` (register, page, cursor) | The wrap D-2 bounds. |
| `AgentTaskService.cs:488-560`, `:2470-2485`, `:3257-3290`; `AgentTaskDispatcher.cs:6377-6396` | S3 lines, both callers, the Resumed-attempt fact. |
| `AgentTaskDispatcher.cs:4670-4690`, `:6403-6428` | S4 lines. |
| `SeatOccupancySampler.cs:30-160`; `SeatDesktopJoin.cs` (entire); `HostBudgetService.cs:22-26`; `CountingCommandInterceptor.cs` | The statements the S5 pin counts and the park pick the arm exercises. |
| `AgentTaskLandNotificationService.cs:150-240`; `SessionMessageQueueService.cs:360-400`; `TypedBodySpill.cs:15-110` | The pointer path the S6 arm proves. |
| `AgentTaskPipelineStatusService.cs:30-45`; `ILandingGit.cs:10-16`; `RemoteWorkspacePreparer.cs:20-25`; `DelegationSettings.cs:625-640` | The constants S6 binds. |
| Tests: `BlockedTaskParkReclaimTests.cs` (entire), `RunnerSeatReleaseFixture.cs:86-135`, `:160-232`, `:296-390`, `:630-830`, `:900-915`, `:1059-1098`; `BlockedTaskParkDeliveryTests.cs:359-460`, `:495-530`, `:576-660`; `BlockedTaskParkResumeTests.cs:251-330`; `SeatOccupancySamplerTests.cs:323-460`; `SeatDesktopJoinTests.cs:275-430`; `BlockedTaskParkProjectionTests.cs` (entire); `RunnerBranchContractDocumentationTests.cs:28-43`; `RunnerSeatOrphanSweepTests.cs:344-389` | Fixture seams, helpers, the arms to extend and the pins to flip. |

### Proves it works now

Every V names its negative-control arm; a V is not done while any arm is missing.

| V | Behaviour | Test (class-qualified) |
|---|---|---|
| V-1 | Held refusal re-stamps (D-1). Fixture with parking, reclaim and backoff 600: a dirty row is Held `park_dirty` after run 1 (`NextAttemptAt == now + 600`, revision r1, `UpdatedAt` u1). Seed a Working "squatter" task whose `WorktreePath` equals the dirty row's path (`RefusalAsync` reports `park_other_writer` before the intent persist). Advance 600 s, run 2: the row is `Held`, `ReasonCode == park_other_writer`, `NextAttemptAt == now + 600`, `UpdatedAt == now`, revision still r1 (a stamp, not a transition), `HeldFromState` unchanged. Advance 1 s, run 3: `UpdatedAt` unchanged and the Git `status` visit count unchanged (the gate held; nothing was prepared). Negative arm: remove the squatter, advance 600 s, run 4: the row is re-prepared and holds `park_dirty` again with a bumped revision. The Working seat stays Running throughout. | `BlockedTaskParkReclaimTests.C1135_HeldRefusalsRestampAndRecordTheirReason` |
| V-2 | The stamp carries the reason (D-1). Same fixture: a second Held `park_dirty` row whose `ReportDigest` the test nulls before its due visit; after the due run `ReasonCode == park_binding_missing`, `NextAttemptAt == now + 600`, revision unchanged. Negative arm: a reserved-race row (`ReclaimReservationGate.RefuseTaskId`) is Held `park_workspace_reserved` with `NextAttemptAt` null and is re-requested on the next run (CARD-1108 G-9 unchanged). | `BlockedTaskParkReclaimTests.C1135_HeldRefusalsRestampAndRecordTheirReason` |
| V-3 | Released counts this run only (D-2). Two legacy rows made releasable (the `ReadyLegacyReleaseAsync` shape: ancient block, qualified observation, clock past the window) and one Held dirty row. Run 1: `Released == 2 == confirmed ledger rows`. Advance 120 s, run 2 on the same rows (both Parked and still Blocked): `Visited == 3`, `Registered == 3`, `Released == 0`, ledger still 2 confirmed, no new version-2 request, no force command. Negative arm: a third legacy row made releasable only before run 2: run 2 reports `Released == 1`. | `BlockedTaskParkReclaimTests.C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce` |
| V-4 | No revisit (D-2). Six Blocked rows; prime the cursor with `ReclaimResultAsync(2, 1)`; then `ReclaimResultAsync(2, 3)` with a boundary that, on the first `ReclaimList:` callback of the run, flips the two highest-id rows to Succeeded through the database: `Visited == 4`, `Eligible == 6`, `Cap == 6`, every `ReclaimList:` id of the run is distinct, and the cursor's `AfterTaskId` is the last distinct row. Negative arm: the same shape without the mid-run flip visits six distinct rows. | `BlockedTaskParkReclaimTests.C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce` |
| V-5 | 119 s boundary (D-2). After the same-clock second call: `Advance(119 s)` returns `LegacyReclaimResult.None` with the cursor unchanged; `Advance(1 s)` enters the existing third arm (`Visited == 5`). | `BlockedTaskParkReclaimTests.C1108_ScheduledSweepIsGatedAndBoundedPerRun` |
| V-6 | Attempt scope (D-3). A parked Blocked task, attempt 1, confirmed release (the Delivery fixture's publish, release and `StampAsync` shape): task detail reports the confirmed park (`CanAnswer`) and a follow-up Create onto that agent is 409 `follow_up_agent_blocked` saying "The published seat was released". Move the task to attempt 2 Blocked with no park of its own (new token, new Blocked event, session not live): detail reports no confirmed park and the 409 names cancel and re-send, not the released seat. Negative arm: give attempt 2 its own confirmed park: detail and the 409 read released again. | `BlockedTaskParkDeliveryTests.C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply` |
| V-7 | The 422 names Reply (D-3). Same test, remote pool predecessor (`RunnerId` set, pool delegate): with a current-attempt confirmed park the follow-up Create is 422 `follow_up_remote_pool_unsupported` whose message contains `-Reply <short>` and the Blocked task id. Negative arm: without a confirmed park the 422 message carries no `-Reply`. The order pin `c1065-remote-before-blocked` keeps holding. | `BlockedTaskParkDeliveryTests.C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply` |
| V-8 | One warning per reason (D-8). A parked continuation whose dispatch the dispatcher refuses with `park_resume_refused:park_dirty` (the S7 dirty-desktop shape driven through `DispatchAsync`): three ticks produce exactly one Warning event with that detail and the task stays Blocked. Change the refusal (clean the dirt and move the mirror tip, or the fixture's other dispatcher refusal) : one more event with the new reason. Clear the refusal: the dispatch claims. Negative arm: two different reasons leave two events, so the dedupe is per reason, not per task. | `BlockedTaskParkResumeTests.C1097_RefusedParkedResumeWarnsOncePerReason` |
| V-9 | Sampler statement pin (D-5). Rig with a card-bearing Blocked task and the counting context: one `SampleOnceAsync` captures exactly 15 commands; exactly one contains `INSERT` and it names `HostOccupancySamples`; none contains `UPDATE` or `DELETE`; two sample rows exist. Negative arm: the rig without the card captures 14. | `SeatOccupancySamplerTests.C1138_Sampler_tick_is_fifteen_reader_statements_per_runner_host` |
| V-10 | Cross-task park (D-5). Session A: Blocked task (older, attempt 1, no park) and Succeeded task (newer, attempt 2, Parked park with receipt), with a card: `OpenTaskId` = Blocked, `LatestTask.Id` = Succeeded, `Park` null, `PublicationReceipt` true; nine statements. Negative arm, session B: Blocked owner is the latest task and carries a Held `park_dirty` park: `Park` set, `PublicationReceipt` false. | `SeatDesktopJoinTests.C1138_Join_park_follows_the_owner_and_receipt_follows_the_latest_task` |
| V-11 | Pointer receipt (D-6). World at the production chunk size, V-27's first arm to the settled report: exactly one parent UserPrompt entry carries `TypedBodySpill.PointerHeadline` and `TypedBodySpill.InboxRelativePath(<completion row id>)`; no UserPrompt text contains the evidence id; the completion row's `RemoteSpillBody` (or the inbox file under the parent cwd) contains `review-evidence=<id>` and `reviewed-sha=<sha>`; the land notification reconciles with no `queue_pointer_content_mismatch`. Negative arm: V-27's inline arm still finds the id in the UserPrompt text at 86,400 bytes (unchanged test). | `BlockedTaskParkDeliveryTests.C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize`; `BlockedTaskParkDeliveryTests.C1065_ParkedReviewReplyBindsFreshEvidence` |
| V-12 | Bound pins (D-7). The five bound pins and the `hostBudget` pin hold against the real docs. Red-first is PC-13: a code-side rename fails the docs test. | `RunnerBranchContractDocumentationTests.C1076_remote_prep_push_contract_is_documented` |
| V-13 | Doc pins (D-9, docs rows 1-8). The two new and four flipped `Require` pins hold, the two `ShouldNotContain` pins hold, and the Known-limits sentence is pinned. | `BlockedTaskParkProjectionTests.C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers`; `BlockedTaskParkProjectionTests.C1065_OccupancyTracksProcessesNotBlockedStatus` |

### Guards the regression

| R | Negative control | Test |
|---|---|---|
| R-1 | CARD-1108 V-1..V-6 and CARD-1065 V-23/V-24: the Held backoff's re-request path after an intent persist (G-8 run 3, the D-1 in-memory consequence), the gated sweep, the job count, the hook, the server-anchored window, the single verify. | whole `BlockedTaskParkReclaimTests` |
| R-2 | The seat-release ledger, discovery, reservation, every list/reserve/send invalidation, and the pool-release sweep; the CARD-1137 shape (56 results in one host). | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` |
| R-3 | Fast-path park release, sync debt, resume and continuation delivery; the reply-before-reserve hold (`HoldUnreservedParkAsync`) still takes its backoff. | whole `BlockedTaskParkReleaseTests`, `BlockedTaskSyncRecoveryTests`, `BlockedTaskParkResumeTests`, `BlockedTaskParkDeliveryTests` |
| R-4 | Publication policy, identity capture and the CAS. | whole `TaskParkPublicationTests`, `TaskParkRunnerIdentityTests` |
| R-5 | Every CARD-1065, CARD-1108 and CARD-1124 doc sentence still holds. | whole `BlockedTaskParkProjectionTests` |
| R-6 | Sweep lifetime, budget and registration. | whole `DispatcherSweepLifetimeTests`, `DispatcherSweepLifetimeRegistrationTests` |
| R-7 | The job finishes pending slot intents and returns their count; slot rules and the slots route. | whole `RunnerSlotEndpointTests`, `PhoneHomeDeferredKillTests`, `RunnerSlotRulesTests` |
| R-8 | The seat projection family (join, sampler, attention, projection) and every context created through the extended `TestDbFixture` overload. | whole `SeatDesktopJoinTests`, `SeatOccupancySamplerTests`, `SeatOccupancyAttentionTests`, `SeatOccupancyProjectionTests` |
| R-9 | Follow-up admission codes and messages outside the park fixtures; the CARD-1076 doc contract. | whole `RemotePoolFollowUpAdmissionTests`; `AgentTaskServiceIntegrationTests` methods `a_follow_up_on_a_prior_task_that_is_itself_blocked_is_refused`, `a_follow_up_onto_an_agent_parked_on_a_blocked_task_is_refused`, `names_cancel_when_the_blocked_tasks_session_is_dead`; whole `RunnerBranchContractDocumentationTests` |

### Guard inventory

| G | Decision: guard | PC |
|---|---|---|
| G-1 | D-1: a refusal on a Held row re-stamps `NextAttemptAt` and records the reason | PC-1 |
| G-2 | D-1: `StampHeldAttemptAsync` writes the reason | PC-2 |
| G-3 | D-1: the in-memory park reads Requested after a successful intent persist | PC-3 |
| G-4 | D-2: `Released` counts only confirmations during the run | PC-4 |
| G-5 | D-2: a run stops at the first repeated row | PC-5 |
| G-6 | D-2: the sweep is gated for the whole interval, including 119 s | PC-6 |
| G-7 | D-3: confirmed-park guidance is scoped to the current attempt | PC-7 |
| G-8 | D-3: the remote-pool 422 names Reply when a current-attempt park is confirmed | PC-8 |
| G-9 | D-8: a repeated refusal reason adds no Warning event | PC-9 |
| G-10 | D-5: the sampler tick issues fifteen reader statements per runner host, never one more | PC-10 |
| G-11 | D-5: `Park` follows the owner, never a settled latest task | PC-11 |
| G-12 | D-6: the retained spill of a pointer completion note carries the review evidence | PC-12 |
| G-13 | D-7: a code-side rename of a pinned constant fails the docs test | PC-13 |
| G-14 | D-9: the retired "does not hear Reply" and the old Known-limits sentence stay absent | PC-14 |

### Positive controls

Mutation runs break/red/restore/green after land; Code runs ordinary V/R; Review judges guard
independence. Each row is one compiling production defect applied singly with every other fact
valid. Use the exact method filter, never the class. Zero executions, a build or fixture failure, or
a timeout is not red. Restore source and rebuild before the green run. PC-1/PC-2/PC-3 share
`TaskParkPublicationService.cs`/`BlockedTaskParkingService.cs`, PC-4/PC-5/PC-6 share
`TerminalRunnerSeatReleaseService.cs`, and PC-7/PC-8 share `AgentTaskService.cs`, so each of those
groups runs sequentially, never batched.

| PC | Break guard by compiling defect | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: `HoldAsync` passes `Requested` as the expected state for a Held row (the pre-S1 line). | `/*/*/BlockedTaskParkReclaimTests/C1135_HeldRefusalsRestampAndRecordTheirReason` | `G-1`: run 2 leaves `ReasonCode` `park_dirty` and `NextAttemptAt` in the past. |
| PC-2 | G-2: `StampHeldAttemptAsync` ignores its `reasonCode`. | `/*/*/BlockedTaskParkReclaimTests/C1135_HeldRefusalsRestampAndRecordTheirReason` | `G-2`: the nulled-digest row keeps `park_dirty` instead of `park_binding_missing`. |
| PC-3 | G-3: `PrepareAsync` omits `park.State = Requested` after the intent persist. | `/*/*/BlockedTaskParkReclaimTests/C1108_HeldEpisodesBackOffUntilNextAttempt` | `G-8` (CARD-1108): run 3's dirty row reads `park_publication_requested` with `NextAttemptAt` null, not `park_dirty` with a fresh stamp. |
| PC-4 | G-4: `released++` on `IsConfirmed` alone, without the `ConfirmedAt >= runStart` term. | `/*/*/BlockedTaskParkReclaimTests/C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce` | `G-4`: run 2 reports `Released == 2`, expected 0. |
| PC-5 | G-5: the per-run `seen` set is dropped. | `/*/*/BlockedTaskParkReclaimTests/C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce` | `G-5`: `Visited == 6` with a repeated `ReclaimList:` id, expected 4 distinct. |
| PC-6 | G-6: `ReclaimScheduledAsync` gates on `now.AddSeconds(2) < due` (opens two seconds early). | `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun` | `G-3` (CARD-1108): the 119 s call reports `Visited == 5`, expected `None`. |
| PC-7 | G-7: the attempt term is dropped from `HasConfirmedPublishedParkAsync`. | `/*/*/BlockedTaskParkDeliveryTests/C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply` | `G-7`: the attempt-2 arm reads a confirmed park and the 409 says the published seat was released. |
| PC-8 | G-8: the 422 message is built without the Reply clause. | `/*/*/BlockedTaskParkDeliveryTests/C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply` | `G-8`: the 422 message lacks `-Reply`. |
| PC-9 | G-9: the newest-Warning read is skipped before `RemoteWarnAsync`. | `/*/*/BlockedTaskParkResumeTests/C1097_RefusedParkedResumeWarnsOncePerReason` | `G-9`: three Warning events, expected one. |
| PC-10 | G-10: one extra `CountAsync` in `SampleRunnerAsync`. | `/*/*/SeatOccupancySamplerTests/C1138_Sampler_tick_is_fifteen_reader_statements_per_runner_host` | `G-10`: 16 commands, expected 15. |
| PC-11 | G-11: `CurrentPark` drops `park.TaskId != owner.Id`. | `/*/*/SeatDesktopJoinTests/C1138_Join_park_follows_the_owner_and_receipt_follows_the_latest_task` | `G-11`: session A's `Park` is the Succeeded task's park, expected null. |
| PC-12 | G-12: the typed-spill fit stages the spill body truncated before the note header (`SessionMessageQueueService.cs:373-394`). | `/*/*/BlockedTaskParkDeliveryTests/C1104_ParkedReviewReplyReceiptSpillsToPointerAtProductionChunkSize` | `G-12`: the retained spill lacks `review-evidence=`. The inline V-27 arm stays green, which is the gap the card named. |
| PC-13 | G-13: `AgentTaskPipelineStatusService.QueueReasonRemotePrep` becomes `"remotePrep2"`. | `/*/*/RunnerBranchContractDocumentationTests/C1076_remote_prep_push_contract_is_documented` | `G-13`: both docs lack `remotePrep2`. |
| PC-14 | G-14: restore "does not hear Reply" at `docs/session-runtime-invariants.md:100` and the old Known-limits sentence at `:117`. | `/*/*/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers` | `G-14`: `c1103-no-silent-422` and `c1141-known-limits` fail. |

### Out of scope

- CARD-1097 items 2-5. Item 2 (a resume-time runner identity read through operation 37) is a new
  RPC flow and stays on the card. Items 3-5 (the inspection lease ends before the dispatch claim;
  confirm-path bookkeeping differs by ordering as legacy did; `git worktree prune` is
  repository-wide by Git's nature) are documented, pinned (`c1065-inspection-lease`,
  `c1065-prune-repo`), fail-closed and have no path to a wrong release; this plan recommends
  closing them as not needed when CARD-1097 is re-scoped to item 2.
- Excluding Parked rows from the reclaim page (D-2 rejected b); any change to `VerifyAsync`,
  `AcceptAsync`, `TryReserveAsync`, `RegisterAndReserveAsync` or `AdvanceAsync`.
- Enabling parking, the reclaim sweep or automatic release; a client change; a migration.
- Running the CARD-1108 or CARD-1124 Mutation: both stay post-land obligations of their own cards;
  this plan only amends their text (D-9).
- Windows checkpoint rows: no runner or native path changes.

### Checkpoints

Exactly one isolated build and one filter per row; reuse rows name the build row of the same
`After` group. Counts are TUnit executed results. Rosters at `9975d163e`: `BlockedTaskParkReclaimTests`
8, `TerminalRunnerSeatReleaseTests` 39 (29 methods, 14 `[Arguments]` rows on 4 of them),
`RunnerSeatOrphanSweepTests` 17, `BlockedTaskParkReleaseTests` 3, `BlockedTaskSyncRecoveryTests` 2,
`BlockedTaskParkResumeTests` 3, `BlockedTaskParkDeliveryTests` 4, `TaskParkPublicationTests` 3,
`TaskParkRunnerIdentityTests` 2, `BlockedTaskParkProjectionTests` 2, `DispatcherSweepLifetimeTests`
6 (4 methods, 3 argument rows), `DispatcherSweepLifetimeRegistrationTests` 1, `RunnerSlotEndpointTests`
10, `PhoneHomeDeferredKillTests` 3, `RunnerSlotRulesTests` 2, `SeatDesktopJoinTests` 4,
`SeatOccupancySamplerTests` 10, `SeatOccupancyAttentionTests` 10, `SeatOccupancyProjectionTests` 15
(4 methods, 13 argument rows), `RemotePoolFollowUpAdmissionTests` 3, `RunnerBranchContractDocumentationTests`
5, `TestClassificationGuardTests` 1, `SlowTestTripwireTests` 2. S1 adds one `BlockedTaskParkReclaimTests`
method (9), S2 one more (10); S3 adds one `BlockedTaskParkDeliveryTests` method (5), S6 one more (6);
S4 adds one `BlockedTaskParkResumeTests` method (4); S5 adds one method each to `SeatDesktopJoinTests`
(5) and `SeatOccupancySamplerTests` (11). No new method carries `[Arguments]`. Confirm the TRX roster
equals the expected set, not merely at least `Min`. Serial is true for every row that touches
PostgreSQL or Git; the doc-pin rows run beside each other. The table was validated at planning
time with the checkpoint importer (`import --plan`, tool built through `scripts/build-slot.ps1` to
`bin-c1108f-driver/`, output deleted afterwards): 35 rows imported, exit 0, no tests run.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1108f-cp1/` | held-s1 | `/*/*/BlockedTaskParkReclaimTests/C1135_*` | V-1, V-2 | exact 1 result, 0 failed/skipped | 1 | 6 | true |
| CP-2 | S1 | CP-1 | reclaim-s1 | `/*/*/BlockedTaskParkReclaimTests/*` | R-1 | exact 9 results (8 + 1 new), 0 failed/skipped | 9 | 7 | true |
| CP-3 | S1 | CP-1 | seat-s1 | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | D-4, R-2 | exact 56 results (39 + 17), 0 failed/skipped | 56 | 9 | true |
| CP-4 | S1 | CP-1 | publication-s1 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results (3 + 2 + 2), 0 failed/skipped | 7 | 2 | true |
| CP-5 | S1 | CP-1 | park-s1 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 12 results (3 + 2 + 3 + 4), 0 failed/skipped | 12 | 5 | true |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1108f-cp6/` | counters-s2 | `/*/*/BlockedTaskParkReclaimTests/C1129_*` | V-3, V-4 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-7 | S2 | CP-6 | gate-s2 | `/*/*/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun*` | V-5 | exact 1 result, 0 failed/skipped | 1 | 3 | true |
| CP-8 | S2 | CP-6 | reclaim-s2 | `/*/*/BlockedTaskParkReclaimTests/*` | R-1 | exact 10 results (9 + 1 new), 0 failed/skipped | 10 | 7 | true |
| CP-9 | S2 | CP-6 | job-s2 | `/*/*/(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(RunnerSlotRulesTests*)/*` | R-7 | exact 15 results (10 + 3 + 2), 0 failed/skipped | 15 | 3 | true |
| CP-10 | S2 | CP-6 | publication-s2 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | true |
| CP-11 | S2 | CP-6 | lifetime-s2 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results (6 + 1), 0 failed/skipped | 7 | 3 | true |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1108f-cp12/` | guidance-s3 | `/*/*/BlockedTaskParkDeliveryTests/C1103_*` | V-6, V-7 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-13 | S3 | CP-12 | delivery-s3 | `/*/*/BlockedTaskParkDeliveryTests/*` | R-3 | exact 5 results (4 + 1 new), 0 failed/skipped | 5 | 5 | true |
| CP-14 | S3 | CP-12 | publication-s3 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | true |
| CP-15 | S3 | CP-12 | followup-s3 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(RemotePoolFollowUpAdmissionTests*)/*` | R-3, R-9 | exact 11 results (3 + 2 + 3 + 3), 0 failed/skipped | 11 | 5 | true |
| CP-16 | S4 | `tests/Antiphon.Tests -> bin-c1108f-cp16/` | warn-s4 | `/*/*/BlockedTaskParkResumeTests/C1097_*` | V-8 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-17 | S4 | CP-16 | park-s4 | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-3 | exact 14 results (3 + 2 + 4 + 5), 0 failed/skipped | 14 | 7 | true |
| CP-18 | S4 | CP-16 | publication-s4 | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)/*` | V-13, R-4, R-5 | exact 7 results, 0 failed/skipped | 7 | 2 | true |
| CP-19 | S4 | CP-16 | lifetime-s4 | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-20 | S5 | `tests/Antiphon.Tests -> bin-c1108f-cp20/` | sampler-s5 | `/*/*/SeatOccupancySamplerTests/C1138_*` | V-9 | exact 1 result, 0 failed/skipped | 1 | 4 | true |
| CP-21 | S5 | CP-20 | join-s5 | `/*/*/SeatDesktopJoinTests/C1138_*` | V-10 | exact 1 result, 0 failed/skipped | 1 | 2 | true |
| CP-22 | S5 | CP-20 | seat-family-s5 | `/*/*/(SeatDesktopJoinTests*)\|(SeatOccupancySamplerTests*)\|(SeatOccupancyAttentionTests*)\|(SeatOccupancyProjectionTests*)\|(RunnerSlotRulesTests*)/*` | R-8 | exact 43 results (5 + 11 + 10 + 15 + 2), 0 failed/skipped | 43 | 8 | true |
| CP-23 | S5 | CP-20 | slots-s5 | `/*/*/RunnerSlotEndpointTests/*` | R-7 | exact 10 results, 0 failed/skipped | 10 | 5 | true |
| CP-24 | S6 | `tests/Antiphon.Tests -> bin-c1108f-cp24/` | pointer-s6 | `/*/*/BlockedTaskParkDeliveryTests/C1104_*` | V-11 | exact 1 result, 0 failed/skipped | 1 | 5 | true |
| CP-25 | S6 | CP-24 | contract-s6 | `/*/*/RunnerBranchContractDocumentationTests/*` | V-12, R-9 | exact 5 results, 0 failed/skipped | 5 | 1 | false |
| CP-26 | S6 | CP-24 | pins-s6 | `/*/*/BlockedTaskParkProjectionTests/*` | V-13, R-5 | exact 2 results, 0 failed/skipped | 2 | 1 | false |
| CP-27 | all | `tests/Antiphon.Tests -> bin-c1108f-final/` | reclaim-final | `/*/*/BlockedTaskParkReclaimTests/*` | V-1..V-5, R-1 | exact 10 results, 0 failed/skipped | 10 | 7 | true |
| CP-28 | all | CP-27 | seat-final | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(RunnerSeatOrphanSweepTests*)/*` | D-4, R-2 | exact 56 results, 0 failed/skipped | 56 | 9 | true |
| CP-29 | all | CP-27 | park-final | `/*/*/(BlockedTaskParkReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkResumeTests*)\|(BlockedTaskParkDeliveryTests*)/*` | V-6, V-7, V-8, V-11, R-3 | exact 15 results (3 + 2 + 4 + 6), 0 failed/skipped | 15 | 8 | true |
| CP-30 | all | CP-27 | publication-final | `/*/*/(TaskParkPublicationTests*)\|(TaskParkRunnerIdentityTests*)\|(BlockedTaskParkProjectionTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-12, V-13, R-4, R-5, R-9 | exact 12 results (3 + 2 + 2 + 5), 0 failed/skipped | 12 | 3 | true |
| CP-31 | all | CP-27 | lifetime-final | `/*/*/(DispatcherSweepLifetimeTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | R-6 | exact 7 results, 0 failed/skipped | 7 | 3 | true |
| CP-32 | all | CP-27 | job-final | `/*/*/(RunnerSlotEndpointTests*)\|(PhoneHomeDeferredKillTests*)\|(RunnerSlotRulesTests*)/*` | R-7 | exact 15 results, 0 failed/skipped | 15 | 3 | true |
| CP-33 | all | CP-27 | seat-family-final | `/*/*/(SeatDesktopJoinTests*)\|(SeatOccupancySamplerTests*)\|(SeatOccupancyAttentionTests*)\|(SeatOccupancyProjectionTests*)/*` | V-9, V-10, R-8 | exact 41 results (5 + 11 + 10 + 15), 0 failed/skipped | 41 | 8 | true |
| CP-34 | all | CP-27 | followup-guard-final | `/*/*/(RemotePoolFollowUpAdmissionTests*)\|(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)/*` | R-9, registry guard | exact 6 results (3 + 1 + 2), 0 failed/skipped | 6 | 2 | true |
| CP-35 | all | CP-27 | service-followup-final | `/*/*/AgentTaskServiceIntegrationTests/(a_follow_up_on_a_prior_task_that_is_itself_blocked_is_refused*)\|(a_follow_up_onto_an_agent_parked_on_a_blocked_task_is_refused*)\|(names_cancel_when_the_blocked_tasks_session_is_dead*)` | R-9 | exact 3 results, 0 failed/skipped | 3 | 3 | true |

Run each group once its slice is committed, through the checkpoint tool:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1108f-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1108f-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1108f-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-07-card-1108-1124-followups-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)"
```

`--after S2` to `--after S6` select the later groups; the S6 Code task then runs `--after all` at
its final committed SHA. Continue `wait` while the exit is 75. Exit 4 is a slot timeout: report the
row as not run. Preserve every tool-produced `CHECKPOINT` line unedited. No unlisted build or test
loop; a red-first run and its restore are unlisted runs with a stated reason and lease, as the
CARD-1108 Code tasks reported them. Code and Review run `scripts/check-evidence-diff.ps1` over the
task range. Delete only producer-owned `bin-c1108f-*` outputs; evidence stays ignored.

### Cost

Estimated, not measured: ordinary Code V/R floor = **155 minutes**, the sum of the
`EstimatedMinutes` column (S1 29, S2 23, S3 17, S4 17, S5 19, S6 7, final 43). Each building row
includes about 2 minutes of isolated build (CARD-1108 reviews measured 107-135 s) and 40 s of host
start. Checkpoint-driver bootstrap allowance is **5 minutes**, not double-counted.

Slices stay inside 30-60 minutes: S1 30 + 29 = 59; S2 32 + 23 = 55; S3 36 + 17 = 53; S4 26 + 17 =
43; S5 34 + 19 = 53; S6 32 + 7 = 39 plus the 43-minute final set, which is check time, not
authoring. Total author = **190 minutes**; Code floor with bootstrap = **160 minutes** across six
Code tasks, or one Code task at `-ExpectAbout` 360 if the orchestrator prefers a single dispatch. If
a slice exceeds its bound, split it at the slice boundary before starting the next one; do not
widen a row.

## Mutation handoff

After land, dispatch a method-scoped SourceLanding Mutation for PC-1..PC-14 of this plan at the
landed SHA, each control one method with the filters above, none batched (PC-1/PC-2/PC-3,
PC-4/PC-5/PC-6 and PC-7/PC-8 share production files). Keep per-PC baseline, red and restored-green
evidence in the external evidence root. Separately, the orchestrator's CARD-1124 Mutation brief
(PC-1..PC-11 of that plan) carries this note: "The Mutation evidence for CARD-1124 PC-1 and PC-2
together closes CARD-1065 G-184/PC-184 (CARD-1140); record PC-184 as closed by PC-1+PC-2." The
CARD-1108 Mutation brief reads PC-11's red as `VerifyCalls` 4 against the pin 3 and CP-28 as 15
(CARD-1141).

## Rollout and activation

Nothing activates and nothing is gated. After the AppHost restart that picks up S1-S4, a Held park
refused before its intent persist carries the refusal reason and a fresh backoff instead of being
prepared on every sweep; a sweep's `Released` and the job's return count only that run's
confirmations; a run never visits a row twice; the follow-up and detail guidance read the current
attempt and the remote-pool 422 names Reply when a confirmed park exists; a refused parked resume
writes one warning per reason. Parking, reclaim and automatic release stay `false` by default and
every one of those paths is unreachable while they are. Confirm activation with `GET /api/version`.
S5 and S6 ship tests and documentation only.

## Handoff

The verification design above is complete, so the next stage is Code S1 on
`feat/card-task-cd4e71ed`'s landed successor with
`checkpoints: docs/superpowers/plans/2026-10-07-card-1108-1124-followups-plan.md@<plan commit sha> section "### Checkpoints"`,
`--after S1`. One Code task may take S1-S6 in order if the orchestrator prefers; the rows are
grouped by `After` either way.
