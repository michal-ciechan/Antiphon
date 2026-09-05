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
/// CARD-0322 S2: walked create from a pin list. Isolated schema: unique stage pin index plus holds.
/// </summary>
[Category("Integration")]
public sealed class RoutingPinCandidateCreateTests
{
    [Test]
    public async Task Required_list_skips_a_held_head_and_stamps_RoutingPinId()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await Pins(db).UpsertAsync(RequiredList("CARD-0301"), null, CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", DateTime.UtcNow.AddHours(4));

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Routing.ShouldNotBeNull();
        created.Routing!.Walked.ShouldBeTrue();
        created.Routing.Source.ShouldContain("CARD-0301");
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("fable");
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.RoutingPinId.ShouldNotBeNull();
        var createdEvent = await db.AgentTaskEvents.SingleAsync(
            e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        createdEvent.Detail.ShouldContain("candidate 2/2");
        createdEvent.Detail.ShouldContain("opus");
    }

    [Test]
    public async Task Required_list_all_held_returns_200_Blocked_naming_the_pin()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await Pins(db).UpsertAsync(RequiredList("CARD-0301"), null, CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", null);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus", null);
        var sessionId = await SeedSessionAsync(db, workspace.Path);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
            new AgentTaskService.Caller(null, sessionId, workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Blocked);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
        created.Warning.ShouldContain("CARD-0301");
        created.Warning.ShouldContain("do not pick a kind yourself");
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.Status.ShouldBe(AgentTaskStatus.Blocked);
        task.FailureReason.ShouldContain("CARD-0301 Plan pin (human, required)");
        task.RoutingPinId.ShouldNotBeNull();
    }

    [Test]
    public async Task RefuseIfExhausted_on_a_walked_pin_is_409_routing_exhausted()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await Pins(db).UpsertAsync(RequiredList("CARD-0301"), null, CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", null);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus", null);

        var ex = await Should.ThrowAsync<RoutingExhaustedException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    Card: "CARD-0301",
                    RefuseIfExhausted: true),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("routing_exhausted");
        ex.StatusCode.ShouldBe(409);
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Required_single_candidate_held_is_still_409_model_disabled_with_coda()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
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
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", DateTime.UtcNow.AddHours(4));

        var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("model_disabled");
        ex.Message.ShouldContain("does not satisfy the pin");
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Preferred_list_falls_through_to_role_policy_when_every_pin_candidate_is_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Preferred,
                Candidates:
                [
                    new(AgentKind.Codex, AgentModelLevel.Frontier),
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                ],
                Reason: "prefer sol then grok"),
            null,
            CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.Codex, "gpt-6-astra", null);
        await SeedHoldAsync(db, AgentKind.Grok, "grok-4.6", null);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        created.Routing!.Candidates[^1].Origin.ShouldBe(RoutingCandidates.OriginRolePolicy);
        created.Routing.Candidates[^1].Outcome.ShouldBe("chosen");
    }

    [Test]
    public async Task Preferred_role_policy_fallback_is_filtered_by_stage_forbid()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Preferred,
                Candidates:
                [
                    new(AgentKind.Codex, AgentModelLevel.Frontier),
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                ],
                ForbiddenAliases: ["fable"],
                Reason: "prefer sol/grok, never fable"),
            null,
            CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.Codex, "gpt-6-astra", null);
        await SeedHoldAsync(db, AgentKind.Grok, "grok-4.6", null);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Blocked);
        created.Warning.ShouldContain(ComplexityRoutingService.RoutingExhaustedPrefix);
        created.Routing!.Candidates[^1].Reason.ShouldBe("forbidden by stage pin");
    }

    [Test]
    public async Task Explicit_kind_narrows_the_walk_to_matching_candidates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                Candidates:
                [
                    new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                    new(AgentKind.ClaudeCode, AgentModelLevel.High),
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                ],
                Reason: "fable, opus, grok"),
            null,
            CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", DateTime.UtcNow.AddHours(1));

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it", Role: AgentTaskRole.Plan, AgentKind: AgentKind.ClaudeCode),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Routing!.Candidates.Count.ShouldBe(2);
        created.Routing.Candidates.ShouldAllBe(c => c.AgentKind == AgentKind.ClaudeCode);
        var stored = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        stored.ExplicitAgentKind.ShouldBe(AgentKind.ClaudeCode);
        stored.ExplicitModelLevel.ShouldBeNull();
    }

    [Test]
    public async Task Explicit_kind_only_narrows_to_a_single_non_head_survivor_and_is_not_walked()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(GrokThenClaudeHigh(), null, CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it", Role: AgentTaskRole.Plan, AgentKind: AgentKind.ClaudeCode),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Routing.ShouldBeNull();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.RoutingPinId.ShouldBeNull();
        task.ExplicitAgentKind.ShouldBe(AgentKind.ClaudeCode);
        task.ExplicitModelLevel.ShouldBeNull();
        task.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        task.ModelLevel.ShouldBe(AgentModelLevel.High);
    }

    [Test]
    public async Task Explicit_level_only_narrows_to_a_single_non_head_survivor_and_is_not_walked()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(GrokThenClaudeHigh(), null, CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it", Role: AgentTaskRole.Plan, ModelLevel: AgentModelLevel.High),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Routing.ShouldBeNull();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.RoutingPinId.ShouldBeNull();
        task.ExplicitAgentKind.ShouldBeNull();
        task.ExplicitModelLevel.ShouldBe(AgentModelLevel.High);
        task.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        task.ModelLevel.ShouldBe(AgentModelLevel.High);
    }

    [Test]
    public async Task Explicit_kind_against_a_required_list_with_no_match_is_409_listing_candidates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(RequiredList(card: null), null, CancellationToken.None);

        var ex = await Should.ThrowAsync<RoutingPinConflictException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it", Role: AgentTaskRole.Plan, AgentKind: AgentKind.Codex),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("routing_pin_conflict");
        ex.Message.ShouldContain("ClaudeCode/Frontier");
        ex.Message.ShouldContain("ClaudeCode/High");
        ex.Message.ShouldContain("Codex");
    }

    [Test]
    public async Task Explicit_kind_and_level_against_a_list_is_not_walked()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(RequiredList(card: null), null, CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "opus", DateTime.UtcNow.AddHours(1));

        var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.High),
                Manual(workspace.Path),
                CancellationToken.None));

        ex.Code.ShouldBe("model_disabled");
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task IgnoreRoutingPin_uses_todays_resolution_and_leaves_the_list_standing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        var pin = await Pins(db).UpsertAsync(RequiredList(card: null), null, CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it",
                Role: AgentTaskRole.Plan,
                IgnoreRoutingPin: true),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.Routing.ShouldBeNull();
        var stored = await db.RoutingPins.AsNoTracking().SingleAsync(p => p.Id == pin.Id);
        stored.ClearedAt.ShouldBeNull();
        stored.Candidates.Count.ShouldBe(2);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.RoutingPinId.ShouldBeNull();
    }

    [Test]
    public async Task Required_list_plus_complexity_bypasses_the_chain()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await SeedHardChainAsync(db);
        await Pins(db).UpsertAsync(
            RequiredList("CARD-0301") with
            {
                Candidates =
                [
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                    new(AgentKind.Codex, AgentModelLevel.Frontier),
                    new(AgentKind.ClaudeCode, AgentModelLevel.High),
                ],
            },
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it",
                Role: AgentTaskRole.Plan,
                Card: "CARD-0301",
                Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("complexity chain bypassed");
        created.Routing!.Candidates.Count.ShouldBe(3);
        created.Routing.Candidates.ShouldAllBe(c => c.Origin == RoutingCandidates.OriginPin);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == created.Id);
        task.Complexity.ShouldBe(TaskComplexity.Hard);
        task.RoutingPinId.ShouldNotBeNull();
    }

    [Test]
    public async Task Preferred_list_plus_complexity_is_pin_then_chain()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await SeedHardChainAsync(db);
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Preferred,
                Candidates:
                [
                    new(AgentKind.Codex, AgentModelLevel.Frontier),
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                ],
                Reason: "prefer sol then grok"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it", Role: AgentTaskRole.Plan, Complexity: TaskComplexity.Hard),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Codex);
        created.Routing!.Source.ShouldStartWith("pin+chain:");
        created.Routing.Candidates[0].Origin.ShouldBe(RoutingCandidates.OriginPin);
        created.Routing.Candidates.ShouldContain(c => c.Origin == RoutingCandidates.OriginChain);
        created.Routing.Candidates.ShouldNotContain(c => c.Origin == RoutingCandidates.OriginRolePolicy);
    }

    [Test]
    public async Task Card_list_beats_the_stage_list()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                Candidates: [new(AgentKind.Grok, AgentModelLevel.Frontier)],
                Reason: "stage grok"),
            null,
            CancellationToken.None);
        await Pins(db).UpsertAsync(RequiredList("CARD-0301"), null, CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        created.Routing!.Source.ShouldContain("CARD-0301");
    }

    [Test]
    public async Task Orchestrator_skips_non_Claude_pin_candidates()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                Candidates:
                [
                    new(AgentKind.Grok, AgentModelLevel.Frontier),
                    new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                ],
                Reason: "grok then fable"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "orchestrate it",
                Kind: AgentTaskKind.Orchestrator,
                Role: AgentTaskRole.Plan),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.Routing!.Candidates[0].Reason.ShouldBe("not a delegate kind");
        created.Routing.Candidates[1].Outcome.ShouldBe("chosen");
    }

    [Test]
    public async Task IgnoreModelDisabled_on_a_walked_pin_is_a_moot_warning()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(RequiredList(card: null), null, CancellationToken.None);
        await SeedHoldAsync(db, AgentKind.ClaudeCode, "fable", DateTime.UtcNow.AddHours(1));

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it",
                Role: AgentTaskRole.Plan,
                IgnoreModelDisabled: true),
            Manual(workspace.Path),
            CancellationToken.None);

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.ModelLevel.ShouldBe(AgentModelLevel.High);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("ignoreModelDisabled had no effect");
        created.Warning.ShouldContain("2 candidates");
    }

    private static PutRoutingPinRequest RequiredList(string? card) =>
        new(
            AgentTaskRole.Plan,
            Card: card,
            Provenance: RoutingPinProvenance.Human,
            Strength: RoutingPinStrength.Required,
            Candidates:
            [
                new(AgentKind.ClaudeCode, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
            ],
            Reason: "fable then opus");

    private static PutRoutingPinRequest GrokThenClaudeHigh() =>
        new(
            AgentTaskRole.Plan,
            Provenance: RoutingPinProvenance.Human,
            Strength: RoutingPinStrength.Required,
            Candidates:
            [
                new(AgentKind.Grok, AgentModelLevel.Frontier),
                new(AgentKind.ClaudeCode, AgentModelLevel.High),
            ],
            Reason: "grok then opus");

    private static async Task SeedHardChainAsync(AppDbContext db)
    {
        db.ComplexityChains.Add(new ComplexityChain
        {
            Id = Guid.NewGuid(),
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
        var routing = new ComplexityRoutingService(db, settings, TimeProvider.System, availability);
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
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-pin-candidates").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
