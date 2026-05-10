# Antiphon Kanban + Agent PTY Orchestration — Plan

## What you're really building

Three sources merged into one design (vibe-kanban dropped — Symphony covers same ground via tracker-driven loop and we'll bolt the kanban UI directly onto Symphony's `Issue` model):

| Source | Borrow |
|--------|--------|
| **Symphony (OpenAI)** | tracker-driven autonomous loop, `WORKFLOW.md` (YAML+Markdown prompt), `Issue` + `RunAttempt` lifecycle, workspace + hooks, polling tick + reconcile, hot reload, snapshot API, worktree-per-issue, exp backoff retry |
| **amux** | parallel sessions, REST/channel coordination, atomic task claiming, watchdog auto-respond, conversation forking |
| **vs-pty.net** | spawn `claude`, `codex`, `gemini`, `cursor-agent` etc. as headed PTY children from .NET — capture full ANSI stream, send keystrokes, resize |

Antiphon already has Workflows / Stages / Gates / Artifacts / SignalR / state machine. Kanban = **second view over Symphony's `Issue` model**, plus new `AgentSession` + `RunAttempt` aggregates for live PTY processes and per-attempt lifecycle.

### Two operational modes — one shared machinery

1. **Tracker-driven** (Symphony default) — orchestrator polls tracker (Linear/GitHub/Jira/Internal); auto-spawns sessions for eligible issues. Headless overnight runs. Kanban board renders the same `Issue` rows grouped by state column. User can also drag a card → forces dispatch (manual override on the same loop).
2. **Chat-driven** (amux) — channels/@mentions; agents delegate to peers; conversation forking. Layered on top — uses same `RunAttempt` + `AgentSession` rows.

Both modes feed the same `RunAttempt` + `AgentSession` machinery. Differ only in trigger source. **No separate "board-driven mode"** — board is just a view onto tracker state, with manual dispatch as a UI affordance.

---

## Domain additions (Onion-clean)

```
Domain/Entities/
  Board.cs                # board per repo or per-workspace; owns WorkflowDefinition + tracker config
  Issue.cs                # Symphony Issue: id, identifier, title, state, priority, labels, blockers[], timestamps
                          # OwnerSessionId for atomic claim; rendered as kanban card grouped by state
  AgentSession.cs         # live PTY child; FK -> Issue; thread_id, turn_id, token usage
  RunAttempt.cs           # one execution of agent on issue; phase, attempt#, startedAt, endedAt, exitReason
  RetrySchedule.cs        # value object: attempt, nextAt, lastError, backoffMs (Symphony retry)
  Worktree.cs             # path, branch, base ref, status
  WorkflowDefinition.cs   # YAML config + Markdown prompt template; immutable per version
  TokenUsage.cs           # inputTokens, outputTokens, cachedTokens (hooks into existing CostLedger)

Domain/Enums/
  AgentKind.cs            # ClaudeCode, Codex, Gemini, Cursor, Aider, Custom
  SessionStatus.cs        # Starting, Running, AwaitingPrompt, Stopped, Crashed
  RunPhase.cs             # PreparingWorkspace, BuildingPrompt, LaunchingAgent,
                          # InitializingSession, StreamingTurn, Finishing,
                          # Succeeded, Failed, TimedOut, Stalled, Canceled
  TrackerKind.cs          # Linear, GitHubIssues, Jira, Internal

Domain/StateMachine/
  RunAttemptStateMachine.cs  # phase transitions per Symphony spec
```

**No `Card` / `Column` entity.** Kanban columns are just the tracker's state strings (`Todo`, `InProgress`, `Review`, `Done`) — `WorkflowDefinition.tracker.active_states` + `terminal_states` configure which appear. UI groups `Issue` rows by `state`. Drag = state mutation via tracker adapter (or internal DB write for `InternalTracker`).

Issue → 0..N AgentSessions (forking like amux). Issue → 0..N RunAttempts. Issue → 1 active Worktree. Worktree → 1 git branch.

---

## Application layer

```
Application/Interfaces/
  # PTY + agent surface
  IPtyAgentRunner.cs            # Start, WriteInput, Resize, Kill, OnOutput event
  IAgentProtocolAdapter.cs      # Codex app-server / Claude JSON-stream / RawPty share one seam
  IAgentRegistry.cs             # known CLI agents + launch args + auto-prompt rules

  # Worktree + workspace lifecycle
  IWorktreeManager.cs           # Create/Remove/List git worktrees, sanitised + root-confined
  IWorkspaceHookRunner.cs       # after_create, before_run, after_run, before_remove (Symphony hooks)

  # Workflow definition (Symphony WORKFLOW.md)
  IWorkflowDefinitionLoader.cs  # parse + hot reload; bad reload keeps last-good
  IPromptTemplateRenderer.cs    # strict variable expansion; unknown var = render fail

  # Tracker + orchestration (Symphony loop)
  IIssueTracker.cs              # FetchCandidates, FetchByStates, FetchByIds
  IOrchestrator.cs              # poll tick, dispatch, reconcile, retry

  # Repositories
  IBoardRepository.cs / IIssueRepository.cs / ISessionRepository.cs
  IRunAttemptRepository.cs / IWorktreeRepository.cs

Application/Services/
  BoardService                  # board CRUD; resolves columns from WorkflowDefinition.tracker.*_states
  IssueService                  # issue CRUD (Internal tracker); state mutation via IIssueTracker for external
  AgentSessionService           # session lifecycle, channel routing, watchdog hooks
  AgentCoordinator              # amux peer discovery + atomic claim
  RunAttemptService             # phase transitions, hook invocation, telemetry, version pinning
  OrchestratorService           # Symphony tick loop, eligibility, dispatch, reconcile, retry scheduling
  WorkflowDefinitionLoaderImpl  # YAML+Markdown parse, hot reload
```

`IPtyAgentRunner` keeps Pty.Net dep out of Domain/Application. Implementation in Infra.

---

## Infrastructure

```
Infrastructure/Agents/
  PtyAgentRunner.cs           # vs-pty.net wrapper; process tree, ANSI buffer ring, JobObject for memory cap
  RawPtyProtocolAdapter.cs    # default protocol — stream raw stdout as text deltas
  ClaudeJsonStreamAdapter.cs  # parse Claude Code JSON-line protocol → typed events + token usage
  CodexAppServerAdapter.cs    # Codex app-server protocol; thread_id/turn_id extraction
  AgentRegistry.cs            # JSON config: name, exe, args template, auto-prompt rules
  AnsiStripParser.cs          # mirror amux: parse without hooks
  WatchdogHostedService.cs    # auto-respond to stuck prompts (Press Enter, (Y/n))
  AgentChannelHub.cs          # SignalR sub-hub for inter-session @mentions

Infrastructure/Git/
  WorktreeManager.cs          # `git worktree` shell-out; sanitise, root-confine
  WorktreeJanitorHostedService.cs # daily prune of Stale worktrees

Infrastructure/Orchestration/
  OrchestratorTickHostedService.cs # Symphony poll loop (default 30s)
  TrackerCache.cs                  # single-fetch-per-tick coalescing (NFR rate limits)
  RetryScheduler.cs                # exp backoff: continuation 1s; failure 10s × 2^(n-1) capped at 5m
  WorkflowFileWatcher.cs           # IFileSystemWatcher → reparse + publish

Infrastructure/Trackers/
  LinearTracker.cs            # GraphQL; blockers from inverse `blocks`
  GitHubIssuesTracker.cs      # REST; priority from label convention
  JiraTracker.cs              # JQL; reuse Jira MCP for inspiration
  InternalTracker.cs          # board-only mode (no external tracker)

Infrastructure/Workspace/
  WorkspaceHookRunner.cs      # shell hooks with timeouts; pre-hook abort, post-hook log-only

Infrastructure/Hosting/
  JobObjectProcessGuard.cs    # Windows JobObject wrap — per-session memory cap, kill-on-host-exit
```

vs-pty.net notes:
- Use `Pty.Net.PtyProvider.SpawnAsync` w/ env, cwd = worktree path
- Read `.ReaderStream` async, push deltas over SignalR `AgentTextDelta` (already exists in Antiphon)
- Track exit via `.ProcessExited`; auto-restart per policy
- Resize on client viewport change via `.Resize(cols, rows)`
- Wrap PID in `JobObject` for memory cap + kill-on-host-exit (NFR)

---

## API (Minimal APIs, follow `/api/...` kebab convention)

```
# Board + issues
GET    /api/boards
POST   /api/boards
GET    /api/boards/{id}/issues                # all issues (rendered as kanban grouped by state)
GET    /api/boards/{id}/workflow              # current WORKFLOW.md content
PUT    /api/boards/{id}/workflow              # edit + hot-reload
POST   /api/boards/{id}/trackers              # attach Linear/GitHub/Jira tracker
POST   /api/issues                            # create issue (Internal tracker only)
PATCH  /api/issues/{id}                       # mutate state (drag column) / fields
POST   /api/issues/{id}/dispatch              # force orchestrator dispatch (manual override)
POST   /api/issues/{id}/spawn                 # body: { agentKind, prompt } -> direct spawn (skip dispatch loop)
DELETE /api/issues/{id}                       # Internal tracker only
POST   /api/issues/{id}/pr                    # open GitHub PR from worktree branch

# Run attempts (Symphony lineage)
GET    /api/issues/{id}/attempts              # full RunAttempt history
GET    /api/attempts/{id}                     # detail w/ phase timings + tokens

# Sessions (live PTY)
GET    /api/sessions
GET    /api/sessions/{id}/buffer              # full ANSI replay for late joiners
POST   /api/sessions/{id}/input               # body: { keys }
POST   /api/sessions/{id}/resize              # body: { cols, rows }
DELETE /api/sessions/{id}                     # SIGTERM then SIGKILL
POST   /api/sessions/{id}/fork                # clone history -> new session

# Orchestrator (Symphony snapshot + control)
GET    /api/orchestrator/state                # snapshot: running, retry queue, aggregate tokens, runtime seconds
POST   /api/orchestrator/pause
POST   /api/orchestrator/resume

# Agent registry
GET    /api/agents                            # registry list for UI picker
```

SignalR new events:
- Board / issue: `IssueStateChanged`, `IssueUpdated`, `BoardUpdated`
- Session: `SessionStarted`, `SessionOutput` (existing `AgentTextDelta`), `SessionExited`
- RunAttempt: `RunAttemptPhaseChanged`, `RunAttemptStalled`
- Orchestrator: `RetryScheduled`, `OrchestratorTick`, `WorkflowReloaded`
- Coordination: `AgentMentioned`

---

## Frontend (`client/src/features/kanban/`)

Mantine + dnd-kit for board, xterm.js for live terminal panes, Monaco for workflow editor.

```
features/kanban/
  BoardPage.tsx              # column grid (columns = tracker states), drag/drop
  IssueModal.tsx             # issue detail + spawn / dispatch agent
  SessionTerminal.tsx        # xterm.js, attach SignalR stream + REST replay
  SessionTabs.tsx            # multi-session per issue
  DiffReview.tsx             # reuse artifact viewer; inline comments
  AgentPicker.tsx            # registry-driven dropdown
  WorkflowEditor.tsx         # Monaco YAML+Markdown editor for WORKFLOW.md
  AttemptTimeline.tsx        # phase swimlane per RunAttempt
  OrchestratorPanel.tsx      # snapshot view: running, retries, token totals
  TrackerConfig.tsx          # Linear/GitHub/Jira hookup per board
hooks/
  useSessionStream.ts        # subscribes to AgentTextDelta filtered by sessionId
  useBoard.ts                # TanStack Query
  useIssues.ts               # TanStack Query for board issues
  useOrchestratorState.ts    # polls /api/orchestrator/state every 5s
stores/
  useTerminalStore.ts        # ANSI ring buffer per session
```

xterm.js gets the REST `/buffer` for backlog, then SignalR deltas for live. Same pattern Antiphon already uses for streaming chat.

---

## WORKFLOW.md (Symphony) — per-board config + prompt

YAML front matter + Markdown prompt body, edited via Monaco, hot-reloaded by `WorkflowFileWatcher`. Running attempts pin to the version captured at launch.

```yaml
---
tracker:
  kind: linear            # linear | github_issues | jira | internal
  endpoint: https://api.linear.app/graphql
  api_key_env: LINEAR_API_KEY
  project: ANTIPHON
  active_states: [Todo, InProgress]
  terminal_states: [Done, Cancelled]

polling:
  interval_seconds: 30

workspace:
  root: D:\Antiphon\workspaces

hooks:
  after_create:  scripts/setup.ps1
  before_run:    scripts/pre.ps1
  after_run:     scripts/post.ps1
  before_remove: scripts/cleanup.ps1

agent:
  kind: ClaudeCode
  max_concurrent: 10
  max_concurrent_by_state:
    InProgress: 6
    Review: 2
  max_turns: 50
  retry_backoff_ms: 300000   # 5m cap
  read_timeout_ms: 5000
  turn_timeout_ms: 3600000   # 1h
  stall_timeout_ms: 300000   # 5m
---

You are an agent working on Antiphon issue {{ issue.identifier }}.
Title: {{ issue.title }}
Priority: {{ issue.priority }}

{{ issue.description }}

When done, commit and push to branch {{ workspace.branch }}.
```

Strict template — unknown variable fails render rather than silently substituting empty.

---

## Worktree lifecycle (Symphony invariants)

1. Issue ingested by tracker (or created via Internal tracker) → no worktree.
2. Issue eligible (active state, unclaimed, slot free) → orchestrator dispatch (or user manually `POST /api/issues/{id}/dispatch` from board) → `git worktree add workspaces/issue-{sanitised-identifier} -b feat/issue-{identifier}`. Path confined under configured root.
3. `after_create` hook runs (failure aborts).
4. `before_run` hook runs (failure aborts attempt).
5. `RunAttempt` created in `PreparingWorkspace`; transitions through `BuildingPrompt → LaunchingAgent → InitializingSession → StreamingTurn → Finishing`.
6. AgentSession spawned with `cwd = worktree path`, wrapped in JobObject.
7. `after_run` hook runs (failure logged-only).
8. Issue moved to **Review** state (by agent or user drag) → diff view, inline comments routed back as channel messages → injected into agent stdin.
9. Issue moved to terminal state (e.g. **Done**) → PR opened or worktree pruned. `before_remove` hook fires.

Auto-cleanup: `WorktreeJanitorHostedService` daily; `Worktree.Status = Stale` after N days → removed.

---

## Orchestrator tick loop (Symphony)

Every `polling.interval_seconds` (default 30):

1. **Reconcile running** — refresh state for in-flight attempts; cancel stalled (no events for `stall_timeout_ms`); refresh tracker state for cards owned by sessions.
2. **Validate config** — workflow definition still parses; bad config keeps last-good.
3. **Fetch candidates** — `IIssueTracker.FetchCandidates()`; coalesce via `TrackerCache` (single fetch per tick).
4. **Sort** — priority → creation → identifier.
5. **Dispatch** — for each eligible candidate while slots free:
   - Issue active state, not running, not claimed.
   - Global `max_concurrent` slot available.
   - Per-state `max_concurrent_by_state` slot available.
   - If state == Todo: all blockers terminal.
   - Optimistic concurrency claim (`Issue.OwnerSessionId` + `RowVersion`). Loser retries next tick.
6. **Notify observers** — emit `OrchestratorTick` SignalR event with snapshot deltas.

Retry: continuation 1s after clean exit; failure `10s × 2^(attempt-1)` capped at 5m. **Antiphon deviation from Symphony — retry queue persists in DB so restart resumes** (Symphony repolls fresh).

Pause / resume via `/api/orchestrator/pause`. Tick still runs but skips dispatch.

---

## amux-style coordination

- **Atomic claim**: `Issue.OwnerSessionId` w/ optimistic concurrency token (`RowVersion`). Two agents can't grab same issue. Used by both trigger modes (orchestrator dispatch, channel delegate).
- **Channels**: SignalR group per `issue:{id}`; agents `@mention` peers via output filter that triggers `AgentChannelHub.SendAsync(targetSessionId, msg)` → injected into target PTY stdin.
- **Shared memory**: dedicated `BoardNotes` table, scoped per board, queried via `/api/boards/{id}/notes`. Agents read/write via tool/MCP.
- **Conversation forking**: `POST /api/sessions/{id}/fork` → new worktree from same base, history cloned; new `AgentSession` row.
- **Watchdog**: `WatchdogHostedService` polls each session, detects "Press Enter to continue" / "(Y/n)" patterns from registry, auto-responds with cooldown to prevent loops.

---

## Risks / decisions to make first

1. **Process model on Windows.** vs-pty.net uses winpty/conhost — fine for Windows, but Antiphon Aspire host launching dozens of PTYs needs resource ceiling + per-session memory cap. Cgroups not native on Windows; lean on `JobObject`.
2. **Worktree race.** Multiple sessions per issue on same worktree = git index corruption. Enforce: 1 active session per worktree; forks get new worktree from same base.
3. **Auth/secrets per agent.** Each CLI agent reads `~/.claude`, `~/.codex`, env vars. Decide: shared user creds vs per-agent secret store.
4. **Buffer storage.** Full ANSI buffer per session can grow huge. Ring buffer w/ disk spill (matches existing `C:\MavLog\Antiphon\` log convention).
5. **Already have Workflows/Stages.** Decide: Issue == thin wrapper over Workflow, or sibling concept. Recommend **Issue spawns Workflow** so existing gate/stage/cost ledger reused for free.
6. **Tracker rate limits.** Linear GraphQL + GitHub REST both have caps. Coalesce poll → single fetch per tick; cache state within tick.
7. **Hot reload races.** Editing `WORKFLOW.md` mid-attempt — existing attempts use snapshot of definition at launch, not live. Persist `WorkflowDefinitionVersion` on each `RunAttempt`.
8. **Hooks = arbitrary shell.** Sandbox or document trust boundary. At minimum: timeout + stderr capture + non-zero abort for pre-hooks.
9. **Retry persistence on restart.** Symphony chooses tracker-driven recovery (no retry queue persistence). Antiphon already has DB and persists Workflows — recommend persist `RetrySchedule` so in-flight retries survive a restart.
10. **Drag-to-mutate-state on external trackers.** When user drags a Linear/GitHub issue between columns, the state mutation has to round-trip via the tracker API and may fail (perms, transition rules). UI must reconcile to tracker truth, not optimistically commit.

---

## Recommended slice order

1. **Pty.Net spike** — prove `claude --version` headed on Windows. JobObject memory-cap proof.
2. **`IPtyAgentRunner` + `IAgentProtocolAdapter` (RawPty first)** — generic agent abstraction so CodexAppServer / ClaudeJsonStream slot in later.
3. **`Worktree` + `WorktreeManager`** with Symphony safety invariants (sanitize, root-confined).
4. **Domain entities + EF migration** — Board, Issue, AgentSession, RunAttempt, Worktree, WorkflowDefinition. Stop server first per AGENTS.md.
5. **`AgentSession` lifecycle + RunAttempt phase machine + SignalR streaming**. Reuse `AgentTextDelta`. Stall detector.
6. **`IWorkspaceHookRunner`** — after_create / before_run / after_run / before_remove with timeouts.
7. **`OrchestratorService`** — tick loop, eligibility, dispatch, reconcile, retry. `InternalTracker` only at first.
8. **xterm.js terminal + kanban Board UI** — columns from `WorkflowDefinition` states, drag = state mutation, manual `dispatch` button per issue. End-to-end usable on Internal tracker.
9. **`WorkflowDefinitionLoader` + hot reload** — Monaco editor in UI.
10. **External tracker adapters** — Linear, GitHub Issues, Jira (use existing Jira MCP for ref). Drag → tracker state-transition API.
11. **amux channels + atomic claim + watchdog**.
12. **DiffReview + GitHub PR open**.
13. **Snapshot/observability API + ops dashboard**.

---

## References

- Symphony (OpenAI): https://github.com/openai/symphony/blob/main/SPEC.md
- amux: https://github.com/mixpeek/amux
- vs-pty.net: https://github.com/microsoft/vs-pty.net
