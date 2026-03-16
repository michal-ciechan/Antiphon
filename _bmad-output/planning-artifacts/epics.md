---
stepsCompleted: [1, 2, 3, 4]
status: 'complete'
completedAt: '2026-03-16'
totalEpics: 8
totalStories: 43
frsCovered: 71
nfrsCovered: 24
uxDrsCovered: 30
inputDocuments:
  - _bmad-output/planning-artifacts/prd.md
  - _bmad-output/planning-artifacts/architecture.md
  - _bmad-output/planning-artifacts/ux-design-specification.md
  - _bmad-output/planning-artifacts/ux-journeys-draft.md
---

# Antiphon - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for Antiphon, decomposing the requirements from the PRD, UX Design, and Architecture into implementable stories.

## Requirements Inventory

### Functional Requirements

FR1: User can create a new workflow by selecting a YAML workflow template and pointing it at a git repository
FR2: User can provide initial context for a workflow (free-text description, pasted ticket details, or other input)
FR3: User can view all active workflows with their current stage and progress status
FR4: User can view the full stage progression of a workflow as a visual progress indicator
FR5: User can pause, resume, or abandon a workflow
FR6: System executes workflow stages sequentially according to the YAML definition
FR7: System can load and execute structured YAML workflow definitions
FR8: Admin can add new YAML workflow templates to the system
FR9: System ships with bundled BMAD workflow templates (full and quick variants)
FR10: Workflow definitions specify stages, stage ordering, executor type (AI agent), model routing, and gate configuration per stage
FR11: System can execute an AI agent at each workflow stage using the configured LLM model
FR12: AI agent receives upstream artifacts, project constitution, and stage-specific instructions as context
FR13: AI agent can read, write, and edit files in the project workspace using built-in tools
FR14: AI agent can execute shell commands in the project workspace
FR15: AI agent can search files by pattern (glob) and search file contents (grep)
FR16: AI agent can perform git operations (clone, checkout, branch, commit, diff, push, tag)
FR17: User can view AI agent output in real-time as it streams
FR18: User can view a live activity status line showing current tool call, cumulative token count, tool call count, and elapsed time (debounced at 500ms)
FR19: System can route different stages to different LLM models (e.g., Opus for architecture, Sonnet for implementation)
FR20: Admin can configure available LLM providers and API keys (Anthropic, OpenAI, Ollama)
FR21: System pauses workflow execution at configured gate points and waits for user action
FR22: User can approve a gate to advance the workflow to the next stage
FR23: User can reject a gate with free-text feedback that is injected into the next agent invocation
FR24: User can go back to a previous stage from the current gate
FR25: When user goes back to a previous stage, system identifies downstream stages affected by the change
FR26: For each affected downstream stage, user can choose to update based on diff, regenerate from scratch, or keep as-is
FR27: User can provide free-text prompts to the agent at any gate to request modifications to the current artifact
FR28: System creates a namespaced workflow branch (antiphon/workflow-{id}/master) when a workflow starts
FR29: System creates ephemeral stage branches (antiphon/workflow-{id}/stage-{name}) for each stage execution
FR30: Agent commits changes to the stage branch at each gate point, with [antiphon] trailer to identify system commits
FR31: System tags stage commits with versioned tags (antiphon/workflow-{id}/{stage}-v{version}) before merging
FR32: System merges stage branches into the workflow master branch on gate approval
FR33: System stores artifacts in a dedicated directory (_antiphon/artifacts/workflow-{id}/) in the repo
FR34: System can compute path-filtered git diffs between stage tags for cascade update context
FR35: User can view artifact content (rendered markdown) in the dashboard
FR36: User can view diff between artifact versions
FR37: User can trigger re-execution of the current stage with modified feedback
FR38: When a stage artifact is updated, system detects upstream/downstream stages that may be affected
FR39: System presents affected stages with options: update based on diff, regenerate, or keep as-is
FR40: System uses git diff between version tags to provide context for AI-driven artifact updates
FR41: System preserves all artifact versions (no destructive overwrites)
FR42: Audit trail captures the full correction history: original, feedback, diff, and updated versions
FR43: Admin can create a project by pointing at a git repository URL
FR44: Admin can configure model routing per stage (which LLM model for which stage)
FR45: System loads project constitution (project-context.md file or folder) from the repo and injects it into agent system prompts
FR46: Admin can configure feature flags to enable/disable optional integrations (GitHub, notifications)
FR47: System records token count (in/out) and approximate USD cost for every LLM call
FR48: System records full audit content (prompts, responses, tool call inputs/outputs) for every agent execution
FR49: Cost ledger records are kept indefinitely; full audit content is stored separately and archivable
FR50: System logs client IP address on every request
FR51: Stage execution audit records reference git tags for traceability
FR52: User can view audit history for any workflow or stage execution
FR53: "Go back to stage" and "update based on diff" events are recorded as first-class audit events
FR54: User can view a dashboard listing all workflows with status, current stage, and progress indicators
FR55: User can view a workflow detail page showing stage progression, current artifact, and gate controls
FR56: Dashboard and all pages update in real-time via SignalR when backend state changes (no manual refresh)
FR57: User can view rendered markdown artifacts with version history
FR58: User can view the activity status line during agent execution (current action, tokens, tool calls, elapsed time)
FR59: System can create a GitHub PR from a stage branch to the workflow master branch
FR60: System can create a GitHub PR from the workflow master branch to main
FR61: System can monitor a GitHub PR for new comments, review feedback, and build status
FR62: When a GitHub PR receives comments or review feedback, system can feed that feedback to the AI agent for response or artifact update
FR63: System can push commits to stage branches (agent-generated fixes in response to PR feedback)
FR64: GitHub integration is feature-flagged and can be disabled per environment
FR65: System polls workflow branches for external commits via git fetch at configurable interval (default 30s)
FR66: System distinguishes Antiphon commits (marked with [antiphon] trailer) from external commits to prevent cascade loops
FR67: When external commits are detected, system automatically pulls and updates local state
FR68: If external changes touch files in _antiphon/artifacts/, system triggers path-based cascade detection to identify affected downstream stages
FR69: For affected downstream stages, system automatically triggers the cascade update flow (update based on diff)
FR70: Code-only external changes (outside _antiphon/artifacts/) update local state without triggering cascade
FR71: Audit trail records all external change events with commit details, author, diff, and any triggered cascades

### NonFunctional Requirements

NFR1: Dashboard page load completes within 2 seconds
NFR2: Agent text streaming latency is under 500ms from LLM response to UI render
NFR3: Agent activity status line updates debounced at 500ms server-side
NFR4: SignalR state change pushes delivered to connected clients within 1 second
NFR5: Git operations (branch, commit, tag, diff) complete within 5 seconds for repositories under 1GB
NFR6: Workflow YAML definitions parse and validate within 1 second
NFR7: LLM API keys are stored in server-side configuration only. Never exposed to frontend, agent context, or audit logs.
NFR8: Agent file tools are scoped to the project's git worktree directory. Path traversal attempts are blocked.
NFR9: Agent bash tool runs with working directory set to the scoped workspace. Not truly sandboxed for MVP.
NFR10: Git credentials are server-side only. Agents access repos through scoped git tools, never with direct credentials.
NFR11: Client IP is logged on every API request for audit trail
NFR12: Agent Framework checkpoints persist after every tool call completion. On crash recovery, agent resumes from last checkpoint.
NFR13: If agent execution fails, stage is marked as failed with full error details. User can retry from last checkpoint or last gate.
NFR14: Git tags and branch state are the source of truth for artifacts. Database is source of truth for orchestration state.
NFR15: PostgreSQL database uses standard transaction isolation. No concurrent writes to the same workflow stage.
NFR16: Every LLM call is recorded with model, tokens in/out, cost estimate, and duration
NFR17: Every tool invocation is recorded with tool name, inputs, outputs, and duration
NFR18: All audit data is queryable by workflow, stage, time range, and cost
NFR19: Structured logging with correlation IDs: every log line carries workflowId, stageId, and executionId
NFR20: OpenTelemetry traces emitted for all LLM calls via Microsoft.Extensions.AI middleware
NFR21: Health check endpoint (/health) reports status of: database, LLM providers, git repos, disk space
NFR22: Cost ledger records retained indefinitely
NFR23: Full audit content retained for configurable period (default 90 days)
NFR24: After retention period, full audit content eligible for deletion via admin API

### Additional Requirements

From Architecture document:

- AR1: Project scaffold uses manual composition: `dotnet new webapi` + `npm create vite@latest --template react-ts`. First implementation story.
- AR2: Monorepo structure: `/server` (ASP.NET Core) + `/client` (Vite React). Single `dotnet publish` produces combined binary.
- AR3: Database seeding: default admin user, bundled BMAD workflow templates, default model routing config. Runs after migrations, idempotent.
- AR4: HttpException hierarchy: NotFoundException, ConflictException, ValidationException, ForbiddenException. Global exception middleware maps to RFC 9457 Problem Details.
- AR5: IEventBus abstraction over SignalR. Services push events through interface, never hub directly.
- AR6: IStageExecutor interface for swapping AgentExecutor (real) and MockExecutor (testing).
- AR7: ToolRegistry registers and discovers built-in agent tools. Extensible for future MCP tools.
- AR8: CachingChatClient decorator for LLM call record/replay. Modes: Record, RecordIfMissing, Replay, PassThrough. File-based in .antiphon-cache/.
- AR9: IStageExecutor return type includes optional SuggestedActions field (v1.1 extensibility hook — UI ignores in MVP).
- AR10: WorkflowStateMachine exposes GetAvailableTransitions(state). API returns available actions with workflow state. Frontend renders buttons from list.
- AR11: AppDbContext lives in Infrastructure, injected into Application services via DI. Standard Onion Architecture.
- AR12: project-context.md created in scaffold story. Extracted from architecture enforcement guidelines.
- AR13: Serilog with AsyncLocal correlation enrichers (workflowId, stageId, executionId).
- AR14: Playwright (.NET) + WebApplicationFactory for E2E tests. AntiphonAppFixture with UsePrebuiltFrontend flag.
- AR15: Frontend test infrastructure: Vitest setup, MSW mocks, renderWithProviders helper.
- AR16: Docker Compose: production (Antiphon + PostgreSQL), dev (PostgreSQL only).
- AR17: IOptions<TSettings> with typed settings classes for all configuration. Never raw IConfiguration.
- AR18: EF Core migrations via CLI only (dotnet ef migrations add). Auto-apply via context.Database.Migrate() on startup.

### UX Design Requirements

UX-DR1: Dark theme as default using Mantine dark theme tokens. Status colors: green (success), orange (pending), red (error), blue (active).
UX-DR2: Dashboard as responsive card grid with WorkflowCard components. Each card: title, status badge, MiniPipeline, current stage, cost, last updated, border-left color for status.
UX-DR3: Dashboard filter bar with search, status filter, owner filter, project filter. Personalized "Pending Review" count.
UX-DR4: Dashboard empty state with "Create your first workflow" call-to-action using Mantine empty state pattern.
UX-DR5: New Workflow dialog (not page): template selection list, project/repo selector, free-text initial context field. Immediate creation — no processing screen.
UX-DR6: WorkflowDetailPage with stateful two-mode design: Conversation Mode (agent executing) and Gate Mode (at checkpoint). Landing mode determined by current workflow state.
UX-DR7: Conversation Mode: main area shows ConversationTimeline with streaming agent output, tool call visibility, activity status line. Right panel (360px fixed) with tabs.
UX-DR8: Gate Mode: main area shows primary artifact rendered at constrained width (~900px). ArtifactContextHint above artifact. Right panel defaults to Outputs tab.
UX-DR9: StagePipeline component: full horizontal stage progression bar on WorkflowDetailPage. Click completed stage to view its artifact. Stage states: Done (green), Active (blue animated), Pending (gray), Failed (red).
UX-DR10: ConversationTimeline: single chronological timeline across entire workflow. Collapsible StageMarker dividers. Message types: AgentMessage, UserPromptMessage, UserCommentMessage, SystemEvent, ToolCallBlock, InlineTransitionCard.
UX-DR11: StageMarker: collapsible divider showing stage name, version, message count, timestamp, gate decision. Current stage expanded, previous collapsed.
UX-DR12: ActionBar: fixed bottom bar. Left: gate action buttons (Approve green, Reject yellow, Go Back gray with icon). Right: text input + two distinct send buttons (blue "Send to Agent", gray "Add Comment"). Stable across mode transitions.
UX-DR13: ContextPanel: right-side panel (360px fixed) with 5 tabs always visible: Outputs, Stage Info, Conversation, Diff, Audit. Per-tab scroll preservation.
UX-DR14: Outputs tab: primary artifact pinned with "Primary" badge. Version history per artifact. Click loads artifact in main area.
UX-DR15: InlineTransitionCard: appears at end of conversation stream when stage completes. Click transitions to Gate Mode.
UX-DR16: CascadeDecisionCard: inline UI in main area for course correction (Go Back). Shows affected stages with reason + three radio options (Update from diff / Regenerate / Keep as-is). "Update from diff" pre-selected as default.
UX-DR17: ArtifactRenderer: react-markdown + rehype-highlight + remark-gfm + Mermaid support (lazy-loaded). Width switching between constrained (~900px) and full-width for diffs.
UX-DR18: ArtifactDiffViewer: raw markdown source diff (not rendered HTML). Side-by-side or unified view. Full-width layout.
UX-DR19: ActivityStatusLine: pulsing dot, current action (tool name + target), tokens in/out, tool call count, elapsed time. Debounced 500ms. aria-live region.
UX-DR20: MiniPipeline: compact stage indicators inside WorkflowCard. Per-stage states: Done (green), Active (blue pulsing), Pending (gray), Failed (red). Adapts to stage count.
UX-DR21: Dual-mode prompt bar: "Send to Agent" triggers re-execution, "Add Comment" leaves note for human collaborators without triggering agent. Enter key → Send to Agent. No shortcut for Add Comment.
UX-DR22: Comment styling: human-to-human comments visually distinct from agent prompts in conversation timeline. Different background, icon, "Comment" label.
UX-DR23: Real-time updates via SignalR everywhere: dashboard cards, stage pipelines, conversation streams, activity status. No manual refresh. Motion for active states, color for completed states.
UX-DR24: Toast notifications only for events outside current focus (SignalR reconnection, background workflow status). No toasts for user-initiated actions. Max 3 visible, auto-dismiss 3-5s.
UX-DR25: Confirmation dialogs only for destructive/irreversible actions (abandon workflow, delete template, remove project). All other actions are direct.
UX-DR26: ErrorBoundary: granular per section (dashboard, workflow detail, context panel, conversation). Shows error + retry + "Go to Dashboard" fallback.
UX-DR27: SuspenseBoundary variants: Page, Panel, Inline, Card. 200ms invisible delay before showing skeleton. Skeletons match expected content layout.
UX-DR28: Two-level navigation: Dashboard (Level 1) → WorkflowDetailPage (Level 2). No Level 3. Navbar: logo left, nav links center, user avatar right. Settings as separate route with tabs.
UX-DR29: Keyboard accessibility: Blueprint/Mantine built-in keyboard support for all interactive components. Focus indicators visible. Motion respects prefers-reduced-motion.
UX-DR30: Settings page with tabs: Templates (YAML workflow template CRUD), LLM Providers (API key config, model routing), Projects (repo URL, constitution path, feature flags).

### FR Coverage Map

| FR | Epic | Description |
|----|------|-------------|
| FR1-2 | Epic 2 | Workflow creation |
| FR3-5 | Epic 2 | View/manage workflows (minimal list view; full dashboard in Epic 4) |
| FR6 | Epic 2 | Sequential stage execution |
| FR7-10 | Epic 1 | Workflow YAML definitions, templates, bundled BMAD |
| FR11-19 | Epic 3 | AI agent execution, tools, streaming, model routing |
| FR20 | Epic 1 | LLM provider configuration |
| FR21-23 | Epic 2 | Gate pause/approve/reject |
| FR24-26 | Epic 5 | Go back, cascade choices |
| FR27 | Epic 2 | Prompt agent at gate |
| FR28-33 | Epic 2 | Git branching, tagging, merging, artifact storage |
| FR34 | Epic 5 | Path-filtered git diffs (spike story in Epic 2 to de-risk) |
| FR35 | Epic 2 | View artifact content |
| FR36 | Epic 5 | View diff between versions |
| FR37-42 | Epic 5 | Course correction flows |
| FR43-46 | Epic 1 | Project config, constitution, feature flags |
| FR47-53 | Epic 6 | Audit and cost tracking |
| FR54 | Epic 4 | Dashboard card grid listing |
| FR55 | Epic 2 | Workflow detail page |
| FR56 | Epic 4 | Real-time dashboard updates |
| FR57 | Epic 2 | Rendered markdown with versions |
| FR58 | Epic 4 | Activity status during execution (dashboard-level) |
| FR59-64 | Epic 7 | GitHub PR integration |
| FR65-71 | Epic 8 | External change detection |

**All 71 FRs covered. No gaps.**

### NFR → Epic Assignment

NFRs are addressed as acceptance criteria within relevant epic stories:

| NFR | Epic | Implementation |
|-----|------|---------------|
| NFR1 (Dashboard <2s) | Epic 4 | AC on dashboard stories |
| NFR2 (Streaming <500ms) | Epic 3 | AC on agent streaming story |
| NFR3 (Status debounce 500ms) | Epic 3 | AC on ActivityStatusLine story |
| NFR4 (SignalR <1s push) | Epic 1 | AC on SignalR infrastructure story |
| NFR5 (Git ops <5s) | Epic 2 | AC on git operations stories |
| NFR6 (YAML parse <1s) | Epic 1 | AC on workflow definition story |
| NFR7 (API keys server-only) | Epic 1 | AC on LLM provider config story |
| NFR8 (Agent tool scoping) | Epic 3 | AC on agent tool stories |
| NFR9 (Bash workspace scoping) | Epic 3 | AC on BashTool story |
| NFR10 (Git creds server-only) | Epic 2 | AC on git service story |
| NFR11 (IP logging) | Epic 1 | AC on CurrentUserMiddleware story |
| NFR12 (Checkpoint/resume) | Epic 3 | AC on agent executor story |
| NFR13 (Failed stage recovery) | Epic 3 | AC on agent error handling story |
| NFR14 (Git source of truth) | Epic 2 | AC on git branching story |
| NFR15 (Transaction isolation) | Epic 1 | AC on database setup story |
| NFR16-17 (LLM/tool recording) | Epic 6 | AC on audit recording stories |
| NFR18 (Queryable audit) | Epic 6 | AC on audit query story |
| NFR19 (Correlation IDs) | Epic 1 | AC on Serilog/middleware story |
| NFR20 (OpenTelemetry) | Epic 1 | AC on Serilog setup story |
| NFR21 (Health endpoint) | Epic 1 | AC on health check story |
| NFR22-24 (Data retention) | Epic 6 | AC on cost ledger + audit archival stories |

### Test Coverage Traceability Matrix

This matrix tracks which tests verify which FRs/NFRs. Updated as each epic is completed.

| FR/NFR | Epic | Story | Test Type | Test Location | Status |
|--------|------|-------|-----------|---------------|--------|
| *(populated during story creation and implementation)* | | | | | |

**Testing Strategy:**
- E2E tests (Playwright) from Epic 1 onward — every epic adds browser-based tests covering its FRs
- Integration tests for every API endpoint → DB round-trip
- Unit tests for domain logic (state machine, value objects)
- Frontend component tests for feature-level interactions
- CachingChatClient in RecordIfMissing mode for local dev, Replay for CI
- Tests accumulate — by Epic 4 completion, E2E suite covers Epics 1-4 user flows

## Build Approach

**Each epic is built to completion before moving to the next.** No partial epics, no deferred work within an epic. When an epic is done, all its FRs work end-to-end in the browser with passing E2E tests.

**MVP Line: Epics 1-4.** These deliver the minimum viable product: platform deployed and configured, full workflow loop with real AI agents, and a live-updating dashboard.

**Post-MVP: Epics 5-8.** Course correction, audit, GitHub integration, and external change detection. Important but deferrable.

## Epic Dependency Graph

```
Epic 1 (Foundation) → Epic 2 (Core Loop) → Epic 3 (AI Agents) → Epic 5 (Course Correction)
                                ↘ Epic 4 (Dashboard)
                                ↘ Epic 6 (Audit)
                                           Epic 3 → Epic 7 (GitHub)
                                           Epic 3 → Epic 8 (External Changes)
```

- Epics 1 → 2 → 3: strictly sequential
- Epic 4 (Dashboard) can start after Epic 2
- Epic 6 (Audit) can start after Epic 2
- Epics 7 and 8 are independent, both after Epic 3
- Epic 5 after Epic 3 (needs real agent re-execution)

## Epic List

### Epic 1: Platform Foundation & Configuration
Admin deploys Antiphon, configures LLM providers and projects, and the platform is operational with all infrastructure in place for subsequent epics.

**FRs covered:** FR7-10, FR20, FR43-46
**ARs covered:** AR1-5, AR8, AR11-18
**UX-DRs covered:** UX-DR1, UX-DR26-27, UX-DR28-29, UX-DR30
**NFRs addressed:** NFR4, NFR6-7, NFR10-11, NFR15, NFR19-21

**Includes:**
- Project scaffold (monorepo, backend + frontend init)
- docs/project-context.md (extracted conventions)
- Database schema + EF Core migrations + seeding (admin user, BMAD templates, model routing)
- HttpException hierarchy + global exception middleware
- IEventBus + SignalR hub + client-side useSignalR + useSignalRInvalidation hooks
- CachingChatClient + Playwright E2E fixtures + frontend test infrastructure (MSW, Vitest setup)
- ICurrentUser middleware + IP logging
- Serilog + correlation ID middleware + OpenTelemetry
- Health check endpoint
- Docker Compose (dev + production)
- Settings page UI with tabs (Templates, LLM Providers, Projects)
- Mantine theme + ErrorBoundary + SuspenseBoundary + navbar + routing

### Epic 2: Core Workflow Loop
Developer creates a workflow, the engine executes stages with a mock executor, git branches/tags are created, artifacts are committed, and the user can review and approve at gates — completing the full create→execute→review→approve loop.

**FRs covered:** FR1-6, FR21-23, FR27-33, FR35, FR55, FR57
**ARs covered:** AR6, AR10, AR12
**UX-DRs covered:** UX-DR5-15, UX-DR17, UX-DR21-22, UX-DR25
**NFRs addressed:** NFR5, NFR14

**Includes:**
- Workflow YAML parsing + WorkflowStateMachine (Domain)
- WorkflowEngine + sequential stage execution
- IStageExecutor + MockExecutor
- Git branching (two-tier), tagging, merging, artifact storage
- Minimal workflow list view at `/` (simple table, before full Dashboard)
- WorkflowDetailPage with two-mode design (Conversation + Gate)
- ConversationTimeline + StageMarker + InlineTransitionCard
- StagePipeline component
- ActionBar with gate actions + dual-mode prompt
- ContextPanel with all 5 tabs
- ArtifactRenderer (react-markdown + Mermaid)
- New Workflow dialog
- Workflow pause/resume/abandon
- **Git diff spike story** (validate diff between tags produces usable cascade context — de-risks Epic 5)

### Epic 3: AI Agent Execution & Streaming
Developer watches a real AI agent execute stages in real-time — streaming output token-by-token, seeing tool calls, activity status, and multi-model routing. Replaces the mock executor from Epic 2.

**FRs covered:** FR11-19
**ARs covered:** AR7, AR9, AR13
**UX-DRs covered:** UX-DR7, UX-DR19, UX-DR23
**NFRs addressed:** NFR1-3, NFR8-9, NFR12-13

**Includes:**
- AgentExecutor (IStageExecutor implementation using Microsoft Agent Framework)
- ToolRegistry + all built-in tools (FileRead, FileWrite, FileEdit, Bash, Glob, Grep, Git)
- Agent workspace scoping + path traversal blocking
- Real-time streaming via SignalR (AgentTextDelta events)
- ActivityStatusLine (tool calls, tokens, elapsed time, debounced 500ms)
- Multi-model routing via IChatClient
- Checkpoint/resume on crash recovery
- SuggestedActions extensibility field (v1.1 hook — MVP ignores)

### Epic 4: Dashboard & Real-Time Monitoring
Users see all workflows at a glance on a live-updating card dashboard with filters, status badges, mini-pipelines, and can triage pending reviews efficiently. Replaces the minimal list from Epic 2.

**FRs covered:** FR54, FR56, FR58
**UX-DRs covered:** UX-DR2-4, UX-DR20, UX-DR23-24
**NFRs addressed:** NFR1, NFR4

**Includes:**
- WorkflowCard component (title, status badge, MiniPipeline, cost, border-left color)
- DashboardGrid (responsive card layout)
- Filter bar (search, status, owner, project)
- Empty state ("Create your first workflow")
- Real-time card updates via SignalR
- Toast notifications for background events
- Dashboard loads in <2s (NFR1)

---

**MVP LINE — Epics 1-4 deliver the minimum viable product**

---

### Epic 5: Course Correction & Cascade Updates
Users can go back to a previous stage, see which downstream stages are affected with reasons, choose how to handle each, and have the AI intelligently patch artifacts using diff-based context. The core differentiator.

**FRs covered:** FR24-26, FR34, FR36-42
**UX-DRs covered:** UX-DR16, UX-DR18

**Includes:**
- Go-back state transitions in WorkflowStateMachine
- CascadeService (detect affected stages, compute diffs)
- CascadeDecisionCard UI (affected stages with reason + 3 options)
- ArtifactDiffViewer (raw markdown source diff)
- Diff-based agent re-execution with git diff context
- Version preservation (no destructive overwrites)
- Cascade audit events (FR42, FR53)

### Epic 6: Audit Trail & Cost Tracking
Users see complete audit history and cost breakdown for every workflow, stage, and LLM call — with queryable data and two-tier storage.

**FRs covered:** FR47-53
**NFRs addressed:** NFR16-18, NFR22-24

**Includes:**
- AuditService + AuditMiddleware (full content recording)
- CostTrackingService + cost ledger (permanent)
- Two-tier storage: cost ledger (relational) + audit content (JSONB, archivable)
- Audit query API + UI (Audit tab in ContextPanel)
- Stage execution records reference git tags
- Audit retention and cleanup via admin API

### Epic 7: GitHub Integration
System creates PRs, monitors CI status and comments, and feeds PR feedback to agents for automated responses — enabling team collaboration through familiar GitHub workflows.

**FRs covered:** FR59-64

**Includes:**
- GitHubService (PR creation: stage→workflow-master, workflow-master→main)
- PR comment/status monitoring (polling, future webhooks)
- Feed PR feedback to agent for response
- Push agent-generated commits to stage branches
- Feature-flagged per environment

### Epic 8: External Change Detection
When team members modify the repo outside Antiphon, the system detects changes, distinguishes Antiphon commits from external ones, and triggers cascade updates for affected artifacts.

**FRs covered:** FR65-71

**Includes:**
- ChangeDetectionService (git fetch polling at configurable interval)
- Commit classification ([antiphon] trailer detection)
- Auto-pull on external commit detection
- Path-based cascade detection (_antiphon/artifacts/ changes)
- Automatic cascade update trigger for affected stages
- Audit trail for external change events

---

## Epic 1: Platform Foundation & Configuration

Admin deploys Antiphon, configures LLM providers and projects, and the platform is operational with all infrastructure in place for subsequent epics.

### Story 1.1: Project Scaffold & Development Environment

As a **developer**,
I want a working monorepo with backend and frontend projects, Docker Compose for PostgreSQL, and AI agent context files,
So that I can start building features with a consistent, documented foundation.

**Acceptance Criteria:**

**Given** a fresh clone of the repository
**When** I run `docker compose -f docker-compose.dev.yml up` and `dotnet run` and `npm run dev`
**Then** the backend serves API at `/api` and the frontend loads at `localhost:5173` with Vite proxy forwarding `/api/*` and `/hubs/*` to the backend
**And** the solution structure follows the architecture: `/server` (Domain, Application, Infrastructure, Api, Migrations) + `/client` (features, shared, stores, api, hooks)
**And** `docs/project-context.md` exists with extracted enforcement guidelines from the architecture document
**And** `AGENTS.md` exists at the project root, referencing `docs/project-context.md` as the primary conventions source
**And** `CLAUDE.md` exists at the project root, pointing at `AGENTS.md` for all agent context
**And** `.editorconfig`, `.gitignore`, `appsettings.json.example`, and `Antiphon.sln` are configured
**And** `docker-compose.yml` (production) and `docker-compose.dev.yml` (dev — PostgreSQL only) exist

### Story 1.2: Database Foundation & Configuration System

As an **admin**,
I want the platform to connect to PostgreSQL with EF Core and have a typed configuration system,
So that data persists reliably and settings are managed consistently.

**Acceptance Criteria:**

**Given** PostgreSQL is running via Docker Compose
**When** the application starts
**Then** `AppDbContext` connects to PostgreSQL and `context.Database.Migrate()` runs automatically
**And** typed settings classes exist: `GitSettings`, `LlmSettings`, `SignalRSettings`, `AuditSettings`, `GithubSettings`
**And** settings bind from `appsettings.json` via `IOptions<T>` — no raw `IConfiguration` in services
**And** transaction isolation prevents concurrent writes to the same workflow stage (NFR15)
**And** JSONB column support is configured for flexible config storage

### Story 1.3: Error Handling & Logging Infrastructure

As a **developer**,
I want all errors to return RFC 9457 Problem Details with full stack traces, and structured logging with correlation IDs,
So that I can debug issues quickly from the frontend error display or server logs.

**Acceptance Criteria:**

**Given** any API endpoint
**When** an unhandled exception occurs
**Then** the global `ExceptionMiddleware` catches it and returns RFC 9457 Problem Details with full stack trace, inner exceptions, and correlation ID
**And** `HttpException` hierarchy exists: `NotFoundException` (404), `ConflictException` (409), `ValidationException` (422 with errors dict), `ForbiddenException` (403)
**And** throwing `new NotFoundException("Workflow", id)` from any service returns the correct status code and Problem Details
**And** Serilog is configured with console + file sinks
**And** `CorrelationIdMiddleware` sets workflowId, stageId, executionId via `AsyncLocal` + `LogContext.PushProperty()`
**And** every log line carries correlation IDs (NFR19)
**And** OpenTelemetry traces are emitted via Microsoft.Extensions.AI middleware (NFR20)
**And** `/health` endpoint reports: database connectivity, LLM provider reachability, git repo accessibility, disk space (NFR21)

### Story 1.4: Authentication Abstraction & Request Attribution

As an **admin**,
I want every API request attributed to a user with IP logging,
So that audit trails are complete from day one and the auth system can be swapped to OIDC later with zero refactoring.

**Acceptance Criteria:**

**Given** any API request
**When** the request is processed
**Then** `CurrentUserMiddleware` resolves `ICurrentUser` to the default admin user + client IP address
**And** client IP is logged on every request (NFR11)
**And** `ICurrentUser` interface is the only auth dependency in Application and Api layers
**And** the `User` entity exists in the database with the seeded default admin

### Story 1.5: Real-Time Communication Infrastructure

As a **developer**,
I want a SignalR hub with group-based routing and an IEventBus abstraction,
So that all future real-time features have a tested, decoupled foundation.

**Acceptance Criteria:**

**Given** the application is running
**When** a browser connects to `/hubs/antiphon`
**Then** a SignalR connection is established via the single hub
**And** clients can join/leave groups (`workflow-{id}`, `dashboard`) via hub methods
**And** `IEventBus` interface exists in Application with `EventBus` implementation in Infrastructure routing to SignalR groups
**And** services push events through `IEventBus`, never calling SignalR directly
**And** frontend `useSignalR` hook manages connection lifecycle (connect, reconnect via `withAutomaticReconnect()`, disconnect)
**And** frontend `useSignalRInvalidation` hook maps SignalR events to TanStack Query invalidation per the mapping table
**And** `connectionStore` (Zustand) tracks connection status
**And** SignalR state changes are delivered to clients within 1 second (NFR4)

### Story 1.6: Test Infrastructure & E2E Foundation

As a **developer**,
I want Playwright E2E fixtures, CachingChatClient for LLM replay, and frontend test setup,
So that every subsequent story can include E2E tests with cached LLM responses from the start.

**Acceptance Criteria:**

**Given** the test projects exist (`Antiphon.Tests.csproj`, `Antiphon.E2E.csproj`)
**When** I run `dotnet test`
**Then** unit and integration tests execute with shared PostgreSQL testcontainer + transaction rollback per test
**And** `CachingChatClient` decorator exists with 4 modes: Record, RecordIfMissing, Replay, PassThrough
**And** cache normalizer strips timestamps, absolute paths, UUIDs from messages before hashing
**And** `.antiphon-cache/` directory stores one JSON file per cached call
**And** `AntiphonAppFixture` sets up `WebApplicationFactory<Program>` with testcontainer DB + CachingChatClient
**And** `AntiphonAppFixture` supports `UsePrebuiltFrontend` flag (true = static files, false = Vite dev server)
**And** `PlaywrightFixture` manages chromium headless browser lifecycle
**And** frontend `client/src/test/` has: `setup.ts`, `mocks/handlers.ts`, `mocks/server.ts`, `utils.ts` (renderWithProviders)
**And** a smoke test verifies the full stack: browser → React → API → DB

### Story 1.7: Frontend Shell & Navigation

As a **user**,
I want to see a polished app shell with dark theme, navigation, error boundaries, and loading states,
So that the application feels professional and I can navigate between pages.

**Acceptance Criteria:**

**Given** I open the application in a browser
**Then** Mantine dark theme is applied with status colors: green (success), orange (pending), red (error), blue (active) (UX-DR1)
**And** navbar shows: logo "Antiphon" (left, click → Dashboard), nav links "Workflows" + "Settings" (center), user avatar placeholder (right) (UX-DR28)
**And** React Router handles routes: `/` (Dashboard placeholder), `/workflow/:id` (placeholder), `/settings` (placeholder)
**And** `ErrorBoundary` wraps each major section with error display + retry + "Go to Dashboard" fallback (UX-DR26)
**And** `SuspenseBoundary` variants (Page, Panel, Inline, Card) exist with 200ms invisible delay before skeleton (UX-DR27)
**And** keyboard accessibility works for all Mantine interactive components (UX-DR29)
**And** `prefers-reduced-motion` media query is respected for animations (UX-DR29)

### Story 1.8: Workflow Template Management

As an **admin**,
I want to add, view, edit, and delete YAML workflow templates through a Settings UI,
So that teams can use different workflow methodologies.

**Acceptance Criteria:**

**Given** I navigate to Settings → Templates tab
**When** I click "Add Template"
**Then** I can provide a name, description, and YAML definition for a new workflow template
**And** the system validates the YAML structure (stages, ordering, executor type, model routing, gate config) within 1 second (NFR6, FR10)
**And** I can view all templates in a list with name and description
**And** I can edit an existing template's YAML definition
**And** I can delete a template with a confirmation dialog (UX-DR25)
**And** the system ships with bundled BMAD workflow templates (full and quick variants) via database seeding (FR9)
**And** API endpoints: `GET/POST /api/settings/templates`, `GET/PUT/DELETE /api/settings/templates/{id}`

### Story 1.9: LLM Provider Configuration

As an **admin**,
I want to configure LLM providers and API keys through a Settings UI,
So that the platform can route stages to different AI models.

**Acceptance Criteria:**

**Given** I navigate to Settings → LLM Providers tab
**When** I add a provider configuration
**Then** I can configure providers: Anthropic Claude, OpenAI, Ollama with API keys and base URLs (FR20)
**And** API keys are stored in server-side configuration only — never returned to the frontend or logged (NFR7)
**And** I can set default model routing (e.g., Opus for architecture, Sonnet for implementation)
**And** I can test provider connectivity from the UI (shows success/error)
**And** Git credentials (tokens, SSH keys) are server-side only (NFR10)
**And** API endpoints: `GET/POST /api/settings/providers`, `PUT/DELETE /api/settings/providers/{id}`, `POST /api/settings/providers/{id}/test`

### Story 1.10: Project Configuration & Feature Flags

As an **admin**,
I want to create projects pointing at git repositories and configure feature flags,
So that workflows can target specific codebases with appropriate integrations enabled.

**Acceptance Criteria:**

**Given** I navigate to Settings → Projects tab
**When** I create a new project
**Then** I can specify a git repository URL and the system validates connectivity (FR43)
**And** I can configure model routing per stage for this project (FR44)
**And** the system detects and loads `project-context.md` from the repo as project constitution (FR45)
**And** I can enable/disable feature flags per project: GitHub integration, notifications (FR46)
**And** I can delete a project with confirmation dialog (UX-DR25)
**And** API endpoints: `GET/POST /api/projects`, `GET/PUT/DELETE /api/projects/{id}`

### Story 1.11: Database Seeding & First Run Experience

As an **admin**,
I want the platform to be immediately usable after first deployment,
So that I don't need to manually configure anything before creating my first workflow.

**Acceptance Criteria:**

**Given** a fresh deployment with empty database
**When** the application starts
**Then** `DatabaseSeeder` runs after migrations, idempotently
**And** default admin user is created
**And** bundled BMAD workflow templates (full + quick) are seeded
**And** default model routing configuration is seeded
**And** running seeding again on an existing database makes no changes (idempotent)
**And** the Settings page shows the seeded templates, providers (unconfigured), and no projects

---

## Epic 2: Core Workflow Loop

Developer creates a workflow, the engine executes stages with a mock executor, git branches/tags are created, artifacts are committed, and the user can review and approve at gates — completing the full create→execute→review→approve loop.

### Story 2.1: Workflow Domain Model & State Machine

As a **developer**,
I want the core workflow domain model with a pure state machine that validates and exposes available transitions,
So that the engine has a correct, testable foundation for all workflow orchestration.

**Acceptance Criteria:**

**Given** the Domain layer
**When** entities are created
**Then** `Workflow`, `Stage`, `GateDecision`, `StageExecution` entities exist with correct properties
**And** `WorkflowStatus` enum: Created, Running, Paused, GateWaiting, Completed, Failed, Abandoned
**And** `StageStatus` enum: Pending, Running, Completed, Failed
**And** `GateAction` enum: Approved, RejectedWithFeedback, GoBack
**And** `WorkflowStateMachine` is pure logic (zero infrastructure dependencies)
**And** `CanTransition(from, to)` validates state transitions
**And** `GetAvailableTransitions(currentState)` returns valid actions for the current state (AR10)
**And** unit tests cover all valid transitions and reject invalid ones

### Story 2.2: Workflow Engine & Sequential Stage Execution

As a **developer**,
I want a workflow engine that parses YAML definitions and executes stages sequentially using IStageExecutor,
So that workflows progress through their defined stage graph automatically.

**Acceptance Criteria:**

**Given** a valid YAML workflow definition and a configured project
**When** a workflow is created
**Then** `WorkflowEngine` parses the YAML definition into a `WorkflowDefinition` value object
**And** stages execute sequentially according to the YAML definition (FR6)
**And** execution delegates to `IStageExecutor` (FR11 abstraction)
**And** `MockExecutor` (AR6) is the default implementation — produces placeholder artifacts for testing
**And** the engine pauses at configured gate points and sets workflow status to `GateWaiting` (FR21)
**And** events are pushed via `IEventBus` for stage start, stage complete, and gate ready
**And** integration tests verify the full engine cycle with MockExecutor

### Story 2.3: Git Branching, Tagging & Artifact Storage

As a **developer**,
I want the system to manage git branches and tags for each workflow and stage,
So that artifacts are version-controlled with clean audit traceability.

**Acceptance Criteria:**

**Given** a workflow is created targeting a git repository
**When** the workflow starts
**Then** system creates `antiphon/workflow-{id}/master` branch (FR28)
**And** system creates `antiphon/workflow-{id}/stage-{name}` branch for each stage execution (FR29)
**And** agent (MockExecutor) commits changes to stage branch with `[antiphon]` trailer (FR30)
**And** system tags stage commits: `antiphon/workflow-{id}/{stage}-v{version}` before merging (FR31)
**And** system merges stage branch into workflow master on gate approval (FR32)
**And** artifacts are stored in `_antiphon/artifacts/workflow-{id}/` in the repo (FR33)
**And** git operations complete within 5 seconds for repos under 1GB (NFR5)
**And** `IGitService` interface with `GitService` implementation in Infrastructure

### Story 2.4: Workflow CRUD API & Minimal List View

As a **developer**,
I want to create workflows via API and see them in a minimal list view,
So that I can create, view, and navigate to workflows before the full dashboard exists.

**Acceptance Criteria:**

**Given** I am on the home page (`/`)
**When** I view the page
**Then** I see a simple table listing all workflows: name, status, current stage, created date, link to detail page (FR3)
**And** I can see a visual progress indicator per workflow (FR4)
**And** the list updates in real-time via SignalR when workflow status changes
**And** API endpoints exist: `POST /api/workflows` (create), `GET /api/workflows` (list), `GET /api/workflows/{id}` (detail)
**And** the API returns `availableTransitions` alongside workflow state (AR10)
**And** TanStack Query hooks exist: `useWorkflows()`, `useWorkflow(id)`

### Story 2.5: New Workflow Dialog

As a **developer**,
I want to create a new workflow by selecting a template, project, and providing context in a dialog,
So that I can start AI-assisted work with minimal ceremony.

**Acceptance Criteria:**

**Given** I click "New Workflow" from the list view or navbar
**When** the dialog opens
**Then** I can select a workflow template from a simple list with descriptions (UX-DR5)
**And** I can select a project/git repository
**And** I can enter initial context as free-text (description, pasted ticket details, etc.) (FR2)
**And** clicking "Create" immediately creates the workflow and navigates to WorkflowDetailPage (FR1)
**And** no "processing" screen — page navigates immediately and streaming begins (UX-DR5)
**And** error states: invalid repo → inline error, template missing → clear error, agent fails → retry button

### Story 2.6: Workflow Pause, Resume & Abandon

As a **developer**,
I want to pause, resume, or abandon a workflow,
So that I can manage active work and clean up workflows I no longer need.

**Acceptance Criteria:**

**Given** an active workflow
**When** I click "Pause"
**Then** the workflow status changes to Paused and agent execution stops (FR5)
**And** I can resume a paused workflow and execution continues from where it left off
**And** I can abandon a workflow with a confirmation dialog (UX-DR25) — marks as Abandoned
**And** state machine validates these transitions via `GetAvailableTransitions`
**And** events pushed via `IEventBus` for pause/resume/abandon
**And** the workflow list view updates in real-time

### Story 2.7: WorkflowDetailPage Shell & Mode Switching

As a **developer**,
I want a WorkflowDetailPage that switches between Conversation Mode and Gate Mode based on workflow state,
So that I see the right view at the right time — streaming during execution, artifact during review.

**Acceptance Criteria:**

**Given** I navigate to `/workflow/:id`
**When** the workflow is in an executing state
**Then** the page renders in Conversation Mode: main area for streaming, right panel (360px fixed) with tabs (UX-DR6-7)
**When** the workflow is at a gate checkpoint
**Then** the page renders in Gate Mode: main area shows artifact, right panel defaults to Outputs tab (UX-DR8)
**And** landing mode is determined by current workflow state, not URL (UX-DR6)
**And** `StagePipeline` shows horizontal stage progression with status per stage (UX-DR9)
**And** clicking a completed stage in the pipeline loads its artifact in the main area
**And** mode transitions preserve prompt bar state (no re-mount, no focus loss) (UX-DR12)

### Story 2.8: ConversationTimeline & Stage Markers

As a **developer**,
I want a chronological conversation timeline with collapsible stage markers,
So that I can follow the full history of a workflow including all feedback cycles and stage transitions.

**Acceptance Criteria:**

**Given** the WorkflowDetailPage in Conversation Mode
**When** the agent is executing
**Then** `ConversationTimeline` renders a single chronological timeline across the entire workflow (UX-DR10)
**And** message types render with distinct styling: AgentMessage, UserPromptMessage (blue "Sent to Agent"), UserCommentMessage (distinct "Comment" label), SystemEvent (muted centered), ToolCallBlock (collapsible)
**And** `StageMarker` dividers show: stage name, version, message count, timestamp, gate decision (UX-DR11)
**And** current stage expanded by default, previous stages collapsed
**And** the same `ConversationTimeline` component instance is shared between main area and Conversation tab (via CSS/portal — never unmounted)
**And** auto-scrolls during streaming, preserves scroll position otherwise

### Story 2.9: ActionBar & Dual-Mode Prompt

As a **developer**,
I want a fixed bottom action bar with gate actions and a dual-mode prompt input,
So that I can approve artifacts, provide feedback to the agent, or leave comments for team members.

**Acceptance Criteria:**

**Given** the WorkflowDetailPage
**When** at a gate checkpoint
**Then** the `ActionBar` shows: gate buttons left (Approve green, Reject yellow, Go Back gray with icon), prompt input + two send buttons right (UX-DR12)
**And** "Send to Agent" (blue) triggers agent re-execution with feedback (FR27) → page transitions to Conversation Mode
**And** "Add Comment" (gray) leaves a human-to-human note in the conversation timeline — does NOT trigger the agent (UX-DR21)
**And** Enter key → Send to Agent (always). No keyboard shortcut for Add Comment (UX-DR21)
**And** comments are visually distinct from agent prompts: different background, icon, "Comment" label (UX-DR22)
**When** in execution mode
**Then** gate buttons are hidden, prompt + send buttons remain visible
**And** the ActionBar is the same component instance across mode transitions — never re-mounted (UX-DR12)

### Story 2.10: ContextPanel with Tabs

As a **developer**,
I want a right-side context panel with 5 tabs providing navigation, artifacts, conversation history, diffs, and audit data,
So that I have all context accessible without leaving the main view.

**Acceptance Criteria:**

**Given** the WorkflowDetailPage
**Then** `ContextPanel` renders as a 360px fixed right panel with 5 always-visible tabs: Outputs, Stage Info, Conversation, Diff, Audit (UX-DR13)
**And** default tab: Stage Info during execution, Outputs at gate
**And** **Outputs tab**: primary artifact pinned with "Primary" badge, version history per artifact, click loads artifact in main area (UX-DR14)
**And** **Stage Info tab**: pipeline progress, stage metadata, upstream context
**And** **Conversation tab**: renders shared `ConversationTimeline` component
**And** **Diff tab**: empty state "No diff available yet" (full implementation in Epic 5)
**And** **Audit tab**: placeholder (full implementation in Epic 6)
**And** per-tab scroll preservation (UX-DR13)
**And** tabs with no content show appropriate empty states

### Story 2.11: ArtifactRenderer with Markdown & Mermaid

As a **developer**,
I want to view AI-generated artifacts as beautifully rendered markdown with syntax highlighting and Mermaid diagram support,
So that I can assess artifact quality quickly and see diagrams inline.

**Acceptance Criteria:**

**Given** a gate checkpoint with an artifact
**When** the artifact is displayed in the main area
**Then** markdown renders via `react-markdown` + `remark-gfm` + `rehype-highlight` + `rehype-raw` (UX-DR17)
**And** code blocks have syntax highlighting
**And** GFM tables, task lists, and strikethrough render correctly
**And** ` ```mermaid ` code fences render as diagrams via lazy-loaded `<MermaidDiagram>` component using `mermaid.render()` (UX-DR17)
**And** Mermaid library (~200KB) only loads when a diagram is present on the page
**And** artifact renders at constrained width (~900px) in gate mode, full-width available for wide content
**And** `ArtifactContextHint` above artifact shows: "Generated · {revisions} revisions · {time ago} · [View conversation →]"
**And** version history is browsable — user can view previous versions (FR57)

### Story 2.12: Gate Review Flow (Approve & Reject)

As a **developer**,
I want to approve or reject an artifact at a gate with clear actions and immediate feedback,
So that I can advance the workflow or request changes with confidence.

**Acceptance Criteria:**

**Given** I am at a gate checkpoint viewing an artifact
**When** I click "Approve"
**Then** the stage branch merges to workflow master (FR32), git tag created (FR31)
**And** workflow advances to next stage — page transitions to Conversation Mode (FR22)
**And** the next stage's agent begins executing (streaming visible)
**When** I click "Reject with Feedback"
**Then** I can enter feedback text explaining what's wrong
**And** the agent re-executes the stage with rejection context injected (FR23)
**And** page transitions to Conversation Mode with streaming
**When** I type feedback and click "Send to Agent"
**Then** the agent receives my feedback and produces a revised artifact (FR27)
**And** `InlineTransitionCard` appears when the revised artifact is ready (UX-DR15)
**And** clicking the transition card switches back to Gate Mode with the updated artifact
**And** confirmation dialogs are NOT shown for approve or reject — these are recoverable actions (UX-DR25)

### Story 2.13: Git Diff Spike (De-risk Epic 5)

As a **developer**,
I want to validate that computing git diffs between stage version tags produces usable context for cascade updates,
So that we have confidence in the course correction approach before building the full cascade UI.

**Acceptance Criteria:**

**Given** a workflow with at least two completed stages, each with versioned tags
**When** I compute a path-filtered diff between `{stage}-v1` and `{stage}-v2` tags (FR34)
**Then** the diff accurately captures what changed in `_antiphon/artifacts/`
**And** the diff output is parseable and provides enough context for an AI agent to intelligently patch downstream artifacts
**And** the diff computation completes within 5 seconds for repos under 1GB (NFR5)
**And** this spike is documented as a technical note for Epic 5 story reference

---

## Epic 3: AI Agent Execution & Streaming

Developer watches a real AI agent execute stages in real-time — streaming output token-by-token, seeing tool calls, activity status, and multi-model routing. Replaces the mock executor from Epic 2.

### Story 3.1: Agent Executor & Microsoft Agent Framework Integration

As a **developer**,
I want the real AI agent executor to replace MockExecutor, using Microsoft Agent Framework with IChatClient,
So that workflow stages are executed by actual LLM-powered agents.

**Acceptance Criteria:**

**Given** a workflow stage configured with an LLM model
**When** the stage executes
**Then** `AgentExecutor` implements `IStageExecutor` using Microsoft Agent Framework
**And** the agent receives upstream artifacts, project constitution, and stage-specific instructions as context (FR12)
**And** the agent uses the configured LLM model via `IChatClient` abstraction (FR11)
**And** the return type includes optional `SuggestedActions` field (AR9 — v1.1 hook, MVP ignores)
**And** Agent Framework checkpoints persist after every tool call completion (NFR12)
**And** on crash recovery, the agent resumes from last checkpoint, not from the beginning (NFR12)
**And** if execution fails, the stage is marked as failed with full error details and user can retry from last checkpoint or last gate (NFR13)

### Story 3.2: Built-in Agent Tools & ToolRegistry

As a **developer**,
I want agents to have built-in tools for file operations, shell commands, search, and git,
So that agents can read, modify, and manage code in the project workspace.

**Acceptance Criteria:**

**Given** an agent executing a stage
**When** the agent needs to interact with the workspace
**Then** `ToolRegistry` discovers and registers all built-in tools (AR7)
**And** `FileReadTool` reads files scoped to the project worktree (FR13, NFR8)
**And** `FileWriteTool` writes files scoped to the project worktree (FR13, NFR8)
**And** `FileEditTool` edits files scoped to the project worktree (FR13, NFR8)
**And** `BashTool` executes shell commands with working directory set to scoped workspace (FR14, NFR9)
**And** `GlobTool` searches files by pattern within the workspace (FR15)
**And** `GrepTool` searches file contents within the workspace (FR15)
**And** `GitTool` performs git operations: clone, checkout, branch, commit, diff, push, tag (FR16)
**And** path traversal attempts are blocked for all file tools (NFR8)
**And** git credentials are server-side only — agents never access credentials directly (NFR10)

### Story 3.3: Real-Time Agent Streaming via SignalR

As a **developer**,
I want to see agent output streaming token-by-token in real-time with tool call visibility,
So that I can watch the AI work and understand what it's doing.

**Acceptance Criteria:**

**Given** an agent is executing a stage
**When** the agent produces output
**Then** `AgentTextDelta` events are pushed via SignalR immediately for token-by-token rendering (FR17)
**And** streaming latency is under 500ms from LLM response to UI render (NFR2)
**And** tool calls appear as collapsible `ToolCallBlock` entries in the ConversationTimeline showing tool name + input/output
**And** the ConversationTimeline auto-scrolls during streaming
**And** the Zustand `streamingStore` accumulates text deltas before render for smooth display
**And** the agent's conversation flows into the shared `ConversationTimeline` as `AgentMessage` entries

### Story 3.4: Activity Status Line

As a **developer**,
I want a live activity status line showing what the agent is currently doing,
So that I always know the agent's state and progress without reading the full output.

**Acceptance Criteria:**

**Given** an agent is executing a stage
**When** the agent makes tool calls or generates output
**Then** `ActivityStatusLine` displays: pulsing status dot, current action (tool name + target), cumulative tokens in/out, tool call count, elapsed time (FR18, UX-DR19)
**And** `AgentActivityUpdate` events are debounced at 500ms server-side (NFR3)
**And** the status line is visible during execution, hidden when idle
**And** the status line uses aria-live region for accessibility (UX-DR19)
**And** no silent periods — user always sees what the agent is doing

### Story 3.5: Multi-Model Routing

As an **admin**,
I want different workflow stages to use different LLM models,
So that I can optimize cost and capability per stage.

**Acceptance Criteria:**

**Given** a workflow definition with per-stage model routing configuration
**When** each stage executes
**Then** the system routes to the configured LLM model via `IChatClient` (FR19)
**And** the routing respects project-level model routing overrides (FR44)
**And** if a model is unavailable, the stage fails with a clear error indicating the model and provider
**And** Serilog correlation enrichers log the model used per stage (AR13)

---

## Epic 4: Dashboard & Real-Time Monitoring

Users see all workflows at a glance on a live-updating card dashboard with filters, status badges, mini-pipelines, and can triage pending reviews efficiently. Replaces the minimal list from Epic 2.

### Story 4.1: WorkflowCard & MiniPipeline Components

As a **user**,
I want rich workflow cards with status badges and mini-pipeline indicators,
So that I can see each workflow's status and progress at a glance.

**Acceptance Criteria:**

**Given** a workflow exists
**When** I view the dashboard
**Then** `WorkflowCard` displays: title, status badge, MiniPipeline, current stage name, cost, last updated, template name (UX-DR2)
**And** card border-left color indicates status: blue (active), orange (pending review), green (complete), red (failed) (UX-DR2)
**And** `MiniPipeline` shows compact stage indicators: Done (green), Active (blue pulsing), Pending (gray), Failed (red) (UX-DR20)
**And** MiniPipeline adapts to stage count (3-stage quick vs 6-stage full) (UX-DR20)
**And** card click navigates to WorkflowDetailPage
**And** card hover shows subtle elevation change
**And** card is focusable with Enter to navigate, status via aria-label

### Story 4.2: Dashboard Card Grid, Filters & Empty State

As a **user**,
I want a dashboard with a responsive card grid and filters,
So that I can triage pending reviews and monitor all workflows efficiently.

**Acceptance Criteria:**

**Given** I navigate to the dashboard (`/`)
**When** workflows exist
**Then** a responsive card grid displays all workflows (FR54), replacing the minimal list from Epic 2
**And** filter bar provides: search, status filter, owner filter, project filter (UX-DR3)
**And** "Pending Review" filter shows count of gates waiting for review (UX-DR3)
**And** dashboard page loads within 2 seconds (NFR1)
**When** no workflows exist
**Then** empty state shows: workflow icon, "No workflows yet", "Create your first AI-assisted workflow", "New Workflow" button (UX-DR4)
**When** filters produce no results
**Then** empty state shows: search icon, "No matching workflows", "Clear filters" link (UX-DR4)

### Story 4.3: Real-Time Dashboard Updates & Toasts

As a **user**,
I want dashboard cards to update in real-time and receive toast notifications for background events,
So that I always see current status without manual refresh.

**Acceptance Criteria:**

**Given** I am on the dashboard
**When** a workflow status changes
**Then** the affected card updates in real-time via SignalR: badge, border color, MiniPipeline, stage name (FR56, UX-DR23)
**And** card updates animate with 1.5s highlight glow with fade-out
**And** new workflows appear with 300ms fade-in
**And** pipeline stage advances show: connector fills green (300ms), new active stage pulses
**And** toast notifications appear for background events (UX-DR24)
**And** toasts auto-dismiss in 3-5s, max 3 visible, clickable to navigate to workflow
**And** SignalR disconnection shows persistent toast: "Connection lost — reconnecting..."
**And** reconnection shows auto-dismiss toast: "Reconnected" (3s)
**And** no toasts for user-initiated actions (UX-DR24)

---

**MVP LINE — Epics 1-4 deliver the minimum viable product**

---

## Epic 5: Course Correction & Cascade Updates

Users can go back to a previous stage, see which downstream stages are affected with reasons, choose how to handle each, and have the AI intelligently patch artifacts using diff-based context. The core differentiator.

### Story 5.1: Go-Back State Transitions & Affected Stage Detection

As a **developer**,
I want the system to handle go-back requests by identifying which downstream stages are affected and why,
So that I understand the impact of course correction before committing to it.

**Acceptance Criteria:**

**Given** I am at a gate checkpoint
**When** I click "Go Back"
**Then** the system identifies downstream stages that were built on the artifact being corrected (FR25, FR38)
**And** for each affected stage, the system determines the REASON it's affected
**And** unaffected stages are identified as "Not affected"
**And** go-back transitions are validated by `WorkflowStateMachine` (FR24)
**And** the go-back event is recorded in the audit trail (FR42, FR53)

### Story 5.2: CascadeDecisionCard & User Choices

As a **developer**,
I want to choose how to handle each affected downstream stage when going back,
So that I can preserve valid work while fixing what's broken.

**Acceptance Criteria:**

**Given** I've triggered a go-back and affected stages are identified
**When** the `CascadeDecisionCard` appears in the main area
**Then** each affected stage shows: stage name, current version, reason for impact, three radio options (UX-DR16)
**And** options are: "Update based on diff" (default/recommended), "Regenerate from scratch", "Keep as-is" (FR26)
**And** unaffected stages show "Not affected — no action needed"
**And** expandable diff preview is available per affected stage before confirming
**And** ActionBar shows "Confirm selections" (primary) and "Cancel go-back" (secondary)
**And** Cancel returns to the original gate review without changes
**And** Confirm triggers re-execution of the previous stage → page transitions to Conversation Mode

### Story 5.3: Diff-Based Cascade Execution

As a **developer**,
I want the AI to intelligently patch downstream artifacts based on what actually changed,
So that course correction preserves valid work instead of regenerating everything.

**Acceptance Criteria:**

**Given** I've confirmed cascade selections and the corrected stage produces a new artifact version
**When** affected stages are processed according to user's choices
**Then** "Update based on diff": system computes git diff between version tags (FR34, FR40) and the agent receives the diff + current downstream artifact, producing an intelligently patched version
**And** "Regenerate": agent re-executes the downstream stage from scratch with updated upstream context
**And** "Keep as-is": no changes to the downstream stage
**And** all artifact versions are preserved — no destructive overwrites (FR41)
**And** the full correction history is captured in the audit trail (FR42)
**And** the user can trigger re-execution of the current stage with modified feedback at any point (FR37)

### Story 5.4: ArtifactDiffViewer

As a **developer**,
I want to view side-by-side or unified diffs between artifact versions,
So that I can see exactly what changed during course correction or between revisions.

**Acceptance Criteria:**

**Given** an artifact with multiple versions
**When** I view the Diff tab in ContextPanel or expand a diff preview in CascadeDecisionCard
**Then** `ArtifactDiffViewer` shows the diff of raw markdown source (not rendered HTML) (UX-DR18, FR36)
**And** side-by-side view available when loaded in main area (full-width)
**And** unified view in the ContextPanel Diff tab (360px)
**And** "View full diff" action in ContextPanel loads side-by-side diff in main area
**And** additions highlighted green, deletions highlighted red, context lines visible

---

## Epic 6: Audit Trail & Cost Tracking

Users see complete audit history and cost breakdown for every workflow, stage, and LLM call — with queryable data and two-tier storage.

### Story 6.1: LLM Call & Tool Recording

As a **developer**,
I want every LLM call and tool invocation recorded with full details,
So that I have complete traceability of what agents did and how much it cost.

**Acceptance Criteria:**

**Given** an agent executes a stage
**When** any LLM call is made
**Then** the system records: model, tokens in/out, approximate USD cost, duration (FR47, NFR16)
**And** full audit content is recorded: prompts, responses, tool call inputs/outputs (FR48, NFR17)
**And** client IP is logged on every request that triggers agent work (FR50)
**And** stage execution audit records reference git tags for traceability (FR51)
**And** "go back" and "update based on diff" events are recorded as first-class audit events (FR53)

### Story 6.2: Two-Tier Audit Storage & Retention

As an **admin**,
I want cost ledger records kept permanently and full audit content archivable after a retention period,
So that cost tracking is always available while storage is managed for full execution logs.

**Acceptance Criteria:**

**Given** audit data is being recorded
**When** data is stored
**Then** cost ledger entries (tokens, USD, model, stage) are stored in relational tables and retained indefinitely (FR49, NFR22)
**And** full audit content (prompts, responses, tool call details) is stored in JSONB blobs separately (FR49)
**And** full audit content is retained for configurable period (default 90 days) (NFR23)
**And** after retention period, full audit content is eligible for deletion via admin API (NFR24)
**And** cost ledger entries are never deleted even when audit content is cleaned up

### Story 6.3: Audit Query API & UI

As a **developer**,
I want to view audit history and cost breakdowns for any workflow or stage,
So that I can understand what happened, debug issues, and track spending.

**Acceptance Criteria:**

**Given** I am viewing a workflow
**When** I open the Audit tab in ContextPanel
**Then** I see: token usage (in/out), tool call count, cost breakdown by model, execution timeline for the current stage
**And** I can view audit history for any workflow or stage execution (FR52)
**And** all audit data is queryable by workflow, stage, time range, and cost (NFR18)
**And** API endpoints: `GET /api/audit?workflowId={id}`, `GET /api/audit?stageId={id}`, query params for time range and cost filtering
**And** the Audit tab in ContextPanel (placeholder from Epic 2) now shows real data

---

## Epic 7: GitHub Integration

System creates PRs, monitors CI status and comments, and feeds PR feedback to agents — enabling team collaboration through familiar GitHub workflows.

### Story 7.1: GitHub PR Creation

As a **developer**,
I want the system to create GitHub PRs from stage and workflow branches,
So that artifacts and code can be reviewed through familiar GitHub workflows.

**Acceptance Criteria:**

**Given** a workflow with GitHub integration enabled (feature flag)
**When** a stage is approved or the workflow completes
**Then** system can create a PR from a stage branch to the workflow master branch (FR59)
**And** system can create a PR from the workflow master branch to main (FR60)
**And** system can push commits to stage branches (agent-generated fixes) (FR63)
**And** GitHub integration is feature-flagged and can be disabled per environment (FR64)
**And** API endpoints for triggering PR creation manually if needed

### Story 7.2: GitHub PR Monitoring & Feedback Loop

As a **developer**,
I want the system to monitor GitHub PRs and feed comments and build status to agents,
So that AI agents can respond to PR feedback automatically.

**Acceptance Criteria:**

**Given** a GitHub PR exists for a workflow
**When** the PR receives new comments, review feedback, or build status changes
**Then** system monitors the PR for updates (FR61)
**And** PR comments and review feedback are fed to the AI agent for response or artifact update (FR62)
**And** the agent can push commits to stage branches in response to feedback (FR63)
**And** monitoring uses polling (future: GitHub webhooks)
**And** all PR events are recorded in the audit trail

---

## Epic 8: External Change Detection

When team members modify the repo outside Antiphon, the system detects changes, distinguishes Antiphon commits from external ones, and triggers cascade updates for affected artifacts.

### Story 8.1: External Commit Detection & Classification

As a **developer**,
I want the system to detect when someone pushes commits to a workflow branch outside of Antiphon,
So that workflows stay in sync with external changes.

**Acceptance Criteria:**

**Given** an active workflow with git branches
**When** the system polls for changes
**Then** `ChangeDetectionService` polls workflow branches via `git fetch` at configurable interval (default 30s) (FR65)
**And** system distinguishes Antiphon commits (marked with `[antiphon]` trailer) from external commits (FR66)
**And** when external commits are detected, system automatically pulls and updates local state (FR67)
**And** all external change events are recorded in audit trail with commit details, author, and diff (FR71)

### Story 8.2: Path-Based Cascade Triggers

As a **developer**,
I want external changes to artifact files to automatically trigger cascade updates,
So that downstream stages are kept in sync without manual intervention.

**Acceptance Criteria:**

**Given** external commits are detected on a workflow branch
**When** changes touch files in `_antiphon/artifacts/`
**Then** system triggers path-based cascade detection to identify affected downstream stages (FR68)
**And** for affected stages, system automatically triggers the cascade update flow (update based on diff) (FR69)
**And** code-only changes (outside `_antiphon/artifacts/`) update local state without triggering cascade (FR70)
**And** all triggered cascades are recorded in the audit trail (FR71)
