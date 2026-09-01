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
/// CARD-0309 S1: Manual PUT/DELETE, kind-wide *, expiry, create 409. Shared-Postgres: seed by id.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ModelAvailabilityManualTests
{
    [Test]
    public async Task Manual_fable_until_Thursday_holds_fable_not_opus_or_Grok()
    {
        var thursday = FutureUtc();
        Guid? holdId = null;
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        Guid createdId = Guid.Empty;
        try
        {
            var availability = Service(db);
            var dto = await availability.UpsertManualAsync(
                "ClaudeCode", "Fable", new DateTimeOffset(thursday, TimeSpan.Zero),
                "fable weekly cap", CancellationToken.None);
            holdId = dto.Id;

            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "opus", CancellationToken.None)).ShouldBeFalse();
            (await availability.IsHeldAsync(AgentKind.Grok, "grok-4.6", CancellationToken.None)).ShouldBeFalse();

            var ex = await Should.ThrowAsync<ModelDisabledException>(() =>
                CreateService(db).CreateAsync(
                    new CreateAgentTaskRequest("plan the work", Role: AgentTaskRole.Plan),
                    new AgentTaskService.Caller(null, null, workspace.Path),
                    CancellationToken.None));
            ex.Code.ShouldBe("model_disabled");
            ex.Message.ShouldContain($"fable is disabled until {thursday:yyyy-MM-ddTHH:mm:ssZ} (manual)");
            ex.Message.ShouldContain("available:");
            ex.Message.ShouldContain("opus");
            var extension = ex.Extensions.ShouldNotBeNull()["modelAvailability"]
                .ShouldBeOfType<ModelAvailabilityProblemDto>();
            extension.Source.ShouldBe("Manual");
            extension.Available.ShouldContain("grok-4.6");

            var grok = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest(
                    "plan on grok",
                    Role: AgentTaskRole.Plan,
                    AgentKind: AgentKind.Grok),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = grok.Id;
            grok.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            if (holdId is { } id)
                await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Open_ended_manual_hold_clears_on_DELETE_and_create_is_then_200()
    {
        Guid? holdId = null;
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        Guid createdId = Guid.Empty;
        try
        {
            var availability = Service(db);
            var dto = await availability.UpsertManualAsync(
                "ClaudeCode", "fable", disabledUntil: null, "manual hold", CancellationToken.None);
            holdId = dto.Id;
            dto.DisabledUntil.ShouldBeNull();

            var ex = await Should.ThrowAsync<ModelDisabledException>(
                () => availability.RequireAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None));
            ex.Message.ShouldContain("fable is disabled (manual, no re-enable time)");

            await availability.ClearAsync("ClaudeCode", "fable", CancellationToken.None);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeFalse();

            await availability.ClearAsync("ClaudeCode", "fable", CancellationToken.None);

            var created = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest("plan after clear", Role: AgentTaskRole.Plan),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = created.Id;
            created.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            if (holdId is { } id)
                await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Kind_wide_star_holds_fable_and_haiku_and_leaves_Grok()
    {
        Guid? holdId = null;
        await using var db = CreateContext();
        try
        {
            var availability = Service(db);
            var dto = await availability.UpsertManualAsync(
                "ClaudeCode", "*",
                new DateTimeOffset(FutureUtc(), TimeSpan.Zero),
                "Claude out until Thursday",
                CancellationToken.None);
            holdId = dto.Id;
            dto.ModelAlias.ShouldBe("*");

            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "haiku", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.Grok, "grok-4.6", CancellationToken.None)).ShouldBeFalse();

            var available = await availability.ListAvailableAsync(CancellationToken.None);
            available.ShouldNotContain("fable");
            available.ShouldNotContain("haiku");
            available.ShouldContain("grok-4.6");

            var ex = await Should.ThrowAsync<ModelDisabledException>(
                () => availability.RequireAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None));
            ex.Message.ShouldContain("ClaudeCode is disabled until");
            ex.Message.ShouldContain("(manual)");

            await availability.ClearAsync("ClaudeCode", "*", CancellationToken.None);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeFalse();
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "haiku", CancellationToken.None)).ShouldBeFalse();
        }
        finally
        {
            if (holdId is { } id)
                await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Star_and_per_alias_are_OR_and_DELETE_star_leaves_the_fable_row()
    {
        Guid? starId = null;
        Guid? fableId = null;
        await using var db = CreateContext();
        try
        {
            var availability = Service(db);
            starId = (await availability.UpsertManualAsync(
                "ClaudeCode", "*", null, "kind-wide", CancellationToken.None)).Id;
            fableId = (await availability.UpsertManualAsync(
                "ClaudeCode", "fable", null, "also fable", CancellationToken.None)).Id;

            await availability.ClearAsync("ClaudeCode", "*", CancellationToken.None);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "haiku", CancellationToken.None)).ShouldBeFalse();
        }
        finally
        {
            if (starId is { } s)
                await db.ModelAvailabilityHolds.Where(h => h.Id == s).ExecuteDeleteAsync();
            if (fableId is { } f)
                await db.ModelAvailabilityHolds.Where(h => h.Id == f).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Expired_manual_hold_is_not_held_and_create_is_200_without_DELETE()
    {
        Guid? holdId = null;
        using var workspace = new TempWorkspace();
        await using var db = CreateContext();
        Guid createdId = Guid.Empty;
        try
        {
            var row = new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = DateTime.UtcNow.AddSeconds(-1),
                HitAt = DateTime.UtcNow.AddMinutes(-5),
                Reason = "manual hold",
            };
            db.ModelAvailabilityHolds.Add(row);
            await db.SaveChangesAsync();
            holdId = row.Id;

            var availability = Service(db);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeFalse();
            await availability.RequireAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None);

            await using var verify = CreateContext();
            (await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == row.Id)).ClearedAt.ShouldNotBeNull();

            var created = await CreateService(db).CreateAsync(
                new CreateAgentTaskRequest("plan after expiry", Role: AgentTaskRole.Plan),
                new AgentTaskService.Caller(null, null, workspace.Path),
                CancellationToken.None);
            createdId = created.Id;
            created.Status.ShouldBe(AgentTaskStatus.Queued);
        }
        finally
        {
            if (createdId != Guid.Empty)
            {
                await db.AgentTaskEvents.Where(e => e.AgentTaskId == createdId).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.Id == createdId).ExecuteDeleteAsync();
            }
            if (holdId is { } id)
                await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Past_until_and_unknown_alias_and_non_delegatable_kind_are_422()
    {
        await using var db = CreateContext();
        var availability = Service(db);

        var past = await Should.ThrowAsync<ValidationException>(() =>
            availability.UpsertManualAsync(
                "ClaudeCode", "fable",
                DateTimeOffset.UtcNow.AddMinutes(-1),
                null,
                CancellationToken.None));
        past.Errors.ShouldContainKey("disabledUntil");

        var alias = await Should.ThrowAsync<ValidationException>(() =>
            availability.UpsertManualAsync(
                "ClaudeCode", "claude-fable-5", null, null, CancellationToken.None));
        alias.Errors.ShouldContainKey("alias");

        var kind = await Should.ThrowAsync<ValidationException>(() =>
            availability.UpsertManualAsync(
                "OpenCode", "fable", null, null, CancellationToken.None));
        kind.Errors.ShouldContainKey("kind");

        var unknownKind = await Should.ThrowAsync<ValidationException>(() =>
            availability.UpsertManualAsync(
                "NotAKind", "fable", null, null, CancellationToken.None));
        unknownKind.Errors.ShouldContainKey("kind");
    }

    [Test]
    public async Task DELETE_of_an_AutoDetected_row_unpauses()
    {
        var id = Guid.NewGuid();
        await using var db = CreateContext();
        try
        {
            db.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = id,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = DateTime.UtcNow.AddHours(1),
                HitAt = DateTime.UtcNow,
                Reason = "session-limit resets 18:10 Europe/London",
            });
            await db.SaveChangesAsync();

            var availability = Service(db);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeTrue();
            await availability.ClearAsync("ClaudeCode", "fable", CancellationToken.None);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeFalse();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    private static DateTime FutureUtc()
    {
        var until = DateTime.UtcNow.AddDays(3);
        return new DateTime(until.Year, until.Month, until.Day, 0, 0, 0, DateTimeKind.Utc);
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
        public string Path { get; } = Directory.CreateTempSubdirectory("antiphon-hold-manual").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
        }
    }
}
