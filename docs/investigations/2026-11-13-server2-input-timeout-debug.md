# server2 Input timeout and Review 3199d57c termination

Debug task **8ff425ec-215f-49e6-be1d-4de78f930487**, 2026-11-13 UTC. Read-only investigation; no process, service, configuration, volume, source-code or running-task changes. Two remediation cards were filed. No builds or tests were run.

**Outcome:** the session was killed by generation-matched failed-launch cleanup after a 60-second phone-home Input reply timeout. This was not a kernel OOM kill or a CARD-0079 stop. The strongest explanation for the missing reply is control-plane queueing behind a long workspace-retention check, but the exact lock owner/request timing is not retained, so the initiating delay remains a hypothesis. There is a confirmed structural mismatch between the global runner mutation lock and Input's deadline, plus a confirmed destructive startup-cleanup path for an ambiguous transport outcome.

## Identity and source binding

- Failed task: **3199d57c-2d82-4be8-85ea-8d65916d2c04**, Review/Frontier/Grok, CARD-1514.
- Session: **4cdb79d5-25da-449b-8802-1a817d33bcf4**; accepted generation **2026-11-13T15:34:42.039023Z**; provider child PID **26655**, child start **15:35:13.2595244Z**, pty-host PID **26628**.
- `GET /api/version` and `GET /api/session-runners/server2/status` both report **ab7af12c699039270af7e22a5d7fd794035bc325**. Runner store **f519bd08-e53a-47d1-adb1-2ab33475446f**, boot **152c4913-b52a-4d06-89ef-1363363f1df9**, epoch **2**.
- Investigation base **e4c45f4b4bb58870bb8684087a6d218e1b988cbe**. The relevant dispatcher, retention, phone-home timeout, session-launch and queue files have no diff between this base and the loaded ab7af12c source. The cleanup catch was also read directly with `git show ab7af12c...:server/Application/Services/AgentSessionService.cs`.

## Timeline

| UTC | Evidence |
|---|---|
| Before 00:00 | Durable host-load capture was already failing: first daily runner log line at 00:00:01 reports `Host load append unavailable`, dropped=8241 in the earlier process. The same warning persists after the morning runner replacement. |
| 15:10:45.509 | Disk emergency Deploy **918ceea9-4626-4d9b-b716-ee1f587ef1b5** launches on the **desktop**, operating on server2 over SSH. It later settled Succeeded at 15:56:25.740; its full final report and committed investigation were read before closing this Debug. |
| 15:15–15:17 | Deploy measures Docker volume roots with `du`; work ~269,967 MiB, runner tmp ~110,529 MiB. These initial broad measurements precede the Input timeout by over 20 minutes. |
| 15:16:46 | Deploy records changing retention override 24 h to 12 h. This investigation made no setting change. |
| 15:26:01 | Deploy's `df` reports **71,160,352,768 bytes free**, 91% used. |
| 15:26:44.408 | Sanctioned retention execute run **7cc7519e-2714-4014-ba1a-c9719da2c64d** starts, overlapping the entire failed launch. |
| 15:33:44.368 | Review task created. This corrects the approximate 15:32 dispatch in the incident brief. |
| 15:34:42.039 | Dispatch/accepted generation. |
| 15:35:12.937–15:35:13.312 | Pty-host starts, attaches runner, launches Grok 1.0.41. Phone-home Launch **67be6ec4-d5ee-47b9-bb60-a928b2396961** returns `started`. |
| 15:35:13.446 | System rules-bootstrap message **0d4e6f04-22b7-4d71-9cc9-b1c0b72ef335** created. |
| 15:35:11 / 15:36:20 | Deploy reports retention at 13 / 15 removals. At 15:36:08 disk free is **113,430,687,744 bytes**. |
| 15:36:09.883 | Transcript sidecar records first input. This records the runner write path, not a complete normalized UserPrompt receipt. |
| During launch | ANSI diagnostic shows `Starting session`, then the rules-bootstrap composer, a rules prompt displayed at 3:37 PM, and `Waiting for response` advancing past 1m40s. The actual Review task brief does not appear in the ANSI text. Screen output is diagnostic only. |
| 15:36:37–15:36:49 | Deploy also examines `/tmp` Claude scratch directories with `du`. Its earlier Git inventory loop is stopped at ~15:36; these are additional filesystem activity, not proof of I/O saturation. |
| About 15:38:26 | Inferred lower-resolution request start: subtracting the Input timeout from cleanup at 15:39:26. The exact Input request ID and start/write/reply clocks were not logged. |
| 15:38:24–15:39:14 | CPU samples mostly **7–21%** on 24 cores, memory used **31.2–32.0 GB**, load1 **26.09–29.66**. Five-second sampling continues. |
| 15:39:26.1166116 | Durable pty-host stop request **a6d79f54-89cc-4550-90b2-0ddd54ce7793**: same session/generation, child PID 26655/start identity, signal **9**, target `Child`, path `ProcessTree`, outcome `Issued`, provenance `LiveIssuer`, durable=true. A recovered JSON claim alone is not issuer authority; it is corroborated here by the live runner command outcome and host exit. |
| 15:39:26.6426087 | Pty-host records child exit: code 0, **signal 9**, reason **KilledByRequest**. The code-0 field is not a successful natural exit. |
| 15:39:26.654 | Phone-home **KillGeneration 37dc54ea-1f42-4984-b8f2-3fe1575ccdd7**, expected accepted generation 15:34:42.039023Z, returns **killed**. |
| 15:39:26.675 | Runner disconnects from this pty-host and acknowledges shutdown; host exits. |
| 15:39:26.833 | Session row ends Failed with `Runner did not reply to Input within 60s.` and SystemRequest stop attribution. |
| 15:39:29.876 | Runner warns that Grok exited without ever creating its deterministic `updates.jsonl`, although input was delivered. Nothing was ingested. |
| 15:39:37 | Deploy reports 16 removals; host `df` at 15:39:31 has **115,774,050,304 bytes free**. |
| 15:42:31.458 | Task settles Failed with no report; this is later than the session death. |
| 15:42:43.578 | Deploy transcript reports execute run finished: **25 removed**, refused=0, partial=0. At 15:42:55 free space **131,885,727,744 bytes**. A scheduled execute run **1591b561-7025-4c58-8e6f-cb394b4e7265** begins immediately afterward. |
| 15:43:29.741 / 15:44:18.362 | Retry **1a93b55a-c95b-45fa-a0a2-f68437229a48** created/dispatched on server2, session **72df94f9-987a-4b8f-a8cf-b25b40085579**. At 15:51 it has **44 normalized transcript entries** and real tool calls, unlike the failed session. No input or state change was sent to it. |

Deploy final report: **56c4e841a1992bc98df33016225d56c3dbd1ae58**, `docs/investigations/2026-11-13-server2-disk-emergency.md` on `feat/card-task-918ceea9`. It confirms **46 whole worktrees removed** across the two runs, **zero refusals**, ~95 GB net reclaimed. The scheduled run finished **15:51:39**. **No /tmp deletion sweep executed**: only a temporary dry run after 15:43; its temporary settings were removed at 15:53:39. No Docker pruning occurred. Thus /tmp mass deletion is excluded by the Deploy report; retention deletion/inspection and read-only filesystem inventories remain the overlapping work.

## Why the reply could be late

`PhoneHomeLiveConnection.RequestAsync` writes a request frame, then waits a bounded duration. `RequestTimeoutFor(Input)` is **60 seconds**. On expiry it removes the server waiter and throws `PhoneHomeTransportException(RequestTimeout)`. It does **not** prove zero backend writes, cancel the runner's queued action, or terminate the connection.

On the runner, `PhoneHomeCommandDispatcher.MutateAsync` uses a single `SemaphoreSlim(1,1)` across **all sessions**, not a per-session gate. Input, ConditionalInput, KillGeneration, Launch and workspace operations take it. The wait itself has no request cancellation token. `RetainAsync` takes this gate around the **whole** `RunnerWorkspaceRetention.CheckAndMoveAsync`: the first checks, then a second validation pass and move. These include full untracked `git status`, live-origin `ls-remote` with a **60-second budget per pass**, holder scanning and other Git children with their own budgets. A legitimate phase-A check can therefore exceed Input's entire reply allowance.

Phase-B multi-GiB `git worktree remove` is correctly **outside** this lock; temp sweep also runs outside it. The claim that removal itself holds the control lock would be wrong. Deletion and `du` can still compete for storage bandwidth, but historical I/O-wait and exact per-request lock timings are absent. No evidence names the specific retention row holding the gate at 15:38:26. Other candidates include a slow backend pipe/evidence read or delayed response scheduling. Thus retention **phase-A queueing is the strongest hypothesis, not an established incident lock trace**.

The receive pump dispatches requests off its socket loop; heartbeat and live host sampling can continue while this gate is occupied. That fits the observed live connection and fresh samples, without requiring runner-wide thread-pool starvation. The runner log contains no thread-pool starvation warning or connection-end event in this interval. Absence of a warning does not prove there was no transient scheduling delay.

## Which rule killed the session

The exception travels out of the rules initialization / queue delivery work inside `AgentSessionService.LaunchInteractiveProcessAsync`. That method's broad catch unconditionally calls `KillAndDisposeAsync(adapter, acceptedGeneration, session)`. Cleanup calls `adapter.KillGenerationAsync` for the exact accepted launch generation; `LaunchInteractiveAsync` then records the failed launch and SystemRequest. The stored timeout text, matched kill generation, stop request and subsecond host exit fit this path. The missing desktop stack trace prevents identifying whether this particular timed-out Input was a body write or an Enter/re-Enter.

This is **failed-launch orphan cleanup**, not the stall detector, an operator stop, memory watchdog or CARD-0079 compaction recovery. Generation matching is correct and prevents killing a replacement. Its safety premise is still insufficient here: `PhoneHomeTransportLoss` explicitly excludes request timeout because it **may have reached the runner and acted**. The launch catch treats that uncertainty as a fatal launch and kills the generation anyway.

No normalized transcript establishes a Working turn, but the diagnostic screen shows a submitted rules prompt waiting for a provider response. There is no evidence of a legitimate measured stall/unreachable-runner verdict, and no evidence the model was irrecoverably wedged. This is a **potential false-positive destructive startup recovery**. Do not generalize it into permission to stop Working sessions, and do not remove genuine failed-start cleanup that prevents orphaned processes (CARD-0056).

## Host pressure, compiler and telemetry limits

Saved live-series data includes 120 five-second points for **15:30–15:40**:

| Metric | Minimum | Mean | Maximum |
|---|---:|---:|---:|
| CPU, host % | 4.76 | 35.77 | 94.87 |
| Load1, 24 cores | 16.25 | 24.61 | 40.05 |
| Used memory, decimal GB | 31.22 | 34.80 | 39.22 |

For **15:38–15:39:30**, 18 points: mean CPU **21.06%**, peak **81.73%** only near the end; used memory **31.22–33.03 GB**, total **135.18 GB**. This does not support host memory exhaustion or continuous CPU saturation as the cause of the whole minute. High load with relatively low busy CPU is compatible with I/O wait, but is not proof of it.

Readable kernel `dmesg -T` contained **no OOM / out-of-memory / killed-process lines**. Current runner memory cgroup: **oom_kill=0**, **under_oom=0**, **failcnt=0**, effectively unlimited memory limit, peak usage **60,163,645,440 bytes**. These checks support the explicit process-stop evidence; they are not a complete host journal or all-container cgroup audit. Trusted `ssh mc@server2` failed host-key verification from this container; verification was not bypassed. Host `journalctl` and desktop server logs could not be retrieved. The canonical desktop-log helper fails because `/work/repos/antiphon/server/logs/antiphon-*.log` does not exist here.

At **15:54:09**, compiler PID 6366 had **9,067,316 KiB RSS (~8.65 GiB)** and **130 threads**; its displayed 569% CPU is a lifetime average, not an instantaneous measurement. The current compiler remains a performance concern covered by **CARD-1885**, already being implemented by task **58c3a29f**; this is not evidence of compiler RAM/CPU at 15:38. Saved series does not retain per-process attribution. A separate two-second `/proc` sample ending **15:54:31.554Z** measured compiler **1.01 CPU cores**, **9,350,680,576 bytes RSS**, **61 threads**, and host **0.85% I/O wait**; this describes that later sample, not the incident.

Historical host-load API returns **items=[], complete=false, gaps=[binding-unknown]** for the requested 15:00–16:00 interval. The failed task's load route returns **windows=[], complete=false, gaps=[event-or-sample-gap]**. Runner status has `load=null`. The append warnings predate the emergency sweep and cannot be used as evidence that the sweep broke storage. Live v1 series still works, but only keeps 30 minutes and provides cpu/load/memory rather than historical disk/I/O wait. Collector warnings suppress their error class/reason, so their cause remains unresolved. Do not read `cursor.json` into reports: its schema contains a secret.

## Delivery, sync, other sessions and present risk

Authoritative server transcript: **entries=[], lastSequence=0**. Native expected path `/state/grok/sessions/%2Fwork%2Fworktrees%2Ftask-3199d57c/4cdb79d5-25da-449b-8802-1a817d33bcf4/updates.jsonl` was never created, confirmed by the runner's final warning. This is a provider transcript-creation/delivery-confirmation gap, not evidence of server ingestion losing an existing file. The queue still held the rules message **Pending, deliveryAttempts=2, parked=false** at inspection. No complete UserPrompt or rules ACK confirms delivery. The real Review brief was never released through the rules barrier; no Review work/result could sync. Task detail has **progressEvidence=null, syncDebt=null, parkSync=null**. There is no evidence of a runner Git synchronization failure causing this death.

Board-scoped task listings were enumerated with `since=15:00Z&includeChecks=true` across **all 16 boards**. Only the Antiphon board had matching server2 rows. At the **15:57:05.690Z** final cross-board inspection cutoff the complete failed/canceled list in the requested **15:00–16:00Z query window** was:

| Task | Session | Outcome / reason |
|---|---|---|
| **3199d57c** | **4cdb79d5** | Failed at 15:42:31, Input reply timeout then generation-matched SystemRequest SIGKILL at 15:39:26. |

One other session release was logged: **d16edc11**, session **93584cf3**, Review Succeeded at **15:07:50.268**; ReleaseSlot at **15:08:06.465** returned `Exited`. This is normal settled-session cleanup, not a second failure. No other KillGeneration was logged in 15:00 through the runner-log inspection. The remainder of the requested hour had not elapsed; this report does not claim future failures are absent.

At **15:51:23**, server2 was live/dispatch-eligible/accepting, six live sessions, no pending phone-home events, **102,592,655,360 bytes available RAM**, CPU **59.96%**, load1 **34.85**. At **15:54:09**, filesystem free was **165,203,320,832 bytes (~153.9 GiB)**, 78% used. Retry transcript was progressing. No active task was proven to be dying or memory threatened. Remaining risk: every session needing a control write shares the same maintenance gate; new Grok startup can still hit ambiguous timeout cleanup; builds still contend with the compiler. No immediate restart/kill is justified by this evidence. The final Deploy report warns that disk growth remains **25–29 GB/hour**: its estimate is **4–4.5 hours to the 50 GiB floor without further reclaim**, or roughly **6–7 hours with the scheduled 12-hour retention**, not a stable end state. Its paid-sync-debt/build-output/temp-family remediation is separate from this incident fix. This investigation left the retry, Deploy and all live sessions untouched.

## Remediation and handoff

Filed on board **8988ca03-7414-47ad-b0b6-51556c701703**:

1. **CARD-1892**, **c26ceedc-3c19-403e-b458-34d77f114c28**: isolate session control from long retention checks while preserving workspace/generation safety; retain and reconcile ambiguous input outcomes before destructive startup cleanup; add bounded sanitized lock/request timings. Acceptance must exercise a held phase-A check and delayed reply after a real write, plus true failed-start orphan cleanup. Timeout inflation alone does not fix the coupling.
2. **CARD-1893**, **1f1d24c5-ea6b-46c5-82e6-14a0e5cf1dcb**: repair server2 collector append failures and physical binding, persist usable I/O/memory/disk history and task windows, and report bounded collector failure reasons. Related desktop-only **CARD-1889** remains separate.

Duplicate searches completed with `card.ps1 ... -Board Antiphon -All` for input timeout, mutation lock, phone-home, failed launch, retention, Host load append, host load and host-load; full candidates CARD-0627, CARD-1519 and CARD-1889 were inspected. No product fix was made. Caller can commission CARD-1892/1893 separately while the existing Review retry continues.

For exact initiating attribution, retrieve the desktop Serilog window **15:35–15:40** correlated with session **4cdb79d5**, Input request and **KillGeneration 37dc54ea**; inspect retention run **7cc7519e** rows near **15:38–15:39** for phaseAMs/phaseBMs. This child token was correctly refused residue-run access with **403 caller_scope**, so no authorization was bypassed. If no lock timing was retained, keep the initiating cause hypothetical and use the new instrumentation in a controlled acceptance run.

Evidence gathered locally under `.antiphon/debug-8ff425ec/` is gitignored and is not a durable delivered artifact. This committed report preserves essential timestamps, identities, counts and missing evidence. Primary durable sources are `/state/runner-logs/session-runner-20261113.log`, `/tmp/antiphon-pty-hosts/logs/4cdb79d525da449b88021a817d33bcf4.log`, `/tmp/antiphon-pty-hosts/stop-intents/4cdb79d525da449b88021a817d33bcf4-639301808820390230-a6d79f5489cc455090b20ddd54ce7793.json`, `/state/session-runner/transcripts/4cdb79d525da449b88021a817d33bcf4.json`, task events, message queue and normalized transcript API. Pty-host evidence is temporary container storage; preserve it from the owning host before a future replacement if further forensic work is needed.

--- next stage ---
next: code
handoff: Commission CARD-1892 and CARD-1893 as separate repairs; retain the initiating lock-delay hypothesis until desktop stack/retention timing evidence resolves it. Existing retry 1a93b55a continues independently.
artifact: docs/investigations/2026-11-13-server2-input-timeout-debug.md
