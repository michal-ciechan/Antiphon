# CARD-0499: Attribute completion to the registered repair source

Date: 2026-09-12. Stage: Plan; verification design is a separate TestDesign stage.
Based on Investigate task `a1f96c28` and checkout
`90fd1c7c0b3646bb86f787988167e89134a3b8b6`.

## Outcome and scope

A Code delegate with verified work on its registered repair source must not fail
`CompletedWithoutProgress` merely because its own managed checkout is unchanged.
An unrelated branch advance must not become evidence of that delegate's work.
Add explicit source identity, durable baselines, a bounded completion probe and
visible evidence. Keep Git checkout, integration, landing and cleanup authority
separate from progress attribution.

This is additive dispatch and settlement behavior, not a worktree scheduler or
ownership-transfer system. It does not change transcript delivery, report verdicts,
card states, provider routing, mutation custody or the landing approval protocol.

## Ground truth

| Card assumption / proposed shortcut | Code and incident evidence | Consequence |
|---|---|---|
| Worktree creation collided with the intended source branch. | `DelegationWorktreeService.CreateForTaskAsync` creates `feat/card-task-<id>` from `MergeTargetRef ?? HEAD`. The later checkout instruction in the brief collided. | Checking the generated branch alone cannot prevent this incident. |
| The intended branch can be found in `MergeTargetRef`. | Incident `aaa668d3` had a null merge target; `feat/card-task-9701b1dd` and its owner appeared only in prose. | Add a source relationship; do not reinterpret the merge destination. |
| Settlement checks the task's actual work. | `AgentTaskReplyService.TryClassifyCompletedWithoutProgressAsync` probes only `task.WorktreePath`, for explicit `done`, Code, Worktree. | Consult only a pre-registered additional source before a negative verdict. |
| The probe proves zero commits and zero files. | `AgentFilesService.ProbeProgressAsync` checks file mtimes and the latest 50 reachable commits; `GitWorkspaceService.GetRecentCommitsAsync` uses author dates. | A timestamp scan is not an exhaustive count. Use dispatch SHA reachability for commit evidence and correct the failure wording. |
| Probe errors already fail open. | The classifier catches exceptions, but the file service swallows sub-probe errors, and Git status/log methods return empty lists on nonzero exit. | Preserve explicit availability through every layer used for completion. |
| A pushed commit establishes post-dispatch progress. | Investigate verified remote `9bc00948f5411e8df04290892eddf97207be8ff9`, matching the repair report; no dispatch baseline existed for that branch. | The incident is real, but historical remote state cannot be fabricated into a baseline. |
| A successful repair may be landed using its alternate coordinates. | Landing uses the completing task's stored source coordinates; merge-back can commit, advance a target and remove its own checkout. | Never overwrite those coordinates from progress evidence; explicitly fence repair-owner publication. |
| Existing Git observations can be called as harmless reads. | `LandingGit.ObserveSourceAsync` resolves the publication endpoint, fetches objects and pins under `refs/antiphon/land/`. | Reuse its strict observation approach without generating landing receipts or abusing its namespace. |

## Decisions

### D-1. Register a repair owner explicitly

Add optional `repairSourceTaskId` (full GUID) to `CreateAgentTaskRequest`, exposed as
`delegate.ps1 -RepairSource <guid>`. It identifies the original Code landing owner.
The server resolves repository, full branch ref and registered checkout from that
task. Callers cannot supply an arbitrary evidence directory, remote URL or baseline
SHA. No inference from the goal, report paths, `FollowUpOfTaskId` or merge target.

For this slice accept fresh Worker/Code/Worktree tasks only. Reject combinations
with SourceLanding, standing/pinned execution or follow-up reuse. Require a distinct,
authorized Code/Worktree owner in the same project and canonical Git common
directory, with complete source coordinates. Reuse existing caller/root permission
checks; knowing a task GUID grants no access. A terminal owner with a kept branch is
valid. A missing, already-published, ambiguous or replaced source is a named refusal.
An in-flight source landing uses the existing dispatch hold mechanism.

Reject an explicitly supplied merge target other than that owner's full branch.
When `-RepairSource` is present and no merge target was explicitly supplied, persist
null: do not inherit a parent target. `-RepairSource` alone grants no integration.
Ordinary tasks retain their existing merge-target defaults.

### D-2. Detect occupancy and route to an isolated branch before launch

Under the existing common-directory mutation lease, revalidate the owner and inspect
`git worktree list --porcelain -z` before creating the repair checkout or session.
An existing checkout of the source branch is expected: record its canonical path
and owner, then create the new task's normal unique branch at the resolved source
SHA. Never try to check out the owner's branch a second time or silently switch the
delegate's cwd. This is the explicit repair exception to default branch ancestry.

The generated brief names the repair owner, source full ref, source baseline SHA,
actual assigned checkout/branch, and whether integration was explicitly requested.
Emit a dispatch Warning describing the occupied source and the isolated route.
Unknown/multiple/mismatched registrations refuse with a stable code such as
`repair_source_identity_unavailable`, including authorized owner/path diagnostics;
no agent launches. A live owner may continue in its own checkout: the repair is
isolated and only committed source state is used. Dirty source files are not copied;
warn that the snapshot excludes them. Existing landing/merge admission still owns
whether the source can subsequently be advanced.

If integration was explicitly requested, use the existing guarded merge-back of
the repair's OWN branch into the owner's branch. Otherwise retain the repair branch
and report that the caller must commission integration through the original owner
or an authorized Merge helper before Review/Land of the original owner. This card
adds no automatic integration job or checkout borrowing mode.

### D-3. Persist a baseline before any delegate can work

Add nullable `RepairSourceTaskId`, `ProgressBaselineJson` and
`CompletionProgressEvidenceJson` to `AgentTask`; generate the EF migration with the
CLI. Use versioned typed payloads and additive DTOs, not arbitrary dictionaries in
the classifier. Preserve the source GUID as historical identity even if retention
removes the related task; do not cascade/delete the snapshot.

The baseline has a capture time and two bounded entries: the primary managed source,
and the optional repair source. Each records canonical repository/common directory,
task owner, canonical registered checkout (when present), exact `refs/heads/...`,
full local tip SHA, and remote observation state. Remote identity includes the
selected publication endpoint fingerprint and exact full ref, never credentials or
an exposed URL. Distinguish `Present(sha)`, `Missing`, `NotConfigured`, `Unavailable`.
An unavailable observation has a stable reason, not a fabricated null/zero baseline.
Store the primary file-probe cutoff alongside the SHA baseline.

Capture after managed checkout creation but persist in the existing transactional
dispatch claim before launch/brief delivery. The source SHA used to create the
repair must equal its captured local baseline. Capture for all new Code/Worktree
tasks so primary remote-only progress can also be checked. Do not require a remote
for a local-only repository. Remote failure permits dispatch with an explicit
unavailable baseline; failure to resolve the requested repair's local identity
refuses dispatch.

Persist once for the logical task. A resumed/requeued task with an existing baseline
must keep it, including unavailable states, and revalidate identity; it must not
rebaseline away work from an earlier attempt. A newly commissioned follow-up is a
new task and gets a new baseline. Capture retry is allowed only before the first
durable dispatch claim. Keep `WorktreeBaseSha` and its current landing/git-summary
meaning separate; do not repurpose or overwrite it from alternate evidence.

### D-4. Require a task-scoped claim for alternate or remote-only credit

For Code briefs add an optional, exact report line:

```text
[antiphon-progress:<this-task-full-guid> commit=<full-40-or-64-hex-sha>]
```

It is required to claim alternate-source or remote-only progress, not to settle a
normal task with direct primary checkout activity. Place it before the existing
next-stage block and closing report token. Parse only this task's correctly formed
line. Accept identical repetitions idempotently; conflicting SHAs or malformed
claims confer no credit and produce a warning. Do not mine arbitrary hexadecimal
strings, quoted predecessor reports or paths for authority. One resulting commit
claim suffices for this fix; it may be an ancestor of a later current tip.

For a candidate source, let `BL` and `BR` be dispatch local/remote tips and `T` a
fresh local or confirmed remote tip. A claimed commit `C` qualifies only when:

1. Repository, full ref and (for remote evidence) endpoint identity still match.
2. `C` is a full commit object reachable from `T`.
3. `C` was not reachable from ANY present baseline tip for that source.
4. The observed candidate lineage descends from its corresponding present baseline;
   for a newly published remote ref, it descends from the local dispatch baseline.
5. All baseline/object queries needed for those statements completed successfully.

This excludes an unchanged/pre-existing commit, a pre-existing remote-ahead commit
later fetched locally, movement of another ref, and a colleague's advance that the
task did not claim. Require the report claim even for the registered alternate's
local ref; dirty files or recent dates in someone else's checkout are not credit.
Task-scoped self-report plus Git corroboration is the attribution contract, not
cryptographic proof of sole authorship. Do not add author-email, card-name or scope
matching heuristics.

For the primary isolated local branch, a new reachable commit beyond the captured
baselines remains direct evidence without a claim; inspect graph differences, not
author dates or a 50-commit window. Preserve the existing primary file-mtime signal
for scope control, with honest availability and its limitations reported. Do not
count a changed HEAD from a different checked-out branch as isolated evidence.
For snapshotted tasks the completion service consumes the strict file result only;
`WorkspaceProgressArm.LastCommitAt` must not short-circuit the graph checks. File
and graph availability are separate, so a successful graph query is not made
unavailable by an unused legacy recent-log query.
Unavailable baseline history cannot establish alternate/remote novelty. Rewritten
history that cannot support the ancestry rule is indeterminate, not inactivity.

### D-5. Use three evidence outcomes; negative evidence requires complete probes

Keep the classifier's eligibility unchanged: explicit `done`, Code, Worktree.
Implement a concrete `TaskCompletionProgressService` coordinating typed Git I/O
through an `ITaskProgressGit` seam implemented in Infrastructure. Reuse existing
canonicalization, process gating and strict endpoint/ref parsing where possible.
Do not put additional shell execution into Application.

Probe only the stored primary source and optional repair source. Use the actual
remote full ref at the captured publication endpoint (`ls-remote --refs --exit-code`),
not a possibly stale `origin/...` tracking ref or a broad remote scan. Read local
objects first; if remote graph objects are missing, fetch only that exact source
into a task-specific observation ref under `refs/antiphon/progress/<task-id>/...`.
Bracket confirmation with remote reads and check fetched SHA/endpoint agreement.
Use bounded commands/retries and existing cancellation/process ownership machinery.
Fetch/pin mutations acquire the common-directory lease and child journal; contention
is unavailable evidence, never an unlocked fallback. Pin captured baseline objects
so later GC cannot erase the comparison; retain these evidence pins in this slice.
Keep pin creation bounded to these sources and the existing settlement/retry count;
probe polling must not accumulate a new permanent ref on every tick.
No checkout, reset, merge, push, branch deletion or worktree removal is part of probing.

| Evidence result | Settlement action |
|---|---|
| `ProgressObserved` from a qualifying primary or registered source | Keep the explicit `done` verdict. Store where/how it was corroborated. An independent positive can suffice even if another optional probe failed. |
| `NoAttributedProgress`, with every applicable negative query complete | Set existing `CompletedWithoutProgress` and Failed. Preserve the report and worktree. Explain that no attributable post-dispatch progress was detected at the named sources. |
| `Indeterminate` because a required baseline, identity or probe is unavailable | Preserve existing fail-open `done` behavior, with durable `progress=unavailable` evidence and a visible Warning. Never call this verified progress. |

Known remote absence and no configured remote are complete negative/not-applicable
answers, respectively. Authentication failure, nonzero status/log exit, malformed
output, timeout, missing commit objects, unstable endpoint/ref or unavailable lease
are not absence. Remote-only deletion after a previously present ref and history
rewrites are indeterminate. Caller cancellation propagates rather than settling.
If all reads succeeded but source movement has no qualifying task claim, record
`NoAttributedProgress` with an `unclaimed_or_unmatched_commit` reason; movement alone
does not rescue the task. If another required arm is unknown, the aggregate is
indeterminate instead. This distinction is essential to the concurrency regression.

Make `AgentFilesService.ProbeProgressAsync` preserve failures from both sub-probes:
catch non-cancellation errors, mark the arm unavailable if either needed negative
query failed, and retain any positive timestamp already found. Its Git methods need
a strict result path; fixing only the outer catch leaves the empty-list bug intact.
Keep best-effort Files UI wrappers compatible. Test stall-policy consumption of
partial positive arms so this safety fix does not make stalls more aggressive.

Remove the invented `0 commits / 0 changed files` counts from the failure sentence.
The retained primary mtime heuristic is an activity signal, not proof no work ever
occurred; full dirty-file fingerprinting and working-tree change attribution are
outside this fix.

### D-6. Surface evidence without transferring ownership

Persist the completion evidence with settlement, before notification: schema/version,
baseline identity, per-source observed local/remote tips, claimed and verified SHA,
actual registered alternate path, availability/reasons, evidence origin and final
assessment. Expose it on task detail and `delegate.ps1 -Status`. Emit one existing
Warning event per settlement when alternate evidence is used or evidence is
indeterminate; put the warning in the caller completion note as well as the detail
surface. A distillation must not be the only carrier of this diagnostic.

Use explicit wording such as `progress=repair-source; owner=<id>; commit=<sha>` or
`progress=unavailable; reason=<code>`. If both sources progressed, record both.
Do not relabel a branch observation as `landed`, tests passed, Review clean or
publication confirmed. Existing task-detail event rendering is sufficient; no new
dashboard or alert sink is required.

Never replace `WorktreePath`, `WorktreeBranch`, `MergeTargetRef`, landing receipts,
cleanup authority or the source owner's state with observed coordinates. When
completion is rescued solely by alternate/remote evidence and there is no direct
primary checkout progress, skip automatic merge-back/autocommit/empty-branch cleanup
and retain the completing task's tree with a warning. An indeterminate primary
probe likewise cannot authorize automatic mutation on a fail-open success.

All tasks carrying `RepairSourceTaskId` refuse explicit Land publication with a
named `repair_source_landing_owner_required` conflict pointing to the original
owner; enforce at normal Land admission and durable protocol entry. Their own
branches can integrate only through the separately authorized merge-back described
in D-2. Detection never authorizes cleanup of the source owner's worktree. Subsequent
Review and Land use the original owner and a fresh full expected source SHA under
existing CARD-0488/CARD-0495 rules.

### D-7. Compatibility and rejected alternatives

Existing rows retain null snapshots. Do not backfill using today's tip, parse old
briefs into source relationships, or automatically change historical Failed tasks.
For an unsnapshotted task keep the current primary-only heuristic, with corrected
probe availability; no new remote attribution without a durable baseline. New
prose-only instructions to work elsewhere are still unsupported: the caller must
use `-RepairSource`. Document this migration limit explicitly.

Rejected: scanning every worktree; treating `MergeTargetRef` as a source; accepting
any remote tip movement; forcing duplicate checkout; borrowing the owner's cwd;
rewriting task ownership after settlement; inventing dispatch SHAs from timestamps;
converting unknown evidence to Failed; adding leases lasting for an entire agent
session or a new worktree routing subsystem. These either repeat the attribution
bug, transfer authority, or exceed the narrow repair.

## Implementation slices and coverage targets

| Slice | Files / responsibilities | Named test targets |
|---|---|---|
| S1: contract and dispatch | `server/Domain/Entities/AgentTask.cs`, typed progress payloads, `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated migration, `server/Application/Dtos/AgentTaskDtos.cs`, `AgentTaskService.cs`, `AgentTaskDispatcher.cs`, `DelegationWorktreeService.cs`, `DelegationReportFormatter.cs`, `scripts/delegate.ps1`. Register/validate owner, route isolated branch, persist baseline before launch, compose claim contract. | Extend `AgentTaskServiceIntegrationTests`, `AgentTaskDispatchBaseGuardTests`, `DelegationWorktreeTests`; add narrowly named `RepairSourceDispatchTests` and `DelegateScriptRepairSourceTests` if isolation keeps existing fixtures simpler. |
| S2: evidence and settlement | New `server/Application/Services/TaskCompletionProgressService.cs`, `server/Application/Interfaces/ITaskProgressGit.cs`, `server/Infrastructure/Git/TaskProgressGit.cs`, typed DTOs and DI registration. `AgentFilesService.cs` / `GitWorkspaceService.cs` strict-result path. `AgentTaskReplyService.cs` classification, persistence and merge-back gate. | Add `TaskCompletionProgressPolicyTests` (Unit) and `TaskProgressGitTests` (Integration); extend `AgentTaskReplyIntegrationTests` and `TaskProgressPolicyFileArmTests`. |
| S3: authority and visibility | `AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs` repair-owner guards; task detail DTO/projection, `delegate.ps1 -Status`, existing completion formatter/events. Add client DTO typing only if required by the projection. | `AgentTaskLandAdmissionTests`, `AgentTaskLandBoundaryControlledTests`, `AgentTaskReplyIntegrationTests`, `DelegateScriptLandStatusTests`; assert zero owner mutations/receipts, not just warning prose. |
| S4: operator contract | Update `docs/orchestration-loop.md` with explicit repair ancestry/owner/integration recipe, `docs/ops-http.md`, `docs/antiphon-api.md`, and `.claude/skills/antiphon-delegate/SKILL.md` parameter reference after reading that owner. Update Code reporting guidance only as needed; compose task-specific identity in the formatter. | Script request/format tests and existing brief/InstructionBundle tests as affected; no runtime build for documentation alone. |

Ship the slices as one behavior change; do not enable alternate success without its
ownership fences and visible unavailable result. Keep production test filters and
landing verification policy unchanged.

## Acceptance cases for TestDesign

TestDesign must add its standard Verification design section, exact V/R methods,
guard inventory and later method-scoped PC variants. Required observable cases:

1. Two-worktree incident: owner branch already checked out, repair managed tree
   unchanged, new task-claimed commit on registered owner branch and remote.
   Succeeded, alternate metadata/warning, original owner unchanged, both trees retained,
   no automatic merge/cleanup and no repair-task publication authority.
2. Source occupancy at dispatch: unique repair branch starts at the recorded owner
   SHA with a different cwd. Ambiguous/foreign/replaced source refuses before session
   launch. Source landing in flight holds. Source metadata alone sets no merge target.
3. Primary or repair remote-only work made from a second clone: stale local refs do
   not hide a new claimed commit. Verify against a scratch bare remote and full graph,
   including a current tip ahead of the claimed commit.
4. Unchanged tips, old claimed SHA, remote already ahead at dispatch, a commit copied
   locally after dispatch, unrelated branch movement and concurrent unclaimed source
   movement confer no progress. With otherwise complete negatives the task fails.
5. Backdated commits and more than 50 intervening commits still qualify by baseline
   ancestry; a branch switch or a pre-existing future-dated commit does not qualify
   through the new commit path. Primary dirty-file behavior remains compatible.
6. Known missing/no-remote states differ from unavailable remote/baseline, malformed
   data, partial status/log failure, graph error, changed endpoint, ref instability,
   missing objects and cancellation. Unknown never produces CompletedWithoutProgress
   or owner mutation; a valid independent direct positive remains usable.
7. Baseline and evidence survive a fresh DbContext/server replay, requeue does not
   reset the baseline, claims are task-bound, and duplicate settlement produces one
   warning/note. A forged path/full ref in prose cannot enlarge the probe roots.
8. Direct own-worktree progress and non-Code/Shared/explicit failed or blocked report
   behavior stay intact. Null legacy baseline uses only the documented legacy path.
9. Explicit integration of a repair's own changes can use existing safe merge-back;
   alternate-only or indeterminate completion cannot. Original-owner fresh-SHA
   Review/Land continues through existing authority checks, and no completion evidence
   becomes a publication or cleanup receipt.

Use existing `ScratchGitRepo`, scratch bare remotes and isolated DB schemas; no live
incident worktree, network remote, production runner, provider or AppHost restart is
needed for test execution. Process-spawning test classes take the assembly-local
`ParallelLimiter<ProcessSpawnLimit>`. Prefer graph fixtures and controlled failures
over timestamp sleeps. TestDesign should cap this to the decision/authority matrix,
not duplicate the landing state-machine suite.

Code/Review validation: one isolated-output build, Unit lane, then named affected
integration classes above using supported TUnit OR filters and fresh nonzero TRX
counts. Follow `docs/testing-and-build.md`; run with `dotnet run`, not `dotnet test`.
Run affected script tests through their existing harness. No broad E2E, full assembly
or Pty suite is required unless TestDesign identifies an uncovered changed boundary.

## Delivery and decisions remaining

Defaults above are sufficient for TestDesign; no operator answer blocks the plan.
The caller must land this plan artifact before dependent stage work, then commission
TestDesign with this path. Implementation activation requires the normal canonical
checkout/server version check after approved landing, not a worktree restart. The
plan itself changes no running behavior and repairs no historical settlement.

## Verification design

TestDesign for the plan above (task d0d7596a, 2026-09-12, base master c8ddfe14). The fix design
is unchanged; this section adds the verification contract Code implements and Mutation executes.
Stable codes, DTO field names and warning wording below are the plan's defaults. Code may rename
one only by amending this section in the same commit, so that Review and Mutation read one vocabulary.

Design facts established while inspecting (Code must build to these, not around them):

- `TaskProgressPolicy.EvaluateAsync` (lines 126-146) discards the whole workspace arm when
  `Available == false`. D-5's "mark the arm unavailable, retain any positive timestamp" therefore
  makes stalls MORE aggressive unless the policy reads positive timestamps from an unavailable arm
  (or the arm gains per-signal availability). R-22 pins the required behaviour; the DTO shape is
  Code's choice.
- `DelegationReportFormatter.TryReadReportVerdict` strips only the closing token, so the
  `[antiphon-progress:...]` claim line stays inside the stored `Result`. V-17 asserts it verbatim.
- `AgentTaskLandService.FindWriterAsync` matches writers by worktree path/canonical directory or
  Shared common directory. An isolated repair tree is a different path, so a live repair does NOT
  hold the owner's landing. The plan leaves landing admission unchanged; recorded as out of scope.
- `LandingGitFixture.FixtureGit` (`BeforeCommand`/`AfterCommand`/`Trace`) is the controlled-failure
  shape for real git. `TaskProgressGit.RunAsync` must be `virtual` like `LandingGit.RunAsync` so the
  new `ControlledTaskProgressGit` test double can inject failures and trace commands.
- Existing settlement tests seed tasks with `CreateWorktreeForAsync` (no dispatcher tick) and
  therefore have a null baseline: they are the legacy path (D-7) and stay that way on purpose.
  Snapshotted behaviour needs a real `AgentTaskDispatcher.TickAsync`, which is what the new world
  fixture provides.

### Inspection

- Read in full: `AgentTaskReplyIntegrationTests` 1-140 (marker gate), 2560-3000 (merge-back and
  CompletedWithoutProgress section incl. `a_worktree_code_task_with_no_progress_fails_completed_without_progress`,
  `..._post_dispatch_commit_succeeds`, `..._changed_file_succeeds`, `unavailable_git_on_a_code_worktree_fails_open`,
  `a_shared_code_task_is_not_failed_for_zero_worktree_progress`, `a_plan_task_with_no_commits_does_not_warn`,
  `CreateWorktreeForAsync`), 3804-3996 (`CreateService`, `SeedDispatchedTaskAsync`, `SeedAgentAsync`,
  `SeedSessionAsync`, `SeedTurnAsync`, `ApplyClosingVerdict`), 4325-4396 (`TestScopeFactory`, `TempWorkspace`);
  `TaskProgressPolicyFileArmTests` (all 205 lines, `Scenario`); `DelegationWorktreeTests` 1-60 and 790-935
  (`NewTask`, `CreateLand`, `CreateService`, `ExpectedCoordinates`); `AgentTaskLandAdmissionTests` (all);
  `AgentTaskLandBoundaryControlledTests` 1-224; `AgentTaskDispatchBaseGuardTests` 1-80 and 220-381
  (`SeedKeptSiblingAsync`, `SeedQueuedWorktreeTaskAsync`, `SeedCardAsync`, `CreateProvider`);
  `AgentTaskServiceIntegrationTests` 292-410 and 1480-1587 (`NewRequest`, `ManualCaller`, `CreateService`);
  `DelegateScriptLandStatusTests`, `DelegateScriptTitleTests` 1-60 (`StubApi.LastBody`);
  `PostLandMutationWorktreeTests` 78-132 (`verification_publication_forbidden` refusal shape);
  `PostLandMutationDeliveryTests` 78-143 and `ConfirmQueuedReceiptAsync` (busy/eligible receipt);
  `LandingSourceFreshnessTests` 1-206; fixtures `ScratchGitRepo`, `LandingGitFixture`, `LandingSafetyHarness`,
  `LandingProtocolHarness`, `ControlledLandingGit`, `BridgeQueueHarness`, `LandApiStub`, `DelegateScriptRunner`,
  `DelegationTestServices`. Production: `AgentTaskReplyService` 690-760, 1371-1460, 2350-2400, 2856-2930;
  `AgentFilesService.ProbeProgressAsync`; `GitWorkspaceService.GetRecentCommitsAsync`/`GetChangesAsync`;
  `WorkspaceProgressArm`; `TaskProgressPolicy` 122-150, 216-228; `AttentionService` 1046-1090;
  `AgentTaskDispatcher.DispatchOneAsync` 2971-3140, 2080-2088, 2819-2846; `DelegationWorktreeService`
  151-215, 357-432; `AgentTaskLandService` 66-153, 192-215, `FindWriterAsync`; `AgentTaskLandingProtocol` 17-75;
  `LandingGit` 191-296; `DelegationReportFormatter` 27-120, 360-395, 523-573; `AgentTaskService` 190-200, 895-910;
  `AgentTask` entity fields; `CreateAgentTaskRequest` and `AgentTaskDetailDto`; `scripts/delegate.ps1` params and
  `-Status`; `server/Bundles/stage-code.md`; `docs/testing-and-build.md` 67-175, 239-290.
- Missing setup recorded: no fixture combines a real dispatcher tick (baseline capture inside the claim),
  a settlement through `AgentTaskReplyService`, a second registered worktree on a kept branch and a scratch
  bare remote. Add `tests/Antiphon.Tests/TestHelpers/RepairSourceWorld.cs`: isolated schema
  (`TestDbFixture.CreateIsolatedSchemaAsync`), `ScratchGitRepo` canonical with `remote.git` bare origin and
  master pushed, an OWNER task (Code/Worktree/Succeeded, real worktree via `DelegationWorktreeService`, one
  commit "owner work" on `feat/card-task-<owner>` pushed, `OwnerRef`/`OwnerSha`), a caller session, and a
  QUEUED repair task (`RepairSourceTaskId = Owner.Id`, `MergeTargetRef` null unless `explicitIntegration`).
  Provider = `AgentTaskDispatchBaseGuardTests.CreateProvider` shape plus the reply collaborators from
  `TestScopeFactory` (`AgentReviewCheckpointService`, `AgentFilesService`, `IWorkspaceProgressProbe`,
  `DeliverableBundleService`, `CapacityRecoveryService`, `ApiErrorRecoveryService`, `ModelAvailability`),
  `AddDelegationWorktreeGraph`, the new progress services, and a `ControlledTaskProgressGit` registered as
  `ITaskProgressGit` (`BeforeCommand`, `Trace`). Methods: `DispatchAsync()` (real `TickAsync`, returns the
  reloaded repair row and its session id), `SettleAsync(report)` (seeds marker prompt + assistant text +
  TurnEnd like `SeedTurnAsync`, then `AgentTaskReplyService.OnTurnEndAsync`), `ClaimLine(sha)`
  (`[antiphon-progress:<repair full guid> commit=<sha>]`), `CommitInOwnerTreeAsync(message, push)`,
  `CommitFromSecondCloneAsync(fullRef, message)` (clone `remote.git` to `clone/`, commit, push),
  `Snapshot()` (owner row, owner tree HEAD, local/remote owner tips, `git worktree list --porcelain -z`,
  repair tree HEAD), `ProgressPins()` (`for-each-ref refs/antiphon/progress/<repair:N>/`), `Note()`
  (caller `SessionQueuedMessages` row), `Warnings()`. Extract `SeedTurnAsync`/`ApplyClosingVerdict`
  (AgentTaskReplyIntegrationTests 3938-3996) into `TestHelpers/TurnSeeding.cs` and use it from both classes.
  Also add `ControlledTaskProgressGit` (subclass of the new Infrastructure `TaskProgressGit`, mirrors
  `LandingGitFixture.FixtureGit`) and an in-memory `FakeTaskProgressGit` (commit graph with parents, local
  refs, remote refs, symbolic HEAD per checkout, per-call failure and cancellation injection) for the Unit lane.
- Boundaries: source {primary only, primary+repair} x movement {none, local-only, remote-only, both,
  other-ref, unclaimed-by-another} x claim {none, this task, other task, malformed, conflicting, quoted,
  arbitrary hex} x baseline remote {Present, Missing, NotConfigured, Unavailable} x probe result
  {complete, status fail, log fail, ls-remote nonzero, exit 2, malformed advertisement, fetch fail,
  missing object, endpoint changed, ref unstable, lease busy, cancelled} x history {linear, backdated,
  >50 commits, rewritten, future-dated base, branch switch} x dispatch {fresh, adopted leftover, relaunch,
  legacy null baseline} x integration {none, explicit owner branch}. Mapped: V-1..V-8 (dispatch),
  V-9..V-16 and R-1..R-20 (policy and git layers), V-17..V-31 and R-21..R-24 (settlement, files, authority,
  visibility, docs), V-38/V-39 (delivery). Excluded with reasons in Out of scope: owner landing held by a live
  repair, live TUI brief delivery, arm64/remote-network endpoints, historical backfill.

### Delivery inventory

Durable identities: the repair task full GUID (in the claim line, the baseline, the evidence and the
`refs/antiphon/progress/<task:N>/` pins), the owner task GUID (`RepairSourceTaskId`, preserved even if the
owner row is retained-out), `SessionQueuedMessages.SourceTaskId` plus the CARD-0478 stamps
`CompletionNoteQueuedAt`/`CompletionNoteDigest` for the caller note.

| Path | Producer | Destination | Persistence boundary | Recovery | Observable receipt |
|---|---|---|---|---|---|
| Repair brief (content changed: owner, source ref, baseline SHA, assigned checkout/branch, integration flag, claim-line contract) | `AgentTaskDispatcher.DispatchOneAsync` after `CreateForTaskAsync`; `SessionMessageQueueService.EnqueueAsync` WhenIdle, Origin=Delegation | new delegate session's first `UserPrompt` | queued row inside the same dispatch flow; brief content composed from the committed baseline | unchanged CARD-0340 interrupted-launch resume; enqueue failure logs and leaves the queued row for redelivery | V-3 asserts the queued row body (substitute, see below); V-39 asserts a `UserPrompt` row carrying the repair block through `BridgeQueueHarness` |
| Dispatch baseline | `AgentTaskDispatcher` claim transaction | `AgentTasks.ProgressBaselineJson` (+ `WorktreeBaseSha` unchanged meaning) | committed with the claim, before session/launch/brief | crash before commit: no baseline, task Queued, leftover tree adopted next tick and baseline captured then (V-7); relaunch/retry keeps the existing JSON (R-8) | V-3/V-7 read the row from a fresh DbContext |
| Completion evidence | `AgentTaskReplyService` settlement | `AgentTasks.CompletionProgressEvidenceJson`, one Warning `AgentTaskEvent` | same SaveChanges as status/Result, before the note enqueue | duplicate settlement (same boundary re-entered) writes nothing new (V-26); a SaveFault before the note leaves evidence and note absent together, next boundary settles once | V-17/V-23/V-26 read the row and events from a fresh DbContext; `GET /api/agent-tasks/{id}` (V-29) |
| Caller completion note (content changed: `progress=` warning above the report) | `AgentTaskReplyService` -> `SessionQueuedMessages` (ParentSessionId) | caller session `UserPrompt` | queued row with `SourceTaskId` and CARD-0478 stamp | existing CARD-0478 `C478_V09a_LandCrashMatrix` cuts; V-38 reruns the `queue-inserted` and `lost-wakeup` cuts for this note | V-38: `ConfirmQueuedReceiptAsync` busy:true and busy:false asserts the caller's `UserPrompt` transcript row with the complete body |

Substitutes declared: in `RepairSourceWorld` there is no adapter, so V-3 proves only that the queued brief
row exists with the repair block; it cannot prove a TUI accepted it. V-39 closes that with the fake adapter
and a `UserPrompt` row, which proves server-side delivery, not a real TUI. `FakeTaskProgressGit` proves
policy decisions, not git behaviour; `TaskProgressGitTests` and the settlement classes run real git. An
accepted create request, a queued row, a `Succeeded` status, a Warning event or a `Sent` flag alone never
satisfies delivery acceptance in this design; the accepting evidence for the caller-facing warning is V-38's
`UserPrompt` row.

### Proves it works now

Layers: Unit = `TaskCompletionProgressPolicyTests` (`[Category("Unit")]`, `FakeTaskProgressGit`, no process);
Git = `tests/Antiphon.Tests/Infrastructure/TaskProgressGitTests.cs` (`[Category("Integration")]`,
`[ParallelLimiter<ProcessSpawnLimit>]`, `LandingGitFixture`-style repo + bare remote); Dispatch =
`tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs`; Settle =
`tests/Antiphon.Tests/Application/RepairSourceSettlementTests.cs` (both Integration, ProcessSpawnLimit,
`RepairSourceWorld`, isolated schema per test, added to `slow-tests-allowlist.txt`); Land =
`tests/Antiphon.Tests/Application/RepairSourceLandRefusalTests.cs` (Integration; `LandingSafetyHarness` for
`RequestAsync`, `LandingProtocolHarness` for `RunAsync`); Script = `DelegateScriptRepairSourceTests`
(`StubApi` pattern from `DelegateScriptTitleTests`, promoted to `TestHelpers/DelegateCreateStubApi.cs`) and
`DelegateScriptLandStatusTests`; Files = `TaskProgressPolicyFileArmTests`; Reply = `AgentTaskReplyIntegrationTests`.

Dispatch contract (S1):

- V-1: `-RepairSource <guid>` posts `repairSourceTaskId` as the full D-format GUID and nothing else changes | Script | `DelegateScriptRepairSourceTests.C499_V01_RepairSourcePostsTheFullOwnerGuid` (`-Role Code -Worktree -RepairSource <guid> -Goal ...`) | `LastBody.repairSourceTaskId == guid.ToString("D")`; no `mergeTargetRef` property; `workspace == "Worktree"`; a 422 body `{code:"repair_source_mode"}` from the stub surfaces as exit 1 with `repair_source_mode` in the output (`C499_V01b_ServerRefusalPassesThrough`).
- V-2: create-time acceptance and refusals | Dispatch (`AgentTaskService.CreateAsync` with `ManualCaller`, real `ScratchGitRepo`, seeded owner row) | `RepairSourceDispatchTests.C499_V02_FreshCodeWorktreeRepairIsAccepted` | row has `RepairSourceTaskId == owner.Id`, `Workspace == Worktree`, `MergeTargetRef == null` even though the caller is a Worktree parent with `WorktreeBranch` set (inheritance suppressed); a terminal owner (Succeeded) with a kept branch is accepted; `C499_V02b_ExplicitOwnerBranchTargetIsAccepted` persists `MergeTargetRef == owner.WorktreeBranch`.
- V-3: occupancy routing at dispatch | Dispatch (`RepairSourceWorld.DispatchAsync`, owner branch checked out at the owner tree) | `RepairSourceDispatchTests.C499_V03_OccupiedSourceRoutesToAnIsolatedBranchAtTheOwnerSha` | repair `Status == Dispatched`; `WorktreeBranch == feat/card-task-<repair short>`; `WorktreePath != Owner.WorktreePath`; `rev-parse HEAD` in the repair tree `== OwnerSha`; `symbolic-ref HEAD` in the owner tree still `refs/heads/<owner branch>`; `git worktree list --porcelain -z` has exactly one more entry than before; `ProgressBaselineJson` deserialises to `ProgressBaselineSnapshot` v1 with `primary.fullRef == refs/heads/feat/card-task-<repair>`, `primary.localSha == OwnerSha`, `repairSource.fullRef == OwnerRef`, `repairSource.localSha == OwnerSha`, `repairSource.remote.state == Present`, `repairSource.remote.sha == OwnerSha`, `repairSource.remote.endpointFingerprint` 64 hex, `fileProbeCutoff` within 5 s of `DispatchedAt`; one Warning event containing "occupied" and the owner tree path; the queued brief body (Origin=Delegation) contains the owner short id, `OwnerRef`, `OwnerSha`, the assigned path and branch, "integration: not requested" and the exact `[antiphon-progress:<repair D guid> commit=<full-40-or-64-hex-sha>]` contract line; `WorktreeBaseSha == OwnerSha`.
- V-4: dirty source is not copied | Dispatch | `C499_V04_DirtyOwnerFilesAreNotSnapshotted` (untracked `scratch.txt` and a modified tracked file in the owner tree before dispatch) | repair tree has neither change; Warning event says the snapshot excludes uncommitted source files; owner tree still dirty afterwards.
- V-5: identity refusal before any side effect | Dispatch | `C499_V05_AmbiguousOrMismatchedRegistrationRefusesBeforeLaunch` with `[Arguments("ambiguous")]` (second registration of the owner branch via `git worktree add --force`), `("mismatched")` (owner tree switched to another branch after create), `("replaced")` (owner branch deleted after create) | task `Status == Failed`, `FailureReason` contains `repair_source_identity_unavailable`, the owner short id and the registered path(s); `WorktreePath == null`; `AgentSessions` count unchanged; `AgentSessionLaunchQueue` has no entry for the task; no new directory under the worktree root.
- V-6: source landing in flight holds | Dispatch (`AgentTaskDispatchBaseGuardTests`, real repo, owner with `LandRequestedAt` set) | `AgentTaskDispatchBaseGuardTests.C499_V06_ARepairIsHeldWhileItsOwnerIsLanding` | first tick: `Status == Queued`, `WorktreePath == null`, one Held event naming the owner short id and "is landing"; clear `LandRequestedAt`; second tick: `Dispatched`, Held count stays 1, baseline persisted.
- V-7: baseline lives inside the claim; crash recovery | Dispatch (`RepairSourceWorld` with the `LandingSafetyHarness.SaveFault` interceptor registered on the DbContext, `AfterCommit=false`, faulting the claim's commit) | `C499_V07_ABaselineIsPersistedOnlyWithTheClaimAndRecapturedOnRetry` | faulted tick throws `InjectedSaveFailure`; row `Status == Queued`, `ProgressBaselineJson == null`, `DispatchedAt == null`; the leftover directory exists; second tick (fault cleared): `Dispatched`, `WorktreePath` equals the leftover path, `ProgressBaselineJson.primary.localSha == rev-parse HEAD` of the adopted tree `== OwnerSha`, one Dispatched event.
- V-8: brief and bundle contract | Reply/Unit (`InstructionBundleTests` and the brief composer test in `DelegationUnitTests`) | `InstructionBundleTests.C499_V08_CodeBriefCarriesTheTaskScopedProgressClaimContract` and `C499_V08b_RepairBriefNamesOwnerSourceBaselineAndAssignedCheckout` | the Code closing contract contains the literal `[antiphon-progress:<task D guid> commit=` line and says it goes before the `--- next stage ---` block; a non-repair Code brief contains the claim contract but no "Repair owner" block; a repair brief contains owner id, source full ref, baseline SHA, assigned path, assigned branch and "integration: not requested" / "integration: requested into <ref>".

Evidence policy (S2, Unit, `FakeTaskProgressGit`; `BL`/`BR` = baseline local/remote tips, `T` = current tip, `C` = claimed commit):

- V-9: two-worktree incident at policy level | Unit | `TaskCompletionProgressPolicyTests.C499_V09_ClaimedChildOfBothBaselinesOnTheRepairSourceIsProgress` (primary `T == BL`, clean; repair local `T = C`, remote `T = C`, `C -> BL`, claim `C`) | outcome `ProgressObserved`, `Origin == RepairSource`, `Commit == C`, `OwnerTaskId` set, `LocalObserved == C`, `RemoteObserved == C`.
- V-10: remote-only progress | Unit | `C499_V10_RemoteOnlyClaimedCommitIsProgress` (repair local `T == BL`, remote `T = C`) | `ProgressObserved`, `Origin == RepairSourceRemote`; the fake records a fetch into `refs/antiphon/progress/<task:N>/`.
- V-11: current tip ahead of the claim | Unit | `C499_V11_ClaimedAncestorOfALaterTipIsProgress` (`D -> C -> BL`, claim `C`) | `ProgressObserved`, `Commit == C`.
- V-12: dates and windows are irrelevant | Unit | `C499_V12_BackdatedAndDeepCommitsQualifyByAncestry` (`C` author date before dispatch; 60 commits between `BL` and `T`) | `ProgressObserved`; the fake's `log -50` was never called.
- V-13: primary direct evidence needs no claim | Unit | `C499_V13_PrimaryNewCommitIsProgressWithoutAClaim` (primary `T = C -> BL_p`, no claim line) | `ProgressObserved`, `Origin == Primary`.
- V-14: both sources progressed | Unit | `C499_V14_BothSourcesAreRecorded` | evidence lists two `ProgressObserved` sources; aggregate `ProgressObserved`.
- V-15: complete negatives fail | Unit | `C499_V15_UnchangedTipsWithCompleteQueriesIsNoAttributedProgress` (all tips equal baselines, clean files, no claim, every query complete) | `NoAttributedProgress`, `Reason == no_movement`, all five arms `Complete`.
- V-16: remote absence and no remote are complete answers | Unit | `C499_V16_MissingRemoteAndNoRemoteAreCompleteNegatives` (`[Arguments("Missing")]`, `("NotConfigured")` in the baseline and the probe) | `NoAttributedProgress` when local is also unchanged; never `Indeterminate`.

Git layer (S2, real git):

- V-32: exact ref observation at the captured endpoint | Git | `TaskProgressGitTests.C499_V32_ObserveExactRefAtCapturedEndpoint` (`[Arguments("present")]`, `("missing")` ref deleted on the remote, `("not-configured")` origin removed before capture) | `Present(sha)` equals `rev-parse` on the bare remote; `Missing` on `ls-remote` exit 2; `NotConfigured` when no origin; the command line contains `ls-remote --refs --exit-code <endpoint> <full ref>` and no `origin/` tracking ref is read.
- V-33: fetch, pin and reachability | Git | `C499_V33_FetchIntoTaskObservationRefAndAnswerAncestry` | after a remote-only push, `IsReachable(C, T)` true and `IsReachable(C, BL)` false; the fetch used `--no-tags --no-write-fetch-head` and the ref `refs/antiphon/progress/<task:N>/...`; `FETCH_HEAD` unchanged; `refs/heads/*` unchanged; pins exist for `BL` and `BR`.
- V-34: read/fetch race is correlated and bounded | Git | `C499_V34_ObservationRaceIsRetriedThenIndeterminate` (push between ls-remote and fetch once: accepted on retry; push on every fetch: `Unavailable(changed_during_confirmation)` after 3) | as stated.
- V-35: strict git results | Git (`GitWorkspaceService` through `AgentFilesService`) | `C499_V35_StrictStatusAndLogResultsSurfaceFailures` (a directory whose `.git` file points at a missing gitdir) | the strict path reports failure for both `status` and `log`; the best-effort Files UI wrappers still return empty lists.

Settlement (S2/S3, `RepairSourceWorld`, real dispatch then real settle):

- V-17: the two-worktree incident (acceptance 1; brief item 1) | Settle | `RepairSourceSettlementTests.C499_V17_TwoWorktreeIncidentSucceedsWithRepairSourceEvidence`: dispatch (V-3 shape); `CommitInOwnerTreeAsync("repair work", push: true)` yields `C`; repair tree untouched; `SettleAsync("Fixed it.\n" + ClaimLine(C) + "\n--- next stage ---\nnext: review\nhandoff: x\n" + ReportToken(done))` | `Status == Succeeded`, `FailureCode == null`; `Result` contains the claim line verbatim; `CompletionProgressEvidenceJson` deserialises with `assessment == ProgressObserved`, one source `origin == RepairSource`, `ownerTaskId == Owner.Id`, `claimedSha == C`, `verifiedSha == C`, `localObserved == C`, `remoteObserved == C`, `registeredPath == Owner.WorktreePath`; exactly one Warning event whose Detail contains `progress=repair-source; owner=<owner short>; commit=<C>`; the caller note `NoteHeader` and `Body` contain that phrase; owner row unchanged field-for-field (`Status`, `WorktreePath`, `WorktreeBranch`, `MergeTargetRef`, `LandRequestedAt`, `ActiveLandingId`, `ConcurrencyToken`); owner tree exists with HEAD `== C`; repair tree exists, registered, HEAD `== OwnerSha`; repair `WorktreePath`/`WorktreeBranch` unchanged and `MergeTargetRef == null`; no `Merged` event; `Trace` has no `rebase`, `--ff-only`, `push`, `checkout`, `reset`, `update-ref refs/heads`, `branch -D`, `worktree remove`; remote owner ref `== C`; `ProgressPins().Count` between 1 and 4; no ref under `refs/antiphon/land/`.
- V-18: remote-only from a second clone (acceptance 3) | Settle | `C499_V18_RemoteOnlyClaimedCommitFromASecondCloneIsProgress` (`CommitFromSecondCloneAsync(OwnerRef, "elsewhere")` yields `C`; `[Arguments(false)]` claim the tip, `[Arguments(true)]` push `D` on top and claim `C`) | `Succeeded`; evidence `origin == RepairSourceRemote`, `remoteObserved == tip`; local `refs/heads/<owner branch>` still `== OwnerSha` (stale, untouched); `Trace` has no fetch that writes under `refs/heads/` or `refs/remotes/`.
- V-19: primary remote-only with a claim; without a claim it fails | Settle | `C499_V19_PrimaryRemoteOnlyNeedsAClaim` (ordinary Code task, no repair source; delegate pushes its own branch from the second clone; `[Arguments(true)]` claim line present, `[Arguments(false)]` absent) | with claim: `Succeeded`, `origin == PrimaryRemote`; without: `Failed`, `CompletedWithoutProgress`, reason contains `unclaimed_or_unmatched_commit`.
- V-20: direct own-worktree progress unchanged with a baseline | Settle | `C499_V20_DirectPrimaryCommitAndDirtyFileStillSucceedWithoutAClaim` (`[Arguments("commit")]`, `("dirty")`) | `Succeeded`, evidence `origin == Primary`, no Warning containing `progress=`; existing `a_worktree_code_task_with_a_post_dispatch_commit_succeeds` and `..._changed_file_succeeds` stay green untouched.
- V-21: corrected failure wording (acceptance 4) | Settle | `C499_V21_NoMovementFailsWithNamedSourcesAndNoInventedCounts` | `Failed`, `CompletedWithoutProgress`; `FailureReason` contains `no attributable post-dispatch progress`, the repair `WorktreePath` and `OwnerRef`; contains neither `0 commits` nor `0 changed`; the Failed event, the `DelegateCompletedWithoutProgress` incident and the caller note carry the same sentence; repair tree retained; `Trace` has no mutation.
- V-22: concurrent unclaimed movement is not progress | Settle | `C499_V22_UnclaimedMovementOnTheOwnerBranchDoesNotRescue` (owner commits `X` in its own tree after dispatch; report has no claim line) | `Failed`, reason contains `unclaimed_or_unmatched_commit`; evidence `localObserved == X`, `claim == null`; owner tree HEAD still `X`.
- V-23: evidence unavailable is not inactivity (acceptance 6; brief item 4) | Settle | `C499_V23_UnavailableEvidenceFailsOpenWithADurableWarning` with `[Arguments("ls-remote-nonzero")]` (`BeforeCommand` returns exit 128 for `ls-remote`), `("malformed-advertisement")`, `("fetch-fails")`, `("status-nonzero")` (primary `status --porcelain` exit 128), `("log-nonzero")`, `("lease-busy")` (acquire the repository lease in the test before settling), `("missing-worktree-dir")` (delete the repair tree) | `Status == Succeeded`, `FailureCode == null`; evidence `assessment == Indeterminate`, `reason` one of `source_remote_unreadable`, `source_remote_response_invalid`, `source_remote_fetch_failed`, `primary_status_unavailable`, `primary_log_unavailable`, `repository_lease_busy`, `primary_checkout_missing`; one Warning `progress=unavailable; reason=<code>`; note carries it; no `Merged` event; `Trace` has no mutation; for `lease-busy` the trace has no `fetch` at all.
- V-24: ownership preservation after an alternate-only success (acceptance 1/9; brief item 5) | Settle + Land | `C499_V24_AlternateSuccessNeverAuthorisesIntegrationOrCleanup`: after the V-17 settlement, (i) assert the V-17 no-mutation set again after a second `OnTurnEndAsync` of the same boundary; (ii) `AgentTaskLandService.RequestAsync(repair.Id, new(expectedSourceSha: C))` throws `ConflictException` with `Code == repair_source_landing_owner_required` and a message naming the owner short id; `AgentTaskLandRequests` count 0; `Queue.IsActive(repair.Id)` false; (iii) `AgentTaskLandingProtocol.RunAsync(repairTask, lease)` throws the same code; `AgentTaskLandings` count 0; (iv) `RequestAsync(owner.Id, new(expectedSourceSha: C))` returns `queued` (owner authority intact) | as stated.
- V-25: explicit integration of the repair's own work still merges back; alternate-only and indeterminate do not | Settle | `C499_V25_MergeBackOnlyForDirectWorkWithAnExplicitTarget` with `[Arguments("direct-explicit")]` (repair created with `MergeTargetRef == owner branch`, commit in the repair tree) , `("alternate-explicit")` (same target, work only on the owner branch with a claim), `("indeterminate-explicit")` (same target, dirty file in the repair tree, primary `status` forced nonzero) | direct: `Merged` event, owner branch tip advanced to contain the repair commit while still checked out in the owner tree (`the_target_advances_even_while_checked_out_in_the_main_repo` shape), repair tree removed; alternate: `Succeeded`, no `Merged` event, note header contains `branch feat/card-task-<repair> left for review` or `verification retained` wording chosen by Code but NOT `merged`, repair tree present; indeterminate: `Succeeded`, no `Merged`, the dirty file still uncommitted (`status --porcelain` non-empty), no `CommitAllChangesAsync` in `Trace`.
- V-26: persistence and idempotence (acceptance 7) | Settle | `C499_V26_BaselineAndEvidenceSurviveReplayAndDuplicateSettlement` | after V-17: a fresh `AppDbContext` reads identical `ProgressBaselineJson` bytes to those read before settlement; `CompletionProgressEvidenceJson.schemaVersion == 1`; second `OnTurnEndAsync` on the same session: Warning events with `progress=` count stays 1, `SessionQueuedMessages` for the caller with `SourceTaskId == repair.Id` count stays 1, `CompletionNoteQueuedAt` unchanged.
- V-27: claims are task-bound and prose cannot enlarge the probe (acceptance 7) | Settle | `C499_V27_ForeignClaimAndProsePathsConferNothing` (report carries `[antiphon-progress:<other guid> commit=<C>]`, plus prose "pushed refs/heads/master at <canonical path>" and a bare 40-hex string) | `Failed`, evidence `claim == null` with `claimWarning == claim_not_for_this_task`; evidence `sources.Count == 2` (primary and registered repair source only); one Warning naming the malformed/foreign claim.
- V-28: other roles and verdicts untouched (acceptance 8) | Settle + Reply | `C499_V28_FailedAndBlockedReportsSkipTheProbe` (`[Arguments("failed")]`, `("blocked")`) and existing `a_shared_code_task_is_not_failed_for_zero_worktree_progress`, `a_plan_task_with_no_commits_does_not_warn`, `a_shared_task_reports_git_unattributable` | failed: `Failed` with the report's first line, `CompletionProgressEvidenceJson == null`, `Trace` has no `ls-remote`; blocked: `Blocked`, no evidence, session kept; existing three stay green untouched.
- V-29: visibility on detail and `-Status` (S3) | Reply + Script | `C499_V29_DetailProjectsProgressEvidence` (`AgentTaskService.GetDetailAsync` on the V-17 row) and `DelegateScriptLandStatusTests.C499_V29b_StatusPrintsProgressEvidence` (`taskStatusBody` with `progressEvidence: { assessment: "ProgressObserved", sources: [{ origin: "RepairSource", ownerTaskId, commit }] }` and a second scenario `assessment: "Indeterminate", reason: "source_remote_unreadable"`) | detail DTO `ProgressEvidence.Assessment == ProgressObserved`, `Sources[0].Origin == RepairSource`, `Sources[0].Commit == C`; script output contains `Progress: repair-source; owner <guid>; commit <sha>` and, for the second, `Progress: unavailable; reason source_remote_unreadable`, both printed before `EXACT REPORT AFTER FACTS`.
- V-30: pins are bounded | Settle | `C499_V30_ProbePinsAreBoundedAcrossRetries` (settle, then invoke the completion probe twice more through the service for the same task) | `ProgressPins().Count` after three probes `<= 4`; no ref created under `refs/antiphon/land/`; `refs/heads/*` unchanged.
- V-31: operator documentation (S4) | Unit | `RepairSourceDocumentationTests.C499_V31_DocsAndSkillNameTheRepairContract` | `docs/orchestration-loop.md` contains `-RepairSource` and `repair_source_landing_owner_required`; `docs/ops-http.md` and `docs/antiphon-api.md` contain `repairSourceTaskId` and `progressEvidence`; `.claude/skills/antiphon-delegate/SKILL.md` contains `-RepairSource`; `server/Bundles/stage-code.md` or the composed Code contract contains `[antiphon-progress:`.
- V-38: caller note reaches a busy and an eligible caller through the real queue | Settle + `BridgeQueueHarness` | `C499_V38_ProgressWarningReachesTheCallerSession` (`[Arguments(true)]` busy caller, `[Arguments(false)]` idle; settle the V-17 world with `ParentSessionId = bridge.SessionId` using the reply service built over `bridge.Provider`, then `ConfirmQueuedReceiptAsync` (promoted from `PostLandMutationDeliveryTests` to `TestHelpers/QueuedReceiptAssertions.cs`) and additionally the `queue-inserted` and `lost-wakeup` cuts) | the caller's `UserPrompt` transcript row contains the complete note body including `progress=repair-source; owner=`; busy caller receives it only after `SetWorkingAsync(false)`; each cut ends with exactly one `UserPrompt` row.
- V-39: repair brief reaches the delegate session | Dispatch + `BridgeQueueHarness` | `C499_V39_RepairBriefIsTypedWithTheRepairBlock` (dispatch via a provider whose queue/runtime are the bridge's; drive the fake adapter for the new session) | a `UserPrompt` row on the delegate session contains the task marker, the owner short id, `OwnerRef`, `OwnerSha` and the claim contract line.

### Guards the regression

- R-1: unchanged tips with a claim of `BL` | `TaskCompletionProgressPolicyTests.C499_R01_ClaimOfTheBaselineTipIsNotNovel` | `Outcome == NoAttributedProgress`, `Reason == claimed_commit_not_novel`.
- R-2: old claimed SHA (ancestor of `BL`) | `C499_R02_ClaimOfAnAncestorOfTheBaselineIsNotNovel` | `NoAttributedProgress`.
- R-3: remote already ahead at dispatch (`BR -> BL`, claim `BR`) | `C499_R03_RemoteAheadAtDispatchIsNotProgress` | `NoAttributedProgress`; decisive: the reachability check ran against `BR`, not only `BL`.
- R-4: pre-existing remote commit fetched locally after dispatch (local `T == BR`) | `C499_R04_CommitCopiedLocallyAfterDispatchIsNotProgress` | `NoAttributedProgress`.
- R-5: unrelated ref movement (another branch advanced; source ref unchanged) | `C499_R05_MovementOfAnotherRefIsNotProgress` | `NoAttributedProgress`, `Reason == no_movement`; the fake shows no query against the other ref.
- R-6: concurrent unclaimed movement on the source (`T = X -> BL`, no claim) | `C499_R06_UnclaimedSourceMovementIsNotProgress` | `NoAttributedProgress`, `Reason == unclaimed_or_unmatched_commit`, `LocalObserved == X`.
- R-7: rewritten history (`T` does not contain `BL`) | `C499_R07_RewrittenHistoryIsIndeterminate` | `Indeterminate`, `Reason == baseline_lineage_broken`; never `NoAttributedProgress`.
- R-8: requeue/relaunch keeps the baseline | `RepairSourceDispatchTests.C499_R08_RelaunchAndRetryKeepTheOriginalBaseline` (dispatch; owner advances; `AgentTaskDispatcher.RelaunchWedgedAsync` and separately `AgentTaskService.RetryAsync` + tick) | `ProgressBaselineJson` byte-equal to the original; a newly commissioned follow-up (fresh task with `RepairSourceTaskId`) gets a different `capturedAt` and the advanced tip.
- R-9: quoted predecessor claim and arbitrary hex | `C499_R09_QuotedAndArbitraryHexAreNotClaims` (body quotes a prior report's `[antiphon-progress:<this guid> commit=<C>]` inside a fenced block and lists a bare 40-hex) | parser yields no claim from the fenced block only when the line is not a top-level line; decisive: `Outcome == NoAttributedProgress`, `ClaimWarning == claim_malformed_or_quoted`. (Code defines "top-level" as a line that is not inside a fenced code block and is not prefixed by `>`.)
- R-10: task-bound claim | `C499_R10_ClaimMustNameThisTask` (`[antiphon-progress:<other guid> commit=<C>]`) | `Claim == null`, `NoAttributedProgress`.
- R-11: conflicting SHAs; identical repeats | `C499_R11_ConflictingClaimsConferNoCreditAndRepeatsAreIdempotent` (`[Arguments("conflict")]`, `("repeat")`) | conflict: `Claim == null`, `ClaimWarning == claim_conflicting`, `NoAttributedProgress`; repeat: `Claim == C`, `ProgressObserved`, one warning at most.
- R-12: claim not reachable from the tip | `C499_R12_ClaimNotReachableFromTheTipIsRefused` (`C` on an unrelated branch) | `NoAttributedProgress`, `Reason == claimed_commit_unreachable`.
- R-13: primary HEAD on another branch | `C499_R13_ABranchSwitchInThePrimaryTreeIsNotIsolatedEvidence` (fake symbolic HEAD `refs/heads/other` with a new commit) | primary arm yields no commit evidence; aggregate `NoAttributedProgress` when clean, `Indeterminate` when the branch check itself fails.
- R-14: malformed claim shapes | `C499_R14_MalformedClaimsAreIgnored` (`[Arguments]` short SHA, uppercase mixed, missing `commit=`, trailing text) | `Claim == null`, `ClaimWarning == claim_malformed_or_quoted`.
- R-15: identity drift | `C499_R15_ChangedEndpointRefOrRepositoryIsIndeterminate` (`[Arguments("endpoint")]`, `("ref")`, `("repository")`) | `Indeterminate`, `Reason` one of `source_remote_endpoint_changed`, `source_ref_changed`, `source_repository_changed`.
- R-16: lease contention on fetch/pin | `TaskProgressGitTests.C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch` (hold the repository lease in the test) | `Unavailable(repository_lease_busy)`; `Trace` contains no `fetch` and no `update-ref`.
- R-17: cancellation propagates | `C499_R17_CancellationPropagates` (fake throws `OperationCanceledException` on `ls-remote`) and `RepairSourceSettlementTests.C499_R17b_CancelledSettlementLeavesTheTaskDispatched` | Unit: `Should.ThrowAsync<OperationCanceledException>`; Settle: `Status == Dispatched`, no evidence, no note.
- R-18: independent positive with a failed sibling arm | `C499_R18_APrimaryPositiveSurvivesARepairProbeFailure` | `ProgressObserved`, `Origin == Primary`, evidence records the repair arm as `Unavailable`.
- R-19: unknown arm plus negative arm | `C499_R19_AnUnknownRequiredArmMakesTheAggregateIndeterminate` (primary complete negative; repair ls-remote fails) | `Indeterminate`, never `NoAttributedProgress`.
- R-20: unavailable baseline cannot establish novelty | `C499_R20_UnavailableBaselineRemoteCannotCreditARemoteClaim` (baseline remote `Unavailable(reason)`, probe remote `Present(C)`, claim `C`) | `Indeterminate`, `Reason == baseline_remote_unavailable`.
- R-21: files arm honesty | `TaskProgressPolicyFileArmTests.C499_R21_AFailedSubProbeMarksTheArmUnavailableAndKeepsPositives` (`AgentFilesService` over a `GitWorkspaceService` stub whose `status` fails, `log` succeeds; then the inverse; then a `.git` file pointing at a missing gitdir) | `Available == false` in all three; `LastFileChangeAt` retained when the file scan succeeded before the log failure; `IsRepositoryAsync == true` plus a failing `status` never yields `Available == true` with both signals null.
- R-22: stall policy consumes a partial positive | `TaskProgressPolicyFileArmTests.C499_R22_APartialPositiveArmStillWithholdsTheStall` (`new WorkspaceProgressArm(Available:false, LastFileChangeAt: now-3m, LastCommitAt: null, false)` or the equivalent per-signal shape) | `EvaluateAsync` returns null; `No_workspace_arm_leaves_the_transcript_verdict_standing` still passes unchanged.
- R-23: legacy timestamp must not short-circuit the graph | `RepairSourceSettlementTests.C499_R23_AFutureDatedBaseCommitDoesNotRescueASnapshottedTask` (before dispatch, the owner commit is created with `GIT_COMMITTER_DATE`/`GIT_AUTHOR_DATE` one hour in the future; after dispatch nothing changes; report has no claim) | `Failed`, `CompletedWithoutProgress`; evidence `primary.arm.lastCommitAt` is recorded but `assessment == NoAttributedProgress`.
- R-25: forbidden `-RepairSource` combinations | `RepairSourceDispatchTests.C499_R25_ForbiddenCombinationsAreRefused` (`[Arguments("source-landing")]`, `("pinned-agent")` `AgentId` set, `("follow-up")` `FollowUpOnTask`, `("shared")`, `("read-only")`, `("plan-role")`, `("orchestrator-kind")`) | `Should.ThrowAsync<ValidationException>` with `Errors` keyed `RepairSourceTaskId` and code `repair_source_mode`; no task row created.
- R-26: invalid owner | `C499_R26_AForeignOwnerIsRefused` (`[Arguments("other-repo")]` owner in a second `ScratchGitRepo`, `("unknown-guid")`, `("no-branch")` owner with `WorktreeBranch == null`, `("published")` owner with a confirmed `AgentTaskLanding`, `("plan-owner")` owner role Plan) | codes `repair_source_owner_invalid`, `repair_source_not_found`, `repair_source_owner_invalid`, `repair_source_published`, `repair_source_owner_invalid`; knowing the GUID of a task outside the caller's allowed roots is refused the same way as an unknown GUID.
- R-27: explicit merge target other than the owner's branch | `C499_R27_ADifferentExplicitMergeTargetIsRefused` (`MergeTargetRef = "master"`) | `ValidationException` code `repair_source_merge_target_mismatch`.
- R-9b: nonzero `ls-remote` is unavailable, not missing | `TaskProgressGitTests.C499_R09b_UnreadableRemoteIsUnavailableNotMissing` (origin URL rewritten to a nonexistent directory after capture, fingerprint compared against the rewritten value so only readability fails) | `State == Unavailable`, `Reason == source_remote_unreadable`; the exit-2 case in V-32 stays `Missing`.
- R-37: local identity failure refuses dispatch | `RepairSourceDispatchTests.C499_R37_LocalIdentityFailureRefusesDispatch` (`ControlledTaskProgressGit` forces `rev-parse --verify <owner ref>^{commit}` to exit 128 during capture) | task `Status == Failed`, `FailureReason` contains `repair_source_identity_unavailable`, `ProgressBaselineJson == null`, no session, no worktree; a remote failure in the same position (`ls-remote` exit 128) instead dispatches with `repairSource.remote.state == Unavailable` and a Warning (`C499_V07b_RemoteFailureStillDispatchesWithAnUnavailableBaseline`).
- R-24: legacy null-baseline path | `AgentTaskReplyIntegrationTests.a_legacy_task_without_a_baseline_gets_no_remote_credit` (seeded via `CreateWorktreeForAsync`, `ProgressBaselineJson == null`; delegate pushes its branch from a second clone and claims `C`; managed tree unchanged) and `unavailable_git_on_a_code_worktree_fails_open` extended | legacy: `Failed`, `CompletedWithoutProgress`, `FailureReason` contains `no attributable post-dispatch progress` and not `0 commits`; extended fail-open: `Succeeded` plus one Warning `progress=unavailable; reason=primary_checkout_missing` and non-null evidence with `assessment == Indeterminate`. Existing `a_worktree_code_task_with_no_progress_fails_completed_without_progress` must drop its `"0 commits"` assertion and assert `no attributable post-dispatch progress` on all four surfaces.

### Guard inventory

- G-1: D-1 `-RepairSource` combination refusal (SourceLanding, standing/pinned agent, follow-up reuse, Shared/ReadOnly, non-Code role, non-Worker kind) with code `repair_source_mode` | PC-1
- G-2: D-1 owner validity: Code/Worktree owner in the same project and canonical common directory with complete source coordinates and no confirmed publication; `repair_source_not_found`, `repair_source_owner_invalid`, `repair_source_published` | PC-2
- G-3: D-1 explicit merge target other than the owner's branch refused (`repair_source_merge_target_mismatch`); absent target persists null, never the parent's branch | PC-3
- G-4: D-2 occupied source routes to a unique branch created at the resolved owner SHA in a new checkout | PC-4
- G-5: D-2 unknown/multiple/mismatched registrations refuse (`repair_source_identity_unavailable`) before any worktree or session exists | PC-5
- G-6: D-2 source landing in flight holds the repair (Held, Queued, no worktree) | PC-6
- G-7: D-3 baseline persists only inside the durable dispatch claim; a faulted claim leaves none; retry recaptures | PC-7
- G-8: D-3 relaunch/retry keeps the existing baseline byte-for-byte | PC-8
- G-9: D-3/D-5 a nonzero `ls-remote` is `Unavailable(reason)`, never `Missing`; exit 2 is `Missing`; no origin is `NotConfigured` | PC-9
- G-10: D-4 a claim must carry this task's full GUID | PC-10
- G-11: D-4 conflicting claims confer no credit and warn | PC-11
- G-12: D-4 rule 2, `C` reachable from `T` | PC-12
- G-13: D-4 rule 3, `C` not reachable from ANY present baseline tip, remote included | PC-13
- G-14: D-4 rule 4, broken baseline lineage is `Indeterminate` | PC-14
- G-15: D-4 rule 1, repository/full ref/endpoint identity must still match | PC-15
- G-16: D-4 alternate or remote-only movement without a claim is `unclaimed_or_unmatched_commit`, not credit | PC-16
- G-17: D-4 snapshotted primary evidence comes from the graph; `WorkspaceProgressArm.LastCommitAt` cannot short-circuit | PC-17
- G-18: D-4 a primary HEAD on a different branch is not isolated commit evidence | PC-18
- G-19: D-5 a negative verdict requires every applicable query complete; an unknown required arm makes the aggregate `Indeterminate` | PC-19
- G-20: D-5 `Indeterminate` keeps the explicit `done` (Succeeded) with a Warning; never `CompletedWithoutProgress` | PC-20
- G-21: D-5 an independent positive suffices when another optional probe failed | PC-21
- G-22: D-5 `AgentFilesService.ProbeProgressAsync` marks the arm unavailable when a needed negative sub-probe failed | PC-22
- G-23: D-5 a partial positive still withholds a stall in `TaskProgressPolicy` | PC-23
- G-24: D-5 fetch/pin run only under the repository lease; contention is unavailable evidence | PC-24
- G-25: D-5 probing performs no checkout, reset, merge, push, branch deletion, `refs/heads` update or worktree removal | PC-25
- G-26: D-5 caller cancellation propagates instead of settling | PC-26
- G-27: D-6 alternate-only success skips merge-back, autocommit and empty-branch cleanup and retains the tree | PC-27
- G-28: D-6 an indeterminate primary never authorises automatic autocommit/merge-back | PC-28
- G-29: D-6 `WorktreePath`, `WorktreeBranch`, `MergeTargetRef` and the owner's state are never overwritten from evidence | PC-29
- G-30: D-6 Land admission (`RequestAsync`) refuses a task with `RepairSourceTaskId` (`repair_source_landing_owner_required`) | PC-30
- G-31: D-6 durable protocol entry (`AgentTaskLandingProtocol.RunAsync`) refuses the same independently | PC-31
- G-32: D-6 evidence persists with settlement before notification; duplicate settlement adds nothing | PC-32
- G-33: D-6 the progress warning is on the task event AND in the caller note, not only a distillation | PC-33
- G-34: D-7 null-baseline rows get the primary-only legacy heuristic; no remote attribution | PC-34
- G-35: D-5 observation pins are bounded per source and attempt; polling never adds a permanent ref | PC-35
- G-36: D-4 only a top-level, exact claim line is parsed; quoted blocks, arbitrary hex and prose paths confer nothing | PC-36
- G-37: D-3 every new Code/Worktree task gets a baseline; failure to resolve the local identity refuses dispatch rather than persisting a fabricated baseline | PC-37

Guards=37, mapped=37, missing=0, duplicate PC mappings=0. No safety-critical guard is left without a control.

### Positive controls

Each PC: apply the mutation, run only the named method with
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-pc/ -- --treenode-filter "/*/*/<Class>/<Method>"`,
confirm the named assertion is the red one (a build failure, fixture error or zero-test run is not red),
restore the source (refresh the timestamp), run the same method green. Batches may combine only controls in
different files and methods. Mutation reports break, red, restore, green after land.

- PC-1: break G-1 by deleting the `request.SourceLandingOperationId is not null` term from the repair combination check in `AgentTaskService.CreateAsync`; expect `RepairSourceDispatchTests.C499_R25_ForbiddenCombinationsAreRefused` (`[Arguments("source-landing")]`) red at `Should.ThrowAsync<ValidationException>` (no exception thrown).
- PC-2: break G-2 by skipping the common-directory equality between owner and request repository; expect `RepairSourceDispatchTests.C499_R26_AForeignOwnerIsRefused` red at `Should.ThrowAsync<ValidationException>` for code `repair_source_owner_invalid`.
- PC-3: break G-3 by applying the ordinary `parent?.WorktreeBranch ?? parent?.MergeTargetRef` inheritance to repair tasks; expect `C499_V02_FreshCodeWorktreeRepairIsAccepted` red at `MergeTargetRef.ShouldBeNull()`.
- PC-4: break G-4 by creating the repair branch from `MergeTargetRef ?? "HEAD"` instead of the resolved owner SHA; expect `C499_V03_OccupiedSourceRoutesToAnIsolatedBranchAtTheOwnerSha` red at `repairHead.ShouldBe(world.OwnerSha)`.
- PC-5: break G-5 by accepting the first registration when the owner branch is registered twice; expect `C499_V05_AmbiguousOrMismatchedRegistrationRefusesBeforeLaunch("ambiguous")` red at `Status.ShouldBe(AgentTaskStatus.Failed)`.
- PC-6: break G-6 by not consulting the owner's `LandRequestedAt`; expect `AgentTaskDispatchBaseGuardTests.C499_V06_ARepairIsHeldWhileItsOwnerIsLanding` red at `held.Status.ShouldBe(AgentTaskStatus.Queued)`.
- PC-7: break G-7 by saving `ProgressBaselineJson` in its own `SaveChangesAsync` before the `FOR UPDATE` claim re-read; expect `C499_V07_ABaselineIsPersistedOnlyWithTheClaimAndRecapturedOnRetry` red at `ProgressBaselineJson.ShouldBeNull()` after the faulted tick.
- PC-8: break G-8 by recapturing the baseline on every dispatch attempt; expect `C499_R08_RelaunchAndRetryKeepTheOriginalBaseline` red at `ProgressBaselineJson.ShouldBe(original)`.
- PC-9: break G-9 by mapping any nonzero `ls-remote` exit to `Missing`; expect `TaskProgressGitTests.C499_R09b_UnreadableRemoteIsUnavailableNotMissing` red at `State.ShouldBe(RemoteState.Unavailable)`.
- PC-10: break G-10 by accepting any well-formed GUID in the claim line; expect `TaskCompletionProgressPolicyTests.C499_R10_ClaimMustNameThisTask` red at `Claim.ShouldBeNull()`.
- PC-11: break G-11 by taking the last of two conflicting SHAs; expect `C499_R11_ConflictingClaimsConferNoCreditAndRepeatsAreIdempotent("conflict")` red at `Outcome.ShouldBe(NoAttributedProgress)`.
- PC-12: break G-12 by skipping the `C` reachable-from-`T` check; expect `C499_R12_ClaimNotReachableFromTheTipIsRefused` red at `Outcome.ShouldBe(NoAttributedProgress)`.
- PC-13: break G-13 by checking novelty against the local baseline tip only; expect `C499_R03_RemoteAheadAtDispatchIsNotProgress` red at `Outcome.ShouldBe(NoAttributedProgress)`.
- PC-14: break G-14 by treating a failed baseline-ancestry check as `NoAttributedProgress`; expect `C499_R07_RewrittenHistoryIsIndeterminate` red at `Outcome.ShouldBe(Indeterminate)`.
- PC-15: break G-15 by ignoring the endpoint fingerprint comparison; expect `C499_R15_ChangedEndpointRefOrRepositoryIsIndeterminate("endpoint")` red at `Outcome.ShouldBe(Indeterminate)`.
- PC-16: break G-16 by crediting any source movement beyond the baseline without a claim; expect `C499_R06_UnclaimedSourceMovementIsNotProgress` red at `Outcome.ShouldBe(NoAttributedProgress)`.
- PC-17: break G-17 by returning `ProgressObserved` whenever `arm.LastCommitAt` is non-null on a snapshotted task; expect `RepairSourceSettlementTests.C499_R23_AFutureDatedBaseCommitDoesNotRescueASnapshottedTask` red at `Status.ShouldBe(AgentTaskStatus.Failed)`.
- PC-18: break G-18 by using `rev-parse HEAD` movement without the `symbolic-ref` branch check; expect `C499_R13_ABranchSwitchInThePrimaryTreeIsNotIsolatedEvidence` red at `Outcome.ShouldBe(NoAttributedProgress)`.
- PC-19: break G-19 by aggregating `NoAttributedProgress` when any arm is a complete negative regardless of unknown arms; expect `C499_R19_AnUnknownRequiredArmMakesTheAggregateIndeterminate` red at `Outcome.ShouldBe(Indeterminate)`.
- PC-20: break G-20 by mapping `Indeterminate` to `CompletedWithoutProgress` in `AgentTaskReplyService`; expect `C499_V23_UnavailableEvidenceFailsOpenWithADurableWarning("ls-remote-nonzero")` red at `Status.ShouldBe(AgentTaskStatus.Succeeded)`.
- PC-21: break G-21 by requiring every arm available before returning a positive; expect `C499_R18_APrimaryPositiveSurvivesARepairProbeFailure` red at `Outcome.ShouldBe(ProgressObserved)`.
- PC-22: break G-22 by keeping `return new WorkspaceProgressArm(true, lastFile, lastCommit, sharedCheckout)` after a caught sub-probe failure; expect `TaskProgressPolicyFileArmTests.C499_R21_AFailedSubProbeMarksTheArmUnavailableAndKeepsPositives` red at `Available.ShouldBeFalse()`.
- PC-23: break G-23 by restoring the `workspace is { Available: true } arm` gate around timestamp consumption in `TaskProgressPolicy`; expect `C499_R22_APartialPositiveArmStillWithholdsTheStall` red at `verdict.ShouldBeNull()`.
- PC-24: break G-24 by running `fetch` when `TryAcquireAsync` returns null; expect `TaskProgressGitTests.C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch` red at `Trace.ShouldNotContain(a => a[0] == "fetch")`.
- PC-25: break G-25 by fetching the source into `refs/heads/<owner branch>` (updating the local ref) during the remote probe; expect `C499_V18_RemoteOnlyClaimedCommitFromASecondCloneIsProgress(false)` red at `localOwnerTip.ShouldBe(world.OwnerSha)`.
- PC-26: break G-26 by catching `OperationCanceledException` in the completion service and returning `Indeterminate`; expect `C499_R17_CancellationPropagates` red at `Should.ThrowAsync<OperationCanceledException>`.
- PC-27: break G-27 by calling `MergeBackAsync` for an alternate-only success; expect `C499_V25_MergeBackOnlyForDirectWorkWithAnExplicitTarget("alternate-explicit")` red at the `Merged` event `ShouldBeFalse()`.
- PC-28: break G-28 by calling `MergeBackAsync` (which autocommits) on an indeterminate primary; expect `C499_V25_MergeBackOnlyForDirectWorkWithAnExplicitTarget("indeterminate-explicit")` red at `status --porcelain` output `ShouldNotBeEmpty()`.
- PC-29: break G-29 by assigning `task.WorktreeBranch = ownerBranch` when repair-source evidence rescues completion; expect `C499_V17_TwoWorktreeIncidentSucceedsWithRepairSourceEvidence` red at `WorktreeBranch.ShouldBe(assignedBranch)`.
- PC-30: break G-30 by removing the `RepairSourceTaskId` check from `AgentTaskLandService.RequestAsync`; expect `RepairSourceLandRefusalTests.C499_V24ii_LandRequestIsRefusedForARepairTask` red at `Should.ThrowAsync<ConflictException>`.
- PC-31: break G-31 by removing the same check from `AgentTaskLandingProtocol.RunAsync`; expect `C499_V24iii_ProtocolEntryIsRefusedForARepairTask` red at `Should.ThrowAsync<ConflictException>`.
- PC-32: break G-32 by adding the progress Warning on every settlement pass instead of once per settled task; expect `C499_V26_BaselineAndEvidenceSurviveReplayAndDuplicateSettlement` red at `warnings.Count.ShouldBe(1)`.
- PC-33: break G-33 by not passing the progress warning into `BuildCompletionNote`; expect `C499_V38_ProgressWarningReachesTheCallerSession(false)` red at `prompt.Text.ShouldContain("progress=repair-source")`.
- PC-34: break G-34 by synthesising a baseline from the current tips when `ProgressBaselineJson` is null; expect `AgentTaskReplyIntegrationTests.a_legacy_task_without_a_baseline_gets_no_remote_credit` red at `Status.ShouldBe(AgentTaskStatus.Failed)`.
- PC-35: break G-35 by pinning a fresh `refs/antiphon/progress/<task:N>/<guid>` on every probe call without reusing the attempt's pin; expect `C499_V30_ProbePinsAreBoundedAcrossRetries` red at `ProgressPins().Count.ShouldBeLessThanOrEqualTo(4)`.
- PC-36: break G-36 by scanning the whole body (fenced blocks included) with a regex for `commit=<hex>`; expect `C499_R09_QuotedAndArbitraryHexAreNotClaims` red at `Outcome.ShouldBe(NoAttributedProgress)`.
- PC-37: break G-37 by persisting a baseline with `localSha = null` when `rev-parse` fails and continuing dispatch; expect `RepairSourceDispatchTests.C499_R37_LocalIdentityFailureRefusesDispatch` (force `rev-parse` to exit 128 through `ControlledTaskProgressGit`) red at `Status.ShouldBe(AgentTaskStatus.Failed)` (task dispatched instead).

### Out of scope

- Holding the OWNER's landing while a repair is live: `FindWriterAsync` matches by path/common directory and the plan keeps landing admission unchanged; V-24(iv) proves the owner still lands. A symmetric hold would be a new admission rule.
- Live TUI acceptance of the repair brief: V-3 checks the queued row, V-39 the fake adapter's `UserPrompt`; a real Claude/Codex/Grok delegate reading the block is the post-land activation check below.
- Cryptographic authorship, author-email/card-name/scope heuristics: rejected by D-4.
- Dirty-file fingerprinting or working-tree attribution on the alternate source: D-5 keeps the primary mtime heuristic only.
- Historical Failed tasks, backfilled baselines, parsing old briefs: D-7 forbids them; R-24 pins the legacy path instead.
- Duplicating the landing state machine (C448 matrices): the owner's Review/Land uses existing authority; only the two repair refusals (V-24) are new.
- Real network remotes, credentials and arm64/Linux: all remotes are scratch bare repositories on this machine.
- Herdr, remote-control and provider routing lanes: unchanged by the plan.

### Post-land activation check (orchestrator-owned)

After Code, ordinary Review, land and the canonical restart with `GET /api/version` at the landed SHA:
dispatch one real Code task with `-RepairSource <owner>` against a live owner branch that is checked out
in another worktree, let it push one commit to the owner branch and report the claim line, and confirm
`delegate.ps1 -Status` prints `Progress: repair-source; owner ...; commit ...`, the task is Succeeded, the
owner worktree and branch are untouched, and `-Land` on the repair task returns
`repair_source_landing_owner_required`. Mutation runs the PCs above; it does not replace this check.

### Cost

All filters run from one isolated build (`--property:OutputPath=bin-c499/`, forward slash) with
`--report-trx` into a fresh results directory per invocation; minutes are estimated on this machine.
`Antiphon.Tests` only; no Pty, E2E or full-assembly run is required by this design.

| Item | Suite and filter | Minutes (estimated) |
|---|---|---|
| Setup/build | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c499/` | 6 |
| Unit lane | `/*/*/*/*[Category=Unit]` (includes `TaskCompletionProgressPolicyTests`, `RepairSourceDocumentationTests`, `DelegationUnitTests`) | 2 |
| Integration named classes | `/*/*/(TaskProgressGitTests*)\|(RepairSourceDispatchTests*)\|(RepairSourceSettlementTests*)\|(RepairSourceLandRefusalTests*)\|(AgentTaskReplyIntegrationTests*)\|(TaskProgressPolicyFileArmTests*)\|(AgentTaskDispatchBaseGuardTests*)\|(AgentTaskServiceIntegrationTests*)\|(DelegateScriptRepairSourceTests*)\|(DelegateScriptLandStatusTests*)\|(InstructionBundleTests*)/*` (per class: 3, 4, 9, 2, 7, 1, 2, 3, 2, 2, 1) | 36 |
| Ordinary V/R floor (Code) | build + Unit lane + named classes | 44 |
| PC floor (Mutation) | 12 Unit-level PCs (PC-10..16, 18, 19, 21, 26, 36) at about 1.0 each = 12; 25 Integration PCs at about 2.2 each (edit, incremental build, red method, restore, green method) = 55 | 67 |
| Total verification floor | 44 + 67 | 111 |

Savings: a method-scoped PC on `RepairSourceSettlementTests` costs about 45 s per run against about 9 min for
the class, roughly 70 min saved across its nine PCs; the named-class filter replaces the 2,349-case
Application namespace run (about 26 min plus triage of unrelated pre-existing failures) on every V/R pass.
Mutation may shard the 25 Integration PCs across two worktrees off the task branch; classes carrying
`ParallelLimiter<ProcessSpawnLimit>` stay serial within each shard.

Bodies read; boundaries mapped; guards=37, mapped=37, missing=0, duplicate PC mappings=0; every PC is a
compiling defect with a named red assertion; cost is numeric and labelled estimated.

Next stage: **code**.
