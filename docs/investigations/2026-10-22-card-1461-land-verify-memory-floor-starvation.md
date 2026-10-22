# CARD-1461: land verification starves behind the desktop build-slot memory floor

Investigate task b2837326, 2026-10-22, read-only. Evidence base: source at
`3fa8d7bd480d7df345aea2bb46c042be19f78a76`, the desktop database (read-only SQL from the server2
mirror), `GET /api/hosts/stats` and `GET /api/hosts/desktop/stats/series?metric=memory&window=30m`
at 07:07Z, the operator's live `GET /build-slots` observation in the card. The desktop server log and
the runner's `/build-slots` endpoint are not reachable from the mirror (see Remaining uncertainties).

## Verdict

Confirmed. The mechanism is reconstructed from code and reproduced in stored evidence:

1. The desktop runner's build-slot broker refuses every grant while Windows reports less than
   6144 MB available physical memory, whatever the label and even with both slots free
   (`src/Antiphon.SessionRunner/BuildSlotBroker.cs:102-108`). There is no lease class, priority or
   reservation: the request carries only pid, start time, label, session and task
   (`src/Antiphon.SessionRunner.Contracts/BuildSlotContracts.cs:12-17`).
2. The Windows RC qualification does not take build slots at all: `scripts/lib/nightly-tests-impl.ps1`
   runs `dotnet build Antiphon.sln` (line 909) and each test chunk as a bare `dotnet <assembly>`
   (lines 1302-1319) through `CheckpointProcessRunner`, never through `Enter-AntiphonBuildSlot`
   (no match for `slot` in `nightly-tests-impl.ps1`, `nightly-run-impl.ps1`, `release-candidate.ps1`;
   `docs/testing-and-build.md:891` says the nightly joins in CARD-0589 Round 2). So the qualification
   consumes the memory the floor measures while `leases=0`, and the only waiter is the land verifier.
3. The server-side gate polls every `retryAfterMs` (15 s) until the floor clears or
   `Landing:BuildSlotWaitMinutes` (30) elapses; a memory-floor refusal counts toward that deadline
   (`server/Infrastructure/Agents/SessionRunner/SessionRunnerBuildSlotGate.cs:62-66,79-89`). A timeout
   is `LandVerification.Failure("build-slot", ...)` (`server/Application/Services/AgentTaskLandService.cs:1824-1825`),
   which the protocol turns into `verification_failed` (`AgentTaskLandingProtocol.cs:287`) and the land is
   not published (`AgentTaskLandService.cs:744-749`). Only an *unreachable* runner falls back to an
   unleased build; a *reachable* refusal never does.
4. While it waits, the land has already released the repository mutation lease (R) but still holds the
   land-execution lock (L) and the land queue's head position, so every other land on the repository
   waits behind it; dispatch is not blocked.
5. The wait stamps no progress, so after 300 s / 900 s the attention feed reports
   `Land Running; no progress`, not "waiting for memory".

## Q1. Floor, budget, availability, admission

| Item | Value | Source |
|---|---|---|
| Section | `SessionRunner:BuildSlots` | `src/Antiphon.SessionRunner/BuildSlotSettings.cs:11` |
| `MinAvailableMemoryMb` | 6144 (code default; 0 disables) | `BuildSlotSettings.cs:22` |
| `MaxConcurrent` / `MaxCpuCount` | 2 / 4 (desktop defaults) | `BuildSlotSettings.cs:16-19` |
| `RetryAfterMs` / `WaiterSilenceMs` / `LeaseTtlMinutes` | 15000 / 60000 / 90 | `BuildSlotSettings.cs:25-31` |
| server2 override | MaxConcurrent 4, MaxCpuCount 6, floor 16384 | `docker-compose.server2-runner.yml:69-71,176-181` |
| desktop override | none found (AppHost, runner appsettings, scripts, runbook) | grep over `Antiphon.AppHost/`, `src/Antiphon.SessionRunner/appsettings*.json`, `scripts/restart-session-runner.ps1`, `docs/apphost-runbook.md` |
| Who changes it | operator, by configuration; read once at broker construction (`BuildSlotBroker.cs:41`), no runtime setter exists (`SetBuildBudget`/`GetBuildSlots` from the CARD-0589 Round 2 plan are not in `src/` or `server/`), so a change needs a session-runner restart | `docs/testing-and-build.md:861-866` |
| Land-side wait | `Landing:BuildSlotWaitMinutes` 30, `Landing:BuildSlotUnreachableGraceSeconds` 60; no `Landing` section in `server/appsettings.json`, so defaults apply | `server/Application/Settings/LandingSettings.cs:16-22`, `server/Program.cs:482` |

**How `availableMb` is computed.** On Windows the broker's `IHostMemoryProbe` is
`WindowsHostStatsProbe.AvailableBytes` = `GlobalMemoryStatusEx().ullAvailPhys`
(`src/Antiphon.SessionRunner/WindowsHostStatsProbe.cs:36-49,126-132`, wired by
`HostStatsRoutes.cs:37`). `ullAvailPhys` is the sum of the free, zeroed and standby page lists. It
excludes every process working set, including `vmmem` (the WSL2/Docker Desktop VM) and the modified
list. The WSL guest's own page cache lives inside the VM's working set, not on the host standby list,
so dropping it frees host memory only as far as the VM returns pages to Windows; that is consistent
with the operator's 0.5 GB result but was not measured here. Linux uses `/proc/meminfo MemAvailable`
(`LinuxHostStatsProbe.cs:33-39`). The design rationale (measure live, never assume a standing
reservation; rejected per-lease reservations) is `docs/superpowers/plans/2026-09-25-card-0589-build-fanout-cap-plan.md` D-5.

**How a waiter is admitted** (`BuildSlotBroker.cs:74-120`): under one lock, sweep dead/expired
leases and silent waiters; a holder asking again gets its lease back; otherwise the caller is
appended to a FIFO waiter list; if its position exceeds the free slots it gets `build_slot_busy`;
else if `AvailableBytes/MiB < MinAvailableMemoryMb` it gets `build_slot_memory_floor` with the
current `availableMb`, `floorMb`, position and `retryAfterMs`; else it is granted. The floor is
evaluated per poll against the instantaneous reading, so a single 5-second dip above the floor
admits the head waiter. No kill, no revoke, no fallback.

## Q2. Lease class and what a waiting land-verify holds

- **Same class.** The land verifier is an ordinary waiter with label `land-verify <worktree name>`
  (`AgentTaskLandService.cs:1822-1823`), held by the server process pid, same budget, same floor, same
  FIFO. `scripts/lib/build-slot.ps1` delegates wait 45 min; the verifier waits 30
  (`LandingSettings.cs:16`).
- **Repository mutation lease (R, `landing.lock`): released before the wait.** The land acquires R
  at `AgentTaskLandService.cs:485-486`, prepares (rebase) under it, then opens a verification window
  and releases R (`:672-686`, log line "Land verification window released R ... L retained") before
  `VerifyPreparedAsync` (`AgentTaskLandingProtocol.cs:47`) which is where the slot is acquired. Dispatch
  preflights are admitted through the live window (`server/Infrastructure/Git/RepositoryVerificationWindows.cs:48-93`,
  `Compatible`), so **dispatch is not blocked** by a starved land. This is CARD-1379
  (`docs/testing-and-build.md:895-902`).
- **Land-execution lock (L, `land-execution.lock`): held for the whole wait.** Acquired at
  `AgentTaskLandService.cs:478`, an `await using` for the rest of the request; a second land on the
  repository gets `land_execution_busy`, text "Another land owns this repository's land worktree and
  verification artifacts" (`:479-484`). The land queue also serialises requests (events say
  `queue position=1; waiting=1`). **Cost: every other land on `C:\src\Antiphon` waits behind the
  starved one.** Measured: CARD-1449's land (task fa175d9b) was requested 05:19:07Z and started
  05:35:43Z, 16.6 min behind CARD-1427's 24-minute verification; CARD-0167's (0f389926) requested
  06:54:08Z, started 06:58:54Z behind CARD-0105. Non-code lands that would have finished in seconds
  (24 of the last 24 h's 80 lands were `non-code` skips) queue the same way.
- **Progress stamping.** `LastProgressAt` moves only on a phase transition
  (`AgentTaskLandingProtocol.cs:889-901`); the wait has none, so `AttentionService.cs:1461-1479`
  emits `LandNoProgress` Warning at 300 s and Error at 900 s (`DelegationSettings.cs:646-647`). The
  `BUILD SLOT waiting ... reason=memory_floor available=..MB floor=..MB` lines go only to the
  verifier observer and the server log (`server/Infrastructure/Git/LandingVerifier.cs:51-54`), never to
  events, so the board cannot tell a memory wait from a hung build.

## Q3. Measured wait history

Source: `AgentTaskLandings` (`VerificationStartedAt` is stamped at `AgentTaskLandingProtocol.cs:292`,
before the slot wait, and `VerifiedAt` after the tests), `AgentTaskLandRequests`, `AgentTaskEvents`
type 32 (LandAged), 36 h to 07:00Z. `PreparedAt -> VerificationStartedAt` was 0.0 min on every row, so
`VerifiedAt - VerificationStartedAt` = slot wait + build + tests.

Baseline: 80 lands in the 24 h to 07:00Z, 24 of them `non-code` skips; of the 56 verified lands, 49 took 1.9-4.5 min each (build plus
`tests/Antiphon.Tests` with the land filter). The seven outliers:

| Verification started (Z) | Card / task | Verify min | LandAged events (type 32) |
|---|---|---:|---|
| 10-21 13:49:56 | CARD-1401 / 760095f1 | 20.4 | Warning 13:54:59, Error 14:04:59 "no progress since 13:49:55" |
| 10-21 15:14:39 | CARD-1400 / 7cf73fd6 | 12.7 | Warning 15:19:40 |
| 10-21 23:12:04 | CARD-1416 / c31e4000 | 17.0 | Warning 23:17:07, Error 23:27:04 |
| 10-21 23:32:18 | CARD-1424 / 60670d4f | 6.6 | Warning 23:37:26 |
| 10-22 03:05:03 | CARD-1432 / 1919d384 | 10.4 | Warning 03:10:08 |
| 10-22 05:10:42 | CARD-1427 / 3b2c2936 | 24.0 | Warning 05:15:43, Error 05:25:43; CARD-1449 Queued behind it (Warning 05:24:09, Error 05:34:13) |
| 10-22 06:35:22 | CARD-0105 / bde3f3ad | 22.5 | Warning 06:40:22, Error 06:50:24 |

Every outlier has `HoldReasonCode=null`, `Attempt=1`, `State=Running` at the time (requests table),
i.e. the time went inside verification, not in a hold. The last two match the operator's live
observation in the card (single waiter `land-verify <sha>` since 06:35Z, `leases=0`,
`availableMb` 4.7-5.2 GB below `floorMb` 6144). The brief names CARD-1428 at 05:10Z; the database
has no land request for CARD-1428 in the window, only CARD-1427's land and CARD-1449's queued behind it.

`availableMb` history: the runner keeps 30 minutes of 5-second host samples
(`HostStatsStore`), the server projection carries no build-slot summary (`buildSlots` null in
`GET /api/hosts/stats`), and nothing persists `availableMb` per land. The 30-minute series fetched
at 07:08Z (06:38:27Z to 07:08:23Z, 356 points) shows available memory oscillating 4.0-5.7 GiB until
about 06:58Z, with 220 of 356 samples below 6144 MB, then rising to 9.3 GiB by 07:08Z. CARD-0105's
verification finished 06:57:55Z, consistent with a grant on a sample that crossed the floor around
06:54-06:56Z and a 2-3 minute build and test. Live at 07:07:45Z: total 31.9 GiB, available 9.2 GiB,
commit charge 34.6 GiB of a 52.1 GiB limit (`swapUsedBytes` = `TotalPageFile - AvailPageFile`),
CPU 99 %, runner working set 0.97 GiB, server 0.57 GiB. The host was overcommitted beyond physical
memory while the qualification ran, which is the situation the floor exists for.

Earlier outliers (13:49Z, 15:14Z, 23:12Z, 23:32Z, 03:05Z) are attributed to the same mechanism by
shape only (same 6-24 min band, same `no progress` events, nothing else in the land path that waits);
the desktop server log holds the proof per land:
`grep "land-verify" C:\src\Antiphon\server\logs\antiphon-*.log | grep "reason=memory_floor"` with the
task id (`Landing verifier task {TaskId} ... {BuildSlotLine}`, `LandingVerifier.cs:52`).

## Q4. Options and trade-offs

| Option | What changes | Helps | Costs / risks |
|---|---|---|---|
| A. Lower floor for land-verify only (second setting, request carries a purpose) | runner contract + broker + server gate; runner restart to take effect | keeps the broker as the single admission point; the verifier still counts against the budget | wire change on both sides; admits a build onto a host that is already paging; a value the operator must pick; does not help once the nightly joins the broker (refusal becomes `busy`) |
| B. Reserved verification slot (budget +1 for verification) | broker | nothing today: the starvation happens at `leases=0`; only useful after the nightly takes leases | adds concurrency on the loaded host |
| C. Bounded floor wait, then build unleased at a smaller `-maxcpucount` | `LandingSettings` + `SessionRunnerBuildSlotGate` (server only, no runner change, no restart of the runner) | caps the latency and removes the 30-minute `build_slot_timeout` failure; fewer MSBuild nodes lowers the build's peak; the line `BUILD SLOT unleased reason=memory_floor_fallback` keeps the run visibly unbudgeted | bypasses the broker for that build (invisible to other waiters; at most one at a time because L serialises lands); same paging risk as A; test-host memory is not reduced |
| D. Release L while waiting | land service, window and journal semantics | lets non-code lands (seconds) pass a starved verification | the next code land is refused by the same floor; CARD-1379-grade change, not 30-60 min |
| E. Memory-aware admission of qualification chunks | `nightly-tests-impl.ps1` (qualified script; a hash change forces requalification per `docs/release-gates.md:513`) | shrinks the overlap | starves the RC lane instead; expensive to requalify; the CARD-0589 Round 2 scope |
| F. Make the wait visible on the board (LandAged detail carries the last `BUILD SLOT` line) | verifier observer -> request writer plumbing | ends the "no progress" misdiagnosis | does not shorten the wait |
| G. Raise `Landing:BuildSlotWaitMinutes` above 30 | configuration only | avoids turning a long stretch into a refused land | longer tail; no code |

Recommendation: C as the one Code task (smallest surface, server-side, default-off so it changes
nothing until the operator sets it), G as an immediate configuration decision, F noted for a
follow-up card. A is the alternative if the operator prefers budgeted admission over an unbudgeted
fallback; it is not smaller.

## Q5. Minimal fix proposal (for the Plan stage; nothing implemented here)

Files: `server/Application/Settings/LandingSettings.cs`,
`server/Infrastructure/Agents/SessionRunner/SessionRunnerBuildSlotGate.cs`,
`tests/Antiphon.Tests/Application/LandVerificationBuildSlotTests.cs`, `docs/testing-and-build.md`
(the desktop land verifier paragraph at line 891).

Behaviour: two settings, `Landing:BuildSlotMemoryFloorFallbackMinutes` (int, default 0 = off,
today's behaviour) and `Landing:BuildSlotMemoryFloorFallbackMaxCpuCount` (int, default 2). In the
gate's `MemoryFloor` branch, record when the floor refusals began (`floorSince`, reset by a `Busy`
answer or a grant); when the fallback is enabled and `elapsed - floorSince >= fallback`, report
`BUILD SLOT unleased reason=memory_floor_fallback available=<a>MB floor=<f>MB waited=<s>s maxcpucount=<n>`
and return `BuildSlotHold(Unleased, null, n, elapsed)`. `VerifyUnderSlotAsync` already applies any
positive `MaxCpuCount` and skips release for a non-granted hold (`AgentTaskLandService.cs:1832-1835,1845-1847`).
Busy refusals keep the existing deadline; the wait deadline still applies while the fallback is off.

Red witnesses (same fixture as the existing tests; `FakeSessionRunnerClient.BuildSlotAcquire` and
`FakeTimeProvider`):
- `A_memory_floor_refusal_past_the_fallback_wait_builds_unleased_at_the_fallback_cpu_count`:
  client answers `MemoryFloor(available 4700, floor 6144, position 1, 15000)` while advancing the clock;
  fallback 5 min; expect `Outcome=Unleased`, `MaxCpuCount=2`, build command contains `-maxcpucount:2`,
  no release call, observer line starts `BUILD SLOT unleased reason=memory_floor_fallback`. Red today:
  the gate loops to `Timeout` and the verification fails with `build_slot_timeout`.
- `A_reachable_memory_floor_runner_times_out_at_the_wait_deadline_when_the_fallback_is_off`:
  mirror of `A_reachable_busy_runner_times_out_at_the_wait_deadline` (lines 97-116) with a
  `MemoryFloor` answer and default settings; expect `Timeout` and no `unleased` line. Green today;
  guards the default.
- `A_busy_answer_resets_the_memory_floor_fallback_clock`: alternate floor and busy answers; expect
  no fallback before the busy-reset window; red today only once the first witness is implemented
  naively.

Closed checkpoint list for the Code task:

| CP | Build | Filter | Expect |
|---|---|---|---|
| CP-1 | `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1461/` via `scripts/build-slot.ps1` | none | build only |
| CP-2 | reuse CP-1 | `/*/Antiphon.Tests.Application/LandVerificationBuildSlotTests/*` | 9/9 (6 existing + 3 new) |
| CP-3 | reuse CP-1 | `/*/Antiphon.Tests.Application/InstructionBundleTests/*` | unchanged (names the build-slot rules text) |
| CP-4 | reuse CP-1 | `/*/Antiphon.Tests.Infrastructure/DockerStackContractTests/*` | unchanged (compose floor values) |

Operator decisions (none made here): whether to enable the fallback and after how many minutes;
the fallback cpu count; whether to raise `Landing:BuildSlotWaitMinutes` (today a 30-minute stretch
refuses the land; the longest observed was 24.0 min); whether the desktop's
`SessionRunner:BuildSlots:MinAvailableMemoryMb` should change at all (it affects every delegate
build and needs a runner restart). This document proposes no value for any of them.

## Remaining uncertainties

- The per-land `BUILD SLOT waiting ... reason=memory_floor` lines are only in the desktop server log,
  unreachable from the mirror; the five earlier outliers are attributed by shape, the two on
  2026-10-22 by the operator's live reading plus the 30-minute series.
- The `vmmem` accounting (why dropping the WSL page cache freed only 0.5 GB) is explained from the
  `ullAvailPhys` definition, not measured.
- When the qualification started and ended on 2026-10-22 is not recorded anywhere reachable
  (Windmill and `C:\Antiphon\nightly\logs` are desktop-only).

## Not done, noted

- Fix idea: bounded memory-floor wait in `SessionRunnerBuildSlotGate` falling back to an unleased
  `-maxcpucount:2` build (Q5); plus a follow-up card for surfacing the last `BUILD SLOT` line in the
  LandAged event detail so the board says "waiting for memory" instead of "no progress".
