using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0090 S2: CreateAsync with -Complexity. Isolated schema: unique chain index plus holds.
/// </summary>
[Category("Integration")]
public class ComplexityCreateTests
{
    [Test]
    public async Task Hard_with_fable_held_creates_on_the_next_candidate_and_warns()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", until: DateTime.UtcNow.AddDays(1));

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Complexity.ShouldBe(TaskComplexity.Hard);
        created.Routing.ShouldNotBeNull();
        created.Routing!.Candidates[0].Outcome.ShouldBe("skipped");
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("fable");
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.Complexity.ShouldBe(TaskComplexity.Hard);
        var createdEvent = await db.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        createdEvent.Detail.ShouldContain("complexity=Hard");
        createdEvent.Detail.ShouldContain("opus");
    }

    [Test]
    public async Task All_held_returns_200_Blocked_with_a_Blocked_event_and_parent_note()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", until: null);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus", until: null);
        await SeedHoldAsync(db, AgentKind.Grok, "grok-4.6", until: null);
        var sessionId = await SeedSessionAsync(db, workspace.Path);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard),
            new AgentTaskService.Caller(null, sessionId, workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Blocked);
        created.Routing.ShouldNotBeNull();
        created.Routing!.Candidates.ShouldAllBe(c => c.Outcome == "skipped");
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
        created.Warning.ShouldContain("do not pick a kind yourself");
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureReason.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
        (await db.SessionQueuedMessages.CountAsync(m => m.SourceTaskId == created.Id)).ShouldBe(1);
    }

    [Test]
    public async Task RefuseIfExhausted_is_409_routing_exhausted_with_the_walk_extension()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", until: null);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus", until: null);
        await SeedHoldAsync(db, AgentKind.Grok, "grok-4.6", until: null);

        var ex = await Should.ThrowAsync<RoutingExhaustedException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    Complexity: TaskComplexity.Hard,
                    RefuseIfExhausted: true),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("routing_exhausted");
        ex.StatusCode.ShouldBe(409);
        ex.Routing.Candidates.Count.ShouldBe(3);
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Plan_Hard_cell_with_head_held_creates_on_candidate_2_and_names_chain_Plan_Hard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db, AgentTaskRole.Plan);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", until: DateTime.UtcNow.AddDays(1));

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("fable");
        created.Routing!.ChainRole.ShouldBe(AgentTaskRole.Plan);
        var createdEvent = await db.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        createdEvent.Detail.ShouldContain("chain=Plan/Hard");
        createdEvent.Detail.ShouldContain("candidate 2/");
        createdEvent.Detail.ShouldContain("opus");
    }

    [Test]
    public async Task Code_Hard_with_only_the_any_role_row_names_chain_Hard()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("write it", Role: AgentTaskRole.Code, Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.Routing!.ChainRole.ShouldBeNull();
        var createdEvent = await db.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        createdEvent.Detail.ShouldContain("chain=Hard");
        createdEvent.Detail.ShouldNotContain("chain=Code/Hard");
        createdEvent.Detail.ShouldNotContain("chain=Plan/Hard");
    }

    [Test]
    public async Task All_cells_empty_blocks_with_the_D3_sentence_and_a_parent_note()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var sessionId = await SeedSessionAsync(db, workspace.Path);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard),
            new AgentTaskService.Caller(null, sessionId, workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Blocked);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("Plan/Hard chain is empty");
        created.Warning.ShouldContain("no Plan/Hard row");
        created.Warning.ShouldContain("no any-role Hard row");
        created.Warning.ShouldContain("no config default");
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.FailureReason.ShouldContain("Plan/Hard chain is empty");
        (await db.AgentTaskEvents.CountAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Blocked)).ShouldBe(1);
        var note = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == created.Id);
        note.Body.ShouldContain("Plan/Hard chain is empty");
    }

    [Test]
    public async Task Explicit_kind_plus_complexity_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.Grok,
                    Complexity: TaskComplexity.Hard),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.StatusCode.ShouldBe(422);
        ex.Errors.ShouldContainKey("Complexity");
    }

    [Test]
    public async Task IgnoreModelDisabled_plus_complexity_is_422()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    Complexity: TaskComplexity.Hard,
                    IgnoreModelDisabled: true),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.StatusCode.ShouldBe(422);
        ex.Errors.ShouldContainKey("IgnoreModelDisabled");
    }

    [Test]
    public async Task No_complexity_is_unchanged_when_fable_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", until: DateTime.UtcNow.AddHours(1));

        var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("model_disabled");
    }

    [Test]
    public async Task A_required_pin_plus_complexity_uses_the_pin_pair_and_warns_bypass()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await SeedHardChainAsync(db);
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0301",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.Codex,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "Plan is Sol, full stop"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301", Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Codex);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("complexity chain bypassed");
        created.Routing!.Candidates.ShouldHaveSingleItem();
        created.Routing.Walked.ShouldBeFalse();
    }

    [Test]
    public async Task A_required_pin_to_a_held_alias_still_409s()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await SeedHardChainAsync(db);
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0301",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.ClaudeCode,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "CARD-0301 stays on fable"),
            null,
            CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", until: DateTime.UtcNow.AddHours(4));

        var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301", Complexity: TaskComplexity.Hard),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("model_disabled");
        ex.Message.ShouldContain("does not satisfy the pin");
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_preferred_pin_is_tried_first()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0304");
        await SeedHardChainAsync(db);
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0304",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Preferred,
                AgentKind: AgentKind.Grok,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "prefer grok"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it", Role: AgentTaskRole.Plan, Card: "CARD-0304", Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok);
        created.Routing!.Candidates[0].Origin.ShouldBe("pin");
        created.Routing.Candidates[0].Outcome.ShouldBe("chosen");
    }

    [Test]
    public async Task Stage_forbid_skips_instead_of_409()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                ForbiddenAliases: ["fable"],
                Reason: "no fable for Plan"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Routing!.Candidates[0].Reason.ShouldBe("forbidden by stage pin");
    }

    [Test]
    public async Task Orchestrator_Hard_skips_non_Claude_candidates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
            [
                new(AgentKind.Grok, AgentModelLevel.Frontier),
                new(AgentKind.Codex, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
            ]),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "own the chunk",
                Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Plan,
                Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.Routing!.Candidates[0].Reason.ShouldBe("not a delegate kind");
        created.Routing.Candidates[1].Reason.ShouldBe("not a delegate kind");
    }

    private static async Task SeedHardChainAsync(AppDbContext db, AgentTaskRole? role = null)
    {
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
            Role = role,
            Complexity = TaskComplexity.Hard,
            CandidatesJson = ComplexityChain.SerializeCandidates(
            [
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
                new(AgentKind.Grok, AgentModelLevel.Frontier),
            ]),
            Provenance = RoutingPinProvenance.Human,
            Reason = "test Hard chain",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedHoldAsync(AppDbContext db, AgentKind kind, string alias, DateTime? until)
    {
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = "manual hold",
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedSessionAsync(AppDbContext db, string directory)
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        db.AgentSessions.Add(new AgentSession
        {
            Id = id,
            DefinitionName = "test",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = directory,
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now,
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static AgentTaskService.Caller Manual(string directory) => new(null, null, directory);

    private static RoutingPinService Pins(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);

    private static AgentTaskService Service(AppDbContext db, TempWorkspace workspace)
    {
        var settings = Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] });
        var availability = new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
        var routing = new ComplexityRoutingService(
            db, settings, TimeProvider.System, availability);
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            settings,
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: availability,
            routingPins: Pins(db),
            complexityRouting: routing);
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-complexity-create").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
