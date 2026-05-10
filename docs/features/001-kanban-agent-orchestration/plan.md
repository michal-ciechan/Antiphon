# Antiphon Kanban + Agent PTY Orchestration — Plan

## What you're really building

Three overlapping concepts merged:

| Source | Borrow |
|--------|--------|
| **vibe-kanban** | board UI, issue→workspace→PR loop, diff review w/ inline comments, worktree-per-card |
| **amux** | parallel sessions, REST/channel coordination, atomic task claiming, watchdog auto-compact, conversation forking |
| **vs-pty.net** | spawn `claude`, `codex`, `gemini`, `cursor-agent` etc. as headed PTY children from .NET — capture full ANSI stream, send keystrokes, resize |

Antiphon already has Workflows / Stages / Gates / Artifacts / SignalR / state machine. Kanban = **second view over existing workflow domain**, plus new `AgentSession` aggregate for live PTY processes.

---

## Domain additions (Onion-clean)

```
Domain/Entities/
  Board.cs              # board per repo or per-workspace
  Card.cs               # = lightweight issue; can spawn AgentSession
  Column.cs             # backlog/in-progress/review/done (configurable)
  AgentSession.cs       # live PTY child; FK -> Card, Workflow optional
  Worktree.cs           # path, branch, base ref, status

Domain/Enums/
  AgentKind.cs          # ClaudeCode, Codex, Gemini, Cursor, Aider, Custom
  SessionStatus.cs      # Starting, Running, AwaitingPrompt, Stopped, Crashed
  CardStatus.cs

Domain/StateMachine/
  CardStateMachine.cs   # mirror existing WorkflowStateMachine pattern
```

Card → 0..N AgentSessions (forking like amux). Card → 1 Worktree. Worktree → 1 git branch.

---

## Application layer

```
Application/Interfaces/
  IPtyAgentRunner.cs    # Start, WriteInput, Resize, Kill, OnOutput event
  IWorktreeManager.cs   # Create/Remove/List git worktrees
  IAgentRegistry.cs     # known CLI agents + launch args
  IBoardRepository.cs / ICardRepository.cs / ISessionRepository.cs

Application/Services/
  BoardService           # CRUD + reorder
  CardService            # spawn worktree + session on move-to-in-progress
  AgentSessionService    # lifecycle, channel routing, watchdog hooks
  AgentCoordinator       # amux-style peer discovery + atomic claim
```

`IPtyAgentRunner` keeps Pty.Net dep out of Domain/Application. Implementation in Infra.

---

## Infrastructure

```
Infrastructure/Agents/
  PtyAgentRunner.cs       # vs-pty.net wrapper. Process tree, ANSI buffer ring.
  AgentRegistry.cs        # JSON config: name, exe, args template, auto-prompt rules
  AnsiStripParser.cs      # mirror amux: parse without hooks
  WatchdogHostedService.cs# auto-compact + auto-respond to stuck prompts
  AgentChannelHub.cs      # SignalR sub-hub for inter-session @mentions

Infrastructure/Git/
  WorktreeManager.cs      # libgit2sharp or `git worktree` shell-out
```

vs-pty.net notes:
- Use `Pty.Net.PtyProvider.SpawnAsync` w/ env, cwd = worktree path
- Read `.ReaderStream` async, push deltas over SignalR `AgentTextDelta` (already exists in Antiphon)
- Track exit via `.ProcessExited`; auto-restart per policy
- Resize on client viewport change via `.Resize(cols, rows)`

---

## API (Minimal APIs, follow `/api/...` kebab convention)

```
GET    /api/boards
POST   /api/boards
GET    /api/boards/{id}/cards
POST   /api/cards
PATCH  /api/cards/{id}            # move column / reorder
POST   /api/cards/{id}/spawn      # body: { agentKind, prompt }  -> creates AgentSession + Worktree
DELETE /api/cards/{id}

GET    /api/sessions
GET    /api/sessions/{id}/buffer  # full ANSI replay for late joiners
POST   /api/sessions/{id}/input   # body: { keys }
POST   /api/sessions/{id}/resize  # body: { cols, rows }
DELETE /api/sessions/{id}         # SIGTERM then SIGKILL
POST   /api/sessions/{id}/fork    # clone history -> new session

POST   /api/cards/{id}/pr         # open GitHub PR from worktree branch
```

SignalR new events: `CardMoved`, `SessionStarted`, `SessionOutput`, `SessionExited`, `AgentMentioned`.

---

## Frontend (`client/src/features/kanban/`)

Mantine + dnd-kit for board, xterm.js for live terminal panes.

```
features/kanban/
  BoardPage.tsx           # column grid, drag/drop
  CardModal.tsx           # detail + spawn agent
  SessionTerminal.tsx     # xterm.js, attach SignalR stream + REST replay
  SessionTabs.tsx         # multi-session per card
  DiffReview.tsx          # reuse artifact viewer; inline comments
  AgentPicker.tsx         # registry-driven dropdown
hooks/
  useSessionStream.ts     # subscribes to AgentTextDelta filtered by sessionId
  useBoard.ts             # TanStack Query
stores/
  useTerminalStore.ts     # ANSI ring buffer per session
```

xterm.js gets the REST `/buffer` for backlog, then SignalR deltas for live. Same pattern Antiphon already uses for streaming chat.

---

## Worktree lifecycle (vibe-kanban model)

1. Card created → no worktree
2. Card moved to **In Progress** (or `/spawn` called) → `git worktree add workspace/card-{id} -b feat/card-{id}`
3. AgentSession spawned with `cwd = worktree path`
4. Card moved to **Review** → diff view, inline comments routed back as agent input via channel
5. Card moved to **Done** → PR opened or worktree pruned

Auto-cleanup: `Worktree.Status = Stale` after N days → background job removes.

---

## amux-style coordination

- **Atomic claim**: `Card.OwnerSessionId` w/ optimistic concurrency token. Two agents can't grab same card.
- **Channels**: SignalR group per `card:{id}`; agents `@mention` peers via output filter that triggers `AgentChannelHub.SendAsync(targetSessionId, msg)` → injected into target PTY stdin.
- **Shared memory**: dedicated `KanbanNotes` table, scoped per board, queried via `/api/boards/{id}/notes`. Agents read/write via tool/MCP.
- **Watchdog**: `WatchdogHostedService` polls each session, detects "Press Enter to continue" / "(Y/n)" patterns from registry, auto-responds.

---

## Risks / decisions to make first

1. **Process model on Windows.** vs-pty.net uses winpty/conhost — fine for Windows, but Antiphon Aspire host launching dozens of PTYs needs resource ceiling + per-session memory cap. Cgroups not native on Windows; lean on `JobObject`.
2. **Worktree race.** Multiple sessions per card on same worktree = git index corruption. Enforce: 1 active session per worktree; forks get new worktree from same base.
3. **Auth/secrets per agent.** Each CLI agent reads `~/.claude`, `~/.codex`, env vars. Decide: shared user creds vs per-agent secret store.
4. **Buffer storage.** Full ANSI buffer per session can grow huge. Ring buffer w/ disk spill (matches existing `C:\MavLog\Antiphon\` log convention).
5. **Already have Workflows/Stages.** Decide: Kanban Card == thin wrapper over Workflow, or sibling concept. Recommend **Card spawns Workflow** so existing gate/stage/cost ledger reused for free.
6. **Vibe-kanban sunsetting** — fork their UI patterns, not their stack (Rust). Antiphon stays C#/React.

---

## Recommended slice order

1. `IPtyAgentRunner` + Pty.Net spike — spawn `claude --version`, capture output, kill. Prove on Windows.
2. `Worktree` + `WorktreeManager` — minimal CRUD over `git worktree`.
3. Domain entities + EF migration (remember: `.\stop-server.ps1` first).
4. `AgentSession` lifecycle + SignalR streaming. Reuse `AgentTextDelta`.
5. xterm.js terminal pane in existing dashboard. Single session, no board yet.
6. Board UI + drag-drop + spawn-on-move.
7. Coordination: channels, atomic claim, watchdog.
8. Diff review reusing artifact feature.
9. GitHub PR open.

---

## Symphony (OpenAI) — additional primitives to steal

Reference: https://github.com/openai/symphony/blob/main/SPEC.md

Symphony = long-running automation service that polls issue tracker, creates per-issue workspace, runs coding agent. Different angle from vibe-kanban (UI-driven) and amux (chat-driven) — **tracker-driven autonomous loop**. Maps cleanly onto Antiphon's Workflow engine.

### Concepts to lift

| Symphony primitive | Antiphon mapping |
|--------------------|------------------|
| `WORKFLOW.md` (YAML front matter + Markdown prompt) | New `Board.WorkflowDefinition` field; reuse Antiphon `Workflow` entity. Hot reload via file watcher. |
| `Issue` (tracker-normalized) | `Card` already covers; add `ExternalRef { trackerKind, trackerId, identifier, priority, blockers[] }`. |
| `Run Attempt` (lifecycle phases) | New `RunAttempt` entity FK→Card. Phases: `PreparingWorkspace, BuildingPrompt, LaunchingAgent, InitializingSession, StreamingTurn, Finishing, Succeeded/Failed/TimedOut/Stalled/Canceled`. Mirrors existing `StageExecution` pattern. |
| `Live Session` (thread_id, turn_id, tokens) | Already implied by `AgentSession`; add `ThreadId`, `TurnId`, `TokenUsage`, `RateLimitInfo`. Compose `SessionId = "{threadId}-{turnId}"`. |
| `Retry Entry` (exp backoff) | New `RetrySchedule` value object on Card: `attempt`, `nextAt`, `lastError`. Defaults: continuation 1s; failure `10s × 2^(n-1)` capped at 5m. |
| `Orchestrator Runtime State` (in-memory) | New `IOrchestrator` singleton hosted service. **Tracker-driven recovery** — no retry-timer persistence; on restart repoll. |
| `Hooks` (after_create, before_run, after_run, before_remove) | `IWorkspaceHookRunner`. Shell scripts in workspace, timeouts enforced. `after_create`/`before_run` failure aborts; `after_run` failure logged-only. |
| Concurrency: `max_concurrent_agents` global + per-state | `OrchestratorSettings.MaxGlobal`, `MaxByColumn[columnId]`. Eligibility check before dispatch. |
| Polling tick (default 30s) | `OrchestratorTickHostedService` w/ `PollIntervalSeconds`. |
| Reconcile loop | Per-tick: refresh state from tracker, detect stalls (`stall_timeout_ms`), cancel stuck attempts. |
| Workspace safety invariants | `WorktreeManager` enforce: path under root; sanitized name (`[A-Za-z0-9._-]+`); agent cwd = workspace only. |
| Dynamic reload of `WORKFLOW.md` | `IFileSystemWatcher` on board's workflow file → re-parse; bad reload keeps last-good + surfaces error event. |
| Snapshot API `/api/v1/state` | `GET /api/orchestrator/state` → running sessions, retry queue, aggregate tokens, runtime seconds. Powers ops dashboard. |
| Tracker abstraction | `IIssueTracker` w/ `FetchCandidates`, `FetchByStates`, `FetchByIds`. Adapters: `LinearTracker`, `GitHubIssuesTracker`, `JiraTracker`, `AntiphonInternalTracker` (board-only). |
| Streaming protocol (Codex app-server) | Generic `IAgentProtocolAdapter` w/ adapters: `ClaudeCodeJsonStream`, `CodexAppServer`, `RawPty` (fallback via vs-pty.net). |
| Timeouts: `read_timeout_ms` 5s, `turn_timeout_ms` 1h, `stall_timeout_ms` 5m | `AgentSessionSettings`. Stall detector tied to last-event timestamp. |

### Three operational modes (combine all three)

1. **Tracker-driven** (Symphony) — orchestrator polls Linear/GitHub/Jira; auto-spawns sessions for eligible issues. Headless overnight runs.
2. **Board-driven** (vibe-kanban) — user drags card → spawn. Interactive review via xterm.js + diff.
3. **Chat-driven** (amux) — channels/@mentions; agents delegate to peers; conversation forking.

All three feed same `RunAttempt` + `AgentSession` machinery. Differ only in trigger source.

### Domain delta from Symphony

Add to Domain layer:

```
Domain/Entities/
  RunAttempt.cs           # phase, attempt#, startedAt, endedAt, exitReason
  RetrySchedule.cs        # value object: attempt, nextAt, lastError, backoffMs
  WorkflowDefinition.cs   # YAML config + prompt template body
  ExternalIssueRef.cs     # trackerKind, trackerId, identifier, priority, blockers[]
  TokenUsage.cs           # inputTokens, outputTokens, cachedTokens (hook into existing CostLedger)

Domain/Enums/
  RunPhase.cs             # PreparingWorkspace .. Succeeded/Failed/TimedOut/Stalled/Canceled
  TrackerKind.cs          # Linear, GitHubIssues, Jira, Internal
```

### Application layer additions

```
Application/Interfaces/
  IIssueTracker.cs
  IOrchestrator.cs
  IAgentProtocolAdapter.cs
  IWorkspaceHookRunner.cs
  IPromptTemplateRenderer.cs   # strict variable expansion; unknown var = render fail

Application/Services/
  OrchestratorService            # poll tick, dispatch, reconcile, retry scheduling
  RunAttemptService              # phase transitions, hook invocation, telemetry
  WorkflowDefinitionLoader       # YAML+Markdown parse, hot reload
```

### API additions

```
GET    /api/orchestrator/state            # snapshot: running, retry queue, totals
POST   /api/orchestrator/pause
POST   /api/orchestrator/resume
GET    /api/boards/{id}/workflow          # current WORKFLOW.md content
PUT    /api/boards/{id}/workflow          # edit + hot-reload
POST   /api/boards/{id}/trackers          # attach Linear/GitHub/Jira tracker
GET    /api/cards/{id}/attempts           # full RunAttempt history
GET    /api/attempts/{id}                 # detail w/ phase timings + tokens
```

SignalR new events: `RunAttemptPhaseChanged`, `RunAttemptStalled`, `RetryScheduled`, `OrchestratorTick`, `WorkflowReloaded`.

### Frontend additions

```
features/kanban/
  WorkflowEditor.tsx       # Monaco YAML+Markdown editor for WORKFLOW.md
  AttemptTimeline.tsx      # phase swimlane per RunAttempt
  OrchestratorPanel.tsx    # snapshot view: running, retries, token totals
  TrackerConfig.tsx        # Linear/GitHub/Jira hookup per board
```

### Updated slice order (revised)

1. **Pty.Net spike** — prove `claude --version` headed on Windows.
2. **`IPtyAgentRunner` + `IAgentProtocolAdapter` (RawPty first)** — generic agent abstraction so CodexAppServer / ClaudeJsonStream slot in later.
3. **`Worktree` + `WorktreeManager`** with Symphony safety invariants (sanitize, root-confined).
4. **Domain entities** — Board, Card, AgentSession, RunAttempt, Worktree, WorkflowDefinition. EF migration (stop server first per AGENTS.md).
5. **`AgentSession` lifecycle + RunAttempt phase machine + SignalR streaming**. Reuse `AgentTextDelta`.
6. **`IWorkspaceHookRunner`** — after_create/before_run/after_run/before_remove with timeouts.
7. **`OrchestratorService`** — tick loop, eligibility, dispatch, reconcile, retry. Internal tracker only at first.
8. **xterm.js terminal + Board UI + drag-drop spawn** — board-driven mode usable.
9. **WorkflowDefinition loader + hot reload** — Monaco editor in UI.
10. **External tracker adapters** — Linear, GitHub Issues, Jira (use existing Jira MCP for ref).
11. **amux-style channels + atomic claim + watchdog**.
12. **DiffReview + GitHub PR open**.
13. **Snapshot/observability API + ops dashboard**.

### Risks added by Symphony scope

7. **Tracker rate limits.** Linear GraphQL + GitHub REST both have caps. Coalesce poll → single fetch per tick; cache state within tick.
8. **Hot reload races.** Editing `WORKFLOW.md` mid-attempt — existing attempts use snapshot of definition at launch, not live. Persist `WorkflowDefinitionVersion` on each `RunAttempt`.
9. **Hooks = arbitrary shell.** Sandbox or document trust boundary. At minimum: timeout + stderr capture + non-zero abort for pre-hooks.
10. **No retry persistence on restart** (Symphony's choice). Antiphon already persists Workflows — decide if RunAttempt retry queue should persist (probably yes, since Antiphon has DB).

---

## References

- vibe-kanban: https://github.com/BloopAI/vibe-kanban
- amux: https://github.com/mixpeek/amux
- vs-pty.net: https://github.com/microsoft/vs-pty.net
- Symphony (OpenAI): https://github.com/openai/symphony/blob/main/SPEC.md
