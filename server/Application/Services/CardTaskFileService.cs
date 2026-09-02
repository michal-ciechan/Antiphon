using System.Text;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0004: load a board's cards, render them into <c>docs/cards/&lt;slug&gt;/</c>, and
/// reconcile that directory (write / delete / leave). The path-scoped commit lands in S2; until
/// then every successful write reports <c>CommitSkipReason</c> <c>autocommit_disabled</c>, which
/// is also the production default of <see cref="CardFileSyncSettings.AutoCommit"/>.
/// </summary>
public sealed class CardTaskFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly AppDbContext _db;
    private readonly CardTaskFileSyncGate _gate;
    private readonly GitWorkspaceService _git;
    private readonly ILogger<CardTaskFileService> _logger;

    public CardTaskFileService(
        AppDbContext db,
        CardTaskFileSyncGate gate,
        GitWorkspaceService git,
        ILogger<CardTaskFileService> logger,
        IOptions<CardFileSyncSettings>? settings = null)
    {
        _db = db;
        _gate = gate;
        _git = git;
        _logger = logger;
        _ = settings;
    }

    public async Task<CardFileSyncBoardResult> SyncBoardAsync(
        Guid boardId, bool dryRun = false, CancellationToken ct = default)
    {
        var board = await _db.Boards.AsNoTracking()
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == boardId, ct)
            ?? throw new NotFoundException(nameof(Board), boardId);

        if (board.ArchivedAt is not null)
            return Skip(board, "board_archived", dryRun, _logger);
        if (board.Project.ArchivedAt is not null)
            return Skip(board, "project_archived", dryRun, _logger);

        var localPath = board.Project.LocalRepositoryPath;
        if (string.IsNullOrWhiteSpace(localPath))
            return Skip(board, "no_repository_path", dryRun, _logger);

        var repoPath = Path.GetFullPath(localPath);
        if (!Directory.Exists(repoPath) || !await _git.IsRepositoryAsync(repoPath, ct))
        {
            _gate.NoteSkipReason(repoPath, "not_a_git_repository");
            return Skip(board, "not_a_git_repository", dryRun, _logger);
        }

        var cards = await _db.Cards.AsNoTracking()
            .Include(c => c.ExternalIssueRef)
            .Where(c => c.BoardId == board.Id)
            .ToListAsync(ct);
        if (cards.Count == 0)
            return Skip(board, "no_cards", dryRun, _logger);

        var lease = await _gate.TryEnterAsync(repoPath, ct);
        if (lease is null)
            throw new ConflictException("Card file sync is already running for this repository.", "card_file_sync_running");

        using (lease)
        {
            var slug = await UniqueBoardSlugAsync(board, ct);
            var relativeDir = $"{CardTaskFileRenderer.CardsRoot}/{slug}";
            var absoluteDir = Path.GetFullPath(Path.Combine(repoPath, "docs", "cards", slug));

            var fileNames = cards.ToDictionary(
                c => c.Id,
                c => CardTaskFileRenderer.CardFileName(c.Identifier, c.Title));
            var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
                desired[fileNames[card.Id]] = CardTaskFileRenderer.RenderCard(card);
            desired[CardTaskFileRenderer.IndexFileName] =
                CardTaskFileRenderer.RenderIndex(board.Name, cards, fileNames);

            var existing = Directory.Exists(absoluteDir)
                ? Directory.GetFiles(absoluteDir, "*.md", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName)
                    .Where(n => n is not null)
                    .Cast<string>()
                    .ToList()
                : [];

            var written = 0;
            var unchanged = 0;
            var deleted = 0;

            foreach (var (name, content) in desired)
            {
                var path = Path.Combine(absoluteDir, name);
                if (File.Exists(path) && await File.ReadAllTextAsync(path, ct) == content)
                {
                    unchanged++;
                    continue;
                }

                written++;
                if (dryRun)
                    continue;
                Directory.CreateDirectory(absoluteDir);
                await File.WriteAllTextAsync(path, content, Utf8NoBom, ct);
            }

            foreach (var name in existing)
            {
                if (desired.ContainsKey(name))
                    continue;
                deleted++;
                if (dryRun)
                    continue;
                File.Delete(Path.Combine(absoluteDir, name));
            }

            // S1: the path-scoped commit is S2. AutoCommit defaults false in production anyway.
            var commitSkip = dryRun ? "dry_run" : "autocommit_disabled";
            if (!dryRun)
                _gate.NoteSkipReason(repoPath, commitSkip);

            return new CardFileSyncBoardResult(
                board.Id,
                board.Name,
                relativeDir,
                written,
                deleted,
                unchanged,
                CommitSha: null,
                WriteSkipReason: null,
                CommitSkipReason: commitSkip,
                Error: null,
                dryRun);
        }
    }

    private async Task<string> UniqueBoardSlugAsync(Board board, CancellationToken ct)
    {
        var baseSlug = CardTaskFileRenderer.BoardSlug(board.Name);
        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = "board";

        var siblings = await _db.Boards.AsNoTracking()
            .Where(b => b.ProjectId == board.ProjectId)
            .Select(b => new { b.Id, b.Name, b.CreatedAt })
            .ToListAsync(ct);

        static string RawSlug(string name)
        {
            var slug = CardTaskFileRenderer.BoardSlug(name);
            return string.IsNullOrEmpty(slug) ? "board" : slug;
        }

        var colliding = siblings
            .Where(b => string.Equals(RawSlug(b.Name), baseSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToList();

        if (colliding.Count <= 1 || colliding[0].Id == board.Id)
            return baseSlug;

        var suffix = $"-{board.Id.ToString("N")[..8]}";
        var maxBase = Math.Max(1, CardTaskFileRenderer.SlugMaxLength - suffix.Length);
        var trimmed = baseSlug.Length <= maxBase ? baseSlug : baseSlug[..maxBase].Trim('-');
        return trimmed + suffix;
    }

    private static CardFileSyncBoardResult Skip(
        Board board, string reason, bool dryRun, ILogger logger)
    {
        logger.LogDebug("Card file sync skipped for board {BoardId}: {Reason}", board.Id, reason);
        return new(
            board.Id,
            board.Name,
            Directory: null,
            Written: 0,
            Deleted: 0,
            Unchanged: 0,
            CommitSha: null,
            WriteSkipReason: reason,
            CommitSkipReason: null,
            Error: null,
            dryRun);
    }
}
