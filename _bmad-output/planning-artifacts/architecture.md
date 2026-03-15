---
stepsCompleted: [1, 2, 3, 4, 5, 6, 7, 8]
lastStep: 8
status: 'complete'
completedAt: '2026-03-15'
inputDocuments:
  - _bmad-output/planning-artifacts/prd.md
  - _bmad-output/planning-artifacts/ux-design-specification.md
  - _bmad-output/planning-artifacts/ux-journeys-draft.md
  - _bmad-output/planning-artifacts/original-rough-spec.md
workflowType: 'architecture'
project_name: 'Antiphon'
user_name: 'Mike'
date: '2026-03-15'
---

# Architecture Decision Document

_This document builds collaboratively through step-by-step discovery. Sections are appended as we work through each architectural decision together._

## Project Context Analysis

### Requirements Overview

**Functional Requirements:**
71 FRs across 10 categories with the following architectural weight:

| Category | FR Count | Architectural Impact |
|----------|----------|---------------------|
| Workflow Management | FR1-FR6 | Core engine: YAML parsing, stage graph execution, state machine |
| Workflow Definition | FR7-FR10 | Template system, YAML schema, bundled defaults |
| AI Agent Execution | FR11-FR20 | Agent Framework integration, tool system, multi-model routing, streaming |
| Approval Gates | FR21-FR27 | State transitions, user interaction model, feedback injection |
| Git-Backed Artifacts | FR28-FR36 | Two-tier branching, tagging, namespaced paths, merge strategy |
| Course Correction | FR37-FR42 | Diff-based cascade, version preservation, affected-stage detection |
| Project Configuration | FR43-FR46 | Project setup, model routing config, constitution loading, feature flags |
| Audit & Cost Tracking | FR47-FR53 | Two-tier storage, cost ledger, full audit content, IP logging |
| Dashboard & Real-Time UI | FR54-FR58 | SignalR hub, event groups, streaming, activity status |
| GitHub Integration (Outbound) | FR59-60, FR63-64 | PR creation, commit push, feature-flagged — write operations with retry semantics |
| GitHub Integration (Inbound) | FR61-62 | PR comment/status monitoring — polling-based inbound event stream (webhooks in future version) |
| External Change Detection | FR65-FR71 | Polling (`git fetch` at 30s intervals), commit classification, path-based cascade triggers. Note: polling across many active workflows is a scalability concern; GitHub webhooks planned for future version to replace polling for GitHub-hosted repos |

**Non-Functional Requirements:**
24 NFRs that constrain architectural choices:

- **Performance (NFR1-6):** Sub-2s dashboard load, sub-500ms streaming latency, 500ms debounce on status, sub-1s SignalR push, sub-5s git ops
- **Security (NFR7-11):** API keys server-side only, agent tools scoped to worktree, path traversal blocking, no true sandboxing in MVP
- **Reliability (NFR12-15):** Checkpoint/resume after every tool call, git as source of truth for artifacts, no concurrent writes to same stage
- **Observability (NFR16-21):** Full LLM/tool call recording, structured logging with correlation IDs, OpenTelemetry traces, health check endpoint
- **Data Retention (NFR22-24):** Cost ledger permanent, audit content archivable after 90 days

**Scale & Complexity:**

- Primary domain: Full-stack web application (React SPA + ASP.NET Core)
- Complexity level: **High** — individually medium-complexity subsystems (workflow engine, agent execution with checkpoint/resume, git branching/tagging strategy, real-time SignalR hub) stack to high complexity overall, especially with solo developer build
- Estimated architectural components: ~12-15 major subsystems

### Source of Truth Model

- **Git** is the source of truth for **artifacts and their versions** — stage outputs, specs, code, and all versioned content live in git branches and tags
- **Database (PostgreSQL)** is the source of truth for **orchestration state, cost data, and audit records** — workflow state machine, gate decisions, audit timestamps, cost ledger entries, and execution history
- The DB is **not** fully reconstructible from git. Artifact *content* can be recovered from git; workflow state, cost, and audit data cannot.

### Technical Constraints & Dependencies

| Constraint | Source | Impact |
|-----------|--------|--------|
| .NET 10 + ASP.NET Core | PRD decision | Runtime, deployment, toolchain |
| Microsoft Agent Framework (RC) | PRD decision | Agent execution model, checkpoint/resume. **Highest-risk technical dependency** — checkpoint/resume API is a potential pivot point if RC→GA introduces breaking changes. Fallback: raw Anthropic.SDK + custom state machine. |
| React 19 SPA (Mantine 7.x) | Architecture decision — replaces PRD's Bootstrap + Blueprint JS | Frontend framework, single UI component library (layout + data-dense components). CSS Modules, no runtime styling overhead. |
| PostgreSQL 16 + EF Core | PRD decision | Data layer, JSONB for flexible config |
| SignalR | PRD decision | All real-time communication |
| IChatClient abstraction | PRD decision | Multi-model routing (Claude, GPT, Ollama) |
| Single binary deployment | PRD requirement | React build in wwwroot/, SPA fallback routing |
| No auth in MVP | PRD scoping | ICurrentUser interface with hardcoded admin + IP logging. **Load-bearing abstraction from day one** — every API endpoint, audit record, and SignalR connection references a user through this interface. Clean swap to OIDC later requires the abstraction to be correct now. |
| Feature flags via config | PRD requirement | GitHub integration toggle, future integration toggles |

### Cross-Cutting Concerns Identified

1. **Real-time event distribution** — SignalR hub with group-based subscriptions touches every page and component. Agent streaming, activity status, dashboard updates, gate notifications all flow through this.

2. **Audit & cost tracking** — Every LLM call, tool invocation, and state change must be recorded. Two-tier storage (permanent cost ledger + archivable full content). Correlation IDs on every log line.

3. **Git operations** — Branch creation, tagging, merging, diffing, commit classification. Used by workflow engine, course correction, artifact management, external change detection, and audit. Central git service needed.

4. **Error recovery & checkpoint** — Agent Framework checkpoints after every tool call. Crash recovery resumes from checkpoint. Failed stages recoverable. Git tags are the durable artifact snapshots. **Depends on Agent Framework RC behavior — highest-risk dependency.**

5. **Feature flags** — Control GitHub integration, future MCP tools, notification channels. Environment variables or config file. Must be checkable at both API and UI layers.

6. **Workspace scoping** — Agent tools must be sandboxed to project worktree. Path traversal blocking. Bash commands scoped to workspace directory. Security boundary for all agent operations.

7. **Cascade decision model** — Course correction (FR37-42) is not just a feature but a cross-cutting pattern that touches the workflow engine (state transitions), git service (diff between tags), agent execution (re-run with context), state machine (go-back transitions), and frontend (cascade decision cards). The diff-based update flow is the core differentiator and must be architecturally first-class.

8. **Workflow state machine** — State transitions (created → running → gate → approved → next stage, plus pause/resume/abandon/fail/retry) ripple across the engine, database, SignalR hub, dashboard UI, and audit system. Getting the state machine wrong ripples everywhere. Must be defined explicitly and shared across backend and frontend.

## Starter Template Evaluation

### Primary Technology Domain

Full-stack web application: ASP.NET Core 10 backend + React 19 TypeScript SPA frontend, based on project requirements analysis.

### Starter Options Considered

**Option A: `dotnet new react` (Official VS Template)** — Rejected. Opinionated .esproj structure and SpaProxy middleware conflicts with custom SignalR/API patterns needed for Antiphon's real-time architecture.

**Option B: ServiceStack `react-spa` Template** — Rejected. Brings ServiceStack framework dependency, wrong architectural direction.

**Option C: Manual Composition** — Selected. Two independent projects in a monorepo, giving full control over both backend and frontend with no framework opinions to fight.

### UI Framework Decision

**Evaluated:** Blueprint JS 6.x, Ant Design 5.x, Mantine 7.x, shadcn/ui + Radix

**Selected: Mantine 7.x** — Replaces the originally-proposed Bootstrap + Blueprint JS combination.

**Rationale:**
- Replaces both Bootstrap (layout/grid) and Blueprint (data-dense components) with a single library
- TypeScript-first architecture matches the frontend language choice
- CSS Modules by default — no runtime styling overhead, important for real-time streaming performance
- Full React 19 support (Blueprint JS React 19 support still in progress)
- 120+ components and 70+ hooks covering panels, modals, tables, code highlight, notifications, split views
- Active community with strong documentation and Discord support
- Eliminates risk of carrying two UI libraries with potential style conflicts

**Trade-off acknowledged:** Blueprint JS has more polished desktop-specific components (EditableText, HotkeysProvider, PanelStack). Some equivalent Mantine components may need light customization to match the same desktop-app feel.

### Frontend State Management Architecture

Antiphon has two distinct data patterns that require different state management strategies:

**1. Server State — TanStack Query (React Query)**
Manages all REST API data: workflow lists, stage configs, audit records, project settings, artifact content.

- Caching, background refetching, stale-while-revalidate, request deduplication
- Optimistic updates for gate actions (approve/reject)
- Suspense integration (`suspense: true`) for clean loading boundaries
- **SignalR → query invalidation bridge:** SignalR events (e.g., "workflow status changed") trigger `queryClient.invalidateQueries()` to refetch fresh server state. Clean separation between real-time push notifications and authoritative server data.
- DevTools included for debugging cache state

**2. Client State — Zustand**
Manages UI-only and real-time state that doesn't come from REST APIs:

- SignalR connection status and active group subscriptions
- Agent streaming text buffer (accumulating `AgentTextDelta` events before render)
- UI state: active panel, conversation/gate mode toggle, selected tab, scroll position
- User preferences: dashboard filters, sort order
- Selector-based subscriptions for surgical re-renders — critical when real-time updates hit multiple components simultaneously

**3. Async Loading — React 19 Suspense**
Scoped to fetch-and-resolve patterns, not real-time streams:

- Route-level boundaries: skeleton while WorkflowDetailPage loads
- Panel-level boundaries: Outputs, Conversation, Stage Info tabs load independently
- Artifact rendering: markdown rendering wrapped in Suspense for heavy content
- Integrated with TanStack Query's `suspense: true` for automatic suspend/resolve
- **Not used for:** agent streaming (continuous push), SignalR-driven updates (event-driven), dashboard cards (render immediately from TanStack Query cache)

### Selected Starter: Manual Composition

**Initialization Commands:**

```bash
# Backend (.NET 10 LTS)
dotnet new webapi -n Antiphon.Server --framework net10.0

# Frontend (Vite + React 19 + TypeScript)
npm create vite@latest antiphon-client -- --template react-ts
cd antiphon-client
npm install @mantine/core @mantine/hooks @mantine/notifications @mantine/code-highlight \
  @tanstack/react-query @tanstack/react-query-devtools \
  zustand \
  react-router-dom @microsoft/signalr react-icons
```

**Architectural Decisions Provided by Starter:**

**Language & Runtime:**
- Backend: C# on .NET 10 (LTS, GA Nov 2025)
- Frontend: TypeScript (strict) on React 19
- Both fully production-ready, long-term supported

**UI Framework:**
- Mantine 7.x — single component library for layout, data-dense components, and interaction patterns
- react-icons — icon library (multi-source)
- CSS Modules — zero runtime styling overhead

**State Management:**
- TanStack Query — server state (REST data, caching, invalidation from SignalR events)
- Zustand — client state (UI state, SignalR status, streaming buffer)
- React 19 Suspense — async loading boundaries (route-level, panel-level, artifact rendering)

**Build Tooling:**
- Vite 6.x with SWC — fast HMR in dev, optimized production builds
- `dotnet publish` produces single binary with React build in `wwwroot/`

**Testing Framework:**
- Backend: xUnit + testcontainers (PostgreSQL)
- Frontend: Vitest + React Testing Library

**Code Organization:**
- Monorepo: `/server` (ASP.NET Core) + `/client` (Vite React)
- Single `dotnet publish` produces combined deployable binary

**Development Experience:**
- `dotnet run` serves API + SignalR hub
- `npm run dev` serves React with Vite HMR
- `vite.config.ts` proxy forwards `/api/*` and `/hubs/*` to backend
- TanStack Query DevTools for cache inspection during development

**Note:** Project initialization using these commands should be the first implementation story.

## Core Architectural Decisions

### Decision Priority Analysis

**Critical Decisions (Block Implementation):**
- Data modeling approach (relational core + JSONB config/audit)
- API pattern (Minimal APIs + RFC 9457 Problem Details)
- Real-time architecture (single SignalR hub + groups)
- Frontend component architecture (pages/features/shared layers)
- Markdown rendering stack (react-markdown + Mermaid + diff viewer)

**Important Decisions (Shape Architecture):**
- Logging (Serilog with correlation enrichers)
- Caching (IMemoryCache, no Redis)
- Migration strategy (EF Core CLI-generated, auto-apply on startup)
- Error handling (full details to frontend, no security filtering in MVP)
- Event bus abstraction over SignalR for testability

**Deferred Decisions (Post-MVP):**
- API versioning (not needed while single client/server deployment)
- Redis/distributed cache (only if scaling beyond single instance)
- Containerized agent sandboxing (MVP uses worktree scoping only)
- Error detail filtering (add config flag to suppress stack traces when hardening for team)

### Data Architecture

| Decision | Choice | Rationale |
|----------|--------|-----------|
| ORM | EF Core (PostgreSQL 16) | Greenfield .NET standard. JSONB column support. |
| Modeling approach | Code-first | Clean domain model, migrations track schema evolution |
| Migration creation | EF Core CLI (`dotnet ef migrations add`) | Never auto-generated. Deliberate, reviewable migration files. |
| Migration application | `context.Database.Migrate()` on startup | Auto-apply safe for single-tenant self-hosted. PRD requirement. |
| Database seeding | Runs after migrations, idempotent | Seeds default admin user, bundled BMAD workflow templates, and default model routing config on first startup. Idempotent — safe to run on every startup. |
| Relational data | Workflows, stages, gate decisions, cost ledger, users | Stable schemas, need indexing and FK relationships |
| JSONB data | Workflow YAML definitions, model routing config, feature flags, agent tool configs | Schema varies per instance/template, flexible by nature |
| JSONB content blobs | Full audit content (prompts, responses, tool call I/O) | Write-heavy, query-light, archivable after retention period |
| Caching | `IMemoryCache` — cache **parsed YAML objects**, not raw strings | Sufficient for single-tenant <10 concurrent users. Hot data: workflow definitions (parsed), project configs, feature flags. No Redis needed. Avoids re-parsing YAML on every cache hit. |

### Authentication & Security

| Decision | Choice | Rationale |
|----------|--------|-----------|
| MVP auth | `ICurrentUser` interface → hardcoded admin + client IP logging | Load-bearing abstraction for future OIDC swap. No login flow. |
| Network security | Bind to internal network (not localhost) | Team can access for early testing. No static API key needed. |
| API key storage | Server-side only (env vars) | Never in frontend, agent context, or audit logs |
| Agent sandboxing | Worktree-scoped file tools + path traversal blocking | Bash tool scoped to working directory, not containerized in MVP |
| Future auth | OIDC via `ICurrentUser` implementation swap | Zero refactoring to API, audit, or workflow code |

### API & Communication Patterns

| Decision | Choice | Rationale |
|----------|--------|-----------|
| API style | ASP.NET Core Minimal APIs (REST) | Clean, performant, no controller ceremony |
| API versioning | None for MVP | Single client/server binary, no version skew possible |
| Error handling | RFC 9457 Problem Details + custom global exception middleware | Standard format. Middleware catches all exceptions and returns full stack trace, inner exceptions, correlation IDs. No security filtering in MVP — fast iteration over safety. |
| Error detail level | Full details to frontend (stack traces, logs, sensitive data) | Internal tool, speed of debugging prioritized. Add config flag to suppress when hardening for team deployment. |
| Custom exceptions | `HttpException` base class + small hierarchy: `NotFoundException`, `ConflictException`, `ValidationException` (with `Dictionary<string, string[]> Errors`), `ForbiddenException` | Throw from anywhere in the .NET stack to return specific HTTP errors. Middleware maps each type to the right status code and Problem Details shape. `throw new NotFoundException("Workflow", id)` reads cleaner than status codes. Unit tests assert `Assert.Throws<NotFoundException>()` directly. Keep hierarchy to 4-5 classes max. |
| SignalR architecture | Single hub (`/hubs/antiphon`) with group-based routing | One connection per browser tab. Clients join/leave groups by navigation context (`workflow-{id}`, `dashboard`). Targeted delivery — no broadcast-everything. |
| SignalR groups | `workflow-{id}` (streaming + activity + gate events), `dashboard` (status changes + progress) | Clients only receive events for what they're viewing. Join on navigate, leave on navigate away. |
| Event bus abstraction | `IEventBus` interface implemented by SignalR hub | Services push events through `IEventBus`, never call `Clients.Group(...).SendAsync(...)` directly. Unit tests mock the interface. Integration tests hit the real hub. Decouples business logic from SignalR. |
| Correlation IDs | `AsyncLocal` via Serilog `LogContext.PushProperty()` middleware | workflowId, stageId, executionId set in middleware, available on every log line without parameter pollution through service methods. |

### Frontend Architecture

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Component layers | Pages → Features → Shared | Pages are route-level with Suspense boundaries. Features are domain composites. Shared are reusable across domains. |
| Folder structure | `src/features/{domain}/` with co-located components, hooks, queries | Flat feature folders. No deep nesting. Each domain self-contained. |
| Routing | React Router, flat structure | `/` dashboard, `/workflow/:id` detail, `/settings/*` admin |
| Markdown rendering | `react-markdown` + `remark-gfm` + `rehype-highlight` + `rehype-raw` | Full GFM support, syntax highlighting, raw HTML for rich artifacts |
| Mermaid diagrams | Custom code block renderer → lazy-loaded `<MermaidDiagram>` component using `mermaid.render()` | Avoids rehype-mermaid re-render bugs. Lazy-loads ~200KB Mermaid library only when diagram present. React stays in DOM control. Accepts optional `renderFn` prop for test mock injection. |
| Artifact diff strategy | Diff **raw markdown source**, not rendered HTML | Shows what the agent actually changed. Source diffing is simpler, more useful, and library-agnostic. |
| Diff viewing | `<ArtifactDiffViewer>` component (library TBD — e.g., `react-diff-viewer`) | Side-by-side or unified diff for artifact version comparison. Operates on raw markdown source. |
| Key components | `<ArtifactViewer>` (markdown + Mermaid), `<PromptBar>` (dual-mode input), `<PipelineIndicator>` (mini stage progress), `<CascadeDecisionCard>`, `<AgentActivityStatus>`, `<ConversationTimeline>` | Domain-specific composites identified from UX spec |

### Infrastructure & Deployment

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Deployment | Single binary: `dotnet publish` bundles React build in `wwwroot/` | PRD requirement. One artifact to deploy. |
| Containerization | Docker Compose: Antiphon + PostgreSQL | Simple orchestration for self-hosted deployment |
| Dev environment | Docker Compose for PostgreSQL only. Antiphon server via `dotnet run`, React via `npm run dev` | Fast iteration with Vite HMR + hot reload |
| Logging | Serilog with `AsyncLocal` correlation enrichers | Structured logging with workflowId, stageId, executionId on every log line. Console + file sinks. OpenTelemetry sink for traces. |
| Observability | OpenTelemetry traces via Microsoft.Extensions.AI middleware | All LLM calls traced with model, tokens, cost, duration |
| Health checks | `/health` endpoint | Reports: DB connectivity, LLM provider reachability, git repo accessibility, workspace disk space |
| Feature flags | Config file + environment variables | Controls GitHub integration, future MCP tools, notification channels. Checkable at API and UI layers. |
| Test database | Shared PostgreSQL testcontainer + transaction rollback per test | One testcontainer spun up per test suite. Each test runs in a transaction that rolls back. Fast, isolated enough for single-tenant. |

### Decision Impact Analysis

**Implementation Sequence:**
1. Project scaffold (monorepo structure, dotnet webapi + vite react-ts)
2. Database schema + EF Core migrations + database seeding + Serilog setup
3. `HttpException` hierarchy + global exception middleware + `IEventBus` abstraction
4. Core domain models (Workflow, Stage, Gate) + Minimal API endpoints + TanStack Query + **audit middleware skeleton** (wire interception points early, storage comes later)
5. Workflow engine + state machine + **mock executor** (simulates agent behavior for testing without Agent Framework dependency)
6. Agent execution integration + streaming (swap mock executor for real Agent Framework)
7. Git operations service
8. Dashboard + WorkflowDetailPage UI
9. Gate review + course correction flows
10. Audit + cost tracking (full two-tier storage, building on skeleton from step 4)
11. GitHub integration (feature-flagged)
12. External change detection

**Critical Path Risk:** Steps 5-7 (workflow engine + agent execution + git service) are deeply coupled and represent the highest-risk delivery block. The **mock executor** (step 5) de-risks this by enabling workflow engine and git service development/testing without dependency on the Agent Framework RC.

**Cross-Component Dependencies:**
- SignalR hub implements `IEventBus` — services depend on the interface, not the hub directly
- Workflow engine depends on: git service (branching/tagging), agent execution (stage runners), state machine (transitions)
- Course correction depends on: git service (diff between tags), workflow engine (re-run), state machine (go-back transitions)
- Audit middleware skeleton wired in step 4 — intercepts LLM calls, tool calls, gate decisions, state changes from the start. Two-tier storage added in step 10.
- Frontend TanStack Query depends on: SignalR events (via `IEventBus`) for cache invalidation triggers

## Implementation Patterns & Consistency Rules

### Naming Patterns

**Database (EF Core + PostgreSQL):**

| Element | Convention | Example |
|---------|-----------|---------|
| Tables | PascalCase plural | `Workflows`, `Stages`, `GateDecisions`, `CostLedgerEntries` |
| Columns | PascalCase | `WorkflowId`, `CreatedAt`, `StageExecutionId` |
| Foreign keys | `{Entity}Id` | `WorkflowId`, `StageId` |
| Indexes | `IX_{Table}_{Columns}` | `IX_Stages_WorkflowId` |

**API (Minimal APIs):**

| Element | Convention | Example |
|---------|-----------|---------|
| Endpoints | lowercase plural, kebab-case | `/api/workflows`, `/api/cost-ledger` |
| Nested resources | hierarchical | `/api/workflows/{id}/stages` |
| Route params | `{id}` | ASP.NET Core convention |
| Query params | camelCase | `?sortBy=createdAt&status=active` |
| JSON serialization | camelCase | C# `WorkflowId` → JSON `workflowId` |

**C# Code:**

| Element | Convention | Example |
|---------|-----------|---------|
| Classes/interfaces | PascalCase | `WorkflowEngine`, `IChatClient` |
| Methods | PascalCase + `Async` suffix | `ExecuteStageAsync()`, `ApproveGateAsync()` |
| Properties | PascalCase | `WorkflowId`, `CreatedAt` |
| Private fields | `_camelCase` | `_workflowRepository`, `_eventBus` |
| Constants | PascalCase | `DefaultPollingInterval`, `MaxRetryCount` |

**TypeScript/React Code:**

| Element | Convention | Example |
|---------|-----------|---------|
| Components | PascalCase file + export | `WorkflowCard.tsx` |
| Hooks | `use` prefix, camelCase file | `useWorkflow.ts`, `useSignalR.ts` |
| Utilities | camelCase file | `formatDate.ts`, `parseMarkdown.ts` |
| Constants | UPPER_SNAKE_CASE | `SIGNALR_HUB_URL`, `DEFAULT_POLLING_INTERVAL` |
| Zustand stores | `use{Name}Store` | `useConnectionStore`, `useStreamingStore` |
| Query keys | Tuple arrays | `['workflows']`, `['workflow', id, 'stages']` |

### Structure Patterns

**Backend:**
```
server/
├── Domain/           # Entities, value objects, enums (ZERO infrastructure dependencies)
├── Application/      # Services, typed settings classes
├── Infrastructure/   # EF Core, SignalR hub, git, LLM clients, caching decorator
├── Api/              # Minimal API endpoints, middleware, filters
├── Migrations/       # EF Core migrations (CLI-generated only)
└── Program.cs        # Composition root
```

**Layer dependency rule (HARD ENFORCEMENT):**
- `Domain/` has **zero dependencies** on infrastructure — no EF Core, no SignalR, no HTTP, no external packages. Contains entities, value objects, enums, and pure domain logic (e.g., workflow state machine transition rules).
- `Application/` depends on `Domain/` and interfaces defined in `Application/` (e.g., `IGitService`, `IEventBus`). Implementations live in `Infrastructure/`.
- `Infrastructure/` depends on `Domain/` and `Application/`. Contains all external I/O implementations.
- `Api/` depends on all layers. Composition root wires everything via DI in `Program.cs`.
- **Violation example:** If an agent puts `AppDbContext` or `[JsonProperty]` in `Domain/`, the architecture is broken.

**Frontend:**
```
client/src/
├── features/
│   ├── dashboard/     # DashboardPage, WorkflowCard, filters
│   ├── workflow/      # WorkflowDetailPage, ConversationTimeline, AgentActivityStatus
│   ├── gate/          # GateReviewPanel, CascadeDecisionCard, PromptBar
│   ├── artifact/      # ArtifactViewer, ArtifactDiffViewer, MermaidDiagram
│   └── settings/      # SettingsPage, TemplateManager, ProviderConfig
├── shared/            # PipelineIndicator, layout, shared hooks
├── stores/            # Zustand stores
├── api/               # TanStack Query hooks + API client functions
├── hooks/             # Shared hooks (useSignalR, useAuth)
└── App.tsx            # Router, providers, Suspense boundaries
```

**Test co-location:**
- Backend: `tests/` directory mirroring `server/` structure
- Frontend: co-located `*.test.tsx` next to source files

### Interface & Abstraction Rules

**Concrete classes by default. Interfaces only when you need a seam:**

| Needs interface | Why | Example |
|----------------|-----|---------|
| SignalR event push | Mock in unit tests, decouple from hub | `IEventBus` |
| LLM calls | Mock in unit tests, swap providers, caching decorator | `IChatClient` (Microsoft.Extensions.AI) |
| Git operations | Mock in unit tests, complex external I/O | `IGitService` |
| Current user | Swap hardcoded admin → OIDC later | `ICurrentUser` |

| No interface needed | Why | Example |
|--------------------|-----|---------|
| Domain services | Just use the class directly | `WorkflowEngine`, `CascadeService` |
| Application services | Concrete class. If it's hard to test, the design is wrong. | `WorkflowService`, `AuditService` |
| Repositories | EF Core `DbContext` is the abstraction. No repository wrapper. | Use `AppDbContext` directly |
| DTOs / mappers | Pure functions, no I/O, trivially testable | `WorkflowDto`, `MapToDto()` |

**Anti-pattern:** `IWorkflowService` + `WorkflowService` where the interface has exactly one implementation and exists only because "best practice." Don't do this.

### Configuration Pattern

**All services consume configuration via `IOptions<TSettings>` with typed settings classes:**

```csharp
// Typed settings class
public class GitSettings
{
    public string DefaultBranch { get; set; } = "main";
    public int PollIntervalSeconds { get; set; } = 30;
    public string WorktreeBasePath { get; set; } = "/tmp/antiphon-worktrees";
}

// Bound in Program.cs
builder.Services.Configure<GitSettings>(builder.Configuration.GetSection("Git"));

// Injected in service
public class GitService : IGitService
{
    private readonly GitSettings _settings;
    public GitService(IOptions<GitSettings> settings) => _settings = settings.Value;
}
```

**Rule:** Never inject `IConfiguration` directly into services. Each feature gets a typed settings class (`GitSettings`, `LlmSettings`, `SignalRSettings`, `AuditSettings`). `Program.cs` is the only place that touches `IConfiguration`.

### LLM Call Caching (Test Infrastructure)

**Purpose:** Persistent record/replay cache for all LLM interactions, enabling deterministic E2E tests without API costs or network flakiness.

**Architecture:**

```
IChatClient (interface — Microsoft.Extensions.AI)
    ├── Production: AnthropicChatClient / OpenAiChatClient / OllamaChatClient
    └── Testing:    CachingChatClient (decorator)
                        ├── wraps real IChatClient
                        ├── normalizer strips volatile content before hashing
                        └── reads/writes .antiphon-cache/ directory
```

**Cache key:** Deterministic hash of normalized `(model, systemPrompt, messages, tools, temperature)`. The **normalizer function** strips volatile content (timestamps, absolute file paths, UUIDs, git SHAs) from messages before hashing, ensuring multi-turn agent conversations with filesystem interaction produce stable cache keys across runs.

**Cache value:** Full serialized LLM response (text content, tool calls, finish reason, token usage).

**Modes (configured via DI):**

| Mode | Behavior | Use case |
|------|----------|----------|
| `Record` | Call real LLM, store input→output, return response | Explicitly re-recording all golden responses |
| `RecordIfMissing` | Serve cached if available, call real LLM on miss, save new response | **Default for local dev and initial test runs.** Tests evolve naturally — new calls get recorded, existing stay cached. |
| `Replay` | Serve from cache only, fail on cache miss | **CI/CD only.** Guarantees zero API calls, zero cost, fully deterministic. |
| `PassThrough` | No caching, direct to LLM | Production |

**Storage:** File-based in `.antiphon-cache/` directory. One JSON file per cached call, named by content hash. Committed to repo or gitignored — team choice.

**Scope:** Covers text generation, agent multi-turn conversations (each turn cached independently), and embedding calls. Anything flowing through `IChatClient`.

### SignalR → Query Invalidation Mapping

**Contract between backend events and frontend cache management:**

| SignalR Event | Invalidates Query Keys | Trigger |
|--------------|----------------------|---------|
| `WorkflowStatusChanged` | `['workflows']`, `['workflow', id]` | Workflow created, paused, resumed, abandoned, completed |
| `StageCompleted` | `['workflow', id, 'stages']`, `['workflow', id]` | Stage execution finishes (success or failure) |
| `GateReady` | `['workflow', id]`, `['workflows']` | Stage output ready for review |
| `GateActioned` | `['workflow', id]`, `['workflows']` | Gate approved, rejected, or go-back initiated |
| `ArtifactUpdated` | `['workflow', id, 'artifacts']` | New artifact version created |
| `CascadeTriggered` | `['workflow', id, 'stages']`, `['workflow', id]` | Course correction cascade initiated |

**Implementation:** Single `useSignalRInvalidation` hook in `hooks/` that subscribes to all events and calls `queryClient.invalidateQueries()` with the mapped keys. All frontend features benefit automatically.

### Format Patterns

**API Responses:**

| Pattern | Format | Example |
|---------|--------|---------|
| Success (single) | Direct response body | `GET /api/workflows/{id}` → `{...}` |
| Success (list) | `{ items: [...], totalCount: N }` when paginated | `GET /api/workflows?page=1` |
| Error | RFC 9457 Problem Details via middleware | `{ type, title, status, detail, traceId, stackTrace }` |
| Dates | ISO 8601 UTC | `"2026-03-15T14:30:00Z"` |
| Nulls | Omit null fields | Don't send `"field": null` |

**SignalR Events:**

| Pattern | Format | Example |
|---------|--------|---------|
| Event names | PascalCase verb+noun | `AgentTextDelta`, `WorkflowStatusChanged`, `GateReady` |
| Payloads | Typed DTOs, camelCase JSON | `{ workflowId, stageId, text }` |
| Correlation | All events carry `workflowId` | For routing and logging |

### Process Patterns

**Backend service pattern (canonical example):**
```csharp
public class WorkflowEngine  // No interface — concrete class
{
    private readonly AppDbContext _db;          // EF Core directly — no repository wrapper
    private readonly IEventBus _eventBus;      // Interface — external I/O (SignalR)
    private readonly IChatClient _chatClient;  // Interface — external I/O (LLM)
    private readonly IGitService _gitService;  // Interface — external I/O (git)
    private readonly IOptions<WorkflowSettings> _settings; // Typed config

    // Constructor injection, private readonly fields
    // Every Async method takes CancellationToken as last parameter
    // Throws HttpException hierarchy for error cases
    // Pushes events via IEventBus, never SignalR directly
    // Never catches exceptions silently — let middleware handle
    // No static state — everything flows through DI
}
```

**CancellationToken rule (MUST):**
- Every `Async` method takes `CancellationToken` as its last parameter
- API layer passes `HttpContext.RequestAborted`
- Agent execution layer uses a linked token source that cancels on user-initiated stage abort
- Agents that omit `CancellationToken` from async methods are violating the pattern

**Frontend data fetching pattern:**
```typescript
// 1. API function in api/ folder
export const getWorkflow = (id: string) => api.get<Workflow>(`/workflows/${id}`);

// 2. TanStack Query hook in feature folder
export const useWorkflow = (id: string) =>
  useQuery({ queryKey: ['workflow', id], queryFn: () => getWorkflow(id) });

// 3. Component consumes hook
const { data: workflow } = useWorkflow(id);
```

**Error boundaries:**
- Route-level `<ErrorBoundary>` per page
- Feature-level for independent panels (Outputs, Conversation, Stage Info)
- Show Problem Details `title` + `detail` from API
- "Retry" button on every error boundary

**Loading states:**
- Suspense boundaries show Mantine `<Skeleton>` matching expected layout
- Never spinner without context — always skeleton
- Agent streaming shows content progressively (no skeleton)

### Test Layer Guidance

| Layer | What to test | What NOT to test | Tools |
|-------|-------------|-----------------|-------|
| **Unit** | Domain logic, `HttpException` mapping, pure utility functions, state machine transitions | Individual React components, trivial getters/setters | xUnit, Vitest |
| **Integration** | API endpoints → DB round-trips, service → `IEventBus` mock interactions, EF Core queries | External LLM calls (use mock), git operations (use mock) | xUnit + testcontainers, transaction rollback |
| **E2E** | Full workflow execution via browser: create → execute → gate → approve → next stage | Low-level unit logic (that's unit/integration tests) | Playwright (.NET) + WebApplicationFactory + `CachingChatClient` in `RecordIfMissing`/`Replay`, real DB via testcontainer |
| **Frontend** | Feature-level component tests with mocked API, user interaction flows, SignalR event handling | Snapshot tests, individual Mantine component rendering | Vitest + React Testing Library + MSW or TanStack Query test utils |

### Enforcement Guidelines

**All AI agents MUST:**
- Follow naming conventions exactly as specified (no creative variations)
- Use concrete classes by default — only introduce interfaces for external I/O seams (`IEventBus`, `IChatClient`, `IGitService`, `ICurrentUser`)
- Respect layer boundaries — `Domain/` has zero infrastructure dependencies
- Use `HttpException` hierarchy for all error responses — never return status codes manually
- Push events through `IEventBus` — never call SignalR hub directly from services
- Use EF Core `AppDbContext` directly — no repository wrapper pattern
- Use `IOptions<TSettings>` for configuration — never inject `IConfiguration` directly
- Include `CancellationToken` as last parameter on every `Async` method
- Create EF Core migrations via CLI only — never auto-generate
- Use TanStack Query for all REST data — never raw `fetch` + `useEffect`
- Use Zustand stores for client state — never React Context for frequently-changing state
- No static state anywhere — everything through DI. Only exception: `AsyncLocal` for Serilog correlation context
- Follow the SignalR → Query Invalidation Mapping table for cache invalidation

## Project Structure & Boundaries

### Complete Project Directory Structure

```
antiphon/
├── .github/
│   └── workflows/
│       └── ci.yml                          # CI pipeline (build, test, lint)
├── .antiphon-cache/                        # LLM record/replay cache (gitignored or committed)
├── docker-compose.yml                      # Production: Antiphon + PostgreSQL
├── docker-compose.dev.yml                  # Dev: PostgreSQL only
├── .gitignore
├── .editorconfig
├── appsettings.json.example                # Placeholder values showing required config structure (safe to commit)
├── Antiphon.sln                            # Solution file
│
├── docs/
│   ├── architecture.md                     # Graduated from planning-artifacts after approval
│   └── project-context.md                  # Project constitution: extracted conventions + enforcement rules, injected into agent system prompts (FR45)
│
├── server/
│   ├── Antiphon.Server.csproj
│   ├── Program.cs                          # Composition root: DI, middleware, endpoints
│   ├── appsettings.json                    # Base config (gitignored — contains API keys)
│   ├── appsettings.Development.json        # Dev overrides
│   │
│   ├── Domain/                             # ZERO infrastructure dependencies
│   │   ├── Entities/
│   │   │   ├── Workflow.cs
│   │   │   ├── Stage.cs
│   │   │   ├── GateDecision.cs
│   │   │   ├── StageExecution.cs
│   │   │   ├── AuditRecord.cs
│   │   │   ├── CostLedgerEntry.cs
│   │   │   ├── Project.cs
│   │   │   └── User.cs
│   │   ├── Enums/
│   │   │   ├── WorkflowStatus.cs           # Created, Running, Paused, GateWaiting, Completed, Failed, Abandoned
│   │   │   ├── StageStatus.cs              # Pending, Running, Completed, Failed
│   │   │   ├── GateAction.cs               # Approved, RejectedWithFeedback, GoBack
│   │   │   └── CascadeAction.cs            # UpdateFromDiff, Regenerate, KeepAsIs
│   │   ├── ValueObjects/
│   │   │   ├── WorkflowDefinition.cs       # Parsed YAML workflow structure
│   │   │   ├── ModelRouting.cs             # Stage → LLM model mapping
│   │   │   └── ArtifactVersion.cs          # Stage + version identifier
│   │   └── StateMachine/
│   │       └── WorkflowStateMachine.cs     # Pure state transition logic (no I/O)
│   │
│   ├── Application/
│   │   ├── Services/
│   │   │   ├── WorkflowEngine.cs           # Orchestrates workflow execution
│   │   │   ├── CascadeService.cs           # Diff-based cascade update logic
│   │   │   ├── AuditService.cs             # Audit recording (uses middleware skeleton)
│   │   │   └── CostTrackingService.cs      # Cost ledger management
│   │   ├── Interfaces/                     # Only for external I/O seams
│   │   │   ├── IEventBus.cs                # SignalR abstraction
│   │   │   ├── IGitService.cs              # Git operations abstraction
│   │   │   ├── ICurrentUser.cs             # Auth abstraction
│   │   │   └── IStageExecutor.cs           # Agent execution abstraction (swap AgentExecutor ↔ MockExecutor)
│   │   ├── Settings/                       # Typed IOptions<T> classes
│   │   │   ├── GitSettings.cs
│   │   │   ├── LlmSettings.cs
│   │   │   ├── SignalRSettings.cs
│   │   │   ├── AuditSettings.cs
│   │   │   └── GithubSettings.cs
│   │   ├── Dtos/                           # API response/request shapes
│   │   │   ├── WorkflowDto.cs
│   │   │   ├── StageDto.cs
│   │   │   ├── GateActionRequest.cs
│   │   │   ├── CascadeDecisionRequest.cs
│   │   │   └── CreateWorkflowRequest.cs
│   │   └── Exceptions/
│   │       ├── HttpException.cs            # Base class
│   │       ├── NotFoundException.cs
│   │       ├── ConflictException.cs
│   │       ├── ValidationException.cs      # With Dictionary<string, string[]> Errors
│   │       └── ForbiddenException.cs
│   │
│   ├── Infrastructure/
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs             # EF Core context
│   │   │   ├── Configurations/            # EF Core entity configs (fluent API)
│   │   │   │   ├── WorkflowConfiguration.cs
│   │   │   │   ├── StageConfiguration.cs
│   │   │   │   └── ...
│   │   │   └── Seeding/
│   │   │       └── DatabaseSeeder.cs       # Default admin, BMAD templates, model routing
│   │   ├── Git/
│   │   │   └── GitService.cs              # IGitService implementation (LibGit2Sharp or CLI)
│   │   ├── Agents/
│   │   │   ├── AgentExecutor.cs            # IStageExecutor: Microsoft Agent Framework integration
│   │   │   ├── MockExecutor.cs             # IStageExecutor: Mock agent for testing workflow engine
│   │   │   ├── ToolRegistry.cs             # Registers and discovers built-in tools for agent executor. Future MCP tool registration extends this.
│   │   │   ├── Tools/                     # Built-in agent tools
│   │   │   │   ├── FileReadTool.cs
│   │   │   │   ├── FileWriteTool.cs
│   │   │   │   ├── FileEditTool.cs
│   │   │   │   ├── BashTool.cs
│   │   │   │   ├── GlobTool.cs
│   │   │   │   ├── GrepTool.cs
│   │   │   │   └── GitTool.cs
│   │   │   └── CachingChatClient.cs       # Record/replay LLM cache decorator
│   │   ├── Realtime/
│   │   │   ├── AntiphonHub.cs             # SignalR hub
│   │   │   └── EventBus.cs                # IEventBus implementation → SignalR groups routing
│   │   ├── GitHub/
│   │   │   ├── GitHubService.cs            # PR creation, monitoring (feature-flagged)
│   │   │   └── GitHubWebhookHandler.cs     # Future: inbound webhook events
│   │   └── ExternalChanges/
│   │       └── ChangeDetectionService.cs   # Polls for external commits, triggers cascades
│   │
│   ├── Api/
│   │   ├── Endpoints/
│   │   │   ├── WorkflowEndpoints.cs        # /api/workflows
│   │   │   ├── StageEndpoints.cs           # /api/workflows/{id}/stages
│   │   │   ├── GateEndpoints.cs            # /api/workflows/{id}/gates (complex — extract handler classes if >50 lines)
│   │   │   ├── ArtifactEndpoints.cs        # /api/workflows/{id}/artifacts
│   │   │   ├── AuditEndpoints.cs           # /api/audit
│   │   │   ├── ProjectEndpoints.cs         # /api/projects
│   │   │   ├── SettingsEndpoints.cs        # /api/settings
│   │   │   └── HealthEndpoints.cs          # /health
│   │   └── Middleware/
│   │       ├── ExceptionMiddleware.cs      # Global HttpException → Problem Details
│   │       ├── CorrelationIdMiddleware.cs  # AsyncLocal Serilog enrichment
│   │       ├── AuditMiddleware.cs          # Audit interception skeleton
│   │       └── CurrentUserMiddleware.cs    # ICurrentUser resolution + IP logging
│   │
│   └── Migrations/                         # EF Core migrations (CLI-generated only)
│       └── (generated by dotnet ef migrations add)
│
├── client/
│   ├── package.json
│   ├── tsconfig.json
│   ├── vite.config.ts                      # Proxy /api/* and /hubs/* to backend
│   ├── index.html
│   │
│   └── src/
│       ├── App.tsx                         # Router, MantineProvider, QueryClientProvider, Suspense
│       ├── main.tsx                        # Entry point
│       │
│       ├── api/                            # API client + TanStack Query hooks
│       │   ├── client.ts                   # Axios/fetch wrapper with base URL, error parsing
│       │   ├── workflows.ts               # getWorkflows, getWorkflow, createWorkflow, etc.
│       │   ├── stages.ts                  # getStages, etc.
│       │   ├── gates.ts                   # approveGate, rejectGate, goBack, etc.
│       │   ├── artifacts.ts               # getArtifact, getArtifactDiff, etc.
│       │   ├── audit.ts                   # getAuditHistory, getCostSummary, etc.
│       │   └── settings.ts               # getSettings, updateSettings, etc.
│       │
│       ├── stores/                        # Zustand stores
│       │   ├── connectionStore.ts         # SignalR connection status, group subscriptions
│       │   ├── streamingStore.ts          # Agent text buffer, activity status
│       │   └── uiStore.ts                # Active panel, mode toggle, preferences
│       │
│       ├── hooks/                         # Shared hooks
│       │   ├── useSignalR.ts              # Hub connection lifecycle
│       │   ├── useSignalRInvalidation.ts  # Event → query invalidation mapping
│       │   └── useCurrentUser.ts          # User context
│       │
│       ├── features/
│       │   ├── dashboard/
│       │   │   ├── DashboardPage.tsx
│       │   │   ├── WorkflowCard.tsx
│       │   │   ├── WorkflowCard.test.tsx
│       │   │   ├── DashboardFilters.tsx
│       │   │   └── EmptyState.tsx
│       │   │
│       │   ├── workflow/
│       │   │   ├── WorkflowDetailPage.tsx
│       │   │   ├── ConversationTimeline.tsx
│       │   │   ├── ConversationTimeline.test.tsx
│       │   │   ├── AgentActivityStatus.tsx
│       │   │   ├── StageInfoPanel.tsx
│       │   │   └── OutputsPanel.tsx
│       │   │
│       │   ├── gate/
│       │   │   ├── GateReviewPanel.tsx
│       │   │   ├── GateReviewPanel.test.tsx
│       │   │   ├── CascadeDecisionCard.tsx
│       │   │   ├── PromptBar.tsx           # Dual-mode: Send to Agent / Add Comment
│       │   │   └── GateActionBar.tsx       # Approve / Reject / Go Back buttons
│       │   │
│       │   ├── artifact/
│       │   │   ├── ArtifactViewer.tsx       # react-markdown + Mermaid
│       │   │   ├── ArtifactViewer.test.tsx
│       │   │   ├── ArtifactDiffViewer.tsx   # Raw markdown source diff
│       │   │   ├── MermaidDiagram.tsx       # Lazy-loaded, mermaid.render()
│       │   │   └── VersionHistory.tsx
│       │   │
│       │   └── settings/
│       │       ├── SettingsPage.tsx
│       │       ├── TemplateManager.tsx      # YAML workflow template CRUD
│       │       ├── ProviderConfig.tsx       # LLM provider + model routing config
│       │       └── ProjectConfig.tsx        # Project setup (repo URL, constitution)
│       │
│       ├── shared/
│       │   ├── PipelineIndicator.tsx        # Mini stage progress bar
│       │   ├── Layout.tsx                  # App shell, navbar, panels
│       │   ├── ErrorBoundary.tsx           # Problem Details display + retry
│       │   └── SkeletonLayouts.tsx         # Mantine Skeleton presets for Suspense
│       │
│       └── test/                           # Frontend test infrastructure
│           ├── setup.ts                    # Vitest setup (MantineProvider, QueryClient)
│           ├── mocks/
│           │   ├── handlers.ts             # MSW request handlers
│           │   └── server.ts               # MSW server setup
│           └── utils.ts                    # renderWithProviders helper
│
└── tests/                                  # Backend tests
    ├── Antiphon.Tests.csproj               # Unit + integration tests
    ├── Domain/
    │   └── StateMachine/
    │       └── WorkflowStateMachineTests.cs
    ├── Application/
    │   ├── WorkflowEngineTests.cs
    │   └── CascadeServiceTests.cs
    ├── Api/
    │   ├── Endpoints/
    │   │   └── WorkflowEndpointsTests.cs   # Integration: API → DB via testcontainer
    │   └── Middleware/
    │       ├── ExceptionMiddlewareTests.cs
    │       ├── CorrelationIdMiddlewareTests.cs
    │       ├── AuditMiddlewareTests.cs
    │       └── CurrentUserMiddlewareTests.cs
    ├── Infrastructure/
    │   └── CachingChatClientTests.cs
    ├── TestHelpers/
    │   ├── TestDbFixture.cs                # Shared testcontainer + transaction rollback
    │   └── MockEventBus.cs
    │
    └── E2E/                                # Playwright browser-based E2E tests
        ├── Antiphon.E2E.csproj             # Separate project (Playwright .NET)
        ├── Fixtures/
        │   ├── AntiphonAppFixture.cs       # WebApplicationFactory + CachingChatClient + optional Vite dev server
        │   └── PlaywrightFixture.cs        # Browser lifecycle (chromium headless)
        ├── PageObjects/
        │   ├── DashboardPage.cs
        │   ├── WorkflowDetailPage.cs
        │   └── SettingsPage.cs
        ├── HappyPath/
        │   └── FullWorkflowTests.cs        # Create → execute → gate → approve via browser
        ├── CourseCorrection/
        │   └── CascadeUpdateTests.cs       # Go-back, diff-based update via browser
        └── EdgeCases/
            └── FailureRecoveryTests.cs     # Agent failure, checkpoint resume, retry via browser
```

### Architectural Boundaries

**API Boundary (server/Api/):**
All external access flows through Minimal API endpoints. No direct service or DbContext access from outside the API layer. Middleware pipeline: `CorrelationId → CurrentUser → Audit → ExceptionHandler → Endpoint`.

**Domain Boundary (server/Domain/):**
Zero infrastructure dependencies. Pure C# — no EF Core attributes, no JSON attributes, no HTTP concepts. Contains entities, enums, value objects, and the workflow state machine (pure transition logic). If it needs `using Microsoft.EntityFrameworkCore`, it doesn't belong here.

**Application Boundary (server/Application/):**
Business logic orchestration. Depends on Domain and interfaces. Services are concrete classes (no unnecessary interfaces). Defines `IEventBus`, `IGitService`, `ICurrentUser`, `IStageExecutor` — implemented by Infrastructure.

**Infrastructure Boundary (server/Infrastructure/):**
All external I/O: database, SignalR, git, LLM providers, GitHub API. Implements interfaces from Application. Contains the `CachingChatClient` decorator and `MockExecutor` for test infrastructure. `ToolRegistry` manages built-in tools and future MCP tool registration.

**Frontend Data Boundary (client/src/api/):**
All REST communication centralized here. Components never call `fetch` directly. TanStack Query hooks wrap every API call. SignalR events trigger query invalidation via `useSignalRInvalidation` hook.

**Frontend State Boundary (client/src/stores/):**
Zustand stores own client-only state. Components read via selectors. No React Context for frequently-changing state.

**Endpoint Complexity Guidance:**
Endpoint files contain route definitions and handler logic. For simple CRUD this is fine. If a handler exceeds ~50 lines (e.g., `GateEndpoints.cs` with approve, reject, go-back, cascade), extract to handler classes in the same directory. Not a hard rule — guidance to prevent bloated files.

### FR Category → Structure Mapping

| FR Category | Backend Location | Frontend Location |
|------------|-----------------|-------------------|
| Workflow Management (FR1-6) | `Application/Services/WorkflowEngine.cs`, `Domain/StateMachine/` | `features/dashboard/`, `features/workflow/` |
| Workflow Definition (FR7-10) | `Domain/ValueObjects/WorkflowDefinition.cs`, `Infrastructure/Data/Seeding/` | `features/settings/TemplateManager.tsx` |
| AI Agent Execution (FR11-20) | `Infrastructure/Agents/`, `Application/Interfaces/IStageExecutor.cs` | `features/workflow/AgentActivityStatus.tsx`, `stores/streamingStore.ts` |
| Approval Gates (FR21-27) | `Application/Services/WorkflowEngine.cs`, `Api/Endpoints/GateEndpoints.cs` | `features/gate/` |
| Git-Backed Artifacts (FR28-36) | `Infrastructure/Git/GitService.cs` | `features/artifact/` |
| Course Correction (FR37-42) | `Application/Services/CascadeService.cs`, `Infrastructure/Git/` | `features/gate/CascadeDecisionCard.tsx` |
| Project Configuration (FR43-46) | `Api/Endpoints/ProjectEndpoints.cs`, `Api/Endpoints/SettingsEndpoints.cs` | `features/settings/` |
| Audit & Cost (FR47-53) | `Application/Services/AuditService.cs`, `Api/Middleware/AuditMiddleware.cs` | `api/audit.ts` |
| Dashboard & Real-Time (FR54-58) | `Infrastructure/Realtime/AntiphonHub.cs` | `features/dashboard/`, `hooks/useSignalR.ts` |
| GitHub Integration (FR59-64) | `Infrastructure/GitHub/` | — (backend-driven, feature-flagged) |
| External Changes (FR65-71) | `Infrastructure/ExternalChanges/` | — (backend-driven, pushes via IEventBus) |

### Cross-Cutting Concerns → Location

| Concern | Backend | Frontend |
|---------|---------|----------|
| Real-time events | `Infrastructure/Realtime/`, `Application/Interfaces/IEventBus.cs` | `hooks/useSignalR.ts`, `hooks/useSignalRInvalidation.ts`, `stores/connectionStore.ts` |
| Audit | `Api/Middleware/AuditMiddleware.cs`, `Application/Services/AuditService.cs` | — |
| Error handling | `Api/Middleware/ExceptionMiddleware.cs`, `Application/Exceptions/` | `shared/ErrorBoundary.tsx` |
| Auth | `Api/Middleware/CurrentUserMiddleware.cs`, `Application/Interfaces/ICurrentUser.cs` | `hooks/useCurrentUser.ts` |
| Correlation IDs | `Api/Middleware/CorrelationIdMiddleware.cs` | — (received in error responses) |
| Feature flags | `appsettings.json`, `Application/Settings/` | `api/settings.ts` |
| Project constitution | `docs/project-context.md` → loaded by agent executor into system prompts (FR45) | — |

### E2E Test Architecture

**Stack:** Playwright (.NET) + WebApplicationFactory + CachingChatClient

**`AntiphonAppFixture` responsibilities:**
- Spins up `WebApplicationFactory<Program>` with test config
- Wires testcontainer PostgreSQL for real DB
- Registers `CachingChatClient` in `RecordIfMissing` (local) or `Replay` (CI) mode
- Optionally starts Vite dev server (`npm run dev`) for frontend, or serves pre-built static files from `wwwroot/`
- Exposes base URL for Playwright to connect to

**`PlaywrightFixture` responsibilities:**
- Manages chromium headless browser lifecycle
- Shared across test classes for performance

**Page Object Model:** Each major page gets a page object (`DashboardPage.cs`, `WorkflowDetailPage.cs`) encapsulating selectors and interactions. Tests read like user stories.

**Full stack tested:** Browser → React → SignalR + REST → ASP.NET Core → DB + cached LLM → Git

**Frontend serving strategy:**
- `UsePrebuiltFrontend = true` (CI) — runs `npm run build` beforehand, serves static files from `wwwroot/`. Fast, deterministic.
- `UsePrebuiltFrontend = false` (local dev) — starts Vite dev server as child process, waits for ready, Playwright connects. Tests the dev experience.

## Architecture Validation Results

### Coherence Validation

**Decision Compatibility:**
All technology choices verified compatible. .NET 10 (GA) + EF Core + PostgreSQL 16 — standard stack. React 19 + Mantine 7.x — supported. TanStack Query + Zustand + React Router — independent, no conflicts. SignalR + Vite proxy — standard pattern. Serilog + OpenTelemetry — compatible via OTel sink. Playwright (.NET) + WebApplicationFactory — standard ASP.NET Core integration pattern. Microsoft Agent Framework (RC) risk isolated behind `IStageExecutor` and `IChatClient` interfaces.

**Pattern Consistency:**
Complete chains verified:
- Naming: PascalCase C# → camelCase JSON → camelCase TypeScript (consistent serialization)
- Errors: `HttpException` → ExceptionMiddleware → Problem Details → TanStack Query → ErrorBoundary (complete)
- Events: `IEventBus` → SignalR hub → groups → `useSignalRInvalidation` → query invalidation (complete)
- State: EF Core → REST API → TanStack Query cache + SignalR invalidation (coherent data flow)
- Execution: `IStageExecutor` → AgentExecutor/MockExecutor → IChatClient → CachingChatClient (complete testing chain)

**Structure Alignment:**
Project structure supports all decisions. Layer boundaries enforced (Domain zero dependencies). FR-to-structure mapping covers all 71 FRs. Cross-cutting concerns mapped to specific files.

No contradictions or conflicts found.

### Requirements Coverage Validation

**Functional Requirements (71 FRs):** All covered.

| FR Range | Status | Architecture Support |
|----------|--------|---------------------|
| FR1-6 (Workflow Mgmt) | Covered | WorkflowEngine + WorkflowStateMachine + YAML parsing |
| FR7-10 (Workflow Def) | Covered | WorkflowDefinition value object + DatabaseSeeder + sample BMAD template |
| FR11-20 (Agent Exec) | Covered | IStageExecutor + AgentExecutor + IChatClient + ToolRegistry + SignalR streaming |
| FR21-27 (Gates) | Covered | GateEndpoints + state machine transitions + PromptBar (dual-mode) + available transitions API |
| FR28-36 (Git Artifacts) | Covered | IGitService + GitService + namespaced branching/tagging |
| FR37-42 (Course Correction) | Covered | CascadeService + git diff + CascadeDecisionCard |
| FR43-46 (Project Config) | Covered | Settings/Project endpoints + IOptions<T> + feature flags |
| FR47-53 (Audit & Cost) | Covered | AuditMiddleware + AuditService + CostTrackingService + two-tier storage |
| FR54-58 (Dashboard & RT) | Covered | SignalR hub + groups + TanStack Query + Zustand streaming |
| FR59-64 (GitHub) | Covered | GitHubService (feature-flagged) + outbound/inbound split |
| FR65-71 (External Changes) | Covered | ChangeDetectionService + commit classification + path-based cascade |

**Non-Functional Requirements (24 NFRs):** All covered.

| NFR Range | Status | Architecture Support |
|-----------|--------|---------------------|
| NFR1-6 (Performance) | Covered | SignalR streaming <500ms, IMemoryCache, Vite optimized builds |
| NFR7-11 (Security) | Covered | Server-side API keys, worktree scoping, path traversal blocking, ICurrentUser + IP |
| NFR12-15 (Reliability) | Covered | Agent Framework checkpoints, IStageExecutor mock/real swap, git tags as snapshots |
| NFR16-21 (Observability) | Covered | Serilog + correlation IDs + OpenTelemetry + /health |
| NFR22-24 (Retention) | Covered | Two-tier: cost ledger permanent, audit content archivable (JSONB) |

**Known accepted trade-off:** NFR9 — agent bash tool not containerized in MVP. Worktree scoping + path traversal blocking only. Containerized sandboxing deferred to v1.1 hardening.

### v1.1 Extensibility Hooks

Architecture includes forward-looking extensibility points for post-MVP features:

| v1.1 Feature | Extensibility Hook | MVP Impact |
|--------------|-------------------|------------|
| AI-suggested actions at gates | `IStageExecutor` return type includes optional `SuggestedActions` field | Field exists but UI ignores it in MVP. v1.1 lights up frontend rendering. |
| MCP tool registration | `ToolRegistry` designed for dynamic tool registration | MVP registers built-in tools only. MCP adds external tool discovery. |
| AI workflow authoring | YAML schema designed to be human-editable AND machine-generatable | MVP: handcrafted YAML. v1.1: AI generates YAML from free-form descriptions. |
| Hierarchical knowledge | `docs/project-context.md` loaded into agent system prompts | MVP: single file. v1.1: pluggable knowledge sources (git repos, vector DB). |

### Design Principles Captured

**YAML Workflow Schema:**
The workflow YAML schema must balance two constraints: expressive enough for the workflow engine to handle stage graphs, model routing, gate configuration, and tool selection — but simple enough that a developer can handcraft a complete workflow definition in 10 minutes. A sample BMAD workflow template ships as a reference implementation in `Infrastructure/Data/Seeding/` and serves as the schema's design validation.

**State Machine API Contract:**
`WorkflowStateMachine` exposes both `CanTransition(from, to)` for validation and `GetAvailableTransitions(currentState)` for UI rendering. The API returns available transitions alongside workflow state. The frontend renders gate action buttons (Approve/Reject/GoBack) from this list — never hardcoded per-state. This ensures the state machine is the single source of truth for what actions are valid.

**DI and Layer References:**
`AppDbContext` lives in `Infrastructure/Data/` but is injected into Application services via constructor injection. Application does NOT have a project reference to Infrastructure. The composition root (`Program.cs`) in the Api layer wires all dependencies. This is standard Onion/Clean Architecture DI — agents must not create direct project references between Application and Infrastructure.

### Architecture Completeness Checklist

**Requirements Analysis**
- [x] Project context thoroughly analyzed (71 FRs, 24 NFRs)
- [x] Scale and complexity assessed (High — stacked subsystem complexity)
- [x] Technical constraints identified (Agent Framework RC highest risk)
- [x] Cross-cutting concerns mapped (8 concerns)
- [x] Source of truth model defined (git for artifacts, DB for orchestration)

**Architectural Decisions**
- [x] All critical decisions documented with verified versions
- [x] Technology stack fully specified (backend + frontend + testing)
- [x] Data architecture defined (relational + JSONB + two-tier audit)
- [x] API patterns established (Minimal APIs + Problem Details + HttpException)
- [x] Real-time architecture defined (single SignalR hub + groups + IEventBus)
- [x] Auth abstraction in place (ICurrentUser, load-bearing from day one)
- [x] LLM caching for tests (CachingChatClient with 4 modes)
- [x] v1.1 extensibility hooks baked in

**Implementation Patterns**
- [x] Naming conventions established (DB, API, C#, TypeScript)
- [x] Structure patterns defined (layers, folders, test co-location)
- [x] Interface rules defined (concrete by default, interfaces for I/O seams)
- [x] Configuration pattern defined (IOptions<T>, never raw IConfiguration)
- [x] Process patterns documented (service pattern, data fetching, error boundaries)
- [x] Test layer guidance specified (unit, integration, E2E, frontend)
- [x] SignalR → query invalidation mapping table defined
- [x] 13 enforcement rules documented

**Project Structure**
- [x] Complete directory tree with every file named
- [x] Layer boundaries explicitly defined and enforced
- [x] FR category → structure mapping complete
- [x] Cross-cutting concerns → location mapping complete
- [x] E2E test architecture defined (Playwright + WebApplicationFactory)
- [x] Frontend test infrastructure defined (Vitest + MSW)

### Architecture Readiness Assessment

**Overall Status:** READY FOR IMPLEMENTATION

**Confidence Level:** High

**Key Strengths:**
- Complete FR and NFR coverage with explicit mapping to project structure
- Clear layer boundaries with hard enforcement rules
- Comprehensive testing strategy across all layers including LLM call caching
- v1.1 extensibility hooks built into MVP architecture (no future rewrites)
- State machine as single source of truth for workflow transitions
- IEventBus abstraction decouples all business logic from SignalR
- Real-time data flow fully specified: SignalR → query invalidation mapping

**Areas for Future Enhancement:**
- Workflow YAML schema detailed specification (implementation story)
- Agent system prompt assembly order (implementation story)
- SignalR reconnection strategy (use built-in `withAutomaticReconnect()`)
- Mantine theme configuration (dark mode, color scheme)
- API pagination parameter standardization

### Implementation Handoff

**AI Agent Guidelines:**
- Follow all architectural decisions exactly as documented
- Use implementation patterns consistently across all components
- Respect project structure and layer boundaries
- Refer to this document for all architectural questions
- Load `docs/project-context.md` for enforcement rules (extracted from this document)
- Reference specific architecture sections in story acceptance criteria

**First Implementation Priority:**
1. Project scaffold (monorepo, `dotnet new webapi`, `npm create vite@latest`)
2. Create `docs/project-context.md` — extract enforcement guidelines from this architecture document so all subsequent agents have conventions in context from the start
3. Database schema + EF Core migrations + DatabaseSeeder + Serilog setup
4. `HttpException` hierarchy + global exception middleware + `IEventBus` abstraction
