# Review bae2a09e — CARD-1807 round 1 (Final, Full)

Subject: Code 5495e6d5, `origin/feat/card-task-5495e6d5`. Red `64f6c48a2635665645d05740cb0965420121e1bb`, fix/tested
`1fcd61c51a9a6698c27e2e06e998cb4aaf363723`, report `628f419f56159e39fe95ffe26a87a322ffb7cd0a` (adds only `.antiphon/task-5495e6d5.md`).
Base master `c6c4c20d902ca187e67ea1d1c6e72fe81af66ae2`; judged against current origin/master `69d23440974ab6f9570c21005faa54d615623a26`.
Landing owner: Code task 5495e6d5. Runner: server2 mirror (Linux). Every build/test went through `scripts/build-slot.ps1`.

**Verdict: CLEAN — no blockers.** Two advisories, A-1 and A-2, for cards. Neither holds the land.

## Checks

1. **Rule = CARD-1747 contract; lanes unchanged.** The diff to the seven Checkpoints classes is exactly one added `[SharedCheckoutChild]` line each.
   No limiter line changed. The seven marked classes are exactly master's `SharedCheckoutCheckpointClasses`, and master's
   `StrictClasses` Checkpoints entries are the same seven. `git grep` finds exactly seven `.cs` files under `tests/Antiphon.Tests/Checkpoints/` with
   `ParallelLimiter<ProcessSpawnLimit>` (the rest are `.txt` fixtures), and all seven carry the marker. No class is double-limited:
   `No_class_carries_both_limiters` is green, and the remaining 66 files with no limiter are unchanged from master.
2. **Scratch-class probes.** I built `ProcessSpawnLimitTests` with an uncommitted probe file and restored the tree afterwards.
   - The census named each of these:
     - an unmarked strict class;
     - a marker on a tool class (two lines);
     - a class derived from a marked base (it inherits the limiter, but the marker is `Inherited=false`);
     - an abstract class;
     - a generic class (`` `1 ``);
     - a partial class (named once);
     - a method-level strict limiter;
     - a marker on a nested class;
     - a marker in `Checkpoints.Sub`;
     - a marked class whose limiter I removed.
   - It named none of these silently skipped cases: a nested unmarked strict class, an unmarked strict class in `Checkpoints.Sub`,
     and an unlimited class. See A-2. All three are identical on master: the same `Namespace == "Antiphon.Tests.Checkpoints" && !IsNested`
     filter applies, and the CARD-1747 contract covers limited classes only.
   - Remove-both mutant: I deleted both `[ParallelLimiter<ProcessSpawnLimit>]` and `[SharedCheckoutChild]` from
     `RerunFilterHostTests`. The run stayed green: `ProcessSpawnLimitTests` + classification, 85/85. See A-1.
3. **StrictClasses retained.** `Process_spawning_classes_carry_the_limiter`, `Process_spawn_guard_...`, and `No_class_carries_both_limiters`
   still iterate the non-Checkpoints roster. The new anti-regression check
   (`StrictClasses.Where(Namespace == Checkpoints).ShouldBeEmpty`) has an outcome assertion. `git grep` shows no other consumer of either
   roster name outside historical docs.
4. **Witnesses are real.** The real census and the in-memory witnesses call the same `CheckpointLimiterMisses(types, out limited)`.
   `FakeClass` asserts that each emitted type's limiter attribute is visible to `HasSpawnLimiter`, so a fake cannot pass vacuously.
   For an independent rerun at `64f6c48a2`, I checked out the red `ProcessSpawnLimitTests.cs` and the seven class files and ran
   `ProcessSpawnLimitTests`. Result: 43 run, 5 failed, exactly the five witnesses, each on its own Shouldly assertion. The tool-class
   control passed. My probes (item 2) also act as mutants that kill each rule.
5. **impacted-classes row.** I ran `scripts/impacted-classes.ps1 -Range` over dangling scratch commits:
   - a new `tests/Antiphon.Tests/Application` file with `[ParallelLimiter<ProcessSpawnLimit>]` → `spawn-limit-rosters`;
   - the same file without the attribute → none;
   - a `Directory.Build.targets` edit → `spawn-limit-rosters`;
   - a `docs/bootstrap.md` edit → none.

   `-Path` gave `spawn-limit-rosters` for `TestHelpers/ProcessSpawnLimit.cs`, `Checkpoints/Sub/X.cs`, and `Directory.Build.targets`. The three widened range
   pins are honest. `c6eee1a0` adds `+[Category("Integration"), Category("Slow"), ParallelLimiter<ProcessSpawnLimit>]`. `ceda26b3`
   edits `Checkpoints/CheckpointTerminalReceiptTests.cs`. The batch span adds four `ParallelLimiter<` lines. The selector returns
   exactly the asserted kinds for all three. `-Audit` ok, with no empty globs and no missing classes. `-MergeSubset -Range c6c4c20d9..1fcd61c51` adds
   `ProcessSpawnLimitTests` to the first fixed row. `MergeHighRadiusSubsetTests` (6/6) imports the documented Merge rows with
   0 warnings. The branch does not touch `docs/orchestration-loop.md`.
6. **Docs.** The `docs/testing-and-build.md` CARD-1747 paragraph is accurate, and the probes confirm each refusal it lists.
   Nit: the row description omits the `Directory.Build.targets` glob.

## Runs (reviewed tree = 628f419f5, source identical to 1fcd61c51 outside `.antiphon/`)

| Run | Filter | Result |
|---|---|---|
| touched (build-slot, clean tip build) | ProcessSpawnLimit*, ImpactedClassesSelector*, MergeHighRadiusSubset*, 7 marked classes | 113/113 passed, 0 skipped, 11m23s. TRX: 43+25+6+16+10+5+3+2+2+1 |
| guards (same build) | TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests*, MechanicalPcContractTests*, TaskPlatformGuidanceTests*, C1729LayeringGuardTests* | 77/77 passed, 2m19s (41+1+5+2+3+11+5+9) |
| probes (scratch, restored) | ProcessSpawnLimitTests | 2 failed / 43 as expected; misses listed in item 2 |
| remove-both mutant (scratch, restored) | ProcessSpawnLimit* + TestClassification* | 85/85 green (A-1) |
| red re-run (64f6c48a2 test sources) | ProcessSpawnLimitTests | 5 failed / 43, the five witnesses |
| checkpoint-tool receipt | `.antiphon/review-bae2a09e-plan.md` CP-1 (74) + CP-2 (77) | run 20261109-090950-6e9d: CP-1 executed=74 passed=74, CP-2 executed=77 passed=77, 0 failed/skipped, sourceState=clean buildSource=verified; `validate --expected-source-sha 628f419f5…` → `CHECKPOINT SOURCE VALID source=628f419f56159e39fe95ffe26a87a322ffb7cd0a rows=2` |

- `check-evidence-diff.ps1 -Repository . -BaseRef c6c4c20d9 -HeadRef 628f419f5`: commits=3, violations=0, exit 0.
- `git merge-tree --write-tree origin/master` (69d234409): clean for both 1fcd61c51 and 628f419f5. Master since base touches
  none of these files and adds no `ParallelLimiter<` line.
- Migration selector: not applicable (no migration in the diff).
- PCs: none in a plan (no plan exists). The pending SourceLanding Mutation still owns any of them.
- Residue: `PhaseJournalKillDiagnosticTests` left the known ignored `tests/Antiphon.Tests/bin-c585h-c1748-{decoy,kill}-*` dirs
  (pre-existing, as recorded by Review 94181272). I left them, because a variable or glob `rm` is not allowed here.

## Advisories (cards; do not hold the land)

- **A-1 — Removing both attributes from a shared-checkout class is no longer caught.**
  - Where: `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`. `SharedCheckoutCheckpointClasses()` is derived, and its only floor is `ShouldNotBeEmpty`.
  - Failure: `RerunFilterHostTests` with both `[ParallelLimiter<ProcessSpawnLimit>]` and `[SharedCheckoutChild]` deleted is green (85/85).
    On master, `Process_spawning_classes_carry_the_limiter` names it, through the `typeof(RerunFilterHostTests)` entry in `StrictClasses`.
    So a class whose child uses the shared checkout can silently become unlimited. That is easiest to do when someone answers the census
    message by deleting the limiter.
  - Why advisory: the card asked for exactly this roster removal, and no class is unlimited today.
  - Fix options for a card: (a) make the floor the exact current count, which brings back a partial roster; or (b) better, a
    source-level witness that a class passing `CheckpointFixtures.RepoRoot` as a child `WorkingDirectory` carries the marker. Code
    declined (b) as fragile; a Roslyn-based check would not be fragile.
- **A-2 — Nested and sub-namespace Checkpoints classes are outside the census.**
  - Pre-existing: master uses the same filter.
  - Failure: a nested class, or a `Antiphon.Tests.Checkpoints.Sub` class, carrying `[ParallelLimiter<ProcessSpawnLimit>]` without
    the marker is not named. The `spawn-limit-rosters` glob `Checkpoints/**` still selects `ProcessSpawnLimitTests` for such a file.
    No such class exists today: every `.cs` under `Checkpoints/` declares `namespace Antiphon.Tests.Checkpoints`.
  - Fix: match `Namespace == X || Namespace.StartsWith(X + ".")`, and apply the rule to nested types too (or refuse limited nested types).
