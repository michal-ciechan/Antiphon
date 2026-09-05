using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Loads a complexity chain and walks a candidate list (CARD-0090). Built as two pieces from
/// day one so CARD-0322 can feed a pin's list into the same walker:
/// <see cref="RoutingCandidates.Compose"/> (pure) and <see cref="WalkCandidatesAsync"/> (the
/// four filters, no knowledge of where the list came from). <see cref="WalkAsync"/> is those
/// two composed together.
/// </summary>
public sealed class ComplexityRoutingService
{
    public const string RoutingExhaustedPrefix = "routing exhausted: ";
    public const int MaxCandidates = 8;

    /// <summary>
    /// Loop-guard Block: every candidate has already been tried this cascade. Still starts with
    /// <see cref="RoutingExhaustedPrefix"/> so attention / BlockedQuestion carve-out / human
    /// reroute keep working; <see cref="AgentTaskDispatcher"/> must not auto-resume these.
    /// </summary>
    public static bool CascadeTriedEveryCandidate(int reroutedCount, int candidateCount) =>
        candidateCount > 0 && reroutedCount >= candidateCount;

    public static string CascadeExhaustedSentence(string cellLabel, int reroutedCount, int candidateCount) =>
        RoutingExhaustedPrefix
        + $"{cellLabel} chain already rerouted {reroutedCount}/{candidateCount} times; "
        + "each candidate at most once this cascade. A human decides: reroute or cancel; "
        + "do not pick a kind yourself.";

    /// <summary>
    /// Roles that may own a chain cell. Check/Distill/Diagnose are seat-pinned and refused.
    /// Order is the grid-row order CARD-0333 renders.
    /// </summary>
    public static readonly AgentTaskRole[] RoutableRoles =
    [
        AgentTaskRole.Investigate,
        AgentTaskRole.Plan,
        AgentTaskRole.TestDesign,
        AgentTaskRole.Code,
        AgentTaskRole.Review,
        AgentTaskRole.Debug,
        AgentTaskRole.Coverage,
        AgentTaskRole.Docs,
        AgentTaskRole.Commit,
        AgentTaskRole.Test,
        AgentTaskRole.Deploy,
        AgentTaskRole.Merge,
        AgentTaskRole.Custom,
    ];

    public static readonly TaskComplexity[] Tiers =
    [
        TaskComplexity.Hard,
        TaskComplexity.Medium,
        TaskComplexity.Easy,
    ];

    private readonly AppDbContext _db;
    private readonly DelegationSettings _settings;
    private readonly TimeProvider _time;
    private readonly ModelAvailability? _availability;
    private readonly SubscriptionQuotaGate? _quotaGate;

    public ComplexityRoutingService(
        AppDbContext db,
        IOptions<DelegationSettings> settings,
        TimeProvider time,
        ModelAvailability? availability = null,
        SubscriptionQuotaGate? quotaGate = null)
    {
        _db = db;
        _settings = settings.Value;
        _time = time;
        _availability = availability;
        _quotaGate = quotaGate;
    }

    public sealed record CandidateOutcome(
        RoutingCandidates.Candidate Candidate,
        string Outcome,
        string? Reason);

    public sealed record Walk(
        TaskComplexity Complexity,
        RoutingPinProvenance? ChainProvenance,
        string ChainSource,
        string Source,
        IReadOnlyList<CandidateOutcome> Outcomes,
        RoutingCandidates.Candidate? Chosen,
        IReadOnlyList<string> Available,
        bool Walked,
        AgentTaskRole Role = default,
        AgentTaskRole? ChainRole = null)
    {
        /// <summary><c>Plan/Hard</c> when a role cell answered; <c>Hard</c> for any-role or config.</summary>
        public string CellLabel => ChainRole is { } r ? $"{r}/{Complexity}" : $"{Complexity}";

        public ComplexityRoutingDto ToDto() => new(
            Complexity,
            ChainProvenance,
            ChainSource,
            Source,
            Outcomes.Select(o => new ComplexityCandidateOutcomeDto(
                o.Candidate.Kind,
                o.Candidate.Level,
                o.Candidate.Alias,
                o.Outcome,
                o.Reason,
                o.Candidate.Origin)).ToList(),
            Available,
            Walked,
            Role,
            ChainRole);

        public string ExhaustedSentence()
        {
            if (Outcomes.Count == 0)
            {
                return RoutingExhaustedPrefix
                    + $"{Role}/{Complexity} chain is empty (no {Role}/{Complexity} row, no any-role {Complexity} row, no config default). "
                    + $"Set one with complexity-chain.ps1 set -Role {Role} -Complexity {Complexity}, or set -Complexity {Complexity} for every role.";
            }

            var bits = Outcomes.Select(o =>
            {
                var why = string.IsNullOrWhiteSpace(o.Reason) ? "skipped" : o.Reason;
                return $"{o.Candidate.Alias} {why}";
            });
            return RoutingExhaustedPrefix
                + $"{CellLabel} chain — {string.Join("; ", bits)}";
        }

        public string SkippedWarning()
        {
            var skipped = Outcomes.Where(o => o.Outcome == "skipped").ToList();
            if (skipped.Count == 0)
                return string.Empty;
            return "skipped " + string.Join(
                ", ",
                skipped.Select(o => $"{o.Candidate.Alias} ({o.Reason})"));
        }
    }

    public sealed record WalkContext(
        AgentTaskKind TaskKind,
        AgentTaskRole Role,
        RoutingPin? StagePin,
        bool StageForbidExempt,
        Agent? SubscriptionOwner,
        bool IgnoreSubscriptionQuota,
        string Source);

    /// <summary>
    /// Load the chain for <paramref name="complexity"/> (active row, else config default),
    /// compose with the pin, then walk. Required-pin-wins / Preferred-pin-prepends live in
    /// <see cref="RoutingCandidates.Compose"/>.
    /// </summary>
    public async Task<Walk> WalkAsync(
        TaskComplexity complexity,
        AgentTaskKind taskKind,
        AgentTaskRole role,
        RoutingPinService.Decision pin,
        Guid? cardId,
        Agent? subscriptionOwner,
        bool ignoreSubscriptionQuota,
        CancellationToken ct)
    {
        var loaded = await LoadChainAsync(role, complexity, ct);
        var chainLabel = loaded.ChainRole is null ? complexity.ToString() : $"{role}/{complexity}";
        var composed = RoutingCandidates.Compose(
            pin,
            loaded.Candidates,
            chainLabel,
            requestKind: null,
            requestLevel: null,
            (k, l) => ResolveAgainstRolePolicy(taskKind, role, k, l));

        var ctx = new WalkContext(
            taskKind,
            role,
            pin.StagePin,
            StageForbidExempt: pin.Applied
                && pin.Pin is { CardId: not null, Provenance: RoutingPinProvenance.Human },
            subscriptionOwner,
            ignoreSubscriptionQuota,
            composed.Source);

        var walked = await WalkCandidatesAsync(composed.Candidates, ctx, ct);
        return walked with
        {
            Complexity = complexity,
            ChainProvenance = loaded.Provenance,
            ChainSource = loaded.ChainSource,
            Walked = composed.Walked,
            Role = role,
            ChainRole = loaded.ChainRole,
        };
    }

    /// <summary>
    /// The four filters of CARD-0090 §Decision 3, in order, with no knowledge of where the
    /// list came from. First survivor is Chosen.
    /// </summary>
    public async Task<Walk> WalkCandidatesAsync(
        IReadOnlyList<RoutingCandidates.Candidate> candidates,
        WalkContext ctx,
        CancellationToken ct)
    {
        var outcomes = new List<CandidateOutcome>(candidates.Count);
        RoutingCandidates.Candidate? chosen = null;
        foreach (var candidate in candidates)
        {
            var reason = await FirstSkipReasonAsync(candidate, ctx, ct);
            if (reason is null && chosen is null)
            {
                chosen = candidate;
                outcomes.Add(new CandidateOutcome(candidate, "chosen", null));
            }
            else
            {
                outcomes.Add(new CandidateOutcome(
                    candidate,
                    "skipped",
                    reason ?? "already chose an earlier candidate"));
            }
        }

        IReadOnlyList<string> available = _availability is not null
            ? await _availability.ListAvailableAsync(ct)
            : [];

        return new Walk(
            TaskComplexity.Hard,
            ChainProvenance: null,
            ChainSource: "config",
            ctx.Source,
            outcomes,
            chosen,
            available,
            Walked: candidates.Count >= 2);
    }

    public async Task<(
        IReadOnlyList<RoutingCandidates.Candidate> Candidates,
        RoutingPinProvenance? Provenance,
        string ChainSource,
        AgentTaskRole? ChainRole,
        string ResolvedFrom)> LoadChainAsync(
        AgentTaskRole role,
        TaskComplexity complexity,
        CancellationToken ct)
    {
        var cell = await FindActiveAsync(role, complexity, ct);
        if (cell is not null)
            return (FromRow(cell), cell.Provenance, "pin", role, "role");

        var any = await FindActiveAsync(role: null, complexity, ct);
        if (any is not null)
            return (FromRow(any), any.Provenance, "pin", null, "any");

        if (_settings.ComplexityChains.TryGetValue(complexity.ToString(), out var config)
            && config.Count > 0)
        {
            var fromConfig = config
                .Select(p => new RoutingCandidates.Candidate(
                    p.Kind,
                    p.Level,
                    ModelLevelAliases.For(p.Kind, p.Level),
                    RoutingCandidates.OriginChain))
                .ToList();
            return (fromConfig, RoutingPinProvenance.Auto, "config", null, "config");
        }

        return ([], null, "config", null, "none");
    }

    /// <summary>Any-role row for <paramref name="complexity"/> (CARD-0090 signature).</summary>
    public Task<ComplexityChain?> FindActiveAsync(TaskComplexity complexity, CancellationToken ct) =>
        FindActiveAsync(role: null, complexity, ct);

    public async Task<ComplexityChain?> FindActiveAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        CancellationToken ct)
    {
        var rows = await _db.ComplexityChains
            .Where(c => c.ClearedAt == null && c.Complexity == complexity && c.Role == role)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return null;

        var now = UtcNow();
        ComplexityChain? live = null;
        var dirty = false;
        foreach (var row in rows)
        {
            if (row.NotAfter is { } notAfter && notAfter <= now)
            {
                row.ClearedAt = now;
                dirty = true;
                continue;
            }

            live ??= row;
        }

        if (dirty)
            await _db.SaveChangesAsync(ct);
        return live;
    }

    public async Task<IReadOnlyList<ComplexityChain>> ListLiveCellsAsync(CancellationToken ct)
    {
        var rows = await _db.ComplexityChains
            .Where(c => c.ClearedAt == null && c.Role != null)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var now = UtcNow();
        var live = new List<ComplexityChain>(rows.Count);
        var dirty = false;
        foreach (var row in rows)
        {
            if (row.NotAfter is { } notAfter && notAfter <= now)
            {
                row.ClearedAt = now;
                dirty = true;
                continue;
            }

            live.Add(row);
        }

        if (dirty)
            await _db.SaveChangesAsync(ct);

        var roleOrder = new Dictionary<AgentTaskRole, int>(RoutableRoles.Length);
        for (var i = 0; i < RoutableRoles.Length; i++)
            roleOrder[RoutableRoles[i]] = i;
        var complexityOrder = new Dictionary<TaskComplexity, int>
        {
            [TaskComplexity.Hard] = 0,
            [TaskComplexity.Medium] = 1,
            [TaskComplexity.Easy] = 2,
        };

        return live
            .OrderBy(c => c.Role is { } r && roleOrder.TryGetValue(r, out var ri) ? ri : int.MaxValue)
            .ThenBy(c => complexityOrder.GetValueOrDefault(c.Complexity, int.MaxValue))
            .ToList();
    }

    private static IReadOnlyList<RoutingCandidates.Candidate> FromRow(ComplexityChain row) =>
        row.ParseCandidates()
            .Select(p => new RoutingCandidates.Candidate(
                p.AgentKind,
                p.ModelLevel,
                ModelLevelAliases.For(p.AgentKind, p.ModelLevel),
                RoutingCandidates.OriginChain))
            .ToList();

    public RoutingCandidates.Candidate ResolveAgainstRolePolicy(
        AgentTaskKind taskKind,
        AgentTaskRole role,
        AgentKind? kind,
        AgentModelLevel? level)
    {
        _settings.RolePolicy.TryGetValue(role.ToString(), out var policy);
        var resolvedKind = kind ?? policy?.Kind ?? AgentKind.ClaudeCode;
        var resolvedLevel = level ?? policy?.Level ?? _settings.DefaultLevel;
        return new RoutingCandidates.Candidate(
            resolvedKind,
            resolvedLevel,
            ModelLevelAliases.For(resolvedKind, resolvedLevel),
            RoutingCandidates.OriginRolePolicy);
    }

    /// <summary>
    /// Holds + quota only — the GET panel's "available now", not the per-task kind clamp or
    /// stage forbid.
    /// </summary>
    public async Task<(bool AvailableNow, string? Reason)> EvaluateAvailabilityNowAsync(
        AgentKind kind,
        AgentModelLevel level,
        CancellationToken ct)
    {
        var alias = ModelLevelAliases.For(kind, level);
        if (_availability is not null)
        {
            var hold = await _availability.GetActiveHoldAsync(kind, alias, ct);
            if (hold is not null)
                return (false, FormatHeld(hold));
        }

        if (_quotaGate is not null)
        {
            var verdict = await _quotaGate.EvaluateAsync(
                kind, SubscriptionUsageKey.For(null, kind), ct);
            if (verdict is not null)
                return (false, FormatQuota(verdict));
        }

        return (true, null);
    }

    internal static string FormatHeld(ModelAvailabilityHold hold)
    {
        var source = hold.Source == ModelAvailabilitySource.Manual
            ? "manual"
            : hold.DisabledUntil is null
                ? "per-model cap"
                : "session-limit";
        if (hold.DisabledUntil is { } until)
            return $"held until {until:yyyy-MM-ddTHH:mm:ssZ} ({source})";
        return source == "manual"
            ? "held (manual, no re-enable time)"
            : "held (per-model cap, no reset stated)";
    }

    internal static string FormatQuota(SubscriptionQuotaVerdict verdict) =>
        $"quota {SubscriptionQuotaPolicy.FormatPercent(verdict.RemainingPercent)}% remaining, resets in {SubscriptionQuotaPolicy.FormatTimeToReset(verdict.TimeToReset)}";

    private async Task<string?> FirstSkipReasonAsync(
        RoutingCandidates.Candidate candidate,
        WalkContext ctx,
        CancellationToken ct)
    {
        if (!AgentTaskService.DelegatableKinds.Contains(candidate.Kind)
            || (ctx.TaskKind == AgentTaskKind.Orchestrator && candidate.Kind != AgentKind.ClaudeCode))
        {
            return "not a delegate kind";
        }

        if (!ctx.StageForbidExempt && ctx.StagePin is { } stage)
        {
            var forbidden = RoutingPinService.SplitForbidden(stage.ForbiddenAliases);
            if (forbidden.Contains(candidate.Alias, StringComparer.OrdinalIgnoreCase))
                return "forbidden by stage pin";
        }

        if (_availability is not null)
        {
            var hold = await _availability.GetActiveHoldAsync(candidate.Kind, candidate.Alias, ct);
            if (hold is not null)
                return FormatHeld(hold);
        }

        if (_quotaGate is not null && !ctx.IgnoreSubscriptionQuota)
        {
            var key = SubscriptionUsageKey.For(ctx.SubscriptionOwner, candidate.Kind);
            var verdict = await _quotaGate.EvaluateAsync(candidate.Kind, key, ct);
            if (verdict is not null)
                return FormatQuota(verdict);
        }

        return null;
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
