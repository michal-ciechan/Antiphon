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
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0022 reader + AutoDetected writer. Isolated schema so ListAvailable / Require / IsHeld
/// do not read other suites' ModelAvailabilityHolds (CARD-0336). The CARD-0309 outrank contract
/// is green here so a later Manual writer layers on the same table.
/// </summary>
[Category("Integration")]
[NotInParallel]
public class ModelAvailabilityTests
{
    [Test]
    public async Task IsHeld_is_true_only_for_the_paused_alias()
    {
        var id = Guid.NewGuid();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, Hold(id, "fable", until: DateTime.UtcNow.AddHours(1)));

            var availability = Service(db);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "opus", CancellationToken.None)).ShouldBeFalse();
            (await availability.IsHeldAsync(AgentKind.Grok, "grok-4.6", CancellationToken.None)).ShouldBeFalse();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Kind_wide_star_holds_every_alias_of_that_kind()
    {
        var id = Guid.NewGuid();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, Hold(id, ModelAlias.KindWide, until: DateTime.UtcNow.AddHours(1)));

            var availability = Service(db);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "haiku", CancellationToken.None)).ShouldBeTrue();
            (await availability.IsHeldAsync(AgentKind.Grok, "grok-4.6", CancellationToken.None)).ShouldBeFalse();

            var available = await availability.ListAvailableAsync(CancellationToken.None);
            available.ShouldNotContain("fable");
            available.ShouldNotContain("haiku");
            available.ShouldContain("grok-4.6");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Lazy_IsHeld_clears_an_expired_timed_hold()
    {
        var id = Guid.NewGuid();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, Hold(id, "fable", until: DateTime.UtcNow.AddSeconds(-1)));

            var availability = Service(db);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None)).ShouldBeFalse();

            await using var verify = CreateContext(schema);
            (await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == id)).ClearedAt.ShouldNotBeNull();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Require_throws_model_disabled_with_available_list()
    {
        var id = Guid.NewGuid();
        var until = DateTime.UtcNow.AddHours(2);
        until = new DateTime(until.Year, until.Month, until.Day, until.Hour, until.Minute, 0, DateTimeKind.Utc);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, Hold(id, "fable", until));

            var ex = await Should.ThrowAsync<ModelDisabledException>(
                () => Service(db).RequireAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None));

            ex.StatusCode.ShouldBe(409);
            ex.Code.ShouldBe(ModelDisabledException.ErrorCode);
            ex.Message.ShouldContain($"fable is disabled until {until:yyyy-MM-ddTHH:mm:ssZ} (session-limit)");
            ex.Message.ShouldContain("available:");
            ex.Message.ShouldContain("opus");
            ex.Message.ShouldContain("grok-4.6");
            var extension = ex.Extensions.ShouldNotBeNull()["modelAvailability"]
                .ShouldBeOfType<ModelAvailabilityProblemDto>();
            extension.ModelAlias.ShouldBe("fable");
            extension.Available.ShouldContain("sonnet");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Auto_detected_does_not_shorten_or_demote_an_active_manual_hold()
    {
        var id = Guid.NewGuid();
        var thursday = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, new ModelAvailabilityHold
            {
                Id = id,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = thursday,
                HitAt = DateTime.UtcNow.AddHours(-2),
                Reason = "manual hold",
            });

            var written = await Service(db).UpsertAutoDetectedAsync(
                AgentKind.ClaudeCode,
                "fable",
                disabledUntil: DateTime.UtcNow.AddMinutes(10),
                reason: "session-limit resets 18:10 Europe/London",
                rawText: UsageLimitWallParser.SessionLimitFixtureText,
                sourceSessionId: Guid.NewGuid(),
                sourceTaskId: null,
                CancellationToken.None);

            written.Id.ShouldBe(id);
            written.Source.ShouldBe(ModelAvailabilitySource.Manual);
            written.DisabledUntil.ShouldBe(thursday);
            written.RawText.ShouldBe(UsageLimitWallParser.SessionLimitFixtureText);
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Auto_detected_does_not_fill_an_open_ended_manual_DisabledUntil()
    {
        var id = Guid.NewGuid();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, new ModelAvailabilityHold
            {
                Id = id,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = null,
                HitAt = DateTime.UtcNow.AddHours(-2),
                Reason = "manual hold",
            });

            var written = await Service(db).UpsertAutoDetectedAsync(
                AgentKind.ClaudeCode,
                "fable",
                disabledUntil: DateTime.UtcNow.AddHours(1),
                reason: "session-limit resets 18:10 Europe/London",
                rawText: UsageLimitWallParser.SessionLimitFixtureText,
                sourceSessionId: Guid.NewGuid(),
                sourceTaskId: null,
                CancellationToken.None);

            written.Id.ShouldBe(id);
            written.Source.ShouldBe(ModelAvailabilitySource.Manual);
            written.DisabledUntil.ShouldBeNull();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Manual_put_converts_an_active_auto_detected_row_in_place()
    {
        var id = Guid.NewGuid();
        var until = DateTime.UtcNow.AddDays(3);
        var thursday = new DateTime(until.Year, until.Month, until.Day, 0, 0, 0, DateTimeKind.Utc);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, Hold(id, "fable", DateTime.UtcNow.AddMinutes(10)));

            var dto = await Service(db).UpsertManualAsync(
                "ClaudeCode",
                "fable",
                new DateTimeOffset(thursday, TimeSpan.Zero),
                "fable weekly cap; Plan on grok until Thursday",
                CancellationToken.None);

            dto.Id.ShouldBe(id);
            dto.Source.ShouldBe(ModelAvailabilitySource.Manual);
            dto.DisabledUntil.ShouldBe(thursday);
            dto.RawText.ShouldBeNull();
            dto.SourceSessionId.ShouldBeNull();
            dto.Reason.ShouldContain("weekly cap");

            await using var verify = CreateContext(schema);
            var row = await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == id);
            row.Source.ShouldBe(ModelAvailabilitySource.Manual);
            row.DisabledUntil.ShouldBe(thursday);
            row.ClearedAt.ShouldBeNull();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task ListAvailable_omits_held_aliases_and_keeps_the_rest()
    {
        var id = Guid.NewGuid();
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, Hold(id, "fable", until: null));

            var available = await Service(db).ListAvailableAsync(CancellationToken.None);
            available.ShouldNotContain("fable");
            available.ShouldContain("opus");
            available.ShouldContain("sonnet");
            available.ShouldContain("haiku");
            available.ShouldContain("grok-4.6");
            available.ShouldContain("gpt-6-astra");
            available.ShouldContain("gpt-5.6-sol");
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task A_fresh_fallback_timestamp_blocks_until_expiry_then_IsHeld_clears_it()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await db.ModelAvailabilityHolds
                .Where(h => h.Kind == AgentKind.ClaudeCode && h.ModelAlias == "fable" && h.ClearedAt == null)
                .ExecuteDeleteAsync();
            var availability = Service(db, time);
            var written = await availability.UpsertAutoDetectedAsync(
                AgentKind.ClaudeCode,
                "fable",
                now.UtcDateTime.AddHours(3),
                "Fable 5 per-model cap (no reset stated)",
                UsageLimitWallParser.FableModelCapIncidentText,
                sourceSessionId: null,
                sourceTaskId: null,
                CancellationToken.None);
            id = written.Id;

            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None))
                .ShouldBeTrue();

            time.Advance(TimeSpan.FromHours(3));
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None))
                .ShouldBeFalse();

            await using var verify = CreateContext(schema);
            (await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == id)).ClearedAt.ShouldNotBeNull();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task An_old_auto_detected_null_row_is_materialized_and_cleared()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero);
        var hitAt = new DateTime(2026, 9, 3, 12, 0, 0, DateTimeKind.Utc);
        var time = new FakeTimeProvider(now);
        var settings = Options.Create(new SupervisionSettings
        {
            ApiErrorRecovery = new ApiErrorRecoverySettings { ModelCapFallbackHoldHours = 4 },
        });
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, new ModelAvailabilityHold
            {
                Id = id,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = null,
                HitAt = hitAt,
                Reason = "Fable 5 per-model cap (no reset stated)",
            });

            var availability = Service(db, time, settings);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None))
                .ShouldBeFalse();

            await using var verify = CreateContext(schema);
            var row = await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == id);
            row.DisabledUntil.ShouldBe(hitAt.AddHours(4));
            row.ClearedAt.ShouldNotBeNull();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task Sweep_materializes_a_recent_auto_detected_null_without_clearing_it()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var hitAt = now.UtcDateTime;
        var time = new FakeTimeProvider(now);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, new ModelAvailabilityHold
            {
                Id = id,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = null,
                HitAt = hitAt,
                Reason = "Fable 5 per-model cap (no reset stated)",
            });

            var availability = Service(db, time);
            (await availability.SweepExpiredAsync(CancellationToken.None)).ShouldBe(0);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None))
                .ShouldBeTrue();

            await using var verify = CreateContext(schema);
            var row = await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == id);
            row.DisabledUntil.ShouldBe(hitAt.AddHours(6));
            row.ClearedAt.ShouldBeNull();
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    [Test]
    public async Task An_open_ended_manual_null_row_remains_held()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 3, 18, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = CreateContext(schema);
        try
        {
            await ReplaceHoldAsync(db, new ModelAvailabilityHold
            {
                Id = id,
                Kind = AgentKind.ClaudeCode,
                ModelAlias = "fable",
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = null,
                HitAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Reason = "manual hold",
            });

            var availability = Service(db, time);
            (await availability.IsHeldAsync(AgentKind.ClaudeCode, "fable", CancellationToken.None))
                .ShouldBeTrue();
            (await availability.SweepExpiredAsync(CancellationToken.None)).ShouldBe(0);

            await using var verify = CreateContext(schema);
            var row = await verify.ModelAvailabilityHolds.SingleAsync(h => h.Id == id);
            row.DisabledUntil.ShouldBeNull();
            row.ClearedAt.ShouldBeNull();
            row.Source.ShouldBe(ModelAvailabilitySource.Manual);
        }
        finally
        {
            await db.ModelAvailabilityHolds.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }

    private static async Task ReplaceHoldAsync(AppDbContext db, ModelAvailabilityHold hold)
    {
        await db.ModelAvailabilityHolds
            .Where(h => h.Kind == hold.Kind && h.ModelAlias == hold.ModelAlias && h.ClearedAt == null)
            .ExecuteDeleteAsync();
        db.ModelAvailabilityHolds.Add(hold);
        await db.SaveChangesAsync();
    }

    private static ModelAvailability Service(
        AppDbContext db, TimeProvider? time = null, IOptions<SupervisionSettings>? settings = null) =>
        new(db, time ?? TimeProvider.System, NullLogger<ModelAvailability>.Instance, settings);

    private static ModelAvailabilityHold Hold(Guid id, string alias, DateTime? until) => new()
    {
        Id = id,
        Kind = AgentKind.ClaudeCode,
        ModelAlias = alias,
        Source = ModelAvailabilitySource.AutoDetected,
        DisabledUntil = until,
        HitAt = DateTime.UtcNow,
        Reason = until is null ? "Fable 5 per-model cap (no reset stated)" : "session-limit resets 18:10 Europe/London",
    };

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
