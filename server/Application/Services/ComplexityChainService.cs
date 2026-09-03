using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Writer and GET snapshot for <see cref="ComplexityChain"/> (CARD-0090, CARD-0332). Human-overwrite
/// and lazy <c>NotAfter</c> match <see cref="RoutingPinService"/>. One active row per
/// (Role?, Complexity); a role cell outranks the any-role row as a whole.
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

    public Task<ComplexityChainListDto> ListAsync(CancellationToken ct) =>
        ListAsync(role: null, ct);

    public async Task<ComplexityChainListDto> ListAsync(AgentTaskRole? role, CancellationToken ct)
    {
        var chains = new List<ComplexityChainDto>();
        if (role is { } requested)
        {
            ValidateCellRole(requested);
            foreach (var complexity in ComplexityRoutingService.Tiers)
                chains.Add(await GetEffectiveAsync(requested, complexity, ct));
        }
        else
        {
            foreach (var complexity in ComplexityRoutingService.Tiers)
                chains.Add(await GetAsync(role: null, complexity, ct));

            foreach (var cell in await _routing.ListLiveCellsAsync(ct))
                chains.Add(await ToRowDtoAsync(cell, resolvedFrom: "role", ct));
        }

        return new ComplexityChainListDto(
            chains,
            ComplexityRoutingService.RoutableRoles,
            ComplexityRoutingService.Tiers.Select(t => t.ToString()).ToList());
    }

    public Task<ComplexityChainDto> GetAsync(TaskComplexity complexity, CancellationToken ct) =>
        GetAsync(role: null, complexity, ct);

    /// <summary>
    /// The row itself: the role cell, or the any-role row/config when <paramref name="role"/> is
    /// null. Does not fall through from a missing cell to the any-role row.
    /// </summary>
    public async Task<ComplexityChainDto> GetAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        CancellationToken ct)
    {
        if (role is { } cellRole)
            ValidateCellRole(cellRole);

        var row = await _routing.FindActiveAsync(role, complexity, ct);
        if (row is not null)
        {
            return await ToRowDtoAsync(
                row,
                resolvedFrom: role is null ? "any" : "role",
                ct);
        }

        if (role is not null)
        {
            return new ComplexityChainDto(
                complexity,
                [],
                Provenance: null,
                "config",
                Reason: null,
                NotAfter: null,
                UpdatedAt: null,
                role,
                "none");
        }

        return await ConfigDtoAsync(complexity, ct);
    }

    /// <summary>D3 resolution for one (role, complexity): cell, then any-role, then config, then none.</summary>
    public async Task<ComplexityChainDto> GetEffectiveAsync(
        AgentTaskRole role,
        TaskComplexity complexity,
        CancellationToken ct)
    {
        ValidateCellRole(role);
        var loaded = await _routing.LoadChainAsync(role, complexity, ct);
        var pairs = loaded.Candidates
            .Select(c => new ComplexityCandidatePair(c.Kind, c.Level))
            .ToList();
        var answering = loaded.ResolvedFrom is "role" or "any"
            ? await _routing.FindActiveAsync(loaded.ChainRole, complexity, ct)
            : null;
        return new ComplexityChainDto(
            complexity,
            await ToCandidateDtosAsync(pairs, ct),
            loaded.Provenance,
            loaded.ChainSource,
            answering?.Reason,
            answering?.NotAfter,
            answering?.UpdatedAt,
            role,
            loaded.ResolvedFrom);
    }

    public Task<ComplexityChainDto> UpsertAsync(
        TaskComplexity complexity,
        PutComplexityChainRequest request,
        Guid? sourceTaskId,
        CancellationToken ct) =>
        UpsertAsync(role: null, complexity, request, sourceTaskId, ct);

    public async Task<ComplexityChainDto> UpsertAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        PutComplexityChainRequest request,
        Guid? sourceTaskId,
        CancellationToken ct)
    {
        if (role is { } cellRole)
            ValidateCellRole(cellRole);

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

        var existing = await _routing.FindActiveAsync(role, complexity, ct);
        if (existing is not null
            && existing.Provenance == RoutingPinProvenance.Human
            && request.Provenance == RoutingPinProvenance.Auto)
        {
            var label = CellLabel(role, complexity);
            throw new ConflictException(
                $"The {label} chain was set by a human (\"{existing.Reason}\"). An automatic "
                + "decision cannot overwrite it — a general policy shift must not silently wash away "
                + "a chain the operator wrote. Clear it explicitly (DELETE), or write the replacement "
                + "as provenance Human.",
                HumanOverwriteCode);
        }

        // D6: an Auto write to a role cell is refused when the any-role row is Human — writing
        // Plan/Hard as Auto would silently route Plan off a list the operator wrote by hand.
        if (role is not null
            && request.Provenance == RoutingPinProvenance.Auto)
        {
            var anyRole = await _routing.FindActiveAsync(role: null, complexity, ct);
            if (anyRole is { Provenance: RoutingPinProvenance.Human })
            {
                throw new ConflictException(
                    $"The any-role {complexity} chain was set by a human (\"{anyRole.Reason}\"). "
                    + $"Writing {role}/{complexity} as Auto would silently route {role} off that list. "
                    + "Write it as Human, or clear the any-role row.",
                    HumanOverwriteCode);
            }
        }

        var reason = Cap(string.IsNullOrWhiteSpace(request.Reason)
            ? $"{CellLabel(role, complexity)} chain"
            : request.Reason);

        if (existing is null)
        {
            existing = new ComplexityChain
            {
                Id = Guid.NewGuid(),
                Role = role,
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
            "Complexity chain set: {Role}/{Complexity} {Provenance} ({Count} candidates)",
            role?.ToString() ?? "any", existing.Complexity, existing.Provenance, pairs.Count);
        return await GetAsync(role, complexity, ct);
    }

    public Task ClearAsync(TaskComplexity complexity, CancellationToken ct) =>
        ClearAsync(role: null, complexity, ct);

    /// <summary>Clear the active row for the cell. Already-clear is a no-op so a script can run twice.</summary>
    public async Task ClearAsync(AgentTaskRole? role, TaskComplexity complexity, CancellationToken ct)
    {
        if (role is { } cellRole)
            ValidateCellRole(cellRole);

        var existing = await _routing.FindActiveAsync(role, complexity, ct);
        if (existing is null)
            return;

        existing.ClearedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Complexity chain cleared: {Role}/{Complexity}",
            role?.ToString() ?? "any", complexity);
    }

    internal static void ValidateCellRole(AgentTaskRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ValidationException(
                "role",
                $"'{role}' is not a task role.");
        }

        if (AgentTaskRoles.IsSpecialist(role))
        {
            throw new ValidationException(
                "role",
                "seat-pinned roles are not routed by chains");
        }

        if (!ComplexityRoutingService.RoutableRoles.Contains(role))
        {
            throw new ValidationException(
                "role",
                $"'{role}' is not a task role.");
        }
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

    private async Task<ComplexityChainDto> ToRowDtoAsync(
        ComplexityChain row,
        string resolvedFrom,
        CancellationToken ct) =>
        new(
            row.Complexity,
            await ToCandidateDtosAsync(row.ParseCandidates(), ct),
            row.Provenance,
            "pin",
            row.Reason,
            row.NotAfter,
            row.UpdatedAt,
            row.Role,
            resolvedFrom);

    private async Task<ComplexityChainDto> ConfigDtoAsync(TaskComplexity complexity, CancellationToken ct)
    {
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
            UpdatedAt: null,
            Role: null,
            ResolvedFrom: pairs.Count == 0 ? "none" : "config");
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

    private static string CellLabel(AgentTaskRole? role, TaskComplexity complexity) =>
        role is { } r ? $"{r}/{complexity}" : complexity.ToString();

    private static string Cap(string value) =>
        value.Length <= ReasonCap ? value : value[..ReasonCap];

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
