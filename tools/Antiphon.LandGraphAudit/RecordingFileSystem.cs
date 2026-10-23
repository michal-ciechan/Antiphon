using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Build.Evaluation;
using Microsoft.Build.FileSystem;

namespace Antiphon.LandGraphAudit;

/// <summary>D-6(a): observe actual evaluator IO, including absent paths and paths through links.</summary>
internal sealed class RecordingFileSystem(string repo) : MSBuildFileSystemBase
{
    private readonly string _realRepo = SolutionAudit.RealPath(repo);
    private readonly ConcurrentDictionary<string, string> _realPaths = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _refusals = new(StringComparer.Ordinal);
    public string? Owner { get; set; }
    public IEnumerable<string> Refusals => _refusals.Keys;

    private void Record(string path, string operation, bool directory = false, bool globTraversal = false)
    {
        var owner = Owner;
        if (owner is null || string.IsNullOrEmpty(path)) return;
        if (Path.DirectorySeparatorChar == '/') path = path.Replace('\\', '/');
        var full = Path.GetFullPath(path);
        var real = _realPaths.GetOrAdd(path, SolutionAudit.RealPath);
        if (!Sensitive(repo, full, directory) && !Sensitive(_realRepo, real, directory)) return;
        // D-4 already examines every actual MSBuild glob, ignoring excludes and today's
        // matches. Its JSON-only allowance must not become a refusal merely because the
        // matcher visits docs/. Never apply that allowance to an Exists/other Condition.
        // Unknown call provenance fails closed; no repository path or expression is trusted.
        var origin = "";
        if (globTraversal && IsMsBuildGlobTraversal(out origin)) return;
        _refusals.TryAdd($"refused: {owner} file-probe {operation} {full}{origin}", 0);
    }

    private static bool IsMsBuildGlobTraversal(out string origin)
    {
        var types = new StackTrace().GetFrames().Select(f => f.GetMethod()?.DeclaringType)
            .Where(t => t?.Assembly == typeof(Project).Assembly).ToArray();
        origin = " (probe-origin: " + string.Join(", ", types.Select(t => t!.FullName).Distinct().Take(8)) + ")";
        return !types.Any(t => t!.FullName == "Microsoft.Build.Evaluation.FunctionCallExpressionNode")
               && types.Any(t => t!.FullName == "Microsoft.Build.Shared.FileMatcher"
                                 || t.DeclaringType?.FullName == "Microsoft.Build.Shared.FileMatcher");
    }

    private static bool Sensitive(string root, string path, bool directory)
    {
        path = Path.TrimEndingDirectorySeparator(path);
        if (path.Equals(Path.Combine(root, "README.md"), StringComparison.OrdinalIgnoreCase)) return true;
        var docs = Path.Combine(root, "docs");
        return (path.Equals(docs, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(docs + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
               && (directory || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
    }

    public override bool FileExists(string path) { Record(path, nameof(FileExists)); return base.FileExists(path); }
    public override bool DirectoryExists(string path) { Record(path, nameof(DirectoryExists), true, true); return base.DirectoryExists(path); }
    public override bool FileOrDirectoryExists(string path) { Record(path, nameof(FileOrDirectoryExists), true); return base.FileOrDirectoryExists(path); }
    public override TextReader ReadFile(string path) { Record(path, nameof(ReadFile)); return base.ReadFile(path); }
    public override string ReadFileAllText(string path) { Record(path, nameof(ReadFileAllText)); return base.ReadFileAllText(path); }
    public override byte[] ReadFileAllBytes(string path) { Record(path, nameof(ReadFileAllBytes)); return base.ReadFileAllBytes(path); }
    public override Stream GetFileStream(string path, FileMode mode, FileAccess access, FileShare share)
    { Record(path, nameof(GetFileStream)); return base.GetFileStream(path, mode, access, share); }
    public override FileAttributes GetAttributes(string path) { Record(path, nameof(GetAttributes), true); return base.GetAttributes(path); }
    public override DateTime GetLastWriteTimeUtc(string path) { Record(path, nameof(GetLastWriteTimeUtc), true); return base.GetLastWriteTimeUtc(path); }
    public override IEnumerable<string> EnumerateFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    { Record(path, nameof(EnumerateFiles), true, true); return base.EnumerateFiles(path, searchPattern, searchOption); }
    public override IEnumerable<string> EnumerateDirectories(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    { Record(path, nameof(EnumerateDirectories), true, true); return base.EnumerateDirectories(path, searchPattern, searchOption); }
    public override IEnumerable<string> EnumerateFileSystemEntries(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    { Record(path, nameof(EnumerateFileSystemEntries), true, true); return base.EnumerateFileSystemEntries(path, searchPattern, searchOption); }
}
