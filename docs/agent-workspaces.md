# Agent workspaces (ClaudeBot convention)

How channel-facing (Telegram-bot) agents are scoped on disk. Part of the Telegram bot agents
epic — spec: [2026-07-21-telegram-bot-agents.md](superpowers/specs/2026-07-21-telegram-bot-agents.md).

## The convention

A channel-facing agent's `Agent.WorkingDirectory` points at a **ClaudeBot agent workspace**:

```
C:\src\ClaudeBot\agents\<agentName>\
```

`WorkingDirectory` is already the session cwd and the resume-matching key — no server-side
enforcement exists or is needed; this is a documented convention. The pilot is
`agents/family/` (persona "Mikey", behind `@antiphon_assistant_bot`); `agents/codeperf/` is
the business-side counterpart.

## Files in a workspace (OpenClaw-style)

| File | Role | Read when |
|---|---|---|
| `CLAUDE.md` | Session-start ritual, compaction recovery steps, Telegram reply contract. Claude Code injects it as system context natively — it survives compaction for free. | Automatic |
| `SOUL.md` | Identity, tone, boundaries, what this agent handles | Session start |
| `USER.md` | Who the agent is talking to (may layer on a root profile) | Session start |
| `MEMORY.md` | Curated long-term memory; agent-owned | Session start + after compaction |
| `memory/YYYY-MM-DD.md` | Daily raw log; agent-owned, created on first use each day | Today + yesterday at session start |
| `BOOTSTRAP.md` | One-time first-run ritual; the agent completes it then DELETES it (absence = already bootstrapped) | First run only |

## How Antiphon interacts with the workspace

- **Channel preamble** (`Agent.SystemPromptAppend`, rendered into `--append-system-prompt` at
  every launch — fresh and resume) carries the channel contract: envelope format, batch
  markers, reply rules, `NO_REPLY`. It deliberately does NOT carry identity — that is the
  workspace's job, so the agent owns its own persona files.
- **Bootstrap note**: on a genuinely fresh session (or a failed-resume fallback) Antiphon
  queues one message telling the agent to run its CLAUDE.md session-start ritual and reply
  READY.
- **Restart note**: on a successful resume, a cheaper note — skim today's memory log, don't
  re-execute completed work, `NO_REPLY` unless there's something for the user.
- **Compaction recovery note**: when a compact boundary is detected on the session, Antiphon
  queues a system note telling the agent to re-read its workspace files before continuing.

The agent writes to its own workspace (memory files); Antiphon never writes into it.
