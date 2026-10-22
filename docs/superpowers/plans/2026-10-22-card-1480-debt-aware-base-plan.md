# CARD-1480 debt-aware same-card worktree base

Plan task `f1df213d`, 2026-10-22. Source audit at `origin/master`
**0ea49a7cc26bba45dffe8d0bfd8bff9d244a592b**; rc audit at `origin/rc/2026-10-20`
**ceb6e3d6b78471db6831258699c493b8eb2d619b** (the SHA `/api/version` reported in the
[CARD-1479/1480 investigation](../../investigations/2026-10-22-runner-sync-lease-busy.md)).
Docs only; no source was edited and no build or test was run by this Plan except the
checkpoint tool's table import.

## Problem in one paragraph

A same-card Worktree dispatch picks its base from **local** `refs/heads/<sibling branch>`
(`WorktreeBaseInspection.BranchCommitsAsync`, one `for-each-ref`). A runner-bound
sibling that settled Succeeded with `desktop-sync=pending` owes a fast-forward of that
desktop branch from its dispatch-time tip B to the pushed source S
(`AgentTaskSyncDebt.SourceSha`). Until the sweep pays the debt, the resolver sees B.
If B is already in the target (typical for a Review/Code follow-up whose predecessor
landed), Auto silently cuts from the target and omits S; explicit `-BaseTask` selects
B as a "validated quiescent" source. Either way the new task examines old bytes while
the caller believes it examines S.

## Ground truth (card claim vs code at 0ea49a7cc)

| # | Card / brief says | Code does | Verdict |
|---|---|---|---|
| G-1 | Resolver reads sibling tips through `BranchCommitsAsync` over local `refs/heads` | `AgentTaskWorktreeBaseResolver.cs:129` calls `inspection.BranchCommitsAsync(branches)`; `WorktreeBaseInspection.cs:184-206` runs `for-each-ref --format=%(objectname) %(refname) refs/heads/<b>...` | True |
| G-2 | `CardSiblingInventory` and the resolver read neither `AgentTaskSyncDebts` nor RemoteSync | `CardSiblingInventory.cs:17-52` reads `AgentTasks`, `AgentTaskLandRequests`, `AgentTaskLandings`, `AgentTaskEvents` only. `grep SyncDebt` over both files: no hit | True |
| G-3 | Auto can omit a pending source "as already merged" | `IsMerged(branch)` (`:131-132`) compares the local tip with `for-each-ref --merged=<target>`; merged rows are dropped at `:156-158` before containment, so a merged B yields `Target` with no warning | True |
| G-4 | Explicit `WorktreeBase=Task` can select the clean old SHA | `:208-216`: a requested row is `NotContained` by construction, then `CheckoutSafeAsync` and `explicitSource = (row, sha)` with `sha` = local B (`:265`) | True |
| G-5 | Explicit `-StartRef` is a safe path | `:82-85` returns `Target()` before any inventory when `WorktreeBaseRequestedRef` is set; `DelegationWorktreeService.cs:427-429` resolves the requested ref | True; untouched by this plan |
| G-6 | Landing fetches and validates the pushed source | `AgentTaskLandSourceResolver` (investigation `:109-169`), `SettlementSyncDebtLandingTests.C1082_PendingSyncOwnerLandsOnPushedBranch` | True; land path untouched |
| G-7 | (implicit) the resolver could fetch S itself | The dispatcher calls the resolver while holding the repository mutation lease R (`AgentTaskDispatcher.cs:5754`, `claimInspection`, `repositoryLease`); `TaskProgressGit.ObserveExactRefAsync` takes its own lease and the lease is not reentrant (`RemoteWorkspaceService.cs:543-545` comment). | A fetch inside the resolver is not possible without restructuring custody; see D-2 |
| G-8 | Debt identity | `AgentTaskSyncDebt` carries `TaskId`, `Attempt`, `FullRef`, `BaselineSha`, `SourceSha`, `State` (`Pending/Ready/Held/Superseded`), `ConfirmedSha`. `SyncSettledAsync` fast-forwards only to `debt.SourceSha` (`RemoteWorkspaceService.cs:384-400`), so S is the settlement's authoritative target, not a re-observation | Use these fields; no schema change |
| G-9 | Worktree creation can cut from a SHA | `DelegationWorktreeService.cs:427-438`: `cardBase` is a `ResolvedBase(sha, CardCurrent)`; `CreateAsync(..., baseRef: sha)`; no branch-tip recheck. `WorktreeBaseTaskId/Branch` record the source task | A substituted S flows through unchanged |
| G-10 | Auto + `Unknown` blocks | Only when `Task` mode, or Auto && `Incomplete` && (Review or previewed) (`AgentTaskDispatcher.cs:5763-5768`). A non-incomplete Auto `Unknown` falls through to `cardBase = null`, i.e. the target, silently | A new reason needs a one-line dispatcher condition (D-4) |
| G-11 | Pending warning "Review and -Land use the pushed branch" | `SettlementSyncDebtPolicy.cs:79-80`; pinned by `SettlementSyncDebtPolicyTests`, `StandingPipelinePolicyDocumentationTests` and `docs/orchestration-loop.md` | Left as is (D-6) |
| G-12 | rc carries the same defect | `AgentTaskWorktreeBaseResolver.cs`, `WorktreeBaseInspection.cs`, `CardSiblingInventory.cs`, `AgentTaskSyncDebt.cs` are byte-identical between master and rc; the dispatcher's base-selection block (from `selected = await _baseResolver.ResolveAsync` to `var cardBase = selected`) is byte-identical too, though the dispatcher file differs elsewhere (+114/-43) | Live on rc; see rc section |

## Decisions

**D-1 Debt-aware tip substitution in the resolver (the seam).** After
`var commits = await inspection.BranchCommitsAsync(branches);` (`AgentTaskWorktreeBaseResolver.cs:129`)
and before `MergedBranchesAsync`, apply settled sources. For each candidate row
(inspectable rows, the explicitly requested row, and pending-land rows) take its
**unconfirmed** debt: newest row by `CreatedAt` with `TaskId == row.Id`,
`Attempt == row.Attempt`, `State != Ready`, `FullRef == "refs/heads/" + row.WorktreeBranch`,
full `SourceSha`. If the local tip L already equals S, nothing changes. Otherwise S is
**verified locally**: `inspection.CommitAsync(S)` returns S (object present as a commit)
and `inspection.AncestorAsync(L, S)` is true (S fast-forwards the local branch, exactly
what the sweep would do). Verified: `commits[branch] = S` and add the warning
`Task <id8> branch <b> local tip <L> is behind its settled source <S> (sync debt <State>); using <S>.`
Everything downstream (merged test, pending-land containment, ancestry ordering,
explicit selection) then sees S. Because `MergedBranchesAsync` returns B for the merged
ref, `IsMerged` becomes false for the substituted branch, and containment of S decides.
Rejected: changing `BranchCommitsAsync` to read `refs/remotes/origin/*` (the desktop's
remote-tracking refs are not refreshed by a runner push; still stale); reading
`progressEvidence.remoteSync` (historical, stays Pending after payment; investigation
finding 3).

**D-2 Unverifiable S is a named refusal, not a wait or a fetch.** If S is absent locally,
not a commit, or does not descend L (rewound/diverged), the resolver returns
`Unknown` with `Reason = "source_sync_unverified"`, `SourceTaskId/SourceBranch` = the row,
`SourceSha = S`, and a warning
`Task <id8> branch <b> owes a settlement sync to <S> (sync debt <State>) and the local branch is at <L>; that source cannot be verified here. Retry after the sync, or recreate with -StartRef <S>.`
This applies in Auto and Task modes. In `Target` (fresh) mode it is only the
"Fresh worktree omits ..." warning naming S. A pending-land row that cannot be verified
keeps the existing `WaitForLand` (`pending_land_unknown`), because the land owns that
row. Rejected: (a) fetch S in the resolver: R is held by the dispatcher, the observation
fetch takes its own non-reentrant lease (G-7), and network under R lengthens the claim
the CARD-1430/1462 work is shrinking; (b) `WaitForLand`-style silent rollback: no event
is written, and on rc the uncapped debt row reached 2,168 attempts, so the wait would be
an invisible unbounded queue stall; (c) running the sync from the resolver: mutates
another task's checkout under dispatch custody.

**D-3 Inventory carries the debts; one extra bounded DB read.** `CardSiblingInventory.Snapshot`
gains `IReadOnlyDictionary<Guid, AgentTaskSyncDebt> SyncDebts` read through
`inspection.ReadAsync` for the sampled row IDs plus pending-land task IDs
(`State != Ready`), then reduced in memory per D-1's identity. The inventory already
bounds the ID set by `MaxInventoryRows`; the read is one query. Git cost: at most two
extra commands per debt-bearing candidate whose local tip differs, under the existing
`WorktreeBaseMaxGitCommands` budget (default 128). The `warningsOnly` caller
(`AgentTaskDispatcher` sibling warnings) is not changed in behaviour.

**D-4 Dispatcher blocks the new reason in every non-fresh mode.** Add
`public bool SyncUnverified => Reason == "source_sync_unverified";` to
`CardWorktreeBaseSelection` and `|| selected.SyncUnverified` to the block condition at
`AgentTaskDispatcher.cs:5763`. The existing `BlockAsync` text already prints the reason,
candidate warnings and the `-StartRef` / `-FreshWorktree` remedies. Create-time preview
(`AgentTaskService.cs:1868-1880`) keeps its rule: explicit `-BaseTask` + `Unknown` refuses
creation with `worktree_base_source_invalid` naming the reason; Auto records the preview.

**D-5 Debt states.** `Pending`, `Held` and `Superseded` are all "unconfirmed" (Superseded
proves no fast-forward happened). `Ready` is paid and ignored. A debt from a prior
`Attempt` is ignored (the row's current attempt has its own settlement).

**D-6 Out of scope.** The Pending warning wording (G-11; three pins and the orchestrator
doc), ReadOnly Reviews aimed at a desktop path, the land path, CARD-1479's waiter wiring,
CARD-1109/1043/1446/1463. After this change the warning's claim holds for same-card
Worktree dispatch (it binds S or refuses by name); the ReadOnly residue stays as the
investigation describes.

**D-7 Platform and lane.** Server-side C# plus Postgres/Git integration tests: Linux is
fine. Runner defaults read live: `globalRunnerId=server2`, no kind override. Code and
checkpoint rows run on the **server2 lane**; omit `-Runner` and `-Platform`.

No session that waits for input is added or changed: the refusal is a dispatch-time
Block of a task that has no session yet, released by the caller's retry/recreate as
today.

## Slices

### S1-test (red witnesses, committed and pushed first; about 30 minutes)

New file `tests/Antiphon.Tests/Application/AgentTaskWorktreeBaseSyncDebtTests.cs`, class
`AgentTaskWorktreeBaseSyncDebtTests` with `[Category("Integration")]`, `[Category("Slow")]`,
`[ParallelLimiter<GitProcessSpawnLimit>]`; copy the small `Context/CardAsync/SourceAsync/
NewRequest/ResolveAsync/CheckoutAsync` helpers from `AgentTaskWorktreeBaseResolverTests`.
Add the class to `tests/Antiphon.Tests/slow-tests-allowlist.txt` with a reason comment.
New partial file `tests/Antiphon.Tests/Application/AgentTaskDispatchBaseGuardTests.SyncDebt.cs`
for the dispatcher witnesses, reusing that class's `CreateProvider`, `SeedCardAsync`,
`SeedKeptSiblingAsync` and `TickInspectedAsync`.

Fixture shape (the card's red shape): seed owner O (Succeeded, `Attempt = 1`) on branch b
with commit B from master; fast-forward master to B (B is in target, O has no Landed
event and no pending land); create S on top of B with `git commit-tree` and hold it only
under `refs/antiphon/progress/<O:N>/observed` (no branch moves); seed
`AgentTaskSyncDebt { TaskId = O, Attempt = 1, FullRef = refs/heads/b, BaselineSha = B,
SourceSha = S, State = Pending }`. "absent" uses a well-formed SHA not in the store;
"diverged" uses a commit on master's parent that does not descend B.

S1-test commits only the witnesses below; each must compile and fail on its decision/SHA
assertion at the parent commit (zero tests or a build error is not red).

| V | Test | Red at base because |
|---|---|---|
| V-1 | `AgentTaskWorktreeBaseSyncDebtTests.C1480_auto_inherits_settled_source_over_merged_local_tip`: Auto → `Continue`, `SourceTaskId = O`, `SourceSha = S`, warning names L and S | base returns `Target` (B merged, silently omitted) |
| V-2 | `...C1480_explicit_task_uses_settled_source`: `RequestedWorktreeBaseMode.Task` on O → `Continue`, `SourceSha = S` | base returns `Continue` with `SourceSha = B` |
| V-3 | `...C1480_unverifiable_settled_source_refuses_by_name(shape, mode, state)` with `[Arguments("absent", Auto, Pending)]`, `[Arguments("absent", Task, Held)]`, `[Arguments("diverged", Auto, Superseded)]` → `Unknown`, `Reason = "source_sync_unverified"`, `SourceSha = S`, warning contains `-StartRef <S>` | base returns `Target` / `Continue B` |
| V-4 | `...C1480_pending_land_row_holds_on_settled_source`: O also has a Queued pending land request → `WaitForLand`, `Reason = "pending_land"`, `SourceSha = S` | base treats merged B as landed and returns `Target` |
| V-5 | `AgentTaskDispatchBaseGuardTests.C1480_dispatch_cuts_from_settled_source`: create a Review (Auto) on the card, `TickInspectedAsync` → `Dispatched`, `WorktreeBaseSha = S`, `WorktreeBaseTaskId = O` | base dispatches at the target SHA with no base task |
| V-6 | `AgentTaskDispatchBaseGuardTests.C1480_dispatch_blocks_unverified_settled_source(role)` with `[Arguments(Code)]`, `[Arguments(Review)]`, S absent → `Blocked`, `FailureReason` contains `source_sync_unverified` and S, `WorktreePath` null | base dispatches at the target |

Executed at S1-test: 1 + 1 + 3 + 1 + 1 + 2 = **9**, all 9 red.

### S1 (fix plus preservation guards; about 45 minutes)

Production files:

* `server/Application/Services/CardSiblingInventory.cs`: `Snapshot.SyncDebts` (D-3).
* `server/Application/Services/AgentTaskWorktreeBaseResolver.cs`: `SyncUnverified`;
  a private `ApplySettledSourcesAsync(inventory, rows, commits, inspection, warnings)`
  called right after `BranchCommitsAsync`; the refusal/fresh-warning handling (D-1, D-2, D-5).
  `WorktreeBaseInspection` needs no change (`CommitAsync` and `AncestorAsync` exist).
* `server/Application/Services/AgentTaskDispatcher.cs`: `|| selected.SyncUnverified` (D-4).

Preservation guards added in S1 (green before and after; not red witnesses):

| V | Test | Guards |
|---|---|---|
| V-7 | `AgentTaskWorktreeBaseSyncDebtTests.C1480_start_ref_bypasses_debt_guard`: `WorktreeBaseRequestedRef = B` with a Pending debt → `Target`, zero Git commands (`TraceGit`) | explicit StartRef identity unchanged |
| V-8 | `...C1480_paid_or_foreign_debts_do_not_change_selection(shape)` with `[Arguments("ready")]`, `[Arguments("same-tip")]`, `[Arguments("prior-attempt")]` → the same selection as with no debt row | D-5 and the L == S no-op |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-test | `tests/Antiphon.Tests -> bin-c1480-red/` | debt-base-red | `/*/Antiphon.Tests.Application/(AgentTaskWorktreeBaseSyncDebtTests*)\|(AgentTaskDispatchBaseGuardTests*)/C1480_*` | V-1..V-6 red witnesses | build succeeds; 9 executed, 9 expected assertion failures | 9 | 8 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1480-green/` | debt-base-green | `/*/Antiphon.Tests.Application/(AgentTaskWorktreeBaseSyncDebtTests*)\|(AgentTaskDispatchBaseGuardTests*)/C1480_*` | V-1..V-8 | 13 executed, all passed, 0 failed/skipped | 13 | 8 |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c1480-resolver/` | base-resolver-regression | `/*/Antiphon.Tests.Application/(AgentTaskWorktreeBaseResolverTests*)\|(AgentTaskWorktreeBaseCreateTests*)\|(DispatchSiblingInspectionTests*)/*` | resolver ordering, identity, budget, create preview, sibling warnings | all selected passed, 0 failed/skipped | 27 | 14 |
| CP-4 | S1 | `tests/Antiphon.Tests -> bin-c1480-dispatch/` | dispatch-guard-regression | `/*/Antiphon.Tests.Application/AgentTaskDispatchBaseGuardTests/*` | dispatcher block/continue rules incl. the new methods | all selected passed, 0 failed/skipped | 49 | 15 |

Min values are `[Test]` method floors (16 + 5 + 6 = 27 for CP-3; 49 methods across the
three existing partial files for CP-4); argument expansion raises the executed count.
CP-4 overlaps CP-2's dispatcher methods by design: it proves the partial class still
builds and every older dispatcher case holds. No assembly-wide run. Run each row group
through the checkpoint tool on the server2 lane (`run --plan <this file> --rows ...
--expected-source-sha <sha>`, then `wait` until the exit is not 75); one run per slice
group, never two concurrent runs in one worktree; delete every `bin-c1480-*` after.

### Cost

S1-test about 30 minutes plus CP-1 (8); S1 about 45 minutes plus CP-2..CP-4 (37).
Two Code slices of 30-60 minutes each.

## rc/2026-10-20 applicability

The defect is live on the running rc (G-12). The production hunks touch only
`CardSiblingInventory.cs`, `AgentTaskWorktreeBaseResolver.cs` (both byte-identical to
master) and the dispatcher base-selection block (byte-identical region; the file differs
elsewhere). The S1 production commit is therefore expected to cherry-pick cleanly; the
landing caller proves it with a per-commit `git merge-tree --merge-base` chain onto
`origin/rc/2026-10-20` and drops this plan document from the pick (rc carries no
`docs/superpowers/plans`). The new test file and the new partial are additions;
`slow-tests-allowlist.txt` is the one shared test file to check in that chain. No
dependency on CARD-1446 or CARD-1479: the debt table and fields exist on rc (migration
`20261006212209_AddAgentTaskSyncDebts`).

## Verification design

Not folded into this dispatch: V-1..V-8 and CP-1..CP-4 above are the proposed closed
scope. The brief names `next: code` with the S1 handoff, so Code executes them as
written; a TestDesign pass is optional and the caller's call.
