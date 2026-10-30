# CARD-1619: Carry Linux PTY exit signals end to end

Date: 2026-10-30. Plan source: `4e67121ef7b85a563c5563cf5fc6aa769cb45743`.
Status: initial skeleton; source audit and executable verification design in progress.
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

Add an immutable `PtyExitObservation(ExitCode, ExitSignal)` at the PTY seam.
`IPtySession.Exited` and `PtyAgentRunner` consume it. Cache completed exit before
notifying; subscribe then replay a completed snapshot with exactly-once completion
so an immediately exiting child cannot lose its result before subscription.
Keep `PtyAgentRunner.Exited`'s integer compatibility task where existing consumers
need it and add structured completion; known signal maps to a documented nonzero
compatibility result only on that legacy adapter task. Host/session fields retain
the actual observed code and separate signal. Windows `ModernConPtyConnection`
supplies signal zero with the same signed exit code; no Windows kill or job changes.

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
| `AgentTaskDispatcher.Pool*`, `AgentSessionJanitor*`, `TerminalRunnerSeatReleaseService` | Ownership/liveness/working/delivery gates | No code-zero release authority; preserve killed/warm/owned states and all conditional holds |
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

Instrument existing authorized Linux entry points only: host KillMessage and
owner/shutdown stop; runner `KillPidBestEffort`, `KillHostIfStillOurs`, pending/
orphan Herdr kills (their existing pid/start validation stays); and explicit
Linux custody termination/recovery (`IPtyCustodyContainment`,
`ILinuxCustodyRecovery`, runner custody ledger). Ordinary observation, sealing
and root exit must not begin a new stop. The cgroup helper sends SIGKILL (v2
`cgroup.kill`; v1 freeze/read/kill/thaw). Record intent before invoking it, but
helper exit zero is only an issued operation: retain CARD-1614's post-drain
accounting, placement proof, Unknown outcomes and retained cgroups.

CARD-1614's plan is in this checkout and its current product files match the
fetched RC ref for the PTY/host/runner paths inspected here. Revalidate at Code
dispatch. Linux custody remains unadvertised until its separate native gates;
this card cannot enable it. Keep Windows seal nonterminating, job kill-on-close,
24-hour tracked linger, and live-unadoptable-host operator ownership unchanged.
No new input-waiting session or hold is introduced. CARD-1610's holds retain their
own release policy; with parking disabled they have no automatic release deadline.

## Slices

Source audit will name concrete files, tests, estimates and landing dependencies for transport, persistence/text and kill-intent slices.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`.
This Plan runs no product tests. Its only planned build/tool validation is the requested checkpoint import, through the host build-slot gate. Code measurements are currently unavailable, not measured p95. The completed design will give exact test selections, bounded collection, phase/setup ownership, whole-minute estimates and provisional exceptions tracked by CARD-1619; derive final timeouts near twice measured p95. Any selection or run over 30 minutes requires Debug with retained timing evidence.

## Verification design

Required witnesses: real Linux PTY external SIGKILL/SIGTERM, normal zero/nonzero exit, Antiphon's own stop, both protocol skew directions, durable manifest/session propagation, honest failure text, and native Windows regression. Exact roster and importable checkpoints follow.

## Review and handoff

Read the investigation at `docs/investigations/2026-10-30-codex-session-exit-no-report.md`.
Review the completed plan with Opus before Code. Resolve placement from `GET /api/runner-defaults` and `GET /api/session-runners`; use portable, Linux-native and Windows-native lane names, not a fixed fleet location.
