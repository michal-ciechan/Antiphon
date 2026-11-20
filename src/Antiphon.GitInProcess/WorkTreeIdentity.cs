using LibGit2Sharp;

namespace Antiphon.GitInProcess;

/// <summary>
/// libgit2 reading of a checkout for the checkpoint tool's source identity (fingerprint v2): HEAD, and for every
/// path git status lists (tracked changes and untracked files, ignored files excluded) its HEAD and index entries.
/// The caller hashes the work-tree bytes. Read in process, so a snapshot starts no git process; the script reads
/// the same facts from <c>git status --porcelain=v2</c> and both must produce the same identity.
/// <para>
/// Refusals (thrown as <see cref="WorkTreeIdentityException"/>, codes shared with the script):
/// <c>root_invalid</c> (not inside a checkout), <c>head_invalid</c> (no HEAD commit), <c>indexed_path_hidden</c>
/// (an assume-unchanged entry, or a version 3 index, which is how git stores skip-worktree and intent-to-add:
/// status cannot see an edit hidden behind either), <c>unmerged_index</c>, <c>submodule_unsupported</c> (any
/// gitlink), and <c>libgit2_unsupported</c> for layouts libgit2 does not read the way git does (shallow, grafts,
/// replace refs, reftable, per-worktree config, sparse checkout, a version 4 or split index).
/// </para>
/// <para>
/// Config: the caller isolates libgit2 from global, XDG and system config, as the hardened git child is
/// (<c>GIT_CONFIG_NOSYSTEM</c>, <c>GIT_CONFIG_GLOBAL=/dev/null</c>), so both read only the repository's own.
/// </para>
/// </summary>
internal static class WorkTreeIdentity
{
    internal sealed record Entry(string Path, string? HeadMode, string? HeadOid, string? IndexMode, string? IndexOid);

    internal sealed record Reading(string Root, string Commit, IReadOnlyList<Entry> Entries);

    internal static Reading Read(string path)
    {
        string? discovered;
        try { discovered = Repository.Discover(path); }
        catch (Exception ex) when (ex is LibGit2SharpException or ArgumentException or IOException) { discovered = null; }
        if (discovered is null) throw new WorkTreeIdentityException("root_invalid");
        using var repo = new Repository(discovered);
        if (repo.Info.IsBare || repo.Info.WorkingDirectory is null) throw new WorkTreeIdentityException("root_invalid");
        var root = Path.GetFullPath(repo.Info.WorkingDirectory).TrimEnd('\\', '/');
        if (InProcessGitReads.Unusual(repo)) throw new WorkTreeIdentityException("libgit2_unsupported");

        switch (IndexVersion(repo.Info.Path))
        {
            case 0 or 2: break;
            case 3: throw new WorkTreeIdentityException("indexed_path_hidden");
            default: throw new WorkTreeIdentityException("libgit2_unsupported");
        }
        if (Directory.EnumerateFiles(repo.Info.Path, "sharedindex.*").Any())
            throw new WorkTreeIdentityException("libgit2_unsupported");

        if (InProcessGitReads.ConfigOf(repo) is not { } view) throw new WorkTreeIdentityException("libgit2_unsupported");
        try
        {
            if (view.Config.Get<bool>("core.sparseCheckout")?.Value == true)
                throw new WorkTreeIdentityException("libgit2_unsupported");
        }
        finally
        {
            if (view.Owned) view.Config.Dispose();
        }

        foreach (var entry in repo.Index)
        {
            if (entry.StageLevel != StageLevel.Staged) throw new WorkTreeIdentityException("unmerged_index");
            if (entry.Mode == Mode.GitLink) throw new WorkTreeIdentityException("submodule_unsupported");
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
        var paths = new SortedSet<string>(Utf8Ordinal.Instance);
        foreach (var item in repo.RetrieveStatus(options))
        {
            if (item.State is FileStatus.Unaltered or FileStatus.Ignored) continue;
            if (item.State.HasFlag(FileStatus.Conflicted)) throw new WorkTreeIdentityException("unmerged_index");
            paths.Add(item.FilePath.Replace('\\', '/'));
        }

        var entries = new List<Entry>(paths.Count);
        foreach (var relative in paths)
        {
            var head = tip[relative];
            if (head is not null && head.Mode == Mode.GitLink) throw new WorkTreeIdentityException("submodule_unsupported");
            var indexed = repo.Index[relative];
            entries.Add(new Entry(relative,
                head is null ? null : ModeText(head.Mode), head?.Target.Sha,
                indexed is null ? null : ModeText(indexed.Mode), indexed?.Id.Sha));
        }
        return new Reading(root, tip.Sha, entries);
    }

    /// <summary>git's six-digit octal mode, as <c>git status --porcelain=v2</c> prints it.</summary>
    internal static string ModeText(Mode mode) => Convert.ToString((int)mode, 8).PadLeft(6, '0');

    /// <summary>The index file's version, 0 when there is no index yet.</summary>
    private static int IndexVersion(string gitDir)
    {
        var file = Path.Combine(gitDir, "index");
        if (!File.Exists(file)) return 0;
        Span<byte> header = stackalloc byte[8];
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Read(header) != 8 || !header[..4].SequenceEqual("DIRC"u8)) throw new WorkTreeIdentityException("index_invalid");
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
