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
