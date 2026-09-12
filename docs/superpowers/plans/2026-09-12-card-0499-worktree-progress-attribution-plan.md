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
