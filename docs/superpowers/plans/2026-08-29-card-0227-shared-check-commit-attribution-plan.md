# CARD-0227 — stop attributing Shared checkout activity to the checked task

**Status:** plan only — no production change in this card's planning task.

**Decision:** make check-ins conservative now. A Shared task will carry no Git commit or
working-tree *authorship* evidence; a Worktree task retains its task-branch range evidence.
Do not introduce a database baseline or use commit author/card text as a proxy for task identity.

## 1. What happened, and what the code actually does

The live incident is exactly the failure described on CARD-0227, not an inference from an
ambiguous log:

- Task `77bfecda` (CARD-0222) was a Shared, Plan-role task in `C:\src\Antiphon`, dispatched at
  `2026-08-28 19:46:20Z`.  It had no write mandate.
- An unrelated CARD-0224 merge fast-forwarded commit `23de792` into that same checkout at
  `20:05:49Z` (`21:05:49+01:00` in Git), by the operator's normal Git identity.  The commit is
  reachable from `master`; the local log for the incident window contains that commit and no
  commit from CARD-0222.
- The first check at `20:46:22Z` read that commit as the checked task's evidence and the
  interpreter returned `PRODUCED - 1 commit created`.  The CARD-0222 investigation independently
  records that `23de792` landed while its 21-minute build was running.

`DelegateCheckProbe.GatherGitAsync` currently picks its query from the presence of
`WorktreeBranch` and `MergeTargetRef`, rather than from an evidence-ownership model:

| Workspace shape | Current command | What it actually proves |
|---|---|---|
| Shared | `git log --oneline --no-decorate -20 --since=<task.DispatchedAt> HEAD` | Only that a commit is reachable from the shared `HEAD` with a committer date after dispatch.  Any operator or other writer can satisfy it. |
| Worktree | `git log --oneline --no-decorate -20 <mergeTarget>..<taskBranch>` | What the dedicated task branch adds over its merge target.  Normal dispatch creates the worktree and branch before the session starts, and the probe reads `WorktreePath` first. |

It then always runs `git status --porcelain` through the read-only Git path and renders the
result as `commits=N changed=N untracked=N`.  The check-interpreter bundle explicitly allows
`PRODUCED` for commits or files, so this fact shape is sufficient to produce the false verdict.
The Git commands are read-only; the defect is attribution, not an index-writing or Git-locking
problem.

## 2. Attribution boundary

The implementation must represent whether Git facts are owned by the task, not infer ownership
from a nullable range.  The following are deliberately **not** valid fixes:

- **A pre-dispatch SHA/range alone.** `baseline..HEAD` removes clock-skew and commit-date quirks,
  but still contains the operator's `23de792` if it lands after the baseline.  It establishes
  change during the window, not who made it.
- **Git author/committer identity.** Delegates and the operator currently use the same configured
  Git identity, so it cannot distinguish the two writers.
- **CARD identifiers or the existing task marker.** Commit messages conventionally cite a CARD,
  not `[antiphon-task:<short-id>]`; multiple tasks can share a CARD.  The existing
  `DelegateBindRefusalRecovery` uses card/short-id needles only as recovery evidence and does not
  create a per-task commit identity.  Its card matcher would reject the measured 0222/0224 pair,
  but remains unsuitable as a general attribution predicate.
- **Working-tree counts.** `changed` and `untracked` are just as shared as `HEAD`, so retaining
  them while suppressing commits would leave the interpreter another false `PRODUCED` signal.

Normal Worktree tasks do not have this false-positive class.  `AgentTaskDispatcher` calls
`DelegationWorktreeService.CreateForTaskAsync` before it creates the delegate session; that creates
the unique `task-<short-id>` branch and `WorktreePath`.  The probe asks that branch's range, so a
new unrelated commit on `master` does not enter the result.  An operator who explicitly writes to
someone else's task branch can still defeat that operational boundary, but that is not the normal
Shared-HEAD race and needs no change in this card.

## 3. Recommended implementation

### S1 — give Git facts an explicit evidence scope

In `server/Application/Services/DelegateCheckProbe.cs`:

1. Add an explicit Git-evidence discriminator to `CheckGitFacts` (for example,
   `TaskBranch` and `SharedWorkspaceUnattributable`), rather than treating `Range == null` as
   "the task's commits since dispatch".  Keep `Directory`, `Unavailable`, and the current
   branch/range fields for the task-branch path.
2. Change `GatherGitAsync` to select the discriminator from `task.Workspace` and the required
   worktree coordinates.  For a valid Worktree, retain the current read-only
   `MergeTargetRef..WorktreeBranch` log and status calls.  If its expected coordinates are missing,
   render Git as unavailable/indeterminate instead of silently falling into Shared semantics.
3. For `WorkspaceMode.Shared`, confirm only that the directory is a repository, then return the
   `SharedWorkspaceUnattributable` shape without calling `LogOnelineAsync(... since:
   task.DispatchedAt)` or `GetWorkingTreeCountsAsync`.  This preserves the probe's read-only
   guarantee and removes the two facts that can belong to another writer.
4. In `RenderDigest`, give Shared a conspicuous, non-numeric explanation such as: "shared
   checkout — commits and working-tree state are deliberately omitted because they cannot be
   attributed to this task."  Do not render `commits=0`, `changed=0`, a commit subject, or the
   old `--since` interpretation; zero would falsely mean no work and any positive value invites
   false credit.  Keep today's rendering, unavailable wording, commit limit, and counts for a
   task branch.

This is intentionally a conservative loss of a weak signal for Shared tasks.  The interpreter can
still say `DOING`, `LOOKS STUCK`, or `AMBIGUOUS` from the task row, session state, transcript tail,
queue, and incidents, and it can still say `PRODUCED` where the bundle has genuinely task-owned
evidence (including a Worktree branch).

### S2 — make the interpreter contract agree with the facts

Update `server/Bundles/check-interpreter.md` so `PRODUCED` may use Git evidence only when the
bundle identifies it as task-owned, and a Shared-checkout disclaimer must never be used to infer
that the checked task wrote a commit or file.  Bump its `contract v1` label and
`CheckInterpretation.ContractVersion` together in
`server/Application/Services/CheckInterpretation.cs`; the provisioner tests deliberately pin the
two copies of that contract version.  The new digest behaviour is the safety boundary even before
the persistent interpreter session is next relaunched; reconciling the bundle updates the standing
agent's append prompt for its next launch/resume.

No `AgentTask` property, EF migration, API DTO, client change, or `GitWorkspaceService` change is
needed for this recommended fix.  In particular, do not add a `SharedHeadAtDispatch` column whose
name would imply that a range proves authorship.

## 4. Verification

Extend `tests/Antiphon.Tests/Application/DelegateCheckProbeTests.cs` with real temporary Git
repositories (the class's existing `TempRepo` helper already creates them).

1. **Deterministic incident regression:** seed a Shared task with `DispatchedAt` in the past; make
   an unrelated, clearly labelled commit and an unrelated working-tree edit after that timestamp.
   Gather the facts and render the digest.  Assert the Shared-attribution discriminator/disclaimer,
   and assert that neither the foreign commit subject nor `commits=`, `changed=`, or `untracked=`
   appears.  This reproduces the exact cause without two agents, a wall-clock race, or an LLM.
2. **Worktree isolation regression:** create a base branch and a task branch with one task commit,
   then advance the merge target with a separate unrelated commit.  Assert the Worktree facts still
   contain only the task branch's commit/range and identify it as task-owned.  This proves the
   same external advancement that breaks Shared does not leak into the Worktree evidence path.
3. Keep the existing Git-failure and read-only-index tests green, adapting their expectations to
   the explicit discriminator so an unavailable task branch is never presented as zero work.
4. Extend the relevant `CheckInterpreterProvisionerTests` contract-version assertion and, if the
   existing check-in integration fixture already exposes a digest, assert the interpreter receives
   the Shared disclaimer.  The implementation must not depend on model behaviour alone.

Run the focused suites from the main checkout with isolated build output:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c227/ -- --treenode-filter "/*/Antiphon.Tests.Application/DelegateCheckProbeTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c227/ -- --treenode-filter "/*/Antiphon.Tests.Application/CheckInterpreterProvisionerTests/*"
```

Remove the generated `bin-c227` directories after the run.  Smoke one real Shared Plan task and
one Worktree Code task after deployment: the former must show the disclaimer and no attributed Git
counts; the latter must retain its branch range and commits.

## 5. Explicitly deferred follow-ups

### Positive Shared commit attribution

If operators need positive commit evidence for Shared tasks, create a separate card for a
task-authenticated commit ledger.  It would need an explicit per-commit report tied to the current
task token and a durable `(AgentTaskId, commit SHA)` record, with checks confirming that the SHA is
still reachable before it is rendered.  A Git hook can make that less dependent on an agent
remembering a reporting command, but it must work for warm reuse (where the live process keeps its
raw task token while the server moves its hash to the new task) and must never tag an operator's
manual commit merely because it occurs in the same checkout.  That is a materially larger launch,
API, schema, and failure-semantics design than this correctness fix.

### Manual orchestrator Git writes during active Shared work

Also open a separate operational-policy card for the root exposure recorded by CARD-0227: the
dispatcher serialises task dispatches, but an operator's direct `merge`/`push` does not consult
active Shared tasks.  A warning or a guarded merge front door may reduce checkout/build races, but
it cannot make a commit attributable after the fact and should not delay the safe probe change.

### Related recovery path

`DelegateBindRefusalRecovery.TryGitAsync` has its own Shared `--since DispatchedAt` query and then
matches task short ids/cards.  That is not the interpreter path and the measured CARD-0222 versus
CARD-0224 commit would not match it, so do not expand CARD-0227's production patch into a
settlement-behaviour change.  Its card-level matching can nevertheless collide between concurrent
tasks on the same card; include it in the authenticated-ledger follow-up's audit.

## Operator decisions

1. Approve the recommended conservative implementation as CARD-0227's scope: it removes false
   `PRODUCED` credit immediately, at the cost of no Git-progress signal for Shared tasks.
2. Decide separately whether the fleet needs the larger authenticated Shared-commit ledger, and
   whether direct orchestrator Git operations should warn or block while Shared tasks are active.
