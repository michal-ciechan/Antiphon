using Antiphon.HostCleanup.Checkpoints;

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
    public static void AdoptCreated(string path, OutputCustody.Window window) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).AdoptCreated(path, window);
    public static DateTimeOffset? FreshRootCreated(string path) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).FreshRootCreated(path);
    public static bool MatchesCreation(string path) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).MatchesCreation(path);
    internal static string? CreationRecord(string path) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).CreationRecord(path);
    public static string? Preflight(string repository, IReadOnlyCollection<string> roots, string? gitExecutable = null, int? timeoutMs = null) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Preflight(repository, roots, gitExecutable, timeoutMs);
    public static string? Refusal(string path, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Refusal(path, ownership);
    public static string? Text(string path, string content, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Text(path, content, ownership);
    public static string? Bytes(string path, ReadOnlySpan<byte> content, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Bytes(path, content, ownership);
    public static string? Append(string path, string content, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Append(path, content, ownership);
    public static string? CopyOverwrite(string source, string destination, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).CopyOverwrite(source, destination, ownership);
    public static string? MoveOverwrite(string source, string destination, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).MoveOverwrite(source, destination, ownership);
    public static string? CreateDirectory(string path, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).CreateDirectory(path, ownership);
    public static bool WasCreated(string path) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).WasCreated(path);
    public static string? CreateFreshDirectory(string path, WriteOwnership ownership) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).CreateFreshDirectory(path, ownership);
    public static string? OpenCreate(string path, WriteOwnership ownership, out FileStream? stream)
    {
        var reason = new CheckpointWrite(CheckpointCleanupDefaults.Context).OpenCreate(path, ownership, out var opened);
        stream = (FileStream?)opened;
        return reason;
    }
    public static string? OpenAppend(string path, WriteOwnership ownership, out FileStream? stream,
        FileShare share = FileShare.ReadWrite | FileShare.Delete)
    {
        var reason = new CheckpointWrite(CheckpointCleanupDefaults.Context).OpenAppend(path, ownership, out var opened, share);
        stream = (FileStream?)opened;
        return reason;
    }
    public static string? OpenGate(string path, WriteOwnership ownership, out FileStream? stream)
    {
        var reason = new CheckpointWrite(CheckpointCleanupDefaults.Context).OpenGate(path, ownership, out var opened);
        stream = (FileStream?)opened;
        return reason;
    }
    public static void Require(string? reason) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Require(reason);
    public static bool CarriesMarker(string path, string runId) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).CarriesMarker(path, runId);
    public static WriteOwnership Under(string root, string? runId = null, WriteKind kind = WriteKind.Ordinary, string? gitExecutable = null) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).Under(root, runId, kind, gitExecutable);
    public static string FormatOutputs(IReadOnlyList<string> labels, IReadOnlyList<string> paths) =>
        new CheckpointWrite(CheckpointCleanupDefaults.Context).FormatOutputs(labels, paths);
}

public sealed class CheckpointWrite(CheckpointCleanupContext context)
{
    private ICheckpointCleanupIo Io => context.Io;
    private CheckpointDelete SafeDelete => new(context);
    public const string Marker = "antiphon-checkpoints";

    // The context owns append-only creation records; disk ledgers never populate them.
    private void RecordContent(string path)
    {
        context.Registry.RecordContent(path, Describe(path));
        context.Registry.RecordIdentity(path, FilesystemIdentityReader.Read(Io, path));
    }
    private void Remember(string path)
    {
        context.Registry.RecordCreated(path);
        context.Registry.RecordIdentity(path, FilesystemIdentityReader.Read(Io, path));
    }
    private string Describe(string path)
    {
        if (Io.DirectoryExists(path)) return "directory";
        var digest = ReceiptFile.Digest(context, path);
        return digest.Length + ":" + digest.Sha256;
    }
    // Seal-time custody for entries a child process wrote under a root this process created
    // (OwnedOutputLedger.Seal). An existing record wins; an entry outside the custody window,
    // unreadable, or whose statx changes across the read stays unrecorded (OutputCustody).
    // A receipt read that hits its deadline is tried once more: a Windows filter can stall the
    // first read of a new image for that whole deadline. The second failure stays unrecorded.
    public void AdoptCreated(string path, OutputCustody.Window window)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var before = OutputCustody.Stat(context, full);
                if (before is null || OutputCustody.Refusal(before.Value, window) is not null) return;
                var record = Describe(full);
                if (OutputCustody.Stat(context, full) == before)
                {
                    context.Registry.RecordContent(full, record);
                    context.Registry.RecordIdentity(full, FilesystemIdentityReader.Read(Io, full));
                }
                return;
            }
            catch (IOException) when (attempt < 2) { }
            catch (UnauthorizedAccessException) when (attempt < 2) { }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
        }
    }
    public DateTimeOffset? FreshRootCreated(string path) =>
        context.Registry.FreshRoot(path);
    public bool MatchesIdentity(string path)
    {
        if (context.Registry.Identity(path) is not { } expected) return false;
        return expected.SameAs(FilesystemIdentityReader.Read(Io, path));
    }
    public bool MatchesCreation(string path)
    {
        if (context.Registry.Content(path) is not { } recorded) return false;
        if (!MatchesIdentity(path)) return false;
        if (Io.DirectoryExists(path)) return recorded == "directory";
        return recorded == Describe(path);
    }

    internal string? CreationRecord(string path) =>
        context.Registry.Content(path);

    public string? Preflight(string repository, IReadOnlyCollection<string> roots, string? gitExecutable = null, int? timeoutMs = null)
    {
        foreach (var root in roots)
        {
            var link = LinkRefusal(root);
            if (link is not null)
                return link;
        }

        return SafeDelete.PreflightRoots(repository, roots, gitExecutable, timeoutMs);
    }

    public string? Refusal(string path, WriteOwnership ownership)
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
        if (!Io.FileExists(full) && !Io.DirectoryExists(full))
            return null;
        if (context.Registry.WasCreated(full))
            return null;
        if (ownership.Kind == WriteKind.ToolOwnedReplaceable
            && !string.IsNullOrEmpty(ownership.RunId)
            && CarriesMarker(full, ownership.RunId))
            return null;
        return "unrecorded-existing";
    }

    public string? Text(string path, string content, WriteOwnership ownership)
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
        var payload = System.Text.Encoding.UTF8.GetBytes(content);
        var armed = false;
        try
        {
            Io.WriteBytes(temporary, payload);
            Remember(temporary);
            reason = Refusal(full, ownership);
            if (reason is not null)
                return reason;
            armed = true;
            ReplacementPublication.Publish(Io, temporary, full, payload);
            Remember(full);
            RecordContent(full);
            return null;
        }
        finally
        {
            if (!armed && Io.FileExists(temporary))
            {
                try { Io.Unlink(temporary); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public string? Bytes(string path, ReadOnlySpan<byte> content, WriteOwnership ownership)
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
        var payload = content.ToArray();
        var armed = false;
        try
        {
            Io.WriteBytes(temporary, payload);
            Remember(temporary);
            reason = Refusal(full, ownership);
            if (reason is not null)
                return reason;
            armed = true;
            ReplacementPublication.Publish(Io, temporary, full, payload);
            Remember(full);
            RecordContent(full);
            return null;
        }
        finally
        {
            if (!armed && Io.FileExists(temporary))
            {
                try { Io.Unlink(temporary); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public string? Append(string path, string content, WriteOwnership ownership)
    {
        var reason = AllowExisting(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        if (Io.DirectoryExists(full))
            return "unrecorded-existing";
        if (!Io.FileExists(full))
            return Text(path, content, ownership);
        Io.AppendText(full, content);
        Remember(full);
        RecordContent(full);
        return null;
    }

    public string? CopyOverwrite(string source, string destination, WriteOwnership ownership)
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

        Io.Copy(source, full, overwrite: true);
        Remember(full);
        RecordContent(full);
        return null;
    }

    public string? MoveOverwrite(string source, string destination, WriteOwnership ownership)
    {
        var reason = Refusal(destination, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(destination)!;
        ReplacementPublication.Publish(Io, source, full, ReadNoFollow(source));
        Remember(full);
        RecordContent(full);
        return null;
    }

    private byte[] ReadNoFollow(string path)
    {
        using var handle = Io.OpenRead(path, new CheckpointReadOpen(0x20000, 0x00200000));
        using var stream = handle.OpenStream();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public string? CreateDirectory(string path, WriteOwnership ownership)
    {
        var full = Canonical(path);
        if (full is null)
            return "path-unsafe";
        if (Io.DirectoryExists(full) && LinkRefusal(full) is null && !Io.FileExists(full))
        {
            if (!InsideRoots(full, ownership.Roots) && !ContainsRoot(full, ownership.Roots))
                return "outside-root";
            return LinkRefusal(full);
        }

        var reason = Refusal(full, ownership);
        if (reason is not null)
            return reason;
        Io.CreateDirectory(full);
        Remember(full);
        RecordContent(full);
        return null;
    }

    public bool WasCreated(string path) => context.Registry.WasCreated(path);

    public string? CreateFreshDirectory(string path, WriteOwnership ownership)
    {
        var reason = Refusal(path, ownership);
        if (reason is not null) return reason;
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        reason = ExclusiveDirectory.Create(context, full);
        if (reason is not null) return reason;
        if (OutputCustody.Stat(context, full) is { IsDirectory: true } made && made.Owner == Io.Owner && (made.Mode & 0x3F) == 0)
            context.Registry.RecordFreshRoot(full, made.Changed);
        Remember(full);
        RecordContent(full);
        return Text(Path.Combine(full, ".checkpoint-output-owner.json"), Guid.NewGuid().ToString("N"), Under(full));
    }

    public string? OpenCreate(string path, WriteOwnership ownership, out Stream? stream)
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

        stream = Io.Open(full, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        Remember(full);
        return null;
    }

    public string? OpenAppend(string path, WriteOwnership ownership, out Stream? stream,
        FileShare share = FileShare.ReadWrite | FileShare.Delete)
    {
        stream = null;
        var reason = AllowExisting(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        if (Io.DirectoryExists(full))
            return "unrecorded-existing";
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        stream = Io.Open(full, FileMode.Append, FileAccess.Write, share);
        Remember(full);
        return null;
    }

    public string? OpenGate(string path, WriteOwnership ownership, out Stream? stream)
    {
        stream = null;
        var reason = AllowExisting(path, ownership);
        if (reason is not null)
            return reason;
        var full = Canonical(path)!;
        if (Io.DirectoryExists(full))
            return "unrecorded-existing";
        var directory = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(directory))
        {
            reason = CreateDirectory(directory, ownership);
            if (reason is not null)
                return reason;
        }

        stream = Io.Open(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Remember(full);
        return null;
    }

    public void Require(string? reason)
    {
        if (reason is not null)
            throw new IOException("refused: " + reason);
    }

    public bool CarriesMarker(string path, string runId)
    {
        try
        {
            if (!Io.FileExists(path))
                return false;
            var text = ReceiptFile.ReadText(context, path);
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

    public WriteOwnership Under(string root, string? runId = null, WriteKind kind = WriteKind.Ordinary, string? gitExecutable = null) =>
        new()
        {
            Roots = [Path.GetFullPath(root)],
            RunId = runId,
            Kind = kind,
            GitExecutable = gitExecutable,
        };

    public string FormatOutputs(IReadOnlyList<string> labels, IReadOnlyList<string> paths)
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
            var present = path is not null && (Io.DirectoryExists(path) || Io.FileExists(path));
            if (path is null || present)
                kept.Add(label);
            else
                deleted.Add(label);
        }

        if (kept.Count > 0)
            return "outputs: refused/kept " + string.Join(", ", kept);
        return "outputs: deleted " + string.Join(", ", deleted);
    }

    private string? AllowExisting(string path, WriteOwnership ownership)
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

    private string? LinkRefusal(string path)
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

    private bool IsLink(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(Io.LinkTarget(path)))
                return true;
            return (Io.Attributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                   or NotSupportedException or FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private bool InsideRoots(string full, IReadOnlyCollection<string> roots)
    {
        foreach (var root in roots)
        {
            if (context.IsStrictlyInside(root, full) || Same(root, full))
                return true;
        }

        return false;
    }

    private bool ContainsRoot(string directory, IReadOnlyCollection<string> roots)
    {
        foreach (var root in roots)
        {
            var full = Canonical(root);
            if (full is not null && (context.IsStrictlyInside(directory, full) || Same(directory, full)))
                return true;
        }

        return false;
    }

    private bool Same(string root, string full)
    {
        var canonical = Canonical(root);
        if (canonical is null)
            return false;
        var comparison = context.PathComparison;
        return string.Equals(canonical, full, comparison);
    }

    private string? Canonical(string path)
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
