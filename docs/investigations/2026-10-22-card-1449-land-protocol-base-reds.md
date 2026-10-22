# CARD-1449 / CARD-1447: land-protocol base reds on Linux server2

Investigate task 1a07bb3b, 2026-10-22, runner mirror on server2 (Linux). Narrow
method filters only, no Unit lane. File:line citations are at this branch tip
cc3e54fba56ea35796228f6464b8f6d0876ab8ac; master 1ed603052 differs on the cited
files only in `ControlledLandingGit.cs` (+31, CARD-1421 lane), the RR test's
limiter attribute (2 lines) and `LandingGit.cs` (CARD-1436 census seam, 6 lines),
none on the lines cited.

## Verdicts

| Cases | First failing commit | rc/2026-10-20 tip ceb6e3d6b | Verdict |
|---|---|---|---|
| `LandingProtocolHarnessTests.C475_LandingKeepsLeaseThroughProtocol` x4 (Fresh, AlreadyPresent, ResumePublication, CleanupRetry) | `e4da91c654f8d52b3a2c6fe5bbe6c9698d2221b1` feat(landing): retain owned verification generations under execution lock (2026-10-20; in master and in rc) | red, 4/4 | **Stale test.** The test counts common-directory queries and probes the lease at query two. The land-execution lock added by this commit resolves the common directory once before the mutation lease is taken, so query two is now the mutation lease's own resolution, which runs before `landing.lock` opens. The lease is held at the protocol's first query as before. |
| `AgentTaskLandPublicationTests.C688_LandNeverRunsGitInTheSourceWorktree` | `f612cbe6078e4fbbd18b124984be490ab6df196e` feat: release repository lease for correlated landing verification (CARD-1379, 2026-10-20); rc carries it as cherry-pick `20c38faa84a6c370c745a997f3f0593515216bc5` | red, 1/1 | **Deliberate contract change, test encodes the superseded CARD-0688 D-2 rule.** After the verification window the protocol validates the ticket in both the land worktree and the source (task) worktree: index-lock probe, sequencer probe (`rev-parse --absolute-git-dir`) and `status --porcelain=v1 -z --untracked-files=no`. The commit documents this ("validates ... tracked cleanliness, locks, sequencers" in `docs/orchestration-loop.md`). One product caveat below. |
| `AgentTaskLandRefusedRetryTests` RR_V1, RR_V3, RR_V4_RemoteReadFailure, RR_V4_TargetStatusFailure(True/False), RR_V6_Preparation(destination/target-commit), RR_V6_ReplacementSave(False/True), 8 cases | same `f612cbe60` / rc `20c38faa8` | red, 8/10 | **Product regression (reporting), low severity.** Every fresh land that goes through the verification window re-enters the protocol with its own operation already Prepared, and the re-entry branch relabels it `Mode = ResumePublication`. The durable `AgentTaskLanding.Mode`, the terminal event detail `mode=...` and the monitor's `LandingMode` now say ResumePublication for an ordinary fresh land. No behavioural branch depends on Fresh vs ResumePublication (only CleanupRetry is tested), so the effect found is misreporting. |

The "Linux" qualifier on both cards is incidental: no platform-conditional code is
on any of the three paths. Windows was not run.

## Runs

All runs: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <x> -- dotnet exec
tests/Antiphon.Tests/bin-<x>/Antiphon.Tests.dll --treenode-filter <f>`, slot
granted every time. Builds: `dotnet build tests/Antiphon.Tests
--property:OutputPath=bin-i1449/ --property:UseAppHost=false -nodeReuse:false`
in detached worktrees (removed afterwards). Filters:

- C688: `/*/*/AgentTaskLandPublicationTests/C688_LandNeverRunsGitInTheSourceWorktree`
- RR: `/*/*/AgentTaskLandRefusedRetryTests/(RR_V1*)|(RR_V3*)|(RR_V4*)|(RR_V6*)` (10 cases; the 2 green in every run are RR_V4_TargetStatusFailureRetriesFromFreshEvidence(False) and RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut, so the card's "both cases" for RR_V4_TargetStatusFailure is not reproduced: only the True case fails)
- C475: `/*/*/LandingProtocolHarnessTests/C475_LandingKeepsLeaseThroughProtocol`

| Commit | Build | C688 | RR (10) | C475 (4) |
|---|---|---|---|---|
| `55b70676132227b7473a8bb030b109d30132a0e6` CARD-1432 base (prebuilt `/tmp/antiphon-checkpoints-baseline/20261022-005506-10f4/baseline-c1432-s2`, bin-c1432-s2) | reused | 1 failed | 8 failed | 4 failed |
| `ceb6e3d6b78471db6831258699c493b8eb2d619b` rc/2026-10-20 tip (live AppHost line) | 3m28s | 1 failed | 8 failed | 4 failed |
| `ed0f6663f5d5f3ee20b49b7ac462e7dade243dfc` = f612cbe60^ | 4m26s | 0 failed | 0 failed | 4 failed |
| `f612cbe6078e4fbbd18b124984be490ab6df196e` | 2m28s | 1 failed | 8 failed | 4 failed |
| `e4da91c654f8d52b3a2c6fe5bbe6c9698d2221b1` | 3m25s | not run | not run | 4 failed |
| `182603100aa36e4f4641b9866ecb96d3a949ef00` = e4da91c65^ | 2m44s | not run | not run | 0 failed |

Identical assertion text at every red commit:

```
C688: inSource.Count(c => c == "rev-parse --absolute-git-dir") should be 1 but was 2
      rev-parse --path-format=absolute --git-path index.lock | rev-parse --absolute-git-dir |
      rev-parse --path-format=absolute --git-path index.lock | rev-parse --absolute-git-dir |
      status --porcelain=v1 -z --untracked-files=no
RR_*: b.Id.ShouldNotBe(A.Id); b.Mode should be LandOperationMode.Fresh but was LandOperationMode.ResumePublication
C475: entryAcquired should be False but was True (the first protocol query must still hold the service lease)
```

Raw logs stayed in the session scratchpad (not committed): `base-*.log`, `rc-*.log`,
`pre-*.log`, `f612-*.log`, `e4-c475.log`, `e4pre-c475.log`.

## Mechanisms

### C475 (CARD-1447)

- The test hooks `ControlledLandingGit.CommonDirectoryAsync`
  (`tests/Antiphon.Tests/TestHelpers/ControlledLandingGit.cs:207-213`) and, on the
  second query only, tries a contending `IRepositoryMutationLease.TryAcquireAsync`
  (`tests/Antiphon.Tests/Application/LandingProtocolHarnessTests.cs:199-211`); the
  comment there states "query one acquires the service lease, query two is the
  protocol's first query, before Owns". Assertion at line 220.
- Since e4da91c65 the land takes the execution lock first:
  `server/Application/Services/AgentTaskLandService.cs:478` calls
  `_executionLeases.TryAcquireAsync`, and `LandExecutionLease.TryAcquireAsync`
  resolves the common directory (`server/Infrastructure/Git/LandExecutionLease.cs:10`),
  query one. The harness passes no execution lease, so the service builds the real
  one from the fixture git (`AgentTaskLandService.cs:60-61`).
- Query two is now the mutation lease's own resolution in
  `RepositoryMutationLease.AcquireAsync` (`server/Infrastructure/Git/RepositoryMutationLease.cs:48`),
  which runs before the `landing.lock` `FileStream` opens (lines 55-56). The hook's
  contender therefore acquires and releases the lock before the land does, and
  `entryAcquired` is true. The protocol's own query, `AgentTaskLandingProtocol.cs:74`,
  followed by `Owns` at line 75, is now query three and still runs under the lease.
- The remaining assertions (both guarded-cleanup inspections under the lease) are
  never reached. Nothing in the land holds the lease for less time than before.
- Note for the next stage: in production `YieldToDispatchAsync` adds yet another
  pre-lease query when lease waiters exist (`AgentTaskLandService.cs:1556`); it is
  skipped in the harness because `CreateLand` passes no `RepositoryLeaseWaiters`
  (`tests/Antiphon.Tests/TestHelpers/LandingProtocolHarness.cs:138-146`). Any
  repair that keeps an absolute query index stays fragile.

### C688 (CARD-1449)

- Assertions: `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs:551-554`
  allow, in the source worktree before cleanup, exactly one
  `rev-parse --absolute-git-dir` plus index-lock probes (CARD-0688 D-2 and D-10,
  plan `docs/superpowers/plans/2026-09-25-card-0688-dedicated-land-worktree-plan.md`).
  Line 556 (`Inspections` == 2) was not reached.
- f612cbe60 routes a windowed land through `PrepareAsync` -> window -> `ResumeAsync`
  (`AgentTaskLandService.cs:651-687`). On resume `ValidateTicketAsync` validates the
  ticket in every checkout the ticket names
  (`server/Application/Services/AgentTaskLandingProtocol.cs:626-632`): the land
  worktree and, when `SourceCanonical` is non-null, the source worktree.
  `SourceCanonical` is captured at line 592 whenever the source directory exists.
- Per checkout the loop runs `RequireNoIndexLockAsync` (lines 914-922, the
  `rev-parse --path-format=absolute --git-path index.lock` probe at
  `server/Infrastructure/Git/LandingGit.cs:926` region), then
  `HasActiveSequencerAsync`, which runs `rev-parse --absolute-git-dir`
  (`LandingGit.cs:368-370`), then `status --porcelain=v1 -z --untracked-files=no`
  (`AgentTaskLandingProtocol.cs:632`). That is exactly the second triple in the
  failing trace. The first pair is the admission probe and the D-2 read the test
  already allows.
- The commit's own docs (`docs/orchestration-loop.md`, CARD-1379 paragraphs added by
  f612cbe60) state that after the window the protocol validates "checkout
  identities, tracked cleanliness, locks, sequencers, pins, source". The live docs
  no longer carry the D-2 "read once, nothing inspects" wording. CARD-1384
  (`03686bed472b2ddc9d363562c515ec8c51dd831c`) re-budgeted the sibling
  `C688_RealLandProfile` for the new inventory but did not touch this test.
- Product caveat (fact, not a verdict): the source worktree is a live task
  checkout, which is why D-2 excluded it. `git status` there runs without
  `--no-optional-locks`, so it may take `index.lock` and refresh the index in a
  worktree an agent is using. The two probes before it are read-only.

### RR_V1/V3/V4/V6 (CARD-1449)

- Assertion: `AgentTaskLandRefusedRetryTests.cs:597` inside `AssertReplacementAsync`
  (`b.Mode.ShouldBe(LandOperationMode.Fresh)` for the replacement operation B after
  refusal A). Green at ed0f6663f, red at f612cbe60.
- Before f612cbe60 one `RunAsync` pass created B (Fresh, `LandOperationFactory`) and
  carried it to publication. Since f612cbe60 the service calls
  `PrepareAsync` (`AgentTaskLandService.cs:651`), releases R, verifies, reacquires and
  calls `ResumeAsync` (line 687), which is `RunCoreAsync` with a ticket
  (`AgentTaskLandingProtocol.cs:53`).
- On that second pass `op` is B itself, already Prepared and source-pinned, so the
  "existing operation" branch at `AgentTaskLandingProtocol.cs:190-192`
  (`if (op.SourcePinned || op.Phase != LandPhase.Inspected) op.Mode = ResumePublication;`)
  relabels it and `SaveAsync(op)` persists the new mode. The branch predates
  CARD-1379 and was written for an operation found mid-flight after a restart.
- Consumers of `Mode`: `AgentTaskLandService.cs:724` and `:754` test only
  `CleanupRetry`; `:803` writes `mode={op.Mode}` into the terminal event detail;
  `:1447` copies it to `terminal.LandingMode`; `AgentTaskLandMonitorService.cs:97`
  reports it as `LandingMode`. So the regression is the durable label and the
  operator-facing reports, not publication behaviour. The two green cases, RR_V4_TargetStatusFailure(False) (B refused again before
  preparation) and RR_V4_RemoteContainment (already-present shortcut), never reach
  a windowed fresh land.
- The 8 cases stop at line 597; later assertions in `AssertReplacementAsync` and
  `AssertSuccessAsync` are unverified against the windowed flow.

## Remaining uncertainties

- RR: assertions after line 597 untested with a corrected mode; a repair may
  surface further inventory differences (C688_RealLandProfile needed a re-budget).
- C688: `Inspections == 2` (line 556) not reached; the source `status` may also
  change the profile's inspection count.
- Mode consumers were found by grep (`.Mode` on landing operations); client or
  tracker readers of `LandingMode` were not audited.
- Master tip 1ed603052 was not built; 55b706761 contains both first-bad commits and
  the later master commits do not touch the cited lines.
- Windows not run.

## Not done, noted

- C475: anchor the contender probe on the protocol's own query (the one followed
  by `Owns`) instead of an absolute query index, or count from the lease acquire.
- C688: either re-budget the test to the CARD-1379 inventory (as CARD-1384 did for
  the profile test) or make the post-window source validation lock-free
  (`--no-optional-locks`, or drop `status` in the source); that choice is a design
  decision for the next stage, not made here.
- RR: skip the `Mode = ResumePublication` relabel on the ticketed resume pass
  (`ticket is not null`) so a fresh land keeps `Fresh`.

## Housekeeping

Five detached worktrees under the session scratchpad and their `bin-i1449/`
outputs were removed. The prebuilt CARD-1432 baseline worktree was used
read-only; the `TestResults/` HTML report my run wrote under its
`bin-c1432-s2/` was deleted again.
