namespace Antiphon.E2E;

/// <summary>
/// A throwaway git repo carrying the REAL antiphon-delegate skill and delegate.ps1, so a live Claude
/// discovers them from its cwd exactly as an agent would in production. Copying the real files (not
/// stubs) is the point: if the skill's wording or the script's argument handling regresses, these
/// tests are what notices.
/// </summary>
public sealed class DelegationScratchRepo : IDisposable
{
    public string Path { get; }

    public DelegationScratchRepo(string prefix = "antiphon-delegation-e2e")
    {
        Path = Directory.CreateTempSubdirectory(prefix).FullName;
        var root = FindRepoRoot();

        var skillDir = System.IO.Path.Combine(Path, ".claude", "skills", "antiphon-delegate");
        Directory.CreateDirectory(skillDir);
        File.Copy(
            System.IO.Path.Combine(root, ".claude", "skills", "antiphon-delegate", "SKILL.md"),
            System.IO.Path.Combine(skillDir, "SKILL.md"));

        var scriptDir = System.IO.Path.Combine(Path, "scripts");
        Directory.CreateDirectory(scriptDir);
        File.Copy(
            System.IO.Path.Combine(root, "scripts", "delegate.ps1"),
            System.IO.Path.Combine(scriptDir, "delegate.ps1"));

        File.WriteAllText(
            System.IO.Path.Combine(Path, "README.md"),
            "# Scratch workspace\n\n## Install\n\nRun `cmd /c setup.bat` to install.\n");

        Git("init");
        Git("add", ".");
        Git("-c", "user.email=test@antiphon.local", "-c", "user.name=Antiphon Test", "commit", "-m", "scratch");
    }

    /// <summary>
    /// The env contract a delegate is launched with. Identity lives here, never in arguments — which
    /// is exactly why the delegate script never has to be told who is calling it.
    /// </summary>
    public static Dictionary<string, string> EnvFor(
        string apiBaseUrl, Guid sessionId, Guid taskId, string? token)
    {
        var env = new Dictionary<string, string>
        {
            // Neutralise nested-Claude markers: these tests usually run from inside a Claude session,
            // and a child that inherits them does not persist its transcript — which would take the
            // JSONL turn-end signal away with it.
            ["CLAUDE_CODE_CHILD_SESSION"] = "",
            ["CLAUDE_CODE_SESSION_ID"] = "",
            ["CLAUDE_CODE_BRIDGE_SESSION_ID"] = "",
            ["ANTIPHON_API"] = apiBaseUrl,
            ["ANTIPHON_SESSION_ID"] = sessionId.ToString("D"),
            ["ANTIPHON_AGENT_ID"] = Guid.Empty.ToString("D"),
            ["ANTIPHON_TASK_ID"] = taskId.ToString("D"),
        };
        if (token is not null)
            env["ANTIPHON_TASK_TOKEN"] = token;
        return env;
    }

    private void Git(params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = Path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = System.Diagnostics.Process.Start(psi);
        process?.WaitForExit(30_000);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir, "Antiphon.sln")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the repository root from the test output.");
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { /* a live pty's lock must not fail the test */ }
        catch (UnauthorizedAccessException) { /* git's read-only object files */ }
    }
}
