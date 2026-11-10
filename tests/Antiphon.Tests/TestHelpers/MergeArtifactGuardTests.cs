using System.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-0766: a tracked <c>*.cs.new</c>, <c>*.orig</c>, or <c>*.rej</c> file is a merge leftover and must not land.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class MergeArtifactGuardTests
{
    [Test]
    public void refuses_each_merge_artifact_suffix()
    {
        MergeArtifactPaths.Find(
        [
            "tests/Antiphon.Tests/Application/LandingProtocolGuardTests.cs",
            "file.original",
            "file.rejection",
            "file.cs.newest",
            "archive/cs.new/readme.md",
            "a.cs.new",
            "nested/b.orig",
            "nested/c.rej",
            "nested/d.CS.NEW",
        ]).ShouldBe(["a.cs.new", "nested/b.orig", "nested/c.rej", "nested/d.CS.NEW"]);
    }

    [Test]
    public void tracked_repository_has_no_merge_artifacts()
    {
        var tracked = MergeArtifactPaths.ReadTracked(RepoRoot);
        tracked.ShouldNotBeEmpty("git ls-files returned no paths");
        var found = MergeArtifactPaths.Find(tracked);
        found.ShouldBeEmpty("tracked merge artifacts: " + string.Join(", ", found));
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}

internal static class MergeArtifactPaths
{
    internal static readonly string[] Suffixes = [".cs.new", ".orig", ".rej"];

    internal static string[] Find(IEnumerable<string> paths)
    {
        var found = new List<string>();
        foreach (var path in paths)
        {
            if (IsArtifact(path))
                found.Add(path);
        }

        found.Sort(StringComparer.Ordinal);
        return found.ToArray();
    }

    internal static bool IsArtifact(string path)
    {
        var name = path.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];
        foreach (var suffix in Suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static string[] ReadTracked(string root)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "-c", "core.fsmonitor=false", "ls-files", "-z" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start git ls-files.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5_000);
            throw new TimeoutException("git ls-files exceeded 15 seconds.");
        }

        var paths = output.GetAwaiter().GetResult();
        var diagnostic = error.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git ls-files failed: " + diagnostic);
        return paths.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
