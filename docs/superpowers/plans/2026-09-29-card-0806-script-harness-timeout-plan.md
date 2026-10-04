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

### Original Plan cost (superseded by Verification design)

Estimates, not measurements (CP-1/CP-4 include tool bootstrap): Linux 6+3+10 = **19 minutes**, Windows 8+4+14 =
**26 minutes**; ordinary V/R floor **45 minutes**, plus slot wait. Allow **120-180
minutes** for authoring the native ownership and fixtures. TestDesign separately
prices method-scoped positive controls; none were executed here. One build per OS
instead of per slice avoids four graph rebuilds, estimated **20 minutes** saved.
No production rollout or full-assembly integration run is needed.

### Original Plan rows (superseded by Verification design)

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c806-linux/` | linux-unit | `/*/*/*/*[Category=Unit]` | D-1/D-2 coordinator and classification | all listed Unit tests, 0 failed; new contract class executed | 1 | 6 |
| CP-2 | S1-S3 | CP-1 | linux-native | `/*/*/(ScriptHarnessProcessTests*)\|(ScriptHarnessLinuxOwnershipTests*)/*` | live root, both open-pipe regressions, D-4 ownership and caller compatibility | all listed native methods, 0 failed/skipped | 14 | 3 |
| CP-3 | S1-S3 | CP-1 | linux-existing-callers | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | unchanged PASS inventory and nested pwsh callers | all 32 existing methods, 0 failed/skipped | 32 | 10 |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c806-windows/` | windows-unit | `/*/*/*/*[Category=Unit]` | D-1/D-2 coordinator and classification | all listed Unit tests, 0 failed; new contract class executed | 1 | 8 |
| CP-5 | S1-S3 | CP-4 | windows-native | `/*/*/(ScriptHarnessProcessTests*)\|(ScriptHarnessWindowsOwnershipTests*)/*` | live root, both open-pipe regressions, D-3 strict job ownership | all listed native methods, 0 failed/skipped | 14 | 4 |
| CP-6 | S1-S3 | CP-4 | windows-existing-callers | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | unchanged PASS inventory and nested pwsh callers | all 32 existing methods, 0 failed/skipped | 32 | 14 |

## Verification design

Port qualification (2026-10-04, Code task `929f4620`): this dispatch ports the
existing implementation to master and commissions Linux CP-1/CP-2/CP-3 only,
without a whole-Unit run. Windows CP-4/CP-5/CP-6, V-4 and R-7 are excluded;
`ScriptHarnessWindowsOwnershipTests` still has zero `[Test]` methods. The caller
owns its Backlog follow-up. All PC-1..PC-70 and both OS variants of PC-13/PC-14
remain pending for post-land Mutation. Master's existing BuildSlotScriptTests
now has 17 methods, so Linux CP-3 runs 24+17=41 instead of the pinned base's
24+8=32. The filter is unchanged and its execution floor is raised accordingly.
The inherited L16 test captures the supervisor PID before cancellation rather
than reading its disposed Process wrapper after cleanup; its deadlines and
assertions stay unchanged.

Finalized 2026-09-29 by TestDesign task `b5dff6bc`, against Plan commit
`c8e1970541c08ea3513bdc3b8325e42f1394fc9f`. **Ready for Code.** D-1 through
D-5 and S1-S3 remain the implementation design. This section replaces the proposed
rosters, counts and estimates above. The original checkpoint heading was renamed
because `PlanTableImporter.ExtractSection` reads the first exact `### Checkpoints`
heading. The final table below is the only executable manifest.

This is verification design, not execution evidence. No build, test or mutation
was run in TestDesign. Code implements these tests and runs ordinary V/R; Review
audits both OS receipts before land; a separately commissioned post-land Mutation
runs the positive controls.

### Inspection

| Bodies read at the pinned base | Boundary and disposition |
|---|---|
| `ScriptHarness.cs`, `Application/DelegateScriptRunner.cs` | Shared deadline, output assertions, results deletion and wrapper compatibility -> V-1/V-2/V-3/V-6, R-1..R-6. DelegateScriptRunner's separate lifecycle is outside this card. |
| `Scripts/RunCheckpointScriptTests.cs`, `Scripts/BuildSlotScriptTests.cs` | 24 + 8 nonparameterized methods, no OS skip -> V-9. PASS assertion counts are not TUnit executions. |
| `scripts/test-run-checkpoint.ps1` (launch and offline shims), `scripts/test-build-slot.ps1` (launch and shim setup), `scripts/lib/c487-harness.ps1` (assertions and completion) | Nested pwsh, inherited environment, named inventory/trailer -> V-3/V-9, R-6. No live slot broker needed inside these fixtures. |
| `Antiphon.CustodyTestChild/Program.cs`, checkpoint `WindowsJobProcessHandle.cs`, `Checkpoints/ProcessDriverTests.cs`, `Checkpoints/DetachedLauncherTests.cs` | Nearest native launch/observation fixtures; existing Windows child is Windows-only, existing job wrapper assigns too late, detached launcher test is not inherited-pipe proof -> V-4/V-5, R-7/R-8/R-9. Do not reuse their weaker custody assumptions. |
| `Antiphon.Tests.csproj` producer-copy targets | DLL/deps/runtimeconfig staging and `UseAppHost=false` -> V-3/V-5. A missing helper is a failed fixture, never a skip. |
| `ProcessSpawnLimitTests.cs`, `TestLaneCategoryGuardTests.cs`, `SlowTestTripwire.cs`, `SlowTestTripwireTests.cs` | 3 + 1 + 2 existing Unit results; limiter list is a floor -> V-10. Add the new classes' limiter assertion to the new contract class. |
| `PlanTableImporter.cs` including `RosterTokens`, `ExtractSection`, Build reuse; testing owner and `stage-test-design.md` | First table wins; category predicates yield no roster tokens -> explicit class filters, per-class roster comparison, serial rows. |

Missing setup belongs to S1-S3: all four new test classes, the owned-I/O double,
native fixture/PowerShell script, portable helper and its copy target do not exist
at the base. Add them; do not claim an existing custody test exercises this fix.
All test names below are final planned names. `U`, `N`, `W`, `L` resolve to
`Antiphon.Tests.Scripts.ScriptHarnessProcessContractTests`,
`ScriptHarnessProcessTests`, `ScriptHarnessWindowsOwnershipTests`, and
`ScriptHarnessLinuxOwnershipTests`, respectively; their source files are in
`tests/Antiphon.Tests/Scripts/`. Each roster row is **one nonparameterized `[Test]`
method**. Subcases/loops are named assertion cases, not extra executions.

#### Fixture and assertion contract

Use D-1's per-invocation options and D-2's I/O seam. For Unit tests, supply a
per-instance monotonic clock/timer and controllable tasks/streams to the real
coordinator; never duplicate its decision logic. Advance the test clock across
known barriers, not by sleeping. An independent real-time watchdog converts a
noncompletion into an explicit assertion failure, then releases fake operations.
No native operation is needed for U. This clock is local to the harness and is
never registered in a server graph.

Native tests keep **execution 5s, cleanup 2s, completion <=10s**, measured from
before owned launch; readiness consumes execution time. An outer 30s safety
watchdog (45s for N11) protects the fixture. Every child has an independent 20s
self-exit ceiling, later than the 10s verdict so it cannot make a missing kill
look green. N11 uses three sequential invocations and checks each one before the
next. Retain evidence outside the harness-owned results/control directory so
successful deletion cannot erase the observer's PID/start-identity receipts.

Native readiness records the nonce, root/child/grandchild identities and selected
pipe holders. Observe Windows identities through retained process handles; Linux
records `/proc` start ticks, PGID, SID and state. Require readiness **before**
accepting a timeout, cancellation or root-exited regression. N2/N3 additionally
require observed logical pwsh exit, a still-executing descendant, one held stream
and EOF on the other. A live supervisor is not the logical root. N1 holds both;
N8 holds neither. N10 writes at least 1 MiB to each stream with independent final
markers, so reading just stdout first cannot accidentally pass.

After the harness returns, inspect owned process identities before the fixture's
independent `finally` sweep. Normal tests require zero executing owned processes,
closed streams, released owner handles/control endpoints and no surviving results
paths. Emit an assertion if the emergency sweep finds residue. **W10 and
L14/L15/L17 are intentional uncertainty cases**: they instead require a cleanup failure, retained
paths and the recorded residue/unknown identity, then independently reap their
known fixture children. Such expected residue never counts as successful cleanup.
L16 requires failure at the cleanup deadline, then separately observes failsafe
death; it must not report those later deaths as timely host cleanup.

Fault seams change external I/O only: Windows syscall return values/handle
observations, Linux identity reads/control frames/signal calls, and coordinator
streams/tasks. Pass seams per invocation, never global flags or ambient environment
mutations. For PCs which could broaden a signal, accept a foreign group or resume
an unowned root, a fixture interlock records the attempted native action and
refuses targets outside the fixture's independently owned handles/identities.
This interlock is not mutated and is not the assertion under test. A rejected
unsafe attempt is itself the expected failing assertion; never actually signal
PID 0, -1, the test runner's group or a foreign process. Keep native happy-path
proofs without the replacement I/O alongside these fault-injection tests.

Linux malformed-handshake subcases run through the actual decoder/validator with
injected identity observations. Corrupt one field at a time; for lower-bound and
own-group cases supply otherwise matching observations so another tuple check
cannot mask the guard. An intercepted start authorization must remain absent.
Use one real valid-handshake control to prove the actual launch path separately.
These subcases prove refusal decisions, not kernel identity; L1/L10 prove that.

Windows ordering is deterministic: a pre-assignment barrier queries membership
and thread suspension, and a syscall trace requires Assign success before the
first Resume attempt. A first-instruction marker is secondary evidence; merely
not seeing a marker during a sleep is insufficient. Capture explicit owner-stop
attempts before disposal so kill-on-close cannot hide a bypassed termination call.

### Delivery inventory

No application queue, session, transcript or durable delivery path changes. The
only new transport is the invocation-local duplex helper channel. Real queue and
UserPrompt tests are therefore excluded; a frame write or ACK is not process
completion evidence. The nonce joins every frame to retained OS process identity;
there is no durable recovery/replay across invocations.

| Producer -> destination | Boundary, recovery and required recipient evidence | Tests / guards |
|---|---|---|
| Supervisor hello -> host | Before authorization, store PID/start/PGID/SID observation and nonce in invocation memory. Bad/missing/oversized hello refuses launch; close and observe the helper. Receipt is host validation plus matching native identity, not a successful write. | L1/L2/L3/L6; G-35..G-42/G-44 |
| Host start -> supervisor | Matching nonce and validated identity are required before child start. Exercise an already-reading recipient, a supervisor held before reading, EOF before start, and wrong authorization. Receipt is child readiness with matching nonce and root identity; zero child-start markers on refused paths. | L4/L5/L20; G-43/G-45/G-60 |
| Supervisor root-exit -> host | Exact nonce and root PID/start identity, exit code and process observation agree. Wrong, missing or truncated receipt cannot fabricate root completion; shared deadline/disconnect cleanup bounds loss. | L7/L8, N2/N3; G-46/G-47 plus G-4 |
| Host stop -> supervisor | Matching nonce; exercise ready reader and blocked reader, EOF during delivery, ACK without termination and supervisor death. Receipt is supervisor exit **and** no executing owned group members **and** stream closure; no ACK is needed after self-SIGKILL. | L9/L10/L12/L14/L16/L19; G-17/G-48/G-49/G-52/G-54/G-59 |
| Supervisor failsafe -> its own group | Independent monotonic deadline still fires with a connected but idle controller or a stalled stop loop; child launch does not restart the budget. Observer remains outside the group. | L13/L20; G-53/G-60 |

L4/L5/L6/L7/L8/L9/L16 use the real bounded framing/control code; injected frames,
paused readers and native observations isolate refusal/race decisions. They do
not substitute for L1/L10/L12/L13's real process/group death evidence. Root stdin
is independently exercised by L21 and never carries protocol frames.

### Proves it works now

| ID | Behavior, layer and exact roster | Expected result |
|---|---|---|
| V-1 | Coordinator: U1-U9/U23 | Valid defaults; invalid budgets reject before spawn; launch/root/both readers share one deadline; faults promptly start cleanup. |
| V-2 | Coordinator cleanup: U10-U22/U26 | Fresh bounded cleanup, preserved primary outcome, explicit owner termination, truthful death/drain evidence and owned-path deletion. |
| V-3 | Portable native: N5/N6/N7/N10 | Real DLL helper and PowerShell fixture work; arguments/cwd/environment/UTF-8 survive, inventory stays strict, streams drain concurrently. |
| V-4 | Windows: W1-W10 | Suspended assignment, failure unwind, nested private job, handle isolation, executable refusal and job accounting. |
| V-5 | Linux: L1-L21 | Private group custody, authenticated local protocol, separate stdin/output, stop/disconnect/failsafe and truthful uncertainty. |
| V-6 | Native bounded termination: N1-N4/N8/N9 | Ready trees are dead before return, including logical root already exited, cancellation and success with silent descendants. |
| V-7 | Repetition: N11 | Three invocations, no process/owner/control/path residue; per-invocation receipts, not one aggregate kill at test end. |
| V-8 | C487 rejection: U24, N6/N7 | Nonzero exit, FAIL line, missing trailer, wrong count, missing required name and wrong summary all remain failures. |
| V-9 | Existing callers: all methods in RunCheckpointScriptTests and BuildSlotScriptTests | Linux port: exactly 24 + 17 executions, expected PASS names/counts, zero failures/skips. Original pinned-base roster: 24 + 8 per OS. |
| V-10 | Classification: U25 plus ProcessSpawnLimitTests, TestLaneCategoryGuardTests, SlowTestTripwireTests | New spawners are Integration, one-wide limiter; U is Unit. Add exact Slow allowlist reasons for measured >=5s cases. |

#### Unit roster (U: 26 results on each OS)

| Key | Exact method | Decisive setup/assertion |
|---|---|---|
| U1 | `Default_options_keep_existing_budgets` | Ordinary wrapper supplies execution 300s and cleanup 10s; independently constructed options cannot alter another invocation. |
| U2 | `Invalid_execution_budgets_never_spawn` | Zero, negative and infinite execution budgets each reject; owner factory calls = 0. |
| U3 | `Invalid_cleanup_budgets_never_spawn` | Same three cleanup cases, valid execution budget; owner factory calls = 0. |
| U4 | `Launch_consumes_execution_deadline` | Partially allocated owner with launch completion held; original deadline starts cleanup and preserves launch diagnostics. |
| U5 | `Root_exit_uses_execution_deadline` | Both readers EOF, root pending -> timeout at original deadline. |
| U6 | `Stdout_uses_execution_deadline` | Root/stderr complete, stdout pending -> timeout; no unbounded wait or early success. |
| U7 | `Stderr_uses_execution_deadline` | Root/stdout complete, stderr pending -> timeout independently. |
| U8 | `Root_exit_does_not_reset_deadline` | Root completes at 80% of budget, one reader held; timeout at 100%, not 180%. |
| U9 | `Reader_fault_starts_cleanup_promptly` | Independently fault stdout then stderr with other tasks pending; preserve exact fault and enter cleanup before execution expiry. |
| U10 | `Timeout_preserves_partial_output_and_cleanup_errors` | Both buffers contain markers, kill/drain subsequently fail; primary is TimeoutException naming harness/case/budget and both partial markers plus secondary errors. |
| U11 | `Caller_cancellation_preserves_token` | Cancellation before spawn and after readiness are OperationCanceledException with exact caller token; pre-canceled case never spawns. |
| U12 | `Cleanup_uses_fresh_token` | Cancel caller while root waits; owned termination receives a non-canceled, independently expiring token and completes. |
| U13 | `Cleanup_has_one_total_deadline` | Kill, death-observation and drain consume portions whose sum exceeds cleanup budget; total stops at original cleanup deadline, not one budget per step. |
| U14 | `Stuck_reader_is_closed_at_cleanup_deadline` | Fake read ignores cancellation; close endpoint, bounded completion and explicit missing-EOF diagnostic. Release/fault the fake afterward and observe task fault. |
| U15 | `Stuck_control_write_is_bounded` | Fake stop write never completes; remaining cleanup budget still bounds return and closes endpoint. |
| U16 | `Stuck_dispose_cannot_extend_cleanup_deadline` | Fake disposal/join is pending after other cleanup; bounded result, diagnostic and eventual fault observation. |
| U17 | `Cleanup_native_error_fails_success` | Otherwise valid PASS output plus access-denied/native failure fails and names operation/error; already-exited observation is a separate benign control. |
| U18 | `Unconfirmed_death_retains_paths` | Successful stop request but unknown death -> failure; invocation results/control paths retained and reported, delete calls = 0. |
| U19 | `Open_streams_delay_path_deletion` | Death confirmed with a reader held; do not delete before closing readers, including drain-expiry closure. |
| U20 | `Cleanup_deletes_only_invocation_paths` | Sibling directory and sentinel remain byte-identical; only canonical invocation results/control paths are passed to deletion. |
| U21 | `Primary_start_read_and_assertion_errors_are_preserved` | Partial-launch, read and inventory exceptions independently survive secondary terminate/drain/delete faults with their identity/stack and secondary diagnostics. |
| U22 | `Repeated_cleanup_is_idempotent` | Concurrent cleanup entry plus dispose perform termination/resource release once; no duplicate delete, dispose or exception replacement. |
| U23 | `Deterministic_completion_deadline_boundary` | All evidence before deadline succeeds; incomplete evidence when deadline wins times out. Drive ordered barriers; a simultaneous native tie need not pick a fixed winner. |
| U24 | `Inventory_failures_remain_failures` | Individually invalidate exit code, FAIL line, trailer, exact count, required name, summary with all others valid; each fails its real wrapper assertion. |
| U25 | `New_spawning_classes_take_process_limiter` | Reflect N/W/L for Integration and assembly-local limiter; U for Unit. Also verify native fixture script is ASCII. |
| U26 | `All_terminal_paths_explicitly_terminate_owner` | Success, timeout, cancellation, read fault and partial launch with an owner each request exactly one termination before disposal; root liveness does not gate it. |

#### Common native roster (N: 11 results on each OS)

| Key | Exact method | Decisive assertion beyond the fixture contract |
|---|---|---|
| N1 | `Live_root_timeout_kills_root_child_and_grandchild` | Ready live tree times out <=10s; explicit stop before disposal; all three identities dead. |
| N2 | `Exited_root_with_stdout_holder_times_out_and_kills_descendant` | Observed root exit and held stdout before timeout; explicit owner stop even though root is dead; child/grandchild dead. |
| N3 | `Exited_root_with_stderr_holder_times_out_and_kills_descendant` | Same independent proof with stderr held and stdout EOF. |
| N4 | `Caller_cancellation_kills_tree_before_returning` | Cancel only after readiness; exact caller token and no survivors on return. |
| N5 | `Passing_case_preserves_inventory_and_argument_boundaries` | Exact C487 trailer/count/names accepted; structured argument receipt preserves empty argument, spaces, quotes, trailing backslash, Unicode, cwd and inherited nonsecret environment marker. |
| N6 | `Nonzero_exit_preserves_output_and_cleans_results` | Exit 37 and both stream markers survive in failure; cleanup/path deletion finish. |
| N7 | `Bad_pass_inventory_still_fails` | Exit zero but missing required name fails the inventory assertion after successful cleanup. |
| N8 | `Completed_root_with_silent_descendant_releases_owner` | Root/both readers complete with live child; explicit owner stop precedes disposal and successful return; child is then dead. |
| N9 | `Root_exit_racing_deadline_still_cleans_owner` | Release root at a recorded deadline boundary; either allowed outcome, owner/descendants always gone. U23 pins deterministic deadline decisions. |
| N10 | `High_volume_on_both_streams_completes` | Both >=1 MiB streams and terminal markers arrive under the same deadline with valid PASS inventory. |
| N11 | `Repeated_timeouts_do_not_leak_owners` | Three ready invocations independently satisfy N1; exact owned process/handle/endpoint inventory empty after each, no emergency sweep survivors. |

#### Windows roster (W: 10 results, Windows only)

| Key | Exact method | Decisive setup/assertion |
|---|---|---|
| W1 | `Child_is_assigned_before_first_instruction` | At pre-resume barrier, root is suspended and a member of the private job; first Resume follows successful Assign. |
| W2 | `Assignment_failure_never_resumes_child` | Inject Assign failure; no Resume attempt/start marker, suspended handle signals exit and all partial handles close. |
| W3 | `Resume_failure_terminates_suspended_child` | Inject Resume failure after assignment; propagate error, explicit termination attempt precedes job close, root dead and no owned handle residue. |
| W4 | `Closing_private_job_kills_owned_tree` | Directly exercise owner safety-net close with a ready tree and no explicit TerminateJobObject; all owned processes exit. Query actual kill-on-close limit before close. |
| W5 | `Nested_job_timeout_kills_owned_descendants_only` | Test-owned outer job holds target and independent sentinel; private job times out, target dies, sentinel stays alive. Observe flags: no CREATE_BREAKAWAY_FROM_JOB, BREAKAWAY_OK or SILENT_BREAKAWAY_OK. |
| W6 | `Private_job_handle_is_not_inherited` | Query job handle inheritance and child's attempted handle access; it has no copy capable of retaining the private job. |
| W7 | `Only_standard_handles_are_inherited` | Add a deliberately inheritable unrelated event handle; child cannot use it; actual startup list contains only child stdin/stdout/stderr, excludes read ends. |
| W8 | `Parent_closes_child_pipe_write_handles` | Once child closes outputs, genuine EOF arrives while parent/owner remain alive; both parent writer copies are closed. |
| W9 | `App_execution_alias_is_refused_before_launch` | Resolver seam supplies an App Execution Alias/reparse launch shape; actionable refusal and zero CreateProcess attempts; real executable control succeeds. No MSIX installation dependency. |
| W10 | `Job_accounting_must_confirm_no_active_processes` | Intercept termination success without killing a ready member, and separately fail accounting query; neither permits cleanup success/deletion. Query real private-job membership first; fixture finally terminates the test job. |

#### Linux roster (L: 21 results, Linux only)

| Key | Exact method | Decisive setup/assertion |
|---|---|---|
| L1 | `Supervisor_pins_group_after_root_exit` | Real root exits; matching live supervisor remains PID=PGID=SID, descendant still in that group and holding output; owner stops it afterward. |
| L2 | `Invalid_group_handshake_never_starts_harness` | Actual validator independently rejects wrong nonce, PID, start ticks, PGID, SID, PGID -1/0/1 and caller's own group. No start authorization/child marker/unsafe signal. Matching frame succeeds. |
| L3 | `Setsid_failure_refuses_child_launch` | Inject setsid failure before child creation; propagate refusal and native error, zero pwsh starts. Valid real launch obtains a private session. |
| L4 | `Start_authorization_requires_matching_nonce` | Hold supervisor at start-read barrier; send wrong authorization, require no child marker and bounded refusal. Matching nonce control reaches readiness. |
| L5 | `Missing_start_authorization_never_launches_child` | Hello received but no start sent, then disconnect before authorization; no root is ever launched, supervisor exits. |
| L6 | `Malformed_control_frames_are_refused` | Actual decoder receives oversized length (configured maximum + 1), truncated frame and malformed payload independently; bounded refusal, no child launch/unsafe signal. Oversized input gets a size-specific error before a payload read/allocation attempt; an I/O recorder caps any attempted oversized allocation safely. Include exact maximum valid frame control. |
| L7 | `Root_exit_receipt_requires_matching_nonce` | Root still executing; inject exit frame with wrong nonce but correct root identity; host must not complete root wait. Real matching receipt after gate release completes it. |
| L8 | `Root_exit_receipt_requires_matching_identity` | Independently alter root PID and start ticks, matching nonce; no fabricated completion or deletion. |
| L9 | `Stop_requires_matching_nonce` | Ready group ignores/rejects foreign stop without signaling; matching stop terminates it. Assert foreign frame did not request a signal before releasing matching control. |
| L10 | `Group_stop_leaves_unrelated_process_alive` | Real ready target group is killed via its confirmed negative PGID; independently owned outside sentinel stays live. Signal-boundary recorder also tests invalid selectors without forwarding them. |
| L11 | `Supervisor_closes_its_output_writers` | Root finishes with no holders; stdout and stderr reach EOF while supervisor remains alive waiting for stop. Neither stream includes control frames. |
| L12 | `Control_disconnect_kills_group` | Close real host control endpoint after readiness with no stop request; group dies within cleanup 2s + 1s observation slack, strictly before failsafe due time. Require >3s left until failsafe at disconnect; insufficient readiness margin is fixture failure. |
| L13 | `Failsafe_kills_group_with_connected_idle_controller` | Drive real supervisor directly, keep duplex channel open, suppress all host cleanup/stop sends and stall control reads; absolute execution+cleanup deadline kills entire group by <=10s. No EOF/stop can supply the verdict. |
| L14 | `Lost_supervisor_reports_cleanup_failure` | After root exit and descendant readiness, fixture kills only its retained supervisor identity; host reports owner lost, retains paths and records remaining child, never infers empty group from supervisor exit. |
| L15 | `Lost_identity_never_signals_saved_group` | Feed stale start identity/foreign replacement after unexpected supervisor loss; group-signal recorder sees zero attempts against saved PGID, failure includes identity mismatch. Native wrong-group kill is interlocked. |
| L16 | `Unresponsive_supervisor_stop_is_bounded` | After readiness with >3s left until failsafe, cancel the caller while the identified supervisor's command-reader loop stalls. Blocked write cannot exceed cleanup remainder; caller cancellation retains explicit missing-receipt diagnostics, independent failsafe later removes group. Finish observation before outer limit. |
| L17 | `Unreadable_group_membership_is_not_empty` | Inject access-denied/unreadable member state during death census with root/owner exit observed; report unknown accounting, retain paths, never report zero. |
| L18 | `Zombie_members_are_not_executing` | Observed group-member snapshots independently contain R/S/T/Z states; R/S/T forbid clean completion, terminated Z does not. A real direct child is held at a barrier: no root-exit receipt before release/observed exit, then await/reap and verify absence. No deliberate unreapable process is needed. |
| L19 | `Stop_ack_waits_for_group_exit` | Fake ACK arrives while real known descendant remains executing; invocation cannot succeed/delete paths until group/streams actually end. Delay group stop behind a fixture barrier, then release it. |
| L20 | `Failsafe_includes_time_waiting_for_start` | Release start authorization at owner-launch +2s, then require tree readiness within the original 5s execution budget. With no stop/disconnect, require group death by owner-launch +8s (7s failsafe plus 1s observation slack), before a reset-at-start deadline could fire at >=9s. |
| L21 | `Root_stdin_is_separate_from_control_pipe` | Fixture root reads a nonce-tagged stdin payload while stop/exit frames use duplex pipe; exact stdin payload contains no protocol bytes and control flow still completes. |

### Guards the regression

| ID | Regression and decisive executable assertion |
|---|---|
| R-1 | Root-only timeout or post-exit hangs: U4-U8, N1-N3 require bounded completion with the named incomplete component. |
| R-2 | Dispose-only, root-liveness-gated or success-path leaks: U26, N2/N3/N8 and N11 require explicit owner termination and independently observed death. |
| R-3 | Canceled cleanup or fresh per-step budgets: U12-U16, N4, L16 require fresh token and one total bound. |
| R-4 | Cleanup hides failure/partial evidence: U10/U11/U17/U21 require original outcome and useful secondary diagnostics. |
| R-5 | ACK/root exit/unknown accounting authorizes deletion: U18-U20, W10, L14-L19 require complete evidence or retained invocation paths. |
| R-6 | Changed C487 or argv contract: U24, N5-N7/N10 plus the 32 existing callers fail at the original named assertion. |
| R-7 | Windows pre-assignment execution, failure continuation, inheritance or breakaway: W1-W9 assert native order/limits/handles and no unowned launch. |
| R-8 | Linux guessed/foreign identity or malformed protocol: L2-L9/L15 assert no authorization, fabricated completion or unsafe signal. |
| R-9 | Supervisor released on root exit or retaining pipe writers: L1/L11 and N2/N3 prove persistent group ownership and genuine EOF independently. |
| R-10 | Disconnect and failsafe masking each other: L12 finishes before failsafe; L13 keeps channel open and suppresses host stop; L20 consumes startup time. |
| R-11 | Shared runner/parallel fixture/slow-lane drift: U25 and existing six classification results; independent fixture cleanup is checked before any rescue action. |

### Guard inventory

**70 guards, 70 distinct PC mappings, missing = 0, duplicate PC mappings = 0.**
Each independently bypassable reader, nonce/identity field, inheritance limit and
termination path has its own control. V/R also checks unchanged compatibility;
unchanged C487 assertion internals are not new custody guards.

| Guard | Plan reference and invariant | Control |
|---|---|---|
| G-1 | D-1: execution budget is finite and positive | PC-1 |
| G-2 | D-1: cleanup budget is finite and positive | PC-2 |
| G-3 | D-1: launch consumes the original execution deadline | PC-3 |
| G-4 | D-1: logical root exit is deadline-bound | PC-4 |
| G-5 | D-1: stdout EOF is deadline-bound | PC-5 |
| G-6 | D-1: stderr EOF is independently deadline-bound | PC-6 |
| G-7 | D-1: root exit cannot restart the execution budget | PC-7 |
| G-8 | D-1: stdout faults are observed before other tasks complete | PC-8 |
| G-9 | D-2: timeout remains primary with partial output/secondary evidence | PC-9 |
| G-10 | D-2: caller cancellation retains caller-token identity | PC-10 |
| G-11 | D-2: cleanup is independent of caller cancellation | PC-11 |
| G-12 | D-2: every terminal path explicitly terminates its owner | PC-12 |
| G-13 | D-2: owner termination does not depend on root liveness | PC-13 |
| G-14 | D-2: successful completion releases silent descendants | PC-14 |
| G-15 | D-2: cleanup has one total budget | PC-15 |
| G-16 | D-2: an uncooperative reader cannot prevent bounded drain | PC-16 |
| G-17 | D-2/D-4: control delivery is bounded by cleanup remainder | PC-17 |
| G-18 | D-2: disposal cannot introduce an unbounded join | PC-18 |
| G-19 | D-2: native cleanup errors cannot turn valid output into success | PC-19 |
| G-20 | D-2: unconfirmed death forbids path deletion | PC-20 |
| G-21 | D-2: streams close before path deletion | PC-21 |
| G-22 | D-2: deletion is confined to invocation-owned paths | PC-22 |
| G-23 | D-2: primary start/read/assertion errors survive secondary cleanup errors | PC-23 |
| G-24 | D-2: repeated cleanup is idempotent | PC-24 |
| G-25 | D-3: executable is created suspended | PC-25 |
| G-26 | D-3: failed assignment refuses execution and unwinds | PC-26 |
| G-27 | D-3: failed resume terminates the suspended root | PC-27 |
| G-28 | D-3: private job has kill-on-close safety net | PC-28 |
| G-29 | D-3: no CreateProcess breakaway request | PC-29 |
| G-30 | D-3: child cannot inherit the ownership job handle | PC-30 |
| G-31 | D-3: startup inheritance is restricted to standard handles | PC-31 |
| G-32 | D-3: parent closes its copy of child stdout writer | PC-32 |
| G-33 | D-3: brokered execution aliases are refused before launch | PC-33 |
| G-34 | D-2/D-3: native job accounting, not kill ACK, proves death | PC-34 |
| G-35 | D-4: setsid failure cannot launch in the inherited group | PC-35 |
| G-36 | D-4: hello nonce matches invocation | PC-36 |
| G-37 | D-4: hello PID matches observed supervisor | PC-37 |
| G-38 | D-4: hello start ticks match observed supervisor identity | PC-38 |
| G-39 | D-4: hello PGID matches observed private group | PC-39 |
| G-40 | D-4: hello SID matches observed private session | PC-40 |
| G-41 | D-4: invalid group selectors <=1 are rejected | PC-41 |
| G-42 | D-4: caller's own group is never authorized | PC-42 |
| G-43 | D-4: start authorization nonce is checked by recipient | PC-43 |
| G-44 | D-4: frame size is bounded before allocation/read | PC-44 |
| G-45 | D-4: child launch requires explicit start authorization | PC-45 |
| G-46 | D-4: root-exit receipt is correlated by invocation nonce | PC-46 |
| G-47 | D-4: root-exit receipt matches logical root PID | PC-47 |
| G-48 | D-4: stop nonce is checked by recipient | PC-48 |
| G-49 | D-4: supervisor signals only its own confirmed negative PGID | PC-49 |
| G-50 | D-4: supervisor persists after logical root exit | PC-50 |
| G-51 | D-4: supervisor closes stdout writer after root launch | PC-51 |
| G-52 | D-4: control disconnect triggers group termination | PC-52 |
| G-53 | D-4: independent failsafe runs without control progress | PC-53 |
| G-54 | D-4: unexpected supervisor exit is not empty-group proof | PC-54 |
| G-55 | D-4: lost identity never permits blind saved-PGID signaling | PC-55 |
| G-56 | D-4: unresponsive supervisor is explicit cleanup failure | PC-56 |
| G-57 | D-4: unreadable group census is unknown, never empty | PC-57 |
| G-58 | D-4: every executing state prevents clean group accounting | PC-58 |
| G-59 | D-4: stop ACK is not final process/pipe receipt | PC-59 |
| G-60 | D-4: failsafe includes time before start authorization | PC-60 |
| G-61 | D-4: root stdin is separate from supervisor control | PC-61 |
| G-62 | D-1/D-2: pre-canceled caller cannot spawn an owner | PC-62 |
| G-63 | D-1: stderr faults are independently observed promptly | PC-63 |
| G-64 | D-3: private job forbids explicit child breakaway | PC-64 |
| G-65 | D-3: private job forbids silent child breakaway | PC-65 |
| G-66 | D-3: Resume follows successful job assignment | PC-66 |
| G-67 | D-4: root-exit receipt matches logical root start identity | PC-67 |
| G-68 | D-4: direct child exit is observed before its exit receipt | PC-68 |
| G-69 | D-3: parent closes stderr writer independently of stdout | PC-69 |
| G-70 | D-4: supervisor closes stderr writer independently of stdout | PC-70 |

### Positive controls

Each PC uses the exact method resolved by its U/N/W/L roster key above, changes
implementation code only, and must compile. The expected failure below is an
assertion in that method, not a fixture-readiness failure, build failure, zero-test
run, runner timeout or emergency rescue. Unit fault injection drives the actual
coordinator; Linux framing/Windows syscall injection drives the actual adapter.
No mutation edits the assertions, fixture watchdog, emergency cleanup or signal
interlock. `Both` requires one cycle on each OS at the same landed source tip.
Common coordinator controls run on Linux; both ordinary lanes execute the same
coordinator roster. Windows and Linux adapter controls run on their native OS.

| PC | OS | Exact roster method | Compiling implementation mutation | Required red assertion |
|---|---|---|---|---|
| PC-1 | Linux | U2 | Remove execution-budget validation while retaining cleanup validation | factory calls stay zero for each invalid execution value. |
| PC-2 | Linux | U3 | Remove cleanup-budget validation while retaining execution validation | factory calls stay zero for each invalid cleanup value. |
| PC-3 | Linux | U4 | Create the execution deadline only after owned launch returns | held partial launch must finish as timeout at the original deadline. |
| PC-4 | Linux | U5 | Omit root-exit completion from the timed wait | pending root cannot produce success and must time out. |
| PC-5 | Linux | U6 | Remove stdout completion from the shared timed wait | pending stdout must time out, never return success. |
| PC-6 | Linux | U7 | Remove stderr completion from the shared timed wait | pending stderr must time out, never return success. |
| PC-7 | Linux | U8 | Reset execution deadline when logical root exits | timeout at original 100%, not 180%, of budget. |
| PC-8 | Linux | U9 | Exclude stdout fault from first-fault detection, leaving final aggregation intact | stdout-fault subcase enters cleanup before execution expiry. |
| PC-9 | Linux | U10 | Return successful inventory after catching deadline expiry | captured result is TimeoutException with required diagnostics. |
| PC-10 | Linux | U11 | Throw OperationCanceledException with CancellationToken.None | exception.CancellationToken equals supplied caller token. |
| PC-11 | Linux | U12 | Pass the canceled caller token to owned termination | cleanup token is initially non-canceled and termination completes. |
| PC-12 | Linux | U26 | Remove the common owner-termination call, leaving disposal intact | one termination before dispose in every terminal-path subcase. |
| PC-13 | Both | N2 | Guard owner termination with logical root HasExited == false | explicit owner-stop receipt exists after observed root exit; descendants dead before return. |
| PC-14 | Both | N8 | Bypass explicit owner cleanup on successful output; retain final dispose | explicit owner stop precedes disposal and successful return. |
| PC-15 | Linux | U13 | Allocate a fresh full cleanup budget at each cleanup phase | completion cannot exceed the one original cleanup deadline. |
| PC-16 | Linux | U14 | Replace bounded drain/close with unconditional await of reader task | method returns by cleanup bound and stuck reader is closed. |
| PC-17 | Linux | U15 | Await stop write without deadline or abandonment | completion by cleanup bound, missing-control diagnostic and endpoint closure. |
| PC-18 | Linux | U16 | Await the pending dispose/join without remaining-budget bound | dispose cannot delay the bounded result. |
| PC-19 | Linux | U17 | Swallow native termination error in otherwise successful path | cleanup result fails and names the native operation/error. |
| PC-20 | Linux | U18 | Allow deletion when stop was requested but death remains unknown | zero delete calls and retained paths in failure. |
| PC-21 | Linux | U19 | Move deletion ahead of reader close/drain completion | no delete event before final stream closure. |
| PC-22 | Linux | U20 | Pass the invocation directory's parent to recursive deletion instead of the exact path | sibling sentinel and canonical delete-target assertions fail. |
| PC-23 | Linux | U21 | Rethrow the secondary cleanup exception in place of the primary | same primary exception/stack remains, secondary detail attached. |
| PC-24 | Linux | U22 | Remove the once-only cleanup gate | terminate/close/delete counters are exactly one under concurrent calls. |
| PC-25 | Windows | W1 | Remove CREATE_SUSPENDED from CreateProcess flags | creation receipt includes suspension before assignment. |
| PC-26 | Windows | W2 | Continue to Resume after Assign reports failure | zero Resume attempts and no first-instruction marker. |
| PC-27 | Windows | W3 | Skip explicit termination on Resume failure, keeping final job close | explicit termination precedes job close; root handle signals exit and all owned handles release. |
| PC-28 | Windows | W4 | Clear JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | native job-limit assertion and close-only tree-death assertion. |
| PC-29 | Windows | W5 | Add CREATE_BREAKAWAY_FROM_JOB to launch flags | launch receipt forbids breakaway, even if outer-job setup would allow it. |
| PC-30 | Windows | W6 | Make the private job handle inheritable | native handle inheritance flag is false; child cannot retain job. |
| PC-31 | Windows | W7 | Use unrestricted inheritable handles instead of the explicit startup list | unrelated inheritable event is unusable in child; whitelist matches stdio only. |
| PC-32 | Windows | W8 | Keep parent's stdout writer open after launch | stdout EOF observed while parent/owner remain alive (stderr tested independently in fixed method). |
| PC-33 | Windows | W9 | Treat alias/reparse classification as a normal executable | CreateProcess attempts remain zero and refusal explains real-executable requirement. |
| PC-34 | Windows | W10 | Treat successful TerminateJobObject as zero active processes without querying accounting | live member or query failure prevents cleanup success/deletion. |
| PC-35 | Linux | L3 | Continue to child launch after injected setsid failure | zero child starts; launch refusal includes native failure. |
| PC-36 | Linux | L2 | Remove only hello nonce comparison | wrong-nonce frame creates no start authorization. |
| PC-37 | Linux | L2 | Remove only hello PID comparison | wrong-PID frame creates no start authorization. |
| PC-38 | Linux | L2 | Remove only start-time comparison | stale-start frame creates no start authorization. |
| PC-39 | Linux | L2 | Remove only PGID comparison | wrong-PGID frame creates no start authorization. |
| PC-40 | Linux | L2 | Remove only SID comparison | wrong-SID frame creates no start authorization. |
| PC-41 | Linux | L2 | Remove PGID lower-bound check; keep tuple/nonce checks | otherwise-matching -1/0/1 frames create no authorization or native signal. |
| PC-42 | Linux | L2 | Remove the own-group comparison | otherwise-matching own-group frame creates no authorization or native signal. |
| PC-43 | Linux | L4 | Remove supervisor's start nonce comparison | wrong start nonce produces no child-start attempt/marker. |
| PC-44 | Linux | L6 | Remove maximum-length rejection | oversized frame is rejected before payload allocation/start; recorder caps allocation safely. |
| PC-45 | Linux | L5 | Launch child immediately after sending hello | zero child-start attempts before authorization or after pre-start EOF. |
| PC-46 | Linux | L7 | Remove root-exit nonce comparison | injected foreign receipt cannot complete root wait. |
| PC-47 | Linux | L8 | Remove root-exit PID comparison | wrong root PID cannot complete root wait. |
| PC-48 | Linux | L9 | Remove stop nonce comparison | foreign stop requests zero signals before matching stop is sent. |
| PC-49 | Linux | L10 | Replace signal target -confirmedPgid with 0 | signal recorder requires exactly -confirmedPgid; unsafe attempted target is never forwarded. |
| PC-50 | Linux | L1 | Exit supervisor as soon as pwsh exit is reported | matching supervisor remains alive with recorded group while descendant holds output. |
| PC-51 | Linux | L11 | Keep supervisor stdout descriptor open after launch | stdout reaches EOF before supervisor stop; stderr remains independently asserted. |
| PC-52 | Linux | L12 | Remove disconnect/EOF handler's group-stop call | entire group dead within 3s of disconnect, before independent failsafe is due. |
| PC-53 | Linux | L13 | Disable failsafe timer while leaving stop/disconnect handling intact | connected idle controller sees group death by original execution+cleanup bound. |
| PC-54 | Linux | L14 | Treat unexpected owner exit as successful cleanup | owner-lost failure, retained paths and known descendant residue are required. |
| PC-55 | Linux | L15 | Replace identity-loss refusal with attempted kill(-savedPgid) | recorded stale-group signal attempts = 0; interlock prevents forwarding. |
| PC-56 | Linux | L16 | Return clean success after stop delivery/receipt timeout | captured result reports missing stop/death evidence within cleanup bound. |
| PC-57 | Linux | L17 | Convert member-state read failure to empty enumeration | cleanup fails and retains paths on access-denied/unknown census. |
| PC-58 | Linux | L18 | Classify stopped T-state member as terminated alongside Z | T subcase forbids clean completion; Z control is allowed. |
| PC-59 | Linux | L19 | Complete owner cleanup when stop write/ACK succeeds | completion/deletion stays pending while known member is alive. |
| PC-60 | Linux | L20 | Restart failsafe at child launch/start authorization | group dies at owner-launch deadline, not a fresh child-launch deadline. |
| PC-61 | Linux | L21 | Give the logical root the supervisor control reader as stdin | exact test stdin payload received without protocol bytes; control channel remains usable. |
| PC-62 | Linux | U11 | Remove cancellation check before owner-factory invocation | pre-canceled subcase factory calls = 0. |
| PC-63 | Linux | U9 | Exclude stderr fault from first-fault detection, leaving final aggregation intact | stderr-fault subcase enters cleanup before execution expiry. |
| PC-64 | Windows | W5 | Add JOB_OBJECT_LIMIT_BREAKAWAY_OK | queried private job limits exclude BREAKAWAY_OK. |
| PC-65 | Windows | W5 | Add JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK | queried private job limits exclude SILENT_BREAKAWAY_OK. |
| PC-66 | Windows | W1 | Move Resume call before Assign while retaining CREATE_SUSPENDED | ordered native trace requires Assign success before first Resume attempt. |
| PC-67 | Linux | L8 | Remove root-exit start-time comparison | correct PID with stale start ticks cannot complete root wait. |
| PC-68 | Linux | L18 | Report root-exit with exitCode 0 before awaiting the direct child's actual exit | no root-exit receipt while the barrier-held root is alive; release then observe/reap it. |
| PC-69 | Windows | W8 | Keep parent's stderr writer open after launch | stderr EOF observed while parent/owner remain alive. |
| PC-70 | Linux | L11 | Keep supervisor stderr descriptor open after launch | stderr reaches EOF before supervisor stop. |

#### Executable mutation commands and receipts

Use a commissioned SourceLanding snapshot at the landed Code SHA. Before any
mutation, copy the unchanged driver and its slot library under the external
evidence root named by that dispatch (same `lib/` relationship):

```powershell
New-Item -ItemType Directory -Force -Path (Join-Path $evidenceRoot 'fixed-driver/lib') | Out-Null
Copy-Item -LiteralPath scripts/run-checkpoint.ps1 -Destination (Join-Path $evidenceRoot 'fixed-driver/run-checkpoint.ps1')
Copy-Item -LiteralPath scripts/lib/build-slot.ps1 -Destination (Join-Path $evidenceRoot 'fixed-driver/lib/build-slot.ps1')
```

The following function resolves every PC's exact method from this document; it
never widens to a class or category. Invoke it for **baseline**, apply just that
row's mutation, invoke for **red**, restore the exact source bytes and refresh
their modification time, then invoke for **green**. Await the complete phase
before editing. The phase builds its own isolated output; no stale mutated DLL
can satisfy restored green. `EvidenceRoot` is the external path bound in the
Mutation brief, not a path in the source snapshot. Use a fresh attempt directory
if a phase must be retried and record the retry.

```powershell
function Invoke-C806PositiveControl {
    param(
        [Parameter(Mandatory)][ValidateRange(1,70)][int]$Pc,
        [Parameter(Mandatory)][ValidateSet('baseline','red','green')][string]$Phase,
        [Parameter(Mandatory)][string]$EvidenceRoot,
        [string]$PlanPath = 'docs/superpowers/plans/2026-09-29-card-0806-script-harness-timeout-plan.md'
    )
    $planLines = Get-Content -LiteralPath $PlanPath
    $pcPattern = '^\| PC-' + $Pc + ' \| (Linux|Windows|Both) \| ([UNWL]\d+) \|'
    $pcRows = @($planLines | Where-Object { $_ -match $pcPattern })
    if ($pcRows.Count -ne 1) { throw 'Expected exactly one PC definition' }
    $null = $pcRows[0] -match $pcPattern
    $lane = $Matches[1]
    $key = $Matches[2]
    $osLane = if ($IsWindows) { 'Windows' } elseif ($IsLinux) { 'Linux' } else { throw 'Unsupported OS' }
    if ($lane -ne 'Both' -and $lane -ne $osLane) { throw 'PC belongs to the other OS lane' }
    $methodPattern = '^\| ' + $key + ' \| \x60([A-Za-z_][A-Za-z0-9_]*)\x60 \|'
    $methodRows = @($planLines | Where-Object { $_ -match $methodPattern })
    if ($methodRows.Count -ne 1) { throw 'Expected exactly one roster method' }
    $null = $methodRows[0] -match $methodPattern
    $methodName = $Matches[1]
    $classes = @{
        U = 'ScriptHarnessProcessContractTests'
        N = 'ScriptHarnessProcessTests'
        W = 'ScriptHarnessWindowsOwnershipTests'
        L = 'ScriptHarnessLinuxOwnershipTests'
    }
    $testClass = $classes[$key.Substring(0,1)]
    $driver = Join-Path $EvidenceRoot 'fixed-driver/run-checkpoint.ps1'
    $resultsRoot = Join-Path $EvidenceRoot ('pc{0}-{1}-{2}' -f $Pc,$osLane,$Phase)
    $outputPath = 'bin-c806-pc{0}-{1}-{2}/' -f $Pc,$osLane.ToLowerInvariant(),$Phase
    pwsh -NoProfile -File $driver -Name "PC-$Pc-$Phase" `
        -Project tests/Antiphon.Tests -OutputPath $outputPath `
        -Filter "/*/*/$testClass/$methodName*" -Expect "$testClass.$methodName" `
        -MinExecuted 1 -ResultsRoot $resultsRoot
    if ($LASTEXITCODE -ne $(if ($Phase -eq 'red') { 1 } else { 0 })) {
        throw "Unexpected driver exit $LASTEXITCODE for PC-$Pc $Phase"
    }
}
# Example for the execution-budget guard; apply/restore PC-1 between phases.
Invoke-C806PositiveControl -Pc 1 -Phase baseline -EvidenceRoot $evidenceRoot
# After applying exactly PC-1:
Invoke-C806PositiveControl -Pc 1 -Phase red -EvidenceRoot $evidenceRoot
# After restoring exact bytes and refreshing the changed source timestamps:
Invoke-C806PositiveControl -Pc 1 -Phase green -EvidenceRoot $evidenceRoot
```

For each of PC-1..PC-70, save commit/OS/mutation diff, exact filter, all three
driver exit receipts, build exit 0, executed method/count (exactly 1), failed
assertion from red TRX and restored green TRX. An exit 1 alone is insufficient;
it must be the mapped assertion with readiness established. PC-13/PC-14 each
need both OS cycles, so **70 controls = 72 cycles = 216 phase executions**.
Do not count subcases as methods. Restore all source and follow the testing
owner's SourceLanding restoration/owned-output receipt contract. No mutation
or evidence artifact is committed to the plan branch.

### Out of scope

- Linux descendants deliberately escaping with setsid/setpgid, Windows brokered
  external processes, privilege changes and kernel-stuck tasks: D-5 excludes
  them; no privileged cgroup helper or production custody change is introduced.
- macOS and other platforms: this plan requires Windows and Linux receipts only;
  refuse unsupported ownership adapters rather than silently using bare Process.
- Full application/category integration suites, provider sessions, live brokers,
  runner deployment and E2E: the changed boundary is test-local process custody.
  The named existing callers cover compatibility without executing their real
  builds or reaching a live build-slot endpoint.
- Delivery durability/replay/queue crash recovery: the local helper has no durable
  queue or resumed invocation; channel loss terminates/refuses the invocation.
  Start/root-exit/stop loss and blocked recipients remain explicitly in scope.
- Concurrent application work, clock services and other process wrappers:
  no source change here. This design never kills an unknown process to validate
  its negative controls.

### Cost

All values are **estimates**, not measurements. Ordinary Code V/R is
Linux **6 + 6 + 10 = 22 minutes**, Windows **8 + 5 + 14 = 27 minutes**,
total **49 minutes** plus slot/runner queue waits. CP-1/CP-4 include one tool
bootstrap and one isolated test-graph build per OS. Unit filters cover the 26 new
contract methods and 6 named classification methods, not the whole Unit category.
Native filters cover 32 Linux and 21 Windows results; compatibility filters cover
41 on current master for the Linux port (32 per OS at the original pinned base).
No broad-suite cost is hidden in these estimates.

Allow **180-240 minutes** authoring/setup for the owner, helper, deterministic
fault seams and native fixtures; Code estimate including ordinary V/R is
**229-289 minutes**. This replaces the preliminary authoring estimate above.
Two ordinary graph builds rather than one after each of three slices on each OS
avoid four graph builds: estimated **20 minutes saved** at five minutes each.

Mutation is separately commissioned: **54 Linux-only + 14 Windows-only + 2
both-platform controls = 56 Linux cycles + 16 Windows cycles**. Each cycle has
baseline/red/restored-green, each method-scoped with Min=1 and an isolated build.
Budget Linux **56 x 3 x 2 = 336 minutes**, Windows **16 x 3 x 3 = 144 minutes**,
plus **10 minutes** snapshot/driver/evidence setup: **490 minutes PC floor**.
Phase estimates include build and the bounded selected method. No PC batching
savings are claimed: these changes share coordinator/adapter/helper files and
can mask one another. SourceLanding does not authorize unbound shard worktrees.
Combined estimated authoring + ordinary verification + subsequent PC battery is
**719-779 minutes**, excluding queue waits and defect repair. This cost is visible
to commissioning; PCs are not part of Code's ordinary closed list.

#### Checkpoint execution and roster audit

Commit S1-S3 before running the final rows. Use the Plan's slot-wrapped tool
bootstrap command above if needed, then set the exact plan and OS-specific rows:

```powershell
$PLAN = 'docs/superpowers/plans/2026-09-29-card-0806-script-harness-timeout-plan.md'
$rows = if ($IsWindows) { 'CP-4,CP-5,CP-6' } elseif ($IsLinux) { 'CP-1,CP-2,CP-3' } else { throw 'Unsupported OS' }
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c806-tool/ -- run --plan $PLAN --rows $rows --max-wait 55s
# If exit 75, set $runId to the reported run identity and repeat until terminal:
dotnet run --project tools/Antiphon.Checkpoints --no-build --property:OutputPath=bin-c806-tool/ -- wait --run $runId --max-wait 55s
```

Do not launch all six rows on one OS: the manifest has no platform selector.
The caller commissions the other OS lane at the **same Code SHA**, using live
runner discovery and the requested Platform; this TestDesign delegate does not
dispatch subdelegates. `Serial=true` also prevents separate Antiphon.Tests hosts
from overlapping, since the process limiter is assembly-local. Do not co-schedule
the Pty test project. Each row driver obtains its own slot; no enclosing lease
is held around tool waiting. Slot timeout 4 is not run; no `-NoSlot` bypass.

Require the fresh executed roster, not `--list-tests`. The checkpoint importer
extracts class tokens from Filter; Expect prose does not independently enforce
every method. Compare emitted `EXECUTED Class.Method` records to every named U/N/W/L
row, the six existing classification methods, and the 24/8 existing caller
methods inspected above. A missing/substituted/extra test, skip, early-return OS
stub or zero count is a failed checkpoint roster even if the driver exits zero.
If Code intentionally changes method expansion or names, update this design and
the counts before accepting evidence. The original ordinary total was **181
results** (96 Linux, 85 Windows). This port's Linux total is **105 results**;
Windows is excluded from this dispatch. These are executions, not assertions.

Use the required CHECKPOINT line for each row with SHA, build state, exact filter,
executed/passed/failed/skipped, TRX and reruns. Native evidence also records
readiness time, elapsed invocation time, root-exit cut, owner identity, group/job
accounting, stream states and emergency-sweep result. If a row fails, fix and
rerun that row; any additional build/test needs a stated reason. No runtime proof
is asserted by this documentation's static roster audit.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1-S3 | `tests/Antiphon.Tests -> bin-c806-linux/` | linux-unit | `/*/*/(ScriptHarnessProcessContractTests*)\|(ProcessSpawnLimitTests*)\|(TestLaneCategoryGuardTests*)\|(SlowTestTripwireTests*)/*` | V-1/V-2/V-8/V-10, R-1..R-6/R-11 | U1-U26 plus 3+1+2 classification methods: exactly 32, 0 failed/skipped | 32 | 6 | true |
| CP-2 | S1-S3 | CP-1 | linux-native | `/*/*/(ScriptHarnessProcessTests*)\|(ScriptHarnessLinuxOwnershipTests*)/*` | V-3/V-5/V-6/V-7/V-8, R-1..R-6/R-8/R-9/R-10 | N1-N11 plus L1-L21: exactly 32, 0 failed/skipped | 32 | 6 | true |
| CP-3 | S1-S3 | CP-1 | linux-existing-callers | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | V-9, R-6 | All 24+17 existing methods: exactly 41, 0 failed/skipped | 41 | 10 | true |
| CP-4 | S1-S3 | `tests/Antiphon.Tests -> bin-c806-windows/` | windows-unit | `/*/*/(ScriptHarnessProcessContractTests*)\|(ProcessSpawnLimitTests*)\|(TestLaneCategoryGuardTests*)\|(SlowTestTripwireTests*)/*` | V-1/V-2/V-8/V-10, R-1..R-6/R-11 | U1-U26 plus 3+1+2 classification methods: exactly 32, 0 failed/skipped | 32 | 8 | true |
| CP-5 | S1-S3 | CP-4 | windows-native | `/*/*/(ScriptHarnessProcessTests*)\|(ScriptHarnessWindowsOwnershipTests*)/*` | V-3/V-4/V-6/V-7/V-8, R-1..R-7 | N1-N11 plus W1-W10: exactly 21, 0 failed/skipped | 21 | 5 | true |
| CP-6 | S1-S3 | CP-4 | windows-existing-callers | `/*/*/(RunCheckpointScriptTests*)\|(BuildSlotScriptTests*)/*` | V-9, R-6 | All 24+8 existing methods: exactly 32, 0 failed/skipped | 32 | 14 | true |
