using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.IO.Abstractions;

namespace Antiphon.Server.Infrastructure.Agents;

/// <summary>
/// CARD-0298 Class B dry-run census. Reads WMI, the runner, Postgres, and manifests; never kills,
/// never writes, never calls <c>AttentionService</c>.
/// </summary>
public sealed class ZombieCensusService
{
    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IZombieProcessCensus _census;
    private readonly ISessionRunnerClient _runner;
    private readonly AppDbContext _db;
    private readonly IFileSystem _fileSystem;
    private readonly TimeProvider _clock;
    private readonly ZombieCensusSettings _settings;
    private readonly ZombieCensusClassifier _classifier = new();

    public ZombieCensusService(
        IZombieProcessCensus census,
        ISessionRunnerClient runner,
        AppDbContext db,
        IFileSystem fileSystem,
        TimeProvider clock,
        IOptions<ZombieCensusSettings> settings)
    {
        _census = census;
        _runner = runner;
        _db = db;
        _fileSystem = fileSystem;
        _clock = clock;
        _settings = settings.Value;
    }

    public async Task<ZombieCensusResult> RunAsync(CancellationToken cancellationToken)
    {
        var started = _clock.GetUtcNow();
        IReadOnlyList<ZombieOsProcess> processes;
        try
        {
            processes = await _census.SnapshotAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Zombie census failed to read the OS process list.", ex);
        }

        IReadOnlyList<SessionRunnerSessionDto> runnerSessions;
        try
        {
            runnerSessions = await _runner.ListAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Zombie census failed: session-runner did not answer GET /sessions.", ex);
        }

        ZombieCensusDbSnapshot snapshot;
        try
        {
            snapshot = await LoadDbSnapshotAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Zombie census failed to read AgentSessions/Agents/AgentTasks.", ex);
        }

        IReadOnlyDictionary<int, Guid> manifests;
        try
        {
            manifests = LoadManifests();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Zombie census failed to read pty-host manifests.", ex);
        }

        snapshot = snapshot with
        {
            Sessions = snapshot.Sessions.Select(FillActivity).ToList()
        };

        var result = _classifier.Classify(
            processes,
            runnerSessions,
            snapshot,
            manifests,
            new ZombieCensusThresholds(
                _settings.MinDoneMinutes,
                _settings.QuietHours,
                _settings.PidReuseToleranceSeconds),
            started);

        return result with { Duration = _clock.GetUtcNow() - started };
    }

    private async Task<ZombieCensusDbSnapshot> LoadDbSnapshotAsync(CancellationToken cancellationToken)
    {
        var sessionRows = await _db.AgentSessions.AsNoTracking()
            .Select(s => new { s.Id, s.Status, s.StartedAt, s.EndedAt, s.Cwd, s.AgentKind })
            .ToListAsync(cancellationToken);
        var sessions = sessionRows.Select(s => new ZombieCensusSessionRow(
            s.Id,
            s.Status,
            new DateTimeOffset(DateTime.SpecifyKind(s.StartedAt, DateTimeKind.Utc)),
            s.EndedAt is { } ended ? new DateTimeOffset(DateTime.SpecifyKind(ended, DateTimeKind.Utc)) : null,
            s.Cwd,
            s.AgentKind,
            null)).ToList();

        var agentRows = await _db.Agents.AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.Slug, a.IsPoolDelegate, a.Status, a.PersistentSessionId, a.WorkingDirectory })
            .ToListAsync(cancellationToken);
        var agents = agentRows.Select(a => new ZombieCensusAgentRow(
            a.Id,
            a.Name,
            a.Slug,
            a.IsPoolDelegate,
            a.Status,
            Guid.TryParse(a.PersistentSessionId, out var sid) ? sid : null,
            a.WorkingDirectory)).ToList();

        var taskRows = await _db.AgentTasks.AsNoTracking()
            .Select(t => new { t.Id, t.AgentId, t.AgentSessionId, t.Status, t.CompletedAt, t.Workspace, t.WorkingDirectory, t.WorktreePath })
            .ToListAsync(cancellationToken);
        var tasks = taskRows.Select(t => new ZombieCensusTaskRow(
            t.Id,
            t.AgentId,
            t.AgentSessionId,
            t.Status,
            t.CompletedAt is { } completed ? new DateTimeOffset(DateTime.SpecifyKind(completed, DateTimeKind.Utc)) : null,
            t.Workspace,
            t.WorkingDirectory,
            t.WorktreePath)).ToList();

        return new ZombieCensusDbSnapshot(sessions, agents, tasks);
    }

    private IReadOnlyDictionary<int, Guid> LoadManifests()
    {
        var dir = _fileSystem.Path.Combine(_settings.SessionLogPath, "pty-hosts", "manifests");
        var map = new Dictionary<int, Guid>();
        if (!_fileSystem.Directory.Exists(dir))
            return map;

        foreach (var file in _fileSystem.Directory.EnumerateFiles(dir, "*.json"))
        {
            string json;
            try
            {
                json = _fileSystem.File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            ManifestLite? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<ManifestLite>(json, ManifestJson);
            }
            catch (JsonException)
            {
                continue;
            }

            if (manifest is { HostPid: > 0 } && manifest.SessionId != Guid.Empty)
                map[manifest.HostPid] = manifest.SessionId;
        }

        return map;
    }

    private ZombieCensusSessionRow FillActivity(ZombieCensusSessionRow session)
    {
        if (session.Status is not (SessionStatus.Stopped or SessionStatus.Failed))
            return session;

        DateTimeOffset? best = session.ActivityUtc;
        var ansi = _fileSystem.Path.Combine(_settings.SessionLogPath, session.Id.ToString("N") + ".ansi.log");
        best = MaxMtime(best, ansi);

        if (session.AgentKind == AgentKind.ClaudeCode && !string.IsNullOrWhiteSpace(session.Cwd))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var proj = _fileSystem.Path.Combine(userProfile, ".claude", "projects", EncodeClaudeProjectDir(session.Cwd));
            if (_fileSystem.Directory.Exists(proj))
            {
                foreach (var file in _fileSystem.Directory.EnumerateFiles(proj, "*.jsonl"))
                    best = MaxMtime(best, file);
            }
        }

        return session with { ActivityUtc = best };
    }

    private DateTimeOffset? MaxMtime(DateTimeOffset? current, string path)
    {
        try
        {
            if (!_fileSystem.File.Exists(path))
                return current;
            var mtime = _fileSystem.FileInfo.New(path).LastWriteTimeUtc;
            if (current is null || mtime > current)
                return mtime;
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return current;
        }
    }

    internal static string EncodeClaudeProjectDir(string cwd)
    {
        var chars = cwd.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            var ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (!ok)
                chars[i] = '-';
        }

        return new string(chars);
    }

    private sealed record ManifestLite(Guid SessionId, int HostPid);
}
