using System.IO.Compression;
using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0179 R1 — zip members, best-effort errors.txt, and no transcript text in the archive.
/// Shared Postgres: every seed is tagged with a unique marker and deleted afterwards.
/// </summary>
[Category("Integration")]
public class DiagnosticsBundleServiceTests
{
    private const string Sentinel = "SECRET_TRANSCRIPT_BODY_xyz_do_not_copy";
    private const string QueueSecret = "QUEUE_BODY_SECRET_do_not_copy";

    [Test]
    public async Task Zip_contains_every_member_for_a_seeded_agent_and_session()
    {
        var marker = $"diag-{Guid.NewGuid():N}";
        var logs = CreateLogDirs(marker);
        try
        {
            var (agentId, sessionId) = await SeedAsync(marker);
            await using var db = CreateContext();
            var runner = new FakeRunner { BufferText = "prompt> " };
            var service = CreateService(db, runner, logs);

            await using var zipStream = await service.BuildAsync(
                new BugReportRequest(
                    Route: "/agents",
                    AgentId: agentId,
                    SessionId: sessionId,
                    ScreenshotPngBase64: TinyPngBase64,
                    Console: [new ConsoleEntry(DateTime.UtcNow, "error", "boom", "/api/x", 500, 12)],
                    IncludePaths: false,
                    Note: "repro"),
                clientSha: "abc123",
                CancellationToken.None);

            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            var names = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.Ordinal);
            names.ShouldContain("manifest.json");
            names.ShouldContain("version.json");
            names.ShouldContain("health.json");
            names.ShouldContain("agent.json");
            names.ShouldContain("session.json");
            names.ShouldContain("transcript-kinds.jsonl");
            names.ShouldContain("buffer.txt");
            names.ShouldContain("screenshot.png");
            names.ShouldContain("console.json");
            names.ShouldContain("server-log.txt");
            names.ShouldContain("runner-log.txt");
            names.ShouldContain("attention.json");

            ReadEntry(zip, "transcript-kinds.jsonl").ShouldContain("UserPrompt");
            ReadEntry(zip, "transcript-kinds.jsonl").ShouldNotContain(Sentinel);
            ReadEntry(zip, "session.json").ShouldNotContain(QueueSecret);
            ReadEntry(zip, "session.json").ShouldContain("bodyLength");
            ReadEntry(zip, "buffer.txt").ShouldContain("prompt>");
            ReadEntry(zip, "server-log.txt").ShouldContain("server-log-line");
            ReadEntry(zip, "console.json").ShouldContain("boom");

            foreach (var entry in zip.Entries)
            {
                if (entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                ReadEntry(zip, entry.FullName).ShouldNotContain(Sentinel);
                ReadEntry(zip, entry.FullName).ShouldNotContain(QueueSecret);
            }
        }
        finally
        {
            await CleanupAsync(marker);
            DeleteLogDirs(logs);
        }
    }

    [Test]
    public async Task A_throwing_section_yields_errors_txt_and_the_rest_of_the_zip()
    {
        var marker = $"diag-{Guid.NewGuid():N}";
        var logs = CreateLogDirs(marker);
        try
        {
            var (agentId, sessionId) = await SeedAsync(marker);
            await using var db = CreateContext();
            var runner = new FakeRunner { BufferError = new InvalidOperationException("runner buffer down") };
            var service = CreateService(db, runner, logs);

            await using var zipStream = await service.BuildAsync(
                new BugReportRequest(AgentId: agentId, SessionId: sessionId),
                clientSha: null,
                CancellationToken.None);

            using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            zip.Entries.Select(e => e.FullName).ShouldContain("errors.txt");
            zip.Entries.Select(e => e.FullName).ShouldContain("agent.json");
            zip.Entries.Select(e => e.FullName).ShouldContain("session.json");
            ReadEntry(zip, "errors.txt").ShouldContain("buffer:");
            ReadEntry(zip, "errors.txt").ShouldContain("runner buffer down");
        }
        finally
        {
            await CleanupAsync(marker);
            DeleteLogDirs(logs);
        }
    }

    private static DiagnosticsBundleService CreateService(
        AppDbContext db, FakeRunner runner, LogDirs logs)
    {
        var agents = new AgentService(
            db,
            new CardWorkflowRunFactory(db, TimeProvider.System),
            new MockEventBus(),
            TimeProvider.System,
            new NoOpDirectoryWriter(),
            NullLogger<AgentService>.Instance);
        var attention = new AttentionService(
            db,
            runner,
            Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()),
            TimeProvider.System,
            NullLogger<AttentionService>.Instance);
        return new DiagnosticsBundleService(
            db,
            agents,
            attention,
            runner,
            TimeProvider.System,
            NullLogger<DiagnosticsBundleService>.Instance,
            Options.Create(new DiagnosticsSettings
            {
                ServerLogDirectory = logs.ServerDir,
                RunnerLogDirectory = logs.RunnerDir,
            }));
    }

    private static async Task<(Guid AgentId, Guid SessionId)> SeedAsync(string marker)
    {
        var now = DateTime.UtcNow;
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using var db = CreateContext();
        db.Projects.Add(new Project
        {
            Id = Guid.NewGuid(),
            Name = marker,
            GitRepositoryUrl = "",
            LocalRepositoryPath = Path.Combine(Path.GetTempPath(), marker),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.Combine(Path.GetTempPath(), marker),
            Cols = 120,
            Rows = 30,
            CreatedAt = now,
            StartedAt = now,
            LastSeenAt = now
        });
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = marker,
            Slug = marker,
            WorkingDirectory = Path.Combine(Path.GetTempPath(), marker),
            Status = AgentStatus.Running,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = now,
            UpdatedAt = now
        });
        db.TranscriptEntries.Add(new TranscriptEntry
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Sequence = 1,
            Kind = "UserPrompt",
            Role = "user",
            Text = Sentinel,
            CreatedAt = now,
            Timestamp = now
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = sessionId,
            Body = QueueSecret,
            Status = QueuedMessageStatus.Pending,
            Sequence = 1,
            CreatedAt = now
        });
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = AgentIncidentKind.TranscriptBindFailed,
            Severity = AlertSeverity.Warning,
            Message = "unbound",
            CreatedAt = now
        });
        await db.SaveChangesAsync();
        return (agentId, sessionId);
    }

    private static async Task CleanupAsync(string marker)
    {
        await using var db = CreateContext();
        var agentIds = await db.Agents.Where(a => a.Name == marker).Select(a => a.Id).ToListAsync();
        var sessionIds = await db.AgentSessions.Where(s => s.Cwd.EndsWith(marker)).Select(s => s.Id).ToListAsync();
        await db.AgentIncidents.Where(i => agentIds.Contains(i.AgentId)).ExecuteDeleteAsync();
        await db.SessionQueuedMessages.Where(m => sessionIds.Contains(m.AgentSessionId)).ExecuteDeleteAsync();
        await db.TranscriptEntries.Where(t => sessionIds.Contains(t.AgentSessionId)).ExecuteDeleteAsync();
        await db.Agents.Where(a => agentIds.Contains(a.Id)).ExecuteDeleteAsync();
        await db.AgentSessions.Where(s => sessionIds.Contains(s.Id)).ExecuteDeleteAsync();
        await db.Projects.Where(p => p.Name == marker).ExecuteDeleteAsync();
    }

    private static AppDbContext CreateContext() => new(TestDbFixture.CreateDbContextOptions());

    private static string ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"missing {name}");
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static LogDirs CreateLogDirs(string marker)
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-diag-" + marker);
        var server = Path.Combine(root, "server");
        var runner = Path.Combine(root, "runner");
        Directory.CreateDirectory(server);
        Directory.CreateDirectory(runner);
        File.WriteAllText(Path.Combine(server, "antiphon-20260824.log"), "server-log-line\n");
        File.WriteAllText(Path.Combine(runner, "session-runner-20260824.log"), "runner-log-line\n");
        return new LogDirs(root, server, runner);
    }

    private static void DeleteLogDirs(LogDirs logs)
    {
        try { Directory.Delete(logs.Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private sealed record LogDirs(string Root, string ServerDir, string RunnerDir);

    private sealed class NoOpDirectoryWriter : IDirectoryWriter
    {
        public void CreateDirectory(string path) { }
    }

    private sealed class FakeRunner : ISessionRunnerClient
    {
        public string BufferText { get; set; } = "";
        public Exception? BufferError { get; set; }

        public Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            Task.FromResult<RunnerCapabilitiesDto?>(new RunnerCapabilitiesDto(
                "ModernConPty", "modern", "test", false, Version: "deadbeef"));

        public Task<string?> GetHealthAsync(CancellationToken ct) =>
            Task.FromResult<string?>("200 Healthy");

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            BufferError is not null
                ? Task.FromException<SessionRunnerBufferDto>(BufferError)
                : Task.FromResult(new SessionRunnerBufferDto(sessionId, BufferText, 1));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
