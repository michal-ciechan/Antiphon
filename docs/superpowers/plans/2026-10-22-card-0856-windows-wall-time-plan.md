# CARD-0856 plan: cut the Antiphon.Tests Windows rc wall time

Card: CARD-0856 (board Antiphon). Evidence: [time profile](../../investigations/2026-10-21-antiphon-tests-time-profile.md)
(Q1-Q3, T1-T4), the CARD-1419/1420/1421 plans beside this file, and the rc/2026-10-20 Windows
qualification that is running while this is written (candidate `rc-2026-10-20-ceb6e3d6b784`, run
`2026-10-22-0157-27a1b370`: 12 of about 84 launches after 4 h).
Base: `294fa3d1a81ff97abb285af3180d93ee4d4b25f6` (master-based task branch). rc tip read:
`ceb6e3d6b` (rc/2026-10-20). master tip read: `1961f0ab6`.
Lane: server2 (`GET /api/runner-defaults`: `globalRunnerId=server2`, Linux) for every row that does not
need Windows. Rows marked **desktop** need the Windows host (`-Runner desktop`) and are the only pins.
Written on the Linux runner mirror: the qualification logs under `C:\Antiphon\releases\...` and the
monitor's `eta.md` were not reachable from here. Every number that depends on them is marked **owed to
the Windows lane** and the first slice reads them.

## State of play (what the brief assumes vs what is landed)

The brief lists Q1, Q2, Q3, then T1, T2 as the work. Three of those are already done, and two of
the three are already on rc.

| Item | Card | master | rc/2026-10-20 | What is left |
|---|---|---|---|---|
| Q1 suite-once census (30 s of each ~44 s gap) | CARD-1419 | landed `95b164870` | cherry-picked `038d94a57` | nothing; the running qualification already has it |
| Q2 fewer launches: node-weight cap, then measured-seconds packing (target 15 min serial, 60 min watchdog) | CARD-1404, CARD-1410 | landed `5447617c5`, `1ea7cf965` | cherry-picked `66e9cfa9f`, `b456fc7c0` | the cost table has 6 measured rows out of 1,355 classes; everything else is a node model (D-3, S3) |
| Q3 cached schema snapshot instead of 212 migrations per DB process | CARD-1420 | landed, closed `0d8557fb`, "master-only" | **not on rc** | cherry-pick (S1) |
| T1 git-only spawners on a 2-4 wide `GitProcessSpawnLimit` lane (104 classes) | CARD-1421 | landed, closed `1ed60305` | **not on rc** | cherry-pick after a Windows concurrency row (S2) |
| T2 Land* matrix fixtures | CARD-1437 (F-1, Backlog) | not started | not started | the one measured 32 min class (S5); the fixture-copy idea is dropped (D-6) |
| T3 bare `[NotInParallel]` global-sweep suites | none | not started | not started | S6 |
| T4 cold-start outliers, residual ~14 s harness gap | none | not started | not started | S7 (measure first) |

## Ground truth

| Card / brief assumes | Code and branches do | Consequence |
|---|---|---|
| Each launch pays ~44 s of harness PowerShell | CARD-1419 is on rc (`038d94a57`): the 30 s census is gone from the gap. The remaining ~14 s (`Get-NightlySafeChildEnvironment`, log stamp rewrite, `Test-NightlyFreshEvidence`, owned-process start; `scripts/lib/nightly-tests-impl.ps1:990-1080`) is unattributed. | Q1 is not a slice. S7 measures the residue before anyone fixes it. |
| ~225 launches at cap 120 | CARD-1410 packs by estimated serial seconds: `NightlyLaunchTargetSeconds = 900`, `NightlyLaunchLoneClassBudgetSeconds = 2400`, `NightlyLaunchMaxGroupOperands = 30` (`nightly-tests-impl.ps1:137-139`), first fit decreasing per namespace, oversized classes split into exact-method chunks. The real listing gives about 84 launches. | Q2 is done as a mechanism. Its inputs are the problem (next row). |
| Launch groups are sized from measurements | `scripts/lib/nightly-class-costs.json` has **6 rows**: 4 `measured` (groups 19, 25, 26, 27 of run `ebd2ff29`) and 2 `pace` (RemoteScriptContractTests 4,591 s, DelegationDispatchRecoveryBoundaryTests 3,505 s, both 9.2 s/node guesses). Every other class uses the node model: 0.5 s, Slow 1.5 s, Slow in a Land/Delivery type 4 s, Land/Delivery/Check names doubled (`:133-136`). The sample measured Slow at a 6.83 s mean and not-Slow at 0.86 s. | The model under-estimates Slow classes by 2-4x, so "15 minute" groups run longer and the first groups of each namespace (largest first) are the longest. 12 launches in 4 h is consistent with that and says nothing about the remaining 72. **Owed to the Windows lane:** the per-group wall table of run `27a1b370`. |
| `MigrateAsync` costs ~22.8 s per DB launch | rc still migrates. master restores a `pg_dump` snapshot keyed by the migrations assembly bytes (`TestDbFixtureLifecycle.cs`, `TestSchemaCache.cs`; `docs/testing-and-build.md:814`). The CARD-1420 Code report measured 41-65 s saved per DB launch on server2. A failed restore drops and migrates, a failed capture is ignored, the probe children run with the cache `off`. | S1 cherry-picks it. `git merge-tree --merge-base 52624fcaf^ origin/rc/2026-10-20 0d8557fba` conflicts only on the plan document, which rc does not carry. |
| One 1-wide `ProcessSpawnLimit` lane | master has two lanes (`GitProcessSpawnLimit`, `Clamp(ProcessorCount / 2, 2, 4)`, override `ANTIPHON_TEST_GIT_SPAWN_LIMIT` 1-8). rc has one. CARD-1421 D-7 verified on Linux only; the Windows file-lock risk was left to "the next Windows rc run". | S2 cherry-picks after one Windows concurrency row. `git merge-tree --merge-base d4414c0bf^ origin/rc/2026-10-20 1ed603052` conflicts only on the plan document. |
| `AgentTaskLandActiveSourceClaimTests` is a fixture problem (3 harnesses × 11 git processes per row) | 16 rows, measured 1,920 s (cost table) on the strict lane, so strictly serial. Per row: 3 `LandingSafetyHarness` (each 11 git processes and one DB clone) plus 2-3 real lands of 30-60 git commands each. 11 git processes are 2-4 s on Windows; the lands are the cost. The class is strict only because of `cmd.exe /c mklink /J` for the 8 alias rows (`:73`, `MklinkJunctionAsync`). | Copying a template repository saves seconds per row (D-6, rejected). Making the class git-only and moving it to the git lane turns 32 serial minutes into about 8-10 wall minutes (S5 = CARD-1437). |
| `[NotInParallel]` keys serialise only their group | Bare `[NotInParallel]` (no key) runs the test alone in the process: nothing else runs. 193 files in `Antiphon.Tests` carry it (2,579 nodes, about 0.9 h serial), among them `AgentTaskReplyIntegrationTests` (141 tests, also strict lane), `AttentionServiceTests` (130), `AgentTaskDeliveryWatchdogTests` (74, measured 227 s at parallelism 1.0). The testing guide (`docs/testing-and-build.md:55`) requires the bare attribute for a global sweep over the shared `antiphon_test`. | A sweep over the class's **own isolated clone** needs no bare attribute. S6 converts the two largest and leaves a template for the rest. |
| The harness can tell lanes apart | Discovery (`--list-tests`) carries class, method, categories. Limiter and `NotInParallel` attributes are invisible to it. Seven of the 27 sampled launches ran at parallelism 1.0-1.1 and held 65% of the summed time; `AgentTaskLandAdmissionControlledTests` (no limiter) ran 24 tests at ~13x in the same process as serial classes. | A lane census is needed before the packer can mix serial and parallel classes (S4). |
| rc fixes shorten the running qualification | A new rc tip is a new candidate; the running qualification stays on `ceb6e3d6b` until it finishes or the operator stops it. | Nothing in this plan shortens run `27a1b370`. S1-S3 shorten the next candidate (D-1). |

## What must not change

- **Test isolation.** Every class keeps its own temp root, `HOME`, repository and DB clone. No slice shares a repository or a database between tests that did not already share one.
- **DB-per-launch safety.** One Testcontainers Postgres per process, owned by the process, torn down with it. The schema cache (S1) only changes how `antiphon_test` reaches its schema; it never reads a configured connection string. `CreateIsolatedSchemaAsync` keeps its one-wide clone lock.
- **The ProcessSpawnLimit contract.** Classify by what is spawned, never by name. Strict lane for anything that is not git. A class moves to the git lane only when it starts nothing but git (S5 makes that true before moving). Both limiters stay assembly-local. Roster tests stay floors.
- **What the roster runs.** No test, argument row or class is removed, skipped or moved out of the rc profile. S5 and S6 change setup code and scheduling attributes, not the assertions or the matrix.
- **The rc profile's semantics.** The suite-level union check, exclusions and watchdog stay. S3 and S4 change only how classes are grouped into launches.
- **The running qualification.** No slice touches the harness clone, state, logs or processes of run `27a1b370`.

## Decisions

- **D-1 Do not stop or restart run `27a1b370` for any of this.** S1-S3 go to rc so the next candidate is faster. Whether to cut the next candidate before this run finishes is the operator's call, taken from `eta.md`. Rejected: pushing to rc and restarting now, which discards 4 h of evidence for a saving that is at most about 1 h per run from S1 alone.
- **D-2 Cherry-pick CARD-1420 to rc first (S1), CARD-1421 second and gated (S2).** Both merge cleanly except for their plan documents (dropped on rc, as CARD-1419/1410 did). CARD-1420 is a test-helper change with designed fallbacks, so it cannot redden the candidate; it can only fail to save time. CARD-1421 changes scheduling for 104 classes and was never run on Windows, so it gets one desktop concurrency row before it lands on rc. Rejected: cherry-picking both at once; a Windows-only red from T1 would then be blamed on the whole pick.
- **D-3 Replace the node model's guesses with TRX measurements and keep the 900 s target (S3).** Measured rows exist for every class the qualification has run; `scripts/refresh-nightly-class-costs.ps1` already merges them. With real costs the packer produces more, smaller launches for the heavy namespaces and keeps lone classes under the 60 min watchdog. Rejected: raising the target to 1,800 s now. Fewer launches save about 35 s each on rc (12 s after S1), roughly 20 min per halving, and a 30 min serial group with a 2x model error is a watchdog kill that costs an hour and a re-run. Revisit the target after S1 and S4 with measured groups.
- **D-4 S2 lands on rc only after CP-5 (desktop) is green.** Width is `Clamp(ProcessorCount / 2, 2, 4)`; the desktop has 8 threads, so 4. Any new Windows red after the pick is triaged against `ANTIPHON_TEST_GIT_SPAWN_LIMIT=1` before it is called a regression, as the CARD-1421 plan says.
- **D-5 Lane-aware packing (S4) uses a source census file, not reflection.** A pwsh refresh script reads `[ParallelLimiter<ProcessSpawnLimit>]`, `[ParallelLimiter<GitProcessSpawnLimit>]` and bare `[NotInParallel]` per class (all are class-level by convention; partial-class siblings are joined by class name) into `scripts/lib/nightly-class-lanes.json`. The packer then fills each group with at most the target of **serial seconds** (strict lane plus bare `NotInParallel`) and tops it up with parallel classes, within the existing per-namespace OR-filter grammar and the 30-operand cap. Rejected: loading the test assembly from pwsh (fragile across SDK versions) or a new `Antiphon.Tests` entry point (the exe is TUnit's; a custom mode is intrusive). Rejected: cross-namespace groups; `Application` holds 75% of nodes, so per-namespace mixing is enough.
- **D-6 Do not build a copied template repository for the Land harness.** 11 git processes are 2-4 s of a 100 s row; the lands are the cost, and a copied repository with a linked worktree needs `git worktree repair` and absolute-path fix-ups, which is new failure surface for seconds of gain. T2 reduces to S5.
- **D-7 S5 replaces `cmd.exe /c mklink /J` with a managed junction helper, then moves the class.** `JunctionPoint.Create` in `tests/Antiphon.Tests/TestHelpers/` uses `CreateDirectory` plus `DeviceIoControl(FSCTL_SET_REPARSE_POINT)` with a mount-point reparse buffer (no process). It must not reach `DirectoryLink`, `DirectoryLinkCommand` or `ScriptHarnessProcess`, which `ProcessSpawnLimitTests.Git_lane_classes_do_not_reach_the_pwsh_backed_link_helpers` forbids on the git lane. Rejected: a third "git plus short helpers" lane (CARD-1421 D-3 rejected it for the same reason); `DirectoryLink.TryCreate` (pwsh on Windows). The Linux alias path (`Directory.CreateSymbolicLink`) is unchanged.
- **D-8 S6 converts a global-sweep class by giving it one isolated clone per class and a keyed `[NotInParallel("<Class>")]`.** The sweep then sees only that class's rows, so the bare attribute is not needed, the class's tests stay serial among themselves, and the rest of the launch runs beside them. Rejected: one clone per test (74 clones through the one-wide clone lock is about the time saved); dropping `NotInParallel` entirely (the sweep is still class-wide).
- **D-9 Estimates in this plan are Windows-run estimates with wide error bars**, taken from the profile's 27-launch sample (89% of nodes imputed). Each slice carries a measured before/after row so the saving is reported, not assumed. Where the row needs Windows it is marked **desktop** and owed to that lane.

## Ranking

Wall saved is per full Windows rc run (about 84 launches). Effort is one Code task unless stated. Risk is to the candidate under test.

| Rank | Slice | Est. wall saved per run | Effort | Risk | Branches |
|---:|---|---:|---|---|---|
| 1 | S1 cherry-pick CARD-1420 (schema cache) to rc | 24-60 min (23 s desktop migrate window × ~62 DB launches; 41-65 s per DB launch measured on server2) | 30 min | low: helper-only, fallbacks to migrate | rc (master has it) |
| 2 | S3 measured class costs from the qualification TRX | 0-30 min directly; removes watchdog-kill exposure (each kill costs up to 60 min plus a re-run) | 30-45 min, **desktop** | none to tests; harness data | master and rc |
| 3 | S2 cherry-pick CARD-1421 (git lane) to rc | 60-80 min (1.75 h serial of git-only classes at up to 4x; Linux-verified only) | 30 min + CP-5 **desktop** | medium: Windows file locks under 4 concurrent fixtures; knob to bisect | rc after CP-5 |
| 4 | S4 lane-aware packing | 60-120 min (serial-lane launches held 65% of sampled time at 1.0x; mixing can approach 2x on them) | 2 tasks × 45-60 min | medium: harness logic; self-tests | master, then rc |
| 5 | S5 ActiveSourceClaim to the git lane (CARD-1437) | 20-24 min (32 min serial to 8-10 min at width 4) | 45-60 min + CP-9 **desktop** | low-medium: Windows reparse-point helper | master; rc at the next cut |
| 6 | S6 sweep suites on isolated clones: DeliveryWatchdog, AttentionService first | 10-15 min for the pair; template for 0.9 h of bare-NotInParallel serial time | 45-60 min per class | low-medium: sweep must still see its rows | master |
| 7 | S7 residual harness gap and cold-start outliers | up to 20 min (14 s × 84) if the residue is removable; unknown for the 46-67 s outliers | 30 min measure, **desktop** | none | after measurement |

## Slices

### S1: cherry-pick CARD-1420 onto rc/2026-10-20

Range `52624fcaf^..0d8557fba` (the 20 CARD-1420 commits on master, including the two reverts and the CP-7 repair; `git log --oneline origin/master | grep CARD-1420`). Mechanism: as CARD-1419 and CARD-1410 were done, `cherry-pick -x` onto an rc-based task branch, then land to rc. Drop `docs/superpowers/plans/2026-10-21-card-1420-cached-test-schema-plan.md` from the pick (rc carries no plan documents; the only `merge-tree` conflict).

Files (test helpers only): `tests/Antiphon.Tests/TestHelpers/TestDbFixture.cs`, `TestDbFixtureLifecycle.cs`, `TestSchemaCache.cs`, `TestDbFixtureIsolationTests.cs`, `TestDbFixtureLazyInitializationTests.cs`, `TestDbFixtureLifecycleTests.cs`, `TestDbFixtureSchemaCacheLifecycleTests.cs`, `TestSchemaCacheTests.cs`, `TestSchemaSnapshotTests.cs`; `docs/testing-and-build.md` (the Cached test schema paragraph).

Tests: the picked witness classes on the rc build (CP-1), one DB-touching class twice on the desktop so the second process is a hit (CP-2, before/after).

rc question: yes, this is the slice's whole purpose. Nothing in it can turn a test red that was green: a cache defect falls back to the migrate path and is logged as `C1420 schema-cache fallback`.

Owed to the Windows lane: CP-2, and a read of the first rc launch's stderr for the `C1420 schema-cache hit|miss` line and its `ms=`.

### S2: cherry-pick CARD-1421 onto rc/2026-10-20

Range `d4414c0bf^..1ed603052` (13 commits; `grep CARD-1421`), minus `docs/superpowers/plans/2026-10-21-card-1421-git-spawn-limit-plan.md`. 104 test files swap one attribute; new `GitProcessSpawnLimit.cs`; `ProcessSpawnLimitTests.cs` rosters and the DirectoryLink guard; `docs/testing-and-build.md` two-lane rule. One of the 106 files (`AgentTaskDispatchBaseGuardTests.cs`) also differs between rc and master for other reasons; `merge-tree` resolves it cleanly.

Tests: CP-3 rosters on the rc build; CP-5 (**desktop**) three git-lane Land classes with the DB clone path in one process at width 4, repeated 3x, before S2 lands.

rc question: yes, after CP-5 is green (D-4). If CP-5 shows a Windows cleanup or `index.lock` red that the base does not, S2 stops and the red is filed; the pick waits for that fix.

### S3: measured class costs from the qualification TRX (desktop)

Steps, read-only against the harness clone:
1. Read `C:\Antiphon\releases\task-c58e21f5\eta.md` and list per-group wall (`-tests.log` mtime minus diag creation) for the completed groups of `C:\Antiphon\releases\candidates\rc-2026-10-20-ceb6e3d6b784\logs\2026-10-22-0157-27a1b370`. Record predicted `seconds` (from the launch plan) against actual wall per group in the Code report. This replaces the "12 of 84" extrapolation.
2. `pwsh -NoProfile -File scripts/refresh-nightly-class-costs.ps1 -TrxPath (Get-ChildItem <27a1b370>\antiphon-*.trx).FullName, (Get-ChildItem <ebd2ff29>\antiphon-*.trx).FullName -Source '<runs and shas>'`, newest run last so its rows win. Classes with no TRX keep their rows; the two `pace` rows are replaced only if their class ran.
3. Commit `scripts/lib/nightly-class-costs.json` (ASCII, committed data). Re-run the packer self-tests (CP-4).
4. Report the launch count and the largest group's estimate from `Get-NightlyProfileLaunchPlan` on the real listing (the C1397 case prints both).

Files: `scripts/lib/nightly-class-costs.json`; no script change. If a TRX class name carries a constructor suffix the refresh script does not strip, fix `refresh-nightly-class-costs.ps1` with a red case in `scripts/test-nightly-tests.ps1` first.

rc question: yes; the file is byte-identical on rc and master today, so the same commit cherry-picks cleanly.

### S4: lane-aware launch packing (two tasks)

S4a: `scripts/refresh-nightly-class-lanes.ps1` writes `scripts/lib/nightly-class-lanes.json` (`format: antiphon-nightly-class-lanes-v1`, `classes: { "<bare class>": "strict" | "git" | "serial" | "parallel" }`) from a regex census of `tests/Antiphon.Tests/**/*.cs` joined by class name across partial files: strict lane, git lane, bare `[NotInParallel]` (serial), keyed `[NotInParallel("...")]` with no limiter and everything else (parallel). A pwsh case `Test-C0856_LaneCensus` over `scripts/Fixtures` partial-class samples is the red witness.

S4b: `Split-NightlyItemsBySeconds` gains a second budget: `SerialSeconds` (strict plus serial classes) per bin at most `NightlyLaunchTargetSeconds`, while total seconds may reach `NightlyLaunchParallelTargetSeconds` (new, 1,800 s) when the extra is parallel or git-lane classes. Items without a lane row count as serial. `Test-C0856_LaneAwareGroups`: a fixture with three strict 600 s classes and six parallel 300 s classes in one namespace must yield no group with two strict classes while a parallel class is unplaced; red against the current packer, which bins by total seconds only. Existing cases `C1397`, `C1410`, `C1419` stay green.

Files: `scripts/lib/nightly-tests-impl.ps1` (`Split-NightlyItemsBySeconds`, `ConvertTo-NightlyProfileLaunchGroups`, `Get-NightlyPackedItemSeconds`), new refresh script and JSON, `scripts/test-nightly-tests.ps1`, `docs/testing-and-build.md` (launch-group paragraph), `docs/release-gates.md` if it names the packer.

rc question: master first; cherry-pick to rc once CP-6/CP-7 are green and S3's measured rows are in, because the gain depends on both. Harness files are identical between rc and master except `nightly-common.ps1`, so the pick is clean.

### S5: `AgentTaskLandActiveSourceClaimTests` to the git lane (CARD-1437)

1. Red: add `AgentTaskLandActiveSourceClaimTests` to `GitLaneClasses` in `ProcessSpawnLimitTests.cs` (fails: the class carries the strict limiter) and a helper test `JunctionPointTests.Creates_a_junction_git_sees_as_the_target` that on Windows creates a junction and asserts `git rev-parse --show-toplevel` through it resolves to the target, and on any other OS asserts `JunctionPoint.Create` throws `PlatformNotSupportedException` (so it is a real test everywhere and never skipped). It is excluded from CP-8 by filter because its Windows half is the one that matters; CP-9 runs it.
2. `tests/Antiphon.Tests/TestHelpers/JunctionPoint.cs`: `Create(string junction, string target)` via P/Invoke (`CreateFileW` with `FILE_FLAG_OPEN_REPARSE_POINT | FILE_FLAG_BACKUP_SEMANTICS`, `DeviceIoControl` `FSCTL_SET_REPARSE_POINT`, `IO_REPARSE_TAG_MOUNT_POINT`, `\??\` substitute name). No `Process`, no `DirectoryLink`, no `ScriptHarnessProcess`.
3. Replace `MklinkJunctionAsync` with `JunctionPoint.Create`; swap the attribute to `[ParallelLimiter<GitProcessSpawnLimit>]`; remove the class from `StrictClasses`.
4. CP-8 (server2) runs the class and the rosters; CP-9 (**desktop**) runs the 16 rows at width 4 and reports wall against the 1,920 s measured row (tolerance: at most 1,000 s, that is at least a 1.9x gain, else report and keep the class strict).

rc question: master; rc at the next cut, not during a qualification.

### S6: global-sweep suites on isolated clones (one class per task)

Order: `AgentTaskDeliveryWatchdogTests` (74 tests, measured 227 s), then `AttentionServiceTests` (130 tests, bare attribute, no spawner), then by test count from the census in Ground truth. Per class:

1. Red: a test in the class asserting `typeof(<Class>).GetCustomAttribute<NotInParallelAttribute>()` carries the key `"<Class>"` and that `CreateContext()` does not point at `TestDbFixture.SharedDatabaseName`.
2. `[Before(Class)]` creates one `IsolatedTestSchema` via `TestDbFixture.CreateIsolatedSchemaAsync()`; `CreateContext()` and every harness in the class use it; `[After(Class)]` disposes it. Replace `[NotInParallel]` with `[NotInParallel("<Class>")]`.
3. Row CP-10: the class alone (green) and the class beside two parallel classes with wall reported against the same pair without the change (tolerance: the mixed launch's wall is at most the sum of the parallel pair's wall plus 60 s; else report).

Files per class: the class file(s) (partial siblings included), `ProcessSpawnLimitTests.cs` untouched, `docs/testing-and-build.md:55` gains one sentence: a sweep over the class's own clone takes a keyed attribute.

rc question: master only.

### S7: measure the residual per-launch gap and the cold-start outliers (desktop, measurement only)

`Measure-Command` around each step of the launch loop (`nightly-tests-impl.ps1:990-1080`) for one two-launch seam run against the real listing on the desktop, and one launch of `AgentTaskDeliveryWatchdogTests` with the 67.5 s outlier first then repeated. Output: an investigation note under `docs/investigations/` with the step table; a fix is a new card. No checkpoint row; no source change.

## Order

1. S1 (rc) and S3 (desktop, data) in parallel; both are safe to land before the running qualification finishes.
2. CP-5 (desktop) then S2 (rc).
3. S4a, S4b (master), then cherry-pick to rc with S3's data.
4. S5, S6 on master as Code depth allows; they ride the next rc cut.
5. S7 whenever the desktop lane is idle; its output ranks the next plan.

## Verification design

| V | Claim | Witness |
|---|---|---|
| V-1 | The rc build with S1 restores on a hit, migrates and publishes on a miss, resets on a failed restore, never publishes when off | `TestDbFixtureSchemaCacheLifecycleTests` (5 methods), `TestSchemaCacheTests`, `TestSchemaSnapshotTests` (3), `TestDbFixtureLazyInitializationTests`, `TestDbFixtureLifecycleTests` on the rc-based branch |
| V-2 | A second DB-touching process on the desktop is a hit and reaches its first test at least 15 s sooner than a miss | CP-2: same class twice, stderr `C1420 schema-cache miss` then `hit`; TRX `Times/@start` to first `startTime` delta: hit at most 12 s, miss at least 20 s (desktop baseline 27.6 s) |
| V-3 | The rc build with S2 has both lanes, no class on both, no git-lane class reaching the pwsh link helpers | `ProcessSpawnLimitTests` (all 8 methods) |
| V-4 | The measured cost table keeps the packer under the watchdog and assigns every node once | `Test-C1410_MeasuredCostGroups`, `Test-C1397_RcAntiphonLaunchGroups` on the refreshed JSON |
| V-5 | Four concurrent git-lane Land classes with DB clones are green on Windows three times | CP-5 |
| V-6 | The lane census classifies partial-class fixtures correctly; the packer never pairs two serial-lane classes while a parallel class is unplaced | `Test-C0856_LaneCensus`, `Test-C0856_LaneAwareGroups` (both red before S4) |
| V-7 | ActiveSourceClaim starts no non-git child and runs on the git lane | roster test red then green; `JunctionPointTests` on the desktop; CP-9 wall at most 1,000 s for the 16 rows |
| V-8 | A converted sweep class keys its attribute, uses its own clone, stays green alone and beside parallel classes | per-class attribute test; CP-10 |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0856-s1/` (rc-based branch) | schema-cache-rc | `/*/*/(TestDbFixtureSchemaCacheLifecycleTests*)\|(TestSchemaCacheTests*)\|(TestSchemaSnapshotTests*)\|(TestDbFixtureLazyInitializationTests*)\|(TestDbFixtureLifecycleTests*)/*` | V-1 | 0 failed, 0 skipped | 41 | 12 | |
| CP-2 | S1 | CP-1 (-NoBuild), **desktop** | schema-cache-hit | `/*/*/AgentTaskCheckInterpreterTests/*` run twice (`--repeat 2`) | V-2 | 0 failed; second ordinal stderr has `C1420 schema-cache hit`; first-test delta at most 12 s | 40 | 6 | ANTIPHON_TEST_SCHEMA_CACHE=%TEMP%\antiphon-c0856-cache |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c0856-s2/` (rc-based branch) | lanes-rc | `/*/*/ProcessSpawnLimitTests/*` | V-3 | 0 failed, 0 skipped | 14 | 8 | |
| CP-4 | S3 | n/a | measured-groups | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1410_MeasuredCostGroups` | V-4 | exit 0 | n/a | 5 | TEMP=/tmp |
| CP-4b | S3 | n/a | rc-groups | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1397_RcAntiphonLaunchGroups` | V-4 | exit 0 | n/a | 5 | TEMP=/tmp |
| CP-5 | S2 | CP-3 (-NoBuild), **desktop** | git-lane-windows | `/*/*/(AgentTaskLandBoundaryTests*)\|(AgentTaskLandRemovalMatrixTests*)\|(AgentTaskLandCleanupSafetyTests*)\|(AgentTaskLandConcurrencyTests*)/*` with `--repeat 3`, `--expected-repeat 3` | V-5 | 0 failed in every ordinal | 126 | 45 | ANTIPHON_TEST_GIT_SPAWN_LIMIT=4 |
| CP-6 | S4a | n/a | lane-census | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C0856_LaneCensus` | V-6 | exit 0 (red before S4a: unknown case, exit 2) | n/a | 3 | TEMP=/tmp |
| CP-7 | S4b | n/a | lane-aware-groups | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C0856_LaneAwareGroups` | V-6 | exit 0 (red before S4b: unknown case, exit 2) | n/a | 3 | TEMP=/tmp |
| CP-7b | S4b | n/a | measured-groups-still | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1410_MeasuredCostGroups` | V-6 | exit 0 | n/a | 5 | TEMP=/tmp |
| CP-7c | S4b | n/a | rc-groups-still | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1397_RcAntiphonLaunchGroups` | V-6 | exit 0 | n/a | 5 | TEMP=/tmp |
| CP-7d | S4b | n/a | census-once-still | `pwsh -NoProfile -File scripts/test-nightly-tests.ps1 -Case C1419_DiscoveryParsedOncePerSuite` | V-6 | exit 0 | n/a | 3 | TEMP=/tmp |
| CP-8 | S5 | `tests/Antiphon.Tests -> bin-c0856-s5/` | claim-git-lane | `/*/*/(AgentTaskLandActiveSourceClaimTests*)\|(ProcessSpawnLimitTests*)\|(JunctionPointTests*)/*` | V-7 | 0 failed, 0 skipped | 31 | 30 | |
| CP-9 | S5 | CP-8 (-NoBuild), **desktop** | claim-width-4 | `/*/*/(AgentTaskLandActiveSourceClaimTests*)\|(JunctionPointTests*)/*` | V-7 | 0 failed; CHECKPOINT wall at most 1,000 s | 17 | 20 | ANTIPHON_TEST_GIT_SPAWN_LIMIT=4 |
| CP-10 | S6 | `tests/Antiphon.Tests -> bin-c0856-s6/` | sweep-beside-parallel | `/*/*/(AgentTaskDeliveryWatchdogTests*)\|(AgentTaskCardBindingTests*)\|(AgentTaskConcurrencyLimitTests*)/*` | V-8 | 0 failed; wall reported against the same filter at base | 121 | 15 | |

Rows marked **desktop** pin `-Runner desktop` and are owed to the Windows lane; every other row runs on server2. Each S6 class after the first adds one CP-10-shaped row with its own build output. CP-2, CP-5 and CP-9 are before/after rows: the Code report states both numbers and the tolerance verdict, and a missed tolerance is a reported fact, not a reason to widen it. `Min` floors are the larger of the `[Test]` and `[Arguments]` counts per class from source, summed per row (a lower bound, never a census); confirm `executed=` in each CHECKPOINT line. S1 and S2 run from an rc-based task branch, so their builds are of rc plus the pick, never of master.

### Cost

S1: one build plus CP-1 on server2, CP-2 on the desktop, about 25 min of machine time. S2: one build, CP-3, and 45 min of desktop CP-5. S3, S4: pwsh cases only, minutes. S5: one build, CP-8, 20 min desktop. S6: one build and one 15 min row per class. Anything beyond this table needs a stated reason.

## Owed to the Windows lane

- Per-group predicted-versus-actual wall for run `27a1b370` and the monitor's ETA (`eta.md`), read-only; the S3 Code report carries the table.
- CP-2, CP-5, CP-9 and the first rc launch's `C1420 schema-cache` line after S1.
- S7 entirely.

## Follow-ups filed by this plan

- CARD-1437 is S5; CARD-1438 (Checkpoints/* lanes) and CARD-1439 (14 limiter-only files) stay as they are and ride the git lane later.
- A card for the remaining 191 bare-`[NotInParallel]` files once the S6 template has run twice.
- A card for raising `NightlyLaunchTargetSeconds` once S3 and S4 have one measured full run behind them (D-3).
