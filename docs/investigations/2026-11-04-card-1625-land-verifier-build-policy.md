# CARD-1625 / CARD-1623: the land verifier under the CARD-1506 build policy

Investigate task f7703a17, 2026-11-04. Base master `5449f8f50b9272d6ea468920725724ec88c56d68`. The work
was read-only apart from one Linux timing probe on server2 (section 1.4): seven builds (one warm-up, six timed), one build-slot
lease each, outputs deleted afterwards. Nothing ran on the desktop, where a Windows qualification is
live. The A/B script in section 4 has **not** been run.

Sources: the cards (`card.ps1 get CARD-1625|1623|1506|1525|1461`); the desktop log check
`docs/investigations/2026-10-31-land-verification-slowdown-desktop-log-check.md` on
`origin/feat/card-task-cb60ed48` (commit `3a062fdba`); the CARD-1461 investigation
`docs/investigations/2026-10-22-card-1461-land-verify-memory-floor-starvation.md`; the CARD-1506 plan
`docs/superpowers/plans/2026-10-22-card-1506-no-resident-compiler-plan.md`; and Debug da743107's report
`.antiphon/task-da743107.md` on `origin/feat/card-task-da743107` (`dd49f9250`).

## Verdict

**The mechanism is confirmed. Which knob costs the most is now partly measured, and the answer is not
the one the cards assumed.**

- The CARD-1506 policy reaches the land verifier's `dotnet build` and its `dotnet run` test child
  through one call, `AgentTaskLandService.cs:1984`. cb60ed48 reconstructs the 3x step from that
  policy using stored evidence: lands took 149-296 s before it, 189-227 s with the opt-out set, and
  413-438 s alone on the broker with the policy on.
- New, measured on Linux only (section 1.4). Each run recompiled the server project once. With
  `UseSharedCompilation=false` alone it took **185 / 206 s**. With the full policy it took
  **305 / 338 s**: the same run plus `DOTNET_gcServer=0` and `DOTNET_gcConcurrent=0` for `csc`. The
  two rounds ran in opposite orders. The GC overrides alone added **~100-150 s (~1.6x)**, and peak
  process-tree RSS **did not drop** (5.7-5.9 GB against 5.9-5.95 GB). `csc.dll` and `VBCSCompiler.dll`
  ship with `System.GC.Server: true` in their runtimeconfig (SDK 10.0.403). The policy turns that off
  for every compiler process it reaches.
- So option (b), "keep `UseSharedCompilation` on and keep the rest", probably recovers less than
  expected. The compiler it starts inherits the GC overrides. A narrower option, (e), "keep
  `UseSharedCompilation=false` and drop only the two GC overrides", keeps CARD-1506's no-resident-
  compiler goal and removes the term this probe found largest. Both are hypotheses for the desktop
  until the A/B runs: Windows magnitudes are not measured.
- The four land E2E failures today (section 2.4) cannot yet be split between the 6,144 MB floor wait
  and a slow build. Debug da743107 says so too. Its fixture heartbeat (master `147034055`) removes the
  harness window as a failure source either way.

## 1. What the CARD-1506 policy sets, where it applies, and the time cost

### 1.1 The six overrides

`scripts/lib/BuildDriverEnvironment.cs:23-38`. The policy writes nothing when the platform is not
Windows (`windows` false), or when `ANTIPHON_BUILD_MEMORY_POLICY_OPTOUT` is exactly `1` (checked with
`ContainsKey` first, the CARD-1525 repair `462dfdd8e`). Otherwise it writes the following into the
child's environment before start:

| Variable | Effect | Memory | Time |
|---|---|---|---|
| `UseSharedCompilation=false` | An MSBuild property, read from the environment. The `Csc` task (`Roslyn/Microsoft.CSharp.Core.targets:171`, `UseSharedCompilation="$(UseSharedCompilation)"`) runs a fresh `csc` process for every project it compiles instead of sending the compile to the resident `VBCSCompiler`. | Nothing stays resident after the build: the CARD-1506 target. CARD-1506's card records "a long-lived VBCSCompiler held ~5 GB working set" and free memory going from 2.4 GB to 8.4 GB after `dotnet build-server shutdown` (operator, 2026-10-22 ~18:50Z). Per-build peak is unchanged or higher: each `csc` loads all its references again. | Every compiling project pays a cold start: process start, JIT/R2R load of Roslyn, and metadata reads of every reference with no cross-project cache. Projects with no compile step to run are not affected. |
| `DOTNET_gcServer=0` | A runtime setting for every .NET child of the build: MSBuild nodes, each `csc`, a `VBCSCompiler` the build starts, and the test host the land's `dotnet run` starts. The policy's design assumes the environment variable overrides runtimeconfig. The probe's timing change agrees, but the GC mode of a `csc` process was not read directly. | `csc`/`VBCSCompiler` runtimeconfig sets `System.GC.Server: true` (read on server2 from `/usr/share/dotnet/sdk/10.0.403/Roslyn/bincore/*.runtimeconfig.json`). The `dotnet`/`MSBuild` runtimeconfigs set no GC mode, so those default to workstation GC and the override changes nothing there. The test project sets no GC property (grep of `tests/`), so that is also no change. Server GC keeps one heap per logical CPU. That is the main reason a resident server-GC compiler is large. | Workstation GC collects on the allocating thread, and Roslyn compiles method bodies in parallel. The probe measured that cost (1.4). |
| `DOTNET_gcConcurrent=0` | Turns off background GC in the same processes, including MSBuild nodes and the test host. | Little peak effect expected; not measured separately. | Longer blocking gen2 pauses. Not separated from `gcServer` in the probe. |
| `DOTNET_CLI_USE_MSBUILD_SERVER=0` | Turns off the MSBuild server for `dotnet build`. | No resident MSBuild server. | Probably no change: in SDK 8+ the MSBuild server is opt-in. Not measured. |
| `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1` | The SDK's admission switch for the same thing (`BuildDriverEnvironment.cs:36`). | As above. | As above. |
| `MSBUILDDISABLENODEREUSE=1` | Worker nodes exit with the build. | No resident worker nodes. | No change: `Directory.Build.rsp:4` already passes `-nodeReuse:false` to every build, and cb60ed48 shows it predates the step. |

`-maxcpucount` is **not** part of the policy. It comes from the build-slot grant (desktop 4,
`BuildSlotSettings.cs:19`; memory-floor fallback 2, `LandingSettings.cs:41`;
`AgentTaskLandService.cs:1952-1954`).

### 1.2 Who applies it (one shared source)

| Caller | Location | Children affected |
|---|---|---|
| Land verifier | `server/Application/Services/AgentTaskLandService.cs:1984` (`RunProcessAsync`) | The `dotnet build --artifacts-path ... -maxcpucount:N` (`:1952-1955`) **and** `dotnet run --project tests/Antiphon.Tests --no-build` (`:1960-1962`) |
| Build-slot wrapper | `scripts/build-slot.ps1:132` (`Enter-AntiphonBuildDriverEnvironment`, scoped to the wrapper process) | Every leased driver: AppHost restore/build, daemon builds, delegates' direct builds |
| Checkpoint script | `scripts/run-checkpoint.ps1:769,859` | Checkpoint row builds and runs |
| Checkpoint tool | `tools/Antiphon.Checkpoints/BuildConfinement.cs:647`, `Execution/ProcessDriver.cs:103` | Graph evaluation, row builds and test hosts (Unit-lane test builds go through here) |
| Nightly / rc qualification | `scripts/lib/nightly-tests-impl.ps1:1851`, `scripts/lib/nightly-owned-process.ps1:29` | The qualification's native test chunks (so `gcConcurrent=0` also reaches its test hosts) and owned processes |
| Watched tests | `scripts/run-tests-watched.ps1:73` | Scoped environment around the test executable |

The land verifier's call is its own line, in its own `ProcessStartInfo` factory. A land-only change
touches no other caller.

### 1.3 The log evidence for the 3x (cb60ed48, unchanged)

Same toolchain directory, same warm artifacts generation (`25df0afb...`) and same land worktree, before
and after:

| Server PID | Policy | land-verify slot hold (build + tests) |
|---|---|---|
| 308056 (10-22 18:32Z .. 10-23 00:28Z) | absent (pre-CARD-1506) | 149-296 s, typically 155-230 s |
| 141036 (10-23 ~01:00-01:51Z) | code present, `OPTOUT=1` | 227 s, 189 s |
| 89580 (from 10-23 ~01:53Z) | on | 766, 636, 496, 488, 500, 528 s |
| 44004 (10-27, alone on the broker, no qualification) | on | 413, 438, 424 s |
| 10-31 under the live qualification | on | 940, 750, 1,566, 955 s |

Artifacts were warm on all 35 lands from 10-27 to 10-31, and slot waits were 0-62 s, so neither
explains the step. Host load explains the later climb from ~420-550 s, not the step itself. The
desktop logs do not separate the knobs. The policy is environment, not arguments, and the log never
prints a command line.

### 1.4 New: knob ranking on Linux (server2, measured today)

A Linux run is a fair test of the knobs themselves. `BuildDriverEnvironment` writes nothing on Linux,
so the probe set the variables directly. Setup: the `server/Antiphon.Server.csproj` dependencies were
warm-built once (115 s held). Each probe then ran `touch server/Program.cs` (mtime only, no content
change) and `dotnet build server/Antiphon.Server.csproj --no-dependencies -p:UseAppHost=false` through
`scripts/build-slot.ps1`. One project recompiled each time (server: 1,739 files, ~1.41 M lines
including migrations). Each probe took one lease at `maxcpucount=6`. The host was loaded (load average
32-53 on 24 CPUs, all four broker slots held by other tasks at the start). Peak RSS is the 1-second
maximum of the build's process tree (`ps` RSS summed below the wrapper pid).

| Order | Variant | Environment | MSBuild `Time Elapsed` | Slot held | Peak tree RSS |
|---|---|---|---:|---:|---:|
| 1 | shared compiler | none | 82 s | 83 s | 953 MB (compiler not in tree) |
| 2 | no shared compiler | `UseSharedCompilation=false` | 206 s | 207 s | 5,868 MB |
| 3 | full policy | all six | 338 s | 339 s | 5,919 MB |
| 4 | full policy | all six | 305 s | 306 s | 5,746 MB |
| 5 | no shared compiler | `UseSharedCompilation=false` | 185 s | 186 s | 5,948 MB |
| 6 | shared compiler | none | 200 s | 201 s | 923 MB (compiler not in tree) |

What the table shows:

- **GC overrides on `csc`: +100-150 s over the no-shared-compiler run**, in both orders (206 to 338 s,
  185 to 305 s), with no lower peak. This is the robust result.
- **The shared-compiler term is not isolable on server2.** Every task on the host shares one compiler
  pipe (`-pipename:AnbIBBwwDvDs0A3cN88vIMFH3zfRkeks7vL9slaqpyg`). At 10:04Z there were **nine
  `VBCSCompiler` processes** on that one pipe name, started by tasks 8347d02a and da743107, holding
  ~11.4 GB RSS in total. One of them, 4,951 MB, had been started 203 s earlier by another task.
  Probe 6 (200 s) therefore probably got a cold or busy compiler, and probe 1 (82 s) a warm one. The
  warm/cold difference (82 against ~190-206 s) is itself a measurement: a warm compiler is what saves
  time, and a lone land that comes more than ~10 minutes after the last build would not have one.
- These are Linux numbers for one project, not a whole-solution land build on Windows. The direction
  is evidence. The desktop magnitudes need the A/B in section 4. One factor could make the desktop
  worse: Windows process start plus Defender scanning a fresh `csc` per project. That is not measured.

## 2. The memory side

### 2.1 The floor

| Item | Value | Source |
|---|---|---|
| `SessionRunner:BuildSlots:MinAvailableMemoryMb` | **6144** (code default, desktop has no override) | `src/Antiphon.SessionRunner/BuildSlotSettings.cs:22`; CARD-1461 investigation Q1 |
| Set by | The operator, by configuration. Read once when the broker is built, so a change needs a session-runner restart | CARD-1461 investigation Q1 |
| Compared value | `GlobalMemoryStatusEx.ullAvailPhys` (free + zeroed + standby), live, per poll | `WindowsHostStatsProbe.cs`, cited in CARD-1461 Q1 |
| Behaviour | A grant is refused below the floor even with a free slot. A held lease is never revoked. Retry every 15 s | `docs/testing-and-build.md` (Build slots) |
| Land side | Waits up to `Landing:BuildSlotWaitMinutes` 30. Memory-floor fallback `Landing:BuildSlotMemoryFloorFallbackMinutes` defaults to 0 (off) in code, but is **active on the desktop**: cb60ed48 saw `unleased reason=memory_floor_fallback ... waited=120s maxcpucount=2` on 10-28 and 10-30 | `LandingSettings.cs:16,32,41`; cb60ed48 section 4 |
| server2 | floor 16384, 4 slots, cpu 6 | `docker-compose.server2-runner.yml` |

### 2.2 The desktop's free-memory profile when qualification and land overlap: what is measured

- **Host:** 32 GB physical (CARD-1506 card). The CARD-1461 snapshot at 10-22 07:07Z read
  31.9 GiB total.
- **CARD-1461 (10-22, policy not yet live):** 356 samples at 5 s from 06:38-07:08Z. Available memory
  swung between 4.0 and 5.7 GiB until ~06:58Z, and 220 of the 356 samples were below 6,144 MB. Commit
  charge was 34.6 GiB of a 52.1 GiB limit, with CPU at 99%. The operator saw 4.7-5.2 GB available
  with `leases=0` while the qualification ran. The qualification takes no build slot for its test
  phases.
- **CARD-1506 (10-22 ~18:50Z):** free memory was 2.4 GB during lands and the qualification, and 8.4 GB
  after `dotnet build-server shutdown`. That is ~6 GB released by stopping resident compiler or
  MSBuild servers, a single operator observation.
- **cb60ed48 (10-31, policy on):** the land broker read `available=5760MB` (07:13Z, 15 s wait) and
  `available=4183MB` (10:00Z, 62 s wait). Earlier fallbacks read `available=4057MB` (10-28 03:53Z).
- **Today (11-04):** no desktop memory sample was read (desktop off-limits). Debug da743107 could not
  tell admission from execution for the E2E failures.

**Not measured anywhere:** a per-land peak working set of the land build (build tree or
`VBCSCompiler`), under the policy or without it; the residency of a `VBCSCompiler` that runs with
workstation GC (option b); the desktop's logical CPU count, which sets the server-GC heap count; and a
time series of available memory across one land during a qualification. The A/B script records each
of these.

### 2.3 What the policy buys in memory

The documented saving is residency, not peak. Without the policy a `VBCSCompiler` with server GC
stays resident after the build. CARD-1506 recorded ~5 GB on the desktop, and server2 today shows
4.95 GB for one instance after a server compile. A 32 GB desktop with a qualification running at
4-6 GB available cannot absorb that without dropping below the 6,144 MB floor for every later build
until the compiler idles out. `docs/testing-and-build.md:1171` gives "ten minutes" as the idle-out;
the Roslyn default was not re-verified. The probe measured no peak reduction from the GC overrides,
so on this evidence `DOTNET_gcServer=0` costs time without lowering the build's own peak. Its memory
value, if any, is in shrinking a resident `VBCSCompiler`, which `UseSharedCompilation=false` already
prevents.

### 2.4 The 2026-11-04 E2E failures

Debug da743107 (rc 0b05506b, `C467_V29 Batch2(residue-cleanup)`): the receipt's 60 s quiet window
renewed only on protocol-git records. The land verifier, meaning the slot wait plus the real
`dotnet build` of the fixture's small owned project, writes none of those, so any verify longer than
60 s failed the receipt. On server2 a cold verify build of that project takes 3.4-6.4 s (n=5). The
fixture's owned runner uses the default 6,144 MB floor, retries every 15 s, and the fixture enables
no fallback. Two causes are possible on the Windows host and neither is shown: floor admission, or a
cold build under load and the policy. A tiny project's policy cost is a few `csc` cold starts, which
makes floor admission the more likely of the two. That is inference only. Resolve it from the
desktop evidence root: `server-logs\*.log`, lines `Landing verifier` / `BUILD SLOT`, 08:04:05-08:05:05Z
(da743107). The fixture heartbeat (`728e11f98`, on master as `147034055`) keeps the receipt alive for
up to 300 s while verification runs. The names and durations of the four failing tests
were not re-read here: that evidence is on the desktop.

## 3. Options

Times are per land-verify build. "Today" is ~420-550 s on a quiet desktop and 750-1,566 s beside a
qualification (1.3).

| Option | Change | Expected time | Expected memory | Risks | Test seam |
|---|---|---|---|---|---|
| (a) Exempt the land verifier completely | The land's `ProcessStartInfo` skips `BuildDriverEnvironment.Apply` (or sets the opt-out on that child) | Back to the pre-policy 149-296 s quiet (cb60ed48 rows 1-2), on a warm compiler | A server-GC `VBCSCompiler` stays resident after each land: ~5 GB (CARD-1506), and idle-out is ~10 min. Lands arrive every 15-60 min, so the desktop runs ~5 GB short for ~10 min after each one | The exact failure CARD-1506 fixed: the qualification and the next builds starve on the 6,144 MB floor, and on a 32 GB host already overcommitted (commit 34.6/52.1 GiB) the OOM/paging risk rises. Any unwrapped build on the desktop (IDE, operator shell) shares that compiler | `RunProcessAsync` builds and starts the process in one step, so the environment is not observable without starting a child. Extract a start-info factory and pin the land child's environment with a real `ProcessStartInfo` and `windows: true` forced through the existing `Apply(StringDictionary, bool)` overload |
| (b) Keep `UseSharedCompilation` on for the land verifier only, keep the other five | Land child drops one override | Recovers the cold-start term only when the compiler is warm. The GC term stays: the compiler it starts inherits `DOTNET_gcServer=0` (assumed: the environment passes to the compiler server the client starts). On the Linux probe that was the larger term. The gain is unknown and probably partial | A resident `VBCSCompiler` with workstation GC. Smaller than ~5 GB but non-zero (caches and heap); residency is unmeasured | The same residency risk as (a), at an unknown size. Under load several compilers can start on one pipe: nine were observed on server2 | As in (a). One more test pins that only `UseSharedCompilation` is absent and the other five are present |
| (c) Land-specific policy with a per-land memory budget | For example: exempt only while the broker grant shows `availableMb >= floor + budget`, otherwise apply the policy. Or a land-only floor through a runner contract change (CARD-1461 option A) | Fast when memory allows, today's speed otherwise. The slow case is the qualification overlap, which is exactly when the budget refuses, so the CARD-1623 overlap stays slow | Bounded by the budget at admission. The resident compiler outlives the grant, so the budget is checked only at start | Most complex. The grant's `availableMb` must reach the process factory (a `BuildSlotHold` field). A budget value the operator must choose. A runner contract change needs a runner restart | Gate fake (`FakeSessionRunnerClient.BuildSlotAcquire`, already used by `LandVerificationBuildSlotTests`) plus the start-info factory test |
| (d) Leave the policy, raise only the harness budget | E2E: done (da743107 heartbeat, 300 s provisional). Production: optionally raise `Landing:BuildSlotWaitMinutes` | No change: lands stay ~3x slower and hold the land-execution lock, and the queue behind them, 2-3x longer | No change: CARD-1506's saving is kept | Land throughput stays low, and land-delivery E2E stays exposed to floor waits during a qualification. It does not address CARD-1623 contention: a longer build overlaps more of the qualification | Existing `LandDeliveryFixtureProbeTests` (da743107) |
| (e) New: drop only the two GC overrides, for the land verifier or everywhere | Remove `DOTNET_gcServer`/`DOTNET_gcConcurrent` from the land child (or from the policy) and keep `UseSharedCompilation=false` and the MSBuild-server/node-reuse switches | Linux probe: 305-338 s down to 185-206 s for one server recompile (~1.6x faster). Desktop magnitude unmeasured | No resident compiler, so CARD-1506's goal is kept. Linux peak RSS unchanged for one large `csc`. On Windows, up to `maxcpucount` concurrent `csc` processes each with per-CPU server-GC heaps could raise the **transient** peak: unmeasured | Transient peak on the desktop during a qualification. If (e) is applied policy-wide, it also changes the qualification's test hosts (`gcConcurrent`) and checkpoint builds. Land-only keeps the blast radius to one call site | As in (a) |

## 4. A/B plan for the desktop (ready, not run)

Script: `docs/investigations/2026-11-04-card-1625-ab-build.ps1` (PowerShell 7, ASCII). Parsed on
server2 with `[Parser]::ParseFile` (0 errors) and `[scriptblock]::Create` (ok). On Linux it exits 2
with `refused: ... run it on the desktop`, as intended. The `Get-VariantEnvironment` mapping was
checked on server2 for all five variants.

What it does:

- For each variant it makes a detached scratch worktree at `-BaseSha` and a separate
  `--artifacts-path`.
- Each repeat checks out `-BaseSha` and runs an untimed warm build, then checks out `-HeadSha` and
  runs the **timed** build. The timed command is the land verifier's own:
  `dotnet build --artifacts-path <out> --configuration Debug`, through `scripts/build-slot.ps1`, one
  lease per build, so the grant adds `-maxcpucount:4` as it does for the land.
- With `-TestFilter`, it also times the land's `dotnet run --project tests/Antiphon.Tests --no-build`.
- Repeats are interleaved A,B,C then C,B,A.
- A 1-second sampler records: `ullAvailPhys` through `GlobalMemoryStatusEx` (the broker's own value),
  the minimum and mean, seconds below 6,144 MB, commit charge, the peak working set of the build's
  process tree, and the peak working set and count of `VBCSCompiler` and `csc`. After the build it
  samples `VBCSCompiler` residency for `-ResidencySeconds` (default 120).
- It writes everything to one JSON file. Exit codes: 0 all green, 1 a build failed, 2 refused.

Variants: `Policy` (today), `Exempt` (a), `SharedCompiler` (b), `PolicyWithoutGc` (e), and
`SharedCompilerServerGc` (diagnostic). The script runs at most one compiler-sharing variant per
invocation, because they share one `VBCSCompiler` whose GC mode is fixed by whichever build starts it.
Unless `-AllowBusyHost` is passed, it refuses when a build-slot lease is held or a `VBCSCompiler` is
already running.

Suggested runs, after the live qualification and its native reruns finish, on a quiet desktop. Each
is ~30-60 min, mostly warm builds:

```powershell
# 1: today vs (e) vs (b); BaseSha/HeadSha = two consecutive master code lands (a real land delta)
pwsh -NoProfile -File docs\investigations\2026-11-04-card-1625-ab-build.ps1 -BaseSha <40hex> -HeadSha <40hex> -Variants Policy,PolicyWithoutGc,SharedCompiler -Repeat 2
# 2: today vs (a), at least 10 minutes after run 1 so its VBCSCompiler has idled out
pwsh -NoProfile -File docs\investigations\2026-11-04-card-1625-ab-build.ps1 -BaseSha <40hex> -HeadSha <40hex> -Variants Policy,Exempt -Repeat 2
```

The decision reads from the JSON:
- `build.wallSeconds` for each variant;
- `build.memory.peakTreeMb` / `peakCscMb` / `minAvailableMb`, the transient cost;
- `residency.peakVbcsMb` and `residencyLastVbcsMb`, the resident cost;
- `slotWaitedSeconds`, which should be 0 on a quiet host.

## 5. Recommendation and the operator's decision

**Decision (operator; CARD-1506 is a memory policy, so this is a trade-off):** what does the land
verifier get?

1. **(e) land-only, recommended default if the A/B confirms it on Windows.** The land child keeps
   `UseSharedCompilation=false` and the MSBuild/node switches and drops `DOTNET_gcServer=0` /
   `DOTNET_gcConcurrent=0`. No compiler stays resident, so CARD-1506's documented ~5 GB saving holds.
   Expected to recover the larger part of the 3x if the Linux ranking carries over. Acceptance on
   the desktop: `PolicyWithoutGc` `build.wallSeconds` at or below 0.7x `Policy`, and its peak tree
   working set no more than 1 GB above `Policy`'s.
2. (b) only if the A/B shows `SharedCompiler` clearly faster than (e) **and** its post-build
   `VBCSCompiler` residency fits under the floor with the qualification running.
3. (a) only if the operator accepts ~5 GB resident for ~10 min after each land, or schedules lands
   away from qualifications. This undoes CARD-1506 for lands.
4. (d) is the status quo plus the E2E heartbeat already on master.

Whether the same GC relaxation should apply to the checkpoint and wrapper callers is a separate
decision, deliberately out of this one-call-site slice.

**Follow-up Code slice (one call site), after the decision:**
- `server/Application/Services/AgentTaskLandService.cs`: extract the start-info construction from
  `RunProcessAsync` (`:1979-1984`) into an internal factory that takes the land build environment
  mode. Apply `BuildDriverEnvironment`, then remove the chosen overrides for the land child only, or
  for (a) skip the call. Gate it on a `Landing:VerifierBuildEnvironment` setting (`Policy` default,
  the chosen value) in `LandingSettings`, so rollback is a configuration change.
  `scripts/lib/BuildDriverEnvironment.cs` stays unchanged; every other caller keeps the policy.
- Tests in `tests/Antiphon.Tests/Application/LandVerificationBuildSlotTests.cs` or a sibling class:
  1. The factory with a real `ProcessStartInfo` and `windows: true` forced through
     `BuildDriverEnvironment.Apply(StringDictionary, bool)`. With the mode set, the land child lacks
     exactly the removed variables and still carries the rest. Red before the change.
  2. With the default mode the land child carries all six. This guards the default.
  3. With the opt-out set to `1` the child is unchanged. The absent-key case is also covered, after
     the CARD-1525 lesson.
  4. Linux (`windows: false`): no variables at all.
- `docs/testing-and-build.md`: the land-verifier exception goes in the Build slots/CARD-1506
  paragraph. Also fix the stale line 1171, "`UseSharedCompilation` stays on".
- Activation needs an rc pick and an AppHost restart. A master land alone does not activate it
  (`docs/testing-and-build.md`, CARD-1506 paragraph).

## Uncertainties

1. The Windows magnitude of each knob is unmeasured. The Linux probe covers one project on a loaded
   host, n=2 per variant.
2. `gcServer` and `gcConcurrent` were not separated from each other.
3. Two things are assumed, not observed: that the environment overrides runtimeconfig for
   `System.GC.Server` in `csc`, and that a `VBCSCompiler` started by the build inherits the GC
   variables. The timing change supports the first. The second decides how much (b) recovers.
4. The 2026-11-04 E2E failures are not yet split between floor wait and slow build (2.4).
5. The `VBCSCompiler` idle-out (~10 min) is taken from repo docs, not re-verified.
6. The brief cites "CARD-1589" for memory evidence. CARD-1589 is a server2 cache-preflight card with
   no build-memory content. The memory evidence used here is CARD-1461 and CARD-1506.

## Not done, noted

- Fix idea (one line): land child drops `DOTNET_gcServer`/`DOTNET_gcConcurrent` behind a `Landing`
  setting, after the desktop A/B.
- server2 had nine `VBCSCompiler` processes on one pipe name holding ~11.4 GB RSS at 10:04Z. That is
  shared-compiler multiplication on a loaded Linux host, possibly worth its own card. Not
  investigated.
- cb60ed48's three superseded land-verify generations (~6.8 GB) on the desktop remain.
