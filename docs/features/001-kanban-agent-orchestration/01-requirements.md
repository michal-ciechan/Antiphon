# Requirements — Kanban + Agent PTY Orchestration

> **Note:** Pre-`/pts-vibe-init` skeleton. Migrated out of `05-epics.md` §0
> so the epics doc stays an index.

---

## 1. Overview

Three modes — **tracker-driven** (Symphony), **board-driven** (vibe-kanban),
**chat-driven** (amux) — feed one shared `RunAttempt` + `AgentSession`
machinery. Cards spawn worktrees; agents run as PTY children via vs-pty.net;
output streams via existing SignalR `AgentTextDelta`. Reuses Antiphon's
existing Workflow / Stage / Gate / Artifact / CostLedger primitives where
possible (Card may wrap Workflow rather than duplicate).

13 epics, sliced so each delivers a usable increment. Spike → infra → domain
→ vertical slices → external integrations → ops surface.

---

## 2. Functional Requirements

- **FR-01** User can create boards (one per repo / workspace).
- **FR-02** User can create cards on a board with title, description, priority, labels.
- **FR-03** User can drag cards across columns (Backlog / In Progress / Review / Done — configurable).
- **FR-04** Moving a card to In Progress (or explicit `/spawn`) creates a git worktree on a fresh branch and starts an `AgentSession` with `cwd = worktree`.
- **FR-05** User picks `AgentKind` (ClaudeCode / Codex / Gemini / Cursor / Aider / Custom) from a registry when spawning.
- **FR-06** Live PTY output streams to xterm.js in browser via SignalR; late joiners get full ANSI replay via REST.
- **FR-07** User can send keystrokes and resize PTY from the browser.
- **FR-08** User can fork a session (clone history, new worktree).
- **FR-09** User can kill a session (SIGTERM → SIGKILL).
- **FR-10** Card moved to Review surfaces a diff view; inline comments route back to the agent as channel messages.
- **FR-11** Card moved to Done can open a GitHub PR from the worktree branch.
- **FR-12** Orchestrator polls a tracker (Linear / GitHub Issues / Jira / Internal) and auto-spawns sessions for eligible issues (Symphony mode).
- **FR-13** Orchestrator enforces global + per-column concurrency caps and exponential-backoff retry on failure.
- **FR-14** Each board has a `WORKFLOW.md` (YAML front matter + Markdown prompt) with hot-reload on edit; running attempts pin to the version captured at launch.
- **FR-15** Workspace hooks run `after_create` / `before_run` / `after_run` / `before_remove`; pre-hook failure aborts the attempt.
- **FR-16** Agents discover peers and `@mention` to delegate; messages inject into target session stdin (amux channels).
- **FR-17** Atomic claim — at most one active session per card.
- **FR-18** Watchdog detects "Press Enter / (Y/n)" prompts and auto-responds per registry rules.
- **FR-19** Snapshot API exposes orchestrator state: running sessions, retry queue, token totals, runtime seconds.
- **FR-20** Stale worktrees auto-prune after N days.

## 3. Non-Functional Requirements

- **NFR-01** Onion architecture preserved — Pty.Net / libgit2sharp confined to `Infrastructure/`.
- **NFR-02** Domain layer has zero external package dependencies.
- **NFR-03** Workspace path stays under configured root; directory names sanitized to `[A-Za-z0-9._-]+`.
- **NFR-04** PTY child cwd = workspace path only (never project root).
- **NFR-05** Per-session memory cap enforced via Windows `JobObject`.
- **NFR-06** ANSI output buffer: ring buffer in memory + disk spill under `C:\MavLog\Antiphon\sessions\`.
- **NFR-07** Tracker rate limits respected — single fetch per tick, in-tick cache.
- **NFR-08** Stalled session detected within `stall_timeout_ms` (default 5m) of last event.
- **NFR-09** Restart recovers running attempts via tracker repoll + filesystem worktree scan; retry queue persists in DB (Antiphon deviation from Symphony).
- **NFR-10** SignalR delta payload size capped; large outputs chunked.
- **NFR-11** All API routes follow `/api/...` kebab-case per `docs/project-context.md`.
- **NFR-12** All new entities migrate via EF Core code-first (`.\stop-server.ps1` first).
