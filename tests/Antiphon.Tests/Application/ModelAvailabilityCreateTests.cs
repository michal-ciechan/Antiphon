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
/// CARD-0022 S3 create-time 409. Shared-Postgres: seed by id, delete in finally.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ModelAvailabilityCreateTests
{
    [Test]
    public async Task Create_Frontier_Claude_against_a_fable_hold_is_409_model_disabled()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", DateTime.UtcNow.AddHours(1));
        Guid? createdId = null;
        try
        {
            var service = CreateService(db);
            var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
                service.CreateAsync(
                    new CreateAgentTaskRequest("plan the work", Role: AgentTaskRole.Plan),
                    new AgentTaskService.Caller(null, null, workspace.Path),
                    CancellationToken.None));

            ex.Code.ShouldBe("model_disabled");
            ex.StatusCode.ShouldBe(409);
            ex.Message.ShouldContain("fable is disabled");
            ex.Message.ShouldContain("available:");
            ex.Message.ShouldContain("opus");
        }
        finally
        {
            if (createdId is { } id)
                await db.AgentTasks.Where(t => t.Id == id).ExecuteDeleteAsync();
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Create_Grok_against_a_grok_4_6_hold_is_409_model_disabled()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "grok-4.6", until: null, kind: AgentKind.Grok);
        try
        {
            var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
                CreateService(db).CreateAsync(
                    new CreateAgentTaskRequest(
                        "plan on grok",
                        Role: AgentTaskRole.Plan,
                        AgentKind: AgentKind.Grok),
                    new AgentTaskService.Caller(null, null, workspace.Path),
                    CancellationToken.None));

            ex.Code.ShouldBe("model_disabled");
            ex.StatusCode.ShouldBe(409);
            ex.Message.ShouldContain("grok-4.6 is disabled");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Create_Claude_succeeds_while_grok_4_6_is_held()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "grok-4.6", until: null, kind: AgentKind.Grok);
        Guid createdId = Guid.Empty;
        try
        {
            var created = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest("plan the work", Role: AgentTaskRole.Plan),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = created.Id;
            created.AgentKind.ShouldBe(AgentKind.ClaudeCode);
            created.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Create_Grok_succeeds_while_fable_is_held()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", until: null);
        Guid createdId = Guid.Empty;
        try
        {
            var created = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan on grok",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.Grok),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = created.Id;
            created.AgentKind.ShouldBe(AgentKind.Grok);
            created.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Create_haiku_succeeds_while_fable_is_held()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", until: null);
        Guid createdId = Guid.Empty;
        try
        {
            var created = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(
                    "run the tests",
                    Role: AgentTaskRole.Test,
                    ModelLevel: AgentModelLevel.Low),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = created.Id;
            created.ModelLevel.ShouldBe(AgentModelLevel.Low);
            created.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Create_with_IgnoreModelDisabled_queues_with_a_warning_and_does_not_409()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", DateTime.UtcNow.AddHours(1), manual: true);
        Guid createdId = Guid.Empty;
        try
        {
            var created = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(
                    "park until Thursday",
                    Role: AgentTaskRole.Plan,
                    IgnoreModelDisabled: true),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = created.Id;
            created.Status.ShouldBe(AgentTaskStatus.Queued);
            created.Warning.ShouldNotBeNull();
            created.Warning.ShouldContain("ignoreModelDisabled");
            created.Warning.ShouldContain("fable is held until");

            await using var verify = CreateContext();
            (await verify.AgentTasks.SingleAsync(t => t.Id == createdId)).Status
                .ShouldBe(AgentTaskStatus.Queued);
            var events = await verify.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ToListAsync();
            events.ShouldContain(e => e.Type == AgentTaskEventType.Warning && e.Detail.Contains("ignoreModelDisabled"));
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Create_without_IgnoreModelDisabled_is_still_409_while_held()
    {
        var holdId = Guid.NewGuid();
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", DateTime.UtcNow.AddHours(1), manual: true);
        try
        {
            var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
                CreateService(db).CreateAsync(
                    new CreateAgentTaskRequest("plan the work", Role: AgentTaskRole.Plan),
                    new AgentTaskService.Caller(null, null, workspace.Path),
                    CancellationToken.None));
            ex.Code.ShouldBe("model_disabled");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Open_ended_hold_sentence_names_the_per_model_cap()
    {
        var hold = new ModelAvailabilityHold
        {
            Id = Guid.NewGuid(),
            Kind = AgentKind.ClaudeCode,
            ModelAlias = "fable",
            Source = ModelAvailabilitySource.AutoDetected,
            DisabledUntil = null,
            HitAt = DateTime.UtcNow,
            Reason = "Fable 5 per-model cap (no reset stated)",
        };

        var message = ModelDisabledException.FormatRefusal(hold, ["opus"]);
        message.ShouldContain("fable is disabled (per-model cap, no reset stated)");
    }

    [Test]
    public async Task Fallback_hold_sentence_names_a_bounded_retry_not_a_provider_reset()
    {
        var holdId = Guid.NewGuid();
        var until = DateTime.UtcNow.AddHours(3);
        until = new DateTime(until.Year, until.Month, until.Day, until.Hour, until.Minute, 0, DateTimeKind.Utc);
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", until, reason: "Fable 5 per-model cap (no reset stated)");
        try
        {
            var ex = await Should.ThrowAsync<ModelDisabledException>(
                () => Service(db).RequireAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None));
            ex.Message.ShouldContain($"fable is disabled until {until:yyyy-MM-ddTHH:mm:ssZ} (per-model cap, fallback retry)");
            ex.Message.ShouldNotContain("session-limit");
            var extension = ex.Extensions.ShouldNotBeNull()["modelAvailability"]
                .ShouldBeOfType<ModelAvailabilityProblemDto>();
            extension.DisabledUntil.ShouldBe(until);
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    private static async Task SeedHoldAsync(
        AppDbContext db, Guid id, string alias, DateTime? until, bool manual = false,
        AgentKind kind = AgentKind.ClaudeCode, string? reason = null)
    {
        await db.ModelAvailabilityHolds
            .Where(h => h.Kind == kind && h.ModelAlias == alias && h.ClearedAt == null)
            .ExecuteDeleteAsync();
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = id,
            Kind = kind,
            ModelAlias = alias,
            Source = manual ? ModelAvailabilitySource.Manual : ModelAvailabilitySource.AutoDetected,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = reason ?? (manual
                ? "manual hold"
                : until is null
                    ? "Fable 5 per-model cap (no reset stated)"
                    : "session-limit resets 18:10 Europe/London"),
        });
        await db.SaveChangesAsync();
    }

    private static AgentTaskService CreateService(AppDbContext db)
    {
        var settings = new DelegationSettings
        {
            MaxDepth = 5,
            MaxTasksPerRoot = 40,
            MaxCostUsdPerRoot = 5.00m,
            AllowedRoots = [],
        };
        return new AgentTaskService(
            db,
            new DelegationWorkspaceResolver(NullLogger<DelegationWorkspaceResolver>.Instance),
            Options.Create(settings),
            new MockEventBus(),
            new RecordingSessionStopper(),
            TimeProvider.System,
            NullLogger<AgentTaskService>.Instance,
            modelAvailability: Service(db));
    }

    private static ModelAvailability Service(AppDbContext db) =>
        new(db, TimeProvider.System, NullLogger<ModelAvailability>.Instance);

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private sealed class TempWorkspace : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-hold-create").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
