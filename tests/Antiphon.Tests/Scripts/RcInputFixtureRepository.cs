using System.Diagnostics;
using Antiphon.Tests.TestHelpers;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// A throwaway git repository for the rc input inventory tests (moved from test-rc-input-owners.ps1 and
/// test-rc-input-manifest.ps1). Files are written under a directory whose name has spaces and committed on main; the
/// whole temp directory is deleted on dispose. Only git is started.
/// </summary>
internal sealed class RcInputFixtureRepository : IDisposable
{
    private static readonly Lazy<string> SourceRootValue = new(() =>
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "Antiphon.Tests"))) return directory.FullName;
        throw new DirectoryNotFoundException("no repository root above " + AppContext.BaseDirectory);
    });

    private readonly string owned;

    public RcInputFixtureRepository(string prefix)
    {
        owned = Directory.CreateTempSubdirectory(prefix).FullName;
        Root = Path.Combine(owned, "repository with spaces");
        Directory.CreateDirectory(Root);
    }

    /// <summary>The fixture repository's working tree.</summary>
    public string Root { get; }

    /// <summary>The owned temp directory that holds the repository; other fixture files may go beside it.</summary>
    public string Scratch => owned;

    /// <summary>The source repository these tests were built from (it holds tests/Antiphon.Tests).</summary>
    public static string SourceRoot => SourceRootValue.Value;

    /// <summary>A file of the source repository's working tree, by repository-relative path.</summary>
    public static string ReadSource(string path) => File.ReadAllText(Path.Combine(SourceRoot, Local(path)));

    private static string Local(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    public void Init()
    {
        Git("init", "-q", "-b", "main");
        Git("config", "user.name", "Fixture");
        Git("config", "user.email", "fixture@example.invalid");
    }

    public void Put(string path, string text)
    {
        var full = Path.Combine(Root, Local(path));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Remove(string path) => File.Delete(Path.Combine(Root, Local(path)));

    /// <summary>Stages everything, commits, and returns the new HEAD.</summary>
    public string Commit(bool allowEmpty = false)
    {
        Git("add", "--all");
        if (allowEmpty) Git("commit", "-qm", "fixture", "--allow-empty");
        else Git("commit", "-qm", "fixture");
        return Head();
    }

    public string Head() => Git("rev-parse", "HEAD");

    /// <summary>Runs git in the fixture; a non-zero exit or a 60 s stall throws. Returns trimmed stdout.</summary>
    public string Git(params string[] args) => GitWith(null, args);

    /// <summary>Runs git with extra environment variables (for example GIT_COMMITTER_DATE).</summary>
    public string GitWith(IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var (name, value) in environment ?? new Dictionary<string, string>()) start.Environment[name] = value;
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("git " + string.Join(' ', args) + " did not exit within 60 s");
        }
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("git " + string.Join(' ', args) + " exited " + process.ExitCode + ": " + error.Result.Trim());
        return output.Result.Trim();
    }

    public void Dispose() => ReadOnlyDirectory.Delete(owned);
}
