# CARD-1233: bounded helper remaining scope

Confirmed: all six inherited reader expressions remain on current master, as do the CARD-1199 fixture gaps. The failure mechanism is incomplete coverage of file open, process exit, output drain and descendant lifetime by one bounded operation. PC-11 also has a confirmed caller-routing coverage gap. CARD-1362 fixes a different subsystem and removes none of this scope. Later Windows work qualifies part of output custody, but does not close R-2.

Investigation only; no source changes, builds, tests, native probes or mutations. The failure mechanisms below are reconstructed from current source and stored prior evidence. A listed possible leak is not a newly measured leak.

## Source and evidence identity

- Inspected on 2026-10-20 UTC. Assigned branch `feat/card-task-befb2bde`; its initial HEAD and freshly fetched `origin/master` both equal `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`. All unqualified file:line references below refer to that SHA.
- CARD-1199 landed ancestor: `e0877982c5dc41990c4e66b80cb47341a1bd0a41`. The historical inventory is `docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md` (called **inventory** below); its old line numbers refer to `d13387548b88ac3a6b37dc046370f40d880cae32`, not today's source.
- CARD-1233 database-backed card detail: ID `eaefb326-3c7e-4973-b7c2-5bbd033f971b`, Antiphon board `8988ca03-7414-47ad-b0b6-51556c701703`, read with `scripts/card.ps1 get CARD-1233 -Board Antiphon`. It explicitly preserves the inherited exclusions from CARD-1199 and records pending PC-11 routing detection and Windows qualification.
- CARD-1362 S2 reviewed source, fetched read-only: `df869f5651bc1400a8e27ef0b85f89db6300fc3c`, `refs/heads/feat/card-task-81ee79e0`. Its review does not make it part of this master snapshot.

Stored task rows read through `scripts/delegate.ps1 -Status <id>`:

| Task / database identity | Stored outcome relevant here |
| --- | --- |
| `9f9613bd-209f-4527-8168-53d940fc2035`, CARD-1199 Review, completed 2026-10-13 15:17 UTC | Scratch probes reproduced unbounded file reads and Git/graph output-drain hangs. Its detailed original report path was `/work/review-evidence/task-9f9613bd/report.md`; that directory is unavailable in this mirror. The checked-in inventory preserves the per-reader measurements and provenance. |
| `391118f3-1bc8-42f5-b04a-b22c55a9498b`, CARD-1199 Review, completed 2026-10-13 21:58 UTC | 259 passed, 0 failed, 1 planned Windows skip. Found overbroad claims that all fixtures used the helper; several still launched directly. |
| `dca33d6b-78e8-46af-95d3-8770bd408827`, CARD-1199 final Review, completed 2026-10-13 23:11 UTC | Clean after narrowing the claim to eight repaired sites. 259 passed, 0 failed, 1 planned Windows skip. Explicitly retains R-2 and PC-1..PC-11, including the routing-detection gap. Reviewed `3838973e991e437e5d5ac8ea205965fe46930699`. |
| `f82bf074-08be-4f2a-86dd-5abfc5fc2942`, CARD-1310 native Windows Review | At `a625778ef9dbf532d1458afa8182e6119a6a1e30`, run `20261017-064648-3f99`: two witnesses passed (seal/adoption and confined UseAppHost); output was adopted and removed, receipt validated. The seal witness includes hardlink and junction refusal. |
| `4c9cd695-21cf-4e73-9691-3f43a8c21096`, CARD-1318 native Windows Review | At `cc022d9122c2d8ef5407489e112b8e62c330f866`, run `20261017-152755-0e49`: four OutputCleanup witnesses plus `HostCleanupCheckpointCustodyTests.Stalled_receipt_read_is_retried_once`, 5 passed; validated receipt and deleted owned output. This is scoped custody evidence, not a shared process-runner race test. |
| `98ff4e38-57b7-4f1c-a38e-93e501899d55`, CARD-1362 S2 Review | Clean at `df869f5651bc1400a8e27ef0b85f89db6300fc3c`; CP-3 source pin 1/1, CP-4 queue/expectation regressions 56/56. Stored red pin failed on 37 synchronous reads; final source has none. S1/S2 were still absent from the fetched master. |

Task discovery was board-scoped: `GET /api/agent-tasks?boardId=8988ca03-7414-47ad-b0b6-51556c701703&since=2026-10-13T00:00:00Z`, then filtered locally by card. This returned no CARD-1199 Mutation task. `card.ps1 search '1199' -Board Antiphon -All` returned ten cards, with no separately titled Mutation completion. This supports retaining the recorded pending status; it is not a claim about evidence outside those records.

## Current helper boundaries

The card's name `CheckpointBoundedReader` is descriptive, not a current production type. The current native C# owner is `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:37`: a three-second deadline, regular-file/type/link/containment checks, size limit and bounded asynchronous reads. `tools/Antiphon.Checkpoints/Coverage/ConfinedFileReader.cs:5` is now a compatibility facade. `ReceiptFile.cs:21` uses that reader with a 64 MiB cap. PowerShell embeds its native boundary in `scripts/lib/checkpoint-file-reader.ps1:44` and exports `Read-CheckpointBytes` / `Read-CheckpointText` at lines 139 and 144.

`scripts/lib/CheckpointProcessRunner.cs:22` owns one cancellation deadline over Git configuration inspection, exit and both drains. Lines 56-65 harden Git; lines 81-85 create a Linux session; lines 98-110 join output with cancellation, terminate the group/job and bound the final direct-child wait to one additional second. This is not a byte-preserving Git API: `Result.Stdout` is a string (line 17), populated through `ReadToEndAsync` (line 98). The two inherited source-fingerprint readers currently return raw bytes. That semantic constraint belongs in the next Plan; a text round-trip cannot silently replace their bytes.

`tests/Antiphon.Tests/Checkpoints/CheckpointFixtureProcess.cs:20` calls the shared runner with `git: false` and throws `TimeoutException` on expiry. Its scratch-repository Git helper is at line 30. Fixture Git behavior deliberately differs from hardened read-only production Git.

## Six inherited readers: exact current list

| ID | Current expression | Mechanism and evidence |
| --- | --- | --- |
| R1 | `scripts/verify-checkpoint-temp-usage.ps1:22`, DLL `Get-FileHash` before the run | Opens and reads a repository-derived path without regular-file admission, byte cap or deadline. Inventory records an ordinary-file pass and FIFO hang beyond a five-second watchdog. `Test-Path -PathType Leaf` at line 21 is not the native regular-file boundary. |
| R2 | Same file, line 109, after-run DLL `Get-FileHash` | Same failure independently after child completion; inventory records the same ordinary/FIFO result. |
| R3 | `scripts/test-run-checkpoint.ps1:353`, `File.ReadAllBytes($script:Runner)` | Repository script ASCII check can block opening a substituted FIFO before producing any bytes; no deadline/cap. Inventory records five-second watchdog expiry with ordinary control. |
| R4 | Same file, line 356, `File.ReadAllBytes($harness)` | Same mechanism for the harness path; this is not merely known-created scratch content. Same stored FIFO/control evidence. |
| R5 | `tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:185`, especially lines 198-206 | Starts Git directly, copies stdout bytes and reads stderr asynchronously, limits direct-child exit to 30 s, then joins both drains without a token/deadline. If the parent exits but a descendant retains stderr/stdout, the joins never finish and the timeout kill is never reached. Inventory records held-pipe and installed-Git fsmonitor reproductions exceeding a 35 s watchdog. No Git hardening/group custody. |
| R6 | `scripts/lib/checkpoint-source.ps1:5`, especially lines 19-26 | Same raw-byte Git reader and 30 s exit-only limit; untimed drains after exit, no group custody/hardening. Same stored held-pipe and installed-Git evidence. |

The inventory's seventh row (`verify-checkpoint-temp-usage.ps1:17`) was explicitly added to the original **six** later; it is launch L1 below, not a seventh file/drain reader silently included in that count. All four files containing R1-R6 are byte-identical between `e0877982c5dc41990c4e66b80cb47341a1bd0a41` and this `origin/master`.

Reconstructed sequence for R5/R6: Git starts a configured helper; the helper forks a pipe-holding child; Git exits inside 30 seconds; `WaitForExit(30000)` succeeds; `copy/error.GetResult()` waits for EOF still owned by the descendant. Direct-child exit therefore defeats the only local timeout. This sequence matches the stored probes; no production incident frequency is inferred.

## Inherited production and verification launches

Bypassing `CheckpointProcessRunner` does not itself mean an infinite wait. The exact remaining sites have three distinct contracts: unbounded synchronous operations, execution with an existing row deadline, and intentionally persistent holders/executors.

| ID | Current site | Existing bound, hang/leak mechanism and scope disposition |
| --- | --- | --- |
| L1 | `scripts/verify-checkpoint-temp-usage.ps1:17`, `git rev-parse HEAD` | Call operator, no local deadline or hardening. A stuck Git/config read can block before usage sampling. Construction evidence only; inventory explicitly says not natively reproduced. |
| L2 | `scripts/verify-checkpoint-orphan-usage.ps1:6`, same Git request | Same. No later routing change. |
| L3 | `verify-checkpoint-orphan-usage.ps1:83-107`, owner pwsh launch | Three-minute readiness poll; timeout then `Kill(true)` plus untimed `WaitForExit` and stderr `GetResult` at 99-100. Successful readiness also uses untimed kill/join at 106-107. A pipe-holding orphan can defeat the error drain; waiting for readiness does not establish whole-operation custody. |
| L4 | Same file, lines 117-120 and 237-244, protected `dotnet hold` child | Intentionally stays alive during the sweep, no redirected output. Final tree kill is followed by untimed `WaitForExit`; cleanup assumes the kill/join succeeds. No held-pipe defect at this launch, and replacing it with a run-to-completion call would alter the fixture contract. |
| L5 | Same file, lines 161-182, native sweeper | 30 s exit wait, then kill and untimed wait; error branch joins both drains without limit. On a normal successful exit the script does not join/dispose those drains/process before continuing. Reparented descendants can outlive the wrapper; error output can hang. CARD-1264 changed results placement, not this lifetime. |
| L6 | `tools/Antiphon.Checkpoints/Baseline/BaselineComparer.cs:45,54,79,156`, fetch/rev-parse/worktree add/remove | All use `ProcessDriver`, not the shared reader runner. `RunLeased` at 235-257 applies remaining time through `RowTimeout`; remove has a fresh one-minute deadline at 157. They are not deadline-less. Git config/hooks remain enabled; on Linux, an exited parent is no longer tree-killable, so a pipe-holding descendant can survive after cancellation abandons drains. Fetch/add/remove are mutating Git operations, unlike the admitted read-only Git preflights. |
| L7 | `Baseline/GitWorktree.cs:7,10` | Request factories for add/remove, not separate process starts. Same driver contract when executed; do not double-count them as additional children. |
| L8 | `BaselineComparer.cs:95,128`; `Execution/RowRunner.cs:153,210,265,324`; `Execution/RunScheduler.cs:423` | Baseline build/run, row command/build/run/rerun, scheduler build. Already use `RowTimeout.RunWithDeadlineAsync` (`RowRunner.cs:151,208,263,322`, `RunScheduler.cs:421`). `ProcessDriver.cs:136` waits with that token; cancellation kills and allows a further 10 s drain at 145-155. The Linux handle's kill is conditional on the direct parent still being alive (`ProcessDriver.cs:39-45`), so an exited-parent descendant can remain. The implementation also supplies streaming callbacks, logs and build confinement; these sites are execution drivers, not simple readers. No new dynamic proof in this investigation. |
| L9 | `Slots/LeaseHolder.cs:44-102` | Deliberately persistent `dotnet hold --parent`. Start-failure cleanup waits at most 2 s; disposal kills and joins with a 2 s token. Stdout/stderr are redirected but never consumed. The real `Program.Hold` at `Program.cs:91-103` emits no output and polls parent liveness every 500 ms. A noisy substituted child could block on a full pipe; a kill failure can be swallowed at disposal. No normal-path infinite wait/leak is established here, so do not label this an unbounded reader or invent a failing test for it. |

The baseline deadline is computed before slot acquisition (`BaselineComparer.cs:243-257`), so the wall-clock total can include slot waiting in addition to that previously computed remaining time. This is an observed qualification of the bound, not a new measured hang.

Existing exclusions still visible in the census:

| Site | Current evidence / ownership |
| --- | --- |
| `scripts/verify-checkpoint-temp-usage.ps1:81-101` | No deadline in `while (!HasExited)`; untimed waits/drains. This is the accepted legacy usage-loop exclusion and is already tracked by CARD-0830 (`2a30c44d-65ff-401b-be1d-e2725657f10b`, Backlog). That card records two stuck Windows reviews and discarded partial outcomes. Do not duplicate its whole usage/progress work under this card; R1/R2/L1 are separately in CARD-1233. |
| `scripts/run-checkpoint.ps1:686-720`, standalone `Invoke-Dotnet` | `WhenAny` of line reads has no cancellation/deadline; final exit wait is untimed and disposal does not kill. Accepted standalone execution-loop exclusion; enclosing row bounds are not a local guarantee. |
| `scripts/test-run-checkpoint.ps1:147-154`, `Invoke-Checkpoint` | Untimed exit and output joins; a blocked pwsh or inherited pipe hangs the harness. Inherited legacy harness, not repaired by the generic ScriptHarness work. |
| Same file, `Start-C578Runner` at 696/705; `Wait-C578Exit` 830-843; `Stop-C578Owned` 846-859 | Has observation/exit guards and timed kills, but final `.Out.Result` / `.Err.Result` at 858-859 remain untimed. Keep distinct from the wholly untimed first launcher. |
| `Evidence/GitSnapshot.cs:41-48`; `Evidence/HostSnapshot.cs:48-59` | Diagnostic Git/dotnet collectors read stdout synchronously before their 30/15 s exit wait; Git stderr is redirected and never read. Blocked read or stderr backpressure can prevent reaching the advertised wait. Still accepted diagnostic exclusions; not a claim of safety. |
| `Execution/DetachedLauncher.cs:22-59` | Returns an executor PID, Linux setsid or Windows `CreateProcessW`; no output read. Survival after starter exit is intentional. No evidence here justifies converting this to run-to-completion. |
| Kernel metadata, execution loaders, staging copies and known-created scratch files | Retain the inventory's explicit exclusions. The launch census is not an audit of every filesystem operation or every repository helper. |

## Remaining fixture children

All paths in the next two tables are under `tests/Antiphon.Tests/Checkpoints/`. Registration is not immediate bounded cleanup: `CheckpointTestScope.cs:95-105` stores the exact direct-child identity; teardown at 124-151 has a shared ten-second cleanup token and kills only an observed still-live registered process tree. A parent that already exited cannot establish custody of a reparented child. Several waits below prevent reaching teardown at all.

### CARD-1199 residuals, plus the later analogous addition

| ID | File:line / method | Current behavior and remaining gap |
| --- | --- | --- |
| F1 | `CheckpointOutputConfinementTests.cs:200-224`, `RunScriptAsync` | Token on exit and both drains; registered child. Cancellation exits the helper without killing there; teardown owns only the surviving direct tree. |
| F2 | `CheckpointShapeConflictTests.cs:482-490`, `WaitAsync` | Same token-only contract. |
| F3 | `CheckpointTrackedBaselineTests.cs:587-595`, `WaitAsync` | Same token-only contract. |
| F4 | `CheckpointStampSafetyTests.cs:25-43` | Four-second exit token; on cancellation kills, then untimed `WaitForExitAsync` and drains. A descendant retaining the pipe can hang the claimed hard-bound failure path. Entire shape method is Linux-only. |
| F5 | `CheckpointReceiptSafetyTests.cs:231-251`, `Child` | Same four-second token followed by untimed kill-join and output drains. |
| F6 | `CheckpointBoundedReaderTests.cs:23-33` | Node supervisor launched directly; no own timeout/token, no `[Timeout]`. Node's internal pwsh watchdog at `scripts/test-checkpoint-bounded-readers.mjs:28-31` has 4/20/35/125 s bounds, but a stuck supervisor itself is unbounded at this layer. |
| F7 | `SafeDeleteProcessBoundaryTests.cs:119-141`, `Run` | 10 s exit, up to 2 s kill wait or drain assertion. Already finite locally, but only tree kill; no group custody. If parent exited and drain assertion fails, no descendant cleanup is performed here. |
| F8 | `CheckpointOwnedOutputTests.cs:61-68`, `InitRepo` | `git init`, no redirected output, 30 s exit assertion. Expiry does not kill until registered-child teardown. |
| F9 | `CheckpointOutputConfinementTests.cs:273-301`, `RunConfinedScriptAsync` | Added by CARD-1310 commit `a14d8214fd`; same direct-start/token-only pattern as F1. Thus later work added one analogous fixture bypass rather than removing F1. |

The original helper's eight repaired routes remain routed: `OutputCleanupTests.cs:1602` (Git), `:658` (parent-link pwsh), `:1006` (wrapper pwsh); `CheckpointTaskOwnershipTests.cs:751` (git init); `CheckpointOutputConfinementTests.cs:505` (Git) and `:468` (SeedBuild); `CheckpointShapeConflictTests.cs:500` and `CheckpointTrackedBaselineTests.cs:612` (GitOut/Git). They are PC-11 caller-routing subjects, not unfinished routing repairs.

Script children in the original inventory: `scripts/test-checkpoint-stamp.ps1:10-15,54` still invokes Git directly inside the supervised pwsh; `scripts/test-checkpoint-bounded-readers.mjs:11,13` runs `mkfifo` synchronously before installing its child watchdog, and `:24` starts its own explicitly supervised pwsh. These are nested harness operations, not additional production Git readers. The `.ps1` bounded-reader probe runs inside that supervisor. A census hit inside fixture source text is not an independent live launch.

### Other inherited fixture launch sites: exact residual census

These were explicitly not re-audited in repair 13k. They are enumerated here so Plan can distinguish CARD-1199 residuals from a larger fixture migration; none has been run during this investigation.

| ID | File:line | Existing bound / possible hang or leak |
| --- | --- | --- |
| I1 | `CheckpointRepeatBuildIsolationTests.cs:44-48` | Caller token (test timeout 300 s), no local kill or registration. Cancellation can leave pwsh/build children. |
| I2 | `CheckpointRepeatFixture.cs:103-109` | Token-bound wait/drains, no local kill or registration. Same leak possibility. |
| I3 | `CheckpointRepeatHostTests.cs:93-98` | Token-bound wait/drains, registered, cleanup deferred until teardown. |
| I4 | `RerunFilterHostTests.cs:79-84` | Awaits stdout, then stderr, then exit without any deadline. Can hang on blocked child, inherited pipe or stderr backpressure while stdout is awaited. Registration cannot rescue a test that never reaches teardown. |
| I5 | `CheckpointTempLifecycleTests.cs:77-82` | Registered; 45 s outer wait covers `WaitForExitAsync`, but independent untimed stdout/stderr tasks are joined afterward. No immediate kill on expiry. |
| I6 | `CheckpointTempUsageTests.cs:26-35`, inspection pwsh | Sequential untimed drains occur before the 30 s exit bound. Same pre-wait hang mechanism as I4. |
| I7 | Same file, `:346-351`, lifecycle host | Same 45 s wait / independent output joins / deferred kill as I5. |
| I8 | Same file, `UsageLibrary.InvokeAsync` at `:398-408` | Registered; 60 s outer exit wait and independent untimed output tasks, no immediate kill on expiry. |
| I9 | `CheckpointSourceExecutionTests.cs:568-590` | 30 s exit token; stdout/stderr created without token and awaited after exit; no kill/registration. Timeout may leave child; exited-parent pipes can hang the output joins. |
| I10 | `CheckpointSourceStateTests.cs:321-339`, fixture `Repo.Run` (also used by other fixture classes) | 30 s synchronous exit wait and kill on timeout, then untimed async output joins. Same exited-parent drain gap as R5. |
| I11 | `PlanCoverageHandleTests.cs:56-71` | Registered; 15 s exit token, but untimed post-kill exit and drains. Same F4/F5 failure-path gap. |
| I12 | `DirectoryLinkHelper.cs:26-37`, Windows cmd/mklink fallback | Synchronous stdout and stderr reads followed by untimed exit wait. No local bound or registration. Native link creation usually returns quickly; that is not a deadline. |
| I13 | `TimeoutTests.cs:215-233`, `PidsWithMarker` | Windows PowerShell stdout read precedes the 20 s wait; redirected stderr is never drained. Can hang before the wait, including while trying to discover cleanup PIDs. |
| I14 | `TimeoutTests.cs:280-303`, `CompileEchoExe` | 60 s exit wait/kill, then untimed stdout/stderr joins; no group/job ownership in this helper. |
| I15 | `DetachedLauncherTests.cs:26-59,62+` | Intentional detach witness: 15 s PID-read watchdog, 10 s marker poll and explicit cleanup. Reader task can remain pending when its watchdog fires; this is a special launcher-lifetime test, not a normal output-capture helper. |
| I16 | `CheckpointRecoveryLinuxTests.cs:51-70,252-266` | Intentional detached starter and `sleep 30`; PID read bounded 10 s, explicit stop bounded 5 s, registration. Reparented descendants are not proven by stopping only the recorded processes. No new hang established. |
| I17 | `CheckpointRecoveryWindowsTests.cs:345-360` | Intentional `cmd /c ping` holder, registered; explicit kill and 5 s join. No output reader, no new infinite wait established. |
| I18 | `OutputCleanupTests.cs:327-329`, later CARD-1242 real build-exit witness | Direct `ProcessDriver.RunAsync` of `sh` with `CancellationToken.None`; this method has no `[Timeout]`. Unlike L8's normal row callers, it supplies no local deadline. Its ordinary shell writes small files and exits, but a stalled child or held pipe can wait indefinitely in the driver. This is another later fixture addition, not a repaired inherited caller. |

`BaselineComparerTests` and `CheckpointImportTests` census hits are fake request matchers/factory signatures, not extra native launches. `CheckpointTaskOwnershipTests` fake DriverRequest rows are likewise not additional processes. Actual row-driver witnesses in `TimeoutTests` use the driver being tested, so banning those request sites would remove the subject of the tests. I18 also deliberately exercises the real driver, but without a local deadline.

## What later work already changed

| Work | Landed/current effect | Effect on CARD-1233 remaining scope |
| --- | --- | --- |
| CARD-1199 repairs 13i/13j/13k | Current routed production calls are `BuildConfinement.cs:580,761`, `scripts/run-checkpoint.ps1:127,771`, and SafeDelete through the native Git adapter. Project XML preflight is `run-checkpoint.ps1:321`; source/stamp/receipt content already uses bounded file reads. | Already done before this follow-up. Do not recode these as missing. Eight fixture routes listed above are also already done. |
| CARD-0987 extraction (`04edbada8fa2dce44b27faddf62c9f034d805575`) | SafeDelete and verified file reader moved into `src/Antiphon.HostCleanup/Checkpoints/`. `SafeDelete.cs:358` calls `context.Git`; `CheckpointCleanupDefaults.cs:16-24` still routes native Git to `CheckpointProcessRunner`. | Relocation/composition, not a bypass regression or repair of R1-R6. Plans must use current ownership paths. |
| CARD-1241 | `CheckpointConfinementLedgerTests.cs:515`, Git setup uses `CheckpointFixtureProcess` (origin `96912c773c`). | A later fixture already compliant. No old remaining reader moved. |
| CARD-1254 | `CheckpointShadowCustodyTests.cs:132-140`, tool wait child uses helper with 15 s limit (origin `87cd241754`). | Later fixture already compliant. Old Stamp/Receipt/BoundedReader fixtures unchanged. |
| CARD-1242 | `OutputCleanupTests.cs:327-329` observes a real shell exit with `ProcessDriver` and `CancellationToken.None`. | Adds I18's unbounded fixture wait; it does not remove any inherited reader gap. |
| CARD-1264 / CARD-1272 | Lifecycle, filter and repeat hosts now put reports in their owned sandbox (`CheckpointTempLifecycleTests.cs:66`, `CheckpointTempUsageTests.cs:330`, `verify-checkpoint-orphan-usage.ps1:166`, `RerunFilterHostTests.cs:74`, `CheckpointRepeatHostTests.cs:83`). | Output placement was fixed; direct starts/waits remain I3-I8/L5. |
| CARD-1310 | `BuildUseAppHostShim` uses helper at `CheckpointOutputConfinementTests.cs:266`; Windows seal helper uses bounded cmd at `OutputCleanupTests.cs:1479`. Added F9 as well. | Some new children are already routed, but no inherited bypass was removed. Windows output seal now has native evidence. |
| CARD-1318 | Owner/DACL/change-time/read-retry work and native Windows evidence; more setup children already use helper (`OutputCleanupTests.cs:1565`). | Partial R-2 evidence, not global Windows file-safety or process-runner qualification. |
| CARD-1231 / CARD-1298 / CARD-1346 | Baseline checkout naming, embedded fixture census text, and vanished `/proc` child observation respectively. | These changes do not route R1-R6 or F1-F8. Registration now tolerates a child disappearing during identity capture (`CheckpointTestScope.cs:102`); it still does not own descendants of an already dead parent. |
| CARD-1342 (`2273f108b`) | `scripts/lib/checkpoint-usage.ps1:666` derives namespace census from source. | Closes the census portion of the older CARD-0830 description, not its child wait or any CARD-1233 reader. |
| CARD-1362 reviewed S1/S2 | Changed files at the fetched reviewed tip are server runtime/queue/watchdog/settings and Application tests/helpers/plan. `RunnerReadSourcePinTests.cs:11-18` names three queue-service source files. | Zero checkpoint helper/reader/fixture overlap. It is a detector precedent only. Its reviewed publication status must not be mistaken for master containment. |

The literal six reader expressions, shared process runner and F4-F6 files have no post-CARD-1199 repair. `git diff e0877982c..origin/master` over the principal named gaps shows only the orphan results-directory addition, unrelated tracked-baseline fixture text, and additions to OutputConfinement; it shows no inherited routing migration.

## PC-11: what it detects and what it misses

The authoritative PC-11 definition is `docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md:177-181` (**plan** below). It names four variant families:

1. Restore synchronous drains before exit waiting in the fixture helper.
2. Drop process-group kill so a descendant survives.
3. Split exit/drain budgets instead of one deadline.
4. Route one listed fixture caller back to direct `Process.Start`.

The two current methods are `CheckpointFixtureProcessTests.fixture_git_blocked_opening_its_config_fails_within_the_limit` (line 18) and `fixture_child_whose_descendant_holds_a_pipe_fails_within_the_limit_and_is_killed` (line 57). Each calls `CheckpointFixtureProcess.Run` directly (lines 34 and 76), uses a 2 s limit, has an 8 s outer watchdog, asserts completion below 4 s, and the second asserts descendant death. The plan preserves tests-first red evidence: both still blocked after 8 s at `fca97365d82683a95871a38b0f245d87131f37f4`; subsequent narrow green was 2/2 at `df378957da906d5f21739ed4961cde953a21c98b` with 2.18/2.03 s elapsed. Those are stored outcomes, not this task's runs.

Variant 4 does not execute either changed caller: restoring direct start in, for example, `OutputCleanupTests.Git` leaves the two PC-11 test call graphs unchanged. That is the confirmed detector gap. Variants 1-3 are behavioral obligations but are still marked pending Mutation; ordinary helper green is not evidence that every proposed mutant is detected. In particular, an immediate-parent-exit witness alone does not establish sensitivity to every split-budget mutation.

**Source-pin assessment:** a source pin is suitable evidence for the narrower wiring invariant. CARD-1362's actual pin checks source-file existence, scans lines for retired synchronous APIs or blocking runner waits, and asserts an empty offender list (`df869f56:tests/Antiphon.Tests/Application/RunnerReadSourcePinTests.cs:21-48`). Its recorded 37-offender red shows it is not a stub. It is limited to named files and lexical patterns; it does not demonstrate process behavior. A checkpoint source pin could detect a caller returning to a direct launch, but it cannot by itself satisfy CARD-1233's stated blocked-read/held-pipe dynamic evidence, prove child death, or qualify Windows. Counting only helper calls is also not proof that the caller executes them. A blanket ban on every `Process.Start` would incorrectly include the driver under test and intentional detached/holder fixtures above. The exact detector and executable method selections remain Plan/TestDesign work.

PC-11 is a Mutation identifier. CP-11 is the older receipt/source regression checkpoint with a planned Windows sharing skip. They are different rows; the CP-11 skip is not evidence about the PC-11 routing detector.

## Windows qualification: remaining obligation

R-2 started with junction behavior (`plan:16`), then acquired the held-file tool-copy cleanup witness (`plan:17`), and the shared runner's Windows custody/pre-assignment race (`plan:156,173`; inventory's reader and fixture tables). It requires native Windows evidence, not a Linux pass or a skipped native test.

Already available evidence reduces the work:

- CARD-1310's native seal method now covers overwritten build output, late writes, hardlinks and a junction without deleting its target (`OutputCleanupTests.cs:1219-1290`). Review f82bf074 ran it successfully and validated real tool cleanup. The blanket historical statement that Windows adopts no outputs is obsolete.
- CARD-1318's native reviews cover owner-only roots/DACLs, actual change time, copied old timestamps, sharing retry and tool output removal. Their exact source/selection provenance above must be retained; they are not relabeled as a fresh CARD-1199 battery.

Still unqualified in the evidence inspected:

| Obligation | Evidence of the remaining gap |
| --- | --- |
| Shared runner job custody, including a child spawned before job assignment, parent exit with held pipes, timeout/cancellation and no survivor | `CheckpointProcessRunner.cs:89` starts before `:94-96` creates/assigns the job. Termination at 107 can cover only assigned members; a pre-assignment child is not proved contained. The separate `WindowsJobProcessHandle.cs:9-13,55-65,140-158` explicitly documents best-effort assignment and is a different implementation. PC-11's tests skip Windows at lines 20/59; `CheckpointBoundedReaderTests.cs:25` also skips it. |
| Tracked-stamp parent junction and remaining Windows reader/receipt shapes | `OutputCleanupTests.parent_symlink_does_not_delete_a_tracked_stamp` explicitly skips Windows at 631. `CheckpointOwnedOutputTests.cs:80` retains its Windows link skip; `CheckpointStampSafetyTests.cs:27` skips all shape cases there. C# and embedded PowerShell Windows reader branches use native attributes/handle checks (`CheckpointFileReader.cs:51-68`; `checkpoint-file-reader.ps1:89-108`), which Linux FIFO evidence does not execute. CARD-1310's output junction is a different entry point. |
| Tool-copy held-file deletion retry | `EvidenceFolderTests.tool_copy_removal_retries_while_a_file_is_still_held_open`, lines 11-40, is Windows-only and expects exactly two native delete attempts. CARD-1318's similarly named sharing-read retry is a different operation. No completion of this specific pending R-2 witness was found in the inspected reports. |
| Mutation PC-1..PC-11 and variants | Last CARD-1199 Review and current plan explicitly leave them pending. Native ordinary seal evidence does not close mutation detection. |

Windows qualification is therefore still needed, with the already qualified seal/adoption cases credited at their actual SHAs. This Linux worktree cannot supply the native portion, and the brief explicitly prohibited running tests. Any later Windows result not present in the inspected records could reduce this residual list; provide its exact task/report, native host, tested SHA and method identities to resolve that uncertainty.

## Scope groups for the next Plan

These are evidence/scope groupings and rough 30-60 minute Code work envelopes, not an implementation design or an executable checkpoint manifest. Each commissioned Code piece needs one actual defect-red and one narrow post-change green for its named behavior; no such future result is claimed here. The red/green columns identify the missing observations, not prescribed test implementations. Slot waiting or a failed build can exceed an envelope and cannot count as the red. Do not combine the entire census into one Code dispatch.

| Group | Scope and estimate | One red observation required | One narrow green observation required |
| --- | --- | --- | --- |
| G1 | R1-R4, repository file hashes/ASCII reads, 30-45 min | FIFO/nonregular path reaches a real caller and exceeds its stated bound (historical controls already identify the defect). | Same caller cases return/refuse within bound; ordinary DLL digest and ASCII outcomes unchanged. |
| G2 | R5, tool source Git reader, 45-60 min | Exited Git parent leaves a pipe-holding child and source capture does not finish. | Same capture completes/refuses within bound, descendant is gone, byte-sensitive fingerprint controls agree. |
| G3 | R6, PowerShell source Git reader, 45-60 min | Same held-pipe failure through the PowerShell source entry point. | Same entry point bounded, child gone, raw-byte source identity preserved. |
| G4 | L1/L2, verification Git reads, 30-45 min | Stuck Git prevents each verifier reaching its initial source identity. | Narrow source-identity controls finish/refuse within bound and preserve valid SHA behavior. |
| G5 | L3-L5, orphan verification children, 45-60 min | Owned blocked/pipe-holding child defeats current owner or sweeper completion/cleanup. | Owner/sweeper cleanup is bounded; protected live-child lifetime and existing orphan acceptance meaning preserved. |
| G6 | L6/L7, baseline Git requests, 45-60 min | Real timed-out/parent-exited baseline Git leaves a descendant or exceeds the admitted drain bound. | Same narrow baseline request returns bounded failure with no child; valid fetch/add/remove behavior preserved. |
| G7 | L8/I18, execution-driver custody and its later unbounded fixture caller, 45-60 min | Exited-parent descendant survives row/build cancellation, or the real-driver fixture never reaches its assertion. | Same exact row/driver witness establishes deadline, cleanup, output/confinement behavior. Existing token tests alone are insufficient. |
| G8 | F1-F3/F9, token-only fixture callers, 45-60 min | Cancelled caller leaves its child/descendant alive. | Same narrow callers terminate owned work and retain ordinary fixture output. |
| G9 | F4/F5, Stamp/Receipt failure paths, 30-45 min | Four-second failure path is itself held by inherited pipes. | Same shape/receipt callers produce their expected result within their stated bounds, with no surviving child. |
| G10 | F6-F8 and nested stamp/supervisor operations, 45-60 min | Stalled supervisor, surviving fixture descendant, or expired init remains owned after failure. | Same narrow harness/fixture failure paths finish with ordinary controls and child death. |
| G11 | PC-11 caller-routing coverage, 30-45 min | Reverting one of the eight existing routes is actually detected; current helper-only tests cannot do this. | Same exact detector passes with that route restored; behavioral helper witnesses retain their separate provenance. |
| G12 | I1-I4, repeat/filter fixtures, 45-60 min if included | Cancellation leak or stdout-before-stderr hang at these real callers. | Corresponding narrow fixture caller outcomes with bounded completion and no leftover child. |
| G13 | I5-I8, lifecycle/usage fixture children, 45-60 min if included | Output/exit gap or deferred-cleanup leak at the named helpers. | Same narrow helpers complete within bound while keeping reports in owned sandboxes. |
| G14 | I9-I11, source/coverage fixtures, 45-60 min if included | Untimed drain or kill-join fails at the real caller. | Same caller bounded, existing receipt/source/coverage assertions preserved. |
| G15 | I12-I14, Windows mklink/discovery/compiler fixture helpers, 45-60 min on Windows if included | Real pre-wait or post-exit pipe hang exceeds the local budget. | Same exact native helper behavior finishes/refuses within bound and cleans owned children. |
| G16 | Residual Windows file/receipt R-2, 45-60 min native qualification | Existing safety assertion with its required negative control; a platform skip is not red. | Exact remaining native junction/held-file/reader cases pass with source-bound receipts. No production repair is presumed. |
| G17 | Windows shared-runner R-2, 45-60 min native qualification | Required pre-assignment/held-pipe custody control distinguishes missing custody; whether a repair is needed is still unmeasured. | Exact native timeout/cancellation/custody witnesses pass with no surviving descendant. |

G1-G11 cover the confirmed reader/fixture-routing follow-up plus the named inherited launch custody. G12-G15 expose the wider inherited fixture census rather than hiding it inside G8-G10; their inclusion is a Plan scope choice. G16-G17 are qualification work, with any newly reproduced production repair commissioned separately. L9 and I15-I17 are intentional lifetime contracts: no defect-red is established for their normal path, so automatic migration is not justified. CARD-0830 retains the full usage-loop/progress work. Legacy standalone and diagnostic exclusions remain explicit above; expanding them requires a named scope decision, not a universal boundedness claim.

## Remaining uncertainties and limits

- No new timing, survivor measurement, Windows run or mutation was performed. Stored probes confirm R1-R6 mechanisms; other launch/fixture hazards are source reconstructions until their real-caller witnesses run.
- Original generated scratch reports for 9f9613bd/dca33d6b are unavailable locally. The database task Results and committed inventory preserve outcomes, but this investigation did not revalidate their raw logs or TRX files.
- The census matches explicit launch forms in the historical paths, expanded to the extracted HostCleanup owner. It cannot prove absence of a launch hidden behind another helper outside those paths. Known-created file reads and metadata/staging exclusions were not reclassified as admitted readers.
- Shared runner cancellation has a further bounded direct-child reap wait (up to 1 s); `ProcessDriver` has a different 10 s drain. A caller's stated acceptance bound must account for the actual contract. Neither cancellation alone nor process disposal proves descendant death.
- PC-11 routing is confirmed missing; kill/split-budget mutant sensitivity remains unexecuted, even where an ordinary helper control exists.
- The reviewed CARD-1362 branch is separate from master. Re-fetch at Plan/Code dispatch to account for intervening lands, without assuming a similarly named feature covers these paths.

## Repeating the source census without running tests

At the pinned source, the principal search used here was:

```sh
rg -n 'Process\.Start\(|Process\]::Start|ProcessStartInfo|new Process( |\(|\{|$)|Start-Process|DriverRequest\(|new\("(git|dotnet)"|spawn(Sync)?\(|execFile|execSync|& *(git|dotnet|pwsh|node|setsid)( |$)|FileName *=' \
  tools/Antiphon.Checkpoints scripts/*checkpoint*.ps1 scripts/*checkpoint*.mjs \
  scripts/lib/*checkpoint*.ps1 scripts/lib/CheckpointProcessRunner.cs \
  tests/Antiphon.Tests/Checkpoints -g '*.cs' -g '*.ps1' -g '*.mjs'
rg -n 'Process\.Start|new Process|ProcessStartInfo|FileName =' src/Antiphon.HostCleanup -g '*.cs'
rg -n 'CheckpointFixtureProcess' tests/Antiphon.Tests/Checkpoints -g '*.cs'
```

Classify hits by their containing method, not by the mere presence of `ProcessStartInfo`: shared-helper setup and fake request factories also match. Read caller tokens, post-timeout joins and cleanup before assigning a bound. For provenance, use `git blame -L <line>,<line> -- <path>` and compare with the CARD-1199 landed SHA. For CARD-1362, `git diff --name-only 6ec71d427574f3a1b74bfbe2042e0ea9a819d191...df869f5651bc1400a8e27ef0b85f89db6300fc3c` establishes the disjoint changed-file set.

## Not done, noted

Plan the remaining bounded-reader, caller-routing and child-custody work against the current helper owners while preserving raw-byte Git identity, execution/holder semantics and the explicitly scoped native Windows evidence obligations.

--- next stage ---
next: plan
handoff: Plan CARD-1233 from the confirmed six reader gaps and exact launch/fixture census; CARD-1362 has no overlap. Preserve byte-valued source Git reads and existing execution/holder contracts, close PC-11 caller-routing detection, credit CARD-1310/1318 native seal evidence, and retain residual Windows R-2 plus pending Mutation.
artifact: docs/investigations/2026-10-20-card-1233-bounded-helper-remaining-scope.md
