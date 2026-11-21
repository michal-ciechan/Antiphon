# Are server2 task builds warm or cold?

**Server2 direct builds are partly warm. Separate checkpoint runs are cold. Windows rc chunks reuse the qualification's prerequisite build.**

## Measurements

Server2 slot `ws-08`, initially at `4e3adda4c5191f03be90798f79f161e80bace3eb`, SDK 10.0.404. Builds ran sequentially under broker leases capped at six MSBuild nodes, with `ANTIPHON_LINUX_COMPILER_POLICY=0`. Load is the one-minute `/proc/loadavg` value immediately before each SDK build. The gate required load below 14. The compiler counts come from diagnostic replay of the build binlogs with `dotnet msbuild <file.binlog> -v:diag`. The project column deduplicates paths with a built `CoreCompile` target. The Csc column counts separate task starts, including repeated builds of one project. Wall seconds are the SDK's reported elapsed time. Checkpoint receipt seconds include additional tool work around the SDK command.

| Scenario | Projects compiled | Csc calls | SDK wall s | Receipt build s | Start load |
|---|---:|---:|---:|---:|---:|
| A1 · first native build on reused slot | 37 | 39 | 184.94 | — | 13.34 |
| A2 · unchanged native repeat | 2 | 4 | 19.62 | — | 13.71 |
| C1 · one trailing comment in `RunnerAbsenceEvidence.cs` | 3 | 5 | 46.99 | — | 13.80 |
| C0 · native rebuild after restoring the original source | 3 | 5 | 19.02 | — | 13.50 |
| C2 · after the note-only commit | 2 | 4 | 22.35 | — | 11.70 |
| Checkpoint tool bootstrap, native | 0 | 0 | 4.73 | — | 12.69 |
| B1 · isolated checkpoint | 42 | 44 | 364.50 | 466.53 | 8.53 |
| B2 · same checkpoint, unchanged source | 42 | 44 | 186.92 | 304.34 | 6.39 |

## Native incrementality

A1 and A2 used `pwsh -NoProfile -File scripts/build-slot.ps1 -Label warm-probe -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo -bl:<path>`. The starts were 17:07:15Z and 17:10:48Z. A2 began as soon as the load gate reopened. The native graph visited 42 project paths in 48 `CoreCompile` evaluations. A1 skipped nine. Its test DLL predated the task by nearly five hours, and its diagnostics cited newer `Directory.Build.props` and regenerated `*.AssemblyInfo.cs` inputs for multiple recompiles. A2 skipped 44 evaluations.

The only A2 recompiles were `Antiphon.PtyHost` and `Antiphon.PtyHost.Protocol`, twice each. The outer build uses `UseAppHost=false`. The Linux staging target restores and builds the native helper with `UseAppHost=true` and `OutDir` under `ptyhost-native/`. A2 diagnostics show reference paths under the producer's own `bin/` in the outer pass and under `ptyhost-native/` in the inner pass. The dependency hashes differed between passes, which rewrote the two projects' shared native `CoreCompileInputs.cache` files and caused recompilation. The four Csc calls totaled 1.054 seconds of compiler task time. [Native helper target](../../tests/Antiphon.Tests/Antiphon.Tests.csproj#L227-L240)

C1 added one compiler call for `Antiphon.SessionRunner.Contracts`. Its implementation DLL advanced to 17:12:28Z and its reference DLL kept its 17:07:28Z timestamp. C0 rebuilt the restored bytes, leaving the note-only commit `95da75de564115ee970682855077979a04f71d4f` as the only change before C2. C2 matched A2 project for project. Generated test metadata was `1.0.0+unknown`. The tested base contains deploy-only stamping commit `b2911c28665fd198f3fa67f74f03c4dfb33cdb5c`. It removed `SetSourceRevisionIdFromGit`, defaults `SourceRevisionId` to `unknown`, and disables SourceLink. [Directory.Build.props](../../Directory.Build.props#L16-L32)

## Checkpoint isolation

The two runs used the already-built checkpoint tool with one serial row for `tests/Antiphon.Tests -> bin-warm-c8d69c40/` and filter `/*/*/DerivedImpactIndexTests/merge_process_names_the_regenerate_step`. The tool used the same source SHA, project, properties and output name in both runs. The output directory was absent before each run. The SDK starts were 17:26:08Z and 17:42:35Z. Each run had 44 built CoreCompile targets and 20 up-to-date evaluations. Both builds started within the 45-minute window. The second receipt finalized afterward.

The tool and PowerShell driver set an absolute owned `OutputPath`, `ArtifactsPath=<output>/artifacts` and `UseArtifactsOutput=true`. SDK 10.0.404's `Sdk/UseArtifactsOutputPath.props:20–27` sets the intermediate base to `<output>/artifacts/obj/<project>/`. B1's binlog recorded that path and both runs created a fresh `project.assets.json` there. The tool creates the owned root afresh and reclaims it after completion, including the intermediates. Neither builder passes `--no-incremental` or a global `BaseIntermediateOutputPath`. Both use `UseAppHost=false` on Linux. With the Linux compiler policy opted out, the checkpoint Csc tasks reported `UseSharedCompilation=True`. [Build arguments](../../tools/Antiphon.Checkpoints/Execution/BuildStep.cs#L24-L78), [layout](../../tools/Antiphon.Checkpoints/BuildConfinement.cs#L81-L89), [PowerShell driver](../../scripts/run-checkpoint.ps1#L31-L38), [fresh root](../../tools/Antiphon.Checkpoints/Execution/RunScheduler.cs#L486-L520), [cleanup](../../tools/Antiphon.Checkpoints/CheckpointApp.cs#L417-L448)

The first exploratory checkpoint attempt `20261121-171821-d7fb` bypassed the PATH recorder under the SDK muxer. It was stopped, its partial output was cleaned, and it was excluded. The two measured runs used a temporary SDK apphost pointed at the tool's own shadow-copy DLL so its real confined driver reached the recorder. The recorder checked load and added a binlog argument before invoking the SDK.

Stored checkpoint transcript excerpts, from `.antiphon/checkpoints/<run-id>/report.md` on the runner.

```text
CHECKPOINT CP-1 commit=95da75de564115ee970682855077979a04f71d4f build=ok filter=/*/*/DerivedImpactIndexTests/merge_process_names_the_regenerate_step executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/20261121-172415-cf46/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=95da75de564115ee970682855077979a04f71d4f sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=466.5349099s startup=7.7591124s testsWall=0.2435002s teardown=0.7310044s hostWall=8.7334472s
outputs: deleted bin-warm-c8d69c40/

CHECKPOINT CP-1 commit=95da75de564115ee970682855077979a04f71d4f build=ok filter=/*/*/DerivedImpactIndexTests/merge_process_names_the_regenerate_step executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/20261121-174033-1fde/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=95da75de564115ee970682855077979a04f71d4f sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=304.3431688s startup=7.0905641s testsWall=0.1296965s teardown=0.4557684s hostWall=7.6760349s
outputs: deleted bin-warm-c8d69c40/
```

## Slot reuse and Windows context

The server2 ledger at `/work/workspace-slots/ledger.json` showed `ws-08` lease generation 25, `cold=false`, `lastResetKind=warm`, and lease start 17:03:43Z. Both the native `obj` and `bin` copies of the test DLL were dated 12:11:41Z. Warm release and lease execute `git clean -ffdx` with exclusions for `obj/`, `bin/`, `node_modules/` and `client/dist/`. Cold resets omit the exclusions. The ledger records a qualification cold reset at 07:29:29Z. [Reset policy](../../src/Antiphon.SessionRunner.Contracts/WorkspaceSlotContracts.cs#L93-L119), [lease](../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs#L127-L131), [release](../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs#L453-L477)

Windows rc chunks are warm within a qualification by construction. One native solution build precedes the chunks, then each chunk starts the built exe or uses `dotnet exec` on the DLL. A synced qualification starts the prerequisite with empty native build outputs because the isolated checkout sync cleans them. Windows was assessed from source. Linux results cover one slot, one project graph and one type of source edit. [Checkout sync](../../scripts/lib/nightly-run-impl.ps1#L467-L477), [prerequisite](../../scripts/lib/nightly-tests-impl.ps1#L733-L751), [chunk host](../../scripts/lib/Antiphon.Nightly/NightlyTestsPlan.cs#L516-L530), [chunk launch](../../scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs#L547-L566)

## Not done, noted

1. Cross-run checkpoint intermediate reuse could approach the native repeat's 40 fewer Csc calls. 2. Combining eligible rows under the tool's existing same-run build reference avoids one duplicate build, about 304–467 seconds across these two receipt build phases. 3. Avoiding the PtyHost cache flips could save four Csc calls, about 1.05 seconds of compiler task time on A2. These estimates can overlap.
