# Desktop read f38e63e0: process disappeared before prompt delivery

Debug task `bd9e5cb5-4bb3-481f-9ad4-da6a4efeb5d8`, 2026-11-15. Read-only runtime diagnosis from server2; documentation is the only committed change. Observations below were collected around 20:00–20:04Z. All times are UTC.

## Finding and classification

The desktop read did **not** begin reading the qualification result. Its Claude process disappeared during startup, with no native exit event, no transcript and no attempted boot-prompt delivery. The runner's liveness backstop detected that disappearance. Severe physical-memory pressure overlapped startup; this is the strongest observed environmental correlate, **not proof of an OOM kill**. The available evidence does not identify the terminating actor or distinguish a Claude crash from a pty-host failure.

| Brief category | Finding |
|---|---|
| (a) Claude CLI launch/exit | Startup did not reach a rendered composer or native transcript. Actual CLI version, exit code and stderr are unavailable for this session. No authentication or version fault was observed. |
| (b) Desktop host | Leading causal hypothesis: memory pressure. Available physical RAM reached 2,076,672 bytes (1.98 MiB) during startup; 26 of 41 samples between dispatch and exit had less than 1 GiB available. Commit-limit exhaustion, handle exhaustion and a Windows resource-exhaustion event are unproved. |
| (c) Qualification teardown collision | No positive evidence. The build-slot release occurred 26 seconds **after** disappearance was detected and its implementation does not kill processes. Native lock release and the supervisor completion path also contain no process sweep. A different earlier tree kill cannot be excluded without native process/Job Object evidence. |
| (d) Runner / pty-host | Confirmed observation mechanism: missing exit event recovered by the liveness sweep. No runner restart occurred in this window. A pty-host crash remains possible; its per-session log could not be retrieved through the available server read surfaces. This shares the pre-prompt symptom with CARD-1939 but does not reproduce its retained live seat. |

Provisional operational disposition: environmental startup failure under heavy memory pressure, with unresolved CLI/pty-host causation. Do not label this a confirmed Antiphon kill, a confirmed OOM, or a proved CARD-1939 product regression.

## Identity, timeline and direct evidence

Failed task: `f38e63e0-f759-415c-b648-74f927bf849f`; session: `9bf9801f-a99a-4588-89fd-f801758b3e07`; desktop PID: `90588`; backend: `PtyHost` / `ModernConPty`, no fallback. Server and runner both reported deployed SHA `64e3d78c487d51570f708492ab67b5db6d0de9a9`. Runner process start was `2026-11-15T04:31:12.3665129Z`.

| Time | Evidence |
|---|---|
| 19:50:39.770788 | Task created. |
| 19:50:48.799728 | Task dispatched; persisted session CreatedAt/StartedAt have this same logical timestamp. |
| 19:50:54.2704901 | Physical memory used 34,296,066,048 of 34,298,142,720 bytes: 99.9939%; available 1.98 MiB. |
| 19:51:35.891 | Runner logged trust seeding for the exact session. This is not a prompt receipt. |
| 19:51:36.449392 | Delegation queue row created; it remained Pending, deliveryAttempts=0. |
| 19:54:09.2430062 | Last memory sample before recorded exit: approximately 1,763 MiB available. |
| 19:54:10.437 | Runner liveness sweep detected the missing process, without an exit event. |
| 19:54:10.804441 | Server session EndedAt; Failed, nullable exit code and signal absent. |
| 19:54:12.312220 | Seed task `1d838cd1-2bcc-4222-99b1-7edc36c4f1d5` dispatched, **after** the read session's recorded end. |
| 19:54:36.385 | Qualification build-slot lease released. |
| 19:54:39.2454131 | Available RAM approximately 15,227 MiB, versus 2,099 MiB five seconds earlier. The consumer responsible for this drop is not identified. |
| 19:55:28.570 | Seed session `313c5fba-f399-4f82-816e-bc2b25e892f1` bound its own native transcript. |
| 19:57:38.806351 | Failed task CompletedAt; dead-session reconciliation settled it later than the process exit. |
| 19:58:41.899902 | Retry `3820a5f1-53fd-45d9-8c3e-ef0bbd2405db` dispatched. |
| 19:59:06.043 | Retry has a normalized UserPrompt receipt. |
| 20:03:39.618061 | Retry still Running/working, with recent transcript activity. |

The title's “one minute” is not the persisted dispatch-to-exit interval: that interval is approximately **3m22s**. The native child launch timestamp was not independently retrieved.

Essential unedited runner lines, obtained through `POST /api/diagnostics/bundle`:

```text
2026-11-15 19:51:35.891 +00:00 [INF] Claude trust seed Seeded session 9bf9801f-a99a-4588-89fd-f801758b3e07
2026-11-15 19:54:10.437 +00:00 [WRN] Liveness sweep marked session "9bf9801f-a99a-4588-89fd-f801758b3e07" as Exited: its process vanished without an exit event
2026-11-15 19:54:36.385 +00:00 [INF] Build slot lease "6b194cff-476d-41ab-a487-821a17acd0ed" released by rc-qualification-ff89ca56 (pid 94744) after 55255s
```

`GET /api/sessions/9bf9801f-a99a-4588-89fd-f801758b3e07/transcript?since=0` returned HTTP 200, `entries: []`, `lastSequence: 0`. Its buffer returned HTTP 200, lastSequence 3, only 35 characters of terminal initialization/title controls, with no readable error, Claude version or composer.

The queue GET returned the boot brief Pending, deliveryAttempts=0, deliveryUnconfirmed=false, lastDeliveryStartedAt=null, with `modalBlockedReason: "Claude dialog screen unknown"`. Thus there was no attempted body to resend and no ambiguous submitted prompt. The guard's refusal is expected for this unreadable startup screen. Session diagnostics had no incidents and no exit-stop intent. Missing records do not establish that no external kill occurred.

## Seat and retry

Two `GET /api/session-runners/desktop/slots` observations agreed: session `9bf9801f-a99a-4588-89fd-f801758b3e07`, PID 90588, status Exited, exitReason Unknown, desktopStatus Failed, occupiesCapacity **false**, orphan true, openTaskId null, park null. **This task kept no occupying runner seat.** There is no numbered seat identifier on this surface. The remembered Exited row is not a capacity leak. Other occupying orphans in the inventory belong to other sessions and were not released by this Debug.

Retry session `75cd8d77-35ad-4ce6-9b13-68a289c23e31`, PID 99740, was Running, non-orphan and owned by task `3820a5f1-53fd-45d9-8c3e-ef0bbd2405db`. Its sampled transcript contained 57 entries: 1 UserPrompt, 26 ToolCall, 26 ToolResult and 4 AssistantText. It was already inspecting the result, so recurrence of the **same startup fault on this retry** had not occurred. This does not promise task completion or future launch reliability. At 20:00:19 the desktop had approximately 13 GiB RAM available, 237 GB disk free and commit charge 38.3 GB against a 62.2 GB limit. Those later values are not measurements of commit headroom at the failure instant.

CARD-1939's earlier description instead records a fully rendered Claude v2.1.325 empty composer and live occupying sessions after task failure. That historical version is not evidence of this failed process's version. Here both readiness and process survival failed, and the seat was recovered.

## What source inspection establishes

Read the deployed SHA as well as the assigned checkout:

- `SessionRunnerRuntime.RunnerSession.MarkVanishedIfDead` checks the recorded child PID/start identity, marks a missing child Exited, publishes ProcessVanished with unknown native status, then schedules host shutdown. That downstream shutdown is not evidence of the original cause. The server's enum conversion maps an unrecognized reason to Unknown; “ProcessExit” names the observation source, not the issuer of a kill.
- `BuildSlotBroker.Release` removes a lease, records metrics and logs. Its sweep reaps lease records; neither path kills a session. The wrapper can kill its own unfinished wrapped process tree during disposal, but the observed later lease release alone cannot attribute any child-tree kill to this incident.
- `scripts/qualification/supervise-rc.ps1` waits, copies final state and logs EXIT/SUPERVISOR-END. Its finally block logs completion. `nightly-run-impl.ps1` releases the native and exclusive lock; their release functions dispose owned lock streams and remove matching lock files.
- `scripts/lib/nightly-owned-process.cs` creates an unnamed Job Object for each owned child, assigns that child before resume and terminates that job on timeout. This is scoped custody, not a global process-name sweep. There is no evidence that PID 90588 belonged to a qualification job. Actual candidate-local scripts and Job membership at the incident were not collected.

## Remaining evidence limits and cheapest action

The diagnostics bundle successfully returned the desktop runner log, including the full 19:50–19:56 window, session metadata and server log tail. Its server tail began after the incident and its only section error was that the already-removed task agent could not be found. The bundle does **not** include per-session pty-host logs. No general desktop file-read operation was found in the documented server/phone-home surface; workspace file reads are constrained to their agent workspace/listing. No desktop SSH, credential-store reads, process signals, restarts, seat releases or live configuration changes were used.

The outstanding native artifact is:

`C:\logs\antiphon\session-runner\pty-hosts\logs\9bf9801fa99a458889fdf801758b3e07.log`

A desktop read-only follow-up should extract that exact file and its matching manifest/stop-intent records if retained, plus Windows Application/.NET Runtime/WER and resource-exhaustion events for 19:50–19:56Z, filtered to the known child and the host PID named by that log. Inspect the qualification's own final custody records for the same PID/start identity before alleging a collision. Do not infer OOM from low physical memory alone. The currently available host history endpoints returned explicit gaps: `binding-unknown` for desktop load history and `event-or-sample-gap` for the task load window; the saved five-second memory series does not identify the largest consumer or historical handle/commit counts.

Cheapest correct action now: **allow the already-working retry to finish**, and avoid launching optional desktop work during the qualification's final memory spike; check actual free RAM/commit headroom after completion rather than using summary-file existence as the end signal. Keep large result extraction streamed. No restart or cleanup is needed for this failed session's seat.

That immediate mitigation is environmental/operational. There is no supported production-code fix to apply from this evidence alone. If native evidence proves a pty-host exit-observer or false-liveness defect, commission a separate red-first Code task on master with the exact reproduced failure. If a qualification-owned job is proved to contain this unrelated child, repair that custody boundary red-first on master. Keep CARD-1939's retained-live-seat issue separate; never bypass the input guard or add blind prompt retries to address this unreadable startup.

## Provenance and validation

Raw generated evidence is gitignored under `.antiphon/debug-evidence/bd9e5cb5/` in the assigned mirror; it is not published with the branch. This document retains the essential observations and log lines so its conclusions do not depend on those temporary payloads surviving mirror retirement.

| Captured artifact | SHA-256 |
|---|---|
| diagnostics.zip | `7f08d18bc545a148e95d60b6776eda849d23584f3362db9d0363c4a28f52bf09` |
| memory.json | `26431441a45f5a48820f7480a5c82d6aabaa3f8202a446071073f2bc0637f195` |
| failed-task.json | `0af474b83dad9410f9180feaf5319ef7481f993eb9fe0a72a66a611ea626d27d` |
| failed-messages.json | `ca4ec5b682a241f720a8592d7cc28dfbc16422133e7c623c2f5ec5bf2777231e` |
| slots-final.json | `af1ce864202996fa5c24f020075edb84fb218d4e3b23ff418ae1597cc0204014` |
| retry-transcript.json | `03abc6e1fbf4ae70d3601f86ac8ca92e75e09270ce83967904e6c31516eacfce` |

HTTP reads forwarded the task token without displaying it; the bundle request only assembled diagnostics. No builds or tests were run for this narrative-only change. Required remaining forensic work is explicitly incomplete; this report does not claim a proved root cause, a product repair or native qualification credit.

--- next stage ---
next: decide
handoff: Startup process disappearance confirmed; memory pressure is the leading hypothesis, terminating actor unproved. No occupying seat remains and retry 3820a5f1 has confirmed prompt/tool activity. Let it finish; decide whether the exact native pty-host/WER follow-up is needed before commissioning any product fix.
artifact: docs/investigations/2026-11-15-why-desktop-read-f38e63e0-died.md
