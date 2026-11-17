# Compiler policy matched A/B on server2 (CARD-1950)

Verdict: **(b) policy 1 is materially slower. Cold `tests/Antiphon.Tests` builds take 2.2x as long (median paired ratio; ratio of medians 2.68x; median paired difference +300 s). Policy 1 was slower in all four cold pairs.** Do not flip the default on. Set the standing runner back to `0` until two defects are fixed. Neither defect is the 6-core cap by itself:

1. **The heap cap is 4 GiB, not 100 MB, and the shared server dies at it.** `DOTNET_GCHeapHardLimit` is parsed as hexadecimal, so `100000000` means 0x100000000 = 4,294,967,296 bytes (witness below). The `Antiphon.Tests` compile needs more than that: the stock server peaked at 5.8 GB RSS. Every policy server that reached 4.4–4.6 GB RSS **disappeared while still busy**, burning about 5 cores in its last 30 s. That happened 9 times between 04:16Z and 06:26Z. An idle exit needs 120 s with no work. The build then went on for minutes with no policy server.
2. **Every capped compiler is pinned to the lowest-numbered CPUs.** `SelectCpus` returns the first *grant* CPUs of the allowed set. So identities with grant 1, 4 and 6 all stack on CPU 0..N-1. One `cpus=0` server held 50 runnable threads on CPU 0 while CPUs 6–23 were 4% busy and load1 was 53.

Warm builds (touch one `Antiphon.Tests` source file; the same 3 obj assemblies are rewritten each time): policy 1 is 1.71x (median paired ratio, n=3 pairs, +140 s). One pair was even (1.03x).

Measurement only. No setting, compose file, runner environment or process was changed. Worktree `feat/card-task-9c6efe95`, base `300c8a99d0d267a39791410b7540189ab702f251`, SDK 10.0.404, 24 CPUs. The standing environment has `ANTIPHON_LINUX_COMPILER_POLICY=1`, overridden per command.

## Protocol as run

- Command, per build: `ANTIPHON_LINUX_COMPILER_POLICY=<1|0> pwsh -NoProfile -File scripts/build-slot.ps1 -Label c-policy-<id> -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false [--no-incremental]`. No `-m` was passed. That is the repository standard: the wrapper adds the grant's `-maxcpucount`, and every grant was `maxcpucount=6`. Default output, one build at a time.
- **Slot leasing was ON**, contrary to the brief. C1-A waited 346 s at `occupied=4/4` and W2-A waited 60 s. `Time Elapsed` excludes the slot wait.
- Cold: `--no-incremental`. Pair order was A,B / B,A / A,B / B,A (A = policy 1, B = policy 0).
- Warm: `touch tests/Antiphon.Tests/CachingChatClientTests.cs` (mtime only), then an incremental build, with the same alternation. **Deviation:** the throwaway warm-up before each timed warm build was dropped to fit the time box. Matching was checked instead: every warm build rewrote exactly the same three obj assemblies (`Antiphon.PtyHost.Protocol`, `Antiphon.PtyHost`, `Antiphon.Tests`) under both policies.
- Load gate: no build started while load1 was above 20. The script polled every 2 minutes, and the wait is recorded below.
- Sampling, every ~30 s:
  - all `VBCSCompiler` processes: stock apphost `.../bincore/VBCSCompiler -pipename:lr8A_…` and policy `dotnet exec .../VBCSCompiler.dll -pipename:ac1885-…`, with %CPU, RSS, cumulative CPU seconds and `Cpus_allowed_list`;
  - per-CPU busy % from `/proc/stat`, split into CPUs 0–5 and 6–23;
  - runnable threads grouped by their affinity;
  - `vmstat` idle and load.
- The first 4 builds' samples missed the stock apphost server (the grep matched only `VBCSCompiler.dll`). A host-wide sampler covered both from 04:51Z (from C3-A on).
- The raw logs are in the session scratchpad, not committed (generated evidence).

## All builds

Times are UTC on 2026-11-17. "Slot" is the build-slot wait. "Gate" is the load-gate wait. Others = `dotnet build|test|run`/MSBuild/VBCSCompiler processes in the container at start. "LINUX COMPILER" counts the policy diagnostic lines in the build output: 1 for every policy-1 build, 0 for every policy-0 build, so the opt-out really disables the launcher. Every build exited 0.

| Id | Policy | Start | Time Elapsed s | load1/5/15 at start | load1/5/15 at end | mean / max load1 during | Slot s | Gate s | Others | Host idle % (mean) | CPUs 0–5 / 6–23 busy % |
|---|---|---|---:|---|---|---|---:|---:|---:|---:|---|
| C1-A | 1 | 04:08:20 | 561.6 | 19.64 20.12 24.28 | 63.22 62.98 47.38 | 55.2 / 88.2 | 346 | 0 | 26 | 59 | 68 / 26 |
| C1-B | 0 | 04:29:32 | 272.8 | 5.66 32.76 39.96 | 36.86 37.93 40.30 | 34.0 / 52.4 | 0 | 360 | 13 | 45 | 85 / 53 |
| C2-B | 0 | 04:36:07 | 191.9 | 14.94 29.56 36.98 | 20.65 27.75 35.05 | 24.7 / 35.4 | 0 | 120 | 17 | 56 | 74 / 48 |
| C2-A | 1 | 04:41:20 | 841.5 | 4.39 19.22 31.04 | 42.29 44.81 38.93 | 39.3 / 118.9 | 0 | 120 | 8 | 47 | 71 / 41 |
| C3-A | 1 | 04:57:27 | 790.0 | 9.53 31.46 34.75 | 32.71 32.07 32.63 | 28.4 / 70.0 | 0 | 120 | 5 | 48 | 69 / 44 |
| C3-B | 0 | 05:12:41 | 501.9 | 10.73 23.87 29.55 | 19.69 26.99 29.40 | 27.9 / 49.3 | 0 | 120 | 20 | 40 | 76 / 55 |
| C4-B | 0 | 05:21:04 | 230.8 | 19.69 26.99 29.40 | 15.73 22.56 27.15 | 20.6 / 27.9 | 0 | 0 | 7 | 56 | 50 / 47 |
| C4-A | 1 | 05:24:56 | 541.4 | 15.73 22.56 27.15 | 25.11 25.25 25.43 | 20.7 / 45.7 | 0 | 0 | 5 | 51 | 69 / 37 |
| W1-A | 1 | 05:36:02 | 154.3 | 13.12 20.99 23.86 | 14.56 18.81 22.58 | 15.2 / 19.8 | 0 | 120 | 12 | 60 | 70 / 34 |
| W1-B | 0 | 05:38:41 | 150.5 | 14.56 18.81 22.58 | 22.84 20.93 22.79 | 20.0 / 29.7 | 0 | 0 | 13 | 50 | 82 / 50 |
| W2-B | 0 | 05:43:14 | 171.2 | 8.42 16.42 20.93 | 46.79 31.25 25.75 | 31.6 / 59.1 | 0 | 120 | 12 | 28 | 69 / 63 |
| W2-A | 1 | 05:48:06 | 310.8 | 19.94 26.32 24.62 | 37.79 38.74 30.55 | 37.7 / 62.4 | 60 | 120 | 21 | 38 | 76 / 55 |
| W3-A | 1 | 05:56:21 | 341.7 | 16.47 30.56 28.55 | 37.31 31.25 28.83 | 24.1 / 44.1 | 0 | 120 | 13 | 48 | 75 / 48 |
| W3-B | 0 | 06:04:07 | 199.9 | 13.67 23.73 26.33 | 22.90 24.46 26.07 | 21.4 / 32.7 | 0 | 120 | 13 | 52 | 80 / 50 |
| W4-B | 0 | 06:11:28 | 141.3 | 14.55 23.49 25.80 | 22.73 23.80 25.58 | 20.1 / 29.6 | 0 | 240 | 13 | 35 | 71 / 59 |
| W4-A (invalid, see below) | 1 | 06:15:52 | 849.5 | 9.57 18.60 23.46 | 42.79 55.62 43.78 | 52.1 / 88.0 | 0 | 120 | 9 | 51 | 70 / 38 |

## Per cell

| Cell | n | median s | min s | max s |
|---|---:|---:|---:|---:|
| cold, policy 1 | 4 | 675.8 | 541.4 | 841.5 |
| cold, policy 0 | 4 | 251.8 | 191.9 | 501.9 |
| warm, policy 1 (valid) | 3 | 310.8 | 154.3 | 341.7 |
| warm, policy 0 | 4 | 160.8 | 141.3 | 199.9 |

Paired difference (policy 1 minus policy 0):

| Pair | Diff s | Ratio | load1 at start A / B |
|---|---:|---:|---|
| C1 | +288.8 | 2.06 | 19.6 / 5.7 |
| C2 | +649.6 | 4.39 | 4.4 / 14.9 |
| C3 | +288.1 | 1.57 | 9.5 / 10.7 |
| C4 | +310.6 | 2.35 | 15.7 / 19.7 |
| **cold median** | **+299.7** | **2.20** (ratio of medians 2.68) | |
| W1 | +3.8 | 1.03 | 13.1 / 14.6 |
| W2 | +139.6 | 1.82 | 19.9 / 8.4 |
| W3 | +141.8 | 1.71 | 16.5 / 13.7 |
| W4 | (+708.2, invalid) | (6.01, invalid) | 9.6 / 14.6 |
| **warm median, 3 valid pairs** | **+139.6** | **1.71** (ratio of medians 1.93) | |

Every paired sign is positive: policy 1 was never faster.

**W4-A is excluded.** My own commit of this note landed at 06:16Z, while W4-A was starting (06:15:52Z). `Directory.Build.props:32` (`SetSourceRevisionIdFromGit`) stamps the HEAD SHA into `AssemblyInformationalVersion`, so W4-A rewrote 40 obj assemblies instead of the 3 in every other warm build. It is a near-cold build, not a matched warm one. The time box did not allow a repeat. The warm decision rests on 3 valid pairs. W4-A still shows the mechanism: policy server 18230 vanished while busy at 4489 MB (06:18:54Z), there was no policy server for 06:19–06:22Z, the next server reached 4403 MB by 06:25:25Z and was gone by 06:25:55Z, and 41–56 runnable threads were pinned to CPU 0 at load1 64 while CPUs 6–23 were 16–26% busy.

## Load covariate

At start, the slow side is not the high-load side. In C2 and C4 the policy-1 build started at *lower* load1 than its policy-0 partner (4.4 vs 14.9, 15.7 vs 19.7) and was still 4.39x and 2.35x slower. C3 started level (9.5 vs 10.7) and came out 1.57x.

Load *during* the build is higher for policy 1 (cold mean load1 20.7–55.2 vs 20.6–34.0). That covariate is partly caused by the policy itself. Load average counts runnable threads, and the policy queues compiler threads on 1–6 pinned CPUs (next section). The one cold pair at equal in-build load is C4: mean load1 20.7 vs 20.6, and the result was still 2.35x.

C3-B (policy 0, 502 s) is the slow stock outlier. During it the stock server `lr8A` did almost no work: one long-lived instance at 18 CPU-seconds, plus six instances that lived under 10 s. A policy server from another session (`17904`) was busy on CPUs 0–5. Why that stock build ran without a busy server is UNOBSERVED.

## Compiler and host CPU while policy 1 runs

**Server deaths at the heap ceiling.** These are policy (`ac1885-1ff7756…`, `cpus=0-5`) server lifetimes from the samples. "Last 30 s CPU" is the change in cumulative CPU seconds between the last two samples before the process vanished.

| Pid | First seen | Last seen | max RSS MB | Last 30 s CPU | Outcome |
|---|---|---|---:|---:|---|
| 17158 | 04:12:42 | 04:16:06 | 4477 | ~273% avg | gone while busy, during C1-A; C1-A ran 7 more min with no policy server |
| 20144 | 04:41:49 | 04:44:15 | 4445 | 412% avg | gone while busy, during C2-A |
| 26922 | 04:57:44 | 05:00:44 | 4430 | 157 s | gone while busy, during C3-A; then 3.5 min with no policy server |
| 31782 | 05:04:14 | 05:06:44 | 4404 | 156 s | gone while busy, during C3-A; then 4 min with no policy server |
| 17904 | 05:10:45 | 05:22:17 | 4534 | 0 | CPU flat from 05:20:17 (1311→1314 s) while RSS fell 4534→825: work abandoned at the ceiling, then idle exit |
| 24601 | 05:25:18 | 05:30:18 | 4509 | 130 s | gone while busy, during C4-A |
| 12681 | 05:31:18 | 05:42:20 | 4597 | 150 s | gone while busy, after 11 min oscillating 2.6–4.6 GB (GC at the limit) |
| 4531 | 05:43:50 | 05:49:21 | 4560 | 152 s | gone while busy, during W2-A |
| 18230 | 06:16:24 | 06:18:54 | 4489 | 122 s | gone while busy, during W4-A |
| 19656 | 06:22:25 | 06:25:25 | 4403 | 103 s | gone while busy, during W4-A |

For contrast, the stock servers (`lr8A_…`, affinity 0–23, no heap cap) peaked at **5849 MB** (pid 4395, C4-B, up to 858% CPU) and 5278 MB (pid 28759). Both ended idle, with no CPU in the last interval and RSS trimmed before exit.

The exit reason itself was not logged. That an `OutOfMemoryException` killed the server is an inference from:

- the 4 GiB limit (witness below);
- the death RSS: 4.40–4.60 GB is 4 GiB of managed heap plus runtime overhead;
- the death while busy. The idle timeout is 120 s, so a busy server cannot be an idle exit.

The Roslyn client then restarts the compile, in the build process or on a new server. Either way the work done so far on `Antiphon.Tests` is thrown away. That fits the cold policy-1 builds being 4.5–10 minutes slower.

Heap-limit witness (same runtime family, no build needed):

```
$ DOTNET_GCHeapHardLimit=100000000 pwsh -NoProfile -c '[GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes'
4294967296
$ DOTNET_GCHeapHardLimit=0x5F5E100 pwsh -NoProfile -c '[GC]::GetGCMemoryInfo().TotalAvailableMemoryBytes'
100000000
```

**Capped and serialised: host idle while pinned CPUs queue.**

- During C1-A, 04:21:11–04:23:20, CPUs 0–5 were 52–82% busy and CPUs 6–23 were **4–14%** busy, at load1 62–69. CPU 0 was at 100% in every sample from 04:18 to 04:41.
- At 04:24:37, load1 was 53.6 and **50 runnable threads all had affinity `0`** (one CPU). CPUs 6–23 were 4% busy.
- The `cpus=0` server was another session's grant-1 build: pipe `ac1885-5ac3b1355…`, pid 1515, 72 threads, `Cpus_allowed_list: 0`. It ran at about 94% of one core for over 30 minutes and grew to 4.2 GB RSS.
- A grant-4 identity (`ac1885-26d118916…`, affinity 0–3) had 5–9 concurrent server instances on one pipe name at 05:11–05:14, each 40–175 MB. They overlap the same CPUs.
- Across all 191 runnable-thread samples: 1042 runnable threads had affinity 0–23, 793 had 0–5 and 296 had CPU 0 alone. So **52% of the runnable threads were confined to 6 of the 24 CPUs.**
- Over the policy-1 cold builds, CPUs 0–5 averaged 68–71% busy against 26–44% on CPUs 6–23, with host idle 47–59%.

The 6-core cap on one shared server does serialise. The worst part is that every capped server, of every identity and grant, lands on the *same* lowest CPUs, so the load goes onto CPUs that are already busy while the rest of the host sits idle.

## Verdict and the change

**(b) Materially slower: cold 2.2x (median paired, +300 s), warm 1.71x (median paired, +140 s, 3 valid pairs).** Default-on is not supported on speed. Recommendation: **revert the standing runner to `0` now** (operator action: the saved `stack.env` value `ANTIPHON_LINUX_COMPILER_POLICY=0`, then a named-phase recreate). Leave the defaults as they are: `docker-compose.server2-runner.yml:67` `:-0` and `scripts/c590-remote.sh:1133` `RUNNER_LINUX_COMPILER_POLICY_DEFAULT=0`.

Re-trial only after a tune. In order of value:

1. **Heap cap** (`scripts/lib/BuildDriverEnvironment.cs:92`, `CompilerEnvironment`; the identity and diagnostic text at `:58` and `:307`). Drop `DOTNET_GCHeapHardLimit` from the shared server, or write it as explicit hex above the `Antiphon.Tests` need, for example `0x200000000` (8 GiB, the CARD-1903 figure). The observed stock peak is 5.8 GB RSS. The literal `100000000` never meant 100 MB.
   - Witness: a unit test that the emitted value parses (`Convert.ToUInt64(v, 16)`) to at least 6 GiB, or is absent.
   - Acceptance: re-run cold pairs C1–C4 and see no policy server vanish while its last interval is busy.
2. **CPU selection** (`SelectCpus`, `:36-55`, called at `:209`). Taking the first *grant* CPUs puts every capped compiler on CPU 0..N-1. Either:
   - give the shared server the whole allowed set and keep only `DOTNET_PROCESSOR_COUNT` (a thread cap, not an affinity), or
   - choose the set from the least-busy or highest-numbered CPUs, and never pin a grant-1 server to a single CPU.
   - Witness: a test that two different grants do not both start at CPU 0. Live, a policy build where the runnable-thread sample shows no queue of more than 6 on CPUs 0–5 while CPUs 6–23 sit under 20% busy.
3. A per-build server (a `SharedCompilationId` per lease) is not needed to explain these numbers. With the heap fixed and pinning spread, one shared server at 6 threads may still queue under 4 concurrent heavy builds. Test that only after 1 and 2.

## UNOBSERVED

- The server exit reason (OOM is inferred, not logged). The cheapest proof is one policy-1 cold build with `RoslynCommandLineLogFile=<path>` on that command only; it was not run, to stay inside the time box.
- Whether the Roslyn client fell back in-process or to a fresh server after each death. Minimal verbosity prints nothing for either.
- Which processes held the runnable threads pinned to 0–5 during the no-server stretches of C1-A (04:16–04:23). The runnable-thread sampler mis-parsed thread names with spaces until 04:24.
- Stock-server CPU and RSS during C1-B and C2-B, before the host-wide sampler started (04:51Z).
- Why C3-B (policy 0) ran 502 s with no busy stock server.
- The policy-0 opt-out was checked only by the absence of the `LINUX COMPILER` line and the stock pipe name. The environment of the stock server's children was not inspected.
- Builds on the temp runner, and any second container's load on the same host.
- The skipped warm-up builds. Matching was checked by the identical sets of rewritten obj assemblies instead.
