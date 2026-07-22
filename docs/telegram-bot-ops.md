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

## Known issue — outbound reply routing depends on the transcript tailer (2026-07-22)

The channel reply path (agent's turn → `ChannelReplyDispatcher` → `channels.outbound` → gateway)
is driven by the **transcript tailer**, which follows `~/.claude/projects/<enc-cwd>/<session-id>.jsonl`
using the session id Antiphon assigned. During live dev verification the agent's Claude process
was observed writing its conversation to a **different, self-chosen** `<uuid>.jsonl` instead of the
`--session-id` Antiphon passed — so the tailer followed a stale file, never saw the turn-end, and
no reply was produced (channels.outbound stayed empty).

What is confirmed:
- Print mode (`claude -p --session-id <id>`) **honors** the id (writes `<id>.jsonl`), with or without `--append-system-prompt`.
- The headed canary's interactive launch honors it too — but only with the nesting-marker env scrub (`ClSession.HeadedSafeEnv`).
- `AgentRegistry.Resolve` now scrubs those markers (`CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDECODE`, …) for spawned ClaudeCode agents, but the dev runner-spawned agent still forked — so a second factor (runner env propagation, or a Claude 2.1.x interactive behavior) remains.

Impact: **inbound delivery is unaffected** (it uses the raw PTY output stream, not the tailer) — messages reach the agent and it responds in its terminal (verified: PONG / NO_REPLY). Only the automatic **reply back to the chat** is blocked when the session-id forks. The reply-routing code itself is covered by CI (`ChannelBridgeTests.Turn_end_sends_the_agents_answer_down_the_channel`, `ChannelBatchingTests`).

Follow-up: confirm the spawned agent's actual environment (read the live process env / add an env dump to the pty audit), and if markers still leak, fix the runner/pty-host env propagation so `AgentRegistry`'s empty-string overrides reach Claude; alternatively teach the tailer to resolve the newest session file for the cwd when `<session-id>.jsonl` never appears.

## Migrations note (2026-07-22)

`AddAgentSystemPromptAppend`, `AddCompactionRecoveryWatermark`, and `AddQueuedMessageOrigin`
are hand-written (no Designer.cs) because the running dev server locked `bin/`. The snapshot
was updated by hand and must be verified with
`dotnet ef migrations has-pending-model-changes --project server` (expect "no changes")
whenever the server is stopped — done at the 2026-07-22 restart.
