using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// The projection behind <c>GET /api/cards/{id}/thread</c>: one card's work, gathered from the four
/// separate places it is recorded — the card row, the plan files in git, the delegated tasks, and
/// the commits.
///
/// <para><b>The insight it rests on.</b> There is no foreign key between <see cref="Card"/> and
/// <see cref="AgentTask"/>, deliberately. What there IS, already, everywhere, is the identifier as
/// a citation: task titles start "CARD-0067 - …", commit messages read
/// <c>fix(channels): CARD-0067 - …</c>, spec filenames read <c>…-card-0035-…</c>. This service makes
/// that existing convention pay rent instead of adding a column that restates it.</para>
///
/// <para><b>Which is why every match is boundary-guarded.</b> A text join has one failure mode and
/// it is silent: <c>CARD-0067</c> is a substring of <c>CARD-00670</c> and <c>#5</c> of <c>#51</c>,
/// so a plain LIKE would attach another card's tasks to this thread with nothing to catch it. The
/// database narrows with ILIKE — that is all an index-free contains can do — and the verdict is
/// then re-taken in memory against an anchored pattern. The git side does the same in the pattern
/// it hands to <c>--grep</c>.</para>
///
/// <para><b>Read-only, and degrading honestly.</b> Every query is <c>AsNoTracking</c>; nothing here
/// writes. A card whose worktree has been deleted and whose project names no local repository has
/// no repo to ask, so the response says <c>reposConsulted: false</c> rather than reporting "no
/// plans, no commits" — the same distinction <c>AttentionDto.RunnerConsulted</c> draws, and for the
/// same reason: a confident empty answer over a broken lookup is worse than no answer.</para>
/// </summary>
public sealed class CardThreadService
{
    /// <summary>
    /// Commits shown per thread. A cap rather than everything: this is the traceability view, not
    /// the review view, and the phone it is designed for scrolls.
    /// </summary>
    private const int CommitLimit = 50;

    /// <summary>Enough of a check reading to decide from; the full history is a per-task query.</summary>
    private const int CheckTextChars = 600;

    private const int CheckDigestTailLines = 6;

    private readonly AppDbContext _db;
    private readonly PlanCatalogService _plans;
    private readonly GitWorkspaceService _git;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CardThreadService> _logger;
    private readonly ContextWindowSettings _contextWindow;

    public CardThreadService(
        AppDbContext db,
        PlanCatalogService plans,
        GitWorkspaceService git,
        TimeProvider timeProvider,
        ILogger<CardThreadService> logger,
        IOptions<ContextWindowSettings>? contextWindow = null)
    {
        _db = db;
        _plans = plans;
        _git = git;
        _timeProvider = timeProvider;
        _logger = logger;
        _contextWindow = contextWindow?.Value ?? new ContextWindowSettings();
    }

    public async Task<CardThreadDto> GetAsync(Guid cardId, CancellationToken ct)
    {
        var card = await _db.Cards
            .AsNoTracking()
            .Include(c => c.AgentSessions)
            .Include(c => c.AssignedAgent)
            .Include(c => c.ActiveWorkflowRun)!.ThenInclude(r => r!.CurrentStage)
            .Include(c => c.CurrentWorktree)
            .Include(c => c.Board).ThenInclude(b => b.Project)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);

        var identifier = card.Identifier;
        var citation = CitationPattern(identifier);

        var tasks = await LoadTasksAsync(identifier, citation, ct);

        // Worktree first: while a card is being worked, its plans and commits live in the throwaway
        // checkout, and the shared one has not seen them yet.
        var root = ResolveRepoRoot(card);

        IReadOnlyList<CardThreadPlanDto> plans = [];
        IReadOnlyList<CardThreadCommitDto> commits = [];
        var consulted = false;

        if (root is not null)
        {
            var catalog = await _plans.ListAsync(root, ct);
            plans = [.. catalog.Plans
                .Select(p => new CardThreadPlanDto(
                    p,
                    Subject: p.Cards.Contains(identifier, StringComparer.OrdinalIgnoreCase)))
                .Where(p => p.Subject
                    || p.Plan.MentionedCards.Contains(identifier, StringComparer.OrdinalIgnoreCase))
                .OrderByDescending(p => p.Subject)
                .ThenByDescending(p => p.Plan.Date ?? DateOnly.MinValue)];

            // Null from git is "the question could not be asked" — a directory that is not a
            // repository, or a git that failed. An empty list is "nothing cites this card". The
            // flag keeps those apart; collapsing them is how a view reports a clean slate over a
            // broken lookup.
            var log = await _git.ListCommitsByGrepAsync(root, identifier, CommitLimit, ct);
            if (log is null)
            {
                _logger.LogDebug("git could not be asked about {Identifier} in {Root}", identifier, root);
            }
            else
            {
                commits = [.. log.Select(c =>
                    new CardThreadCommitDto(c.Sha, c.ShortSha, c.Author, c.Date, c.Subject))];
                consulted = true;
            }
        }

        var cardDto = await SessionContextUsage.AttachToCardAsync(
            _db, BoardService.ToCardDto(card), _contextWindow, _logger, ct);
        return new CardThreadDto(
            cardDto,
            identifier,
            consulted ? root : null,
            consulted,
            _timeProvider.GetUtcNow().UtcDateTime,
            plans,
            tasks,
            commits);
    }

    /// <summary>
    /// The card's own checkout, else the project's. Null when neither exists on this machine, which
    /// is what turns <c>reposConsulted</c> off rather than producing an empty commit list.
    /// </summary>
    private static string? ResolveRepoRoot(Card card)
    {
        var worktree = card.CurrentWorktree?.Path;
        if (!string.IsNullOrWhiteSpace(worktree) && Directory.Exists(worktree))
            return worktree;

        var project = card.Board?.Project?.LocalRepositoryPath;
        return !string.IsNullOrWhiteSpace(project) && Directory.Exists(project) ? project : null;
    }

    private async Task<IReadOnlyList<CardThreadTaskDto>> LoadTasksAsync(
        string identifier, Regex citation, CancellationToken ct)
    {
        // ILIKE narrows in the database — a contains is all an unindexed text match can do — and
        // the boundary guard below re-takes the verdict. Doing only the ILIKE would put CARD-00670's
        // tasks on CARD-0067's thread with nothing anywhere to notice.
        var patterns = LikePatterns(identifier);
        var first = $"%{patterns[0]}%";
        var second = patterns.Count > 1 ? $"%{patterns[1]}%" : null;
        var third = patterns.Count > 2 ? $"%{patterns[2]}%" : null;

        var candidates = await _db.AgentTasks.AsNoTracking()
            .Where(t =>
                EF.Functions.ILike(t.Title, first) || EF.Functions.ILike(t.Goal, first)
                || (second != null
                    && (EF.Functions.ILike(t.Title, second) || EF.Functions.ILike(t.Goal, second)))
                || (third != null
                    && (EF.Functions.ILike(t.Title, third) || EF.Functions.ILike(t.Goal, third))))
            .ToListAsync(ct);
        var matched = candidates
            .Select(t => (Task: t, Where: MatchedOn(t, citation)))
            .Where(m => m.Where is not null)
            .OrderByDescending(m => m.Task.CreatedAt)
            .ToList();

        if (matched.Count == 0)
            return [];

        var costs = await LoadSubtreeCostsAsync([.. matched.Select(m => m.Task)], ct);
        var checks = await LoadLatestChecksAsync([.. matched.Select(m => m.Task.Id)], ct);

        return
        [
            .. matched.Select(m => new CardThreadTaskDto(
                m.Task.Id,
                m.Task.Title,
                m.Task.Status,
                m.Task.Kind,
                m.Task.AgentKind,
                m.Task.ModelLevel,
                m.Task.AgentName,
                m.Task.AgentSessionId,
                m.Task.CreatedAt,
                m.Task.DispatchedAt,
                m.Task.CompletedAt,
                m.Task.NextCheckAt,
                m.Task.CheckCount,
                m.Task.CostUsd,
                costs.GetValueOrDefault(m.Task.Id, m.Task.CostUsd),
                m.Where!,
                checks.GetValueOrDefault(m.Task.Id),
                m.Task.Result,
                m.Task.ResultFilePath,
                m.Task.FailureReason)),
        ];
    }

    private static string? MatchedOn(AgentTask task, Regex citation)
    {
        var inTitle = citation.IsMatch(task.Title ?? string.Empty);
        var inGoal = citation.IsMatch(task.Goal ?? string.Empty);
        return (inTitle, inGoal) switch
        {
            (true, true) => "both",
            (true, false) => "title",
            (false, true) => "goal",
            _ => null,
        };
    }

    /// <summary>
    /// The written forms of one card identifier: the canonical <c>CARD-0067</c>, the unpadded
    /// <c>CARD-67</c>, and the display form <c>#67</c> that CARD-0042 made a stable citation. A
    /// foreign tracker's identifier (<c>PROJ-12</c>) contributes only itself — <c>#12</c> would not
    /// mean it.
    /// </summary>
    internal static IReadOnlyList<string> LikePatterns(string identifier)
    {
        var patterns = new List<string> { identifier };
        var ours = OurIdentifier.Match(identifier);
        if (!ours.Success || !int.TryParse(ours.Groups["n"].Value, out var number))
            return patterns;

        patterns.Add($"CARD-{number}");
        patterns.Add($"#{number}");
        return patterns.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// The same identifier forms, anchored. The trailing <c>(?![0-9])</c> is the whole guard: it is
    /// what stops <c>#5</c> matching <c>#51</c> and <c>CARD-0067</c> matching <c>CARD-00670</c>.
    /// </summary>
    internal static Regex CitationPattern(string identifier)
    {
        var alternatives = string.Join('|', LikePatterns(identifier).Select(Regex.Escape));
        return new Regex(
            $@"(?<![A-Za-z0-9])({alternatives})(?![0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static readonly Regex OurIdentifier =
        new(@"^CARD-(?<n>\d{1,4})$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Rolled-up spend per task. Reuses <see cref="AgentTaskService.IsDescendantOf"/> for the same
    /// reason <c>AttentionService</c> does: two answers to "what has this run cost" would eventually
    /// disagree, and the board's figure is the one an operator has already learned to read.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadSubtreeCostsAsync(
        IReadOnlyList<AgentTask> subjects, CancellationToken ct)
    {
        var costs = new Dictionary<Guid, decimal>();
        if (subjects.Count == 0)
            return costs;

        var rootIds = subjects.Select(t => t.RootTaskId).Distinct().ToList();
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => rootIds.Contains(t.RootTaskId))
            .ToListAsync(ct);
        var byRoot = family.GroupBy(t => t.RootTaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AgentTask>)[.. g]);

        foreach (var task in subjects)
        {
            if (costs.ContainsKey(task.Id))
                continue;
            var run = byRoot.GetValueOrDefault(task.RootTaskId) ?? [];
            var total = task.CostUsd;
            foreach (var other in run)
            {
                if (other.Id != task.Id && AgentTaskService.IsDescendantOf(other, task.Id, run))
                    total += other.CostUsd;
            }
            costs[task.Id] = total;
        }
        return costs;
    }

    /// <summary>
    /// The latest check reading per task: the interpreter's reading when CARD-0035 slice 5 stamped
    /// one, the digest tail otherwise. Absence of a reading is not a degradation to report — a check
    /// that ran before that slice, or whose interpreter was busy, simply falls back to the digest,
    /// and the DTO says which of the two the client is looking at.
    /// </summary>
    private async Task<Dictionary<Guid, CardThreadCheckDto>> LoadLatestChecksAsync(
        IReadOnlyList<Guid> taskIds, CancellationToken ct)
    {
        var readings = new Dictionary<Guid, CardThreadCheckDto>();
        if (taskIds.Count == 0)
            return readings;

        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => taskIds.Contains(e.AgentTaskId) && e.Type == AgentTaskEventType.Check)
            .Select(e => new { e.AgentTaskId, e.Detail, e.At })
            .ToListAsync(ct);

        foreach (var group in events.GroupBy(e => e.AgentTaskId))
        {
            var latest = group.OrderByDescending(e => e.At).First();
            if (AgentTaskCheckService.TryReadInterpretation(latest.Detail) is { } reading)
            {
                readings[group.Key] = new CardThreadCheckDto(
                    Clamp(reading, CheckTextChars), FromInterpreter: true, latest.At);
                continue;
            }

            var tail = Tail(latest.Detail, CheckDigestTailLines);
            if (tail.Length > 0)
                readings[group.Key] = new CardThreadCheckDto(tail, FromInterpreter: false, latest.At);
        }
        return readings;
    }

    private static string Clamp(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max].TrimEnd() + "…";
    }

    /// <summary>The last few lines of a digest — where the probe puts what it concluded.</summary>
    private static string Tail(string? detail, int lines)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return string.Empty;
        var all = detail.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Clamp(string.Join('\n', all.TakeLast(lines)), CheckTextChars);
    }
}
