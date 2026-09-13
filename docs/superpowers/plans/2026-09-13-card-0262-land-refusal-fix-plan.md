# CARD-0262: Resolve the S1 land refusal against current evidence

Plan task: `3fb695dc`. Date: 2026-09-13. Authoring base:
`f26aa721e81905f046b47247054e6696262e12ef`.

Status: Plan complete; the original publication failure has already recovered.
Next: **Investigate the remaining cleanup custody**, not another implementation of
the resolver fix. TestDesign was not folded into this dispatch. If investigation
establishes a new code defect, revise the affected slice and commission separate
TestDesign before Code.

## Outcome and scope

Task `604d1c73` published CARD-0262 S1 at 15:25:35 UTC on September 13. Its subsequent
refusal is cleanup-only: `ignored_content_preserved`. CARD-0498's resolver/monitor
coordination and failure diagnostics are already in this checkout and in the
running server. The implementation plan is therefore to retain that existing fix,
record the completed publication/activation, and establish custody of the retained
files before commissioning any cleanup action. No new production edit is justified
by the current evidence.

This is a landing recovery addendum to the
[CARD-0262 feature plan](2026-09-08-card-0262-kb-preference-to-agent-instructions-plan.md).
It neither supersedes that feature design nor declares its remaining S2-S4 slices
complete. The existing concurrency implementation and its full verification design
belong to the [CARD-0498 plan](2026-09-13-card-0498-land-refusal-diagnostics-concurrency-plan.md).

Owners: [orchestration and cleanup](../../orchestration-loop.md),
[HTTP inspection](../../ops-http.md), [card lifecycle](../../agent-card-lifecycle.md),
[project conventions](../../project-context.md), and
[testing/build](../../testing-and-build.md).

## Evidence and chronology

The brief's example task `7e1c69a6` is an unrelated investigation of four baseline
test failures. CARD-0262 history revisions 22-23 identify **`446a5581`** as the
successful investigation of `604d1c73`'s land refusal. Read its full report with
`pwsh -NoProfile -File scripts/delegate.ps1 -Status 446a5581`; its detailed evidence
is retained at `C:\src\Antiphon\.antiphon\task-446a5581.md`.

That report attributes the historical pre-checkpoint failure to the known
resolver/monitor concurrency race. It explicitly cannot recover the exact CLR
exception from either historical request: the old handler discarded it. This plan
retains that distinction between a confirmed defect class and an unavailable
incident-specific exception. No retrospective exception value should be invented.

| Time (UTC, 2026-09-13) | Durable observation |
|---|---|
| 13:21:36 and 13:24:01 | Two `LandRefused` events for `604d1c73`, generic operation-failed detail, no operation/source checkpoint. |
| 14:55 | Investigation `446a5581` succeeded, recommending CARD-0498 publication/activation before retry. |
| 15:25:35 | Remote containment confirmed for operation `851f2b33-2dea-4eb0-8dc5-c65a46f1474b`. |
| 15:25:46 | `LandedWithResidue`: publication `Landed`, cleanup `Refused`, reason `ignored_content_preserved`. |
| 17:36:59 | CARD-0498 task `154dd60c` recorded `AlreadyPresent`, operation `67191d43-446f-418f-8cac-de9074af6a78`, independently confirming its source on master. This later receipt does not establish its first publication time. |
| 17:52:01 | `LandingCleanup` for the same CARD-0262 operation; `CleanupRetry` again preserved ignored content. No second publication. |
| Plan-time read, approximately 21:50 | API version, local ancestry, remote master, canonical checkout, and retained source agree with the publication evidence below. |

Recorded identities:

- Approved original S1 source: `300940435826a9885d0bde4210ffb39356aef8c8`.
- Verified/post-rebase S1 source and running API version:
  `8ccdb1c93d6dc288b05738ee0f7faded35548b4a`.
- CARD-0498 source: `89e373584c6bb32d8acc0218a3ed159eb6856262`.
- Canonical `C:\src\Antiphon` HEAD, plan base, and directly queried remote master:
  `f26aa721e81905f046b47247054e6696262e12ef`.
- `GET /api/version` advertises the exact `land-v2` capability.
- `git merge-base --is-ancestor <CARD-0498 source> <running API version>` returned
  0. The same check from verified S1 source to plan base returned 0.
- Retained source `C:\Antiphon\worktrees\card-task-604d1c73` remains attached to
  `refs/heads/feat/card-task-604d1c73`, now at the verified S1 SHA. Its ordinary
  porcelain status was empty. The canonical checkout status was also empty.
- A read-only `git ls-files --others --ignored --exclude-standard` inventory found
  **1,635 ignored paths**: `.antiphon` 21, `server` 476, `src` 390, `tests` 748.
  These counts establish residue, not ownership or permission to delete it.

Read the live structured `landing` and `landRequest` separately. The latest
CARD-0262 cleanup request `feba4463-ae12-4b61-a2a0-6cdfd16f95ae` is `Completed`, with
`sourceResolutionState=None` and no terminal failure code. Its linked publication
receipt remains valid; those empty source fields on a cleanup retry do not recreate
the historical pre-checkpoint failure. Some outcome notifications still lack a
confirmed receipt or have `destination_stopped`; delivery is a third independent
state, not evidence that publication failed.

## Ground truth

| What the card/brief assumes | What the code or current evidence does | Consequence |
|---|---|---|
| `604d1c73` still cannot publish. | Structured operation confirms `Landed`; the latest event is `LandingCleanup` for the same operation. | Do not rerun fresh publication to fix a cleanup refusal. |
| CARD-0498 must first be implemented or activated. | Its commit is an ancestor of the running server's reported SHA; current sources contain the coordinated writer. | Reuse the existing implementation. No restart is needed for this incident now. |
| Monitor writes can still collide with a stale resolver save in the old way. | `AgentTaskLandRequestWriter.ApplyAsync` takes the task row lock, reloads task/request, validates baseline identity and source progress, then applies the saved patch. Monitor uses the same lock. | Preserve the coordinated checkpoints and existing race controls. |
| All pre-operation exceptions still become opaque `landing_failed`. | `AgentTaskLandService.FailRequestAsync` and `PersistFailureAsync` classify and persist diagnostic identity/type under a task lock, preserving publication if already confirmed. | A future exception must be investigated using its structured diagnostic fields. |
| A missing Review evidence ID requires changing admission. | Explicit full-SHA approval is supported. The successful S1 receipt also has null `reviewEvidenceId`. | Add no approval bypass or new mandatory Review condition. Ordinary Review remains its separate workflow gate. |
| Ordinary clean Git status authorizes worktree removal. | `GuardedWorktreeRemoval.HasProtectedIgnored` rejects any nonempty ignored-path snapshot; even non-forcing Git removal deletes ignored files. | Ordinary status is insufficient. Establish exact artifact custody. |
| The approved and deployed source must have the same SHA. | The land rebased `30094043...` to `8ccdb1c9...`; the receipt preserves both. | Use original SHA for the existing request identity and verified SHA for containment/activation. |
| All CARD-0262 work is done once S1 lands. | The Code report implements pin store/API only; the feature plan still assigns composition, file/import and queue work to later slices. | Keep feature continuation independent of this incident disposition. |

## Decisions

### D-1. Reuse CARD-0498's landed fix

The needed coordination and diagnostics already exist and are active. Keep the
short task-first transaction, reload and baseline/patch validation. Reject a second
writer implementation, a lock across Git I/O, removal of token rotation, and blind
retries: these add competing behavior or erase the concurrency invariant that the
existing controls exercise.

### D-2. Preserve publication, cleanup and notification as separate outcomes

The successful operation and verified SHA are the incident's publication verdict.
Keep historical refusals and later cleanup events intact. Reject clearing task
state, reconstructing a fresh operation, or treating an absent notification receipt
as permission to publish again. Those actions lose durable identity without
addressing the remaining files.

### D-3. Retain unknown ignored content

Default to preserving all 1,635 paths until a bounded custody investigation names
their producers, whether any process still uses them, and which evidence must be
retained externally. Reject `git clean`, force removal, blanket `bin-*` ownership
assumptions, or weakening `HasProtectedIgnored`. File names and Git ignore rules
do not establish ownership. A later authorized cleanup may remove only exact
proven producer-owned outputs after evidence preservation and path validation.

### D-4. Use the existing guarded retry after custody is resolved

Keep task `604d1c73` as landing owner and operation
`851f2b33-2dea-4eb0-8dc5-c65a46f1474b` as the publication receipt. A later cleanup
executor uses `delegate.ps1 -Land 604d1c73 -ExpectedSourceSha
300940435826a9885d0bde4210ffb39356aef8c8` only after its commissioned artifact work.
The service must revalidate current remote containment and emit `LandingCleanup`
for that operation without another push. Reject manual branch deletion or source
reset to the original pre-rebase SHA. This Plan dispatch performs no cleanup retry.

### D-5. Keep the feature implementation and this recovery addendum distinct

No pin-store, API, launch, import or queue change is part of the land-refusal fix.
Reject reopening S1 to create a commit solely to trigger a new land. Later feature
slices continue under their original plan and ordinary Review/Mutation obligations.

### D-6. Route the corrected premise to Investigate

No product choice blocks this artifact. The missing fact is artifact custody, not
a design preference. The next dispatch measures that fact and proposes a concrete
disposition. If it establishes a production defect, return to Plan and then separate
TestDesign; if the guard is behaving correctly, commission a bounded cleanup task
or explicitly retain the residue. Do not dispatch Code from this addendum alone.

## Implementation and recovery slices

### S1. Coordinated source checkpoints — implemented by CARD-0498

Files: `server/Application/Services/AgentTaskLandRequestWriter.cs`,
`AgentTaskLandSourceResolver.cs`, `AgentTaskLandMonitorService.cs`, and
`AgentTaskLandService.cs` in the same service directory.

Keep source patches outside the reloadable EF entities. At every checkpoint, lock
the task, reload, reject stale request/attempt/approval or newer source progress,
apply only the intended source patch, and commit before dependent Git work.
Operation attachment follows the same guarded identity checks. This is existing
implementation, not a new edit list.

Tests: `tests/Antiphon.Tests/Application/AgentTaskLandSourcePersistenceTests.cs`:
`C498_MonitorPassDuringObservedInspection`, `C498_MonitorPassAtEveryCheckpoint`,
`C498_ResolverCheckpointDoesNotHoldLockAcrossGit`,
`C498_MonitorWaitsForResolverCheckpoint`, `C498_StaleRequestGuards`,
`C498_NewerSourceProgressConflict`, `C498_StaleOperationAttachAbandonsCandidate`,
`C498_ChildStartCallbackConflictFailsCallback`, and
`C498_RecoverySweepWriteReloadsUnderLock`.

### S2. Durable exception and inspection evidence — implemented by CARD-0498

Files: `server/Application/Services/AgentTaskLandService.cs`,
`server/Infrastructure/Orchestration/AgentTaskLandHostedService.cs`,
`server/Infrastructure/Git/LandingGit.cs`, and `scripts/delegate.ps1`.

Preserve the existing terminal transaction: classify the exception, persist a
bounded failure code/diagnostic ID/type, retain confirmed publication, and expose
safe generated inspection metadata through task detail and CLI. Do not expose
captured Git stderr or reinterpret a semantic source refusal as an exception.

Tests under `tests/Antiphon.Tests/Application/`:
`AgentTaskLandFailureDiagnosticTests.C498_HostedDrainClassifiesExecutionException`,
`C498_FailureAfterPublicationRetainsPublication`,
`C498_RepeatedFailureCallbackIsIdempotent`, and
`C498_InspectionDiagnosticPropagatesToRequest` in that class;
`AgentTaskLandContractEndpointTests.C498_TaskGetExposesFailureDiagnostics` and
`C498_TaskGetExposesInspectionDiagnostics`;
`DelegateScriptLandStatusTests.C498_StatusPrintsDiagnosticLines`.
Git command coverage: `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs`,
`C498_RequiredCommandFailureKeepsCommandIdentity`.

### S3. Confirm publication and activation — completed during planning

Record the operation, original/verified SHAs, remote containment and API build in
this plan. Existing operational entry points are `scripts/delegate.ps1` and
`docs/apphost-runbook.md`; neither needs an edit or execution of a restart here.
An ancestry check alone is paired with direct API version/capability observation,
not a health-only inference. No new runtime test is needed for this documentation
slice. If runtime state changes before follow-up, refresh the observations below.

### S4. Resolve retained-artifact custody — next investigation

Read-only target: `C:\Antiphon\worktrees\card-task-604d1c73`. Code owners to consult
are `server/Infrastructure/Git/GuardedWorktreeRemoval.cs` and the landing service;
do not edit either unless new evidence shows an incorrect refusal.

Write a bounded inventory/disposition artifact, for example
`docs/investigations/2026-09-13-card-0262-cleanup-custody.md`, with only safe metadata.
Keep sensitive or bulky raw manifests in an external evidence directory outside
the retained source. Measure:

1. Fresh receipt, registered path, branch, HEAD and ignored-path inventory; detect
   changes from the 1,635-path snapshot instead of assuming it is still current.
2. Exact paths, counts/sizes and producer evidence for each retained output set;
   identify test reports and any other irreplaceable artifacts needing preservation.
3. Process ownership and open writers before proposing any output removal. Report
   unknown ownership explicitly; a stopped parent alone does not prove child exit.
4. Whether all remaining files can be accounted for. For proven outputs, name the
   exact bounded removal candidates and evidence destination; for unknown files,
   retain them and name the unresolved fact. No deletion occurs in Investigate.

Existing acceptance controls are in
`tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs`:
`C448_V04_IgnoredFilesSurviveConfirmedPublication` preserves sentinel bytes, source
ref and publication while refusing cleanup; `C448_V33_CleanupRetriesDoNotEmitAnotherPublication`
removes only fixture-owned bytes and checks same-operation cleanup, unchanged
publication time and zero additional pushes. Extend these only if investigation
demonstrates a behavior they do not cover, through separate Plan/TestDesign/Code.

## Named coverage and TestDesign handoff

The methods above are existing coverage, not newly executed evidence. CARD-0498's
stored Code report credits the observed-inspection control's three rows and the
checkpoint control's eight rows as passing; this Plan dispatch ran **no tests or
builds** and does not promote that historical report to a fresh full-suite pass.
Its pending positive controls remain owned by CARD-0498's post-land Mutation work.

For a future change, TestDesign must name only the affected S1/S2/S4 methods and
any demonstrated gap, define expected assertions and positive controls, and specify
ordinary regression scope under `docs/testing-and-build.md`. Use real persistence
and controlled barriers for a concurrency defect; do not replace the monitor's
actual committed token change with a mock no-op or delay-based retry.

TUnit execution uses `dotnet run --project tests/Antiphon.Tests` with a producer-owned
alternate `--property:OutputPath=bin-<name>/`. For example, the exact method selector
for the original race is
`/*/Antiphon.Tests.Application/AgentTaskLandSourcePersistenceTests/C498_MonitorPassDuringObservedInspection`.
Require a fresh TRX containing executed method names and nonzero expanded counts.
Commit before long runs, await each run, keep its source frozen, and remove only
that producer's alternate outputs. This plan does not authorize a broad suite run
or repeat the historical CARD-0498 verification merely to reconfirm its report.

## Completion criteria and stage routing

The original refusal is resolved when the verified S1 commit is remotely contained
and the required concurrency fix is active. Both are observed here. Cleanup is
resolved independently only after an exact custody disposition and either guarded
removal or explicit retained-residue disposition. A cleanup retry must preserve
the original operation and remote-confirmation time, with no second publication.

Refresh evidence using the documented read-only front doors:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Status 604d1c73
pwsh -NoProfile -File scripts/delegate.ps1 -Status 154dd60c
pwsh -NoProfile -File scripts/card.ps1 history CARD-0262 -Json
git -C C:\Antiphon\worktrees\card-task-604d1c73 status --porcelain=v1 --untracked-files=all
git -C C:\Antiphon\worktrees\card-task-604d1c73 ls-files --others --ignored --exclude-standard
```

Also query `GET /api/version` using the base/header procedure in `docs/ops-http.md`
and repeat the ancestry checks with the newly observed version. A new structured
execution failure goes to its diagnostic ID/code/type; a current cleanup-only
receipt goes to S4. Historical generic refusals are not a reason to replay landing.

Required next stage: **investigate**, because the brief's ongoing-publication-failure
premise is no longer true. Handoff: confirm current cleanup receipt and measure
artifact/process custody for the retained S1 worktree; return a bounded disposition
without deleting files or changing the already-active concurrency fix.
