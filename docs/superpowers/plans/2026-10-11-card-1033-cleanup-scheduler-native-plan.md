# CARD-1033: completed-card cleanup scheduling and native qualification

Plan task: `9dea6ddd-7b64-4689-acde-d57671e8634a`. Inspected checkout:
`c141d200e996dfea80e5f2eed295e1dbb2f6073b`; fetched master:
`a91ba68a72185d57d39e87b218e459c5df2451de`, 2026-10-11 UTC.
**Plan complete; next: TestDesign.** This dispatch does not fold TestDesign into
Plan. The checkpoint proposal below must be frozen against CARD-1032's landed
interfaces before Code; it is not a claim that absent methods compile or pass.

## Problem and premise

The gap still exists. CARD-1017's foundation records Done obligations and can
perform guarded local cleanup in explicitly composed fixtures. Production does
not register its executor or artifact-preservation service, the residue sweep
does not enumerate its endpoints, and the runner/mirror implementation and the
remaining recovery, delivery and Windows methods are absent from this tree.
No fixing or superseding commit was found in fetched master; the inspected
cleanup files are unchanged between checkout HEAD and that master.

CARD-1033 owns original S4/S5: bounded scheduling, durable recovery/reporting,
remaining acceptance, and native qualification. CARD-1032 owns original S3 and
its portable CP-3/4/5. Its current card explicitly prohibits scheduler registration
and activation. This plan must consume that work, not implement a competing
runner protocol. Both cards depend on the foundation described in
[the replay record](../../investigations/2026-10-04-card-1017-replay-15d5a0c9.md).
The original reviewed foundation tip was `6929fc842c4abb4ea39af8598fc23eb1ac9957ea`;
the replay is already represented in this source.

The controlling policy remains CARD-1017's operator decision: "for cleanup we
should delete whole worktree after card is done, then fine to leave gitignored".
It does not authorize destruction of unique source, artifacts, live-process
workspaces, historical cohorts on upgrade, or SourceLanding snapshots.

Live card/history reads succeeded after transient HTTP 502s. CARD-1033 has one
revision, this Plan dispatch, and no intervening policy decision. CARD-1032 is
InProgress; that status is not evidence that S3 has landed. CARD-1017 still
carries the same operator policy. The older card description's claim that all
components are already implemented is superseded by the source facts below.

## Ground truth

All source paths are repository-relative. These are read-only observations,
not test results.

| Card assumption | What the inspected code does | Consequence |
|---|---|---|
| Foundation is inert, not missing. | `CardRevisionLog.AppendMove` attaches a `CardWorktreeCleanup` with the Done revision in the caller's save. `CardWorktreeCleanupService.DiscoverAsync` inventories exact attempts and revokes unissued old generations. `20261004011911_CompletedCardWorktreeCleanup` creates the three cleanup tables. | Reuse these records and the card transaction; do not add a second Done obligation. |
| Scheduler can invoke the foundation today. | `Program.cs` registers `WorktreeResidueSweepService`/`WorktreeResidueJob`, but not `CardWorktreeCleanupService`, `CardWorktreeCleanupExecutor`, or `CardDoneArtifactPreservation`. The sweep knows Inventory, SettledTask and Publication, not CardDone endpoints. | Add scheduling behind explicit gates; defer production composition until qualification is complete. |
| Execute, age, budget and cutoff are implemented together. | `WorktreeResidueSettings.Execute` and shipped configuration are false; settling floor is 120 minutes and per-run budget 25. No Done cutoff exists. The local executor checks age but not Execute/cutoff. | A nullable cutoff must fail closed; gate every new automatic CardDone action, including direct executor entry and mirror-only recovery. |
| Cooldown already prevents repeats. | `PersistCooldownAsync` writes a 24-hour `NotBefore`; the sweep never reads it. Publication has no equivalent cooldown. `C459_CooldownSurvivesRestart` uses an unrelated cursor without eligible work; `C459_BudgetIsShared` previews an empty inventory. | Preserve the established 24-hour delay, implement reads, and add populated behavioral witnesses. Existing tests cannot discharge this contract. |
| Action accounting survives a crash. | `PersistRunAsync` increments a local variable after calls and saves `ActionsAccepted` at the end. Candidates are also saved after I/O. | Persist an admission and its run linkage before I/O; recovery must not replenish a spent destructive-command slot or lose a reportable intent. |
| Endpoint retry fields need a new ledger. | `CardWorktreeCleanupEndpoint` already has `OperationId`, `RequestDigest`, `Attempts`, `NextAttemptAt`, `Retryable`, component facts, and a concurrency token, in `CardWorktreeCleanupTarget.cs`. | Extend this ledger and existing residue cursors minimally; do not recreate endpoint tables. |
| Local completion covers the mirror. | Discovery creates only a local endpoint. `CardWorktreeMirrorCleanupService` and `CompletedCardWorkspaceRemovalTests` are absent. Legacy retirement can return immediately for Complete. | S3 is a hard dependency for mirror scheduling and composed/native acceptance. Pending mirrors remain independently visible after local completion. |
| Every terminal agent-bound task is removable. | `CardWorktreeCleanupExecutor.EligibilityAsync` returns `process_ownership_unknown` whenever `AgentId` is present, in addition to active-consumer checks. | Preserve this conservative hold. No removal claim for that cohort without independently qualified ownership evidence. Native holder tests must exercise the runner guard, not pass only because this earlier hold masks it. |
| CARD-1153 absence evidence can stand in for exited-process proof. | `RunnerAbsenceEvidenceValidator` admits only a fresh authenticated **never-created** identity. | Do not reuse it as proof that a formerly running PtyHost has exited. No session-service edit belongs here. |
| Partial deletion is just another fresh removal. | `TaskWorktreeRetirementService.TryRetireCardDoneAsync`, `RetirementCommandJournal`, `GuardedWorktreeRemoval`, and `WorktreeSetAside` already carry command/physical component state. A recreated original path is protected. | Recover the same operation and exact aside. Patch only gaps demonstrated by the remaining K methods; never reset intent or infer absence from a timeout. |
| Reporting already describes card and mirror identity. | `WorktreeResidueCandidateDto` in `server/Application/Dtos/WorktreeRetirementDtos.cs` and run rows carry task, retirement/landing/request and local component facts, but no card generation, task attempt, endpoint, runner store, or next-attempt state. `GetRunAsync` reads durable rows; the script returns that API object. | Add nullable correlated fields to storage and both projections; preserve existing fields and manual Release/Revoke meanings. |
| Preview scope is enforced by its stored labels. | `PersistRunAsync` records `BoardId` but filters tasks only by project; landings are loaded separately without either filter. | Build all three lanes from one scoped task set. A scoped preview must not expose unrelated cards or landings. |
| Remaining V methods exist. | D=18 and L=36 exist. U=21, M=12, K=16, A=11 and W=4 named by the parent freeze are absent. `C1017_RecoveryDebtHolds` covers one pending-request arm, not every frozen debt variant. | S3 supplies U/M; this card supplies K/A/W and finishes the D debt vectors. Keep all parent requirements visible. |
| Current placement can be hardcoded. | Successful GETs of `/api/runner-defaults` and `/api/session-runners` returned defaults revision 2, an eligible Windows lane, a stale Linux preference and another stale/draining entry. | This was a transient observation, not a reservation. Read both routes again at dispatch; no fleet location is a plan dependency. |

## Decisions

### D-1. Preserve the parent safety contract and split implementation ownership

[The CARD-1017 plan and TestDesign freeze](2026-10-03-card-1017-completed-card-worktree-cleanup-plan.md)
remain the authority for V-1..V-9, R-1..R-3, G/PC-1..111 and the exact required
vectors. This plan changes packaging and scheduling detail, not evidence-disposal
policy. Ordinary ownership, preservation, no-follow deletion, independent endpoint
receipts, no-force behavior and immutable publication all remain mandatory.

Reject duplicating S3 in this card, broadening classifier globs, a new cleanup
agent/cron, and using TTL or a board status as deletion proof. CARD-0824 reusable
slot trees and SourceLanding custody stay excluded. No origin branch deletion.

### D-2. Fail-closed execution and cohort boundary

Add nullable UTC `WorktreeResidue:CardDoneAfterUtc`. Null means inventory only for
CardDone, even if an existing deployment already has Execute=true for legacy
retirement. A current immutable Done revision qualifies only when
`DoneAt > CardDoneAfterUtc`; equality stays historical. The timestamp comes from
the operator, never from migration time, process startup, filesystem age, or a
fallback to the epoch. Validate non-UTC/malformed configuration at startup.

Preserve Execute=false, MinSettledMinutes=120, MaxActionsPerRun=25 and the current
cron. Apply Execute, non-preview and cohort checks before new local or remote
CardDone destructive admission, and again at executor entry so a new caller
cannot bypass them. Keep the existing settling check: null CompletedAt and
119:59.999 are held; 120:00 is eligible if all other guards pass. Read-only receipt
reconciliation may continue while execution is disabled; any resumption that can
mutate bytes must wait for execution authority. Existing fences/receipts survive
disablement and reopen. An admitted old intent remains bound to its exact tree.

Tests that call the local executor directly must opt in explicitly with a
fixture-only cutoff; change `CompletedCardCleanupFixture` accordingly. Never
change deployed configuration to make a test pass. Reject automatic historical
backfill deletion and an Execute check only at the Hangfire entry point.

### D-3. One bounded sweep, durable admission before side effects

Extend the existing residue job, using a small concrete
`CardWorktreeCleanupScheduler` to enumerate obligations/endpoints and the existing
executors for mutation. Discover current Done cards, including missed obligations
with a real transition revision and newly settled post-Done attempts. Continue
discovery after all known endpoints complete; header completion cannot seal the
set. A legacy Done status with no transition revision is visible held inventory.

Use one candidate sequence across legacy publication/settled and CardDone
local/mirror work, ordered by persisted last evaluation then stable identity.
Resolve board/project scope through the same task set for all lanes. Deduplicate
a CardDone-linked retirement/publication against its legacy candidate so one
physical endpoint is not scheduled twice. Complete local endpoints do not filter
out pending mirrors. Scope uncertainty holds work rather than expanding scope.

An action is acceptance of an eligible endpoint/legacy request for an execution
attempt, not a successful deletion. Persist the candidate's stable action key,
accepted-at time and `ActionsAccepted` debit in a short transaction before calling
an executor or enqueueing work. Claim under a run-row lock; unique
`(RunId, ActionKey)` prevents duplicate debit in that run. Do not debit held,
cooling, excluded or preview rows. A crash after admission leaves a spent debit;
never refund uncertain work. Preserve the separate existing destructive-command
slot and OperationId when resuming an accepted command. A later run can budget
one visit to that same debt; it cannot manufacture a second destructive command.

The admission row links the endpoint before first I/O; the endpoint/retirement
journal records the command identity before mutation. Reconcile these two records
after a crash between them. No DB row lock spans Git, queue waits, process reads
or network I/O. Eligibility/claim losers stay reportable. Budget exhaustion
produces `Deferred/budget_exhausted`, not false Removed or successful completion.
Existing endpoint claims and repository leases continue to exclude duplicate
execution across concurrent runs. Reject per-lane allowances and end-of-run-only
accounting. TestDesign must bind the lock ordering to the landed S3 admission.

### D-4. Durable cooldown and changed-evidence retry

Use existing endpoint `NextAttemptAt`/`Retryable` and the residue candidate cursor
for legacy lanes. Keep the existing 24-hour transient delay; apply it consistently
after bounded failed attempts, including publication and mirror transport failure.
The due predicate is `now >= NotBefore/NextAttemptAt`. Restart reads persisted
values using TimeProvider; no sleeping, hidden retry loop, or new reset command.

Persist a digest of the non-secret eligibility evidence that produced a permanent
safety refusal. Relevant evidence includes attempt/revision, source and repository
identity, preservation pointers/digest, current use/ownership evidence, endpoint
binding and observed content/registration state. A periodic read may discover
changed evidence; elapsed time alone does not authorize another destructive call.
Changed evidence triggers full eligibility checks, not automatic approval. Unknown
errors remain held with a reason; maintain an explicit reason classification.
Source/artifact repairs do not mutate old command identity. A new attempt or Done
generation requires its own normal admission.

Reject treating every refusal as transient, leaving cursors write-only, and a
random task-level cursor key that conflates attempts or distinct mirrors.

### D-5. Recover exact endpoints without repeating publication

Enumerate unresolved endpoints independently of local retirement status. A lost
runner acknowledgement queries/replays the exact S3 OperationId/RequestDigest;
an offline/unsupported/replaced store remains pending or held with its specific
reason. Never fall back to legacy WorkspaceRemove. Local progress can finish
while a mirror is offline; reverse partial outcomes remain distinct too.

Keep CardDone authority when reconstructing a local removal from the retirement
journal and aside receipt. Validate original path, admin identity, receipt owner
and source; never touch a replacement original path or a foreign aside. Directory,
registration and local-ref facts remain independent. Missing/access-denied paths
without an owned receipt are not Complete. Publication retries use the existing
cleanup-only land request and real queue; they neither republish nor change the
saved publication ID/SHA. Save failures leave recoverable debt, even when physical
removal already succeeded. No fresh command slot merely because the worker died.

### D-6. Report committed facts through the existing API and script

Extend `WorktreeResidueRunCandidate` and the DTO with nullable CardId,
DoneRevisionId, TaskAttempt, TargetId, EndpointId, EndpointIdentity, RunnerId,
RunnerStoreId, OperationId, RequestDigest, endpoint state, retryability and
next-attempt time. Preserve existing local booleans and landing/request links.
Store action admission fields from D-3 and an evidence digest on the durable
retry owner from D-4. Generate the additive EF migration with the CLI; old runs
read back with null new fields. Do not edit historical migrations.

Use a shared projection for Preview and `GetRunAsync`; retain caller restrictions,
bounded pages and stable ordering. Excluded/held/partial/pending/complete are
different facts. Every intent, including one with no result yet, must be queryable
after restart. Persist outcome plus correlated task event before publishing the
existing `AgentTaskChanged` refresh hint. Failure of that hint does not lose facts.
Keep file/report contents and credential-bearing paths out of reason text.
The actual script must expose both local and mirror outcomes from a populated
fixture HTTP response, not merely contain expected strings in its source.

Reject a new notification/caller-message transport, a separate retention-copy
service, and returning Removed for only local success. Canonical Result, distinct
author report detail and external deliverables must still be retrievable byte for
byte after tree/evidence disposal.

### D-7. Wakeup is an optimization; persisted obligations own recovery

Add an infrastructure wakeup adapter for the existing Hangfire job. The manual
Done path requests it only after the actual card commit, outside `CardRevisionLog`.
A missing/failed wakeup leaves the committed obligation intact. Tracker/other
Done writers remain covered by the atomic obligation and recurring discovery;
qualify those paths instead of adding I/O to the shared synchronous revision helper.
Use the existing job for a startup recovery enqueue and ordinary recurring work;
respect Enabled=false and do not register a second scheduler. No external
transaction may enqueue work that assumes its uncommitted Done is visible.

Production DI and wakeup calls land last. Until then newly added services are
composed explicitly only by fixtures. Final composition is safe on upgrade because
Execute remains false and an absent cutoff denies CardDone execution even on
legacy Execute=true installations. Reject making successful enqueue or a socket
ACK the acceptance oracle: the real recipient and physical facts decide.

### D-8. Native qualification and process ownership are separate from activation

Use the supported ModernConPty backend and parent-owned native children on Windows.
Require actual denied rename/delete, locked aside retry, late junction substitution
and outside-byte survival. A test-owned live PtyHost with a terminal DB row remains
alive and its workspace is held; report its exact known identity, while uncertain
ownership retains `process_ownership_unknown`. Exercise S3's runner guard directly
so the local AgentId hold cannot mask a broken runner guard. Do not remove that
local hold in this card without a separately reviewed ownership-proof change.

The native test owner releases and joins its own holder after observing refusal;
cleanup code never stops, kills, pools or detaches it. Missing native prerequisites
fail qualification, not Skip or an early-return pass. Use the existing owned-host
fixture contract; do not modify AgentSessionService/AgentService/AgentControlService.
No session that waits for input is added or changed. Cleanup has no session-release
deadline; with parking disabled, an input-waiting session keeps its seat (CARD-1083).

### D-9. Placement and stage boundary

All portable implementation/checkpoint work should use the Linux lane where
available. Omit `-Runner`; omit `-Platform` for portable work. `-Platform Any`
clears an inherited pin. Native witnesses alone require `-Platform Windows`.
Re-read defaults/catalogue and effective concurrency before dispatch, particularly
because Linux was stale at this inspection. Do not embed today's host location.

TestDesign is separate and must produce the executable guard/PC binding, exact
post-S3 roster, checkpoint import/coverage checks and revised costs. Existing parent
PC obligations remain pending. Neither this Plan nor ordinary Code greens stand
in for post-land Mutation. No builds, tests or Mutation ran in this Plan dispatch.

## Operator decisions

No operator answer is required to write this plan or proceed to TestDesign.
Existing authority covers planning and publishing this task branch.

Before **activation**, the operator must supply the UTC Done-cohort cutoff, approve
the bounded canary cohort and its execution window, and explicitly authorize
enabling Execute. This plan supplies no guessed timestamp, cohort or approval.
After inspecting the historical preview, widening that cutoff requires a separate
operator decision. All code can land with Execute=false and cutoff=null. Keeping
those values is the required shipping behavior, not permission to activate later.

## Landable slices and collision map

Budgets below are 30–60 minutes **including the selected checkpoint allowance**,
assuming S3 and the shared fixtures named below have landed. Split a slice before
dispatch if TestDesign finds it exceeds that bound; do not reduce its assertions.
Each slice compiles, carries its own focused tests, and is safe to land with no
automatic CardDone caller. Commit/push before checkpoints. S1–S3 may be prepared
while S3 runner work is pending, but shared-file edits must be serialized.

Paths abbreviated below: A=`server/Application/Services/`, T=`tests/Antiphon.Tests/`.
New files are explicitly marked. R/K/A/W test aliases are defined in the next section.

| Slice / budget | Concrete file footprint and behavior | Tests / dependency |
|---|---|---|
| S1 / 45 min: fail-closed policy | `server/Application/Settings/WorktreeResidueSettings.cs`, validator; A/`CardWorktreeCleanupExecutor.cs`; new A/`CardWorktreeCleanupPolicy.cs`; T/`TestHelpers/CompletedCardCleanupFixture.cs`; new T/`Application/CardWorktreeCleanupRecoveryTests.cs` | K.ExecuteFalse, HistoricalCutoff, SettlingFloor; CP-1..3. Preserve shipped false; tests explicitly opt in. |
| S2 / 45 min: admission/report schema | `server/Domain/Entities/WorktreeResidueRun.cs`, `CardWorktreeCleanupTarget.cs`; `server/Infrastructure/Data/AppDbContext.cs`; CLI-generated `server/Migrations/*CardCleanupScheduling*` and model snapshot; new T/`Application/CardCleanupSchedulingSchemaTests.cs` | Add D-3/4/6 fields and uniqueness only; CP-4. No backfill deletion or runtime registration. |
| S3 / 60 min: one shared durable budget | new A/`CardWorktreeCleanupScheduler.cs` and `WorktreeResidueAdmission.cs`; A/`WorktreeResidueSweepService.cs`; `server/Domain/Enums/WorktreeRetirementEnums.cs` additive lane values; K test file; new T/`TestHelpers/CompletedCardSchedulingFixture.cs` | Populated legacy/local/mirror candidate admissions, scope/dedup/fairness, crash-after-debit; CP-5. Mirror dispatcher can remain unavailable and no deletion is fabricated. Depends S1/S2. |
| S4 / 45 min: due and evidence retry | scheduler/admission, A/`WorktreeResidueSweepService.cs`; K test file | Persist/read cooldown and permanent evidence holds; CP-6..7. Depends S3. |
| S5 / 45 min: exact intent recovery | A/`CardWorktreeCleanupExecutor.cs`, `TaskWorktreeRetirementService.cs`; `server/Infrastructure/Data/RetirementCommandJournal.cs`; K and scheduling fixture | Same command on restart, independent component facts, result-save failure; CP-8..10. |
| S6 / 60 min: aside/absence recovery | `server/Infrastructure/Git/GuardedWorktreeRemoval.cs`, `WorktreeSetAside.cs`; `server/Infrastructure/Data/WorktreeRemovalEvidence.cs`; K and scheduling fixture | Authority, foreign/replacement path and unknown absence; CP-11..14. Patch only defects exposed by these witnesses. |
| S7 / 45 min: publication/debt recovery | A/`AgentTaskLandService.cs`, `CardWorktreeCleanupExecutor.cs`; K; T/`Application/CardDoneWorktreeCleanupTests.cs`; existing completed-card fixtures | Real cleanup-only queue, frozen D.RecoveryDebtHolds variants including confirmed-publication positive arm; CP-15..16. Do not touch general agent services. |
| S8 / 60 min: scheduler recovery adapter | new `server/Application/Interfaces/ICardWorktreeCleanupWakeup.cs`, new `server/Infrastructure/Agents/CardWorktreeCleanupWakeup.cs`; scheduler and `WorktreeResidueJob.cs`; K and scheduling fixture | Existing job recipient, lost Done wakeup, failed Hangfire/land enqueue; CP-17..18. Interface is an external scheduling I/O seam. No production hook yet. |
| S9 / 60 min: durable readback | `server/Application/Dtos/WorktreeRetirementDtos.cs`; A/`TaskWorktreeRetirementService.cs`, `WorktreeResidueSweepService.cs`; new A/`WorktreeResidueProjection.cs`; executor outcome/event path; `scripts/worktree-residue.ps1`; new T/`Application/CardWorktreeCleanupAcceptanceTests.cs` | A.OperatorOutcome, EveryIntent, NoOpaqueBytes, ReportReadback; CP-19..22. Existing routes suffice; no endpoint edit planned. |
| S10 / 45 min: Done delivery | A test file; new T/`TestHelpers/CompletedCardDeliveryFixture.cs`; wakeup/job adapter fixes limited to that boundary | A.DoneQueueBusyRecipient/AlreadyEligible; CP-23..24. Real Hangfire, actual Done writer and physical removal. |
| S11 / 45 min: publication delivery | A test file and delivery fixture; localized A/`AgentTaskLandService.cs` fixes only if required | A.PublicationQueueBusyRecipient/AlreadyEligible; CP-25..26. Actual land queue, same publication. |
| S12 / 60 min: mirror delivery | A test file and delivery fixture; A/`CardWorktreeMirrorCleanupService.cs` (owned first by CARD-1032) only after its land | A.PhoneHomeQueueBusyRecipient/AlreadyEligible; CP-27..28. Real socket/dispatcher/workspace filesystem; hard S3 dependency. |
| S13a / 60 min: crash-worker plumbing and local cuts | new T/`TestHelpers/CompletedCardCleanupCrashWorker.cs`; `TestHelpers/TestWorkerModes.cs`; A test file/delivery fixture; internal fault hooks only in the already-named scheduling/local owners | Local Done/Hangfire/land/aside/result/UI cuts from parent A.CrashEveryHandoff. Add a local-cut companion method, CP-29. No reflection-loading a test DLL into pwsh. |
| S13b / 60 min: complete crash handoff contract | same crash fixture and A file; S3 coordinator hooks after its land | Full parent A.CrashEveryHandoff including send, runner intent, response loss and independent server/runner restart; CP-30. Local companion remains; do not mark the parent method complete with a subset. |
| S14 / 60 min: Windows lock/junction witnesses | new T/`Infrastructure/CompletedCardWorktreeRemovalWindowsTests.cs`; new T/`TestHelpers/CompletedCardWindowsFixture.cs`; no production changes unless separately narrowed to the proven removal defect | W.LockedSetAsideRetries and JunctionOutsideBytesSurvive; CP-31..32, native Windows only. |
| S15 / 60 min: Windows process witnesses | same W file/native fixture; reuse landed owned-PtyHost test helpers without changing their contracts | W.LingeringPtyHostIsReported/CleanupNeverKillsHolder; CP-33..34. Requires S3 Windows guard and staged ModernConPty. |
| S16 / 60 min: final compatibility qualification | Test-only narrow repairs within files already named; `tests/Antiphon.Tests/slow-tests-allowlist.txt` for any newly Slow class, with its adjacent reason | CP-35..40 at the composed tip; parent CP-3/4/5 and CP-10/11/12 reconciliation below. No whole Unit run. Split into portable and Windows dispatches if lane/cost requires it. |
| S17 / 60 min: production composition, still disabled | `server/Program.cs`; A/`CardService.cs` post-commit wakeup only; `server/Infrastructure/Agents/HangfireConfiguration.cs`; T/`Application/WorktreeResidueRegistrationTests.cs`; new T/`Application/CardCleanupCompositionTests.cs`; `docs/orchestration-loop.md`, `docs/agent-card-lifecycle.md`, `docs/testing-and-build.md`, `docs/ops-http.md`, `docs/bootstrap.md` | Only after all required ordinary/native evidence; CP-41..44. Read bootstrap owner before edits. Preserve Execute=false and null cutoff; startup discovery is report-only. |

Each slice introducing a Slow class updates `tests/Antiphon.Tests/slow-tests-allowlist.txt`
in that same commit, not only at S16. CP-44 checks the final compiled registry;
TestDesign should reuse that exact guard in any earlier slice that adds a class.

`tools/Antiphon.Checkpoints/**`, `AgentSessionService.cs`, `AgentService.cs`,
`AgentControlService.cs`, `scripts/c590-remote.sh` and `RemoteScriptContractTests.cs`
are explicitly outside the edit footprint because the brief identifies in-flight
owners. Use the checkpoint tool without modifying it. No runner contract/runtime
edits are assigned here; CARD-1032 owns them. Its mirror coordinator, Program,
cleanup executor, schema/model snapshot, shared test-worker entry and owner docs
are serialization points. Recheck the active task footprints before every dispatch.
Do not combine S1–S17 into one large Code task.

## Verification requirements for TestDesign

This section proposes the execution decomposition. The parent freeze supplies
the safety vectors and mutation obligations; TestDesign must audit each real body,
bind missing seams and freeze exact result counts before Code. New methods are
single `[Test]` results with internal named vector loops unless that freeze
explicitly revises the roster. Assertions/loop iterations do not increase Min.

K=`Antiphon.Tests.Application.CardWorktreeCleanupRecoveryTests` (16 parent methods);
A=`Antiphon.Tests.Application.CardWorktreeCleanupAcceptanceTests` (11 parent methods
plus the local crash companion); W=`Antiphon.Tests.Infrastructure.CompletedCardWorktreeRemovalWindowsTests`
(4); D=`CardDoneWorktreeCleanupTests`; L=`CompletedCardWorktreeRemovalTests`;
U=`Antiphon.SessionRunner.Tests.CompletedCardWorkspaceRemovalTests` (21, S3 owner);
M=`Antiphon.Tests.Application.CardWorktreeMirrorCleanupTests` (12, S3 owner).
Every inherited method retains its `C1017_` prefix and parent guard/PC identity.

Minimum acceptance:

- D-2: populated eligible local and mirror fixtures with Execute=false, preview,
  null cutoff, historical/equal/new Done timestamps and exact age boundaries;
  no destructive calls, bytes intact, actual new cohort positive arm.
- D-3/4: 26 eligible actions across lanes with budget 25, stable fair ordering,
  duplicate CardDone/legacy identity, concurrent claims and crash after debit;
  restart before and exactly at due time; permanent refusal repeated unchanged
  and then evidence repaired. A board/project-scoped preview excludes an unrelated
  task and publication in both rows and counts. No empty-inventory proof.
- D-5: parent K83..96/110/111 vectors, independent endpoint facts, real Git,
  isolated PostgreSQL, owned set-asides and exact source/report bytes. Extend
  D.RecoveryDebtHolds with every original repair/recovery variant; one pending
  request is insufficient. Existing C459 recovery methods remain regressions.
- D-6: a second DB connection reads intent/outcome at event emission and after
  restart; actual HTTP plus PowerShell script sees per-endpoint facts and no
  synthetic secret/file contents. Full canonical report and external artifacts
  survive. Preview is inventory only, including historical Done and exclusions.
- D-7: three real queues, each with an owned busy recipient and an already-eligible
  recipient. Hangfire ends at removal; land queue ends at cleanup without a push;
  phone-home ends at real runner receipt **and** independent filesystem/Git facts.
  All 15 parent `C1017_CrashEveryHandoff` cuts plus enqueue/send/save faults remain
  mandatory; busy test owners release their own blockers. Row/ACK-only tests fail
  this acceptance contract. Startup and tracker lost-wakeup cases use the same
  persisted-obligation recovery.
- D-8: all four W witnesses and repeated U/L/M on Windows at the same composed
  source SHA as the portable qualification; no Linux unlink result substituted
  for native sharing semantics. Native missing prerequisites are reported as not
  qualified, never a successful skip.

Reuse `CompletedCardCleanupFixture`, `CompletedCardRemovalFixture`,
`LandingSafetyHarness`, real Git hooks, isolated-schema `TestDbFixture`, the real
`AgentTaskLandQueue` and S3's real dispatcher fixture. Loopback phone-home uses
`PhoneHomeTestHost` framing with the real dispatcher rather than canned success.
Use locally inherited `dotnet` crash workers, deterministic pipes/barriers and
TimeProvider. Respect each assembly's process-spawn limiter; run serially and
join every owned child. A host booting real Program must use ProductionRunnerGuard
and an isolated runner. No deployed worktree, mirror, runner or secret store.

### Parent checkpoint debt crosswalk

Parent IDs in this table refer only to the original CARD-1017 artifact; CP IDs
in the following manifest belong to this CARD-1033 artifact.

| Parent obligation | Owner and proposed discharge |
|---|---|
| CP-3 / CP-4 / CP-5, U21 + runner legacy28 + M12/remote8 | CARD-1032 implements and ordinarily qualifies them. CP-35..37 below validate integration at the final composed source; a board status or old unrelated receipt cannot discharge them. |
| CP-6, K16 + C459 recovery3 | CP-1..3,5..15,17..18,38. K's exact 16 methods are preserved, split by behavior. |
| CP-7, A11 + endpoint6/script1/registration3 | CP-19..28,30,41. CP-29 adds a local crash companion; it does not replace A.CrashEveryHandoff. |
| CP-10, W4 + settled-removal78 | CP-31..34,39. Native Windows only. |
| CP-11, U21 on Windows | CP-40. Same U methods as CP-35, different required OS. |
| CP-12, L36 + M12 on Windows | CP-42, after final composition; same source used for portable final checks. |
| Foundation CP-1/2/8/9 and D debt gap | Foundation historical receipts stay historical; CP-16 extends debt coverage. TestDesign must add the exact affected foundation regressions if production changes alter those guards. No silent whole-Unit expansion. |

### Checkpoints

**Proposed, not yet the Code freeze.** Each row names one behavior or existing
compatibility contract and an exact narrow selector. A reused build refers to an
earlier row in the same After group; each new build uses a forward-slash isolated
output. All rows are serial. Lane is explicit in Group: `linux-` is portable work
preferred on Linux; `windows-` requires native Windows. Select explicit `--rows`,
not `--after all`. TestDesign must reconcile the parent result rosters with S3 and
the then-current tree, split expensive rows, and validate import without changing
the checkpoint tooling. Every row requires zero failed/skipped and exact names.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1033-s1/` | linux-execute-gate | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_ExecuteFalseCannotDelete*` | D-2; parent PC-91 | 1 result | 1 | 4 | true |
| CP-2 | S1 | CP-1 | linux-cohort-cutoff | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_HistoricalCutoffCannotDelete*` | D-2; PC-92 | 1 result | 1 | 1 | true |
| CP-3 | S1 | CP-1 | linux-settling-floor | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_SettlingFloorCannotDelete*` | D-2; PC-93 | 1 result | 1 | 1 | true |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1033-s2/` | linux-schema-upgrade | `/*/*/CardCleanupSchedulingSchemaTests*/C1033_AdditiveUpgradePreservesHistory*` | D-3/4/6 | 1 result; model/migration/unique key/old rows | 1 | 4 | true |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-c1033-s3/` | linux-shared-admission | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_ActionBudgetIsShared*` | D-3; PC-94 | 1 result; populated scope/dedup/crash/concurrency vectors | 1 | 6 | true |
| CP-6 | S4 | `tests/Antiphon.Tests -> bin-c1033-s4/` | linux-cooldown | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_CooldownSurvivesRestart*` | D-4; PC-95 | 1 result | 1 | 4 | true |
| CP-7 | S4 | CP-6 | linux-permanent-refusal | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_PermanentRefusalAwaitsEvidence*` | D-4; PC-96 | 1 result | 1 | 1 | true |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c1033-s5/` | linux-command-recovery | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_IntentRecoveryDoesNotResetBudget*` | D-5; PC-84 | 1 result | 1 | 4 | true |
| CP-9 | S5 | CP-8 | linux-component-facts | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_ComponentFactsAreIndependent*` | D-5; PC-89 | 1 result | 1 | 1 | true |
| CP-10 | S5 | CP-8 | linux-result-save-debt | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_ResultSaveFailureRetainsDebt*` | D-5; PC-111 | 1 result | 1 | 1 | true |
| CP-11 | S6 | `tests/Antiphon.Tests -> bin-c1033-s6/` | linux-aside-authority | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_SetAsideRecoveryPreservesAuthority*` | D-5; PC-85 | 1 result | 1 | 4 | true |
| CP-12 | S6 | CP-11 | linux-aside-identity | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_SetAsideIdentityMustMatch*` | D-5; PC-86 | 1 result | 1 | 1 | true |
| CP-13 | S6 | CP-11 | linux-replacement-protection | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_ReplacementPathIsUntouched*` | D-5; PC-87 | 1 result | 1 | 1 | true |
| CP-14 | S6 | CP-11 | linux-absence-receipt | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_AbsenceNeedsReceipt*` | D-5; PC-88 | 1 result | 1 | 1 | true |
| CP-15 | S7 | `tests/Antiphon.Tests -> bin-c1033-s7/` | linux-publication-identity | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_PublicationIsImmutable*` | D-5; PC-90 | 1 result | 1 | 5 | true |
| CP-16 | S7 | CP-15 | linux-recovery-debt | `/*/*/CardDoneWorktreeCleanupTests*/C1017_RecoveryDebtHolds*` | remaining V-4 debt vectors | 1 result | 1 | 2 | true |
| CP-17 | S8 | `tests/Antiphon.Tests -> bin-c1033-s8/` | linux-lost-wakeup | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_LostWakeupRecovers*` | D-7; PC-83 | 1 result | 1 | 5 | true |
| CP-18 | S8 | CP-17 | linux-enqueue-failure | `/*/*/CardWorktreeCleanupRecoveryTests*/C1017_EnqueueFailureRemainsRecoverable*` | D-7; PC-110 | 1 result | 1 | 2 | true |
| CP-19 | S9 | `tests/Antiphon.Tests -> bin-c1033-s9/` | linux-outcome-before-event | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_OperatorOutcomeIsDurable*` | D-6; PC-97 | 1 result | 1 | 4 | true |
| CP-20 | S9 | CP-19 | linux-intent-reporting | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_EveryIntentIsReportable*` | D-6; PC-98 | 1 result | 1 | 1 | true |
| CP-21 | S9 | CP-19 | linux-report-redaction | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_NoOpaqueBytesInReport*` | D-6; PC-99 | 1 result; real script/HTTP | 1 | 2 | true |
| CP-22 | S9 | CP-19 | linux-artifact-readback | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_ReportReadbackSurvivesDeletion*` | D-6; PC-100 | 1 result | 1 | 2 | true |
| CP-23 | S10 | `tests/Antiphon.Tests -> bin-c1033-s10/` | linux-done-busy | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_DoneQueueBusyRecipient*` | V-1/2/7 delivery | 1 result | 1 | 5 | true |
| CP-24 | S10 | CP-23 | linux-done-ready | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_DoneQueueAlreadyEligible*` | V-1/2/7 delivery | 1 result | 1 | 2 | true |
| CP-25 | S11 | `tests/Antiphon.Tests -> bin-c1033-s11/` | linux-publication-busy | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_PublicationQueueBusyRecipient*` | V-2/7 delivery | 1 result | 1 | 5 | true |
| CP-26 | S11 | CP-25 | linux-publication-ready | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_PublicationQueueAlreadyEligible*` | V-2/7 delivery | 1 result | 1 | 2 | true |
| CP-27 | S12 | `tests/Antiphon.Tests -> bin-c1033-s12/` | linux-mirror-busy | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_PhoneHomeQueueBusyRecipient*` | V-5/6/7 delivery | 1 result | 1 | 5 | true |
| CP-28 | S12 | CP-27 | linux-mirror-ready | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_PhoneHomeQueueAlreadyEligible*` | V-5/6/7 delivery | 1 result | 1 | 2 | true |
| CP-29 | S13a | `tests/Antiphon.Tests -> bin-c1033-s13a/` | linux-local-crash-cuts | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1033_LocalCrashHandoffs*` | local subset of V-7 | 1 result; owned child joined at every cut | 1 | 8 | true |
| CP-30 | S13b | `tests/Antiphon.Tests -> bin-c1033-s13b/` | linux-all-crash-handoffs | `/*/*/CardWorktreeCleanupAcceptanceTests*/C1017_CrashEveryHandoff*` | full parent V-7 | 1 result; all 15 cuts plus faults | 1 | 12 | true |
| CP-31 | S14 | `tests/Antiphon.Tests -> bin-c1033-s14/` | windows-locked-aside | `/*/*/CompletedCardWorktreeRemovalWindowsTests*/C1017_LockedSetAsideRetries*` | V-9; PC-103 | 1 native result | 1 | 5 | true |
| CP-32 | S14 | CP-31 | windows-junction | `/*/*/CompletedCardWorktreeRemovalWindowsTests*/C1017_JunctionOutsideBytesSurvive*` | V-9; PC-104 | 1 native result | 1 | 2 | true |
| CP-33 | S15 | `tests/Antiphon.Tests -> bin-c1033-s15/` | windows-lingering-host | `/*/*/CompletedCardWorktreeRemovalWindowsTests*/C1017_LingeringPtyHostIsReported*` | V-9; PC-101 | 1 native result | 1 | 6 | true |
| CP-34 | S15 | CP-33 | windows-holder-survives | `/*/*/CompletedCardWorktreeRemovalWindowsTests*/C1017_CleanupNeverKillsHolder*` | V-9; PC-102 | 1 native result | 1 | 2 | true |
| CP-35 | S16 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-s16-runner/` | linux-s3-guarded-contract | `/*/*/CompletedCardWorkspaceRemovalTests*/C1017_*` | parent CP-3 dependency | parent 21; recensus S3 | 21 | 8 | true |
| CP-36 | S16 | CP-35 | linux-legacy-runner-contract | `/*/*/(RunnerWorkspaceServiceTests*)\|(PhoneHomeCommandDispatcherTests*)/(Mirror_*)\|(Publish_*)\|(Remove_*)\|(Workspace_*)` | parent CP-4 dependency | parent 28; recensus | 28 | 3 | true |
| CP-37 | S16 | `tests/Antiphon.Tests -> bin-c1033-s16-server/` | linux-s3-mirror-contract | `/*/*/(CardWorktreeMirrorCleanupTests*)\|(RemoteWorktreeMirrorTests*)/*` | parent CP-5 dependency | parent 20; recensus S3 | 20 | 7 | true |
| CP-38 | S16 | CP-37 | linux-existing-recovery | `/*/*/WorktreeResidueRecoveryTests*/(C459_IntentCommittedBeforeGit*)\|(C459_ComponentsAreIndependent*)\|(C459_OutageKeepsFence*)` | parent CP-6 regressions | 3 results | 3 | 2 | true |
| CP-39 | S16 | `tests/Antiphon.Tests -> bin-c1033-s16-windows/` | windows-settled-removal-contract | `/*/*/SettledWorktreeRemovalTests*/C459_*` | parent CP-10 regressions | parent 78; recensus | 78 | 10 | true |
| CP-40 | S16 | `tests/Antiphon.SessionRunner.Tests -> bin-c1033-s16-win-runner/` | windows-s3-guarded-contract | `/*/*/CompletedCardWorkspaceRemovalTests*/C1017_*` | parent CP-11 | parent 21; recensus S3 | 21 | 8 | true |
| CP-41 | S17 | `tests/Antiphon.Tests -> bin-c1033-s17/` | linux-reporting-compatibility | `/*/*/(WorktreeResidueEndpointTests*)\|(WorktreeResidueScriptTests*)\|(WorktreeResidueRegistrationTests*)/*` | parent CP-7 regressions | parent 10; recensus | 10 | 5 | true |
| CP-42 | S17 | `tests/Antiphon.Tests -> bin-c1033-s17-windows/` | windows-local-mirror-contract | `/*/*/(CompletedCardWorktreeRemovalTests*)\|(CardWorktreeMirrorCleanupTests*)/C1017_*` | parent CP-12 | parent 48; recensus S3 | 48 | 10 | true |
| CP-43 | S17 | CP-41 | linux-disabled-composition | `/*/*/CardCleanupCompositionTests*/C1033_UpgradeStaysReportOnly*` | D-2/7; actual DI/startup/manual Done/tracker recovery | 1 result | 1 | 3 | true |
| CP-44 | S17 | CP-41 | linux-classification-contract | `/*/*/TestClassificationGuardTests*/Registry_matches_compiled_metadata*` | new Slow classes and adjacent cost reasons | 1 result | 1 | 1 | true |

### Cost and execution rules

Estimates, not measurements: the proposal totals **170 checkpoint minutes** and
**265 result executions** across 44 rows. The 18 slice envelopes total **975 minutes**
including that checkpoint allowance, leaving 805 minutes for authoring/fixture
work. Slot waits, TestDesign, Review and Mutation are additional. TestDesign must
recalculate these totals from the frozen roster, including each new build once;
budget source-authoring time within the 30–60-minute slices above. The fine-grained method rows
reuse builds within a slice to avoid rebuilding for each assertion. S16/S17 span
OS lanes and may need separate tasks on the same source. Final Windows witnesses
must match the final composed SHA; receipts from S14/S15 at an earlier SHA are
development evidence until delta-aware Review qualifies or reruns them.

The composition change is a stated reason for final verification, not a repeat of
unchanged green source: TestDesign must bind final portable and Windows selections
to one post-S17 SHA. At minimum qualify CP-31..43 against that SHA, reusing prior
receipts only where Review explicitly validates unaffected source/build provenance.
Any required reruns add their listed time to the final dispatch; do not hide them
inside S17's 60-minute envelope. A separate verification slice may carry those runs.

Use `tools/Antiphon.Checkpoints run --plan <this-plan> --rows <selection>
--expected-source-sha <committed-sha>` after the TestDesign freeze, and keep
waiting until exit is not 75. Use the host build-slot gate for launcher builds,
EF scaffolding and any other build driver; row drivers lease slots themselves.
Preserve unedited CHECKPOINT lines, expanded names/counts and source provenance.
Never use `dotnet test`, a whole Unit lane, a foreign running executor, or skip a
native witness. Clean task-owned alternate outputs after runs. Generated evidence
stays ignored; essential receipts belong in the stored report. Code/Review run
`scripts/check-evidence-diff.ps1` over their full task range.

No tests/builds were needed for this read-only Plan. All parent PC-1..111 remain
pending method-scoped post-land Mutation, including their named variants; no PC
is discharged by the checkpoint proposal. TestDesign must add controls for the
new durable debit, scope filtering, cutoff null/equality, evidence digest and
disabled-composition guards rather than pretending parent mapping covers them.

## Risks, rollout and stop conditions

The largest dependency is S3: its operation identity, retry API and native fixture
must be inspected after landing before freezing S12/S13/S15/S16. Unknown or
unsupported peers retain residue. Native handles, process identity and no-follow
removal cannot be certified from this Linux planning checkout.

The local AgentId hold can retain many historical trees. Report that limitation;
do not silently replace it with a terminal-status or never-created-certificate
shortcut. A requirement to delete those trees needs a dedicated ownership-proof
design. Current source retains the bytes safely.

Persisted admission introduces transaction ordering alongside existing card,
workspace and endpoint locks; deterministic contention/crash witnesses are
mandatory. A changed-evidence digest must include relevant filesystem facts
without storing file contents. A poor digest either retries permanent refusals
or hides newly repaired work; both require explicit vectors. Schema changes are
additive and migrations are CLI-generated. Reconcile collision-prone model and
worker-entry files before dispatch, not during an active long test run.

Implementation rollout: land qualified runner support first; land server slices
with Execute=false/null cutoff; only after all ordinary/native controls and the
separately required Review/Mutation stages are satisfied may the caller commission
activation. The orchestrator owns the canonical server migration/restart and
loaded-SHA check. This docs-only plan needs no restart.

Before enablement, run a report-only preview and inspect every page: current Done
generation, terminal attempt, source/artifact preservation, local/mirror reasons,
unknown ownership, SourceLanding exclusions and historical cutoff holds. With
explicit operator authorization, use one bounded newly Done canary; check physical
local and mirror outcomes independently and read its durable report after restart.
Stop expansion on unexplained deletion, mismatched identity, missing report,
unaccounted action, killed holder, or historical execution. Disable Execute to
prevent new mutation; do not discard intents, fences or debt. Historical cleanup
requires the separate cutoff decision after preview, never an upgrade side effect.

Next stage: TestDesign. Post-land Mutation was **NOT run**.
