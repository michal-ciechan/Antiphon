# CARD-1623: land verification builds versus the Windows qualification run

Investigate task 19a14614, 2026-11-04. Base master `1ef88240b3c5ee2c759db958e5fb0b635b1248cc`.
Read-only. Nothing ran on the desktop and nothing was built. Evidence comes from three places: the
source at the base; read-only SQL against the desktop dev Postgres (`BEGIN READ ONLY`, from
server2); and `git log origin/master`. CARD-1625's investigation (`origin/feat/card-task-f7703a17`,
`62f4aaf99`) was read. Its operator decision between (a) and (e) is still pending, and this report
does not make it.

## Verdict

1. **No desktop land verification build ran during either run in question.** The desktop land
   service stopped verifying builds at 2026-11-01 11:13Z. `AgentTaskLandings`: the last
   `VerificationStartedAt` is 11-01 11:13:18Z and the last `UpdatedAt` is 11-01 11:21:43Z.
   `AgentTaskLandRequests`: the last `RequestedAt` is 11-01 11:13Z. Since 11-01 14:39Z, master
   has moved only through **`Merge batch N` tasks on server2**: 74 tasks on `server2`, 6 on
   `server2-temp` and 1 on the desktop (the 11-01 07:39 conflict resolution). They cherry-pick,
   build and test on Linux, then push master as a fast-forward. Both run windows, 0b05506b
   (11-03 16:36Z to 11-04 10:13Z) and 766e3df5 (from 10:18Z), fall entirely inside this period.
   The failures in groups 125/128/77/79/96/114 and the land E2E family **cannot have coincided
   with a production land verification build**, because there was none. If those failures came
   from host contention, it came from something else (section 3).
2. The harm described on the card was real while lands went through the desktop land service. On
   10-30 and 10-31 that service verified 11 lands beside qualification d47848cc. Those builds took
   **698-1,603 s (mean 1,015 s)**. The same build on 11-01 took **457-499 s** (section 2.3).
3. **Recommendation: (1), throttle when the native lock is held, as a small dormant slice.**
   Reject (2): it would block ~30-45 lands during a 30-hour run. Defer (3): the server2 Merge
   batches already do (3) in practice, outside the land service. (1) and CARD-1625 (e) are
   independent: (1) changes scheduling, (e) changes the GC environment. They meet at the same
   start-info seam (section 4).

## 1. Where the verifier build starts, and how each proposal would be built

### 1.1 Launch path (base `1ef88240b`)

| Step | Location | Notes |
|---|---|---|
| Skip or build decision | `server/Application/Services/AgentTaskLandingProtocol.cs:314-344`; `LandVerificationPolicy.cs:9-27` | Git-only (`diff-tree`/`ls-tree` against `LandVerificationBuildGraph.Fingerprint`). A docs-only diff skips. Otherwise `VerificationCommand = "dotnet build; dotnet run --project tests/Antiphon.Tests; policy=<reason>"` |
| Verifier | `server/Infrastructure/Git/LandingVerifier.cs:13-43` (DI `server/Program.cs:514`) | Prepares the reusable artifacts generation, then calls `VerifyWithObserverAsync` |
| Build slot | `AgentTaskLandService.cs:1904-1939` | `buildSlots.AcquireAsync("land-verify <worktree>")` against the desktop runner broker. `Landing:BuildSlotWaitMinutes` 30, memory-floor fallback at `-maxcpucount:2` (`LandingSettings.cs:16,32,41`) |
| Arguments | `AgentTaskLandService.cs:1947-1962` | `dotnet build --artifacts-path <out> --configuration Debug -maxcpucount:<slot.MaxCpuCount>`, where the grant is 4 (`src/Antiphon.SessionRunner/BuildSlotSettings.cs:19`). Then, when a filter is set, `dotnet run --project tests/Antiphon.Tests --no-build ...` |
| Process start | `AgentTaskLandService.cs:1980-2006` (`RunProcessAsync`) | A plain `ProcessStartInfo`. `BuildDriverEnvironment.Apply(start)` at `:1985` is the CARD-1506 policy that CARD-1625 is about. **No priority, affinity or job object is set anywhere on this path.** A grep for `PriorityClass`/`BelowNormal` in `server/` finds nothing |
| Seams already present | `LandProcessRunner` delegate (`:1902`, the V-8 fake). `IBuildSlotGate` fakes (`FakeSessionRunnerClient.BuildSlotAcquire`, used by `LandVerificationBuildSlotTests`) | Tests can check the arguments without building anything |

### 1.2 How a server would detect the native lock

The lock is `C:\Antiphon\verification\native-run.lock`. Its root can be changed with
`-CoordinationRoot`, which tests use (`scripts/lib/release-gate.ps1:19,310-314`). How it works:

- **Acquire:** `Enter-ReleaseGateNativeLock` (`release-gate.ps1:385-447`) calls `File.Open(...,
  CreateNew, Write, FileShare.Read)`, writes ASCII compact JSON and **closes the handle at once**,
  so the file can be read while the run is live. The record is `{pid, processStartedAt (ISO 'o'
  UTC), runId, lane, continuationId, acquiredAt}` (`New-ReleaseGateLockOwner`, `:366-383`).
- **Holder:** the outer `nightly-run.ps1` process (`scripts/lib/nightly-run-impl.ps1:444`). A
  self-reexec hop does not rewrite the record. The child proves it inherited the lock: the parent
  is alive and pid, start, run and lane all match (`:402-419`). The recorded pid therefore stays
  alive for the whole run. Both lanes take the same lock: rc qualifications (through
  `scripts/qualification/launch-rc.ps1`) and the master nightly.
- **Release:** `Exit-ReleaseGateNativeLock` (`:449-468`) deletes the file in `finally`
  (`nightly-run-impl.ps1:853`). A crash leaves it behind, and it is **never reclaimed
  automatically** (`docs/testing-and-build.md:1414`). A file that exists is therefore not proof
  of a live run.
- **Owner check a server needs:** read with `FileShare.ReadWrite`, as `Read-NightlyLockRecord`
  does (`scripts/lib/nightly-common.ps1:545-575`, which explains the Windows sharing-violation
  reason). Parse the JSON. "Held" means `Process.GetProcessById(pid)` is alive **and** its
  `StartTime.ToUniversalTime()` is within 1 s of `processStartedAt`. That is the
  `launch-rc.ps1:46-53` rule. The contender path in `release-gate.ps1:440-443` checks only that the
  pid is alive, which is too weak against pid reuse. A missing file means not held. A file that is
  present but unreadable or malformed should count as **held**: throttling is the safe direction
  and only slows a land.
- **No C# code reads this lock today.** A grep for `native-run`/`CoordinationRoot` in `server/`
  and `src/` finds nothing.
- **The build-slot broker is not a usable signal.** `launch-rc.ps1:64` wraps the entire
  `nightly-run.ps1` in one `build-slot.ps1` lease. The broker sets `ExpiresAtUtc = granted + 90
  min` (`BuildSlotBroker.cs:116`). `Renew` updates only `LastRenewedAtUtc` (`:145-154`), and the
  sweep reaps any lease at `ExpiresAtUtc` (`:196-197`). So the qualification shows as a lease for
  its first 90 minutes only. After that the broker sees no qualification, and both desktop slots
  (`MaxConcurrent` 2) are open to other builds.

### 1.3 Proposal implementations (exact files)

| Proposal | Code change |
|---|---|
| **(1) Below-normal priority and a CPU cap while the lock is held** | New `INativeRunLockProbe` in `server/Application/Interfaces/` and its implementation in `server/Infrastructure/Files/NativeRunLockProbe.cs` (section 1.2 rules), registered beside `server/Program.cs:514`. New `LandingSettings` keys: `NativeRunLockPath` (default the Windows path, empty disables), `NativeRunLockMaxCpuCount` (2) and `NativeRunLockBelowNormalPriority` (true), so rollback is a configuration change. `LandingVerifier` gets the probe and passes one probe result into `VerifyWithObserverAsync`. `VerifyUnderSlotAsync` (`:1950`) uses `min(slot.MaxCpuCount, NativeRunLockMaxCpuCount)` when the lock is held. `RunProcessAsync` (`:1987`) sets `process.PriorityClass = BelowNormal` right after `Process.Start`, before `StartedAsync`. On Windows, processes created afterwards (the MSBuild nodes, each `csc`, the test host) inherit BELOW_NORMAL. A `BUILD SLOT native-lock-held runId=.. lane=.. maxcpucount=2 priority=BelowNormal` line goes to the observer, and a note goes on `VerificationCommand` beside `BuildSlotNote`, so the land record shows the throttle |
| (2) Defer while the lock is held | The same probe, plus a new hold reason in the land request state machine (`AgentTaskLandRequests.HoldReasonCode`) that is re-evaluated every tick. Lands are serialised under the land-execution and repository lease, so one deferred land blocks the whole queue |
| (3) Verify on server2 | A new runner verification API (build, TRX, result). Before publication the rebased SHA exists only in the desktop land worktree (`.git/antiphon/land...`), so it would first need a push to a scratch ref, which is a new outward write. The result would also have to be bound back to `VerifiedSourceSha`. The skip and digest decision (`LandVerificationPolicy`) is Git-only and would stay on the server, so the graph digest would be identical by construction. That is a large change across the server, runner contracts and runner |

## 2. What the landing flow does today during a qualification

### 2.1 Behaviour

The land service knows nothing about qualification. A land requested while a run holds the native
lock acquires a build slot and builds at the grant's `-maxcpucount:4`, at normal priority, with
the CARD-1506 environment. In the run's first 90 minutes it shares the two desktop slots with the
run's own lease. After that the run's lease has been reaped (1.2), so the land does not wait on the
run at all. The only thing that can hold it back is the 6,144 MB memory floor, followed by the
fallback at `-maxcpucount:2` after a wait.

### 2.2 How master actually moves now (DB and git)

| Period | Path | Count |
|---|---|---|
| 10-30 00:00Z to 11-01 11:13Z | Desktop land service (`AgentTaskLandings`, `C:\src\Antiphon`) | 19 operations, 18 verified builds |
| 11-01 14:39Z onward | `Merge batch N` tasks (`Role=10`) on server2: cherry-pick, Linux build and guard tests, fast-forward push | Completed: 11-02: 17, 11-03: 25, 11-04: 21 (to 17:05Z) |
| Run 0b05506b (17.6 h) | Merge tasks completed inside the window | **17** (batches 37-53, mean 24 min, max 75 min). 131 master commits in 24 push clusters more than 120 s apart |
| Run 766e3df5 (10:18Z to 17:40Z, 7.4 h) | Same | **11** (batches 54-64, mean 19 min). 64 commits in 14 clusters |
| Desktop land service during either run | | **0** |

That is about 1 land per hour in run 0b05506b and about 1.5 per hour in run 766e3df5.

### 2.3 Desktop land verification time, beside a run and without one (`AgentTaskLandings`)

| Lands | Context | `VerifiedAt - VerificationStartedAt` |
|---|---|---|
| 10-30 08:34Z to 10-31 09:59Z (9) | Beside d47848cc: its test assemblies were built 10-30 08:27-08:33 (card) | 698, 871, 1016, 1220, 876, 1001, 790, 1603, 1061 s: **mean 1,015 s** |
| 10-31 19:28Z to 11-01 11:13Z (7) | Qualification state not established here | 532, 483, 499, 476, 492, 458, 457 s: **mean 485 s** |

These numbers agree with cb60ed48: ~420-550 s on a quiet desktop and 750-1,566 s beside a run.
They are **land-side** costs. The run-side cost (stretched silent gaps) was never measured.

## 3. Evidence of harm

- **Run 766e3df5, groups 125/128/77/79/96/114, and run 0b05506b's land E2E family:** no production
  land verification build overlapped them (Verdict 1). That rules out the land verifier as their
  cause, from stored rows. The land E2E family runs its **own** fixture verifier, a small owned
  project built under the fixture's private runner (CARD-1625 2.4, da743107). That build is part
  of the test, not CARD-1623 contention.
- **Other desktop CPU sources during the windows, not ruled out.** Desktop-dispatched tasks:
  - `Analyse the pace of Windows run 0b05506b` (11-03 19:28-19:35), read-only;
  - `Debug retire-temp preview failures` (11-03 20:01-20:15);
  - `Deploy slides-server2` (11-03 19:51-21:45, `C:\src\slides`);
  - `Stop run 0b05506b, seed from it, restart rc` (11-04 10:10-10:35). It overlaps the start of
    766e3df5 at 10:18Z, and any AppHost restart build it ran would also overlap it;
  - the Distill sessions.

  Also, once the 90-minute reap has freed the run's lease, the desktop broker grants leases to any
  local driver.
- **Evidence needed to attribute a failure to contention.** All of it lives on the desktop.
  1. The run's per-chunk start and end times and the failing tests' timestamps (the run directory
     under `C:\Antiphon\nightly`, chunk TRX files and `dispatch-schedule.json`).
  2. The desktop session-runner log lines `Build slot lease ... granted to {Label}`, `released`
     and `reaped: ttl 90m expired` (`BuildSlotBroker.cs:120,139,207`) for both windows. These
     show every leased build beside the run.
  3. `server-logs\*.log` lines `Landing verifier` and `BUILD SLOT` for the windows. Zero is
     expected, which would confirm Verdict 1 from the log side.
  4. `logs/apphost.restart.*` for any restart build after 10:18Z on 11-04.
  5. Any CPU counter series for the host. None is known to be recorded.

  The test: a failure counts as contention-linked only if its test's time span overlaps a
  granted build lease or a restart build, **and** the same test passes when re-run alone on the
  same candidate.

## 4. Recommendation

**(1), as a dormant, configuration-gated slice.** Smallest safe slice:

1. `NativeRunLockProbe` with the owner check in 1.2, plus the three `LandingSettings` keys.
2. The verifier applies `maxcpucount = min(grant, 2)` and `PriorityClass = BelowNormal` while the
   lock is held. When it is not held, behaviour is unchanged.
3. The observer line and the `VerificationCommand` note.

Tests, in `tests/Antiphon.Tests/Application/LandVerificationBuildSlotTests.cs` or a sibling class.
Each must go red before the change.

- Probe: a live owner (the test process's own pid and start time) in a private lock path is
  **held**. A dead pid is **not held**. A live pid with a start time that differs by more than 1 s
  is **not held** (pid reuse). A missing file is **not held**. A present but malformed file is
  **held**. An empty `NativeRunLockPath` is **not held**.
- Arguments: through the `LandProcessRunner` fake with a grant of 4, lock held gives
  `-maxcpucount:2` on the build; lock not held gives `-maxcpucount:4`; a grant of 1 stays 1 (the
  cap never raises the count).
- Priority: through the real `RunProcessAsync` with the probe held, the child seen in
  `StartedAsync` reads `PriorityClass == BelowNormal`; not held reads `Normal`. Windows-only
  unless Linux `setpriority` mapping is shown green. If it is Windows-only, it needs a NativeLinux
  policy row.
- Observation: the land record carries the `native-lock-held` note.

Why not the others:

- **(2) is unacceptable.** A 30-hour run at the measured 1-1.5 lands per hour defers **~30-45
  lands**. Lands are serial, so the first deferred land holds the whole land queue for up to the
  rest of the run. The current 30-minute `BuildSlotWaitMinutes` would turn every one of them into
  a `build_slot_timeout` failure. A stale lock left by a crashed run would stop lands until an
  operator intervened, and that lock is never reclaimed automatically.
- **(3) is already the de-facto practice, outside the land service.** Since 11-01, server2 Merge
  batches verify on Linux and push directly, which is why no desktop verify build overlaps the
  runs (Verdict 1). Building it into the land service is a large cross-component change (1.3) for
  a path that is not currently used.

Interaction with CARD-1625 (e):

- The two changes are independent and additive. (e) removes `DOTNET_gcServer` and
  `DOTNET_gcConcurrent` from the land child's environment, expected to make the build faster
  (1.6x on the Linux probe). (1) lowers the child's scheduling priority and node count only while
  the lock is held.
- Both edit the same place, the `ProcessStartInfo` built in `RunProcessAsync`
  (`AgentTaskLandService.cs:1980-1987`). CARD-1625 proposes extracting it into a factory. Whichever
  slice lands first should introduce that factory, with both knobs as factory inputs, so the
  second slice adds one parameter.
- (1) does not depend on the (a)-(e) decision. Under (b), a `VBCSCompiler` already running at
  normal priority (started by another build) would **not** be lowered, because it is not a child
  of this land. Under (a)/(b) that limits (1). Under (e) and today's policy it does not matter,
  since `UseSharedCompilation=false` means every `csc` is a child.
- Lower priority makes a land slower beside a run (today ~1,015 s against ~485 s quiet). (e), if
  chosen, offsets part of that.

Priority of the work: the land service does no master verification today (Verdict 1), so (1)
protects only a future return to land-service lands, or rc lands through the service. It is cheap
and safe, but not urgent.

## Uncertainties

1. Run-side harm was never measured. No silent-gap count or chunk timing was set against land
   builds (card "Measure"); that needs the desktop evidence in section 3.
2. Whether d47848cc was still running for the 10-31 19:28Z to 11-01 lands is not established
   here, so the 485 s "quiet" mean is an assumption about context.
3. Setting `PriorityClass` after `Process.Start` races any grandchild created in the first
   microseconds. MSBuild node start takes far longer, but this is not measured. A job object
   with `JOB_OBJECT_LIMIT_PRIORITY_CLASS` closes the race. `src/Antiphon.Agents.Pty/WindowsJobObject.cs`
   has a pid-based job helper, but it is `internal` to that assembly.
4. `-maxcpucount` caps MSBuild nodes only. A single `csc` still uses every core for one project, so
   most of the relief comes from priority, not the cap.
5. The desktop runner `:17204/build-slots` was not reachable from server2 (empty answer), so live
   lease state was not read.
6. The Merge-batch path's own safety (no land-v2 publication record) is outside this card.

## Not done, noted

- Fix idea (one line): `NativeRunLockProbe` plus a held-lock `min(grant,2)` and BelowNormal on
  the land child, behind `Landing:NativeRunLock*` settings.
- The 90-minute absolute TTL reaps the qualification's whole-run lease (`launch-rc.ps1:64`
  against `BuildSlotBroker.cs:116,196`). After that the broker admits two other desktop builds
  beside a live run. That probably deserves its own card. Not investigated further.
