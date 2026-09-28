# GitService seam overhead in FakeGit traces

## Result

**GitService contributes 0 seconds of the qualified CP-42 76.3725563-second outer interval.** None of the 19 `AgentTaskLandBoundaryTests` cases calls a `GitService` method. The 3,632 Git children in CP-42 (including 2,665 labelled `service`) are attributable to the landing path and its other Git entry points, not to `GitService`. Thus moving the FakeGit seam from `IGitCommandExecutor` up to `IGitService` cannot save any of CP-42's 10.920507814 seconds of child-process time or any surrounding GitService time. `service` in the trace is a lifecycle role, not the C# class name.

Source proof: `LandingSafetyHarness` injects `Fixture.Git` as `ILandingGit` and drives `AgentTaskLandService`/`AgentTaskLandingProtocol`. `DelegationWorktreeService` resolves in that graph, but its GitService-backed `CommitAllChangesAsync` arm is outside this landing trace; the containment paths it uses have their own `GitAsync`. The CP-42 qualified result and raw archive are `docs/investigations/2026-09-28-fakegit-pilot-results.md` and `docs/investigations/2026-09-28-fakegit-cp42-raw.tar.gz` on `feat/card-task-2aa8cd10`, at source `e0d450f947194c3f3129557a4ab9effd981ff032`.

## Direct GitService measurement

To estimate overhead where `GitService` *is* called, I profiled the real backend's 12-case `GitServiceTests` selection at `c00e33df48f92d6ce174d4cacd5b117efcfe0065` (the executor-seam pilot). In a detached scratch worktree I temporarily timed entry-to-exit of each operational public method and the awaited interval of each `_executor.ExecuteAsync` call with `Stopwatch.GetTimestamp()`. For each method, **own time = inclusive method time minus executor-await time**. This includes command construction, parsing, logging and file operations outside the executor; it excludes process launch, pipe draining and Git execution inside the executor. The temporary instrumentation was never put on the deliverable branch.

The first run used `ANTIPHON_TEST_REAL_GIT=1` and `pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name GS-PROFILE-1 -Project tests/Antiphon.Tests -OutputPath bin-gsprofile/ -Filter '/*/*/GitServiceTests/*' -MinExecuted 12 -Expect GitServiceTests -ResultsRoot .antiphon/gs-profile-4ddd-1`. The repeat used the same filter and output path with `-NoBuild`, `-Name GS-PROFILE-2`, and fresh results root `.antiphon/gs-profile-4ddd-2`. Both held build slots and executed 12 passed, 0 failed, 0 skipped. Six tests are naming-only; the six operational tests made 22 service-method calls and 57 executor calls per run. [Raw timing samples](2026-09-28-gitservice-seam-samples.csv) preserve each call's Stopwatch ticks.

First run (TUnit selection duration 1.617 seconds):

| Method | Invocations | Executor calls | Inclusive ms | Executor ms | Own ms |
|---|---:|---:|---:|---:|---:|
| InitializeWorkflowBranchesAsync | 6 | 18 | 123.179 | 111.954 | 11.225 |
| CreateStageBranchAsync | 5 | 5 | 21.021 | 20.646 | 0.375 |
| CommitArtifactAsync | 5 | 15 | 82.272 | 80.632 | 1.640 |
| TagStageAsync | 4 | 16 | 43.428 | 42.879 | 0.550 |
| MergeStageBranchAsync | 1 | 2 | 9.970 | 9.813 | 0.157 |
| GetDiffBetweenTagsAsync | 1 | 1 | 3.589 | 3.423 | 0.167 |
| **Total** | **22** | **57** | **283.460** | **269.346** | **14.114** |

The repeat had 6,317.154 ms inclusive method time, 6,238.231 ms executor time, and **78.923 ms own time**; TUnit selection duration was 9.719 seconds under heavier host contention. The method-call/executor-call counts were identical. `CommitArtifactAsync`'s own time rose from 1.640 to 58.085 ms, consistent with file/scheduler variability. Own time stayed below 1% of selection duration in both runs. These are sums of call durations, not additive estimates for CP-42, whose tests exercise different code. Timing instrumentation and scheduling noise make millisecond-scale values approximate; they do not change the zero-call CP-42 finding.

## Sequential calls and recommendation

`GetDiffBetweenTagsAsync` (both overloads) and `GetBranchDiffAsync` each issue one Git command. `GetWorktreeDiffAsync` issues `diff --find-renames` followed by `ls-files --others --exclude-standard`, then reads and formats untracked files. A single ordinary read-only `git diff` cannot include untracked file content. `TagStageAsync` makes four sequential calls (`checkout`, `rev-parse HEAD`, `rev-list` tag, conditional `tag`); a real-Git fast path could create the tag at the named stage ref in one `git tag <tag> <stage-ref>` call and probe only on an existing-tag failure. That change is possible inside GitService and does not require a semantic fake. `InitializeWorkflowBranchesAsync`, `CommitArtifactAsync`, `MergeStageBranchAsync`, and `CommitAllChangesAsync` also chain commands, but these perform ordered mutations and checks. A semantic fake could compress them into one in-memory operation, but the low-level fake already removes each Git subprocess, leaving only the measured small in-process overhead.

**Recommendation:** Do not build a GitService-level seam for the CP-42 landing pilot or as a speed optimization on current evidence. It has zero reach in the 19-case trace and only 14–79 ms of extra removable GitService work in a selection that actually exercises it. A semantic seam could reduce fake CLI syntax emulation, but fake-backed tests would stop exercising GitService's command construction, parsing, and failure handling; the real parity lane would then have to retain those assertions. If another selection exposes a larger bottleneck, trace the Git entry point it actually uses (notably `LandingGit`/worktree services) before proposing a new seam. A targeted `TagStageAsync` command-count optimization can be considered independently of FakeGit.
