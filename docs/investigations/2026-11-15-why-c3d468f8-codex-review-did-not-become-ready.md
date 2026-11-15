# Why Codex Review c3d468f8 did not become ready

**Status:** finding. No product change. Debug task `f1cf9f3c`.

**Date:** 2026-11-15. **Incident task:** `c3d468f8-71b0-49a6-9f2f-c23835302a95` (Worker/Review, Codex, Frontier = gpt-6-astra, runner server2). **Session:** `13d3c57e-96dd-4c52-ae75-e5c34ecb0f42`.

**Server:** `GET /api/version` = `64e3d78c487d51570f708492ab67b5db6d0de9a9` (capabilities `land-v2`, `operator-shutdown-v1`). **Runner:** phone-home `server2`, build `ab7af12c699039270af7e22a5d7fd794035bc325`, process up since 2026-11-13T07:35:46Z, Codex CLI 0.160.0.

## Verdict

The Codex TUI painted one welcome frame and then wrote nothing. The positive ready gate (CARD-0574) requires a settled model/effort/cwd footer and correctly refused that frame. The stop actor is Antiphon's own launch-failure `KillGeneration` (`SystemRequest`), which the pty host carried out as SIGKILL (`KilledByRequest`). The sentence "signal unknown" is how the server described that kill after the durable stop record failed to replace and the runner liveness sweep stored `ProcessVanished` with a null signal.

This is not a Frontier model, quota, version, or auth refusal. It is not a ready budget that was too short for a slow paint. It is not the 04:26Z AppHost restart, a seat release, or a phone-home drop.

The next Codex Frontier launch on this runner is likely to become ready. The same CLI painted `GPT-6-Astra xhigh` earlier the same day and kept writing, and later Codex launches after this failure also reached a model footer. A repeat needs the TUI to freeze again before that footer.

Do not widen `CodexReadyMaxWaitMs`. Do not treat the footer-less welcome frame as ready.

## Classification

| Bucket | Result |
|---|---|
| (a) Codex Frontier launch incident (model, quota, version, auth) | Ruled out. `gpt-6-astra` is in `GET /api/model-availability` `available`. The only hold is `ClaudeCode` / `fable`, manual, open-ended, from 2026-11-09. `GET /api/routing-pins` returned no rows. The runner's Codex auth probe logged `loggedIn=true error=null` at 05:48:32Z and again at 05:49:32Z, immediately before launch. CLI 0.160.0 started and painted. `codexCliVersionError=stderr_output` is advisory display policy (`CodexCliObservation.IsAdvisory`) once a version parses; it does not refuse the launch. |
| (b) Host-load readiness timeout | Host load was high, and it is not what the gate was waiting on. The welcome frame was on disk 0.3 s after the child started. No later PTY byte arrived for the rest of the process lifetime. Extra ready time would have watched the same frame. |
| (c) Who asked for the stop | Antiphon, on the not-ready path. Phone-home `KillGeneration` `427e3b7d-1f0c-417e-af79-63ded95c93bf` at 05:51:38.505Z, `outcome=killed`, same generation `2026-11-15T05:49:32.105597Z`. |

## Timeline (UTC)

| UTC | What | Source |
|---|---|---|
| 05:48:32.452 | Codex auth probe `loggedIn=true error=null` | `/state/runner-logs/session-runner-20261115.log` |
| 05:49:32.405 | Second auth probe, same result, at dispatch | same |
| 05:49:32.105 | Session accepted | runner session row; launch log |
| 05:49:37.585 | Pty host start, pid 8141 | `/tmp/antiphon-pty-hosts/logs/13d3c57e96dd4c52ae75e5c34ecb0f42.log` |
| 05:49:37.971 | Child start | transcript sidecar `childStartUtc` |
| 05:49:37.998 | Launched `/usr/local/bin/codex`, child pid 8178, UnixPty | pty-host log |
| 05:49:38.018 | Phone-home Launch `df959699-234e-43db-80df-29b119002b82` `outcome=started` | runner log |
| 05:49:38.269 | ANSI log reaches 1545 bytes and never grows | `/state/session-runner/13d3c57e96dd4c52ae75e5c34ecb0f42.ansi.log` |
| 05:51:38.381 | Stop-intent temp written (see below) | `/tmp/antiphon-pty-hosts/stop-intents/…json.tmp-…` |
| 05:51:38.466 | `Stop diagnostic record unavailable; issuer memory retained.` | pty-host log |
| 05:51:38.504 | Liveness sweep: process vanished without an exit event | runner log |
| 05:51:38.505 | Phone-home KillGeneration `outcome=killed` | runner log |
| 05:51:39.019 | Child exited code 0, signal 9, reason `KilledByRequest` | pty-host log |

Launch ack to kill is 120.487 s. Child start to the stop-diagnostic warning is 120.495 s. No prompt was typed: sidecar `firstInputAtUtc` is null, `resumeLaunch` is false, `transcriptPath` is null, runner `transcriptBound=false`, `transcriptUnboundReason=awaiting-input`, `lastSequence=13`.

The task row Failed at 05:54:52Z with `Session died before the task settled: its session is Failed (Agent process did not become ready.; Antiphon stop requested; signal unknown (SystemRequest)).` Events before that are the explicit Frontier override on Codex (`gpt-6-astra`) at 05:49:32Z, workspace holds, and a warning that the Review/Medium row was not consulted. There is no transport-loss event and no restart-resume event.

The 04:26:16Z phone-home close matches the caller's AppHost restart and is more than an hour before this launch. The next `Phone-home connection ended` line is 06:19:01Z. The socket stayed up across the incident. The failure text has no "Resumed launch after a server restart failed" prefix, and the task had no transport-loss warning. `LaunchInteractiveProcessAsync` retries only `PhoneHomeTransportLoss`, which excludes timeouts.

## What the screen showed

The whole ANSI file, stripped, is one Codex 0.160.0 welcome and nothing after it:

```
OpenAI Codex (v0.160.0)
/work/worktrees/task-c3d468f8
permissions: YOLO mode

Show me the bit that's being weird.
› Ask Codex to do anything
```

There is no `model:` row and no `GPT-6-Astra xhigh · <cwd>` footer. `CodexStartupScreen.Classify` returns Unknown for that layout. Unknown cannot settle. Account-bootstrap is a different frame (all three of "account/read failed during TUI bootstrap", "workspace routing discovery timed out", and "-32603") and is not this screen. Sign-in is not this screen.

`CodexReadyWait` (`src/Antiphon.Agents.Pty/CodexStartupReadiness.cs`) returns ready only after a positive layout stays quiet for `CodexReadyQuietPeriodMs` (1000) inside `CodexReadyMaxWaitMs` (60000 in `server/appsettings.json` and `AgentRegistrySettings`). `WaitForReadyOrThrowAsync` then throws `Agent process did not become ready.` The launch catch stamps `SessionTerminationSource.SystemRequest` and calls `KillAndDisposeAsync`. `SessionExitPolicy.Describe` appends `Antiphon stop requested; signal unknown` when the stop is a requested one and both the exit code and the signal on the session row are null.

## Same CLI, same day, footer present

Every other Codex launch in the runner log on 2026-11-15 painted a model footer in the first 400 KB and then grew far past this 1545-byte file.

| Session | Launch (UTC) | ANSI bytes | Footer in the first 400 KB |
|---|---|---|---|
| `58d4a516-4287-4dcb-8319-aa14cd8b4a10` | 02:02:11 | 6,779,825 (mtime 02:53:42) | `GPT-6-Astra xhigh` on `task-78d0` (the Frontier Code task that ran) |
| `88fd4f06-d145-4f35-bb0c-3b3c6f0e30fd` | 02:02:01 | 3,331,043 | `GPT-6.1-Sol high` on `task-1ba18` |
| `22507d6d-be0e-495d-9484-b61bdf15b634` | 06:13:21 | 577,736 at 06:17 | `GPT-6.1-Sol high` |
| `6aa57ec4-b989-4444-9cb0-78a6fa627792` | 06:13:36 | 547,106 at 06:17 | `GPT-6.1-Sol high` |

The 02:02 Astra session is the same CLI version's ready screen, including the Frontier `xhigh` effort. The two 06:13 launches are after this failure. The gate and the CLI can succeed on this runner. This session is the one whose PTY stopped at the welcome frame.

## Stop actor and "signal unknown"

The pty host recorded the kill it was asked for: signal 9, reason `KilledByRequest`, child pid 8178. The leftover stop-intent file is still a `.tmp` (the replace into the durable name did not happen). Its JSON says `durable=true`, `target=1` (Child), `attemptedSignal=9`, `path=1` (ProcessTree), `outcome=1` (Issued), `provenance=1` (LiveIssuer), `targetPid=8178`, `requestedAtUtc` 05:51:38. The pty host then logged `Stop diagnostic record unavailable; issuer memory retained.`

One millisecond before the KillGeneration log line, the liveness sweep marked the session Exited as `ProcessVanished` with a null exit code, null signal, and null stop intent. That is the row `Describe` read. With a requested stop and no signal, the text is `Antiphon stop requested; signal unknown`. The SIGKILL was real. The sentence lost it.

`GET /api/session-runners/server2/slots` at about 06:17Z: `declaredCapacity=10`, `occupied=7`, this session `Exited`, `occupiesCapacity=false`, `orphan=true`. The process had already been started and painted, so a seat was granted. The kill is the launch-failure `KillGeneration`, not a slot-release of a live seat. `StartingGraceMs` (90000) and `AgentGraceMs` (120000) do not issue this kill: the first only skips reconciliation of a young Starting row, and the second applies to a Working agent with no live session.

## Host load

Runner live samples, 24 cores, window 05:49:30Z–05:51:45Z (27 points, 5 s):

| Metric | min | avg | max |
|---|---|---|---|
| load1 | 20.81 | 32.03 | 39.66 |
| cpu percent | 1.64 | 24.31 | 90.94 |

Load1 at the paint (05:49:34Z) was 22.44 and climbed through the high 30s while the PTY stayed frozen. A sample at 06:04:39Z still had about 99 GB of 135 GB memory available, so memory was not the constraint around that hour. CARD-1906's loaded host is real. It does not explain a TUI that finished its first paint in under a second and then produced no bytes.

`Host load append unavailable; live sampling continues` was already logging at midnight (`dropped=29039`) and is a spool-append failure. `GET /api/hosts/server2/load/history` for 05:49–05:52Z returned `complete=false`, no items, gap `binding-unknown`. `GET /api/agent-tasks/c3d468f8-…/load?attempt=1` returned no windows, gap `event-or-sample-gap`. Live `/host-stats` on the runner is the sample source above.

## The 120 s wall versus the 60 s setting

`Agents:CodexReadyMaxWaitMs` in the deployed server's tree is 60000. The kill landed 120.5 s after the runner logged `outcome=started`.

`PhoneHomeLiveConnection.RequestTimeoutFor` gives Launch two minutes and Snapshot the default 60 s. Launch had already returned at 05:49:38, so this is not the Launch RPC sitting out its two-minute timeout. `ReadBoundedAsync` cancels an in-flight snapshot at the ready deadline, and `RequestAsync` observes that cancellation through its linked delay before it reports its own timeout. A snapshot that honors cancel should not add another 60 s after the deadline.

The server warning `codex-startup not-ready reason=… elapsedMs=…` is the measurement that would say whether the wait itself ran 60 s or 120 s. That line is in the server container log. This runner cannot read that container (its docker socket sees only a nested container). An `Agents__CodexReadyMaxWaitMs` override on the server process is equally unreadable from here. Either one can be true. Neither changes the classification: the footer never arrived, and a longer watch of a frozen PTY would not have typed the prompt.

## Recurrence and the cheapest fix

A later Codex Frontier launch on this runner should be treated as likely to become ready. Twelve Codex launches in today's runner log besides this one reached a model footer, including `GPT-6-Astra xhigh` at 02:02Z and two launches at 06:13Z. Recurrence is "this TUI freezes after the welcome frame again," not "Frontier / astra / 0.160.0 cannot pass the gate."

The cheapest correct Antiphon action is no setting change and no retry-budget change. Widening `CodexReadyMaxWaitMs` waits longer on a frame that is already not ready. Counting the welcome frame as ready would type into a composer whose model and effort were never confirmed, which is the hole CARD-0574 closed.

Two follow-ups are worth a decision, and neither is this incident's cause:

1. Say the signal the pty host already knew. The user-facing clause should be able to name SIGKILL / `KilledByRequest` when the stop-intent replace fails and the liveness sweep stores `ProcessVanished` with a null signal.
2. Leave the ready budget at 60 s unless the server `elapsedMs` line shows the wait itself ran 120 s. If it did, that is a deadline bug to fix red-first. This diagnosis could not read that line.

The orchestrator already re-dispatched the review on Grok under the 2026-11-08 failed-dispatch rule.
