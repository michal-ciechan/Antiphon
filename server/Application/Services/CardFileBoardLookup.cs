using System.Collections.Immutable;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>Process-local ownership lookup. BoardChanged and committed pin writes invalidate it.
/// A clean opted-out skip drops database work only. Every sweep still probes the pinned
/// directory and the index. Startup, the sweep backstop, and a restoring server git command
/// clear the skip without dropping the ownership snapshot.</summary>
public sealed class CardFileBoardLookup
{
    private readonly object _lock = new();
    private readonly SemaphoreSlim _fill = new(1, 1);
    private readonly HashSet<Guid> _cleanOptedOut = [];
    private Snapshot? _snapshot;
    private long _generation;
    private long _reinspection;

    public long Generation { get { lock (_lock) return _generation; } }

    public long Reinspection { get { lock (_lock) return _reinspection; } }

    public long Invalidate()
    {
        lock (_lock)
        {
            _snapshot = null;
            _cleanOptedOut.Clear();
            return ++_generation;
        }
    }

    internal void NoteCleanOptedOut(Guid boardId, long generation, long reinspection)
    {
        lock (_lock)
            if (_generation == generation && _reinspection == reinspection)
                _cleanOptedOut.Add(boardId);
    }

    /// <summary>
    /// Drops clean opted-out boards from the sweep skip set without discarding the ownership
    /// snapshot. A repository path limits that drop to boards whose card-file working tree is
    /// that directory; null re-checks every skipped board. An in-flight sweep that already
    /// captured the previous reinspection cannot mark a board clean again.
    /// </summary>
    public void RequestOptedOutReinspection(string? repositoryPath = null)
    {
        lock (_lock)
        {
            _reinspection++;
            if (string.IsNullOrWhiteSpace(repositoryPath) || _snapshot is null)
            {
                _cleanOptedOut.Clear();
                return;
            }
            string git;
            try { git = NormalizeDirectory(repositoryPath); }
            catch (Exception)
            {
                _cleanOptedOut.Clear();
                return;
            }
            foreach (var board in _snapshot.Boards)
            {
                var root = board.CardFilesRepositoryPath ?? board.LocalRepositoryPath;
                if (string.IsNullOrWhiteSpace(root))
                {
                    _cleanOptedOut.Remove(board.Id);
                    continue;
                }
                try
                {
                    if (SameWorkingTree(git, NormalizeDirectory(root)))
                        _cleanOptedOut.Remove(board.Id);
                }
                catch (Exception) { _cleanOptedOut.Remove(board.Id); }
            }
        }
    }

    private static string NormalizeDirectory(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static bool SameWorkingTree(string git, string card)
    {
        if (string.Equals(git, card, StringComparison.OrdinalIgnoreCase)) return true;
        var separator = Path.DirectorySeparatorChar;
        return card.StartsWith(git + separator, StringComparison.OrdinalIgnoreCase)
            || git.StartsWith(card + separator, StringComparison.OrdinalIgnoreCase);
    }

    public void NoteServerGit(string repository, IReadOnlyList<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(repository) || !GitMayRestoreWorktree(arguments)) return;
        RequestOptedOutReinspection(repository);
    }

    public void NoteServerGit(string repository, string arguments) =>
        NoteServerGit(repository, SplitGitArguments(arguments));

    /// <summary>
    /// True for a git invocation that can put committed files back into the working tree.
    /// Index-only <c>restore --staged</c> and mixed <c>reset</c> do not.
    /// </summary>
    internal static bool GitMayRestoreWorktree(IReadOnlyList<string> arguments)
    {
        string? command = null;
        var worktree = false;
        var staged = false;
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            if (command is null)
            {
                if (arg is "-c" or "-C" or "--config-env") { i++; continue; }
                if (arg.StartsWith('-')) continue;
                command = arg;
                continue;
            }
            if (arg is "--staged" or "--cached") staged = true;
            if (arg is "--worktree" or "--hard" or "--merge" or "--keep") worktree = true;
        }
        return command switch
        {
            "checkout" or "switch" or "pull" or "merge" or "rebase" or "cherry-pick" or "revert"
                or "clone" or "checkout-index" or "stash" => true,
            "restore" => worktree || !staged,
            "reset" => worktree,
            _ => false
        };
    }

    internal static IReadOnlyList<string> SplitGitArguments(string arguments)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        char? quote = null;
        foreach (var c in arguments)
        {
            if (quote is not null)
            {
                if (c == quote) quote = null;
                else current.Append(c);
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (char.IsWhiteSpace(c))
            {
                if (current.Length == 0) continue;
                args.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) args.Add(current.ToString());
        return args;
    }

    internal bool NeedsInspection(Entry board)
    {
        lock (_lock) return board.SyncCardFiles || !_cleanOptedOut.Contains(board.Id);
    }

    internal async Task<Snapshot> GetAsync(AppDbContext db, CancellationToken ct)
    {
        // A setup transaction can see its own uncommitted boards. Never publish those to
        // other scopes, nor use a pre-transaction snapshot for its ownership validation.
        if (db.Database.CurrentTransaction is not null)
            return await LoadAsync(db, ct);
        lock (_lock)
            if (_snapshot is { } hit) return hit;
        await _fill.WaitAsync(ct);
        try
        {
            while (true)
            {
                long generation;
                lock (_lock)
                {
                    if (_snapshot is { } hit) return hit;
                    generation = _generation;
                }
                var loaded = await LoadAsync(db, ct);
                lock (_lock)
                {
                    // An event during the SELECT must not let old data refill the cache.
                    if (generation != _generation) continue;
                    return _snapshot = loaded;
                }
            }
        }
        finally { _fill.Release(); }
    }

    private static async Task<Snapshot> LoadAsync(AppDbContext db, CancellationToken ct) => new(
        (await db.Boards.AsNoTracking().TagWith("CardFileBoardLookup")
            .Select(b => new Entry(b.Id, b.ProjectId, b.Name, b.CreatedAt, b.SyncCardFiles,
                b.CardFilesDirectorySlug, b.CardFilesRepositoryPath, b.ArchivedAt,
                b.Project.Name, b.Project.LocalRepositoryPath, b.Project.RepositoryVisibility, b.Project.ArchivedAt))
            .ToListAsync(ct)).ToImmutableArray());

    internal sealed record Entry(Guid Id, Guid ProjectId, string Name, DateTime CreatedAt, bool SyncCardFiles,
        string? CardFilesDirectorySlug, string? CardFilesRepositoryPath, DateTime? ArchivedAt,
        string ProjectName, string? LocalRepositoryPath, RepositoryVisibility RepositoryVisibility, DateTime? ProjectArchivedAt)
    {
        // Callers may build a prospective Board. Never expose cached mutable EF entities.
        internal Board ToBoard() => new()
        {
            Id = Id, ProjectId = ProjectId, Name = Name, CreatedAt = CreatedAt, SyncCardFiles = SyncCardFiles,
            CardFilesDirectorySlug = CardFilesDirectorySlug, CardFilesRepositoryPath = CardFilesRepositoryPath,
            ArchivedAt = ArchivedAt,
            Project = new Project { Id = ProjectId, Name = ProjectName, LocalRepositoryPath = LocalRepositoryPath,
                RepositoryVisibility = RepositoryVisibility, ArchivedAt = ProjectArchivedAt }
        };
    }

    internal sealed class Snapshot
    {
        internal ImmutableArray<Entry> Boards { get; }
        private readonly Dictionary<Guid, Dictionary<string, Guid>> _owners = [];
        private readonly Dictionary<Guid, Dictionary<string, Guid>> _unpinnedOwners = [];

        internal Snapshot(ImmutableArray<Entry> boards)
        {
            Boards = boards.OrderBy(b => b.ProjectId).ThenBy(b => b.Id).ToImmutableArray();
            foreach (var group in boards.GroupBy(b => b.ProjectId))
            {
                var owners = _owners[group.Key] = new(StringComparer.OrdinalIgnoreCase);
                var unpinned = _unpinnedOwners[group.Key] = new(StringComparer.OrdinalIgnoreCase);
                foreach (var board in group.OrderBy(b => b.CreatedAt).ThenBy(b => b.Id))
                {
                    var raw = RawSlug(board.Name);
                    owners.TryAdd(board.CardFilesDirectorySlug ?? raw, board.Id);
                    unpinned.TryAdd(raw, board.Id);
                }
            }
        }

        internal string UniqueSlug(Board board, bool ignorePins = false)
        {
            var slug = RawSlug(board.Name);
            var owners = ignorePins ? _unpinnedOwners : _owners;
            if (!owners.TryGetValue(board.ProjectId, out var siblings)
                || !siblings.TryGetValue(slug, out var owner) || owner == board.Id) return slug;
            var suffix = $"-{board.Id.ToString("N")[..8]}";
            var maxBase = Math.Max(1, CardTaskFileRenderer.SlugMaxLength - suffix.Length);
            return (slug.Length <= maxBase ? slug : slug[..maxBase].Trim('-')) + suffix;
        }

        private static string RawSlug(string name)
        {
            var slug = CardTaskFileRenderer.BoardSlug(name);
            return string.IsNullOrEmpty(slug) ? "board" : slug;
        }
    }
}
