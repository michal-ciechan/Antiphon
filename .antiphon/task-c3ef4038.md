# CARD-1678 Review round 1 of 3 (task c3ef4038): Clean

Subject: `feat/card-task-6ff01870` tip `f547852cdcde0fad6708e036072171a8b0a0f16d` (Code task
6ff01870-a09c-481c-b4e7-7326bb9d24c3), base master `59a37c1675862a7ccc8bc7bc36769e2ac50bc0b8`. Reviewed on server2
(Linux), runner mirror `/work/worktrees/task-c3ef4038`. One build-slot lease per build or run, native output,
`-p:UseAppHost=false`, `TEMP=/tmp`; every slot granted with waited=0s. No checkpoint plan table exists for this card,
so there are no checkpoint receipts.

No defects.

## (1) Test-only

`git diff --stat 25fe114e3..f547852cd` (the CARD-1678 commits) touches only `AgentTaskCardBindingTests.cs` (+45),
`LandGraphCanaryToleranceTests.cs` (new, 182), `RunnerAlarmHostedServiceTests.cs` (+95) and the Code report. There are
no product, csproj, props or targets changes. The two CARD-1674 commits below them, cbb54d3d5 and 25fe114e3, were
reviewed separately as 15f1bfffe.

## (2) Mutants: reapplied in place, one gated build per set, restored with `git checkout -- <exact path>`

Run filter for each set: `/*/*/(RunnerAlarmHostedServiceTests*)|(AgentTaskCardBindingTests*)|(LandGraphCanaryToleranceTests*)/C1678*` (8 tests)

| Set | Mutants | Red / 8 | Red at the intended assertion |
|---|---|---|---|
| A | M71-6 `DrainWakeSlack` 1 s to 0 s; M47-8 Succeeded filter dropped; LG-I in-repo `StartsWith` check removed | 5 | NextDue one drain 08:05:54 vs :55; one-pass `Due` 00:30:00 vs 00:30:01; several-drains `Due` 00:30:00; `IsIgnored(outside)` True; control card warned `review_round_limit: CARD-0168 already has 3 ...` |
| B | M71-8 latest drain instead of earliest; LG-S copy-set check removed | 3 | NextDue several 30 min late; several-drains `Due` 01:00:01; `IsIgnored(copied)` True |
| C | LG-C canary-absence check removed (tool-only change) | 1 | `IsIgnored(planted)` True |

- Each canary mutant turned only its own canary witness red.
- The control-only canary test stayed green under all three canary mutants.
- M71-6 also turns the several-drains hosted test red, as expected, because both tests assert `Due`.
- None of the red was a timeout: every hosted red failed at the `Due` assertion, after the 30 s first-timer wait had passed.

## (3) Reflection seam

- **It tests the real tool code.** Antiphon.Tests builds the tool through a `ProjectReference` with
  `SetTargetFramework=net10.0`. `CopyStagedHelpers` then copies its output into `land-graph-audit/`, using
  `SkipUnchangedFiles` (which compares timestamp and size). After every build, `cmp` showed the staged dll was
  byte-identical to `tools/Antiphon.LandGraphAudit/bin/Debug/net10.0`. Set C changed only the tool source, rebuilt in
  53 s, and the mutant was visible to the test. After restoring it, the 15 s rebuild went green.
- **Seam drift fails loudly.** A renamed ctor, `_copied` or `<Root>k__BackingField` throws
  `MissingMethodException` or `MissingFieldException`, which is red. It does not pass silently.
- **Loading net10 into net9.** The test host is net9.0. The mirror has runtimes 9.0.22 and 10.0.14, and the host
  runs on 9.0.22. `ToolLoadContext.Load` resolves each reference by simple name only (the version is dropped), so
  System.Runtime 10 binds to the host's 9.0 framework. LandGraphAudit.Core comes from Default because the test
  project references it. Microsoft.Build is never touched by the members called here. The called members (private
  ctor, `IsIgnoredRepositoryFile`, `Dispose`/`DeleteRoot`) have no OS-specific API calls; only the `\` to `/` path
  rewrite branches on OS. So Windows JITs the same calls.
- **Windows readiness (by reading the code, not run here).**
  - The class is portable by design, not Linux-only.
  - Paths come from a single `GetTempPath` string, so 8.3 short names stay consistent through `GetFullPath`.
  - The staged path is `AppContext.BaseDirectory\land-graph-audit\` on both OSes.
  - **File locks:** `LoadFromAssemblyPath` keeps the dll mapped for the life of the test process. Other in-process
    tests only execute the dll (`dotnet <dll>`, which is a read), and none of them overwrite or delete it, so the
    lock is harmless.
  - No Windows execution evidence exists yet: nothing runs on the desktop. The rc Windows qualification is the
    first native run. LG-K (OrdinalIgnoreCase on Windows) is still owed to a Windows lane, as the test file states.
- **Leaks.** No `/tmp/c1678-canary-*` or `antiphon-landgraph-canary-*` directories were left after the runs.

## (4) Hosted-service timing: `RunnerAlarmHostedServiceTests` x3 in one lease, cold process each

| Run | Wall | Result | C1678 one-pass | C1678 several-drains | NextDue |
|---|---|---|---|---|---|
| 1 | 20.1 s | 10/12 | 6.31 s | 1.55 s | 0.03 s |
| 2 | 24.7 s | 10/12 | 6.03 s | 6.67 s | 0.01 s |
| 3 | 18.9 s | 10/12 | 6.48 s | 6.34 s | 0.03 s |

- The new tests passed 9/9. The 30 s wait is only the upper bound on the first-timer wait; passing runs take the
  measured ~6 s.
- **The 2 reds per run are inherited, not caused by this branch** (see A-1).
  - Failing tests: `C1671_the_loop_wakes_when_a_drain_passes_its_bound...` and
    `a_failing_pass_is_logged_and_retried_without_waiting_for_the_sweep`.
  - Both hit `TimeoutException` at the existing 5 s `FirstTimer` wait.
  - Master `919d8d42f` (detached scratch worktree, same lease pattern) shows the same thing: 2/7 red in each of 3 runs.
    The failing tests there were `C1671_...`, `the_loop_raises_on_the_grace_timer_and_wakes_on_a_fence_signal` and
    `a_failing_pass_...`, each at the same 5 s wait.

## (5) Inherited land-graph reds

At the tip, with `tests/Antiphon.E2E/TestOutput/Logs/run.log` present (created, then removed by exact path):

- 10/12 passed: the 9 `C1385_Ignored*` / `C1385_Copied*` cases and `C1483_S4_RealRepositoryApproves`.
- 2 red:
  - `C1385_RegistryMatchesNarrowGraph`: recorded digest 7456743D... vs actual 266595CBAA5CA00BE0182F33804AE94AD851DB9BBCF74C3FAAF211D5D5A49CFA.
  - `C1483_S5_CheckRequiresEvaluationRecord`: `real.ExitCode` 1.

Both reds are inherited and independent of this branch:

- `git diff --name-only 59a37c167..HEAD` contains no proj, props, targets, sln or global.json file.
- origin/master has since moved to `919d8d42f` ("chore: land graph digest..."). That commit records exactly
  `266595CB...` in both `LandVerificationBuildGraph.evaluation.txt` and `.manifest.txt`, so the land rebase clears
  both reds.

## (6) Touched classes at the tip (restored, rebuilt)

- **183/183 passed, 0 skipped:**
  - 50 in the eight alarm classes: AlarmSettingsValidator 2, RunnerAlarmAttention 3, Coordinator 13, Delivery 11,
    HostedService 12, Notifier 1, Wiring 1, RunnerStateAlarmExclusion 7.
  - CardBinding 23, ReviewRoundsPolicy 7, InstructionBundle 78, ReviewEvidenceParser 21, LandGraphCanaryTolerance 4.
  - Counts are taken from the TRX className census.
- **Extra run for the new `[Category("Unit")]` class:** `TestClassificationPolicyTests` + `NativeLinuxPolicyWitnessTests`,
  42/42 passed.
- **check-evidence-diff** (`-BaseRef 59a37c167... -HeadRef f547852cd...`): commits=6, entries=1, violations=0, exit 0.
- **merge-tree:**
  - onto origin/master `919d8d42f`: clean, tree `31bd48ba1`.
  - onto rc/2026-11-03 (= `919d8d42f`): clean.
  - onto rc/2026-11-01 `10a826b9d`: conflicts. The whole-branch merge conflicts in `client/src/api/agentTasks.ts`
    (unrelated history). A per-commit cherry-pick chain conflicts at cbb54d3d5, 25fe114e3 and 747bd708d, because
    that rc lacks the CARD-1671/1674/1647 bases. This only matters for porting (A-4).
- **Not run: the whole Unit lane** (~3 h on server2). The brief scoped this round to the touched classes within a
  60-minute budget, so `ordinaryScopeCompleted: Interim`.

## Advisories

- A-1: Inherited cold-start red in `RunnerAlarmHostedServiceTests`. When the class runs alone in a fresh process, the
  existing 5 s `FirstTimer` waits time out, because the cold first pass takes about 6 s on server2. This happens at
  master `919d8d42f` too. Any class-scoped shard of the rc qualification can hit it. It needs a Debug task: widen
  those setup waits to the 30 s used here, or warm the first pass.
- A-2: The ALC is declared collectible but is never unloaded (the `Lazy<Type>` is static). This is harmless.
- A-3: The seam ignores the `ANTIPHON_LANDGRAPH_TOOL_DLL` override that `LandGraphEvaluationTests.ToolDll()` honours.
  Nothing in the repo sets that variable.
- A-4: An rc/2026-11-01 port would need the CARD-1671/1674/1647 bases. rc/2026-11-03 takes the branch cleanly.

Cleanup:

- Every bin/obj directory from my builds was removed by exact literal path.
- The scratch base worktree was removed with `git worktree remove`.
- TRX and logs are only in the session scratchpad.
