# CARD-1144 / CARD-1146 / CARD-1147: confirmed-park Reply and reclaim confirmation

Plan date: 2026-10-08. Source inspection: 2026-10-07 UTC.
Plan task: `3f2a3eda-e5c5-4736-835c-03ba4fc5e3f2`.
Base: fetched `origin/master` = **e2c5150101014b259b436adb951f163dc0b1cdcd**.
The assigned branch was fast-forwarded from b5e78700ae9a76430c13d75cc03399055dd82e59;
no rebase, reset, amendment, or force push was used. All line citations below refer
to that fetched master, not the historical revisions quoted on the cards.

Read the full Antiphon card descriptions for CARD-1144, CARD-1146, CARD-1147 and
the landed CARD-1108, CARD-1124, CARD-1145 code and named tests. The latter card is
still in Review on the board, but its post-commit accounting implementation is in
this source. Board status alone is not the source verdict. No runtime experiment,
build, or test was performed by this Plan task; findings below are source traces.

**Outcome:** five ordered, independently committed slices. CARD-1144 needs a small
production repair; CARD-1146 needs a shared EF query, not merely more wording tests;
CARD-1147 is test-only. Verification design is folded into this artifact because
the brief requests executable checkpoints, mutation detectors, and regressions.
Next stage is **Code**, followed by ordinary Review, Land, and separately commissioned
SourceLanding Mutation. This plan grants no parking enablement.

## Ground truth

| Card assumption / question | What current master does (verified file:line) | Consequence |
|---|---|---|
| Mark-read breaks the accepted release identity. | `server/Application/Services/AgentTaskService.cs:2615` reads the task; `:2622` stamps ReadAt and `:2623` rotates ConcurrencyToken on the first read. `server/Application/Services/TerminalRunnerSeatReleaseService.cs:533` requires session existence and runner equality, and `:541` through `:544` compare task, attempt, session, agent, runner, store, generation, settlement revision and settled time. | CARD-1144 remains present. Read metadata must not invalidate the application revision used as settlement authority. Keep the settlement-revision comparison. |
| A missed release cannot cause delivery to the stopped session. | `AgentTaskService.cs:3173` through `:3178` returns false when no admissible release is found and no accepted answer is pending. `server/Application/Services/AgentTaskReplyService.cs:388` falls through on false; `:434` sets Working and `:465` enqueues to the old session. There is no confirmed-park veto in that fallback. | Fix both the ordinary mark-read trigger and the unsafe fallback for already-stale confirmed parks. Do not silently repair historical release identities. |
| Existing tests prove mark-read Reply is safe. | `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs:24` exercises the admitted arm with AnswerAsync, but its stale arm at `:53` only calls MarkReadAsync and asserts 422 wording at `:61`. `AgentTaskReplyIntegrationTests.cs:2711` only pins first-read timestamp idempotence. | Add actual AnswerAsync outcomes after first/repeated reads. Preserve the stale-wording assertions by replacing their setup with explicit real revision drift. |
| Guidance already shares real admission. | `AgentTaskService.cs:3297` explicitly says it copies the predicates. Its correlated query at `:3320` through `:3349` duplicates both identity and confirmation. It executes one ToListAsync. Real acceptance calls FindAttemptReleaseAsync at `:3162` and `:3197`; continuation at `:3387` and `:3414` additionally uses IsConfirmed. | Extract one translatable identity query and one translatable confirmed-receipt query, used by the real Reply path and the guidance. Keep guidance at one database round trip. |
| Every accepted answer immediately has a confirmed release. | `AgentTaskService.cs:3166` allows a linked published ReleasePending/Reserved park, and `:3174` permits Unresolved. Acceptance commits the answer at `:3242`; confirmed continuation occurs separately at `:3385`. | Guidance promises a confirmed continuation, not merely buffered acceptance. Preserve Reserved/Unresolved durable buffering and all locked rechecks. |
| Missing session can be inferred safe from a park. | `TerminalRunnerSeatReleaseService.cs:536` through `:539` returns null for missing task runner/session, missing server session or wrong session runner. Guidance correlates the server session at `AgentTaskService.cs:3343`. | Shared query must retain these fail-closed facts. No absent-session shortcut. |
| Follow-up refusal precedence may be rearranged. | `AgentTaskService.cs:515` calls RefuseRemotePoolFollowUp before the live Blocked 409 at `:521`; the remote pool error is thrown at `:3291`. | Preserve 422 `follow_up_remote_pool_unsupported`, no insert, and the fresh-task guidance. |
| Released is still a wall-clock comparison. | `TerminalRunnerSeatReleaseService.cs:147` creates a per-run set; `:174` requires membership AND IsConfirmed on a fresh lookup. `:965` commits ConfirmAsync before `:966` through `:967` add the task ID. `:188` clears the set in finally. | CARD-1145 is already correct by inspection. CARD-1147 needs a detector, not a production accounting rewrite. |
| A rollback test alone kills an early-add mutation. | Even with an early set insertion, the fresh IsConfirmed check at `TerminalRunnerSeatReleaseService.cs:174` rejects an unconfirmed rolled-back ledger. `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs:754` covers backward/equal clocks and prior confirmations, not commit failure. | Include an independently confirmed-after-rollback arm, so the set must prove which invocation committed. A plain rollback-only assertion is insufficient for the requested PC. |
| The sweep is ungated or the job counts visits. | `TerminalRunnerSeatReleaseService.cs:204` gates scheduled reclaim, `:219` calls ReclaimLegacyAsync(32,3), and `server/Infrastructure/Agents/SessionRunner/RunnerSlotReconcileJob.cs:47` adds only Released. Tests at `BlockedTaskParkReclaimTests.cs:280`, `:394`, `:413`, `:721`, `:754` pin gate, job, dispatcher hook, distinct visits, and per-run accounting. | Reuse the scheduled sweep and existing fixture boundaries. No new scheduler, gate, SQL tally, or counter field. |
| CARD-1124 occupancy is still missing. | `server/Application/Services/SeatDesktopJoin.cs:69` through `:72` includes Queued and Blocked owners; `server/Application/Dtos/RunnerSlotDtos.cs:4` defines park evidence and `:24` includes it on slots. `SeatDesktopJoinTests.cs:182`, `:276` and `SeatOccupancySamplerTests.cs:101` pin the behavior. | Do not change occupancy or declare a Blocked seat free. Retain bounded regression coverage. |
| Waiting has a release deadline. | `server/Application/Settings/BlockedTaskParkingOptions.cs:6` and `:7` default both switches off. `docs/session-runtime-invariants.md:70` through `:79` explicitly say nothing automatically releases an input wait while disabled; the 120-second proof/interval and 600-second backoff are not deadlines. | No automatic stop or enablement change. CARD-0079 remains the only automatic stop policy for a Working session. |
| The docs already describe the repaired behavior. | `docs/session-runtime-invariants.md:120` lists CARD-1144 as a known limit, pinned verbatim by `BlockedTaskParkProjectionTests.cs:85`. Runtime lines `:101` through `:103` and `docs/orchestration-loop.md:966` describe exact confirmed-park guidance. | Update owner sentences and their existing pins together, after implementation passes. Preserve the other known limits. |

## Decisions

### D-1. Separate read metadata from settlement authority

In MarkReadAsync remove only the ConcurrencyToken assignment. Retain first-read
ReadAt, SaveChangesAsync, not-found handling, family/summary projection and repeated-read
idempotence. ReadAt is operator acknowledgement metadata, not a new task attempt,
answer, settlement or workspace owner. EF only writes modified properties here;
there is no reason to rewrite release or park receipts when a drawer is opened.

This deliberately applies to all mark-read calls, including reads before reservation,
after publication, and after durable answer acceptance. It does not promise serialization
of simultaneous first-read timestamps beyond the existing contract. No new read-write
transaction or broader mark-read concurrency redesign is needed for these cards.

Rejected: drop SettlementRevision from admission (admits stale authority); replace it
with CompletedAt (not a revision and nullable for nonreport blocks); synchronize a
receipt to the new token (rewrites accepted evidence); add a migration for a second
revision (unnecessary and forbidden); fix only 422 prose (leaves lost Reply behavior).

### D-2. Fail closed on a confirmed park whose exact identity no longer matches

Inside TryAcceptReleasedSeatAnswerAsync, in the branch that would return false at
current lines 3173-3178, retain the existing accepted-answer/pending-release refusal,
then check HasConfirmedPublishedParkAsync. If true, throw ConflictException with
code `park_release_identity_mismatch` and bounded text explaining that the published
park's release identity no longer matches. Do this before any live fallback workspace
admission, Working transition, Replied event or queue write. No new public endpoint.

HasConfirmedPublishedParkAsync intentionally remains the current-attempt, linked,
published Parked/ResumePending plus confirmed-ledger detector. It is a veto on unsafe
fallback, not an alternative source of continuation authority. This also protects
historical rows already damaged by mark-read: refuse; do not retag the receipt.
Rows with no confirmed current-attempt park keep the established live/local/warm
Reply behavior. Keep the old-session queue gate and both task/release/session lock
rechecks. A concurrent genuine task revision still returns its existing conflict.

Rejected: require a live session for all Reply (breaks valid released continuation);
auto-retry, auto-cancel or auto-launch a damaged park; enqueue then repair the status;
convert all Reserved/Unresolved parks into conflicts (loses durable answer buffering).

### D-3. Share an EF-translatable query, including confirmed continuation

Add internal query helpers in
`server/Application/Services/RunnerSeatReleaseQueries.cs`:

- ForAttempt(AppDbContext, AgentTask) returns an AsNoTracking IQueryable of releases.
  Capture task scalars. Empty/null RunnerId or missing AgentSessionId yields an empty
  query. Match the nine release fields listed in V-3, plus a correlated AgentSessions
  Any for exact session ID, task runner, store and accepted StartedAt. Preserve nullable
  equality semantics for SettledAt; do not add normalization to stored runner identity.
- Confirmed(IQueryable<RunnerSeatRelease>) adds State=Confirmed, non-null ConfirmedAt
  and ActionId, and the exact Released/AlreadyExited/AlreadyAbsent outcome whitelist.

FindAttemptReleaseAsync delegates to ForAttempt.SingleOrDefaultAsync. Add a corresponding
FindConfirmedAttemptReleaseAsync (or a clearly named query call at each site) using
Confirmed(ForAttempt). Both continuation lookups use this shared confirmed query.
TryAcceptReleasedSeatAnswerAsync keeps its state-independent exact lookup and its
Reserved/Unresolved logic. RemotePoolParkReplyAsync embeds the same confirmed query
as a correlated Any keyed by the park's RunnerSeatReleaseId in its existing single
projection. Retain the separate weak Confirmed projection that selects mismatch wording.

This replaces the copied admission expression; calling a helper and then retaining
the copied expression does not satisfy CARD-1146. No Compile, Invoke, AsEnumerable,
per-park reads, or client evaluation inside the EF query. No mutable/static cache or
DI policy interface. The existing IsConfirmed helper may remain for accounting and
other non-Reply users; V-5 compares its outcomes with the shared confirmation query.
The real Reply continuation and remote guidance must both use the new query.

The current exact lookup has two statements (session then release); the joined query
can reduce that to one. Guidance must stay one statement, not one plus a preload.
The 422 precedence and current-attempt/publication/link/state park filters stay intact.

Rejected: copy-and-pin both predicates, in-memory release materialization, calling
FindAttemptReleaseAsync once per projected park, or broadening admission to accommodate
inconsistent receipts. These lose the shared implementation or statement budget.

### D-4. Test the confirmation commit boundary without production instrumentation

CARD-1147 adds one test method to BlockedTaskParkReclaimTests, using test-local EF
command/transaction interceptors through RunnerSeatReleaseFixture.CreateAsync's
existing configureDb parameter (`RunnerSeatReleaseFixture.cs:128`). The accounting
implementation, job and dispatcher are read-only in this slice.

The fault identifies the chosen release's Confirmed UPDATE and its actual transaction,
then rolls it back at TransactionCommittingAsync and throws a deterministic fixture
exception once. Arm only after source publication/idle eligibility, and only for that
transaction; do not fault reservation, Unresolved intent, cursor or fixture setup.
Confirm write and rollback hits must each equal one. Use no sleeps or timeout widening.

V-8 has a healthy control and two fault worlds: ordinary rollback, then rollback
followed by independent recovery before the sweep's accounting read. For the latter,
use the existing BeforeAttentionPublish boundary (`TerminalRunnerSeatReleaseService.cs:598`)
when the fault interceptor has recorded the target's rollback. PublishAttentionAsync
is reached through RegisterAndReleaseAsync's finally. After the failed transaction
has disposed and AdvanceAsync has released the queue gate, invoke
ReconcileAcceptedAnswerAsync in a different service scope. Give its runner a fresh
complete absent/exited inventory and retain source proof, so this real recovery
commits the target release. Disable the fault for that scope; its service has no
outer BoundaryAsync callback, and a one-shot flag prevents repeating recovery.
Assert that it has a different context/service identity and that recovery completed
before allowing the outer sweep to continue. All work is awaited and test-owned.

The second scope has no outer legacyConfirmations set. Healthy source therefore
counts only the other rows; moving the Add immediately before tx.CommitAsync causes
the failed invocation to claim the independently committed release and overcount.
Do not replace this with reflection into the set, a mocked IsConfirmed result, or a
manual ledger write that bypasses the confirmation path. If the boundary cannot be
reached with these existing seams, report that fixture gap; do not add a production
hook or call the simple rollback arm mutation-sensitive.

### D-5. Keep the dormant safety and input-wait contract explicit

No migration, settings change, runner protocol change, assertion deletion/weakening,
provider launch change, process stop, or automatic resume. Publication and release
stay default-off. Accepted-answer recovery continues even when new parking is disabled.
The only new refusal is a damaged confirmed park falling toward the old session.

Input-wait checklist: with shipping settings **nothing automatically releases the
waiting session, and there is no automatic release deadline** (CARD-1083). With the
three gates explicitly enabled, the existing publication/source/custody/idle and
conditional-release receipts control physical release; neither 120 seconds nor
600 seconds promises a release time. Reply after confirmed release queues a new
attempt; Reply to a live Blocked seat resumes it without freeing the seat. Parking
never stops Working. Do not expand CARD-0079's exceptional automatic-stop policy.

### D-6. Portable execution and bounded stages

Read GET /api/runner-defaults and GET /api/session-runners at approximately
2026-10-07 23:27 UTC. Defaults revision was 2, with no kind overrides. The catalogue
had an accepting Linux lane, a draining Linux entry, and an accepting Windows lane.
This observation does not pin a fleet location or establish future capacity.
Use the **Linux / isolated PostgreSQL** lane for integration rows and the portable
Linux unit lane for doc pins. Dispatch without -Runner or -Platform; re-read live
defaults/catalogue before dispatch. -Platform Any is the explicit unpin operation.

Postgres rows are Serial=true and retain NotInParallel("MessageQueue") and the
assembly-local ProcessSpawnLimit. Do not co-schedule Pty/FakeClaude test assemblies.
Use isolated schemas and fixture runner endpoints, never the production runner.
The brief's narrow manifest overrides the generic whole-Unit recipe.

## Slices and ownership

Budgets are authoring estimates; checkpoint time is separately budgeted below.
Implement in this order, commit and push each meaningful slice, and freeze source
for every checkpoint run. Do not combine these into one unreviewable repair.

| Slice | Budget | Files | Result and verification | AppHost activation |
|---|---:|---|---|---|
| S1 — CARD-1144 behavioral repair | 55 min | `server/Application/Services/AgentTaskService.cs`; `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs`; `tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.cs` | D-1/D-2; V-1/V-2; preserve C1103 stale assertions by setting a genuinely different task revision directly in its fixture, not by mark-read; strengthen the existing read-idempotence test with token assertions. CP-1..CP-5. | Server restart after reviewed land to activate MarkRead/Reply changes; none during Code. |
| S2 — CARD-1146 shared query and identity parity | 60 min | new `server/Application/Services/RunnerSeatReleaseQueries.cs`; `server/Application/Services/TerminalRunnerSeatReleaseService.cs`; `server/Application/Services/AgentTaskService.cs`; `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs` | D-3, V-3/V-4; production refactor and all identity fields. Commit/push; finish the same verification group with S3. | Server restart after reviewed land, can share S1's activation. No runner restart. |
| S3 — CARD-1146 confirmation, park scope and SQL parity | 50 min | `tests/Antiphon.Tests/Application/BlockedTaskParkReplyAdmissionTests.cs` | V-5..V-7 using the real helper and public follow-up route; retain all existing tests. CP-6..CP-11 close S2-S3. No further production change expected. | None independently. |
| S4 — CARD-1147 commit-failure detector | 60 min | `tests/Antiphon.Tests/Application/BlockedTaskParkReclaimTests.cs` | D-4, V-8. Private interceptors/helpers in this file; use existing configureDb and scope seams. CP-12..CP-15. No accounting production edit. | None. |
| S5 — owner contracts and bounded regressions | 30 min | `docs/session-runtime-invariants.md`; `docs/orchestration-loop.md`; `tests/Antiphon.Tests/Application/BlockedTaskParkProjectionTests.cs` | Exact sentences/pins below; CP-16..CP-33 cover the existing release, resume, delivery, occupancy and default-off contracts. | None independently; deploy S1-S2 through canonical main checkout after reviewed land. |

S1 and S2 both edit AgentTaskService and the admission test; S2 and S3 share the
admission test. Run sequentially. S4 tests the terminal service changed in S2, so
its statement accounting uses the S2-S3 source as its baseline.

| Reported in-flight owner | Shared files with this plan | Sequencing |
|---|---|---|
| CARD-1149/1150 S2, Code 7c5280e8: AgentTaskDispatcher.cs and SessionReconciliationService.cs | No planned edits to either. S5's existing resume/dispatch regressions consume the dispatcher. | Do not modify either file to accommodate a failure. Any necessary overlapping fix waits for that owner to land and is separately commissioned. Refresh the regression baseline after its landing if it precedes S5. |
| CARD-1115 S1, Code c57f1d8a: AgentTaskDispatcher.cs pin guards and RepairSourceDispatchTests | No planned shared files. | Keep pin guards and repair tests out of scope. Any scope extension touching them is sequenced after this owner, never concurrent. |
| CARD-1105 audit fix: scripts/c590-remote.sh and RemoteScriptContractTests | No planned shared files or native-script changes. | No dependency. A newly discovered script overlap waits for this owner; no cleanup edits here. |

Before Code, refresh the active ownership/branch inventory. If any of the listed
plan files has acquired another writer, defer that slice until the writer lands.
Current master already contains CARD-1149/1150 S1 and CARD-1125/1128; do not replay
their historical patches. No AppHost or runner restart belongs in this Plan task.
For activation, the caller follows docs/apphost-runbook.md from the canonical main
checkout, then verifies /api/version against its source HEAD and all parking flags
remain off. Never restart from this worktree or use -AllowWorktree.

## Owner sentences and pins

S5 adds these exact sentences to the confirmed-park paragraphs of both
docs/session-runtime-invariants.md and docs/orchestration-loop.md:

> Mark-read records the first ReadAt without changing the task's settlement revision; a matching confirmed-park Reply still queues one new attempt (CARD-1144).

> Reply to a confirmed published park with a mismatched release identity returns 409 park_release_identity_mismatch before any old-session input is queued (CARD-1144).

> Confirmed-park Reply guidance and continuation use the same EF release-identity and confirmed-receipt queries; guidance remains one database read (CARD-1146).

Keep the existing CARD-1103 422, exact identity and attempt-scope sentences. Extend
C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers with labels c1144-read-revision,
c1144-no-old-session and c1146-shared-query for both owner files. These doc pins
supplement V-1..V-7; they do not prove runtime behavior.

Replace the runtime's one known-limits line and its existing exact equality pin with:

> Known limits stay on CARD-1097 item 2 (resume reads the desktop checkout, not the runner mirror), CARD-1104 for a remote parent with RunnerCwd, and CARD-1143 (a Held row whose episode can no longer be loaded is visited on later sweeps without a re-stamp).

Add CARD-1144 to that pin's closed-card exclusion list; keep its existing labels and
all other assertions. Add this runtime sentence with label c1147-confirm-commit:

> A failed or rolled-back confirmation contributes nothing to that sweep's Released count, even if a separate recovery confirms the same release before the sweep reads it (CARD-1147).

No generated docs/cards edits. No AGENTS, bundles or skill edits are necessary.

## Verification design

All new production-behavior tests use the existing real PostgreSQL service graph,
RunnerSeatReleaseFixture, real Git publication and controlled runner wire. Start
from BlockedTaskParkDeliveryTests.PublishAsync then StampAsync where applicable.
Read durable results through a fresh context; seed and verify outside measured SQL
windows. Keep fixture disposal and every foreground operation awaited.

### New coverage and witnesses

| ID | Method (file under tests/Antiphon.Tests/Application) | Setup, action, required outcome |
|---|---|---|
| V-1 | `BlockedTaskParkReplyAdmissionTests.C1144_MarkReadPreservesConfirmedParkReply` | Three fresh worlds: no read, one read, two reads. Capture task token, release revision and old session after real publish/stamp. Read through AgentTaskService, advance fake time between reads, then AnswerAsync with the same distinctive Unicode/tail answer. Assert ReadAt first-stamp semantics, unchanged task token before Reply, unchanged release identity, and after Reply exactly attempt 2/Queued, full ReleasedSeatAnswer, one answer ID and one Replied event, no answer queue row/input to old session and no launch before dispatch. Include follow-up before Reply: same 422, names this task's -Reply, no inserted task. One [Test], three internal worlds = one result. |
| V-2 | `BlockedTaskParkReplyAdmissionTests.C1144_StaleParkNeverFallsBackToOldSession` | Real confirmed park, then genuine task revision change, leaving release unchanged (this models pre-fix damaged rows). AnswerAsync must return 409 park_release_identity_mismatch; status remains Blocked, attempt/session/token unchanged, no accepted answer, no Replied event/queue/input/launch. A second world with deleted server session but the retained task session ID and confirmed ledger must give the same veto, not a live fallback or inferred continuation. One result. |
| V-3 | `BlockedTaskParkReplyAdmissionTests.C1146_ReleaseIdentityParity` | Nine separately reported [Arguments] cases change exactly one release field: TaskId, Attempt, SessionId, AgentId, RunnerId, RunnerStoreId, AcceptedStartedAt, SettlementRevision, SettledAt. Historical release fields have no FKs (`server/Domain/Entities/RunnerSeatRelease.cs:5`), so change the ledger, not both sides. Each starts from an admitted real park and checks the positive control before corruption; then FindAttempt returns null, confirmed query empty, public follow-up is unchanged 422 without -Reply/no inserts, public Answer is the V-2 conflict/no old input. Restore that one field and prove admission returns. Labels identify the exact field. |
| V-4 | `BlockedTaskParkReplyAdmissionTests.C1146_SessionIdentityParity` | Six [Arguments] cases: missing server session, mismatched session RunnerId, null task RunnerId, empty task RunnerId, null task AgentSessionId, null session RunnerStoreId. Direct exact and confirmed queries must be empty; guidance must never name Reply; public Answer must refuse without queue/launch on the current confirmed park. For the null task-runner case, exercise the guidance query directly in addition to public Create, since upstream routing can take the local 409 branch. Keep that branch's existing precedence. |
| V-5 | `BlockedTaskParkReplyAdmissionTests.C1146_ConfirmationParity` | Ten [Arguments] cases: Released, AlreadyExited, AlreadyAbsent (admitted); Observing, Reserved, Unresolved, null ConfirmedAt, null ActionId, null OutcomeCode, unknown OutcomeCode (no confirmed candidate). Keep identity exact. Assert shared confirmed query and existing IsConfirmed agree, plus public 422 names Reply iff confirmed. Positive cases actually Answer and reach Queued/new attempt. For negatives do not conflate confirmed continuation with buffering: Reserved/Unresolved retain existing accepted-answer behavior; no test may require an immediate new attempt. Existing recovery regressions cover these branches. |
| V-6 | `BlockedTaskParkReplyAdmissionTests.C1146_ParkScopeParity` | Seven [Arguments] cases: Parked and ResumePending positives; foreign task, prior attempt, null publication receipt, null linked release, Resumed negatives. Mutate the park only. Public follow-up stays 422/no inserts and names -Reply only in positives; positives actually Answer. Do not assert that a legacy exact release with no matching park can never buffer an answer: that is a different existing contract. |
| V-7 | `BlockedTaskParkReplyAdmissionTests.C1146_GuidanceUsesOneStatement` | FullCommandCounter on the fixture (all six sync/async command paths). Measure the actual RemotePoolParkReplyAsync query separately from Create's unrelated reads, using an internal method visibility change if required (no new behavior or fixture-only branch). In admitted, stale, missing-session and multiple-park worlds, exactly one SELECT, no writes. Also execute public Create and assert the same 422 classification/no inserts. Capture SQL roster; reject client evaluation and per-park reads. One method/result. |
| V-8 | `BlockedTaskParkReclaimTests.C1147_RolledBackConfirmationIsNotThisRunsRelease` | D-4's healthy control and two fault worlds each contain three eligible published rows, target in middle of deterministic cursor order and two healthy neighbors. Run ReclaimScheduledAsync with zero configured interval and established 120-second server idle window. All worlds: Visited=3, Registered=3, one visit each, cursor reaches last row, no force call. Control: Released=3. Fault worlds: Released=2, two healthy Parked/confirmed neighbors, confirm-write/rollback hits each exactly one. Plain rollback: target stays Unresolved/ReleasePending and session/agent projections did not commit. Independent recovery: target confirmed by the other scope, total ledger confirmations=3 but this sweep Released=2; recovery finishes before tally. Sweep-context full SQL roster/counts match control; independent recovery commands are separately attributed. Tally window has exactly two SELECTs per row after S2, no added count query. One [Test] with three internal worlds = one result. |

Expose RemotePoolParkReplyAsync and its nested result enum as internal only if needed
for V-4/V-7's precise query boundary; it is within S2's declared AgentTaskService edit.
Public Create still supplies the integration assertion. Do not add a delegate that
substitutes the query under test. V-8 compares its healthy control with both fault
worlds in the same test. The commit fault is after all confirmation statements, so
the sweep context must execute the same command count/normalized SQL roster in all
three worlds. Attribute the independent recovery's extra commands to its separate
context, not to the sweep. At BeforeAttentionPublish, after any independent recovery,
also open a tally measurement window; close it at the first cursor command. Expect
exactly the existing task SELECT and the S2 joined release SELECT per visited row,
with no new tally query. Compare SQL shape/counts, not parameter values or IDs.
Review additionally confirms S4 changes no production file.

### Regression selection

R-1: existing C1103 admitted/stale/previous-attempt guidance and first-read timestamp
test; R-2: live local/warm Reply; R-3: real public attempt-scoped follow-up and remote
pool refusal precedence. R-4: C1145 backward/equal-clock accounting; R-5: C1129 unique
visits/prior confirmations; R-6: scheduled overlap/bounds; R-7: job counts;
R-8: dispatcher hook. R-9: published-source requeue; R-10: answer/release races;
R-11: exact stop-bypass receipt; R-12: uncertain accepted-answer recovery;
R-13: ordinary admission refusal retains the answer; R-14: publication-before-release;
R-15: Working custody veto; R-16: old/new-session complete UserPrompt delivery;
R-17: CARD-1124 owner and park projection; R-18: owner/default-off doc pins.

The named classes and exact selected methods are the CP table. Large
AgentTaskReplyIntegrationTests and TerminalRunnerSeatReleaseTests are intentionally
method-scoped. No whole-Unit, whole-namespace, assembly, browser, or live-provider run.
Existing failures must be reproduced with the same failing method at the recorded
base before attribution; never weaken assertions, widen timeouts or add retries.

### Negative controls (post-land SourceLanding Mutation)

PCs are pending, not executed by this Plan or ordinary Code. For each variant,
fresh exact-method green -> compiling production mutation -> intended assertion
red -> restore -> fresh exact-method green. Use the literal single-method filters
below (without the ordinary checkpoint trailing wildcard); parameterized methods
run their bounded cases. Zero tests, a setup/translation/build error or a fixture
timeout is not red. Never mutate a test to obtain red. Record exact failed label,
nonzero counts, C/O/L provenance and full restoration outside the snapshot.

| PC | Production mutation and expected failure | Detecting filter | Variants |
|---|---|---|---:|
| PC-1 | Restore MarkRead's token rotation; V-1 fails unchanged token/attempt 2. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_MarkReadPreservesConfirmedParkReply` | 1 |
| PC-2 | Bypass the new confirmed-park fallback veto in TryAcceptReleasedSeatAnswerAsync; V-2 fails 409/no old queue. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_StaleParkNeverFallsBackToOldSession` | 1 |
| PC-3a..i | Omit one of the nine ForAttempt release comparisons, one at a time; that field's direct-query-null assertion fails before later transactional guards can mask it. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ReleaseIdentityParity` | 9 |
| PC-4a..f | For V-4's missing-session, wrong-session-runner and null-store cases, respectively bypass the correlated session Any, its runner equality, or its store equality. For each null task runner, empty task runner and null task session case, change that early empty-query branch to return releases by task ID alone (an unsafe fallback from missing identity). This kills a reachable exclusion without pretending deletion of a redundant null check would suffice. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_SessionIdentityParity` | 6 |
| PC-5a..d | Drop State, ConfirmedAt, ActionId, or outcome-whitelist condition from the shared Confirmed query independently; its exact negative case fails. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ConfirmationParity` | 4 |
| PC-5e..g | Remove Released, AlreadyExited, or AlreadyAbsent from the accepted whitelist independently; the corresponding positive case fails. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ConfirmationParity` | 3 |
| PC-6a..e | Bypass each of foreign-task, current-attempt, publication-receipt, linked-release, and accepted-park-state filters independently in guidance. For null-link case replace the link with the fixture's otherwise matching current-attempt release, not an always-false nullable comparison. | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ParkScopeParity` | 5 |
| PC-7 | Add one redundant awaited release SELECT to RemotePoolParkReplyAsync; V-7 sees 2 instead of 1 (a compiling performance regression). | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_GuidanceUsesOneStatement` | 1 |
| PC-8 | Move the existing changed==1/task-ID Add from after CommitAsync to immediately before it; preserve every other guard. V-8's independent-recovery arm reports Released=3 instead of 2. Plain rollback remains a required green arm but cannot alone detect this mutation. | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1147_RolledBackConfirmationIsNotThisRunsRelease` | 1 |

Total: **31 compiling variants**, not 31 tests. PC-4 may require a small compiling
replacement of a complete exclusion (rather than deletion of one conjunct) because
the early guard and correlated session predicate deliberately overlap. Name the
exact variant and preserve all unrelated fences. Review must reject equivalent
mutants; do not claim an exclusion tested just because another fence hid it.
The changed doc pins also receive one text-control cycle against their owner sentence,
using only C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers; that is a doc mutation,
separate from the 31 production variants. Existing assertion names and gates stay.

### Execution, evidence and cost

Each checkpoint is one behavior and one exact filter. Trailing method wildcards are
intentional for pinned TUnit discovery; inspect the executed names and expanded
argument counts. Every row expects zero failures/skips. Reused builds share the exact
After token. S2 and S3 share After=S2-S3 and execute after both commits exist.

Use the checkpoint tool, one run per group. Bootstrap its isolated executable
through the host build-slot gate once if no suitable built tool exists, then run
that executable without an outer lease; its row drivers acquire their own slots.
The repository's normal source invocation is shown below; if it would build the
tool, first use the leased isolated build and then invoke that DLL instead.
Substitute the committed SHA:

```powershell
dotnet <isolated-tool-output>/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-08-card-1144-1146-1147-confirmed-park-reply-plan.md --after S1 --expected-source-sha <sha>
```

The normal source form is `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <plan>`;
the DLL form avoids an unleased bootstrap build and avoids nested driver leases. Follow
docs/testing-and-build.md's checkpoint runner instructions; keep waiting in the
foreground until exit is not 75, and do not leave a run alive at settlement. Split
wait calls at 60 seconds for progress reporting. Exit 4 is not-run/blocked, never
permission to retry unleased. Alternate output paths use forward slashes and are
removed by checkpoint cleanup after runs finish.

Record every unedited CHECKPOINT line, source SHA, dirty/sourceState/buildSource,
actual expanded counts, skipped count and receipt path. Code/Review run
scripts/check-evidence-diff.ps1 over the full assigned task range and validate
checkpoint receipts against their exact committed source. Generated evidence stays
ignored. Freeze any tracked report before final qualification; do not relabel an
earlier run after a later edit. If a later slice changes earlier production, rerun
the affected earlier rows and report why; doc-only S5 does not imply a full rerun.

Authoring estimate: 255 minutes. Ordinary floor is the CP table sum, **108 minutes**; build-slot queue time is additional.
Mutation floor: 31 variants x 5 minutes = 155 minutes, plus 5 minutes for the doc
control and 20 for source discovery/restoration = **180 minutes**. These are
estimates, not measured performance or execution counts. Total minimum authoring +
ordinary + Mutation allocation is **543 minutes**; ordinary Review/landing are
separately commissioned. If a method exceeds a foreground window, await it through
the tool rather than broadening the filter or abandoning it.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1144-s1/` | linux-pg-mark-read-reply | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_MarkReadPreservesConfirmedParkReply*` | V-1 | named method, 0 failed/skipped | 1 | 7 | true |
| CP-2 | S1 | CP-1 | linux-pg-stale-park-veto | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_StaleParkNeverFallsBackToOldSession*` | V-2 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-3 | S1 | CP-1 | linux-pg-existing-guidance | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1103_RemotePoolReplyNamesOnlyAdmittedRelease*` | R-1 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-4 | S1 | CP-1 | linux-pg-first-read-stamp | `/*/Antiphon.Tests.Application/AgentTaskReplyIntegrationTests/marking_a_task_read_is_idempotent_and_preserves_the_first_stamp*` | R-1 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-5 | S1 | CP-1 | linux-pg-live-reply | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Reply_on_live_local_or_warm_session_is_unchanged*` | R-2 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-6 | S2-S3 | `tests/Antiphon.Tests -> bin-c1146-s23/` | linux-pg-release-identity | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ReleaseIdentityParity*` | V-3 | all 9 argument cases, 0 failed/skipped | 9 | 9 | true |
| CP-7 | S2-S3 | CP-6 | linux-pg-session-identity | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_SessionIdentityParity*` | V-4 | all 6 argument cases, 0 failed/skipped | 6 | 5 | true |
| CP-8 | S2-S3 | CP-6 | linux-pg-confirmation-parity | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ConfirmationParity*` | V-5 | all 10 argument cases, 0 failed/skipped | 10 | 5 | true |
| CP-9 | S2-S3 | CP-6 | linux-pg-park-scope | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_ParkScopeParity*` | V-6 | all 7 argument cases, 0 failed/skipped | 7 | 4 | true |
| CP-10 | S2-S3 | CP-6 | linux-pg-guidance-sql | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1146_GuidanceUsesOneStatement*` | V-7 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-11 | S2-S3 | CP-6 | linux-pg-follow-up-scope | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/C1103_ConfirmedParkGuidanceIsAttemptScopedAndNamesReply*` | R-3 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-12 | S4 | `tests/Antiphon.Tests -> bin-c1147-s4/` | linux-pg-confirm-rollback | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1147_RolledBackConfirmationIsNotThisRunsRelease*` | V-8 | named method and all internal witnesses, 0 failed/skipped | 1 | 8 | true |
| CP-13 | S4 | CP-12 | linux-pg-confirm-clock | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1145_ReleasedCountsConfirmationsThisRunProduces*` | R-4 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-14 | S4 | CP-12 | linux-pg-unique-reclaim-visits | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1129_SweepCountsThisRunsReleasesAndVisitsEachRowOnce*` | R-5 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-15 | S4 | CP-12 | linux-pg-reclaim-gates | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1108_ScheduledSweepIsGatedAndBoundedPerRun*` | R-6 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-16 | S5 | `tests/Antiphon.Tests -> bin-c1144-final/` | linux-pg-job-count | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1108_ReconcileJobCountsOnlyReleases*` | R-7 | named method, 0 failed/skipped | 1 | 5 | true |
| CP-17 | S5 | CP-16 | linux-pg-dispatcher-hook | `/*/Antiphon.Tests.Application/BlockedTaskParkReclaimTests/C1108_DispatcherHookRunsTheGatedSweep*` | R-8 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-18 | S5 | CP-16 | linux-pg-published-requeue | `/*/Antiphon.Tests.Application/BlockedTaskParkResumeTests/C1065_ReplyStartsOneAttemptFromPublishedSource*` | R-9 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-19 | S5 | CP-16 | linux-pg-answer-race | `/*/Antiphon.Tests.Application/BlockedTaskParkResumeTests/C1065_ReplyRacePersistsOneAnswerAndOneOwner*` | R-10 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-20 | S5 | CP-16 | linux-pg-release-receipt | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Answer_stop_bypass_requires_the_exact_release_receipt*` | R-11 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-21 | S5 | CP-16 | linux-pg-ambiguous-answer | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Legacy_ambiguous_answer_requires_fresh_absence_without_another_release*` | R-12 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-22 | S5 | CP-16 | linux-pg-answer-admission | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Answer_admission_guards_preserve_the_accepted_reply*` | R-13 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-23 | S5 | CP-16 | linux-pg-publication-before-release | `/*/Antiphon.Tests.Application/BlockedTaskParkReleaseTests/C1065_ReportPublicationPrecedesPhysicalRelease*` | R-14 | named method, 0 failed/skipped | 1 | 3 | true |
| CP-24 | S5 | CP-16 | linux-pg-working-custody | `/*/Antiphon.Tests.Application/TerminalRunnerSeatReleaseTests/Working_session_keeps_ownership_and_visible_debt*` | R-15 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-25 | S5 | CP-16 | linux-pg-complete-answer-delivery | `/*/Antiphon.Tests.Application/BlockedTaskParkDeliveryTests/C1065_FullAnswerRequiresNewSessionUserPrompt*` | R-16 | named method, 0 failed/skipped | 1 | 4 | true |
| CP-26 | S5 | CP-16 | linux-pg-blocked-owner | `/*/Antiphon.Tests.Application/SeatDesktopJoinTests/C1124_Join_owner_task_is_queued_dispatched_working_or_blocked*` | R-17 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-27 | S5 | CP-16 | linux-pg-owner-park | `/*/Antiphon.Tests.Application/SeatDesktopJoinTests/C1124_Join_reports_the_current_attempt_park_in_nine_statements*` | R-17 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-28 | S5 | CP-16 | linux-pg-blocked-seat | `/*/Antiphon.Tests.Application/SeatOccupancySamplerTests/C1124_Blocked_owner_is_idle_not_orphan_and_carries_its_park*` | R-17 | named method, 0 failed/skipped | 1 | 2 | true |
| CP-29 | S5 | CP-16 | linux-unit-default-off-pins | `/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/C1065_DefaultOffRetainsRecoveryOfAcceptedAnswers*` | R-18 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-30 | S5 | CP-16 | linux-unit-occupancy-pins | `/*/Antiphon.Tests.Application/BlockedTaskParkProjectionTests/C1065_OccupancyTracksProcessesNotBlockedStatus*` | R-18 | named method, 0 failed/skipped | 1 | 1 | true |
| CP-31 | S5 | CP-16 | linux-pg-final-mark-read | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_MarkReadPreservesConfirmedParkReply*` | V-1 | named method after shared-query refactor, 0 failed/skipped | 1 | 3 | true |
| CP-32 | S5 | CP-16 | linux-pg-final-stale-veto | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1144_StaleParkNeverFallsBackToOldSession*` | V-2 | named method after shared-query refactor, 0 failed/skipped | 1 | 3 | true |
| CP-33 | S5 | CP-16 | linux-pg-final-legacy-guidance | `/*/Antiphon.Tests.Application/BlockedTaskParkReplyAdmissionTests/C1103_RemotePoolReplyNamesOnlyAdmittedRelease*` | R-1 | named method after shared-query refactor, 0 failed/skipped | 1 | 2 | true |
