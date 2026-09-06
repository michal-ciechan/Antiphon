using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class CardTaskFileService
{
    public async Task<IDisposable> EnterBoardAsync(Guid boardId, bool wait, CancellationToken ct)
    {
        var projectId = await _db.Boards.Where(b => b.Id == boardId).Select(b => (Guid?)b.ProjectId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Board), boardId);
        return await EnterProjectAsync(projectId, wait, null, ct);
    }

    public async Task<IDisposable> EnterCardAsync(Guid cardId, CancellationToken ct)
    {
        var boardId = await _db.Cards.Where(c => c.Id == cardId).Select(c => (Guid?)c.BoardId).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
        return await EnterBoardAsync(boardId, true, ct);
    }

    public async Task<IDisposable> EnterProjectAsync(Guid projectId, bool wait, string? newPath, CancellationToken ct)
    {
        var leases = new List<IDisposable>();
        try
        {
            leases.Add(await _gate.EnterProjectAsync(projectId, wait, ct) ?? throw Busy());
            var currentPath = await _db.Projects.AsNoTracking().Where(p => p.Id == projectId)
                .Select(p => p.LocalRepositoryPath).FirstOrDefaultAsync(ct);
            var roots = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in new[] { currentPath, newPath })
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                try
                {
                    _repository.ValidatePath(path, path);
                    var root = await _git.GetRepoToplevelAsync(path, ct);
                    if (root is not null) roots.Add(Path.GetFullPath(root));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { /* Project lease still orders disable/repair of an unavailable target. */ }
            }
            foreach (var root in roots)
                leases.Add(await _gate.EnterRepositoryAsync(root, wait, ct) ?? throw Busy());
            return new CombinedLease(leases);
        }
        catch { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); throw; }
    }

    private static ConflictException Busy() => new("Card file sync is already running for this repository.", "card_file_sync_running");

    private sealed class CombinedLease(List<IDisposable> leases) : IDisposable
    {
        private List<IDisposable>? _leases = leases;
        public void Dispose()
        {
            var held = Interlocked.Exchange(ref _leases, null);
            if (held is not null) foreach (var lease in held.AsEnumerable().Reverse()) lease.Dispose();
        }
    }

    private async Task<Board> ReadBoardAsync(Guid id, CancellationToken ct) =>
        await _db.Boards.AsNoTracking().Include(b => b.Project).FirstOrDefaultAsync(b => b.Id == id, ct)
        ?? throw new NotFoundException(nameof(Board), id);

    private CardFileStatusDto BaseStatus(Board board) => new()
    {
        BoardId = board.Id, Enabled = _syncSettings.Enabled, SyncCardFiles = board.SyncCardFiles,
        RepositoryVisibility = board.Project.RepositoryVisibility,
        VisibilitySource = board.Project.RepositoryVisibility == RepositoryVisibility.Unknown ? "Unknown" : "Configured",
        AutoCommit = _syncSettings.AutoCommit, IntervalSeconds = _syncSettings.IntervalSeconds,
        Warnings = board.Project.RepositoryVisibility switch
        {
            RepositoryVisibility.Public => ["public_repository"],
            RepositoryVisibility.Unknown => ["repository_visibility_unknown"], _ => []
        }
    };

    public async Task<CardFileStatusDto> GetStatusAsync(Guid boardId, CancellationToken ct)
    {
        using var lease = await EnterBoardAsync(boardId, true, ct);
        return await StatusUnderGateAsync(await ReadBoardAsync(boardId, ct), ct);
    }

    private async Task<CardFileStatusDto> StatusUnderGateAsync(Board board, CancellationToken ct)
    {
        try { return (await InspectBoardAsync(board, false, ct)).Status; }
        catch (OperationCanceledException) { throw; }
        catch (ConflictException ex) when (ex.Code is "unsafe_card_file_path" or "card_file_directory_conflict")
        { return BaseStatus(board) with { Reason = ex.Code, Warnings = Codes(BaseStatus(board).Warnings, [ex.Code]) }; }
        catch (Exception)
        { return BaseStatus(board) with { Reason = "status_unavailable", Warnings = Codes(BaseStatus(board).Warnings, ["status_unavailable"]) }; }
    }

    public async Task<CardFileCardStatusDto> GetCardStatusAsync(Guid cardId, CancellationToken ct)
    {
        using var lease = await EnterCardAsync(cardId, ct);
        var card = await _db.Cards.AsNoTracking().Where(c => c.Id == cardId)
            .Select(c => new { c.BoardId, c.CardFileVisibility, c.Identifier, c.Title }).FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(Card), cardId);
        var board = await StatusUnderGateAsync(await ReadBoardAsync(card.BoardId, ct), ct);
        var reason = card.CardFileVisibility == CardFileVisibility.Private
            && board.Reason is null or "no_publishable_cards" or "card_files_ignore_missing" or "card_file_path_ignored" or "card_file_cleanup_required"
            ? "card_private" : board.Reason;
        return new CardFileCardStatusDto
        {
            BoardId = board.BoardId, Enabled = board.Enabled, SyncCardFiles = board.SyncCardFiles,
            RepositoryVisibility = board.RepositoryVisibility, VisibilitySource = board.VisibilitySource,
            RepositoryPath = board.RepositoryPath, Directory = board.Directory,
            Eligible = reason is null, Reason = reason, Warnings = board.Warnings, Ignored = board.Ignored,
            WorkingTreeRemovalPending = board.WorkingTreeRemovalPending, GitRemovalPending = board.GitRemovalPending,
            AutoCommit = board.AutoCommit, IntervalSeconds = board.IntervalSeconds,
            CardFileVisibility = card.CardFileVisibility,
            RelativeFile = reason is null ? $"{board.Directory}/{CardTaskFileRenderer.CardFileName(card.Identifier, card.Title)}" : null
        };
    }

    private sealed record BoardInspection(Board Board, CardFileStatusDto Status, string? Root, string? AbsoluteDirectory,
        string Slug, IReadOnlyList<CardFilePublicCard> Cards, int Total,
        Dictionary<string, string> Desired, CardFileRepositoryState? Repository, HashSet<string> Removals);

    private async Task<BoardInspection> InspectBoardAsync(Board board, bool empty, CancellationToken ct)
    {
        var status = BaseStatus(board);
        var warnings = new HashSet<string>(status.Warnings, StringComparer.Ordinal);
        var total = await _db.Cards.CountAsync(c => c.BoardId == board.Id, ct);
        var root = board.CardFilesRepositoryPath ?? board.Project.LocalRepositoryPath;
        var slug = board.CardFilesDirectorySlug ?? await UniqueBoardSlugAsync(board, ct);
        var desired = new Dictionary<string, string>(StringComparer.Ordinal);
        var removals = new HashSet<string>(StringComparer.Ordinal);
        var cards = new List<CardFilePublicCard>();
        if (string.IsNullOrWhiteSpace(root))
        {
            status = status with { Reason = _syncSettings.Enabled ? "no_repository_path" : "card_file_sync_disabled",
                WorkingTreeRemovalPending = false, GitRemovalPending = false };
            return new(board, status, null, null, slug, cards, total, desired, null, removals);
        }
        root = Path.GetFullPath(root);
        _repository.ValidatePath(root, root);
        if (!Directory.Exists(root) || !await _git.IsRepositoryAsync(root, ct))
        {
            warnings.UnionWith(["card_file_cleanup_status_unknown", "card_file_cleanup_required"]);
            status = status with { Reason = _syncSettings.Enabled ? "not_a_git_repository" : "card_file_sync_disabled", Warnings = Codes(warnings) };
            return new(board, status, null, null, slug, cards, total, desired, null, removals);
        }
        if (slug.Length == 0 || slug.Length > 60 || slug != CardTaskFileRenderer.BoardSlug(slug))
            throw new ConflictException("Card-file directory is unsafe.", "unsafe_card_file_path");
        var directory = $"docs/cards/{slug}";
        var absolute = Path.Combine(root, "docs", "cards", slug);
        _repository.ValidatePath(root, absolute);
        // Compare all project targets, including legacy unpinned owners, before pinning/mutating.
        var others = await _db.Boards.AsNoTracking().Include(b => b.Project).Where(b => b.Id != board.Id).ToListAsync(ct);
        foreach (var other in others)
        {
            var otherRoot = other.CardFilesRepositoryPath ?? other.Project.LocalRepositoryPath;
            if (string.IsNullOrWhiteSpace(otherRoot)) continue;
            var otherSlug = other.CardFilesDirectorySlug ?? await UniqueBoardSlugAsync(other, ct);
            if (string.Equals(Path.GetFullPath(Path.Combine(otherRoot, "docs", "cards", otherSlug)), absolute, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Card-file directories have conflicting owners.", "card_file_directory_conflict");
        }
        var reason = new CardFilePolicyService().GetReason(_syncSettings.Enabled, board.ArchivedAt is not null,
            board.Project.ArchivedAt is not null, board.SyncCardFiles, board.Project.RepositoryVisibility, CardFileVisibility.Inherit);
        if (reason is null && !empty)
            cards = await _db.Cards.AsNoTracking().Where(c => c.BoardId == board.Id
                && (c.CardFileVisibility == CardFileVisibility.Inherit || c.CardFileVisibility == CardFileVisibility.Public))
                .Select(CardFilePublicProjection.Select).ToListAsync(ct);
        if (reason is null && cards.Count == 0) reason = "no_publishable_cards";
        foreach (var card in cards)
            desired[$"{directory}/{CardTaskFileRenderer.CardFileName(card.Identifier, card.Title)}"] = CardTaskFileRenderer.RenderCard(card);
        var indexPath = $"{directory}/INDEX.md";
        if (cards.Count > 0)
            desired[indexPath] = CardTaskFileRenderer.RenderIndex(board.Name, cards,
                cards.ToDictionary(c => c.Id, c => CardTaskFileRenderer.CardFileName(c.Identifier, c.Title)));
        var state = await _repository.InspectAsync(root, absolute, ct);
        foreach (var path in state.WorkingPaths.Concat(state.Index.Keys).Concat(state.Head.Keys))
            if (!desired.ContainsKey(path)) removals.Add(path);
        // INDEX can retain revoked identities despite its filename remaining desired. Compare public hashes only.
        if (cards.Count > 0 && cards.Count < total)
        {
            var content = desired[indexPath];
            var oid = await _repository.HashAsync(root, indexPath, content, ct);
            var working = await _repository.ReadAsync(root, Path.Combine(root, indexPath), ct);
            if ((working is not null && Normalize(working) != content)
                || (state.Index.TryGetValue(indexPath, out var staged) && staged != oid)
                || (state.Head.TryGetValue(indexPath, out var committed) && committed != oid)) removals.Add(indexPath);
        }
        var workPending = state.WorkingPaths.Any(removals.Contains);
        var gitPending = state.Index.Keys.Concat(state.Head.Keys).Any(removals.Contains);
        var stagedOnly = state.Index.Keys.Any(p => removals.Contains(p) && !state.Head.ContainsKey(p));
        var slugs = new List<string>();
        foreach (var enabled in others.Append(board).Where(b => b.ProjectId == board.ProjectId && b.SyncCardFiles))
            slugs.Add(enabled.CardFilesDirectorySlug ?? await UniqueBoardSlugAsync(enabled, ct));
        var protectedByIgnore = await _repository.HasIgnoreProtectionAsync(root, slugs, ct);
        if (!protectedByIgnore) { warnings.Add("card_files_ignore_missing"); reason ??= "card_files_ignore_missing"; }
        bool? ignored = desired.Count > 0 ? await _repository.IsIgnoredAsync(root, desired.Keys.ToArray(), ct) : null;
        if (ignored == true) { warnings.Add("card_file_path_ignored"); reason ??= "card_file_path_ignored"; }
        if (workPending || gitPending) warnings.Add("card_file_cleanup_required");
        if (stagedOnly) warnings.Add("card_file_staged_private_residue");
        if (!workPending && gitPending) warnings.Add("card_file_git_cleanup_pending");
        if (gitPending) reason ??= stagedOnly ? "card_file_staged_private_residue" : "card_file_cleanup_required";
        if (!_syncSettings.Enabled) warnings.Add("card_file_sync_disabled");
        status = status with { RepositoryPath = root, Directory = directory, Eligible = reason is null,
            Reason = reason, Warnings = Codes(warnings), Ignored = ignored,
            WorkingTreeRemovalPending = workPending, GitRemovalPending = gitPending };
        return new(board, status, root, absolute, slug, cards, total, desired, state, removals);
    }

    private static string[] Codes(params IEnumerable<string>[] sets) => sets.SelectMany(s => s).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    public async Task ValidateEnableAsync(Board board, CancellationToken ct)
    {
        var candidate = new Board { Id = board.Id, ProjectId = board.ProjectId, Project = board.Project, Name = board.Name,
            CreatedAt = board.CreatedAt, SyncCardFiles = true, CardFilesDirectorySlug = board.CardFilesDirectorySlug,
            CardFilesRepositoryPath = board.CardFilesRepositoryPath, ArchivedAt = board.ArchivedAt };
        var status = (await InspectBoardAsync(candidate, false, ct)).Status;
        if (status.RepositoryPath is null || board.Project.RepositoryVisibility == RepositoryVisibility.Unknown
            || status.Warnings.Contains("card_files_ignore_missing"))
        {
            var reason = status.RepositoryPath is null ? status.Reason : board.Project.RepositoryVisibility == RepositoryVisibility.Unknown
                ? "repository_visibility_unknown" : "card_files_ignore_missing";
            throw new ConflictException("Card-file publication prerequisites are not satisfied.", "card_file_policy_refused",
                new Dictionary<string, object?> { ["reason"] = reason, ["warnings"] = status.Warnings,
                    ["repositoryPath"] = status.RepositoryPath, ["directory"] = status.Directory });
        }
    }

    public async Task<CardFileStatusDto> UpdateSettingsAsync(Guid boardId, bool? value, bool? expected, IEventBus events, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (value is null) errors["syncCardFiles"] = ["syncCardFiles is required."];
        if (expected is null) errors["expectedSyncCardFiles"] = ["expectedSyncCardFiles is required."];
        if (errors.Count > 0) throw new ValidationException(errors);
        using var lease = await EnterBoardAsync(boardId, true, ct);
        var board = await _db.Boards.Include(b => b.Project).FirstAsync(b => b.Id == boardId, ct);
        await _db.Entry(board).ReloadAsync(ct);
        await _db.Entry(board.Project).ReloadAsync(ct);
        if (board.SyncCardFiles != expected) throw new ConflictException("Card-file policy changed; refresh and retry.", "card_file_policy_changed");
        if (board.SyncCardFiles != value)
        {
            if (value == true) await ValidateEnableAsync(board, ct);
            board.SyncCardFiles = value!.Value;
            await _db.SaveChangesAsync(ct);
            await events.PublishToAllAsync("BoardChanged", new { boardId, projectId = board.ProjectId }, ct);
        }
        return await StatusUnderGateAsync(board, ct);
    }

    public async Task EnsureDrainedAsync(Guid projectId, Guid? boardId, CancellationToken ct)
    {
        var boards = await _db.Boards.AsNoTracking().Include(b => b.Project)
            .Where(b => b.ProjectId == projectId && (boardId == null || b.Id == boardId)).ToListAsync(ct);
        var targets = new List<object>();
        foreach (var board in boards)
        {
            CardFileStatusDto status;
            try { status = (await InspectBoardAsync(board, true, ct)).Status; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { status = BaseStatus(board); }
            if (status.RemovalPending) targets.Add(new { boardId = board.Id, status.RepositoryPath, status.Directory });
        }
        if (targets.Count > 0) throw new ConflictException("Drain the previous card-file targets before changing ownership.",
            "card_file_cleanup_required", new Dictionary<string, object?> { ["targets"] = targets });
    }
}
