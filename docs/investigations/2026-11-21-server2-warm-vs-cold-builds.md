# Are server2 task builds warm or cold?

Native builds on a reused server2 slot are partly warm. The unchanged build skipped 44 of 48 `CoreCompile` evaluations. Its four compiler calls came from the normal and native-apphost builds of PtyHost and its protocol project.

## Measurements

Server2 slot `ws-08`, starting at `4e3adda4c5191f03be90798f79f161e80bace3eb`. Builds used one broker lease at a time with `ANTIPHON_LINUX_COMPILER_POLICY=0`. Load is the one-minute value from `/proc/loadavg` immediately before the SDK started. Builds were admitted below 14. Wall times are individual observations. Project counts deduplicate project paths and exclude up-to-date targets. Csc counts include separate builds of the same project. Both were counted by replaying the build binlogs with `dotnet msbuild <file.binlog> -v:diag`.

| Scenario | Projects compiled | Csc calls | Wall seconds | Start load |
|---|---:|---:|---:|---:|
| A1 · native build on reused slot | 37 | 39 | 184.94 | 13.34 |
| A2 · same command, unchanged source | 2 | 4 | 19.62 | 13.71 |
| C1 · one trailing comment in `RunnerAbsenceEvidence.cs` | 3 | 5 | 46.99 | 13.80 |

A1 and A2 ran `pwsh -NoProfile -File scripts/build-slot.ps1 -Label warm-probe -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo -bl:<path>`. The build starts were 17:07:15Z and 17:10:48Z, with A2 starting when the load gate reopened. Of 48 `CoreCompile` evaluations across 42 project paths, A1 skipped nine and A2 skipped 44. A1 diagnostics named a newer `Directory.Build.props` or regenerated `*.AssemblyInfo.cs` among the inputs that triggered compilation. The old test DLL was written at 12:11:41Z and the slot copy of `Directory.Build.props` at 13:06:34Z.

A2 and C1 each compiled `Antiphon.PtyHost` and `Antiphon.PtyHost.Protocol` twice. The binlog named each project's updated `CoreCompileInputs.cache` as the reason in both passes. The test project stages a Linux apphost by restoring and building PtyHost again with `UseAppHost=true` and a separate `OutDir` while the outer build uses `UseAppHost=false`. The passes share the native intermediates. A2's four Csc calls totaled 1.054 seconds of task time, inside a 19.62-second build. The C1 comment added just one compiler call, for `Antiphon.SessionRunner.Contracts`. Its implementation DLL advanced to 17:12:28Z and its reference DLL kept its 17:07:28Z timestamp. The comment was restored byte for byte. [Antiphon.Tests.csproj](../../tests/Antiphon.Tests/Antiphon.Tests.csproj#L227-L240)

## Slot reuse and Windows context

The server2 ledger at `/work/workspace-slots/ledger.json` showed `ws-08` lease generation 25, `cold=false`, `lastResetKind=warm`, and current lease start 17:03:43Z. Both `obj/Debug/net9.0/Antiphon.Tests.dll` and `bin/Debug/net9.0/Antiphon.Tests.dll` predated that lease by nearly five hours. Release and lease both use `git clean -ffdx` with warm exclusions for `obj/`, `bin/`, `node_modules/` and `client/dist/`. Cold resets omit those exclusions. The same ledger records a 07:29:29Z qualification cold reset. [Reset policy](../../src/Antiphon.SessionRunner.Contracts/WorkspaceSlotContracts.cs#L93-L119), [lease](../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs#L127-L131), [release](../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs#L453-L477)

Windows rc chunks are warm within a qualification by construction. The prerequisite invokes one native solution build, then each chunk starts the built exe or uses `dotnet exec` on the DLL. A synced qualification can start that prerequisite cold because the isolated checkout sync cleans ignored outputs. No Windows commands were run for this investigation. [Checkout sync](../../scripts/lib/nightly-run-impl.ps1#L467-L477), [prerequisite](../../scripts/lib/nightly-tests-impl.ps1#L733-L751), [chunk host](../../scripts/lib/Antiphon.Nightly/NightlyTestsPlan.cs#L516-L530), [chunk launch](../../scripts/lib/Antiphon.Nightly/NightlyChunkRunner.cs#L547-L566)
