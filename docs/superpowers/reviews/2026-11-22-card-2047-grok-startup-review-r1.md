# CARD-2047 Grok startup deadline and retry: Final Review round 1

Verdict: CLEAN. No blocker and no regression. Next is land. Advisories below do not hold the land.

- Reviewed source: `d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040`
- Code owner: `e3e939aa-835e-4223-8a6c-24647d7812f2` (Codex). Reviewer is Grok.
- Base: `611df2dbc9606154d9490cdf12d46c55879f0bff`
- Range: `611df2dbc9606154d9490cdf12d46c55879f0bff..d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040`
- Fetched `origin/master` at review: `2321e4afc763b238a265ba1d853b671161a454de` (the brief named `d7c46ddbbf4bb2e59ccbdbe4bf478b527f8f34ad`; master moved before this fetch)
- `reviewedSourceClean`: true. Checkpoint run `20261122-024247-9d52` recorded `sourceState=clean` and `buildSource=verified` at that SHA. Scratch mutants were restored before evidence-diff, the index regeneration, and land-graph Check.
- `ordinaryScopeCompleted`: Full
- Review round: 1
- Downgrade selector count: 0. The diff has no migration file, and `impacted-classes.ps1` selected no migrations kind.

## Prompt gate

The first prompt stays withheld until ready. `AgentSessionService` calls `WaitForReadyOrThrowAsync` and only then sets `SessionStatus.Running` (`AgentSessionService.cs` around the launch path at line 229). `DeliverNextLockedAsync` returns `FlushResult.Nothing` unless that status is `Running` (`SessionMessageQueueService.cs` lines 2500 and 2521). The boot brief therefore cannot be typed during a Working extension or the observation retry.

Ready opens only from `GrokReadyTracker.Observe` returning true: two consecutive same-region observations whose `IsReady` is true, spanning the settle interval, with elapsed time still inside the current deadline, and a re-check that the deadline has not passed (`GrokStartupReadiness.cs` lines 89-101 and 327-335). `IsReady` is only `GrokStartupReason.Ready`. Working and Unknown reset the positive streak in `RecordScreen`. A frame taken at `elapsed >= deadline` is recorded and cannot return true (lines 289-298).

The only pty write inside `GrokReadyWait.WaitAsync` is `GrokTrustPromptDetector.AffirmativeKey` (`y`). It runs only when `firstWindow is null && !trustWritten` (line 304). That is the first window, including a Working extension that has not started the retry. The retry assigns `firstWindow` before it continues, so the second window does not write. Sign-in sets `OnSignIn` and fails; it does not type. This wait has no update-modal writer. Plan S3 forbids input during the second window, and the first-window trust answer remains the existing path.

## Bounds

At the shipped `GrokReadyMaxWaitMs` of 60000, `workingCeiling` is 180 seconds from the original start (`DefaultWorkingCeiling`). `ExtendWorking` adds 30 seconds and clamps to that ceiling, and it returns false when the deadline is already at the ceiling or the next step would already be behind elapsed time. Continuous Working therefore fails at 180000 ms. An Unknown screen with MCP seen, at the first deadline, opens exactly one retry: `firstWindow` is set once and never cleared, the tracker is replaced, and the new deadline is `elapsed + min(MaxWait, ceiling - elapsed)`. At the default that is 120 seconds. Working during that retry can still walk to the same 180-second ceiling. Oscillation does not start a fresh 180 seconds; an extension happens only when elapsed time has reached the current deadline. A configured `MaxWait` already above 180 seconds keeps that timeout and receives neither extension nor retry, because the deadline starts at the ceiling and `ContinuePastDeadline` runs only at the deadline.

`AgentSessionLaunchQueue` has no timeout of its own. Pty host `LaunchTimeout` (30 seconds) is the protocol "Launch received" budget. `FailNeverStartedAsync` uses `DeliveryFailTimeoutMinutes` (default 10) from `DispatchedAt`, or from a later `LaunchResumedAt`. A Starting session the runner still serves is deferred to the reconciler. The new worst case holds the seat in `Starting` for 180 seconds. That sits inside the 10-minute watchdog and does not trip it by itself.

Cancellation is the caller token. The loop starts with `ThrowIfCancellationRequested`, the delay and bounded read use that token, and a caller cancel is not swallowed (the `OperationCanceledException` filter requires `!ct.IsCancellationRequested`). `DisposeAsync` only stops watching. There is no new retry loop.

## Capture deletion

`GrokStartupCaptureStore.Prune` enumerates `grok-startup-*.txt` in the capture directory, then keeps a file only when `TryGetCaptureTime` accepts the exact name (prefix, UTC timestamp, two `N` guids, `.txt`). Reparse-point files are skipped before `Length`. `FileNotFoundException` on `Length` is ignored. Delete catches `IOException` and `UnauthorizedAccessException`. `StoreStartupCapture` catches `IOException`, `UnauthorizedAccessException`, and `ArgumentException` and logs `captureWriteFailed=true` without a path, so those failures stay inside the launch path.

Age comes from the name, not mtime. Seven days expires a capture even under the count cap. The count cap is clamped to 1..200 (default changed from 10 to 200) and the first 24 hours are exempt from that count. The hard 50 MiB cap wins over the 24-hour grace: the keep test requires `bytes <= 50 MiB` as well as the age and count clauses, and the loop deletes oldest-by-ordinal-name until that test holds, then `break`. Plan S1 says the hard 50 MiB cap can evict any age. That is what the code does.

On the success path the file just written is the newest name and is at most 128 KiB (`FileLimit`). After older deletes succeed, the loop stops once the remaining total is within 50 MiB, so the warning's path is still on disk. The byte-cap test seeds exactly 50 MiB of older files and expects the oldest one removed and `written` kept.

## No regression

The fast path is unchanged: extension and retry run only at a deadline. Unknown with no MCP fails at the first deadline and does not set `firstWindow` (`C2047_Unknown_retries_only_if_mcp_was_seen`, inside CP-6). The diff does not touch Claude or Codex adapters. Failure outcomes stay `Deadline`, `Trust`, `SignIn`, `SnapshotFailure`, and `Exited`. The new diagnostic fields are `workingCeilingMs` and the first/second window timings.

The changed C# diff contains no `OperatingSystem`, `IsWindows`, `IsLinux`, or `IsOSPlatform` lexeme. R-2's Windows execution of CP-2, CP-3, CP-6, CP-7 and a qualified native Grok launch stays owed. Local `127.0.0.1:17202` was not up, so this review did not pin a runner.

## Tests and scratch mutants

CP-2 and CP-6 drive production `RunnerGrokAdapter.WaitForReadyAsync` / `GrokReadyWait` and `GrokStartupCaptureStore` with scripted frames and `PollGateClock`. They are not a reimplementation.

Checkpoint tool run `20261122-024247-9d52`, one build per project, native cold outputs deleted by the tool, verdict GREEN, exit 0, unlisted none, 171 passed, 0 failed. CP-1 was not re-run: it is the historical red row at base production, and R-1's cases are inside CP-2 and CP-6, which passed. Host load at the start of later checks was below 14 (evidence-diff 4.06, index 3.44, land-graph 9.01). No REFUSING line was printed.

Scratch mutants, both restored, neither committed:

- Ceiling: removed the `deadline >= workingCeiling` return and the clamp inside `ExtendWorking`. `C2047_Continuous_working_stops_at_the_ceiling` failed 1/1 with "Working must stop at the ceiling" (lease `a03ea388-e056-4e59-aa11-b40c2fa1ed78`, held 325s, exit 2). Restored before the second mutant.
- Retry key: dropped `firstWindow is null` from the trust-write condition. `C2047_Retry_exhaustion_captures_both_windows_without_input(True)` failed because `client.Writes` was `["y"]`; the False argument passed (total 2, failed 1, succeeded 1, lease `71fa69f8-2bd5-4c89-abc4-050861057113`, held 33s, exit 2). Restored with `git checkout` of `src/Antiphon.Agents.Pty/GrokStartupReadiness.cs`. `git diff` was empty and HEAD was still `d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040` before the generated checks.

PC-1 through PC-11 stay pending. Mutation checks are waived for this round (operator 2026-11-12). These two scratches are review evidence, not that SourceLanding pass.

## Generated checks

- Derived index, twice: build of `scripts/regenerate-derived-impact-index` through build-slot lease `d924da17-1484-4bdc-98c0-da371e975341`, then `-NoBuild`. Both printed `derived-impact windowsOnly count=31 linuxOnly count=1` and `derived-impact-index unchanged`. The committed index was not edited.
- `scripts/antiphon evidence-diff --base 611df2dbc9606154d9490cdf12d46c55879f0bff --head d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040`: `commits=11 entries=0 violations=0`, exit 0 (lease `b1c30076-6d04-4698-8bb0-c61d13a1593f`).
- `scripts/audit-land-verification-graph.ps1 -Check`: `approved`, digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80, exit 0 (lease `46a83ddb-7ead-4c78-abe5-984d4c48445d`). The tool DLL was missing and the script rebuilt it. Not `-Update`.
- `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`: exit 0, tree `8a0b97b949160dc42c8349153ec890497ca31ace`. Merge-base remains `611df2dbc9606154d9490cdf12d46c55879f0bff`. Not merged.

`impacted-classes.ps1 -Range 611df2db..d93ea8ba -Json` kinds are `pty-host-fixture` (`Card1277PtyHostFixtureTests`, executed in CP-7, 3/3) and `test-lane-category` (`TestLaneCategoryGuardTests`, inside CP-4, 84/84 for the nine-class union). `AgentRegistrySettings.cs` is owed/capped at 127. Plan V-4 closes the named classes, so that cap was not executed. The Windows filter from the selector stays with R-2.

## Advisories

These do not hold the land. File them as cards if wanted.

- Failed delete and the byte total. If an older `Delete` throws, `Prune` does not subtract that file's bytes and continues. While the running total stays above 50 MiB it can delete the capture just written, and the warning then names that path. Successful deletes stop before the newest name. This shows up when delete fails (a Windows lock or access denial is the realistic case). Linux unlink of a regular file in that directory usually succeeds. There is no test for a throwing delete.
- No symlink test. Production skips `FileAttributes.ReparsePoint` before length and delete, so a capture-named link is not unlinked. `Directory.CreateDirectory` and `EnumerateFiles` follow a directory symlink. Only names that parse as captures are eligible.

## Still owed, not blockers

- R-2 Windows: CP-2, CP-3, CP-6, CP-7 on a Windows runner from this source, then one qualified native Grok launch with first input held until Ready.
- PC-1 through PC-11, method-scoped, for SourceLanding Mutation.
- Activation needs a server restart. This review does not restart anything. A land is publication, not activation.

## Checkpoint lines

```
CHECKPOINT CP-2 commit=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 build=ok filter=/*/*/(GrokStartupCaptureStoreTests*)|(GrokStartupReadinessTests*)|(RunnerGrokAdapterSignInPromptTests*)|(RunnerGrokAdapterTrustPromptTests*)|(GrokLinuxBlockingPromptTests*)/* executed=27 passed=27 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261122-024247-9d52/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 build=reused filter=/*/*/(GrokStartupReadyOrderingTests*)|(Card1619ExitConsumerTests*)/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261122-024247-9d52/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261122-024247-9d52/rows/CP-4/run.trx slot=granted waited=15s dirty=0 source=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 build=reused filter=/*/*/RunnerGrokAdapterReadyTests/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261122-024247-9d52/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 build=reused filter=/*/*/Card1277PtyHostFixtureTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261122-024247-9d52/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 build=ok filter=/*/*/(Card1619RunnerExitTests*)|(GrokRulesStoreFailureTests*)/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261122-024247-9d52/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=d93ea8ba2fdeb6c1bbbc3c113b652c37cefc5040 sourceState=clean buildSource=verified
```
