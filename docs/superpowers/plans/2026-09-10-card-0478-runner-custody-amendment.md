# CARD-0478: Runner custody amendment

Date: 2026-09-10. Stage: Plan with the requested TestDesign amendment folded in.
Base inspected: `4fbb8e77`; blocked Code findings: `10140e3d`, task `62229bff`.
This is an implementation and verification design, not a claim that custody has shipped.

This amendment is normative for D-6/S3 of
[the original plan](2026-09-10-card-0478-mutation-after-land-plan.md).
It authorizes the runner/PTY/transport/schema extension requested by the blocked
Code handoff. Implement it with S1-S5; the original release and post-land Mutation
requirements remain. The original 182 controls stay pending. This document adds
48 controls, G-183..G-230 / PC-183..PC-230: **230 planned PCs total**.

## Ground truth

| Inspected boundary | Current behavior | Required change |
|---|---|---|
| Code's findings at `10140e3d` | Task/session terminal state, a kill attempt, worker manifests and process snapshots cannot prove command descendants exited. RepositoryChildJournal covers application-owned Git/verifier children only. | Produce independent OS-container evidence in the runner backend; keep repository journals as an additional exclusion. |
| `ModernConPtyConnection.Spawn` | Creates the job first, but calls CreateProcessW without suspension and assigns the running child afterward. | Close the creation-to-assignment race; the old spawn sequence cannot advertise custody. |
| `PtySession.cs` / `WindowsJobObject.cs` | Porta does not expose its job; the optional memory job attaches after launch and enumerates a bounded PID list. | Neither is the custody oracle. Query the retained launch job's accounting; nested memory jobs remain subordinate. |
| `Win32ProcessSpawner.StartDetachedWithFallback` | The host intermediary requests job breakaway, then retries plain detached on access denied. Its CWD is inherited. | Under an enclosing Mutation custody job, keep the successful plain-detached child contained. Test this actual path and give sourced hosts an external CWD; detachment must not be confused with escape. |
| `PtyHostManifest` / `HostSession.ObserveExitAsync` / `Shutdown` | Session-keyed manifest records root exit; Shutdown deletes it; linger expiry exits the host. | Separate durable execution-keyed custody records that survive manifest deletion and session reuse. Preserve the observer until proof is durable or record loss as unknown. |
| `SessionRunnerRuntime.RunnerSession` | Root exit, vanished root and adoption can trigger host shutdown. Kill may return immediately on HasExited. | Root exit remains a session event, but does not complete custody or dispose its observer. |
| `RunnerLaunchRequest` / `LaunchMessage` | No accepted application generation or verification execution binding travels to the native job. | Carry a persisted immutable binding to host launch, adoption and custody reads. |
| `IWorktreeRemovalEvidence` / `WorktreeRemovalEvidence` | Reloads publication/task evidence from an independent DbContext. | Add verification-specific evidence including all execution receipts and the cleanup seal, without borrowing Code's publication-removal authority. |
| Existing native tests | PtyKillProcessTreeTests checks known child PIDs and directory handles; PtyHostAdoptionTests models runner replacement; HostHarness runs host protocol in-process. | Reuse setup, but add orphaned descendants, atomic launch, receipt persistence and actual process-crash tests. Existing tests alone do not establish the new contract. |

The scratch patch at
`C:\Antiphon\worktrees\card-task-62229bff\.antiphon\task-62229bff-unfinished.patch`
is not verified implementation. Reuse individual S1/S2 ideas only after review.
Its cleanup reader is superseded; do not apply the patch wholesale.

## Decisions

### D-10: A closed process container is the authority

Support `windows-job-v1` only for a **fresh detached PtyHost using the resolved
ModernConPty backend on Windows with job-list-at-creation support**. Provider kind
(ClaudeCode/Codex/Grok) is a separate axis. A kind name or requested modern setting
does not prove the host actually supports tracking.

The host, as part of the runner runtime, owns one dedicated, non-inheritable,
unnamed Job Object for the provider root and its OS descendants. It owns the
handle continuously from before native creation through the final observation.
The job has KILL_ON_JOB_CLOSE, neither BREAKAWAY_OK nor SILENT_BREAKAWAY_OK, and is
never reused or assigned additional roots. Nested memory/test jobs must retain
the custody job as an ancestor; memory limit 0 must still have custody.

Use `PROC_THREAD_ATTRIBUTE_JOB_LIST` alongside the pseudoconsole attribute in
`STARTUPINFOEX`, with `CREATE_SUSPENDED`. Check every native result, verify
membership/configuration, persist Tracking, then resume the root. No
run-then-assign fallback. Failure before resume prevents provider execution;
failure after native start intent retains uncertainty unless the owning live
job can be sealed and observed empty. A crash with an unresumed child still has
kill-on-close containment. The host's own CWD, console plumbing, ANSI logs and
custody files are outside the snapshot; host/runner readers of the snapshot
must be drained separately before removal.

The existing host intermediary's access-denied-to-plain-detached retry remains
valid when test runners are themselves descendants of Mutation: it retains
membership in the outer custody job. Do not enable breakaway on that job to
make a nested test host outlive the Mutation worker. This is distinct from a
forbidden fallback to an untracked PTY backend or run-then-assign launch.

Windows supports assigning a job list at creation; ordinary child creation
inherits job membership, while breakaway configuration changes that behavior.
This is the basis for the design, not a native test result.
[Creation attributes](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute),
[Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

**Coverage boundary:** detached/new-console children and orphaned grandchildren
created within that OS job remain covered even with relative command lines and
exited ancestors. Local child MCP servers are covered when actually launched in
that job. Work delegated to a pre-existing external broker (WMI process-create,
Task Scheduler, services, elevation, remote executors, or containers with a host
worktree mount) is not an OS descendant of the contained root. A job is not an
adversarial security sandbox. The sourced Mutation recipe must use local
inherited process execution, fresh build servers (`MSBUILDDISABLENODEREUSE=1` and
`UseSharedCompilation=false` for its builds), and no externally hosted executor
with snapshot access. A configured broker/remote execution lane is
`UnsupportedBackend`; observed or suspected out-of-job work makes custody
`Unknown` with `external_execution_untracked`. It can never be cleared by a
worker's all-awaited assertion. This release does not promise to prove arbitrary
work submitted to other services; such runs retain their tree pending separate
custody design/recovery. Do not mislabel this limit as complete machine-wide
process tracking.

Rejected: process census or command-line matching; root PID/start-time tests;
a worker-written `commandsAwaited` manifest; the optional memory job; a terminal
status; a successful kill/transport ACK; always refusing even supported clean
runs. The last preserves safety but fails the positive cleanup acceptance.

### D-11: Bind every launch attempt before it can run

Add a narrow domain entity `VerificationExecution` (one row per accepted launch
attempt), additive nullable task metadata, and CLI-generated EF migration.
This is launch custody history, not a new verification workflow engine.

Task metadata includes custody contract version, execution-set revision and
the nullable cleanup seal. Each execution stores B, durable launch-boundary
state, native identity, imported receipt bytes/digest and import time. Foreign
keys retain task/source relations; legacy rows remain null and never acquire
proof by migration. Persist a monotonic runner-call intent before any runner
I/O, so a missing response cannot be mistaken for a never-enqueued attempt.

Its immutable binding `B` contains:

- `ExecutionId` (fresh GUID, idempotency key), Mutation task GUID, source operation
  O and full L.
- `SessionId` and `AcceptedStartedAt` from the committed session reservation,
  normalized to PostgreSQL microsecond precision. This is the accepted application
  generation, **not** runner-local wall time, native process start time or the
  provider's resumable conversation ID.
- Canonical repository/common Git directory, worktree path/Git directory,
  managed branch and the recorded creation identity.
- `CustodyContractVersion=1` and the selected execution backend.

Use immutable structural value objects for task/source identity, accepted
generation and creation coordinates. Each equality is one indivisible
comparison in the receipt validator; its test varies each constituent field
independently. If Code instead introduces separate independently bypassable
guards, expand the guard/PC inventory rather than claiming the grouped control
tested them all.

Persist B in the reservation transaction before launch enqueue or runner I/O.
Append attempts; never overwrite the earlier row when a retry changes SessionId
or generation. A unique execution key and a unique task/session/generation
binding prevent duplicate records. Every launch path for a sourced task,
including recovery, must consume this persisted binding; no reconstruction from
the latest session row. Carry B in typed launch options/spec, HTTP contracts,
host launch and manifest. Neither public task-create JSON nor the worker's
environment/report can set custody outcomes.

The runner durably records a start intent before invoking native creation. The
host records `ContainerId` (new random GUID for this job), `HostInstanceId`,
native host/root identity, and stable `RunnerStoreId`. Native identities are
diagnostics/adoption checks, not the exit verdict. Runner restart keeps its
store ID; replacing or losing that store is not an empty runner.

A replay with the same ExecutionId and identical B retrieves/adopts the same
attempt. Changed B is `verification_custody_identity_mismatch`; an indeterminate
start intent cannot spawn again. The runner fences SessionId reuse while an
unresolved custody attempt exists, including a legacy launch request that omits
B. A new accepted generation gets a new ExecutionId and fresh host/job only
after the previous attempt is reconciled. Failed attempts remain required by
cleanup even after a later attempt completes.

Sourced workers never enter the warm pool or become standing owners at release.
Use the existing authorized terminal release/stop path, recording custody
separately. No new kills are triggered by cleanup, a stall or a missing receipt.

### D-12: Seal, observe, persist, then acknowledge

Expose typed custody state independently of SessionStatus and TaskStatus:

| State / reason | Meaning | Cleanup authority |
|---|---|---|
| `Starting` | Durable launch intent; launch outcome not yet settled | None |
| `Tracking` | Containment established; root/descendants may run | None |
| `Draining` / `descendants_running` | Producer sealed but job is nonempty, or release still pending | None |
| `Exited` | Sealed generation, successful OS accounting query of the original job at zero, host I/O drained, final receipt durably written | Eligible only with every other cleanup check |
| `NeverStarted` | Sealed attempt with positive durable evidence that no provider root was created | Eligible for that attempt only |
| `Unknown` / typed reason | Missing/corrupt/inconsistent receipt, observation error, lost observer, timeout, or untracked execution | None |
| `UnsupportedBackend` / `verification_custody_unsupported_backend` | This backend/protocol cannot establish the required tracking | None; explicit refusal and residue |

Keep failure classifications distinct. Observation/read timeouts are retryable
only while the same original handle and intact launch history survive. Lost
containment, external execution, corrupt history and unsupported tracking cannot
be upgraded by a subsequent empty observation or a fresh job. A valid final
receipt may be re-imported idempotently; it must not be reconstructed from state
summaries. UnsupportedBackend takes precedence over a generic no-start result;
it never becomes cleanup authority merely because the provider was refused.

The host serializes launch, input, job assignment, observation and sealing.
Root exit or the **existing authorized release** closes input/launch admission
for B. Persist that irreversible producer seal before certifying exit. Reject
late input and launch for B; resume is a new attempt, never an unseal. A job
freshly created at zero before the first launch is not Exited.

Observe `QueryInformationJobObject(JobObjectBasicAccountingInformation)` on the
original held handle and require successful return plus `ActiveProcesses == 0`.
The launch job includes the root, so zero also proves root exit. Completion-port
notifications may wake the observer; they are never the verdict, and periodic
queries must progress even if a notification is lost. Do not use a PID-list
length, root-exit event, or generic wait on a job handle to infer this result.
[Job accounting](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_basic_accounting_information),
[QueryInformationJobObject](https://learn.microsoft.com/en-us/windows/win32/api/jobapi2/nf-jobapi2-queryinformationjobobject),
[Completion-port guarantees](https://learn.microsoft.com/en-us/windows/win32/api/winnt/ns-winnt-jobobject_associate_completion_port).

Record termination return/error separately. A successful `TerminateJobObject`
request is not this receipt. A failed termination can later be followed by a
valid naturally empty job; a successful termination followed by nonzero/failed
observation cannot. Bounds/cancellation end waiting, not authority: retain
Draining/Unknown with the reason and allow later observation while the same
host retains the handle.

After zero, stop/drain host-owned worktree readers and output tasks; seal the
host's snapshot access. Persist a final `VerificationCustodyReceipt` containing
B, runner/host/container identities, schema, monotonic state revision,
SealedAt, observation method/time/count, I/O-drained fact and terminal
disposition. No argv, environment values or secrets. Store it under
`<SessionLogPath>\verification-custody\<ExecutionId>\` outside every worktree,
separate from the disposable session manifest. Use unique temporary files,
Flush(true)/write-through and atomic rename; errors never return Exited.
The host writes `producer-receipt.json`; the runner writes
`accepted-receipt.json` only after validating the producer's exact binding.
Mutable observation diagnostics live separately and cannot overwrite either
terminal receipt. The store is runtime-owned, not a worker artifact upload:
use the existing local trusted runner/host control boundary and permissions.
This is not protection against a malicious local administrator or same-user
process rewriting runtime state; such tampering voids the custody evidence.
A final receipt is immutable; identical replay is idempotent, conflicting
terminal bytes are an integrity error that blocks cleanup.

Runner adoption reads this ledger before admitting launches or reporting ready.
A live matching host can continue observation or resend its receipt. The runner
durably imports the final host receipt into its retained custody store before
publishing a custody change or acknowledging host shutdown. Shutdown ACK may
remove the ordinary manifest, never custody records. Existing root-exit
shutdown, vanished-root disposal and EnsureExitedHostGone paths must respect
this extra boundary for tracked attempts.

The host need not live forever: explicit stop and existing linger expiry may
end it. If it exits without the final durable proof, retain Unknown; kill-on-close,
missing manifest, PID reuse, host death or a later reboot never synthesizes
Exited. No named job is reopened/recreated and queried empty as a substitute.
An already durable valid receipt survives host death, runner/server restart and
reboot. Missing proof after host loss intentionally needs separate recovery;
there is no `-Force`/`-ConfirmDescendantsExited` shortcut on verification cleanup.

NeverStarted is deliberately narrow. Before native creation, a sealed launch
rejection/cancellation with its durable no-start transition may produce it.
A returned native creation failure may do so after the host persists that
observed failure and closes admission. An unresolved native-start intent,
missing row, dead PID or no manifest may not. Already-provisioned but
never-dispatched snapshots can clean only after the server durably cancels
their launch reservation and proves that it never crossed the runner boundary;
otherwise obtain the runner's NeverStarted receipt or retain Unknown.

### D-13: Negotiate support; never silently degrade it

Add runner feature `verificationCustodyV1` with backend support detail. Sourced
launches require it in the server and again in the runner/host. Verify actual
resolved backend in the host before provider spawn. A new runner talking to an
old host does not qualify. Extend the v1 host handshake with additive feature
advertisement (absent means no custody); add optional binding/status fields and
new negotiated custody request/reply messages. Send new discriminators only
after negotiation; existing hello/launch/exit meanings stay unchanged.
Perform the server capability check before worktree provisioning where possible;
a later host-level fallback can still leave a provisioned snapshot, which is
retained with UnsupportedBackend rather than silently launched or removed.

First-release matrix:

| Backend | Sourced launch / cleanup |
|---|---|
| New Windows detached PtyHost + actual modern ConPTY + atomic job creation | Supported only after native containment succeeds |
| Porta/inbox, including missing-redistributable fallback | Refuse sourced provider start with UnsupportedBackend |
| Herdr launched/attached/retained pane | UnsupportedBackend; do not detach, close or PID-kill a pane for cleanup |
| In-process/direct-process adapters, non-Windows, legacy runner/host/manifest | UnsupportedBackend or Unknown legacy evidence; never pretend to support custody |
| Brokered/remote executor with worktree access | UnsupportedBackend; no local job receipt can cover it |

Keep ordinary unsourced launch fallback behavior and transcript delivery
semantics intact. A supported configuration whose containment setup fails
returns `verification_custody_start_failed` and a safe no-start/unknown state,
rather than retrying uncontained. Quota/pins/provider selection are unchanged;
the caller must explicitly select an allowed supported lane or upgrade it.

Proposed authenticated runner reads:
`GET /sessions/{sessionId}/executions/{executionId}/custody` and an additive
custody summary on runner/task detail. The request identifies B's generation;
mismatch is 409, missing durable identity yields explicit Unknown, unsupported
returns the typed unsupported code/state. Proposed idempotent
`POST /sessions/{sessionId}/executions/{executionId}/seal` accepts B and closes
that attempt's launch/input admission; it does **not** terminate processes.
These are runner routes without `/api`. The server's existing runner client is
the only public cleanup evidence importer; no worker-writable receipt endpoint.

### D-14: Freeze the task's launch set before deletion

Add a durable task `VerificationCleanupSeal`: seal ID/revision, creation
identity, and the complete accepted ExecutionId set. Obtain it only for a
terminal sourced Worktree task under the same task/session admission locks
used by launch/retry/stop. Sealing atomically fences new task attempts and
snapshots that set. All launch/recovery paths check it before enqueue **and**
at runner execution; seal each already-accepted attempt at the runner and
resolve any in-flight start against that seal. A concurrent accepted launch is
either included and awaited or refused; never omitted.

Sealing is irreversible for that task/worktree even when removal fails.
Retry cleanup uses the same seal; more verification uses a **new** explicitly
commissioned task/worktree. An empty attempt set grants no authority without a
durably fenced, never-enqueued reservation history. A later successful attempt
cannot erase a prior Unknown/Unsupported/Draining attempt.

The cleanup service collects seals/receipts before acquiring the Git lease;
do not hold a repository lock across unbounded runner I/O. It imports complete
validated receipt bytes/digest and B into the DB transaction, with restricted
deletion while referenced. Event loss is recovered by polling this exact
execution route; an ACK or projection is not the persisted receipt. No forced
network dependency is needed once an immutable validated final receipt is
durably imported and the task's launch set sealed.

Under the genuine canonical repository lease, `IWorktreeRemovalEvidence`
reloads the task seal, **all** execution rows and validated terminal receipts
through a fresh context, plus creation/source/restoration evidence. Extend its
typed verification reader; keep publication reading unchanged. Require exact
task/O/L, accepted generation, runner/host/container and creation coordinates.
Any missing/unknown/unsupported record refuses. Recheck standing/pool/task
ownership and unfinished RepositoryChildJournal entries independently: the new
receipt does not clear them.

Immediately before the **first output deletion**, and again at existing final
remove/ref boundaries, reload that durable authority and fresh file/Git/
ownership state under the lease. The frozen launch set and irreversible native
seals close the new-process race; polling an owner census again would not.
Application-owned Git/cleanup children retain their existing journal protocol.
The task seal does not authorize reuse of its directory by another task.
Pending host/runner worktree reads must drain before output removal too.

Expose `verificationCleanupResidue` plus typed custody reason/execution identity
on task detail and through `delegate.ps1 -CleanupVerification`. Refusal preserves
the snapshot, branch, evidence and original publication/battery verdict.
Successful cleanup removes only the original plan's authorized coordinates.
Keep receipts and the task seal after cleanup as audit evidence; do not prune
them with ordinary manifests, audit TTL or task deletion. A later retention
policy is separate work.

## Implementation slices

S1-S5 remain the unit of delivery. S3 is expanded into these ordered dependencies,
not an independently deployable checkpoint:

| Slice | Concrete owners and work | Verification |
|---|---|---|
| S1 addition | `VerificationExecution`/task seal domain records; `AppDbContext` and CLI-generated migration; task detail. Persist reservation binding in dispatcher/launch queue at accepted generation. | V-13, new application custody tests, V-1 parity/migration |
| S2 addition | `AgentLaunchSpec`/options, `SessionRunnerHttpClient`/client seam, actual session reservation/queue adapters. Fresh nonpooled release, all-attempt history, seal checks on every launch/recovery path. | V-13/V-17; existing generation/dispatch/reply tests |
| S3a native | `ModernConPtyConnection.cs`, `PtySession.cs`, `PtyAgentRunner.cs`; new dedicated job-custody helper, native I/O seam for faults. Atomic job list, retained handle, real query, drained output. Preserve DA1, paste, teardown and nested memory behavior. | V-14; PtyCustodyTests and existing native contracts |
| S3b host/runner | `SessionRunnerContracts.cs`, `PtyHostMessages.cs`/manifest, `HostSession.cs`/`PtyHostServer.cs`/client; `PtyHostLauncher.cs`/`Win32ProcessSpawner.cs` ensure sourced host CWD is outside the snapshot; `SessionRunnerRuntime.cs`/`ISessionChild.cs`, new custody store, startup recovery and API routes. Version negotiation, persistent state and conservative unsupported arms. | V-15/V-16; HostCustodyTests, RunnerCustodyTests, restart/framing tests |
| S3c removal | `WorktreeRemovalRequest.cs`, `IWorktreeRemovalEvidence.cs`, infrastructure evidence reader, `GuardedWorktreeRemoval.cs`, cleanup service/API/CLI. Import receipt, freeze attempts, fresh authority before any deletion. | V-6/V-17; PostLandMutationCustodyTests and cleanup authority tests |
| S4 addition | Original D-8 files plus `docs/session-runtime-invariants.md`, ADR 0002 and `docs/herdr-sessions.md`/`docs/antiphon-api.md`. Document actual supported lane, receipt/status distinction, no pool, no external executor and explicit residue recovery. | V-11; existing active-contract tests plus PC-230 |
| S5 addition | Real host/runner-to-application custody acceptance and deployment record. Verify new runner/host capability and first commissioned generation's tracking, not just server health. | V-12 extended plus V-15..V-17 |

No instruction-only release, broad process census redesign, fleet kill sweep or
live backend switch. If native acceptance cannot establish containment on the
chosen supported lane, return to Plan; do not substitute text tests.

## Verification design

### Inspection

Inspected existing bodies: `PtyKillProcessTreeTests` (held directory, known
grandchild, modern/inbox and memory-job cases), `PtyHostAdoptionTests` (live and
exited-while-runner-down adoption, vanished host), `HostHarness` (unique pipe/
temp paths, in-process server), `HostSession.ObserveExitAsync/Shutdown`,
`PtyHostManifest.SaveAtomic/TryLoad`, `Win32ProcessSpawner.StartDetachedWithFallback`,
and the original TestDesign's scoped
Git/removal/DB harness contracts. Existing cases are compatibility coverage,
not the new descendant proof.

New fixtures required: a compiled Windows child helper that starts a child and
grandchild, exchanges deterministic named-event/pipe barriers, and exits its
ancestors while the final descendant lives. It records native handles/start
identity to a test supervisor for teardown; its PID list is a **test oracle**,
never runtime deletion authority. Test ordinary/DETACHED_PROCESS/new-console
children, a nested job and attempted breakaway. No paid provider, elevation,
WMI, Task Scheduler or production broker/runner is launched.

`PtyCustodyTests` belongs to `tests/Antiphon.Agents.Pty.Tests`;
`HostCustodyTests` to `tests/Antiphon.PtyHost.Tests`;
`RunnerCustodyTests` to `tests/Antiphon.SessionRunner.Tests`;
`PostLandMutationCustodyTests` to `tests/Antiphon.Tests/Application`.
Add Unit/Integration/Pty categories according to each assembly's convention and
its **assembly-local** ProcessSpawnLimit. Run these assemblies sequentially.
Application tests use isolated DB/schema, real temporary Git and a random-port
test runner for V-17. Never 17204. Use real/offset clocks, bounded barriers and
foreground ownership. Existing HostHarness disposal is not sufficient crash
evidence: add an owned out-of-process runner/host fixture with exact-handle
teardown in finally, await exit and preserve failure logs.

Native faults use an I/O seam for native create/query and durable store writes,
not interfaces for each service. Test call ordering before an injected failure
so later guards cannot mask the missing earlier check. A native success test
must use the real Win32 API, real host receipt and real importer; no test-written
Exited receipt in V-14/V-17. Seeded receipts are appropriate for explicitly
labeled consumer-policy negatives only.

### Delivery inventory

| Producer -> recipient | Durable boundary / identity | Observable receipt and recovery |
|---|---|---|
| Reservation -> runner -> host | B committed before queue/HTTP; runner start intent before native creation; host ledger before resume | V-13/V-15 read exact B from real host/store after replay and dropped ACK; no duplicate root. Worker brief still requires original V-9b's complete matching UserPrompt. |
| Host OS observation -> runner | Sealed B/container, successful zero query and drained I/O; immutable host receipt before shutdown ACK | V-15 disconnects runner, lets orphan exit, restarts and adopts; exact persisted receipt survives manifest removal. Lost wakeup recovered by polling; no UserPrompt is involved in this machine receipt. |
| Runner -> server cleanup consumer | Full receipt read from authenticated runner, validated and committed to VerificationExecution | V-17 loses event/HTTP response and crashes importer before/after commit; new context retrieves exact receipt bytes/digest and binding before first removal. Transport ACK cannot authorize deletion. |
| Cleanup outcome -> caller | Existing task residue/detail plus explicit CLI response | Read back same task seal/outcome. No new chat delivery is added; original V-9c busy/idle completion receipt matrix remains mandatory and carries custody/residue identity when available. |

Crash matrix for the first three rows: before intent, after intent/before native
create, after create/before Tracking persistence, before/after resume, root exit
with live orphan, after seal/before zero, after zero/before receipt flush, after
flush/before rename, after rename/before host ACK, after runner receipt/before
server import, and after DB commit/before response. Exercise actual killed
test host/runner processes at the native-create, pre-receipt and post-receipt
cuts; other storage cuts use fault injection plus fresh services. Repeat sweeps/
reads twice. Busy/nonempty versus already-empty hosts are both required; they
are custody states, not transcript receipt substitutes.

### Proves it works now

| ID | Exact scenario methods | Required positive and negative observations |
|---|---|---|
| V-13 | `PostLandMutationCustodyTests.C478_V13_AcceptedExecutionHistory` | Actual reservation/queue persists B before first runner call, exact microsecond generation roundtrip, duplicate request one root, distinct retry rows, seal/start race, null legacy migration. |
| V-14 | `PtyCustodyTests.C478_V14_RealDescendantContainer` | Native fast child is contained at first instruction; root/intermediate exit leaves live relative-argv grandchild and nonzero job; later natural exit produces zero. Detached/new-console/nested-job arms and actual host-intermediary breakaway-denied fallback; rejected escape; memory 0/nonzero. Process creation failures never execute helper payload. |
| V-15 | `HostCustodyTests.C478_V15_ReceiptBeforeShutdown`; `RunnerCustodyTests.C478_V15_RestartReceiptMatrix` | Real host writes immutable receipt before ACK; actual runner crash/restart retains tracking; host death before proof stays Unknown, after proof stays Exited; manifest deletion and SessionId reuse preserve prior attempt. Crash matrix above. |
| V-16 | `RunnerCustodyTests.C478_V16_UnsupportedAndObservationFailures` | Herdr/inbox/direct/non-Windows/old host/modern-fallback return explicit unsupported and zero sourced provider starts; live query error, kill failure/partial completion, timeout, malformed records and missing store never authorize. Ordinary unsourced launch remains compatible. |
| V-17 | `PostLandMutationCustodyTests.C478_V17_RealReceiptToGuardedRemoval` | Real runner/host/helper in an actual managed Git snapshot; after terminal task/root exit and surviving grandchild, cleanup makes zero destructive calls. Grandchild exits, receipt is persisted/imported, explicit cleanup then removes exactly the snapshot/ref, preserving external evidence. Run Succeeded/Failed/Canceled and server restart. No fixture-forged receipt in the positive path. |

V-6's existing matrix is amended, not replaced: successful restored terminal
runs need V-17's native receipt, terminal/owner/PID data alone still refuse.
G-89 covers live standing/pool/session ownership independently with otherwise
valid sealed receipt. G-90 covers a nonempty job even with a dead root; its
first-command observer must defeat a later Git refusal masking that guard.
G-110 reloads sealed attempt membership/receipt and owner state, not a census.
G-119 covers missing/unknown custody. G-120 proves a reused/gone PID cannot
replace the original container receipt. Preserve their existing PC IDs.

### Guards the regression

R-12: G-183..G-190 and V-13 pin durable launch binding, support and lifecycle.
R-13: G-191..G-202 and V-14 pin actual containment and observation.
R-14: G-203..G-214 and V-15/V-16 pin store/transport/restart evidence.
R-15: G-215..G-230 and V-17 pin consumer identity, frozen attempts and cleanup.
R-5/R-6 and the amended original five controls remain distinct consumer checks.
R-11/V-12 additionally require loaded server **and runner/host** custody support.

### Guard inventory and positive controls

All new guards implement D-10..D-14 in S1/S2/S3/S4/S5. Each row below defines
one guard and its same-numbered PC, exact method, compiling production defect
and decisive assertion. Prefix `A` means PostLandMutationCustodyTests, `P` means
PtyCustodyTests, `H` means HostCustodyTests and `R` means RunnerCustodyTests;
the assembly mapping above is part of each method's execution identity.

| Guard / PC | Exact method | Safety assertion | Compiling defect | Decisive assertion |
|---|---|---|---|---|
| G-183 / PC-183 | `A.C478_G183_PersistBinding` | B is committed before runner I/O | move execution-row save after StartAsync | At the first runner call an independent context reads exact B; zero calls if reservation commit fails |
| G-184 / PC-184 | `A.C478_G184_AcceptedGeneration` | Binding uses the reserved generation | replace AcceptedStartedAt with runner/current session time | Queued old generation and microsecond roundtrip retain the committed value; newer row cannot receive that launch |
| G-185 / PC-185 | `R.C478_G185_DuplicateLaunch` | One native root per ExecutionId | drop durable idempotency lookup before native create | Concurrent/replayed identical B starts exactly one root; changed B returns identity mismatch |
| G-186 / PC-186 | `A.C478_G186_AttemptHistory` | Retry preserves each earlier attempt | overwrite the prior execution row on retry | Two attempts remain with their distinct bindings and original Unknown/Exited dispositions |
| G-187 / PC-187 | `A.C478_G187_SupportAdmission` | Server requires advertised support | skip verificationCustodyV1 capability guard | Unsupported/missing feature refuses before provider launch with exact typed code; no fallback selection |
| G-188 / PC-188 | `R.C478_G188_ActualBackend` | Host validates actual resolved backend | treat requested modern as supported after forced inbox fallback | UnsupportedBackend and zero provider payload starts for fallback/inbox/Herdr/direct/legacy arms |
| G-189 / PC-189 | `A.C478_G189_NoPool` | Tracked Mutation release cannot pool the session | allow sourced session through the warm-pool release branch | PoolIdleSince remains null and release follows owned stop/custody path; task success alone gives no cleanup proof |
| G-190 / PC-190 | `R.C478_G190_RequiredBinding` | Every tracked launch requires immutable B | allow launch without B on a session fenced by a tracked attempt | Legacy/raw launch on the protected session is refused before native create; the existing binding is unchanged |
| G-191 / PC-191 | `P.C478_G191_AtomicMembership` | The child belongs to the job at creation | remove JOB_LIST from native creation, leaving later validation intact | Native-create boundary records the job attribute and first-instruction helper is already in that job; a later failed validation cannot mask this assertion |
| G-192 / PC-192 | `P.C478_G192_BreakawayFlag` | Explicit breakaway is disallowed | set JOB_OBJECT_LIMIT_BREAKAWAY_OK | Independent query of job limits has the bit clear; attempted breakaway cannot create an outside descendant |
| G-193 / PC-193 | `P.C478_G193_SilentBreakaway` | Silent breakaway is disallowed | set JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK | Native job configuration bit is clear and ordinary child is contained |
| G-194 / PC-194 | `P.C478_G194_ContainmentValidation` | Membership/setup failure blocks execution | ignore failed membership/configuration result before resume | Injected invalid containment yields zero ResumeThread calls and no helper payload marker |
| G-195 / PC-195 | `P.C478_G195_TrackingBeforeResume` | Tracking is durable before first instruction | resume before committing Tracking | Resume boundary observes durable Tracking for exact ContainerId; store failure means zero resume calls |
| G-196 / PC-196 | `P.C478_G196_OriginalJobQuery` | Only the retained custody job is queried | query the optional memory job or a fresh empty job instead | Recorded query handle is the original launch job; live orphan with no memory job prevents final receipt |
| G-197 / PC-197 | `P.C478_G197_QueryFailure` | Failed native query is unknown | map QueryInformationJobObject false to zero | Native query failure yields no Exited candidate, with observation_failed diagnostic |
| G-198 / PC-198 | `P.C478_G198_NonemptyJob` | All descendants must exit | use root HasExited instead of ActiveProcesses == 0 | Dead root plus surviving grandchild stays Draining and produces no Exited candidate |
| G-199 / PC-199 | `P.C478_G199_NotificationOnly` | Notifications do not certify exit | publish empty on ACTIVE_PROCESS_ZERO without querying | Injected stale notification while native count is nonzero produces no receipt; missing notification still progresses by query |
| G-200 / PC-200 | `P.C478_G200_ProducerSeal` | Producer closes admission before final observation | perform final query before closing launch/input gate | At query boundary gate is durably sealed; late input/assignment rejected and no post-seal root appears |
| G-201 / PC-201 | `H.C478_G201_DrainBeforeReceipt` | Host snapshot I/O finishes before receipt | persist Exited while an output/reader task is held at a barrier | No final receipt while owned I/O is pending; successful receipt follows drain and retained output bytes are complete |
| G-202 / PC-202 | `P.C478_G202_KillIsNotReceipt` | Termination result is not exit proof | set Exited candidate when TerminateJobObject returns true | Success plus nonzero count stays Draining; failed kill followed by real natural zero may complete with recorded failure |
| G-203 / PC-203 | `H.C478_G203_ReceiptFlush` | Final receipt is durable before visibility | skip Flush(true)/atomic final rename and expose temp bytes | Fault boundary has no readable final receipt/Exited response until durable store commit; restart ignores temp residue |
| G-204 / PC-204 | `H.C478_G204_ReceiptBeforeAck` | Host persists custody before its final custody reply | reply with completed custody before saving the final receipt | Independent store observer at the host reply sees exact receipt; injected save failure yields zero successful custody replies |
| G-205 / PC-205 | `R.C478_G205_HostShutdownOrder` | Root exit cannot discard the observer | retain old unconditional ShutdownHostAsync on root exit | Live orphan keeps tracked host available and zero custody shutdowns; root SessionExited may still publish |
| G-206 / PC-206 | `R.C478_G206_HostLoss` | Observer loss without proof is Unknown | map dead host/root or reboot recovery to Exited | Actual host death at pre-proof cut retains Unknown and the original intent, regardless of empty census |
| G-207 / PC-207 | `R.C478_G207_MissingJob` | No reopening an empty replacement job | create a new job for a lost ContainerId and certify its zero | Missing original handle/host refuses; native replacement-create count is zero during recovery |
| G-208 / PC-208 | `R.C478_G208_CorruptStore` | Unreadable/malformed durable state cannot vanish | skip malformed/torn/unknown-version ledger records as absent | Startup/read exposes Unknown and fences that identity; no second provider launch or cleanup authority |
| G-209 / PC-209 | `R.C478_G209_StoreIdentity` | Receipt origin is the original runner store | accept a receipt from a different RunnerStoreId | Changed/replaced store is identity mismatch; accepted receipt and execution row remain unchanged |
| G-210 / PC-210 | `R.C478_G210_HostIdentity` | Adoption requires exact host instance | match adoption by SessionId or PID alone | Reused PID/wrong HostInstanceId/start identity never attaches to or asks a stranger for custody; Unknown retained |
| G-211 / PC-211 | `H.C478_G211_TerminalConflict` | A terminal receipt cannot be overwritten | last-write-wins a conflicting final receipt | Identical replay is unchanged; conflicting bytes produce integrity error and preserve the first receipt |
| G-212 / PC-212 | `R.C478_G212_PublicationAfterPersist` | Runner persists import before publishing it | publish Exited before durable runner import | At custody-change event/response observer the exact receipt is readable after restart; save fault publishes no Exited |
| G-213 / PC-213 | `H.C478_G213_VersionNegotiation` | New messages require host feature support | send custody discriminator to a legacy hello without checking features | Old-host peer receives zero new messages and sourced launch explicitly refuses; ordinary legacy launch still works |
| G-214 / PC-214 | `H.C478_G214_NeverStarted` | No-start proof requires a closed proven no-start path | convert any Starting record with missing PID to NeverStarted | Crash after native start intent is Unknown; a durably sealed pre-create refusal alone yields NeverStarted |
| G-215 / PC-215 | `A.C478_G215_ImportSchema` | Importer accepts only the complete supported schema | accept unsupported schema/missing mandatory fields | Unknown schema or absent required observation/seal fields remains unaccepted; zero removal; valid native receipt imports |
| G-216 / PC-216 | `A.C478_G216_RunnerProvenance` | Only runner-produced evidence can authorize | read custody state from worker restoration manifest | Worker-written Exited/commandsAwaited/PID inventory cannot create a validated receipt row or deletion authority |
| G-217 / PC-217 | `A.C478_G217_TaskOperation` | Receipt must match this Mutation task and O | omit task/O identity comparisons in importer | Each wrong task/O rejected independently, including equal L; receipt import count zero |
| G-218 / PC-218 | `A.C478_G218_GenerationBinding` | Receipt must match accepted session generation | compare SessionId only and ignore ExecutionId/AcceptedStartedAt | Same SessionId reused at new generation cannot consume old receipt; exact old attempt remains recorded |
| G-219 / PC-219 | `A.C478_G219_Coordinates` | Receipt covers the exact snapshot creation | skip receipt-to-reservation creation coordinate comparisons | Individually crossed path/common dir/Git dir/branch/creation ID/L refuse before import or destructive request |
| G-220 / PC-220 | `A.C478_G220_ContainerBinding` | Imported native container belongs to this attempt | accept any host/container ID with matching task/session | Cross-attempt ContainerId/HostInstanceId rejected even with matching high-level IDs; stored binding immutable |
| G-221 / PC-221 | `A.C478_G221_TaskSealAtomic` | Seal serializes against new accepted launches | release task admission lock before freezing execution set | Barrier race includes accepted B or refuses it; never a sealed set omitting a runner-bound launch |
| G-222 / PC-222 | `R.C478_G222_RunnerSealExecutionRace` | Sealed attempt cannot execute delayed launch | skip seal check in the lower runner launch path | Old queued/replayed start after durable seal produces zero native starts, including after runner restart |
| G-223 / PC-223 | `A.C478_G223_AllAttempts` | Every attempted generation needs proof | inspect only latest successful execution | Latest Exited plus older Draining/Unknown/Unsupported refuses; zero destructive requests; every row retained |
| G-224 / PC-224 | `A.C478_G224_EmptyHistory` | Absence of execution rows is not no-start proof | treat empty history as vacuously safe | Legacy/missing history refuses; explicit fenced never-enqueued reservation is the only empty-history positive |
| G-225 / PC-225 | `A.C478_G225_ReadBeforeDelete` | Fresh frozen authority precedes each deletion | reuse prefetched receipt/seal at owned-output or final remove boundary | Barrier changes seal/membership/evidence; first forbidden destructive call count is zero, including output deletion |
| G-226 / PC-226 | `A.C478_G226_Retention` | Cleanup preserves referenced custody records | delete receipts/seal when worktree/manifest/task expires | After successful removal and cleanup sweeps, receipt bytes/digest and seal are retrievable; FK blocks premature parent deletion |
| G-227 / PC-227 | `R.C478_G227_Timeouts` | Wait cancellation cannot become exit proof | return Exited in timeout/cancellation catch | Held live descendant and failed query yield Draining/Unknown with explicit reason, not Exited; later valid observation can recover |
| G-228 / PC-228 | `A.C478_G228_NoCleanupKill` | Cleanup has no termination side effect | call stop/kill on custody refusal inside cleanup | Recording stop/kill/session-start adapters see zero calls for every cleanup refusal; original task/result unchanged |
| G-229 / PC-229 | `A.C478_G229_SealPersistsRefusal` | Failed cleanup cannot reopen the task | clear the durable task seal when Git removal fails | Retry/launch for same task remains refused after restart; cleanup retry uses original seal and retained residue |
| G-230 / PC-230 | `A.C478_G230_ExternalExecutorContract` | Active sourced recipe forbids external execution with snapshot access | remove the no-external-executor requirement from active Mutation/basics/loop resources | Loaded sourced-worker contract still requires local inherited execution and forbids broker/remote execution with snapshot access; this is an instruction control, not proof that arbitrary IPC was prevented |

Each numbered row is one logical invariant. Predicate-value arms (task/O or
creation coordinates) each run with just that field corrupted and the remaining
binding coherent; patch all duplicated evaluations of **that invariant** for
its PC, leaving other guards intact. If implementation separates these into
additional independently meaningful safeguards, append new IDs rather than
hiding them in this table. Distinct producer and consumer checks deliberately
have separate PCs. Native-create/order PCs assert at the first boundary;
a later refusal does not kill them.

### Boundaries and execution

Run all original V/R and these tests unmutated in Code. Ordinary Review is
pre-land. **Only post-land Role Mutation executes PC-1..PC-230.** All are pending.

Positive/negative combinations: no-start vs started, root alive/dead with orphan
alive/dead, zero/nonzero/failed query, missing/stale notification, natural exit
vs authorized kill success/failure/timeout, first launch/replay/new generation,
same/different task/O/L/container/store, valid/torn/missing/legacy persistence,
runner/server restart and host death before/after proof, explicit cleanup
success/failure/cancel and repeated cleanup. Run canonical supported positives
on Windows with the actual shipped modern backend; a skip is pending native
acceptance, never a passing V-14/V-17. No full Cartesian product is promised:
orthogonal identities vary independently, crash cuts cover their interactions.

Use the existing output ownership procedure. For this battery set
`MSBUILDDISABLENODEREUSE=1`, pass `--property:UseSharedCompilation=false`, and use
unique owned build/result roots. Do not connect to an existing compiler/test
server with this snapshot. The original plan's commands otherwise stand.

Additional ordinary commands, sequentially (after methods are implemented):

```powershell
$env:MSBUILDDISABLENODEREUSE = '1'
dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c478-vr/ --property:UseSharedCompilation=false -- --treenode-filter "/*/*/PtyCustodyTests/*" --report-trx --report-trx-filename c478-pty-custody.trx
if ($LASTEXITCODE -ne 0) { throw 'Pty custody ordinary verification failed' }
dotnet run --project tests/Antiphon.PtyHost.Tests --property:OutputPath=bin-c478-vr/ --property:UseSharedCompilation=false -- --treenode-filter "/*/*/HostCustodyTests/*" --report-trx --report-trx-filename c478-host-custody.trx
if ($LASTEXITCODE -ne 0) { throw 'Host custody ordinary verification failed' }
dotnet run --project tests/Antiphon.SessionRunner.Tests --property:OutputPath=bin-c478-vr/ --property:UseSharedCompilation=false -- --treenode-filter "/*/*/RunnerCustodyTests/*" --report-trx --report-trx-filename c478-runner-custody.trx
if ($LASTEXITCODE -ne 0) { throw 'Runner custody ordinary verification failed' }
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c478-vr/ --property:UseSharedCompilation=false -- --treenode-filter "/*/*/PostLandMutationCustodyTests/*" --report-trx --report-trx-filename c478-app-custody.trx
if ($LASTEXITCODE -ne 0) { throw 'Application custody ordinary verification failed' }
```

Every fresh TRX must contain the intended class/method and nonzero executions.
Record failures/skips and actual counts. Run affected existing
PtyKillProcessTreeTests, PtyBackendContractTests, ModernPtyDa1Tests,
HostSessionPipeTests, FramingTests, PtyHostAdoptionTests, RunnerStartupReadinessTests
and RunnerCapabilitiesTests with exact class filters once; keep any additional
changed delivery/generation test methods scoped. Host/runner/Pty assemblies use
their own limiter, so do not overlap them with each other or Antiphon.Tests.

For a PC use its assembly and exact expanded method (e.g.
`/*/*/PtyCustodyTests/C478_G198_NonemptyJob`) for baseline, red and restored green.
Fresh unique TRX/stdout/stderr, intended assertion, nonzero counts, restored
bytes, refreshed timestamps and nonincremental rebuild remain mandatory.
Save backups and reports outside the snapshot; record O/L and ExecutionId as
well as the Mutation task ID. Await every command/fixture child before moving
to another mutant. Native fakes prove error handling; real native methods
prove containment. Neither replaces the other.

### Out of scope

No general-purpose privileged sandbox, remote-process inventory, manual
verification-custody override, Linux cgroup backend, Herdr containment retrofit,
automatic card creation/spend, active-session migration or live-stack restart
is part of this amendment. These exclusions cannot be used to report an
unsupported/unknown run clean. Existing child-journal recovery remains separate
and never fabricates this receipt. Evidence custody does not attest test
correctness or worker restoration; all original file/evidence checks remain.

### Cost

Estimates only; no builds, tests or mutation cycles ran in this Plan amendment.
These are additive to the original TestDesign estimates, not measured timings.

| Additional ordinary work | Minutes | Coverage |
|---|---:|---|
| Four-assembly isolated build/migration setup | 4 | New schema and native/host/runner graph |
| Native/host ordinary scenarios + changed compatibility classes | 8 | PtyCustodyTests / HostCustodyTests and scoped native contracts |
| Runner restart/protocol/error matrix | 6 | RunnerCustodyTests / changed adoption/readiness/framing methods |
| Application binding/import/real cleanup acceptance | 6 | PostLandMutationCustodyTests and amended V-6 |
| **Additional ordinary V/R** | **24** | **New total 42 + 24 = 66** |

| New PC group | Controls | Minutes per full cycle | Minutes |
|---|---:|---:|---:|
| G-183..G-190: launch/lifecycle | 8 | 2.5 | 20 |
| G-191..G-202: native containment/drain | 12 | 3.5 | 42 |
| G-203..G-214: persistence/restart/protocol | 12 | 3 | 36 |
| G-215..G-230: consumer/seal/cleanup/contracts | 16 | 2.5 | 40 |
| **Additional PC floor** | **48** | | **138** |

Original scheduled PC floor 402 + 138 = **540 minutes** for 230 planned controls.
Code authoring allowance becomes 240 + 210 = **450 minutes** for the added
cross-process work, plus ordinary 66: **Code ExpectAbout >=516 minutes**.
Mutation analysis/discovery/reporting allowance becomes **30 minutes**:
**Mutation ExpectAbout >=570 minutes**. Ordinary Review **45 minutes**;
caller identity/capability/loaded-runtime acceptance **12 minutes**, plus actual
deployment. New total verification floor = **66 + 540 = 606 minutes**;
with Review/caller acceptance = **663 minutes**; with authoring/Mutation
analysis = **1,143 minutes** plus deployment. Adjust upward when native build/
crash fixtures, invalid mutants or added safeguards require it; do not stop at
an estimate.

For comparison, moving this 540-minute battery off the pre-land path saves
approximately **540 - 45 ordinary Review - 6 companion bookkeeping = 489 minutes**
if Review is entirely added; approximately 534 if Review was already owed.
The 138 additional PC minutes verify new custody behavior; they are not a speedup
over the previous plan. No parallel speedup or free crash matrix is assumed.

### Completion and Code handoff

Amended census: original 182 plus **48 new guards/PCs = 230**;
mapped=230, missing=0, duplicate PC mappings=0. V-1..V-17 and R-1..R-15
are the combined design. Every PC remains pending for post-land Mutation.

Land the plan amendment through the caller's normal task land operation, then
resume **all S1-S5**, including S3a-S3c above. Code writes the real tests, runs
ordinary V/R, commits/pushes and returns **next: review**. Ordinary Review
precedes publication. The caller records the companion, lands Code, deploys the
complete server/runner/host change from the canonical checkout, checks loaded
capability/composition, then dispatches the 230-PC battery at confirmed L.
Do not ship the scratch cleanup reader or infer proof from a terminal session.
