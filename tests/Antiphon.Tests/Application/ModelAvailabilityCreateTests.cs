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
    public async Task Open_ended_hold_sentence_names_the_per_model_cap()
    {
        var holdId = Guid.NewGuid();
        await using var db = CreateContext();
        await SeedHoldAsync(db, holdId, "fable", until: null);
        try
        {
            var ex = await Should.ThrowAsync<ModelDisabledException>(
                () => Service(db).RequireAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None));
            ex.Message.ShouldContain("fable is disabled (per-model cap, no reset stated)");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == holdId).ExecuteDeleteAsync();
        }
    }

    private static async Task SeedHoldAsync(AppDbContext db, Guid id, string alias, DateTime? until)
    {
        await db.ModelAvailabilityHolds
            .Where(h => h.Kind == AgentKind.ClaudeCode && h.ModelAlias == alias && h.ClearedAt == null)
            .ExecuteDeleteAsync();
        db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
        {
            Id = id,
            Kind = AgentKind.ClaudeCode,
            ModelAlias = alias,
            Source = ModelAvailabilitySource.AutoDetected,
            DisabledUntil = until,
            HitAt = DateTime.UtcNow,
            Reason = until is null
                ? "Fable 5 per-model cap (no reset stated)"
                : "session-limit resets 18:10 Europe/London",
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
