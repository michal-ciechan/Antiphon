# Review 482f2d8c: CARD-1446 milestone 3 S5 (Code task b163cc39), Final

Verdict: **clean**. Reviewed `2619b66f545d539ec84d1611904abe4ec7bb803d` (tested source `5d2cb6f2f` plus report-only commits), base `7febbb891`.

## Receipt at the reviewed SHA

Run `20261022-105600-b81c`, plan `docs/superpowers/plans/2026-10-22-card-1446-stale-landings-plan-m3-checkpoints.md`, `--rows CP-15,CP-16,RV-2`, server2, verdict GREEN exit=0, `unlisted: none`, `outputs: deleted bin-c1446-s5/`:

```
CHECKPOINT CP-15 commit=2619b66f545d539ec84d1611904abe4ec7bb803d build=ok filter=/*/*/WorktreeResidueCandidateRowTests/* executed=3 passed=3 failed=0 skipped=0 ... slot=granted waited=165s dirty=0 source=2619b66f545d539ec84d1611904abe4ec7bb803d sourceState=clean buildSource=verified
CHECKPOINT RV-2 commit=2619b66f545d539ec84d1611904abe4ec7bb803d build=reused filter=/*/*/WorktreeResidueSweepTests/* executed=15 passed=15 failed=0 skipped=0 ... slot=granted waited=195s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=2619b66f545d539ec84d1611904abe4ec7bb803d build=reused filter=/*/*/(WorktreeResidueRegistrationTests*)|(WorktreeRetirementRaceTests*)|(CardWorktreeMirrorCleanupTests*)/* executed=84 passed=84 failed=0 skipped=0 ... slot=granted waited=255s dirty=0 sourceState=clean buildSource=verified
```

`scripts/validate-checkpoint-receipt.ps1 -Evidence .../20261022-105600-b81c/report.json -ExpectedSourceSha 2619b66f5... -Rows CP-15,CP-16,RV-2`: `CHECKPOINT SOURCE VALID source=2619b66f545d539ec84d1611904abe4ec7bb803d rows=3`, exit 0. The tree was clean before and after the run. The checkpoint tool was built under `build-slot.ps1` (`bin-rv482/`, now deleted). The one other driver run was the mutant below, also under `build-slot.ps1`, in a scratch worktree that has since been removed.

## Throwaway mutation

In a detached scratch worktree at 2619b66f5, `PruneWorktreeResidueHeldRowsAsync` was changed to `.Where(c => c.EvaluatedAt < cutoff)`, so it also prunes action rows. Running `/*/*/WorktreeResidueCandidateRowTests/C1446_retention_prunes_only_old_held_rows` under build-slot gave total 1, failed 1: `PruneWorktreeResidueHeldRowsAsync(...) should be 2 but was 6` ("pruned"). The worktree was removed and the mutation discarded.

## Checks

1. **Migration:** passes. Up is one `AddColumn` (nullable `character varying(200)`) and Down is one `DropColumn`, both additive with no rewrite. There is no index, which is correct because the column is read only by the already-indexed `CandidateKey` lookup. The id `20261022095749` sorts after `origin/master`'s newest migration, `20261022074212_AddProviderEndpoints`, at `e5e19c1d3`. The merge-tree with `origin/master` (`e5e19c1d3`) is clean (tree `e2a8cd1c7`). In that merged tree, the snapshot, AppDbContext and entity differ from `origin/master` only by the `LastOutcomeKey` property and its `HasMaxLength(200)`. At the branch, the Designer's `BuildTargetModel` equals the snapshot's `BuildModel`. So the merged snapshot is master's model plus this column. EF's pending-model check reads the snapshot, so the Designer missing ProviderEndpoints after the merge is cosmetic, as the report says. I did not run `dotnet ef migrations has-pending-model-changes` itself; the merged-tree diff is the equivalent evidence.
2. **Dedupe:** I traced every transition. Held→same Held writes no row. Held→action, action→Held, a reason change and a lane change each write a row because the key differs. A deleted cursor is re-created with a null key and writes a row. Preview writes every row and leaves the cursor alone. A server restart is harmless because the state lives in the database. The only readers of candidate rows are `GetRunAsync` paging, the preview checks (preview writes every row) and `queuedBefore`. `queuedBefore` counts only `Queued` rows, which are never deduped and never pruned, so the abandonment cap from S4c/M2 is intact. `PublicationLanePolicy.Decide` reads only `NotBefore`. `ShouldWriteRow` and `GetOrAddCursor` never change `NotBefore`, so pacing is unchanged. No later lane reads Held rows (plan G-7). No hidden operator-relied state change found.
3. **Retention:** passes. The delete selects only `Outcome in (Held, Preview)` with `EvaluatedAt < cutoff`. The cutoff is exclusive and comes from the UTC `TimeProvider`. Run headers and action rows are untouched, and 0 or less disables the pass. The cursor keeps its own key, so no row is needed for the dedupe. A second pass deletes nothing (tested). It is one `ExecuteDeleteAsync` with no batching. The first pass after deploy deletes about 95k rows in a single statement over a sequential scan, which is acceptable at this size.
4. **Tests:** V-11 and V-12 assert behaviour: row counts, counters, the log line, cursor keys, and the exact surviving ids. The CP-14 red at `d5cf68dc4` failed with `second-run-rows should be 0 but was 5`, which is the plan's stated reason. RV-2's floor of 15 matches the class's 15 methods (executed=15), and its unlisted reason is legitimate: S5 rewrote the SettledTask cursor write.
5. **rc split:** verified by a trial cherry-pick in a scratch worktree on `origin/rc/2026-10-20` (`ceb6e3d6b`), since discarded. `efbab099a` (S5-test: column, migration and tests) and `d5cf68dc4` apply cleanly. `0e157c67f` conflicts in `WorktreeResidueSweepService.cs` and `docs/orchestration-loop.md`, because rc has no `PublicationLanePolicy` (S4c). Its `DataRetentionService`, `RetentionSettings` and `appsettings` hunks pass `git apply --check` on their own. So the column, migration, retention and V-12 can go to rc alone, and the dedupe and docs need S4c, as the report says. The Designer must be regenerated on rc. If the master-only migrations `015609`/`074212` later reach rc, they would sort before `095749`; EF still applies them, but out of id order.

`check-evidence-diff.ps1 -BaseRef 7febbb891 -HeadRef 2619b66f5`: `commits=6 entries=2 violations=0`.

## Disclosures (not defects under the standing verdict policy)

- **D1, no witness for "action rows always written"** (already disclosed by Code). A mutant that drops `outcome == Held` from `unchanged` survives every S5 row. It would lose an operator-relevant record only when two consecutive non-preview outcomes share an action key:
  - Publication `Queued`→`Queued` with `CleanupRetryCooldownDays=0`. `queuedBefore` would then undercount, so the `MaxCleanupRetries` abandonment would never fire. With the default of 7 days, a `Held|cooldown` row comes in between.
  - SettledTask `Refused`→`Refused`. Nothing reads the task cursor's 24h `NotBefore`, which predates S5.
  
  None of this is reachable while `Execute=false`. This is the Backlog suggestion below.
- **D2, key collision on `Held|release_required`.** `EvaluateEligibilityAsync` returns `(true, null)`, which becomes `release_required`. So an authorized candidate that is not acted on (Execute off, or budget used up) has the same key as a candidate with no release. The daily rows therefore no longer show it becoming eligible, which used to be visible as `RetirementId` appearing. Preview still shows it, and it implements the plan's key exactly. The ambiguous reason code predates S5.
- **D3, `Removed` rows keep growing.** A retirement stays `Active` when it reaches `Complete`, so every daily run writes a `SettledTask|Removed|removed` row for it. Removed rows are never deduped or pruned, so after Execute is turned on they grow without limit. This predates S5; S5 neither causes nor fixes it.
- **D4, concurrent runs.** `WorktreeResidueJob` has no `DisableConcurrentExecution`. In two overlapping non-preview runs, the second one's first-time cursor inserts would fail with a unique violation, so that run fails and loses nothing. A verdict that flaps between the two runs could leave the cursor key different from the last row written. This is narrow.
- **D5, held candidates vanish from run pages.** A candidate held for more than 30 days has no row in any retained run once its only row is pruned; only the counters still count it. This is by design (G-7: no reader).

## Backlog suggestion (one)

Add an `Execute=true` witness for S5 "action rows always written". Run two `Queued` retries with `CleanupRetryCooldownDays=0`, assert two `Queued` rows, and assert that `MaxCleanupRetries` is reached. Optionally give authorized-but-not-acted candidates a distinct reason code (D2).
