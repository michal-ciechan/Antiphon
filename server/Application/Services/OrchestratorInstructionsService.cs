using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>The file and row a GET route serves. Null when nothing has been written yet.</summary>
public sealed record OrchestratorInstructionsPublication(
    string Version,
    long Revision,
    string FileText,
    string WrittenPath);

/// <summary>
/// CARD-0822. One coalesced reconcile writes the instructions file and the fleet row.
/// Notices are queued from <see cref="QueueNoticesAsync"/> after a successful write.
/// </summary>
public sealed class OrchestratorInstructionsService : IOrchestratorInstructionsSignals
{
    internal const string NoteHeader = "[orchestrator-instructions]";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex SinceVersion = new(
        @"changed more than once since v(?<since>[0-9a-f]{8})|\(v(?<arrow>[0-9a-f]{8}) →",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopes;
    private readonly SemaphoreSlim _reconcile = new(1, 1);
    private readonly object _signalLock = new();
    private string? _pendingReason;
    private Task? _pump;

    public OrchestratorInstructionsService(IServiceScopeFactory scopes) => _scopes = scopes;

    public void Signal(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return;

        lock (_signalLock)
        {
            _pendingReason = reason.Trim();
            if (_pump is null || _pump.IsCompleted)
                _pump = Task.Run(PumpAsync);
        }
    }

    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task pump;
            lock (_signalLock)
            {
                if (_pendingReason is null && (_pump is null || _pump.IsCompleted))
                    return;
                pump = _pump ?? Task.CompletedTask;
            }

            await pump.ConfigureAwait(false);
        }
    }

    public async Task<string?> ReconcileNowAsync(string reason, CancellationToken ct = default)
    {
        await _reconcile.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await ReconcileCoreAsync(reason, ct).ConfigureAwait(false);
        }
        finally
        {
            _reconcile.Release();
        }
    }

    public async Task<OrchestratorInstructionsPublication?> ReadAsync(CancellationToken ct = default)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .OrchestratorInstructionsStates.AsNoTracking()
            .SingleOrDefaultAsync(state => state.Id == OrchestratorInstructionsState.FleetId, ct)
            .ConfigureAwait(false);
        return row is null ? null : Publish(row);
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            string? reason;
            lock (_signalLock)
            {
                reason = _pendingReason;
                _pendingReason = null;
                if (reason is null)
                {
                    _pump = null;
                    return;
                }
            }

            try
            {
                await ReconcileNowAsync(reason, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogFailure(ex, reason);
            }
        }
    }

    private async Task<string?> ReconcileCoreAsync(string reason, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var settings = provider.GetRequiredService<IOptions<DelegationSettings>>().Value;
        if (!settings.OrchestratorInstructions.Enabled)
            return null;

        var db = provider.GetRequiredService<AppDbContext>();
        var row = await db.OrchestratorInstructionsStates
            .SingleOrDefaultAsync(state => state.Id == OrchestratorInstructionsState.FleetId, ct)
            .ConfigureAwait(false);
        var clippedReason = Clip(reason.Trim(), 200);

        OrchestratorInstructionsSnapshot snapshot;
        string body;
        string version;
        try
        {
            snapshot = await provider.GetRequiredService<OrchestratorInstructionsSnapshotBuilder>()
                .BuildAsync(ct)
                .ConfigureAwait(false);
            body = OrchestratorInstructionsRenderer.Render(snapshot, settings.OrchestratorInstructions.MaxBytes);
            version = OrchestratorInstructionsRenderer.VersionOf(body);
        }
        catch (OrchestratorInstructionsRefusedException ex)
        {
            var failedAt = provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            await RecordWriteFailureAsync(db, row, clippedReason, ex, failedAt, ct).ConfigureAwait(false);
            return row?.Version;
        }

        if (row is not null && string.Equals(row.Version, version, StringComparison.Ordinal))
            return version;

        var path = AntiphonDataPaths.ResolveOrchestratorInstructionsPath(
            OrchestratorInstructionsPaths.FromCurrentProcess(),
            settings.OrchestratorInstructions.Path);
        var revision = (row?.Revision ?? 0) + 1;
        var fileText = FileText(version, revision, body);
        try
        {
            WriteAtomically(path, fileText);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            var failedAt = provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
            await RecordWriteFailureAsync(db, row, clippedReason, ex, failedAt, ct).ConfigureAwait(false);
            return row?.Version;
        }

        var json = JsonSerializer.Serialize(snapshot, Json);
        var previousJson = row?.SnapshotJson;
        var previous = Deserialize(previousJson);
        var oldVersion = row?.Version;
        var now = provider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        if (row is null)
        {
            row = new OrchestratorInstructionsState { Id = OrchestratorInstructionsState.FleetId };
            db.OrchestratorInstructionsStates.Add(row);
        }

        row.Revision = revision;
        row.Version = version;
        row.Body = body;
        row.PreviousSnapshotJson = string.IsNullOrEmpty(previousJson) ? null : previousJson;
        row.SnapshotJson = json;
        row.WrittenAt = now;
        row.WrittenPath = path;
        row.LastReason = clippedReason;
        row.LastWriteError = null;
        var delta = previous is null ? "" : OrchestratorInstructionsDelta.Format(previous, snapshot);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            Kind = AgentIncidentKind.OrchestratorInstructionsRegenerated,
            Severity = AlertSeverity.Info,
            CreatedAt = now,
            Message = Clip(string.IsNullOrEmpty(delta) ? clippedReason : clippedReason + ". " + delta, AgentIncident.MessageMaxLength),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var url = settings.ApiBaseUrl.TrimEnd('/') + "/api/orchestrator-instructions";
        await QueueNoticesAsync(provider, previous, snapshot, oldVersion, version, path, url, ct)
            .ConfigureAwait(false);
        return version;
    }

    private async Task QueueNoticesAsync(
        IServiceProvider services,
        OrchestratorInstructionsSnapshot? before,
        OrchestratorInstructionsSnapshot after,
        string? previousVersion,
        string version,
        string path,
        string url,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        var queue = services.GetRequiredService<SessionMessageQueueService>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var population = await LoadPopulationAsync(db, ct).ConfigureAwait(false);
        var chosen = OrchestratorInstructionsRecipients.Select(
            population.Agents,
            population.Sessions,
            population.Tasks,
            version,
            settings.OrchestratorInstructions.Notify);
        if (chosen.Count == 0)
            return;

        var delta = OrchestratorInstructionsDelta.Format(before, after);
        var rows = await db.AgentSessions.Where(session => chosen.Contains(session.Id)).ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var session in rows)
        {
            var columnVersion = session.OrchestratorInstructionsVersion;
            var pending = await db.SessionQueuedMessages
                .Where(message => message.AgentSessionId == session.Id
                    && message.Status == QueuedMessageStatus.Pending
                    && message.NoteHeader == NoteHeader)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            string? since = null;
            if (pending.Count > 0)
            {
                since = ParseSince(pending.Select(message => message.Body));
                foreach (var message in pending)
                {
                    message.Status = QueuedMessageStatus.Canceled;
                    message.CanceledAt = now;
                }

                since ??= columnVersion ?? previousVersion;
            }

            var body = ChannelPreamble.OrchestratorInstructionsChangedBody(
                delta,
                path,
                url,
                oldVersion: since is null ? columnVersion ?? previousVersion : null,
                newVersion: since is null ? version : null,
                changedMoreThanOnceSince: since);
            session.OrchestratorInstructionsVersion = version;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            await queue.EnqueueAsync(
                session.Id,
                body,
                MessageSendMode.WhenIdle,
                ct,
                origin: QueuedMessageOrigin.System,
                noteHeader: NoteHeader,
                deliverIfIdle: true).ConfigureAwait(false);

            var sessionText = session.Id.ToString("D");
            var agentId = session.StandingAgentId ?? await db.Agents.AsNoTracking()
                .Where(agent => agent.PersistentSessionId == sessionText)
                .Select(agent => (Guid?)agent.Id)
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = session.Id,
                Kind = AgentIncidentKind.OrchestratorInstructionsNotified,
                Severity = AlertSeverity.Info,
                CreatedAt = now,
                Message = Clip("orchestrator instructions " + version + " notified", AgentIncident.MessageMaxLength),
            });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    internal static async Task<(
        List<OrchestratorInstructionsAgent> Agents,
        List<OrchestratorInstructionsSession> Sessions,
        List<OrchestratorInstructionsTask> Tasks)> LoadPopulationAsync(AppDbContext db, CancellationToken ct)
    {
        var bundleIds = await db.AgentBundleAttachments.AsNoTracking()
            .Where(attachment => attachment.BundleKey == InstructionBundles.Orchestrator)
            .Select(attachment => attachment.AgentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var bundles = bundleIds.ToHashSet();
        var agentRows = await db.Agents.AsNoTracking()
            .Select(agent => new
            {
                agent.Id,
                agent.IsPoolDelegate,
                agent.StandingSpecialistRole,
                agent.PersistentSessionId,
                agent.PolicyRefreshMode,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var agents = agentRows.Select(agent => new OrchestratorInstructionsAgent(
            agent.Id,
            bundles.Contains(agent.Id),
            agent.IsPoolDelegate,
            StandingSpecialistSeatPolicy.IsCheck(new Agent { StandingSpecialistRole = agent.StandingSpecialistRole }),
            Guid.TryParse(agent.PersistentSessionId, out var sessionId) ? sessionId : null,
            agent.PolicyRefreshMode)).ToList();

        var tasks = await db.AgentTasks.AsNoTracking()
            .Where(task => task.Status == AgentTaskStatus.Dispatched || task.Status == AgentTaskStatus.Working)
            .Select(task => new OrchestratorInstructionsTask(task.Kind, task.Status, task.AgentSessionId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var sessionIds = agents.Select(agent => agent.PersistentSessionId).OfType<Guid>()
            .Concat(tasks.Select(task => task.SessionId).OfType<Guid>())
            .Distinct()
            .ToArray();
        var sessions = sessionIds.Length == 0
            ? []
            : await db.AgentSessions.AsNoTracking()
                .Where(session => sessionIds.Contains(session.Id))
                .Select(session => new OrchestratorInstructionsSession(
                    session.Id,
                    session.StandingAgentId,
                    session.Status,
                    session.SessionBackend,
                    session.CardId,
                    session.OrchestratorInstructionsVersion))
                .ToListAsync(ct)
                .ConfigureAwait(false);
        return (agents, sessions, tasks);
    }

    private static string? ParseSince(IEnumerable<string> bodies)
    {
        foreach (var body in bodies)
        {
            var match = SinceVersion.Match(body);
            if (!match.Success)
                continue;
            if (match.Groups["arrow"].Length == 8)
                return match.Groups["arrow"].Value;
            if (match.Groups["since"].Length == 8)
                return match.Groups["since"].Value;
        }

        return null;
    }

    private async Task RecordWriteFailureAsync(
        AppDbContext db,
        OrchestratorInstructionsState? row,
        string reason,
        Exception ex,
        DateTime now,
        CancellationToken ct)
    {
        if (row is not null)
            row.LastWriteError = Clip(ex.Message, 2000);
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            Kind = AgentIncidentKind.OrchestratorInstructionsWriteFailed,
            Severity = AlertSeverity.Warning,
            CreatedAt = now,
            Message = Clip(reason + ". " + ex.GetType().Name, AgentIncident.MessageMaxLength),
        });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    internal static string FileText(string version, long revision, string body) =>
        $"[orchestrator-instructions v{version} rev {revision}]\n{body}\n";

    private static OrchestratorInstructionsPublication Publish(OrchestratorInstructionsState row) =>
        new(row.Version, row.Revision, FileText(row.Version, row.Revision, row.Body), row.WrittenPath);

    private static OrchestratorInstructionsSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<OrchestratorInstructionsSnapshot>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private void LogFailure(Exception ex, string reason)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            scope.ServiceProvider.GetService<ILogger<OrchestratorInstructionsService>>()
                ?.LogWarning(ex, "Orchestrator instructions reconcile failed ({Reason})", reason);
        }
        catch (Exception logEx) when (logEx is not OutOfMemoryException)
        {
            // The reconcile failure is already swallowed by the pump.
        }
    }

    private static void WriteAtomically(string path, string text)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, text, Utf8);
        File.Move(temp, path, overwrite: true);
    }

    private static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..max];
}

internal static class OrchestratorInstructionsLaunch
{
    public const string PathVariable = "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS";
    public const string UrlVariable = "ANTIPHON_ORCHESTRATOR_INSTRUCTIONS_URL";

    public static void Apply(IDictionary<string, string> env, DelegationSettings settings)
    {
        if (!settings.OrchestratorInstructions.Enabled)
            return;
        env[PathVariable] = AntiphonDataPaths.ResolveOrchestratorInstructionsPath(
            OrchestratorInstructionsPaths.FromCurrentProcess(),
            settings.OrchestratorInstructions.Path);
        env[UrlVariable] = settings.ApiBaseUrl.TrimEnd('/') + "/api/orchestrator-instructions";
    }
}

internal static class OrchestratorInstructionsPaths
{
    public static AgentTuiPathEnvironment FromCurrentProcess()
    {
        var platform = OperatingSystem.IsWindows()
            ? AgentTuiPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? AgentTuiPlatform.MacOS
                : AgentTuiPlatform.Linux;
        return new AgentTuiPathEnvironment(
            platform,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}
