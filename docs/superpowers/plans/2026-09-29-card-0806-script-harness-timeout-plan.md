# CARD-0806: own and terminate ScriptHarness processes after timeout

Date: 2026-09-29. Stage: Plan. Source inspected: `16da56da5a9c9bc3d93ba5006dd139b072750c26`.
Next stage: **TestDesign**; verification was not folded into this dispatch. This plan
defines the implementation, regression requirements and proposed checkpoint roster;
TestDesign adds the executable guard/positive-control inventory before Code.

## Outcome and scope

Make `ScriptHarness` own each invocation before its script can spawn children. Its
existing 300-second budget covers launch, root exit, stdout EOF and stderr EOF. A
timeout terminates the invocation's Windows Job Object or Linux process group, even
after the script's `pwsh` root has exited. Cleanup has a separate bounded budget and
cannot turn the timeout into a pass.

Scope is test infrastructure: `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`, new
test-local process owners and fixtures, their build wiring, and the testing owner
doc. No session-runner, checkpoint-driver, production custody, ports, database or
deployment changes. Do not expand this card to other process wrappers.

The input is [task 15310834's investigation](../../investigations/2026-09-29-card-0806-script-harness-timeout.md).
It was an untracked file in `/work/worktrees/task-15310834` and absent from this
task's checkout. This Plan preserves an unchanged copy so the evidence travels
with the plan. Its SHA-256 is
`ea2792f131d1d78dac3d0b95a65564e9401b11c1bf5235b90dcf934ba2e48e4f`.
The investigation establishes the defect from control flow, not a live reproduction.
No builds or tests were run during Plan.

## Ground truth

| Assumption | Inspected behavior | Consequence |
|---|---|---|
| The timeout stops the child. | `ScriptHarness.cs` creates a 300-second CTS only for `WaitForExitAsync`; there is no kill. `Process.Dispose` closes the wrapper. | Add explicit owned termination before disposal. |
| Root exit completes the operation. | Both `ReadToEndAsync` tasks are awaited after the timed wait, without a deadline. | One deadline must include both readers, including the root-already-exited case. |
| A tree kill fixes both cases. | Descendants can outlive and be reparented away from the root. Root `HasExited` is not descendant completion. | Establish ownership at launch; never condition owner termination on root liveness. |
| The checkpoint Windows owner can be reused unchanged. | `WindowsJobProcessHandle.Start` uses `Process.Start` before best-effort job assignment; its own comment documents escaping early children and package aliases. | Use its interop as reference only; assignment before execution is required here. |
| `setsid`'s returned PID is always the group identity. | `DetachedLauncher` starts `setsid` and returns its `Process.Id`; util-linux may fork. | Obtain an explicit ownership handshake from a persistent group leader instead of guessing a PGID. |
| All children are fictional. | `test-run-checkpoint.ps1` starts nested `pwsh`; `test-nightly-run.ps1` also launches children. | Real child/grandchild and inherited-pipe tests are required. |
| An existing native child fixture fits both platforms. | `Antiphon.CustodyTestChild/Program.cs` exits unless Windows and requires job/named-event setup. | Add a small portable helper; do not change production custody fixtures to fit this card. |
| Every caller needs rewriting. | The static wrapper has a common signature and C487 PASS-inventory validation; BuildSlot and RunCheckpoint already use it. | Preserve that signature and validation; put new options in a separate internal overload. |
| One native lane is enough. | Read-only `GET /api/runner-defaults` returned revision 2, global `server2`; `GET /api/session-runners` reported eligible Windows and Linux runners on 2026-09-29. | Require both OS lanes; choose runners from the live catalogue at dispatch, without hard-coded host pins. |

Bodies read include ScriptHarness, DelegateScriptRunner, BuildSlotScriptTests,
RunCheckpointScriptTests, Antiphon.Tests.csproj, CustodyTestChild, checkpoint
WindowsJobProcessHandle/ProcessDriver/DetachedLauncher, and the required sections
of `docs/testing-and-build.md` and `docs/project-context.md`.

## Decisions

### D-1. Keep the ordinary budget and caller contract

Keep `RunHarnessCaseAsync(harness, prefix, caseName, expectedRows, params requiredRows)`.
It delegates to an internal overload taking immutable invocation options, an
`IReadOnlyList<string>` inventory and a final `CancellationToken`. Options carry
the execution budget (default 300 seconds), cleanup budget (default 10 seconds),
and a test-only executable/script location seam. Reject zero/negative/infinite
budgets before launching. There is no machine-wide mutable timeout or environment
override. Existing callers retain their current signature; explicit cancellation
is available to the new overload without inventing an unverified TUnit ambient API.

Use a monotonic deadline starting before owned launch. Concurrently pump both
streams and await the logical script root's exit plus both EOFs under that same
deadline. Starting a fresh execution timer after root exit is rejected: it would
allow another 300 seconds for an inherited pipe. Check a faulted reader promptly;
do not wait for every other task before noticing its failure.

### D-2. One cleanup path and a bounded failure result

An invocation owns the root, ownership container, readers, control channel and
unique results/control directories. A small internal `IOwnedScriptProcess` is
permitted as an external-I/O seam; keep the orchestration concrete. Expose logical
root exit separately from owner exit, stream completion and termination. This
distinction is essential on Linux, where the supervisor outlives `pwsh`.

On timeout, caller cancellation, a read error, launch failure after partial setup,
or a normal exit, run idempotent owner cleanup exactly once. Normal completion also
releases any background descendants that have closed their output pipes: this
harness never transfers a child to a standing owner. Do not test `root.HasExited`
before terminating the job/group. A live-root `Kill(entireProcessTree: true)` may
be a best-effort fallback, but never substitutes for the container.

Cleanup uses a **fresh**, non-canceled token and **one total 10-second budget** for
termination, root/owner observation and pipe drain, not ten seconds for each step.
Never make an unbounded `WaitForExit`, reader await, control write, or dispose-time
join. Close readers/control endpoints at that deadline, observe eventual task
faults, and report which completion evidence was missing. Kill acknowledgement or
root exit alone is not proof all owned processes exited. Windows queries the job's
active-process accounting; Linux observes the supervisor and invocation-group
members, distinguishing terminated zombies from executing processes and reaping
direct children it owns. Exhausted verification is an explicit cleanup failure.

Expiry throws `TimeoutException` naming harness, case and budget; caller
cancellation remains `OperationCanceledException` with the caller token. Preserve
the original assertion/start/read exception on other paths. Include completed or
partial stdout/stderr and secondary kill/drain/delete errors in diagnostics. Pump
into per-stream buffers so diagnostics do not require a hung `ReadToEndAsync` to
finish. Keep successful output and existing PASS assertions intact. A cleanup
failure following otherwise passing output fails the invocation.

Attempt deletion of only this invocation's results/control paths **after** owned
termination and stream closure. If process death remains unconfirmed, retain its
paths and include them in the failure; do not delete files from under an unknown
writer. Expected already-exited races are benign; access denied, failed native
calls and a drain deadline are not silently swallowed. Disposal is a final
idempotent safety net, not the primary termination mechanism.

### D-3. Windows: assign the suspended executable to a private Job Object

Add a test-local Windows adapter. Resolve a real `pwsh.exe`, use `CreateProcessW`
with `CREATE_SUSPENDED`, create a private non-inheritable kill-on-close Job Object,
assign the root, then resume its primary thread. An assignment/resume failure
terminates the still-owned suspended root and closes all handles; script execution
must never continue without the job. Use nested jobs under an existing test-runner
job; do not request breakaway or enable either breakaway limit. These choices use
the documented [suspended-start contract](https://learn.microsoft.com/en-us/windows/win32/procthread/process-creation-flags)
and [job inheritance/termination contract](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

Own the redirected pipes with safe handles. Restrict inherited handles to child
stdin/stdout/stderr using a startup handle list; keep read ends and the job handle
non-inheritable, and close the parent's copies of child write ends immediately.
Observe root exit by its process handle. Keep the job handle through root exit and
drain, call `TerminateJobObject` during cleanup, and close it only after bounded
accounting/pipe observation. Do not assume a generic wait on the job handle means
all processes are gone.

Preserve working directory, inherited environment, UTF-8 PowerShell output and
argument boundaries, including spaces, quotes and trailing backslashes; use the
Windows command-line quoting rules, not `string.Join`. This adapter cannot launch
an App Execution Alias and claim strict custody of the process a broker creates:
detect/refuse that launch shape before running the harness, with an actionable
message to expose the real PowerShell executable. A test-local explicit executable
path is allowed; no checkout- or host-specific path is committed.

Rejected: assigning after `Process.Start`, reconstructing ancestry at timeout,
`taskkill /T`, and reusing the checkpoint best-effort implementation. Each leaves a
window before ownership or loses descendants when the root exits.

### D-4. Linux: a persistent, private process-group supervisor

Add `tests/Antiphon.ScriptHarnessHost`, a small `net9.0` console helper. Start it
through `dotnet <helper.dll>` so Linux `UseAppHost=false` works. In `linux-owner`
mode it calls `setsid()` before starting any harness child and verifies
`PID == PGID == SID`. A fresh process is expected not to be a group leader; failure
is a launch refusal, never permission to use the inherited group. The syscall
[creates the new session and group](https://man7.org/linux/man-pages/man2/setsid.2.html).
Do not call `setsid` in the test runner itself or fork managed runtime threads.

Use a private, invocation-scoped duplex control pipe with a nonce and bounded
messages. The host records PID, `/proc` start-time identity, PGID and SID, verifies
them against the handshake and rejects PGID <= 1 or its own group. Only a matching
start authorization lets the supervisor launch the requested `pwsh`. Environment,
cwd and arguments are passed structurally; never interpolate a shell command.

The supervisor launches the logical script root in its group and stays alive
after that root exits. It reports the root PID/start identity and exit code over
the control channel. Root/descendants inherit the output pipes; after launch the
supervisor closes **its own** stdout/stderr writer descriptors and never uses them
again, so a successful root can produce genuine EOF. Root stdin is separate from
the supervisor's control channel. This protocol must not contaminate PASS output.

On stop, control disconnect, or its own failsafe deadline, the supervisor signals
its **own** confirmed group with SIGKILL. A negative PID targets a process group;
zero and -1 have broader meanings and must never be used. See the
[signal API contract](https://man7.org/linux/man-pages/man2/kill.2.html). Keeping the
supervisor alive pins the group identity after the script root exits; self-signaling
also avoids sending to a recycled root PID/PGID. The host bounds control delivery
and observes supervisor exit and pipe completion. Give the supervisor an independent
monotonic failsafe equal to the invocation execution deadline, plus the cleanup
budget, so loss of the caller's cleanup path is also bounded.

An unexpectedly lost or unresponsive supervisor is a cleanup failure, not proof of
an empty group. Do not perform a delayed blind `kill(-savedPgid)` or a process-name
sweep. Record the owned identities; any fallback must validate the exact identity
and must not signal a group after its identity is lost. TestDesign must cover this
refusal and the independent failsafe as well as the ordinary stop command.

Rejected: bare `setsid pwsh` with its transient root as the only ownership token,
polling PPIDs after reparenting, and adding privileged cgroup setup to this helper.
The supervisor costs one small process per invocation and supplies a persistent
group identity without host configuration.

### D-5. Be precise about the ownership boundary

This card covers ordinary descendants inheriting the Windows job or Linux process
group, including children reparented after root exit. It is not containment of
hostile code: Linux children that deliberately call `setsid`/`setpgid`, Windows
broker/WMI-created external processes, privilege changes and kernel-stuck tasks
are outside that guarantee. Cgroups would be required for Linux group escape;
production custody already owns that separate concern. No privileged helpers or
changes to it belong here. If a real ScriptHarness consumer depends on escaping
the group, report the exact consumer before claiming full coverage.

## Implementation slices

Commit S1-S3 as a single verification group after their meaningful slice commits;
the native proof depends on the finished owner, fixture and caller integration.
No build is needed for each intermediate commit.

| Slice | Files | Change and acceptance |
|---|---|---|
| S1 | `tests/Antiphon.Tests/Scripts/ScriptHarness.cs`; new `ScriptHarnessProcess.cs`, `ScriptHarnessProcessContractTests.cs` in the same directory | Preserve wrapper/inventory; add immutable options, shared execution deadline, I/O ownership seam, concurrent output pumps, one bounded cleanup path and primary-error preservation. Unit doubles exercise actual coordinator decisions, not copied logic. |
| S2 | New `WindowsScriptHarnessProcess.cs`, `LinuxScriptHarnessProcess.cs` in that directory; new `tests/Antiphon.ScriptHarnessHost/{Antiphon.ScriptHarnessHost.csproj,Program.cs}`; `tests/Antiphon.Tests/Antiphon.Tests.csproj` | Implement D-3/D-4; reference the helper as `ReferenceOutputAssembly=false` and copy its producer-reported output into `script-harness-host/`, following existing child-project copy targets. Always invoke the DLL with dotnet. Add the helper to `Antiphon.sln` for solution build discoverability. |
| S3 | New `ScriptHarnessProcessTests.cs`, `ScriptHarnessWindowsOwnershipTests.cs`, `ScriptHarnessLinuxOwnershipTests.cs`, `ScriptHarnessProcessFixture.cs`; new `Scripts/Fixtures/script-harness-process.ps1`; helper fixture modes; `tests/Antiphon.Tests/slow-tests-allowlist.txt` if required; `docs/testing-and-build.md` | Real process regressions, independent fixture cleanup, existing-caller compatibility, documented timeout/ownership contract. No production config or script changes. Register Slow classes with an exact reason if the measured >=5-second tripwire requires it. |

## Regression requirements for TestDesign

Every spawning class is `Integration` and has the assembly-local
`[ParallelLimiter<ProcessSpawnLimit>]`; the non-spawning coordinator class is
`Unit`. No real Program host, runner, broker or provider is launched. Existing
BuildSlot/RunCheckpoint fixtures use their offline shims. New PowerShell files are
ASCII-only. TestDesign supplies exact V/R IDs and one distinct executable PC per
independently bypassable safety guard, and appends `## Verification design`.

Use a per-invocation private temp directory, nonce and readiness barrier. The
fixture `pwsh` launches a child and grandchild and records PID plus process-start
identity for all three; on Windows retain observation handles, on Linux also
record group/session/start ticks. The children acknowledge that the intended tree
and inherited pipe handles exist before the fixture advances. The fixture supports
holding stdout alone, stderr alone, both, or neither. For parent-exited cases,
prove the **logical pwsh root exited** while a descendant is still alive and the
selected reader has not reached EOF; killing a live supervisor is not a substitute
for this observation.

Proposed native deadline: **5 seconds**, cleanup **2 seconds**, observed method
completion **<=10 seconds** from owned launch. Native startup/readiness is part of
the execution budget and has an explicit assertion: a test that timed out before
the child/grandchild was ready is a fixture failure, not a regression pass. Gate
release follows readiness instead of sleeping for guessed PowerShell startup.
Each test has a 30-second outer safety limit independent of ScriptHarness. The
repeat test runs three invocations with a 45-second outer limit. These are test
seam values; never wait 300 seconds or widen normal budgets for this regression.

The fixture has its own `finally` cleanup using recorded identities/handles and a
hard child self-exit ceiling. It must not rely only on the implementation being
tested. Assert absence of executing owned descendants **before** the emergency
sweep; report if the sweep found anything, so it cannot conceal a failed kill.
No PID-name sweep, guessed group kill, or test-runner termination is allowed.

| Proposed common native method (`ScriptHarnessProcessTests`) | Decisive outcome |
|---|---|
| `Live_root_timeout_kills_root_child_and_grandchild` | Ready live tree; timeout, bounded completion, all three identities exited, paths cleaned after death. |
| `Exited_root_with_stdout_holder_times_out_and_kills_descendant` | Root exit is observed before timeout; stdout stays open; timeout and descendant death are both required. |
| `Exited_root_with_stderr_holder_times_out_and_kills_descendant` | Same independent assertion for stderr; cannot pass by timing stdout alone. |
| `Caller_cancellation_kills_tree_before_returning` | Cancel after readiness; caller-token cancellation preserved, cleanup runs with a fresh token, no survivors. |
| `Passing_case_preserves_inventory_and_argument_boundaries` | Real C487 trailer and exact PASS count/required names accepted; paths/arguments round-trip, including Unicode path and quoted case text. |
| `Nonzero_exit_preserves_output_and_cleans_results` | Nonzero exit fails with stdout and stderr diagnostic markers; cleanup still completes. |
| `Bad_pass_inventory_still_fails` | Exit zero with a missing required PASS name fails, even though process cleanup succeeds. |
| `Completed_root_with_silent_descendant_releases_owner` | Root and both readers finish; remaining child with closed output is gone before successful return. |
| `Root_exit_racing_deadline_still_cleans_owner` | Barrier-controlled near-boundary exit may complete or time out as appropriate; either outcome leaves no child or owner. Coordinator unit tests pin the deterministic winner separately. |
| `High_volume_on_both_streams_completes` | Both streams exceed pipe capacity with terminal markers; no sequential-read deadlock, PASS contract retained. |
| `Repeated_timeouts_do_not_leak_owners` | Three ready trees time out; every invocation's owner and descendants are dead, and independent cleanup found no survivors. |

Windows-only class: `Child_is_assigned_before_first_instruction` (native job
membership plus a deterministic pre-resume barrier),
`Assignment_failure_never_resumes_child` (injected assignment failure, no first
instruction marker, suspended process gone), and
`Nested_job_timeout_kills_owned_descendants_only` (test-owned outer job and an
unrelated sentinel; target tree dies, sentinel survives).

Linux-only class: `Supervisor_pins_group_after_root_exit` (root exit, live matching
supervisor, actual member PGID/SID and inherited-pipe state),
`Invalid_group_handshake_never_starts_harness` (nonce/identity/own-group refusals
through the real handshake parser), and
`Group_stop_leaves_unrelated_process_alive` (target group dies; an independently
owned sentinel survives). Add separate methods for control disconnect/failsafe and
lost-supervisor refusal when TestDesign finalizes the roster; update Min from
executed methods, never assertion counts.

Coordinator unit cases must cover the shared deadline on each reader independently,
fresh cleanup token after cancellation, kill/drain failure preserving the primary
timeout, launch-failure unwind, default 300-second options, invalid budgets before
spawn, normal cleanup failure becoming failure, and idempotent cleanup. Use a
noncompleting **fake** stream for the drain-deadline case, not a deliberately escaped
real process. These supplement, and cannot replace, native tests.

Required positive-control targets for TestDesign include: remove owner termination;
guard it on root liveness; time only root exit; omit either reader from the common
deadline; reuse the canceled token for cleanup; remove the drain bound; swallow a
native cleanup error; resume before Windows assignment; allow assignment failure;
release the Linux supervisor on root exit; accept a foreign/own PGID; and bypass
the Linux control-disconnect/failsafe path. Map each to an observable assertion;
split independently bypassable guards. TestDesign owns the complete count and
mutation commands, not this proposed list.

No user/session delivery changes: the owner handshake is local test-helper control
traffic. TestDesign's delivery inventory should cover its start, root-exit and stop
receipts with the invocation nonce; a stop write is not an exit receipt. No durable
queue, card event or transcript is introduced by this card.

## Proposed checkpoint execution

These are the Plan's proposed rows for TestDesign to finalize under its
`## Verification design`. Do not dispatch Code until that section and complete PCs
exist. All rows share `After=S1-S3`; builds are reused only within the same OS lane
and same commit. Run CP-1..3 on **Linux**, CP-4..6 on **Windows**. Group names state
the lane because the manifest schema has no Platform column. Cross-platform rows
are required evidence, not skips on the other platform. Resolve live runners and
request the needed `-Platform`; omit `-Runner` unless the caller must pin one.

Checkpoint-tool form, after TestDesign is complete (PLAN is the path to this file).
If the tool is not already built at this source tip, the following explicitly
listed administrative bootstrap is required once per OS; its build-slot lease
must be released before starting rows that obtain their own leases:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c806-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c806-tool/ --nologo
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c806-tool/ -- run --plan $PLAN --rows CP-1,CP-2,CP-3
```

Use CP-4,CP-5,CP-6 on Windows. The second command is the already-built orchestration
CLI, not a test/build driver; each row driver takes its own slot. Do not hold an
extra wrapper lease throughout the CLI wait. Follow any running exit 75 with tool `wait` for the
reported run until it reaches a terminal exit; use foreground waits no longer than
60 seconds per call. Report slot timeout 4 as not run. No bypass with `-NoSlot`.
Report each CP with commit, filter, executed/passed/failed/skipped counts, TRX and
reruns. Report any unlisted build/test and the reason before running it. Review
requires native receipts from both platforms at the same source tip.

The compatibility row deliberately selects two representative existing callers
with real nested PowerShell children and the shared PASS contract: 24
RunCheckpointScriptTests methods and 8 BuildSlotScriptTests methods, **32 executions**.
It does not rerun every release/nightly policy suite, whose internal policy is
unchanged. New native tests cover ScriptHarness's lifecycle itself. The Unit lane
also runs classification guards. The common/native floors below are 11+3=14;
TestDesign must raise them when adding the specified Linux protocol cases.

### Cost

Estimates, not measurements (CP-1/CP-4 include tool bootstrap): Linux 6+3+10 = **19 minutes**, Windows 8+4+14 =
**26 minutes**; ordinary V/R floor **45 minutes**, plus slot wait. Allow **120-180
minutes** for authoring the native ownership and fixtures. TestDesign separately
prices method-scoped positive controls; none were executed here. One build per OS
instead of per slice avoids four graph rebuilds, estimated **20 minutes** saved.
No production rollout or full-assembly integration run is needed.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c806-linux/` | linux-unit | `/*/*/*/*[Category=Unit]` | D-1/D-2 coordinator and classification | all listed Unit tests, 0 failed; new contract class executed | 1 | 6 |
| CP-2 | S1-S3 | CP-1 | linux-native | `/*/*/(ScriptHarnessProcessTests*)\|(ScriptHarnessLinuxOwnershipTests*)/*` | live root, both open-pipe regressions, D-4 ownership and caller compatibility | all listed native methods, 0 failed/skipped | 14 | 3 |
| CP-3 | S1-S3 | CP-1 | linux-existing-callers | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | unchanged PASS inventory and nested pwsh callers | all 32 existing methods, 0 failed/skipped | 32 | 10 |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c806-windows/` | windows-unit | `/*/*/*/*[Category=Unit]` | D-1/D-2 coordinator and classification | all listed Unit tests, 0 failed; new contract class executed | 1 | 8 |
| CP-5 | S1-S3 | CP-4 | windows-native | `/*/*/(ScriptHarnessProcessTests*)\|(ScriptHarnessWindowsOwnershipTests*)/*` | live root, both open-pipe regressions, D-3 strict job ownership | all listed native methods, 0 failed/skipped | 14 | 4 |
| CP-6 | S1-S3 | CP-4 | windows-existing-callers | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | unchanged PASS inventory and nested pwsh callers | all 32 existing methods, 0 failed/skipped | 32 | 14 |
