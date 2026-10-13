using System.Collections.Concurrent;

namespace Antiphon.Checkpoints;

public enum WriteKind
{
    Ordinary,
    ToolOwnedReplaceable,
}

public sealed class WriteOwnership
{
    public IReadOnlyCollection<string> Roots { get; init; } = [];
    public WriteKind Kind { get; init; } = WriteKind.Ordinary;
    public string? RunId { get; init; }
    public string? GitExecutable { get; init; }
    public int GitTimeoutMilliseconds { get; init; } = 15_000;
    public bool AllowMissingRepository { get; init; } = true;
}

public static class SafeWrite
{
    public const string Marker = "antiphon-checkpoints";

    private static readonly ConcurrentDictionary<string, byte> Created = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal);

    // Append-only creation records stay in this process. Disk ledgers are diagnostic only.
    private static readonly ConcurrentDictionary<string, string> CreatedContent = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static void RecordContent(string path)
    {
        if (Directory.Exists(path)) CreatedContent.TryAdd(path, "directory");
        else
        {
            using var stream = File.OpenRead(path);
            CreatedContent.TryAdd(path, stream.Length + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)));
        }
    }
    public static bool MatchesCreation(string path)
    {
        if (!CreatedContent.TryGetValue(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), out var recorded)) return false;
        if (Directory.Exists(path)) return recorded == "directory";
        using var stream = File.OpenRead(path);
        return recorded == stream.Length + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
    }

    public static string? Preflight(string repository, IReadOnlyCollection<string> roots, string? gitExecutable = null, int? timeoutMs = null)
    {
        foreach (var root in roots)
        {
            var link = LinkRefusal(root);
            if (link is not null)
                return link;
        }

        return SafeDelete.PreflightRoots(repository, roots, gitExecutable, timeoutMs);
    }

    public static string? Refusal(string path, WriteOwnership ownership)
    {
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        if (!InsideRoots(full, ownership.Roots))
            return "outside-root";
        var link = LinkRefusal(full);
        if (link is not null)
            return link;
        var git = SafeDelete.PreflightRoots(full, [full], ownership.GitExecutable, ownership.GitTimeoutMilliseconds);
        if (git is not null)
            return git;
        if (!File.Exists(full) && !Directory.Exists(full))
            return null;
        if (Created.ContainsKey(full))
            return null;
        if (ownership.Kind == WriteKind.ToolOwnedReplaceable
            && !string.IsNullOrEmpty(ownership.RunId)
            && CarriesMarker(full, ownership.RunId))
            return null;
        return "unrecorded-existing";
    }

    public static string? Text(string path, string content, WriteOwnership ownership)
    {
        var reason = Refusal(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            Created.TryAdd(temporary, 0);
            reason = Refusal(full, ownership);
            if (reason is not null)
                return reason;
            File.Move(temporary, full, overwrite: true);
            Created.TryAdd(full, 0);
            RecordContent(full);
            return null;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static string? Bytes(string path, ReadOnlySpan<byte> content, WriteOwnership ownership)
    {
        var reason = Refusal(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        var temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, content.ToArray());
            Created.TryAdd(temporary, 0);
            reason = Refusal(full, ownership);
            if (reason is not null)
                return reason;
            File.Move(temporary, full, overwrite: true);
            Created.TryAdd(full, 0);
            RecordContent(full);
            return null;
        }
        finally
        {
            if (File.Exists(temporary))
            {
                try { File.Delete(temporary); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public static string? Append(string path, string content, WriteOwnership ownership)
    {
        var reason = AllowExisting(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        if (Directory.Exists(full))
            return "unrecorded-existing";
        if (!File.Exists(full))
            return Text(path, content, ownership);
        File.AppendAllText(full, content);
        Created.TryAdd(full, 0);
        RecordContent(full);
        return null;
    }

    public static string? CopyOverwrite(string source, string destination, WriteOwnership ownership)
    {
        var reason = Refusal(destination, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(destination)!;
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        File.Copy(source, full, overwrite: true);
        Created.TryAdd(full, 0);
        RecordContent(full);
        return null;
    }

    public static string? MoveOverwrite(string source, string destination, WriteOwnership ownership)
    {
        var reason = Refusal(destination, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(destination)!;
        File.Move(source, full, overwrite: true);
        Created.TryAdd(full, 0);
        RecordContent(full);
        return null;
    }

    public static string? CreateDirectory(string path, WriteOwnership ownership)
    {
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        if (Directory.Exists(full) && LinkRefusal(full) is null && !File.Exists(full))
        {
            if (!InsideRoots(full, ownership.Roots) && !ContainsRoot(full, ownership.Roots))
                return "outside-root";
            return LinkRefusal(full);
        }

        var reason = Refusal(full, ownership);
        if (reason is not null)
            return reason;
        Directory.CreateDirectory(full);
        Created.TryAdd(full, 0);
        RecordContent(full);
        return null;
    }

    public static bool WasCreated(string path) => Created.ContainsKey(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    public static string? CreateFreshDirectory(string path, WriteOwnership ownership)
    {
        var reason = Refusal(path, ownership);
        if (reason is not null) return reason;
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        reason = ExclusiveDirectory.Create(full);
        if (reason is not null) return reason;
        Created.TryAdd(full, 0);
        RecordContent(full);
        return Text(Path.Combine(full, ".checkpoint-output-owner.json"), Guid.NewGuid().ToString("N"), Under(full));
    }

    public static string? OpenCreate(string path, WriteOwnership ownership, out FileStream? stream)
    {
        stream = null;
        var reason = Refusal(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Created.TryAdd(full, 0);
        return null;
    }

    public static string? OpenAppend(string path, WriteOwnership ownership, out FileStream? stream,
        FileShare share = FileShare.ReadWrite | FileShare.Delete)
    {
        stream = null;
        var reason = AllowExisting(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        if (Directory.Exists(full))
            return "unrecorded-existing";
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        stream = new FileStream(full, FileMode.Append, FileAccess.Write, share);
        Created.TryAdd(full, 0);
        return null;
    }

    public static string? OpenGate(string path, WriteOwnership ownership, out FileStream? stream)
    {
        stream = null;
        var reason = AllowExisting(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        if (Directory.Exists(full))
            return "unrecorded-existing";
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        stream = new FileStream(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Created.TryAdd(full, 0);
        return null;
    }

    public static void Require(string? reason)
    {
        if (reason is not null)
            throw new IOException("refused: " + reason);
    }

    public static bool CarriesMarker(string path, string runId)
    {
        try
        {
            if (!File.Exists(path))
                return false;
            var text = File.ReadAllText(path);
            if (text.StartsWith(Marker + " " + runId, StringComparison.Ordinal))
                return true;
            return text.Contains("\"toolOwner\": \"" + Marker + "\"", StringComparison.Ordinal)
                && (text.Contains("\"runId\": \"" + runId + "\"", StringComparison.Ordinal)
                    || text.Contains("\"runId\":\"" + runId + "\"", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static WriteOwnership Under(string root, string? runId = null, WriteKind kind = WriteKind.Ordinary, string? gitExecutable = null) =>
        new()
        {
            Roots = [Path.GetFullPath(root)],
            RunId = runId,
            Kind = kind,
            GitExecutable = gitExecutable,
        };

    public static string FormatOutputs(IReadOnlyList<string> labels, IReadOnlyList<string> paths)
    {
        if (labels.Count == 0 && paths.Count == 0)
            return "outputs: none";
        var kept = new List<string>();
        var deleted = new List<string>();
        var count = Math.Max(labels.Count, paths.Count);
        for (var i = 0; i < count; i++)
        {
            var label = i < labels.Count ? labels[i] : Path.GetFileName(paths[i].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + "/";
            var path = i < paths.Count ? paths[i] : null;
            var present = path is not null && (Directory.Exists(path) || File.Exists(path));
            if (path is null || present)
                kept.Add(label);
            else
                deleted.Add(label);
        }

        if (kept.Count > 0)
            return "outputs: refused/kept " + string.Join(", ", kept);
        return "outputs: deleted " + string.Join(", ", deleted);
    }

    private static string? AllowExisting(string path, WriteOwnership ownership)
    {
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        if (!InsideRoots(full, ownership.Roots))
            return "outside-root";
        var link = LinkRefusal(full);
        if (link is not null)
            return link;
        return SafeDelete.PreflightRoots(full, [full], ownership.GitExecutable, ownership.GitTimeoutMilliseconds);
    }

    private static string? LinkRefusal(string path)
    {
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        for (var current = full; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (IsLink(current))
                return "link-component";
        }

        return null;
    }

    private static bool IsLink(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(new DirectoryInfo(path).LinkTarget) || !string.IsNullOrEmpty(new FileInfo(path).LinkTarget))
                return true;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static bool InsideRoots(string full, IReadOnlyCollection<string> roots)
    {
        foreach (var root in roots)
        {
            if (OutputCleanup.IsStrictlyInside(root, full) || Same(root, full))
                return true;
        }

        return false;
    }

    private static bool ContainsRoot(string directory, IReadOnlyCollection<string> roots)
    {
        foreach (var root in roots)
        {
            var full = Canonical(root);
            if (full is not null && (OutputCleanup.IsStrictlyInside(directory, full) || Same(directory, full)))
                return true;
        }

        return false;
    }

    private static bool Same(string root, string full)
    {
        var canonical = Canonical(root);
        if (canonical is null)
            return false;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(canonical, full, comparison);
    }

    private static string? Canonical(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
