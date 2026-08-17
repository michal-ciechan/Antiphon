# CARD-0064 investigation — TranscriptBindFailed on az-care

Investigated 2026-08-17 (task `a65cc6fe`), read-only. Nothing was killed, restarted or changed.

---

## Corrections to the briefed facts

Four of the six briefed facts are wrong. They matter, because three of them point at the wrong agent.

| # | Briefed | Measured |
|---|---------|----------|
| 1 | 520 incidents, "all on agent slug az-care", 2026-08-13 09:40:03Z → 2026-08-16 20:28:31Z | 520 across **three** agents: **az-care 499** (Critical, 2026-08-14 20:11:21Z → **2026-08-16 13:42:01Z**), **family 12** (Critical, 2026-08-13 09:40:03Z → 10:35:04Z), **antiphon-check-interpreter 9** (Warning, 2026-08-16 20:02:22Z → 20:28:31Z). The briefed window is the union of all three. |
| 2 | Message is `TranscriptMissing: The session's child process exited without producing an identifiable transcript…` | That is **antiphon-check-interpreter's** message. az-care's `FailureReason` is **`AdoptionRefused`**, listing five candidate files each refused with *"no prompt in it matches input delivered to this session"* — i.e. **C4**, not TranscriptMissing. family's is **C3** ("first timestamped record … predates the child start"). |
| 3 | az-care Status 2, AlwaysOn, PersistentSessionId 276811ea | ✅ confirmed |
| 4 | Session 276811ea CreatedAt 2026-08-03 18:21:50Z, StartedAt 2026-08-13 19:44:18Z, Status 2, runner lists Running | ✅ confirmed (runner: pid 9724, hostPid 13180, `adopted: true`, lastSequence 226) |
| 5 | 545 TranscriptEntries, latest 2026-08-16 13:44:30Z | 545 ✅, but that is `CreatedAt` (ingestion). The latest **`Timestamp`** is **2026-08-13 19:44:59Z**. Entries 533–545 all carry `CreatedAt` 2026-08-16 13:44:30 — a **single backfill burst** of the 08-13 boot conversation, three days late. |
| 6 | Incidents stopped 20:28 on the 16th | For **az-care** they stopped at **13:42:01Z**. 20:28 is check-interpreter's last one. |

Queries used:

```sql
SELECT a."Slug", i."Severity", count(*), min(i."CreatedAt"), max(i."CreatedAt"), count(DISTINCT i."SessionId")
FROM "AgentIncidents" i JOIN "Agents" a ON a."Id"=i."AgentId" WHERE i."Kind"=15 GROUP BY 1,2;
```

---

## (A) Can az-care answer a channel message right now? **YES.**

Every link in the chain verified, without sending anything to a real channel:

1. **A transcript IS bound.** `C:\logs\antiphon\session-runner\transcripts\276811ea624b4e32941bbb660197d486.json`:
   ```json
   { "transcriptPath": "C:\\Users\\lndco\\.claude\\projects\\C--src-ClaudeBot-agents-az-care\\8606558b-69a1-42d3-bc27-efafb57ee050.jsonl",
     "how": "migration-shim", "updatedAtUtc": "2026-08-16T13:44:30.0484392Z", "resumeLaunch": true }
   ```
   Runner log, 14:44:30 local 16/08: `Tailing transcript …8606558b….jsonl for session 276811ea…`. **Zero** refusal lines for this session since. DB: incident **Kind 16, Severity 0** at 2026-08-16 13:44:30 — *"Transcript bound by migration-shim"*.
2. **Child and pty-host alive.** pid 9724 (`claude.exe.old.1786690808661`, started 13/08 20:44:19 local, 1681 s CPU) and pid 13180 (`Antiphon.PtyHost`).
3. **TUI is healthy and idle** — `GET :17204/sessions/276811ea…/snapshot` renders a normal composer: `⏵⏵ bypass permissions on … ← for agents`, `/rc` badge lit, last output `● NO_REPLY (5s · ↓ 245 tokens)`. **No modal, no trust dialog**, composer empty (the dim `commit the invoice 174 changes` is Claude's history hint, rendered SGR-2; it is not in `last-prompt` and not held content).
4. **Reads idle, not working.** Last transcript entry (seq 545) is a `TurnEnd`, so `IsWorkingAsync` → idle and `WhenIdle` deliveries will fire.
5. **Channel bridge live.** `antiphon-20260817.log:220` — `Channel bridge started; consuming inbound channel messages`; `:225` — `consuming channels.inbound as antiphon-server-bridge` (08:48:42 local today).
6. **Server↔runner stream live.** `SessionRunnerEventPump`: *"Transcript catch-up completed for 51 runner session(s)"*, 08:48:43 local today.
7. **Whole Kafka pipe healthy.** All three groups **Stable**, **lag 0**: `antiphon-server-bridge` (channels.inbound 124/124), `antiphon-messaging-service-outbound` (channels.outbound 166/166), `antiphon-messaging-service-inbox`. The gateway on server2 is connected right now.
8. **Routing intact.** ChatChannel *AZ Care* (`-5031013177`, Enabled) → AgentId `8acdd711` → `PersistentSessionId` = 276811ea, Status Running. `GET /api/agents`: `queueLength: 0`, `alwaysOn: true`, `remoteControlEnabled: true`, `supervision.suspended: false`, `consecutiveFailures: 0`.

**Caveat, unavoidable without sending a message:** everything upstream of "Claude reads the composer" is proven; the final hop (a delivered prompt actually being answered) has not been exercised since 2026-08-13 19:44:59Z, because nothing has been sent. The known blocker is gone.

---

## (B) Were any inbound messages missed? **No. Not one.**

Two independent sources agree:

- **Kafka `channels.inbound` on server2** (the gateway's own record, upstream of Antiphon entirely; log-start 89 = 2026-08-07, high-watermark 124 — nothing relevant trimmed). Last message for conversation `-5031013177` ("AZ Care") is **offset 122, `2026-08-11T09:07:45Z`**, from Ola Z. Offset 123 is a *Family* message at `2026-08-16T11:01:57Z`. **Zero AZ Care traffic in the whole outage window.**
- **`SessionQueuedMessages`** for session 276811ea: highest sequence is **78**, `CreatedAt 2026-08-11 09:07:45.87Z`, **Status 1 (Sent)**, `SentAt 09:07:45.88Z`, `DeliveryAttempts 0`. Every one of the 78 is Sent. Nothing Pending, nothing parked.
- `ChatChannels."LastMessageAt"` for AZ Care = `2026-08-11 09:07:45Z`, `MessageCount 86`. That column is written in `ChatChannelService.cs:106` at **ingest**, before any dispatch — so it records arrival, not success, and its silence is real silence.
- The 08-11 09:07 message *was* answered: transcript entry seq 531 (`AssistantText`, `2026-08-11 09:08:40Z`) is the reply.
- Ingest was demonstrably alive throughout: the Family message on 2026-08-16 11:01:57Z was consumed normally.

**No human was ignored.** This lowers the card's priority materially — but see the last-line risk in (D).

---

## (C) Why binding failed — C4, and it was structurally unsatisfiable

The child **never exited**. It has been alive and idle since 2026-08-13 20:44:19 local. This was a **binding gap**, not a crashing agent.

The real outage is **2026-08-13 19:45:39Z → 2026-08-16 13:44:30Z = 65 h 59 m**. Runner log: the refusal stream for this session starts at 20:45:39 local on 13/08 — 81 s after the child started — and fires **792** times at a 5-minute cadence with no gap (792 × 5 min ≈ 66 h — exact). The incident table only covers 41.5 h of it because the **server was down** for the first 24.4 h (`server/logs/antiphon-20260814.log` ends 17:10 local).

**Mechanism.** All five cwd-matching candidates, *including the session's own true file* `8606558b`, were refused on **C4** — "no prompt in it matches input delivered to this session". C4 is `SessionInputLog.MatchesRecordedInput` → `PromptSubmissionMatch.TryBuildNeedle`, which **rejects anything shorter than `MinMatchChars` = 12** (`src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs:34,136`).

The only `user`-prompt record ever written to `8606558b` is record 19: **`"—"` — one character.** Below the floor, therefore unmatchable, therefore C4 could never be satisfied.

Why nothing else was available:

- The long resume note (`ChannelPreamble.RestartResumeBody`, ~200 chars — an ideal needle) was `queue-operation: enqueue`d at 19:44:45.249Z and `queue-operation: **remove**`d at 19:44:47.821Z. It **never became a prompt record**. (Contrast the `"—"`, which was enqueue→**dequeue**→submitted.)
- `/remote-control` (15 chars, and `PromptSubmissionMatch` is pinned to match its `<command-name>` wrapper per CARD-0056) produced **no `<command-name>` record in this file at all** — the boot slash-commands landed before Claude created the JSONL. `custom-title: "AZ Care"` at record 1 proves `/rename` ran; the record itself is absent.
- No further input was ever typed, because no channel message ever arrived (see B).

So the failure is **self-reinforcing**: quiet agent → nothing typed → C4 has nothing to match → no bind → stays quiet.

`SessionInputLog`'s own doc comment names the assumption that broke:

> *"Deliberately NOT persisted: after a runner restart the log is empty, which is fine because a restarted session re-tails from its sidecar rather than re-running discovery."*

276811ea (created 2026-08-03) **predated sidecars and had never bound**, so it had no sidecar — it re-ran discovery with an empty in-memory log. C1/C2/C2b/C3 all passed on the correct file; only C4 stood, and it could not be satisfied by construction.

**The rules were right.** Every refusal was correct — the tailer declined to bind a file it could not prove, and ran with no transcript, exactly as CARD-0006 intends.

---

## (D) Why it stopped at 13:42 on the 16th — **neither CARD-0047 nor CARD-0056 fixed it**

Timeline (local, BST):

| Time (16/08) | Event |
|---|---|
| 14:35:50 | **session-runner restarts**; re-adopts 8 pty-hosts incl. 276811ea (host pid 13180) |
| 14:37:01, 14:42:01 | last two refusals (`after 60s`, `after 360s`) |
| **14:44:30** | `adopting …8606558b….jsonl (**migration-shim**)` → sidecar written, 13 entries backfilled, Info incident kind 16 |
| 21:04–21:28 | check-interpreter's 9 `TranscriptMissing` warnings (different agent, different fault) |
| **22:27:58** | `6072027` CARD-0047 trust-dialog fix — **7 h 45 m after az-care recovered** |
| 22:54–23:35 | `1793d6f`/`e9643f5`/`8458961`/`77065c3` CARD-0056 slices — **8–9 h after** |

**Verdict: what cured it was the CARD-0006 migration shim** (`TranscriptTailer.cs:474`, shipped 2026-08-11 in `07763ec` — the same commit that created the strict rules), finally getting its one chance. Its gate is `_restartAdopt && _knownTranscriptPath is null && shimEligible.Count == 1`, and `shimEligible` only admits a candidate whose **mtime is inside a 20-second `_activeWriteWindow`** (`TranscriptTailer.cs:111,403,447`). For an idle session that is close to a coin flip per poll. It needed (a) a **session-runner restart** to put the tailer back on the restart-adopt path, and (b) a poll to coincide with Claude touching the file — which took a further **8 m 39 s**.

The restarts that evening were incidental to CARD-0047/CARD-0056 work. **No shipped fix addressed this.** The same shim cured `family` the same way on 2026-08-13 (runner log 11:38:00 local, ~3 min after family's last incident at 10:35:04Z).

CARD-0047's fix does look to have cured **check-interpreter's** separate `TranscriptMissing` episode (a brand-new cwd `C:\logs\antiphon\check-interpreter` → trust modal → child dies, exactly CARD-0047's symptom). Its latest session `1cb2fadb` started 2026-08-16 21:11:54Z, is still Running, and raised no further incident. Worth recording — but it is not az-care's bug.

---

## Residual risk (the reason this still needs a fix)

az-care now **has** a sidecar, so a *session-runner restart* is safe: `SessionRunnerRuntime.cs:621` passes `knownTranscriptPath: sidecar?.TranscriptPath, restartAdopt: true`, and the tailer binds via `how: sidecar` with no C4 (`TranscriptTailer.cs:297`).

But a **session relaunch** is not. The AlwaysOn supervisor relaunching az-care (kill + `--resume`) creates a fresh session with `restartAdopt: false`, no sidecar, an empty input log — and **the shim is deliberately unavailable on a fresh launch** (that is where the 2026-08-09 privacy incident happened). If the relaunch again records only a sub-12-character prompt, az-care goes straight back into the hole **with no escape hatch at all**. The 66-hour outage survived only because nobody messaged AZ Care during it; the next one may not be so lucky.

Also: the shim's own comment says *"removable one release after deploy"*. **Do not remove it before the fix below ships** — it is currently the only thing holding az-care's binding.

---

## Recommended fix (not implemented — say the word)

Both restore binding by **strengthening the evidence**, not by weakening C1–C4.

**1. Persist `SessionInputLog` next to the sidecar** (primary). A small bounded rolling file per session, written on `RunnerSession.WriteAsync` and reloaded on adopt/relaunch. This preserves the only positive identification C4 has instead of destroying it on every runner restart, closing the "re-adopt / relaunch with an empty log" hole. It cannot weaken any rule — it only stops evidence being thrown away. It also makes the shim genuinely removable.

**2. Guarantee one C4-usable prompt per launch** (cheap complement). Ensure at least one launch-time write is ≥ `MinMatchChars` **and** actually lands as a `user`-prompt record in the file the tailer probes:
   - replace the 1-character `"—"` resume ping with a real body (`ChannelPreamble.RestartResumeBody` already exists and is ~200 chars); and
   - make sure it is **submitted**, not enqueued-and-removed — az-care's own transcript shows `enqueue`→`remove` for it, which is why no needle existed.

**3. Surfacing** (already CARD-0035's job, worth a line on the card). 499 Critical incidents over 41 hours reached nobody; the `Alerts` row deduped on `supervisor:15:<agentId>` fires once and then stays quiet. CARD-0035's `RecentCriticalIncident` condition is the right home for this.

**Do NOT** widen `_activeWriteWindow`, relax `MinMatchChars`, or make the shim available on fresh launches. Each of those re-opens the 2026-08-09 failure.

---

## What CARD-0064's description should be corrected to say

Title → **"az-care ran 66 hours with no transcript bound — C4 had nothing to match, and only a lucky restart fixed it"**

Priority → **downgrade from the escalated Critical framing**: no human was ignored, and the agent is answering now. Keep it open as a **reliability** bug, because the cause is unfixed and the escape hatch is scheduled for removal.

Body should replace the "80 incidents / adoption refused / a human has been receiving no answer" narrative with:

1. **Scope**: 499 Critical `TranscriptBindFailed` on az-care (not 80, and not the only agent — 12 on family, 9 Warning on antiphon-check-interpreter, which is a *different* fault, `TranscriptMissing`, and CARD-0047's bug).
2. **az-care is answering now.** Bound since 2026-08-16 13:44:30Z to `…az-care\8606558b….jsonl`, `how: migration-shim`. Child pid 9724 alive, TUI idle at a healthy composer, bridge + Kafka + event pump all live with zero lag.
3. **Nobody was ignored.** Zero inbound AZ Care messages between 2026-08-11 09:07:45Z and now — confirmed independently on Kafka `channels.inbound` (offset 122 is the last) and in `SessionQueuedMessages` (all 78 Sent). The escalation's central claim ("a human messaging AZ Care has been receiving no answer at all") is **false**; strike it.
4. **The real outage was 65 h 59 m**, 2026-08-13 19:45:39Z → 2026-08-16 13:44:30Z — longer than the incident rows show, because the server was down for the first 24.4 h. 792 refusals at 5-min cadence in the runner log.
5. **Root cause is C4, structurally unsatisfiable**, not "which condition is failing, nobody looked". The only prompt ever recorded was `"—"` (1 char, below `MinMatchChars` = 12); the long resume note was enqueued then removed; `/remote-control`'s `<command-name>` record never reached the file. `SessionInputLog` is in-memory only and its stated safety net ("a restarted session re-tails from its sidecar") does not exist for a session that has never bound.
6. **Nothing shipped fixed it.** The CARD-0006 migration shim did, opportunistically, needing a runner restart plus a 20-second mtime coincidence. CARD-0047 (`6072027`) landed 7 h 45 m later and CARD-0056 8–9 h later; neither touched az-care. CARD-0047 *does* appear to have cured check-interpreter's separate `TranscriptMissing` episode — record that separately.
7. **It will recur on the next relaunch** (shim unavailable on a fresh launch), and the shim is marked for removal. Fix = persist `SessionInputLog` + guarantee one ≥12-char submitted boot prompt. Keep the CARD-0006 "do not loosen the rules" warning verbatim — it is still correct, and the rules behaved correctly throughout.
