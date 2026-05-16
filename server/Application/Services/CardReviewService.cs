using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class CardReviewService
{
    private readonly AppDbContext _db;
    private readonly IGitService _gitService;
    private readonly IGitHubService _gitHubService;
    private readonly AgentChannelService _agentChannelService;
    private readonly GithubSettings _githubSettings;
    private readonly ILogger<CardReviewService> _logger;

    public CardReviewService(
        AppDbContext db,
        IGitService gitService,
        IGitHubService gitHubService,
        AgentChannelService agentChannelService,
        IOptions<GithubSettings> githubSettings,
        ILogger<CardReviewService> logger)
    {
        _db = db;
        _gitService = gitService;
        _gitHubService = gitHubService;
        _agentChannelService = agentChannelService;
        _githubSettings = githubSettings.Value;
        _logger = logger;
    }

    public async Task<BranchDiffDto> GetDiffAsync(Guid cardId, CancellationToken ct)
    {
        var card = await LoadCardAsync(cardId, ct);
        var worktree = RequireCurrentWorktree(card);
        var baseRef = ResolveBaseRef(card, worktree);

        string rawDiff;
        try
        {
            rawDiff = await _gitService.GetWorktreeDiffAsync(baseRef, worktree.Path, ct);
        }
        catch (InvalidOperationException ex)
        {
            throw new ConflictException($"Failed to compute card diff: {ex.Message}");
        }

        return new BranchDiffDto(
            baseRef,
            worktree.Branch,
            UnifiedDiffParser.Parse(rawDiff),
            PrNumber: null,
            PrUrl: null,
            PrTitle: null,
            PrState: null);
    }

    public async Task<CardCommentResult> PostCommentAsync(
        Guid cardId,
        CardCommentRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ValidationException(nameof(request.Message), "Review comment must not be empty.");

        if (request.Line is <= 0)
            throw new ValidationException(nameof(request.Line), "Review comment line must be positive.");
        if (NormalizeCommentSide(request.Side) is null && !string.IsNullOrWhiteSpace(request.Side))
            throw new ValidationException(nameof(request.Side), "Review comment side must be old, new, or context.");

        var card = await LoadCardAsync(cardId, ct);
        var target = FindActiveSession(card)
            ?? throw new ConflictException($"Card '{card.Identifier}' has no running session to receive review comments.");

        var message = FormatReviewComment(card, request);
        await _agentChannelService.SendToSessionAsync(
            sourceSessionId: null,
            target.Id,
            message,
            ct);

        return new CardCommentResult(card.Id, target.Id, message);
    }

    public async Task<CardPullRequestResult> OpenPullRequestAsync(Guid cardId, CancellationToken ct)
    {
        var card = await LoadCardAsync(cardId, ct);
        var worktree = RequireCurrentWorktree(card);
        var project = card.Board.Project;
        var baseRef = ResolveBaseRef(card, worktree);

        if (!_githubSettings.Enabled)
            throw new ConflictException("GitHub integration is disabled globally.");

        if (!project.GitHubIntegrationEnabled)
            throw new ConflictException($"GitHub integration is not enabled for project '{project.Name}'.");

        var ownerRepo = ParseOwnerRepo(project.GitRepositoryUrl)
            ?? throw new ValidationException(nameof(project.GitRepositoryUrl), "Project git repository URL must identify a GitHub owner/repo.");

        var title = $"{card.Identifier}: {card.Title}";
        var lastAttempt = card.RunAttempts
            .OrderByDescending(a => a.AttemptNumber)
            .FirstOrDefault();
        var body = BuildPullRequestBody(card, lastAttempt);

        try
        {
            await _gitService.CommitAllChangesAsync(worktree.Path, title, ct);
            await _gitHubService.PushBranchAsync(worktree.Path, worktree.Branch, ct);
            var existing = await _gitHubService.FindPullRequestForBranchAsync(
                ownerRepo.Owner,
                ownerRepo.Repo,
                worktree.Branch,
                ct);
            if (existing is not null)
            {
                return new CardPullRequestResult(
                    card.Id,
                    existing.Number,
                    ownerRepo.Owner,
                    ownerRepo.Repo,
                    worktree.Branch,
                    existing.BaseBranch,
                    existing.HtmlUrl,
                    existing.State,
                    Created: false);
            }

            var prNumber = await _gitHubService.CreatePullRequestAsync(
                ownerRepo.Owner,
                ownerRepo.Repo,
                worktree.Branch,
                baseRef,
                title,
                body,
                ct);

            return new CardPullRequestResult(
                card.Id,
                prNumber,
                ownerRepo.Owner,
                ownerRepo.Repo,
                worktree.Branch,
                baseRef,
                BuildPullRequestUrl(ownerRepo.Owner, ownerRepo.Repo, prNumber),
                PrState: "open",
                Created: true);
        }
        catch (Exception ex) when (ex is not HttpException and not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to open pull request for card {CardId}", card.Id);
            throw new ConflictException($"Failed to open pull request: {ex.Message}");
        }
    }

    internal static string BuildPullRequestBody(Card card, RunAttempt? lastAttempt)
    {
        var body = new StringBuilder();
        body.AppendLine($"## {card.Identifier}: {card.Title}");
        body.AppendLine();
        if (!string.IsNullOrWhiteSpace(card.Description))
        {
            body.AppendLine("### Card Description");
            body.AppendLine(card.Description.Trim());
            body.AppendLine();
        }

        body.AppendLine("### Last Attempt Summary");
        if (lastAttempt is null)
        {
            body.AppendLine("No run attempts were recorded for this card.");
            return body.ToString();
        }

        body.AppendLine($"- Attempt: {lastAttempt.AttemptNumber}");
        body.AppendLine($"- Phase: {lastAttempt.Phase}");
        body.AppendLine($"- Started: {lastAttempt.StartedAt:O}");
        if (lastAttempt.CompletedAt is DateTime completedAt)
            body.AppendLine($"- Completed: {completedAt:O}");
        if (lastAttempt.ExitCode is int exitCode)
            body.AppendLine($"- Exit code: {exitCode}");
        if (!string.IsNullOrWhiteSpace(lastAttempt.ErrorDetails))
            body.AppendLine($"- Error: {lastAttempt.ErrorDetails.Trim()}");

        return body.ToString();
    }

    internal static (string Owner, string Repo)? ParseOwnerRepo(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            return null;

        if (gitUrl.StartsWith("git@", StringComparison.OrdinalIgnoreCase))
        {
            var separator = gitUrl.IndexOf(':', StringComparison.Ordinal);
            if (separator >= 0)
                return ParseOwnerRepoPath(gitUrl[(separator + 1)..]);
        }

        if (Uri.TryCreate(gitUrl, UriKind.Absolute, out var uri))
            return ParseOwnerRepoPath(uri.AbsolutePath.Trim('/'));

        return ParseOwnerRepoPath(gitUrl.Trim());
    }

    private async Task<Card> LoadCardAsync(Guid cardId, CancellationToken ct)
    {
        return await _db.Cards
            .Include(c => c.Board).ThenInclude(b => b.Project)
            .Include(c => c.CurrentWorktree)
            .Include(c => c.AgentSessions)
            .Include(c => c.RunAttempts)
            .FirstOrDefaultAsync(c => c.Id == cardId, ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
    }

    private static Worktree RequireCurrentWorktree(Card card) =>
        card.CurrentWorktree
        ?? throw new ConflictException($"Card '{card.Identifier}' has no current worktree.");

    private static string ResolveBaseRef(Card card, Worktree worktree) =>
        string.IsNullOrWhiteSpace(worktree.BaseRef)
            ? card.Board.Project.BaseBranch
            : worktree.BaseRef;

    private static AgentSession? FindActiveSession(Card card) =>
        card.AgentSessions
            .Where(s => s.Status == SessionStatus.Running)
            .OrderByDescending(s => s.Id == card.OwnerSessionId)
            .ThenByDescending(s => s.StartedAt)
            .FirstOrDefault();

    private static string FormatReviewComment(Card card, CardCommentRequest request)
    {
        var location = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(request.FilePath))
            location.Append($"File: {request.FilePath.Trim()}");
        if (request.Line is int line)
        {
            var side = NormalizeCommentSide(request.Side);
            var lineText = side is null ? $"line {line}" : $"{side} line {line}";
            location.Append(location.Length == 0 ? $"{char.ToUpperInvariant(lineText[0])}{lineText[1..]}" : $" {lineText}");
        }

        return location.Length == 0
            ? $"Review comment for {card.Identifier}: {request.Message.Trim()}"
            : $"Review comment for {card.Identifier} ({location}): {request.Message.Trim()}";
    }

    private static string? NormalizeCommentSide(string? side)
    {
        if (string.IsNullOrWhiteSpace(side))
            return null;

        var normalized = side.Trim().ToLowerInvariant();
        return normalized is "old" or "new" or "context" ? normalized : null;
    }

    private static string BuildPullRequestUrl(string owner, string repo, int prNumber) =>
        $"https://github.com/{owner}/{repo}/pull/{prNumber}";

    private static (string Owner, string Repo)? ParseOwnerRepoPath(string path)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return null;

        var owner = segments[^2];
        var repo = segments[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[^1][..^4]
            : segments[^1];

        return string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo)
            ? null
            : (owner, repo);
    }
}
