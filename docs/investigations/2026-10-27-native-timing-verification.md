# Native Windows timing verification, 2026-10-27

Windows CP-8 passed all 28 cases, including actual Windows PowerShell 5.1 summary replay and the paired overhead preflight. The complete requested Windows verification is **not green**: the standalone timing harness refuses PowerShell 5.1, and all three ChangeAwareRelease tests failed with `selection-worker-timeout` after correcting a child-shell path prerequisite. No implementation was changed.

## Source and evidence identity

- Task: `41990cb3`; candidate branch `feat/card-task-04298664`; verified source **`d3fd72f941abda0fd6026f5e564d66ac410bdf47`**.
- Report branch: `feat/card-task-41990cb3`. Fetch completed first. This clean, previously unpublished task branch initially pointed at `718bd29f4a1504c64c491d607d31403fcb87dae2`; it was aligned to the commissioned candidate before any verification. All builds and tests used that candidate. The only tracked deliverable is this report.
- Host: native Windows 10.0.19045, x64, .NET runtime 9.0.22; PowerShell 7.6.9 (packaged and installed executables); Windows PowerShell **5.1.19041.6456 Desktop**. Initial available memory: **8,757 MiB** of 32,709 MiB, above the required 6,144 MiB.
- Worktree: `C:\Antiphon\worktrees\card-task-41990cb3`. Below, `E` means its `.antiphon\native-41990cb3` directory. Logs, TRX, JSON, HTML, diagnostic scripts and copied fixture evidence remain ignored; they are not committed.
- Exactly one native build, output `bin-native-41990cb3/`, under the host build-slot gate. It passed in **8m37.19s**, 0 errors / 1,198 warnings. The warnings include existing nullable/analyzer warnings and two ASP.NET `RouteHandlerAnalyzer` AD0001 exceptions; this is not a warning-free build. Source and scratch `Antiphon.Tests.dll` SHA-256 both equal `971fc9c6b3068a9edc62a028baabb45906ce2a37fba0c55567091d6de1415770` (`E/binary-provenance.json`).
- All subsequent compiled tests used `--no-build`. No AppHost, production runner or desktop-runner service/session operation, production nightly checkout, shared native-run lock, tag, promotion or publication was performed. Build-slot broker acquisition/release was the mandated runner interaction. Standing caller authority was sufficient; no additional approval was requested.

## Commands and outcomes

Commands ran from the worktree unless stated. Define `$e = 'C:\Antiphon\worktrees\card-task-41990cb3\.antiphon\native-41990cb3'`. `G(label, command)` below is the exact prefix `pwsh -NoProfile -File scripts/build-slot.ps1 -Label <label> -- <command>`; every build/native test driver used it. The **installed-shell** rows additionally set **only their process PATH** to `'C:\Program Files\PowerShell\7;' + oldPATH`, invoke that installed `pwsh.exe`, and restore PATH afterward. Both shell installations report 7.6.9. Preliminary ungated PS5.1 version/parser guard probes are retained in the corresponding logs without the `-gated` suffix; their final rows below were repeated through the required gate, with unchanged outcomes (1s, 9s and 3s held). Inventory-only parsing did not execute startup scripts.

| Check | Command after G, or complete command when ungated | Exit | Evidence / verdict |
|---|---|---:|---|
| Single build | `G(native-41990cb3-build, dotnet build tests/Antiphon.Tests --property:OutputPath=bin-native-41990cb3/ --nologo)` | 0 | `E/build.log`: `1198 Warning(s)`, `0 Error(s)`, `Time Elapsed 00:08:37.19`; slot granted, held 520s. PASS. |
| Timing harness, PS7 | `G(native-41990cb3-timing-ps7, pwsh -NoProfile -File scripts/test-qualification-timing.ps1)` | 0 | `E/qualification-ps7.log`: **57 PASS**, final `OK`; held 96s. PASS. Linux-only custody cases are excluded by the script on Windows and are not credited here. |
| Same harness, actual PS5.1 | `G(native-41990cb3-ps51-version, powershell.exe -NoProfile -File scripts/test-qualification-timing.ps1)` | 1 | `E/qualification-ps51-gated.log:2`: requires Windows PowerShell 7.0; `:7` `ScriptRequiresUnmatchedPSVersion`. **FAIL before execution**, not a skipped/passing test. |
| Existing AppHost ASCII/parser guard | `G(native-41990cb3-ps51-apphost-guard, powershell.exe -NoProfile -File scripts/test-apphost-server-version.ps1 -Case T19)` | 0 | `E/apphost-ps51-guard-gated.log`: **15 passed, 0 failed**. This is the existing inert fixture guard; no live service was called. |
| Existing checkpoint ASCII/PS5.1 guard | `G(native-41990cb3-ps51-checkpoint-guard, powershell.exe -NoProfile -File scripts/test-checkpoint-stamp-ps51-parse.ps1)` | 0 | `E/checkpoint-ps51-guard-gated.log`: `PARSER PASS version=5.1.19041.6456 edition=Desktop ascii=4 parsed=4 negative=2`. |
| Script-tree audit | `powershell.exe -NoProfile -File "$e/audit-ps51.ps1" -Repo $PWD.Path` | 0 | Inventory, not a blanket pass: `E/ps51-audit.json` records all **228** script-tree `.ps1` files, byte counts, required versions and actual 5.1 parser errors. See findings below. |
| Root script audit | `powershell.exe -NoProfile -File "$e/root-audit.ps1" -Repo $PWD.Path` | 0 | Inventory of **20** root `.ps1` files in `E/root-audit.json`; non-ASCII and parser findings below mean the requested blanket compatibility condition fails. Preliminary guard probes `E/root-parse.ps1` and the explicit startup-file probe exited 1 at `dev-backup.ps1` and `verify-dev-stack.ps1`, respectively; the complete audit then enumerated every root file. |
| CP-8 | `G(native-41990cb3-cp8, dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-native-41990cb3/ -- --treenode-filter <CP8> --report-trx --report-trx-filename cp8.trx --results-directory "$e/cp8" --no-progress --no-ansi)` | 0 | `E/cp8.log:21`: **28 total, 28 passed, 0 failed/skipped**, **5m30.778s**; `E/cp8/cp8.trx`. PASS. Exact row roster/count from the plan, using the brief's single-build alternate output and build-slot route; no checkpoint-tool receipt is claimed. |
| Additional five classes, original PATH | Same no-build command, label `native-41990cb3-narrow`, filter `<NARROW>`, filename `narrow.trx`, directory `$e/narrow` | 2 | `E/narrow.log:134`: **23 total, 5 passed, 18 failed, 0 skipped**, 7.016s. All 18 failures were the WindowsApps shell guard. Historical red retained. |
| Only the 18 refused cases, installed shell | Same no-build command, label `native-41990cb3-narrow-installed`, filter `<RETRY>`, filename `narrow-installed.trx`, directory `$e/narrow-installed` | 2 | `E/narrow-installed.log:119`: **18 total, 15 passed, 3 failed, 0 skipped**, **7m17.708s**. The three remaining failures are listed below. No second build. |
| Exact containing class for the PS5.1 method, installed shell | Same no-build command, label `native-41990cb3-summary-class`, filter `/*/*/TestPhaseSummaryTests/*`, filename `summary-class.trx`, directory `$e/summary-class` | 0 | `E/summary-class.log:21`: **7 passed, 0 failed/skipped**, 58.161s. `C1514T_PS51AndRerun` is a method, not a class (`tests/Antiphon.Tests/TestHelpers/TestPhaseSummaryTests.cs:96`). |
| Scratch nightly wrapper, installed shell | `G(native-41990cb3-scratch-normal, pwsh -NoProfile -File "$e/scratch-native.ps1" -Mode normal)` | 0 diagnostic; wrapper 1 | `E/scratch-normal.log`: **15/15 native cases passed**, 2 complete timing rows, 1 timeline, no remaining owned processes. Qualification stays incomplete/red for the intentionally partial roster. Scope limitations below. |
| Scratch killed chunks, installed shell | Same command, label `native-41990cb3-scratch-killed`, `-Mode killed` | 0 diagnostic; wrapper 1 | `E/scratch-killed.log`: **2 timeout rows**, missing TRX retained, no invented completion, no remaining owned processes. Expected timeout qualification failure. |
| Independent Windows descendant custody | `G(native-41990cb3-C1039_DescendantExit, pwsh -NoProfile -File scripts/test-nightly-native.ps1 -Case C1039_DescendantExit -ResultsDirectory "$e/C1039_DescendantExit" -FixtureExecutable <FIXTURE>)` | 0 | `E/C1039_DescendantExit.log`: **17 passed**, including independent child handles, job membership, breakaway refusal and zero remaining job count. |
| Independent Windows output/handle completion | Same command, label/case/directory `C1039_DrainBeforeReturn` | 0 | `E/C1039_DrainBeforeReturn.log`: **22 passed**, including held stdout/stderr/log-write barriers and final sentinels. |

Exact filter/fixture tokens (literal `|`, not shell pipelines):

```text
CP8 = /*/*/(TestTimingRecorderTests*|TestTimingLifecycleTests*|TestTimingChokePointTests*|TestPhaseSupervisorTests*|TestPhaseSummaryTests*|TestPhaseCustodyTests*)/C1514T_*
NARROW = /*/*/(NightlyScriptsTests*|NightlyPrerequisiteTests*|NightlyDiscoveryParserTests*|NightlyExecutionDiagnosticTests*|ChangeAwareReleaseTests*)/*
RETRY = /*/*/(NightlyPrerequisiteTests*|NightlyDiscoveryParserTests*|NightlyExecutionDiagnosticTests*|ChangeAwareReleaseTests*)/*
FIXTURE = C:\Antiphon\worktrees\card-task-41990cb3\tests\Antiphon.Tests\bin-native-41990cb3\nightly-owned-child\OwnedChild.exe
```

`TestPhaseSummaryTests`, `TestPhaseSupervisorTests` and `TestPhaseCustodyTests` were all covered by CP-8; the summary class was additionally run with the explicitly requested exact class filter. CP-8 is the row at `docs/superpowers/plans/2026-10-23-card-1514-test-timing-instrumentation-plan.md:475`, referenced by `docs/testing-and-build.md:1625`.

## Overhead evidence

`E/overhead-raw.json` is copied unedited from the exact `C1514_OVERHEAD` path in `E/cp8/cp8.trx`: `C:\Users\lndco\AppData\Local\Temp\c1514-976877ec1126479b91e78880b785b982\overhead-raw.json`.

- Nine predeclared paired samples; minimum OFF 10,324.1525 ms, minimum ON 10,305.0955 ms; matching repetitions; **zero dropped records**.
- Paired median **0.165096%**, bootstrap 95% upper bound **0.325124%**, both below 2%.
- Raw ratios in percent: `-0.743420, -0.184587, 1.150420, 0.930980, 0.325124, 0.165096, 0.238853, -1.499258, -0.017741`.
- Shared measured setup 8,626.9714 ms. Scope-only OFF/ON 40.002 / 1,659.22 ns; allocated bytes per scope 0 / 693.60136. Whole-case OFF/ON 40.926 / 3,592.252 ns per scope, including the enabled flush/serialization path. Enabled bytes per case 12,585.32.
- The method took 3m44.745s. Assertions are `TestTimingChokePointTests.cs:163`, `:197`, `:198`; the test preserves raw samples at `:162` and `:194`. No retries, discarded pairs or threshold changes.

## Confirmed failures and compatibility boundaries

1. **The requested standalone PS5.1 execution is impossible at this source.** `scripts/test-qualification-timing.ps1:1` declares `#requires -Version 7.0`; the native 5.1 engine rejects it before any assertion. The script itself is ASCII and parses under 5.1. This differs from the actual summary library/replay, which passed on 5.1 in CP-8 and the exact summary-class run.
2. **The initial 18 failures were executable selection, not failing timing assertions.** `ScriptHarnessProcess.cs:191` walks PATH and accepts the first existing `pwsh.exe`; the observed first path was `C:\Users\lndco\AppData\Local\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe`. `WindowsScriptHarnessProcess.cs:64` rejects WindowsApps/reparse executables, with the exact error: `ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.` Prepending the installed executable for that child run admitted all 18 cases; 15 then passed and three reached the separate failure below. System/user PATH was not edited.
3. **Three ChangeAwareRelease methods reached the selection-worker deadline.** `scripts/lib/release-selection.ps1:291` starts a worker via `CheckpointProcessRunner.Run(..., 60000, false)`; `:294` throws `selection-worker-timeout` when its timeout bit is set. `E/narrow-installed.log:14`, `:45`, `:74` and the corresponding TRX rows reconstruct the propagation to `ScriptHarness.Validate` (`ScriptHarness.cs:25`, expected exit 0, actual 1):

   | Method | Duration | Exact observed failure |
   |---|---:|---|
   | `C1313_ParameterHops` | 116.986669s | `hop-summary-count=0`; `FAIL C1313 hop-baseline-bound`; release-cut refusal `selection-worker-timeout`, exit 3; harness exit 1. |
   | `C1313_BaselineAuthority` | 84.147272s | Exception at `release-selection.ps1:294`: `selection-worker-timeout`; harness exit 1. |
   | `C1313_ModeCredit` | 188.383804s | `default-full` and `explicit-full` passed, then `selection-worker-timeout`; harness exit 1. Earlier fixture output also contained `run-context ownership (origin/wrong-origin)`; it was not the terminal assertion. |

   These runs already used the commissioned base source, with no source modifications. They are not attributed to this documentation task or proven to be regressions introduced by the timing patch. The inner operation consuming the worker budget is not established by these transcripts. A separate diagnostic comparison is recorded below; no deadline was widened and the failed class verdict is retained.

4. **Blanket ASCII/PS5.1 compatibility does not hold across startup tooling.** The changed timing production scripts and ordinary AppHost guard roster are ASCII and parse under 5.1. The inventory nevertheless records:
   - Under `scripts/`: `test-test-selection.ps1` has 2 non-ASCII bytes; `verify-agent-tui-profile.ps1` has 33 and a 5.1 parser error at line 217. These are separate tooling findings, not claimed timing-library failures.
   - Root scripts with non-ASCII bytes: `dev-backup.ps1` 3, `dev-fresh.ps1` 27, `dev-restore.ps1` 12, `dev-start.ps1` 591, `dev-upgrade.ps1` 33, `restart-session-runner.ps1` 3, `verify-dev-stack.ps1` 879. Parser-error counts are respectively 0, 2, 0, 1, 2, 0, 38. Root `dev-aspire.ps1` is ASCII and parses cleanly.
   - `verify-dev-stack.ps1:82` uses the PS7 ternary operator: actual 5.1 error `Unexpected token '?' in expression or statement.` Its PS7 parse has zero errors. This was a parser-only check, not a live health probe.
   - Some scripts intentionally require PS7: notably the build-slot entry and `scripts/lib/nightly-owned-process.ps1:1`. The latter is loaded at `nightly-tests-impl.ps1:765`, so a 5.1-parsing wrapper is not proof that its native execution boundary runs in 5.1.
   - `git diff a97194ee7a4adf215454fd82c453ba46362596ee HEAD -- verify-dev-stack.ps1 scripts/lib/nightly-owned-process.ps1 dev-start.ps1` was empty. Those bytes predate the candidate timing work. Full inventories are retained; no runtime startup/service mutation was attempted.

## Scratch wrapper and cleanup evidence

An independent clone was created with:

```powershell
git clone --local --no-hardlinks --single-branch --branch feat/card-task-41990cb3 C:\Antiphon\worktrees\card-task-41990cb3 C:\Antiphon\scratch\native-41990cb3
```

Its HEAD was the candidate SHA. Only the already-built test output was copied to its ordinary `tests/Antiphon.Tests/bin/Debug/net9.0` location, with the DLL hash checked. The external policy selected `Antiphon.Tests.TestHelpers.TestTimingRecorderTests` and `Antiphon.Tests.TestHelpers.TestTimingLifecycleTests` in two exact class chunks. The production `nightly-tests.ps1` entry was called with private `-RepoRoot`, `-LogRoot`, `-PolicyPath`, `-SeamsPath`, `-Trigger feature`, candidate `-Sha`, task `-GitRef` and unique `-RunId` (`E/scratch-native.ps1`).

**Limit:** `nightly-tests-impl.ps1:1104` through `:1177` unconditionally run npm prerequisites and a solution build. To honor the brief's one-build constraint, `E/scratch-seam.ps1` intercepted just those four prerequisite commands and logged each in `prerequisites-not-executed.log`. Every native execution/discovery call used the unchanged `Start-NightlyNativeOwner` with real compiled binaries and its real completion result. Consequently this proves native timing integration/custody with reused binaries, **not an unseamed full bootstrap/build/qualification run**. Prerequisite/build timing in this diagnostic is synthetic and receives no build or qualification credit. The bootstrap/lock entry `nightly-run.ps1` was not used; no shared native-run lock was touched.

Normal run evidence:

- `E/live-heartbeat.json` captured the second row while its last phase was `run`, `lastPhaseUtc=2026-10-27T16:57:03.9378776Z`, `completedAt=""`, before the wrapper returned. The production write-before-launch is `nightly-tests-impl.ps1:1359`.
- Both `antiphon-tiny-class-1.timing.json` and `antiphon-tiny-class-2.timing.json` exist beside TRX and under `timing-native-41990cb3-normal`. TRX counts are **14/14** and **1/1** passed.
- `summary.json` has the resolved `timingRoot`, **two `timingChunks`**, and **one `suiteTimeline`**. Timeline: `2026-10-27T16:56:12.6933338Z` through `2026-10-27T16:57:38.5479646Z`.
- First/second discovery wall: **31.360 / 27.452s**; run wall **5.184 / 7.027s**; p50 **0.019 / 5.081s**, p95/max **0.265 / 5.081s**. The emitted `summary.md` has the standard phase table with populated test IDs, Passed outcomes and totals; `tests.csv`, planned/executed manifests and JSONL shards exist.
- Gate wait **91s** for the memory floor is retained separately in `E/scratch-normal.log`; the wrapper itself documents build-slot `not-used`/unknown fields, not an estimate of this outer gate wait.
- Wrapper exit **1**, `testsPassed=true`, `coverageComplete=false`: expected partial-suite selection and `missing-expanded-row union` for omitted cases (`nightly-tests-impl.ps1:1598`). The two executed chunk verdicts are `pass`, exit 0, empty `evidenceReasons`. The diagnostic assertions return 0 without changing the qualification verdict.

Killed run evidence:

- The external diagnostic policy used a 100ms watchdog; no repository timeout was changed. Two real native test hosts were terminated by the Windows owner. Both timing rows retain `lastPhase=timeout`, `why=timeout`, missing TRX and blank `completedAt` (`E/scratch-killed/*.timing.json`).
- Native result rows for PIDs **431124** and **463416**: `TimedOut=true`, `TerminationSucceeded=true`, `RemainingProcessCount=0`, `CleanupComplete=true`, `OutputDrained=true`, `jobHandleAfter=0`. Normal run PIDs **85340**, **229176**, **450216**, **190804** likewise returned exit 0, zero remaining count, complete cleanup and zero job handle.
- Both run logs report `REMAINING_OWNED_PROCESSES=0`. The independent 17+22 assertion native fixture checks additionally prove descendant-handle and pipe boundaries, rather than relying only on those self-reported booleans. Ownership/timeout/drain logic is `scripts/lib/nightly-owned-process.cs:168`, `:184`, `:194`, `:267`.

## Diagnostic comparison, remaining uncertainty and disposition

One additional **diagnostic**, on the same source with the installed shell and the same compiled helper, ran the failing baseline harness outside TUnit:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label native-41990cb3-release-timeout-repro -- pwsh -NoProfile -File scripts/test-change-aware-release.ps1 -Case C1313_BaselineAuthority -ResultsDirectory "$e/release-direct" -FilterHost C:\Antiphon\worktrees\card-task-41990cb3\tests\Antiphon.Tests\bin-native-41990cb3\checkpoint-filter-host\Antiphon.Checkpoints.FilterHost.dll
```

`E/release-direct.log` returned **exit 0, 14 passed / 0 failed**, with the build-slot command held **362 seconds**. The worker still had its unchanged 60-second deadline. The comparison **did not include TUnit's 300-second case budget** (`ScriptHarnessProcess.cs:16`) and therefore cannot replace the failed TUnit verdict. It also does not isolate outer ownership from host load or timing variance. No other failure was repeatedly rerun until green.

Host contention was observed later: the scratch gate waited 91 seconds on `memory_floor`; at 16:55:33 UTC an independent compiler occupied about 4,999 MiB and free memory was 5,052 MiB. This is evidence of changing host load, not proof that it caused the earlier selection-worker timeouts. A later read found that compiler had exited; the gate then admitted the scratch run. No foreign process was stopped.

The timing harness PS5.1 version rejection and original PATH guard are confirmed mechanisms. Native CP-8, timing rows and Windows custody have positive evidence. The fully green Windows obligation remains open because of the PS5.1 harness refusal, the three red ChangeAwareRelease cases and the explicitly seamed prerequisite path. The worker's detailed runtime attribution remains unresolved; per-worker phase/call timing and a controlled native run would resolve whether its budget is consumed by graph evaluation, child startup, contention, or another operation. No timeout extension, source repair or passing qualification is inferred from these observations.

Not done, noted: follow-up owns PS5.1 harness compatibility and the selection-worker timeout investigation; no fix was designed or implemented here.

Final read-only census at 2026-10-27T17:10:24.9072818Z found **zero running executables under the task's native build outputs or scratch clone**, and both tracked trees were clean before writing this report (`E/final-process-census.json`). All foreground runs were joined.

Automatic approval review rejected the attempted recursive cleanup of the 33 task-created `bin-native-41990cb3` directories and `C:\Antiphon\scratch\native-41990cb3`, returning **"blocked by policy"** with no more specific reason. The rejected command did not execute. A read-only recheck confirmed **33 output roots and the scratch clone remain**; exact output paths are in `E/build-output-roots.txt`. They are left for the caller's cleanup, as the brief anticipated. No alternate deletion route was attempted. Raw evidence remains under E; source repairs require a separately commissioned task.

This report routes to **Investigate** because the worker timeout's underlying cause remains unconfirmed; the confirmed PS5.1 version rejection is ready for caller triage. It does not grant a fully green Windows/Final Review result.

--- next stage ---
next: investigate
handoff: CP-8 passes 28/28 on d3fd72f941abda0fd6026f5e564d66ac410bdf47, but the timing harness requires PS7 and three ChangeAwareRelease cases hit selection-worker-timeout on Windows. Resolve worker runtime attribution and the remaining native compatibility obligation before green review; retain all red evidence.
artifact: docs/investigations/2026-10-27-native-timing-verification.md
