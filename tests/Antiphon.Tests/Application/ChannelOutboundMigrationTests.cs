using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0418 V-21: the upgrade is additive. A database that has been running since before this card
/// keeps every row and every value it had; the new columns arrive null, which is the OFF position.
///
/// <para>The migration is applied for real, forwards, over rows written against the PRECEDING
/// schema — not against a fresh database created by the final model, which would prove nothing
/// about an existing installation.</para>
/// </summary>
[NotInParallel]
[Category("Integration")]
public class ChannelOutboundMigrationTests
{
    private const string PreviousMigration = "20260913130300_StoreInternalDecisionPolicyAsText";
    private const string OutboundMigration = "20260913222505_Card0418ChannelOutbound";

    [Test]
    [Timeout(300_000)]
    public async Task Upgrade_preserves_history_and_defaults(CancellationToken ct)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();

        // Roll the isolated store BACK to the schema this card started from, then write rows the
        // way an installation running that version would have.
        await using (var db = NewContext(schema))
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration, ct);

        var channelId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var bundleDir = Path.Combine(Path.GetTempPath(), "antiphon-legacy-bundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bundleDir);
        var legacyPdf = Path.Combine(bundleDir, "legacy.pdf");
        await File.WriteAllBytesAsync(legacyPdf, "%PDF-1.7\nlegacy bytes\n"u8.ToArray(), ct);
        await File.WriteAllTextAsync(Path.Combine(bundleDir, "01-requirements.md"), "# legacy source\n", ct);

        await using (var connection = new NpgsqlConnection(schema.ConnectionString))
        {
            await connection.OpenAsync(ct);
            await InsertLegacyRowAsync(connection, "ChatChannels", new Dictionary<string, object>
            {
                ["Id"] = channelId,
                ["Provider"] = "slack",
                ["ExternalId"] = "legacy-conversation",
                ["Kind"] = 0,
                ["Title"] = "Legacy",
                ["Enabled"] = true,
                ["DigestEnabled"] = false,
                ["MessageCount"] = 7L,
            }, ct);

            await InsertLegacyRowAsync(connection, "AgentSessions", new Dictionary<string, object>
            {
                ["Id"] = sessionId,
                ["DefinitionName"] = "fake",
                ["AgentKind"] = 0,
                ["Status"] = 2,
                ["Cwd"] = bundleDir,
                ["Cols"] = 120,
                ["Rows"] = 30,
            }, ct);

            await InsertLegacyRowAsync(connection, "AgentTasks", new Dictionary<string, object>
            {
                ["Id"] = taskId,
                ["RootTaskId"] = taskId,
                ["Title"] = "Legacy docs",
                ["Goal"] = "Write the legacy docs.",
                ["Kind"] = 0,
                ["Role"] = 0,
                ["ModelLevel"] = 2,
                ["Workspace"] = 0,
                ["WorkingDirectory"] = bundleDir,
                ["Status"] = 4,
                ["DeliverableBundleDir"] = bundleDir,
                ["DeliverablePdfPath"] = legacyPdf,
                ["DeliverableFileCount"] = 4,
                ["MaxAttempts"] = 3,
                ["ReplyTo"] = 0,
            }, ct);

            await InsertLegacyRowAsync(connection, "SessionQueuedMessages", new Dictionary<string, object>
            {
                ["Id"] = messageId,
                ["AgentSessionId"] = sessionId,
                ["Body"] = "Legacy inbound",
                ["Status"] = 2,
                ["Sequence"] = 1L,
                ["Origin"] = 1,
                ["ConversationKey"] = "slack:legacy-conversation",
            }, ct);
        }

        // Now upgrade.
        await using (var db = NewContext(schema))
            await db.GetService<IMigrator>().MigrateAsync(OutboundMigration, ct);

        await using (var db = NewContext(schema))
        {
            var channel = await db.ChatChannels.AsNoTracking().FirstAsync(c => c.Id == channelId, ct);
            channel.Title.ShouldBe("Legacy");
            channel.MessageCount.ShouldBe(7);
            channel.Enabled.ShouldBeTrue();
            channel.OutboundAgentProfile.ShouldBeNull("an existing channel is opted OUT after the upgrade");

            var task = await db.AgentTasks.AsNoTracking().FirstAsync(t => t.Id == taskId, ct);
            task.Title.ShouldBe("Legacy docs");
            task.DeliverableBundleDir.ShouldBe(bundleDir);
            task.DeliverablePdfPath.ShouldBe(legacyPdf, "a historical PDF path survives the upgrade");
            task.DeliverableFileCount.ShouldBe(4);
            task.MaxAttempts.ShouldBe(3, "the upgrade does not impose the conversion task's single attempt");
            task.OutboundDeliveryId.ShouldBeNull();

            var message = await db.SessionQueuedMessages.AsNoTracking().FirstAsync(m => m.Id == messageId, ct);
            message.Body.ShouldBe("Legacy inbound");
            message.ConversationKey.ShouldBe("slack:legacy-conversation");
            message.OutboundDeliveryId.ShouldBeNull();

            db.ChannelOutboundDeliveries.ShouldNotBeNull();
            (await db.ChannelOutboundDeliveries.CountAsync(ct)).ShouldBe(0);
        }

        // The files the old bundle pointed at are still on disk and still readable.
        (await File.ReadAllTextAsync(legacyPdf, ct)).ShouldContain("legacy bytes");
        (await File.ReadAllTextAsync(Path.Combine(bundleDir, "01-requirements.md"), ct))
            .ShouldContain("legacy source");

        // The new relationships are enforced by the database, not only by application code.
        await using (var db = NewContext(schema))
        {
            var key = "duplicate-source-key";
            db.ChannelOutboundDeliveries.Add(NewDelivery(key));
            await db.SaveChangesAsync(ct);
            db.ChannelOutboundDeliveries.Add(NewDelivery(key));
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(ct));
        }

        await using (var db = NewContext(schema))
        {
            var deliveryId = (await db.ChannelOutboundDeliveries.AsNoTracking().FirstAsync(ct)).Id;
            db.AgentTasks.Add(NewTask(deliveryId, bundleDir));
            await db.SaveChangesAsync(ct);
            db.AgentTasks.Add(NewTask(deliveryId, bundleDir));
            await Should.ThrowAsync<DbUpdateException>(async () => await db.SaveChangesAsync(ct));
        }

        Directory.Delete(bundleDir, recursive: true);
    }

    /// <summary>
    /// Enum values are a wire and storage contract. The new members are appended; nothing that was
    /// already persisted moves.
    /// </summary>
    [Test]
    public void New_enum_values_preserve_historical_numbers()
    {
        ((int)AttentionKind.OutboundDelivery).ShouldBe(39);

        ((int)ChannelOutboundDeliveryState.Pending).ShouldBe(0);
        ((int)ChannelOutboundDeliveryState.Converting).ShouldBe(1);
        ((int)ChannelOutboundDeliveryState.Ready).ShouldBe(2);
        ((int)ChannelOutboundDeliveryState.Publishing).ShouldBe(3);
        ((int)ChannelOutboundDeliveryState.Published).ShouldBe(4);
        ((int)ChannelOutboundDeliveryState.Held).ShouldBe(5);
        ((int)ChannelOutboundDeliveryState.PublishUncertain).ShouldBe(6);
        ((int)ChannelOutboundDeliveryState.Failed).ShouldBe(7);

        ((int)ChannelOutboundTrigger.MarkdownSources).ShouldBe(0);
        ((int)ChannelOutboundTrigger.EveryAgentReply).ShouldBe(1);
        ((int)ChannelOutboundSendKind.Main).ShouldBe(0);
        ((int)ChannelOutboundSendKind.Control).ShouldBe(3);
        ((int)ChannelOutboundOrigin.AgentReply).ShouldBe(0);
        ((int)ChannelOutboundOrigin.Control).ShouldBe(1);

        // Held, Failed and PublishUncertain are distinct from Published. Nothing may treat them as
        // a successful send, so nothing may share its number.
        new[]
        {
            ChannelOutboundDeliveryState.Held,
            ChannelOutboundDeliveryState.Failed,
            ChannelOutboundDeliveryState.PublishUncertain,
        }.ShouldAllBe(s => s != ChannelOutboundDeliveryState.Published);
    }

    // ---- fixture helpers -----------------------------------------------------------------------

    private static AppDbContext NewContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));

    private static ChannelOutboundDelivery NewDelivery(string sourceKey)
    {
        var now = DateTime.UtcNow;
        return new ChannelOutboundDelivery
        {
            Id = Guid.NewGuid(),
            SourceKey = sourceKey,
            SendKind = ChannelOutboundSendKind.Main,
            ChannelProvider = "slack",
            ConversationId = "legacy-conversation",
            State = ChannelOutboundDeliveryState.Pending,
            InputHash = "hash",
            CreatedAt = now,
            UpdatedAt = now,
            DeadlineAt = now.AddMinutes(2),
        };
    }

    private static AgentTask NewTask(Guid deliveryId, string workingDirectory)
    {
        var id = Guid.NewGuid();
        return new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "Outbound conversion",
            Goal = "Convert.",
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Custom,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = workingDirectory,
            Status = AgentTaskStatus.Queued,
            OutboundDeliveryId = deliveryId,
            CreatedAt = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// Inserts one row into <paramref name="table"/> using the columns the caller cares about, and
    /// fills every OTHER non-nullable column that has no database default with a type-appropriate
    /// placeholder.
    ///
    /// <para>The alternative is spelling out a legacy row's full column list, which would have to be
    /// re-edited every time an unrelated card adds a required column — and a test that is edited to
    /// keep compiling is a test nobody re-reads. What this fixture is ABOUT is the handful of named
    /// values below and whether the upgrade preserves them.</para>
    /// </summary>
    private static async Task InsertLegacyRowAsync(
        NpgsqlConnection connection,
        string table,
        Dictionary<string, object> values,
        CancellationToken ct)
    {
        var required = new List<(string Name, string Type)>();
        await using (var probe = new NpgsqlCommand(
            """
            SELECT column_name, data_type
            FROM information_schema.columns
            WHERE table_name = @table AND is_nullable = 'NO' AND column_default IS NULL;
            """, connection))
        {
            probe.Parameters.AddWithValue("table", table);
            await using var reader = await probe.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                required.Add((reader.GetString(0), reader.GetString(1)));
        }

        foreach (var (name, type) in required)
        {
            if (values.ContainsKey(name))
                continue;
            values[name] = type switch
            {
                "uuid" => Guid.NewGuid(),
                "boolean" => false,
                "integer" or "smallint" or "bigint" => 0,
                "numeric" or "double precision" or "real" => 0m,
                var t when t.StartsWith("timestamp", StringComparison.Ordinal) => DateTime.UtcNow,
                _ => "",
            };
        }

        var columns = string.Join(",", values.Keys.Select(k => $"\"{k}\""));
        var placeholders = string.Join(",", values.Keys.Select((_, i) => "@p" + i));
        await using var insert = new NpgsqlCommand(
            $"INSERT INTO \"{table}\" ({columns}) VALUES ({placeholders});", connection);
        var index = 0;
        foreach (var value in values.Values)
            insert.Parameters.AddWithValue("p" + index++, value);
        await insert.ExecuteNonQueryAsync(ct);
    }
}
