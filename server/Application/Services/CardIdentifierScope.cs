using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The board-scope walk CARD-0040's binder and CARD-0218's card-id resolver share. Identifiers
/// are unique per BOARD, not globally, so a short name is resolved narrowest-first and uniqueness
/// is demanded inside the scope that answers — never a silent first row.
/// </summary>
internal sealed record CardScopeContext(
    Guid? ExplicitBoardId,
    Guid? InheritedCardId,
    Guid? CallerSessionId,
    string? Directory)
{
    public static readonly CardScopeContext None = new(null, null, null, null);
}

internal sealed record CardMatch(
    Guid Id,
    string Identifier,
    string Title,
    CardStatus Status,
    Guid BoardId,
    string BoardName);

internal sealed record CardScopeResult(
    CardMatch? Match,
    IReadOnlyList<CardMatch> Candidates,
    string ScopeName);

internal static class CardIdentifierScope
{
    public const string AmbiguousCode = "card_identifier_ambiguous";

    public static async Task<CardScopeResult> ResolveAsync(
        AppDbContext db, string canonical, CardScopeContext ctx, CancellationToken ct)
    {
        // Explicit board is a fence, not a hint: the caller named the board and we never
        // fall through to a different one.
        if (ctx.ExplicitBoardId is Guid boardId)
        {
            var onBoard = await MatchesAsync(db, canonical, [boardId], ct);
            if (onBoard.Count == 1)
                return new CardScopeResult(onBoard[0], onBoard, "board");

            var elsewhere = await MatchesAsync(db, canonical, boardIds: null, ct);
            return new CardScopeResult(null, elsewhere, "board");
        }

        var scopeA = await CallerBoardsAsync(db, ctx, ct);
        if (scopeA.Count > 0)
        {
            var inScope = await MatchesAsync(db, canonical, scopeA, ct);
            if (inScope.Count == 1)
                return new CardScopeResult(inScope[0], inScope, "caller");
        }

        var scopeB = await RepositoryBoardsAsync(db, ctx, ct);
        if (scopeB.Count > 0)
        {
            var inScope = await MatchesAsync(db, canonical, scopeB, ct);
            if (inScope.Count == 1)
                return new CardScopeResult(inScope[0], inScope, "repository");
            if (inScope.Count > 1)
                return new CardScopeResult(null, inScope, "repository");
        }

        var everywhere = await MatchesAsync(db, canonical, boardIds: null, ct);
        return everywhere.Count switch
        {
            1 => new CardScopeResult(everywhere[0], everywhere, "all"),
            0 => new CardScopeResult(null, [], "all"),
            _ => new CardScopeResult(null, everywhere, "all"),
        };
    }

    /// <summary>
    /// The one sentence both the card API's 409 and the binder's Warning event print.
    /// Titles are truncated at 60 characters here; the problem-details extension carries them whole.
    /// </summary>
    public static string DescribeCandidates(string canonical, IReadOnlyList<CardMatch> candidates)
    {
        var ordered = candidates
            .OrderBy(m => m.BoardName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var lines = new System.Text.StringBuilder();
        lines.Append($"Card identifier '{canonical}' matches {ordered.Count} cards:");
        foreach (var match in ordered)
        {
            var title = match.Title.Length <= 60 ? match.Title : match.Title[..59] + "…";
            lines.AppendLine();
            lines.Append($"  {match.BoardName}  {match.Id}  {match.Status}  \"{title}\"");
        }

        lines.AppendLine();
        lines.Append("Pass -Board <name|guid>, use the card's guid, or run from the project's checkout.");
        return lines.ToString();
    }

    private static async Task<List<CardMatch>> MatchesAsync(
        AppDbContext db, string canonical, IReadOnlyCollection<Guid>? boardIds, CancellationToken ct)
    {
        var query = db.Cards.AsNoTracking().Where(c => c.Identifier == canonical);
        if (boardIds is not null)
            query = query.Where(c => boardIds.Contains(c.BoardId));

        return await query
            .Join(
                db.Boards.AsNoTracking(),
                c => c.BoardId,
                b => b.Id,
                (c, b) => new CardMatch(c.Id, c.Identifier, c.Title, c.Status, c.BoardId, b.Name))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Scope A: boards this caller demonstrably works on — the inherited card's board, the calling
    /// session's card's board, and the standing agent that owns the calling session.
    /// </summary>
    private static async Task<List<Guid>> CallerBoardsAsync(
        AppDbContext db, CardScopeContext ctx, CancellationToken ct)
    {
        var boards = new List<Guid>();

        if (ctx.InheritedCardId is Guid inheritedId)
        {
            var boardId = await db.Cards.AsNoTracking()
                .Where(c => c.Id == inheritedId).Select(c => (Guid?)c.BoardId).FirstOrDefaultAsync(ct);
            if (boardId is Guid inheritedBoard)
                boards.Add(inheritedBoard);
        }

        if (ctx.CallerSessionId is Guid sessionId)
        {
            var sessionBoard = await (
                from session in db.AgentSessions.AsNoTracking()
                join card in db.Cards.AsNoTracking() on session.CardId equals card.Id
                where session.Id == sessionId
                select (Guid?)card.BoardId).FirstOrDefaultAsync(ct);
            if (sessionBoard is Guid fromSession && !boards.Contains(fromSession))
                boards.Add(fromSession);

            // The same join DeriveCallerProjectAsync already makes: a standing agent claims its
            // session by id, and its board is the board its orchestrator works.
            var persistentSessionId = sessionId.ToString("D");
            var agentBoard = await db.Agents.AsNoTracking()
                .Where(a => a.PersistentSessionId == persistentSessionId && a.BoardId != null)
                .Select(a => a.BoardId)
                .FirstOrDefaultAsync(ct);
            if (agentBoard is Guid fromAgent && !boards.Contains(fromAgent))
                boards.Add(fromAgent);
        }

        return boards;
    }

    /// <summary>
    /// Scope B: boards of every project whose local checkout contains this task's repository. Path
    /// matching is separator- and case-insensitive through the resolver's own rule, because the
    /// live rows spell the same tree both ways (<c>C:/src/Antiphon</c> vs <c>C:\src\Antiphon</c>).
    /// </summary>
    private static async Task<List<Guid>> RepositoryBoardsAsync(
        AppDbContext db, CardScopeContext ctx, CancellationToken ct)
    {
        var path = ctx.Directory;
        if (string.IsNullOrWhiteSpace(path))
            return [];

        var projects = await db.Projects.AsNoTracking()
            .Where(p => p.LocalRepositoryPath != null)
            .Select(p => new { p.Id, p.LocalRepositoryPath })
            .ToListAsync(ct);

        var projectIds = projects
            .Where(p => DelegationWorkspaceResolver.IsWithinRoot(path, p.LocalRepositoryPath!))
            .Select(p => p.Id)
            .ToList();
        if (projectIds.Count == 0)
            return [];

        return await db.Boards.AsNoTracking()
            .Where(b => projectIds.Contains(b.ProjectId))
            .Select(b => b.Id)
            .ToListAsync(ct);
    }
}
