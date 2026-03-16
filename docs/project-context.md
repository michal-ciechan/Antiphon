# Antiphon Project Context

This document is the project constitution. All AI coding agents and human developers MUST follow these conventions.

## Tech Stack

| Layer | Technology | Version |
|-------|-----------|---------|
| Backend runtime | .NET / ASP.NET Core | 10.0 LTS |
| Backend API pattern | Minimal APIs (REST) | — |
| Database | PostgreSQL | 16 |
| ORM | EF Core (code-first) | — |
| Frontend framework | React | 19.x |
| Build tool | Vite | 8.x |
| UI components | Mantine | 8.x |
| Server state | TanStack Query | 5.x |
| Client state | Zustand | 5.x |
| Routing | React Router | 7.x (single package) |
| Real-time | SignalR | — |
| Containerization | Docker Compose | — |

## Naming Conventions

### Database (EF Core + PostgreSQL)

| Element | Convention | Example |
|---------|-----------|---------|
| Tables | PascalCase plural | `Workflows`, `Stages`, `GateDecisions`, `CostLedgerEntries` |
| Columns | PascalCase | `WorkflowId`, `CreatedAt`, `StageExecutionId` |
| Foreign keys | `{Entity}Id` | `WorkflowId`, `StageId` |
| Indexes | `IX_{Table}_{Columns}` | `IX_Stages_WorkflowId` |

### API (Minimal APIs)

| Element | Convention | Example |
|---------|-----------|---------|
| Endpoints | lowercase plural, kebab-case | `/api/workflows`, `/api/cost-ledger` |
| Nested resources | hierarchical | `/api/workflows/{id}/stages` |
| Route params | `{id}` | ASP.NET Core convention |
| Query params | camelCase | `?sortBy=createdAt&status=active` |
| JSON serialization | camelCase | C# `WorkflowId` -> JSON `workflowId` |

### C# Code

| Element | Convention | Example |
|---------|-----------|---------|
| Classes/interfaces | PascalCase | `WorkflowEngine`, `IChatClient` |
| Methods | PascalCase + `Async` suffix | `ExecuteStageAsync()`, `ApproveGateAsync()` |
| Properties | PascalCase | `WorkflowId`, `CreatedAt` |
| Private fields | `_camelCase` | `_workflowRepository`, `_eventBus` |
| Constants | PascalCase | `DefaultPollingInterval`, `MaxRetryCount` |

### TypeScript/React Code

| Element | Convention | Example |
|---------|-----------|---------|
| Components | PascalCase file + export | `WorkflowCard.tsx` |
| Hooks | `use` prefix, camelCase file | `useWorkflow.ts`, `useSignalR.ts` |
| Utilities | camelCase file | `formatDate.ts`, `parseMarkdown.ts` |
| Constants | UPPER_SNAKE_CASE | `SIGNALR_HUB_URL`, `DEFAULT_POLLING_INTERVAL` |
| Zustand stores | `use{Name}Store` | `useConnectionStore`, `useStreamingStore` |
| Query keys | Tuple arrays | `['workflows']`, `['workflow', id, 'stages']` |

### SignalR Events

| Element | Convention | Example |
|---------|-----------|---------|
| Event names | PascalCase verb+noun | `AgentTextDelta`, `WorkflowStatusChanged`, `GateReady` |
| Payloads | Typed DTOs, camelCase JSON | `{ workflowId, stageId, text }` |
| Correlation | All events carry `workflowId` | For routing and logging |

## Layer Boundaries (Onion Architecture)

**HARD ENFORCEMENT — violations break the architecture.**

### Backend

```
server/
  Domain/           # ZERO infrastructure dependencies (no EF Core, no SignalR, no HTTP)
  Application/      # Depends on Domain only. Interfaces for external I/O seams.
  Infrastructure/   # Implements Application interfaces. All external I/O.
  Api/              # Composition root. Minimal API endpoints, middleware, DI wiring.
  Migrations/       # EF Core migrations (CLI-generated only)
```

- `Domain/` — Pure C#: entities, value objects, enums, state machine. ZERO external packages.
- `Application/` — Services, typed settings, DTOs, exceptions. Depends on Domain only. Interfaces ONLY for external I/O seams.
- `Infrastructure/` — EF Core, SignalR hub, git, LLM clients. Depends on Domain + Application.
- `Api/` — Composition root. Depends on all layers. DI wiring in Program.cs.

**Violation example:** If `AppDbContext` or `[JsonProperty]` appears in `Domain/`, the architecture is broken.

### Frontend

```
client/src/
  features/         # Domain feature folders (dashboard, workflow, gate, artifact, settings)
  shared/           # Reusable components, layout, shared hooks
  stores/           # Zustand stores
  api/              # TanStack Query hooks + API client functions
  hooks/            # Shared hooks (useSignalR, useAuth)
```

## Enforcement Rules

All AI agents and developers MUST follow these 13 rules:

1. **Follow naming conventions exactly** — see tables above (C# PascalCase, TypeScript camelCase, API kebab-case, DB PascalCase plural). No creative variations.

2. **Use concrete classes by default** — interfaces ONLY for external I/O seams: `IEventBus`, `IChatClient`, `IGitService`, `ICurrentUser`, `IStageExecutor`. No `IWorkflowService` + `WorkflowService` pairs.

3. **Respect layer boundaries** — `Domain/` has zero infrastructure dependencies. No EF Core, no SignalR, no HTTP, no external packages in Domain.

4. **Use `HttpException` hierarchy for all error responses** — never return status codes manually. Throw `NotFoundException`, `ConflictException`, `ValidationException`, `ForbiddenException`. Middleware maps to Problem Details.

5. **Push events through `IEventBus`** — never call SignalR hub directly from services. Services depend on `IEventBus` interface, not the hub.

6. **Use EF Core `AppDbContext` directly** — no repository wrapper pattern. EF Core DbContext IS the abstraction.

7. **Use `IOptions<TSettings>` for configuration** — never inject `IConfiguration` directly into services. Each feature gets a typed settings class. Only `Program.cs` touches `IConfiguration`.

8. **Include `CancellationToken` as last parameter on every `Async` method** — API layer passes `HttpContext.RequestAborted`. Agent execution uses linked token source.

9. **Create EF Core migrations via CLI only** — `dotnet ef migrations add`. Never auto-generate.

10. **Use TanStack Query for all REST data** — never raw `fetch` + `useEffect`. API function in `api/`, query hook in feature folder, component consumes hook.

11. **Use Zustand stores for client state** — never React Context for frequently-changing state. Zustand for UI state, SignalR status, streaming buffer.

12. **No static state anywhere** — everything through DI. Only exception: `AsyncLocal` for Serilog correlation context.

13. **Follow SignalR -> Query Invalidation Mapping** — `useSignalRInvalidation` hook subscribes to SignalR events and calls `queryClient.invalidateQueries()` with mapped keys. All features benefit automatically.

## SignalR -> Query Invalidation Mapping

| SignalR Event | Invalidates Query Keys | Trigger |
|--------------|----------------------|---------|
| `WorkflowStatusChanged` | `['workflows']`, `['workflow', id]` | Workflow created, paused, resumed, abandoned, completed |
| `StageCompleted` | `['workflow', id, 'stages']`, `['workflow', id]` | Stage execution finishes |
| `GateReady` | `['workflow', id]`, `['workflows']` | Stage output ready for review |
| `GateActioned` | `['workflow', id]`, `['workflows']` | Gate approved, rejected, or go-back |
| `ArtifactUpdated` | `['workflow', id, 'artifacts']` | New artifact version created |
| `CascadeTriggered` | `['workflow', id, 'stages']`, `['workflow', id]` | Course correction cascade initiated |
