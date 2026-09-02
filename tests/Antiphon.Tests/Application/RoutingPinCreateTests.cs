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
/// CARD-0305 S1/S3: what a pin does to <c>CreateAsync</c>. The pin is read BEFORE the role policy,
/// the quota gate and the CARD-0309 hold, so the alias <c>Require</c> sees is the pinned one.
///
/// <para>Isolated schema per test: the stage-wide index is unique on role alone, so these cannot
/// share a database with each other.</para>
/// </summary>
[Category("Integration")]
public class RoutingPinCreateTests
{
    [Test]
    public async Task A_card_pin_routes_a_create_that_named_no_kind()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0304");
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0304",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.Codex,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "operator: CARD-0304 plans on Sol"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0304"),
            Manual(workspace.Path),
            CancellationToken.None);

        // Without the pin this is ClaudeCode/Frontier — the role policy's fable — which is exactly
        // the instruction that used to live only in a chat message.
        created.AgentKind.ShouldBe(AgentKind.Codex);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        var createdEvent = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        createdEvent.Detail.ShouldContain("pin=");
        createdEvent.Detail.ShouldContain("human required");
    }

    [Test]
    public async Task No_pin_leaves_todays_resolution_untouched()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        var createdEvent = await db.AgentTaskEvents.AsNoTracking()
            .SingleAsync(e => e.AgentTaskId == created.Id && e.Type == AgentTaskEventType.Created);
        createdEvent.Detail.ShouldNotContain("pin=");
    }

    [Test]
    public async Task An_explicit_kind_against_a_required_pin_is_409_routing_pin_conflict()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0304");
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0304",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.Codex,
                Reason: "operator: CARD-0304 plans on Sol"),
            null,
            CancellationToken.None);

        var refusal = await Should.ThrowAsync<RoutingPinConflictException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it", Role: AgentTaskRole.Plan, Card: "CARD-0304", AgentKind: AgentKind.Grok),
                Manual(workspace.Path),
                CancellationToken.None));

        refusal.Code.ShouldBe("routing_pin_conflict");
        refusal.StatusCode.ShouldBe(409);
        refusal.Message.ShouldContain("Grok");
        refusal.Message.ShouldContain("ignoreRoutingPin");
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task IgnoreRoutingPin_proceeds_and_leaves_the_pin_standing()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0304");
        var pin = await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0304",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.Codex,
                Reason: "operator: CARD-0304 plans on Sol"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it",
                Role: AgentTaskRole.Plan,
                Card: "CARD-0304",
                AgentKind: AgentKind.Grok,
                IgnoreRoutingPin: true),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("ignoreRoutingPin");
        // One-shot, never a clear: the next create without the flag is refused again.
        var stored = await db.RoutingPins.AsNoTracking().SingleAsync(p => p.Id == pin.Id);
        stored.ClearedAt.ShouldBeNull();
        stored.AgentKind.ShouldBe(AgentKind.Codex);
    }

    [Test]
    public async Task A_preferred_pin_yields_to_an_explicit_kind_and_says_so()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Preferred,
                AgentKind: AgentKind.Codex,
                Reason: "prefer Sol for planning"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, AgentKind: AgentKind.Grok),
            Manual(workspace.Path),
            CancellationToken.None);

        created.AgentKind.ShouldBe(AgentKind.Grok);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("Overrode preferred");
    }

    [Test]
    public async Task A_stage_forbid_refuses_the_alias_it_names()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Preferred,
                ForbiddenAliases: ["fable"],
                Reason: "planning off fable"),
            null,
            CancellationToken.None);

        var refusal = await Should.ThrowAsync<RoutingPinForbiddenException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.Frontier),
                Manual(workspace.Path),
                CancellationToken.None));

        refusal.Code.ShouldBe("routing_pin_forbidden");
        refusal.Message.ShouldContain("fable is forbidden for Plan");
    }

    [Test]
    public async Task A_human_card_pin_overrides_the_stage_forbid_that_names_its_alias()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0301");
        var pins = Pins(db);
        await pins.UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                ForbiddenAliases: ["fable"],
                Reason: "planning off fable"),
            null,
            CancellationToken.None);
        await pins.UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0301",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.ClaudeCode,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "operator: CARD-0301 stays on fable"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
            Manual(workspace.Path),
            CancellationToken.None);

        // The exception the whole card exists for: a general "not fable" rule that a per-card human
        // decision deliberately outranks, without the operator repeating themselves.
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
    }

    [Test]
    public async Task An_auto_card_pin_gets_no_exemption_from_a_human_stage_forbid()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0302");
        var pins = Pins(db);
        await pins.UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                ForbiddenAliases: ["fable"],
                Reason: "planning off fable"),
            null,
            CancellationToken.None);
        await pins.UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0302",
                Provenance: RoutingPinProvenance.Auto,
                AgentKind: AgentKind.ClaudeCode,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "dispatcher guess"),
            null,
            CancellationToken.None);

        // The card's central invariant, applied to the forbid list too: an automatic decision must
        // not be a back door around a human one.
        await Should.ThrowAsync<RoutingPinForbiddenException>(() =>
            Service(db, workspace).CreateAsync(
                new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0302"),
                Manual(workspace.Path),
                CancellationToken.None));
    }

    [Test]
    public async Task A_dated_pin_still_creates_the_task_Queued()
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
                NotBefore: DateTimeOffset.UtcNow.AddDays(2),
                Reason: "operator: plan on fable after the weekly cap"),
            null,
            CancellationToken.None);

        var created = await Service(db, workspace).CreateAsync(
            new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
            Manual(workspace.Path),
            CancellationToken.None);

        // Deliberately the opposite of a fleet hold's 409: the PIN is why the work exists, so it is
        // queued and the dispatcher owns the wait.
        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
    }

    [Test]
    public async Task A_held_alias_a_required_pin_named_is_409_model_disabled_with_a_pin_coda()
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
                Reason: "operator: CARD-0301 stays on fable"),
            null,
            CancellationToken.None);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = DateTime.UtcNow.AddHours(4),
            HitAt = DateTime.UtcNow,
            Reason = "weekly cap",
        });
        await db.SaveChangesAsync();

        var refusal = await Should.ThrowAsync<ModelDisabledException>(() =>
            Service(db, workspace, availability: true).CreateAsync(
                new CreateAgentTaskRequest("plan it", Role: AgentTaskRole.Plan, Card: "CARD-0301"),
                Manual(workspace.Path),
                CancellationToken.None));

        // Two mechanisms, one refusal: the hold's code and available list stay exactly as CARD-0309
        // shipped them, and the coda is what stops an operator reading "available: opus" as
        // permission to reroute work a human pinned to fable.
        refusal.Code.ShouldBe("model_disabled");
        refusal.Message.ShouldContain("fable is disabled until");
        refusal.Message.ShouldContain("available:");
        refusal.Message.ShouldContain("does not satisfy the pin");
        refusal.Message.ShouldContain("CARD-0301");
    }

    [Test]
    public async Task IgnoreModelDisabled_queues_a_required_pin_to_a_held_alias()
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
                Reason: "operator: CARD-0301 stays on fable"),
            null,
            CancellationToken.None);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = DateTime.UtcNow.AddHours(4),
            HitAt = DateTime.UtcNow,
            Reason = "weekly cap",
        });
        await db.SaveChangesAsync();

        var created = await Service(db, workspace, availability: true).CreateAsync(
            new CreateAgentTaskRequest(
                "plan it",
                Role: AgentTaskRole.Plan,
                Card: "CARD-0301",
                IgnoreModelDisabled: true),
            Manual(workspace.Path),
            CancellationToken.None);

        // Queue, do not reroute: the pin still named fable, the hold still holds it, and the
        // dispatcher skip is what waits. ignoreRoutingPin is a different flag.
        created.Status.ShouldBe(AgentTaskStatus.Queued);
        created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
        created.ModelLevel.ShouldBe(AgentModelLevel.Frontier);
        created.Warning.ShouldNotBeNull();
        created.Warning.ShouldContain("ignoreModelDisabled");
    }

    [Test]
    public async Task IgnoreRoutingPin_still_hits_the_hold_on_the_requests_kind()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0304");
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Card: "CARD-0304",
                Provenance: RoutingPinProvenance.Human,
                Strength: RoutingPinStrength.Required,
                AgentKind: AgentKind.Codex,
                ModelLevel: AgentModelLevel.Frontier,
                Reason: "operator: CARD-0304 plans on Sol"),
            null,
            CancellationToken.None);
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.Grok,
            ModelAlias = "grok-4.6",
            Source = ModelAvailabilitySource.Manual,
            DisabledUntil = DateTime.UtcNow.AddHours(4),
            HitAt = DateTime.UtcNow,
            Reason = "weekly cap",
        });
        await db.SaveChangesAsync();

        var refusal = await Should.ThrowAsync<ModelDisabledException>(() =>
            Service(db, workspace, availability: true).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    Card: "CARD-0304",
                    AgentKind: AgentKind.Grok,
                    IgnoreRoutingPin: true),
                Manual(workspace.Path),
                CancellationToken.None));

        // The pin was ignored, so Require sees the REQUEST's kind, and the coda that names the
        // pin must not appear — that sentence is only for a Required pin that actually applied.
        refusal.Code.ShouldBe("model_disabled");
        refusal.Message.ShouldContain("grok-4.6");
        refusal.Message.ShouldNotContain("does not satisfy the pin");
        (await db.AgentTasks.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task A_pin_never_writes_a_model_availability_hold()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        using var workspace = new TempWorkspace();
        await Pins(db).UpsertAsync(
            new PutRoutingPinRequest(
                AgentTaskRole.Plan,
                Provenance: RoutingPinProvenance.Human,
                ForbiddenAliases: ["fable"],
                Reason: "planning off fable"),
            null,
            CancellationToken.None);

        await Should.ThrowAsync<RoutingPinForbiddenException>(() =>
            Service(db, workspace, availability: true).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan it",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.ClaudeCode,
                    ModelLevel: AgentModelLevel.Frontier),
                Manual(workspace.Path),
                CancellationToken.None));

        // "Do not merge the tables": forbidding an alias for one stage says nothing about whether
        // the fleet may use it.
        (await db.ModelAvailabilityHolds.CountAsync()).ShouldBe(0);
    }

    private static AgentTaskService.Caller Manual(string directory) => new(null, null, directory);

    private static RoutingPinService Pins(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<RoutingPinService>.Instance);

    private static AgentTaskService Service(
        AppDbContext db, TempWorkspace workspace, bool availability = false) => new(
        db,
        new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
        Options.Create(new DelegationSettings { AllowedRoots = [workspace.Path] }),
        new MockEventBus(),
        new RecordingSessionStopper(),
        TimeProvider.System,
        NullLogger<AgentTaskService>.Instance,
        modelAvailability: availability
            ? new ModelAvailability(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance)
            : null,
        routingPins: Pins(db));

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-routing-pin").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
