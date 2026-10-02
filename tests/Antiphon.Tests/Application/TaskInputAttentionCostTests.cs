using System.Data.Common;
using System.Reflection;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("AgentQueue")]
public sealed class TaskInputAttentionCostTests
{
    [Test]
    public async Task No_input_reads_no_transcripts_despite_large_history()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await SeedHistoryAsync(f);
        // Ordinary delegate queue traffic must not make the session an input candidate.
        await using (var db = f.Db())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = 1,
                Body = "An ordinary delegation brief without a caller input.",
                Origin = QueuedMessageOrigin.Delegation, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var probe = new TranscriptReadProbe();
        (await ReadBuilderAsync(f, probe)).ShouldBeEmpty();
        probe.ReadOperations.ShouldBe(0, "no-input transcript materialization cost");
        probe.Commands.ShouldBe(0, "no-input transcript query cost");
    }

    [Test]
    public async Task Input_reads_exclude_unrelated_history_before_first_delivery()
    {
        await using var f = await TaskInputSpillFixture.CreateAsync();
        await SeedHistoryAsync(f);
        var row = await SeedInputAsync(f, 3000);
        await AddEntryAsync(f, 3001, TranscriptKinds.AssistantText,
            $"I cannot read {row.RemoteSpillRelativePath}.");
        // A retried row has a newer baseline. The first complaint still belongs to it.
        await using (var db = f.Db())
            await db.SessionQueuedMessages.Where(r => r.Id == row.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.LastDeliveryBaselineSequence, 3001L)
                    .SetProperty(r => r.DeliveryAttempts, 2));
        var probe = new TranscriptReadProbe();
        (await ReadBuilderAsync(f, probe)).Single().MessageId.ShouldBe(row.Id);
        probe.ReadOperations.ShouldBeLessThanOrEqualTo(8,
            "input transcript reads must be bounded independently of 2,000 historical rows");
        probe.MaxColumns.ShouldBeLessThanOrEqualTo(4, "transcript projection only");
    }

    private static async Task SeedHistoryAsync(TaskInputSpillFixture f)
    {
        await using var db = f.Db();
        var now = DateTime.UtcNow.AddDays(-1);
        for (var sequence = 1; sequence <= 2000; sequence++)
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
                Kind = sequence % 2 == 0 ? TranscriptKinds.UserPrompt : TranscriptKinds.AssistantText,
                Text = "unrelated old transcript " + new string('x', 1000),
                Timestamp = now, CreatedAt = now,
            });
        await db.SaveChangesAsync();
    }

    private static async Task<SessionQueuedMessage> SeedInputAsync(TaskInputSpillFixture f,
        long sequence)
    {
        await f.Replies.RefineAsync(f.TaskId, new string('q', 1800), CancellationToken.None);
        await using var db = f.Db();
        var row = await db.SessionQueuedMessages.AsNoTracking().OrderByDescending(r => r.Sequence).FirstAsync();
        await AddEntryAsync(f, sequence, TranscriptKinds.UserPrompt, row.Body);
        return row;
    }

    private static async Task AddEntryAsync(TaskInputSpillFixture f, long sequence,
        string kind, string? text, bool timestamp = true)
    {
        await using var db = f.Db();
        var now = new DateTime(2026, 10, 2, 10, 0, 0, DateTimeKind.Utc).AddSeconds(sequence);
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(), AgentSessionId = f.SessionId, Sequence = sequence,
            Kind = kind, Text = text, Timestamp = timestamp ? now : null, CreatedAt = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<List<AttentionItemDto>> ReadBuilderAsync(TaskInputSpillFixture f,
        TranscriptReadProbe? probe = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>(
            TestDbFixture.CreateDbContextOptions(f.ConnectionString));
        if (probe is not null)
            options.AddInterceptors(probe);
        await using var db = new AppDbContext(options.Options);
        var tasks = await db.AgentTasks.AsNoTracking().OrderBy(t => t.CreatedAt).ToListAsync();
        var service = new AttentionService(db, new BridgeQueueHarness.EmptyRunnerClient(),
            Options.Create(new SupervisionSettings()), Options.Create(new DelegationSettings()),
            TimeProvider.System, NullLogger<AttentionService>.Instance);
        var method = typeof(AttentionService).GetMethod("BuildTaskInputUnreadableItemsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<List<AttentionItemDto>>)method.Invoke(service, [tasks, CancellationToken.None])!;
    }

    private sealed class TranscriptReadProbe : DbCommandInterceptor
    {
        public int Commands { get; private set; }
        public int ReadOperations { get; private set; }
        public int MaxColumns { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
            CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("\"TranscriptEntries\"", StringComparison.Ordinal))
                MaxColumns = Math.Max(MaxColumns, result.FieldCount);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult DataReaderDisposing(DbCommand command,
            DataReaderDisposingEventData eventData, InterceptionResult result)
        {
            if (command.CommandText.Contains("\"TranscriptEntries\"", StringComparison.Ordinal))
            {
                Commands++;
                ReadOperations += eventData.ReadCount;
            }
            return result;
        }
    }
}
