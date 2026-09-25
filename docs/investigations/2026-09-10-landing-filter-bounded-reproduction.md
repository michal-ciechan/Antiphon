# Landing* filter: bounded reproduction and async dump

No deadlock was reproduced on local master `7a6b44f97a7534578fa017af35c1376f90809729`. The exact filter advanced through nine passing cases during a ten-minute observation; its diagnostic snapshot showed a real Git command in progress. The captured method then passed all four argument rows in a focused rerun. **Full-filter completion and the historical 30-minute incident remain unverified.**

This corrects the certainty of the earlier [CARD-0415 investigation](2026-09-09-card-0415-landing-tests-600s-hang.md), not its recorded observations. A long-running, silent process was observed there; a deadlock was not established.

## Measured results

Worktree: `C:\Antiphon\worktrees\card-task-c2965233`. Date: 10 September 2026. Local master and HEAD both matched the SHA above before building. TUnit 1.44.0, Microsoft.Testing.Platform 2.2.2, .NET runtime 9.0.16, Windows 10.0.19045. No production source or test changes were made.

| Operation | Results | Duration / limit |
|---|---|---|
| Isolated-output build | 0 errors, 143 warnings | 52.21 seconds |
| Exact `/*/*/Landing*/*` observation | 9 passed, 0 observed failures; remainder not assessed | 600-second observation cutoff, followed by CPU samples and dump capture; watcher ended at 622 seconds |
| `LandingAdmissionControlTests.C448_V14_shared_land_first` rerun | 4 passed, 0 failed, 0 skipped; fresh TRX | Runner 3m44.261s; watcher 230 seconds; 600-second hard limit |

The exact-filter process was deliberately stopped after capture; exit -1 is termination, not an assertion failure or a green suite result. It produced no final TRX. Nine completions are counted from its detailed log. The source contains 211 expanded cases across the six matching classes; this is a static count, not executed coverage. The four rerun cases are additional completions, not proof of all 211 cases.

The initial local watchdog preparation had an argv-construction error and launched a process without the intended arguments. PID 27892 was stopped after 26 seconds, with only startup output observed. It supplies no test evidence and is excluded. The corrected exact-filter run used PID 29060; the focused repeat used PID 34460. Builds and test processes were sequential. No concurrent Antiphon.Agents.Pty.Tests process was found during the ownership checks.

## CPU and completion evidence

The corrected exact-filter process started at 18:16:19 BST. Samples were collected roughly every ten seconds. Examples from `landing.progress`:

| Elapsed | Cumulative CPU seconds | Log bytes |
|---:|---:|---:|
| 62s | 28.7 | 5,083 |
| 73s | 29.5 | 5,083 |
| 255s | 39.6 | 5,304 |
| 317s | 43.3 | 5,376 |
| 380s | 48.6 | 5,444 |
| 463s | 53.0 | 5,595 |
| 561s | 56.5 | 5,670 |

The independent final pair was **57.96875 CPU seconds at 18:26:23.545**, then **58.1875 at 18:26:28.597**: +0.21875 CPU seconds in 5.052 wall seconds. CPU activity alone cannot exclude a livelock, but the nine successive passing cases establish actual progress. Their body times ranged from 25.993 to 97.319 seconds. The 180-second quiet-and-idle condition did not fire. This was an observation cutoff, not a detected stall.

The old shell command piped output through `grep -E "^\s+(total|failed|succeeded|skipped):|^failed "`. That removes passing-case lines even with detailed output; default normal output is also quiet for passing tests. An unchanged filtered transcript for 30 minutes is therefore compatible with a large progressing suite. The old single cumulative value of approximately 117 CPU seconds cannot establish CPU activity between observations.

## Exact async snapshot

`dotnet-dump collect` succeeded before the owned process tree was stopped. The full dump is 546,774,640 bytes. `dumpasync` stack 43 shows:

```text
LandingAdmissionControlTests.C448_V14_shared_land_first(Fresh)
  -> AgentTaskLandAdmissionTests.C448_V14_RealDispatchAdmissionAndEveryLandModeExcludeEachOther
     writerKind=shared, mode=Fresh, order=land-first
  -> LandingSafetyHarness.RunAsync
  -> AgentTaskLandService.RunAsync / RunRequestAsync
  -> AgentTaskLandingProtocol.RunAsync / CheckTargetAsync
  -> LandingGitFixture.FixtureGit.RunAsync
  -> LandingGit.RunAsync / ExecuteAsync
  -> System.Diagnostics.Process.WaitForExitAsync
```

The method's state-machine string fields establish the argument values; this is not an inference from the last completed test. The Git argument collection contains **`symbolic-ref`, `-q`, `HEAD`**. The await is `server/Infrastructure/Git/LandingGit.cs:59`, called from `server/Application/Services/AgentTaskLandingProtocol.cs:375`. Standard-output/error readers are also pending, as expected while waiting for the child. Other scheduler stacks wait in TUnit's `ExecuteTestInternalAsync` semaphore. There is no demonstrated circular dependency in this snapshot.

The snapshot was taken roughly 50 seconds into this case, not after a long stationary await. A single process-exit wait cannot prove a hung Git child. The focused rerun strengthens that distinction:

| Mode | Result | Body duration |
|---|---|---:|
| CleanupRetry | Passed | 61.3398s |
| AlreadyPresent | Passed | 19.0324s |
| ResumePublication | Passed | 50.7301s |
| Fresh, the captured row | Passed | 62.0753s |

No assertion or production repair is justified by these observations.

## Why this filter is expensive

The filter does not directly select the `AgentTaskLand*` classes from CARD-0474, but several selected classes call those test methods directly:

| Matching class | Expanded cases | Work |
|---|---:|---|
| LandingAdmissionControlTests | 42 | Calls admission and concurrency real-Git tests |
| LandingSourceBoundaryControlTests | 30 | Calls the boundary real-Git matrix |
| LandingRemovalControlTests | 34 | Calls removal and cleanup safety real-Git tests |
| LandingGitTests | 29 | Git integration and parsing checks |
| LandingIdentityControlTests | 26 | Controlled inspection decisions |
| LandingRemovalPolicyControlTests | 50 | Controlled removal decisions |

The first three wrappers account for 106 rows that repeat existing safety-test methods under additional names. The process-spawning classes share `ProcessSpawnLimit.Limit = 1`, so expensive Git cases serialize. Their fixtures create private temporary repositories and local remotes; they do not clone the full Antiphon repository. This provides a concrete connection to CARD-0474's expensive protocol matrices, rather than merely similar class names.

`git diff c2423ba8 HEAD` was empty for the six matching test files and the examined admission, concurrency, boundary and removal-matrix implementations, LandingGitFixture and LandingGit. This supports relevance to the earlier incident, but is not a complete comparison of every dependency or the earlier running environment.

The supported conclusion is **slow, progressing Git work in the observed window; historical hang unproven**. It would overstate the evidence to say the historical run definitely had no deadlock, or that all current filter cases finish. A complete longer watched run is the next experiment if that stronger claim is required. Performance planning should include these wrappers alongside CARD-0474's matrices and retain the existing safety coverage and PTY limiter.

## Artifacts and reproduction

Evidence directory: `C:\Antiphon\worktrees\card-task-c2965233\.antiphon\landing-c2965233`.

Key files: `build.log`, `build-output-inventory.txt`, `landing.log`, `landing.progress`, `completed-cases.txt`, `cutoff-cpu.json`, `cutoff.txt`, `cutoff.dmp`, `dump-collect.log`, `active-stack.txt`, `dumpasync.txt`, `active-states.txt`, `active-identity.txt`, `active-command.txt`, `source-counts.json`, `admission-repeat.log`, `admission-repeat.progress`, `admission-repeat.trx`, and `repeat-summary.json`.

`cutoff-children.json` is a raw parent-PID query, not an ownership inventory: it also matched two older live PtyHost processes whose previous parent had the same recycled PID. `parent-pid-reuse-audit.json` records their earlier creation times and confirms both remained alive after test shutdown. Do not kill processes based only on that raw list. The retained repeat helper filters creation times and uses the owned Process object for shutdown.

From this worktree:

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c2965233/ --nologo
# Fresh output directory, CPU sampling, 180s quiet/idle trigger, bounded dump capture.
pwsh -NoProfile -File .antiphon/landing-c2965233/reproduce.ps1 -TimeoutSec 600
pwsh -NoProfile -File .antiphon/landing-c2965233/reproduce.ps1 -Filter '/*/*/LandingAdmissionControlTests/C448_V14_shared_land_first' -TimeoutSec 600
dotnet-dump analyze .antiphon/landing-c2965233/cutoff.dmp -c dumpasync -c exit
```

The measured runs used the retained `watch.ps1` copy of `scripts/run-tests-watched.ps1`, with detailed output, TRX arguments and hidden windows. The broad run initially had a 5,400-second outer ceiling; `cutoff.ps1` imposed the deliberate ten-minute snapshot/stop. `reproduce.ps1` is a consolidated repeat helper, syntax-checked after the measurements; it was not used to produce the reported timings. Exit 124 from that helper indicates a diagnostic cutoff, not a proven deadlock. Use a new tag for each run and keep test projects sequential.

Only investigation documentation and ignored diagnostic/build artifacts were created. No stack restart, production runner request, test-source fix, timeout-policy change or parallelism change was made.

--- next stage ---
next: decide
handoff: No deadlock reproduced: ten-minute Landing* run made nine completions; dump found a Git child wait and the captured method passed all four rows. Historical 30-minute hang and full-filter completion remain unverified. Decide whether to require a longer complete watched run or fold wrapper duplication into CARD-0474 performance planning.
artifact: docs/investigations/2026-09-10-landing-filter-bounded-reproduction.md
