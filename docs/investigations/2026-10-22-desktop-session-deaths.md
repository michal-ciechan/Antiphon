# Desktop session deaths, 2026-10-22

Debug task `99c08598`, investigated from `feat/card-task-99c08598` at base
`a38e1e1bd0f515d6ae7a6682f537a5fe5e4cdd49`. All incident times below are **UTC**;
the desktop was on BST (UTC+1). Evidence collected 09:03-09:29 UTC.

## Findings

1. **The qualification monitor was killed by the scheduled CLI maintenance path.**
   At 07:00 UTC / 08:00 local, `ClaudeBot Daily Update` invoked a script that
   force-stops every running non-WindowsApps `claude.exe`. Thirteen independent
   desktop sessions exited with `-1 / ProcessExited` within 3.329 seconds. This
   includes both qualification monitors. The runner and their pty-hosts survived
   long enough to observe the exits and complete the normal acknowledgement path.
   The daily update log confirms the update to Claude Code 2.1.307 and the session
   restart step. **CARD-1477** tracks the repair in the separate ClaudeBot repository.
2. **The failed Investigate launch hit its 60-second visible-output readiness
   deadline; Antiphon then killed it.** Its retained buffer contains only terminal
   control/title sequences and its normalized transcript is empty. The reason
   Claude failed to produce visible output within that deadline is not established
   by retained evidence. Load sensitivity is real in the implementation, but an
   OOM or CPU-starvation diagnosis for this individual launch would overstate it.
3. Memory pressure was present, including available RAM below the build broker's
   6144 MiB floor. It does not explain the synchronized maintenance kills. The
   historic CPU/commit series needed to quantify the failed launch had already
   aged out of the runner's 30-minute in-memory ring. **CARD-1478** proposes a
   separate, default-off queued-worker admission guard, not a cure for CARD-1477.

No source, live scripts, settings, Scheduled Tasks or services were changed. No
builds/tests or live provider launches were performed. The qualification tree,
supervisor PID 296224 and its children, clone, state and logs were not accessed or
modified. Qualification observations quoted here came only from previously
recorded session transcripts returned by the server API. No provider home,
`auth.json`, credential file or private-notes route was opened.

## Incident timelines and transcript evidence

### A: Investigate 38656cc0, session e7f25c16-4fe8-425f-ad50-c9044da2edb7

| UTC | Evidence |
|---|---|
| 07:06:52.025 | Task created; audit says explicit desktop, despite default server2. |
| 07:07:40.490 | Task's stored Dispatched timestamp; worktree/preparation followed. |
| 07:08:47.429 | Server log records dispatch to this session. |
| 07:08:50.364 | Pty-host PID 221420 starts. |
| 07:08:51.335 | Pty-host launches native Claude PID 343180 with ModernConPty. |
| 07:09:52.419 | Child exits `code 1, reason KilledByRequest`. |
| 07:09:52.514 | Server logs `Agent process did not become ready`, from `WaitForReadyOrThrowAsync`. |
| 07:09:52.558 | Pty-host exits after runner shutdown acknowledgement. |
| 07:13:48.991 | Task Failed event; dead-session reconciliation reports the earlier failure. |

The apparent six-minute launch timeout is a task-state interval, not the ready
budget. The actual launched child survived 61.084 seconds. The runner still
returned its terminal DTO during this investigation: `Exited`, PID 343180,
hostPid 221420, exitCode 1, exitReason `KilledByRequest`.

`GET /api/sessions/{id}/transcript?since=0` returned **0 entries, lastSequence 0**.
Both server and runner buffer endpoints returned lastSequence 4 with this entire
buffer (ESC escaped for readability):

```text
ESC[1t ESC[c ESC[?1004h ESC[?9001h ESC]0;claude ESC\ ESC7 ESC[r ESC8 ESC[?25h
```

These are terminal controls and an OSC title, not a Claude composer or response.
The visible-output predicate excludes them. There is no transcript tail to
interpret as user work, no retained sign-in/trust/error text, and no input-probe
failure log for this session. The failure occurred before the composer probe.

Identical retry `77c15d47`, session `a55de0c0-b8b0-41f8-b4ef-7adbc171e413`,
launched PID 12748 at 07:17:16.525. Its first normalized UserPrompt is at
07:18:04.944, 48.419 seconds later; that includes readiness and delivery, not just
provider startup. It completed with a marked report at 07:33:52.135, followed by
normal requested cleanup at 07:33:56.813. It ran the same rc source base as the
failed task. This proves the launch problem was transient; it does not isolate
load, CLI initialization or another cause of the initial absence of output.

### B: monitor c58e21f5, session 9b664084-88e9-4fa7-af6c-135822972c84

| UTC | Evidence |
|---|---|
| 05:46:11.663 | Stored task dispatch timestamp. |
| 05:46:22.815 | Pty-host PID 208816 starts. |
| 05:46:23.271 | Native Claude PID 36996 launches. |
| 06:58:59.851 | Transcript sequence 100: last completed tool result. |
| 06:59:02.187 | Sequence 101: starts a 7.5-minute qualification-status poll, tool timeout 540000 ms. |
| 07:00:17.539 | Pty-host observes `Child exited (code -1, reason ProcessExited)`. |
| 07:00:19.289 | Runner disconnects; pty-host exits on acknowledgement. |
| 07:00:21.592 | Server session endedAt. |
| 07:05:40.971 | Task fails through dead-session reconciliation. |

The child lifetime was **73m54s**, whereas the recorded task interval was
**79m29s**. The last tool call has no ToolResult or report; the retained PTY tail
is the working spinner, without a fatal/OOM message. The runner terminal DTO
also returns exitCode -1 and `ProcessExited`. This was not a ready deadline or
the Test role's 240-minute ceiling.

### The shared termination and its owner

The pty-host log inventory found these thirteen `ProcessExited`, code -1 events:

| UTC | Session |
|---|---|
| 07:00:16.942 | `4f71db42-71fd-4f64-b278-64a17b29b960` |
| 07:00:16.943 | `d1880154-dd37-45d7-a705-a0ccbf4d8b03` |
| 07:00:16.944 | `b788e4ec-1635-4451-8784-3067c5b17064` |
| 07:00:17.037 | `cea73d57-3072-4cc2-8cb0-ab859e7d3415` |
| 07:00:17.040 | `b8aad212-ca2a-4f4e-9c7e-604a0ba1f8c7` |
| 07:00:17.118 | `384d59ee-45b7-4ae4-9bc3-a271a8bee8d4` |
| 07:00:17.151 | `8c1d3da6-7fad-4c7b-8897-e2c7c18914d6` |
| 07:00:17.539 | `9b664084-88e9-4fa7-af6c-135822972c84` |
| 07:00:17.751 | `35112e15-a0e1-4600-8656-64659b8b16bd` |
| 07:00:17.801 | `1d86dbf5-14c1-478a-8e01-c966267b795d` |
| 07:00:17.922 | `daec6a72-771d-4a66-9634-fbe474d87464` |
| 07:00:18.677 | `e1626153-65bd-46eb-8257-6b67d44f6e32` |
| 07:00:20.271 | `5421385a-26bd-42ca-882a-78d835a865dd` |

Read-only Scheduled Task inspection gives `ClaudeBot Daily Update` last run
2026-10-22 08:00 local, result 0, next run 2026-10-23 08:00 local. Its action is
PowerShell running `C:\src\ClaudeBot\scripts\daily-update.ps1`.

The causal chain is explicit in those scripts:

- `daily-update.ps1:43` invokes `update-claude.ps1 -NoElevate` and discards the
  detailed output. Its header explicitly warns that the daily 08:00 job kills
  every Claude Code CLI session.
- `update-claude.ps1:105-115` queries every `claude.exe` with a command line other
  than WindowsApps, then calls `Stop-Process -Id $p.ProcessId -Force`. It does not
  ask Antiphon whether a session is Working, standing, or running a qualification
  monitor. It does not distinguish sessions by ownership.
- Lines 148 and 158-160 kill on a locked-update fallback and after a successful
  update respectively. Lines 165-173 perform another 90-second old-binary sweep.
- The latest block in `C:\Users\lndco\.daily-update.log` starts
  `Daily tool update - Thu 22 Oct 08:00`, reports browser-harness 0.1.0 -> 0.1.13,
  and says `Claude Code: now 2.1.307 (Claude Code) (all sessions restarted; ClaudeBot relaunched)`.
  The log was last written at 07:01:51 UTC.

The published [PowerShell 7.6 Stop-Process implementation](https://raw.githubusercontent.com/PowerShell/PowerShell/v7.6.0/src/Microsoft.PowerShell.Commands.Management/commands/management/Process.cs)
calls `Process.Kill()`. The [.NET 10 Windows implementation](https://raw.githubusercontent.com/dotnet/runtime/v10.0.0/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs)
calls `TerminateProcess(handle, -1)`. This makes the updater's termination path
consistent with the exact observed exit code. The inspection shell was
PowerShell 7.6.7 / .NET 10.0.13; these links identify the version-family
implementation, not an incident-time binary checksum. No kill probe was run.

This combination of scheduled execution, matching explicit termination code,
successful maintenance summary and synchronized exits establishes the operational
cause with high confidence. A per-PID kernel caller audit is unavailable: Security
log access was denied and TaskScheduler/Operational is disabled; the updater
throws away its per-PID stop output. Do not represent the exit integer alone as
proof of OOM or as an audit of the terminating PID.

The updater's comment that Antiphon automatically relaunches dispatched workers is
unsafe for delegated work: this task actually failed without a report. Standing
agents recovering does not restore the monitor's interrupted tool call.

### The other two observations are different lifecycle events

`a5303e1a` failed at 05:45:56.866 under the 240-minute Test ceiling. Its failure
explicitly states the session **was not killed**. Its transcript continued through
06:56:07.439, and its pty-host recorded the later 07:00:20.271 maintenance kill.
Thus its earlier task failure is neither an earlier OOM nor a process death.

Server2 Review `6e65c8a6`, session `ba1e8fa7-bacb-45ca-a4b1-41e55084d542`, had
35 transcript entries. Sequence 35 at 04:40:41.506 is a Bash call preparing
mutation scratch copies, with no returned result. The task was Canceled at
05:39:53.638. The previous tool result recorded ordinary checkpoint receipts
(30/30 and 4/4); it did not prove completion of the mutations. This is an
unreturned tool call followed by cancellation on another host, not evidence for
the desktop death mechanism. No remote mutation process or workspace was touched.

## Windows readiness and exit interpretation

The relevant server source files are unchanged between live server SHA
`ceb6e3d6b78471db6831258699c493b8eb2d619b` and this investigation base:

- `AgentRegistrySettings.cs:12-59`: visible-output/quiet MaxWait **60000 ms**,
  quiet **5000 ms**, minimum total launch wait **9000 ms**, composer probe
  **90000 ms**, polling 250 ms, retype 30000 ms / at most three writes, clear
  timeout 10000 ms. Trust and effort settlement each default to 15000 ms.
- `RunnerClaudeAdapter.WaitForReadyAsync` first awaits visible output plus quiet;
  only after success does it pass the floor and enter the trust/effort/input-probe
  sequence. Consequently there is no single universal 60-second bound for every
  successful/failed Claude startup phase.
- `RunnerTerminalSession.cs:212-260` uses `DateTime.UtcNow + maxWait`, 50 ms polling
  and runner snapshot/sequence reads. CPU starvation, delayed HTTP reads or a
  provider that paints late consume wall-clock time; the budget does not pause
  for host pressure. Reads can make actual completion exceed the nominal bound.
- `AgentSessionService` throws for readiness=false and its launch catch calls
  `KillAndDisposeAsync`; this matches A's code 1 / KilledByRequest.

The retained A buffer identifies the first visible-output gate specifically. The
60-second checked-in setting and observed 61-second lifetime agree; no complete
runtime configuration dump was taken and no setting was changed.

The production runner advertises SHA
`81cfb267a0af5c0ddb65e8b238af355067bee9cc`, processStartUtc
`2026-10-19T15:56:41.3474292Z`. PID 223888 still had that start time during the
read-only check. The relevant exit implementation files also have no diff from
that runner SHA to this investigation base:

- `ModernConPtyConnection.OnProcessExited` forwards the Windows process's
  `ExitCode`; it does not invent -1 to mean an unknown cause.
- `PtyAgentRunner` classifies a plain observed exit as `ProcessExited` unless its
  own memory-limit/requested-kill evidence says otherwise.
- `WindowsJobObject`'s explicit memory kill code is **137** and the reason is
  `MemoryKilled`. A's requested cleanup and the retry's cleanup both yielded 1.
  Neither is the observed B shape.

There is no production `ParallelLimiter<ProcessSpawnLimit>` in server/src; that
is test scheduling, not an agent-process killer. The pty-hosts logged the child
exits before runner disconnection and shutdown acknowledgement, excluding a
pty-host shutdown as the initiating recorded event. No runner restart occurred.

## Memory, CPU, commit, event logs

The monitor's authoritative transcript preserves these historical runner broker
observations, without reading the qualification's files:

| UTC | Available MiB (broker) | Floor MiB | Transcript sequence |
|---|---:|---:|---:|
| 06:43:09 | 5564 | 6144 | 91 |
| 06:45:40 | 5231 | 6144 | 91 |
| 06:48:11 | 5186 | 6144 | 91 |
| 06:50:41 | 4552 | 6144 | 91 |
| 06:53:30 | 5360 | 6144 | 95 |
| 06:56:00 | 5127 | 6144 | 95 |
| 06:58:31 | 6189 | 6144 | 95 |

Memory recovered above the broker floor in the last available sample, 106 seconds
before B's exit. This does not prove the instantaneous value at death, but it
contradicts treating every -1 as a documented out-of-memory termination. The
transcript's `cpu=...` values are accumulated process CPU seconds, not system CPU
percent; they cannot establish a saturation percentage.

`GET /host-stats` at 09:09:44 UTC confirmed retentionMinutes=30 and interval=5 s.
That later observation was CPU 87.66%, available 7587688448 bytes, total physical
34298142720 bytes, commit charge 40506290176 bytes and commit limit 55906156544
bytes. The 30-minute CPU average was 87.16%, max 100%. These establish ongoing
load **at collection**, not at 07:00 or 07:09. Windows `swapUsedBytes` and
`swapTotalBytes` on this API mean commit charge/limit. The brief's roughly 35 GB
commit against 31.9 GiB RAM is not itself exhaustion: compare commit to its limit,
and physical pressure to available RAM. No historical commit limit was retained
for the incident minutes.

Read-only `Get-WinEvent` results:

- Application and System warning/error/critical events, 05:30-07:30 UTC: no
  Claude or pty-host application crash and no resource-exhaustion event. The
  relevant Application Error / .NET Runtime pair at 06:55:44-45 names
  **Antiphon.Messaging.FakeGateway.exe**, exception 0xc0000005, not Claude. An
  Aspire health request also timed out around then. These show another incident
  nearby, not the cause of these session exits.
- System event 2004 since 00:00 UTC: zero matches.
- Defender/Operational, 06:45-07:20 UTC: no termination/detection evidence. A
  scheduled quick scan at 07:07:30 was immediately skipped because of a recent
  successful scan. Other events are routine health/history maintenance.
- CodeIntegrity/Operational in that window: no matching events.
- TaskScheduler/Operational disabled; Security process auditing inaccessible to
  this unelevated session. No audit settings or privileges were changed.
- PowerShell/Operational around 07:00 recorded a separate bob-daily-sync script
  and system errors. That is not the updater's stop receipt; the task metadata,
  daily-update summary and source chain above identify the relevant maintenance.

Conclusion by candidate: B is explained by the external updater's force-stop;
A is explained at the platform boundary by readiness expiration and requested
cleanup. There is no positive evidence of an OOM killer, per-session memory cap,
handle limit, antivirus termination, runner restart or process-spawn limiter as
the initiating cause of either. The underlying no-visible-output stall in A and
its exact pre-launch memory/CPU/commit remain unknown.

## Placement and smallest safe repairs

`DefaultRunnerRoutingPolicy` preserves explicit local/remote requests and
defaults; task creation checks the platform. `HostBudgetService` and
`AgentTaskDispatcher` impose count limits. `SessionRunnerRuntime.StartAsync`
checks platform and per-session identity/custody before launch. None of those
paths consumes a memory-floor admission rule. The only live-memory floor found
is `BuildSlotBroker` using `IHostMemoryProbe`, whose Windows implementation reads
`GlobalMemoryStatusEx.AvailPhys`. A build lease does not gate agent creation.

**First repair, CARD-1477:** make unattended ClaudeBot CLI maintenance preserve
active processes. Use a non-disruptive mode from daily-update, defer a locked
installer, and remove stop/sweep/relaunch effects from that path. Keep any
explicitly requested manual disruptive workflow separate. A fake-inventory
offline witness must fail on the current default's stop calls and pass after
repair. Scope: two owner scripts plus one isolated fixture; 30-60 minutes Code.
The task still exists and its next scheduled run was 08:00 local on 23 October;
this Debug assignment did not authorize changing it.

**Independent resilience, CARD-1478:** delay a new desktop delegated-worker
process while an enabled, operator-configured floor exceeds a fresh available
memory observation. Leave the task Queued with a deduplicated Held reason and
retry through the existing tick, before claim/worktree/lease/provider effects.
Unknown/stale is an explicit hold when enabled; zero disables the new policy.
Keep remote work, warm-session reuse, existing sessions, provider choices and
Windows binding intact. This limited cached-snapshot guard is backpressure,
not an atomic host-memory reservation or a guarantee for every session entry
point. Plan must bind both ready-waiter and ordinary launch paths. Scope: one
45-60 minute Code task. It cannot prevent CARD-1477's external kill and is not
proof that A would have been avoided. Do not simply raise readiness timeouts.

Both cards are **Backlog**, with red-witness shapes, named cases and closed
checkpoint lists in their descriptions. No tests described there were run here.

| Card | Closed proposed verification | Expected scope |
|---|---|---|
| CARD-1477 | CP-1: `test-update-claude-safety.ps1 -Case DailyDefaultPreservesLiveSessions`, exactly one assertion-red case committed before fix. CP-2: `-Case AllSafety`, eight named passing offline cases. | No build or live updater; all process, CLI, delay, launch and network effects stubbed. |
| CARD-1478 | CP-1: `/*/*/DesktopLaunchMemoryAdmissionTests/Low_memory_holds_before_launch`, isolated red build. CP-2: `/*/*/DesktopLaunchMemoryAdmissionTests/*`, separate green build, eight named methods. | Real dispatcher with isolated DB schema, fake clock/snapshot and spy launcher; checkpoint tool/build-slot gate. |

Complete board searches preceded filing: `memory` (103 matches), `daily update`
(0), `update-claude` (0), `session launch memory` (0), `memory pressure` (2),
`memory floor` (2), `Claude Code CLI` (7), `readiness timeout` (6); all reported
truncated=false. Full candidate details were inspected for CARD-0589, CARD-0718,
CARD-1424 and CARD-1461. They cover resource/build pressure, stats or capability
freshness, not this unattended termination or the proposed session admission.
CARD-1461's completed fallback was explicitly not activated in its card outcome.

## Evidence and reproducibility

The documented log owners were used: `docs/logs.md` and `docs/ops-http.md`.
Raw task-owned evidence is ignored under
`C:\Antiphon\worktrees\card-task-99c08598\.antiphon\task-99c08598\`:
`task-*.json`, `transcript-*.json`, `buffer-*.json`, `runner-session-*.json`,
`runner-capabilities.json`, `runner-host-stats.json`, `runner-build-slots.json`,
`events-*.json`, `session-matches.log`, `key-events.log`,
`simultaneous-exits.log`, `daily-update-today.log` and card search/create receipts.
These generated payloads are not committed. This document retains the necessary
findings independently of those temporary files.

Primary files for exact incident lines:

- `C:\logs\antiphon\session-runner\pty-hosts\logs\<sessionId:N>.log`.
- `C:\src\Antiphon\server\logs\antiphon-20261022_001.log`, line 8007
  readiness exception and line 9735 dead-session reconciliation; the non-rolled
  `antiphon-20261022.log` includes the 08:00 local mass-exit reconciliation.
- `%TEMP%\antiphon-logs\session-runner-20261022.log` and
  `C:\src\Antiphon\logs\session-runner.log` for runner continuity; the latter's
  application-started receipt names PID 223888 and its 19 October start.
- External owner script SHA-256 at inspection:
  `daily-update.ps1` = `EA81EB0567BDD06C19534EA7E0F86DB2E35F4588D423CD57F25B9A71F2AFF803`;
  `update-claude.ps1` = `D5E78D9BA9D1896074259121194C108C399C310116090247557BA8140E424BED`.
  Both scripts' last-write times predate the event.

Re-read transcripts using `GET /api/sessions/{id}/transcript?since=0` and inspect
their `entries`/`lastSequence`; use `/api/sessions/{id}/buffer` for the retained
PTY diagnostic. Runner `/sessions/{id}` is supporting process evidence. Supply
the inherited task header without printing it; use 10-15 second HTTP deadlines.
Use targeted `rg` on the above log paths, including the rolled `_001` file.
Event logs were bounded by the UTC windows above. Task-owned reader processes
had 35-55 second hard limits after the initial repository reads; all were joined.

--- next stage ---
next: plan
handoff: Prioritize CARD-1477 in the owning ClaudeBot repository, with an offline zero-kill witness and explicit deployment handoff before the next daily run. Bind CARD-1478's independent narrow dispatch guard and eight checkpoint cases without treating it as the cause of the mass exit. Preserve A's unresolved startup cause; do not touch the running qualification.
artifact: docs/investigations/2026-10-22-desktop-session-deaths.md
