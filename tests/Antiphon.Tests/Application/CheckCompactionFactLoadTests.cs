using System.Data.Common;
using System.Text.RegularExpressions;
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

/// <summary>
/// CARD-1754. The legacy fact loop is the oracle. Verdicts must stay identical on a transcript
/// with many Check tasks and prompts. The load witnesses are red while LoadFacts still projects
/// every row's text and still scans that text for every Check token.
/// </summary>
[Category("Integration")]
public class CheckCompactionFactLoadTests
{
    private const string HistoricalAssistant = "HISTORICAL-ASSISTANT-CARD-1754";
    private const string HistoricalPrompt = "HISTORICAL-PROMPT-CARD-1754";

    [Test]
    public async Task Seeded_transcript_policy_verdict_matches_the_legacy_fact_computation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString);
        await using var db = NewDb(schema.ConnectionString);
        var loaded = await Service(db).LoadFactsAsync(seeded.SessionId, CancellationToken.None);
        var legacy = LegacyFacts(seeded.Rows, seeded.CheckIds, seeded.HumanBodies);
        var scope = CompactionScopeSnapshot.EligibleSeat(seeded.SessionId, seeded.Accepted);
        var when = seeded.ContinuationAt.AddMinutes(10);

        CompactionContinuationPolicy.Evaluate(scope, loaded, 10, when)
            .ShouldBe(CompactionContinuationPolicy.Evaluate(scope, legacy, 10, when));
    }

    [Test]
    public async Task Rows_outside_the_judged_window_do_not_return_transcript_text()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString);
        var capture = new CommandCapture();
        await using var db = NewDb(schema.ConnectionString, capture);
        var loaded = await Service(db).LoadFactsAsync(seeded.SessionId, CancellationToken.None);

        loaded.Any(f => f.Text != null && f.Text.Contains(HistoricalAssistant, StringComparison.Ordinal))
            .ShouldBeFalse();
        loaded.Any(f => f.Text != null && f.Text.Contains(HistoricalPrompt, StringComparison.Ordinal))
            .ShouldBeFalse();
        var projecting = capture.Commands.Where(ProjectsTranscriptText).ToArray();
        projecting.ShouldNotBeEmpty();
        foreach (var sql in projecting)
            TextProjectionIsRestricted(sql).ShouldBeTrue(sql);
    }

    [Test]
    public async Task Unchanged_session_does_not_project_transcript_text_again()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seeded = await SeedAsync(schema.ConnectionString);
        var capture = new CommandCapture();
        await using var db = NewDb(schema.ConnectionString, capture);
        var service = Service(db);
        await service.LoadFactsAsync(seeded.SessionId, CancellationToken.None);
        capture.Commands.Clear();
        await service.LoadFactsAsync(seeded.SessionId, CancellationToken.None);

        capture.Commands.Where(ProjectsTranscriptText).ShouldBeEmpty();
    }

    private static CheckCompactionContinuationService Service(AppDbContext db) =>
        new(db, TimeProvider.System, Options.Create(new DelegationSettings()),
            new CheckCompactionContinuationGate(),
            NullLogger<CheckCompactionContinuationService>.Instance);

    private static AppDbContext NewDb(string connectionString, params IInterceptor[] interceptors) =>
        new(interceptors.Length == 0
            ? TestDbFixture.CreateDbContextOptions(connectionString)
            : TestDbFixture.CreateDbContextOptions(connectionString, interceptors));

    private static async Task<Seeded> SeedAsync(string connectionString)
    {
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var checkIds = Enumerable.Range(0, 40).Select(_ => Guid.NewGuid()).ToArray();
        var rows = new List<TranscriptEntry>();
        var at = accepted;
        rows.Add(Entry(sessionId, 1, TranscriptKinds.TurnEnd, null, at));
        for (var i = 0; i < 30; i++)
        {
            at = at.AddSeconds(1);
            var id = checkIds[i % checkIds.Length];
            var token = (i % 3) switch
            {
                0 => id.ToString("D"),
                1 => id.ToString("N"),
                _ => DelegationReportFormatter.TaskMarker(id),
            };
            rows.Add(Entry(sessionId, 10 + (i * 2), TranscriptKinds.UserPrompt,
                HistoricalPrompt + " " + token + " " + new string('p', 400), at));
            at = at.AddSeconds(1);
            rows.Add(Entry(sessionId, 11 + (i * 2), TranscriptKinds.AssistantText,
                HistoricalAssistant + " " + new string('a', 2000), at));
        }

        var promptAt = at.AddMinutes(1);
        var owning = "please review " + DelegationReportFormatter.TaskMarker(checkIds[0])
            + " and also " + checkIds[1].ToString("N") + "ff";
        rows.Add(Entry(sessionId, 1000, TranscriptKinds.TurnEnd, null, promptAt.AddSeconds(-1)));
        rows.Add(Entry(sessionId, 1001, TranscriptKinds.UserPrompt, owning, promptAt));
        rows.Add(Entry(sessionId, 1002, TranscriptKinds.ToolCall, "read", promptAt.AddSeconds(1), tool: "tool-1"));
        rows.Add(Entry(sessionId, 1003, TranscriptKinds.ToolResult, "ok", promptAt.AddSeconds(2), tool: "tool-1"));
        var boundaryAt = promptAt.AddSeconds(3);
        var continuationAt = promptAt.AddSeconds(4);
        rows.Add(Entry(sessionId, 1004, TranscriptKinds.CompactBoundary, "Context compacted (auto)", boundaryAt, "boundary-1"));
        rows.Add(Entry(sessionId, 1005, TranscriptKinds.UserPrompt,
            TranscriptKinds.CompactionContinuationPromptPrefix + " summary", continuationAt, "cont-1"));

        await using var db = NewDb(connectionString);
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "check",
            Slug = "check-" + agentId.ToString("N")[..8],
            WorkingDirectory = Path.GetTempPath(),
            Kind = AgentKind.ClaudeCode,
            AlwaysOn = true,
            Status = AgentStatus.Running,
            StandingSpecialistRole = AgentTaskRole.Check,
            StandingSpecialistOwnerId = agentId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = accepted,
            UpdatedAt = accepted,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            StandingAgentId = agentId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            CreatedAt = accepted,
            StartedAt = accepted,
            LastSeenAt = continuationAt,
        });
        foreach (var id in checkIds)
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = id,
                RootTaskId = id,
                Title = "check",
                Goal = "look",
                Role = AgentTaskRole.Check,
                Kind = AgentTaskKind.Worker,
                Status = AgentTaskStatus.Succeeded,
                AgentId = agentId,
                AgentSessionId = sessionId,
                WorkingDirectory = Path.GetTempPath(),
                CreatedAt = promptAt,
                CompletedAt = promptAt,
            });
        }

        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = "ok",
            Origin = QueuedMessageOrigin.Ui,
            Status = QueuedMessageStatus.Sent,
            CreatedAt = promptAt,
            SentAt = promptAt,
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = HistoricalPrompt + " human paste that is long enough to match",
            Origin = QueuedMessageOrigin.Channel,
            Status = QueuedMessageStatus.Sent,
            CreatedAt = promptAt,
            SentAt = promptAt,
        });
        db.TranscriptEntries.AddRange(rows);
        await db.SaveChangesAsync();
        return new Seeded(sessionId, accepted, continuationAt, checkIds, rows,
            ["ok", HistoricalPrompt + " human paste that is long enough to match"]);
    }

    private static CompactionTranscriptFact[] LegacyFacts(
        IReadOnlyList<TranscriptEntry> rows, IReadOnlyList<Guid> checkIds, IReadOnlyList<string> humanBodies)
    {
        var tokens = checkIds.SelectMany(id => new[]
        {
            id.ToString("D"), id.ToString("N"), DelegationReportFormatter.TaskMarker(id),
        }).ToArray();
        var humanMatchable = humanBodies.Where(PromptSubmissionMatch.RequiresTextMatch).ToArray();
        return rows.OrderBy(row => row.Sequence).Select(row => new CompactionTranscriptFact(
            row.Sequence,
            row.Kind,
            row.Text,
            row.Timestamp,
            row.CreatedAt,
            row.Uuid,
            row.ToolUseId,
            IsCorrelatedCheck: row.Kind == TranscriptKinds.UserPrompt
                && row.Text is not null
                && tokens.Any(token => row.Text.Contains(token, StringComparison.Ordinal)),
            IsHumanOrigin: row.Kind == TranscriptKinds.UserPrompt
                && row.Text is not null
                && humanMatchable.Any(body => PromptSubmissionMatch.IsCompleteIn(body, row.Text)))).ToArray();
    }

    private static TranscriptEntry Entry(
        Guid sessionId, long sequence, string kind, string? text, DateTime at, string? uuid = null, string? tool = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        Timestamp = at,
        CreatedAt = at,
        Uuid = uuid,
        ToolUseId = tool,
    };

    private static bool ProjectsTranscriptText(string sql)
    {
        if (!sql.Contains("TranscriptEntries", StringComparison.Ordinal))
            return false;
        var from = sql.IndexOf(" FROM ", StringComparison.OrdinalIgnoreCase);
        var select = from > 0 ? sql[..from] : sql;
        select = Regex.Replace(
            select,
            @"(?i)(char_length|character_length|octet_length|length)\s*\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)?""Text""\s*\)",
            "");
        return select.Contains("\"Text\"", StringComparison.Ordinal);
    }

    private static bool TextProjectionIsRestricted(string sql)
    {
        var whereAt = sql.IndexOf(" WHERE ", StringComparison.OrdinalIgnoreCase);
        if (whereAt < 0)
            return false;
        var where = sql[whereAt..];
        return where.Contains("\"Kind\"", StringComparison.Ordinal)
            || where.Contains("\"Sequence\"", StringComparison.Ordinal);
    }

    private sealed record Seeded(
        Guid SessionId,
        DateTime Accepted,
        DateTime ContinuationAt,
        Guid[] CheckIds,
        List<TranscriptEntry> Rows,
        string[] HumanBodies);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = new();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
