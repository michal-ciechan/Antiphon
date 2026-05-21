# ADR 0001: Agent Queues Foundation Decisions

## Status

Accepted

## Date

2026-05-20

## Context

Antiphon's agent-queue work introduced several design decisions that were initially tracked in `TODO.md` as open questions. Those decisions are now settled and should be recorded outside the backlog so implementation can proceed without re-litigating the same points.

This ADR captures the first set of accepted decisions for the agent-queue model and its runtime boundaries.

## Decisions

### 1. `Card` remains a sibling work-item concept, not a wrapper over `Workflow`

`Card` will remain the board and queue work-item concept. A card may spawn or own workflow execution state, but it does not collapse into the existing `Workflow` entity.

This keeps the board and queue model explicit while still reusing workflow, stage, gate, and cost-ledger behavior.

### 2. Agent credentials start as shared user credentials

Agent processes will initially use the same machine-user credentials already available through locations such as `~/.claude`, `~/.codex`, and relevant environment variables.

Antiphon will document this trust boundary clearly. Per-agent secret storage is deferred until there is a stronger multi-user or hosted isolation requirement.

### 3. Hooks are trusted local code with guardrails

Hooks are treated as trusted local code in the first version rather than being sandboxed immediately.

Antiphon should enforce practical guardrails:

- timeout handling
- stdout and stderr capture
- non-zero exit handling
- clear documentation that hooks run with local user privileges

Heavier sandboxing, such as JobObject restrictions or path allowlists, is deferred until there is a concrete threat model that justifies the extra complexity.

### 4. Interactive PTY sessions fail hard if the PTY path is unavailable

Interactive PTY-backed agent sessions will fail fast if the intended PTY path is unavailable.

Antiphon will not silently fall back to plain `Process` plus redirected pipes for interactive sessions, because that would materially change terminal behavior and hide PTY-specific failures. If a non-PTY mode is needed later, it should be introduced as an explicit mode, not as an automatic fallback.

## Consequences

- Agent queue implementation can proceed without reopening these four design questions.
- `TODO.md` stays focused on active work instead of storing settled decisions.
- Future implementation and review work has a stable reference point for these boundaries.
- If later evidence invalidates one of these choices, the change should be made with a new ADR rather than silently editing this one.
