# Agent Queues Design

## Summary

Antiphon will add a top-level Agents area where users define persistent agents, assign cards to agent queues, and monitor each agent's current card, workflow state, terminal, and memory. Cards remain the work item model. The new Agents surface becomes the primary operating view, while boards remain useful as a secondary projection over card state.

Agents are durable runtime identities, not just launch templates. An agent has a working directory, details, default workflow, assignment policy, persistent session id, status, and memory configuration. Antiphon launches or resumes the same agent session across cards when the selected agent adapter supports durable sessions.

## Decisions

- Use the current board/card system as the foundation.
- Cards remain queue items and review/diff units.
- Add top-level agents with persistent resumable sessions.
- Each card gets its own workflow run and stage state.
- Agent default workflow is used unless a card overrides it.
- Reuse existing workflow template/YAML concepts first.
- Antiphon owns workflow state and gates; agents report/request progress.
- Human Review blocks the assigned agent.
- Assignment policy is per-agent and defaults to auto-pick.
- Cards are manually assigned to agent queues in the first version.
- Cards can be handed off between agents through an explicit handoff flow.
- Persistent memory content lives in scoped files under the agent working directory.

## Architecture

The design evolves existing boards and cards rather than replacing them. A new `Agent` entity becomes the long-lived runtime identity. It owns the working directory, persistent session id, default workflow, assignment policy, status, and memory configuration.

A `Card` remains the work item. Cards can be assigned to an agent queue, ordered in that queue, and moved through board columns as today. The key change is that a card may have an active workflow run that records the actual per-card workflow state.

An `AgentSession` remains the terminal/process record, but persistent agent sessions can span multiple cards. Run attempts, audit entries, token usage, and worktree state remain tied to specific cards so history and cost remain attributable.

Boards remain part of the product, but the Agents page becomes the main control surface for agentic work. Board columns become a projection of card/workflow state for agent-driven cards, while existing card movement behavior remains available for manually managed cards.

## Domain Model

### Agent

An agent includes:

- `Id`
- `Name`
- `Slug`
- `WorkingDirectory`
- `Details`
- `DefaultWorkflowDefinitionId`
- `AssignmentPolicy`
- `Status`
- `PersistentSessionId`
- `CurrentCardId`
- memory scope configuration
- timestamps

Initial assignment policies:

- `AutoPick`: take the next queued card when unblocked.
- `ManualConfirm`: show Ready and wait for a human to pick/confirm the next card.
- `Paused`: do not pick new cards.

Default policy is `AutoPick`.

### Card Queue Metadata

Cards gain enough metadata to act as agent queue items:

- assigned agent id
- queue position
- active workflow run id
- handoff status or pending handoff target

A separate queue item table is not required for the first version unless queue history or multi-queue membership becomes necessary.

### Card Workflow Run

Each assigned card has a workflow run:

- `Id`
- `CardId`
- `AgentId`
- workflow definition snapshot
- current stage id/name
- status
- started/completed timestamps
- failure or blocked reason

The workflow definition must be snapshotted per card run so later edits to agent defaults or templates do not rewrite active card behavior.

### Card Workflow Stage

Each stage tracks:

- stage order and name
- executor type/model metadata from the workflow definition
- status
- prompt/system prompt snapshot
- gate requirements
- result summary
- attempt metadata
- timestamps

## Workflow Behavior

When a card is assigned to an agent queue, Antiphon determines its workflow from the card override if present, otherwise from the agent default workflow. The workflow is snapshotted into a card workflow run.

Antiphon remains authoritative for workflow state. It sends stage prompts to the persistent agent session, records stage transitions, and pauses at gates. The agent can report progress or request a transition, but Antiphon validates and persists the state.

Human Review is a blocking gate. When a card reaches Human Review, the agent becomes `WaitingForHumanReview` and does not take another card. A human can approve, reject, redirect, or hand off the card. The agent resumes from the same persistent context after the gate action.

When the card completes, Antiphon runs the configured compaction prompt. Only after compaction succeeds, is skipped by a human, or is explicitly retried to completion does the agent become eligible for the next queue item.

## Persistent Sessions

Agent sessions should be launched with deterministic ids, for example:

```text
agent-{agentSlug}-{yyyyMMdd-HHmm-ssfff}
```

The exact adapter arguments are agent-specific. For Claude Code, the launch/resume integration starts with a session id and resumes later with that id through the adapter capability that maps to Claude's durable-session support.

Antiphon stores the current persistent session id on the agent. Resume/start controls operate on the agent, while card run attempts remain per-card. The terminal UI for an agent should show the persistent session and clearly indicate whether it is live, resumable, stopped, failed, or blocked.

## Memory Files

Memory content lives in the agent working directory, split by scope. The first version supports:

```text
.antiphon/agents/{agentSlug}/agent.md
.antiphon/memory/repo.md
.antiphon/memory/workflows/{workflowSlug}.md
.antiphon/memory/environments/{environment}.md
```

The agent configuration stores the memory paths and compaction prompt templates. Antiphon does not treat the database as the source of truth for memory content. It stores metadata such as last compaction time, last failure, and configured scopes.

Compaction runs after a card completes and before the agent takes the next card. The default prompt should ask the agent to preserve generic, reusable facts and discard card-specific noise unless it is useful for future work.

## Handoff

Cards can move between agents after work has started. Handoff is explicit:

1. Human requests handoff to a target agent.
2. Antiphon asks the source agent to produce handoff context and compact relevant memory.
3. Antiphon records the handoff on the card workflow run.
4. The target agent receives the card with the handoff context, workflow state, scoped memory files, and current gate/stage information.

If handoff fails, the card remains assigned to the source agent until a human retries, skips handoff context, or force-reassigns.

## Agents UI

Add a top-level `Agents` nav item. The page starts with an agent roster. Each tile shows name, working directory, current status, current card, queue length, assignment policy, and session state.

Agent statuses:

- `Working`: actively running a card/stage.
- `WaitingForHumanReview`: blocked on a card gate.
- `Ready`: card finished and compaction is complete; can take another queue item.
- `Idle`: no active card and no queued work.
- `Stopped`: persistent session is not running but can be resumed.
- `Disconnected`: Antiphon cannot currently attach to the expected session.
- `Failed`: last agent/session/workflow action failed.

Selecting an agent opens its detail area:

- working directory and details
- persistent session id
- default workflow
- assignment policy
- current card and workflow stage
- queue with reorder controls
- terminal
- memory scopes and file status
- recent card run history
- start/resume/stop controls

Queue management is manual in the first version. Users assign cards to an agent and reorder the queue. If the assignment policy is `AutoPick`, the agent takes the next card when unblocked. If it is `ManualConfirm`, the UI highlights `Ready` and asks the user to select or confirm the next card. Human Review blocks the agent for all assignment policies.

## API Surface

Initial REST surface:

- `GET /api/agents`
- `GET /api/agents/{id}`
- `POST /api/agents`
- `PATCH /api/agents/{id}`
- `POST /api/agents/{id}/start`
- `POST /api/agents/{id}/resume`
- `POST /api/agents/{id}/stop`
- `POST /api/agents/{id}/queue`
- `PATCH /api/agents/{id}/queue`
- `DELETE /api/agents/{id}/queue/{cardId}`
- `POST /api/cards/{id}/workflow/approve`
- `POST /api/cards/{id}/workflow/reject`
- `POST /api/cards/{id}/handoff`
- `POST /api/cards/{id}/compact`

Endpoints follow the existing Minimal API pattern, use `HttpException` subclasses for errors, and publish SignalR events through `IEventBus`.

## SignalR And Query Invalidation

New events keep the Agents page and board projection fresh:

- `AgentChanged`
- `AgentQueueChanged`
- `CardWorkflowChanged`
- `AgentSessionChanged`
- `AgentMemoryChanged`

Frontend query keys mirror existing TanStack Query conventions, for example `['agents']`, `['agent', id]`, and `['agent', id, 'queue']`.

## Error Handling

Agent-level and card-level failures are distinct. A persistent agent can be failed, stopped, or disconnected while a card workflow run remains waiting, failed, or blocked.

Resume failure must not advance the card. Compaction failure must not silently move the agent to the next card. Handoff failure must keep the card assigned to the source agent unless a human force-reassigns it.

The UI should expose retry/skip/force controls for compaction and handoff failure states, with audit records for each human override.

## Testing Strategy

Testing should focus on integration and user workflows, with unit tests for state machines and queue selection.

Backend integration tests should cover:

- creating an agent with working directory and default workflow
- assigning cards to an agent queue
- queue ordering and auto-pick selection
- creating a per-card workflow run from the agent default workflow
- human review blocking the agent
- approve/reject behavior
- compaction success and failure
- scoped memory file writes
- persistent session start/resume/stop behavior
- handoff success and failure

Playwright E2E tests should cover:

- Agents nav and roster visibility
- creating/configuring an agent
- assigning a card to a queue
- agent status changing from idle to working
- Human Review blocking the agent
- approving review and seeing the agent continue
- completed card triggering compaction and next-card pickup
- stop/resume controls for a persistent session

Unit tests should cover:

- agent status derivation
- queue selection by assignment policy
- workflow run creation from defaults and overrides
- legal workflow/card/agent transitions

## Initial Implementation Slices

1. Add Agent domain model, API, and basic Agents page roster.
2. Add manual card assignment to agent queues.
3. Add card workflow run/state from agent default workflow.
4. Connect persistent agent sessions to agents and current cards.
5. Add auto-pick scheduling and Human Review blocking.
6. Add scoped memory file compaction.
7. Add handoff between agents.

This order keeps each slice useful while avoiding a parallel replacement for boards.
