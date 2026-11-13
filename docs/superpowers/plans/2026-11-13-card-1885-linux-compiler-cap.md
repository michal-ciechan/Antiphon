# CARD-1885: bound the Linux shared compiler

Plan and folded verification design, 2026-11-13. Task `54d9f0fb`.
Repository base: `3dc5e5c335c9ea0e3d622b042ebf809584ce0117`.

Recommend **compiler-only scheduler affinity**, installed through a private, immutable copy of the selected SDK's Roslyn tools. Its `VBCSCompiler` launcher applies the granted CPU count before exec. Preserve shared compilation and its warm cache. Add a compiler-only managed-heap ceiling and a finite idle lifetime. Do not impose affinity or GC settings on MSBuild, tests, the session runner or agent CLIs.

This is an implementable recommendation, not a claim that the throughput/RSS acceptance gates have passed. A scratch experiment proved the launch mechanism on SDK 10.0.404. The full solution comparison remains required before enabling the policy by default. Code must report a failed acceptance gate, not relax the ten-percent limit.

## Evidence and ground truth

Read the complete investigation on `origin/feat/card-task-fdf6e86b`, commit `428903d83a2ac55c178612e1105a1075f4162803`, at `docs/investigations/2026-11-13-task-fdf6e86b-server2-runner-cpu.md`, and live Antiphon cards CARD-1884 and CARD-1885. CARD-1884 owns host/process observability; this card does not change its API.

| Card assumption | Code/measurement fact | Consequence |
|---|---|---|
| The runner is consuming the CPU | The 895-second trace measured the runner at 0.184 cores, compiler at 5.330 cores, peak compiler minute 13.00, final RSS 9887.8 MB | Target compiler startup; no runner polling changes |
| A six-node build grant bounds the compiler | `BuildSlotBroker` grants admission and `MaxCpuCount`; it never starts the command. `scripts/build-slot.ps1` and checkpoint drivers start it locally. `-maxcpucount` controls MSBuild nodes | The cap must be attached where Roslyn starts its long-lived process |
| A compiler belongs to its first lease | The observed compiler was reparented to PID 1 and served several leases | Never release/kill it when that lease ends; do not attach it to an expiring lease cgroup |
| Linux already receives build-driver memory policy | `BuildDriverEnvironment.GetOverrides` returns empty on Linux; the PowerShell adapter also returns before loading it | Preserve the Windows policy and opt-out exactly; add a separate Linux command preparation path |
| Environment can set any SDK property | SDK `Microsoft.NET.Sdk.BeforeCommon.targets:379-380` assigns `RoslynTargetsPath` and `RoslynTasksAssembly` unconditionally for Core | Pass both as MSBuild global command-line properties, not environment properties |
| `RoslynTargetsPath` alone relocates server startup | First scratch attempt built successfully but launched the original SDK server with 24-CPU affinity and a 600-second idle timeout | A successful build alone is a false positive; inspect compiler identity, affinity and Roslyn request logs |
| `DOTNET_PROCESSOR_COUNT=6` is a hard CPU quota | It changes runtime processor-count-dependent heuristics; GC and other threads still exist | Use it only to align runtime heuristics with a kernel affinity limit |
| `VBCSCOMPILER_KEEPALIVE` is an established .NET 10 setting | No supporting installed SDK documentation or published Roslyn contract was found. This SDK accepts server `-timeout:<seconds>`; its logs confirmed 10000 ms in the probe | Use the measured server argument, never ship the speculative environment name |
| A writable `cpu.max` is available | This container has v1 controllers plus a v2 entry; the named leaf is absent from the mounted controller tree, as in the investigation | No privileged cgroup manipulation or assumption of systemd inside the runner |
| Disabling sharing is an inexpensive fallback | CARD-0795 measured 278 s versus 153 s for serial Antiphon.Tests builds, +81.7%; the investigation records that it was not shipped | Keep sharing; do not repeat a costly solution experiment just to reconfirm this rejected default |
| The cap necessarily saves 1.7 average cores | That figure truncates old minute buckets and ignores delayed work | Treat it as a counterfactual peak reduction, not a measured reduction in total CPU work |

### Plan-time SDK probe

`dotnet --list-sdks` returned `10.0.404 [/usr/share/dotnet/sdk]`. The installed Roslyn informational version is `5.9.0-1.26529.119+2ab2854867733112c84ac12c4273d7970ac3afdf`; its VBCS runtime config targets net10.0/runtime 10.0.15 with server GC enabled. Attempts to fetch the matching source commit were unavailable; upstream main is explanatory, not version-matched proof.

A literal private scratch root `/tmp/card-1885-plan-probe` held a package-free net10.0 library with 800 trivial types and a copy of the SDK's Roslyn directory. Only the copied `bincore/VBCSCompiler` was replaced with an executable launcher. No installed SDK file or live compiler was changed. Each of five scratch builds took a separate real build-slot lease and used native `bin/` and `obj/`.

The corrected invocation supplied both `-p:RoslynTargetsPath=<copy>` and `-p:RoslynTasksAssembly=<copy>/Microsoft.Build.Tasks.CodeAnalysis.dll`, plus a task-private `SharedCompilationId`. The launcher executed `taskset -c 0-5 dotnet exec <original-sdk>/Roslyn/bincore/VBCSCompiler.dll -timeout:10 ...` with `DOTNET_PROCESSOR_COUNT=6` scoped to that process.

| Probe | Build-reported wall | Wrapper-inclusive wall | Observed outcome |
|---|---:|---:|---|
| Initial incomplete override | 5.71 s | not retained | Build succeeded, but not proof of capping |
| Uncapped private pipe, cold / warm | 5.64 / 2.92 s | 8.554 / 5.332 s | PID 19058, original SDK apphost, CPUs 0-23, 600000 ms idle |
| Corrected copied-toolset launcher, cold / warm | 6.97 / 3.53 s | 9.174 / 5.503 s | Both requests served by PID 28463, CPUs 0-5, processor-count environment 6 |

The corrected compiler logged `Keep alive hit` at 14:10:13.878Z, about ten seconds after the second build request completed, and its process exited. The earlier task-private uncapped server was shut down by its exact private pipe; no global `dotnet build-server shutdown` was used. All probe builds passed with zero errors/warnings. There were no test-suite runs. These tiny, loaded-host samples are neither solution-throughput results nor p95 measurements: their apparent slowdown cannot be accepted or extrapolated away. RSS stress and the proposed heap ceiling were not tested.

Primary references: [Roslyn compiler-server behavior](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Compiler%20Server.md), [server process creation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Shared/BuildServerConnection.cs), [MSBuild task shared-pipe selection](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/MSBuildTask/ManagedCompiler.cs), [server timeout parsing](https://source.dot.net/VBCSCompiler/src/roslyn/src/Compilers/Server/VBCSCompiler/BuildServerController.cs.html), [runtime processor count](https://learn.microsoft.com/en-us/dotnet/api/system.environment.processorcount?view=net-10.0), [GC limits](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/garbage-collector), [thread-pool settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/threading). Installed-SDK behavior above takes precedence over examples from upstream main.

## Decisions

### D-1: select a compiler-only launcher, rather than a whole-build limit

Implement option (c) using scheduler affinity at exec, without a broker API change. Make a private copy of the selected SDK Roslyn directory, replacing only its server apphost with an ASCII launcher; select its task assembly through the two measured global properties. The launcher execs the original, version-matched compiler DLL. Keep Csc/Vbc tasks, analyzers, reference assemblies and compilation options otherwise unchanged.

Choose the first N logical CPUs from the calling process's **actual allowed CPU set**, N = min(the broker grant's MaxCpuCount, allowed CPU count). Never assume CPU zero exists, a contiguous mask, 24 CPUs or a host name. All callers with the same allowed set choose the same subset: multiple policy/server generations cannot get disjoint six-core pools. A more restricted parent must never widen its affinity. Record the chosen set; differing caller affinity is a qualification failure for a purported single shared pool, not a reason to silently create more pools.

The broker remains admission-only, budget four unchanged. A shared compiler is scoped to a user/container/SDK/policy generation, not to one lease or worktree. One executable can serve all builds selecting that generation. Use a stable private pipe identity derived from those inputs, not the default SDK pipe. Roslyn's existing startup mutex arbitrates simultaneous clients. Every automatic replacement uses the same capped launcher; a prestarted server alone would leave an uncapped auto-respawn race.

Rejected alternatives:

| Option | Benefit / measured trade-off | Decision |
|---|---|---|
| (a) `UseSharedCompilation=false` | Known +81.7% serial-build cost on CARD-0795; no current-SDK solution remeasurement | Reject default and automatic fallback |
| (b) processor-count / thread-pool environment alone | Small change, may reduce threads/heaps; no measured hard six-core ceiling. Max worker threads excludes GC and native threads and can delay concurrent requests | Reject as sole CPU control; retain processor count only inside capped compiler |
| (c) compiler-only affinity launcher | Probe proves hard placement before runtime startup, warm sharing and idle exit; solution cost unknown | Selected; no privileges, host cgroup mounts or live PID mutation |
| (c) `taskset` around the entire build | Easy inheritance, but constrains MSBuild, generators and child tests as well; multiple builds compete in the same small pool | Reject wider effect |
| (c) compiler cgroup / `systemd-run --scope` | CPU quota can distribute work over all CPUs and `memory.max` can bound RSS; controller delegation is unavailable here, per-lease scope lifetime is wrong, cross-container broker cannot own this PID | Defer unless the selected approach fails acceptance; needs a separate deployment design |
| (c) discover a live compiler and change its affinity | Avoids toolset copy, but races startup/threads/PID reuse and alters other tasks | Reject |
| (d) idle timeout / shutdown alone | Releases retained memory between bursts; does not limit active CPU or continuous-load RSS | Use a measured idle argument as a companion; never periodic global shutdown |
| (e) count compiler as a weighted build slot | Could reserve capacity but shared requests cannot be attributed to a single lease; a long-lived extra lease can block its own clients and does not impose a CPU cap | Defer broker accounting; report compiler CPU separately in acceptance |

### D-2: resource defaults and boundaries

Use these stated implementation defaults, all covered by tests:

- `ANTIPHON_LINUX_COMPILER_POLICY=1`: opt-in during implementation/measurement; make default-on only in the final enabling slice after acceptance passes. Exact `0` is rollback. Windows ignores this policy entirely.
- `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`: transport the existing grant into command preparation where needed. This is not a new broker field. Prefer the typed grant at the caller; validate a positive integer. Existing non-dotnet wrapper warning semantics stay intact. Do not export a forged grant to test/agent children. Unleased/unlimited existing paths use their already selected fallback count, and report that provenance.
- Compiler child only: `DOTNET_PROCESSOR_COUNT=N`, `DOTNET_GCHeapHardLimit=100000000` (hexadecimal 4 GiB), and server argument `-timeout:120`. Keep server GC enabled and do not set `DOTNET_gcConcurrent`, worker-thread minimum/maximum or Windows memory-policy keys.
- The heap limit bounds managed heap commitment, **not RSS**. Acceptance additionally requires peak aggregate managed-policy compiler RSS <= 8 GiB and release after the 120-second idle period. A universal OS-enforced RSS cap is not claimed. If 8 GiB cannot be met, this design fails memory acceptance; do not call a managed limit an RSS limit.
- Never set `UseSharedCompilation=false`, change an explicit caller sharing choice, or override explicit custom compiler/toolset properties silently. For an explicit unsupported toolset or conflicting pipe/property, return a clear policy refusal when enabled; policy-off retains current behavior. No machine/user environment writes.

Only commands that can compile are prepared (`dotnet build`, `publish`, `pack`, `msbuild` target execution, `test`/`run` with implicit build). Evaluation-only `-getProperty`/`-getItem`, `--no-build` tests, CLI identity reads, agent executables and normal application launches remain outside the policy. Preserve literal argv and the `--` application boundary. Existing implicit-build warnings stay; direct callers should build first and run `--no-build`.

### D-3: immutable toolset cache and exact launch integration

Put Linux command preparation beside the existing shared helper, in `scripts/lib/BuildDriverEnvironment.cs`, retaining its C# 5 / Windows PowerShell 5.1 compilation contract. Separate its pure policy/argv functions from filesystem/process work. The existing `GetOverrides` Windows dictionary remains unchanged. A process-start overload prepares Linux **after full argv exists**; Windows returns through its current path. Use runtime-safe access where APIs are absent from Windows PowerShell's framework, and test compilation under the existing adapter harness.

Resolve the actual SDK selected for the command's working directory/global.json through a bounded evaluation/identity probe, never the highest installed SDK. Resolve the native taskset and dotnet executable paths. Validate the apphost/DLL/task assembly/runtime config, copy the complete Roslyn toolset once into a user-owned cache outside source/output directories, write a regular executable launcher, and atomically publish a manifest with SDK/content/policy identities. No symlinks to writable SDK files, no replacing the installed apphost, no global SDK mutation. Immutable cache entries are shared across worktrees; an exclusive bounded creation lock prevents partially published entries. Reject symlink/untrusted-owner/cache-integrity mismatches and SDKs not yet qualified. A newly selected SDK needs the native witness before activation.

The cache key includes SDK content identity, UID/container scope, selected CPU set, heap ceiling, idle timeout and launcher version. Do not include lease IDs or checkout paths. Prepare a cache hit cheaply; record first-copy cost separately. Changing a CPU grant/policy can create another immutable generation, but every generation must fit the same CPU subset; measure aggregate RSS and disclose transition overlap. Routine builds never delete a live cache entry. Existing retention/explicit maintenance can later remove unreferenced entries; this card adds no sweeper.

Append both global toolset properties and the stable `SharedCompilationId` before process creation. Include the normalized policy and SDK identity in checkpoint build provenance; actual launch arguments must remain inspectable. Never count a test-only fake taskset as native proof. A missing utility/invalid cache refuses the opted-in build with a bounded diagnostic before compiler launch; a compiler failure can still cause Roslyn's own stand-alone fallback. Acceptance must detect such fallback and fail, rather than interpreting a successful fallback build as a capped shared build.

Integration census:

| Launch path | Change / invariant | Named witness |
|---|---|---|
| `scripts/build-slot.ps1` | Call preparation on complete child ProcessStartInfo after grant; retain scoped Windows environment and exact exit/release behavior | `BuildSlotScriptTests`, new offline policy harness |
| `scripts/lib/build-driver-environment.ps1` | Permit the Linux full-command path; preserve GetOverrides/Enter/Exit Windows and opt-out behavior | existing `scripts/test-build-driver-environment.ps1` plus new harness |
| `scripts/run-checkpoint.ps1` | Existing Set helper sees complete build argv; wire typed grant if needed and preserve stamp/binding properties | `RunCheckpointScriptTests`, receipt/binding witnesses |
| `tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs` | Existing Apply occurs after args/environment; central helper handles Linux. Prefer no call-site edit | pure launch-policy tests, native checkpoint witness |
| `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`, `BuildStep.cs` and build-binding code | Audit actual launch properties versus recorded properties; add normalized policy identity at the existing binding boundary if absent | `CheckpointColdBuildTests`, `CheckpointReceiptSafetyTests`, `CheckpointTerminalReceiptTests` |
| `server/Application/Services/AgentTaskLandService.cs:RunProcessAsync` | Existing Apply sees complete argv and an existing `-maxcpucount` grant; central helper applies without a landing-service source change | `LandVerificationBuildSlotTests`; compile existing server consumer |
| graph evaluation / nightly / watched tests | Pure evaluation and no-build application execution do not receive compiler limits; actual Linux builds must use the same preparation path | negative command classification tests; keep Windows policy harness |

Do not edit the landing service solely to duplicate policy. If a checkpoint binding edit is necessary, add its exact affected tests before implementation; the table below already includes the checkpoint-tool high-radius receipts. There is no permission to change admission, leases, public DTOs, operation labels or remote deployment.

### D-4: activation and placement

At plan time `GET /api/runner-defaults` revision 2 selected the available Linux runner as global default. `GET /api/session-runners` showed the Linux runner available (8/10 sessions), the Windows runner available, and the temporary runner retired. These are observations, not embedded fleet configuration. Resolve both routes again at dispatch. Checkpoints below name Linux/portable/Windows lanes. Omit `-Runner`; use `-Platform Linux` only for native Linux qualification, Windows only for the Windows regression slice. No fleet URL, mount or host name is compiled into the policy.

First qualify on the assigned Linux worktree using private compiler pipes, independently gated builds and no changes to existing sessions. Default-on changes affect new build drivers only. An old SDK-default compiler can coexist until its clients finish and its existing idle timeout elapses. Do not kill, renice, taskset or shut it down. Report activation as transitional until old uncapped requests cease; aggregate acceptance must not hide that process. Rolling service activation remains the caller's separate operational task; this Plan/Code task does not restart runners, Docker or the live Windows qualification.

Rollback sets the Linux policy to `0` for future builds; existing capped compilers leave at idle. This does not promise immediate memory release or eliminate transition overlap. No session that waits for input is added or changed.

## Slices

1. **S1 — policy tests red, then pure preparation and private launcher.** Files: `scripts/lib/BuildDriverEnvironment.cs`, `scripts/lib/build-driver-environment.ps1`, new `tests/Antiphon.Tests/Infrastructure/LinuxSharedCompilerPolicyTests.cs`, new `scripts/test-linux-shared-compiler.ps1`. Keep fixture implementation in that script or a fixture under `scripts/fixtures/`; do not add a remote deploy script. The pure class tests classification, preserved Windows settings, SDK/cache identity, CPU-list parsing (including sparse sets), property conflicts and explicit opt-out. Commit/push the meaningful failing assertions before the implementation; no stub or setup error counts as red.
2. **S2 — driver integration and native proof.** Files: `scripts/build-slot.ps1`, `scripts/test-build-slot.ps1`, existing helper/adapter; `scripts/run-checkpoint.ps1` and checkpoint binding/BuildStep/RunScheduler files only where needed for provenance. Extend `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs` and `RunCheckpointScriptTests.cs` only for changed behavior. Use existing linked C# source in server/checkpoint projects; no new project/reference or landing-service edit is planned. Add `scripts/measure-linux-shared-compiler.ps1` for the reproducible private-pipe measurement (one build per invocation, never a build loop inside one lease). Commit/push before long runs.
3. **S3 — qualify and enable.** Run the bounded measurement design below at the committed S2 SHA. Write essential results/provenance as an individual Markdown report under `docs/investigations/`; keep raw JSON/CSV/TRX/logs ignored. If CPU, memory, fallback or wall-time acceptance fails, retain opt-in and return the evidence for a design decision. If all pass, enable the tested policy default, update `docs/testing-and-build.md`'s build-slot section, add its narrow documentation assertion, commit/push and run the final applicable checkpoint rows. An enable-default edit requires a policy/default test and fresh final-SHA qualification, not relabeling S2 receipts.

## Existing tests affected

Apply [CARD-1782's impacted-classes table](../../testing-and-build.md#impacted-classes-card-1782) to the **actual** base..HEAD diff, including the `added` rules. Migrations, public DTOs/routes, operation labels, remote script functions and statement budgets: **none** in this design. The standalone local compiler harness is not a remote deployment function.

`tools/Antiphon.Checkpoints/**` selects the checkpoint-tool row: `CheckpointReceiptSafetyTests`, `CheckpointTerminalReceiptTests`, `HostCleanupCheckpointReceiptTests`. New/changed Antiphon.Tests source selects `TestLaneCategoryGuardTests`. Avoid adding process-spawning work to the pure Unit class; native proof is a command row. If a TUnit process-spawning test becomes necessary it must use the assembly limiter and adds the table's ProcessSpawnLimitTests rows for each owning project, executed sequentially. Do not silently broaden a closed filter.

Existing affected regressions explicitly selected: `BuildSlotScriptTests`, `RunCheckpointScriptTests`, `CheckpointColdBuildTests`, `CheckpointManifestDocumentationTests`, `CheckpointRepeatDocumentationTests`, `LandVerificationBuildSlotTests`, and the checkpoint receipt classes above. Windows wrapper cases with an OS skip on Linux remain owed on Windows. The nine-class standing guard union is included below. Source roster inspection found no suffix collisions among its class prefixes or the selected existing regression classes; the two proposed class names are unused. Shared-source guard classes must be run in the named project, not guessed across assemblies.

## Performance budget

Apply [docs/testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards) and its R1-R8 owner. This Plan ran five small gated scratch builds, not the checkpoint suite. There is no measured p95 for the new policy, the native fixture, the selected regression batch or a standard solution build on this SDK. **Every new estimate below is provisional under CARD-1885**, with collection and adjustment required before qualification credit.

- App/category graph: build tooling shared by checkpoint CLI and server; pure policy/guard tests are Unit, wrapper/receipt/landing regressions retain their existing Integration categories. Native compiler acceptance is the Linux build-tooling batch, not session/pty runtime. Compile both existing consumers via Antiphon.Tests' project graph; retain the resolved ProjectReference closure in receipts. No client/browser/DB behavior changes.
- Reuse copied SDK tools and package restore once per run; independent native outputs are one per caller worktree. Native fixture uses one scratch solution with two dependent package-free projects in **one build**, proving two requests use the same server. No AppHost, database, migrations, Docker startup or seed setup is needed. Existing regression shared fixtures retain assembly/run lifetime; do not introduce per-test containers.
- Record admission wait, SDK resolution, first toolset copy/cache hit, restore, MSBuild/compilation, compiler idle wait and teardown separately. Per-test TRX and exact roster plus correlated JSONL phase records go to ignored `.antiphon/c1885/<run-id>/`. Record host load, grant, SDK, SHA, platform, CPU set, source-clean state and all compiler PID/start identities. Mark uninstrumented existing phases unknown/remainder; a missing span is never zero. Warm/cold is a measured condition, not a label inferred from exit code.
- Historical anchor: CARD-0795's 153/278 s applies to Antiphon.Tests on an older SDK; CARD-1629's native cold/unchanged builds were 220.3/15.2 s. Neither is a p95 for `Antiphon.sln`. Scratch native cold/warm were 6.97/3.53 s; only two samples. Use these to choose a bounded first collection, not to claim calibrated production timeouts.
- Initial bounds: SDK/identity HTTP or CLI 15 s each; cache preparation 30 s; lock acquisition 30 s separately; scratch build 30 s (provisional roughly twice a 15 s bound including loaded-host variation), fixture idle 10 s + 5 s exit/drain. Production idle 120 s + 10 s observed-exit allowance. Pure assertions <1 s each, wrapper scenarios provisional <=30 s each; record actual p50/p95/max after the first run and set subsequent timeouts approximately twice measured p95. No repeated full runs merely to grow a sample.
- Row estimates below include builds. Configure explicit row/build deadlines around twice the first measured p95, keeping admission separately finite (5-minute qualification queue budget, exit 4 means not run). Until those samples exist use at most 2x the provisional row estimate; document the temporary exception under CARD-1885. Do not inherit the importer's 15-minute/3x floors as measured budgets. Total execution uses `DeriveTotalMinutes`; do not insert a total below `2*sum+10` without the required override reason.
- Run ordinary policy/regression rows and native acceptance as separately identified runs because they have different fixtures and timing questions. Expected ordinary cost is about 20 minutes including one shared test build; native fixture about 1 minute. The **full solution performance selection is cost-unknown**. Its first paired baseline/candidate collection is bounded to 20 minutes execution; use its durations to cost the remaining samples before launching them. If any selection, combined batch or run is expected/observed above 30 minutes, stop expanding it and commission a Debug task with exact selection/SHA/timings/load. Do not hide an over-30-minute selection by splitting its report or rerunning it unchanged.

## Verification design

The brief explicitly requests next `code`; verification design is folded into this plan. Code executes ordinary V/R; ordinary Review judges it separately before land. Post-land SourceLanding Mutation owns deliberate mutants and restoration, not this Plan task.

### Behavioral assertions

- **V-1 policy:** `LinuxSharedCompilerPolicyTests` includes `Only_compiling_commands_receive_toolset_properties`, `Windows_and_optout_preserve_existing_policy`, `Sparse_affinity_is_intersected_with_grant`, `Cache_and_pipe_identity_are_stable_across_worktrees`, `Conflicting_toolset_is_refused`, `Untrusted_or_incomplete_cache_is_refused`, `Compiler_limits_do_not_leak_to_driver_environment`, and `Default_enablement_requires_qualified_policy`. No OS process is required for these pure cases.
- **V-2 adapter:** offline script cases run the real wrapper with fixture-local broker/launcher seams and assert literal argv, grant provenance, command classification, retained exit status, release on preparation failure, parent environment restoration, both global properties and no test/daemon leakage. Windows memory-policy harness retains all original scenarios and exact opt-out semantics.
- **V-3 native:** the script's Native case builds two dependent projects once on a private pipe. Observe the original compiler DLL behind the copied launcher; at least two successful server responses share one PID/start identity. Verify every thread's allowed CPU set is within the chosen N set, driver affinity unchanged, UseSharedCompilation not disabled, no stand-alone fallback, and automatic idle exit within 15 seconds with fixture timeout 10. Before/after process inventory proves no task-owned residue. This intentionally uses a fresh server once because startup/idle are the subject.
- **V-4 resource acceptance:** the measurement protocol below proves real solution throughput, aggregate compiler CPU/RSS and continued warm reuse under two or three independent concurrent build leases.
- **R-1:** exact Windows policy script and wrapper regressions; Windows-native cases are owed to the Windows lane, never credited from Linux skips.
- **R-2:** checkpoint source/build binding, receipts, cold-build and wrapper regressions plus LandVerificationBuildSlotTests; existing admission and no-build behavior preserved.
- **R-3:** documentation assertions, standing nine-class guards and actual CARD-1782 selection.

### Measurement protocol and acceptance (V-4)

Use an assigned Linux worker on the host under investigation, selected from live runner metadata. Do not move or stop other tasks. The measurement script has one build per invocation and no lease acquisition of its own when invoked as a checkpoint command; every invocation is owned by its checkpoint's lease or a single `build-slot.ps1` wrapper, never both. Record the actual grant; supply that `-maxcpucount` explicitly to script-internal dotnet. Slot timeout is not permission to proceed unleased.

1. Record source SHA/cleanliness, SDK version/digests, actual build command, grant, CPU affinity and occupancy/free memory. Use the repository's normal Linux `Antiphon.sln` Debug build, `UseAppHost=false`, native outputs, `--no-incremental` for the comparison, identical properties in both modes. Separate restore once; no network variance inside timed build bodies. If this solution cannot build on this lane, retain the failing command and qualify a documented existing Linux solution selection through the caller; do not silently substitute the trivial probe or Antiphon.Tests.
2. Baseline uses an isolated **uncapped** private compiler pipe, candidate a different capped private pipe/toolset. Neither uses the live SDK pipe. Both use identical SDK/configuration and equivalent warmed caches; record the cold priming build separately. Explicit benchmark baseline bypass is scoped to that owned child and is unavailable in normal default-on callers.
3. Collect at least three paired baseline/candidate solution builds in balanced order after priming, one build at a time per worktree. Report arithmetic mean build-body wall time, ratio, individual values and uncertainty; report p50/p95 as provisional with this small sample. Compare matched occupancy/load windows; a mismatched load or cache state is inconclusive, not passing. Maximum collection is one priming pair plus three measured pairs; no indefinite repeats to get green. Cost this selection after the first pair and apply the 30-minute Debug rule above before continuing.
4. Concurrent witness: two, then at most three, owned independent worktrees/builds share the same candidate pipe and deterministic CPU set. Each holds its own lease, no nested wrapper lease or shared output. Run only when admission permits; never reserve extra session seats or tune host budgets. Use compile bodies lasting at least 60 seconds, or an explicitly repeated source workload **within one build invocation**; a quiet compiler does not prove the ceiling. Record per-build completion/wait and group makespan so a longer slot hold is visible.
5. Sample owned compiler `/proc/<pid>/stat` and `status` every second with PID/start-time validation, plus thread affinity. CPU cores = delta(utime+stime)/CLK_TCK/monotonic elapsed. Aggregate every owned compiler generation; account for birth/exit intervals instead of discarding them. Calculate rolling full 60-second windows. Retain residual live default compiler CPU as separate context, never attribute it to the private measurement. Do not use `/host-stats.cpuPercent` as compiler CPU.
6. All gates must pass: maximum rolling-minute candidate compiler CPU **<= 6.00 cores**, also <= the grant/allowed-set bound if smaller; mean candidate solution build-body wall **<= 1.10 x baseline**; peak aggregate candidate compiler RSS **<= 8 GiB**; all compiler threads stay within the selected set; at least two builds demonstrably use the same warm server; zero OOM/crash/stand-alone fallback; candidate compiler processes exit within **130 s** after their final active request at production timeout 120. Also report driver/server GC CPU, slot-hold duration and concurrent makespan; they may expose a failure concealed by the serial mean.
7. No claim of fleet activation until default-on builds use the new identity and old uncapped requests naturally drain. An idle-release bound cannot promise memory relief while seven sessions continuously compile. If any gate fails, keep opt-in, preserve evidence, and return `next: decide` with the failed metric and proposed adjustment; no widening of the cap, heap or ten-percent gate inside Code.

### Exact performance command inventory

The new measurement script implements these arguments. Each invocation is one build and owns only its private compiler identity. The working directory defaults to the invoking worktree; concurrent invocations use independent caller-owned copies of the same committed source with native outputs, prepared before the run and recorded by absolute path in its receipt. The controller must await every child. Do not launch detached work and return.

For direct execution, prefix **each** row invocation with `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <ID> --`. The script receives the grant through the wrapper's existing non-dotnet environment export and explicitly passes it to its one dotnet build. For checkpoint command execution omit that wrapper and pass the row's typed grant; there must be exactly one lease-owning layer.

| ID | Exact command after the gate prefix | Order / admission |
|---|---|---|
| P-0A | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Baseline -Phase Prime -Sample 0` | one cold baseline prime |
| P-0B | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Candidate -Phase Prime -Sample 0` | one cold candidate prime; cost remaining selection now |
| P-1A | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Baseline -Phase Measured -Sample 1` | A then B |
| P-1B | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Candidate -Phase Measured -Sample 1` | same load/cache class |
| P-2B | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Candidate -Phase Measured -Sample 2` | B then A |
| P-2A | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Baseline -Phase Measured -Sample 2` | same load/cache class |
| P-3A | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Baseline -Phase Measured -Sample 3` | A then B |
| P-3B | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Candidate -Phase Measured -Sample 3` | same load/cache class |
| P-4A / P-4B | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Candidate -Phase Concurrent -Sample 4` | exactly two independently leased invocations, separate roots, shared pipe |
| P-5A / P-5B / P-5C | `pwsh -NoProfile -File scripts/measure-linux-shared-compiler.ps1 -Mode Candidate -Phase Concurrent -Sample 5` | exactly three independently leased invocations if admitted |

Thirteen builds maximum, including primes and concurrent witnesses. This is an upper inventory, not an assertion that thirteen solution builds fit a thirty-minute selection. Apply the Debug stop gate after P-0A/P-0B cost the selection. Preserve inconclusive/failed attempts; extra attempts require a recorded reason and revised cost/selection before execution. The 120-second production idle setting means a server may expire while the opposite mode builds: observe the actual PID/cache state. If needed, give **both benchmark pipes** a longer, explicitly recorded temporary keepalive for the paired timing experiment, then test the production 120-second lifetime separately in the concurrent witness; never classify a respawned server as warm.

### Positive controls pending for SourceLanding Mutation

Keep controls method-scoped and only on a commissioned source snapshot. PC-1: drop one of the two global properties; the corresponding literal-argv assertion must fail. PC-2: ignore affinity intersection; `Sparse_affinity_is_intersected_with_grant` must fail. PC-3: make identity worktree-dependent; `Cache_and_pipe_identity_are_stable_across_worktrees` must fail. PC-4: apply compiler environment to the driver; `Compiler_limits_do_not_leak_to_driver_environment` must fail. PC-5: bypass Linux/opt-out guard; `Windows_and_optout_preserve_existing_policy` must fail. Native controls remove taskset or the idle argument only on a private fixture launcher; the native affinity/exit assertion must detect the defect, followed by exact restoration and fresh green proof. Fixture/setup failures, missing SDKs and zero tests are not expected red. Keep PC evidence separate from throughput acceptance and ordinary receipts.

### Checkpoints

Closed ordinary Code list. Import this table with the checkpoint tool, one run per committed slice group; wait until exit is not 75. Gate the tool bootstrap in native default output. Checkpoint-owned builds use the declared isolated outputs. No whole Unit/assembly run. Reuse CP-1's output only for rows with the same `After=all`; native command rows own one build each. Windows rows execute only on Windows; omit them from the Linux selection and list them as owed until their native result exists.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1885-a/` | policy-portable | `/*/*/LinuxSharedCompilerPolicyTests/*` | V-1 | all eight named methods, 0 failed/skipped | 8 | 9 | true |
| CP-2 | all | CP-1 | wrappers-linux | `/*/*/(BuildSlotScriptTests*)\|(RunCheckpointScriptTests*)/*` | V-2, R-1, R-2 | every portable/Linux method; Windows-only skips explicitly owed | 1 | 15 | true |
| CP-3 | all | CP-1 | receipts-portable | `/*/*/(CheckpointColdBuildTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointTerminalReceiptTests*)\|(HostCleanupCheckpointReceiptTests*)\|(LandVerificationBuildSlotTests*)/*` | R-2 | all listed classes, 0 failed; named platform-only skips recorded | 1 | 6 | true |
| CP-4 | all | CP-1 | guards-portable | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(CheckpointManifestDocumentationTests*)\|(CheckpointRepeatDocumentationTests*)\|(DerivedImpactIndexTests*)/*` | R-3 | all listed classes, 0 failed; recount parameterized results at import | 1 | 3 | true |
| CP-5 | all | n/a | environment-portable | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1` | R-1 | all existing child/direct cases, 0 failed | n/a | 1 | true |
| CP-6 | all | n/a | compiler-adapter-portable | `pwsh -NoProfile -File scripts/test-linux-shared-compiler.ps1 -Case Adapter` | V-2 | named positive and negative adapter assertions, 0 failed | n/a | 1 | true |
| CP-7 | all | n/a | compiler-native-linux | `pwsh -NoProfile -File scripts/test-linux-shared-compiler.ps1 -Case Native` | V-3 | two server requests; one build; native affinity, no fallback, idle-exit and no-residue assertions | n/a | 1 | true |
| CP-8 | all | `tests/Antiphon.Tests -> bin-c1885-w/` | wrapper-windows | `/*/*/BuildSlotScriptTests/*` | R-1 | every Windows/portable method; Linux-only skip recorded | 1 | 8 | true |
| CP-9 | all | n/a | policy-windows | `pwsh -NoProfile -File scripts/test-build-driver-environment.ps1` | R-1 | native Windows policy and PowerShell adapter cases, 0 failed | n/a | 1 | true |

V-4 is a required **separate bounded performance experiment**, not an unlisted correctness-test sweep. Its closed invocation inventory is the priming baseline/candidate pair, three measured pairs and the two-/three-builder witness described above, each through its own build lease. The new measurement script must emit one receipt per build with mode/pair/phase and no hidden build loops. Before execution, enumerate those concrete commands/owned worktree paths in the committed measurement report; report actual counts including incomplete/refused rows. No solution performance build is authorized beyond that inventory. Both CP results and V-4 acceptance are required before default-on; an unavailable Windows lane or missing throughput sample is incomplete verification, not a green plan claim.

## Risks and stop conditions

A six-core compiler can lengthen parallel builds and hold scarce slots longer. A heap ceiling can trigger GC pressure, compiler OOM or silent Roslyn stand-alone fallback, particularly with seven active sessions; the 9.9 GB observation makes this a real acceptance concern. Shorter idle lifetime trades retained RSS for cold-start cost. A copied toolset adds disk and SDK-version coupling; copies are immutable, initialized once and fingerprinted. Mixed old/new driver generations can temporarily retain two servers. Affinity counts logical CPUs and may select SMT siblings; preserve deterministic placement for the initial design and measure throughput before considering topology-aware placement. CPU restriction reduces bursts, not necessarily total compilation work.

Do not proceed by weakening assertions, raising the RSS/CPU limit, changing the ten-percent threshold, disabling sharing, reducing the operator's concurrency budget or touching the live compiler. If the solution budget cannot be met, report which measurement failed and commission the next design/debug decision. The Plan deliverable itself is complete once committed and pushed; production acceptance remains Code's explicit work.

--- next stage ---
next: code
handoff: Implement the compiler-only affinity launcher behind opt-in, preserve Windows policy and warm sharing, execute the closed correctness rows and bounded private-pipe solution experiment, and enable by default only after CPU, RSS, no-fallback and <=10% wall-time gates pass. Do not alter live compilers or restart services.
artifact: docs/superpowers/plans/2026-11-13-card-1885-linux-compiler-cap.md

## CARD-1885 commissioned Debug correction (17799819)

The 2026-11-13 Debug brief supersedes the solution-only measurement selection for
this bounded experiment. Use `server/Antiphon.Server.csproj`: a standard production
project with its reference graph, less expensive than the complete solution or
Antiphon.Tests. SessionRunner.Tests has an Exec that starts another build without
forwarding the private compiler properties, so it cannot isolate this comparison.
This smaller-project evidence cannot qualify default-on solution/fleet acceptance.

The previous CP-1..4 invocation explicitly set `rowTimeoutMinutes: 6`; this replaced
the imported shared-build deadline (18 minutes), despite zero admission wait. The
compiler policy was OFF. Keep one shared isolated build for these four rows. Do not
repeat the six-minute override. The next run uses an explicit provisional 16-minute
execution bound, approximately twice the observed uncapped 477.858-second solution
build, with a 30-minute hard wall cap. The timeout is not a measured p95: one
solution sample and a censored test build are insufficient. Collect one successful
checkpoint build and the three A/B samples, then record p95/sample limits and revise
the estimates from those measurements. This is a CARD-1885/R5 provisional exception;
no test assertion or acceptance threshold changes.

Each performance command retains its own build-slot lease. Pass
`-Project server/Antiphon.Server.csproj -ExperimentId debug-17799819
-CompilerCacheRoot /tmp/card-1885-debug-17799819-cache` to every invocation. Restore
once, then P-0A/P-0B prime, P-1A/P-1B, P-2B/P-2A, P-3A/P-3B. Record all attempts,
evaluate the 30-minute phase cost after priming, and stop expansion if it cannot
fit. No concurrent worktrees or production-idle witness in this authorized reduced
inventory. Shut down only these two private pipes after collection. Preserve the
original solution/concurrent/Windows obligations as unqualified; default stays OFF.

### Measured Debug refinement and reduced inventory

At executable source `191bbf25c83f0d13e8f30258a79bcdde8a662fd0`, the shared
build passed: MSBuild 303.530 s, checkpoint execution 393.929 s, admission 60 s,
wall 468.789 s. CP-1: 8 passed, 8.702 s execution / 23.771 s wall. CP-2:
44 passed, two native Windows skips, 772.874 s execution / 848.102 s wall.
CP-3: 45 passed, 197.667 s execution / 332.848 s wall. These are N=1 row
samples, not reliable p95 estimates. CP-1's estimate now includes its actual
build and row wall (492.560 s, rounded to 9 minutes); CP-2 rounds its measured
wall to 15 minutes. CP-3's existing six-minute estimate covers its observed
5m33s wall. CP-4 stays provisional until executed.

The earlier successful build at `93482b89885d4dbd1b1828d30875a3bdd4c055f7`
took 313.585 s execution. Nearest-rank empirical p95 of these two heterogeneous
successful samples is 393.929 s, **provisional**; 2x is 13.131 minutes. The
16-minute correction additionally covers twice the prior censored 458.070 s
execution sample (15.269 minutes). Preserve the CARD-1885/R5 exception; do not
call this a calibrated p95. The override also applies to rows because the CLI
couples their deadlines to the build deadline. No test's own timeout changed.

The first run was stopped through the supported tool command after CP-3, at
28m19s, while CP-4 still awaited admission. Its 97 passed cases and two native
Windows skips remain recorded; its terminal exit 130 remains canceled. CP-4's
no-build continuation was canceled at the five-minute admission bound before
any test began. Retain both outcomes. Split subsequent correctness invocations
into bounded selections and retain the verified build with `--keep-outputs`
where a later `row --no-build` consumes it; never repeat successful rows merely
to hide the canceled run. Full receipt/source qualification remains incomplete.

The server-project performance pilot cost 173.775 s build body plus 150 s
admission, peak compiler minute 8.086 cores, peak RSS 5,774,454,784 bytes,
zero fallback. Eight such invocations plus restore would exceed 30 minutes.
The pilot's private compiler was shut down by its exact pipe and an inventory
confirmed zero residue. No candidate or measured sample belongs to that pilot.

Replace the performance project with the existing standard
`tests/Antiphon.TimeoutAudit.Tests/Antiphon.TimeoutAudit.Tests.csproj` and its
real tool reference. It has two compilations and no nested build Exec. New
private identities use `-ExperimentId debug-small-17799819`, the same external
cache root, and evidence under `.antiphon/c1885-debug/small-performance`.
The closed commands are P-R (Baseline/Restore/0), P-0A (Baseline/Prime/0),
P-0B (Candidate/Prime/0), P-1A (Baseline/Measured/1), P-1B (Candidate/Measured/1),
P-2B (Candidate/Measured/2), P-2A (Baseline/Measured/2), P-3A (Baseline/Measured/3),
P-3B (Candidate/Measured/3), each through its own five-minute-wait build-slot
wrapper. Every command passes the literal project, identity, cache and evidence
arguments above. No hidden build loop. Restore is separate; cap the measured
collection at 30 minutes and reevaluate after priming. Report the pilot and all
admission/execution costs separately, not as successful paired samples.

For builds shorter than a minute, sample the private compiler through 61 seconds
from build start, including post-build idle. Receipts separate build-body wall
and post-build observation seconds. The rolling full-minute measurement is
real but does not prove a continuously active minute or the original concurrent
stress/production-idle gates. Do not extrapolate it to full-solution acceptance
or default enablement. Thresholds remain <=10% slowdown, <=6 minute cores,
<=8 GiB RSS and zero fallback; report small-N and load/cache limitations.

### Foreground compiler lifetime correction

The first small-project measured stock call was refused by its unchanged warm
assertion: its primed process was absent and a new PID/start identity served it.
Both primed pipes were gone across separate command lifetimes, well before their
600-second setting. The exact terminating actor is unproven. Retain that failed
receipt and both primes at source `e504feef864765df5ad30dbedc4be83f98403c0a`;
they are not three-pair acceptance.

Run `pwsh -NoProfile -File scripts/run-card-1885-bounded-experiment.ps1` in the
foreground, without an outer build lease. This local controller runs the nine
explicit commands above, with a distinct `debug-held-17799819` identity and fresh
`.antiphon/c1885-debug/held-performance` evidence. Each command independently
invokes `scripts/build-slot.ps1 -SlotWaitMinutes 5`; there is still exactly one
lease and one build per child. The standing controller owns compiler lifetime
across these child commands, awaits every child, checks the priming projection,
enforces the collection bound, and shuts down only its exact private pipes in
finally. No external executor, service, detached controller or global shutdown.
This is a repaired lifetime protocol, not permission to credit the failed cold
replacement or repeat until the throughput ratio passes. Collect exactly three
measured pairs after the new priming pair, preserving all earlier costs.
