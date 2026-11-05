using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Git;

/// <summary>What an earlier guarded removal set aside, recorded before the move.</summary>
internal sealed record WorktreeSetAsideRecord(string WorktreePath, string SetAsidePath, string GitDirectory, DateTime RecordedAt,
    string? DirectoryIdentity = null, string? ContentSeal = null,
    string? EntryManifest = null, string? EvidenceManifest = null);

/// <summary>One approved set-aside entry. <see cref="Hash"/> is SHA-256 for a content-bound file and null for a name-only entry.</summary>
internal sealed record WorktreeApprovedEntry(string Path, string Kind, string? Hash);

/// <summary>A retained evidence copy the receipt can re-check without trusting the listing seal.</summary>
internal sealed record EvidencePin(string Relative, string RetainedPath, string Sha256);

/// <summary>
/// CARD-0665 (review 0c0b9a4e item 1): the set-aside tree has one deterministic name per worktree
/// path and a record under <c>&lt;common&gt;/antiphon/worktree-removal/</c> written before the move.
/// A pass that is stopped part-way (a locked file, a crash) leaves the record, so a later pass
/// finishes the same tree instead of seeing an absent, unregistered path and calling it done.
/// </summary>
internal static class WorktreeSetAside
{
    public static string SetAsidePath(string worktreePath)
    {
        var full = Normalize(worktreePath);
        var parent = Path.GetDirectoryName(full) ?? throw new IOException("worktree_root_has_no_parent");
        return Path.Combine(parent, $".{Path.GetFileName(full)}.removing-{Key(full)}");
    }

    /// <summary>Where Git's administrative entry is moved when its registration is dropped.</summary>
    public static string RetiredAdminPath(string commonDirectory, string worktreePath) =>
        Path.Combine(RecordDirectory(commonDirectory), Key(Normalize(worktreePath)) + ".admin");

    public static void Record(string commonDirectory, WorktreeSetAsideRecord record, WorktreeRenames? renames = null)
    {
        var path = RecordPath(commonDirectory, record.WorktreePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, record);
            stream.Flush(flushToDisk: true);
        }
        // A scanner may hold the fresh temporary file or the record for a moment.
        var r = renames ?? WorktreeRenames.System;
        r.Retrying(r.ReplaceFile, temporary, path);
    }

    /// <summary>
    /// SHA-256 of the sorted no-follow listing (kind, relative path, length, UTC write ticks).
    /// The worktree's own <c>.git</c> entry is omitted. A same-size same-mtime byte edit does not change it.
    /// </summary>
    internal static string ListingSeal(string root)
    {
        var lines = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(root, entry).Replace(Path.DirectorySeparatorChar, '/');
                if (relative.Equals(".git", StringComparison.OrdinalIgnoreCase)) continue;
                var stamp = WorktreeNoFollowDelete.TryStamp(entry) ?? throw new IOException("listing_seal_entry_absent: " + relative);
                lines.Add($"{stamp.Kind}\t{relative}\t{stamp.Length}\t{stamp.MtimeTicks}");
                if (stamp.Kind == "dir") pending.Push(entry);
            }
        }
        lines.Sort(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
    }

    /// <summary>Null when nothing is recorded; an unreadable record is an I/O error, never absence.</summary>
    public static WorktreeSetAsideRecord? Read(string commonDirectory, string worktreePath)
    {
        string text;
        try { text = File.ReadAllText(RecordPath(commonDirectory, worktreePath)); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
        try { return JsonSerializer.Deserialize<WorktreeSetAsideRecord>(text) ?? throw new IOException("set_aside_record_unreadable"); }
        catch (JsonException) { throw new IOException("set_aside_record_unreadable"); }
    }

    /// <summary>Drops the record and any retired administrative entry once the set-aside tree is gone.</summary>
    public static bool TryClear(string commonDirectory, string worktreePath)
    {
        try
        {
            var admin = RetiredAdminPath(commonDirectory, worktreePath);
            if (Directory.Exists(admin)) WorktreeNoFollowDelete.Delete(admin);
            File.Delete(RecordPath(commonDirectory, worktreePath));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static string RecordPath(string commonDirectory, string worktreePath) =>
        Path.Combine(RecordDirectory(commonDirectory), Key(Normalize(worktreePath)) + ".json");

    private static string RecordDirectory(string commonDirectory) => Path.Combine(commonDirectory, "antiphon", "worktree-removal");

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    // Paths compare case-insensitively on Windows (LandingGit.PathsEqual), so the key does too.
    private static string Key(string full) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full)))[..16];
}
