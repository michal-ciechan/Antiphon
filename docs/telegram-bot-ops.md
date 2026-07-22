# Telegram bot agents — ops

How to stand up (and verify) a Telegram-bot-backed agent. Spec:
[2026-07-21-telegram-bot-agents.md](superpowers/specs/2026-07-21-telegram-bot-agents.md) ·
plan: [2026-07-21-telegram-bot-agents-plan.md](superpowers/plans/2026-07-21-telegram-bot-agents-plan.md) ·
workspace convention: [agent-workspaces.md](agent-workspaces.md).

## Per-bot deployment model

One `Antiphon.Messaging.Service` instance per bot token (bot name = persona). The `family`
pilot uses `@antiphon_assistant_bot` (token in Bitwarden item "Antiphon Telegram Bot"); the
gateway instance runs on server2 alongside the existing `school_revision` one — same
`ghcr.io/michal-ciechan/antiphon-messaging-telegram` image, same Kafka, its own
`Telegram__BotToken` and a fail-closed `Telegram__AllowedChatIds` list (see
[messaging-standalone.md](messaging-standalone.md) for the compose shape).

**Known limitation (accepted):** `ChatChannel` is keyed `(Provider, ExternalId)` — two bots
joined to the SAME Telegram group would collide on one channel row and route to whichever
agent that row is bound to. Policy: one bot per group. Future fix: a `BotId` discriminator
column.

## Configure the agent (Channels + Agents pages, or API)

1. Agent row: `WorkingDirectory = C:\src\ClaudeBot\agents\<name>` (workspace must contain
   CLAUDE.md/SOUL.md — see agent-workspaces.md), **Always on** = on for a bot persona,
   **System prompt (appended)** = the Telegram preset ("Use Telegram preset" button;
   `GET /api/agents/preamble-preset?provider=telegram`). Clearing the preamble disables the
   bootstrap/restart/compaction notes for that agent — the per-agent kill-switch.
2. Send one message to the bot so the channel row appears, then on the Channels page bind it
   to the agent and enable routing (`PATCH /api/channels/{id}` — binding lives in
   `ChatChannelService.UpdateAsync`).
3. Optional: point an admin group's **Alerts** at Warning+ so supervision incidents land next
   to the conversation.

## Behaviour knobs (`ChannelBridge` section)

| Key | Default | Meaning / kill-switch |
|---|---|---|
| `Enabled` | false (dev AppHost forces true) | Bridge consumes `channels.inbound` at all |
| `DebounceWindowMs` | 500 | Same-sender merge window; **0 = passthrough (kill-switch)** |
| `DebounceMaxMs` | 2000 | Hard cap from first buffered message |
| `BatchingEnabled` | true | Coalesce same-conversation runs at turn end; **false = one-per-turn (kill-switch)** |

## Dev-stack smoke (fake gateway, no Telegram)

Prereqs: `dev-aspire.ps1` stack up (server 17202, fake gateway 17208), local broker running,
an agent bound to a channel with a preamble set. Executed 2026-07-22 against the `Family`
agent; re-run after changes touching the bridge/queue/dispatcher:

1. **Batching path** — three rapid inbounds from one sender:
   ```powershell
   1..3 | ForEach-Object {
     Invoke-RestMethod -Method Post http://localhost:17208/inbound -ContentType application/json `
       -Body (@{ chatId = '<boundChatId>'; text = "smoke msg $_"; username = 'mike' } | ConvertTo-Json)
   }
   ```
   Expect: ONE new turn in the session transcript (debounce merges within the window; if the
   sends straddle the window, the queue batches under the context/current markers instead) and
   exactly ONE reply in `GET http://localhost:17208/deliveries?since=<t>`.
2. **Compaction path** — queue `/compact` into the session
   (`POST /api/sessions/{id}/messages {"body":"/compact","mode":"WhenIdle"}`), then:
   - `GET /api/agents/{id}/incidents` gains a `ContextCompacted` (Info) row, NO alert;
   - the recovery note turn produces NO delivery (`NO_REPLY` honoured — check /deliveries
     did not grow).
3. **Restart note** — restart the agent (Stop → Start): the session resumes, the restart
   note turn sends nothing to the chat, and the launch args carry `--append-system-prompt`
   (visible in the session's pty-host manifest / audit).

## Transcript tailer discovers Claude's forked session id (2026-07-22, RESOLVED)

Reply routing (agent's turn → `ChannelReplyDispatcher` → `channels.outbound` → gateway) is driven
by the **transcript tailer**. Interactive Claude does **not** reliably honour `--session-id`: the
runner-spawned agent writes its conversation to a self-chosen `<uuid>.jsonl` instead of the id
Antiphon passed. Investigation (2026-07-22): the launch command line is correct (`--session-id`
present, read live off `claude.exe`); it is not `--append-system-prompt` (a headed diagnostic
honoured the id with the flag); and the live process env has **no** `CLAUDE_CODE_SESSION_ID` /
`CLAUDE_CODE_CHILD_SESSION` (read via PEB) — so it is neither an arg nor a nesting-marker bug, but a
Claude interactive-mode behaviour we don't control.

**Fix:** `TranscriptTailer.LocateAsync` now prefers `<session-id>.jsonl` but, after a 10 s grace,
falls back to discovering the real transcript by its `cwd` field — the newest transcript whose
recorded cwd matches this session's, preferring one that appeared after the tailer started (a fresh
fork) and, on re-adoption, the newest cwd match overall. **Verified live end-to-end** the same day:
inbound → agent → **PONG reply delivered to `channels.outbound` and recorded by the gateway**.

Limitation: two agents sharing one working directory could be ambiguous under the fallback (the
exact-id fast path is unaffected). The workspace model gives each agent its own cwd, so this is not
a concern in practice. `AgentRegistry` still scrubs the nesting markers (harmless/defensive).

## Migrations note (2026-07-22)

`AddAgentSystemPromptAppend`, `AddCompactionRecoveryWatermark`, and `AddQueuedMessageOrigin`
are hand-written (no Designer.cs) because the running dev server locked `bin/`. The snapshot
was updated by hand and must be verified with
`dotnet ef migrations has-pending-model-changes --project server` (expect "no changes")
whenever the server is stopped — done at the 2026-07-22 restart.
