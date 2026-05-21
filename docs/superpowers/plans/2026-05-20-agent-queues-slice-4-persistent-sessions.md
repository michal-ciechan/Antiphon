# Agent Queues Slice 4 Persistent Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the existing session runtime into the agent model so the Agents page can start, resume, stop, and inspect a persistent session for the agent's current card.

**Architecture:** Add an agent-facing orchestration layer on the server (`AgentControlService`) that selects the current card and delegates actual process work to the existing `CardService` and `AgentSessionService`. Keep `AgentSessionService` responsible for syncing assigned-agent state whenever a session starts, resumes, fails, or stops, and expose the current card plus active session in the agent detail projection so the React agents UI can reuse the existing terminal transport.

**Tech Stack:** ASP.NET Core Minimal APIs, EF Core/PostgreSQL, SignalR, React 19, Mantine 8, TanStack Query 5, Vitest, TUnit.

---

## File Map

- `server/Application/Dtos/AgentDtos.cs`
  Adds the request contracts for agent lifecycle actions and extends `AgentDetailDto` with `CurrentCard` and `ActiveSession`.
- `server/Application/Services/AgentControlService.cs`
  New agent-facing lifecycle service that selects the current card and calls the existing session/card services.
- `server/Application/Services/AgentService.cs`
  Keeps CRUD/queue responsibilities, but expands detail projection so the UI can show current card + active session.
- `server/Application/Services/AgentSessionService.cs`
  Syncs assigned-agent state after session start, resume, failure, and stop, regardless of whether the action came from the agent page or the board page.
- `server/Api/Endpoints/AgentEndpoints.cs`
  Adds `POST /api/agents/{id}/start`, `POST /api/agents/{id}/resume`, and `POST /api/agents/{id}/stop`.
- `server/Program.cs`
  Registers `AgentControlService`.
- `tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs`
  New integration tests for agent-level start/resume/stop behavior.
- `tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs`
  Adds sync tests proving assigned agents are updated when sessions change state.
- `client/src/api/agents.ts`
  Adds the expanded agent detail types and mutations for agent start/resume/stop.
- `client/src/features/agents/AgentSessionPanel.tsx`
  New UI component that shows session state, control buttons, and the existing terminal component.
- `client/src/features/agents/AgentsPage.tsx`
  Renders the current card/session block and wires the new mutations.
- `client/src/features/agents/AgentsPage.test.tsx`
  Covers start/resume/stop UI behavior and active-session rendering.

## Scope Guard

- This slice **does** add agent-level start/resume/stop and current-card/session visibility.
- This slice **does not** implement auto-pick, Human Review blocking transitions, compaction, or cross-card session carry-over. A stopped session is still resumed for the same current card.
- Keep the current `AgentSession` persistence model intact. Do not redesign session-to-card ownership in this slice.

### Task 1: Add Failing Agent Lifecycle Integration Tests

**Files:**
- Create: `tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs`

- [ ] **Step 1: Write the failing test file for agent start/resume/stop**

```csharp
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentLifecycle")]
public class AgentControlServiceIntegrationTests
{
    [Test]
    public async Task StartAsync_selects_queue_head_and_records_current_card_and_persistent_session()
    {
        await using var harness = await AgentControlHarness.CreateAsync();
        var agent = await harness.CreateAgentAsync();
        await harness.AssignCardAsync(agent.Id, harness.Graph.CardA.Id);
        await harness.AssignCardAsync(agent.Id, harness.Graph.CardB.Id);

        var detail = await harness.Control.StartAsync(
            agent.Id,
            new StartAgentRequest(DefinitionName: "fake", Cols: 120, Rows: 30),
            CancellationToken.None);

        detail.Status.ShouldBe(AgentStatus.Working);
        detail.CurrentCard.ShouldNotBeNull();
        detail.CurrentCard!.CardId.ShouldBe(harness.Graph.CardA.Id);
        detail.ActiveSession.ShouldNotBeNull();
        detail.PersistentSessionId.ShouldBe(detail.ActiveSession!.Id.ToString("D"));
    }

    [Test]
    public async Task StopAsync_stops_the_persistent_session_and_preserves_resume_state()
    {
        await using var harness = await AgentControlHarness.CreateAsync();
        var agent = await harness.CreateStartedAgentAsync();

        var detail = await harness.Control.StopAsync(agent.Id, CancellationToken.None);

        detail.Status.ShouldBe(AgentStatus.Stopped);
        detail.CurrentCardId.ShouldNotBeNull();
        detail.PersistentSessionId.ShouldNotBeNull();
    }

    [Test]
    public async Task ResumeAsync_reuses_the_recorded_persistent_session()
    {
        await using var harness = await AgentControlHarness.CreateAsync();
        var agent = await harness.CreateStartedAgentAsync();
        var stopped = await harness.Control.StopAsync(agent.Id, CancellationToken.None);

        var resumed = await harness.Control.ResumeAsync(
            stopped.Id,
            new ResumeAgentRequest(AgentSessionResumeMode.Resume),
            CancellationToken.None);

        resumed.Status.ShouldBe(AgentStatus.Working);
        resumed.PersistentSessionId.ShouldBe(stopped.PersistentSessionId);
        resumed.ActiveSession.ShouldNotBeNull();
        resumed.ActiveSession!.Status.ShouldBe(SessionStatus.Running);
    }
}
```

- [ ] **Step 2: Run the new backend tests to verify they fail**

Run:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentControlServiceIntegrationTests/*"
```

Expected: FAIL because `AgentControlService`, `StartAgentRequest`, and `ResumeAgentRequest` do not exist yet.

- [ ] **Step 3: Commit the failing tests**

```bash
git add tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs
git commit -m "test: cover agent lifecycle controls"
```

### Task 2: Implement Server-Side Agent Lifecycle Controls

**Files:**
- Create: `server/Application/Services/AgentControlService.cs`
- Modify: `server/Application/Dtos/AgentDtos.cs`
- Modify: `server/Api/Endpoints/AgentEndpoints.cs`
- Modify: `server/Program.cs`

- [ ] **Step 1: Add the new agent lifecycle request contracts and detail fields**

```csharp
public sealed record StartAgentRequest(
    string? DefinitionName = null,
    int Cols = 120,
    int Rows = 30,
    string? Prompt = null);

public sealed record ResumeAgentRequest(
    AgentSessionResumeMode Mode = AgentSessionResumeMode.Resume);

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
    AgentQueueCardDto? CurrentCard,
    AgentSessionSummaryDto? ActiveSession,
    IReadOnlyList<AgentQueueCardDto> Queue,
    DateTime CreatedAt,
    DateTime UpdatedAt);
```

- [ ] **Step 2: Create `AgentControlService` as the agent-facing orchestration layer**

```csharp
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed class AgentControlService
{
    private readonly AppDbContext _db;
    private readonly AgentService _agentService;
    private readonly CardService _cardService;
    private readonly AgentSessionService _agentSessionService;
    private readonly AgentRegistry _agentRegistry;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public AgentControlService(
        AppDbContext db,
        AgentService agentService,
        CardService cardService,
        AgentSessionService agentSessionService,
        AgentRegistry agentRegistry,
        IEventBus eventBus,
        TimeProvider timeProvider)
    {
        _db = db;
        _agentService = agentService;
        _cardService = cardService;
        _agentSessionService = agentSessionService;
        _agentRegistry = agentRegistry;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<AgentDetailDto> StartAsync(Guid agentId, StartAgentRequest request, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);
        var card = await ResolveCurrentOrQueuedCardAsync(agent, ct);

        var spawn = await _cardService.SpawnAsync(
            card.Id,
            new SpawnCardRequest(
                request.DefinitionName,
                request.Cols,
                request.Rows,
                request.Prompt,
                card.ConcurrencyToken),
            ct);

        agent.CurrentCardId = card.Id;
        agent.PersistentSessionId = spawn.SessionId.ToString("D");
        agent.Status = AgentStatus.Working;
        agent.UpdatedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);

        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    public async Task<AgentDetailDto> ResumeAsync(Guid agentId, ResumeAgentRequest request, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);
        var sessionId = ParsePersistentSession(agent);
        var session = await _db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new ConflictException($"Agent '{agent.Name}' has no persisted session to resume.");
        var spec = _agentRegistry.Resolve(session.DefinitionName, new AgentLaunchOptions(
            Cwd: session.Cwd,
            Cols: session.Cols,
            Rows: session.Rows));

        await _agentSessionService.ResumeAsync(sessionId, spec, request.Mode, ct);
        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    public async Task<AgentDetailDto> StopAsync(Guid agentId, CancellationToken ct)
    {
        var agent = await LockAgentAsync(agentId, ct);
        var sessionId = ParsePersistentSession(agent);
        await _agentSessionService.KillAsync(sessionId, ct);
        return await _agentService.GetByIdAsync(agent.Id, ct);
    }

    private async Task<Agent> LockAgentAsync(Guid agentId, CancellationToken ct) =>
        await _db.Agents
            .FromSqlInterpolated($"""SELECT * FROM "Agents" WHERE "Id" = {agentId} FOR UPDATE""")
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException(nameof(Agent), agentId);
}
```

- [ ] **Step 3: Add the new `/api/agents/{id}/start|resume|stop` endpoints**

```csharp
agents.MapPost("/{id:guid}/start", async (
    Guid id,
    StartAgentRequest request,
    AgentControlService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.StartAsync(id, request, cancellationToken));
});

agents.MapPost("/{id:guid}/resume", async (
    Guid id,
    ResumeAgentRequest request,
    AgentControlService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.ResumeAsync(id, request, cancellationToken));
});

agents.MapPost("/{id:guid}/stop", async (
    Guid id,
    AgentControlService service,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await service.StopAsync(id, cancellationToken));
});
```

- [ ] **Step 4: Register the new service in `Program.cs`**

```csharp
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<AgentControlService>();
builder.Services.AddScoped<AgentDraftService>();
```

- [ ] **Step 5: Run the backend control tests to verify they pass**

Run:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentControlServiceIntegrationTests/*"
```

Expected: PASS for the new agent start/resume/stop integration tests.

- [ ] **Step 6: Commit the backend lifecycle layer**

```bash
git add server/Application/Dtos/AgentDtos.cs server/Application/Services/AgentControlService.cs server/Api/Endpoints/AgentEndpoints.cs server/Program.cs tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs
git commit -m "feat(agent): add lifecycle controls"
```

### Task 3: Sync Assigned-Agent State Inside `AgentSessionService`

**Files:**
- Modify: `server/Application/Services/AgentSessionService.cs`
- Modify: `server/Application/Services/AgentService.cs`
- Modify: `tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs`

- [ ] **Step 1: Add failing sync tests to `AgentSessionServiceIntegrationTests.cs`**

```csharp
[Test]
public async Task StartAsync_updates_assigned_agent_to_working_and_records_persistent_session()
{
    await using var db = CreateContext();
    var tempRoot = Path.Combine(Path.GetTempPath(), $"antiphon-agent-sync-{Guid.NewGuid():N}");
    Directory.CreateDirectory(tempRoot);

    try
    {
        var graph = CreateGraph(Path.Combine(tempRoot, "repo"));
        var now = DateTime.UtcNow;
        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            Name = "Frontend Claude",
            Slug = "frontend-claude",
            WorkingDirectory = tempRoot,
            AssignmentPolicy = AgentAssignmentPolicy.AutoPick,
            Status = AgentStatus.Idle,
            CreatedAt = now,
            UpdatedAt = now
        };
        graph.Card.AssignedAgentId = agent.Id;
        graph.Card.AssignedAgent = agent;
        db.Add(graph.Project);
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var eventBus = new MockEventBus();
        var adapter = new FakeAgentProtocolAdapter { PromptOutput = "SYNC_OK" };
        await using var provider = BuildProvider();
        var (service, _) = BuildServiceWithFakes(
            db,
            eventBus,
            provider,
            adapter,
            Path.Combine(tempRoot, "worktree"),
            CreateSessionSettings(tempRoot));

        var result = await service.StartAsync(
            new StartAgentSessionRequest(graph.Card.Id, "fake", AgentKind.Raw, "hello"),
            new AgentLaunchSpec("fake", AgentKind.Raw, "fake", [], new Dictionary<string, string>(), tempRoot, 120, 30),
            CancellationToken.None);

        var persistedAgent = await db.Agents.SingleAsync(a => a.Id == agent.Id);
        persistedAgent.Status.ShouldBe(AgentStatus.Working);
        persistedAgent.CurrentCardId.ShouldBe(graph.Card.Id);
        persistedAgent.PersistentSessionId.ShouldBe(result.SessionId.ToString("D"));
        eventBus.PublishedEvents.ShouldContain(e => e.EventName == "AgentChanged");
    }
    finally
    {
        DeleteDirectoryBestEffort(tempRoot);
    }
}
```

- [ ] **Step 2: Run the sync test to verify it fails before implementation**

Run:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentSessionServiceIntegrationTests/StartAsync_updates_assigned_agent_to_working_and_records_persistent_session"
```

Expected: FAIL because `AgentSessionService` currently does not update the assigned `Agent` row.

- [ ] **Step 3: Add an internal helper in `AgentSessionService` that maps session state to agent state**

```csharp
private async Task SyncAssignedAgentAsync(
    Guid? assignedAgentId,
    Guid cardId,
    Guid sessionId,
    SessionStatus sessionStatus,
    CancellationToken ct)
{
    if (assignedAgentId is not Guid agentId)
        return;

    var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
    if (agent is null)
        return;

    agent.CurrentCardId = cardId;
    agent.PersistentSessionId = sessionId.ToString("D");
    agent.Status = sessionStatus switch
    {
        SessionStatus.Running => AgentStatus.Working,
        SessionStatus.Stopped => AgentStatus.Stopped,
        SessionStatus.Failed => AgentStatus.Failed,
        _ => agent.Status
    };
    agent.UpdatedAt = UtcNow();
    await _db.SaveChangesAsync(ct);
    await _eventBus.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agent.Id), ct);
}
```

- [ ] **Step 4: Call the helper from session start, resume, kill, and failure paths**

```csharp
await WaitForReadyOrThrowAsync(adapter, ct);
session.Status = SessionStatus.Running;
session.LastSeenAt = UtcNow();
await _db.SaveChangesAsync(ct);
await SyncAssignedAgentAsync(card.AssignedAgentId, card.Id, session.Id, session.Status, ct);
```

```csharp
session.Status = memoryKilled
    ? SessionStatus.Failed
    : killed ? SessionStatus.Stopped : SessionStatus.Failed;
session.EndedAt = UtcNow();
session.LastSeenAt = session.EndedAt.Value;
await _db.SaveChangesAsync(ct);
await SyncAssignedAgentAsync(card?.AssignedAgentId, session.CardId, session.Id, session.Status, ct);
```

- [ ] **Step 5: Extend `AgentService` detail projection to expose `CurrentCard` and `ActiveSession`**

```csharp
var currentCard = agent.CurrentCard is null
    ? null
    : new AgentQueueCardDto(
        agent.CurrentCard.Id,
        agent.CurrentCard.BoardId,
        agent.CurrentCard.Board.Name,
        agent.CurrentCard.Identifier,
        agent.CurrentCard.Title,
        agent.CurrentCard.Priority,
        agent.CurrentCard.AgentQueuePosition ?? 0,
        agent.CurrentCard.ActiveWorkflowRunId,
        agent.CurrentCard.ActiveWorkflowRun?.Status,
        agent.CurrentCard.ActiveWorkflowRun?.CurrentStage?.Name);

var activeSession = TryParsePersistentSessionId(agent.PersistentSessionId, out var persistentSessionId)
    ? agent.CurrentCard?.AgentSessions
        .Where(s => s.Id == persistentSessionId)
        .OrderByDescending(s => s.CreatedAt)
        .Select(s => new AgentSessionSummaryDto(
            s.Id,
            s.DefinitionName,
            s.AgentKind,
            s.Status,
            s.Cwd,
            s.CreatedAt,
            s.StartedAt,
            s.LastSeenAt,
            s.EndedAt,
            s.ExitCode,
            s.FailureReason))
        .FirstOrDefault()
    : null;
```

- [ ] **Step 6: Run both backend suites to verify the lifecycle and sync behavior**

Run:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentControlServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentSessionServiceIntegrationTests/*assigned_agent*"
```

Expected: PASS for the lifecycle control tests and the new assigned-agent sync coverage.

- [ ] **Step 7: Commit the sync + projection work**

```bash
git add server/Application/Services/AgentSessionService.cs server/Application/Services/AgentService.cs tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs
git commit -m "feat(agent): sync agent state from session lifecycle"
```

### Task 4: Add Agents Page Session Controls And Terminal

**Files:**
- Modify: `client/src/api/agents.ts`
- Create: `client/src/features/agents/AgentSessionPanel.tsx`
- Modify: `client/src/features/agents/AgentsPage.tsx`
- Modify: `client/src/features/agents/AgentsPage.test.tsx`

- [ ] **Step 1: Extend the agent API client with current-card/active-session fields and mutations**

```ts
import type { AgentSessionResumeMode } from './sessions'
import type { AgentSessionSummaryDto } from './boards'

export interface StartAgentRequest {
  definitionName?: string | null
  cols?: number
  rows?: number
  prompt?: string | null
}

export interface ResumeAgentRequest {
  mode?: AgentSessionResumeMode
}

export interface AgentDetailDto extends AgentSummaryDto {
  currentCard: AgentQueueCardDto | null
  activeSession: AgentSessionSummaryDto | null
  queue: AgentQueueCardDto[]
}

export function useStartAgent(agentId: string) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: (request: StartAgentRequest) => apiPost<AgentDetailDto>(`/agents/${agentId}/start`, request),
    onSuccess: (agent) => {
      queryClient.setQueryData(agentKeys.detail(agentId), agent)
      queryClient.invalidateQueries({ queryKey: agentKeys.all })
    },
  })
}
```

- [ ] **Step 2: Create `AgentSessionPanel.tsx` that reuses `SessionTerminal`**

```tsx
import { Alert, Badge, Button, Group, Paper, Stack, Text } from '@mantine/core'
import { TbPlayerPlay, TbPlayerStop, TbRefresh } from 'react-icons/tb'
import type { AgentDetailDto } from '../../api/agents'
import { useResumeAgent, useStartAgent, useStopAgent } from '../../api/agents'
import { SessionTerminal } from '../board/SessionTerminal'

interface AgentSessionPanelProps {
  agent: AgentDetailDto
}

export function AgentSessionPanel({ agent }: AgentSessionPanelProps) {
  const start = useStartAgent(agent.id)
  const resume = useResumeAgent(agent.id)
  const stop = useStopAgent(agent.id)
  const canStart = !agent.activeSession && !!agent.currentCard
  const canResume = !!agent.activeSession && (agent.activeSession.status === 'Stopped' || agent.activeSession.status === 'Failed')
  const canStop = !!agent.activeSession && (agent.activeSession.status === 'Starting' || agent.activeSession.status === 'Running')

  return (
    <Paper withBorder p="md">
      <Stack gap="sm">
        <Group justify="space-between">
          <Stack gap={2}>
            <Text fw={700}>Session</Text>
            <Text size="sm" c="dimmed">
              {agent.currentCard ? `${agent.currentCard.identifier} - ${agent.currentCard.title}` : 'No current card selected'}
            </Text>
          </Stack>
          <Group gap="xs">
            {canStart && (
              <Button leftSection={<TbPlayerPlay size={14} />} onClick={() => start.mutate({})}>
                Start
              </Button>
            )}
            {canResume && (
              <Button variant="light" leftSection={<TbRefresh size={14} />} onClick={() => resume.mutate({ mode: 'Resume' })}>
                Resume
              </Button>
            )}
            {canStop && (
              <Button color="red" variant="light" leftSection={<TbPlayerStop size={14} />} onClick={() => stop.mutate()}>
                Stop
              </Button>
            )}
          </Group>
        </Group>
        {agent.activeSession ? (
          <>
            <Badge variant="light">{agent.activeSession.status}</Badge>
            <SessionTerminal session={agent.activeSession} fill />
          </>
        ) : (
          <Alert color="gray" variant="light">No active session for this agent yet.</Alert>
        )}
      </Stack>
    </Paper>
  )
}
```

- [ ] **Step 3: Render the new panel from `AgentsPage.tsx`**

```tsx
{selected.data && (
  <Stack gap="md">
    <Paper withBorder p="md">
      {/* existing summary block */}
    </Paper>
    <AgentSessionPanel agent={selected.data} />
  </Stack>
)}
```

- [ ] **Step 4: Add UI tests for start/resume/stop rendering and mutation calls**

```tsx
it('starts the current agent card from the agents page', async () => {
  const startSpy = vi.fn()
  server.use(
    http.get('/api/agents', () => HttpResponse.json([agentSummary])),
    http.get('/api/agents/:id', () => HttpResponse.json({
      ...agentDetail,
      currentCard: {
        cardId: 'card-1',
        boardId: 'board-1',
        boardName: 'Delivery',
        identifier: 'CARD-0001',
        title: 'Build agent UI',
        priority: 1,
        queuePosition: 1,
        activeWorkflowRunId: 'run-1',
        workflowStatus: 'Queued',
        currentStageName: 'Implement',
      },
      activeSession: null,
    })),
    http.post('/api/agents/agent-1/start', async ({ request }) => {
      startSpy(await request.json())
      return HttpResponse.json({
        ...agentDetail,
        status: 'Working',
        persistentSessionId: 'session-1',
        currentCardId: 'card-1',
        currentCard: {
          cardId: 'card-1',
          boardId: 'board-1',
          boardName: 'Delivery',
          identifier: 'CARD-0001',
          title: 'Build agent UI',
          priority: 1,
          queuePosition: 1,
          activeWorkflowRunId: 'run-1',
          workflowStatus: 'Running',
          currentStageName: 'Implement',
        },
        activeSession: {
          id: 'session-1',
          definitionName: 'fake',
          agentKind: 'Raw',
          status: 'Running',
          cwd: 'D:/src/app',
          createdAt: '2026-05-20T10:00:00Z',
          startedAt: '2026-05-20T10:00:00Z',
          lastSeenAt: '2026-05-20T10:00:01Z',
          endedAt: null,
          exitCode: null,
          failureReason: null,
        },
        queue: [],
      })
    }),
  )

  renderWithProviders(<AgentsPage />)
  await userEvent.click(await screen.findByRole('button', { name: 'Start' }))
  await waitFor(() => expect(startSpy).toHaveBeenCalledWith({}))
})
```

- [ ] **Step 5: Run the frontend agent tests**

Run:

```powershell
cd client
npm run test -- src/features/agents/AgentsPage.test.tsx
```

Expected: PASS for the updated agents page tests.

- [ ] **Step 6: Commit the agents UI**

```bash
git add client/src/api/agents.ts client/src/features/agents/AgentSessionPanel.tsx client/src/features/agents/AgentsPage.tsx client/src/features/agents/AgentsPage.test.tsx
git commit -m "feat(agent-ui): add session controls to agents page"
```

### Task 5: Final Verification

**Files:**
- Modify: none unless verification exposes gaps

- [ ] **Step 1: Run the targeted backend integration coverage**

Run:

```powershell
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentControlServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/Antiphon.Tests/AgentSessionServiceIntegrationTests/*assigned_agent*"
```

Expected: PASS

- [ ] **Step 2: Run the targeted frontend test coverage**

Run:

```powershell
cd client
npm run test -- src/features/agents/AgentsPage.test.tsx
```

Expected: PASS

- [ ] **Step 3: Run a smoke build for the client**

Run:

```powershell
cd client
npm run build
```

Expected: `vite build` completes successfully.

- [ ] **Step 4: Verify the working tree before handoff**

Run:

```powershell
git status --short
```

Expected: only the planned slice-4 implementation files are modified.

- [ ] **Step 5: Commit the verification pass if any fixups were required**

```bash
git add -A
git commit -m "test: verify agent persistent session slice"
```

