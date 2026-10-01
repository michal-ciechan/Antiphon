using System.Buffers.Binary;
using System.Diagnostics;
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

public sealed class SourceSnapshot
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex FullOid = new("^(?:[0-9a-f]{40}|[0-9a-f]{64})$", RegexOptions.Compiled);
    private readonly Func<string, IReadOnlyList<string>, byte[]> _git;

    public SourceSnapshot(Func<string, IReadOnlyList<string>, byte[]>? git = null)
    {
        _git = git ?? RunGit;
    }

    public SourceObservation Capture(string repository)
    {
        try
        {
            // Resolve a nested caller to the worktree root before hashing untracked paths.
            // Git's status paths are relative to that root, including for linked worktrees.
            var root = StrictUtf8.GetString(_git(repository, ["rev-parse", "--show-toplevel"])).Trim();
            if (string.IsNullOrWhiteSpace(root) || !Path.IsPathRooted(root))
                throw new SourceCaptureException("root_invalid");
            var first = CaptureOnce(root);
            var second = CaptureOnce(root);
            if (first.Commit != second.Commit || first.DirtyFiles != second.DirtyFiles ||
                first.Fingerprint != second.Fingerprint)
                return SourceObservation.Unknown("capture_changed");
            return second with { ObservedAtUtc = DateTimeOffset.UtcNow };
        }
        catch (SourceCaptureException ex) { return SourceObservation.Unknown(ex.Code); }
        catch (IOException) { return SourceObservation.Unknown("file_io"); }
        catch (UnauthorizedAccessException) { return SourceObservation.Unknown("file_access"); }
        catch (DecoderFallbackException) { return SourceObservation.Unknown("path_encoding"); }
        catch (Exception) { return SourceObservation.Unknown("capture_failed"); }
    }

    private SourceObservation CaptureOnce(string repository)
    {
        var commit = StrictUtf8.GetString(_git(repository, ["rev-parse", "HEAD"])).Trim();
        if (!FullOid.IsMatch(commit)) throw new SourceCaptureException("head_invalid");
        var status = _git(repository, ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--ignore-submodules=none"]);
        var entries = ParseStatus(status);
        var headDiff = _git(repository, ["-c", "core.quotePath=false", "diff", "HEAD", "--binary", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames"]);
        var cachedDiff = _git(repository, ["-c", "core.quotePath=false", "diff", "--cached", "--binary", "--no-ext-diff", "--no-textconv", "--no-color", "--no-renames"]);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, [1]);
        Add(hash, StrictUtf8.GetBytes(commit));
        Add(hash, status);
        Add(hash, headDiff);
        Add(hash, cachedDiff);
        foreach (var path in entries.Untracked.OrderBy(p => p, ByteArrayComparer.Instance))
        {
            Add(hash, path);
            var relative = StrictUtf8.GetString(path);
            var full = Path.GetFullPath(Path.Combine(repository, relative));
            var within = Path.GetRelativePath(Path.GetFullPath(repository), full);
            if (within != ".." && !within.StartsWith("../", StringComparison.Ordinal) &&
                !within.StartsWith("..\\", StringComparison.Ordinal))
            {
                var info = new FileInfo(full);
                if (!info.Exists && info.LinkTarget is null) throw new SourceCaptureException("untracked_missing");
                byte[] digest;
                if (info.LinkTarget is string target)
                    digest = SHA256.HashData(StrictUtf8.GetBytes(target));
                else
                {
                    var beforeLength = info.Length;
                    var beforeWrite = info.LastWriteTimeUtc;
                    using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
                    digest = SHA256.HashData(stream);
                    info.Refresh();
                    if (info.Length != beforeLength || info.LastWriteTimeUtc != beforeWrite)
                        throw new SourceCaptureException("file_changed");
                }
                Add(hash, digest);
            }
            else throw new SourceCaptureException("path_escape");
        }
        return new(commit, entries.Count, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
            DateTimeOffset.UtcNow, "known");
    }

    private static (int Count, List<byte[]> Untracked) ParseStatus(byte[] bytes)
    {
        var count = 0;
        var untracked = new List<byte[]>();
        var index = 0;
        while (index < bytes.Length)
        {
            var end = Array.IndexOf(bytes, (byte)0, index);
            if (end < index || end - index < 4 || bytes[index + 2] != (byte)' ')
                throw new SourceCaptureException("status_invalid");
            var x = bytes[index];
            var y = bytes[index + 1];
            if (x == (byte)'U' || y == (byte)'U' || x == (byte)'A' && y == (byte)'A' ||
                x == (byte)'D' && y == (byte)'D')
                throw new SourceCaptureException("unmerged_index");
            var path = bytes[(index + 3)..end];
            if (x == (byte)'?' && y == (byte)'?') untracked.Add(path);
            else if (x == (byte)'m' || y == (byte)'m' || x == (byte)'?' || y == (byte)'?')
                throw new SourceCaptureException("submodule_unsupported");
            count++;
            index = end + 1;
            if (x is (byte)'R' or (byte)'C' || y is (byte)'R' or (byte)'C')
            {
                end = Array.IndexOf(bytes, (byte)0, index);
                if (end < index) throw new SourceCaptureException("status_invalid");
                index = end + 1;
            }
        }
        return (count, untracked);
    }

    private static void Add(IncrementalHash hash, ReadOnlySpan<byte> bytes)
    {
        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static byte[] RunGit(string repository, IReadOnlyList<string> args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        if (!process.Start()) throw new SourceCaptureException("git_start");
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(true);
            throw new SourceCaptureException("git_timeout");
        }
        copy.GetAwaiter().GetResult();
        error.GetAwaiter().GetResult();
        if (process.ExitCode != 0) throw new SourceCaptureException("git_exit");
        return output.ToArray();
    }

    private sealed class SourceCaptureException(string code) : Exception
    {
        public string Code { get; } = code;
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
