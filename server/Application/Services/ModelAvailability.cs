using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Reader, AutoDetected writer (CARD-0022 / CARD-0335), and Manual writer (CARD-0309) for
/// <see cref="ModelAvailabilityHold"/>. Held if an active row exists for
/// <c>(kind, alias)</c> OR <c>(kind, *)</c> AND
/// (<see cref="ModelAvailabilityHold.DisabledUntil"/> is null OR <c>now &lt; DisabledUntil</c>).
/// AutoDetected holds are always timed; a legacy AutoDetected null is materialized to
/// <c>HitAt + ModelCapFallbackHoldHours</c> on sweep or lazy read. Manual null stays
/// open-ended. Auto-resume is by construction: a sweep (and lazy check on read) sets
/// <see cref="ModelAvailabilityHold.ClearedAt"/> when <c>DisabledUntil &lt;= now</c>.
///
/// <para>Outrank: one active row per key. A Manual row outranks AutoDetected — AutoDetected may
/// refresh evidence (<c>RawText</c>/<c>HitAt</c>/<c>SourceSessionId</c>/<c>SourceTaskId</c>/<c>Reason</c>)
/// but must not shorten <c>DisabledUntil</c> or demote <c>Source</c> back to AutoDetected.
/// Manual PUT converts an AutoDetected row in place.</para>
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
    private readonly TimeSpan _modelCapFallbackHold;

    public ModelAvailability(
        AppDbContext db,
        TimeProvider time,
        ILogger<ModelAvailability> logger,
        IOptions<SupervisionSettings>? settings = null)
    {
        _db = db;
        _time = time;
        _logger = logger;
        var hours = settings is null
            ? 6
            : settings.Value.ApiErrorRecovery.EffectiveModelCapFallbackHoldHours;
        _modelCapFallbackHold = TimeSpan.FromHours(hours);
    }

    public async Task<bool> IsHeldAsync(AgentKind kind, string alias, CancellationToken ct)
    {
        var now = UtcNow();
        var hold = await FindActiveAsync(kind, alias, now, ct);
        return hold is not null;
    }

    /// <summary>
    /// Create/start door. Throws <see cref="ModelDisabledException"/> when the resolved alias
    /// (or the kind-wide <c>*</c>) is held. Create may skip this when
    /// <c>ignoreModelDisabled</c> is set (queue, do not spawn). Start never skips. Never
    /// silently reroute.
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
    /// Also materializes a legacy AutoDetected/null row to <c>HitAt + fallback</c> and clears it
    /// in the same save when that timestamp has elapsed. No Hangfire job.
    /// </summary>
    public Task<int> SweepExpiredAsync(CancellationToken ct) => SweepExpiredAsync(UtcNow(), ct);

    public async Task<int> SweepExpiredAsync(DateTime now, CancellationToken ct)
    {
        var rows = await _db.ModelAvailabilityHolds
            .Where(h => h.ClearedAt == null && (
                (h.DisabledUntil != null && h.DisabledUntil <= now)
                || (h.DisabledUntil == null && h.Source == ModelAvailabilitySource.AutoDetected)))
            .ToListAsync(ct);
        if (rows.Count == 0)
            return 0;

        var cleared = 0;
        foreach (var row in rows)
        {
            NormalizeLegacyAutoDetected(row, now, out var expired);
            if (expired)
                cleared++;
        }

        await _db.SaveChangesAsync(ct);
        return cleared;
    }

    /// <summary>
    /// CARD-0022 / CARD-0335 AutoDetected writer. Upserts the active row for <c>(kind, alias)</c>
    /// with a required <paramref name="disabledUntil"/>. If an active Manual hold exists, evidence
    /// fields refresh and <c>DisabledUntil</c> / <c>Source</c> stay put (CARD-0309 outrank). Never
    /// writes alias <c>*</c>. Never keys on a stub <c>&lt;synthetic&gt;</c> model id — the caller
    /// must pass a canonical alias.
    /// </summary>
    public async Task<ModelAvailabilityHold> UpsertAutoDetectedAsync(
        AgentKind kind,
        string alias,
        DateTime disabledUntil,
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
                kind, canonical, disabledUntil, row.Reason);
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

    /// <summary>
    /// The live hold that <see cref="IsHeldAsync"/> / <see cref="RequireAsync"/> consult, or
    /// null. Used by create-with-<c>ignoreModelDisabled</c> to write the queue-until-clear
    /// warning without throwing.
    /// </summary>
    public Task<ModelAvailabilityHold?> GetActiveHoldAsync(
        AgentKind kind, string alias, CancellationToken ct) =>
        FindActiveAsync(kind, alias, UtcNow(), ct);

    /// <summary>
    /// CARD-0309 Manual writer. Upserts the active row for <c>(kind, alias)</c> with
    /// <see cref="ModelAvailabilitySource.Manual"/>. Converts an AutoDetected row in place
    /// (same <c>Id</c>). Kind-wide alias is <c>*</c>. A past <paramref name="disabledUntil"/>
    /// is 422. Unknown / non-delegatable kind or unknown alias is 422.
    /// </summary>
    public async Task<ModelAvailabilityHoldDto> UpsertManualAsync(
        string kind,
        string alias,
        DateTimeOffset? disabledUntil,
        string? reason,
        CancellationToken ct)
    {
        var parsedKind = ParseHoldKind(kind);
        var canonical = ParseHoldAlias(alias);
        var now = UtcNow();
        DateTime? untilUtc = null;
        if (disabledUntil is { } until)
        {
            untilUtc = until.UtcDateTime;
            if (untilUtc <= now)
            {
                throw new ValidationException(
                    "disabledUntil",
                    $"disabledUntil {untilUtc:yyyy-MM-ddTHH:mm:ssZ} is in the past; use a future UTC instant or omit it for an open-ended hold.");
            }
        }

        var existing = await _db.ModelAvailabilityHolds
            .FirstOrDefaultAsync(
                h => h.Kind == parsedKind && h.ModelAlias == canonical && h.ClearedAt == null, ct);

        var cappedReason = Cap(string.IsNullOrWhiteSpace(reason) ? "manual hold" : reason, ReasonCap)
            ?? "manual hold";

        if (existing is null)
        {
            var row = new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = parsedKind,
                ModelAlias = canonical,
                Source = ModelAvailabilitySource.Manual,
                DisabledUntil = untilUtc,
                HitAt = now,
                RawText = null,
                SourceSessionId = null,
                SourceTaskId = null,
                Reason = cappedReason,
            };
            _db.ModelAvailabilityHolds.Add(row);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Manual hold {Kind}/{Alias} until {Until} ({Reason})",
                parsedKind, canonical, (object?)untilUtc ?? "(until cleared)", row.Reason);
            return ToDto(row);
        }

        // Convert AutoDetected in place: one active row per key. Operator until wins, including null.
        existing.Source = ModelAvailabilitySource.Manual;
        existing.DisabledUntil = untilUtc;
        existing.HitAt = now;
        existing.RawText = null;
        existing.SourceSessionId = null;
        existing.SourceTaskId = null;
        existing.Reason = cappedReason;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Manual hold {Kind}/{Alias} until {Until} ({Reason}) — converted in place",
            parsedKind, canonical, (object?)untilUtc ?? "(until cleared)", existing.Reason);
        return ToDto(existing);
    }

    /// <summary>
    /// CARD-0309: set <c>ClearedAt = now</c> on the active row (any Source). Idempotent — already
    /// clear is a no-op. Does not delete the row.
    /// </summary>
    public async Task ClearAsync(string kind, string alias, CancellationToken ct)
    {
        var parsedKind = ParseHoldKind(kind);
        var canonical = ParseHoldAlias(alias);
        var existing = await _db.ModelAvailabilityHolds
            .FirstOrDefaultAsync(
                h => h.Kind == parsedKind && h.ModelAlias == canonical && h.ClearedAt == null, ct);
        if (existing is null)
            return;

        existing.ClearedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Cleared hold {Kind}/{Alias}", parsedKind, canonical);
    }

    private static AgentKind ParseHoldKind(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind)
            || !Enum.TryParse<AgentKind>(kind, ignoreCase: true, out var parsed)
            || !Enum.GetNames<AgentKind>().Any(n => n.Equals(kind.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            throw new ValidationException("kind", $"Unknown kind '{kind}'.");
        }

        if (!AgentTaskService.DelegatableKinds.Contains(parsed))
        {
            throw new ValidationException(
                "kind",
                $"{parsed} is not a delegatable kind. Holds apply to {string.Join(", ", AgentTaskService.DelegatableKinds)}.");
        }

        return parsed;
    }

    private static string ParseHoldAlias(string alias)
    {
        var canonical = ModelAlias.CanonicalHoldAlias(alias);
        if (canonical is null)
        {
            var known = string.Join(", ", ModelAlias.DelegatableAliases.Select(a => a.Alias).Distinct());
            throw new ValidationException(
                "alias",
                $"Unknown alias '{alias}'. Use a ModelLevelAliases value ({known}) or '*'.");
        }

        return canonical;
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
            if (NormalizeLegacyAutoDetected(row, now, out var expired))
                dirty = true;
            if (expired)
                continue;

            live ??= row;
            // A specific alias hold and a kind-wide hold can both be active; either is enough.
            if (row.ModelAlias == canonical)
                live = row;
        }

        if (dirty)
            await _db.SaveChangesAsync(ct);
        return live;
    }

    /// <summary>
    /// Materialize a legacy AutoDetected/null row to <c>HitAt + fallback</c> and clear it when
    /// that timestamp has elapsed. A Manual null is left untouched. Returns whether the row was
    /// mutated.
    /// </summary>
    private bool NormalizeLegacyAutoDetected(ModelAvailabilityHold row, DateTime now, out bool expired)
    {
        var mutated = false;
        if (row.DisabledUntil is null && row.Source == ModelAvailabilitySource.AutoDetected)
        {
            row.DisabledUntil = row.HitAt + _modelCapFallbackHold;
            mutated = true;
        }

        expired = row.DisabledUntil is { } until && until <= now;
        if (expired)
        {
            row.ClearedAt = now;
            mutated = true;
        }

        return mutated;
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
