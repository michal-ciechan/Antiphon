using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Reader, writer and create-time resolver for <see cref="RoutingPin"/> (CARD-0305). A pin is the
/// standing instruction the NEXT create must read; the task row still snapshots the resolved kind
/// and level, so changing a pin never rewrites work that is already Queued.
///
/// <para>Deliberately NOT <see cref="ModelAvailability"/>. That answers "is this kind+alias usable
/// at all" for the fleet; this answers "for this card+role, which kind/tier should run". A pin
/// naming a held alias consumes the hold's 409 <c>model_disabled</c> (with a coda saying the
/// available list does not satisfy the pin) and never writes a hold. <c>ignoreRoutingPin</c> and
/// <c>ignoreModelDisabled</c> are different flags.</para>
/// </summary>
public sealed class RoutingPinService
{
    public const string HumanOverwriteCode = "routing_pin_human";

    internal const int ReasonCap = 400;

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly ILogger<RoutingPinService> _logger;
    private readonly ComplexityRoutingService? _complexityRouting;

    public RoutingPinService(
        AppDbContext db,
        TimeProvider time,
        ILogger<RoutingPinService> logger,
        ComplexityRoutingService? complexityRouting = null)
    {
        _db = db;
        _time = time;
        _logger = logger;
        _complexityRouting = complexityRouting;
    }

    /// <summary>What a create is asking for, before the role policy fills anything in.</summary>
    public sealed record Ask(
        AgentKind? AgentKind,
        AgentModelLevel? ModelLevel,
        Guid? AgentId,
        bool IgnoreRoutingPin);

    /// <summary>
    /// The pin's verdict for one create. <see cref="Pin"/> is the CHOSEN grain (card outranks
    /// stage as a whole row); <see cref="StagePin"/> is kept alongside it because a stage pin's
    /// <c>ForbiddenAliases</c> still bite an Auto card pin.
    /// </summary>
    public sealed record Decision(
        RoutingPin? Pin,
        RoutingPin? StagePin,
        string? CardIdentifier,
        AgentKind? AgentKind,
        AgentModelLevel? ModelLevel,
        Guid? AgentId,
        string? Warning,
        string? EventNote,
        bool Ignored,
        IReadOnlyList<RoutingCandidate>? Candidates = null)
    {
        public static readonly Decision None =
            new(null, null, null, null, null, null, null, null, false);

        /// <summary>True when a pin was found and actually applied (not ignored).</summary>
        public bool Applied => Pin is not null && !Ignored;

        /// <summary>
        /// The chosen grain's list, before explicit-request narrowing. Empty when none.
        /// </summary>
        public IReadOnlyList<RoutingCandidate> PinCandidates => Candidates ?? [];
    }

    // ---------------------------------------------------------------- reads

    /// <summary>
    /// The one active pin for a grain, expiring a <see cref="RoutingPin.NotAfter"/> that has
    /// passed on the way past (lazy clear, same shape as a model hold's).
    /// </summary>
    public async Task<RoutingPin?> FindActiveAsync(Guid? cardId, AgentTaskRole role, CancellationToken ct)
    {
        // A specialist row is about a task, never a card, so it has no stage and never carries a pin.
        if (AgentTaskRoles.IsSpecialist(role))
            return null;

        var query = _db.RoutingPins.Where(p => p.ClearedAt == null && p.Role == role);
        query = cardId is Guid id
            ? query.Where(p => p.CardId == id)
            : query.Where(p => p.CardId == null);

        var rows = await query.ToListAsync(ct);
        if (rows.Count == 0)
            return null;

        var now = UtcNow();
        RoutingPin? live = null;
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

    public async Task<IReadOnlyList<RoutingPinDto>> ListAsync(
        string? card, AgentTaskRole? role, CancellationToken ct)
    {
        Guid? cardId = null;
        var cardFilter = !string.IsNullOrWhiteSpace(card);
        if (cardFilter)
            cardId = await ResolveCardAsync(card!, ct);

        await ExpireAsync(ct);

        var query = _db.RoutingPins.AsNoTracking().Where(p => p.ClearedAt == null);
        if (cardFilter)
        {
            // A card query includes the stage-wide rows: those are what applies when the card has
            // no pin of its own, so hiding them would answer the wrong question.
            var id = cardId!.Value;
            query = query.Where(p => p.CardId == id || p.CardId == null);
        }

        if (role is { } wanted)
            query = query.Where(p => p.Role == wanted);

        var pins = await query.OrderBy(p => p.Role).ThenBy(p => p.CardId).ToListAsync(ct);
        return await ToDtosAsync(pins, ct);
    }

    // --------------------------------------------------------------- writes

    /// <summary>
    /// Upsert the active row for the grain. Human replaces Auto and Human; <b>Auto never replaces
    /// Human</b> (409 <c>routing_pin_human</c>) — that is the whole reason provenance is a column
    /// and not a comment.
    /// </summary>
    public async Task<RoutingPinDto> UpsertAsync(
        PutRoutingPinRequest request, Guid? sourceTaskId, CancellationToken ct)
    {
        var role = ValidateRole(request.Role);
        Guid? cardId = null;
        if (!string.IsNullOrWhiteSpace(request.Card))
            cardId = await ResolveCardAsync(request.Card, ct);

        var written = ResolveWrittenCandidates(request);
        if (request.AgentId is not null && written.Count > 1)
        {
            throw new ValidationException(
                nameof(request.Candidates),
                "A standing agent is one program — pin the agent, or list candidates, not both.");
        }

        var now = UtcNow();
        DateTime? notBefore = null;
        if (request.NotBefore is { } nb)
        {
            notBefore = nb.UtcDateTime;
            if (notBefore <= now)
            {
                throw new ValidationException(
                    nameof(request.NotBefore),
                    $"notBefore {notBefore:yyyy-MM-ddTHH:mm:ssZ} is in the past; a dated pin holds "
                    + "work until a FUTURE instant. Omit it to pin with no date.");
            }
        }

        DateTime? notAfter = null;
        if (request.NotAfter is { } na)
        {
            notAfter = na.UtcDateTime;
            if (notAfter <= now)
            {
                throw new ValidationException(
                    nameof(request.NotAfter),
                    $"notAfter {notAfter:yyyy-MM-ddTHH:mm:ssZ} is in the past; the pin would expire "
                    + "the moment it was written.");
            }

            if (notBefore is { } before && notAfter <= before)
            {
                throw new ValidationException(
                    nameof(request.NotAfter),
                    "notAfter must be after notBefore; a window that closes before it opens pins nothing.");
            }
        }

        if (request.AgentId is { } agentId)
        {
            var agent = await _db.Agents.AsNoTracking()
                .Where(a => a.Id == agentId)
                .Select(a => new { a.Id, a.Name, a.IsPoolDelegate })
                .FirstOrDefaultAsync(ct)
                ?? throw new ValidationException(nameof(request.AgentId), $"No agent {agentId} exists.");
            if (agent.IsPoolDelegate)
            {
                throw new ValidationException(
                    nameof(request.AgentId),
                    $"'{agent.Name}' is a pool delegate, so it cannot be pinned to a card+role. "
                    + "Pin a STANDING agent, or use a follow-up for 'the same delegate again'.");
            }
        }

        var forbidden = NormalizeForbidden(request.ForbiddenAliases);

        var existing = await FindActiveAsync(cardId, role, ct);
        if (existing is not null
            && existing.Provenance == RoutingPinProvenance.Human
            && request.Provenance == RoutingPinProvenance.Auto)
        {
            throw new ConflictException(
                $"{Describe(existing, await IdentifierAsync(existing.CardId, ct))} was set by a human "
                + $"(\"{existing.Reason}\"). An automatic decision cannot overwrite it — a general "
                + "policy shift must not silently wash away a pin the operator wrote. Clear it "
                + "explicitly (DELETE), or write the replacement as provenance Human.",
                HumanOverwriteCode);
        }

        var reason = Cap(string.IsNullOrWhiteSpace(request.Reason) ? "routing pin" : request.Reason);

        if (existing is null)
        {
            existing = new RoutingPin
            {
                Id = Guid.NewGuid(),
                CardId = cardId,
                Role = role,
                CreatedAt = now,
            };
            _db.RoutingPins.Add(existing);
        }

        existing.Provenance = request.Provenance;
        existing.Strength = request.Strength;
        existing.SetCandidates(written);
        existing.AgentId = request.AgentId;
        existing.ForbiddenAliases = forbidden.Count == 0 ? null : string.Join(",", forbidden);
        existing.NotBefore = notBefore;
        existing.NotAfter = notAfter;
        existing.Reason = reason;
        existing.SourceTaskId = sourceTaskId;
        existing.UpdatedAt = now;

        await _db.SaveChangesAsync(ct);
        var identifier = await IdentifierAsync(cardId, ct);
        _logger.LogInformation("Routing pin set: {Pin}", Describe(existing, identifier));
        return ToDto(existing, identifier);
    }

    /// <summary>Clear one pin by id. Already-clear is a no-op so a script can be run twice.</summary>
    public async Task<bool> ClearAsync(Guid id, CancellationToken ct)
    {
        var pin = await _db.RoutingPins.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pin is null)
            return false;
        if (pin.ClearedAt is not null)
            return true;

        pin.ClearedAt = UtcNow();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Routing pin cleared: {Pin}", Describe(pin, await IdentifierAsync(pin.CardId, ct)));
        return true;
    }

    // ------------------------------------------------------------- resolving

    /// <summary>
    /// What this create should run as. Card grain outranks stage grain AS A WHOLE ROW — that is
    /// what lets CARD-0301's pin name fable while the Plan stage forbids it.
    ///
    /// <para>Per field, an explicit request wins over the pin and the pin fills what the request
    /// omitted. A request that DISAGREES with a <c>Required</c> pin is 409
    /// <c>routing_pin_conflict</c>; with a <c>Preferred</c> pin it wins and is warned about.</para>
    /// </summary>
    public async Task<Decision> ResolveAsync(
        Guid? cardId, AgentTaskRole role, Ask ask, CancellationToken ct)
    {
        var cardPin = cardId is Guid id ? await FindActiveAsync(id, role, ct) : null;
        var stagePin = await FindActiveAsync(null, role, ct);
        var pin = cardPin ?? stagePin;
        if (pin is null)
            return Decision.None;

        var identifier = await IdentifierAsync(pin.CardId, ct);
        if (ask.IgnoreRoutingPin)
        {
            return new Decision(
                pin,
                stagePin,
                identifier,
                null,
                null,
                null,
                $"ignoreRoutingPin: {Describe(pin, identifier)} was not applied to this task. "
                + "The pin is unchanged.",
                null,
                Ignored: true);
        }

        var pinCandidates = pin.Candidates;
        var compatible = pinCandidates.Where(c => CompatibleWithAsk(c, ask)).ToList();
        var conflicts = new List<string>();
        if (pinCandidates.Count > 0
            && (ask.AgentKind is not null || ask.ModelLevel is not null)
            && compatible.Count == 0)
        {
            if (ask.AgentKind is { } askedKind)
                conflicts.Add($"kind {askedKind}");
            if (ask.ModelLevel is { } askedLevel)
                conflicts.Add($"level {askedLevel}");
        }

        if (pin.AgentId is { } pinAgent && ask.AgentId is { } askedAgent && askedAgent != pinAgent)
            conflicts.Add($"agent {askedAgent} against pinned agent {pinAgent}");

        string? warning = null;
        if (conflicts.Count > 0)
        {
            var detail = string.Join("; ", conflicts);
            if (pin.Strength == RoutingPinStrength.Required
                && pinCandidates.Count > 0
                && compatible.Count == 0
                && (ask.AgentKind is not null || ask.ModelLevel is not null))
            {
                throw new RoutingPinConflictException(
                    ToRef(pin, identifier),
                    $"{Describe(pin, identifier)} is REQUIRED and lists {FormatCandidateList(pin)}; "
                    + $"this request asks for {detail} (\"{pin.Reason}\"). Match the pin, replace it "
                    + "(PUT /api/routing-pins), or pass ignoreRoutingPin to override it for this one task.");
            }

            if (pin.Strength == RoutingPinStrength.Required
                && ask.AgentId is not null
                && pin.AgentId is not null
                && ask.AgentId != pin.AgentId)
            {
                throw new RoutingPinConflictException(
                    ToRef(pin, identifier),
                    $"{Describe(pin, identifier)} is REQUIRED, and this request asks for {detail} "
                    + $"(\"{pin.Reason}\"). Match the pin, replace it (PUT /api/routing-pins), or "
                    + "pass ignoreRoutingPin to override it for this one task.");
            }

            warning = $"Overrode preferred {Describe(pin, identifier)}: {detail}.";
        }

        var resolvedKind = ask.AgentKind ?? pin.AgentKind;
        var resolvedLevel = ask.ModelLevel ?? pin.ModelLevel;
        var resolvedAgent = ask.AgentId ?? pin.AgentId;

        return new Decision(
            pin,
            stagePin,
            identifier,
            resolvedKind,
            resolvedLevel,
            resolvedAgent,
            warning,
            EventNote(pin, identifier),
            Ignored: false,
            pinCandidates);
    }

    /// <summary>
    /// The stage-wide pin's forbid list, applied to the alias the create actually resolved.
    /// Skipped for an ignored pin, and skipped for a <b>Human</b> card pin — a human naming an
    /// alias for one card is the deliberate exception to their own stage rule. An <b>Auto</b> card
    /// pin gets no such exemption: that is the "auto silently overrides human" hole this card exists
    /// to close.
    /// </summary>
    public void EnforceForbiddenAliases(Decision decision, AgentKind kind, AgentModelLevel level)
    {
        if (decision.Ignored || decision.StagePin is not { } stage)
            return;
        if (decision.Pin is { CardId: not null, Provenance: RoutingPinProvenance.Human })
            return;

        var forbidden = SplitForbidden(stage.ForbiddenAliases);
        if (forbidden.Count == 0)
            return;

        var alias = ModelLevelAliases.For(kind, level);
        if (!forbidden.Contains(alias, StringComparer.OrdinalIgnoreCase))
            return;

        throw new RoutingPinForbiddenException(
            ToRef(stage, null),
            $"{alias} is forbidden for {stage.Role} by the {Provenance(stage.Provenance)} stage-wide "
            + $"routing pin (\"{stage.Reason}\"; forbidden: {string.Join(", ", forbidden)}). "
            + "Choose another kind/level, or write a card pin (provenance Human) that deliberately "
            + "overrides the stage rule for this card.");
    }

    // --------------------------------------------------------------- shaping

    public async Task<RoutingPinDto?> GetActiveDtoAsync(
        Guid? cardId, AgentTaskRole role, CancellationToken ct)
    {
        var pin = await FindActiveAsync(cardId, role, ct);
        return pin is null ? null : ToDto(pin, await IdentifierAsync(pin.CardId, ct));
    }

    public static RoutingPinRefDto ToRef(RoutingPin pin, string? identifier) => new(
        pin.Id,
        pin.CardId,
        identifier,
        pin.Role,
        pin.Provenance,
        pin.Strength,
        pin.AgentKind,
        pin.ModelLevel,
        pin.NotBefore,
        pin.Reason,
        pin.Candidates.Count);

    public static RoutingPinDto ToDto(
        RoutingPin pin,
        string? identifier,
        IReadOnlyList<RoutingPinCandidateDto>? candidates = null)
    {
        var list = candidates ?? pin.Candidates.Select(c => ToCandidateDto(c, true, null)).ToList();
        return new RoutingPinDto(
            pin.Id,
            pin.CardId,
            identifier,
            pin.Role,
            pin.Provenance,
            pin.Strength,
            pin.AgentKind,
            pin.ModelLevel,
            AliasOf(pin.Head),
            pin.AgentId,
            SplitForbidden(pin.ForbiddenAliases),
            pin.NotBefore,
            pin.NotAfter,
            pin.Reason,
            pin.SourceTaskId,
            pin.CreatedAt,
            pin.UpdatedAt,
            list,
            list.Count);
    }

    /// <summary>The sentence a task's Created event carries when a pin decided its routing.</summary>
    internal static string EventNote(RoutingPin pin, string? identifier)
    {
        var parts = new List<string> { Describe(pin, identifier) };
        if (pin.NotBefore is { } notBefore)
            parts.Add($"notBefore={notBefore:yyyy-MM-ddTHH:mm:ssZ}");
        return "pin=" + string.Join(" ", parts);
    }

    internal static string Describe(RoutingPin pin, string? identifier)
    {
        var grain = pin.CardId is null ? "stage-wide" : identifier ?? pin.CardId.ToString()!;
        var text = $"the {Provenance(pin.Provenance)} {pin.Strength.ToString().ToLowerInvariant()} "
            + $"{grain} {pin.Role} routing pin";
        if (pin.Head is { } head)
        {
            text += " " + FormatHead(head);
            if (pin.Candidates.Count > 1)
                text += $" +{pin.Candidates.Count - 1}";
        }

        return text;
    }

    private static string Provenance(RoutingPinProvenance provenance) =>
        provenance == RoutingPinProvenance.Human ? "human" : "auto";

    internal static IReadOnlyList<string> SplitForbidden(string? stored) =>
        string.IsNullOrWhiteSpace(stored)
            ? []
            : stored.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static IReadOnlyList<string> NormalizeForbidden(IReadOnlyList<string>? raw)
    {
        if (raw is null || raw.Count == 0)
            return [];

        var canonical = new List<string>();
        foreach (var token in raw)
        {
            if (string.IsNullOrWhiteSpace(token))
                continue;

            var alias = ModelAlias.CanonicalHoldAlias(token)
                ?? ModelAlias.Normalize(AgentKind.ClaudeCode, token);
            if (alias is null || alias == ModelAlias.KindWide)
            {
                var known = string.Join(", ", ModelAlias.DelegatableAliases.Select(a => a.Alias).Distinct());
                throw new ValidationException(
                    nameof(PutRoutingPinRequest.ForbiddenAliases),
                    $"'{token}' is not a model alias. Forbid one of: {known}. A whole kind is "
                    + "excluded by pinning a different agentKind, not by forbidding '*'.");
            }

            if (!canonical.Contains(alias, StringComparer.Ordinal))
                canonical.Add(alias);
        }

        return canonical;
    }

    /// <summary>
    /// The same walk <c>AgentTaskCardBinder</c> and the card API use (CARD-0218), so "which card is
    /// CARD-51" cannot mean one thing to a pin and another to a dispatch. An identifier that binds
    /// nothing, or binds ambiguously, is a 422 — a pin that silently attached to the wrong card
    /// would route someone else's work.
    /// </summary>
    private async Task<Guid> ResolveCardAsync(string card, CancellationToken ct)
    {
        var raw = card.Trim();
        if (Guid.TryParse(raw, out var cardGuid))
        {
            return await _db.Cards.AsNoTracking().AnyAsync(c => c.Id == cardGuid, ct)
                ? cardGuid
                : throw new ValidationException(
                    nameof(PutRoutingPinRequest.Card), $"No card with id {cardGuid} exists.");
        }

        var canonical = CardService.TryCanonicalIdentifier(raw)
            ?? throw new ValidationException(
                nameof(PutRoutingPinRequest.Card),
                $"'{raw}' is not a card identifier. Use CARD-0305, card-305, #305, 305, or the card's guid.");

        var result = await CardIdentifierScope.ResolveAsync(_db, canonical, CardScopeContext.None, ct);
        if (result.Match is { } match)
            return match.Id;
        throw new ValidationException(
            nameof(PutRoutingPinRequest.Card),
            result.Candidates.Count > 0
                ? CardIdentifierScope.DescribeCandidates(canonical, result.Candidates)
                : $"Identifier {canonical} matches no card on any board.");
    }

    private static AgentTaskRole ValidateRole(AgentTaskRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ValidationException(nameof(PutRoutingPinRequest.Role), $"'{role}' is not a task role.");
        if (AgentTaskRoles.IsSpecialist(role))
        {
            throw new ValidationException(
                nameof(PutRoutingPinRequest.Role),
                "Specialist rows (Check, Distill, Diagnose) are furniture, not a card stage, so there "
                + "is no stage to pin. Pin the role whose work you mean (Plan, Code, Debug, ...).");
        }

        if (!ComplexityRoutingService.RoutableRoles.Contains(role))
        {
            throw new ValidationException(
                nameof(PutRoutingPinRequest.Role),
                $"'{role}' is not a task role.");
        }

        return role;
    }

    /// <summary>Clear every pin whose <c>NotAfter</c> has passed. Lazy expiry, not a background job.</summary>
    private async Task ExpireAsync(CancellationToken ct)
    {
        var now = UtcNow();
        var expired = await _db.RoutingPins
            .Where(p => p.ClearedAt == null && p.NotAfter != null && p.NotAfter <= now)
            .ToListAsync(ct);
        if (expired.Count == 0)
            return;

        foreach (var pin in expired)
            pin.ClearedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<RoutingPinDto>> ToDtosAsync(
        IReadOnlyList<RoutingPin> pins, CancellationToken ct)
    {
        var cardIds = pins.Where(p => p.CardId is not null).Select(p => p.CardId!.Value).Distinct().ToList();
        var identifiers = cardIds.Count == 0
            ? []
            : await _db.Cards.AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Identifier, ct);

        var result = new List<RoutingPinDto>(pins.Count);
        foreach (var p in pins)
        {
            var identifier = p.CardId is Guid id && identifiers.TryGetValue(id, out var card)
                ? card
                : null;
            var candidates = await EvaluateCandidatesAsync(p, ct);
            result.Add(ToDto(p, identifier, candidates));
        }

        return result;
    }

    private async Task<string?> IdentifierAsync(Guid? cardId, CancellationToken ct) =>
        cardId is Guid id
            ? await _db.Cards.AsNoTracking().Where(c => c.Id == id).Select(c => c.Identifier)
                .FirstOrDefaultAsync(ct)
            : null;

    private static string Cap(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= ReasonCap ? trimmed : trimmed[..ReasonCap];
    }

    internal static IReadOnlyList<RoutingCandidate> ResolveWrittenCandidates(PutRoutingPinRequest request)
    {
        var hasList = request.Candidates is { Count: > 0 };
        var hasEmptyList = request.Candidates is { Count: 0 };
        var hasShorthand = request.AgentKind is not null || request.ModelLevel is not null;

        if (hasEmptyList)
        {
            throw new ValidationException(
                nameof(request.Candidates),
                "A pin lists 1 to 8 candidates. Omit candidates to write a forbid-only or agent-only pin.");
        }

        if (hasList && hasShorthand)
        {
            throw new ValidationException(
                nameof(request.Candidates),
                "Send either the agentKind/modelLevel shorthand or candidates, not both.");
        }

        if (hasList)
            return ValidateCandidateList(request.Candidates!);

        if (hasShorthand)
        {
            if (request.AgentKind is { } kind && !AgentTaskService.DelegatableKinds.Contains(kind))
            {
                throw new ValidationException(
                    nameof(request.AgentKind),
                    $"{kind} is not a delegate kind. A pin may name "
                    + $"{string.Join(" or ", AgentTaskService.DelegatableKinds)}.");
            }

            return [new RoutingCandidate(request.AgentKind, request.ModelLevel)];
        }

        return [];
    }

    internal static IReadOnlyList<RoutingCandidate> ValidateCandidateList(
        IReadOnlyList<RoutingCandidateRequest> raw)
    {
        if (raw.Count > ComplexityRoutingService.MaxCandidates)
        {
            throw new ValidationException(
                nameof(PutRoutingPinRequest.Candidates),
                $"A pin may list at most {ComplexityRoutingService.MaxCandidates} candidates (got {raw.Count}).");
        }

        var pairs = new List<RoutingCandidate>(raw.Count);
        var seen = new HashSet<RoutingCandidate>();
        foreach (var item in raw)
        {
            if (item.AgentKind is null && item.ModelLevel is null)
            {
                throw new ValidationException(
                    nameof(PutRoutingPinRequest.Candidates),
                    "A candidate must name a kind or a level.");
            }

            if (item.AgentKind is { } kind)
            {
                if (!Enum.IsDefined(kind))
                {
                    throw new ValidationException(
                        nameof(PutRoutingPinRequest.Candidates),
                        $"'{kind}' is not an agent kind.");
                }

                if (!AgentTaskService.DelegatableKinds.Contains(kind))
                {
                    throw new ValidationException(
                        nameof(PutRoutingPinRequest.Candidates),
                        $"{kind} is not a delegate kind. A pin may name "
                        + $"{string.Join(" or ", AgentTaskService.DelegatableKinds)}.");
                }
            }

            if (item.ModelLevel is { } level && !Enum.IsDefined(level))
            {
                throw new ValidationException(
                    nameof(PutRoutingPinRequest.Candidates),
                    $"'{level}' is not a model level.");
            }

            var pair = new RoutingCandidate(item.AgentKind, item.ModelLevel);
            if (!seen.Add(pair))
            {
                throw new ValidationException(
                    nameof(PutRoutingPinRequest.Candidates),
                    $"Duplicate candidate {pair.Describe()}. A pin lists each pair once.");
            }

            pairs.Add(pair);
        }

        return pairs;
    }

    internal static bool CompatibleWithAsk(RoutingCandidate candidate, Ask ask)
    {
        if (ask.AgentKind is { } kind && candidate.AgentKind is { } ck && ck != kind)
            return false;
        if (ask.ModelLevel is { } level && candidate.ModelLevel is { } cl && cl != level)
            return false;
        return true;
    }

    internal static string FormatCandidateList(RoutingPin pin) =>
        string.Join(", ", pin.Candidates.Select(FormatHead));

    internal static string FormatHead(RoutingCandidate candidate)
    {
        if (candidate.AgentKind is { } kind && candidate.ModelLevel is { } level)
            return $"{kind}/{level} ({ModelLevelAliases.For(kind, level)})";
        if (candidate.AgentKind is { } kindOnly)
            return kindOnly.ToString();
        if (candidate.ModelLevel is { } levelOnly)
            return levelOnly.ToString();
        return "*";
    }

    internal static string? AliasOf(RoutingCandidate? candidate) =>
        candidate?.AgentKind is { } kind && candidate.ModelLevel is { } level
            ? ModelLevelAliases.For(kind, level)
            : null;

    private static RoutingPinCandidateDto ToCandidateDto(
        RoutingCandidate candidate, bool availableNow, string? unavailableReason) =>
        new(candidate.AgentKind, candidate.ModelLevel, AliasOf(candidate), availableNow, unavailableReason);

    private async Task<IReadOnlyList<RoutingPinCandidateDto>> EvaluateCandidatesAsync(
        RoutingPin pin, CancellationToken ct)
    {
        if (pin.Candidates.Count == 0)
            return [];

        var list = new List<RoutingPinCandidateDto>(pin.Candidates.Count);
        foreach (var candidate in pin.Candidates)
        {
            var available = true;
            string? reason = null;
            if (_complexityRouting is not null
                && candidate.AgentKind is { } kind
                && candidate.ModelLevel is { } level)
            {
                (available, reason) = await _complexityRouting.EvaluateAvailabilityNowAsync(kind, level, ct);
            }

            list.Add(ToCandidateDto(candidate, available, reason));
        }

        return list;
    }

    private DateTime UtcNow() => _time.GetUtcNow().UtcDateTime;
}
