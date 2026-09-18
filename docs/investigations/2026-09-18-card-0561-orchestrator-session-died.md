# CARD-0561: Antiphon-Orchestrator Failed, restart-backoff=9, remote control "off"

Investigated 2026-09-18 (task 8ffa7df1). All times below are UTC unless marked `local`.
Server and runner Serilog files stamp `+01:00`; subtract one hour.

Sources: `GET /api/agents/a392cbc4-…/incidents?take=200`,
`GET /api/agents/a392cbc4-…/sessions`, runner `GET :17204/sessions/39c6eb3a-…`,
`C:\src\Antiphon\server\logs\antiphon-20260918.log`,
`C:\Users\lndco\AppData\Local\Temp\antiphon-logs\session-runner-20260918.log`,
Claude JSONL `C:\Users\lndco\.claude\projects\C--src-Antiphon\39c6eb3a-5e96-4f67-9b09-253c106a1d64.jsonl`
(34,281,839 bytes).

## Verdict

**Confirmed.** Distinct from CARD-0331 (land-queue restart safety). The standing
Antiphon-Orchestrator conversation `39c6eb3a-5e96-4f67-9b09-253c106a1d64` entered a
kill/resume loop that the supervisor cannot leave, because resume never replaces the
conversation (CARD-0466) and every automatic path re-enters the same poisoned transcript.

The card's `RestartBackoffFailures=9` / `Failed` snapshot is **wave 2** (04:05–05:23 UTC).
An operator `fresh:true` at 05:31:54 UTC created `0b70a773-…`, which recovered after 10 minutes.
The session is Running now (`restartBackoffFailures=0`, `remoteControlEnabled=true`).

## One-paragraph mechanism

A Claude tool result at 03:41:25Z wrote `\u0000` bytes into the JSONL. Persist of
`TranscriptEntries.Text` then threw `Postgres 22021 invalid byte sequence for encoding "UTF8": 0x00`
(`AgentSessionRuntime.PersistTranscriptAsync` `:879`/`:903`, catch returns `PersistResult.Empty`
at `:911-914`). Delivery confirmation reads stored `UserPrompt` rows, not the JSONL, so the next
queue flush reported `NoTranscriptRecord` even when the screen redrew. AlwaysOn + idle ⇒ kill
(`SessionMessageQueueService.HandleDeliveryFailureAsync` `:3883-3886`). The supervisor resumes
the **same** session id (`AgentSupervisorService` `:259-274`; `FreshAfterResumeFailures` is
ignored). Resume re-ingests the poison line, so confirmation stays blind. Concurrently the
conversation is at **463.3%** of the 200k ceiling, so some resumes fail ready
(`AgentSessionService.NotReadyMessage` `:1931-1934`). Boot then sees Claude's `/remote-control`
management menu (`armed=Armed|Unarmed menu=true`) and still flushes the queue; those bodies
cannot land. The in-flight launch's post-flush `RequireCurrentCheckLaunchAsync` (`:555-565`)
observes `Status != Starting|Running` after the delivery-kill and throws
`specialist_start_intent_revoked` — the FailureReason the card saw. Backoff is
`min(5s · 2ⁿ, 30d)` and never gives up; at n=10 it pages `BackoffEscalated` Warning (hourly).
Nothing in that ladder Freshs. Remote-control being "off" is the Unarmed/menu observation, not
the agent flag, and it did not cause the poison persist; it made post-resume typing worse.

## Identities

| | |
|---|---|
| Agent | Antiphon-Orchestrator `a392cbc4-0fc0-4603-b4d0-5198d1929718` |
| Failed conversation | `39c6eb3a-5e96-4f67-9b09-253c106a1d64` (created 2026-09-13 16:55:34Z, ended 2026-09-18 05:23:39Z) |
| Replacement | `0b70a773-d281-4449-b975-1a1b8a081a97` (explicit Fresh 05:31:54Z) |
| Backend / kind | PtyHost / ClaudeCode, cwd `C:\src\Antiphon` |
| JSONL | 34.3 MiB, exact bind |

## Two waves on 2026-09-18

### Wave 1 (01:36–01:53 UTC) — recovered

Ignition **before** the NUL. Server log (local 02:36–02:43):

| local | UTC | Evidence |
|---|---|---|
| 02:36:40 | 01:36:40 | `NoTranscriptRecord`: 3705-char body never a UserPrompt past seq 5797; **screen never advanced** (`antiphon-20260918.log:3077`) |
| 02:37:05 | 01:37:05 | `killed=true`, alwaysOn, working=false (`:3081`) |
| 02:37:04 | 01:37:04 | Runner: process vanished without an exit event (`session-runner-20260918.log`) |
| 02:38:17 | 01:38:17 | RC setup: `armed="Armed" menu=true` (`:3101`); incident `RemoteControlModalDetected` |
| 02:39:33 | 01:39:33 | `NoComposerEvidence` 3705 chars, killed again (`:3116-3117`) |
| 02:40:32 | 01:40:32 | RC: `armed="Armed" menu=true` |
| 02:41:43 | 01:41:43 | `NoComposerEvidence` 3072 chars, killed |
| 02:43:18 | 01:43:18 | RC: `armed="Unarmed" menu=true`; `RcDegraded` / `RemoteControlNotArmed` — this is the card's "RC was off" |
| 02:53:18 | 01:53:18 | `Recovered` after 10 min healthy uptime; backoff reset |

Gotcha #57 (`docs/session-runtime-invariants.md`) is the contract: `/remote-control` is not
idempotent; an already-live bridge opens Disconnect/Show QR/Continue, which swallows input.
Wave 1 left the conversation intact.

### Poison record (03:41:25Z) — still on disk

JSONL line 15369, `timestamp":"2026-09-18T03:41:25.604Z"`, tool_result:

```text
No Instance(s) Available.\r\r\n\r\u0000\n\u0000
```

Persist warning starts 04:41:26.163 local = 03:41:26 UTC (`antiphon-20260918.log:5592`),
one second after that record. `unnamed portal parameter $19` is `TranscriptEntries.Text`
on the INSERT at `:869-896`. Raw file has **zero** `0x00` bytes (the NULs are JSON
`\u0000` escapes); parsing them into `e.Text` makes Postgres reject the batch.

Count on 2026-09-18: **2102** `Failed to persist transcript entries for session "39c6eb3a-…"`
lines, still firing at 07:14 local after the row is Failed (runner still lists the session
`Exited`, `transcriptBound=true`).

### Wave 2 (04:05–05:23 UTC) — the card

| UTC | Incident / log | What happened |
|---|---|---|
| 04:05:27 | log `:26792` | `NoTranscriptRecord` 500 chars past seq 6054; **screen DID advance** (redraw, nothing submitted). Persist of the same session failed 18s earlier (`:26751`). |
| 04:05:53 | RestartScheduled attempt 2, 10s | Delivery-kill; supervisor resume. |
| 04:06:41 | RemoteControlModalDetected; log `armed="Armed" menu=true` | Boot RC setup on resume. |
| 04:08:04–04:08:12 | DeliveryVerificationFailed then Crash `specialist_start_intent_revoked` | Flush during launch typed; delivery-kill flipped the row Failed; `RequireCurrentCheckLaunchAsync` after Flush threw (`AgentSessionService.cs:521-565`). |
| 04:09:42 | Crash `Agent process did not become ready (resuming a session at 463% context).` | Ready wait failed. Log `:29430` `Context fullness 463.3 % exceeds 100% for model 'claude-sonnet-5' with ceiling 200000 tokens`. |
| 04:11:06–04:12:13 | Modal + parked message + attempt 5 (1.3m) | Same loop. |
| 04:14:11–04:38:13 | attempts 6–9 | Repeated `menu=true` (Armed or Unarmed) + `NoTranscriptRecord` + `specialist_start_intent_revoked`. Attempt 9 = **backoff 21.3m** — the count the bot reported. |
| 05:00:53 | attempt 10, 42.7m | |
| 05:23:43 | attempt 11, 1.4h + `BackoffEscalated` Warning | Hourly-or-slower tier (`AgentSupervisorService.cs:465-481`). |
| 05:31:54 | `StandingFreshSelected` | Explicit Fresh `39c6eb3a` → `0b70a773` (HTTP `POST /api/agents/…/start`, user admin). |
| 05:42:44 | Recovered | 10 min healthy uptime on the new id. |

`DeliveryTransportFailed` 500s on the same seconds as the kills are input/kill against a
runner session already `Exited` (`KilledByRequest`, exit 1) — knock-on, not a separate root.

## What the restart-backoff mechanism actually does

Pinned in `SupervisionSettings.cs:5-7,17-20` and `AgentSupervisorService.Backoff` `:459-463`.

- Ladder: `min(5s · 2^n, 2_592_000s)` = 5s, 10s, 20s, 40s, 1.3m, 2.7m, 5.3m, 10.7m, 21.3m, 42.7m, 1.4h, … capped at 30 days. **Never gives up.**
- `RestartBackoffFailures` is the exponent (`RestartFailurePolicy.Charge` `:49-54`). `ConsecutiveFailures` only increments for `LaunchOrProcessFailure`. Infrastructure/unknown still pace the ladder.
- Reset requires Running + `InteractiveLaunchCompletedAt` older than `HealthyUptimeResetMinutes` (10) (`:155-163`). A resume that dies inside the boot flush never qualifies.
- Escalation: Warning at delay ≥ 1h (n≈10 here), Critical at delay ≥ 1 day. There is **no** stop, no auto-Fresh, no extra page at n=9.
- CARD-0466: failure counts must not authorize Fresh. The operator Fresh is what broke the loop; the supervisor would have sat on a 1.4h then ~2.8h cadence against the same JSONL.

This is not CARD-0331. Land-queue durability is unrelated. The closest prior class is CARD-0056 (delivery-kill of a healthy session + false Failed) plus CARD-0466 (strict resume).

## Did remote-control being "off" cause it?

No. The agent flag is `remoteControlEnabled=true` now and RC setup ran on every resume.

What the bot saw as "off" is `RcDegraded` / `RemoteControlNotArmed` at 01:43:18Z and later
`armed="Unarmed" menu=true`. That is Gotcha #57: the management menu is on screen, automatic
arm is withheld, `/rename` skipped. It **contributed after each resume** (NoComposerEvidence,
bodies typed into a redrawing non-composer) but the wave-2 ignition is the NUL persist
blinding `UserPrompt` confirmation.

A session with RC fully armed still would have failed wave 2: confirmation uses DB rows, and
those inserts were rejected.

`IsModalBlockedLockedAsync` (`SessionMessageQueueService.cs:4405-4415`) is supposed to hold
ordinary queue bytes while an open `RemoteControlModalEpisode` matches the current generation.
Boot still typed 30s after `menu=true` (e.g. 05:06:41 menu, 05:07:12 `NoTranscriptRecord`).
Likely the episode is generation-keyed and the resume restamp does not match, or Detect commits
after Flush starts. Remaining uncertainty, not required to confirm the persist/kill loop.

## Why it went to 9 instead of surfacing sooner

- Each Crash/RestartScheduled is Warning, not Critical.
- `BackoffEscalated` Warning only at the hourly tier (attempt 11 / n=10), ~1h20m after wave-2 start.
- AlwaysOn supervisor keeps retrying forever; Failed is the expected resting row between attempts.
- Attention exists (`AttentionKind.RemoteControlModal` for open episodes) but the card was filed
  from a bot health reading of Failed + count=9, not from the first Error `DeliveryVerificationFailed`.
- RC menu meant there was no claude.ai bridge to watch; that delayed **human** notice, not
  Antiphon's own incident stream.

## Not CARD-0331; not a Herdr hold

PtyHost, not Herdr. `herdrConsecutiveFailures=0`, no `herdr_supervision_held`.
`docs/herdr-sessions.md` § Supervision hold does not apply.

## Remaining uncertainties

1. Why `FlushSessionAsync` typed after `menu=true` despite the modal hold (generation mismatch vs Detect timing).
2. Wave-1 02:36 local `screen never advanced` at seq 5797 — high context / overlay, not the NUL (NUL is 03:41Z).
3. Which tool produced `No Instance(s) Available` with embedded NULs (shape is a Windows instance query, not Antiphon).
4. Persist still retries the poison line on the Failed session hours later; whether the tailer should have unbound on exit is a follow-on, not the crash itself.
5. Exact Telegram digest path (am-service vs Attention) was not reconstructed; the incidents and Failed row are sufficient for the count=9 report.

## Not done, noted

Strip NULs (and persist per-row, not all-or-nothing) in `PersistTranscriptAsync` so one tool_result cannot blank delivery confirmation; do not auto-Fresh from backoff.

--- next stage ---
next: plan
handoff: Plan CARD-0561 from docs/investigations/2026-09-18-card-0561-orchestrator-session-died.md: NUL in JSONL tool_result at 03:41:25Z made TranscriptEntries persist fail (Postgres 22021), so AlwaysOn delivery-kill/resume looped the same 463% conversation until explicit Fresh; backoff never Freshs; RC menu was contributing not causal.
artifact: docs/investigations/2026-09-18-card-0561-orchestrator-session-died.md
