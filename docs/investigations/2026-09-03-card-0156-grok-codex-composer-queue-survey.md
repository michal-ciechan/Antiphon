# CARD-0156 survey — Grok/Codex composer-queue records

**Date:** 2026-09-03 (task `530e8435`)
**Card:** CARD-0156 (`a6163fd9-079f-4d0c-a4b5-900fdb5a996a`)
**Status:** survey complete. No app code was changed.
**Verified against:** worktree `feat/card-task-530e8435` at `649c5edb`, live `GROK_HOME`
(`%USERPROFILE%\.grok`) and `CODEX_HOME` (`%USERPROFILE%\.codex`) on this machine, Grok Build
user-guide under `~/.grok/docs/user-guide/`.

---

## Verdict, in one sentence

Claude's `queued_command` / `queue-operation` pair is still the **entire JSONL evidence set**.
Grok has a real in-TUI prompt queue (default `follow_up_behavior = "queue"`) that writes
**nothing** until drain, then a normal `user_message_chunk` → `UserPrompt`. Codex has no
observed queue kind; mid-turn user text, when it happens, is a normal `user_message` /
`UserMessage` → `UserPrompt`. `TranscriptPromptSpan` does not need a new kind. The only
follow-up is Grok's silent in-memory queue, which CARD-0135's checks cannot see because there
is no record to see.

---

## 1. Corpora (real files, not source)

| Surface | Files | Lines | Queue-shaped kinds (`queued_command`, `queue-operation`, `queued`, `composer_queue`) |
|---|---|---|---|
| Grok `updates.jsonl` | 804 | 191,519 | **none** |
| Grok `events.jsonl` | 387 (sampled 5 completed sessions; live file locked) | telemetry only | **none** (`"type":".*queue"` over the tree: 0 hits) |
| Codex `rollout-*.jsonl` | 169 | 55,235 | **none** |

Grep for `"sessionUpdate":"...queue"` on Grok updates: 0. Grep for
`"type":"(queued_command|queue-operation|queued|composer_queue|queue_operation)"` on Codex
rollouts: 0.

Content hits on those strings are agents **reading Claude source / CARD-0135 docs**, not
Grok or Codex writing the kind.

---

## 2. What each TUI actually writes for user input

### Grok ACP `updates.jsonl` — unique `sessionUpdate` values (all 804 files)

| Count | `sessionUpdate` |
|---|---|
| 96,268 | `tool_call_update` |
| 48,159 | `tool_call` |
| 19,172 | `agent_thought_chunk` |
| 15,403 | `hook_execution` |
| 4,780 | `agent_message_chunk` |
| 2,059 | `task_backgrounded` |
| 2,056 | `task_completed` |
| **1,055** | **`user_message_chunk`** ← the only user-input kind |
| 1,042 | `plan` |
| 988 | `turn_completed` |
| 346 | `retry_state` |
| 84 | `auto_compact_completed` |
| 84 | `compaction_checkpoint` |
| 8 | `subagent_finished` / `subagent_spawned` |
| 4 | `session_recap` |
| 3 | `auto_compact_started` |

No queue / composer / follow-up kind.

**Shape of a user prompt** (truncated):

```json
{"timestamp":1787643797,"method":"session/update","params":{"sessionId":"<guid>","update":{"sessionUpdate":"user_message_chunk","content":{"type":"text","text":"[antiphon-task:…] …"}},"_meta":{"eventId":"…","agentTimestampMs":1787643797xxx}}}
```

- **Kind Antiphon stores:** `UserPrompt` (`GrokTranscriptNormalizer.FromUserChunk`).
- **Timestamp clock:** `params._meta.agentTimestampMs` (unix milliseconds) preferred;
  else top-level `timestamp` (unix seconds). Drain time, not enqueue time — there is no
  enqueue row.
- **Accompanying user record:** none. `user_message_chunk` *is* the user record.

`events.jsonl` is local telemetry (`phase_changed`, `tool_started`/`completed`,
`permission_*`, `turn_started`/`turn_ended`, MCP). Not a second transcript. Sampled types
from five completed sessions included none of queue / follow_up / composer.

### Codex rollout — unique top-level `type` / `payload.type` (all 169 files)

Top-level: `response_item` (28,046), `event_msg` (26,301), `turn_context` (407),
`world_state` (291), `session_meta` (172), `compacted` (11),
`inter_agent_communication_metadata` (7).

User-input `payload.type` values:

| Count | Kind | Notes |
|---|---|---|
| 89 | `event_msg` / `user_message` | Flat dialect (`codex exec` / Codex Desktop) |
| (inside `item_completed`) | `item.type = UserMessage` | TUI dialect Antiphon launches |

Plus `response_item` / `message` / `role=user` (same body, other dialect). The normalizer
latches the first dialect it sees and maps both to `UserPrompt`
(`CodexTranscriptNormalizer`: `user_message` / `item_completed{UserMessage}`).

**Flat user record:**

```json
{"timestamp":"2026-08-17T21:00:00.350Z","type":"event_msg","payload":{"type":"user_message","message":"how do i install codex cli\n",…}}
```

**TUI user record:**

```json
{"timestamp":"2026-09-01T22:34:35.922Z","type":"event_msg","payload":{"type":"item_completed","item":{"type":"UserMessage","content":[{"type":"text","text":"exit"}]},"started_at_ms":1788302075922,"completed_at_ms":1788302075922}}
```

- **Kind Antiphon stores:** `UserPrompt`.
- **Timestamp clock:** ISO-8601 UTC on the row; TUI items also carry `started_at_ms` /
  `completed_at_ms` (unix milliseconds, equal at submit in every sampled row).
- **Accompanying user record:** the `user_message` / `UserMessage` *is* the user record.
  Desktop sessions also write a parallel `response_item` `message` `role=user`; the
  normalizer does not double-emit.

---

## 3. Overlaps that looked like queuing, and were not

### Grok — `user_message_chunk` while the previous turn was still open

803 sessions excluding this live worktree. 178 had two or more user chunks. 6 file-order
"overlaps" before `turn_completed` / `task_backgrounded`:

| Session | What it actually is |
|---|---|
| `81db3ea1` (`C:\src\Antiphon`) | `turn_completed cancelled` then two `user_message_chunk` of the **same** `[antiphon-task:beb675aa]` brief 50 s apart. Cancel-and-send (CARD-0159 shape), not a queue row. |
| `b697d7b5` (`C:\src\Antiphon`) | Same: `cancelled` then a **clipped** chunk (`2f180b] role=Debug…`) and the full brief 25 s later. Clip + re-delivery after cancel. |
| 4 files under `tests\Antiphon.Agents.Pty.Tests\bin-grok` | FakeGrok sequential prompts in the same unix second with no `turn_completed` between them. Test artefact. |

Zero of 1,055 `user_message_chunk` rows is a composer-queue record.

### Codex — user record while the previous task was still open

396 user records, 4 file-order overlaps:

| Rollout | What it actually is |
|---|---|
| `01a01586-…` 2026-08-20 19:44:24Z | Two **different** Desktop `user_message` rows 17 ms apart ("An you verify next time expiry…" / "Should be 11/27") while tools were still completing. Written as normal `user_message` + `response_item` `role=user`. Visible as `UserPrompt`. Not a queue kind. |
| `01a02336-…` | Mid-turn `/compact …` as `item_completed` `UserMessage`. Codex records `/compact` as a plain user message (ProviderContractCatalog). |
| `01a058a1-…` | Duplicate auto-continue `UserMessage` ("Your previous turn was killed…") 2 s apart, then `task_complete` with a usage-limit error. Retry, not a queue. |

So Codex **can** accept extra user text while a task is open (observed on Desktop). It
writes a normal user record immediately. CARD-0135's `UserPrompt` arm would see it.

---

## 4. Grok's prompt queue exists — in TUI memory, not JSONL

Vendor docs, not inferred from source:

- `~/.grok/docs/user-guide/03-keyboard-shortcuts.md` § "During an active turn": plain
  `Enter` with text **queues** a follow-up. Default `[ui].follow_up_behavior = "queue"`
  (config reference + `05-configuration.md`). Follow-ups run after the current turn and
  **hold** while the agent is blocked on a background task or subagent. `Ctrl+;` toggles
  the prompt-queue pane.
- `10-hooks.md`: a blocked prompt "is not recorded: it never enters the conversation
  history the model sees on later turns, the on-disk session record, or the session
  summary." Queued rows behind a block do not auto-run. After restart the blocked bubble
  is gone "exactly because nothing was stored."
- `ui.combine_queued_prompts` merges consecutive follow-ups into **one** turn at drain —
  one `user_message_chunk`, not N queue rows.

That matches the corpus: 191k ACP rows, 0 queue kinds, and every observed user chunk is
either a fresh turn, a cancel-and-send, or a clip retry.

**What Antiphon would see if a brief were Enter'd into a busy Grok composer**
(Mode.Now / channel / a WhenIdle flush that lost the idle race — the CARD-0233 Claude
path):

| When | JSONL | Antiphon kind | CARD-0135 checks (`TranscriptPromptSpan`, settlement, delivery watchdog) |
|---|---|---|---|
| Enqueue (plain Enter mid-turn) | **nothing** | — | **blind** |
| Previous turn still running | **nothing** | — | **blind** (watchdog 10 min clock is ticking) |
| Drain after `turn_completed` | `user_message_chunk` | `UserPrompt` | **see it** |
| Session dies before drain | **nothing** | — | **never** — no `queued_command` to recover from |

This is the inverse of Claude. Claude writes `queued_command` at drain-from-composer-queue
(enqueue clock on the attachment) with **no** accompanying `user` record; CARD-0135 taught
`TranscriptPromptSpan` to count `QueuedUserPrompt`. Grok writes the user record only when
the follow-up actually runs, at drain time, as `UserPrompt`. Widening `TranscriptPromptSpan`
or `ChannelReplyDispatcher` cannot help: there is no kind to add.

Codex TUI (Antiphon's launch mode) has no documented prompt-queue pane in the local corpus
and no queue kind in 169 rollouts. Do not file a Codex follow-up until a headed probe
observes one.

---

## 5. Follow-up

Filed **CARD-0355** (`dc5aa3ae-0d9c-4a31-98cb-6e4c3bc383cf`): *Grok prompt queue is
TUI-memory-only — CARD-0135 checks have no JSONL kind to see*. Not urgent. Headed probe
first (Enter into a busy Grok session, confirm `updates.jsonl` stays silent until drain).
Do not invent a `QueuedUserPrompt` mapping. Delivery confirmation / the 10-minute watchdog
are the surfaces that can fire while the brief is sitting in the queue pane;
`TranscriptPromptSpan` is the wrong lever.

No Codex follow-up.

---

## 6. What this survey did not do

- No headed Grok/Codex probe (the card asked for existing transcripts first; keep this
  pass proportionate).
- No Postgres scan of ingested `QueuedUserPrompt` rows by agent kind (Claude-only kind by
  construction; Grok/Codex normalizers never emit it).
- Did not change `ProviderContractCatalog`, the Grok/Codex normalizers, or FakeGrok.
