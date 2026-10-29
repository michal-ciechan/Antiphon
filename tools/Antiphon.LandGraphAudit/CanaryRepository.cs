using System.Runtime.InteropServices;

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

    // Set for the whole copy. A timed-out copy exits the process, which skips Dispose.
    private static string? ActiveRoot;

    private readonly Dictionary<string, string> _directoryRealPaths = new(StringComparer.Ordinal);

    public static CanaryRepository Create(string repo)
    {
        var copy = new CanaryRepository(repo);
        ActiveRoot = copy.Root;
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
                if (target is null)
                {
                    // A byte copy of every tracked file is what made a loaded Windows host
                    // spend the whole combined budget after the first pass. Same-volume
                    // hard links share the bytes; skippable docs are written above, not linked.
                    if (!TryHardLink(source, destination)) File.Copy(source, destination);
                    continue;
                }
                if (Path.IsPathRooted(target) && (target == repo || target.StartsWith(repo + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                    target = copy.Root + target[repo.Length..];
                if (Directory.Exists(source)) Directory.CreateSymbolicLink(destination, target);
                else File.CreateSymbolicLink(destination, target);
            }
            var added = Path.Combine(copy.Root, "docs", "c1483-canary", "canary.md");
            copy.CheckDestination(added);
            Directory.CreateDirectory(Path.GetDirectoryName(added)!);
            File.WriteAllText(added, Canary);
            ActiveRoot = null;
            return copy;
        }
        catch { copy.Dispose(); ActiveRoot = null; throw; }
    }

    /// <summary>Best-effort removal of a copy that did not finish. The copier may still be running.</summary>
    internal static void DeleteActive()
    {
        var root = ActiveRoot;
        ActiveRoot = null;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        try { Directory.Delete(root, recursive: true); }
        catch (Exception) { }
    }

    private void CheckDestination(string path)
    {
        var real = SolutionAudit.RealPath(path, _directoryRealPaths);
        if (!real.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("canary destination escapes through a link: " + path);
    }

    private static bool TryHardLink(string source, string destination)
    {
        if (OperatingSystem.IsWindows())
        {
            if (CreateHardLinkW(destination, source, IntPtr.Zero)) return true;
            if (Marshal.GetLastPInvokeError() == 17) return false;
            throw new IOException("hard link failed (" + Marshal.GetLastPInvokeError().ToString() + "): " + destination);
        }
        if (OperatingSystem.IsLinux())
        {
            if (Link(source, destination) == 0) return true;
            if (Marshal.GetLastPInvokeError() == 18) return false;
            throw new IOException("hard link failed (" + Marshal.GetLastPInvokeError().ToString() + "): " + destination);
        }
        return false;
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string existing, string path);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
