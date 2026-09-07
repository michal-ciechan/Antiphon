using Antiphon.Server.Application.Interfaces;
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
public sealed partial class CardTaskFileService
{
    private readonly AppDbContext _db;
    private readonly CardTaskFileSyncGate _gate;
    private readonly GitWorkspaceService _git;
    private readonly ICardFileRepository _repository;
    private readonly CardFileSyncSettings _syncSettings;
    private readonly ILogger<CardTaskFileService> _logger;

    public CardTaskFileService(
        AppDbContext db,
        CardTaskFileSyncGate gate,
        GitWorkspaceService git,
        ILogger<CardTaskFileService> logger,
        ICardFileRepository repository,
        IOptions<CardFileSyncSettings>? settings = null)
    {
        _db = db;
        _gate = gate;
        _git = git;
        _logger = logger;
        _syncSettings = settings?.Value ?? new CardFileSyncSettings();
        _repository = repository;
    }

    public async Task<IReadOnlyList<CardFileSyncBoardResult>> SyncAllAsync(
        bool dryRun = false, CancellationToken ct = default)
    {
        if (!_syncSettings.Enabled) return [];
        var ids = await _db.Boards.AsNoTracking().OrderBy(b => b.ProjectId).ThenBy(b => b.Id).Select(b => b.Id).ToListAsync(ct);
        var results = new List<CardFileSyncBoardResult>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try { results.Add(await SyncBoardAsync(id, dryRun, ct)); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                var board = await _db.Boards.AsNoTracking().Include(b => b.Project).FirstOrDefaultAsync(b => b.Id == id, ct);
                var code = ex is HttpException http ? http.Code ?? "card_file_sync_error" : "card_file_sync_error";
                var basis = board is null ? new CardFileStatusDto { BoardId = id, Enabled = _syncSettings.Enabled } : BaseStatus(board);
                var policy = basis with { Reason = code, Warnings = Codes(basis.Warnings, [code]) };
                results.Add(new(id, board?.Name ?? "Unavailable board", null, 0, 0, 0, null, code, code,
                    "Card-file sync failed; retry reconciliation.", dryRun) { Policy = policy, Warnings = policy.Warnings });
                if (!dryRun) _gate.NoteSkipReason(id, null, code);
            }
        }
        return results;
    }

    public async Task<CardFileSyncBoardResult> SyncBoardAsync(Guid boardId, bool dryRun = false, CancellationToken ct = default)
    {
        if (!_syncSettings.Enabled) throw new ConflictException("Card file sync is disabled.", "card_file_sync_disabled");
        using var lease = await EnterBoardAsync(boardId, false, ct);
        // Authoritative reads occur only after both the project and Git-root leases are owned.
        var board = await ReadBoardAsync(boardId, ct);
        BoardInspection inspection;
        try { inspection = await InspectBoardAsync(board, false, ct); }
        catch (OperationCanceledException) { throw; }
        catch (HttpException) { throw; }
        catch (Exception ex)
        {
            var policy = ex is CardFileInspectionException unavailable ? unavailable.Status : BaseStatus(board) with { Reason = "status_unavailable", Warnings = ["git_error", "status_unavailable"] };
            return new(board.Id, board.Name, null, 0, 0, 0, null, "git_error", "git_error",
                "Card-file inspection failed; retry reconciliation.", dryRun) { Policy = policy, Warnings = policy.Warnings };
        }
        var status = inspection.Status;
        var written = 0;
        var deleted = 0;
        var unchanged = 0;
        string? sha = null, error = null;
        var writeSkip = status.Reason;
        string? commitSkip = dryRun ? "dry_run" : !_syncSettings.AutoCommit ? "autocommit_disabled" : "nothing_to_commit";
        var operationWarnings = new List<string>();
        var phase = "card_file_io_error";
        if (inspection.Root is { } root && inspection.AbsoluteDirectory is { } directory && inspection.Repository is { } state)
        {
            // Database failures precede filesystem effects and remain ordinary request failures.
            if (!dryRun && (board.CardFilesDirectorySlug is null || board.CardFilesRepositoryPath is null))
            {
                await _db.Boards.Where(b => b.Id == board.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(b => b.CardFilesDirectorySlug, inspection.Slug)
                    .SetProperty(b => b.CardFilesRepositoryPath, root), ct);
                board.CardFilesDirectorySlug = inspection.Slug;
                board.CardFilesRepositoryPath = root;
            }
            try
            {
                if (!dryRun)
                {
                    _repository.RemoveTemporaryFiles(root, directory, board.Id);
                }
                // Privacy cleanup completes before any additions, including a refreshed INDEX.
                foreach (var path in inspection.Removals.Order(StringComparer.Ordinal))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!state.WorkingPaths.Contains(path)) continue;
                    if (!dryRun) _repository.Delete(root, Path.Combine(root, path));
                    deleted++;
                }
                var gitRemovals = inspection.Removals.Where(p => state.Index.ContainsKey(p) || state.Head.ContainsKey(p)).ToArray();
                var stagedOnly = gitRemovals.Where(p => state.Index.ContainsKey(p) && !state.Head.ContainsKey(p)).ToArray();
                phase = "git_error";
                if (!dryRun && stagedOnly.Length > 0)
                    await _repository.UnstageAsync(root, directory, stagedOnly, ct);
                var headRemovals = gitRemovals.Where(state.Head.ContainsKey).ToArray();
                var drained = headRemovals.Length == 0;
                if (headRemovals.Length > 0 && _syncSettings.AutoCommit)
                {
                    if (dryRun) drained = true;
                    else
                    {
                        var cleanup = await _repository.CommitAsync(root, directory,
                            headRemovals.ToDictionary(p => p, _ => (string?)null),
                            "antiphon: remove unpublished card files", ct);
                        sha = cleanup.Sha;
                        commitSkip = cleanup.SkipReason;
                        error = cleanup.Error;
                        drained = cleanup.Sha is not null || cleanup.SkipReason == "nothing_to_commit";
                        if (!drained) operationWarnings.Add(cleanup.SkipReason ?? "git_error");
                    }
                }
                var permission = inspection.Cards.Count > 0 && status.Reason is null or "card_file_cleanup_required" or "card_file_staged_private_residue";
                if (!drained)
                    writeSkip = permission ? error is not null ? "git_error" : "card_file_cleanup_required" : writeSkip;
                else if (permission)
                {
                    writeSkip = null;
                    phase = "card_file_io_error";
                    foreach (var (relative, content) in inspection.Desired)
                    {
                        ct.ThrowIfCancellationRequested();
                        var path = Path.Combine(root, relative);
                        var old = dryRun && inspection.Removals.Contains(relative) ? null : await _repository.ReadAsync(root, path, ct);
                        if (old is not null && Normalize(old) == content) { unchanged++; continue; }
                        if (!dryRun) await _repository.WriteAsync(root, path, content, board.Id, ct);
                        written++;
                    }
                    phase = "git_error";
                    if (!dryRun && _syncSettings.AutoCommit)
                    {
                        var publication = await _repository.CommitAsync(root, directory,
                            inspection.Desired.ToDictionary(p => p.Key, p => (string?)p.Value),
                            $"antiphon: sync card files ({board.Name})", ct);
                        sha = publication.Sha ?? sha;
                        commitSkip = publication.SkipReason;
                        error = publication.Error;
                        if (publication.SkipReason is not null and not "nothing_to_commit") operationWarnings.Add(publication.SkipReason);
                    }
                }
                if (!dryRun) status = await StatusUnderGateAsync(board, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (HttpException) { throw; }
            catch (CardFileGitGuardException ex)
            {
                commitSkip = ex.Code;
                operationWarnings.Add(ex.Code);
                if (writeSkip is null or "card_file_cleanup_required" or "card_file_staged_private_residue") writeSkip = ex.Code;
                if (!dryRun) status = await StatusUnderGateAsync(board, ct);
            }
            catch (Exception)
            {
                writeSkip = commitSkip = phase;
                error = phase == "git_error" ? "Card-file Git operation failed; retry reconciliation."
                    : "Card-file filesystem operation failed; retry reconciliation.";
                operationWarnings.Add(phase);
                if (!dryRun) status = await StatusUnderGateAsync(board, ct);
            }
        }
        var warnings = Codes(status.Warnings, operationWarnings);
        if (!dryRun) _gate.NoteSkipReason(board.Id, inspection.Root, writeSkip ?? (error is not null ? commitSkip : null));
        return new(board.Id, board.Name, status.Directory, written, deleted, unchanged, sha, writeSkip, commitSkip, error, dryRun)
        {
            EligibleCards = inspection.Cards.Count, ExcludedCards = inspection.Total - inspection.Cards.Count,
            Policy = status, Warnings = warnings
        };
    }

    private async Task<string> UniqueBoardSlugAsync(Board board, CancellationToken ct, bool ignorePins = false)
    {
        var baseSlug = CardTaskFileRenderer.BoardSlug(board.Name);
        if (string.IsNullOrEmpty(baseSlug))
            baseSlug = "board";

        var siblings = await _db.Boards.AsNoTracking()
            .Where(b => b.ProjectId == board.ProjectId)
            .Select(b => new { b.Id, b.Name, b.CreatedAt, b.CardFilesDirectorySlug })
            .ToListAsync(ct);

        static string RawSlug(string name)
        {
            var slug = CardTaskFileRenderer.BoardSlug(name);
            return string.IsNullOrEmpty(slug) ? "board" : slug;
        }

        var colliding = siblings
            .Where(b => string.Equals((ignorePins ? null : b.CardFilesDirectorySlug) ?? RawSlug(b.Name), baseSlug, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.CreatedAt)
            .ThenBy(b => b.Id)
            .ToList();

        if (colliding.Count == 0 || colliding[0].Id == board.Id)
            return baseSlug;

        var suffix = $"-{board.Id.ToString("N")[..8]}";
        var maxBase = Math.Max(1, CardTaskFileRenderer.SlugMaxLength - suffix.Length);
        var trimmed = baseSlug.Length <= maxBase ? baseSlug : baseSlug[..maxBase].Trim('-');
        return trimmed + suffix;
    }

}
