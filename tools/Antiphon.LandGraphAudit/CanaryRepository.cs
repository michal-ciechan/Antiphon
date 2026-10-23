namespace Antiphon.LandGraphAudit;

/// <summary>A disposable evaluation-only copy. Never writes through a link into the source tree.</summary>
internal sealed class CanaryRepository : IDisposable
{
    private const string Canary = "antiphon-c1483-canary";
    // Preserve ancestor configuration discovery (.editorconfig/.globalconfig, SDK searches).
    // A temporary directory under a different parent changes more than documentation.
    private CanaryRepository(string repo) => Root = Path.Combine(
        Path.GetDirectoryName(repo) ?? throw new IOException("repository has no parent for canary evaluation"),
        "antiphon-landgraph-canary-" + Guid.NewGuid().ToString("N"));

    public string Root { get; }

    public static CanaryRepository Create(string repo)
    {
        var copy = new CanaryRepository(repo);
        try
        {
            Directory.CreateDirectory(copy.Root);
            var files = BuildInputPolicy.TrackedFiles(repo, includeUntracked: true).ToHashSet(StringComparer.Ordinal);
            // Ignored Markdown is still skippable input. Do not follow directory links while
            // enumerating; their tracked link entries are copied separately and rebased below.
            var docs = Path.Combine(repo, "docs");
            if (Directory.Exists(docs))
                foreach (var file in Directory.EnumerateFiles(docs, "*", new EnumerationOptions
                         { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false }))
                    if (file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) files.Add(Path.GetRelativePath(repo, file));

            foreach (var relative in files.Order(StringComparer.Ordinal))
            {
                if (relative.Equals("README.md", StringComparison.OrdinalIgnoreCase)) continue;
                var source = Path.Combine(repo, relative);
                var destination = Path.GetFullPath(Path.Combine(copy.Root, relative));
                if (!destination.StartsWith(copy.Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new IOException("canary path outside repository: " + relative);
                copy.CheckDestination(destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var skippable = relative.Replace('\\', '/').StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                                && relative.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
                if (skippable) { File.WriteAllText(destination, Canary); continue; }
                var target = new FileInfo(source).LinkTarget;
                if (target is null) { File.Copy(source, destination); continue; }
                if (Path.IsPathRooted(target) && (target == repo || target.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    target = copy.Root + target[repo.Length..];
                if (Directory.Exists(source)) Directory.CreateSymbolicLink(destination, target);
                else File.CreateSymbolicLink(destination, target);
            }
            var added = Path.Combine(copy.Root, "docs", "c1483-canary", "canary.md");
            copy.CheckDestination(added);
            Directory.CreateDirectory(Path.GetDirectoryName(added)!);
            File.WriteAllText(added, Canary);
            return copy;
        }
        catch { copy.Dispose(); throw; }
    }

    private void CheckDestination(string path)
    {
        var real = SolutionAudit.RealPath(path);
        if (!real.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("canary destination escapes through a link: " + path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
