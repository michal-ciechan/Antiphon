# CARD-1047: finish Windows ScriptHarness ownership qualification

Date: 2026-10-04. Plan and folded TestDesign. Inspected base:
`19cd9131393fa104ad2f39879b20018a08400ef8`. Ready for bounded **Code** slices.

Implement the missing W1-W10 from the
[CARD-0806 plan](2026-09-29-card-0806-script-harness-timeout-plan.md), repair the
Windows-only fixture/adapter defects that prevent those tests running, and obtain
native Windows receipts. CARD-0806's Linux implementation is already landed
(`1e31612b2`); its Linux qualification and outstanding mutation obligations are
not recommissioned here. This document supersedes only its Windows execution
selection, stale caller count and Windows test-design details specified below.

The card was read with `card.ps1 get CARD-1047 -Board Antiphon`. No builds, native
tests or positive controls were run during Plan. Static findings below are not
Windows runtime evidence. The plan is delivered on the assigned task branch;
the caller lands the settled Plan task before dispatching S1 from master.

## Ground truth

| Card/old-plan assumption | Code at the inspected base | Design consequence |
|---|---|---|
| Windows ownership tests are missing. | `tests/Antiphon.Tests/Scripts/ScriptHarnessWindowsOwnershipTests.cs` has Integration and ProcessSpawnLimit attributes but **zero** Test methods. | Author all ten named W methods; neither class existence nor Linux receipts closes this card. |
| The Windows adapter needs implementing. | `WindowsScriptHarnessProcess.cs` already creates a suspended root, assigns a private kill-on-close job, supplies a handle list, terminates the job and queries active processes. | Keep this adapter and its IOwnedScriptProcess contract; introduce narrow per-instance native observation/fault seams. |
| The existing Windows pipes are ready for native proof. | CreatePipe handles feed FileStream with `isAsync: true`; the landed nightly adapter uses synchronous handles with independent readers. | Correct the handle-mode mismatch and prove actual output/EOF. Anonymous pipes do not support overlapped I/O ([Microsoft contract](https://learn.microsoft.com/en-us/windows/win32/ipc/anonymous-pipe-operations)). No claim about the precise current exception without a Windows reproduction. |
| The portable child is Windows-ready. | Both fixture child modes unconditionally call CloseOwnOutputWriters, which reads `/proc/self/fd` and calls libc readlink/close. The Linux owner itself is OS-gated. | Add a Windows-only output-close branch; leave the Linux supervisor/protocol unchanged. |
| Common native tests already provide full independent Windows observations. | There are 11 N methods, but ProcessIdentity reopens by PID/start time, Win32Exception becomes false, emergency cleanup does not join, and no explicit-stop trace distinguishes termination from job disposal. | Retain Windows observation handles, make observation errors failures, bound emergency joins, and record stop-before-dispose and separate EOF observations. |
| CARD-1039 can be reused as the entire owner. | Landed S1n-a (`56772d0`) has NativeProcessOwner, native observation/barrier patterns and an independently compiled job-limit observer. It returns a nightly result/log contract and its fixture has unbounded release loops. | Reuse the suspended-launch, synchronous-reader and independent-handle/query patterns. Do not transplant its orchestration, waits, timeout policy or nightly result model. |
| Original Windows CP-6 means 24+8 caller results. | RunCheckpointScriptTests has 24 methods. BuildSlotScriptTests now has 19: 18 Windows-applicable and one Linux-only SIGINT method. | Split compatibility into 24 and 18 results, explicitly exclude the SIGINT method by positive selection. No accepted skips. |
| A whole Unit run is needed. | The original finalized contract roster is 26 harness methods plus 3 ProcessSpawnLimit, 1 TestLaneCategoryGuard and 2 SlowTestTripwire methods. | Run those four classes only: 32 results. |
| Platform can be inferred from the card. | The card currently says Any. Read-only GET `/api/runner-defaults` returned revision 2 with no kind overrides; GET `/api/session-runners` returned an eligible Windows lane and an eligible Linux lane, plus an unavailable descriptor. | Every execution row in this plan is **Windows-only**. Dispatch with `-Platform Windows`, omit `-Runner`, and resolve eligible capacity from the live catalogue. Do not embed a fleet location. |

## Decisions

**D-1 — Keep the existing boundary.** Changes stay in the test-local Windows
adapter, its test fixtures/helper and the testing owner documentation. Ordinary
ScriptHarness defaults remain execution 300 seconds and cleanup 10 seconds;
native cases keep execution 5 seconds, cleanup 2 seconds and observed completion
within 10 seconds. Reject a production custody refactor, wholesale replacement
by NightlyNativeOwnership, new package or additional helper project: each enlarges
the change without supplying the missing ScriptHarness assertions.

**D-2 — Small native seams, actual OS observations.** Add an optional internal
instance-scoped hooks/native-operations object to WindowsScriptHarnessProcess;
the ordinary constructor uses real operations. Cover create flags/handles,
before assignment, before resume, assignment/resume failure, termination and job
accounting. Record calls before forwarding, including explicit termination before
disposal. Pass executable classification through a narrow per-instance file
observation seam for W9. No static mutable hooks or environment switches.

Keep sequencing in the actual adapter. Query IsProcessInJob, handle flags, job
limits/accounting and retained process handles independently in the fixture;
do not assert only a hook's claimed success. W1 records actual CreateProcess flags,
job membership before resume, and the first successful ResumeThread's previous
suspend count of one. Setup failures must unwind a suspended root before handles
close. Track individual owned handles, not a noisy global process handle count.
Never replace the entire owner with a successful fake to prove native custody.

**D-3 — Reuse the landed patterns selectively.** Reference
`scripts/lib/nightly-owned-process.cs`, `scripts/test-nightly-native.ps1`,
`scripts/fixtures/nightly/owned-child/Program.cs` (NativeJobObserver) and
`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs`. Port the small
independent job-limit observation pattern into the new Windows test fixture;
do not make ScriptHarness depend on the nightly script assembly. In the adapter,
wrap CreatePipe handles as synchronous FileStreams; keep stdout/stderr pumping
concurrently. Correctly transfer/close handle ownership on reader-construction
failure. Await EOF under the existing deadline, never add an unbounded drain or
join. S1's common native tests must reach assertions, not fail at pipe setup.

In `Antiphon.ScriptHarnessHost/Program.cs`, put Windows behavior before the Linux
descriptor implementation. Close only the selected inherited standard writers
using Windows handles (avoid double-closing identical handles); avoid cached
Console writers retaining a duplicate. Start descendants with only intended
stdio inheritance. Prove stdout-only, stderr-only and neither-held variants with
real EOF while the children are still alive. Keep the existing Linux branch
byte-for-byte except the necessary dispatch around it. Do not paper over a
fixture failure by accepting early child exit.

**D-4 — Independent bounded fixture custody.** Add
`ScriptHarnessWindowsProcessFixture.cs` in the Scripts test directory. Capture
root/child/grandchild handles and start identity at readiness, before an early
root can exit; place observer records outside harness-owned results/control
directories. Use a release barrier after this observation for parent-exited
cases. Keep all barriers time-limited, release them in finally, and retain the
20-second child self-exit ceiling. The Race PowerShell loop also gets a ceiling.

All new W methods hard-fail if run off Windows or without a real executable/helper;
they never return early or skip. Classify Integration, Slow when required, and use
the assembly-local ProcessSpawnLimit. Each W/N invocation has a 30-second outer
watchdog (45 seconds for N11's three sequential invocations) independent of the
5+2-second harness budget. Finally has at most five seconds total to terminate
and join exact fixture-owned handles. Readiness failure, unknown process state,
watchdog rescue or residue before the emergency sweep is a failure. W10's injected
uncertainty is the sole W exception: require failure/retention, then record and
independently reap its known children. A later safety-net kill cannot erase it.

W5 assigns the still-suspended target to a fixture-owned outer job before the
adapter assigns its private nested job; a separately owned sentinel belongs to
the outer job only. Never assign the TUnit host itself to a new job. The fixture
owns and cleans the sentinel after observing that private-job cleanup spared it.
No process-name sweep, production runner or broker participates in these cases.

**D-5 — Preserve the old acceptance, fix its roster.** Keep all W1-W10 names and
all 11 common N methods. Strengthen Windows observations in existing N2/N3/N8
without adding or weakening outcomes. Final ordinary scope is **95 executions**:
32 contract/classification + 21 native + 24 checkpoint callers + 18 slot callers.
Original CP-4/CP-5 retain their numbers here; original CP-6 becomes CP-6 plus CP-7.
CP-1/CP-2 are bounded intermediate slice checks; CP-3 is intentionally unused so
the inherited Windows checkpoint references remain recognizable. The new table,
not the six-row CARD-0806 table, is the executable manifest for this card.

**D-6 — Budget and evidence are stop conditions.** Commission S1, S2 and S3
serially as separate 30-60-minute Code tasks, preserving the prior slice source
through normal landing or an explicitly recorded continuation base. They touch
the same files and must not run concurrently. Commit/push meaningful slices before
verification. If a native defect needs broader coordinator/Linux changes or the
slice cannot fit its budget, report the exact failing method, elapsed phase and
required follow-up; do not broaden filters or increase deadlines. An inherited
failure needs the same failing method at the recorded base, not a baseline suite.

## Implementation slices

| Slice | Files | Work, tests and handoff |
|---|---|---|
| S1: runnable Windows owner and launch refusal | `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs`; new `ScriptHarnessWindowsProcessFixture.cs`; `ScriptHarnessWindowsOwnershipTests.cs`; `ScriptHarnessProcessFixture.cs`; `ScriptHarnessProcessTests.cs`; `tests/Antiphon.ScriptHarnessHost/Program.cs`; `tests/Antiphon.Tests/Scripts/Fixtures/script-harness-process.ps1`; `tests/Antiphon.Tests/slow-tests-allowlist.txt` if needed | D-2/D-3/D-4 scaffolding, pipe-mode repair, Windows helper/observer branch, bounded barriers and cleanup. Add W1-W3; make N1-N11 runnable with truthful Windows observations. Commit, then CP-1. No new test project/copy target is needed: the helper is already staged. |
| S2: private-job and handle boundary | Windows adapter/fixture/ownership test files above | Add W4-W8, independent job flag/handle queries, outer-job sentinel and explicit writer-closure evidence. Add only the per-instance seams these tests require. Commit, then CP-2. |
| S3: refusal/accounting and final qualification | Same Windows files; `docs/testing-and-build.md`; this plan only if measured roster/cost correction is necessary | Add W9/W10 including query-error and active-member arms. Document Windows-only qualification and bounded fixture behavior. Commit all source first, then CP-4..CP-7 at one clean SHA. Complete separate ordinary Windows Review before final land; commission PCs separately. |

## Verification design

### Inspection

| Bodies read | Boundary and disposition |
|---|---|
| ScriptHarness.cs, ScriptHarnessProcess.cs, WindowsScriptHarnessProcess.cs | Real adapter, deadline/cleanup coordinator, executable resolution and PASS validation -> V-1..V-4, R-1..R-4. The coordinator remains unchanged. |
| ScriptHarnessProcessContractTests.cs, ScriptHarnessProcessTests.cs, ScriptHarnessProcessFixture.cs, empty Windows class; fixture ps1 and ScriptHarnessHost Program.cs | Current counts, PID-only observation weakness, Linux-only output close and missing Windows setup -> V-1..V-4. |
| Nightly native adapter, script, test class and compiled OwnedChild/NativeJobObserver | Reusable suspended launch, synchronous pipe readers, private-job queries and retained process handles -> V-1/V-2. These tests exercise another owner and earn no ScriptHarness credit. |
| BuildSlotScriptTests.cs, RunCheckpointScriptTests.cs; Antiphon.Tests.csproj | 19/24 current methods, Linux SIGINT exclusion, already-staged helpers -> V-5. |
| ProcessSpawnLimitTests, TestLaneCategoryGuardTests, SlowTestTripwireTests; testing/build and orchestration owners; stage-test-design bundle | Native serialization, classification and executable manifest -> V-4/V-5. |
| Checkpoints Program.cs, PlanTableImporter, CheckpointManifest, ManifestValidator and RunScheduler | Supported flags, sequential build reuse, explicit row/total deadlines and roster enforcement -> execution recipe. |

Missing setup is explicit: W1-W10, instance native seams, independent Windows
fixture observers, bounded Windows helper output handling and emergency joins.
S1-S3 supply it; no existing nightly test substitutes for the new tests.

### Delivery inventory

No application queue, session delivery, transcript or persistence path changes.
The invocation-local boundaries are create -> assign -> resume -> script readiness,
stdio -> reader EOF, and termination -> process/job observations. The join key is
the invocation nonce plus retained OS handles and process start identity. Fixture
readiness files are atomically published and nonce-checked; they are transient
observations, not durable messages. A write, termination return value or root exit
alone is never completion evidence.

W1-W3 hold/fail assignment and resume before recipient execution. N2/N3 prove a
root has exited while a descendant still holds exactly one stream. W8 proves both
EOFs with the parent owner alive. W10 proves an acknowledged stop with a live
member and a failed accounting query cannot become cleanup success. Finally and
the independent fixture ceiling recover only fixture-owned processes after a
broken assertion. There is no durable replay/queue to test; real-queue crash
recovery and UserPrompt evidence are outside this test-only boundary.

### Proves it works now

| ID | Tests / layer | Required result |
|---|---|---|
| V-1 | ScriptHarnessWindowsOwnershipTests W1-W3/W9 (native adapter) | Assignment precedes execution, failure never releases a root, alias refusal precedes CreateProcess; partial handles unwind. |
| V-2 | ScriptHarnessWindowsOwnershipTests W4-W8/W10 (native adapter) | Private and nested job limits, handle isolation, genuine pipe EOF, and observed empty accounting. |
| V-3 | ScriptHarnessProcessTests N1-N11 on Windows | Existing timeout/cancel/success/output/inventory/repeat outcomes, with ready live trees, retained handles, 5+2-second deadlines and no emergency-sweep residue. |
| V-4 | ScriptHarnessProcessContractTests (26 existing); ProcessSpawnLimitTests (3 existing); TestLaneCategoryGuardTests (1 existing); SlowTestTripwireTests (2 existing) | Preserve coordinator and PASS outcomes; new spawning tests retain correct categories, limiter and exact Slow reasons. |
| V-5 | RunCheckpointScriptTests (24 existing); the 18 positively selected BuildSlotScriptTests methods below | Existing wrappers keep exit/output/PASS inventory and argument behavior on Windows with 0 skips. Offline shims remain offline. |

W means `ScriptHarnessWindowsOwnershipTests` in all following tables. Every W row
is one nonparameterized Test result. Fault arms/loops are assertions, not extra
executions. N/U names and acceptance come from CARD-0806's final rosters, with D-4's
stronger Windows observations and these specific W contracts:

| Key | Exact W method | Decisive assertion before emergency cleanup |
|---|---|---|
| W1 | `Child_is_assigned_before_first_instruction` | Actual flags include suspension; root is in the private job at pre-resume barrier; successful Assign precedes first Resume and its previous suspend count is one. Record first-instruction marker only after release. |
| W2 | `Assignment_failure_never_resumes_child` | Inject Assign refusal after a real suspended create. Zero Resume attempts/start marker; retained root signals within unwind budget; all partial handles close. |
| W3 | `Resume_failure_terminates_suspended_child` | Inject resume failure after real assignment; preserve error, explicit termination precedes job close, root signals and owned handles close. |
| W4 | `Closing_private_job_kills_owned_tree` | Query real kill-on-close flag; with a ready tree call owner disposal without explicit TerminateAsync. Independently held root/child/grandchild handles signal within two seconds. |
| W5 | `Nested_job_timeout_kills_owned_descendants_only` | Timeout a ready nested tree; target handles signal, outer-only sentinel remains alive. Observe no CreateProcess breakaway and neither job breakaway flag. |
| W6 | `Private_job_handle_is_not_inherited` | GetHandleInformation shows private job non-inheritable; child probe cannot query/retain the transported job-handle value. Do not count an enclosing checkpoint job as the private job. |
| W7 | `Only_standard_handles_are_inherited` | Create an unrelated inheritable event. Actual startup list is exactly child stdin/stdout/stderr; parent read ends and event are absent, child cannot signal that event. |
| W8 | `Parent_closes_child_pipe_write_handles` | Child closes both output writers but stays alive; independently observed stdout and stderr EOF arrive with parent/owner alive. Both parent writer handles are closed. |
| W9 | `App_execution_alias_is_refused_before_launch` | Exercise WindowsApps-shaped path and reparse classification independently, with other checks valid. Actionable real-executable refusal, zero CreateProcess calls; real installed executable control reaches script output. No installed alias or privilege prerequisite. |
| W10 | `Job_accounting_must_confirm_no_active_processes` | Arm A: real ready member, intercepted termination ACK without killing. Arm B: real membership established, accounting query then fails. Both refuse success/delete, preserve diagnostic paths; explicit accounting evidence captured before job close. Finally independently kills/joins known handles. |

For V-3, N2/N3 additionally require opposite-stream EOF and selected-stream
non-EOF before timeout. N2 and N8 record explicit TerminateAsync before Dispose;
kill-on-close alone is insufficient. N5 compares the actual payload receipt before
owned-path deletion, not just a PASS line. Keep the wrapper's existing literal
argument, UTF-8 and inventory contract. These are outcome assertions on the real
Windows path; do not change Linux cleanup/protocol behavior to implement them.

### Guards the regression

| ID | Regression | Detecting evidence |
|---|---|---|
| R-1 | Root runs before ownership or after refused launch. | W1-W3/W9 native trace, membership, no-resume and retained-handle assertions. |
| R-2 | Timeout, root exit or successful output loses descendants. | W4/W5/W10 and N1/N2/N3/N4/N8/N11: explicit stop, private accounting and independent death before sweep. |
| R-3 | Inherited handles/writers prevent bounded EOF or retain custody. | W6-W8; N2/N3/N8/N10: genuine separate EOF and live-holder evidence, concurrent large output. |
| R-4 | Missing native setup or wider caller suite creates false green. | No skip/early return; exact 32/21/24/18 final roster, ready-tree assertions and strict source-bound receipts. |

### Guard inventory

One PC per independently bypassable behavior, without multiplied OS or retry
variants. All PC rows are Windows-only. These replace this card's Windows control
selection, not the original card's outstanding non-Windows controls. Existing
coordinator policy PCs remain with CARD-0806; the two Windows explicit-stop cases
below supply its Windows PC-13/PC-14 counterparts.

| Guard | Decision / protected behavior | Positive control |
|---|---|---|
| G-1 | D-2 suspended create | PC-1 |
| G-2 | D-2 assignment before resume | PC-2 |
| G-3 | D-2 failed assignment refuses execution | PC-3 |
| G-4 | D-2 failed resume explicitly terminates | PC-4 |
| G-5 | D-1 kill-on-close safety net | PC-5 |
| G-6 | D-4 no create breakaway | PC-6 |
| G-7 | D-4 no explicit job breakaway | PC-7 |
| G-8 | D-4 no silent job breakaway | PC-8 |
| G-9 | D-2 private job is non-inheritable | PC-9 |
| G-10 | D-2 stdio-only inheritance | PC-10 |
| G-11 | D-3 parent stdout writer closes | PC-11 |
| G-12 | D-3 parent stderr writer closes | PC-12 |
| G-13 | D-2 aliases/reparse launch shapes refuse | PC-13 |
| G-14 | D-1 active job members prevent death confirmation | PC-14 |
| G-15 | D-1 failed accounting is unknown, not empty | PC-15 |
| G-16 | D-3 native pipe handle mode matches its FileStream | PC-16 |
| G-17 | D-3 Windows child closes selected stdout writers | PC-17 |
| G-18 | D-3 Windows child closes selected stderr writers | PC-18 |
| G-19 | D-5 root exit does not bypass explicit stop | PC-19 |
| G-20 | D-5 successful output does not bypass explicit stop | PC-20 |

### Positive controls

Mutation targets are `WindowsScriptHarnessProcess.cs` unless stated. Every row
uses exactly one method, baseline/red/restored-green, Min=1. PC-16's method must
capture the adapter exception and fail a named successful-native-output assertion;
an unhandled fixture/build error is not acceptable red. Native safety interlocks
intercept an attempt to resume without successful assignment or request breakaway;
record the attempted bad action, fail its assertion, and never forward that unsafe
action. Normal baseline paths still use the real kernel operations. Do not mutate
the independent observer, finally sweep or child ceiling along with the guard.

| PC | Compiling change | Exact detecting filter | Required red assertion |
|---|---|---|---|
| PC-1 | Remove CREATE_SUSPENDED. | `/*/*/ScriptHarnessWindowsOwnershipTests/Child_is_assigned_before_first_instruction` | Recorded native create flags require suspension. |
| PC-2 | Move Resume before Assign. | `/*/*/ScriptHarnessWindowsOwnershipTests/Child_is_assigned_before_first_instruction` | First Resume attempt must follow successful Assign. |
| PC-3 | Continue to Resume after Assign failure. | `/*/*/ScriptHarnessWindowsOwnershipTests/Assignment_failure_never_resumes_child` | Resume attempts must equal zero. |
| PC-4 | Remove explicit job/root termination in resume-failure unwind; retain handle close. | `/*/*/ScriptHarnessWindowsOwnershipTests/Resume_failure_terminates_suspended_child` | Explicit termination must precede job close. |
| PC-5 | Clear KILL_ON_JOB_CLOSE. | `/*/*/ScriptHarnessWindowsOwnershipTests/Closing_private_job_kills_owned_tree` | Independent live job readback must contain kill-on-close. |
| PC-6 | Add CREATE_BREAKAWAY_FROM_JOB. | `/*/*/ScriptHarnessWindowsOwnershipTests/Nested_job_timeout_kills_owned_descendants_only` | Actual native create flags must forbid breakaway. |
| PC-7 | Add JOB_OBJECT_LIMIT_BREAKAWAY_OK. | `/*/*/ScriptHarnessWindowsOwnershipTests/Nested_job_timeout_kills_owned_descendants_only` | Independent job flags must forbid explicit breakaway. |
| PC-8 | Add JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK. | `/*/*/ScriptHarnessWindowsOwnershipTests/Nested_job_timeout_kills_owned_descendants_only` | Independent job flags must forbid silent breakaway. |
| PC-9 | Mark the private job handle inheritable. | `/*/*/ScriptHarnessWindowsOwnershipTests/Private_job_handle_is_not_inherited` | Actual job handle inheritance flag must be false. |
| PC-10 | Remove the handle-list restriction while retaining handle inheritance. | `/*/*/ScriptHarnessWindowsOwnershipTests/Only_standard_handles_are_inherited` | Child cannot signal the unrelated inheritable event. |
| PC-11 | Retain the parent's stdout writer until disposal. | `/*/*/ScriptHarnessWindowsOwnershipTests/Parent_closes_child_pipe_write_handles` | Stdout EOF must arrive with parent/owner alive. |
| PC-12 | Retain the parent's stderr writer until disposal. | `/*/*/ScriptHarnessWindowsOwnershipTests/Parent_closes_child_pipe_write_handles` | Stderr EOF must arrive independently with parent/owner alive. |
| PC-13 | Remove the alias/reparse refusal predicate. | `/*/*/ScriptHarnessWindowsOwnershipTests/App_execution_alias_is_refused_before_launch` | Refused shapes have zero CreateProcess attempts; intercept any attempted alias launch. |
| PC-14 | Return from ConfirmDeadAsync after stop ACK without accounting. | `/*/*/ScriptHarnessWindowsOwnershipTests/Job_accounting_must_confirm_no_active_processes` | Live-member arm cannot report clean cleanup or delete evidence. |
| PC-15 | Treat QueryInformationJobObject failure as zero active members. | `/*/*/ScriptHarnessWindowsOwnershipTests/Job_accounting_must_confirm_no_active_processes` | Query-failure arm must report death-confirmation failure and retain paths. |
| PC-16 | Revert synchronous pipe FileStream mode to isAsync=true. | `/*/*/ScriptHarnessWindowsOwnershipTests/Child_is_assigned_before_first_instruction` | Real-executable arm must produce output and complete without a native pipe exception. |
| PC-17 | In ScriptHarnessHost's Windows branch omit selected stdout closure. | `/*/*/ScriptHarnessProcessTests/Exited_root_with_stderr_holder_times_out_and_kills_descendant` | Opposite (stdout) stream must have EOF while held stderr descendant is alive. |
| PC-18 | In that Windows branch omit selected stderr closure. | `/*/*/ScriptHarnessProcessTests/Exited_root_with_stdout_holder_times_out_and_kills_descendant` | Opposite (stderr) stream must have EOF while held stdout descendant is alive. |
| PC-19 | In ScriptHarnessProcess.cs skip TerminateAsync when the root task completed. | `/*/*/ScriptHarnessProcessTests/Exited_root_with_stdout_holder_times_out_and_kills_descendant` | Explicit owner-stop receipt must precede Dispose after logical root exit. |
| PC-20 | In ScriptHarnessProcess.cs skip TerminateAsync when primary outcome is success. | `/*/*/ScriptHarnessProcessTests/Completed_root_with_silent_descendant_releases_owner` | Explicit owner-stop receipt must precede successful return and Dispose. |

Guards=20; mapped=20; missing=0; duplicate PC mappings=0. Controls are specified,
not executed. Code runs ordinary V/R; Review audits it and this design; post-land
Mutation runs the PCs in a separately commissioned Windows SourceLanding snapshot.
Use the owner-documented fixed local run-checkpoint driver, external evidence root
and restoration contract. Each phase records build exit, exact executed method,
assertion failure (red only), elapsed time and bounded cleanup. No whole-class PC,
whole-Unit run, skip, discovery-only or timeout-as-red credit. Restore exact bytes
and rebuild with fresh timestamps before green. No snapshot commit/push.

### Out of scope

Linux native execution/ownership changes, production session custody, nightly
launcher changes, MSIX installation, deliberate job/group escape, application
queues, database, browser, providers, full Unit/assembly testing and deployment.
The retained nightly code is a reference; its separate qualification is not rerun.
If shared coordinator behavior must change, stop and commission the additional
scope/verification instead of silently adding a Linux checkpoint to this plan.

### Execution and evidence

All CP rows below run on **Windows**, with `-Platform Windows` and no host pin.
Code and Review re-read runner defaults/catalogue at dispatch. Hard-fail an OS
mismatch before tool bootstrap. Run from the assigned checkout with a real
PowerShell executable; an App Execution Alias is a prerequisite failure.

Each native method and emergency sweep has D-4's limits. Set the checkpoint tool's
**row timeout to 10 minutes** explicitly; do not use the importer's 15-minute
minimum/3x estimate as the test deadline. Builds have the tool's separate bounded
15-minute deadline. Intermediate runs have a 20-minute total deadline; the final
run has a 35-minute total deadline, including slot waits. These are caps, not
permission to widen tests. If a row exhausts its cap, retain it as failed/not run,
inspect which phase held, and split only its named methods after a plan correction.

One explicitly listed administrative tool bootstrap is permitted per Code/Review
checkout (estimate included in the first build-bearing row). The prebuilt CLI only
orchestrates; its row drivers each take their own build slot. Do not wrap the
entire CLI run in another slot lease.

```powershell
if (-not $IsWindows) { throw 'CARD-1047 checkpoints require Windows' }
$PLAN = 'docs/superpowers/plans/2026-10-04-card-1047-windows-script-harness-plan.md'
$SOURCE = (git rev-parse HEAD).Trim()
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1047-tool -SlotWaitMinutes 5 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1047-tool/ --nologo
if ($LASTEXITCODE -ne 0) { throw 'Checkpoint tool bootstrap failed or slot timed out' }
# S1 uses --rows CP-1 --total-timeout 20m.
# S2 uses --rows CP-2 --total-timeout 20m.
# S3 and final ordinary Review use this exact final selection:
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1047-tool/ -- run --plan $PLAN --rows CP-4,CP-5,CP-6,CP-7 --serial --row-timeout 10m --total-timeout 35m --expected-source-sha $SOURCE --max-wait 55s
# For exit 75, set $runId to the printed identity and keep waiting to terminal:
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c1047-tool/ -- wait --run $runId --max-wait 55s
```

Await every foreground command and every reported run before ending the task.
The bootstrap is subject to the slice's 60-minute cap; stop it through its owned
process if that budget cannot be met. Slot exit 4 is not run; never retry unleased.
No source editing while a run is in flight, no automatic retry or known-flaky flag.
Commit a necessary fix before rerunning only the affected row; report reruns.

Preserve unedited CHECKPOINT lines, exact source SHA, executed roster and
executed/passed/failed/skipped counts, source/build provenance and TRX paths.
The importer checks class tokens, so independently compare emitted EXECUTED names
with the promised roster. Min is only a result floor, not a substitute for that
audit. Require 0 failed/skipped and clean/stable source with verified build binding.
Validate final receipts with `validate-checkpoint-receipt.ps1` at the tested SHA;
Review reports reviewedSourceClean true only with validated CP-4..CP-7 evidence.
Run `check-evidence-diff.ps1` over the complete task base-to-pushed-tip range.

Keep generated receipts, JSON, logs and TRX ignored. Report native readiness,
job/handle observations, logical root-exit ordering, separate EOF, elapsed time
and emergency-sweep findings in the stored task report. Remove only producer-owned
alternate outputs (including tool bootstrap output) using the output inventory;
keep evidence. No test execution on the Linux Plan host is Windows qualification.

### Cost

Estimates, not measurements; slot waits consume the hard dispatch/run caps.

| Dispatch | Authoring / inspection / evidence | Ordinary CP floor | Total target |
|---|---:|---:|---:|
| S1 / CP-1 | 42 min | 9 min | 51 min |
| S2 / CP-2 | 38 min | 8 min | 46 min |
| S3 / CP-4..CP-7 | 26 min | 27 min | 53 min |
| Separate final Windows Review | 15 min | 27 min | 42 min |

Code's ordinary floor is **44 minutes** (9+8+8+5+8+6); authoring/setup/evidence is
106, totaling **150 minutes across three bounded Code dispatches**, never one
150-minute Code brief. Final clean receipt selection is 95 results. Intermediate
CP-1 has 14 and CP-2 has 8, so all authoring checkpoints execute 117 results;
they do not inflate final coverage. Final Review executes the 95-result selection
at the final reviewed SHA, without repeating the intermediate rows.

Mutation floor: **20 controls x 3 method-scoped phases x 3 estimated minutes =
180 minutes**, plus 10 minutes evidence/restoration setup, **190 minutes**. Every
phase has the exact filter above; estimates include isolated build and the bounded
method. Partition commissioning by named PC subsets if needed; preserve one cycle
per behavior. These controls mostly touch the same adapter and cannot be safely
batched in one mutant. Do not add unbound SourceLanding worktrees. Total Code +
ordinary Review + later Mutation estimate is **382 minutes**, excluding repair.

Reuse of CP-4's build for three final rows avoids three graph builds (estimated
15 minutes). No broad-Unit/full-assembly cost is hidden. Budget changes require
the failing method/phase and measured cause, not guessed larger timeouts.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1047-s1/` | windows-launch | `/*/*/(ScriptHarnessProcessTests)\|(ScriptHarnessWindowsOwnershipTests)/*` | V-1/V-3, R-1/R-2/R-3 | Windows only; N1-N11 plus W1-W3 at S1: exactly 14, 0 failed/skipped; 10m row cap | 14 | 9 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1047-s2/` | windows-job-handles | `/*/*/ScriptHarnessWindowsOwnershipTests/(Child_is_assigned_before_first_instruction)\|(Assignment_failure_never_resumes_child)\|(Resume_failure_terminates_suspended_child)\|(Closing_private_job_kills_owned_tree)\|(Nested_job_timeout_kills_owned_descendants_only)\|(Private_job_handle_is_not_inherited)\|(Only_standard_handles_are_inherited)\|(Parent_closes_child_pipe_write_handles)` | V-1/V-2, R-1/R-2/R-3 | Windows only; W1-W8: exactly 8, 0 failed/skipped; 10m row cap | 8 | 8 | true |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c1047-final/` | windows-contract | `/*/*/(ScriptHarnessProcessContractTests)\|(ProcessSpawnLimitTests)\|(TestLaneCategoryGuardTests)\|(SlowTestTripwireTests)/*` | V-4, R-4 | Windows only; 26+3+1+2: exactly 32, 0 failed/skipped; 10m row cap | 32 | 8 | true |
| CP-5 | S1-S3 | CP-4 | windows-native | `/*/*/(ScriptHarnessProcessTests)\|(ScriptHarnessWindowsOwnershipTests)/*` | V-1/V-2/V-3, R-1/R-2/R-3 | Windows only; N1-N11 plus W1-W10: exactly 21, 0 failed/skipped; 10m row cap | 21 | 5 | true |
| CP-6 | S1-S3 | CP-4 | windows-checkpoint-callers | `/*/*/RunCheckpointScriptTests/*` | V-5, R-4 | Windows only; all 24 existing methods, 0 failed/skipped; 10m row cap | 24 | 8 | true |
| CP-7 | S1-S3 | CP-4 | windows-slot-callers | `/*/*/BuildSlotScriptTests/(C589_*)\|(Wrapper_renews_a_renew_mode_grant_while_the_command_runs)\|(C800_WrapperPassesWildcardArgvLiterally)\|(C800_WrapperStartsUnitFilterWithinDeadline)\|(C800_WrapperForwardsScriptTokens)\|(C800_WrapperLaunchesNativeExecutableLiterally)\|(C845_*)\|(C1048_*)` | V-5, R-4 | Windows only; 7 C589 + 1 renew + 4 C800 + 4 C845 + 2 C1048: exactly 18, 0 failed/skipped; 10m row cap | 18 | 6 | true |
