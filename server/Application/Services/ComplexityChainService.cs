using System.Text.Json;
using System.Text.Json.Serialization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Writer and GET snapshot for <see cref="ComplexityChain"/> (CARD-0090, CARD-0332). Human-overwrite
/// and lazy <c>NotAfter</c> match <see cref="RoutingPinService"/>. One active row per
/// (Role?, Complexity); a role cell outranks the any-role row as a whole.
/// </summary>
public sealed class ComplexityChainService
{
    public const string HumanOverwriteCode = "complexity_chain_human";
    public const string StaleCode = "complexity_chain_stale";
    internal const int ReasonCap = 400;

    // The 409 body's `current` row is pre-serialized: the problem-details writer drops nulls
    // and writes enums as numbers, and a client compares this row with the one it opened.
    private static readonly JsonSerializerOptions CurrentRowJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) },
    };

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ComplexityRoutingService _routing;
    private readonly DelegationSettings _settings;
    private readonly ILogger<ComplexityChainService> _logger;
    private readonly RoutingPinService? _pins;
    private readonly IServiceProvider? _services;

    public ComplexityChainService(
        AppDbContext db,
        TimeProvider time,
        ComplexityRoutingService routing,
        IOptions<DelegationSettings> settings,
        ILogger<ComplexityChainService> logger,
        RoutingPinService? pins = null,
        IServiceProvider? services = null)
    {
        _db = db;
        _time = time;
        _routing = routing;
        _settings = settings.Value;
        _logger = logger;
        _pins = pins;
        _services = services;
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
            ComplexityRoutingService.Tiers.Select(t => t.ToString()).ToList(),
            _settings.DefaultComplexity.GetValueOrDefault());
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
        var failures = new List<string>();
        var decision = _pins is null ? RoutingPinService.Decision.None
            : await ReadPartAsync("stage pin", () => _pins.ResolveAsync(null, role,
                new RoutingPinService.Ask(null, null, null, false), ct), RoutingPinService.Decision.None, failures, ct);
        var empty = (Candidates: (IReadOnlyList<RoutingCandidates.Candidate>)[],
            Provenance: (RoutingPinProvenance?)null, ChainSource: "config",
            ChainRole: (AgentTaskRole?)null, ResolvedFrom: "none");
        var loaded = await ReadPartAsync("chain", () => _routing.LoadChainAsync(role, complexity, ct), empty, failures, ct);
        var answering = loaded.ResolvedFrom is "role" or "any"
            ? await ReadPartAsync("chain metadata", () => _routing.FindActiveAsync(loaded.ChainRole, complexity, ct),
                (ComplexityChain?)null, failures, ct)
            : null;

        string? agentLabel = null;
        if (decision.Applied && decision.Pin!.AgentId is { } agentId)
        {
            var agent = await ReadPartAsync("standing agent", () => _db.Agents.AsNoTracking()
                .Where(a => a.Id == agentId).Select(a => new { a.Name, a.Kind }).SingleOrDefaultAsync(ct),
                null, failures, ct);
            // A missing/unreadable label cannot turn an agent-bound pin into a matrix target.
            agentLabel = agent is null ? $"{agentId} (unavailable)" : $"{agent.Name} ({agent.Kind})";
        }

        var effective = ComplexityEffective.Compose(decision, loaded.Candidates,
            loaded.ChainRole is null ? complexity.ToString() : $"{role}/{complexity}", loaded.ResolvedFrom,
            RolePolicyReadService.PolicyTarget(_settings, role), complexity == _settings.DefaultComplexity, agentLabel);

        // Share observations between stored and effective lists so one failed availability read
        // cannot produce two different verdicts for the same pair in this cell.
        var observations = new Dictionary<(AgentKind, AgentModelLevel), ComplexityCandidateDto>();
        async Task<IReadOnlyList<ComplexityCandidateDto>> WithAvailabilityAsync(
            IEnumerable<ComplexityCandidateDto> candidates, CancellationToken token)
        {
            var result = new List<ComplexityCandidateDto>();
            foreach (var candidate in candidates)
            {
                var key = (candidate.AgentKind, candidate.ModelLevel);
                if (!observations.TryGetValue(key, out var observed))
                {
                    var before = failures.Count;
                    var availability = await ReadPartAsync("availability",
                        () => _routing.EvaluateAvailabilityNowAsync(key.AgentKind, key.ModelLevel, token),
                        (AvailableNow: false, Reason: (string?)null), failures, token);
                    observed = candidate with
                    {
                        AvailableNow = availability.AvailableNow,
                        UnavailableReason = failures.Count > before ? failures[^1] : availability.Reason,
                    };
                    observations.Add(key, observed);
                }
                result.Add(observed);
            }
            return result;
        }

        var storedCandidates = await WithAvailabilityAsync(loaded.Candidates.Select(c =>
            new ComplexityCandidateDto(c.Kind, c.Level, c.Alias, true, null)), ct);
        effective = effective with { Candidates = await WithAvailabilityAsync(effective.Candidates, ct) };
        if (failures.Count > 0)
            effective = effective with
            {
                WithheldReason = string.Join("; ", failures.Prepend(effective.WithheldReason)
                    .Where(reason => reason is not null).Distinct()),
            };
        return new ComplexityChainDto(
            complexity,
            storedCandidates,
            loaded.Provenance,
            loaded.ChainSource,
            answering?.Reason,
            answering?.NotAfter,
            answering?.UpdatedAt,
            role,
            loaded.ResolvedFrom,
            effective,
            // The cell's own token only: a role cell answered by the any-role row has no row.
            loaded.ResolvedFrom == "role" ? answering?.ConcurrencyToken : null);
    }

    private async Task<T> ReadPartAsync<T>(string part, Func<Task<T>> read, T fallback,
        List<string> failures, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try { return await read(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var reason = $"{part} read failed ({ex.GetType().Name})";
            failures.Add(reason);
            _logger.LogWarning("Effective complexity cell: {Reason}", reason);
            return fallback;
        }
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

        var reason = Cap(string.IsNullOrWhiteSpace(request.Reason)
            ? $"{CellLabel(role, complexity)} chain"
            : request.Reason);

        ComplexityChain existing;
        for (var attempt = 1; ; attempt++)
        {
            existing = await StageUpsertAsync(
                role, complexity, request, pairs, reason, notAfter, sourceTaskId, now, ct);
            var lost = await TrySaveAsync(ct);
            if (lost is null)
                break;
            if (!RetriesLostRace(request.ConcurrencyToken, attempt))
                throw await StaleAsync(role, complexity, request.ConcurrencyToken, ct, lost);
        }

        OrchestratorInstructionsSignal.Fire(_services, $"complexity-chain {role?.ToString() ?? "any"}/{complexity}");
        _logger.LogInformation(
            "Complexity chain set: {Role}/{Complexity} {Provenance} ({Count} candidates)",
            role?.ToString() ?? "any", existing.Complexity, existing.Provenance, pairs.Count);
        return await GetAsync(role, complexity, ct);
    }

    /// <summary>
    /// One read-modify-write attempt up to the save: reads the active row, applies the stale and
    /// Human checks to it, and stages the caller's values with a fresh token.
    /// </summary>
    private async Task<ComplexityChain> StageUpsertAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        PutComplexityChainRequest request,
        IReadOnlyList<ComplexityCandidatePair> pairs,
        string reason,
        DateTime? notAfter,
        Guid? sourceTaskId,
        DateTime now,
        CancellationToken ct)
    {
        var existing = await _routing.FindActiveAsync(role, complexity, ct);
        await ThrowIfStaleAsync(role, complexity, existing, request.ConcurrencyToken, ct);
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
        existing.ConcurrencyToken = Guid.NewGuid();

        return existing;
    }

    public Task ClearAsync(TaskComplexity complexity, CancellationToken ct) =>
        ClearAsync(role: null, complexity, ct);

    public Task ClearAsync(AgentTaskRole? role, TaskComplexity complexity, CancellationToken ct) =>
        ClearAsync(role, complexity, expectedToken: null, ct);

    /// <summary>
    /// Clear the active row for the cell. Already-clear is a no-op so a script can run twice,
    /// whatever <paramref name="expectedToken"/> says: the state the caller asked for holds. A
    /// present row whose token differs is 409 <see cref="StaleCode"/> (CARD-1275).
    /// </summary>
    public async Task ClearAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        Guid? expectedToken,
        CancellationToken ct)
    {
        if (role is { } cellRole)
            ValidateCellRole(cellRole);

        for (var attempt = 1; ; attempt++)
        {
            var existing = await _routing.FindActiveAsync(role, complexity, ct);
            if (existing is null)
                return;

            await ThrowIfStaleAsync(role, complexity, existing, expectedToken, ct);
            existing.ClearedAt = UtcNow();
            existing.ConcurrencyToken = Guid.NewGuid();
            var lost = await TrySaveAsync(ct);
            if (lost is null)
                break;
            // A clear that lost to another clear still reached the state it asked for.
            if (await _routing.FindActiveAsync(role, complexity, ct) is null)
                return;
            if (!RetriesLostRace(expectedToken, attempt))
                throw await StaleAsync(role, complexity, expectedToken, ct, lost);
        }

        OrchestratorInstructionsSignal.Fire(_services, $"complexity-chain {role?.ToString() ?? "any"}/{complexity}");
        _logger.LogInformation(
            "Complexity chain cleared: {Role}/{Complexity}",
            role?.ToString() ?? "any", complexity);
    }

    /// <summary>
    /// Null <paramref name="expected"/> = no check against a version the caller read.
    /// <see cref="Guid.Empty"/> = the caller saw no active row. Otherwise the active row must
    /// carry exactly that token.
    /// </summary>
    private async Task ThrowIfStaleAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        ComplexityChain? existing,
        Guid? expected,
        CancellationToken ct)
    {
        if (expected is not { } token)
            return;
        var matches = token == Guid.Empty ? existing is null : existing?.ConcurrencyToken == token;
        if (!matches)
            throw await StaleAsync(role, complexity, token, ct);
    }

    /// <summary>
    /// The check above reads then writes; a writer between the two makes this save fail
    /// instead: the UPDATE names the original token (EF concurrency token) and a second active
    /// row breaks the unique index. Returns that failure with the tracker cleared, so the next
    /// read sees the committed row; null when the save landed.
    /// </summary>
    private async Task<DbUpdateException?> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (IsLostRace(ex))
        {
            _db.ChangeTracker.Clear();
            return ex;
        }
    }

    /// <summary>
    /// Attempts a write without a token makes before a lost race is 409 <see cref="StaleCode"/>
    /// (CARD-1275 D1). Such a write has no version to keep, so it re-reads the row and applies
    /// the caller's values again (last writer wins, as before the token existed).
    /// </summary>
    internal const int UnconditionalAttempts = 3;

    /// <summary>
    /// A write with a token is refused on its first lost race: the version it read is gone.
    /// </summary>
    private static bool RetriesLostRace(Guid? expected, int attempt) =>
        expected is null && attempt < UnconditionalAttempts;

    private async Task<HttpException> StaleAsync(
        AgentTaskRole? role,
        TaskComplexity complexity,
        Guid? expected,
        CancellationToken ct,
        Exception? inner = null)
    {
        var current = await GetAsync(role, complexity, ct);
        var label = CellLabel(role, complexity);
        var now = current.ConcurrencyToken is { } currentToken ? $"version {currentToken}" : "no row";
        var message = expected is not { } token
            ? $"The {label} chain changed during each of {UnconditionalAttempts} write attempts (now {now}). "
                + "Write again, or read it and send its concurrencyToken."
            : $"The {label} chain changed since it was read (read "
                + (token == Guid.Empty ? "no row" : $"version {token}") + $", now {now}). "
                + "Reload it, review the difference and write again with the current concurrencyToken.";
        _logger.LogInformation("Complexity chain write refused as stale: {Role}/{Complexity}",
            role?.ToString() ?? "any", complexity);
        var extensions = new Dictionary<string, object?>
        {
            ["current"] = JsonSerializer.SerializeToElement(current, CurrentRowJson),
        };
        return inner is null
            ? new ConflictException(message, StaleCode, extensions)
            : new StaleConflictException(message, extensions, inner);
    }

    private static bool IsLostRace(DbUpdateException ex)
    {
        if (ex is DbUpdateConcurrencyException)
            return true;
        for (var current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
                && pg.ConstraintName == "IX_ComplexityChains_Role_Complexity_Active")
                return true;
        }

        return false;
    }

    private sealed class StaleConflictException(
        string message, IReadOnlyDictionary<string, object?> extensions, Exception inner)
        : HttpException(409, message, inner, StaleCode, extensions);

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
            resolvedFrom,
            ConcurrencyToken: row.ConcurrencyToken);

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
