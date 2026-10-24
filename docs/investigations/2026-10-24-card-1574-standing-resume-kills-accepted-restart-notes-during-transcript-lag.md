# CARD-1574: Standing resume kills accepted restart notes during transcript lag

Date: 2026-10-24. Investigation task: `b491703f`. Evidence-only; no product changes, restarts, session kills, or queue changes by this investigation. The only tracker write was creating/updating the Backlog finding CARD-1574.

## Root cause

The running desktop build contains CARD-0510's failure accounting, but not its automatic fresh-conversation reservation. It always resumes, then holds continuity at five failures. CARD-0510 was Done on master with an explicit **not active on AppHost/server2** disclosure.

The immediate restart loop was a false delivery failure: Claude accepted all four restart notes, but Antiphon's persisted receipt frontier remained stale. Verification, including its bounded direct-send grace, expired and killed the launches. Replaying the 373 MB file imposes at least 106.5 seconds before those receipts are readable; the last three launches died earlier. Provider context exhaustion is not established.

Confirmed by reconstructing deployed source, native JSONL records, persisted transcript rows and timestamped server/PTY logs. The additional minutes of ingress latency remain unlocated; that uncertainty does not alter the accepted-prompt/false-kill or missing-activation findings.

## Provenance and identities

- Standing agent: `a392cbc4-0fc0-4603-b4d0-5198d1929718`, Antiphon-Orchestrator, ClaudeCode, AlwaysOn=true, IsPoolDelegate=false. The failure log itself records AlwaysOn=true. Historical/current ownership is also recorded in both session rows' StandingAgentId.
- Old conversation: `daec6a72-771d-4a66-9634-fbe474d87464`, created `2026-10-15T07:44:46.336885Z`.
- Replacement: `b2f1a869-9bc5-4974-b8ae-53686448fd20`.
- `GET http://localhost:17202/api/version` returned `5a126f6fc5ae4e16d803b628716b8f0485a37174` with `land-v2` and `operator-shutdown-v1`. Canonical checkout HEAD is the same detached SHA. Antiphon.Server PID 219900 started at 10:24:34 BST; server log `C:\src\Antiphon\server\logs\antiphon-20261024.log:50316` records application startup at 10:25:28 BST, before the incident. The same server epoch appears in incident-period and later probe logs. Version observation and process inspection occurred during this investigation, not at incident time.
- Worktree source baseline: `67860d9e35ccaac745f1be37923420c0d2214f56`. It contains newer recovery code, so **deployed-source references below are pinned to `5a126f6fc5ae4e16d803b628716b8f0485a37174`**, not silently attributed to worktree HEAD.
- Server log timestamps use `+01:00`; the tables below use UTC. The runner supervisor log has BST clock-only timestamps; the PTY-host log supplies explicit UTC dates.
- Read owners: `docs/session-runtime-invariants.md`, `docs/logs.md`, `docs/ops-http.md`, `docs/agent-card-lifecycle.md`; relevant stage/report rules in `docs/orchestration-loop.md`.

Generated evidence is deliberately gitignored at:

`C:\Antiphon\worktrees\card-task-b491703f\.antiphon\investigation-b491703f\`

It includes `queries.sql`, `db-rows.txt`, `version.json`, source files exported with `.deployed.txt` suffixes, `server-excerpt.log`, `pty-excerpt.log`, `runner-tail-starts.log`, `native-summary.json` and `native-metadata.json`. Essential rows and provenance are retained in this report, so the conclusion does not depend on committing generated payloads. `native_scan.py` reads only the named provider transcript and extracts metadata; it does not inspect provider credentials.

## Failure sequence: one initial delivery failure, four supervised resumes

The brief's "five failed resumes" conflates the initial running generation's delivery failure with four subsequent resume launches. The stored RestartScheduled incidents call those subsequent attempts 2, 3, 4 and 5. The initial process had been launched on October 23; it was not a new October 24 resume attempt.

All times below are on 2026-10-24, UTC. The PTY evidence source is `C:\logs\antiphon\session-runner\pty-hosts\logs\daec6a72771d4a669634fbe474d87464.log` (dated entries are preserved in `pty-excerpt.log`).

| Generation / attempt | PTY child launch | Failure evidence | Child termination | Supervisor consequence |
|---|---|---|---|---|
| Initial running generation | Oct 23 02:16:17.977, pid 215700 | At 17:10:20.525, 3,111-character Check delivery has NoComposerEvidence after 15s; Enter withheld | 17:10:21.864, code 1 / KilledByRequest | At 17:10:28.118994, attempt 2 scheduled for 17:10:37, 10s backoff |
| Resume 2 | 17:11:00.603, pid 128392 | At 17:12:30.430, 230-character restart note has NoTranscriptRecord beyond frontier 29155 after 30s and 3 Enters; recovery kill logged 17:12:51.059 | 17:12:51.039, code 1 / KilledByRequest | Crash 17:12:55.140039; attempt 3 scheduled 17:13:15, 20s backoff |
| Resume 3 | 17:13:16.586, pid 414712 | Same note/frontier verdict 17:14:18.288; kill logged 17:14:40.731 | 17:14:40.483, code 1 / KilledByRequest | Crash 17:14:45.594377; attempt 4 scheduled 17:15:25, 40s backoff |
| Resume 4 | 17:15:29.352, pid 367104 | Same note/frontier verdict 17:16:32.632; kill logged 17:16:57.321 | 17:16:57.191, code 1 / KilledByRequest | Crash 17:17:01.103041; attempt 5 scheduled 17:18:19, about 80s backoff |
| Resume 5 | 17:18:27.495, pid 9896 | Same note/frontier verdict 17:19:23.172; kill logged 17:19:46.973 | 17:19:46.951, code 1 / KilledByRequest | Crash 17:19:55.028456; continuity hold 17:19:55.066583 |

Server log citations for these rows: `antiphon-20261024.log:84036`, `:84054`, `:89951`, `:90516`, `:96994`, `:97201`, `:104423`, `:104734`, `:122271`, `:123452`. These are absolute-source log line numbers, also preserved in the excerpt.

Each of the four boot launches ends with the same secondary exception: **"The standing launch is no longer authorized for this generation."** For example, `antiphon-20261024.log:90564` and the following stack cite deployed `AgentSessionService.cs:792`, called after launch-note delivery at `:680`. The delivery kill precedes that exception. The guard at deployed `server/Application/Services/AgentSessionService.cs:781` rejects a session that is no longer Starting/Running, so this exception is consistent with the launch's own earlier cleanup; it is not evidence by itself of a competing manual Start or broken native resume.

No `StoppedBeforeFirstPrompt` delegate failure is the recorded outcome here. The old session row ends Failed (5), RestartFailureKind Unknown (0), TerminationSource SystemRequest (2), exit 1, StartedAt `17:18:25.831826Z`, EndedAt `17:19:47.107125Z`, InteractiveLaunchCompletedAt null, FailureReason equal to the exception above. The row is reused for resumes; its final StartedAt does not preserve all earlier generations, hence the incident/PTY reconstruction.

## Accepted receipts contradict the failure verdict

`TranscriptEntries` is keyed here by AgentSessionId=the old conversation and Sequence as shown. Provider Timestamp and database CreatedAt are separate columns. Every receipt is a **UserPrompt**, has the exact 230-character restart-note body, and has a matching UUID in the native JSONL.

| Sequence | Provider Timestamp (UTC) | Persisted CreatedAt (UTC) | Lag seconds | Native JSONL line / byte offset |
|---|---|---|---:|---|
| 29159 | 17:12:00.824 | 17:23:32.237685 | 691.414 | 113257 / 372588245 |
| 29164 | 17:13:50.475 | 17:36:33.316570 | 1362.842 | 113296 / 372877661 |
| 29177 | 17:16:02.545 | 17:36:33.668686 | 1231.124 | 113341 / 373001073 |
| 29192 | 17:18:52.888 | 17:41:30.108291 | 1357.220 | 113402 / 373221107 |

UUIDs respectively: `a949958e-51a0-4360-b5fe-fb10af5af6a4`, `5385e0c9-48ba-4055-a9d4-03b7390fd9f5`, `49b03f62-65f5-4244-b0d1-c7fd6cd8b237`, `402a64af-6d66-425b-a564-d53eccc814bf`.

Known native file: `C:\Users\lndco\.claude\projects\C--src-Antiphon\daec6a72-771d-4a66-9634-fbe474d87464.jsonl`. It is 373,336,918 bytes (about 356 MiB), created October 15, last written `17:19:10Z` October 24. The scan also finds ordinary Claude responses/tool use. At native line 113430 / database Sequence 29200, Claude emits end_turn at `17:19:10.178Z`, before the final Antiphon kill. Model is `claude-sonnet-5-5`, not a synthetic API-error stub; usage is input 2 + cache read 381830 + cache creation 1437 = 383269 input-context tokens. Bytes on disk are not the live context size.

The first failure differs: its submitted Check body never had composer evidence, so no accepted submission is established for that original 3,111-character body. Earlier Check attempts were classified Truncated at `antiphon-20261024.log:83819` through `:83963`. This report does not infer an original provider failure from those classifications.

### Mechanism in the deployed code

1. `server/Application/Services/AgentSessionService.cs:3426` selects the resume note and normally sends Mode.Now. No pending Channel obligation diverted this observed path. Direct Now delivery has no queue row during verification (`SessionMessageQueueService.cs:448`, `:544`); it captures a baseline before typing (`:586`).
2. **There is already a direct-send grace.** `SessionMessageQueueService.cs:629` calls `TryGraceConfirmModeNowAsync` for NoSubmitOutput/NoTranscriptRecord. At `:4337` that method pulls `CatchUpTranscriptAsync`, checks stored matching records and stops at its PostFailureConfirmGraceSeconds deadline. Do not diagnose this incident as "no grace implemented." The row-based grace at `:4904` requires messageIds and is a different path.
3. When that bounded grace still has no receipt, `SessionMessageQueueService.cs:657` calls HandleDeliveryFailureAsync with null messageIds. The persistence-blindness brake at `:5031` requires a recorded persist failure and reverting queue rows. Lag without a recorded persist exception does not satisfy it.
4. The handler reads the same stale working projection (`:5070`, `:5464`) and permits an AlwaysOn kill when it appears idle (`:5075`). It kills the captured generation with SystemRequest (`:5216`). Logs prove these conditions evaluated as alwaysOn=true, working=false, killed=true.
5. Launch-note delivery catches that failure and enqueues a System fallback copy (`AgentSessionService.cs:3459`); launch processing subsequently fails the generation guard. Thus a successful native receipt can coexist with a newly Pending duplicate fallback note and a Failed launch.

The native receipts and timing establish the false-negative mechanism without replaying messages to a real session.

## Why no automatic fresh session appeared

At the deployed SHA:

- `server/Application/Services/AgentSupervisorService.cs:288` always sends StartAgentRequest(Fresh:false), automatic:true. Its capacity-recovery path does the same at `:473`.
- `:478` implements TryHoldRepeatedResumeFailureAsync; `:486` reads **Supervision:ResumeFailureHoldAttempts**, default **5** (`server/Application/Settings/SupervisionSettings.cs:39`). `RestartFailurePolicy.Charge` increments ContinuityResumeFailures for Unknown or LaunchOrProcessFailure; infrastructure failures have different accounting.
- `server/Application/Services/StandingContinuityState.cs:25` sets ContinuityHeldAt/SessionId/Reason/Evidence, clears NextRestartAt at `:35`, and writes StandingContinuityHeld. The supervisor exits immediately on a continuity hold at `AgentSupervisorService.cs:146`. It retains the failed session and persistent pointer instead of replacing the conversation.
- The durable incident at `17:19:55.066583Z`, Kind 58, says: "5 consecutive supervised resumes ended before healthy uptime; last outcome Failed/Unknown" and offers inspect/repair/select/explicit Fresh. Its stored severity is **Info (0)** in this deployed implementation. Do not substitute the newer documentation's Error wording for this row.
- **Supervision:FreshAfterResumeFailures is deprecated/ignored.** The deployed settings file already declares **AutomaticFreshAfterResumeFailures=3** (`:32`) and S1 accounting fields, but there is no deployed StandingRestartEscalationPolicy file or fresh reservation caller. A declared threshold is not an active implementation. No configuration change to that value could make absent S2 code run.

Source ancestry corroborates the partial delivery: `git merge-base --is-ancestor 2d49f44111f533d9d242ae529304ae6bd95e5ea0 5a126f6f...` exits 0 (S1 present); the same check with S2 introduction `0d7e6af7018b8d1e8290a2ee56c63642dcb64057` exits 1. Worktree/master has S2/S3, including `StandingRestartEscalationPolicy.cs:12`, `AgentControlService.cs:595` and `:831`. Its setting defaults to three admitted failures and its reservation creates a new identity, rebuilds launch instructions and records an incident.

CARD-0510 (`fe0fd650-89ed-44ad-a6c5-dc43037debdb`, Antiphon board), GET status Done, completed `2026-10-21T10:12:57.685313Z`, explicitly records S1 `2d49f44111f533d9d242ae529304ae6bd95e5ea0`, S2 repair tip `ebd5632a0fd3e3291f83ba867acda8035fbf7453`, S3 tip `569cdd327612ee23583119802e815f0559154984` and **"NOT yet active on the AppHost/server2"** pending rc backport/activation. This is the direct answer to the operator's fresh-restart question. AlwaysOn and Claude kind were eligible; the absent reservation implementation, not a demonstrated disabled threshold, explains the observed behavior.

Even activating that newer implementation would not guarantee a fresh start for this queue. `StandingRestartEscalationPolicy.cs:31` accepts only untouched ordinary Ui/System messages with no producer bindings. `AgentControlService.cs:637` refuses attempted input and `:639` refuses nontransferable producer input. This old queue had both. The newer policy deliberately holds/alerts rather than silently transferring them; this investigation did not execute that policy against a live session.

## File size, guard, and remaining latency uncertainty

At deployed `src/Antiphon.SessionRunner/TranscriptTailer.cs:366`, every new tailer starts offset=0. `:416` reads at most one MiB per loop and `:440` delays 300 ms after each loop (constants `:38`, `:49`). EmitLine republishes normalized records in file order. There is no measured pre-resume file-size/age refusal in the traced standing path; the nine-day-old, 373 MB native history was admitted repeatedly.

Each restart receipt lies in chunk `floor(byteOffset / 1048576)+1 = 356`. Reaching it therefore requires at least **355 preceding sleeps x 0.3s = 106.5s**, plus parsing, I/O, fork checks, transport and server work. Runner tail-start lines `C:\src\Antiphon\logs\session-runner.log:300212`, `:300416`, `:300614`, `:300929` are 17:11:01, 17:13:16, 17:15:29 and 17:18:28 UTC. The last three launches survived only approximately 84.5s, 88.2s and 79.0s after those starts. Their new tailers could not yet have reached the receipts when destructive verification ran. The first lasted about 110s, barely beyond the sleep-only bound and without parsing/transport cost included.

This is a reconstructed, quantitative size contribution to **receipt blindness**, not proof that Claude's --resume cannot load 373 MB. The file contains accepted prompts and normal responses, no demonstrated context-overflow error in the incident window. The latest stored CompactBoundary is Sequence 28165 at `10:04:56.112Z`, "Context compacted (manual)"; no incident-window compaction failure is established. CARD-0509's older 163-164% overflow incident and CARD-0510's October 15 overflow recurrence do not prove this October 24 incident had the same provider cause.

The 106.5-second lower bound does **not** explain all 691-1363 seconds between provider Timestamp and CreatedAt. It proves a pacing mismatch for three launches; the rest needs per-hop evidence (runner consumed offset/frontier, snapshot/stream arrival, ingest-lock entry and commit times, including handling of replayed duplicates). The current event pump awaits sequential event dispatch and subscription catch-up (`server/Infrastructure/Agents/SessionRunner/SessionRunnerEventPump.cs:65`, `:73`), but its individual contribution was not timed here. CARD-1259 already owns broader ingestion-latency attribution. No live reproduction, test/build driver, or synthetic traffic was run.

## Pending notes and manual replacement

The four fallback System rows in SessionQueuedMessages stayed associated with the old session. Sequence/id/CreatedAt:

- 6333 / `4fab9d7e-bade-4892-8d2e-4d394f743565` / `17:12:51.145700Z`.
- 6336 / `653c670c-93ff-47bc-8acc-cbe9105a8b7e` / `17:14:41.409245Z`.
- 6338 / `ef54ba9f-b27d-4c6a-a1f0-ec1d10c2ad7f` / `17:16:58.666229Z`.
- 6341 / `6e21eb2e-e387-4a0e-97b2-2704167c3ba9` / `17:19:47.038279Z`.

They have DeliveryAttempts=0 because they are fallback **copies** created after the direct note attempt; that does not invalidate the native UserPrompts above. The export `C:\src\ClaudeBot\temp\orchestrator-pending-messages.txt` includes the same queued-note timestamps. It is an operator snapshot, not independent delivery authority.

Other undelivered queue evidence includes Check 6328/id `20f4c2a9-73e4-4397-8f5d-82ceab0c4e19`, DeliveryAttempts=1, NoComposerEvidence; Delegation 6330/id `d665d70c-98ad-4a0b-b61b-144b93edd48f` for task `8a0ab444...`; and land-note rows 6332, 6335, 6340 with producer notification IDs `69d8d96b-c355-4da0-8aa1-2c2d9ce910ed`, `fbbaa987-df92-4bd6-9aac-41ef9273a756`, `bbb3cedb-1182-466e-b498-b134a8c643c9`. More completion notes were queued at 17:52:35, 17:54:36 and 18:08:15 after the hold.

At **22:20:50Z**, the server rejects a conversation switch with HTTP 409 and "Resolve attempted pending input ... before switching" (`antiphon-20261024.log:319823`, deployed `AgentControlService.cs:573`). At **22:21:35.853537-22:21:36.135036Z**, 17 old-session queue rows are marked Canceled (2), including the attempted Check, all four fallback notes and the listed delegation/land notes. The database retains their original session IDs and canceled timestamps; they were not transferred. This investigation did not cancel them. The evidence shows the cancellation batch preceding recovery; it does not independently identify which operator/client issued each cancellation.

The replacement session row is created/started at **22:21:44.614114Z**, with InteractiveLaunchCompletedAt `22:22:28.756631Z`, same StandingAgentId and current agent PersistentSessionId updated to it. This corrects the brief's approximately 18:30Z estimate for **this specific replacement ID**. The current observation shows no old pending queue rows transferred to the replacement. Canceled rows are excluded from the deployed switch's Pending-only transfer (`AgentControlService.cs:569`); only never-attempted input passes `StandingQueueSwitchPolicy.cs:7`. A future replacement does not automatically redeliver these canceled notes. Producer-specific recovery, if any, must be checked per obligation; this report does not certify it from queue status alone.

## Existing cards, new finding, and uncertainties

- **CARD-0510, Done:** bounded fresh fallback already implemented on master; not active in the deployed SHA. No duplicate implementation card filed for that known gap.
- **CARD-1259, Review:** ingestion latency versus verification; the 11-23 minute persistence lag here is additional evidence for its existing scope.
- **CARD-0509, Backlog:** prior orchestrator overflow incident. Relevant history, not proof of overflow today.
- **CARD-1574, Backlog**, `d63c90f7-c772-4dc0-8b1b-a7415ee73d45`: confirmed destructive verification of already accepted standing restart notes. Distinct scope is the standing-launch kill/readiness decision under stale receipt evidence; broader latency attribution stays with CARD-1259. Duplicate searches used `card.ps1 search ... -Board Antiphon -All` for resume, fresh, transcript, replay, launch note and NoTranscriptRecord; relevant full cards were inspected before filing.

Remaining uncertainties: exact additional delay per ingress hop; initial NoComposerEvidence's underlying TUI state; exact operator/client responsible for the later cancellation batch; producer-specific obligations after those cancellations; runtime override values beyond the observed five-failure threshold (the absent fresh reservation remains decisive). No tests or builds were run; the mechanism was reconstructed from production evidence and pinned source.

## Not done, noted

Recommended fix direction only: commission CARD-1574 planning for receipt-readiness-aware destructive recovery, and separately activate the already reviewed CARD-0510 rc backport with its attempted/producer-input holds preserved; no fix designed or implemented here.

--- next stage ---
next: plan
handoff: Plan CARD-1574 from confirmed accepted restart UserPrompts followed by false NoTranscriptRecord kills; account for the 106.5s minimum replay delay and existing Mode.Now grace. Keep additional ingress-lag attribution with CARD-1259 and CARD-0510 activation separate; do not assume producer-bound or attempted notes can transfer.
artifact: docs/investigations/2026-10-24-card-1574-standing-resume-kills-accepted-restart-notes-during-transcript-lag.md
