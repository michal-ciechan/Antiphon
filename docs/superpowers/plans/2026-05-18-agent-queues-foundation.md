# Agent Queues Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first working Agents surface: persistent agent definitions, manual card assignment to agent queues, per-card workflow snapshots from agent defaults, and a top-level Agents roster/detail UI.

**Architecture:** Reuse existing cards as work items and add persistent `Agent` records around them. Move the current launch-definition registry under `/api/agents/definitions`, then make `/api/agents` the REST surface for persistent agents and queues. This plan does not change terminal runtime ownership; persistent session reuse, memory compaction, and handoff get separate implementation plans after this foundation is merged.

**Tech Stack:** ASP.NET Core Minimal APIs, EF Core/PostgreSQL, React 19, Mantine, TanStack Query, SignalR invalidation, TUnit integration tests, Playwright E2E.

---

## Scope Boundary

This plan implements the first vertical slice from the approved spec:

- Persistent agent records with working directory, details, default workflow template, assignment policy, and status.
- Manual assignment of existing cards to an agent queue.
- Queue ordering.
- A per-card workflow run and stage snapshot created at assignment time from the agent default workflow template.
- Top-level Agents page with roster, detail, queue, and card assignment modal.
- Backend integration tests, frontend tests, and one Playwright E2E user flow.

The following spec sections require their own follow-up plans:

- Persistent terminal sessions that span multiple cards.
- Durable CLI session id launch/resume integration.
- Stage execution against persistent agents.
- Human-review blocking connected to live agent execution.
- Scoped memory file compaction.
- Agent-to-agent handoff.

## File Structure

Backend domain:

- Create `server/Domain/Entities/Agent.cs`: persistent agent definition and queue owner.
- Create `server/Domain/Entities/CardWorkflowRun.cs`: per-card workflow snapshot root.
- Create `server/Domain/Entities/CardWorkflowStage.cs`: per-stage snapshot and state.
- Create `server/Domain/Enums/AgentAssignmentPolicy.cs`: `AutoPick`, `ManualConfirm`, `Paused`.
- Create `server/Domain/Enums/AgentStatus.cs`: `Idle`, `Ready`, `Working`, `WaitingForHumanReview`, `Stopped`, `Disconnected`, `Failed`.
- Create `server/Domain/Enums/CardWorkflowRunStatus.cs`: `Queued`, `Running`, `WaitingForHumanReview`, `Completed`, `Failed`, `Canceled`.
- Create `server/Domain/Enums/CardWorkflowStageStatus.cs`: `Pending`, `Running`, `WaitingForHumanReview`, `Completed`, `Failed`, `Skipped`.
- Modify `server/Domain/Entities/Card.cs`: add assigned-agent queue metadata and active workflow run navigation.

Backend application/API:

- Modify `server/Application/Dtos/AgentDtos.cs`: keep launch-definition DTOs and add persistent agent/queue DTOs.
- Create `server/Application/Services/AgentService.cs`: CRUD, queue assignment, queue reorder, queue removal.
- Create `server/Application/Services/CardWorkflowRunFactory.cs`: snapshots workflow template YAML into `CardWorkflowRun` and `CardWorkflowStage` rows.
- Modify `server/Api/Endpoints/AgentEndpoints.cs`: add persistent agent endpoints and move registry endpoint to `/api/agents/definitions`.
- Modify `server/Application/Dtos/BoardDtos.cs`: expose card assignment/workflow summary fields.
- Modify `server/Application/Services/BoardService.cs`: populate new card DTO fields.
- Modify `server/Program.cs`: register `AgentService` and `CardWorkflowRunFactory`.
- Modify `server/Infrastructure/Data/AppDbContext.cs`: configure new entities and card relationships.
- Create EF migration through CLI only: `AgentQueuesFoundation`.

Frontend:

- Modify `client/src/api/agents.ts`: split launch-definition hook from persistent agent hooks.
- Modify `client/src/features/board/AgentPicker.tsx`: use `useAgentDefinitions`.
- Create `client/src/features/agents/AgentsPage.tsx`: roster, detail, queue, create/edit actions.
- Create `client/src/features/agents/AgentCreateModal.tsx`: create persistent agent.
- Create `client/src/features/agents/AgentQueueAssignModal.tsx`: assign existing cards to an agent queue.
- Modify `client/src/shared/Layout.tsx`: add Agents nav item.
- Modify `client/src/App.tsx`: add `/agents` route.
- Modify `client/src/hooks/useSignalRInvalidation.ts`: invalidate agent queries for `AgentChanged` and `AgentQueueChanged`.

Tests:

- Create `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs`.
- Modify `tests/Antiphon.Tests/Infrastructure/KanbanPersistenceTests.cs`.
- Modify `client/src/features/board/CardModal.test.tsx`.
- Create `client/src/features/agents/AgentsPage.test.tsx`.
- Create or extend `tests/Antiphon.E2E/AgentE2ETests.cs`.

## Task 1: Move Launch Definitions Under `/api/agents/definitions`

**Files:**

- Modify: `server/Api/Endpoints/AgentEndpoints.cs`
- Modify: `client/src/api/agents.ts`
- Modify: `client/src/features/board/AgentPicker.tsx`
- Modify: `client/src/features/board/CardModal.test.tsx`

- [ ] **Step 1: Update the frontend test to prove AgentPicker uses the new definitions endpoint**

In `client/src/features/board/CardModal.test.tsx`, change the AgentPicker MSW handler from `/api/agents` to `/api/agents/definitions`.

```tsx
function agentDefinitionsHandler() {
  return http.get('/api/agents/definitions', () =>
    HttpResponse.json({
      defaultDefinition: 'claude',
      definitions: [
        { name: 'claude', kind: 'ClaudeCode', isDefault: true },
        { name: 'raw', kind: 'Raw', isDefault: false },
      ],
    }),
  )
}
```

- [ ] **Step 2: Run the frontend test and verify it fails**

Run:

```powershell
npm test -- CardModal
```

Expected: `AgentPicker` fails to load options because `useAgents` still calls `/api/agents`.

- [ ] **Step 3: Rename the client hook to `useAgentDefinitions`**

In `client/src/api/agents.ts`, keep the existing DTO names but change the hook name and query key:

```ts
export const agentKeys = {
  definitions: ['agents', 'definitions'] as const,
}

export function useAgentDefinitions() {
  return useQuery({
    queryKey: agentKeys.definitions,
    queryFn: () => apiGet<AgentRegistryDto>('/agents/definitions'),
  })
}
```

- [ ] **Step 4: Update AgentPicker to use the renamed hook**

In `client/src/features/board/AgentPicker.tsx`:

```tsx
import { useAgentDefinitions } from '../../api/agents'

export function AgentPicker({ value, onChange }: AgentPickerProps) {
  const { data, isLoading } = useAgentDefinitions()
  const options = (data?.definitions ?? []).map((agent) => ({
    value: agent.name,
    label: agent.isDefault ? `${agent.name} (${agent.kind}, default)` : `${agent.name} (${agent.kind})`,
  }))

  useEffect(() => {
    if (!value && data?.defaultDefinition) {
      onChange(data.defaultDefinition)
    }
  }, [data?.defaultDefinition, onChange, value])

  return (
    <Select
      label="Agent"
      data={options}
      value={value}
      onChange={onChange}
      disabled={isLoading || options.length === 0}
      searchable
      allowDeselect={false}
    />
  )
}
```

- [ ] **Step 5: Move the registry endpoint**

In `server/Api/Endpoints/AgentEndpoints.cs`, keep the route group as `/api/agents` and change the current `MapGet("/")` to `MapGet("/definitions")`:

```csharp
agents.MapGet("/definitions", (AgentRegistry registry) =>
{
    var settings = registry.Settings;
    var definitions = settings.Definitions
        .OrderBy(kvp => kvp.Key)
        .Select(kvp =>
        {
            var kind = Enum.TryParse<AgentKind>(kvp.Value.Kind, ignoreCase: true, out var parsed)
                ? parsed
                : AgentKind.Raw;
            return new AgentDefinitionDto(
                kvp.Key,
                kind,
                string.Equals(kvp.Key, settings.DefaultDefinition, StringComparison.Ordinal));
        })
        .ToList();

    return Results.Ok(new AgentRegistryDto(settings.DefaultDefinition, definitions));
});
```

- [ ] **Step 6: Run focused frontend test**

Run:

```powershell
npm test -- CardModal
```

Expected: all `CardModal`/`AgentPicker` tests pass.

- [ ] **Step 7: Commit**

```powershell
git add server/Api/Endpoints/AgentEndpoints.cs client/src/api/agents.ts client/src/features/board/AgentPicker.tsx client/src/features/board/CardModal.test.tsx
git commit -m "refactor(agents): separate launch definitions endpoint"
```

## Task 2: Add Agent Queue Domain Model And EF Mapping

**Files:**

- Create: `server/Domain/Entities/Agent.cs`
- Create: `server/Domain/Entities/CardWorkflowRun.cs`
- Create: `server/Domain/Entities/CardWorkflowStage.cs`
- Create: `server/Domain/Enums/AgentAssignmentPolicy.cs`
- Create: `server/Domain/Enums/AgentStatus.cs`
- Create: `server/Domain/Enums/CardWorkflowRunStatus.cs`
- Create: `server/Domain/Enums/CardWorkflowStageStatus.cs`
- Modify: `server/Domain/Entities/Card.cs`
- Modify: `server/Infrastructure/Data/AppDbContext.cs`
- Modify: `tests/Antiphon.Tests/Infrastructure/KanbanPersistenceTests.cs`
- Create: generated migration under `server/Migrations/`

- [ ] **Step 1: Write persistence test first**

Add this test to `tests/Antiphon.Tests/Infrastructure/KanbanPersistenceTests.cs`:

```csharp
[Test]
public async Task AppDbContext_round_trip_persists_agent_queue_and_card_workflow_run()
{
    await using var db = CreateContext();
    var graph = CreateKanbanGraph();
    var now = DateTime.UtcNow;

    var template = new WorkflowTemplate
    {
        Id = Guid.NewGuid(),
        Name = $"Agent Template {Guid.NewGuid():N}",
        Description = "Agent queue test template",
        YamlDefinition = """
            name: One Shot
            description: Implement then review
            stages:
              - name: Implement
                executorType: agent
                gateRequired: false
              - name: Human Review
                executorType: human
                gateRequired: true
            """,
        IsBuiltIn = false,
        CreatedAt = now,
        UpdatedAt = now
    };

    var agent = new Agent
    {
        Id = Guid.NewGuid(),
        Name = "Frontend Claude",
        Slug = $"frontend-claude-{Guid.NewGuid():N}",
        WorkingDirectory = "D:/src/app",
        Details = "Handles UI cards",
        DefaultWorkflowTemplateId = template.Id,
        AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
        Status = AgentStatus.Idle,
        CreatedAt = now,
        UpdatedAt = now
    };

    var run = new CardWorkflowRun
    {
        Id = Guid.NewGuid(),
        CardId = graph.Card.Id,
        AgentId = agent.Id,
        WorkflowTemplateId = template.Id,
        WorkflowName = "One Shot",
        WorkflowDefinitionSnapshot = template.YamlDefinition,
        Status = CardWorkflowRunStatus.Queued,
        CreatedAt = now,
        UpdatedAt = now
    };

    var stage = new CardWorkflowStage
    {
        Id = Guid.NewGuid(),
        CardWorkflowRunId = run.Id,
        StageOrder = 0,
        Name = "Implement",
        ExecutorType = "agent",
        GateRequired = false,
        Status = CardWorkflowStageStatus.Pending,
        CreatedAt = now,
        UpdatedAt = now
    };

    db.WorkflowTemplates.Add(template);
    db.Add(graph.Project);
    db.Agents.Add(agent);
    db.CardWorkflowRuns.Add(run);
    db.CardWorkflowStages.Add(stage);
    graph.Card.AssignedAgentId = agent.Id;
    graph.Card.AgentQueuePosition = 1;
    graph.Card.ActiveWorkflowRunId = run.Id;

    await db.SaveChangesAsync();

    await using var verify = CreateContext();
    var stored = await verify.Cards
        .Include(c => c.AssignedAgent)
        .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.Stages)
        .SingleAsync(c => c.Id == graph.Card.Id);

    stored.AssignedAgent!.Name.ShouldBe("Frontend Claude");
    stored.AgentQueuePosition.ShouldBe(1);
    stored.ActiveWorkflowRun!.WorkflowName.ShouldBe("One Shot");
    stored.ActiveWorkflowRun.Stages.Single().Name.ShouldBe("Implement");
}
```

- [ ] **Step 2: Run the persistence test and verify it fails to compile**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~KanbanPersistenceTests.AppDbContext_round_trip_persists_agent_queue_and_card_workflow_run"
```

Expected: compile fails because the `Agent`, workflow run entities, enums, and card properties do not exist.

- [ ] **Step 3: Add domain enums**

Create the enum files with this content:

```csharp
namespace Antiphon.Server.Domain.Enums;

public enum AgentAssignmentPolicy
{
    AutoPick = 0,
    ManualConfirm = 1,
    Paused = 2
}
```

```csharp
namespace Antiphon.Server.Domain.Enums;

public enum AgentStatus
{
    Idle = 0,
    Ready = 1,
    Working = 2,
    WaitingForHumanReview = 3,
    Stopped = 4,
    Disconnected = 5,
    Failed = 6
}
```

```csharp
namespace Antiphon.Server.Domain.Enums;

public enum CardWorkflowRunStatus
{
    Queued = 0,
    Running = 1,
    WaitingForHumanReview = 2,
    Completed = 3,
    Failed = 4,
    Canceled = 5
}
```

```csharp
namespace Antiphon.Server.Domain.Enums;

public enum CardWorkflowStageStatus
{
    Pending = 0,
    Running = 1,
    WaitingForHumanReview = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5
}
```

- [ ] **Step 4: Add domain entities**

Create `server/Domain/Entities/Agent.cs`:

```csharp
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class Agent
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Guid? DefaultWorkflowTemplateId { get; set; }
    public AgentAssignmentPolicy AssignmentPolicy { get; set; } = AgentAssignmentPolicy.AutoPick;
    public AgentStatus Status { get; set; } = AgentStatus.Idle;
    public string? PersistentSessionId { get; set; }
    public Guid? CurrentCardId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public WorkflowTemplate? DefaultWorkflowTemplate { get; set; }
    public Card? CurrentCard { get; set; }
    public ICollection<Card> QueueCards { get; set; } = new List<Card>();
    public ICollection<CardWorkflowRun> WorkflowRuns { get; set; } = new List<CardWorkflowRun>();
}
```

Create `server/Domain/Entities/CardWorkflowRun.cs`:

```csharp
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class CardWorkflowRun
{
    public Guid Id { get; set; }
    public Guid CardId { get; set; }
    public Guid AgentId { get; set; }
    public Guid? WorkflowTemplateId { get; set; }
    public string WorkflowName { get; set; } = string.Empty;
    public string WorkflowDefinitionSnapshot { get; set; } = string.Empty;
    public CardWorkflowRunStatus Status { get; set; } = CardWorkflowRunStatus.Queued;
    public Guid? CurrentStageId { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Card Card { get; set; } = null!;
    public Agent Agent { get; set; } = null!;
    public WorkflowTemplate? WorkflowTemplate { get; set; }
    public CardWorkflowStage? CurrentStage { get; set; }
    public ICollection<CardWorkflowStage> Stages { get; set; } = new List<CardWorkflowStage>();
}
```

Create `server/Domain/Entities/CardWorkflowStage.cs`:

```csharp
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Domain.Entities;

public class CardWorkflowStage
{
    public Guid Id { get; set; }
    public Guid CardWorkflowRunId { get; set; }
    public int StageOrder { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ExecutorType { get; set; } = string.Empty;
    public string? ModelName { get; set; }
    public bool GateRequired { get; set; }
    public string? SystemPrompt { get; set; }
    public CardWorkflowStageStatus Status { get; set; } = CardWorkflowStageStatus.Pending;
    public string? ResultSummary { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public CardWorkflowRun CardWorkflowRun { get; set; } = null!;
}
```

- [ ] **Step 5: Extend Card**

In `server/Domain/Entities/Card.cs`, add:

```csharp
public Guid? AssignedAgentId { get; set; }
public int? AgentQueuePosition { get; set; }
public Guid? ActiveWorkflowRunId { get; set; }

public Agent? AssignedAgent { get; set; }
public CardWorkflowRun? ActiveWorkflowRun { get; set; }
public ICollection<CardWorkflowRun> WorkflowRuns { get; set; } = new List<CardWorkflowRun>();
```

- [ ] **Step 6: Configure AppDbContext**

Add DbSets:

```csharp
public DbSet<Agent> Agents => Set<Agent>();
public DbSet<CardWorkflowRun> CardWorkflowRuns => Set<CardWorkflowRun>();
public DbSet<CardWorkflowStage> CardWorkflowStages => Set<CardWorkflowStage>();
```

Add entity configuration in `OnModelCreating`:

```csharp
modelBuilder.Entity<Agent>(entity =>
{
    entity.ToTable("Agents");
    entity.HasKey(a => a.Id);
    entity.Property(a => a.Name).IsRequired().HasMaxLength(200);
    entity.Property(a => a.Slug).IsRequired().HasMaxLength(120);
    entity.Property(a => a.WorkingDirectory).IsRequired().HasMaxLength(1000);
    entity.Property(a => a.Details).IsRequired().HasMaxLength(4000);
    entity.Property(a => a.AssignmentPolicy).IsRequired();
    entity.Property(a => a.Status).IsRequired();
    entity.Property(a => a.PersistentSessionId).HasMaxLength(200);
    entity.Property(a => a.CreatedAt).IsRequired();
    entity.Property(a => a.UpdatedAt).IsRequired();

    entity.HasIndex(a => a.Slug).IsUnique().HasDatabaseName("IX_Agents_Slug");
    entity.HasIndex(a => a.Status).HasDatabaseName("IX_Agents_Status");

    entity.HasOne(a => a.DefaultWorkflowTemplate)
        .WithMany()
        .HasForeignKey(a => a.DefaultWorkflowTemplateId)
        .OnDelete(DeleteBehavior.SetNull);

    entity.HasOne(a => a.CurrentCard)
        .WithMany()
        .HasForeignKey(a => a.CurrentCardId)
        .OnDelete(DeleteBehavior.SetNull);
});

modelBuilder.Entity<CardWorkflowRun>(entity =>
{
    entity.ToTable("CardWorkflowRuns");
    entity.HasKey(r => r.Id);
    entity.Property(r => r.CardId).IsRequired();
    entity.Property(r => r.AgentId).IsRequired();
    entity.Property(r => r.WorkflowName).IsRequired().HasMaxLength(200);
    entity.Property(r => r.WorkflowDefinitionSnapshot).IsRequired();
    entity.Property(r => r.Status).IsRequired();
    entity.Property(r => r.FailureReason).HasMaxLength(4000);
    entity.Property(r => r.CreatedAt).IsRequired();
    entity.Property(r => r.UpdatedAt).IsRequired();

    entity.HasIndex(r => r.CardId).HasDatabaseName("IX_CardWorkflowRuns_CardId");
    entity.HasIndex(r => r.AgentId).HasDatabaseName("IX_CardWorkflowRuns_AgentId");
    entity.HasIndex(r => new { r.CardId, r.Status }).HasDatabaseName("IX_CardWorkflowRuns_CardId_Status");

    entity.HasOne(r => r.Card)
        .WithMany(c => c.WorkflowRuns)
        .HasForeignKey(r => r.CardId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(r => r.Agent)
        .WithMany(a => a.WorkflowRuns)
        .HasForeignKey(r => r.AgentId)
        .OnDelete(DeleteBehavior.Restrict);

    entity.HasOne(r => r.WorkflowTemplate)
        .WithMany()
        .HasForeignKey(r => r.WorkflowTemplateId)
        .OnDelete(DeleteBehavior.SetNull);

    entity.HasOne(r => r.CurrentStage)
        .WithMany()
        .HasForeignKey(r => r.CurrentStageId)
        .OnDelete(DeleteBehavior.SetNull);
});

modelBuilder.Entity<CardWorkflowStage>(entity =>
{
    entity.ToTable("CardWorkflowStages");
    entity.HasKey(s => s.Id);
    entity.Property(s => s.CardWorkflowRunId).IsRequired();
    entity.Property(s => s.StageOrder).IsRequired();
    entity.Property(s => s.Name).IsRequired().HasMaxLength(200);
    entity.Property(s => s.ExecutorType).IsRequired().HasMaxLength(100);
    entity.Property(s => s.ModelName).HasMaxLength(200);
    entity.Property(s => s.SystemPrompt).HasMaxLength(4000);
    entity.Property(s => s.Status).IsRequired();
    entity.Property(s => s.ResultSummary).HasMaxLength(4000);
    entity.Property(s => s.FailureReason).HasMaxLength(4000);
    entity.Property(s => s.CreatedAt).IsRequired();
    entity.Property(s => s.UpdatedAt).IsRequired();

    entity.HasIndex(s => new { s.CardWorkflowRunId, s.StageOrder })
        .IsUnique()
        .HasDatabaseName("IX_CardWorkflowStages_RunId_StageOrder");

    entity.HasOne(s => s.CardWorkflowRun)
        .WithMany(r => r.Stages)
        .HasForeignKey(s => s.CardWorkflowRunId)
        .OnDelete(DeleteBehavior.Cascade);
});
```

Extend `Card` configuration:

```csharp
entity.Property(c => c.AgentQueuePosition);
entity.HasIndex(c => c.AssignedAgentId).HasDatabaseName("IX_Cards_AssignedAgentId");
entity.HasIndex(c => new { c.AssignedAgentId, c.AgentQueuePosition })
    .HasDatabaseName("IX_Cards_AssignedAgentId_AgentQueuePosition");
entity.HasIndex(c => c.ActiveWorkflowRunId).HasDatabaseName("IX_Cards_ActiveWorkflowRunId");

entity.HasOne(c => c.AssignedAgent)
    .WithMany(a => a.QueueCards)
    .HasForeignKey(c => c.AssignedAgentId)
    .OnDelete(DeleteBehavior.SetNull);

entity.HasOne(c => c.ActiveWorkflowRun)
    .WithOne()
    .HasForeignKey<Card>(c => c.ActiveWorkflowRunId)
    .OnDelete(DeleteBehavior.SetNull);
```

- [ ] **Step 7: Create migration through CLI**

Stop the server first:

```powershell
.\stop-server.ps1
```

Create the migration:

```powershell
dotnet ef migrations add AgentQueuesFoundation --project server
```

Expected: new migration files under `server/Migrations/` and updated `AppDbContextModelSnapshot.cs`.

- [ ] **Step 8: Run persistence tests**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~KanbanPersistenceTests"
```

Expected: all `KanbanPersistenceTests` pass.

- [ ] **Step 9: Commit**

```powershell
git add server/Domain/Entities server/Domain/Enums server/Infrastructure/Data/AppDbContext.cs server/Migrations tests/Antiphon.Tests/Infrastructure/KanbanPersistenceTests.cs
git commit -m "feat(agents): add queue persistence model"
```

## Task 3: Add AgentService And Workflow Snapshot Creation

**Files:**

- Modify: `server/Application/Dtos/AgentDtos.cs`
- Create: `server/Application/Services/CardWorkflowRunFactory.cs`
- Create: `server/Application/Services/AgentService.cs`
- Modify: `server/Program.cs`
- Create: `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs`

- [ ] **Step 1: Write integration tests first**

Create `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs` with tests named:

```csharp
[Test]
public async Task CreateAsync_persists_agent_with_default_auto_pick_policy()

[Test]
public async Task AssignCardAsync_assigns_card_to_next_queue_position_and_snapshots_default_workflow()

[Test]
public async Task ReorderQueueAsync_rewrites_positions_without_cross_agent_cards()

[Test]
public async Task RemoveCardAsync_clears_assignment_and_active_workflow_run()
```

The assignment test must assert:

```csharp
storedCard.AssignedAgentId.ShouldBe(agent.Id);
storedCard.AgentQueuePosition.ShouldBe(1);
storedCard.ActiveWorkflowRunId.ShouldNotBeNull();
storedCard.ActiveWorkflowRun!.WorkflowDefinitionSnapshot.ShouldContain("name: One Shot");
storedCard.ActiveWorkflowRun.Stages.Select(s => s.Name).ShouldBe(["Implement", "Human Review"]);
eventBus.PublishedEvents.Any(e => e.EventName == "AgentQueueChanged").ShouldBeTrue();
eventBus.PublishedEvents.Any(e => e.EventName == "CardChanged").ShouldBeTrue();
```

- [ ] **Step 2: Run tests and verify they fail to compile**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~AgentServiceIntegrationTests"
```

Expected: compile fails because `AgentService`, DTOs, and `CardWorkflowRunFactory` do not exist.

- [ ] **Step 3: Add persistent agent DTOs**

Append these DTO records to `server/Application/Dtos/AgentDtos.cs`:

```csharp
public sealed record AgentSummaryDto(
    Guid Id,
    string Name,
    string Slug,
    string WorkingDirectory,
    string Details,
    Guid? DefaultWorkflowTemplateId,
    string? DefaultWorkflowTemplateName,
    AgentAssignmentPolicy AssignmentPolicy,
    AgentStatus Status,
    string? PersistentSessionId,
    Guid? CurrentCardId,
    int QueueLength,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AgentDetailDto(
    Guid Id,
    string Name,
    string Slug,
    string WorkingDirectory,
    string Details,
    Guid? DefaultWorkflowTemplateId,
    string? DefaultWorkflowTemplateName,
    AgentAssignmentPolicy AssignmentPolicy,
    AgentStatus Status,
    string? PersistentSessionId,
    Guid? CurrentCardId,
    IReadOnlyList<AgentQueueCardDto> Queue,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record AgentQueueCardDto(
    Guid CardId,
    Guid BoardId,
    string BoardName,
    string Identifier,
    string Title,
    int Priority,
    int QueuePosition,
    Guid? ActiveWorkflowRunId,
    CardWorkflowRunStatus? WorkflowStatus,
    string? CurrentStageName);

public sealed record CreateAgentRequest(
    string Name,
    string WorkingDirectory,
    string? Details = null,
    Guid? DefaultWorkflowTemplateId = null,
    AgentAssignmentPolicy AssignmentPolicy = AgentAssignmentPolicy.AutoPick);

public sealed record UpdateAgentRequest(
    string Name,
    string WorkingDirectory,
    string? Details,
    Guid? DefaultWorkflowTemplateId,
    AgentAssignmentPolicy AssignmentPolicy);

public sealed record AssignAgentCardRequest(Guid CardId);

public sealed record ReorderAgentQueueRequest(IReadOnlyList<Guid> CardIds);
```

- [ ] **Step 4: Add workflow run factory**

Create `server/Application/Services/CardWorkflowRunFactory.cs`:

```csharp
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Domain.ValueObjects;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class CardWorkflowRunFactory
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _timeProvider;

    public CardWorkflowRunFactory(AppDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<CardWorkflowRun> CreateFromAgentDefaultAsync(
        Card card,
        Agent agent,
        CancellationToken ct)
    {
        var template = agent.DefaultWorkflowTemplateId is Guid templateId
            ? await _db.WorkflowTemplates.FirstOrDefaultAsync(t => t.Id == templateId, ct)
                ?? throw new NotFoundException(nameof(WorkflowTemplate), templateId)
            : await _db.WorkflowTemplates
                .OrderBy(t => t.Name)
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException("defaultWorkflowTemplateId", "At least one workflow template is required.");

        var definition = WorkflowDefinitionParser.ParseYamlDefinition(template.YamlDefinition);
        return CreateRun(card, agent, template, definition);
    }

    private CardWorkflowRun CreateRun(
        Card card,
        Agent agent,
        WorkflowTemplate template,
        WorkflowDefinition definition)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var run = new CardWorkflowRun
        {
            Id = Guid.NewGuid(),
            CardId = card.Id,
            AgentId = agent.Id,
            WorkflowTemplateId = template.Id,
            WorkflowName = definition.Name,
            WorkflowDefinitionSnapshot = template.YamlDefinition,
            Status = CardWorkflowRunStatus.Queued,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var stage in definition.Stages.Select((stage, index) => new { stage, index }))
        {
            run.Stages.Add(new CardWorkflowStage
            {
                Id = Guid.NewGuid(),
                CardWorkflowRunId = run.Id,
                StageOrder = stage.index,
                Name = stage.stage.Name,
                ExecutorType = stage.stage.ExecutorType,
                ModelName = stage.stage.ModelName,
                GateRequired = stage.stage.GateRequired,
                SystemPrompt = stage.stage.SystemPrompt,
                Status = CardWorkflowStageStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        run.CurrentStageId = run.Stages.OrderBy(s => s.StageOrder).FirstOrDefault()?.Id;
        return run;
    }
}
```

- [ ] **Step 5: Add AgentService**

Create `server/Application/Services/AgentService.cs`. Required public methods:

```csharp
public Task<IReadOnlyList<AgentSummaryDto>> GetAllAsync(CancellationToken ct)
public Task<AgentDetailDto> GetByIdAsync(Guid id, CancellationToken ct)
public Task<AgentDetailDto> CreateAsync(CreateAgentRequest request, CancellationToken ct)
public Task<AgentDetailDto> UpdateAsync(Guid id, UpdateAgentRequest request, CancellationToken ct)
public Task<AgentDetailDto> AssignCardAsync(Guid id, AssignAgentCardRequest request, CancellationToken ct)
public Task<AgentDetailDto> ReorderQueueAsync(Guid id, ReorderAgentQueueRequest request, CancellationToken ct)
public Task RemoveCardAsync(Guid id, Guid cardId, CancellationToken ct)
```

Validation rules:

```csharp
if (string.IsNullOrWhiteSpace(request.Name))
    errors[nameof(request.Name)] = ["Agent name is required."];
if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
    errors[nameof(request.WorkingDirectory)] = ["Working directory is required."];
```

Slug generation rule:

```csharp
private static string Slugify(string name)
{
    var chars = name.Trim().ToLowerInvariant()
        .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
        .ToArray();
    var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    return string.IsNullOrWhiteSpace(slug) ? $"agent-{Guid.NewGuid():N}" : slug;
}
```

Queue assignment must:

```csharp
var nextPosition = await _db.Cards
    .Where(c => c.AssignedAgentId == agent.Id && c.AgentQueuePosition != null)
    .MaxAsync(c => (int?)c.AgentQueuePosition, ct) ?? 0;

card.AssignedAgentId = agent.Id;
card.AgentQueuePosition = nextPosition + 1;
card.ActiveWorkflowRun = await _workflowRunFactory.CreateFromAgentDefaultAsync(card, agent, ct);
card.ActiveWorkflowRunId = card.ActiveWorkflowRun.Id;
card.UpdatedAt = now;
card.ConcurrencyToken = Guid.NewGuid();
```

After assignment, publish:

```csharp
await _eventBus.PublishToAllAsync("AgentQueueChanged", new { agentId = agent.Id, cardId = card.Id }, ct);
await _eventBus.PublishToAllAsync("CardChanged", new { boardId = card.BoardId, cardId = card.Id }, ct);
```

- [ ] **Step 6: Register services**

In `server/Program.cs`:

```csharp
builder.Services.AddScoped<CardWorkflowRunFactory>();
builder.Services.AddScoped<AgentService>();
```

- [ ] **Step 7: Run integration tests**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~AgentServiceIntegrationTests"
```

Expected: all new `AgentServiceIntegrationTests` pass.

- [ ] **Step 8: Commit**

```powershell
git add server/Application/Dtos/AgentDtos.cs server/Application/Services/CardWorkflowRunFactory.cs server/Application/Services/AgentService.cs server/Program.cs tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs
git commit -m "feat(agents): add queue service"
```

## Task 4: Add Persistent Agent API Endpoints

**Files:**

- Modify: `server/Api/Endpoints/AgentEndpoints.cs`
- Add endpoint coverage to: `tests/Antiphon.Tests/SmokeTests.cs` or create `tests/Antiphon.Tests/Application/AgentEndpointSmokeTests.cs`

- [ ] **Step 1: Add endpoint smoke test**

Create a test that posts an agent and then reads `/api/agents`. The test must use the existing app fixture style from `SmokeTests`.

Expected assertions:

```csharp
createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
var agents = await client.GetFromJsonAsync<List<AgentSummaryDto>>("/api/agents");
agents.ShouldNotBeNull();
agents!.Any(a => a.Name == "Endpoint Agent").ShouldBeTrue();
```

- [ ] **Step 2: Run endpoint smoke test and verify it fails**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~AgentEndpoint"
```

Expected: `POST /api/agents` returns 404 or compile fails because test references new endpoint DTOs.

- [ ] **Step 3: Add persistent agent endpoints**

In `server/Api/Endpoints/AgentEndpoints.cs`, add these mappings before or after `/definitions`:

```csharp
agents.MapGet("/", async (AgentService service, CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetAllAsync(cancellationToken));
});

agents.MapGet("/{id:guid}", async (Guid id, AgentService service, CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.GetByIdAsync(id, cancellationToken));
});

agents.MapPost("/", async (CreateAgentRequest request, AgentService service, CancellationToken cancellationToken) =>
{
    var agent = await service.CreateAsync(request, cancellationToken);
    return Results.Created($"/api/agents/{agent.Id}", agent);
});

agents.MapPatch("/{id:guid}", async (
    Guid id,
    UpdateAgentRequest request,
    AgentService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.UpdateAsync(id, request, cancellationToken));
});

agents.MapPost("/{id:guid}/queue", async (
    Guid id,
    AssignAgentCardRequest request,
    AgentService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.AssignCardAsync(id, request, cancellationToken));
});

agents.MapPatch("/{id:guid}/queue", async (
    Guid id,
    ReorderAgentQueueRequest request,
    AgentService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.ReorderQueueAsync(id, request, cancellationToken));
});

agents.MapDelete("/{id:guid}/queue/{cardId:guid}", async (
    Guid id,
    Guid cardId,
    AgentService service,
    CancellationToken cancellationToken) =>
{
    await service.RemoveCardAsync(id, cardId, cancellationToken);
    return Results.NoContent();
});
```

- [ ] **Step 4: Run endpoint tests**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~AgentEndpoint"
```

Expected: endpoint tests pass.

- [ ] **Step 5: Commit**

```powershell
git add server/Api/Endpoints/AgentEndpoints.cs tests/Antiphon.Tests
git commit -m "feat(agents): expose persistent agent API"
```

## Task 5: Expose Agent Assignment On Board/Card DTOs

**Files:**

- Modify: `server/Application/Dtos/BoardDtos.cs`
- Modify: `server/Application/Services/BoardService.cs`
- Modify: `client/src/api/boards.ts`
- Modify: `client/src/hooks/useSignalRInvalidation.ts`
- Modify: `tests/Antiphon.Tests/Application/BoardServiceIntegrationTests.cs`

- [ ] **Step 1: Write BoardService assertion**

Extend `Board_create_card_and_detail_round_trip_returns_ordered_columns_and_labels` or add a new test that assigns a card to an agent using `AgentService`, then calls `BoardService.GetByIdAsync`.

Assert:

```csharp
var dto = detail.Columns.SelectMany(c => c.Cards).Single(c => c.Id == card.Id);
dto.AssignedAgentId.ShouldBe(agent.Id);
dto.AssignedAgentName.ShouldBe("Frontend Claude");
dto.AgentQueuePosition.ShouldBe(1);
dto.ActiveWorkflowRunId.ShouldNotBeNull();
dto.WorkflowRunStatus.ShouldBe(CardWorkflowRunStatus.Queued);
```

- [ ] **Step 2: Run BoardService test and verify it fails**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~BoardServiceIntegrationTests"
```

Expected: compile fails because `CardDto` lacks the new fields.

- [ ] **Step 3: Extend CardDto**

In `server/Application/Dtos/BoardDtos.cs`, add these fields after `CurrentWorktreeId`:

```csharp
Guid? AssignedAgentId,
string? AssignedAgentName,
int? AgentQueuePosition,
Guid? ActiveWorkflowRunId,
CardWorkflowRunStatus? WorkflowRunStatus,
string? CurrentWorkflowStageName,
```

- [ ] **Step 4: Load and map assignment data**

In `BoardService.LoadBoardAsync`, include:

```csharp
.Include(b => b.Cards).ThenInclude(c => c.AssignedAgent)
.Include(b => b.Cards).ThenInclude(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
```

In `BoardService.ToCardDto`, pass:

```csharp
card.AssignedAgentId,
card.AssignedAgent?.Name,
card.AgentQueuePosition,
card.ActiveWorkflowRunId,
card.ActiveWorkflowRun?.Status,
card.ActiveWorkflowRun?.CurrentStage?.Name,
```

- [ ] **Step 5: Extend frontend CardDto**

In `client/src/api/boards.ts`, add:

```ts
assignedAgentId: string | null
assignedAgentName: string | null
agentQueuePosition: number | null
activeWorkflowRunId: string | null
workflowRunStatus: 'Queued' | 'Running' | 'WaitingForHumanReview' | 'Completed' | 'Failed' | 'Canceled' | null
currentWorkflowStageName: string | null
```

- [ ] **Step 6: Add SignalR invalidation**

In `client/src/hooks/useSignalRInvalidation.ts`, add mappings:

```ts
{
  event: 'AgentChanged',
  getKeys: (p) => [['agents'], ...(p.agentId ? [['agent', p.agentId]] : [])],
},
{
  event: 'AgentQueueChanged',
  getKeys: (p) => [
    ['agents'],
    ...(p.agentId ? [['agent', p.agentId], ['agent', p.agentId, 'queue']] : []),
    ['boards'],
    ...(p.boardId ? [['boards', p.boardId]] : []),
  ],
},
```

Extend `EventPayload` with `agentId?: string`.

- [ ] **Step 7: Run tests**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~BoardServiceIntegrationTests"
npm test -- BoardPage CardModal
```

Expected: all focused backend and frontend tests pass.

- [ ] **Step 8: Commit**

```powershell
git add server/Application/Dtos/BoardDtos.cs server/Application/Services/BoardService.cs client/src/api/boards.ts client/src/hooks/useSignalRInvalidation.ts tests/Antiphon.Tests/Application/BoardServiceIntegrationTests.cs
git commit -m "feat(agents): expose card queue assignment"
```

## Task 6: Add Agents Frontend API And Page

**Files:**

- Modify: `client/src/api/agents.ts`
- Create: `client/src/features/agents/AgentsPage.tsx`
- Create: `client/src/features/agents/AgentCreateModal.tsx`
- Create: `client/src/features/agents/AgentQueueAssignModal.tsx`
- Create: `client/src/features/agents/AgentsPage.test.tsx`
- Modify: `client/src/shared/Layout.tsx`
- Modify: `client/src/App.tsx`

- [ ] **Step 1: Write page tests first**

Create `client/src/features/agents/AgentsPage.test.tsx` with tests:

```tsx
it('renders agent roster with status and queue length', async () => {
  server.use(
    http.get('/api/agents', () =>
      HttpResponse.json([
        {
          id: 'agent-1',
          name: 'Frontend Claude',
          slug: 'frontend-claude',
          workingDirectory: 'D:/src/app',
          details: 'UI work',
          defaultWorkflowTemplateId: 'template-1',
          defaultWorkflowTemplateName: 'One Shot',
          assignmentPolicy: 'AutoPick',
          status: 'Idle',
          persistentSessionId: null,
          currentCardId: null,
          queueLength: 2,
          createdAt: '2026-05-18T09:00:00Z',
          updatedAt: '2026-05-18T09:00:00Z',
        },
      ]),
    ),
  )

  renderWithProviders(<AgentsPage />)

  expect(await screen.findByText('Frontend Claude')).toBeInTheDocument()
  expect(screen.getByText('Idle')).toBeInTheDocument()
  expect(screen.getByText('2 queued')).toBeInTheDocument()
})
```

Add a second test that clicks an agent tile and expects its queue card title from `GET /api/agents/agent-1`.

- [ ] **Step 2: Run test and verify it fails**

Run:

```powershell
npm test -- AgentsPage
```

Expected: fails because `AgentsPage` does not exist.

- [ ] **Step 3: Add persistent agent API functions**

In `client/src/api/agents.ts`, keep launch-definition DTOs and add:

```ts
export type AgentAssignmentPolicy = 'AutoPick' | 'ManualConfirm' | 'Paused'
export type AgentStatus = 'Idle' | 'Ready' | 'Working' | 'WaitingForHumanReview' | 'Stopped' | 'Disconnected' | 'Failed'
export type CardWorkflowRunStatus = 'Queued' | 'Running' | 'WaitingForHumanReview' | 'Completed' | 'Failed' | 'Canceled'

export interface AgentSummaryDto {
  id: string
  name: string
  slug: string
  workingDirectory: string
  details: string
  defaultWorkflowTemplateId: string | null
  defaultWorkflowTemplateName: string | null
  assignmentPolicy: AgentAssignmentPolicy
  status: AgentStatus
  persistentSessionId: string | null
  currentCardId: string | null
  queueLength: number
  createdAt: string
  updatedAt: string
}

export interface AgentQueueCardDto {
  cardId: string
  boardId: string
  boardName: string
  identifier: string
  title: string
  priority: number
  queuePosition: number
  activeWorkflowRunId: string | null
  workflowStatus: CardWorkflowRunStatus | null
  currentStageName: string | null
}

export interface AgentDetailDto extends AgentSummaryDto {
  queue: AgentQueueCardDto[]
}

export interface CreateAgentRequest {
  name: string
  workingDirectory: string
  details?: string | null
  defaultWorkflowTemplateId?: string | null
  assignmentPolicy?: AgentAssignmentPolicy
}

export interface AssignAgentCardRequest {
  cardId: string
}

export const agentKeys = {
  definitions: ['agents', 'definitions'] as const,
  all: ['agents'] as const,
  detail: (id: string) => ['agent', id] as const,
  queue: (id: string) => ['agent', id, 'queue'] as const,
}

export function useAgentList() {
  return useQuery({
    queryKey: agentKeys.all,
    queryFn: () => apiGet<AgentSummaryDto[]>('/agents'),
  })
}

export function useAgent(id: string | null) {
  return useQuery({
    queryKey: id ? agentKeys.detail(id) : ['agent', 'missing'],
    queryFn: () => apiGet<AgentDetailDto>(`/agents/${id}`),
    enabled: !!id,
  })
}

export function useCreateAgent() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: CreateAgentRequest) => apiPost<AgentDetailDto>('/agents', request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: agentKeys.all }),
  })
}

export function useAssignAgentCard(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: AssignAgentCardRequest) => apiPost<AgentDetailDto>(`/agents/${agentId}/queue`, request),
    onSuccess: (agent) => {
      queryClient.setQueryData(agentKeys.detail(agentId), agent)
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
    },
  })
}
```

- [ ] **Step 4: Add AgentsPage**

Create `client/src/features/agents/AgentsPage.tsx` with:

```tsx
export function AgentsPage() {
  const agents = useAgentList()
  const [selectedAgentId, setSelectedAgentId] = useState<string | null>(null)
  const selected = useAgent(selectedAgentId)
  const [createOpen, setCreateOpen] = useState(false)
  const [assignOpen, setAssignOpen] = useState(false)

  useEffect(() => {
    if (!selectedAgentId && agents.data?.[0]) {
      setSelectedAgentId(agents.data[0].id)
    }
  }, [agents.data, selectedAgentId])

  return (
    <Stack gap="md">
      <Group justify="space-between">
        <Title order={2}>Agents</Title>
        <Button leftSection={<TbPlus size={16} />} onClick={() => setCreateOpen(true)}>
          New Agent
        </Button>
      </Group>

      <SimpleGrid cols={{ base: 1, sm: 2, lg: 4 }}>
        {(agents.data ?? []).map((agent) => (
          <Paper
            key={agent.id}
            withBorder
            p="md"
            role="button"
            aria-label={`Agent ${agent.name}`}
            onClick={() => setSelectedAgentId(agent.id)}
            style={{ cursor: 'pointer' }}
          >
            <Group justify="space-between">
              <Text fw={700}>{agent.name}</Text>
              <Badge variant="light">{agent.status}</Badge>
            </Group>
            <Text size="xs" c="dimmed" lineClamp={1}>{agent.workingDirectory}</Text>
            <Text size="sm">{agent.queueLength} queued</Text>
          </Paper>
        ))}
      </SimpleGrid>

      {selected.data && (
        <Paper withBorder p="md">
          <Group justify="space-between" mb="sm">
            <Stack gap={2}>
              <Title order={3}>{selected.data.name}</Title>
              <Text size="sm" c="dimmed">{selected.data.workingDirectory}</Text>
            </Stack>
            <Button variant="light" leftSection={<TbPlus size={16} />} onClick={() => setAssignOpen(true)}>
              Add Card
            </Button>
          </Group>

          <Table>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Position</Table.Th>
                <Table.Th>Card</Table.Th>
                <Table.Th>Board</Table.Th>
                <Table.Th>Workflow</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {selected.data.queue.map((card) => (
                <Table.Tr key={card.cardId}>
                  <Table.Td>{card.queuePosition}</Table.Td>
                  <Table.Td>{card.identifier} - {card.title}</Table.Td>
                  <Table.Td>{card.boardName}</Table.Td>
                  <Table.Td>{card.currentStageName ?? card.workflowStatus ?? '-'}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        </Paper>
      )}

      <AgentCreateModal opened={createOpen} onClose={() => setCreateOpen(false)} />
      {selectedAgentId && (
        <AgentQueueAssignModal
          agentId={selectedAgentId}
          opened={assignOpen}
          onClose={() => setAssignOpen(false)}
        />
      )}
    </Stack>
  )
}
```

Use Mantine imports already common in the repo: `Badge`, `Button`, `Group`, `Paper`, `SimpleGrid`, `Stack`, `Table`, `Text`, `Title`.

- [ ] **Step 5: Add modals**

`AgentCreateModal` fields: name, working directory, details, assignment policy. It calls `useCreateAgent`.

`AgentQueueAssignModal` uses `useBoards`, `useBoard`, and `useAssignAgentCard`:

```tsx
const availableCards = selectedBoard?.columns.flatMap((column) => column.cards)
  .filter((card) => !card.assignedAgentId) ?? []
```

On create/assign success, close the modal and show a green notification.

- [ ] **Step 6: Add nav and route**

In `client/src/shared/Layout.tsx`, add nav item:

```tsx
<Anchor component={NavLink} to="/agents" underline="never" c="dimmed" fw={500}>
  Agents
</Anchor>
```

In `client/src/App.tsx`, import and route:

```tsx
import { AgentsPage } from './features/agents/AgentsPage'
```

```tsx
<Route
  path="agents"
  element={
    <ErrorBoundary fallbackTitle="Agents error">
      <SuspenseBoundary variant="page">
        <AgentsPage />
      </SuspenseBoundary>
    </ErrorBoundary>
  }
/>
```

- [ ] **Step 7: Run frontend tests and build**

Run:

```powershell
npm test -- AgentsPage CardModal
npm run build
```

Expected: tests pass and build completes.

- [ ] **Step 8: Commit**

```powershell
git add client/src/api/agents.ts client/src/features/agents client/src/shared/Layout.tsx client/src/App.tsx
git commit -m "feat(agents): add agents page"
```

## Task 7: Add Playwright E2E Coverage And Screenshots

**Files:**

- Create: `tests/Antiphon.E2E/AgentE2ETests.cs`
- Add screenshot artifact under: `docs/screenshots/agents/01-agent-queue-foundation.png`

- [ ] **Step 1: Write E2E test**

Create `tests/Antiphon.E2E/AgentE2ETests.cs` with a test named:

```csharp
[Test]
public async Task Agents_page_creates_agent_and_assigns_card_to_queue()
```

The test should:

1. Create project, board, and card through API helpers.
2. Create a workflow template directly through `AppDbContext`.
3. Navigate to `/agents`.
4. Click `New Agent`.
5. Fill name `E2E Agent {suffix}`.
6. Fill working directory using a temp repo path.
7. Create the agent.
8. Select the agent.
9. Click `Add Card`.
10. Select the board/card.
11. Confirm the assignment.
12. Assert the queue row contains the card title and workflow stage.
13. Save screenshot to `docs/screenshots/agents/01-agent-queue-foundation.png`.

Use the same Playwright fixture style as `tests/Antiphon.E2E/BoardE2ETests.cs`.

- [ ] **Step 2: Run E2E test and verify it fails before implementation is complete**

Run:

```powershell
dotnet run --project tests/Antiphon.E2E -p:UseAppHost=false -p:OutDir=D:\src\Antiphon\.tmp\e2e-bin\ -- --treenode-filter "/*/*/AgentE2ETests/Agents_page_creates_agent_and_assigns_card_to_queue" --maximum-parallel-tests 1 --no-progress --output Normal
```

Expected before UI/API completion: test fails on missing page or controls.

- [ ] **Step 3: Run E2E test after Tasks 1-6**

Run the same command again.

Expected: test succeeds and writes:

```text
D:\src\Antiphon\docs\screenshots\agents\01-agent-queue-foundation.png
```

- [ ] **Step 4: Clean transient output**

Run:

```powershell
$root = (Resolve-Path .).Path
if (Test-Path .tmp) {
  $tmp = (Resolve-Path .tmp).Path
  if (-not $tmp.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove path outside workspace: $tmp"
  }
  Remove-Item -LiteralPath $tmp -Recurse -Force
}
```

- [ ] **Step 5: Commit**

```powershell
git add tests/Antiphon.E2E/AgentE2ETests.cs docs/screenshots/agents/01-agent-queue-foundation.png
git commit -m "test(agents): cover queue assignment e2e"
```

## Task 8: Final Verification

**Files:**

- No new files.

- [ ] **Step 1: Run backend focused tests**

Run:

```powershell
dotnet test tests/Antiphon.Tests/Antiphon.Tests.csproj --filter "FullyQualifiedName~AgentServiceIntegrationTests|FullyQualifiedName~KanbanPersistenceTests|FullyQualifiedName~BoardServiceIntegrationTests"
```

Expected: all selected tests pass.

- [ ] **Step 2: Run frontend tests**

Run:

```powershell
npm test -- AgentsPage CardModal BoardPage
```

Expected: all selected test files pass.

- [ ] **Step 3: Run frontend build**

Run:

```powershell
npm run build
```

Expected: TypeScript and Vite build pass.

- [ ] **Step 4: Run E2E test**

Run:

```powershell
dotnet run --project tests/Antiphon.E2E -p:UseAppHost=false -p:OutDir=D:\src\Antiphon\.tmp\e2e-bin\ -- --treenode-filter "/*/*/AgentE2ETests/Agents_page_creates_agent_and_assigns_card_to_queue" --maximum-parallel-tests 1 --no-progress --output Normal
```

Expected: one E2E test passes and screenshot exists.

- [ ] **Step 5: Check formatting and git state**

Run:

```powershell
git diff --check
git status --short
```

Expected: `git diff --check` produces no errors. `git status --short` contains only intended source/test/screenshot files before final commit or is clean after commits.

- [ ] **Step 6: Restart local app for manual browser check**

Run backend:

```powershell
cd server
dotnet run --urls "http://localhost:17281"
```

Run frontend in another terminal:

```powershell
cd client
npm run dev
```

Open:

```text
http://localhost:17282/agents
```

Expected: Agents nav item is visible, agents roster loads, a new agent can be created, and a card can be assigned to its queue.

- [ ] **Step 7: Push after all commits**

```powershell
git push origin master
```

Expected: local commits push to `origin/master`.

## Self-Review Notes

Spec coverage in this plan:

- Covered: persistent agent definitions, agent roster/detail UI, manual queue assignment, queue ordering, agent default workflow selection, per-card workflow snapshots, board/card DTO projection, SignalR invalidation, integration tests, frontend tests, and Playwright E2E coverage.
- Deferred to separate plans: durable terminal session reuse, adapter-specific launch/resume session ids, live stage execution against persistent agents, human-review blocking tied to runtime execution, memory compaction files, and agent handoff.

Type consistency check:

- Backend uses `Agent`, `AgentAssignmentPolicy`, `AgentStatus`, `CardWorkflowRun`, `CardWorkflowStage`, `CardWorkflowRunStatus`, and `CardWorkflowStageStatus`.
- Frontend mirrors those names with string-union DTO types in `client/src/api/agents.ts` and `client/src/api/boards.ts`.
- Query keys use `agentKeys.definitions`, `agentKeys.all`, `agentKeys.detail(id)`, and `agentKeys.queue(id)`.
