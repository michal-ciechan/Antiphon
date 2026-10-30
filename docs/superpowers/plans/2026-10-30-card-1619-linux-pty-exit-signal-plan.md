# CARD-1619: Carry Linux PTY exit signals end to end

Date: 2026-10-30. Plan source: `4e67121ef7b85a563c5563cf5fc6aa769cb45743`.
Status: source-audited plan with folded verification design; 12-row import passed; ready for independent Opus plan Review.
Plan task: `9a468e98`; product code is outside this dispatch.

## Decisions

- **D-1 — Preserve exit evidence, not inferred success.** Carry a nullable signal through the PTY, detached host, runner and stored session; retain a distinct unknown state for old producers. Reject guessing signals from historical zero exit codes.
- **D-2 — Make the change additive across rollout skew.** Old hosts and old runners must still parse each other's messages. Detailed wire, persistence and attribution contracts follow the source audit.
- **D-3 — Keep settlement authority unchanged.** Correct stop evidence and failure wording without replacing transcript-confirmed report settlement, CARD-1610 holds, or CARD-1614 custody proof.
- **D-4 — Fold verification design into this plan.** The requested next stage is Opus Review of the plan, then Code. Code slices are bounded to 150 minutes each.
- **D-5 — Land as a fix.** RC first, separately reviewed master port; no deployment, tag or publish is authorized by this Plan task.
- **D-6 — Own a small, pinned Porta managed patch.** Porta 1.0.7 has no public signal accessor: the signal is private on the Unix connection, not on `PtyExitedEventArgs`. Vendor its managed sources at the package's exact source revision, retain its license/provenance, and expose the already-decoded signal in the event and a completed-exit snapshot. Continue using the pinned package's native runtime assets. Do not use reflection, race a second `waitpid`, infer from `Process.ExitCode`, or write a new PTY implementation. A future upstream replacement requires the same real-path witnesses.
- **D-7 — Keep the existing integer exit-code contract; add `int? ExitSignal`.** Zero explicitly means an observed exit without a POSIX signal; positive values are an observed signal; null means unavailable/legacy/observer failure. Windows producers emit zero (no signal) and retain their exact existing exit codes. A signaled Unix process retains Porta's existing integer zero in legacy fields; every new consumer must inspect the signal first. Do not invent `128+signal` as the actual process exit code. Integer-only adapter APIs may use a documented nonzero compatibility result for a known signal, but must also expose the structured evidence.
- **D-8 — Attribute intent separately from observed death.** Persist a generation-bound stop intent before sending a signal. A request is not proof of its success or of who delivered the fatal signal. Record the signal actually attempted, path, request time and result; combine it with the wait observation conservatively. No new automatic stop, escalation, release deadline or custody authority is added.
- **D-9 — Make provenance failure visible.** A new instrumented Linux stop whose intent cannot be durably written refuses before signaling and retains existing stop debt/ownership. Reject silently performing an unattributable kill. This deliberate stop-path availability tradeoff must be checked in plan Review; Windows is not gated by this Linux journal.

## Ground-truth table

| Card assumption | Code / observed truth | Design consequence |
|---|---|---|
| The Linux exit signal is lost | `src/Antiphon.Agents.Pty/PtySession.cs:41` forwards only `e.ExitCode` | Replace the integer-only exit seam with explicit evidence |
| Exit zero proves a clean self-exit | Investigation found 75/75 logged requested Linux kills reported zero | Unknown old evidence must not claim self-exit |
| This is a missing-report settlement defect | Investigation found no final message; dispatcher already tries report settlement before dead-session failure | Keep the existing settlement ordering |
| Porta exposes the signal on its exit event | Pinned upstream `PtyExitedEventArgs.cs:17-25` contains only `ExitCode`; `Unix/PtyConnection.cs:25` has a private `exitSignal` | A one-line `e.ExitSignal` edit cannot compile; first expose the existing native observation |
| Linux uses .NET Process to obtain the PTY wait result | Upstream `Linux/PtyConnection.cs:38-40` calls `pty_waitpid`; native `porta_pty.c:245-247` calls libc `waitpid`; `Unix/PtyConnection.cs:179-182` decodes and drops the signal | Keep one reaper; use its result |
| Stops send TERM then KILL | `PtySession.cs:55-66` tree-kills with .NET; fallback Porta Linux `Kill:27` sends SIGHUP | Record SIGKILL or fallback SIGHUP; do not describe a nonexistent SIGTERM grace period |
| Only the sentence is wrong | `AgentSessionRuntime.cs:260` and `SessionReconciliationService.cs:775` classify zero as clean | Audit and fix signal-aware classification as well as presentation |
| Host protocol is unversioned | `PtyHostProtocol.Version=1`; framed JSON with optional-member tolerance; host logs a version difference and continues | Add fields without a protocol-version bump |
| An old host disappears at a runner upgrade | Detached hosts survive and are adopted from their manifest | Null must stay unknown across status, replay and adoption |

## Native source and evidence contract

Source pin is Porta.Pty 1.0.7's NuGet repository commit
`54684ba55148ed6bcd0c827ad7e8841a3289a466`, verified from the installed nuspec.
Primary source: [Unix wait/decode](https://github.com/tomlm/Porta.Pty/blob/54684ba55148ed6bcd0c827ad7e8841a3289a466/src/Porta.Pty/Unix/PtyConnection.cs#L150),
[Linux native binding](https://github.com/tomlm/Porta.Pty/blob/54684ba55148ed6bcd0c827ad7e8841a3289a466/src/Porta.Pty/Linux/PtyConnection.cs#L38),
[native shim](https://github.com/tomlm/Porta.Pty/blob/54684ba55148ed6bcd0c827ad7e8841a3289a466/src/Porta.Pty.Native/porta_pty.c#L245).
There is no repository-owned Linux `waitpid` implementation in PtyHost today.
The native shim is packaged `runtimes/linux-*/native/libporta_pty.so`.
Successful blocking wait decodes `status & 127` and, only for no signal,
`(status >> 8) & 255`. Wait errors, including ECHILD, are not a known normal exit.
The patch must preserve EINTR handling and represent unavailable observation explicitly.

Import managed upstream files into `src/Antiphon.Porta.Pty/` with an MIT license
and provenance README, use assembly name `Antiphon.Porta.Pty`, retain the Porta
namespaces, and replace the managed package reference in
`src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj` with a project reference.
The new project pins Porta.Pty 1.0.7 with `IncludeAssets="native"` and
`PrivateAssets="all"`, and explicitly retains the upstream Vanara 4.2.1 dependency.
Prove native assets flow to the detached host output; do not ship two managed Porta
assemblies. The vendored patch is limited to exit evidence/replay; do not modernize
unrelated upstream code. This is a bounded exception to ADR 0002's rejected broad
Windows fork: Windows spawning and both native shims remain unchanged.
Implement the new completion members in the vendored Windows connection as well
as Unix, returning the observed Windows code and zero signal; do not edit its
spawn/kill algorithm. If native-only dependency metadata does not propagate the
shim through project references, explicitly stage the pinned package assets as
Content using the repository's existing RID layout and copy/publish metadata;
the detached-host witness must prove the shadow copy resolves that file.

Add an immutable `PtyExitObservation(ExitCode, ExitSignal)` at the PTY seam.
`IPtySession.Exited` and `PtyAgentRunner` consume it. Cache completed exit before
notifying; subscribe then replay a completed snapshot with exactly-once completion
so an immediately exiting child cannot lose its result before subscription.
Keep `PtyAgentRunner.Exited`'s integer compatibility task where existing consumers
need it and add structured completion; known signal maps to a documented nonzero
compatibility result only on that legacy adapter task. Host/session fields retain
the actual observed code and separate signal. Windows `ModernConPtyConnection`
supplies signal zero with the same signed exit code; no Windows kill or job changes.
This card qualifies Linux only among Unix platforms. The adapter leaves macOS
signal evidence unknown until that platform's mapping is separately qualified.

## Wire and rollout compatibility

Append default-null `ExitSignal` to `ExitedMessage`, `StatusReplyMessage`,
`PtyHostManifest`, runner session DTO and exited event, server equivalents and
public session projections. Include it in attach-after-exit replay, status polling,
live event publication, dead-host manifest adoption and HTTP/phone-home mapping.
Keep `$type`, frame length format, existing fields and protocol/schema version 1.
The signal's presence is per observation; a feature flag is not evidence that a
particular exit was observed. Unknown positive signal numbers survive unchanged;
display the number and `UNKNOWN`, not a guessed name. Linux names include at least
SIGHUP=1, SIGKILL=9, SIGTERM=15; do not apply that name table to another OS's numbers.

| Host | Runner/server | Required result |
|---|---|---|
| Old | New | Missing field parses as null, keeps code, displays `signal unknown`; status/replay/manifest are equally conservative |
| New | Old | Old frozen v1 reader ignores extra members and still parses the frame/manifest; it cannot promise corrected classification |
| New | New | Signal reaches storage and all presentation/branching consumers |
| Old | Old | Historical behavior, no new correctness claim |
| New runner | Old server | Additive DTO/event fields are ignored; full behavior needs the server update |

Deploy additive database/server readers first, then runner/new-host producers.
Surviving old hosts continue to report unknown until their natural/authorized end;
do not kill them merely to upgrade evidence. During reverse skew, an old runner
necessarily loses the new field: compatibility means parsing/liveness survives,
not that old code gains the fix. Qualify the complete new stack before claiming
signal-safe classification. No backfill invents signals for the historical kills.

## Persistence, classification and text

Add nullable `AgentSession.ExitSignal` and `AgentSession.ExitStopIntentJson` (the
bounded typed intent projection, not arbitrary log text). Generate an additive EF
migration in `server/Migrations/` with `dotnet ef migrations add`; update
`AppDbContextModelSnapshot.cs` through the CLI. Old rows remain null. Mirror signal
on `RunAttempt` because `AgentSessionService` copies exit evidence there and
`CardReviewService` displays it. Add `AgentIncident.ExitSignal` for the supervisor's
stored exit incidents. No data backfill, nonnull default, enum renumbering or index
is required. Domain fields do not depend on wire/JSON libraries. Application owns
the bounded intent DTO conversion and pure exit-description/classification helper.

Every accepted-generation exit write, including a row already closed by an
authorized stop, enriches missing observation fields without overwriting stronger
evidence or another generation. Unknown replay must not erase a known signal.
Conflicting same-generation observations retain the first complete observation
and log a bounded diagnostic; do not silently flip success/failure. Start/resume
paths clear signal and intent together with ExitCode (`AgentSessionService`,
`AgentControlService`, `SessionReconciliationService`). Stale or generation-less
events retain the existing refusal behavior. No migration retroactively rewrites
historical failure sentences or terminal task outcomes.

Classification is signal-first. A **newly observed unsolicited signal exit** is
Failed even when legacy ExitCode is zero. A known normal exit retains existing
zero/nonzero behavior. Legacy null-signal observations retain the old lifecycle
fallback for compatibility, but are never presented as proof of clean/self exit.
That unavoidable degraded case is explicit in the rollout matrix. An already
authorized request stop may remain Stopped/Canceled; the CPU-spin resumability
exception remains a policy stop, not a successful execution. MemoryKilled remains
Failed. A completed report remains the only report-derived task outcome; a zero
process code never creates Succeeded. Preserve `SessionTermination.Record`'s
first-writer rule: signal observation is not an actor. `ProcessExit` means an exit
was observed, not that the process voluntarily stopped.

| Evidence | Required failure/detail text |
|---|---|
| Signal 9/15, no matching intent | `killed by signal 9 (SIGKILL); sender unknown` / `killed by signal 15 (SIGTERM); sender unknown` |
| Known signal plus matching issued intent | `stopped by Antiphon (SIGKILL; matching stop request recorded); killed by signal 9 (SIGKILL)` |
| Request only, missing observed signal | `Antiphon stop requested; signal unknown` (never assert that the request killed it) |
| Known normal zero/nonzero | `process exited with exit code 0` / `process exited with exit code 23` |
| Legacy zero/null signal | `process ended (exit code 0; signal unknown)` |
| Observer failed / vanished process | `process ended (exit code unknown; signal unknown)` plus the observed failure reason; do not turn a synthetic -1 into a real native code |
| Unknown positive signal number | `killed by signal N (UNKNOWN); sender unknown` |
| Intent followed by normal exit, failed signal call, mismatched generation or signal | Describe the observed exit; separately say a request was recorded, never attribute its death to that request |

Even WIFEXITED/no-signal does not prove user intent (the child can catch a signal
then exit), so remove `exited on its own` altogether. Keep
`DeadSessionFailurePrefix`, `StoppedBeforeFirstPrompt`, authentication priority,
existing diagnostic text and transcript-recovery ordering. Extend `DescribeStop`
for Failed as well as Stopped fallback rows; an existing FailureReason must be
composed with structured exit evidence rather than hiding it. Attention and the
dispatcher use the same updated snapshot/helper. Operator/SystemRequest and
PolicyRefresh still name their recorded request; no fabricated operator or killer.

Public session/board DTOs, session SignalR event and incident detail gain optional
nullable signal and typed intent properties at the end. Update handwritten
`client/src/api/agents.ts` and `client/src/api/boards.ts`; these contracts are not
generated clients at this source. Regenerate OpenAPI artifacts only if Code finds
a committed generator input/output on its target. Missing properties remain valid
for old servers and old JavaScript consumers ignore new members. Display already
formatted failure text; do not require a frontend-only signal formatter.

## Consumer audit and required disposition

This is a source audit at the plan base, not a claim that the implementation is
already fixed. Re-run `rg -n 'ExitCode|exitCode|Exited.Result' src server client/src`
on the Code target and record any additional session consumer.

| Consumer / source anchor | Current use | Planned change / branch invariant |
|---|---|---|
| `PtySession.cs:41`, `ModernConPtyConnection.cs:428`, `PtyAgentRunner.cs:238-247` | Integer exit callback/completion | Structured observation, single reaper and replay; Windows code unchanged |
| `HostSession.cs:255,299,395-440` | Attach replay, status, manifest, log, live EXIT | Carry identical signal/intent in every surface; no linger/custody changes |
| `PtyHostClient.cs:212`, `SessionRunnerRuntime.cs:3454,3583,4380`, `ExitEnvelope:2766`, `ToDto:3990` | Live, status and manifest adoption reduce to code/reason | Carry optional evidence through all routes; retain generation and sequence |
| `ISessionChild.cs:23`, Herdr/direct-process child routes | Other backends report their own exit | Optional unknown signal unless that backend actually observes it; never infer Linux signal from a numeric code |
| `RunnerContractMapper.cs:39,106`, `SessionRunnerHttpClient.cs:987,1275`, phone-home event/recovery pumps | DTO/event conversion | Copy fields; no loss in reconnect catch-up |
| `PhoneHomeCommandDispatcher.cs:963`, `RemoteLaunchAlreadyExitedException`, `AgentSessionService.cs:450,572` | Already-exited launch errors and durable close | Carry evidence in typed exception/problem extensions; honest text; same-generation launch remains refused |
| `RunnerTerminalSession.cs:365`, `IAgentProtocolAdapter.Exited`, local Raw/Claude/Codex and runner Raw/Claude/Codex/Grok/OpenCode adapters | Integer compatibility completion; null becomes zero | Expose structured observation via additive interface member with unknown default for old/test implementations; known signal or missing code returns -1 on compatibility task, not false zero; consumers persist structured actual fields |
| `AgentSessionRuntime.cs:260` | `exitCode == 0` controls session AND agent Stopped/Failed | Signal-first helper; explicit CPU-spin stop exception; enrich terminal rows; preserve generation fences |
| `SessionReconciliationService.cs:775` | Same zero-success branch | Use same helper and failure text as live event; same snapshots produce same fate |
| `AgentSessionService.cs:905-906` | Launch cleanup decides ownership from matching code or null/zero | Replace code-only fallback with generation, issued intent and observed evidence; normal-exit race must not be relabelled |
| `AgentSessionService.cs:1892,2052-2101,3793`, `AgentSessionRuntime.cs:2324` | Adoption/stop/finalization/test adapter persistence | Copy actual signal/intent; stop ack alone is not exit; RunAttempt stays Canceled/Failed on kill |
| `AgentTaskLiveness.cs`, `AgentTaskDispatcher.cs:3092-3097,3190-3204`, `AttentionService.cs:881-886` | Dead verdict and shared description | Extend snapshot and text; preserve report-first settlement and dead/live predicate |
| `AttentionService.Leaks.cs:160-207` (SessionStopStuck) | Five-minute Stopping/pending-intent projection, inventory-based liveness | No timeout or release branch change; include structured stop detail when present; an exit code is not absence evidence |
| `AgentSupervisorService.cs:272,848`, `DiagnosticsBundleService.cs:191,206`, `CardReviewService.cs:208` | Stored incident, diagnostic and attempt text | Show signal and intent; preserve restart policy and audit history |
| `AgentService`, `BoardService`, session events, `AgentDtos`, `BoardDtos` | API projection | Add nullable fields; no UI/HTTP compatibility break |
| `CheckCompactionContinuationService.cs:391` | Synthesizes zero for a confirmed policy stop | Do not label it a native exit observation; preserve existing zero compatibility value with unknown signal until real event arrives; never erase later known signal |
| Check interpreter/standing-agent checks, task deadlines and `TaskProgressStalled` | Consume task/session status and failure text; no native-code success branch | New wording only downstream; no new hold, automatic retry or kill |
| `AgentTaskDispatcher.cs:9047-9511` (release sweep and pool janitor), `PoolDelegateRelease`, `AgentTaskReplyService.cs:2790-2929`, `TerminalRunnerSeatReleaseService` | Ownership/liveness/working/delivery gates | No code-zero release authority; preserve killed/warm/owned states and all conditional holds |
| `AgentControlService.cs:533`, `AgentSessionRuntime.cs:2306`, `RunnerTerminalSession.cs:312` | Code presence used as an exited hint | Keep explicit status/observed exit semantics; signal-positive observations also count as exited, never as success |
| Git/workspace/landing results, auth/version/custody-helper probes, `BashTool`/`GitTool`, `RunnerProcessProbe` | Separate .NET child processes with their own exit codes | Not consumers of the dropped Porta field; no global rewrite of unrelated `== 0` checks; helper success is still not custody accounting |

## Stop intent and CARD-1614 boundary

The normal path is `HostSession.KillAsync` -> `PtyAgentRunner.KillAsync` ->
`PortaPtySession.Kill` -> `Process.Kill(entireProcessTree:true)` (SIGKILL).
On failure the Porta Linux fallback sends SIGHUP. There is no TERM-then-KILL
escalation. `PtyAgentRunner.SetExitReason` is presently in-memory and set before
the call; it cannot serve as durable provenance and can race a normal exit.

Add a small optional `ProcessStopIntent` wire record and an owner-local atomic
`ProcessStopIntentStore` in `Antiphon.PtyHost.Protocol`, reused by host and runner.
One record per attempt under the existing session-log root, keyed by session,
accepted generation and request id; store request time, host instance, child
PID/start time (or custody namespace/container identity), target kind, attempted
signal, path and outcome (`requested`, `issued`, `failed`). No command line,
environment or provider payload. Use create-new + flush + atomic rename, avoid
shared overwrite files, and retain with session diagnostics after host ack.
Host manifest and exit payload reference/copy the bounded matching record; the
runner can recover the journal if a host itself was killed. Record host-target
and child-target kills separately; a host kill is not a known child's wait status.

Write `requested` before the syscall; write `issued` only when the syscall/helper
returns successfully. The exit observer and stop writer serialize evidence
publication so a fast exit can include the issued update; recovery after a crash
between signal and issued-write stays requested/unknown. No file write is a kill
receipt. An intent-store write failure refuses that NEW instrumented Linux stop
attempt visibly, leaving existing pending-stop/retry ownership; it must not report
success or silently issue an unrecorded signal. Windows stop behavior is unchanged.
Use an injected journal seam at the actual kill boundary so the SIGKILL attempt
and SIGHUP fallback each get their real requested signal. Normal exit preceding
the attempt is not attributed. Concurrent same-signal external delivery cannot be
distinguished by waitpid: the wording explicitly says a matching request was
recorded, not that a kernel sender PID was observed.
The serialization gate covers journal write, syscall and issued update only;
release it before waiting on the exit task, or the observer and KillAsync would
deadlock. Do not latch `KilledByRequest` on a failed/refused attempt or turn a
normal-exit race into a request kill. Preserve reason overrides only when the
observed stop and matching issued attempt support them.

Instrument existing authorized Linux entry points only: host KillMessage and
owner/shutdown stop; runner `KillPidBestEffort`, `KillHostIfStillOurs`, pending/
orphan Herdr kills (their existing pid/start validation stays); and explicit
Linux custody termination/recovery (`IPtyCustodyContainment`,
`ILinuxCustodyRecovery`, runner custody ledger). Ordinary observation, sealing
and root exit must not begin a new stop. The cgroup helper sends SIGKILL (v2
`cgroup.kill`; v1 freeze/read/kill/thaw). Record intent before invoking it, but
helper exit zero is only an issued operation: retain CARD-1614's post-drain
accounting, placement proof, Unknown outcomes and retained cgroups.
Also cover `PortaPtySession.Dispose`: upstream Unix disposal closes the master
streams then sends SIGHUP. If the root is still live, record the disposal/hangup
intent before either operation. A disposal after observed exit emits no new
death attribution. Unexpected host death/master closure may cause kernel SIGHUP
without a durable request; preserve sender unknown in that case. The custody
helper's own timeout tree-kill at `IPtyCustodyContainment.cs:195` targets the helper,
not proof of the tracked root's exit; give it target-kind helper evidence and
retain Unknown accounting. Add these arms to V-13's fallback/host-target tests.

CARD-1614's plan is in this checkout and its current product files match the
fetched RC ref for the PTY/host/runner paths inspected here. Revalidate at Code
dispatch. Linux custody remains unadvertised until its separate native gates;
this card cannot enable it. Keep Windows seal nonterminating, job kill-on-close,
24-hour tracked linger, and live-unadoptable-host operator ownership unchanged.
No new input-waiting session or hold is introduced. CARD-1610's holds retain their
own release policy; with parking disabled they have no automatic release deadline.

## Slices

Each row is a separately commissioned Code task, at most 150 minutes including
focused red/green verification. Do not dispatch all four source areas concurrently.
Commit and push the failing witness before its repair; a compilation failure is
not the required red. Keep code frozen during each run. The native Windows rows
run on an eligible Windows lane at the same source SHA; Linux cannot credit them.

| Slice | Value, files and tests | Budget / closure |
|---|---|---|
| **S1 — retain the actual native observation** | Add `src/Antiphon.Porta.Pty/` pinned managed sources/license/provenance and project; update `Antiphon.sln` and `src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj`; patch vendored `Unix/PtyConnection.cs`, `PtyExitedEventArgs.cs`, `IPtyConnection.cs` completion snapshot; add `PtyExitObservation.cs`; update `PtySession.cs`, `PtyAgentRunner.cs`, `ModernConPtyConnection.cs`. Add `tests/Antiphon.Agents.Pty.Tests/Card1619UnixExitTests.cs` and `Card1619WindowsExitTests.cs`. | 135 minutes: 25 native dependency/staging, 55 seam/replay, 25 witnesses, 15 checkpoints, 15 graph/review notes. CP-1; final Windows closure CP-2 after S4. No broad native fork. |
| **S2 — carry it across process/restart boundaries** | Update `PtyHostMessages.cs`, `PtyHostManifest.cs`, `HostSession.cs`, `PtyHostClient.cs` if mapping needed, `SessionRunnerContracts.cs`, `ISessionChild.cs`, `SessionRunnerRuntime.cs`. Add `tests/Antiphon.PtyHost.Tests/Card1619ExitWireTests.cs`, `Card1619LinuxHostExitTests.cs`, `tests/Antiphon.SessionRunner.Tests/Card1619RunnerExitTests.cs`, frozen old-schema fixtures. | 140 minutes: 55 propagation, 45 real detached-host/restart and skew tests, 25 checkpoints, 15 review notes. CP-3/CP-4. Highest transport risk is exit while detached. |
| **S3 — persist and describe it without false success** | Update `AgentSession`, `RunAttempt`, `AgentIncident`, generated migration/snapshot; `SessionRunnerDtos`, `AgentDtos`, `BoardDtos`; `RunnerContractMapper`, HTTP/phone-home launch exception mapping; `IAgentProtocolAdapter`, listed local/runner adapters and `RunnerTerminalSession`; `AgentSessionRuntime`, `AgentSessionService`, `SessionReconciliationService`, `SessionTermination`, `AgentTaskLiveness`, dispatcher/attention projections, supervisor/review/diagnostics projections, reset writers and `CheckCompactionContinuationService`; handwritten client types. Add `Application/Card1619ExitPolicyTests.cs`, `Card1619ExitPersistenceTests.cs`, `Card1619ExitConsumerTests.cs` under `tests/Antiphon.Tests/`. Update owner docs. | 150 minutes: 60 mapping/helper/persistence, 25 migration/reset/API, 35 witnesses, 20 checkpoints, 10 notes. CP-5..CP-8. If the audit reveals further branching consumers, split a follow-on Code slice before widening this budget. |
| **S4 — record Antiphon's stop intent** | Add `ProcessStopIntent.cs` and `ProcessStopIntentStore.cs` in `src/Antiphon.PtyHost.Protocol/`; use an injected low-level stop callback in `PtySession`/`PtyAgentRunner` without introducing a PTY-to-host dependency; wire `HostSession`, `SessionRunnerRuntime`, `IPtyCustodyContainment`, `ILinuxCustodyRecovery`, `RunnerCustodyLedger`; append optional intent to wire/manifest/DTO and S3 persistence/helper. Add `Card1619StopIntentTests.cs` (PtyHost.Tests), `Card1619LinuxStopIntentTests.cs` (SessionRunner.Tests), `Application/Card1619StopAttributionTests.cs` (Antiphon.Tests). Update session/PTY owner docs. | 150 minutes: 45 durable journal/order, 35 alternate authorized paths, 30 race/crash/native tests, 30 checkpoints, 10 notes. CP-9..CP-12; final CP-2 runs in native Review. No helper privilege or custody policy expansion. |

S1 is independently reviewable; S2 depends on S1; S3 depends on S1/S2; S4 depends
on S3 and also adds `tests/Antiphon.SessionRunner.Tests/Card1619LinuxCustodyStopIntentTests.cs`
for CP-12, with the existing NativeLinuxOnly/Integration/Slow classification and
assembly-local process limiter. Its fixture requires installed root-owned helper
paths, the entrypoint namespace marker and delegated v1 pids/freezer roots, as in
`LinuxRunnerCustodyLifecycleTests`; their absence stays native-pending.

All four slices may be reviewed incrementally, but this plan's complete fix is
land-ready only after S4 and final native CP-2 pass. S1-S3 implement signal
retention; S4 closes attribution. Activation of new producers waits for S3
reader/migration readiness and the final native gates. Native-asset staging must
pass the detached-host witnesses even if the first slice alone builds.

The S3 footprint is broad but mostly mechanical propagation. Use one helper,
one DTO projection per boundary, and focused fixtures rather than copying policy
into every writer. If 150 minutes will be exceeded, push the complete current
slice with its truthful evidence and request another Code dispatch; never hide
owed consumers behind a partial completion claim.

## Landing and overlapping work

Classify as **fix**: target the current reviewed `rc/2026-10-27` line first,
then commission a separate master port with its own conflict resolution/review.
The assigned Plan branch is never merged/rebased into either target by this task.
Code must start from the caller's pinned current RC source, retain the exact
CARD-1614 repairs already present, and report actual source SHAs. No tags, publish,
activation or fleet restarts belong to this Plan. A pushed/landed commit is not
activation.

CARD-1610's latest plan on `origin/feat/card-task-a98460ca` is a separate hold/
late-report/janitor change. It keeps Working holds alive and phase-supervised;
this fix only handles a positively exited generation. Coordinate ownership of
`AgentTaskDispatcher`, `AgentTaskLiveness`/dead-session text, attention snapshots
and janitor tests. Preserve its new predicates, hold fields and report-first
ordering whichever lands first; do not import its earlier automatic Failed-to-
Succeeded proposal. Re-run the precise dead-session/report witnesses after the
separate master port. The corrected CARD-1610 plan targets master; that is not a
reason to redirect this fix's RC-first instruction.

Definition-file changes in S1 (`*.csproj`, solution, any props/targets/lock file)
require `scripts/audit-land-verification-graph.ps1 -Update` after committing the
definition input, using the real built evaluator and current pins. Commit the
generated `LandVerificationBuildGraph.manifest.txt` and `.evaluation.txt` records;
never hand-edit them or bypass a refused pin. Check again with `-Check`. EF
migration `.cs` and snapshot files alone are not definition-inventory entries;
adding migration references, package/project settings or a fixture csproj is.
Re-run Update in any later slice/port that changes that inventory. Audit includes
native-only package asset handling and the new project graph. A refused dynamic
vendor build target must be simplified/reviewed, not exempted.

Authorized preparatory tooling (separate from the closed product-test roster):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-graph -- dotnet build tools/Antiphon.LandGraphAudit -c Release --property:OutputPath=bin-card1619-graph/Release/net10.0/
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-graph-update -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Update -ToolPath tools/Antiphon.LandGraphAudit/bin-card1619-graph/Release/net10.0/Antiphon.LandGraphAudit.dll
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-graph-check -- pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-card1619-graph/Release/net10.0/Antiphon.LandGraphAudit.dll
```

For EF, restore the repository-pinned tool and scaffold
`AddSessionExitSignalEvidence` with `dotnet ef migrations add`, project `server`,
output directory `Migrations`, under `scripts/build-slot.ps1`. Arrange an isolated
`bin-card1619-migration/` build and matching EF metadata before using `--no-build`;
verify EF's resolved assembly path. `-- --property:OutputPath=...` is NOT an EF
build-output override (it forwards application arguments), and is forbidden here.
Do not let scaffolding rebuild a daemon's standard bin. Record tooling costs separately (provisional
5 minutes graph, 5 minutes migration), and remove only producer-owned alternate
outputs after use. Do not run migrations on a live database from Code tests.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`.
This Plan runs no product tests. Its only build/tool validation is checkpoint
import through the host build-slot gate. The new selections have **no measured
p95 yet**. All estimates below are provisional engineering budgets, tracked by
CARD-1619 under the R5 small-sample exception; they are not measured performance
claims. The investigation measures incidence, not test runtime. Do not use the
importer's 15-minute floor/3x estimate as timeout evidence.

| Group | Expected build / execution wall minutes | Initial execution ceiling | Measurement obligation |
|---|---|---|---|
| S1 Linux CP-1 | 3 / 1 | 4 minutes | Six native cases; per-case expected 5 seconds, provisional 15-second bound |
| Final Windows CP-2 | 4 / 3 | 8 minutes | Two new cases plus five existing tree-kill tests; native timings |
| S2 Linux CP-3/4 | 4+4 / 2+2 | 8 minutes per row | Host ready, pipe, manifest IO, restart/adoption and teardown |
| S3 Linux CP-5/6/7/8 | 6 / 5+1+2+2 | 12/2/4/4 minutes | Shared DB template/web host, isolated cases; separate typecheck span |
| S4 Linux CP-9/10/11 | 4+4+6 / 2+2+3 | 8/8/10 minutes | Journal fault cuts, real stop, mapper/persistence, lifecycle |
| S4 cgroup-v1 CP-12 | reuse CP-10 / 2 | 4 minutes | Exact owned container; no fleet-wide census or kill |

Whole-minute checkpoint estimates include build where needed; reused rows include
execution only. S1 Linux run 4 minutes; S2 12; S3 16; S4 23 including its native
cgroup row; final Windows 7. Run separate committed-slice groups. Never combine
all slices into a >30-minute run. If any selection/batch/row/run is expected or
observed over 30 minutes, request Debug with timings before repetition. A timeout
is red/blocked evidence, never permission for a silent retry or deadline increase.

Collect timings from each necessary red/green run and independent native Review,
retaining SHA, OS/architecture, runner build, load, sample count, exact group
membership, per-test TRX duration and nearest-rank p95. Keep platforms, cold builds
and warm execution separate. Within Code plus Review obtain three comparable
passing samples per new group before replacing provisional ceilings with about
`ceil(2 * p95)`; label the small sample. Extra timing repetitions are only for this
documented uncertainty, not a second full suite. Budget them separately and amend
cost/timeouts from evidence; collection is inside each task's 150-minute ceiling.

Share one TestDbFixture container/template and migration per assembly run, one
in-process test application per selection, and isolate cases by schema/transaction
or template clone. No per-test AppHost, Docker, npm install or dotnet build. Stage
native hosts/helpers once in the producing build. A fresh host per case is allowed
only for spawn/exit/restart witnesses, whose subject is that lifetime. All native
spawners carry the assembly-local `ParallelLimiter<ProcessSpawnLimit>`; rows are
serial with width one. Never overlap Antiphon.Tests and Antiphon.Agents.Pty.Tests.

Provisional phase targets: container readiness 30 seconds, migration/template 30,
web host 10, per-case clone 3, native host readiness 5, body 5, output/exit wait 5,
teardown/join 5. The migration-upgrade witness gets one extra isolated DB because
upgrade is its subject. These are phase targets, not sleeps. Initial individually
cancellable IO/pipe/process bounds are 10 seconds and DB readiness/migration 60;
refine near 2x measured phase p95. Cancellation joins owned processes; residue is
a failure. Production stop deadlines and CARD-1614's 30-second drain do not change.

Record admission, build, discovery, process startup, schema/seed/migration, clone,
body, drain/teardown and remainder separately. Use `tests/Shared/TestTiming.cs`
and valid `ANTIPHON_TEST_PHASE_*` run/group/attempt/suite metadata in actual test
children. Store ignored TRX, timings, rosters and receipts under
`.antiphon/card1619/<run-id>/`. Missing spans are unknown, never zero. Slot admission
is separately bounded to 10 minutes; exit 4 never permits an unleased retry.
The commands below pin per-row timeouts in imported YAML. The current tool still
gives builds a 15-minute minimum (`RunScheduler.BuildDeadlineMinutes`); this is
an explicit provisional CARD-1619 build-only exception, not a measured p95 or a
test-row allowance. Measured build p95 and a separate tooling change are needed
to remove that floor. No CLI row override is used, since it would erase the pins.

Dependency/category map: shared PTY -> PtyHost -> SessionRunner -> server runtime;
Claude/Codex/Grok adapters are downstream shared-transport consumers. CP-1/2 cover
native PTY branches, CP-3/4 host/runner, CP-5..7 server/checks/attention/settlement,
CP-8 client types, CP-9..12 provenance/custody. No model/provider calls are needed.
Use Unit for pure classes and Integration/PtyHost/Pty for DB/host classes per their
assembly. This focused ordinary roster is not Full RC qualification or a substitute
for CARD-1545 downstream release gates.

## Verification design

### Fixtures and red-first protocol

Use the production `PtyAgentRunner` backend factory and real packaged Linux shim.
Launch an owned `/bin/cat` or a controlled shell fixture that execs it, wait for
a nonce/readiness handshake and retain PID/start identity. The test process is
the external actor and sends exactly SIGKILL or SIGTERM to that owned PID. Do not
substitute Antiphon's stop API, a mocked EXIT, shell exit 137, or terminal Ctrl-C.
For normal cases a controlled child reads a release instruction then exits 0 or
23; omit that barrier deliberately for the fast-exit race. No AI CLI, sign-in,
provider call or production runner port is involved.

Detached-host witnesses use staged launcher/pipe helpers and fresh scratch roots.
Keep the host connected without Shutdown ack until reading its manifest, then ack
and join it. Adoption detaches runtime A, observes child death while detached, and
creates runtime B on the same owned state root. Test live-host status/replay and
dead-host manifest paths separately. Fixture cleanup runs after assertions and
cannot supply the observed kill.

DB integration uses real mapper/runtime/reconciler and dead-session classifier
against isolated schemas. The four real Linux cases traverse host -> runner ->
mapped event -> durable AgentSession -> dead-session/attention text, not merely
an injected DTO. S3 reports those as four separate TUnit results. The report-first
test stores a marked report and asserts its outcome wins before dead-session
failure. An exit by itself never supplies a task result.

Red commits may introduce compile-compatible nullable scaffolding with default
unknown evidence, then exercise the real current path and assert dropped signal,
wrong branch or wrong text. A compile error, missing executable, fixture error,
zero tests or NotImplemented exception is not red. Retain intended assertions
and source SHA. Normal-exit/backward-parse controls may already be green: label
them and prove their discriminating assertions with method-scoped positive
controls after implementation (drop/swap signal, erase unknown, bypass the signal
check, write intent after kill, let a stale generation overwrite). Restore and
rerun the exact method green. Mutation execution remains a separately commissioned
stage under its custody rules; no ad hoc whole-suite mutation loop in Code.

### Witness roster

Names below are concrete proposed tests; new files are named after their class.
Minimums count reported executions, not assertions or internal matrix loops.
These files do not exist in this Plan commit.

| IDs / class / lane | Methods or separately reported cases | Discriminating result |
|---|---|---|
| V-1..6 `Card1619UnixExitTests`, Agents.Pty.Tests, Linux | `External_SIGKILL_preserves_signal`, `External_SIGTERM_preserves_signal`, `Normal_zero_has_known_no_signal`, `Normal_nonzero_preserves_code`, `Fast_exit_is_replayed_once`, `Signal_exit_never_completes_legacy_adapter_as_zero` (6) | Signal 9/15 separate from raw code; zero/no-signal distinct from null; code 23 intact; exactly-once completion; compatibility task never false zero |
| R-1 `Card1619WindowsExitTests`, Agents.Pty.Tests, Windows | `Modern_exit_and_stop_preserve_windows_behavior`, `Inbox_exit_and_stop_preserve_windows_behavior` (2) | Production factory selects actual modern/inbox backend; normal 0/23 and kill result unchanged, signal zero |
| V-7 `Card1619ExitWireTests`, PtyHost.Tests, portable | `Old_exit_frame_is_unknown`, `New_exit_frame_is_accepted_by_frozen_v1_reader`, `Status_and_attach_preserve_signal`, `Manifest_both_versions_round_trip`, `Unknown_signal_number_is_preserved`, `Null_signal_is_not_normalized_to_zero` (6) | Frozen v1 framing/DTO fixture from base with provenance, not today's reader twice; length prefix/discriminator and optional null/absence survive |
| V-8 `Card1619LinuxHostExitTests`, PtyHost.Tests, Linux | `Live_SIGKILL_matches_manifest`, `Live_SIGTERM_matches_manifest`, `Exited_attach_and_status_replay_identical_evidence`, `Normal_exit_code_survives_detached_host` (4) | Actual host wait reaches wire/status/manifest/log; one meaning across all surfaces |
| V-9 `Card1619RunnerExitTests`, SessionRunner.Tests, Linux | `Exit_while_detached_survives_adoption`, `Dead_host_manifest_preserves_signal`, `Old_host_adoption_keeps_signal_unknown`, `Http_exit_and_list_keep_signal`, `Phone_home_exit_and_recovery_keep_signal`, `Stale_generation_does_not_replace_exit_evidence` (6) | Restart plus HTTP/phone-home event/list paths retain evidence; wrong/missing generation cannot overwrite it |
| V-10 `Card1619ExitPolicyTests`, Antiphon.Tests, Unit | `Describe_exit_evidence` with 10 `[Arguments]` cases: external 9, external 15, unknown positive, normal 0, normal 23, legacy 0, unknown code, requested-only, normal-exit race, CPU-spin policy stop (10) | Exact truthful text/classification; no self-exit or guessed sender; control asserts normal code 137 stays code 137 |
| V-11 `Card1619ExitPersistenceTests`, Antiphon.Tests, Integration/Linux | `Native_exit_reaches_database_and_failure_text` with four cases (9/15/exit0/exit23), `Live_event_and_reconciliation_agree`, `Terminal_row_is_enriched_without_changing_request_source`, `Resume_and_stale_events_keep_generation_boundary`, `Migration_preserves_old_rows_and_nullable_contract` (8) | Fresh DbContext after commit, live/reconciliation parity, restart/reset, additive schema upgrade; assertion fails on lost signal |
| V-12 `Card1619ExitConsumerTests`, Antiphon.Tests, Integration | `Attention_and_dispatcher_describe_same_signal`, `Report_before_signal_death_still_settles`, `Signal_death_without_report_never_succeeds`, `Check_and_supervisor_keep_signal_evidence`, `Stop_stuck_remains_pending_without_observed_exit`, `Pool_and_terminal_seat_do_not_release_from_code_zero`, `Launch_cleanup_does_not_steal_a_normal_exit`, `Api_attempt_and_diagnostics_preserve_optional_signal` (8) | Actual consumer methods/projections, not source-string tests; hold/absence/custody/report authority unchanged |
| V-13 `Card1619StopIntentTests`, PtyHost.Tests, portable ordering plus Linux host | `Intent_is_durable_before_signal`, `Write_failure_sends_no_signal`, `Issued_update_follows_syscall`, `Crash_before_send_is_only_requested`, `Crash_after_send_before_update_is_unknown`, `Fallback_records_SIGHUP_separately`, `Natural_exit_race_is_not_our_kill`, `Host_kill_is_not_child_wait_evidence` (8) | Inspect durable journal at syscall barrier; real host case proves signal/intent; no false attribution |
| V-14 `Card1619LinuxStopIntentTests`, SessionRunner.Tests, Linux | `Own_stop_preserves_SIGKILL_and_intent`, `Restart_recovers_requested_intent`, `Kill_all_records_each_owned_generation`, `Orphan_and_pending_kills_keep_identity`, `Custody_invocation_records_intent_before_helper`, `Unobserved_or_failed_kill_retains_stop_debt` (6) | Real runtime routes with deterministic boundary hooks; wrong PID/start/generation sends nothing; intent/helper exit is not success |
| V-15 `Card1619StopAttributionTests`, Antiphon.Tests, Integration | `Matching_issued_intent_is_qualified_Antiphon_stop`, `No_intent_means_sender_unknown`, `Mismatched_generation_or_signal_is_not_attributed`, `Requested_only_and_failed_intents_remain_uncertain`, `Late_intent_enrichment_preserves_known_exit`, `Operator_and_policy_sources_are_not_overwritten` (6) | DTO/storage round trip and final task/attention text, including request-only and normal-exit races |
| V-16 `Card1619LinuxCustodyStopIntentTests`, SessionRunner.Tests, native cgroup-v1 | `Explicit_release_records_signal_without_fabricating_custody`, `Dead_host_recovery_records_intent_and_remains_unknown` (2) | Demonstrated live descendant in owned bound tree before release, observed exit afterward; intent cannot turn Unknown into a receipt |

R-2 keeps all 5 existing `PtyKillProcessTreeTests` on native Windows. R-3 keeps all
6 `AgentTaskLivenessTests`. R-4 selects existing
`AgentTaskDeadSessionReconciliationTests.a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing`,
`a_stopped_session_with_transcript_but_no_report_still_fails`, and
`the_predicate_and_the_attention_projection_agree_on_every_case` (3 results).
CP-8 typechecks optional client fields; it has no invented Vitest assertion count.

Suffix audit at plan base: R-4 methods are unique; `PtyKillProcessTreeTests` and
`AgentTaskLivenessTests` have no distinct longer-prefix class collision. Proposed
`Card1619*Tests` names are unused. Repeat the declaration census after adding them
and after porting. OR operands below are parenthesized, each ending in `*`; table
pipes are escaped. Fresh TRX must contain each named class/method with zero skips
on its required lane. A skipped native witness remains owed.

Linux-native CP-1/3/4/5/9/10 and cgroup-v1 CP-12 are required Linux evidence.
Portable subcases do not replace native rows. CP-2 runs on Windows at the **final
S1-S4 source**, covering modern and explicit inbox paths. macOS and cgroup-v2 are
outside this card's activation claim; CARD-1614's v2 obligation stays open. No
new fleet host provisioning or Linux custody advertisement is implied.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-card1619-pty/` | linux-native-pty-exit | `/*/*/Card1619UnixExitTests/*` | V-1..6 | all 6 listed, 0 failed/skipped | 6 | 4 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-2 | S1-S4 | `tests/Antiphon.Agents.Pty.Tests -> bin-card1619-windows/` | windows-native-exit-regression | `/*/*/(Card1619WindowsExitTests*)\|(PtyKillProcessTreeTests*)/*` | R-1,R-2 | all 7 listed, 0 failed/skipped | 7 | 7 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-3 | S2 | `tests/Antiphon.PtyHost.Tests -> bin-card1619-host/` | linux-host-wire-and-skew | `/*/*/(Card1619ExitWireTests*)\|(Card1619LinuxHostExitTests*)/*` | V-7,V-8 | all 10 listed, 0 failed/skipped | 10 | 6 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-4 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-card1619-runner/` | linux-runner-exit-adoption | `/*/*/Card1619RunnerExitTests/*` | V-9 | all 6 listed, 0 failed/skipped | 6 | 6 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-5 | S3 | `tests/Antiphon.Tests -> bin-card1619-server/` | linux-server-exit-evidence | `/*/*/(Card1619ExitPolicyTests*)\|(Card1619ExitPersistenceTests*)\|(Card1619ExitConsumerTests*)/*` | V-10,V-11,V-12 | all 26 listed, 0 failed/skipped | 26 | 11 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-6 | S3 | CP-5 | portable-liveness-regression | `/*/*/AgentTaskLivenessTests/*` | R-3 | all 6 listed, 0 failed/skipped | 6 | 1 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-7 | S3 | CP-5 | portable-report-first-regression | `/*/*/AgentTaskDeadSessionReconciliationTests/(a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing*)\|(a_stopped_session_with_transcript_but_no_report_still_fails*)\|(the_predicate_and_the_attention_projection_agree_on_every_case*)` | R-4 | all 3 listed, 0 failed/skipped | 3 | 2 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-8 | S3 | n/a | portable-client-contract | `pwsh -NoProfile -File scripts/typecheck-client.ps1` | V-12 API contract | exit 0, both client projects typecheck | n/a | 2 | true | n/a |
| CP-9 | S4 | `tests/Antiphon.PtyHost.Tests -> bin-card1619-host-intent/` | linux-stop-intent-ordering | `/*/*/Card1619StopIntentTests/*` | V-13 | all 8 listed, 0 failed/skipped | 8 | 6 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-10 | S4 | `tests/Antiphon.SessionRunner.Tests -> bin-card1619-runner-intent/` | linux-stop-intent-routes | `/*/*/Card1619LinuxStopIntentTests/*` | V-14 | all 6 listed, 0 failed/skipped | 6 | 6 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-card1619-attribution/` | portable-stop-attribution | `/*/*/Card1619StopAttributionTests/*` | V-15 | all 6 listed, 0 failed/skipped | 6 | 9 | true | TUNIT_MAX_PARALLEL_TESTS=1 |
| CP-12 | S4 | CP-10 | linux-native-cgroup-v1-attribution | `/*/*/Card1619LinuxCustodyStopIntentTests/*` | V-16 | all 2 listed on owned cgroup-v1 fixture, 0 failed/skipped | 2 | 2 | true | TUNIT_MAX_PARALLEL_TESTS=1 |

## Exact execution commands

Run commands from the checkout root. Bootstrap the checkpoint tool once through
the build-slot gate; use its built DLL afterward (the checkpoint executor takes
its own slots). The table is the closed test roster; preparatory graph/EF commands
are explicitly accounted above. No product test was run by this Plan.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-checkpoint-tool -SlotWaitMinutes 10 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1619-tools/
$tool = 'tools/Antiphon.Checkpoints/bin-card1619-tools/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md'
$source = (git rev-parse HEAD).Trim()
$runKey = 'card1619-' + [guid]::NewGuid().ToString('N')
$manifest = '.antiphon/' + $runKey + '.yaml'
dotnet $tool import --plan $plan --out $manifest
if ($LASTEXITCODE -ne 0) { throw 'Plan import failed' }
$yaml = Get-Content -LiteralPath $manifest -Raw
$yaml = $yaml -replace '(?m)^  slotWaitMinutes: 45\r?$', '  slotWaitMinutes: 10'
$pins = @{ 'CP-1'=4; 'CP-2'=8; 'CP-3'=8; 'CP-4'=8; 'CP-5'=12; 'CP-6'=2; 'CP-7'=4; 'CP-8'=4; 'CP-9'=8; 'CP-10'=8; 'CP-11'=10; 'CP-12'=4 }
$yaml = [regex]::Replace($yaml, '(?ms)^- id: (CP-\d+)\r?\n.*?(?=^- id: CP-|\z)', {
    param($match)
    $id = $match.Groups[1].Value
    [regex]::Replace($match.Value, '(?m)^  timeoutMinutes: \d+\r?$', ('  timeoutMinutes: ' + $pins[$id]))
})
Set-Content -LiteralPath $manifest -Value $yaml -NoNewline
```

Before execution, add valid per-row timing metadata to the generated YAML:
absolute `ANTIPHON_TEST_PHASE_ROOT` under this run's ignored timing directory,
`ANTIPHON_TEST_PHASE_RUN_ID=$runKey`, group equal to the table Group,
`ANTIPHON_TEST_PHASE_ATTEMPT=1`, and suite equal to the test project assembly.
This changes only environment/timeout metadata, never filters, Min or roster.
Retain the imported and pinned manifest alongside receipts. The following are
separate runs, each executed only after its source group is committed:

```powershell
# S1, Linux. Red witness commit uses the same selection; retain its intended red.
dotnet $tool run $manifest --rows CP-1 --expected-source-sha $source --serial --total-timeout 12 --hard-wall-timeout 22 --max-wait 45s --heartbeat 15s
# S2, Linux.
dotnet $tool run $manifest --rows CP-3,CP-4 --expected-source-sha $source --serial --total-timeout 24 --hard-wall-timeout 34 --max-wait 45s --heartbeat 15s
# S3, Linux (native end-to-end cases plus portable server/client cases).
dotnet $tool run $manifest --rows CP-5,CP-6,CP-7,CP-8 --expected-source-sha $source --serial --total-timeout 29 --hard-wall-timeout 39 --max-wait 45s --heartbeat 15s
# S4, Linux; CP-12 additionally requires the owned cgroup-v1 fixture.
dotnet $tool run $manifest --rows CP-9,CP-10,CP-11,CP-12 --expected-source-sha $source --serial --total-timeout 29 --hard-wall-timeout 39 --max-wait 45s --heartbeat 15s
# Final source, native Windows. Refresh source/tool/manifest on that checkout.
dotnet $tool run $manifest --rows CP-2 --expected-source-sha $source --serial --total-timeout 16 --hard-wall-timeout 26 --max-wait 45s --heartbeat 15s
```

After **each** run invocation returning 75, use its printed exact run directory:

```powershell
dotnet $tool wait --run <printed-run-directory> --expected-source-sha $source --max-wait 45s --heartbeat 15s
```

Continue waiting until exit is not 75 before starting the next group or ending
the task. Do not leave an executor orphaned. Exit 4 means not run/blocked by slot
admission, never retry unleased. Preserve every unedited CHECKPOINT line, actual
count, TRX and verified source/build receipt; run the standard evidence-diff guard
for Code/Review. Delete only producer-owned alternate outputs after joining all
work. Refresh `$source` and manifest for every newly committed slice; never reuse
a previous source's receipt. Unknown native/cgroup prerequisites stay owed.

## Plan validation and publication

The Plan-only import at `df2178a7aeb87bc4727c69a94d9cdb543a8b2a53` accepted **12 rows**, exit 0, through a granted
build slot. The bootstrap held its slot for 30 seconds. Five compiler warnings
were emitted in existing checkpoint/HostCleanup sources; no product tests ran.
The output was isolated under `/tmp/card1619-plan-build/`; generated YAML stays
ignored. Final import after the documentation refinements is recorded at handoff.

Placement reads on 2026-10-30 succeeded for both required API routes. The current
default selected an eligible Linux runner; an eligible Windows lane was available
and another Linux lane was drained/unavailable. These are observations, not a
fleet pin. Re-read before dispatch, omit `-Runner`, and set `-Platform Linux` or
`-Platform Windows` only for their native rows; portable work omits Platform.

`git fetch origin master` observed
`698bf1b73e8abf449d5569ea74ac8c82e80f92a1`. The required master merge-tree check
has 12 inherited conflict paths from the RC-derived base, confirmed by repeating
it at `4e67121ef7b85a563c5563cf5fc6aa769cb45743`; the new plan path is not among
them. Do not merge this branch wholesale into master. The RC merge-tree check
against `e4c9547e10e3da59fd20684b49c029cdc6063fa8` was clean. Recheck the actual
landing tip; the separate master port is intentional, not a hidden landability
claim. CARD-1610 reference read at `76c583946d5c594052305da87a8b99e85f8639bd`.

## Review and handoff

Read the investigation at `docs/investigations/2026-10-30-codex-session-exit-no-report.md`.
Review the completed plan with Opus before Code. Resolve placement from `GET /api/runner-defaults` and `GET /api/session-runners`; use portable, Linux-native and Windows-native lane names, not a fixed fleet location.

Opus should judge D-6's bounded managed dependency patch, D-7's legacy/raw code
versus signal contract, D-9's refusal on lost provenance, generation-safe terminal
enrichment, native-asset propagation, and the 150-minute slices. No unanswered
operator decision prevents this plan: these are explicit design decisions for
review, not silently assumed implementation facts. On acceptance, commission
S1 Code from the current pinned RC with this artifact; follow with S2-S4 and
independent native Review. Product tests, native gates and measured performance
remain owed, and the old mixed-version fleet cannot be credited as fixed.
