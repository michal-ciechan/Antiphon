using System.Text;
using System.Text.Json;
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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

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

    /// <summary>S3 fills the WhenIdle notice lane. S2 keeps the write path free of delivery.</summary>
    private Task QueueNoticesAsync(
        IServiceProvider services,
        OrchestratorInstructionsSnapshot? before,
        OrchestratorInstructionsSnapshot after,
        string? previousVersion,
        string version,
        string path,
        string url,
        CancellationToken ct)
    {
        _ = (services, before, after, previousVersion, version, path, url, ct);
        return Task.CompletedTask;
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
