using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

public sealed record RunnerDefaultSnapshot(
    long Revision,
    string? GlobalRunnerId,
    IReadOnlyDictionary<AgentKind, string> KindDefaults);

/// <summary>
/// CARD-0710 D-9/D-11. Runtime runner defaults. The legacy <c>Delegation:DefaultRunnerId</c> key
/// is imported once into revision 1 and is not read again after a row exists.
/// </summary>
public sealed class RunnerDefaultSettingsService
{
    public const string MigrationReason = "Imported Delegation:DefaultRunnerId.";
    public static readonly AgentKind[] SupportedKinds = [AgentKind.Grok, AgentKind.ClaudeCode, AgentKind.Codex];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _clock;
    private readonly IEventBus? _events;
    private readonly ISessionRunnerDirectory? _runners;

    public RunnerDefaultSettingsService(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider clock,
        IEventBus? events = null,
        ISessionRunnerDirectory? runners = null)
    {
        _db = db;
        _settings = settings.Value;
        _clock = clock;
        _events = events;
        _runners = runners;
    }

    public async Task<RunnerDefaultSnapshot> EnsureInitializedAsync(CancellationToken ct)
    {
        var current = await ReadSnapshotAsync(ct);
        if (current is not null)
            return current;

        var now = _clock.GetUtcNow().UtcDateTime;
        var global = CanonicalImport(_settings.DefaultRunnerId);
        var row = new RunnerRoutingSettings
        {
            Id = RunnerRoutingSettings.SingletonKey,
            Revision = 1,
            GlobalRunnerId = global,
            UpdatedAt = now,
            LastReason = MigrationReason,
            LastProvenance = "Migration",
        };
        var snapshot = new SnapshotBody(global, []);
        _db.RunnerRoutingSettings.Add(row);
        _db.RunnerRoutingRevisions.Add(new RunnerRoutingRevision
        {
            Id = Guid.NewGuid(),
            SettingsId = row.Id,
            Revision = 1,
            SnapshotJson = JsonSerializer.Serialize(snapshot, Json),
            CreatedAt = now,
            Reason = MigrationReason,
            Provenance = "Migration",
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return await ReadSnapshotAsync(ct)
                ?? throw new ConflictException("Runner defaults could not be initialized.", RunnerPlatformProblems.DefaultsRevision);
        }

        return new RunnerDefaultSnapshot(1, global, new Dictionary<AgentKind, string>());
    }

    public async Task<RunnerDefaultSnapshot?> ReadSnapshotAsync(CancellationToken ct)
    {
        var row = await _db.RunnerRoutingSettings.AsNoTracking()
            .Include(s => s.KindDefaults)
            .SingleOrDefaultAsync(s => s.Id == RunnerRoutingSettings.SingletonKey, ct);
        if (row is null)
            return null;
        return new RunnerDefaultSnapshot(
            row.Revision,
            row.GlobalRunnerId,
            row.KindDefaults.ToDictionary(k => k.AgentKind, k => k.RunnerId));
    }

    public async Task<RunnerDefaultsDto> GetAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        return await ProjectAsync(ct);
    }

    public async Task<RunnerDefaultsDto> PutAsync(
        PutRunnerDefaultsRequest request, Guid? callerTaskId, CancellationToken ct)
    {
        ValidatePut(request);
        await EnsureInitializedAsync(ct);
        var row = await _db.RunnerRoutingSettings
            .Include(s => s.KindDefaults)
            .SingleAsync(s => s.Id == RunnerRoutingSettings.SingletonKey, ct);
        if (request.ExpectedRevision != row.Revision)
            throw new ConflictException(
                $"Runner defaults revision {row.Revision} does not match expected {request.ExpectedRevision}.",
                RunnerPlatformProblems.DefaultsRevision);
        if (string.Equals(request.Provenance, "Auto", StringComparison.Ordinal)
            && string.Equals(row.LastProvenance, "Human", StringComparison.Ordinal))
        {
            throw new ConflictException(
                "An automatic edit cannot replace a human runner-default choice.",
                RunnerPlatformProblems.DefaultsHuman);
        }

        var global = CanonicalWrite(request.GlobalRunnerId);
        var kinds = request.KindDefaults
            .Select(k => (k.AgentKind, RunnerId: CanonicalWrite(k.RunnerId) ?? throw new ValidationException(
                nameof(request.KindDefaults), "A kind default cannot be blank.")))
            .OrderBy(k => k.AgentKind)
            .ToList();
        if (_runners is not null)
            RejectNewUnknown(row, global, kinds);
        if (Same(row, global, kinds))
            return await ProjectAsync(ct);

        var next = row.Revision + 1;
        var now = _clock.GetUtcNow().UtcDateTime;
        row.Revision = next;
        row.GlobalRunnerId = global;
        row.UpdatedAt = now;
        row.LastReason = request.Reason.Trim();
        row.LastProvenance = request.Provenance;
        row.LastCallerTaskId = callerTaskId;
        _db.RunnerKindDefaults.RemoveRange(row.KindDefaults);
        foreach (var kind in kinds)
        {
            _db.RunnerKindDefaults.Add(new RunnerKindDefault
            {
                Id = Guid.NewGuid(),
                SettingsId = row.Id,
                AgentKind = kind.AgentKind,
                RunnerId = kind.RunnerId,
            });
        }

        _db.RunnerRoutingRevisions.Add(new RunnerRoutingRevision
        {
            Id = Guid.NewGuid(),
            SettingsId = row.Id,
            Revision = next,
            PreviousRevision = request.ExpectedRevision,
            SnapshotJson = JsonSerializer.Serialize(new SnapshotBody(global, kinds.Select(k => new KindBody(k.AgentKind.ToString(), k.RunnerId)).ToList()), Json),
            CreatedAt = now,
            Reason = request.Reason.Trim(),
            Provenance = request.Provenance,
            CallerTaskId = callerTaskId,
        });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDefaultsConflict(ex))
        {
            throw new ConflictException(
                "Runner defaults were updated by someone else.",
                ex,
                RunnerPlatformProblems.DefaultsRevision);
        }

        if (_events is not null)
        {
            try
            {
                await _events.PublishToAllAsync("RunnerDefaultsChanged", new { revision = next }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The revision is durable. A lost event is recovered by refetch.
            }
        }

        return await ProjectAsync(ct);
    }

    public async Task<RunnerDefaultsRevisionPageDto> RevisionsAsync(long? beforeRevision, int limit, CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        limit = Math.Clamp(limit, 1, 100);
        var query = _db.RunnerRoutingRevisions.AsNoTracking()
            .Where(r => r.SettingsId == RunnerRoutingSettings.SingletonKey);
        if (beforeRevision is long before)
            query = query.Where(r => r.Revision < before);
        var rows = await query.OrderByDescending(r => r.Revision).Take(limit + 1).ToListAsync(ct);
        var page = rows.Take(limit).Select(ToRevision).ToList();
        long? next = rows.Count > limit ? page[^1].Revision : null;
        return new RunnerDefaultsRevisionPageDto(page, next);
    }

    public static string? CanonicalImport(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return null;
        var trimmed = configured.Trim();
        if (RunnerRequestIntent.IsDesktopAlias(trimmed))
            return RunnerPlatformWire.DesktopId;
        return trimmed;
    }

    private async Task<RunnerDefaultsDto> ProjectAsync(CancellationToken ct)
    {
        var row = await _db.RunnerRoutingSettings.AsNoTracking()
            .Include(s => s.KindDefaults)
            .SingleAsync(s => s.Id == RunnerRoutingSettings.SingletonKey, ct);
        var kinds = row.KindDefaults.OrderBy(k => k.AgentKind).Select(k => new RunnerKindDefaultDto(
            k.AgentKind, k.RunnerId, row.GlobalRunnerId, "KindDefault")).ToList();
        var unresolved = new List<string>();
        if (_runners is not null)
        {
            var known = KnownIds();
            NoteUnresolved(unresolved, known, row.GlobalRunnerId);
            foreach (var kind in row.KindDefaults)
                NoteUnresolved(unresolved, known, kind.RunnerId);
        }
        return new RunnerDefaultsDto(
            row.Revision,
            row.GlobalRunnerId,
            kinds,
            row.UpdatedAt,
            row.LastReason,
            row.LastProvenance,
            row.LastCallerTaskId,
            SupportedKinds.Select(k => k.ToString()).ToList(),
            unresolved);
    }

    private static void ValidatePut(PutRunnerDefaultsRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Reason is null || request.Reason.Trim().Length is < 1 or > 400)
            errors[nameof(request.Reason)] = ["A reason of 1 to 400 characters is required."];
        if (request.Provenance is not ("Human" or "Auto"))
            errors[nameof(request.Provenance)] = ["Provenance must be Human or Auto."];
        if (request.KindDefaults is null)
            errors[nameof(request.KindDefaults)] = ["kindDefaults is required."];
        else
        {
            var seen = new HashSet<AgentKind>();
            foreach (var kind in request.KindDefaults)
            {
                if (!Enum.IsDefined(kind.AgentKind) || !SupportedKinds.Contains(kind.AgentKind))
                    errors[nameof(request.KindDefaults)] = ["Kind defaults must use Grok, ClaudeCode or Codex."];
                if (!seen.Add(kind.AgentKind))
                    errors[nameof(request.KindDefaults)] = ["Each agent kind may appear once."];
            }
        }

        if (errors.Count > 0)
            throw new ValidationException(errors);
    }

    private void RejectNewUnknown(
        RunnerRoutingSettings row,
        string? global,
        List<(AgentKind AgentKind, string RunnerId)> kinds)
    {
        var known = KnownIds();
        var previous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (row.GlobalRunnerId is { } oldGlobal)
            previous.Add(oldGlobal);
        foreach (var old in row.KindDefaults)
            previous.Add(old.RunnerId);
        RequireKnown(known, previous, global);
        foreach (var kind in kinds)
            RequireKnown(known, previous, kind.RunnerId);
    }

    private HashSet<string> KnownIds()
    {
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RunnerPlatformWire.DesktopId,
            PhoneHomeProtocol.LocalRunnerId,
        };
        if (_runners is not null)
        {
            foreach (var id in _runners.KnownRunnerIds)
                known.Add(id);
        }

        return known;
    }

    private static void RequireKnown(HashSet<string> known, HashSet<string> previous, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || known.Contains(id) || previous.Contains(id))
            return;
        throw new ValidationException(
            "globalRunnerId",
            $"Runner '{id}' is not in the catalogue.",
            "runner_unknown");
    }

    private static void NoteUnresolved(List<string> unresolved, HashSet<string> known, string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || RunnerRequestIntent.IsDesktopAlias(id) || known.Contains(id))
            return;
        if (!unresolved.Contains(id, StringComparer.OrdinalIgnoreCase))
            unresolved.Add(id);
    }

    private static string? CanonicalWrite(string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId))
            return null;
        var trimmed = runnerId.Trim();
        if (RunnerRequestIntent.DescribeInvalid(trimmed) is not null && !RunnerRequestIntent.IsDesktopAlias(trimmed))
            throw new ValidationException("globalRunnerId", $"runner id {trimmed} is invalid.", "runner_id_invalid");
        if (RunnerRequestIntent.IsDesktopAlias(trimmed))
            return RunnerPlatformWire.DesktopId;
        return trimmed;
    }

    private static bool IsDefaultsConflict(DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException)
            return true;
        for (var current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg
                && pg.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure)
                return true;
        }

        return false;
    }

    private static bool Same(RunnerRoutingSettings row, string? global, List<(AgentKind AgentKind, string RunnerId)> kinds)
    {
        if (!string.Equals(row.GlobalRunnerId, global, StringComparison.Ordinal))
            return false;
        var current = row.KindDefaults.OrderBy(k => k.AgentKind).Select(k => (k.AgentKind, k.RunnerId)).ToList();
        return current.SequenceEqual(kinds);
    }

    private static RunnerDefaultsRevisionDto ToRevision(RunnerRoutingRevision row)
    {
        var body = JsonSerializer.Deserialize<SnapshotBody>(row.SnapshotJson, Json) ?? new SnapshotBody(null, []);
        var kinds = body.Kinds.Select(k => new RunnerKindDefaultDto(
            Enum.Parse<AgentKind>(k.AgentKind), k.RunnerId, body.GlobalRunnerId, "KindDefault")).ToList();
        return new RunnerDefaultsRevisionDto(
            row.Revision, row.PreviousRevision, body.GlobalRunnerId, kinds,
            row.CreatedAt, row.Reason, row.Provenance, row.CallerTaskId);
    }

    private sealed record SnapshotBody(string? GlobalRunnerId, IReadOnlyList<KindBody> Kinds);
    private sealed record KindBody(string AgentKind, string RunnerId);
}
