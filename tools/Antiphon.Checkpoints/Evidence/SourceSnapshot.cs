using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Checkpoints;

public sealed record SourceObservation(
    string? Commit,
    int? DirtyFiles,
    string? Fingerprint,
    DateTimeOffset ObservedAtUtc,
    string CaptureStatus,
    string? ErrorCode = null)
{
    public static SourceObservation Unknown(string code) =>
        new(null, null, null, DateTimeOffset.UtcNow, "unknown", code);
}

public sealed class SourceEvidence
{
    public int Version { get; set; } = 1;
    public SourceObservation Start { get; set; } = SourceObservation.Unknown("not_observed");
    public SourceObservation? End { get; set; }
    public string State { get; set; } = "unknown";
    public string BuildSource { get; set; } = "unknown";

    public static string StateOf(SourceObservation start, SourceObservation? end)
    {
        if (start.CaptureStatus != "known" || end?.CaptureStatus != "known") return "unknown";
        if (start.Commit != end.Commit || start.Fingerprint != end.Fingerprint || start.DirtyFiles != end.DirtyFiles)
            return "changed";
        return start.DirtyFiles == 0 ? "clean" : "dirty";
    }

    public static string Token(SourceObservation observation)
    {
        if (observation.CaptureStatus != "known" || observation.Commit is null)
            return "unknown";
        return observation.DirtyFiles == 0
            ? observation.Commit
            : observation.Commit + "+dirty:" + observation.Fingerprint;
    }
}


/// <summary>One path git status lists (tracked change or untracked file), with its HEAD and index entries.</summary>
public sealed record SourceReadEntry(string Path, string? HeadMode, string? HeadOid, string? IndexMode, string? IndexOid);

/// <summary>A reading of a checkout: its root, HEAD commit and the listed paths.</summary>
public sealed record SourceRead(string Root, string Commit, IReadOnlyList<SourceReadEntry> Entries);

/// <summary>A checkout the reader refuses, with the refusal code reported as the observation's error.</summary>
public sealed class SourceReadException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Source identity, fingerprint v2: SHA-256 over length-framed records, version 2, HEAD, then for each path git
/// status lists (ignored files excluded) in git's byte order: the path, its HEAD mode and id, its index mode and
/// id (empty when absent) and its work-tree content (<see cref="WorkTreeToken"/>). The checkout is read with
/// libgit2 in process (no git child); scripts/lib/checkpoint-source.ps1 reads the same facts from
/// <c>git status --porcelain=v2</c> and must produce the same identity. Captured twice; any difference is
/// <c>capture_changed</c>.
/// </summary>
public sealed class SourceSnapshot
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex FullOid = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.Compiled);
    private readonly Func<string, SourceRead> _read;

    public SourceSnapshot(Func<string, SourceRead>? read = null)
    {
        _read = read ?? ReadWithLibGit2;
    }

    public SourceObservation Capture(string repository)
    {
        try
        {
            var first = CaptureOnce(repository);
            var second = CaptureOnce(repository);
            if (first.Commit != second.Commit || first.DirtyFiles != second.DirtyFiles ||
                first.Fingerprint != second.Fingerprint)
                return SourceObservation.Unknown("capture_changed");
            return second with { ObservedAtUtc = DateTimeOffset.UtcNow };
        }
        catch (SourceReadException ex) { return SourceObservation.Unknown(ex.Code); }
        catch (IOException) { return SourceObservation.Unknown("file_io"); }
        catch (UnauthorizedAccessException) { return SourceObservation.Unknown("file_access"); }
        catch (DecoderFallbackException) { return SourceObservation.Unknown("path_encoding"); }
        catch (Exception) { return SourceObservation.Unknown("capture_failed"); }
    }

    private SourceObservation CaptureOnce(string repository)
    {
        var read = _read(repository);
        if (!FullOid.IsMatch(read.Commit)) throw new SourceReadException("head_invalid");
        if (string.IsNullOrWhiteSpace(read.Root) || !Path.IsPathRooted(read.Root))
            throw new SourceReadException("root_invalid");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, [2]);
        Add(hash, StrictUtf8.GetBytes(read.Commit));
        foreach (var entry in read.Entries.OrderBy(e => StrictUtf8.GetBytes(e.Path), ByteArrayComparer.Instance))
        {
            Add(hash, StrictUtf8.GetBytes(entry.Path));
            Add(hash, StrictUtf8.GetBytes(entry.HeadMode ?? ""));
            Add(hash, StrictUtf8.GetBytes(entry.HeadOid ?? ""));
            Add(hash, StrictUtf8.GetBytes(entry.IndexMode ?? ""));
            Add(hash, StrictUtf8.GetBytes(entry.IndexOid ?? ""));
            Add(hash, StrictUtf8.GetBytes(WorkTreeToken(read.Root, entry.Path)));
        }
        return new(read.Commit, read.Entries.Count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            DateTimeOffset.UtcNow, "known");
    }

    /// <summary>
    /// The work-tree side of a listed path: <c>file:&lt;sha256&gt;</c> (plus <c>:x</c> when executable off Windows),
    /// <c>link:&lt;target&gt;</c>, <c>directory</c> (a nested repository git lists as <c>dir/</c>) or <c>absent</c>.
    /// A file that changes while it is read is <c>file_changed</c>.
    /// </summary>
    internal static string WorkTreeToken(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative.TrimEnd('/')));
        var within = Path.GetRelativePath(Path.GetFullPath(root), full);
        if (within == ".." || within.StartsWith("../", StringComparison.Ordinal) ||
            within.StartsWith("..\\", StringComparison.Ordinal) || Path.IsPathRooted(within))
            throw new SourceReadException("path_escape");
        var info = new FileInfo(full);
        if (info.LinkTarget is string target) return "link:" + target;
        var directory = new DirectoryInfo(full);
        if (directory.LinkTarget is string directoryTarget) return "link:" + directoryTarget;
        if (directory.Exists) return "directory";
        if (!info.Exists) return "absent";
        var beforeLength = info.Length;
        var beforeWrite = info.LastWriteTimeUtc;
        var digest = Convert.ToHexString(SHA256.HashData(ReceiptFile.ReadBytes(full))).ToLowerInvariant();
        info.Refresh();
        if (info.Length != beforeLength || info.LastWriteTimeUtc != beforeWrite)
            throw new SourceReadException("file_changed");
        var executable = !OperatingSystem.IsWindows() && (info.UnixFileMode & UnixFileMode.UserExecute) != 0;
        return "file:" + digest + (executable ? ":x" : "");
    }

    private static SourceRead ReadWithLibGit2(string repository)
    {
        ToolLibGit2.EnsureIsolated();
        try
        {
            var reading = Antiphon.GitInProcess.WorkTreeIdentity.Read(repository);
            return new SourceRead(reading.Root, reading.Commit, reading.Entries
                .Select(e => new SourceReadEntry(e.Path, e.HeadMode, e.HeadOid, e.IndexMode, e.IndexOid)).ToList());
        }
        catch (Antiphon.GitInProcess.WorkTreeIdentityException ex) { throw new SourceReadException(ex.Code); }
        catch (LibGit2Sharp.LibGit2SharpException) { throw new SourceReadException("libgit2_failed"); }
    }

    private static void Add(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();
        public int Compare(byte[]? left, byte[]? right)
        {
            if (left is null || right is null) return left is null ? right is null ? 0 : -1 : 1;
            for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
                if (left[i] != right[i]) return left[i].CompareTo(right[i]);
            return left.Length.CompareTo(right.Length);
        }
    }
}
