using System.Text;
using Antiphon.HostCleanup.Checkpoints;
using LibGit2Sharp;

namespace Antiphon.Checkpoints;

// CARD-2052: one owned-output removal asks "is this entry tracked?" once per entry. Reading and parsing the whole
// index for each of a 2,190-entry output took 4-5 minutes on server2 (Review ad310231). The scope keeps one
// in-process listing with the index version it was read from, and answers from it only while a fresh stamp still
// shows that version. Nothing outlives the scope.
//
// Review d1e0f4ca (round 1) bound that version to what git itself would read:
// - D-1. A listing whose stamp moved while it was read is never used: the entry is refused (index-changed-during-read).
//   Every in-process decision also records its version, and the unlink it authorizes happens only while the index is
//   still that version (index-changed-before-unlink otherwise). What remains is the instant between that last stamp
//   and the unlink syscall itself; only git's own index.lock could close it, and holding that lock would fail a
//   concurrent git command and could be left behind by a crash. Review 2ff6a0bc (round 2): a decision that bound no
//   version (git answered it, or the layout has no stamp) is asked again after content validation, and the entry is
//   kept unless that fresh check answers untracked.
// - D-2. The stamp holds the index file's own checksum trailer (git writes the SHA-1 of the content as its last 20
//   bytes, and ListIndex verifies it), so an equal-size rewrite that keeps the inode, size and both times inside one
//   timestamp tick still shows a new version. An index written in the last 2 s (git's racy-index rule) is never
//   reused: a rewrite in progress may not have reached its trailer yet.
// - D-3. The stamp holds the text of every config file the fresh read or git consults for the mapping (the git
//   directory's and the common directory's config and config.worktree, libgit2's global, XDG, system and ProgramData
//   files, GIT_CONFIG_GLOBAL and GIT_CONFIG_SYSTEM) and the variables that move them. Any include, per-worktree
//   config (a config.worktree file or extensions.worktreeConfig) in any of those files declines reuse: an included
//   file is not stamped. A change anywhere reads the index again through every IndexListing check.
// - Review 2b79e113 (round 3) blocker 1: a stamp that declines reuse binds no decision either, so an include, a
//   per-worktree config or a racy index sends the entry to a fresh lookup (no reuse) after content validation.
internal sealed class TrackedIndexScope
{
    private string? _owner;
    private TrackedIndexStamp? _stamp;
    private HashSet<string>? _entries;
    private HashSet<string>? _directories;

    // The checkout and the stamp observed when the current entry's tracked check was made (whoever answered it), and
    // whether the in-process listing answered it from that stamp's version. Review 2ff6a0bc (round 2) D-1: an
    // unbound decision never permits the unlink by itself; the caller asks again after content validation
    // (CheckpointOutputLedger).
    private string? _observedOwner;
    private TrackedIndexStamp? _observed;
    private bool _bound;

    internal void Clear()
    {
        _owner = null;
        _stamp = null;
        _entries = null;
        _directories = null;
    }

    internal void BeginDecision()
    {
        _observedOwner = null;
        _observed = null;
        _bound = false;
    }

    // Review 2b79e113 (round 3) blocker 1: what the current entry's check saw, even when git answered it or the stamp
    // is incomplete, so a change after the decision is still seen at the final check (ObservationChanged).
    internal void Observe(string owner, TrackedIndexStamp stamp)
    {
        _observedOwner = owner;
        _observed = stamp;
        _bound = false;
    }

    // The in-process listing answered from this stamp's version. Review 2b79e113 (round 3) blocker 1: only a complete,
    // settled stamp (Reusable: every config input observed and none of them includes another, no per-worktree
    // config, index outside the racy window) binds a decision. Any other decision stays unbound, so the caller asks
    // again with no reuse after content validation.
    internal void Decide(string owner, TrackedIndexStamp stamp)
    {
        Observe(owner, stamp);
        _bound = stamp.Reusable;
    }

    // Whether the current entry's decision was read in process from a complete index version it recorded.
    internal bool DecisionBound => _bound && _observed is { Reusable: true } && _observedOwner is not null;

    // Whether the index and every config input the current entry's decision was read from are still the current
    // version, re-read now (after content validation). An unbound decision, a stamp that cannot be read now, or one
    // that is no longer complete (an include or per-worktree config appeared) does not hold.
    internal bool DecisionStillHolds(CheckpointCleanupContext context) =>
        DecisionBound
        && TrackedIndexStamp.Read(context, _observedOwner!) is { Reusable: true } fresh
        && _observed!.SameVersion(fresh);

    // Review 2b79e113 (round 3) blocker 1: for an unbound decision, whether anything the stamp observed at the
    // decision (the index, the config files and every file they include, the variables) is different now, or can no
    // longer be read. False when the decision observed no stamp (nothing to compare; the fresh lookup decides).
    internal bool ObservationChanged(CheckpointCleanupContext context) =>
        _observed is not null && _observedOwner is not null
        && !_observed.SameVersion(TrackedIndexStamp.Read(context, _observedOwner));

    // Keeps the listing only for a stamp that may be reused (settled, no include or per-worktree config).
    internal void Store(string owner, TrackedIndexStamp stamp, string listing)
    {
        Clear();
        if (!stamp.Reusable)
            return;
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var entries = new HashSet<string>(comparer);
        var directories = new HashSet<string>(comparer);
        foreach (var entry in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            entries.Add(entry);
            for (var at = entry.IndexOf('/'); at > 0; at = entry.IndexOf('/', at + 1))
                directories.Add(entry[..at]);
        }

        _owner = owner;
        _stamp = stamp;
        _entries = entries;
        _directories = directories;
    }

    // The same answer as SafeDelete's scan of the listing: an entry equal to a path, or under it as a directory,
    // or any entry at all for the checkout root. Null when the scope holds nothing for this stamp.
    internal bool? Answer(string owner, TrackedIndexStamp? stamp, IReadOnlyList<string> relatives)
    {
        if (stamp is null || !stamp.Reusable || _stamp is null || _entries is null || _directories is null
            || !string.Equals(owner, _owner, StringComparison.Ordinal) || stamp != _stamp)
            return null;
        foreach (var relative in relatives)
            if (relative.Length == 0 ? _entries.Count > 0 : _entries.Contains(relative) || _directories.Contains(relative))
                return true;
        return false;
    }
}

internal sealed record TrackedIndexStamp(
    string GitDirectory,
    string DotGitText,
    CheckpointStat DotGit,
    CheckpointStat Index,
    string IndexVersion,
    CheckpointStat CommonDirFile,
    string CommonDirText,
    string Configs,
    string Environment,
    bool Reusable)
{
    private const int MaxPointerBytes = 4096;
    private const int MaxConfigBytes = 1024 * 1024;
    private const int ChecksumBytes = 20;
    private const long RacyNanoseconds = 2_000_000_000L;

    // The variables IndexListing declines on, and those that move the config files git or libgit2 reads.
    private static readonly string[] StampedVariables =
    [
        "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_NAMESPACE",
        "GIT_REPLACE_REF_BASE", "GIT_SHALLOW_FILE", "GIT_GRAFT_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE",
        "GIT_CONFIG", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM", "GIT_CONFIG_COUNT",
        "GIT_CONFIG_PARAMETERS", "HOME", "XDG_CONFIG_HOME", "USERPROFILE", "PROGRAMDATA", "ANTIPHON_GIT_INPROCESS",
    ];

    // The same index version: every observed part equal, whether or not either stamp may be reused.
    internal bool SameVersion(TrackedIndexStamp? other) =>
        other is not null && this with { Reusable = false } == other with { Reusable = false };

    // Null (no reuse and no binding) for anything other than a plain .git directory or a plain "gitdir: <path>" file,
    // an index that is not a regular file with an inode, a change time and a checksum trailer, a config file that is
    // not a regular file, and any unreadable part.
    internal static TrackedIndexStamp? Read(CheckpointCleanupContext context, string owner)
    {
        try
        {
            CheckpointStat Stat(string path) => context.IndexStampStat is { } seam ? seam(path, context.Io.CustodyStat(path)) : context.Io.CustodyStat(path);
            var dotGitPath = Path.Combine(owner, ".git");
            var dotGit = Stat(dotGitPath);
            string gitDirectory;
            var dotGitText = "";
            if (dotGit.Kind == "directory")
                gitDirectory = dotGitPath;
            else if (dotGit.Kind == "file" && Pointer(dotGitPath, dotGit, "gitdir: ") is { } target)
            {
                dotGitText = target;
                gitDirectory = Path.GetFullPath(target, owner);
            }
            else
                return null;

            var commonDirPath = Path.Combine(gitDirectory, "commondir");
            var commonDirFile = Stat(commonDirPath);
            var commonDirText = "";
            var common = gitDirectory;
            if (commonDirFile.Kind == "file")
            {
                if (Pointer(commonDirPath, commonDirFile, "") is not { } pointer)
                    return null;
                commonDirText = pointer;
                common = Path.GetFullPath(pointer, gitDirectory);
            }
            else if (commonDirFile.Kind != "absent")
                return null;

            var configs = new StringBuilder();
            var declines = false;
            var repositoryFiles = new List<string> { Path.Combine(gitDirectory, "config"), Path.Combine(gitDirectory, "config.worktree") };
            if (!string.Equals(Path.GetFullPath(common), Path.GetFullPath(gitDirectory), StringComparison.Ordinal))
            {
                repositoryFiles.Add(Path.Combine(common, "config"));
                repositoryFiles.Add(Path.Combine(common, "config.worktree"));
            }
            // Review 2b79e113 (round 3) blocker 1: an include (include.path or includeIf) in ANY consulted file,
            // repository or user/system, may hold settings the scope cannot interpret, so the stamp is never complete:
            // it is neither reused nor bound, and the entry's unlink waits for a fresh lookup after content validation.
            // The included files are still observed (path, metadata and bytes, recursively, whatever the include's
            // condition), so a change to any of them between the decision and the unlink keeps the entry.
            var included = new Queue<(string File, int Depth)>();
            foreach (var file in repositoryFiles.Concat(UserAndSystemConfigs()))
            {
                if (Config(file, Stat, configs) is not { } text)
                    return null;
                if (file.EndsWith("config.worktree", StringComparison.Ordinal) && text.Exists
                    || text.Content.Contains("include", StringComparison.OrdinalIgnoreCase)
                    || text.Content.Contains("worktreeconfig", StringComparison.OrdinalIgnoreCase))
                    declines = true;
                Includes(file, text.Content, 1, included, configs);
            }
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (included.TryDequeue(out var next))
            {
                if (next.Depth > MaxIncludeDepth)
                {
                    configs.Append("include-depth-exceeded\0");
                    continue;
                }
                if (!visited.Add(next.File))
                    continue;
                if (IncludedConfig(next.File, Stat, configs) is { } content)
                    Includes(next.File, content, next.Depth + 1, included, configs);
            }

            var environment = string.Join("\0", StampedVariables.Select(name => name + "=" + System.Environment.GetEnvironmentVariable(name)));
            // Review 2ff6a0bc: the index is observed last, so a final check's trailer read is the stamp's latest one.
            var indexPath = Path.Combine(gitDirectory, "index");
            var index = Stat(indexPath);
            if (index.Kind != "file" || index.Inode == 0 || index.Changed == 0)
                return null;
            if (Checksum(indexPath) is not { } indexVersion)
                return null;
            var now = context.Time.GetUtcNow().ToUnixTimeMilliseconds() * 1_000_000L;
            var settled = index.Modified <= now - RacyNanoseconds;
            return new TrackedIndexStamp(gitDirectory, dotGitText, dotGit, index, indexVersion, commonDirFile,
                commonDirText, configs.ToString(), environment, Reusable: settled && !declines);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or System.Text.DecoderFallbackException
                                   or DllNotFoundException or TypeInitializationException or BadImageFormatException
                                   or EntryPointNotFoundException or LibGit2SharpException)
        {
            return null;
        }
    }

    // The index's length and its trailing checksum: the version of its whole content.
    private static string? Checksum(string path)
    {
        Span<byte> trailer = stackalloc byte[ChecksumBytes];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length < 12 + ChecksumBytes)
            return null;
        stream.Seek(length - ChecksumBytes, SeekOrigin.Begin);
        stream.ReadExactly(trailer);
        return length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Convert.ToHexString(trailer);
    }

    private readonly record struct ConfigText(bool Exists, string Content);

    // git's own include depth limit.
    private const int MaxIncludeDepth = 10;

    // Queues every "path = <value>" in one config file (include.path and includeIf.<condition>.path; any other
    // section's "path" key too, which only observes one more file), resolved as git resolves an include: "~/" from
    // HOME, a relative path from the including file's directory. A value the scope cannot resolve is recorded as such.
    private static void Includes(string file, string content, int depth, Queue<(string, int)> into, StringBuilder stamp)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.StartsWith('['))
            {
                var close = line.IndexOf(']');
                if (close < 0)
                    continue;
                line = line[(close + 1)..].TrimStart();
            }
            if (!line.StartsWith("path", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = line[4..].TrimStart();
            if (!rest.StartsWith('='))
                continue;
            if (IncludeValue(rest[1..]) is not { } value || ResolveInclude(value, file) is not { } target)
            {
                stamp.Append("unresolved-include\0").Append(file).Append('\0').Append(line).Append('\0');
                continue;
            }
            into.Enqueue((target, depth));
        }
    }

    // A config value: quotes removed, \\ \" \n \t \b unescaped, a ; or # outside quotes ends it. Null for a
    // continuation line or any other escape.
    private static string? IncludeValue(string text)
    {
        var value = new StringBuilder();
        var quoted = false;
        var trailing = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\\')
            {
                if (++i >= text.Length)
                    return null;
                var escaped = text[i] switch { '\\' => '\\', '"' => '"', 'n' => '\n', 't' => '\t', 'b' => '\b', _ => '\0' };
                if (escaped == '\0')
                    return null;
                value.Append(escaped);
                trailing = value.Length;
                continue;
            }
            if (c == '"') { quoted = !quoted; trailing = value.Length; continue; }
            if (!quoted && (c == ';' || c == '#'))
                break;
            if (!quoted && value.Length == 0 && char.IsWhiteSpace(c))
                continue;
            value.Append(c);
            if (quoted || !char.IsWhiteSpace(c))
                trailing = value.Length;
        }
        if (quoted)
            return null;
        var result = value.ToString(0, trailing);
        return result.Length == 0 ? null : result;
    }

    private static string? ResolveInclude(string value, string includingFile)
    {
        if (value.StartsWith("%(", StringComparison.Ordinal) || value.Any(char.IsControl))
            return null;
        if (value.StartsWith("~/", StringComparison.Ordinal) || value == "~")
        {
            var home = System.Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(home) && OperatingSystem.IsWindows())
                home = System.Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(home))
                return null;
            value = Path.Combine(home, value.Length > 2 ? value[2..] : "");
        }
        else if (value.StartsWith('~'))
            return null;
        var directory = Path.GetDirectoryName(includingFile);
        return string.IsNullOrEmpty(directory) ? null : Path.GetFullPath(value, directory);
    }

    // Appends one included file's path, metadata and bytes; anything but a regular file up to 1 MiB is recorded by
    // its metadata alone (its content is not followed). The content, for further includes.
    private static string? IncludedConfig(string path, Func<string, CheckpointStat> stat, StringBuilder into)
    {
        var observed = stat(path);
        into.Append("include\0").Append(path).Append('\0').Append(observed.ToString()).Append('\0');
        if (observed.Kind != "file" || observed.Size > MaxConfigBytes)
            return null;
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length > MaxConfigBytes)
            {
                into.Append("oversize\0");
                return null;
            }
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        var content = Encoding.Latin1.GetString(bytes);
        into.Append(content).Append('\0');
        return content;
    }

    // Appends one config file's path, metadata and exact bytes (Latin-1 keeps every byte) to the stamp. Null for
    // anything but a regular file or nothing at all, and for a file over 1 MiB.
    private static ConfigText? Config(string path, Func<string, CheckpointStat> stat, StringBuilder into)
    {
        var observed = stat(path);
        into.Append(path).Append('\0').Append(observed.ToString()).Append('\0');
        if (observed.Kind == "absent")
            return new ConfigText(false, "");
        if (observed.Kind != "file" || observed.Size > MaxConfigBytes)
            return null;
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length > MaxConfigBytes)
                return null;
            bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
        }
        var content = Encoding.Latin1.GetString(bytes);
        into.Append(content).Append('\0');
        return new ConfigText(true, content);
    }

    // libgit2's global, XDG, system and ProgramData files (what the fresh read's config view loads beside the
    // repository's), and the files git's child is pointed at instead.
    private static IEnumerable<string> UserAndSystemConfigs()
    {
        foreach (var (level, name) in new[]
                 {
                     (ConfigurationLevel.Global, ".gitconfig"), (ConfigurationLevel.Xdg, "config"),
                     (ConfigurationLevel.System, "gitconfig"), (ConfigurationLevel.ProgramData, "config"),
                 })
            foreach (var directory in GlobalSettings.GetConfigSearchPaths(level))
                if (!string.IsNullOrEmpty(directory))
                    yield return Path.GetFullPath(Path.Combine(directory, name));
        foreach (var variable in new[] { "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM" })
            if (System.Environment.GetEnvironmentVariable(variable) is { Length: > 0 } file)
                yield return Path.GetFullPath(file);
    }

    // A small pointer file's single line after the prefix (git strips only the trailing line end).
    private static string? Pointer(string path, CheckpointStat stat, string prefix)
    {
        if (stat.Size <= 0 || stat.Size > MaxPointerBytes)
            return null;
        var text = File.ReadAllText(path, new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true));
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var target = text[prefix.Length..].TrimEnd('\n', '\r');
        if (target.Length == 0 || target.Any(char.IsControl) || target != target.Trim())
            return null;
        return target;
    }
}
