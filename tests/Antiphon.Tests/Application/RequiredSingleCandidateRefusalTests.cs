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
/// CARD-1271: a Required forbid-only stage pin over one matrix row leaves a single, not-walked
/// candidate. When that candidate fails a walk filter (kind, hold, pin forbid, quota), create
/// takes the Required-single-candidate arm (AgentTaskService "today's 409s, never Blocked");
/// when it passes them all, the walk chooses it and the arm is not entered. The refusal tests
/// here are the server witnesses for that arm, including the Orchestrator kind filter the arm
/// used to skip and a held ClaudeCode Orchestrator that must still get model_disabled. The one
/// creating test is a control on the chosen branch, not on the arm. Isolated schema: unique
/// chain/pin indexes.
/// </summary>
[Category("Integration")]
public sealed class RequiredSingleCandidateRefusalTests
{
    [Test]
    public async Task An_orchestrator_with_complexity_over_one_codex_row_is_409_routing_exhausted()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, TaskComplexity.Hard, AgentKind.Codex, AgentModelLevel.Frontier);
        await f.ForbidOnlyPin("grok-4.7");

        var ex = await Should.ThrowAsync<RoutingExhaustedException>(() => f.Create(new(
            "orchestrate it",
            Kind: AgentTaskKind.Orchestrator,
            Role: AgentTaskRole.Plan,
            Complexity: TaskComplexity.Hard,
            Workspace: WorkspaceMode.Shared)));

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("routing_exhausted");
        ex.Message.ShouldStartWith("routing exhausted: Plan/Hard chain — gpt-6-astra not a delegate kind");
        ex.Message.ShouldContain("An orchestrator runs only on ClaudeCode");
        ex.Routing.Walked.ShouldBeFalse();
        ex.Routing.Candidates.Count.ShouldBe(1);
        ex.Routing.Candidates[0].AgentKind.ShouldBe(AgentKind.Codex);
        ex.Routing.Candidates[0].Outcome.ShouldBe("skipped");
        ex.Routing.Candidates[0].Reason.ShouldBe("not a delegate kind");
        (await f.Db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task An_orchestrator_with_complexity_over_one_claude_row_is_created_on_it()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, TaskComplexity.Hard, AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        await f.ForbidOnlyPin("grok-4.7");

        var created = await f.Create(new(
            "orchestrate it",
            Kind: AgentTaskKind.Orchestrator,
            Role: AgentTaskRole.Plan,
            Complexity: TaskComplexity.Hard,
            Workspace: WorkspaceMode.Shared));

        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        var task = await f.Db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == created.Id);
        task.Kind.ShouldBe(AgentTaskKind.Orchestrator);
        task.AgentKind.ShouldBe(AgentKind.ClaudeCode);
    }

    [Test]
    public async Task An_orchestrator_with_complexity_over_one_held_claude_row_is_409_model_disabled()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, TaskComplexity.Hard, AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        await f.ForbidOnlyPin("grok-4.7");
        await f.Hold(AgentKind.ClaudeCode, "fable");

        var ex = await Should.ThrowAsync<ModelDisabledException>(() => f.Create(new(
            "orchestrate it",
            Kind: AgentTaskKind.Orchestrator,
            Role: AgentTaskRole.Plan,
            Complexity: TaskComplexity.Hard,
            Workspace: WorkspaceMode.Shared)));

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("model_disabled");
        ex.Message.ShouldContain("fable");
        ex.Message.ShouldContain("does not satisfy the pin");
        (await f.Db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_forbid_only_required_pin_over_one_held_default_row_is_409_model_disabled()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, TaskComplexity.Medium, AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        await f.ForbidOnlyPin("grok-4.7");
        await f.Hold(AgentKind.ClaudeCode, "fable");

        var ex = await Should.ThrowAsync<ModelDisabledException>(() => f.Create());

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("model_disabled");
        ex.Message.ShouldContain("fable");
        ex.Message.ShouldContain("does not satisfy the pin");
        (await f.Db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_forbid_only_required_pin_that_forbids_the_one_default_row_is_409_routing_pin_forbidden()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, TaskComplexity.Medium, AgentKind.ClaudeCode, AgentModelLevel.Frontier);
        await f.ForbidOnlyPin("fable");

        var ex = await Should.ThrowAsync<RoutingPinForbiddenException>(() => f.Create());

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("routing_pin_forbidden");
        ex.Message.ShouldStartWith("fable is forbidden for Plan by the human stage-wide routing pin");
        (await f.Db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_forbid_only_required_pin_over_one_quota_low_default_row_is_409_subscription_quota_low()
    {
        await using var f = await Fixture.CreateAsync();
        await f.Row(AgentTaskRole.Plan, TaskComplexity.Medium, AgentKind.Codex, AgentModelLevel.Frontier);
        await f.ForbidOnlyPin("grok-4.7");
        await f.QuotaSample(AgentKind.Codex, remaining: 3, hoursToReset: 36);

        var ex = await Should.ThrowAsync<SubscriptionQuotaLowException>(() => f.Create());

        ex.StatusCode.ShouldBe(409);
        ex.Code.ShouldBe("subscription_quota_low");
        var quota = ex.Extensions.ShouldNotBeNull()["quota"].ShouldBeOfType<SubscriptionQuotaProblemDto>();
        quota.RemainingPercent.ShouldBe(3);
        quota.SubscriptionKey.ShouldBe("Codex");
        (await f.Db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    private sealed class Fixture(IsolatedTestSchema schema, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;
        public string Path { get; } = Directory.CreateTempSubdirectory("c1271-single").FullName;

        private RoutingPinService Pins => new(Db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);

        public static async Task<Fixture> CreateAsync()
        {
            var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            return new Fixture(schema, new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString)));
        }

        public Task<AgentTaskCreatedDto> Create(CreateAgentTaskRequest? request = null)
        {
            var settings = Options.Create(new DelegationSettings { AllowedRoots = [Path] });
            var availability = new ModelAvailability(Db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);
            var gate = new SubscriptionQuotaGate(
                new SubscriptionUsageReader(Db, TimeProvider.System),
                Options.Create(new SubscriptionQuotaGateSettings()),
                TimeProvider.System,
                NullLogger<SubscriptionQuotaGate>.Instance);
            var service = new AgentTaskService(
                Db,
                new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
                settings,
                new MockEventBus(),
                new RecordingSessionStopper(),
                TimeProvider.System,
                NullLogger<AgentTaskService>.Instance,
                quotaGate: gate,
                modelAvailability: availability,
                routingPins: Pins,
                complexityRouting: new ComplexityRoutingService(Db, settings, TimeProvider.System, availability, gate));
            return service.CreateAsync(
                request ?? new("plan it", Role: AgentTaskRole.Plan, Workspace: WorkspaceMode.Shared),
                new AgentTaskService.Caller(null, null, Path),
                CancellationToken.None);
        }

        public async Task Row(AgentTaskRole role, TaskComplexity complexity, AgentKind kind, AgentModelLevel level)
        {
            Db.ComplexityChains.Add(new ComplexityChain
            {
                Id = Guid.NewGuid(),
                Role = role,
                Complexity = complexity,
                CandidatesJson = ComplexityChain.SerializeCandidates([new(kind, level)]),
                Provenance = RoutingPinProvenance.Human,
                Reason = "c1271 single row",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            await Db.SaveChangesAsync();
        }

        public Task<RoutingPinDto> ForbidOnlyPin(string forbidden) => Pins.UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                ForbiddenAliases: [forbidden],
                Reason: "c1271 forbid-only"),
            null,
            CancellationToken.None);

        public async Task Hold(AgentKind kind, string alias)
        {
            Db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                ModelAlias = alias,
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = DateTime.UtcNow.AddHours(4),
                HitAt = DateTime.UtcNow,
                Reason = "c1271 hold",
            });
            await Db.SaveChangesAsync();
        }

        public async Task QuotaSample(AgentKind provider, double remaining, int hoursToReset)
        {
            var now = DateTime.UtcNow;
            Db.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
            {
                Id = Guid.NewGuid(),
                Provider = provider,
                SubscriptionKey = provider.ToString(),
                PlanLabel = "SuperPlan",
                RemainingPercent = remaining,
                ResetsAt = now.AddHours(hoursToReset),
                ObservedAt = now,
                AgentSessionId = Guid.NewGuid(),
                SourceCommand = "/status",
                ParseStatus = SubscriptionUsageParseStatus.Parsed,
                RawExcerpt = "seeded",
            });
            await Db.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await schema.DisposeAsync();
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
