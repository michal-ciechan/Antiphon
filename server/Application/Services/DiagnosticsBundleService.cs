using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Builds the CARD-0179 Report-bug zip. Every member is best-effort: a throwing section
/// writes an <c>errors.txt</c> line and the rest of the archive still returns.
/// </summary>
public sealed class DiagnosticsBundleService
{
    public const string ClientShaHeader = "X-Antiphon-Client-Sha";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    private readonly AppDbContext _db;
    private readonly AgentService _agents;
    private readonly AttentionService _attention;
    private readonly ISessionRunnerClient _runner;
    private readonly TimeProvider _clock;
    private readonly DiagnosticsSettings _settings;
    private readonly IHostEnvironment? _env;
    private readonly HealthCheckService? _health;
    private readonly ILogger<DiagnosticsBundleService> _logger;

    public DiagnosticsBundleService(
        AppDbContext db,
        AgentService agents,
        AttentionService attention,
        ISessionRunnerClient runner,
        TimeProvider clock,
        ILogger<DiagnosticsBundleService> logger,
        IOptions<DiagnosticsSettings>? settings = null,
        IHostEnvironment? env = null,
        HealthCheckService? health = null)
    {
        _db = db;
        _agents = agents;
        _attention = attention;
        _runner = runner;
        _clock = clock;
        _logger = logger;
        _settings = settings?.Value ?? new DiagnosticsSettings();
        _env = env;
        _health = health;
    }

    public async Task<MemoryStream> BuildAsync(
        BugReportRequest request,
        string? clientSha,
        CancellationToken ct)
    {
        var screenshot = DecodeScreenshotOrThrow(request.ScreenshotPngBase64);
        var projectDirs = await LoadProjectDirectoriesAsync(request.AgentId, ct);
        var redactor = new DiagnosticsRedactor(request.IncludePaths, projectDirs);
        var errors = new List<string>();
        var now = _clock.GetUtcNow().UtcDateTime;

        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var serverSha = AntiphonVersion.Sha;
            string? runnerSha = null;

            await TrySection(errors, "version", async () =>
            {
                RunnerCapabilitiesDto? caps = null;
                try { caps = await _runner.GetCapabilitiesAsync(ct); }
                catch (Exception ex)
                {
                    errors.Add($"version.runner: {ex.Message}");
                }

                runnerSha = caps?.Version ?? caps?.Build?.CommitSha;
                WriteText(zip, redactor, "version.json", JsonSerializer.Serialize(new
                {
                    server = serverSha,
                    runner = runnerSha ?? "unknown",
                    client = string.IsNullOrWhiteSpace(clientSha) ? "unknown" : clientSha,
                    serverInformational = AntiphonVersion.Informational,
                    runnerBuild = caps?.Build
                }, Json));
            });

            await TrySection(errors, "manifest", async () =>
            {
                WriteText(zip, redactor, "manifest.json", JsonSerializer.Serialize(new
                {
                    at = now,
                    route = request.Route,
                    agentId = request.AgentId,
                    sessionId = request.SessionId,
                    includePaths = request.IncludePaths,
                    note = request.Note,
                    server = serverSha,
                    runner = runnerSha ?? "unknown",
                    client = string.IsNullOrWhiteSpace(clientSha) ? "unknown" : clientSha
                }, Json));
                await Task.CompletedTask;
            });

            await TrySection(errors, "health", async () =>
            {
                object? serverHealth = null;
                if (_health is not null)
                {
                    var report = await _health.CheckHealthAsync(ct);
                    serverHealth = new
                    {
                        status = report.Status.ToString(),
                        entries = report.Entries.ToDictionary(
                            e => e.Key,
                            e => new { status = e.Value.Status.ToString(), e.Value.Description })
                    };
                }

                string? runnerHealth = null;
                try { runnerHealth = await _runner.GetHealthAsync(ct); }
                catch (Exception ex)
                {
                    errors.Add($"health.runner: {ex.Message}");
                }

                object? capabilities = null;
                try { capabilities = await _runner.GetCapabilitiesAsync(ct); }
                catch (Exception ex)
                {
                    errors.Add($"health.capabilities: {ex.Message}");
                }

                WriteText(zip, redactor, "health.json", JsonSerializer.Serialize(new
                {
                    server = serverHealth,
                    runnerHealth,
                    runnerCapabilities = capabilities
                }, Json));
            });

            if (request.AgentId is Guid agentId)
            {
                await TrySection(errors, "agent", async () =>
                {
                    var detail = await _agents.GetByIdAsync(agentId, ct);
                    WriteText(zip, redactor, "agent.json", JsonSerializer.Serialize(detail, Json));
                });
            }

            if (request.SessionId is Guid sessionId)
            {
                await TrySection(errors, "session", async () =>
                {
                    var session = await _db.AgentSessions.AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
                        ?? throw new InvalidOperationException($"Session {sessionId} was not found.");

                    var working = await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct);
                    var queue = await _db.SessionQueuedMessages.AsNoTracking()
                        .Where(m => m.AgentSessionId == sessionId)
                        .OrderBy(m => m.Sequence)
                        .ToListAsync(ct);
                    var incidentQuery = _db.AgentIncidents.AsNoTracking()
                        .Where(i => i.SessionId == sessionId
                            || (request.AgentId != null && i.AgentId == request.AgentId));
                    var incidents = await incidentQuery
                        .OrderByDescending(i => i.CreatedAt)
                        .Take(50)
                        .Select(i => new
                        {
                            i.Id, i.AgentId, i.SessionId,
                            kind = i.Kind.ToString(),
                            severity = i.Severity.ToString(),
                            i.Message, i.ExitCode, i.FailureReason, i.CreatedAt
                        })
                        .ToListAsync(ct);

                    WriteText(zip, redactor, "session.json", JsonSerializer.Serialize(new
                    {
                        session.Id,
                        session.DefinitionName,
                        agentKind = session.AgentKind.ToString(),
                        status = session.Status.ToString(),
                        session.Cwd,
                        session.CreatedAt,
                        session.StartedAt,
                        session.LastSeenAt,
                        session.EndedAt,
                        session.ExitCode,
                        session.FailureReason,
                        sessionBackend = session.SessionBackend.ToString(),
                        session.EffectiveModelId,
                        working,
                        queue = queue.Select(m => new
                        {
                            m.Id,
                            m.Sequence,
                            status = m.Status.ToString(),
                            origin = m.Origin.ToString(),
                            bodyLength = m.Body.Length,
                            bodySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(m.Body)))[..16]
                                .ToLowerInvariant(),
                            m.CreatedAt,
                            m.DeliveryAttempts
                        }),
                        incidents
                    }, Json));
                });

                await TrySection(errors, "transcript-kinds", async () =>
                {
                    var entries = await _db.TranscriptEntries.AsNoTracking()
                        .Where(t => t.AgentSessionId == sessionId)
                        .OrderByDescending(t => t.Sequence)
                        .Take(200)
                        .Select(t => new
                        {
                            seq = t.Sequence,
                            ts = t.Timestamp,
                            kind = t.Kind,
                            role = t.Role,
                            len = t.Text == null ? 0 : t.Text.Length
                        })
                        .ToListAsync(ct);
                    entries.Reverse();
                    var sb = new StringBuilder();
                    foreach (var e in entries)
                        sb.AppendLine(JsonSerializer.Serialize(e, Json));
                    WriteText(zip, redactor, "transcript-kinds.jsonl", sb.ToString());
                });

                await TrySection(errors, "buffer", async () =>
                {
                    var buffer = await _runner.GetBufferAsync(sessionId, ct);
                    WriteText(zip, redactor, "buffer.txt", buffer.Buffer);
                });
            }

            if (screenshot is { Length: > 0 })
            {
                await TrySection(errors, "screenshot", async () =>
                {
                    var entry = zip.CreateEntry("screenshot.png", CompressionLevel.Fastest);
                    await using var dest = entry.Open();
                    await dest.WriteAsync(screenshot, ct);
                });
            }

            await TrySection(errors, "console", async () =>
            {
                var cap = Math.Max(1, _settings.MaxConsoleEntries);
                var entries = (request.Console ?? []).TakeLast(cap).ToList();
                WriteText(zip, redactor, "console.json", JsonSerializer.Serialize(entries, Json));
                await Task.CompletedTask;
            });

            await TrySection(errors, "server-log", async () =>
            {
                WriteText(zip, redactor, "server-log.txt", TailNewestLog(
                    ResolveLogDirectory(_settings.ServerLogDirectory),
                    _settings.ServerLogPattern));
                await Task.CompletedTask;
            });

            await TrySection(errors, "runner-log", async () =>
            {
                WriteText(zip, redactor, "runner-log.txt", TailNewestLog(
                    ResolveLogDirectory(_settings.RunnerLogDirectory),
                    _settings.RunnerLogPattern));
                await Task.CompletedTask;
            });

            await TrySection(errors, "attention", async () =>
            {
                var attention = await _attention.GetAsync(ct);
                WriteText(zip, redactor, "attention.json", JsonSerializer.Serialize(attention, Json));
            });

            if (errors.Count > 0)
            {
                WriteText(zip, redactor, "errors.txt", string.Join(Environment.NewLine, errors) + Environment.NewLine);
                _logger.LogWarning("Diagnostics bundle completed with {Count} section error(s)", errors.Count);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private byte[]? DecodeScreenshotOrThrow(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var payload = raw.Trim();
        const string prefix = "base64,";
        var comma = payload.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (comma >= 0)
            payload = payload[(comma + prefix.Length)..];

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            throw new BadRequestException("ScreenshotPngBase64 is not valid base64.");
        }

        if (bytes.Length > _settings.MaxScreenshotBytes)
        {
            throw new BadRequestException(
                $"Screenshot exceeds {_settings.MaxScreenshotBytes} bytes (got {bytes.Length}).");
        }

        return bytes;
    }

    private async Task<IReadOnlyList<string>> LoadProjectDirectoriesAsync(Guid? agentId, CancellationToken ct)
    {
        var dirs = await _db.Projects.AsNoTracking()
            .Where(p => p.LocalRepositoryPath != null && p.LocalRepositoryPath != "")
            .Select(p => p.LocalRepositoryPath!)
            .ToListAsync(ct);
        if (agentId is Guid id)
        {
            var cwd = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => a.WorkingDirectory)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(cwd))
                dirs.Add(cwd);
        }

        return dirs;
    }

    private string ResolveLogDirectory(string configured)
    {
        if (Path.IsPathRooted(configured) || _env is null)
            return configured;
        return Path.Combine(_env.ContentRootPath, configured);
    }

    private string TailNewestLog(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
            return string.Empty;

        var newest = new DirectoryInfo(directory)
            .EnumerateFiles(pattern)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        return newest is null ? string.Empty : TailFile(newest.FullName, _settings.MaxLogLines, _settings.MaxLogBytes);
    }

    internal static string TailFile(string path, int maxLines, int maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0)
            return string.Empty;

        var extra = 64 * 1024;
        var take = (int)Math.Min(fs.Length, (long)maxBytes + extra);
        fs.Seek(-take, SeekOrigin.End);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = text.Split('\n');
        if (lines.Length > maxLines)
            lines = lines[^maxLines..];
        var joined = string.Join('\n', lines);
        var utf8 = Encoding.UTF8.GetBytes(joined);
        if (utf8.Length <= maxBytes)
            return joined;
        return Encoding.UTF8.GetString(utf8[^maxBytes..]);
    }

    private static void WriteText(ZipArchive zip, DiagnosticsRedactor redactor, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(redactor.Redact(content));
    }

    private static async Task TrySection(List<string> errors, string name, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (BadRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add($"{name}: {ex.Message}");
        }
    }
}
