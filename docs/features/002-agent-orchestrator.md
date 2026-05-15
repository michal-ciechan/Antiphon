# Feature 002 — Agent Orchestrator (Hook-Driven Work Queue)

> **Status:** Draft — to merge / reconcile with [Feature 001 — Kanban + Agent PTY Orchestration](001-kanban-agent-orchestration/plan.md).
>
> Captured from chat design session 2026-05-15. Preserves the original framing
> ("OSS agent framework, scheduler + REST + hooks, agent-per-folder") so it can
> be cross-referenced against the existing F001 design and merged where overlap
> exists. **Many concepts duplicate F001 — this doc is the input, not the
> final plan.** Reconciliation pass needed before any code.

---

## 1. Goal

Generic, MIT-licensed agent framework that:

- Hosts a **work queue** server. Agents pull next job when idle.
- Provides **Claude Code hooks** that connect agents to the server.
- Uses **agent-per-folder** layout: `agents/<id>/` holds skills, `CLAUDE.md`,
  hooks, settings — agent runs with that folder as cwd.
- Has a **scheduler/orchestrator** above the agent folders that:
  - Spawns agents that aren't running.
  - Monitors session liveness without polling Claude Code (event-driven via hooks).
  - Recovers from crashes / hung sessions.
- Supports **two spawn modes**:
  - `visible` — real terminal window per agent (originally proposed: Windows Terminal `wt.exe` tabs).
  - `headless` — no window, agent driven via PTY (Antiphon already uses `Porta.Pty`).
- Exposes a **REST API** for job submission + status.
- Renames terminal window titles to the agent name in visible mode.

Originally framed as a standalone OSS project; **decision (2026-05-15)**: build
into Antiphon. This doc is its input plan.

---

## 2. Locked design decisions (from chat)

| Decision | Choice | Rationale |
|---|---|---|
| Stack | C# / .NET + ASP.NET Core + EF Core | Matches Antiphon stack |
| Queue backend | EF Core, SQLite default, provider-pluggable | Antiphon currently Postgres → reconcile |
| Agent runtime | `claude` CLI (interactive subprocess) | Cost — `-p` (headless API mode) ruled out, sub uses Claude Max/Pro plan |
| Transport | HTTP **long poll** (Stop hook → blocking GET) | Fits hook lifecycle; SSE/WS overkill |
| Idle re-trigger | Stop hook returns `{"decision":"block","reason":"<prompt>"}` to keep session live without user keystroke | Only supported mechanism in interactive Claude Code |
| Liveness | Stop-hook heartbeat + PID watch (layered) | Catches both hung-but-alive and clean-crash |
| Identity | env var `AGENT_ID` = folder name, injected at spawn | Simple, deterministic |
| Window title | Scheduler renames terminal title to `AGENT_ID` (visible mode) | UX |
| Job submission | REST API | `POST /api/jobs` |
| Lifecycle hooks | Stop (request next work) + UserPromptSubmit/SessionStart (notify "starting", send prompt prefix) | Round-trip telemetry |
| Spawn modes | `visible` (real terminal) + `headless` (PTY) | Two use cases |
| Visible spawner | `wt.exe -w 0 nt --title {name} --startingDirectory {dir} -- ...` (Windows-only first) | Modern Windows default |
| PTY library | `Porta.Pty` (already used by `Antiphon.Agents.Pty`) | Active maintenance — matches existing code |
| Cardinality | 1 agent per folder | Keeps semantics simple |
| Job result reporting | Full transcript / final message | Audit + debug |
| License | MIT | Antiphon already MIT |

---

## 3. Architecture (proposed before reconciling with F001)

```
┌──────────────────────────────────────────────┐
│  Scheduler + Server (single ASP.NET host)    │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐   │
│  │ Web API  │  │ Job Queue│  │ Spawner  │   │
│  │ (REST +  │  │ (EFCore) │  │ (PTY/wt) │   │
│  │  SignalR)│  │          │  │          │   │
│  └──────────┘  └──────────┘  └──────────┘   │
└──────────────────────────────────────────────┘
         ▲                        │
         │ HTTP (long poll)       │ spawn process
         │ from Stop hook         ▼
┌──────────────────────────────────────────────┐
│  agents/foo/  (cwd, AGENT_ID=foo)            │
│  ├── .claude/                                │
│  │   ├── settings.json (hooks)               │
│  │   ├── skills/                             │
│  │   ├── CLAUDE.md  ("soul")                 │
│  │   └── hooks/                              │
│  │       ├── on-stop.sh / .ps1               │
│  │       │   (long-poll for next work)       │
│  │       └── on-start.sh / .ps1              │
│  │           (notify start + prompt prefix)  │
│  └── (working files)                         │
└──────────────────────────────────────────────┘
```

### Differences from Feature 001

| F002 (this doc) | F001 (existing) | Reconcile |
|---|---|---|
| `Job` queue, agents pull | `Issue` + `RunAttempt`, orchestrator dispatches | Same shape — `Job` ≈ `RunAttempt` work unit |
| Agent identity = folder name | Agent identity = `AgentSession` row + worktree path | F001 richer; folder-name id can be agent's `WORKFLOW.md` board |
| Long-poll Stop hook returns prompt | Orchestrator tick spawns agent fresh per attempt | F002 keeps interactive session alive across jobs (cheaper); F001 spawns per attempt (cleaner state) |
| Visible mode = `wt.exe` tabs | Visible mode = xterm.js in browser | F001 superior — single dashboard, cross-OS, attaches/detaches, scrollback |
| SQLite default | Postgres | Pick one — Postgres for consistency, EF provider abstraction allows SQLite for OSS demo |
| Window title via wt.exe | n/a (browser tab) | Drop wt.exe path if going browser-only |

**Recommendation:** F001's xterm.js + per-attempt spawn model is superior to
F002's wt.exe + persistent-session model **for production / multi-user**.
F002's persistent-session + long-poll model is superior **for personal /
local single-machine use** because it preserves Claude conversation context
across jobs. **Both could coexist**: F001 spawn-per-attempt + F002
persistent-session as alternate `IRunMode`.

---

## 4. Data model (EF Core)

```csharp
public class Agent {
    public string Id { get; set; }           // = folder name
    public string FolderPath { get; set; }
    public SpawnMode Mode { get; set; }      // Visible | Headless
    public AgentStatus Status { get; set; }  // Stopped | Starting | Idle | Working | Crashed
    public DateTime LastHeartbeat { get; set; }
    public int? ProcessId { get; set; }
    public string? CurrentJobId { get; set; }
}

public class Job {
    public Guid Id { get; set; }
    public string AgentId { get; set; }      // FK
    public string Prompt { get; set; }
    public JobStatus Status { get; set; }    // Queued | Running | Succeeded | Failed | Cancelled
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? Transcript { get; set; }  // populated on completion
    public string? FinalMessage { get; set; }
    public int Priority { get; set; }
    public int AttemptCount { get; set; }
}

public class JobEvent {
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public JobEventType Type { get; set; }   // Started | Progress | Completed | Failed | Heartbeat
    public DateTime At { get; set; }
    public string? Payload { get; set; }     // JSON
}

public enum SpawnMode { Visible, Headless }
public enum AgentStatus { Stopped, Starting, Idle, Working, Crashed }
public enum JobStatus { Queued, Running, Succeeded, Failed, Cancelled }
public enum JobEventType { Started, Progress, Completed, Failed, Heartbeat }
```

**Reconcile with F001:** `Job` overlaps `RunAttempt`. `Agent` overlaps
`AgentSession`. Likely collapse into F001 entities + add fields, not separate
tables.

---

## 5. REST API

```
# Job submission + status
POST   /api/jobs                        body: { agentId, prompt, priority? }  -> { jobId }
GET    /api/jobs/{id}                   status + transcript when done
GET    /api/jobs?agentId=&status=       list with filters
DELETE /api/jobs/{id}                   cancel (in-flight or queued)

# Agent control
GET    /api/agents                      list + current status
POST   /api/agents/{id}/start           force spawn (idempotent)
POST   /api/agents/{id}/stop            terminate cleanly
POST   /api/agents/{id}/kill            SIGKILL
GET    /api/agents/{id}/logs            recent stdout (headless only) — ring buffer

# Hook endpoints (called by Claude Code hooks, not for end users)
GET    /api/hooks/{agentId}/next-work          long-poll (default 300s), 200 + JSON {jobId, prompt} or 204 on timeout
POST   /api/hooks/{agentId}/job-started        body: { jobId, promptPrefix }
POST   /api/hooks/{agentId}/job-done           body: { jobId, success, transcript, finalMessage, durationMs }
POST   /api/hooks/{agentId}/heartbeat          body: { state }  — keepalive while polling
```

All routes follow Antiphon `kebab-case` convention per `project-context.md`.

---

## 6. Hook contracts

### `on-stop.sh` (Stop hook — pulls next work)

```bash
#!/usr/bin/env bash
# AGENT_ID + AGENT_SERVER injected at spawn.
SERVER="${AGENT_SERVER:-http://localhost:5000}"
STATE_DIR=".claude/.agent-state"
mkdir -p "$STATE_DIR"

# 1. Notify completion of previous job (if any).
if [ -f "$STATE_DIR/current-job" ]; then
  jobid=$(cat "$STATE_DIR/current-job")
  transcript=$(jq -Rs . < "$STATE_DIR/last-transcript" 2>/dev/null || echo '""')
  curl -s -X POST "$SERVER/api/hooks/$AGENT_ID/job-done" \
    -H 'Content-Type: application/json' \
    -d "{\"jobId\":\"$jobid\",\"success\":true,\"transcript\":$transcript}"
  rm "$STATE_DIR/current-job"
fi

# 2. Long-poll for next work. Loop on timeout to avoid idle gap.
while true; do
  resp=$(curl -s --max-time 300 "$SERVER/api/hooks/$AGENT_ID/next-work")
  if [ -n "$resp" ] && [ "$resp" != "null" ]; then
    jobid=$(echo "$resp" | jq -r .jobId)
    prompt=$(echo "$resp" | jq -r .prompt)
    echo "$jobid" > "$STATE_DIR/current-job"
    # Re-prompt Claude session with next job's prompt (no user keystroke needed).
    jq -n --arg p "$prompt" '{decision:"block", reason:$p}'
    exit 0
  fi
  # 204 / empty -> reconnect, server will hold next request again.
done
```

PowerShell variant ships alongside for Windows agents.

### `on-start.sh` (UserPromptSubmit / SessionStart — notify start)

```bash
#!/usr/bin/env bash
SERVER="${AGENT_SERVER:-http://localhost:5000}"
STATE_DIR=".claude/.agent-state"
jobid=$(cat "$STATE_DIR/current-job" 2>/dev/null || echo "")
prompt_prefix=$(echo "$CLAUDE_USER_PROMPT" | head -c 500)

curl -s -X POST "$SERVER/api/hooks/$AGENT_ID/job-started" \
  -H 'Content-Type: application/json' \
  -d "$(jq -n --arg j "$jobid" --arg p "$prompt_prefix" \
        '{jobId:$j, promptPrefix:$p}')"
```

### `settings.json.template`

```json
{
  "hooks": {
    "Stop": [
      { "matcher": "*", "hooks": [{ "type": "command", "command": ".claude/hooks/on-stop.sh" }] }
    ],
    "UserPromptSubmit": [
      { "matcher": "*", "hooks": [{ "type": "command", "command": ".claude/hooks/on-start.sh" }] }
    ]
  }
}
```

---

## 7. Spawn modes

### Visible (`wt.exe` — Windows-first; OSS users on mac/linux pluggable later)

```
wt.exe -w 0 nt --title "agent:{name}" \
  --startingDirectory "{folder}" \
  -- cmd /c "set AGENT_ID={name}&& set AGENT_SERVER={server}&& claude"
```

Each agent = new tab in shared Windows Terminal window. Title set via
`--title`. Env injected via wrapping `cmd /c set ... && ...`.

**Cross-platform later:** `IVisibleSpawner` interface, OS-specific
implementations (`WtSpawner`, `ITermSpawner`, `XTerminalSpawner`) selected
via config command template.

### Headless (PTY via `Porta.Pty`)

- Reuse `Antiphon.Agents.Pty.PtyAgentRunner`.
- Spawner allocates PTY, runs `claude` with env, captures stdout to
  `RingBuffer<string>` (already exposed via `Antiphon.Agents.Pty`).
- Optional `PtySessionAudit` writes full transcript to disk.
- Exposed via `GET /api/agents/{id}/logs` for ring buffer tail; full
  transcript available per-job via `Job.Transcript`.

`IPtyAdapter` abstraction stays — fits cleanly with existing F001 plan
(`Infrastructure/Agents/PtyAgentRunner.cs`).

---

## 8. Liveness + recovery

| Mechanism | Detects | Action |
|---|---|---|
| Heartbeat (hook posts every 60s while polling) | Hang within active session | Mark `Crashed` if no heartbeat in 90s; kill PID; respawn |
| PID watch (spawner tracks child) | Process exit (clean or crash) | Mark `Stopped`; on enabled-with-queued → respawn |
| Scheduler tick (10s) | `Stopped` agent with queued jobs | Respawn |
| Hook posts `job-done` with `success:false` after respawn | Crashed mid-job | Re-queue per policy (max-attempts default 3) |

Stashed `current-job` in `.claude/.agent-state/` lets hook self-report
mid-job crash on next start.

---

## 9. Folder layout

### Repo (already Antiphon)

```
d:/src/Antiphon/
├── server/
│   ├── Domain/
│   ├── Application/
│   ├── Infrastructure/
│   │   ├── Orchestration/      # F001 Symphony tick + retry — extend with F002 long-poll endpoint
│   │   ├── Agents/             # PtyAgentRunner (Porta.Pty) — already exists
│   │   ├── Spawning/           # NEW: WtVisibleSpawner, HeadlessPtySpawner
│   │   └── Hosting/
│   └── Api/                    # NEW endpoints: /api/jobs, /api/hooks/...
├── client/                     # xterm.js + Mantine (F001 plan)
├── src/Antiphon.Agents.Pty/    # PTY runner library (already exists)
├── hooks/                      # NEW: shippable hook scripts
│   ├── on-stop.sh / on-stop.ps1
│   ├── on-start.sh / on-start.ps1
│   └── settings.json.template
├── examples/agents/            # NEW: demo agent folders
│   └── demo-agent/
│       ├── .claude/
│       │   ├── settings.json   (copied from hooks/settings.json.template)
│       │   ├── hooks/
│       │   ├── skills/
│       │   └── CLAUDE.md
│       └── README.md
└── docs/features/002-agent-orchestrator.md   # this file
```

### Per-agent (under configured `AgentsRoot`)

```
{AgentsRoot}/{agentId}/
├── .claude/
│   ├── settings.json           hooks wired
│   ├── hooks/
│   │   ├── on-stop.sh
│   │   └── on-start.sh
│   ├── skills/                 per-agent skills
│   ├── CLAUDE.md               per-agent "soul" / instructions
│   └── .agent-state/           current-job, last-transcript (gitignored)
└── (working files agent operates on)
```

---

## 10. Config (Antiphon `appsettings.json`)

```json
{
  "AgentOrchestrator": {
    "AgentsRoot": "D:\\Antiphon\\agents",
    "ServerUrl": "http://localhost:5000",
    "Spawn": {
      "DefaultMode": "Visible",
      "VisibleCommand": "wt.exe -w 0 nt --title \"agent:{name}\" --startingDirectory \"{dir}\" -- cmd /c \"set AGENT_ID={name}&& set AGENT_SERVER={server}&& claude\"",
      "HeadlessAdapter": "PortaPty"
    },
    "Polling": {
      "LongPollSeconds": 300,
      "HeartbeatSeconds": 60,
      "IdleTimeoutSeconds": 90
    },
    "Recovery": {
      "MaxAttemptsPerJob": 3,
      "RespawnTickSeconds": 10
    }
  }
}
```

Provider abstraction for queue:
```json
"Database": {
  "Provider": "Postgres",   // Postgres | Sqlite | SqlServer
  "ConnectionString": "..."
}
```

---

## 11. Open questions / decisions to reconcile with F001

1. **Persistent-session vs spawn-per-attempt model.** F002 keeps `claude`
   alive across jobs (cheaper, preserves context). F001 spawns fresh per
   `RunAttempt` (cleaner state, matches Symphony). Pick one or support both
   as `IRunMode` strategy.
2. **`Job` vs `RunAttempt`.** Likely same concept — collapse F002 `Job` into
   F001 `RunAttempt` + add fields (`Transcript`, `FinalMessage`).
3. **`Agent` vs `AgentSession`.** F002 `Agent` = persistent identity;
   F001 `AgentSession` = single live PTY child. F002's identity layer
   maps to F001 board/agent registry; F001's session = transient.
4. **Visible mode: `wt.exe` vs xterm.js.** F001's xterm.js wins for multi-user
   / dashboard. F002's `wt.exe` wins for "real terminal feel" + native
   keybindings. Decision: ship xterm.js as primary (per F001), keep
   `wt.exe` as optional `IVisibleSpawner` for users who prefer real terminal.
5. **SQLite vs Postgres.** F001 = Postgres (per `project-context.md`).
   F002 = SQLite default. Decision: keep Postgres for Antiphon proper;
   document SQLite path for OSS users / personal mode via EF Core provider
   swap. Add provider switch + migrations for both.
6. **Hook script trust model.** Same as F001 risk #9 — hooks are arbitrary
   shell. Document trust boundary; consider JobObject + path allowlist later.
7. **Re-trigger mechanism.** F002 relies on Stop hook returning
   `{"decision":"block"}` to keep interactive session alive. Verify
   Claude Code supports this exact contract (it does per docs, but pin the
   version). F001 doesn't need this — fresh spawn per attempt sidesteps it.
8. **Window title rename.** Only meaningful for `wt.exe` mode. Browser/xterm.js
   replaces this. Fold into `IVisibleSpawner` impl.

---

## 12. Recommended merge into F001

1. Add **F002 `Job` lifecycle hooks** (REST endpoints `next-work`,
   `job-started`, `job-done`, `heartbeat`) as alternate dispatch path
   alongside F001's tracker-driven orchestrator. One acts on `RunAttempt`
   rows, both use same downstream.
2. Add **persistent-session run mode** as `IRunMode` strategy (`SpawnPerAttempt`
   default, `PersistentSession` opt-in for personal use).
3. Add **`IVisibleSpawner`** interface with `WtVisibleSpawner` (Windows)
   alongside the F001 xterm.js path.
4. Ship **hook scripts under `hooks/`** + **`examples/agents/demo-agent/`** so
   OSS users can clone Antiphon and run a single agent without the full
   tracker setup.
5. Add **EF Core SQLite provider option** alongside Postgres for personal /
   single-binary OSS deploy.

After merge, F002 effectively becomes "personal/single-host operating mode
+ hook-driven dispatch path" of F001 rather than a separate feature.

---

## 13. Phase plan (if shipped standalone before merge)

1. **MVP**: SQLite, REST `/api/jobs` + hook endpoints, `wt.exe` visible mode,
   single-host, hook scripts + demo agent.
2. **Headless mode**: wire `Porta.Pty` adapter, log ring buffer.
3. **xterm.js dashboard** (collapses with F001 E08).
4. **Multi-host**: scheduler + remote workers (collapses with F001 future).
5. **Auth**: API keys per agent + per submitter.

---

## 14. References

- Feature 001 plan: [`001-kanban-agent-orchestration/plan.md`](001-kanban-agent-orchestration/plan.md)
- Feature 001 requirements: [`001-kanban-agent-orchestration/01-requirements.md`](001-kanban-agent-orchestration/01-requirements.md)
- Feature 001 epics: [`001-kanban-agent-orchestration/05-epics.md`](001-kanban-agent-orchestration/05-epics.md)
- Antiphon conventions: [`../project-context.md`](../project-context.md)
- Existing PTY runner: `src/Antiphon.Agents.Pty/PtyAgentRunner.cs` (uses `Porta.Pty`)
- Claude Code Stop hook docs (re-prompt via `decision:"block"`): https://docs.claude.com/en/docs/claude-code/hooks
- Symphony (referenced by F001): https://github.com/openai/symphony
- amux (referenced by F001): https://github.com/mixpeek/amux
