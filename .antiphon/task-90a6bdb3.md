# CARD-1461 option C Final Review (task 90a6bdb3) of Code task 75c03be9

Outcome: clean. Default path unchanged, fallback capped and logged at Warning, all four witnesses assert behaviour, receipt valid at the reviewed SHA.

Subject: `feat/card-task-75c03be9` at `6fe66110f46e9cbbc31a1287a44710fc69eab433` (base `cbb297f8f`). The run was at the reviewed tip, not at `6dacaed6b`. The two differ only in the runs record and the `.antiphon/task-75c03be9.md` report, so the receipt is bound to the SHA that lands.

## Receipt (checkpoint tool, server2, Linux)

Run `20261022-082230-1bae`, closed list CP-1..CP-3 from `docs/superpowers/plans/2026-10-22-card-1461-code-runs.md`, `unlisted: none`, verdict GREEN exit=0:

```
CHECKPOINT CP-1 commit=6fe66110f46e9cbbc31a1287a44710fc69eab433 build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/(A_memory_floor_*)|(A_busy_answer_restarts_*)|(A_reachable_memory_floor_*) executed=4 passed=4 failed=0 skipped=0 ... slot=granted waited=0s dirty=0 source=6fe66110f46e9cbbc31a1287a44710fc69eab433 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=6fe66110f46e9cbbc31a1287a44710fc69eab433 build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/* executed=10 passed=10 failed=0 skipped=0 ... slot=granted waited=0s dirty=0 source=6fe66110f46e9cbbc31a1287a44710fc69eab433 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=6fe66110f46e9cbbc31a1287a44710fc69eab433 build=ok filter=/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/* executed=10 passed=10 failed=0 skipped=0 ... slot=granted waited=0s dirty=0 source=6fe66110f46e9cbbc31a1287a44710fc69eab433 sourceState=clean buildSource=verified
```

- `validate --rows CP-1,CP-2,CP-3` and `scripts/validate-checkpoint-receipt.ps1 -Rows CP-1,CP-2,CP-3` both printed `CHECKPOINT SOURCE VALID source=6fe66110f... rows=3` (exit 0).
- The CP-3 TRX names all ten methods: the four new ones and the six existing ones.
- Unlisted runs:
  - One tool build, `dotnet build tools/Antiphon.Checkpoints` to `bin-rv1461tool/`, through `build-slot.ps1` (granted after 105 s). It was needed to run start, wait and validate.
  - One mutation run, below.
  - Both `bin-*` outputs were deleted. The worktree is clean.
- `scripts/check-evidence-diff.ps1 -BaseRef cbb297f8f -HeadRef HEAD`: `commits=6 entries=1 violations=0`.

## Throwaway mutation (PC-3)

Setup: a scratch detached worktree at `6fe66110f`. The window comparison `elapsed - floorSince >= fallback` was changed to `>`, and the CP-1 filter was run through `build-slot.ps1` (granted, 0 s wait).

Result: `total: 4 failed: 3 succeeded: 1`.
- V-1, V-2 and V-3 each failed with `TimeoutException`: the fallback never fires at the window edge, so `WaitAsync(5 s)` expires.
- V-4 passed, as a guard should.

The worktree was removed. PC-1..PC-n remain pending for SourceLanding Mutation.

## Checks

1. **Default off is unchanged.**
   - With `fallback == 0`, the new branch is guarded out by `fallback > TimeSpan.Zero` (`SessionRunnerBuildSlotGate.cs:96`).
   - `floorSince` is written only inside the busy and unreachable branches, and nothing reads it when the guard is off.
   - The timeout check (`:73`), the busy/floor/unreachable lines, the `RetryAfter` and `remaining` delays, the return values and `ReleaseAsync` are untouched.
   - The six existing tests ran unchanged and green. The Fixture refactor (`Time` and `Settings` properties) keeps the old values exactly (`BuildSlotWaitMinutes=30`, `BuildSlotUnreachableGraceSeconds=0`, a fresh `FakeTimeProvider`).
   - `Landing` is bound from configuration (`Program.cs:482`). No appsettings carries the new keys, so the shipped default is off.
2. **Semantics match D-2..D-6.**
   - The window starts at 0. A busy answer (`:82`) and a null/unreachable answer (`:110`) restart it.
   - The timeout check stays first, so a window at or above the wait never fires.
   - Only a typed 409 `build_slot_memory_floor` produces a `MemoryFloor` answer. `SessionRunnerHttpClient.AcquireBuildSlotAsync` maps every other status, problem type or exception to null, which goes to the unreachable path. A disabled broker answers `unlimited`. Neither of those can reach the fallback.
   - Time comes from the injected `TimeProvider`.
   - The verifier's `finally` does call `ReleaseAsync` for the unleased hold. The gate returns before any runner call unless the outcome is `Granted` with a lease (`:126`), so nothing is released and nothing leaks. V-2 asserts `ReleasedBuildSlots` is empty and there is no `release` timeline entry.
   - The verifier passes `-maxcpucount:N` only when `MaxCpuCount > 0` (`AgentTaskLandService.cs:1845`). That is why the clamp to at least 1 matters, and it is in place (`:40`).
3. **Safety.**
   - The fallback build is capped: default 2, with no path to 0.
   - Negative minutes clamp to 0 (off).
   - A huge minutes value is no overflow risk; it simply never fires before the 30-minute timeout.
   - There is no upper bound on `MaxCpuCount`, but that value is operator-owned (disclosure 2).
   - Visibility: every fallback logs at Warning (`BUILD SLOT unleased reason=memory_floor_fallback ...`). The verifier observer also logs the line at Information with the task, operation and request ids. That is enough to rule out a masked outage with no visibility (disclosure 1).
4. **Tests.**
   - V-1, V-2 and V-3 assert the outcome, the cpu count, the line and the absence of a timeout.
   - V-4 pins the default and the timeout.
   - The red record (3 failed with `TimeoutException`, 1 passed) matches the stated reason. My PC-3 mutation reproduced the same shape.
   - The CP-1 filter correction is legitimate: V-4's method starts with `A_reachable_`, and adding that operand is the only change from the plan's table.
   - Fake time is advanced from the test body. The helper waits at most 5 s of real time for each poll and never blocks on the gate, so the tests cannot deadlock. A lagging poll would show up as red, never as a false green.
5. **Docs.**
   - `testing-and-build.md:891` and the `orchestration-loop.md` pointer line are accurate. They describe the setting as default off, with the 6144 floor and `Landing:BuildSlotWaitMinutes` unchanged.
   - The phrase "nothing to release" is accurate in effect (see check 2).

## Landability

- Master: `git merge-tree --write-tree origin/master HEAD` is clean, with master at `a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49` (later than `bb43ec352`).
- rc: I chained `merge-tree --merge-base <c>^` for `2fc898ef5`, `4b4aa96a0` and `6dacaed6b` onto `origin/rc/2026-10-20` (`ceb6e3d6b78471db6831258699c493b8eb2d619b`).
  - All three are clean.
  - The resulting `LandingSettings.cs`, `SessionRunnerBuildSlotGate.cs` and `LandVerificationBuildSlotTests.cs` blobs are byte-identical to the subject's.
  - The docs hunks are identical. The runs record is not part of the pick (D-8).
  - Nothing was written to rc.

## Disclosures (none is a defect under the standing verdict policy)

1. **The fallback is visible only in the server log.** The land's verification result still reads `build OK, tests n/n`. Nothing marks the fallback on the land operation, its events or the attention feed, so an operator has to grep the log for `memory_floor_fallback`.
2. **No upper bound on `BuildSlotMemoryFloorFallbackMaxCpuCount`.** An operator value like 64 is effectively uncapped. The selected-test `dotnet run --no-build` is not cpu-capped on any path; that predates this change.
3. **Untested branches.** Mutating any of these would leave the suite green:
   - the clamp of 0 or a negative count to 1;
   - the restart of the window on a missed (unreachable) answer;
   - a window at or above the wait never firing.
4. **Two fallbacks at once are not ruled out host-wide.** Lands are serialised per repository lease, but lands in different repositories, or deferred-verification tickets (`VerifyPreparedAsync`), could each fall back at the same time. Each one is capped.

Backlog suggestion (one): record the fallback on the land operation (for example in `VerificationCommand` or as a land event) and add witnesses for the clamp and the missed-answer restart.
