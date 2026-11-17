# Linux compiler-policy trial (CARD-1950)

Recommendation: **extend the trial**. Do not flip the default on, and do not revert.

The standing server2 runner is applying `ANTIPHON_LINUX_COMPILER_POLICY=1`. Builds since 2026-11-16 00:40Z use the pinned SDK 10.0.404 toolset, CPU affinity `0-5`, and one shared `VBCSCompiler` pipe `ac1885-1ff7756…`. No trial-window compiler crash, `fallback=stock` build, or `Build FAILED` was found. The CARD-1950 acceptance gate is not met: `tests/Antiphon.Tests` wall times are slower after the cutoff, and that sample is unmatched on cold/warm and on host load, so the slowdown is not attributed to the policy.

Master SHA read after `git fetch origin master`: `300c8a99d0d267a39791410b7540189ab702f251`. Worktree HEAD before this note: `c3cc0bb9b7b1d0c55464d07cd8e60a63b4239028`.

## What the policy is

Exact `ANTIPHON_LINUX_COMPILER_POLICY=1` on Linux opts a `dotnet build|test|run|publish|pack|msbuild` into the CARD-1885 launcher. Exact `0`, any other value, or Windows keeps the stock compiler. Preparation failure also keeps the stock compiler and logs `LINUX COMPILER fallback=stock`.

| Site | Behavior today |
|---|---|
| `scripts/lib/BuildDriverEnvironment.cs:15` | `LinuxSharedCompilerPolicy.Enabled`: `!windows && setting == "1"` |
| `scripts/lib/BuildDriverEnvironment.cs:219` | Refuses unless the SDK base path's directory name is `10.0.404`, then falls back to stock |
| `scripts/lib/BuildDriverEnvironment.cs:231` and `:358` | Copies Roslyn under `$XDG_CACHE_HOME/antiphon-compiler/<identity>` and passes `-p:RoslynTargetsPath`, `-p:RoslynTasksAssembly`, `-p:SharedCompilationId`. Idle 120s, heap hard limit `100000000`, CPU grant from the build slot |
| `docker-compose.server2-runner.yml:67` | `ANTIPHON_LINUX_COMPILER_POLICY: &RunnerLinuxCompilerPolicyDefault ${ANTIPHON_LINUX_COMPILER_POLICY:-0}` |
| `scripts/c590-remote.sh:1133` | `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0` (missing saved key) |
| `docs/docker-stack.md:946-991` | Documents default OFF, the 24-hour trial, and a later default-on flip |

This session's environment is `ANTIPHON_LINUX_COMPILER_POLICY=1`. `dotnet --info` reports SDK `10.0.404` at `/usr/share/dotnet/sdk/10.0.404/`.

## Cards

- **CARD-1950** (InProgress, moved from Backlog at 2026-11-17 03:39Z for this task). Operator text: if the trial works, make the policy the default and keep only an opt-out. Gate: compiler stays inside its cap; matched build rows are not slower at p95 (or the operator accepts the slowdown); timeouts and reruns do not rise. The described flip is `Enabled` (missing/empty = on, exact `0` = off), the Compose `:-0` default, `RUNNER_LINUX_COMPILER_POLICY_DEFAULT`, `docker/stack.env.example`, docs, and the tests that pin default off. Windows stays on the stock compiler.
- **CARD-1906** (Review). Current title is the server2 oversubscription card. History is six moves: Investigate `665816dd` (2026-11-15 04:45Z), Code `28eecdf5` (11:39Z), Debug `c2489e73`, Review `baba7a4e` settled Succeeded at 2026-11-15 18:04Z. The plumbing note is `docs/investigations/2026-11-15-card-1906-compiler-policy-plumbing.md`. Its own text says activation is a later rollout, not that implementation.
- **CARD-1903** (Backlog, zero revisions). Clean-review advisories that must land before any default-on slice, including a full-solution A/B (the reduced-project A/B was −34.9% and was not solution acceptance) and the note that one compiler identity is shared per user, container, SDK, and CPU set.

## Trial evidence

Transcripts searched: 207 Codex rollout, Claude, and Grok `updates.jsonl` files newer than 2026-11-14, 865,429,985 bytes, excluding this task. 300 server2 and server2-temp Code/Review/Merge/Debug/Test reports since 2026-11-13 contained no `Time Elapsed`, `Build succeeded`, or `LINUX COMPILER` line. Durations below are from the transcripts, deduped by file + project + elapsed seconds.

Live policy diagnostics (full `policy=c1885-v1` line with identity, cpus, cache, prepareMs): **93 unique lines in 38 files, all at or after 2026-11-16 00:40:43Z**, through 2026-11-17 03:55Z. None of the 11 earlier `LINUX COMPILER policy` hits (2026-11-15 04:45Z–21:22Z) carried cache/cpu/identity fields; they are quotes in the CARD-1906 sessions, not fleet builds. The stated env time 2026-11-15 23:57Z was not re-checked with `docker inspect` from this container. The first observed build-driver use is 00:40Z.

| After-cutoff diagnostic | Count |
|---|---|
| `cache=copy` | 62 |
| `cache=hit` | 28 |
| `cpus=0,1,2,3,4,5` | 87 |
| `cpus=0` | 4 |
| identity prefix `1ff7756efdc7` | 77 |
| identity prefix `7aaf282f7469` | 10 |
| identity prefix `5ac3b135531b` | 4 |
| `prepareMs` on copy, median / max | 892 / 2177 |
| `prepareMs` on hit, median / max | 402 / 706 |

One full line (Codex rollout `2026-11-16T23-39-47`, Checkpoints build, `cache=copy`): SDK `/usr/share/dotnet/sdk/10.0.404`, `sdkContent=36e59c0e…`, `identity=1ff7756efdc7af5fe8ad24e509de69a77ba6dfb1eafbd3743468284806fc303a`, `pipe=ac1885-1ff7756efdc7af5fe8ad24e509de69a77ba6dfb1`, `cpus=0,1,2,3,4,5`, `heap=100000000`, `idle=120`, `prepareMs=878`, `resolutionMs=461`, `grantSource=typed`.

On disk at investigation time: 81 directories named that `1ff7756…` identity under per-session `/tmp/antiphon-pwsh-cache-*/antiphon-compiler/`. One measured copy is 44MB (`du -sh`). The oldest of those directories is 2026-11-16 15:29Z; earlier Nov 13 caches are other identities from the CARD-1885 experiments.

Live process, one `VBCSCompiler.dll`, pid 22102 at sample time, parent an MSBuild node: `/usr/bin/dotnet exec /usr/share/dotnet/sdk/10.0.404/Roslyn/bincore/VBCSCompiler.dll -timeout:120 -pipename:ac1885-1ff7756efdc7af5fe8ad24e509de69a77ba6dfb1`. `/proc/22102`: `Cpus_allowed_list: 0-5`, `DOTNET_PROCESSOR_COUNT=6`, `DOTNET_GCHeapHardLimit=100000000`, `ANTIPHON_LINUX_COMPILER_POLICY=1`, RSS 4.56GB after 4m41s at about 321% CPU. An earlier process on the same pipe (pid 8783, started 03:47Z) was already 900MB at 27s and a still earlier one was about 4.3GB. The 100MB heap setting is present and does not hold RSS near 100MB.

## Build durations

`Time Elapsed` on `Antiphon.Tests.csproj` / `Antiphon.SessionRunner.Tests.csproj`, deduped. Cold versus warm is **UNOBSERVED** (the logs do not label it; `--no-incremental` was not consistently in the 110 characters before `Time Elapsed`). Load at the start of each build is **UNOBSERVED**. Slot `held=` in task reports was not a usable series.

| Project | Window | n | p50 | p95 | min | max |
|---|---|---:|---:|---:|---:|---:|
| Antiphon.Tests | before 2026-11-15 23:57Z | 114 | 242.9s | 395.9s | 1.1s | 565.5s |
| Antiphon.Tests | after | 21 | 501.3s | 919.4s | 220.5s | 1227.1s |
| Antiphon.Tests | after, session start before 2026-11-17 00:30Z | 11 | 438.4s | 743.3s | 220.5s | 769.9s |
| Antiphon.Tests | after, session start from 2026-11-17 00:30Z | 10 | 567.7s | 1088.6s | 227.2s | 1227.1s |
| SessionRunner.Tests | before | 18 | 36.3s | 93.8s | 23.0s | 145.9s |
| SessionRunner.Tests | after | 3 | 114.3s | 124.6s | 43.3s | 125.7s |

The after `Antiphon.Tests` list, session start and seconds: 00:40Z 262; 03:39Z 356 and 438; 10:35Z 298; 15:06Z 221; 18:24Z 546; 18:57Z 717; 19:44Z 770; 20:21Z 447; 21:54Z 379; 23:12Z 447; then 17 Nov 00:33Z 541, 00:51Z 725, 01:07Z 418, 01:11Z 227, 01:34Z 919, 02:20Z 790 and 594, 02:20Z 512, 02:41Z 501, 03:33Z 1227.

n=21 and n=3 are too small, and they are not matched to the before rows (different commits, unknown incremental state, unknown load). The after p50 is about double the before p50 even before the 17 Nov 00:30Z cluster. That is a real difference in this sample. It is not a causal result for the policy. Some after builds (221s, 227s, 262s) sit on the before p50.

`Antiphon.Agents.Pty.Tests.csproj` has before rows only (n=5, p50 24.6s). No after row was tied to that project name.

## Failures

After the cutoff, in the same transcript set:

- `Build FAILED`: 0 files.
- `LINUX COMPILER fallback=stock reason=`: 0. The only such line is in the 2026-11-15 04:45Z CARD-1906 rollout and is truncated at `reason=`.
- `error CS`: 2 files, both `error CS1503` (argument type mismatch in the project being compiled: the 00:40Z and 18:16Z rollouts). Not a compiler crash.

Before the cutoff, `Build FAILED` appears for ordinary project failures (missing generated file, `MSBUILDDEBUGPATH`, `Antiphon.SessionRunner.csproj`) on 2026-11-14/15. Those predate the trial.

No transcript line matched a VBCSCompiler timeout or a compiler-cache integrity failure during the trial window. Task-report search of the 300 server2/server2-temp builds did not surface one either.

## CPU and the shared server

`GET /api/hosts/server2/load` at 2026-11-17 03:46Z: `current` null, last-hour `cpuPercent` validCount 0/720, decision reasons `sample_stale`. `GET /api/hosts/server2/load/history?from=2026-11-16T00:00:00Z&to=2026-11-17T04:00:00Z&resolution=hour` returned `items: []`, `complete: false`, `gaps: ["binding-unknown"]`. Host load 36–68 on 2026-11-17 01:xx is **UNOBSERVED** on this route.

`GET` of `ANTIPHON_BUILD_SLOTS_URL` + `/metrics` returned **405**. Live `GET` of the slot URL showed occupied 2, budget 4, waiters 0, at one sample. Historical grant/hold statistics are **UNOBSERVED**.

The point sample that does exist: one shared compiler, affinity 0–5, about 3.2 cores, RSS 4.56GB. A 6-core cap cannot be the source of host load 36–68. The CARD-1885 baseline on an uncapped private compiler reached 12.4 rolling-minute cores and 6.3GB RSS (`docs/investigations/2026-11-13-card-1885-code.md`). One capped server is the benefit the policy is for. The same single server is also a queue: many builds share it, and the higher after-cutoff wall times are consistent with that contention plus host load. Which share is the policy's is **UNOBSERVED**.

## Temp runner

Task list since 2026-11-13: server2 339, server2-temp 17, desktop 61. Temp tasks are rollout canaries and a few Code/Review/Merge rows, almost all before the cutoff. The three temp completions after 2026-11-15 23:57Z are a CARD-1514 canary plan, a CARD-1955 review, and a worktree-cleanup custom task. Their reports have no `Time Elapsed`. Temp-runner transcripts and its `/tmp` compiler cache are not on this container. Comparable policy-0 versus policy-1 builds are **UNOBSERVED**.

## What default-on would change

Not done in this task. The current defaults are the Compose interpolation `:-0` (`docker-compose.server2-runner.yml:67`, anchor `RunnerLinuxCompilerPolicyDefault`) and `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0` (`scripts/c590-remote.sh:1133`). Saved `stack.env` values of exact `0` or `1` win over those defaults. Opt-out remains exact `ANTIPHON_LINUX_COMPILER_POLICY=0` in the saved file, then a named-phase recreate. `Enabled` still requires the string `1`, so a missing environment variable stays off even if a comment says otherwise. CARD-1950 also names changing `Enabled` so a missing value is on; that is a wider change than the Compose default, and the tests in `RunnerLinuxCompilerPolicyTests` pin the missing-key and Compose-absent cases to `0`.

Windows: `Enabled` returns false when `windows` is true, before the setting is read. The server2 Compose key is not the desktop AppHost. Windows impact of this flip: none for the Linux launcher. The separate Windows build-memory policy in `GetOverrides` (`UseSharedCompilation=false` and the GC/MSBuild-server switches) is already independent of this variable.

Risks if the default flips before the gate:

1. Per-session `XDG_CACHE_HOME` copies the same 44MB toolset (81 copies, about 3.6GB on `/tmp`) even though the pipe name is shared. Copy `prepareMs` median is under a second; the cost is disk, not the first build.
2. An SDK directory name other than `10.0.404` falls back to the stock compiler (`BuildDriverEnvironment.cs:219`). The image is on 10.0.404 now. A later SDK bump silently disables the policy.
3. `DOTNET_GCHeapHardLimit=100000000` does not keep the shared server near 100MB. Observed RSS 4.56GB, under the CARD-1903 8GiB figure and far above the reduced-project 0.322GiB result. CARD-1903's solution A/B is still not done.
4. Unmatched `Antiphon.Tests` p50 rose from 243s (n=114) to 501s (n=21). If that survives a matched A/B, default-on needs an explicit operator acceptance under the CARD-1950 gate.

## Not done, noted

One shared cache root would stop the per-session 44MB copies; the pipe is already shared.

## Evidence not obtained, ranked

1. Matched cold and warm `tests/Antiphon.Tests` pairs, policy 1 versus policy 0, with `Time Elapsed` and load1 at start. Obtain by running the 6+6 commands in "Extend" below on the standing runner and on the temp runner (or one opt-out session) in the same load band.
2. Host CPU, run queue, and compiler minute-cores for the trial window. `GET /api/hosts/server2/load/history` answers `binding-unknown` and an empty item list. Obtain by fixing the server2 host-load binding (CARD-1893 history path / physical host key) and re-reading `topConsumers` where `role=compiler`.
3. Build-slot hold series (admission wait versus execution). The shared broker answers `GET /build-slots/metrics` with 405, the same pin noted in `docs/testing-and-build.md` for a pre-CARD-1489 broker. Obtain by moving `BUILD_SLOTS_SHA12` to a build that serves `/metrics` and recreating `build-slots` while no lease is held.
4. Temp-runner (policy 0) build logs. They are not in this container's `/tmp` or `/state`. Obtain from the temp container's transcripts or from a deliberate paired build.
5. Container env at 2026-11-15 23:57Z. This container was not given a Docker socket inspect. The first build-driver diagnostic is 2026-11-16 00:40:43Z. Obtain with the `docker inspect` line in `docs/docker-stack.md:969` on the standing runner container.
6. Per-build cold/warm label and the test pass/fail counts beside each `Time Elapsed`. The 110-character window before `Time Elapsed` usually names the csproj and not the command. Obtain by keeping the `dotnet build` argv in the same evidence row as `Time Elapsed`.

## Extend

Keep standing server2 at `ANTIPHON_LINUX_COMPILER_POLICY=1`. Do not edit the Compose default. For the next measured window (one day is enough if the pairs are actually run; another ambient day is not):

- Six `dotnet build tests/Antiphon.Tests -p:UseAppHost=false --no-incremental` and six warm builds of the same project (one throwaway warm-up, then the timed build, same obj) on the standing runner.
- The same twelve commands with policy `0` (temp runner, or one session whose environment is exact `0`).
- Record, per command: start time, load1, full `LINUX COMPILER` line or its absence, `Time Elapsed`, exit code.
- Call the gate only when each cell has n≥6 and the policy-1 p95 is compared with the policy-0 p95 at a recorded load1. Until host-load history returns rows, also record `ps` RSS and `%CPU` of the `ac1885-` pipe at the start and end of each pair.

## next stage

next: decide
handoff: Extend the CARD-1950 server2 compiler trial; do not default-on and do not revert. Policy=1 is in use (shared VBCSCompiler pipe ac1885-1ff7756, cpus 0-5, SDK 10.0.404, no trial-window fallback). Unmatched Antiphon.Tests p50 243s (n=114) to 501s (n=21). Run 6 cold + 6 warm builds on policy 1 and 0 with load1 before any flip. Host-load history is binding-unknown.
artifact: docs/investigations/2026-11-17-linux-compiler-policy-trial.md
