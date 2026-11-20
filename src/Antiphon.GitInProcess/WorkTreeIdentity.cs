using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// libgit2 reading of a checkout for the checkpoint tool's source identity (fingerprint v2): HEAD, and for every
/// record git status --porcelain=v2 prints (tracked changes and untracked files, ignored files excluded) its HEAD
/// and index entries. The caller hashes the work-tree bytes. Read in process, so a snapshot starts no git process;
/// the script reads the same facts from <c>git status --porcelain=v2</c> and both must produce the same identity.
/// <para>
/// Refusals shared with git's reading (<see cref="WorkTreeIdentityException"/>): <c>root_invalid</c>,
/// <c>head_invalid</c>, <c>unmerged_index</c>, and <c>indexed_path_hidden</c> for an assume-unchanged entry.
/// Everything libgit2 does not read the way git does is declined (<see cref="WorkTreeIdentityDeclinedException"/>)
/// and read through git instead: the in-process switch off, shallow, grafts, replace refs, reftable, per-worktree
/// config, sparse checkout, a version 3 (skip-worktree, intent-to-add), version 4 or split index, and any gitlink.
/// </para>
/// <para>
/// Config: the caller isolates libgit2 from global, XDG and system config, as the hardened git child is
/// (<c>GIT_CONFIG_NOSYSTEM</c>, <c>GIT_CONFIG_GLOBAL</c> and <c>core.excludesFile</c> on the null device), so both
/// read only the repository's own.
/// </para>
/// </summary>
internal static class WorkTreeIdentity
{
    internal sealed record Entry(string Path, string? HeadMode, string? HeadOid, string? IndexMode, string? IndexOid);

    internal sealed record Reading(string Root, string Commit, IReadOnlyList<Entry> Entries);

    internal static Reading Read(string path)
    {
        if (!InProcessGitReads.Enabled) throw new WorkTreeIdentityDeclinedException("disabled");
        string? discovered;
        try { discovered = Repository.Discover(path); }
        catch (Exception ex) when (ex is LibGit2SharpException or ArgumentException or IOException) { discovered = null; }
        if (discovered is null) throw new WorkTreeIdentityException("root_invalid");
        using var repo = new Repository(discovered);
        if (repo.Info.IsBare || repo.Info.WorkingDirectory is null) throw new WorkTreeIdentityException("root_invalid");
        var root = Path.GetFullPath(repo.Info.WorkingDirectory).TrimEnd('\\', '/');
        if (InProcessGitReads.Unusual(repo)) throw new WorkTreeIdentityDeclinedException("layout");

        if (IndexVersion(repo.Info.Path) is not (0 or 2)) throw new WorkTreeIdentityDeclinedException("index-version");
        if (Directory.EnumerateFiles(repo.Info.Path, "sharedindex.*").Any())
            throw new WorkTreeIdentityDeclinedException("split-index");

        if (InProcessGitReads.ConfigOf(repo) is not { } view) throw new WorkTreeIdentityDeclinedException("worktree-config");
        try
        {
            if (view.Config.Get<bool>("core.sparseCheckout")?.Value == true)
                throw new WorkTreeIdentityDeclinedException("sparse");
        }
        finally
        {
            if (view.Owned) view.Config.Dispose();
        }

        foreach (var entry in repo.Index)
        {
            if (entry.StageLevel != StageLevel.Staged) throw new WorkTreeIdentityException("unmerged_index");
            if (entry.Mode == Mode.GitLink) throw new WorkTreeIdentityDeclinedException("gitlink");
            if (entry.AssumeUnchanged) throw new WorkTreeIdentityException("indexed_path_hidden");
        }

        var tip = repo.Head.Tip ?? throw new WorkTreeIdentityException("head_invalid");
        var options = new StatusOptions
        {
            Show = StatusShowOption.IndexAndWorkDir,
            IncludeUntracked = true,
            RecurseUntrackedDirs = true,
            IncludeIgnored = false,
            DetectRenamesInIndex = false,
            DetectRenamesInWorkDir = false,
            ExcludeSubmodules = false,
            IncludeUnaltered = false,
        };
        var entries = new List<Entry>();
        foreach (var item in repo.RetrieveStatus(options))
        {
            if (item.State is FileStatus.Unaltered or FileStatus.Ignored) continue;
            if (item.State.HasFlag(FileStatus.Conflicted)) throw new WorkTreeIdentityException("unmerged_index");
            // libgit2 reports paths with native separators on Windows; a backslash elsewhere is part of the name.
            var relative = OperatingSystem.IsWindows() ? item.FilePath.Replace('\\', '/') : item.FilePath;
            // A tracked or staged path has a HEAD and/or index side. Only a blob or symlink in HEAD is a HEAD entry:
            // git prints 000000 when HEAD holds a directory there. A HEAD gitlink is git's to judge.
            var head = tip[relative];
            if (head is not null && head.TargetType == TreeEntryTargetType.GitLink)
                throw new WorkTreeIdentityDeclinedException("gitlink");
            if (head is not null && head.TargetType != TreeEntryTargetType.Blob) head = null;
            var indexed = repo.Index[relative];
            var tracked = item.State & ~FileStatus.NewInWorkdir;
            if (tracked != FileStatus.Unaltered || head is not null || indexed is not null)
                entries.Add(new Entry(relative,
                    head is null ? null : ModeText(head.Mode), head?.Target.Sha,
                    indexed is null ? null : ModeText(indexed.Mode), indexed?.Id.Sha));
            // Untracked: an untracked file, or the file left on disk after `git rm --cached` (git prints that path
            // twice, a staged deletion and an untracked file).
            if (item.State.HasFlag(FileStatus.NewInWorkdir) && indexed is null)
                entries.Add(new Entry(relative, null, null, null, null));
        }
        entries.Sort(EntryOrder.Instance);
        return new Reading(root, tip.Sha, entries);
    }

    /// <summary>git's six-digit octal mode, as <c>git status --porcelain=v2</c> prints it.</summary>
    internal static string ModeText(Mode mode) => Convert.ToString((int)mode, 8).PadLeft(6, '0');

    /// <summary>Path in git's byte order, then a path's tracked record before its untracked one.</summary>
    internal sealed class EntryOrder : IComparer<Entry>
    {
        public static readonly EntryOrder Instance = new();

        public int Compare(Entry? x, Entry? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var byPath = Utf8Ordinal.Instance.Compare(x.Path, y.Path);
            if (byPath != 0) return byPath;
            return Untracked(x).CompareTo(Untracked(y));
        }

        private static bool Untracked(Entry e) => e.HeadMode is null && e.IndexMode is null;
    }

    /// <summary>The index file's version, 0 when there is no index yet.</summary>
    private static int IndexVersion(string gitDir)
    {
        var file = Path.Combine(gitDir, "index");
        if (!File.Exists(file)) return 0;
        Span<byte> header = stackalloc byte[8];
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Read(header) != 8 || !header[..4].SequenceEqual("DIRC"u8)) throw new WorkTreeIdentityDeclinedException("index-header");
        return (int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
    }

    /// <summary>Ordinal order of the UTF-8 bytes (git's path order), which differs from UTF-16 ordinal above U+FFFF.</summary>
    internal sealed class Utf8Ordinal : IComparer<string>
    {
        public static readonly Utf8Ordinal Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            return System.Text.Encoding.UTF8.GetBytes(x).AsSpan().SequenceCompareTo(System.Text.Encoding.UTF8.GetBytes(y));
        }
    }
}

/// <summary>A checkout <see cref="WorkTreeIdentity"/> refuses to read, with the refusal code.</summary>
internal sealed class WorkTreeIdentityException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>A checkout libgit2 does not read the way git does: the caller reads it through git instead.</summary>
internal sealed class WorkTreeIdentityDeclinedException(string reason) : Exception(reason)
{
    public string Reason { get; } = reason;
}
