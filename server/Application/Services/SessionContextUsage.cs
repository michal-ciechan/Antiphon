using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The rows <see cref="SessionContextUsage.Compute"/> needs — a projection of
/// <c>TranscriptEntry</c>, not the entity, so the calculation stays a pure function.
/// </summary>
public sealed record TranscriptContextRow(
    long Sequence,
    string Kind,
    string? Text,
    int? InputTokens,
    int? OutputTokens,
    int? CacheReadTokens,
    int? CacheCreationTokens,
    string? Model,
    bool? IsApiError = null,
    int? ModelCalls = null,
    DateTime? Timestamp = null);

/// <summary>Result of <see cref="SessionContextUsage.Compute"/>.</summary>
/// <param name="Fullness">
/// Tokens used / ceiling. Null when unknown: no usage-bearing row, or a CompactBoundary / /clear
/// landed after the newest usage-bearing row.
/// </param>
/// <param name="TokensUsed">Sum of the winning usage row, even when <paramref name="Fullness"/> is unknown.</param>
/// <param name="CeilingTokens">Resolved ceiling (override or default).</param>
/// <param name="ModelId">Model used for the ceiling (row, then fallback, then null).</param>
public sealed record SessionContextUsageResult(
    double? Fullness,
    int? TokensUsed,
    int CeilingTokens,
    string? ModelId);

/// <summary>
/// Live context fullness from the newest usage-bearing transcript row (CARD-0082). Pure
/// computation — no cache, no persisted column. The session DTO recomputes this on read.
/// </summary>
public static class SessionContextUsage
{
    /// <summary>
    /// Claude's own auto-compact fires around ~92%. A computed fullness below this on an
    /// <c>(auto)</c> CompactBoundary means our configured ceiling is too big.
    /// </summary>
    public const double AutoCompactHeadroomThreshold = 0.80;

    /// <summary>Local-command name that wipes the conversation (invalidates stored usage).</summary>
    public const string ClearCommandName = "/clear";

    public static SessionContextUsageResult Compute(
        IReadOnlyList<TranscriptContextRow> rows,
        string? fallbackModelId,
        ContextWindowSettings settings,
        ILogger? logger = null,
        ContextWindowUsageContract? contract = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var usage = rows
            .Where(IsUsageBearing)
            .OrderByDescending(r => r.Sequence)
            .FirstOrDefault();

        if (usage is null)
        {
            var emptyCeiling = settings.ResolveCeiling(fallbackModelId);
            return new SessionContextUsageResult(null, null, emptyCeiling, fallbackModelId);
        }

        var tokens = TokensOf(usage);
        var modelId = FirstNonEmpty(usage.Model, fallbackModelId);
        var ceiling = settings.ResolveCeiling(modelId);

        if (contract is { State: AgentTuiCapabilityState.Degraded,
            CeilingSource: ContextWindowCeilingSource.SelfReported })
        {
            logger?.LogDebug(
                "Context fullness suppressed for a {State}/{Ceiling} contract: {Reason}",
                contract.State, contract.CeilingSource, contract.Reason);
            return new SessionContextUsageResult(null, ToInt(tokens), ceiling, modelId);
        }

        var fullness = ceiling > 0 ? tokens / (double)ceiling : (double?)null;

        if (fullness is > 1.0)
        {
            logger?.LogWarning(
                "Context fullness {Fullness:P1} exceeds 100% for model '{Model}' with ceiling {Ceiling} tokens",
                fullness.Value, modelId ?? "(none)", ceiling);
        }

        var later = rows.Where(r => r.Sequence > usage.Sequence).ToList();
        var laterAutoBoundary = later.FirstOrDefault(IsAutoCompactBoundary);
        if (laterAutoBoundary is not null && fullness is < AutoCompactHeadroomThreshold)
        {
            logger?.LogWarning(
                "An (auto) CompactBoundary was recorded on a session computed at {Fullness:P1} fullness (model '{Model}', ceiling {Ceiling} tokens); the configured ceiling may be too high",
                fullness.Value, modelId ?? "(none)", ceiling);
        }

        if (later.Any(IsInvalidator))
            return new SessionContextUsageResult(null, ToInt(tokens), ceiling, modelId);

        return new SessionContextUsageResult(fullness, ToInt(tokens), ceiling, modelId);
    }

    /// <summary>
    /// One indexed-friendly query per batch of sessions: usage-bearing rows plus the CompactBoundary
    /// / local-command wrappers that can invalidate them. Returns session id → fullness (null = unknown).
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, double?>> LoadFullnessAsync(
        AppDbContext db,
        IReadOnlyCollection<(Guid SessionId, string? EffectiveModelId, AgentKind Kind)> sessions,
        ContextWindowSettings settings,
        ILogger? logger,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(settings);

        if (sessions.Count == 0)
            return new Dictionary<Guid, double?>();

        var ids = sessions.Select(s => s.SessionId).Distinct().ToList();
        var fallbacks = sessions
            .GroupBy(s => s.SessionId)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => ids.Contains(t.AgentSessionId) && (
                t.InputTokens != null
                || t.Kind == TranscriptKinds.CompactBoundary
                || (t.Kind == TranscriptKinds.UserPrompt
                    && t.Text != null
                    && t.Text.Contains(TranscriptKinds.LocalCommandPrefix))))
            .Select(t => new
            {
                t.AgentSessionId,
                t.Sequence,
                t.Kind,
                t.Text,
                t.InputTokens,
                t.OutputTokens,
                t.CacheReadTokens,
                t.CacheCreationTokens,
                t.Model,
                t.IsApiError,
                t.ModelCalls,
                t.Timestamp,
            })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, double?>(ids.Count);
        foreach (var id in ids)
        {
            var sessionRows = rows
                .Where(r => r.AgentSessionId == id)
                .Select(r => new TranscriptContextRow(
                    r.Sequence, r.Kind, r.Text,
                    r.InputTokens, r.OutputTokens, r.CacheReadTokens, r.CacheCreationTokens,
                    r.Model, r.IsApiError, r.ModelCalls, r.Timestamp))
                .ToList();
            var meta = fallbacks.GetValueOrDefault(id);
            var contract = meta == default
                ? null
                : ProviderContractCatalog.For(meta.Kind).ContextWindowUsage;
            result[id] = Compute(
                sessionRows, meta.EffectiveModelId, settings, logger, contract).Fullness;
        }

        return result;
    }

    public static async Task<IReadOnlyList<AgentSessionSummaryDto>> AttachAsync(
        AppDbContext db,
        IReadOnlyList<AgentSessionSummaryDto> sessions,
        ContextWindowSettings settings,
        ILogger? logger,
        CancellationToken ct)
    {
        if (sessions.Count == 0)
            return sessions;

        var fullness = await LoadFullnessAsync(
            db,
            sessions.Select(s => (s.Id, s.EffectiveModelId, s.AgentKind)).ToList(),
            settings,
            logger,
            ct);

        return sessions
            .Select(s => s with { ContextFullness = fullness.GetValueOrDefault(s.Id) })
            .ToList();
    }

    public static async Task<CardDto> AttachToCardAsync(
        AppDbContext db,
        CardDto card,
        ContextWindowSettings settings,
        ILogger? logger,
        CancellationToken ct)
    {
        var sessions = await AttachAsync(db, card.Sessions, settings, logger, ct);
        return card with { Sessions = sessions };
    }

    public static async Task<BoardDetailDto> AttachToBoardAsync(
        AppDbContext db,
        BoardDetailDto board,
        ContextWindowSettings settings,
        ILogger? logger,
        CancellationToken ct)
    {
        var all = board.Columns.SelectMany(c => c.Cards).SelectMany(card => card.Sessions).ToList();
        if (all.Count == 0)
            return board;

        var fullness = await LoadFullnessAsync(
            db,
            all.Select(s => (s.Id, s.EffectiveModelId, s.AgentKind)).Distinct().ToList(),
            settings,
            logger,
            ct);

        return board with
        {
            Columns = board.Columns.Select(col => col with
            {
                Cards = col.Cards.Select(card => card with
                {
                    Sessions = card.Sessions
                        .Select(s => s with { ContextFullness = fullness.GetValueOrDefault(s.Id) })
                        .ToList(),
                }).ToList(),
            }).ToList(),
        };
    }

    private static bool IsUsageBearing(TranscriptContextRow row) =>
        row.InputTokens is not null
        && !TranscriptKinds.IsApiErrorStub(row.Kind, row.IsApiError);

    private static bool IsInvalidator(TranscriptContextRow row) =>
        row.Kind == TranscriptKinds.CompactBoundary
        || string.Equals(
            TranscriptKinds.TryReadLocalCommandName(row.Kind, row.Text),
            ClearCommandName,
            StringComparison.Ordinal);

    private static bool IsAutoCompactBoundary(TranscriptContextRow row) =>
        row.Kind == TranscriptKinds.CompactBoundary
        && row.Text is not null
        && row.Text.Contains("(auto)", StringComparison.Ordinal);

    private static long TokensOf(TranscriptContextRow row) =>
        (long)(row.InputTokens ?? 0)
        + (row.CacheReadTokens ?? 0)
        + (row.CacheCreationTokens ?? 0)
        + (row.OutputTokens ?? 0);

    private static int? ToInt(long tokens) =>
        tokens > int.MaxValue ? int.MaxValue : (int)tokens;

    private static string? FirstNonEmpty(string? preferred, string? fallback) =>
        !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback;
}
