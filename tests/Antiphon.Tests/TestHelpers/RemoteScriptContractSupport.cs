using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Tests.Application;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

internal static class RemoteScriptContractSupport
{
    internal static readonly AsyncLocal<bool> ForceNoLinuxPwsh = new();

    internal static string PrepareLinuxShellScript(string body, string? repositoryVariable, string nativeRoot, bool isWindows)
    {
        if (repositoryVariable is not null and not "root" and not "repo")
            throw new ArgumentException("The repository-variable argument must be root or repo.", nameof(repositoryVariable));

        // Check the caller's body before inserting the trusted native-root assignment. Keep this
        // specific to the current checkout so unrelated Windows-path fixtures remain valid.
        if (isWindows)
        {
            var forwardRoot = nativeRoot.Replace('\\', '/');
            var spellings = new[] { nativeRoot, forwardRoot,
                nativeRoot.Replace("'", "'\\''"), forwardRoot.Replace("'", "'\\''") };
            if (spellings.Any(root => body.Contains(root, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("RepoRoot in a Linux shell body needs the repository-variable argument.");
        }

        if (repositoryVariable is null) return body;
        var prelude = repositoryVariable + "='" + nativeRoot.Replace("'", "'\\''") + "'\n";
        if (isWindows)
        {
            prelude += $$"""
                if ! {{repositoryVariable}}="$(wslpath -u "${{repositoryVariable}}")"; then
                    printf '%s\n' 'C980_REPO_ROOT_CONVERSION_FAILED' >&2
                    exit 1
                fi
                case "${{repositoryVariable}}" in
                    /*) ;;
                    *) printf '%s\n' 'C980_REPO_ROOT_CONVERSION_FAILED' >&2; exit 1 ;;
                esac
                """ + "\n";
        }
        return prelude + body;
    }

    // The remote script only ever runs under Linux bash, and these defects are behaviour (a
    // symlink's target mode, a restart's ordering), not text. On Windows the Linux shell is WSL:
    // Git Bash can neither create a symlink without privilege nor keep a 0600 mode.
    internal static string LinuxShell(string script, string? repositoryVariable = null)
        => LinuxShellResult(script, repositoryVariable).Output;

    internal static (int Exit, string Output) LinuxShellResult(string script, string? repositoryVariable = null)
    {
        if (ForceNoLinuxPwsh.Value)
            throw new InvalidOperationException("A Linux script block ran before the CARD-0905 pwsh skip.");
        script = PrepareLinuxShellScript(script, repositoryVariable, DelegateScriptRunner.RepoRoot, OperatingSystem.IsWindows());
        ProcessStartInfo start;
        if (OperatingSystem.IsWindows())
        {
            var wsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
            if (!File.Exists(wsl))
                Skip.Test("No Linux shell: wsl.exe is not installed, and c590-remote.sh only runs under Linux bash.");
            start = new ProcessStartInfo(wsl) { ArgumentList = { "-e", "bash", "-s" } };
        }
        else
        {
            start = new ProcessStartInfo("bash") { ArgumentList = { "-s" } };
        }

        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.StandardInputEncoding = new UTF8Encoding(false);
        start.StandardOutputEncoding = Encoding.UTF8;
        start.StandardErrorEncoding = Encoding.UTF8;
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var input = script.Replace("\r\n", "\n");
        Console.WriteLine($"REMOTE_SHELL_START platform={Environment.OSVersion.Platform}; executable={start.FileName}; " +
            $"input-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}");
        var elapsed = Stopwatch.StartNew();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        // cleanup-probe consumer is fourteen full fixture runs and does not sleep.
        // Idle Linux 2026-10-29: script wall 29.4s (finalize 16.9s, workspace
        // init 4.3s), then 57.0s on the same host once it was busy. Loaded
        // server2 passed the test at 58.7s and this wait killed it at 60s.
        // 120s is twice that 58.7s observation (provisional; the samples are
        // those three timings). Windows starts this bash through wsl.exe, so
        // its cap is 180s: 120s plus a provisional minute for WSL startup,
        // which this host cannot measure.
        var budgetMs = OperatingSystem.IsWindows() ? 180_000 : 120_000;
        if (!process.WaitForExit(budgetMs))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"the Linux shell harness did not finish in {budgetMs / 1000}s");
        }

        process.WaitForExit();
        Console.WriteLine($"REMOTE_SHELL_EXIT exit={process.ExitCode}; elapsed-ms={elapsed.ElapsedMilliseconds}");
        var output = stdout.Result;
        Console.WriteLine(output);
        Console.WriteLine(stderr.Result);
        return (process.ExitCode, output);
    }
}
