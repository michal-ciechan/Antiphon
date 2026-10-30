using System.Text;

namespace Antiphon.LandGraphAudit;

/// <summary>A disposable byte copy. Writes never share data with the source tree.</summary>
internal sealed class CanaryRepository : IDisposable
{
    private const string Canary = "antiphon-c1483-canary";
    internal const string DirectoryPrefix = "antiphon-landgraph-canary-";

    // Preserve ancestor configuration discovery (.editorconfig/.globalconfig, SDK searches).
    // A temporary directory under a different parent changes more than documentation.
    private CanaryRepository(string repo) => Root = Path.Combine(
        Path.GetDirectoryName(repo) ?? throw new IOException("repository has no parent for canary evaluation"),
        DirectoryPrefix + Guid.NewGuid().ToString("N"));

    public string Root { get; }

    // The script reads this after a kill. The tool writes it before creating the directory.
    internal static string? RecordPath { get; set; }

    // Set until Dispose. A kill skips Dispose; the process-exit hook and the script both use this path.
    private static string? ActiveRoot;
    private static volatile bool Cancelled;

    public static CanaryRepository Create(string repo)
    {
        Cancelled = false;
        var copy = new CanaryRepository(repo);
        ActiveRoot = copy.Root;
        Publish(copy.Root);
        try
        {
            Directory.CreateDirectory(copy.Root);
            var tracked = BuildInputPolicy.TrackedFiles(repo, includeUntracked: true);
            // Ignored Markdown is still skippable input. Do not follow directory links while
            // enumerating; their tracked link entries are copied separately and rebased below.
            var documentation = new List<string>();
            var docs = Path.Combine(repo, "docs");
            if (Directory.Exists(docs))
                foreach (var file in Directory.EnumerateFiles(docs, "*", new EnumerationOptions
                         { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
                    if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        documentation.Add(Path.GetRelativePath(repo, file));
            var files = CanaryEntries.Build(tracked, documentation, Path.DirectorySeparatorChar);
            var rewriteOrdinarySource = CopyRaceRewrite(repo);

            var ordinary = new List<(string Source, string Destination, bool Skippable)>();
            void CopyOrdinary()
            {
                // Join each batch before creating a link. No worker can introduce a link
                // into another worker's destination ancestry, and every write still walks it.
                Parallel.ForEach(ordinary, new ParallelOptions
                {
                    MaxDegreeOfParallelism = OperatingSystem.IsWindows() ? Math.Min(4, Environment.ProcessorCount) : 1
                }, entry =>
                {
                    if (Cancelled) throw new IOException("canary copy cancelled");
                    copy.CheckDestination(entry.Destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(entry.Destination)!);
                    if (entry.Skippable) { File.WriteAllText(entry.Destination, Canary); return; }
                    rewriteOrdinarySource?.Invoke(entry.Source);
                    if ((File.GetAttributes(entry.Source) & FileAttributes.ReparsePoint) != 0
                        && new FileInfo(entry.Source).LinkTarget is not null)
                        throw new IOException("canary source became a link: " + entry.Source);
                    File.Copy(entry.Source, entry.Destination);
                });
                ordinary.Clear();
            }

            foreach (var relative in files.Order(StringComparer.Ordinal))
            {
                if (Cancelled) throw new IOException("canary copy cancelled");
                if (relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
                var source = Path.Combine(repo, relative);
                var destination = Path.GetFullPath(Path.Combine(copy.Root, relative));
                if (!destination.StartsWith(copy.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException("canary path outside repository: " + relative);
                var skippable = relative.Replace('\\', '/').StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                                && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                var target = !skippable && (File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0
                    ? new FileInfo(source).LinkTarget : null;
                if (target is null) { ordinary.Add((source, destination, skippable)); continue; }
                CopyOrdinary();
                copy.CheckDestination(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (Path.IsPathRooted(target) && (target == repo || target.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    target = copy.Root + target[repo.Length..];
                if (Directory.Exists(source)) Directory.CreateSymbolicLink(destination, target);
                else File.CreateSymbolicLink(destination, target);
            }
            CopyOrdinary();
            var added = Path.Combine(copy.Root, "docs", "c1483-canary", "canary.md");
            copy.CheckDestination(added);
            Directory.CreateDirectory(Path.GetDirectoryName(added)!);
            File.WriteAllText(added, Canary);
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    internal static void CancelCopy() => Cancelled = true;

    // Witness seam for the worker re-check. A temp fixture names one ordinary source to
    // replace with a link after classification and before that re-check. Repositories
    // outside the temp path are never read, and a missing marker does nothing.
    private const string CopyRaceMarkerName = ".antiphon-landgraph-copy-race";

    private static Action<string>? CopyRaceRewrite(string repo)
    {
        if (!repo.StartsWith(Path.GetTempPath(), StringComparison.Ordinal)) return null;
        var marker = Path.Combine(repo, CopyRaceMarkerName);
        if (!File.Exists(marker)) return null;
        var spec = File.ReadAllText(marker).Trim();
        var bar = spec.IndexOf('|');
        if (bar <= 0 || bar == spec.Length - 1) return null;
        var relative = spec[..bar].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        var linkTarget = spec[(bar + 1)..].Trim();
        var expected = Path.GetFullPath(Path.Combine(repo, relative));
        return source =>
        {
            if (!string.Equals(Path.GetFullPath(source), expected, StringComparison.Ordinal)) return;
            File.Delete(source);
            File.CreateSymbolicLink(source, linkTarget);
        };
    }

    /// <summary>Removes the live canary. A second call is a no-op once the directory is gone.</summary>
    internal static void DeleteActive()
    {
        var root = ActiveRoot;
        if (string.IsNullOrEmpty(root)) return;
        DeleteRoot(root);
    }

    internal static bool IsCanaryDirectoryName(string name)
    {
        if (name.Length != DirectoryPrefix.Length + 32 || !name.StartsWith(DirectoryPrefix, StringComparison.Ordinal))
            return false;
        for (var i = DirectoryPrefix.Length; i < name.Length; i++)
        {
            var c = name[i];
            if (c is not (>= '0' and <= '9' or >= 'a' and <= 'f')) return false;
        }
        return true;
    }

    private static void Publish(string root)
    {
        var record = RecordPath;
        if (!string.IsNullOrEmpty(record))
        {
            using var stream = new FileStream(record, FileMode.Create, FileAccess.Write, FileShare.Read);
            var bytes = Encoding.UTF8.GetBytes(root);
            stream.Write(bytes);
            stream.Flush(true);
        }
        Console.Out.WriteLine("canary=" + root);
        Console.Out.Flush();
    }

    private void CheckDestination(string path)
    {
        var real = SolutionAudit.RealPath(path);
        if (!real.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("canary destination escapes through a link: " + path);
    }

    public void Dispose()
    {
        DeleteRoot(Root);
        if (string.Equals(ActiveRoot, Root, StringComparison.Ordinal)) ActiveRoot = null;
    }

    private static void DeleteRoot(string root)
    {
        if (!IsCanaryDirectoryName(Path.GetFileName(root))) return;
        try
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"canary delete failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
