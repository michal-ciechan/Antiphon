using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Reader (and AutoDetected writer) for <see cref="ModelAvailabilityHold"/> (CARD-0022).
/// Held if an active row exists for <c>(kind, alias)</c> OR <c>(kind, *)</c> AND
/// (<see cref="ModelAvailabilityHold.DisabledUntil"/> is null OR <c>now &lt; DisabledUntil</c>).
/// Auto-resume is by construction: a sweep (and lazy check on read) sets
/// <see cref="ModelAvailabilityHold.ClearedAt"/> when <c>DisabledUntil &lt;= now</c>.
///
/// <para>CARD-0309 outrank (implemented here so a later Manual writer layers on cleanly):
/// one active row per key. A Manual row outranks AutoDetected — AutoDetected may refresh
/// evidence (<c>RawText</c>/<c>HitAt</c>/<c>SourceSessionId</c>/<c>SourceTaskId</c>/<c>Reason</c>)
/// but must not shorten <c>DisabledUntil</c> or demote <c>Source</c> back to AutoDetected.</para>
/// </summary>
public sealed class ModelAvailability
{
    /// <summary>Applied by the recovery writer, not the parser (CARD-0022 S2).</summary>
    public static readonly TimeSpan SessionLimitResumePadding = TimeSpan.FromMinutes(2);

    internal const int RawTextCap = 2000;
    internal const int ReasonCap = 400;

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<ModelAvailability> _logger;

    public ModelAvailability(AppDbContext db, TimeProvider time, ILogger<ModelAvailability> logger)
    {
        _db = db;
        _time = time;
        _logger = logger;
    }

    public async Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct)
    {
        var now = UtcNow();
        var hold = await FindActiveAsync(kind, alias, now, ct);
        return hold is not null;
    }

    /// <summary>
    /// Create/start door. Throws <see cref="ModelDisabledException"/> when the resolved alias
    /// (or the kind-wide <c>*</c>) is held. No override flag on this card — CARD-0309 may add
    /// <c>ignoreModelDisabled</c>. Never silently reroute.
    /// </summary>
    public async Task RequireAsync(AgentKind kind, string alias, CancellationToken ct)
    {
        var now = UtcNow();
        var hold = await FindActiveAsync(kind, alias, now, ct);
        if (hold is null)
            return;

        var available = await ListAvailableAsync(now, ct);
        throw new ModelDisabledException(hold, available);
    }

    public async Task<IReadOnlyList<ModelAvailabilityHold>> ListHeldAsync(CancellationToken ct)
    {
        var now = UtcNow();
        await SweepExpiredAsync(now, ct);
        return await _db.ModelAvailabilityHolds
            .Where(h => h.ClearedAt == null && (h.DisabledUntil == null || h.DisabledUntil > now))
            .OrderBy(h => h.Kind)
            .ThenBy(h => h.ModelAlias)
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<string>> ListAvailableAsync(CancellationToken ct) =>
        ListAvailableAsync(UtcNow(), ct);

    public async Task<ModelAvailabilityDto> GetSnapshotAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var held = await ListHeldAsync(ct);
        var available = await ListAvailableAsync(now, ct);
        return new ModelAvailabilityDto(
            held.Select(ToDto).ToList(),
            available);
    }

    /// <summary>
    /// Minute-pass + lazy-read clearer. Sets <c>ClearedAt = now</c> when a timed hold has elapsed.
    /// No Hangfire job.
    /// </summary>
    public Task<int> SweepExpiredAsync(CancellationToken ct) => SweepExpiredAsync(UtcNow(), ct);

    public async Task<int> SweepExpiredAsync(DateTime now, CancellationToken ct)
    {
        var expired = await _db.ModelAvailabilityHolds
            .Where(h => h.ClearedAt == null && h.DisabledUntil != null && h.DisabledUntil <= now)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return 0;

        foreach (var row in expired)
            row.ClearedAt = now;
        await _db.SaveChangesAsync(ct);
        return expired.Count;
    }

    /// <summary>
    /// CARD-0022 AutoDetected writer. Upserts the active row for <c>(kind, alias)</c>.
    /// If an active Manual hold exists, evidence fields refresh and <c>DisabledUntil</c> /
    /// <c>Source</c> stay put (CARD-0309 outrank). Never writes alias <c>*</c>.
    /// Never keys on a stub <c>&lt;synthetic&gt;</c> model id — the caller must pass a
    /// canonical alias.
    /// </summary>
    public async Task<ModelAvailabilityHold> UpsertAutoDetectedAsync(
        AgentKind kind,
        string alias,
        DateTime? disabledUntil,
        string reason,
        string? rawText,
        Guid? sourceSessionId,
        Guid? sourceTaskId,
        CancellationToken ct)
    {
        if (alias == ModelAlias.KindWide)
            throw new InvalidOperationException("AutoDetected never writes a kind-wide '*' hold.");
        if (string.Equals(alias, "<synthetic>", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The stub model '<synthetic>' is never a hold key.");

        var now = UtcNow();
        var canonical = alias.Trim().ToLowerInvariant();
        var existing = await _db.ModelAvailabilityHolds
            .FirstOrDefaultAsync(
                h => h.Kind == kind && h.ModelAlias == canonical && h.ClearedAt == null, ct);

        if (existing is null)
        {
            var row = new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                ModelAlias = canonical,
                Source = ModelAvailabilitySource.AutoDetected,
                DisabledUntil = disabledUntil,
                HitAt = now,
                RawText = Cap(rawText, RawTextCap),
                SourceSessionId = sourceSessionId,
                SourceTaskId = sourceTaskId,
                Reason = Cap(reason, ReasonCap) ?? reason,
            };
            _db.ModelAvailabilityHolds.Add(row);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Paused {Kind}/{Alias} until {Until} ({Reason})",
                kind, canonical, (object?)disabledUntil ?? "(cleared manually)", row.Reason);
            return row;
        }

        existing.HitAt = now;
        existing.RawText = Cap(rawText, RawTextCap);
        existing.SourceSessionId = sourceSessionId;
        existing.SourceTaskId = sourceTaskId;
        existing.Reason = Cap(reason, ReasonCap) ?? existing.Reason;

        // CARD-0309 outrank: Manual keeps its until and its source. AutoDetected may not shorten
        // a human DisabledUntil, including an open-ended null.
        if (existing.Source != ModelAvailabilitySource.Manual)
        {
            existing.DisabledUntil = disabledUntil;
            existing.Source = ModelAvailabilitySource.AutoDetected;
        }

        await _db.SaveChangesAsync(ct);
        return existing;
    }

    private async Task<ModelAvailabilityHold?> FindActiveAsync(
        AgentKind kind, string alias, DateTime now, CancellationToken ct)
    {
        var canonical = string.IsNullOrWhiteSpace(alias)
            ? alias
            : alias.Trim().ToLowerInvariant();
        // Kind-wide '*' is stored as '*' and ORs with a per-alias hold (CARD-0309).
        var rows = await _db.ModelAvailabilityHolds
            .Where(h => h.Kind == kind
                && h.ClearedAt == null
                && (h.ModelAlias == canonical || h.ModelAlias == ModelAlias.KindWide))
            .ToListAsync(ct);

        ModelAvailabilityHold? live = null;
        var dirty = false;
        foreach (var row in rows)
        {
            if (row.DisabledUntil is { } until && until <= now)
            {
                row.ClearedAt = now;
                dirty = true;
                continue;
            }

            live ??= row;
            // A specific alias hold and a kind-wide hold can both be active; either is enough.
            if (row.ModelAlias == canonical)
                live = row;
        }

        if (dirty)
            await _db.SaveChangesAsync(ct);
        return live;
    }

    private async Task<IReadOnlyList<string>> ListAvailableAsync(DateTime now, CancellationToken ct)
    {
        await SweepExpiredAsync(now, ct);
        var held = await _db.ModelAvailabilityHolds.AsNoTracking()
            .Where(h => h.ClearedAt == null && (h.DisabledUntil == null || h.DisabledUntil > now))
            .Select(h => new { h.Kind, h.ModelAlias })
            .ToListAsync(ct);

        var kindWide = held
            .Where(h => h.ModelAlias == ModelAlias.KindWide)
            .Select(h => h.Kind)
            .ToHashSet();
        var heldKeys = held
            .Select(h => (h.Kind, Alias: h.ModelAlias))
            .ToHashSet();

        var available = new List<string>();
        foreach (var (kind, alias) in ModelAlias.DelegatableAliases)
        {
            if (kindWide.Contains(kind))
                continue;
            if (heldKeys.Contains((kind, alias)))
                continue;
            available.Add(alias);
        }

        return available;
    }

    private static ModelAvailabilityHoldDto ToDto(ModelAvailabilityHold hold) => new(
        hold.Id,
        hold.Kind.ToString(),
        hold.ModelAlias,
        hold.Source,
        hold.DisabledUntil,
        hold.HitAt,
        hold.Reason,
        hold.RawText,
        hold.SourceSessionId,
        hold.SourceTaskId);

    private static string? Cap(string? value, int max)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
