using System.Diagnostics;
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
/// CARD-0004: load a board's cards, render them into <c>docs/cards/&lt;slug&gt;/</c>, reconcile
/// that directory (write / delete / leave), and path-scoped commit when
/// <see cref="CardFileSyncSettings.AutoCommit"/> is on. Production AutoCommit defaults false.
/// </summary>
public sealed class CardTaskFileService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly AppDbContext _db;
    private readonly CardTaskFileSyncGate _gate;
    private readonly GitWorkspaceService _git;
    private readonly GitProcessGate _processGate;
    private readonly CardFileSyncSettings _syncSettings;
    private readonly GitSettings _gitSettings;
    private readonly ILogger<CardTaskFileService> _logger;

    public CardTaskFileService(
        AppDbContext db,
        CardTaskFileSyncGate gate,
        GitWorkspaceService git,
        ILogger<CardTaskFileService> logger,
        IOptions<CardFileSyncSettings>? settings = null,
        GitProcessGate? processGate = null,
        IOptions<GitSettings>? gitSettings = null)
    {
        _db = db;
        _gate = gate;
        _git = git;
        _logger = logger;
        _syncSettings = settings?.Value ?? new CardFileSyncSettings();
        _processGate = processGate ?? new GitProcessGate();
        _gitSettings = gitSettings?.Value ?? new GitSettings();
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

            string? commitSha = null;
            string? commitSkip;
            string? error = null;
            if (dryRun)
            {
                commitSkip = "dry_run";
            }
            else
            {
                var commit = await CommitAsync(repoPath, absoluteDir, board.Name, ct);
                commitSha = commit.Sha;
                commitSkip = commit.SkipReason;
                error = commit.Error;
                _gate.NoteSkipReason(repoPath, commitSkip);
            }

            return new CardFileSyncBoardResult(
                board.Id,
                board.Name,
                relativeDir,
                written,
                deleted,
                unchanged,
                commitSha,
                WriteSkipReason: null,
                commitSkip,
                error,
                dryRun);
        }
    }

    /// <summary>
    /// Reconcile step 4: path-scoped add+commit of <paramref name="absoluteDir"/>, or a skip
    /// reason. Never add/commit without the pathspec; never push/stash/checkout/reset.
    /// </summary>
    private async Task<(string? Sha, string? SkipReason, string? Error)> CommitAsync(
        string repoPath, string absoluteDir, string boardName, CancellationToken ct)
    {
        if (!_syncSettings.AutoCommit)
            return (null, "autocommit_disabled", null);

        var status = await RunGitAsync(repoPath, ct, "status", "--porcelain", "--", absoluteDir);
        if (status.ExitCode != 0)
            return GitError(status);
        // Empty porcelain: the common tick. Do not git add (that would take index.lock).
        if (string.IsNullOrWhiteSpace(status.Stdout))
            return (null, "nothing_to_commit", null);

        var rebaseMerge = await GitPathExistsAsync(repoPath, "rebase-merge", ct);
        if (rebaseMerge.Error is not null)
            return (null, "git_error", rebaseMerge.Error);
        var rebaseApply = await GitPathExistsAsync(repoPath, "rebase-apply", ct);
        if (rebaseApply.Error is not null)
            return (null, "git_error", rebaseApply.Error);
        if (rebaseMerge.Exists || rebaseApply.Exists)
            return (null, "rebase_in_progress", null);

        var mergeHead = await GitPathExistsAsync(repoPath, "MERGE_HEAD", ct);
        if (mergeHead.Error is not null)
            return (null, "git_error", mergeHead.Error);
        if (mergeHead.Exists)
            return (null, "merge_in_progress", null);

        var cherryPick = await GitPathExistsAsync(repoPath, "CHERRY_PICK_HEAD", ct);
        if (cherryPick.Error is not null)
            return (null, "git_error", cherryPick.Error);
        if (cherryPick.Exists)
            return (null, "cherry_pick_in_progress", null);

        var symbolic = await RunGitAsync(repoPath, ct, "symbolic-ref", "-q", "HEAD");
        if (symbolic.ExitCode != 0)
            return (null, "detached_head", null);

        var unmerged = await RunGitAsync(
            repoPath, ct, "diff", "--name-only", "--diff-filter=U", "--", absoluteDir);
        if (unmerged.ExitCode != 0)
            return GitError(unmerged);
        if (!string.IsNullOrWhiteSpace(unmerged.Stdout))
            return (null, "conflicted_paths", null);

        var add = await RunGitAsync(repoPath, ct, "add", "-A", "--", absoluteDir);
        if (add.ExitCode != 0)
            return GitError(add);

        var message = $"antiphon: sync card files ({SanitizeSubject(boardName)})";
        var commit = await RunGitAsync(
            repoPath,
            ct,
            "commit",
            "--only",
            "-m",
            message,
            "--trailer",
            "antiphon=true",
            "--",
            absoluteDir);
        if (commit.ExitCode != 0)
            return GitError(commit);

        var head = await RunGitAsync(repoPath, ct, "rev-parse", "HEAD");
        if (head.ExitCode != 0 || string.IsNullOrWhiteSpace(head.Stdout))
            return GitError(head);

        return (head.Stdout.Trim(), null, null);
    }

    private async Task<(bool Exists, string? Error)> GitPathExistsAsync(
        string repoPath, string gitPath, CancellationToken ct)
    {
        var result = await RunGitAsync(repoPath, ct, "rev-parse", "--git-path", gitPath);
        if (result.ExitCode != 0)
            return (false, TrimError(result));

        var path = result.Stdout.Trim();
        if (path.Length == 0)
            return (false, null);

        var full = Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(repoPath, path));
        return (Directory.Exists(full) || File.Exists(full), null);
    }

    private async Task<GitCommandResult> RunGitAsync(
        string workingDirectory,
        CancellationToken ct,
        params string[] arguments)
    {
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _gitSettings.ExecutableName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var lease = await _processGate.EnterAsync(ct);
            process = Process.Start(psi);
            if (process is null)
                return new GitCommandResult(-1, "", $"{_gitSettings.ExecutableName} failed to start");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _gitSettings.TimeoutSeconds));
            timeoutCts.CancelAfter(timeout);
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);
            return new GitCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (ct.IsCancellationRequested)
                throw;

            _logger.LogWarning(
                "git {Args} timed out after {Timeout} in {Dir}; child killed",
                string.Join(' ', arguments),
                TimeSpan.FromSeconds(Math.Max(1, _gitSettings.TimeoutSeconds)),
                workingDirectory);
            return new GitCommandResult(-1, "", "timeout");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "git {Args} failed in {Dir}", string.Join(' ', arguments), workingDirectory);
            return new GitCommandResult(-1, "", ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TryKill(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // A concurrently-exiting child is already the desired state.
        }
    }

    private static (string? Sha, string? SkipReason, string? Error) GitError(GitCommandResult result) =>
        (null, "git_error", TrimError(result));

    private static string TrimError(GitCommandResult result)
    {
        var stderr = result.Stderr.Trim();
        if (stderr.Length > 0)
            return stderr;
        var stdout = result.Stdout.Trim();
        if (stdout.Length > 0)
            return stdout;
        return $"git failed with exit code {result.ExitCode}";
    }

    private static string SanitizeSubject(string boardName) =>
        boardName.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private sealed record GitCommandResult(int ExitCode, string Stdout, string Stderr);

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
