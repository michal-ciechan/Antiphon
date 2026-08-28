using System.Text.RegularExpressions;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>The card a task was bound to, and what the caller should hear about how (CARD-0040).</summary>
/// <param name="CardId">Null when nothing bound — normal, and never an error for a derived binding.</param>
/// <param name="Identifier">The bound card's identifier, for the creation response and the log line.</param>
/// <param name="Warning">
/// Recorded as a <see cref="AgentTaskEventType.Warning"/> event AND returned to the caller at
/// creation: a mis-bind is worth a sentence at dispatch, not a discovery on the board a week later.
/// </param>
internal sealed record AgentTaskCardBinding(Guid? CardId, string? Identifier, string? Warning)
{
    public static readonly AgentTaskCardBinding None = new(null, null, null);
}

/// <summary>
/// Works out which card a delegated task is about (CARD-0040 §2.1). The convention already existed
/// in prose — 397 of 627 live task titles begin with <c>CARD-nnnn</c> — and this is what turns it
/// into a durable row the transition sweep can act on.
/// </summary>
/// <remarks>
/// Static over an <see cref="AppDbContext"/> rather than an injected service: <c>AgentTaskService</c>
/// is constructed by hand in a dozen test harnesses, and a new constructor argument would be a
/// mechanical edit to every one of them for no behavioural gain.
///
/// <para>Identifiers are unique per BOARD, not globally (<c>IX_Cards_BoardId_Identifier</c>): two
/// boards on this deployment both hold CARD-0001…0011. So resolution walks scopes narrowest-first
/// and demands uniqueness INSIDE the scope that answers — ambiguity binds nothing rather than
/// guessing, because a task moving the wrong project's card is worse than a task moving none.</para>
/// </remarks>
internal static class AgentTaskCardBinder
{
    /// <summary>
    /// The identifier shape as it appears in a title. Deliberately anchored on the literal
    /// <c>CARD-</c> prefix rather than <see cref="CardService.TryCanonicalIdentifier"/>'s full
    /// vocabulary: a title is prose, and treating a bare <c>#5</c> or the word "card 5" as a
    /// binding claim would bind on a sentence that merely mentions one (CARD-0175's rule).
    /// </summary>
    private static readonly Regex TitleIdentifier = new(
        @"\bCARD-0*(\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Every distinct canonical identifier named in <paramref name="text"/>, in title order.</summary>
    public static IReadOnlyList<string> IdentifiersIn(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var found = new List<string>();
        foreach (Match match in TitleIdentifier.Matches(text))
        {
            if (!int.TryParse(match.Groups[1].Value, out var number))
                continue;
            var canonical = $"CARD-{number:0000}";
            if (!found.Contains(canonical, StringComparer.Ordinal))
                found.Add(canonical);
        }

        return found;
    }

    /// <summary>What the caller is, for the purposes of scoping an identifier to a board.</summary>
    /// <param name="Role">Check rows are about a task, not a card, and never bind.</param>
    /// <param name="InheritedCardId">The parent / followed-up / conflicted task's card.</param>
    /// <param name="CallerSessionId">The calling session — its card, or its standing agent's board.</param>
    internal sealed record Context(
        AgentTaskRole Role,
        string Title,
        Guid? InheritedCardId,
        Guid? CallerSessionId,
        string? RepoPath,
        string WorkingDirectory);

    /// <summary>
    /// Resolve the binding. An EXPLICIT card that resolves to nothing throws
    /// <see cref="ValidationException"/> (422) — an explicit binding that silently fails is worse
    /// than no binding at all. Every derived route returns <see cref="AgentTaskCardBinding.None"/>
    /// with a warning instead.
    /// </summary>
    public static async Task<AgentTaskCardBinding> BindAsync(
        AppDbContext db, string? explicitCard, Context context, CancellationToken ct)
    {
        // Rule 4, checked first: a check-in interpretation is about a task. Binding one would put
        // a card into In Progress because somebody asked how another task was doing.
        if (context.Role == AgentTaskRole.Check)
            return AgentTaskCardBinding.None;

        if (!string.IsNullOrWhiteSpace(explicitCard))
            return await BindExplicitAsync(db, explicitCard.Trim(), context, ct);

        var titleIdentifiers = IdentifiersIn(context.Title);
        var inherited = context.InheritedCardId is Guid inheritedId
            ? await db.Cards.AsNoTracking()
                .Where(c => c.Id == inheritedId)
                .Select(c => new { c.Id, c.Identifier })
                .FirstOrDefaultAsync(ct)
            : null;

        // Rule 3 outranks rule 2 only when it actually resolves. A title naming a card nobody can
        // find is a typo, not an instruction to unbind the parent's card.
        if (titleIdentifiers.Count > 0)
        {
            var resolution = await ResolveAsync(db, titleIdentifiers[0], context, ct);
            var extras = titleIdentifiers.Count > 1
                ? $"Title names {string.Join(", ", titleIdentifiers.Skip(1))} as well; "
                  + $"bound to {titleIdentifiers[0]} — pass -Card to choose."
                : null;

            if (resolution.CardId is Guid titleCardId)
                return new AgentTaskCardBinding(titleCardId, resolution.Identifier, extras);

            var warning = Join(resolution.Warning, extras);
            return inherited is null
                ? new AgentTaskCardBinding(null, null, warning)
                : new AgentTaskCardBinding(inherited.Id, inherited.Identifier, warning);
        }

        return inherited is null
            ? AgentTaskCardBinding.None
            : new AgentTaskCardBinding(inherited.Id, inherited.Identifier, null);
    }

    private static async Task<AgentTaskCardBinding> BindExplicitAsync(
        AppDbContext db, string explicitCard, Context context, CancellationToken ct)
    {
        if (Guid.TryParse(explicitCard, out var cardGuid))
        {
            var byId = await db.Cards.AsNoTracking()
                .Where(c => c.Id == cardGuid)
                .Select(c => new { c.Id, c.Identifier })
                .FirstOrDefaultAsync(ct);
            return byId is null
                ? throw new ValidationException(
                    "card", $"No card with id {cardGuid} exists.")
                : new AgentTaskCardBinding(byId.Id, byId.Identifier, null);
        }

        var canonical = CardService.TryCanonicalIdentifier(explicitCard)
            ?? throw new ValidationException(
                "card",
                $"'{explicitCard}' is not a card identifier. Use CARD-0040, card-40, #40, 40, or the card's guid.");

        var resolution = await ResolveAsync(db, canonical, context, ct);
        return resolution.CardId is null
            ? throw new ValidationException(
                "card",
                resolution.Warning
                ?? $"No card {canonical} is visible from this task's caller, project or repository.")
            : new AgentTaskCardBinding(resolution.CardId, resolution.Identifier, null);
    }

    /// <summary>
    /// The narrowest scope that resolves wins; inside a scope the identifier must be unique or
    /// nothing binds. A scope that finds nothing simply falls through to the next. Walks the
    /// same <see cref="CardIdentifierScope"/> the card API uses (CARD-0218) so two spellings of
    /// "which card is CARD-51" cannot disagree.
    /// </summary>
    private static async Task<AgentTaskCardBinding> ResolveAsync(
        AppDbContext db, string canonical, Context context, CancellationToken ct)
    {
        var result = await CardIdentifierScope.ResolveAsync(
            db,
            canonical,
            new CardScopeContext(
                ExplicitBoardId: null,
                InheritedCardId: context.InheritedCardId,
                CallerSessionId: context.CallerSessionId,
                Directory: context.RepoPath ?? context.WorkingDirectory),
            ct);

        if (result.Match is { } match)
            return new AgentTaskCardBinding(match.Id, match.Identifier, null);

        if (result.Candidates.Count > 0)
            return new AgentTaskCardBinding(
                null, null, CardIdentifierScope.DescribeCandidates(canonical, result.Candidates));

        return new AgentTaskCardBinding(
            null, null, $"Identifier {canonical} matches no card on any board.");
    }

    private static string? Join(string? first, string? second) =>
        (first, second) switch
        {
            (null or "", null or "") => null,
            (null or "", _) => second,
            (_, null or "") => first,
            _ => first + " " + second,
        };
}
