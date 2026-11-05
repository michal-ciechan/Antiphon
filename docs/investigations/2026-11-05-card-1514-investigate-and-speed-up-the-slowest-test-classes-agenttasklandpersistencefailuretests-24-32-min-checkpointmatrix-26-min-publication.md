# CARD-1514: g120 C1313_BaselineAuthority timeout investigation

Date: 2026-11-05. Task: `5c10955b-0618-4b90-9c51-d5d9f42de4f7`.

## Outcome

The initiating Windows root cause remains **unconfirmed**. A completed root wait task is not proof that its process exited, and all 14 PASS assertions precede script cleanup. The reported signature therefore cannot distinguish slow execution/cleanup from a pipe or exit wait defect. A portable reconstruction confirms the diagnostic ambiguity. Ten Linux direct-case invocations (five rc, five master) completed in 246.882–294.207 seconds; last-PASS-to-root-exit intervals were 0.405–1.285 seconds. No exit/EOF hang was reproduced. Thirty requested repetitions remain unrun within the 45-minute budget. These observations grant no Windows or TUnit qualification credit.

## Incident provenance and limits

The stored assignment, `.antiphon/inbox/5d445fb7-6eb1-472e-aa48-035ef43fbcac.md:5`, reports Windows qualification run `b19a9880`, group `antiphon-all-group-120`, rc source `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a`, observed around 2026-11-05 06:15 UTC:

```text
TimeoutException: ScriptHarness test-change-aware-release.ps1 case C1313_BaselineAuthority exceeded 00:05:00 execution budget (completion root=True stdout=False stderr=False)
```

The same incident text is stored in the `goal` field returned by `GET /api/agent-tasks/5c10955b-0618-4b90-9c51-d5d9f42de4f7` (read-only task-row projection, retained as `task.json`). It is a recorded caller report, not the underlying g120 TRX.

The assignment reports all 14 `PASS C1313` lines, desktop CPU 100%, 6.8/32 GB free at 06:18 UTC, other slow/failing groups, and a concurrent cleanup task canceled at 06:20 UTC. Those are caller observations, not independently retrieved process/resource measurements. The original g120 TRX, console timestamps, timing sidecar, native root status and pipe handle owners are not available in this Linux checkout. The brief does not say whether the C487 summary/exit markers were present. I requested that specific stored excerpt without touching the desktop or its live qualification.

The task API returned task ID above, `cardId=c92555be-65d0-45b6-a759-3d1ba5938bd2`, `cardIdentifier=CARD-1514`, board `8988ca03-7414-47ad-b0b6-51556c701703`, runner `server2`, observed platform `linux`. The card is the existing slow-test investigation, not a separately filed g120 bug. Raw read-only task/card responses are in the ignored evidence root.

## Mechanism reconstructed from source

All file:line references in this section refer to assigned rc SHA `5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a`.

1. `tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:23` gives the default execution/cleanup budgets as 300/10 seconds, with a 30-second post-exit drain. `ChangeAwareReleaseTests.cs:16` uses that default for the 14-row case; `ChangeAwareReleaseTests.cs:35` separately grants 720 seconds to the cleanup-ownership witness. That separate test does not raise this case's budget.
2. `ScriptHarnessProcess.cs:84` starts the root wait with the execution token. `WindowsScriptHarnessProcess.cs:214` polls the native process handle and `:217` awaits a cancellable delay. Execution cancellation can complete this task in the Canceled state before the process signals. `ScriptHarnessProcess.cs:137` prints `root.IsCompleted`, which includes cancellation and faults, rather than successful native exit. A `True` there is not an exit receipt.
3. The final assertion is at `scripts/test-change-aware-release.ps1:286`. The caller still executes the `finally` at `:390`: retain diagnostics (`:394`), clear read-only fixture files (`:395`), and remove fixture roots (`:398`). Only afterward does `:400` call `Complete-C487Harness`. `scripts/lib/c487-harness.ps1:345` also clears read-only results before printing its summary (`:347`) and successful exit marker (`:357`). All PASS lines can coexist with a still-executing root.
4. `ScriptHarnessProcess.cs:96` arms its post-exit timer after it has observed and removed the root task. A drain-only timeout has distinct text and a `holder=` diagnostic at `:118`. Execution expiry remains linked during drain (`:105`), so a root observed near 300 seconds can still produce the generic execution-budget message. The reported message is not sufficient to establish which situation occurred.
5. In the success path, `ScriptHarness.cs:25` requires exit 0, the C487 exit marker, the complete 14-row inventory and the summary. PASS count alone does not meet the existing test contract.
6. Each `New-ReleaseSelectionAttempt` starts a fresh PowerShell worker through the shared process runner (`scripts/lib/release-selection.ps1:283`, `:293`) with a 60-second boundary. Git selection reads use a 30-second boundary (`scripts/lib/test-selection.ps1:139`). `scripts/lib/CheckpointProcessRunner.cs:169` jointly waits for direct-child exit and both streams; its `finally` terminates its owned process group/job (`:173`). Git automatic maintenance/fsmonitor/hooks are disabled at `:127`. These are source bounds and ownership intentions, not proof that no historical native handle leaked.
7. The outer `ScriptHarnessProcess` deletes its owned result directory only after its execution/validation region and cleanup join (`ScriptHarnessProcess.cs:149`, `:175`). That outer directory deletion cannot itself be the five-minute execution wait. The script's own earlier fixture deletion can be inside it.

8. The Windows adapter creates inheritable pipe endpoints at `WindowsScriptHarnessProcess.cs:87`, clears inheritance on the parent read ends (`:103`), restricts its own child to the three standard handles (`:121`), and closes its parent copies of child endpoints with `using` (`:98`, `:179`). `ScriptHarnessWindowsOwnershipTests.cs:212` checks the own-child whitelist, and `:240` checks parent closure. Those source paths do not identify a historical foreign holder. Its reader explicitly uses a synchronous-handle `FileStream` (`WindowsScriptHarnessProcess.cs:408`); the Linux Node observer does not execute that Windows reading path.

The same ambiguity had already been retained in `docs/investigations/2026-10-27-scripts-lane-product-repairs.md:77`. Its historical Linux case took 185.18 seconds (`:56`), and its earlier native report stopped after 12 assertions. That is separate evidence, not a rerun or diagnosis of this 14-assertion g120 incident.

### Portable diagnostic reconstruction

The ignored `root-state-probe.ps1` reads and compiles the exact current `WindowsScriptHarnessProcess.StartAndWaitForRootAsync` method body. Its native observations are modeled as either a never-signaled process or a signaled process; no native Windows API executes. Under the granted master-batch lease, the unedited output was:

```text
ROOT_STATE exitBeforeCancel=False root=True canceled=True successful=False processSignaled=False
ROOT_STATE exitBeforeCancel=True root=True canceled=False successful=True processSignaled=True
```

This confirms why the stored boolean cannot prove process exit. It does not reconstruct which state the actual desktop process had at expiry.

## Comparison and measurement method

Assigned branch stayed `feat/card-task-5c10955b`; rc HEAD stayed at the assigned SHA during every run. `git fetch origin master` pinned master at `3ce7f87ef06315ace62572d7e51b2de0347dbef9` on 2026-11-05. Master was exported with `git archive` to an ignored local source snapshot, with a private detached HEAD/index backed by local object alternates so the unchanged fixture's `git ls-files '*.csproj'` uses the master census. No task branch switch, target merge, external executor, desktop run, deployment or landing occurred.

The outer harness, Windows adapter, case script and all `scripts/lib` files are byte-identical between those SHAs. Master does differ in `tests/Antiphon.Tests/Antiphon.Tests.csproj` (OpenTelemetry reference/linked source), which the fixture copies into its private Git repository, and in other test files. Master measurements execute that actual snapshot rather than substituting rc bytes. Matching code does not prove matching native runtime outcomes.

Selected source SHA-256 values (matching on rc and master):

| File | SHA-256 |
|---|---|
| `tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs` | `236c0c695d6ce6e9f927db44bc2f2c75b33d95f5f39f3530b6959e2c7398a165` |
| `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs` | `4d0678c53660aa1029e3a3825655df4a5447035d110850fc3e5585e8c49e346b` |
| `scripts/test-change-aware-release.ps1` | `9555348ad4e1371ee88572a4f8e32e3bc524ed884431441b6e3863d37d56430a` |
| `scripts/lib/CheckpointProcessRunner.cs` | `4e0097a6256268d93b1bac938a76f132d85f32d328fc4a8c423b32911573e69f` |

Host: Linux `4.15.0-213-generic`, 24 visible processors, no cgroup v1 CPU quota or practical memory limit. `/tmp` and `/work` reside on the same ext4 filesystem. It is a shared loaded server2 host, not an idle or Windows-equivalent laboratory. Each JSONL row retains start/end `/proc/stat`, memory and load averages. CPU percentage below uses aggregate non-idle/non-iowait deltas excluding guest double counting. Phase fields absent from instrumentation remain unknown; there are no inferred zero-cost DB/HTTP/container phases.

Each direct invocation runs only `C1313_BaselineAuthority`, with real local Git, fresh fixture roots, and `pwsh -NoProfile -NonInteractive`. This case does not call `Get-FxDiscovery` or use `FilterHost`; omitting that otherwise supplied argument does not omit its 14 assertions (`scripts/test-change-aware-release.ps1:223`). A Node observer records each PASS, C487 markers, root process exit, stdout/stderr EOF and total wall time. Its diagnostic bound is **360 seconds**, deliberately independent of the unchanged harness's 300-second deadline; a direct script completion above 300 seconds is not a passing TUnit harness result. Two-second process-census observations in later samples track owned descendant identities; short-lived between-poll children can be missed. No sampled non-survival claim establishes Windows pipe ownership.

One host build-slot gate covers each sequential case batch; any separate build has its own gate. Slot waits are excluded from case timings. Commands run in the foreground and all returned sessions are joined before settlement. The requested 20 rc plus 20 master repeats cannot be represented as completed unless all 40 rows exist. The first rc wall time was 246.882 seconds: serially extrapolating that observation gives about 164.6 minutes for 40, exceeding the brief's 45-minute investigation budget. The repeat driver has a cutoff for starting further cases, leaving its complete six-minute per-case bound and publication time. Counts and shortfall below are explicit, with no small-sample percentile promoted into a timeout prescription.

### Results

Direct case invocations: **10/10 completed with all 14 assertions, C487 success markers and exit 0**; 0 hit the diagnostic 360-second bound. No compiled TUnit test was run. 0 direct invocations exceeded the existing 300-second harness execution allowance.

| Revision | Completed / requested | Min s | Median s | Max s | Remaining unrun |
|---|---:|---:|---:|---:|---:|
| rc | 5 / 20 | 246.882 | 263.002 | 294.207 | 15 |
| master | 5 / 20 | 252.270 | 261.139 | 290.098 | 15 |

**Repetition shortfall: 30 of the requested 40 remain unrun.** The observer stopped starting new cases after 06:51 UTC to preserve its full 360-second per-case bound and the 45-minute publication budget. Both batches reached that cutoff normally; a time-budget stop is not 20 passing samples. These are shared-host samples, not comparable idle/loaded native percentile evidence.

| Sample | Start UTC | Wall s | Last PASS s | C487 summary s | Root exit s | stdout EOF s | stderr EOF s | Result |
|---|---|---:|---:|---:|---:|---:|---:|---|
| rc-01 | 2026-11-05T06:26:26.199Z | 246.882 | 246.406 | 246.704 | 246.882 | 246.868 | 246.881 | 14/14, exit 0 |
| master-01 | 2026-11-05T06:30:48.164Z | 289.776 | 289.201 | 289.576 | 289.774 | 289.764 | 289.774 | 14/14, exit 0 |
| master-02 | 2026-11-05T06:35:37.941Z | 290.098 | 289.339 | 289.873 | 290.096 | 290.085 | 290.096 | 14/14, exit 0 |
| rc-02 | 2026-11-05T06:36:04.108Z | 294.207 | 293.642 | 294.055 | 294.206 | 294.196 | 294.205 | 14/14, exit 0 |
| master-03 | 2026-11-05T06:40:28.042Z | 252.270 | 251.007 | 252.013 | 252.269 | 252.268 | 252.268 | 14/14, exit 0 |
| rc-03 | 2026-11-05T06:40:58.316Z | 261.567 | 260.392 | 261.306 | 261.566 | 261.531 | 261.566 | 14/14, exit 0 |
| master-04 | 2026-11-05T06:44:40.315Z | 261.139 | 260.583 | 260.967 | 261.138 | 261.128 | 261.137 | 14/14, exit 0 |
| rc-04 | 2026-11-05T06:45:19.910Z | 291.271 | 290.181 | 290.939 | 291.269 | 291.203 | 291.269 | 14/14, exit 0 |
| master-05 | 2026-11-05T06:49:01.480Z | 252.556 | 252.150 | 252.462 | 252.555 | 252.551 | 252.555 | 14/14, exit 0 |
| rc-05 | 2026-11-05T06:50:11.204Z | 263.002 | 261.716 | 262.443 | 263.000 | 262.887 | 262.999 | 14/14, exit 0 |

Across the completed samples, the last-PASS-to-root-exit interval was **0.405–1.285 seconds**. The observer saw both EOFs and root exit in every completed invocation; no open-ended exit/EOF wait was reproduced. A timer or pump inside the Windows adapter was not exercised by this observation. The 9 monitored samples had 0 known surviving descendants at post-exit census; the initial rc sample had no descendant census and supplies no such non-survival proof. In master-01, the census observed 19 PowerShell processes (root plus 18 descendants). Its individual child-call durations were not instrumented.

The first rc sample reached its first assertion at **24.284 seconds**, baseline-full-bound at **108.823 seconds**, and its last assertion at **246.406 seconds**. C487 summary followed at **246.704 seconds** and root exit at **246.882 seconds**. Those are observed milestones, not separately attributed Git/PowerShell/filesystem phase costs. All samples select the same script case, corresponding to the Integration-category method in `tests/Antiphon.Tests/Scripts/ChangeAwareReleaseTests.cs:8`; their IDs distinguish fresh invocations, not additional TUnit members or qualification receipts.


### Build load and admission

The intentionally concurrent load was one native Debug build at unchanged rc source:

```sh
TEMP=/tmp timeout --signal=INT --kill-after=30s 1200s pwsh -NoProfile -File scripts/build-slot.ps1 -Label 5c10955b-cpu-load-build -SlotWaitMinutes 5 -- dotnet build server/Antiphon.Server.csproj -p:UseAppHost=false --nologo
```

It succeeded: **0 errors, 24 warnings**, MSBuild elapsed **104.75 seconds**, lease held **107 seconds**, admission wait **271 seconds**, grant `maxcpucount=6`, lease `a49b7294-78de-4ad4-b022-cedb893066f6`. Its foreground wrapper session completed with exit 0. No custom output path, timeout change, test source or product repair was introduced. The separate 1,200-second outer bound includes admission plus build; it did not expire.

This build overlapped `master-01` (06:30:48.164–06:35:37.940 UTC). The process observation at 06:34 UTC retained the owned root dotnet PID 17183 under wrapper PID 1683 and five child dotnet workers, with working directories confined to this checkout. The first master sample consumed 289.776 seconds, versus 246.882 seconds for the earlier rc sample, with aggregate host active CPU about **73.90%** versus **65.22%** respectively. This is an observed shared-host comparison, not a causal 17.4% build-load factor: revision, time, source snapshot path and ambient workload also differ. It does not reproduce the caller's 100% Windows CPU or memory pressure.

Case batch admission: initial rc wait **166 seconds**, held **247 seconds**, lease `52d9243b-3ee0-4b30-911c-ff620db0ca31`; master wait **165 seconds**, held **1,351 seconds**, lease `26abe919-6f58-4fbb-87ac-cb945b3168aa`; rc repeat wait **316 seconds**, held **1,111 seconds**, lease `6e157829-a42b-495f-875b-0dfb2a05be1a`. Both repeat batches are sequential internally and overlap each other after admission; the selection is one exact script case, but the host is not isolated. These comparisons cannot supply an idle/native p95. All wrappers use the real configured host broker, without bypass.

## Evidence and repeat commands

Ignored evidence root: `/work/worktrees/task-5c10955b/.antiphon/investigate-5c10955b/`.
`measurements.jsonl` stores per-invocation events/resources; `<sample>.stdout.log` and `.stderr.log` retain unedited streams; `samples.md`/`summary.json` are derived; `rc-source-sha256.txt`/`master-source-sha256.txt` pin compared bytes; `measure.mjs` is the local observer and `root-state-probe.ps1` the modeled-native diagnostic. These are generated diagnostics, not committed test or product changes. The essential observations are preserved in this report.

The initial invocation was:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label 5c10955b-rc-sample -- node .antiphon/investigate-5c10955b/measure.mjs /work/worktrees/task-5c10955b rc 1
```

Subsequent batches use the same observer for rc repeats 2–20 and the pinned master snapshot (up to 20), subject to the recorded time cutoff. For an independently commissioned repeat without this observer, run the unchanged case under the gate with a fresh owned results path:

```sh
TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label g120-direct-case -- pwsh -NoProfile -NonInteractive -File scripts/test-change-aware-release.ps1 -Case C1313_BaselineAuthority -ResultsDirectory /tmp/g120-new-owned-results
```

That is a direct script measurement. To exercise the actual compiled harness, first build natively with `-p:UseAppHost=false` under one lease, then run under another with identical properties and `--no-build`, selecting only `/*/*/ChangeAwareReleaseTests*/C1313_BaselineAuthority`. This investigation did not run or claim the compiled TUnit selection.

## Remaining uncertainties and discriminator

- **Load-sensitive execution/cleanup remains live.** The first measured Linux body already consumes 246.406 seconds, while post-assertion completion takes 0.476 seconds. The caller reports saturated Windows CPU, but no comparable idle/loaded Windows timings or per-line timestamps establish its contribution. Linux timing cannot supply a Windows load multiplier.
- **A native exit/drain/handle defect remains live.** Neither `root=True` nor the 14 lines confirms historical native exit. A Windows-only reader starvation or retained pipe owner is not excluded by Linux direct-process completion. The historical pipe owner remains unidentified.
- Retrieve the original g120 exception including any cleanup suffix, stdout tail and timing sidecar. Establish the timestamp of the last PASS, whether C487 summary/exit markers appeared, and elapsed time before/after those markers. If the process was still live, locate its executing/waiting phase. If its native handle had signaled, correlate root task status, job member roster and actual pipe handle ownership; a job PID list alone is not a handle-owner proof.
- Once the live qualification permits a separately commissioned native diagnostic, collect successful/canceled/faulted root task state, native handle signaling/exit code, independently timestamped stdout/stderr EOF and process/job/pipe ownership before cleanup. Those observations distinguish the two mechanisms without a speculative timeout change. No desktop action is commissioned or taken here.
- Complete the unrun repetition counts with an explicit larger measurement budget or a scoped native collection after permission to use that host. Until then, p95 and native behavior on master remain unestablished.

## Not done, noted

No fix designed or implemented; a timeout change or process/pipe repair remains only a candidate pending native phase/ownership evidence.

## Publication and cleanup

The only tracked deliverables are this evidence report and the requested summary at `docs/investigations/2026-11-05-investigate-g120-c1313-timeout.md`, in one task-branch documentation commit. No product/test source, timeout, policy, qualification state or target branch was changed. The task-branch push and independently read remote tip are reported in the final receipt; no landing is requested or claimed.

All foreground script/build sessions completed with exit 0, all three case-batch leases and the separate build lease were released, and the diagnostic observer recorded zero known surviving sample descendants after final identity checks. The first sample had no descendant census; absence of an unobserved leak is not claimed.

Pre-build inventory contained zero native `bin`/`obj` directories; the build created 14. All 14 were removed by exact literal absolute paths, along with this task's fixture-results and empty retained-fixture directories. `cleanup-verified.json` records zero remaining owned output paths and zero known live sampled descendants. Raw event/resource logs and local source snapshots remain ignored. Compared source hashes were rechecked after all runs; the master snapshot has no tracked differences. Documentation whitespace and staged-path checks were completed before commit.

--- next stage ---
next: investigate
handoff: Retrieve g120's original TRX/console timestamps and C487 tail; distinguish canceled root wait from signaled native exit, then collect native pre-cleanup phase/job/pipe evidence when the live run permits. Preserve the measured Linux samples and complete the disclosed repetition shortfall; root cause remains unconfirmed.
artifact: docs/investigations/2026-11-05-card-1514-investigate-and-speed-up-the-slowest-test-classes-agenttasklandpersistencefailuretests-24-32-min-checkpointmatrix-26-min-publication.md
