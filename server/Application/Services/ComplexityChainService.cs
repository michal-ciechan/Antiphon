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
/// Writer and GET snapshot for <see cref="ComplexityChain"/> (CARD-0090). Human-overwrite and
/// lazy <c>NotAfter</c> match <see cref="RoutingPinService"/>.
/// </summary>
public sealed class ComplexityChainService
{
    public const string HumanOverwriteCode = "complexity_chain_human";
    internal const int ReasonCap = 400;

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ComplexityRoutingService _routing;
    private readonly DelegationSettings _settings;
    private readonly ILogger<ComplexityChainService> _logger;

    public ComplexityChainService(
        AppDbContext db,
        TimeProvider time,
        ComplexityRoutingService routing,
        IOptions<DelegationSettings> settings,
        ILogger<ComplexityChainService> logger)
    {
        _db = db;
        _time = time;
        _routing = routing;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ComplexityChainListDto> ListAsync(CancellationToken ct)
    {
        var chains = new List<ComplexityChainDto>(3);
        foreach (var complexity in new[] { TaskComplexity.Hard, TaskComplexity.Medium, TaskComplexity.Easy })
            chains.Add(await GetAsync(complexity, ct));
        return new ComplexityChainListDto(chains);
    }

    public async Task<ComplexityChainDto> GetAsync(TaskComplexity complexity, CancellationToken ct)
    {
        var row = await _routing.FindActiveAsync(complexity, ct);
        if (row is not null)
        {
            return new ComplexityChainDto(
                complexity,
                await ToCandidateDtosAsync(row.ParseCandidates(), ct),
                row.Provenance,
                "pin",
                row.Reason,
                row.NotAfter,
                row.UpdatedAt);
        }

        _settings.ComplexityChains.TryGetValue(complexity.ToString(), out var config);
        var pairs = (config ?? [])
            .Select(c => new ComplexityCandidatePair(c.Kind, c.Level))
            .ToList();
        return new ComplexityChainDto(
            complexity,
            await ToCandidateDtosAsync(pairs, ct),
            Provenance: pairs.Count == 0 ? null : RoutingPinProvenance.Auto,
            "config",
            Reason: null,
            NotAfter: null,
            UpdatedAt: null);
    }

    public async Task<ComplexityChainDto> UpsertAsync(
        TaskComplexity complexity,
        PutComplexityChainRequest request,
        Guid? sourceTaskId,
        CancellationToken ct)
    {
        if (!Enum.IsDefined(complexity))
        {
            throw new ValidationException(
                "complexity",
                $"'{complexity}' is not a complexity tier. Use Hard, Medium, or Easy.");
        }

        var pairs = ValidateCandidates(request.Candidates);
        var now = UtcNow();
        DateTime? notAfter = null;
        if (request.NotAfter is { } na)
        {
            notAfter = na.UtcDateTime;
            if (notAfter <= now)
            {
                throw new ValidationException(
                    nameof(request.NotAfter),
                    $"notAfter {notAfter:yyyy-MM-ddTHH:mm:ssZ} is in the past; the chain would expire "
                    + "the moment it was written.");
            }
        }

        var existing = await _routing.FindActiveAsync(complexity, ct);
        if (existing is not null
            && existing.Provenance == RoutingPinProvenance.Human
            && request.Provenance == RoutingPinProvenance.Auto)
        {
            throw new ConflictException(
                $"The {complexity} chain was set by a human (\"{existing.Reason}\"). An automatic "
                + "decision cannot overwrite it — a general policy shift must not silently wash away "
                + "a chain the operator wrote. Clear it explicitly (DELETE), or write the replacement "
                + "as provenance Human.",
                HumanOverwriteCode);
        }

        var reason = Cap(string.IsNullOrWhiteSpace(request.Reason)
            ? $"{complexity} chain"
            : request.Reason);

        if (existing is null)
        {
            existing = new ComplexityChain
            {
                Id = Guid.NewGuid(),
                Complexity = complexity,
                CreatedAt = now,
            };
            _db.ComplexityChains.Add(existing);
        }

        existing.CandidatesJson = ComplexityChain.SerializeCandidates(pairs);
        existing.Provenance = request.Provenance;
        existing.Reason = reason;
        existing.NotAfter = notAfter;
        existing.SourceTaskId = sourceTaskId;
        existing.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Complexity chain set: {Complexity} {Provenance} ({Count} candidates)",
            complexity, existing.Provenance, pairs.Count);
        return await GetAsync(complexity, ct);
    }

    /// <summary>Clear the active row for the tier. Already-clear is a no-op so a script can run twice.</summary>
    public async Task ClearAsync(TaskComplexity complexity, CancellationToken ct)
    {
        var existing = await _routing.FindActiveAsync(complexity, ct);
        if (existing is null)
            return;

        existing.ClearedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Complexity chain cleared: {Complexity}", complexity);
    }

    internal static IReadOnlyList<ComplexityCandidatePair> ValidateCandidates(
        IReadOnlyList<ComplexityCandidateRequest>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            throw new ValidationException(
                "Candidates",
                "A chain needs 1 to 8 candidates. An empty list is a DELETE, not a PUT.");
        }

        if (raw.Count > ComplexityRoutingService.MaxCandidates)
        {
            throw new ValidationException(
                "Candidates",
                $"A chain may list at most {ComplexityRoutingService.MaxCandidates} candidates (got {raw.Count}).");
        }

        var pairs = new List<ComplexityCandidatePair>(raw.Count);
        var seen = new HashSet<(AgentKind, AgentModelLevel)>();
        foreach (var item in raw)
        {
            if (!Enum.IsDefined(item.AgentKind))
            {
                throw new ValidationException(
                    "Candidates",
                    $"'{item.AgentKind}' is not an agent kind.");
            }

            if (!AgentTaskService.DelegatableKinds.Contains(item.AgentKind))
            {
                throw new ValidationException(
                    "Candidates",
                    $"{item.AgentKind} is not a delegate kind. A chain may name "
                    + $"{string.Join(" or ", AgentTaskService.DelegatableKinds)}.");
            }

            if (!Enum.IsDefined(item.ModelLevel))
            {
                throw new ValidationException(
                    "Candidates",
                    $"'{item.ModelLevel}' is not a model level.");
            }

            var pair = new ComplexityCandidatePair(item.AgentKind, item.ModelLevel);
            if (!seen.Add((pair.AgentKind, pair.ModelLevel)))
            {
                throw new ValidationException(
                    "Candidates",
                    $"Duplicate candidate {pair.AgentKind}/{pair.ModelLevel}. A chain lists each pair once.");
            }

            pairs.Add(pair);
        }

        return pairs;
    }

    private async Task<IReadOnlyList<ComplexityCandidateDto>> ToCandidateDtosAsync(
        IReadOnlyList<ComplexityCandidatePair> pairs,
        CancellationToken ct)
    {
        var list = new List<ComplexityCandidateDto>(pairs.Count);
        foreach (var pair in pairs)
        {
            var alias = ModelLevelAliases.For(pair.AgentKind, pair.ModelLevel);
            var (available, reason) = await _routing.EvaluateAvailabilityNowAsync(
                pair.AgentKind, pair.ModelLevel, ct);
            list.Add(new ComplexityCandidateDto(
                pair.AgentKind, pair.ModelLevel, alias, available, reason));
        }

        return list;
    }

    private static string Cap(string value) =>
        value.Length <= ReasonCap ? value : value[..ReasonCap];

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
