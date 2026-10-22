# rc bundle B dry-run onto rc/2026-10-20 (2026-10-22)

Task 9ce9eda3 (Investigate, read-only). Scratch detached worktrees at `origin/rc/2026-10-20`
`6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45` (bundle A applied). Nothing was pushed except this
document. The recipe style follows [the bundle A dry-run](2026-10-22-rc-bundle-dry-run.md).

## Outcome

All twelve picked items apply to rc. No code hunk needed a hand-resolved conflict. Five
adaptations are needed, and each is scripted in the recipe. Both builds have 0 errors. The evidence
diff is clean. The bundle adds one EF migration, which sorts after rc's latest.

- The recipe below ran **verbatim twice**, in two fresh worktrees, and produced the same tree both
  times: `aa7963bbd977b5949fa2d40d07fd5e292db0f41a`. That is 94 commits over rc: 89 clean picks,
  1 pick whose only conflict was a plan doc, and 4 adaptation commits. A 5th adaptation is an amend
  of the CARD-1463 pick. The recipe also skips 10 commits that touch nothing but plan docs or
  reports.
- Commit SHAs depend on committer time. The two runs gave tips `e04df04cd` and `ab2ef4617`. The
  **tree** is deterministic, so a mechanical re-run must reproduce it exactly.
- Two plan docs are kept because a test reads each of them:
  - The CARD-1469 plan, read by `StandingPipelinePolicyDocumentationTests`.
  - **The CARD-1409 plan**, read by `LandNonCodeSkipTests.C1409_NormaliserAndItemOperationTablesHaveWitnesses`.

  The kept CARD-1409 plan is byte-identical to the copy at origin/master. Every other plan doc a
  pick touches is undone. rc goes from 832 to 834 plan docs.
- The recipe's `pick()` corrects a latent bug in the bundle A helper. Bundle A ran `git rm` on
  every plan path that a pick touched. CARD-1409 `624d4466d` modifies
  `2026-10-21-card-1400-docs-input-bypasses-plan.md`, which **exists on rc**, so the old helper
  would have deleted it. The new `undo()` restores rc's copy instead.
- **CARD-1420 was skipped as briefed.** Its schema cache port (branch `feat/card-task-242906fa`,
  `fed245d31`) needs its own Review.

## SHA verification

`git cat-file -t` returns `commit` for every listed SHA. Of the listed SHAs, only these are
ancestors of origin/master: `6ee09ad07`, `03686bed4`, `020276b3b`, `cc4a5564d`, `9b39df4e0` and
`6a2fe6ae3`. Each of the others has an origin/master copy with the same subject and the **same
stable patch-id**. The recipe uses the master copies throughout, so the `-x` trailers name master
SHAs.

| Item | Listed (branch) SHA | origin/master copy used |
|---|---|---|
| 1427/1428 | (none listed) | 020276b3b 0fcc1c1b2 0984d8e7d c00e3d1da e1aacb4cd 927a0b78c be14b8b0d (+ plan-only 87855ba43, 77f34b7f3 skipped) |
| 1432 | (none listed) | 9f50222f2 74deabb48 c579d09ce 6cd9317a0 c336a9887 a056d9164 88a1080d1 5a016184f ee62ec013 705026da8 (+ CARD-1456 f4ba26383) |
| 1449 | 6ee09ad07 / 03686bed4 | same (on master) + 5c5897312 |
| 1412 | 52cd17ddb | 29dc7dc1e |
| 1452 | 5fbe4e6d9 | 4e4f04726 |
| 1446 S1+S2 | 47687718a ab1214259 de4e8f59c fe0c0d81e f8510082e c05f27a6d + aa4fa734f | e5df91864 32bfcbc9e c3fe1fc26 1d4062563 b80ae90e3 78297d864 + 824621e96 |
| 1446 S4a/S4b/S4d, m2 | (by name) | b41307495 045dca309 / 3d2c8cb8a d6064590a / 7292b36db; m2 amendment 64636b1b0 (plan-only, skipped) |
| 1446 S5 | (by name) | 33a8a3542 9edd38b42 + 987775b82 (DataRetentionService.cs only) |
| 1430 S3 | b9230ddf8 / bceeeb21e | 8e7bcdc59 / e3a98632a |
| 1463 | 6559f7400 | 921f2c516 |
| 1461 | 2fc898ef5 4b4aa96a0 6dacaed6b | 1a110de19 da0665f64 c2631a580 |
| 1469 | (six commits) | 2336d1b45 d86f19b13 0305caf5c (report only, skipped) dacf4f3b6 e22361575 8fce73036 + 1c0ab7b83 (investigation) |
| 1472 | 47647ac2 / e8a44d43a | c1a0153a2 / 3c5d9456e |
| 1479 | 354a5a6e8 (+ test file) | 9f7874404 (+ test 96200c953) |
| 1475 | 22d057843 / bbae064cf | 5fdfb9df3 / 8822fa3f4 |
| 1409 | 9b39df4e0..078a37a79 (41) | 5070ab5f5^..6a2fe6ae3 (41; every pair has an equal patch-id) |

On rc, a subject grep finds no commit for any of these cards. CARD-1421 appears only in bundle A's
two adaptation subjects, and `GitProcessSpawnLimit` is absent from rc.

### Where the brief's reading was ambiguous

- **"27 commits incl. 1427/1428/1432".** On master, a grep for CARD-1432 returns 19 subjects. Add
  the 8 CARD-1427/1428 commits and you get 27. Those 19 include three commits that are not
  CARD-1432 work:
  - CARD-1446 S3 `407f245e6`, excluded because item (5) says NOT S3.
  - CARD-1462's plan `bb43ec352`, which is docs only.
  - CARD-1456 `f4ba26383`, **included**. S2 deletes the observation pin, which turns the RR_V4
    test red, and `f4ba26383` re-pins `RR_V4_RemoteReadFailure` to match (memory note
    rr-v4-remote-read-pins).

  The two CARD-1432 investigation docs (`4765fcb4f`, `121ba4813`) are left out because no test
  reads them. The 1432 plan docs are dropped.
- **Item 2.** `03686bed4` is **CARD-1384** "reuse the post-reacquire worktree list", from 10-21,
  and is not a CARD-1449 commit. It is not on rc and it is picked as briefed. CARD-1449's own
  compile fix `5c5897312` ("C688 inventory assertion passes Case.Sensitive", found by CP-1's build)
  is **added**. Without it, `6ee09ad07` is the commit its own author recorded as not compiling.
- **Item 7.** Dropping the CARD-1463 test-comment hunk was meant for an rc without `6ee09ad07`.
  Bundle B carries `6ee09ad07`, so the hunk would now apply cleanly. Dropping it is kept as
  briefed. The change is cosmetic and affects a comment only.
- **Items 10 and 11** pick the test before the fix (topological order). The brief lists the fix
  first.

## Per-pick result (verbatim run)

| Item | Result |
|---|---|
| (1) 1427/1428 | 7 clean; 87855ba43 and 77f34b7f3 skipped (plan only) |
| (1) 1432 + 1456 | 11 clean |
| (2) 1449 | 6ee09ad07 clean (dropped its plan), 5c5897312 clean; 03686bed4 **plan-conflict-dropped** (modify/delete on `2026-10-21-card-1384-real-land-profile-plan.md`, which is absent on rc) |
| (3) 1412, (4) 1452 | clean, clean (1452 plan dropped) |
| (5) 1446 S1+S2 | 7 clean |
| (5) 1446 S4a/S4b/S4d | 5 clean; 64636b1b0 skipped (plan only) |
| (5) 1446 S5 | 33a8a3542 clean (m3 plan dropped), 9edd38b42 clean; **987775b82 STOPs if picked whole**: `WorktreeResidueSweepService.cs` conflicts because its dedupe is written on top of S4c's `PublicationLanePolicy`, cooldown and Abandon code. Adaptations A1-A3 apply. |
| (6) 1430 | 2 clean |
| (7) 1463 | clean, then adaptation A4 (comment hunk dropped by amend) |
| (8) 1461 | 3 clean |
| (9) 1469 | 5 clean (both plan edits kept; `.antiphon/task-44b53fef.md` dropped); 0305caf5c skipped (report only); 1c0ab7b83 clean |
| (10) 1472 | 2 clean (plan dropped) |
| (11) 1479 | 2 clean, then adaptation A5 |
| (12) 1475 | 2 clean (plan dropped) |
| (13) 1409 | 35 clean, including 5070ab5f5, which adds the kept plan; 624d4466d restores rc's card-1400 plan; 6 report-only commits skipped |

### Adaptations (exact)

**A1, CARD-1446 S5 migration Designer regenerated against rc's model.** Master's
`20261022095749_AddWorktreeResidueCursorLastOutcome.Designer.cs` was generated on a master base
that already had the master-only migration `20261022015609_AddRuntimeActionConversationKey`. On
rc, its `BuildTargetModel` differs from the post-pick `AppDbContextModelSnapshot.BuildModel` in
exactly one place, `SessionRuntimeAction.ConversationKey`. Removing those three lines makes the two
bodies identical. The recipe checks that equality, so this is the Designer `dotnet ef` would
generate on rc. The migration itself (`Up`: add the nullable `WorktreeResidueCandidateCursors.LastOutcomeKey`
varchar(200)) and the snapshot hunk applied unchanged.

```diff
--- a/server/Migrations/20261022095749_AddWorktreeResidueCursorLastOutcome.Designer.cs
+++ b/server/Migrations/20261022095749_AddWorktreeResidueCursorLastOutcome.Designer.cs
@@ -8314,9 +8314,6 @@ namespace Antiphon.Server.Migrations
                     b.Property<int>("Attempts")
                         .HasColumnType("integer");
 
-                    b.Property<string>("ConversationKey")
-                        .HasColumnType("text");
-
                     b.Property<DateTime>("CreatedAt")
                         .HasColumnType("timestamp with time zone");
```

**A2, CARD-1446 S5-impl partial pick.** The recipe runs `git show 987775b82 --
server/Application/Services/DataRetentionService.cs | git apply --index`. This is the retention
pass `PruneWorktreeResidueHeldRowsAsync`: it prunes Held/Preview candidate rows older than
`Retention:WorktreeResidueHeldRowRetentionDays`, which defaults to 30. It also adds the sweep-result
field. Three parts of `987775b82` are left out:
- the `WorktreeResidueSweepService.cs` dedupe, which needs S4c;
- its `docs/ops-http.md` dedupe row;
- its `docs/orchestration-loop.md` paragraph.

**A3, the two S5 dedupe witnesses are dropped.**
`tests/Antiphon.Tests/Application/WorktreeResidueCandidateRowTests.cs` lines 34-120 are deleted,
which removes `C1446_unchanged_held_rows_are_not_rewritten` and
`C1446_changed_verdict_and_preview_write_rows`. Both assert the dedupe that A2 leaves out, so they
would be red on rc. `C1446_retention_prunes_only_old_held_rows` is kept. The recipe guards lines 35
and 122 by content before it deletes. The patch is:

```diff
--- a/tests/Antiphon.Tests/Application/WorktreeResidueCandidateRowTests.cs
+++ b/tests/Antiphon.Tests/Application/WorktreeResidueCandidateRowTests.cs
@@ -31,93 +31,6 @@ public sealed class WorktreeResidueCandidateRowTests
     private const int SettledTasks = 3;
     private const int CandidatesPerRun = SettledTasks + 1 + 1;
 
-    [Test]
-    public async Task C1446_unchanged_held_rows_are_not_rewritten()
-    {
-        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
-        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
-        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
-        await SeedAsync(db);
-        var logger = new CapturingLogger();
-        var service = Build(db, clock, logger);
-
-        await service.RunAsync(CancellationToken.None);
-        var first = await LatestRunAsync(db);
-        first.Candidates.ShouldBe(CandidatesPerRun, "first-run-candidates");
-        first.Held.ShouldBe(CandidatesPerRun, "first-run-held");
-        var firstRows = await RowsAsync(db, first.Id);
-        firstRows.Count.ShouldBe(CandidatesPerRun, "first-run-rows");
-        firstRows.ShouldAllBe(r => r.Outcome == nameof(WorktreeResidueCandidateOutcome.Held));
-        firstRows.Select(r => r.Lane).Distinct().OrderBy(l => l).ShouldBe(
-            new[] { nameof(WorktreeResidueLane.Publication), nameof(WorktreeResidueLane.SettledTask) });
-
-        clock.Advance(TimeSpan.FromDays(1));
-        await service.RunAsync(CancellationToken.None);
-        var second = await LatestRunAsync(db);
-        second.Id.ShouldNotBe(first.Id);
-        // The counters still count every evaluation...
-        second.Candidates.ShouldBe(CandidatesPerRun, "second-run-candidates");
-        second.Held.ShouldBe(CandidatesPerRun, "second-run-held");
-        // ...but no verdict changed, so no Held row is rewritten.
-        (await RowsAsync(db, second.Id)).Count.ShouldBe(0, "second-run-rows");
-        logger.Messages.ShouldContain(m => m.Contains($"candidates={CandidatesPerRun} rows=0 unchanged={CandidatesPerRun}"),
-            "unchanged-log-line");
-
-        // Every evaluated candidate has a cursor carrying its last verdict.
-        var cursors = await db.WorktreeResidueCandidateCursors.AsNoTracking().ToListAsync();
-        cursors.Count.ShouldBe(CandidatesPerRun, "cursor-per-candidate");
-        cursors.ShouldAllBe(c => c.LastOutcomeKey != null && c.LastOutcomeKey.Contains("|Held|"));
-        cursors.ShouldAllBe(c => c.LastEvaluatedAt == Now.AddDays(1));
-    }
-
-    [Test]
-    public async Task C1446_changed_verdict_and_preview_write_rows()
-    {
-        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
-        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
-        var clock = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
-        var (tasks, _) = await SeedAsync(db);
-        var service = Build(db, clock, new CapturingLogger());
-
-        await service.RunAsync(CancellationToken.None);
-        var first = await LatestRunAsync(db);
-        (await RowsAsync(db, first.Id)).Count.ShouldBe(CandidatesPerRun, "first-run-rows");
-
-        // The verdict of one task changes: release_required -> live_owner.
-        var changed = tasks[0];
-        changed.Status = AgentTaskStatus.Working;
-        await db.SaveChangesAsync();
-
-        // A preview writes every row (the operator asked to see them) and consumes no change.
-        clock.Advance(TimeSpan.FromHours(1));
-        var preview = await service.PreviewAsync(null, null, CancellationToken.None);
-        preview.Preview.ShouldBeTrue();
-        var previewRows = await RowsAsync(db, preview.Id);
-        previewRows.Count.ShouldBe(CandidatesPerRun, "preview-rows");
-        previewRows.Single(r => r.TaskId == changed.Id && r.Lane == nameof(WorktreeResidueLane.SettledTask))
-            .ReasonCode.ShouldBe("live_owner");
-
-        // The next daily run writes exactly the changed verdict.
-        clock.Advance(TimeSpan.FromDays(1));
-        await service.RunAsync(CancellationToken.None);
-        var second = await LatestRunAsync(db);
-        second.Preview.ShouldBeFalse();
-        second.Candidates.ShouldBe(CandidatesPerRun, "second-run-candidates");
-        var secondRows = await RowsAsync(db, second.Id);
-        var only = secondRows.ShouldHaveSingleItem("changed-verdict-row");
-        only.TaskId.ShouldBe(changed.Id);
-        only.Lane.ShouldBe(nameof(WorktreeResidueLane.SettledTask));
-        only.Outcome.ShouldBe(nameof(WorktreeResidueCandidateOutcome.Held));
-        only.ReasonCode.ShouldBe("live_owner");
-
-        // And the run after that writes nothing again.
-        clock.Advance(TimeSpan.FromDays(1));
-        await service.RunAsync(CancellationToken.None);
-        var third = await LatestRunAsync(db);
-        third.Candidates.ShouldBe(CandidatesPerRun, "third-run-candidates");
-        (await RowsAsync(db, third.Id)).Count.ShouldBe(0, "third-run-rows");
-    }
-
     [Test]
     public async Task C1446_retention_prunes_only_old_held_rows()
     {
```

**A4, CARD-1463 without its test-comment hunk.** After picking `921f2c516`, the recipe runs
`git checkout HEAD~1 -- tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs` and
amends the pick. The hunk below is reverse-applied:

```diff
--- a/tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs
+++ b/tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs
@@ -530,8 +530,9 @@ public sealed partial class AgentTaskLandPublicationTests
         // read-only git, and exactly this inventory: the admission index-lock probe (D-10), the one
         // git-directory read (D-2), then the post-window ticket validation in the source checkout
         // (index-lock probe, sequencer probe, tracked-tree status). Nothing mutates it and nothing
-        // else inspects it. The source status still runs without --no-optional-locks; that is a
-        // separate card, not a reason to widen this list.
+        // else inspects it. CARD-1463: those three source probes are observations, never refusals
+        // (the tree is cleanup residue, D-2/D-12), and every LandingGit child already runs with
+        // GIT_OPTIONAL_LOCKS=0, so the status argv stays as listed; this list is not widened.
         await using var h = new LandingSafetyHarness();
         await h.InitializeAsync();
         await h.AddSourceAsync();
```

**A5, CARD-1479 test lane.** rc lacks CARD-1421 `cc4a5564d` (`GitProcessSpawnLimit`). This is the
same swap bundle A made twice:

```diff
--- a/tests/Antiphon.Tests/Application/RunnerSyncLeaseFairnessTests.cs
+++ b/tests/Antiphon.Tests/Application/RunnerSyncLeaseFairnessTests.cs
@@ -26,7 +26,7 @@ namespace Antiphon.Tests.Application;
 /// clock is fake, so slices and budgets are advanced, never slept.
 /// </summary>
 [Category("Integration")]
-[ParallelLimiter<GitProcessSpawnLimit>]
+[ParallelLimiter<ProcessSpawnLimit>]
 public sealed class RunnerSyncLeaseFairnessTests
 {
     /// <summary>
```

The CARD-1479 test also needs CARD-1427 `020276b3b` (`DecideYield`, `LandYieldOutcome`). Item (1)
carries it, and the build confirms that it resolves.

## Build (bundle tree aa7963bbd, tip e04df04cd)

| Build | Command (through `scripts/build-slot.ps1`) | Result |
|---|---|---|
| B-1 | `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-rcbundleb/` (builds server) | **Build succeeded, 0 errors**, 1119 warnings, 2m04s; lease 5609ae28 waited 0s |
| B-2 | `dotnet build tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj --property:OutputPath=bin-rcbundleb/` | **Build succeeded, 0 errors**, 43 warnings; lease 656f35ae waited 60s |

The bundle changes nothing under `src/`. B-2 was run because the brief asked for it.

Ten warning lines are attributed to bundle commits by `git blame` against rc. All of them are
carried from master as-is:

- `LandRefRetention.cs:31` CS8604 (CARD-1432 S2);
- `LandNonCodeSkipTests.cs:1431` CS0219 (CARD-1409);
- TUnit0015 in five `BlockedReplyReportRetentionTests` and three `DispatcherLeaseSpanTests`
  methods.

All 29 `bin-rcbundleb` directories were removed, along with the scratch worktrees.

**Not built:** `client/` (CARD-1475 changes `SmartComposer.tsx` and its Vitest file). No Vitest
or type-check was run.

### Bundle test classes that compile on rc

Both builds succeeded, so every test file the bundle adds or modifies compiles. For each new
class, a string search of the built `Antiphon.Tests.dll` confirms the class is there.

- **New:**
  - AbandonedLandingReconcilerTests, SettlementSyncDebtAttentionTests,
    FileSystemEvidenceRetentionTests and WorktreeResidueCandidateRowTests, which keeps only
    `C1446_retention_prunes_only_old_held_rows` (1446).
  - LandRefRetentionTests, ServerGitProcessTests (1432).
  - LeaseTurnScenarioTests, DispatcherSiblingInspectionSchedulingTests.LeaseRetention,
    TestHelpers/FakeRepositoryLease (1427); SiblingInspectionResultsTests (1428).
  - DispatcherLeaseSpanTests and TestHelpers/LeaseSpanRecorder (1430);
    BlockedReplyReportRetentionTests (1472); RunnerSyncLeaseFairnessTests (1479);
    DeliveryUnverifiedDiagnosticTests (1475).
- **Modified:**
  - AgentTaskLandPublicationTests and LandingProtocolHarnessTests (1449);
    AgentTaskLandRefusedRetryTests (1384, 1456).
  - CardRelationshipSchemaTests (1452); LandDeliveryWaitRoutingTests (1412);
    LandVerificationBuildSlotTests (1461); SessionMessageQueueDeliveryVerificationTests (1475).
  - StandingPipelinePolicyDocumentationTests and CheckpointRepeatDocumentationTests (1469).
  - LandNonCodeSkipTests (1409); RepositoryLeaseWaitersTests, DispatcherSiblingInspectionSchedulingTests,
    LandingGitProfileTests, LandingGitTests and ControlledLandingGit (1427/1428/1432).
  - SettlementSyncRecoveryTests, RunnerSettlementWorld and WorktreeIgnoredContentClassifierTests
    (1446).

The orchestrator bundle pin checks out. `server/Bundles/orchestrator.md` on the bundle hashes to
`ded83f88...a556a`, the SHA-256 that CARD-1469's `CheckpointRepeatDocumentationTests` pins
(11,501 bytes). No test suite was run.

### Docs that bundle tests read

The bundle adds four test-read `docs/**/*.md` literals that do not resolve: `docs/build.md`,
`docs/guide.md`, `docs/style.md` and `docs/sub/s.md`. All four are LandNonCodeSkipTests fixture
paths, not repository files. A test reads each of these docs, and all of them are present:

- the CARD-1409 plan;
- the CARD-1469 plan;
- `docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md`.

## Evidence diff

`pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef 6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45 -HeadRef HEAD`
exits 0 with
`EVIDENCE result commits=94 entries=0 violations=0 base=6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45 head=e04df04cd16c41d6ffcc2ff2c5a78bbb65c1a68e`.

## EF migrations

The bundle adds exactly one migration, `20261022095749_AddWorktreeResidueCursorLastOutcome`
(CARD-1446 S5, a nullable column). It sorts after rc's latest, `20261020142734_AddStandingAutomaticFreshRecovery`,
and no other bundle commit touches `server/Migrations/`. The Designer is regenerated for rc (A1).
Only `StandingRestartEscalationTests.Boundaries` addresses a migration by name: it migrates to the
predecessor of AddStandingAutomaticFreshRecovery, then to latest, so it does not pin the newest
migration. CARD-1452's C100 re-pin, which is in the bundle, removes the one test that did.

Master has three migrations around this one that rc does **not** get:
`20261022015609_AddRuntimeActionConversationKey`, `20261022074212_AddProviderEndpoints` and
`20261022110953_AddPoolProviderProjection`. A later rc port of the first two would carry IDs older
than an applied `095749`. EF applies pending migrations by ID, so they would still apply, but each
ported Designer would again need regenerating against rc's model. The migration ID matches
master's, so an rc database that later runs master code has `095749` recorded already.

## Activation footprint

The bundle changes the server and the built client:

- `server/` (Program.cs registrations: `AbandonedLandingReconciler`, `FileSystemEvidenceRetention`);
- the migration;
- appsettings: the `**/.antiphon/**` evidence glob and `WorktreeResidueHeldRowRetentionDays: 30`;
- `client/src` (1475);
- `scripts/audit-land-verification-graph.ps1` (1409).

The docs it touches are AGENTS.md, orchestration-loop.md, session-runtime-invariants.md and
testing-and-build.md, plus `.claude/skills/antiphon-{delegate,orchestrator}/SKILL.md` and
`server/Bundles/orchestrator.md` (1469). Nothing changes under `src/`, so **no session-runner
restart is needed**. Totals: 94 files, +21165/-479.

## Decisions for the orchestrator (defaults applied in this recipe)

1. **CARD-1446 S5 is ported without dedupe.** `LastOutcomeKey` is added but unused on rc. Only the
   retention pass is live, and the two dedupe witnesses are dropped (A2/A3). The other way is to
   port S4c (`1032b91a3` + `78338e9cd`) and take S5 whole, which the brief excluded.
2. **CARD-1446 S4d docs (`7292b36db`) describe S4c**, and S4c is not on rc. The docs describe the
   Publication-lane cooldown (`WorktreeResidue:CleanupRetryCooldownDays`), `MaxCleanupRetries` and
   `Cleanup=Abandoned`. rc has none of these. The text was picked as briefed, so rc's
   orchestration-loop.md overstates the feature. The fix is a docs trim or porting S4c.
3. CARD-1384 `03686bed4` is picked because the brief lists it. It is not CARD-1449 work. CARD-1449's
   compile fix `5c5897312` was added.
4. CARD-1456 `f4ba26383` is included with the CARD-1432 chain because S2 changes what RR_V4 sees.

## Pick recipe (mechanical)

Run it from any repository clone with `origin` fetched:

```bash
git fetch origin rc/2026-10-20 master
git worktree add --detach ../rc-bundle-b origin/rc/2026-10-20   # must be 6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45
cd ../rc-bundle-b
bash /path/to/rc-bundle-b-recipe.sh            # script below, saved verbatim; it checks the start SHA
test "$(git rev-parse HEAD^{tree})" = aa7963bbd977b5949fa2d40d07fd5e292db0f41a   # proven tree
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -Repository . -BaseRef 6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45 -HeadRef HEAD
# then build/test as the rc gate requires, and publish the bundle to rc per docs/release-gates.md
```

The expected output has these lines and no others:

- 89 `clean` lines;
- 1 `plan-conflict-dropped` line (`03686bed4`);
- 10 `skip` lines;
- 5 `adapt` lines;
- `bundle tip <sha> tree aa7963bbd977b5949fa2d40d07fd5e292db0f41a`.

Any `STOP` line means that rc or a pick has drifted from this dry-run. In that case, run
`git cherry-pick --abort` if a pick is in progress and report the stop. Do not resolve it by hand.

`rc-bundle-b-recipe.sh`:

```bash
#!/usr/bin/env bash
# rc bundle B recipe onto origin/rc/2026-10-20 6e79f3e13 (bundle A applied). Run inside a worktree
# checked out at origin/rc/2026-10-20. Every SHA is the origin/master copy. Stops on any unexpected conflict.
set -euo pipefail
PLANS=docs/superpowers/plans
test "$(git rev-parse HEAD)" = 6e79f3e13d66f0bf0dfad7de9a8a60db27bb2d45 || { echo "STOP   HEAD is not rc 6e79f3e13"; exit 1; }

# undo <base> <path>...: take back a pick's change to each path - restore the rc copy when rc has
# the path (an rc plan doc the pick modified), otherwise remove it.
undo() {
  local base=$1 p; shift
  for p in "$@"; do
    if git cat-file -e "$base:$p" 2>/dev/null; then git checkout -q "$base" -- "$p"; else git rm -q -f -- "$p"; fi
  done
}

# pick <sha> [keep=<plan path>]: cherry-pick -x; undo the plan docs this commit touches (except the
# kept one) and every .antiphon/task-*.md report. A commit that touches nothing else is skipped.
pick() {
  local s=$1 keep=${2:-} plans reports drop rest base bad
  keep=${keep#keep=}
  plans=$(git show --format= --name-only "$s" -- "$PLANS" | grep -v -x -F "${keep:-//none}" || true)
  reports=$(git show --format= --name-only "$s" -- '.antiphon/task-*.md')
  drop=$(echo $plans $reports)
  rest=$(git show --format= --name-only "$s" | grep -v -x -F -f <(printf '%s\n' $drop) || true)
  if [ -z "$rest" ]; then echo "skip   $s (only: $drop)"; return; fi
  base=$(git rev-parse HEAD)
  if git cherry-pick -x "$s" >/dev/null 2>&1; then
    if [ -n "$drop" ]; then undo "$base" $drop; git commit -q --amend --no-edit; fi
    echo "clean  $s${drop:+ (dropped: $drop)}"
  else
    bad=$(git diff --name-only --diff-filter=U | grep -v -x -F -f <(printf '%s\n' $drop) || true)
    if [ -n "$bad" ]; then echo "STOP   $s conflict outside dropped paths: $bad"; exit 1; fi
    undo "$base" $drop
    git -c core.editor=true cherry-pick --continue >/dev/null
    echo "plan-conflict-dropped $s (dropped: $drop)"
  fi
}

# expect <file> <line> <text>: guard a line-numbered adaptation.
expect() { [ "$(sed -n "$2p" "$1")" = "$3" ] || { echo "STOP   $1:$2 is not: $3"; exit 1; }; }
done_adapt() { git commit -q -m "$1" -- "${@:2}"; echo "adapt  $(git log -1 --format=%h) ${*:2}"; }

# (1) CARD-1427/1428 (plan 87855ba43 and checkpoint table 77f34b7f3 are plan-only: skipped)
for s in 87855ba43 020276b3b 0fcc1c1b2 0984d8e7d c00e3d1da e1aacb4cd 77f34b7f3 927a0b78c be14b8b0d; do pick $s; done
#     CARD-1432 S1/S2/S4 (+ CARD-1456 f4ba26383, the RR_V4 re-pin S2 makes necessary)
for s in 9f50222f2 74deabb48 c579d09ce 6cd9317a0 c336a9887 a056d9164 88a1080d1 5a016184f ee62ec013 705026da8 f4ba26383; do pick $s; done
# (2) CARD-1449 6ee09ad07 + its compile fix 5c5897312, then CARD-1384 03686bed4 (as briefed)
pick 6ee09ad07; pick 5c5897312; pick 03686bed4
# (3) CARD-1412  (4) CARD-1452
pick 29dc7dc1e; pick 4e4f04726
# (5) CARD-1446 M1 S1+S2 (+ aa4fa734f = 824621e96); not S3 407f245e6
for s in e5df91864 32bfcbc9e c3fe1fc26 1d4062563 b80ae90e3 78297d864 824621e96; do pick $s; done
#     M2 S4a, S4b, S4d (+ m2 checkpoint amendment 64636b1b0, plan-only: skipped); not S4c 1032b91a3/78338e9cd
for s in b41307495 3d2c8cb8a 64636b1b0 045dca309 d6064590a 7292b36db; do pick $s; done
#     S5 column + migration + retention only
pick 33a8a3542; pick 9edd38b42
D=server/Migrations/20261022095749_AddWorktreeResidueCursorLastOutcome.Designer.cs
expect $D 8317 '                    b.Property<string>("ConversationKey")'
expect $D 8318 '                        .HasColumnType("text");'
sed -i '8317,8319d' $D
diff <(sed -n '/protected override void BuildTargetModel/,$p' $D | tail -n +2) \
     <(sed -n '/protected override void BuildModel/,$p' server/Migrations/AppDbContextModelSnapshot.cs | tail -n +2) >/dev/null \
  || { echo "STOP   Designer body differs from rc's model snapshot"; exit 1; }
done_adapt "fix(CARD-1446): rc port - regenerate the S5 migration Designer against rc's model (drop master-only SessionRuntimeAction.ConversationKey)" $D
git show 987775b82 -- server/Application/Services/DataRetentionService.cs | git apply --index
git commit -q -m "feat(CARD-1446): rc port - S5-impl retention pass only (DataRetentionService); dedupe needs S4c, not ported

(partial cherry pick of commit 987775b82: server/Application/Services/DataRetentionService.cs only)"
echo "adapt  $(git log -1 --format=%h) 987775b82 DataRetentionService.cs only"
T=tests/Antiphon.Tests/Application/WorktreeResidueCandidateRowTests.cs
expect $T 35 '    public async Task C1446_unchanged_held_rows_are_not_rewritten()'
expect $T 122 '    public async Task C1446_retention_prunes_only_old_held_rows()'
sed -i '34,120d' $T
done_adapt "test(CARD-1446): rc port - drop the two S5 dedupe witnesses (dedupe needs S4c, not ported); keep the retention witness" $T
# (6) CARD-1430 S3
pick 8e7bcdc59; pick e3a98632a
# (7) CARD-1463 minus its AgentTaskLandPublicationTests.cs comment hunk
pick 921f2c516
git checkout -q HEAD~1 -- tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs
git commit -q --amend -m "$(git log -1 --format=%B)

rc port: AgentTaskLandPublicationTests.cs comment hunk dropped (brief item 7)."
echo "adapt  $(git log -1 --format=%h) 921f2c516 minus AgentTaskLandPublicationTests.cs"
# (8) CARD-1461 option C (default off)
pick 1a110de19; pick da0665f64; pick c2631a580
# (9) CARD-1469 incl. its plan doc (rule-6 test reads it) and the investigation the test reads; report 0305caf5c skipped
P1469=$PLANS/2026-10-22-card-1469-standing-operating-rules-plan.md
pick 2336d1b45 keep=$P1469; pick d86f19b13; pick 0305caf5c; pick dacf4f3b6; pick e22361575 keep=$P1469; pick 8fce73036; pick 1c0ab7b83
# (10) CARD-1472 (test file from 3c5d9456e, its plan dropped; fix c1a0153a2)
pick 3c5d9456e; pick c1a0153a2
# (11) CARD-1479 test 96200c953 + fix 9f7874404; rc lacks CARD-1421 GitProcessSpawnLimit
pick 96200c953; pick 9f7874404
T=tests/Antiphon.Tests/Application/RunnerSyncLeaseFairnessTests.cs
sed -i 's/\[ParallelLimiter<GitProcessSpawnLimit>\]/[ParallelLimiter<ProcessSpawnLimit>]/' $T
done_adapt "test(CARD-1479): rc port - ParallelLimiter<ProcessSpawnLimit>; rc lacks CARD-1421 GitProcessSpawnLimit" $T
# (12) CARD-1475
pick 5fdfb9df3; pick 8822fa3f4
# (13) CARD-1409 as landed on master 5070ab5f5..6a2fe6ae3 (41); its plan is kept (C1409_NormaliserAndItemOperationTablesHaveWitnesses reads it)
P1409=$PLANS/2026-10-22-card-1409-import-fail-closed-plan.md
for s in $(git rev-list --reverse 5070ab5f5^..6a2fe6ae3); do pick "$(git rev-parse --short=9 "$s")" keep=$P1409; done

if git grep -n 'GitProcessSpawnLimit' -- '*.cs'; then echo "STOP   GitProcessSpawnLimit still referenced"; exit 1; fi
echo "bundle tip $(git rev-parse HEAD) tree $(git rev-parse HEAD^{tree})"
```

## Remaining uncertainties

- Only compilation was checked. No test, Vitest or client build was run on the bundle, by brief.
  Each card's Review-clean status was established on master bases, not on rc.
- CARD-1409 was re-verified as a clean cherry-pick onto the new rc tip, except for plan docs. Its
  behaviour on rc is untested.
- `6ee09ad07`'s C688/C475 expectations were written on a master that had CARD-1384. With
  `03686bed4` picked, rc now matches that base, but this was not run.
- CARD-1446 S1's daily job registration (`AbandonedLandingReconciler`) refuses abandoned
  unpublished landings older than seven days. This takes effect at the first activation on rc
  hosts.

## Not done, noted

- If decision 2 is kept, trim the S4c sentences from `7292b36db`'s orchestration-loop paragraph
  as a follow-up docs task. Otherwise, port S4c and pick S5 whole.
