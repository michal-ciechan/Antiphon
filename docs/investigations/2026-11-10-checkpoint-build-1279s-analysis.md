# Checkpoint build 1279s (CARD-1225 receipt 20261110-212422-f102)

Debug task e887a1ba. Receipt of Review 224d47be at `bcbc01f23e299c6cb4ba84a36abb82a7264424be`, host Debian 12, 24 cores, server2. Wall 33m46s (`wallSeconds` 2026.5). CP-1's isolated `tests/Antiphon.Tests` build is the long piece. The tests are not.

No product change. The same isolated layout, measured here with a free build slot, finished in 7m35s. A native-output cold build of the same project finished in 3m25s. The step from 3m25s to 7m35s is `CopyStagedHelpers` rebuilding staged helpers because one absolute `OutputPath` is shared (CARD-1238). The step from 7m35s to the receipt's 18m59s of MSBuild ran while server2's four build slots were full; this pass did not reproduce that load.

## Receipt split

Run `20261110-212422-f102`. `maxConcurrentRows=1`, `maxConcurrentBuilds=1`. Source clean, `buildSource=verified`, exit 0. Host load at the report was `28.89 33.82 41.53` with 89326 MB available. The four CHECKPOINT `waited=` values (90s, 30s, 0s, 0s) are row-slot waits. The build-slot waits sit on the builds.

| Slice | Seconds | Where |
|---|---:|---|
| Wall | 2026.5 | `report.json` `wallSeconds` |
| All slot waits | 240.0 | `budget.admissionOnlySeconds` (45+75 build, 90+30+0+0 row) |
| `bin-c1225-rv1` receipt clock | 1279.4 | `builds[].seconds`, includes its 45s slot wait |
| `bin-c1225-rv1` MSBuild | 1139.4 | `builds/bin-c1225-rv1/build.log` `Time Elapsed 00:18:59.37`, 2093 warnings, 0 errors |
| `bin-c1225-rv2` receipt clock | 91.6 | includes its 75s slot wait |
| `bin-c1225-rv2` MSBuild | 16.6 | `Time Elapsed 00:00:16.58`, 0 warnings |
| CP-1 test body | 37.5 | 58/58, row executed 61.5 after the 90s wait |
| CP-2 test body | 82.7 | 77/77, `TestClassificationPolicyTests` 82.2s, row executed 101.2 after the 30s wait |
| CP-3 test body | 3.8 | 24/24, row executed 14.8, waited 0s |
| CP-4 test body | 2.2 | 14/14, row executed 6.7, waited 0s |
| Test bodies | 126.2 | sum of the four `testsWallSeconds` |
| Output cleanup | 92.7 | executor `FINALIZATION phase=output-cleanup` |

`builds[].seconds` starts before the slot acquire and stops when the driver returns. For rv1 that is 45s of wait plus 1139s of MSBuild plus about 95s of driver time around MSBuild's own timer. Lease `b51cc8b8-3680-469f-8931-4136cac7bb72` was granted at 21:25:52Z (`waited=45s`, `maxcpucount=6`) after the broker reported `occupied=4` `budget=4`. Row clocks sum to `sequentialEquivalentSeconds` 304.4 and exclude the builds. Finalization is 93.0s, almost all the cleanup of `bin-c1225-rv1/` and `bin-c1225-rv2/`.

## Same host, other Code builds

`held=` is the build-slot lease, so it includes the build process and is the figure those notes recorded.

| Note | What | Slot |
|---|---|---|
| `/work/worktrees/task-08ee53e0/.antiphon/task-08ee53e0.md` (CARD-1300) | Fix compile before CP-1 | `be831651-effa-4597-b0ed-723ea471728f` waited=0s held=391s, 0 errors |
| `/work/worktrees/task-0ddd898d/.antiphon/task-0ddd898d.md` (CARD-1820) | Rebuild before the class run | `499b5e15-cf4a-46d2-9f2a-4fcfbc79b639` waited=0s held=272s, 0 errors |
| same note | Tag rebuild | `3c0837f4-36e4-4c8a-b6ec-31a4aecbb03f` waited=15s held=217s, 0 errors, 2036 warnings |
| `/work/worktrees/task-ef2fee3f/.antiphon/task-ef2fee3f.md` (CARD-1834) | Guard-union build | `170cc7b5-93c4-445c-bbb1-453006d79aea` waited=0s held=177s |

Those leases are 177–391s. The tag rebuild printed 2036 warnings in a 217s lease. The receipt printed 2093 warnings in 1139s of MSBuild.

## Measurements at `ef7afd44927206f6c03547670a5add84130bdce9`

Both through `scripts/build-slot.ps1`, `-nodeReuse:false`, `-clp:PerformanceSummary`, `UseAppHost=false`. The receipt SHA is `bcbc01f2`; these builds are current master. Both logs show the same helper-folder outputs (`channel-outbound-probe/Antiphon.Server.dll`, `host-load-probe/Antiphon.Server.dll`), so the rebuild shape is the one the receipt ran.

| Build | Slot | Load (1 / 5 / 15) | MSBuild | Warnings | `CopyStagedHelpers` | `Csc` | `CoreCompile` |
|---|---|---|---:|---:|---:|---:|---:|
| Native cold `dotnet build tests/Antiphon.Tests` | `59563459-7089-4f8a-a1ce-a204e7fb94cf` waited=0s held=207s | start 26.54 / 15.13 / 23.95, end 25.13 / 22.25 / 25.17 | 00:03:25.39 (205s) | 2022 | 4883 ms, 15 calls | 211670 ms, 42 calls | 211797 ms, 46 calls |
| Checkpoint layout: absolute `OutputPath` `.../bin-c1225-probe/`, `ArtifactsPath` `.../bin-c1225-probe/artifacts`, `UseArtifactsOutput=true`, `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1` | `cb20369d-5dba-4b0a-a3f8-58dd1bec2246` waited=0s held=457s | start 28.63 / 28.77 / 27.14, end 38.65 / 39.95 / 33.56 | 00:07:35.08 (455s) | 2093 | 168541 ms, 15 calls | 595382 ms, 57 calls | 595699 ms, 86 calls |
| Receipt rv1, same layout shape | waited=45s, budget 4/4 | report 28.89 / 33.82 / 41.53 | 00:18:59.37 (1139s) | 2093 | (no PerformanceSummary in that log) | | |

Restores on the receipt and on both measurements were about a second per project. `TreatWarningsAsErrors` did not fail either build. Analyzer targets in the isolated summary are milliseconds (`ResolveLockFileAnalyzers` 7 ms). `IncrementalClean` was 499 ms / 86 calls, `FindUnderPath` 312 ms, `ReadLinesFromFile` 38 ms.

Inside the isolated `CopyStagedHelpers` total, `Antiphon.HostLoad.Probe` `Build` was 89840 ms and `Antiphon.ChannelOutbound.Probe` `Build` was 73086 ms. Those two rebuild `Antiphon.Server` into `host-load-probe/` and `channel-outbound-probe/`. The receipt log has those same two extra `Antiphon.Server ->` lines.

## What the candidates measured as

- Warning text. 2036 warnings fit in a 217s lease (CARD-1820). 2022 warnings fit in the 205s native build. 2093 warnings are the receipt and the 455s isolated build. The warning count matches the layout, and it does not grow with the jump to 1139s.
- NuGet and the analyzer cache. Restores stayed under about a second. The isolated build's analyzer targets were milliseconds.
- Cold native `obj`. This worktree had no `tests/Antiphon.Tests` `obj` before the native build. That cold compile was 205s, and `Csc` accounted for it (212s summed against a 205s wall).
- CARD-0222 `FileListAbsolute.txt`. The review worktree's ledgers, written before the 21:25Z compile, are 94094 bytes (`tests/Antiphon.Tests`, 21:05:40Z) and 26174 bytes (`server`, 21:02:23Z). The receipt log has no `CARD-0222` line. The isolated measurement's clean targets were under a second. The historical 21-minute ledger hang is a different sample.
- Tampered gitignored `obj`. Task 3838725c refused `tampered` and recorded executed 0. This receipt's build succeeded with `buildSource=verified`.
- `CopyStagedHelpers` on one absolute `OutputPath`. `tests/Antiphon.Tests/Antiphon.Tests.csproj` sets `_StagedHelperShared` when the helper's output directory is this `OutDir`, then builds that helper with `OutDir` pointed at the nested folder. The comment names CARD-1238: copying from the shared directory nested earlier helper folders and reached 38 GB. `BuildOutputNestingTests` guards that. Native output keeps a distinct directory per project, so the target copies (4883 ms). The checkpoint's one absolute `OutputPath` takes the rebuild branch (168541 ms) and compiles `Server` again for the two heavy probes. That is the measured 205s to 455s gap, on a busier sample (1-minute load ended at 38.7).
- Four full slots. rv1 waited because `occupied=4` `budget=4` (`docker-compose.server2-runner.yml`: `MaxConcurrent=4`, `MaxCpuCount=6`). After the grant the budget was full again, so three other builds ran beside this `-maxcpucount:6` compile. 1139 / 455 = 2.5, against a 15-minute load already near 41. This pass's isolated build waited 0s. The 2.5× is the receipt versus a free-slot run of the same shape; it is not an A/B that held the other three slots.

## Recommendation

Leave the checkpoint tool alone. The helper rebuild is the CARD-1238 guard, and the 19-minute sample adds a full slot budget that this pass did not reproduce. Disabling analyzers, suppressing the warning flood, forcing `-m:1`, and sharing an `obj` cache do not match the timers above.

A later Code change can shorten the layout tax by building each staged helper without rebuilding `ProjectReference`s that already sit in the shared `OutDir`, so `Antiphon.Server` is compiled once. That has to keep `BuildOutputNestingTests` at zero nested copies and still produce each helper folder's own assembly. It is not a one-line flag, and it does not explain the receipt's extra 11 minutes over the 455s measurement.

Server2's `BuildSlots__MaxConcurrent=4` with `MaxCpuCount=6` on 24 cores can put four compiles on the machine together. Changing that budget wants its own A/B. This note does not change it.
