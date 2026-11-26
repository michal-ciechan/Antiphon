using System.Diagnostics;
using System.Text;
using Antiphon.LiveLane;
using Antiphon.Scripts;
using Antiphon.Tests.Application;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// W2 slice S22: a fixture repository for the live lane's fingerprint, receipt and status tests (the in-process successors
/// of scripts/test-live-lane-fingerprint.ps1 and test-live-lane-receipt.ps1). git runs with a fixed author, committer and
/// date, no hooks, no signing and no line-ending conversion, so a commit's id depends only on its tree, message and parent.
/// </summary>
internal sealed class LiveLaneRepo
{
    private static readonly UTF8Encoding Utf8 = new(false);

    public LiveLaneRepo(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
        Hooks = Path.Combine(Path.GetDirectoryName(root)!, ".no-hooks");
        Directory.CreateDirectory(Hooks);
        Git("init", "-q", "-b", "main");
    }

    public string Root { get; }
    public string Hooks { get; }

    public string Git(params string[] arguments) => RunGit(Root, Hooks, arguments);

    /// <summary>git in any directory with the fixture's identity, dates and settings.</summary>
    public static string RunGit(string directory, string hooks, params string[] arguments)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=" + hooks,
                     "-c", "core.autocrlf=false" }.Concat(arguments)) start.ArgumentList.Add(argument);
        start.Environment["GIT_AUTHOR_DATE"] = "2026-11-01T00:00:00Z";
        start.Environment["GIT_COMMITTER_DATE"] = "2026-11-01T00:00:00Z";
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(' ', arguments) + ": " + output + error.Result);
        return output.Trim();
    }

    public void Put(string path, string text)
    {
        var full = Path.Combine(Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, Utf8);
    }

    public void Drop(string path) => File.Delete(Path.Combine(Root, path));

    public string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    /// <summary>Every change of the working tree staged, then committed.</summary>
    public string Commit(string message = "fixture")
    {
        Git("add", "--all");
        return CommitIndex(message);
    }

    /// <summary>The index committed as staged (a mode, a symbolic link or a gitlink the working tree cannot carry).</summary>
    public string CommitIndex(string message = "fixture")
    {
        Git("commit", "-q", "--allow-empty", "-m", message);
        return Git("rev-parse", "HEAD");
    }

    public void Checkout(string sha)
    {
        Git("checkout", "-q", "-f", "--detach", sha);
        Git("clean", "-q", "-fdx");
    }

    /// <summary>A symbolic link entry (mode 120000) at path naming target, staged without touching the working tree.</summary>
    public void StageLink(string path, string target)
    {
        var file = Path.Combine(Path.GetDirectoryName(Root)!, "link-target-" + Guid.NewGuid().ToString("N")[..8] + ".txt");
        File.WriteAllText(file, target, Utf8);
        var blob = Git("hash-object", "-w", file);
        Git("update-index", "--add", "--cacheinfo", "120000," + blob + "," + path);
    }

    /// <summary>A gitlink entry (mode 160000) at path naming commit.</summary>
    public void StageGitlink(string path, string commit) => Git("update-index", "--add", "--cacheinfo", "160000," + commit + "," + path);
}

/// <summary>The scratch directory of one test, removed afterwards.</summary>
internal sealed class LaneScratch : IDisposable
{
    public LaneScratch(string prefix)
    {
        Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string Path(params string[] parts) => System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public void Dispose()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(Root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>The CLI's reads (the nightly core's git and rc project graph), fresh per call as the CLI makes them.</summary>
internal static class LiveLaneTestSources
{
    public static LiveLaneSources Create() => LiveLaneNightlySources.Create();

    public static string RepoRoot => DelegateScriptRunner.RepoRoot;
}
