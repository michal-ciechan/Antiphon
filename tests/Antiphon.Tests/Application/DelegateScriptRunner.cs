using System.Diagnostics;

namespace Antiphon.Tests.Application;

/// <summary>
/// Runs the REAL <c>scripts/delegate.ps1</c> under pwsh, pointed at a test-owned API base URL.
///
/// <para>Shared by <see cref="DelegateScriptKindTests"/> (CARD-0084 S2, which asserts on the JSON
/// the script posts) and <see cref="GrokDelegateEndToEndTests"/> (S6, which lets that JSON reach the
/// real <c>AgentTaskService</c>). One copy on purpose: the value of both is that an agent's actual
/// entry point is executed, so the way it is invoked — <c>-NoProfile -NonInteractive -File</c>, the
/// ANTIPHON_* environment, argument-list quoting that keeps a multi-line <c>-Goal</c> intact — must
/// be the same in both or one of them is testing a different caller than the other.</para>
/// </summary>
internal static class DelegateScriptRunner
{
    /// <param name="apiBaseUrl">
    /// What the script sees as <c>ANTIPHON_API</c>. Trailing slash tolerated — the script trims it,
    /// and passing it verbatim is what an agent's environment actually looks like.
    /// </param>
    public static async Task<(int ExitCode, string Output)> RunAsync(string apiBaseUrl, params string[] args)
    {
        var scriptPath = Path.Combine(RepoRoot, "scripts", "delegate.ps1");
        var startInfo = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["ANTIPHON_API"] = apiBaseUrl.TrimEnd('/');
        startInfo.Environment["ANTIPHON_TASK_TOKEN"] = string.Empty;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("pwsh did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);

        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>The checkout root, found by walking up from the test binaries to the solution file.</summary>
    public static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Antiphon.sln")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate the Antiphon repository root.");
        }
    }
}
