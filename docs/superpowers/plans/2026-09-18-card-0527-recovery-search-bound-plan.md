# CARD-0527: bound the settlement recovery search so a slow checkout degrades instead of re-handing forever

Plan task `fcec8966`, 2026-09-18, inspected checkout `3a62074e` (= `origin/master`; the
investigation commit `b945ad34` is cherry-picked onto this branch as `fd4daf12`, code unchanged).
Investigation:
[2026-09-18-card-0527-commit-on-task-settle.md](../../investigations/2026-09-18-card-0527-commit-on-task-settle.md)
(task `e28d4c3d`). Read-only against code and three live checkouts; timing runs are read-only
`git log` commands. No fix was built. TestDesign is a separate stage; the acceptance table at the
end is written at guard granularity so it can be short.

## Disposition in four lines

1. **The `--reflog` walk is replaced, not tuned.** The identity resolver searches refs
   (`git log --all …`, 1.6 s in the 402-worktree checkout) plus this checkout's own HEAD reflog
   (`git log --walk-reflogs HEAD …`, 0.13 s). Together they cover every scenario the F15 recipe
   covered except one manual-sabotage combination (D-1). `--single-worktree` was measured and does
   not help (79.7 s), so the cost is not the per-worktree reflogs and no scoping of `--reflog`
   rescues it.
2. **A failed search holds a settlement only when a durable obligation says a commit may exist.**
   With no unresolved `CommitRecoveryStarted` row and no found settlement commit, every inspection
   failure in the hook (search, repository, status) degrades to an honest `uncommitted:N (… unavailable)`
   note plus the pre-existing Warning event, and the task settles once (D-2). The nine stuck tasks
   had no obligation; under D-2 each would have settled on its first boundary with the warning.
3. **The Worktree sweep keeps its merge when only the receipt is missing.** A
   `CommitInspectionPendingException` after a successful gated commit is caught by name in
   `TryMergeBackAsync`, the merge proceeds, and the outcome detail says the receipt is pending (D-3).
   Today it is reported as "Committing the delegate's work failed" with the branch left unmerged.
4. **No new knobs.** No search-specific timeout, no reflog-hygiene job, no SHA-in-row scheme (D-4).
   CARD-0547's bounded hold for obligation-bearing settlements is untouched.

## Ground truth

Verified on `3a62074e` on 2026-09-18 (line numbers at that SHA) unless the row cites the investigation.

| Brief / investigation assumption | Observed | Consequence |
|---|---|---|
| The live recipe takes 74-135 s; without `--reflog` 1.1 s. | Re-measured today in `C:\src\Antiphon` (403 registered worktrees, HEAD `3a62074e`): `--all` **1.6 s**; `--all --reflog --single-worktree` **79.7 s**; `--walk-reflogs --all` (every reflog entry, no ancestry walk) **6.2 s**; `--walk-reflogs HEAD` **0.13 s** (1 284 entries). From a linked worktree (`card-task-fcec8966`): `--all` **2.5 s**, `--walk-reflogs HEAD` **0.4 s**. Loose objects 7 516, 9 packs. | D-1 recipe is two legs, `--all` then `-g HEAD`. The internal cause of the `--reflog` cost is still not isolated (entries alone cost 6 s; the remaining ~70 s is inside the `--reflog` revision walk) and the design does not depend on it. |
| "Decide if reflog coverage is load-bearing." | It is load-bearing for exactly one class: a gated commit that is no longer on any ref when the re-hand searches (V-75/V-76 `reflog` variant: `branch -D master` after the commit). `CommitOnlyAsync` is porcelain `git commit` (`GitWorkspaceService.cs:824-841`), which always appends to the checkout's HEAD reflog. Scratch check (git 2.50.1): `git log --walk-reflogs HEAD --fixed-strings --all-match --grep=<task> --grep=<identity> --format=%H` finds the deleted-branch commit; `--all` does not. | The HEAD-reflog leg keeps that coverage at 0.13-0.4 s. |
| The unborn-HEAD case "can still have attempts in reflogs". | Scratch check: on an unborn HEAD both `-g HEAD` and `-g 'HEAD@{0}'` exit 128 (`unknown revision`); `--all --reflog` still works. The existing unborn validation (`:948-964`: `rev-parse --verify HEAD` = 1, `symbolic-ref` → `refs/heads/*`, `show-ref` = 1) already computes `unborn`. V-75/V-76 `unborn` variants keep `master`, so `--all` finds the commit. | D-1: skip the reflog leg when `unborn` is true. Dropped combination: unborn HEAD **and** the commit gone from every ref (requires `checkout --orphan` plus `branch -D` by hand between commit and re-hand). Documented, not covered. |
| Expired or missing reflogs break `-g HEAD`. | Scratch check: after `reflog expire --expire=now --all`, and with `.git/logs` deleted, `git log --walk-reflogs HEAD --format=%H` exits 0 with no output. | No special-casing; an empty reflog leg is a successful empty leg. |
| The timeout is swallowed and re-handed every 60 s with no bound. | `RunCoreAsync` returns `(-1, "", "timeout")` after the 15 s budget (`:1048`, `:1063-1071`); `FindGatedCommitsAsync` returns `Succeeded=false` (`:967`); `TryCommitOnSettleAsync` throws at `:3177-3179`; `OnTurnEndLockedAsync` catches every non-cancellation exception at `:203-206` and logs a warning; `SettleDeferredReportsAsync` re-hands an unchanged boundary once per `ReportSweepRehandSeconds` (`AgentTaskDispatcher.cs:2424`). No attempt counter exists. With no obligation there is no CARD-0547 attention row, so nothing on the board shows the loop. | D-2 removes the throw for obligation-free settlements; nothing else in the loop needs to change. |
| Worktree tasks are exposed through `GatedCommitService.cs:195` (reasoned, not observed). | Confirmed from code plus an existing test, still not from production: `CommitHeldAsync` ends in `InspectCommitAsync(knownCommitted: true)` (`:195`), which runs the same resolver (`:213`) and throws `CommitInspectionPendingException` on any failed or non-unique search (`:216`); `GatedCommitLiteralPathsTests.cs:80` proves the throw. `TryMergeBackAsync` catches it as a generic exception (`DelegationWorktreeService.cs:552-555`) → `MergeResult.Failed`; `MergeBackAsync` then writes a `Failed` event "Merge-back failed: …" and the note `NOT merged (…) — branch … kept` (`AgentTaskReplyService.cs:1499-1502`). The gated result's `Sha`/`Files` are not used after the outcome check (`:557-604`). Exposure needs a dirty worktree at settle; every Worktree settle since go-live was clean (investigation §6). | D-3: one wrong outcome per dirty Worktree settle, not a loop; fixed by catching the pending exception by name and continuing. |
| "Record the gated commit's SHA in the `CommitRecoveryStarted` row." | The row is saved in its own scope **before** `gated.CommitAsync` (`:3299-3307`), so the SHA is unknown when it is written; the failure being recovered is exactly the save that fails after the commit. The `Committed` row (`:3397`) is written in the outer context that fails. | Rejected in D-4. |
| A separate, longer timeout for the search. | Precedent exists (`GitSettings.WorktreeAddTimeoutSeconds`, `WorktreeRemoveTimeoutSeconds`). On 2026-09-17, 19 *other* git commands (`status`, `rev-parse`, `log -50`) also timed out under load; the settle needs 5-8 git calls, not one. | Rejected in D-4: it moves the cliff, and the D-2 degrade is what handles load. |
| The `HEAD`-first inspection shortcut after a commit. | F15 removed every "ad-hoc HEAD-relative search" as the attempt identity (`docs/investigations/2026-09-16-card-0527-f15-f16-repair.md:11-19`); the resolver's uniqueness check (`GatedCommitService.cs:214`) is also the duplicate-trailer guard after a rebase. | Rejected in D-3: with the two-leg recipe at 0.5-4 s per resolution there is nothing left to buy, and the identity contract stays single-sourced. |
| Existing failure injections would need rewriting. | Six tests inject `args.Contains("--all") ? (128, "", "history unavailable")` (`GatedCommitRecoveryIdentityTests.cs:43`, `AgentTaskReplyC527MovedBranchTests.cs:41`, `GatedCommitTrailerTests.cs:45`, `AgentTaskCommitTrailerTests.cs:57`, `AgentTaskReplyC547RecoveryTests.cs:110`, plus `C527InspectionTests.cs:45` on `status`/`--is-inside-work-tree`). Every one runs with an obligation already saved or through `RecoverAsync`. | D-1 runs the `--all` leg first and short-circuits on its failure, so all six keep their meaning unchanged; none pins the obligation-free hold that D-2 removes. |
| The Worktree test graph can inject a failing search. | `DelegationTestServices.CreateGitGraph` builds `new GitWorkspaceService(...)` (`DelegationTestServices.cs:123`) and wires the gate from it (`:124-130`); `DelegationWorktreeTests.CreateService` (`:1061-1072`) calls it. | S3 threads an optional `GitWorkspaceService? workspaceGit` parameter through `CreateGitGraph`/`CreateService` so a `RecordingGitWorkspaceService` can fail `--all` after `commit`. |
| The note header decides durable completion minting. | `PersistDeliverThenReleaseAsync` mints the durable completion outbox only for `committed:` / `commit refused:` / `commit task` headers or `Durable` notes (`:1565-1572`). Existing `uncommitted:N (…)` headers do not. | D-2's new headers start with `uncommitted:` and are not `Durable`; parity with the Off arm. |
| The investigation's per-day metric. | The `Report names N file(s) still uncommitted in the shared checkout:` Warning text is what §3 counted per day. | D-2's Warning event keeps that prefix verbatim. |

## Decisions

### D-1. Resolver recipe: refs plus this checkout's HEAD reflog, never `--reflog`

`GitWorkspaceService.FindGatedCommitsAsync` (`:945-989`) replaces the single
`log --all --reflog --fixed-strings --all-match --grep=<task> --grep=<identity> --format=%H` with two legs, in this order:

1. `log --all --fixed-strings --all-match --grep=<taskId:D> --grep=<identity> --format=%H`
2. `log --walk-reflogs HEAD --fixed-strings --all-match --grep=<taskId:D> --grep=<identity> --format=%H`,
   **skipped when `unborn` is true** (the existing validation at `:948-964` already computes it).

A non-zero exit on leg 1 returns `new(false, [], code, stderr)` without running leg 2. A non-zero
exit on leg 2 returns failure the same way. Candidate SHAs are the ordinal-distinct union of both
outputs; the per-SHA exact-trailer verification (`:969-985`) is unchanged, as are
`FindSettlementCommitsAsync`, `FindCommitOperationAsync`, `ReadTrailersAsync`, `HasExactTrailer`
and every caller. The comment block at `:942-944` is rewritten to state the coverage: every ref,
plus every commit this checkout's HEAD has pointed at within reflog retention.

Why `HEAD` and not the current branch: a gated commit is porcelain `git commit` at HEAD (attached or
detached), so `logs/HEAD` (or the linked worktree's own `logs/HEAD`) always records it; the branch
reflog is deleted with the branch (`branch -D`), HEAD's is not. Why not `--walk-reflogs --all`:
6.2 s here and it grows with the ref count; the HEAD reflog is bounded by this checkout's own history.

Rejected:

- **Keep `--reflog`, scope it with `--single-worktree`.** Measured 79.7 s. The cost is not the 402
  per-worktree reflogs.
- **Keep `--reflog` behind a longer timeout.** See D-4.
- **Drop reflog coverage entirely (`--all` only).** Loses the `reflog` variant of V-75/V-76 (a real
  scenario after any `reset`/`rebase` of the shared branch between commit and re-hand) for no gain
  over the 0.13-0.4 s HEAD leg.
- **Fall back to `--all --reflog` only when `unborn`.** Correct, but it keeps a 75 s command reachable
  from production for a combination that needs manual sabotage; the dropped combination is documented instead.
- **Run leg 2 first (cheaper).** The six existing failure injections key on `--all`; running it
  first keeps their short-circuit semantics and costs nothing.

### D-2. Only a durable obligation or a found settlement commit may hold a settlement

In `TryCommitOnSettleAsync` (`:3139-3382`) define once, after `existing` and `repository` are read:

```csharp
// CARD-0527: the durable obligation (or a commit already found for this settlement) is the
// only evidence that a commit may exist. Without it, no inspection failure holds the task.
var holds = recoveryStarted || existing.Items.Count > 0;
```

and change the four hold sites so that each throws `ServiceUnavailableException` (same messages,
same `settlement_recovery_unavailable` code) **only when `holds`**, and otherwise returns a note:

| Site (at `3a62074e`) | Today | Under D-2 when `!holds` |
|---|---|---|
| `:3177-3179` search failed / ambiguous / obligation with no commit | throw | `CommitSkippedNoteAsync(… "history search", existing.Error)` |
| `:3181-3183` repository inspection not `Worktree` (and not the `NotWorktree` null at `:3172`) | throw | `CommitSkippedNoteAsync(… "repository inspection", null)` |
| `:3185-3192` status failed | throw when `existing.Items.Count == 1 \|\| nothingToCommit` | unchanged note `commit refused: status inspection unavailable`; the throw condition becomes `holds` |
| `:3337-3345` fresh `NothingToCommit`, residual status failed | throw | `new("no commit needed (status inspection unavailable)", "The gate found nothing to commit for the task's footprint; residual dirty paths could not be inspected.")`, not `Durable` |

`CommitSkippedNoteAsync(db, git, task, repo, what, error, now, ct)`:

1. `TryGetChangesAsync`; on failure return the existing `commit refused: status inspection unavailable` note.
2. `SettlementDirtyPaths`; if empty return `null` (the hook would have done nothing; the
   `TryDescribeGitAsync` fallback renders `landed`/`unattributable` as before).
3. Add a Warning event whose Detail is
   `Report names {N} file(s) still uncommitted in the shared checkout: a, b. Commit-on-settle skipped: {what} unavailable ({error}).`
   (first 20 paths, same prefix as the Off arm at `:3236-3240` so the log metric keeps counting).
4. `_logger.LogWarning` naming the task, repo, `what`, `error` and `N`.
5. Return `new($"uncommitted:{N} ({what} unavailable)", "The report names {N} file(s) that are still
   uncommitted in the shared checkout — the work has not landed. Commit-on-settle was skipped because
   the {what} was unavailable ({error}); no commit was attempted. Commit before building on it.")`.

No commit is attempted and no Commit-role child is spawned on this path: git has just failed in
this checkout, and the child would run the same git under the same load. `nothingToCommit` (a
resolved obligation) never holds: the `CommitRecoveryNotNeeded` row is the "durable, explicitly
known no-commit result" the `:3175-3176` comment demands, so a failed search falls through to
`NoCommitNeededNote` exactly as a successful empty one does.

Unchanged: every hold when `holds` is true (V-76, the CARD-0547 hold tests, the D-3 bounded hold
and its attention row); `existing.Items.Count > 1` with no obligation still throws (unreachable in
practice: the second identity can only come from a rebase copy of a commit whose obligation is
unresolved).

Rejected:

- **Proceed to the gate when the search fails without an obligation.** Safe against double
  commits (the obligation is saved before every commit), but it asks the same failing git for
  `status`, `check-ignore`, `add` and `commit` within the same 15 s budget each; the honest outcome
  under a failing git is "not committed", which is what the note says.
- **Spawn the Commit child on the degrade path.** The child's settle audit needs the same
  history reads; it turns one warning into a task at the cap.
- **Bound the obligation-free hold by attempt count instead of removing it.** The hold buys
  nothing without an obligation: no commit can exist to recover.

### D-3. Worktree sweep: a pending receipt is not a failed commit

In `DelegationWorktreeService.TryMergeBackAsync` (`:514-551`) add
`catch (CommitInspectionPendingException pending)` before the generic catch: log a warning, set
`receipt = $"gated commit receipt pending (operation {pending.OperationId})"`, and continue to the
merge. Every `MergeOutcome` returned after that point appends `"; " + receipt` to its `Detail`
(`Merged`, `LeftForHuman` when no target is set, `Conflicted`, `Failed`). `MergeBackAsync`
(`AgentTaskReplyService.cs:1445-1507`) needs no change: the `Merged` event already carries
`outcome.Detail`. `CommitInspectionPendingException` gains an `OperationId` property (it currently
only stores it in the extensions dictionary, `CommitInspectionPendingException.cs:6-14`).

The rebase and merge do not need the receipt: `_landingGit.InspectAsync` reads the branch HEAD and
the trailers travel with the rebased commit. A later `GET /api/agent-tasks/{id}/commit/{operationId}`
from the main checkout finds the rebased copy on the target once the worktree (and its HEAD reflog)
is removed, exactly as today.

Rejected: leave the outcome as `Failed` (the commit exists; "Committing the delegate's work failed"
is false and the branch is stranded); block the merge until the receipt is available (a settle is
not a place to wait on git).

### D-4. No new configuration

No `GitSettings.HistorySearchTimeoutSeconds`; no reflog expiry job; no `--reflog` fallback. The
resolver is now 0.5-4 s in the worst known checkout under the existing 15 s budget, and D-2/D-3
define what happens when even that is exceeded. `Delegation:CommitRecoveryHoldMinutes`,
`ReportSweepRehandSeconds` and `Delegation:CommitOnSettle` keep their meanings.

### D-5. Observability on the degrade paths

The existing `git {Args} timed out after {Timeout}` warning (`GitWorkspaceService.cs:1067-1069`)
stays. D-2 adds one Warning event per skipped settle (board-visible on the task timeline) and one
log line; D-3 adds the receipt text to the `Merged` event and one log line. No attention kind, no
incident: the task settles and the note reaches the caller, which is the surface the card asked for.

### D-6. Board corrections (for the orchestrator; no code, not re-litigated here)

The investigation established, and this plan carries forward without re-examination:

- CARD-0527's revision-33 note and CARD-0548/CARD-0549 are wrong: the 2026-09-15..17 chain
  (Investigate → Plan → TestDesign → Code S1-S6 → repair rounds F8-F18) was this card's own feature
  and its commit-recovery sub-mechanism, not an unrelated "recovery-identity-loss" fix. CARD-0549 is a
  retroactive duplicate record of those repair rounds; CARD-0548's "zero work done" premise is false.
- Mutation for CARD-0527 was never dispatched: PC-1..PC-87 in
  `docs/investigations/2026-09-16-card-0527-f17-f18-repair.md` are owed. This plan's S1 changes the
  wording of PC-74 ("remove `--all`") and PC-75 (now "remove the `--walk-reflogs HEAD` leg"); both
  still fail the `Recovery_finds_exact_attempt_across_refs_and_reflogs` variants they name.
- The nine tasks in investigation §3 (`c9140e5d`, `b355c108`, `117a4690`, `4a2497b2`, `91773d94`,
  `2b64d75a`, `0d3ff398`, `b5cd5ee0`, `fb65210d`) were this defect, not a report-format stall;
  CARD-0551 should be re-scoped or closed against this card.
- The operator memory `feedback-default-worktree-isolation` (2026-09-17 20:43Z) masks the defect;
  once this lands, Shared dispatch on Antiphon is no longer exposed to it. Whether to keep the
  policy is a separate question (its stated reason was slot serialisation).

### D-7. Noticed, out of scope

- `:3322-3336` (CARD-0547 D-2): after `CommitFailed`, a failed `absent` search leaves the obligation
  open and the settle still proceeds to spawn the child and return a note, so the task settles
  Succeeded with an unresolved obligation that `BuildOpenTaskItemsAsync` never shows (it projects
  open tasks only). Pre-existing; file under CARD-0547 follow-up.
- Why `--reflog` costs ~70 s beyond reading the entries in this checkout. Not needed for the fix.
- Checkout hygiene (`git worktree prune`, `reflog expire`) for `C:\src\Antiphon`. The fix must not
  depend on it.

## Components (exact)

| File | Change |
|---|---|
| `server/Application/Services/GitWorkspaceService.cs` `:942-989` | D-1 two-leg recipe; comment rewritten. |
| `server/Application/Services/AgentTaskReplyService.cs` `:3168-3192`, `:3337-3345` | D-2 `holds`, four sites, `CommitSkippedNoteAsync`. |
| `server/Application/Services/DelegationWorktreeService.cs` `:511-555` and the returns through `:604` | D-3 pending catch, receipt suffix. |
| `server/Application/Exceptions/CommitInspectionPendingException.cs` | `OperationId` property. |
| `tests/Antiphon.Tests/TestHelpers/DelegationTestServices.cs` `:101-132`, `tests/Antiphon.Tests/Application/DelegationWorktreeTests.cs` `:1061-1072` | optional `GitWorkspaceService? workspaceGit` seam. |
| `docs/orchestration-loop.md` "Commit on settle" (`:266-283`) | recipe, skipped headers, receipt-pending detail. `CommitOnSettleDocumentationTests` pins are additive-safe. |
| `docs/investigations/2026-09-16-card-0527-f17-f18-repair.md` | not edited; PC-74/75 wording change is recorded here (D-6). |

## Slices

### S1. Resolver recipe (independent; lands first, is the production fix on its own)

D-1. Tests: `GatedCommitRecoveryIdentityTests.Recovery_finds_exact_attempt_across_refs_and_reflogs`
(3 variants) and `AgentTaskReplyC527MovedBranchTests.C527_branch_movement_after_failed_save_recovers_original_parent_receipt`
(3 variants) stay green unchanged. New in `GatedCommitRecoveryIdentityTests`: the recipe pin
(A-1), reflog-leg failure (A-2), `--all` short-circuit (A-3), unborn skips the reflog leg (A-4).
Code-stage evidence: run the exact two commands once, read-only, in `C:\src\Antiphon` and in one
linked worktree and record the wall times in the Code report (A-17).

### S2. Obligation-free settlements degrade (depends on nothing; verified with S1's recipe)

D-2 and D-5. New tests in a new `AgentTaskReplyC527SkippedTests.cs` partial of
`AgentTaskReplyIntegrationTests`, using `SeedC527Async`, `C527Factory(gitSpy:)`,
`RecordingGitWorkspaceService.OverrideRun` returning `(-1, "", "timeout")` (the exact shape
`RunCoreAsync` returns on the budget) and `AssertParentReceivedNoteAsync`: A-6..A-9, A-11, A-12.
Carried forward without edits: every `AssertC527RecoveryPendingAsync` caller and the C547 hold tests (A-10).

### S3. Worktree receipt pending (independent of S2)

D-3 and the test seam. Tests in `DelegationWorktreeTests`: A-13, A-15; `a_commit_all_failure_on_a_live_worktree_still_fails`
unchanged (A-14). Integration: one `AgentTaskReplyIntegrationTests` case that settles a dirty
Worktree task with the injected search failure and asserts the note says `merged →` and the
`Merged` event contains `receipt pending` (A-13b).

### S4. Docs

`docs/orchestration-loop.md` "Commit on settle": one sentence each for the two-leg search and its
coverage, the `uncommitted:N (history search unavailable)` / `(repository inspection unavailable)` /
`no commit needed (status inspection unavailable)` headers, and the Worktree `receipt pending`
detail. `docs/cards/` is generated; do not edit.

Verification profile for Code (ordinary V/R): the 21 `C527*`/`C547*`/`CommitOnSettle*`/`CommitRecovery*`/`GatedCommit*`
test files plus `DelegationWorktreeTests`, `AgentTaskCommitTrailerTests`,
`AgentTaskRetryCommitRecoveryEndpointTests`, run with `--treenode-filter` per class; the full
`Antiphon.Tests` assembly is not required (CARD-0110 profile). Six inherited reds are documented in
the F17/F18 record; re-verify any new red at the base commit before attributing it.

## Acceptance cases and guard candidates for TestDesign

| ID | Slice | Setup | Expected |
|---|---|---|---|
| A-1 | S1 | Spy on any resolver call | Recorded `log` searches are exactly `["log","--all","--fixed-strings","--all-match","--grep=<task>","--grep=<identity>","--format=%H"]` then `["log","--walk-reflogs","HEAD", …same tail]`; no recorded args contain `--reflog`. |
| A-2 | S1 | `OverrideRun` fails args containing `--walk-reflogs` | `FindSettlementCommitsAsync.Succeeded == false`; `RecoverAsync` throws `ServiceUnavailableException`; with an obligation present the settlement holds (same shape as V-76's `--all` failure). |
| A-3 | S1 | `OverrideRun` fails `--all` | No `--walk-reflogs` call is recorded for that resolution. |
| A-4 | S1 | V-75 `unborn` variant | No `--walk-reflogs` call recorded; commit still recovered from `--all`. |
| A-5 | S1 | V-75 `branch` variant (expired reflogs) and `reflog` variant (`branch -D master`) | Both recover the original SHA and files (existing assertions). |
| A-6 | S2 | No obligation; dirty attributable footprint (2 files); `OverrideRun` returns `(-1,"","timeout")` for `--all` | Task `Succeeded` on the first boundary; header `uncommitted:2 (history search unavailable)`; one Warning event starting `Report names 2 file(s) still uncommitted in the shared checkout:` and containing `history search unavailable (timeout)`; zero `CommitRecoveryStarted`/`Committed` rows; no child; spy verbs contain no `commit`/`push`; `write-tree` and `symbolic-ref HEAD` unchanged; parent receives one note; a second `OnTurnEndAsync` is inert (`SubmittedBodies.Count == 1`). |
| A-7 | S2 | As A-6 with a clean tree and a report naming committed files | Hook returns `null`; header `landed` (fallback arm). |
| A-8 | S2 | No obligation; `--is-inside-work-tree` fails non-negatively (`retry prerequisite unavailable`) | Header `uncommitted:N (repository inspection unavailable)`; settles once. Negative (`not a git repository`) still returns `null` as today. |
| A-9 | S2 | No obligation; `--all` fails and `status` fails | Header `commit refused: status inspection unavailable`; settles once; no commit. |
| A-10 | S2 | Obligation present (commit made, save failed), `--all` fails on re-hand | Holds: `AssertC527RecoveryPendingAsync` shape; unchanged tests `C527_branch_movement…`, `C547_CommitFailed_with_a_failed_history_search…`, `C527_retry_prerequisite_failure…`. |
| A-11 | S2 | Obligation resolved by `CommitRecoveryNotNeeded` (gate found nothing), save failed; on re-hand `--all` fails | Settles with `NoCommitNeededNote` (`landed` or `uncommitted:N (selected changes reverted)`); no second `commit`; no hold. |
| A-12 | S2 | Fresh settle: gate returns `NothingToCommit`, then `status` fails | Header `no commit needed (status inspection unavailable)`; `CommitRecoveryNotNeeded` row present; settles once. |
| A-13 | S3 | Dirty worktree with a merge target; `OverrideRun` fails `--all` once `commit` has been recorded | `MergeResult.Merged`; target contains the file; `Detail` contains `receipt pending (operation `; worktree removed. |
| A-13b | S3 | Same through `OnTurnEndAsync` on a Worktree task | Note contains `merged →`; `Merged` event Detail contains `receipt pending`; no `Failed` event. |
| A-14 | S3 | `index.lock` held (existing test) | Still `Failed` with `Committing the delegate's work failed`. |
| A-15 | S3 | As A-13 with no merge target | `LeftForHuman`; `Detail` ends with the receipt note. |
| A-16 | S4 | Docs | `docs/orchestration-loop.md` names the two legs, the three skipped headers and `receipt pending`; `CommitOnSettleDocumentationTests` green. |
| A-17 | S1 | Manual, Code report | The two recipe commands complete in under 5 s each in `C:\src\Antiphon` and in one linked worktree (record the numbers). |

Guard candidates (Mutation PCs; method-scoped): remove the `--walk-reflogs` leg → A-5 `reflog`
variant red; ignore leg-2 failure → A-2 red; run leg 2 before leg 1 → A-3 red; re-add `--reflog` →
A-1 red; `holds = true` unconditionally → A-6/A-8/A-9/A-11/A-12 red; `holds = false`
unconditionally → A-10 red (the PC-77 shape); catch the pending exception as `Failed` → A-13 red;
drop the receipt suffix → A-13/A-15 red; change the Warning prefix → A-6 red.

## Carried-forward controls (must stay green, none renumbered)

V-75, V-76 (all variants), V-77..V-79, R-1..R-17 from the F17/F18 record; every
`AgentTaskReplyC527*`, `AgentTaskReplyC547*`, `CommitRecoveryObligations*`, `AttentionServiceCommitRecoveryTests`,
`AgentTaskRetryCommitRecoveryEndpointTests`, `GatedCommit*`, `CommitOnSettle*` test;
`DelegationWorktreeTests` in full.

## Scope boundaries

In: D-1..D-5, S1..S4. Out: D-6 board edits (orchestrator), D-7 items, the worktree-default policy,
CARD-0547's open-obligation-after-CommitFailed residue, any change to `GatedCommitService`'s
ignore gate, trailers, manifest or HTTP endpoints, and any change to what delegates are told about committing.

## Cost and next stage

TestDesign: about 45 minutes (the table above is at guard granularity; settle the S2 test file
name, the S3 seam signature and whether A-13b lives in `AgentTaskReplyC527RecoveryTests`).
Code: 2-3 hours including tests and the targeted verification runs (about 10 minutes per targeted
pass). Review: ordinary, different company from Code. Land S1 even if S2/S3 need another round: S1
alone stops the production loop (the search completes; the hook commits or warns as designed).
Mutation for this card (PC-1..87 plus the guards above) is owed after land.
