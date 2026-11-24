using Antiphon.Tests.TestHelpers;

namespace Antiphon.E2E;

/// <summary>
/// A throwaway git repo carrying the REAL antiphon-delegate skill and the delegate command's launchers, so a live
/// Claude discovers them from its cwd exactly as an agent would in production. The skill is copied (not stubbed): if its
/// wording regresses, these tests are what notices. scripts/antiphon and scripts\antiphon.cmd forward to this checkout's
/// launchers, so the command (Antiphon.Nightly.DelegateCli, which replaced scripts/delegate.ps1 in unit 28 of the
/// 2026-11-22 plan) is the real one and runs with the scratch repository as its working directory.
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

        // The launchers forward to this checkout's (which build and run antiphon-scripts); the working directory stays the
        // scratch repository, so the command resolves its checkout there.
        var scriptDir = System.IO.Path.Combine(Path, "scripts");
        Directory.CreateDirectory(scriptDir);
        var shLauncher = System.IO.Path.Combine(scriptDir, "antiphon");
        File.WriteAllText(shLauncher,
            "#!/bin/sh\nexec \"" + System.IO.Path.Combine(root, "scripts", "antiphon").Replace('\\', '/') + "\" \"$@\"\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(shLauncher, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        File.WriteAllText(System.IO.Path.Combine(scriptDir, "antiphon.cmd"),
            "@echo off\r\ncall \"" + System.IO.Path.Combine(root, "scripts", "antiphon.cmd") + "\" %*\r\nexit /b %errorlevel%\r\n");

        File.WriteAllText(
            System.IO.Path.Combine(Path, "README.md"),
            "# Scratch workspace\n\n## Install\n\nRun `cmd /c setup.bat` to install.\n");

        Git("init");
        Git("add", ".");
        Git("-c", "user.email=test@antiphon.local", "-c", "user.name=Antiphon Test", "commit", "-m", "scratch");
    }

    /// <summary>
    /// The env contract a delegate is launched with. Identity lives here, never in arguments — which
    /// is exactly why the delegate command never has to be told who is calling it.
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
        try { ReadOnlyDirectory.Delete(Path); }
        catch (IOException) { /* a live pty's lock must not fail the test */ }
        catch (UnauthorizedAccessException) { /* a live pty's lock must not fail the test */ }
    }
}
