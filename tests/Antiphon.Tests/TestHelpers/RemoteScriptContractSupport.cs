using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Antiphon.Nightly;
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
        var windows = OperatingSystem.IsWindows();
        var input = PrepareLinuxShellScript(script, repositoryVariable, DelegateScriptRunner.RepoRoot, windows)
            .Replace("\r\n", "\n");
        var executable = windows ? Path.Combine(Environment.SystemDirectory, "wsl.exe") : "bash";
        if (windows && !File.Exists(executable))
            Skip.Test("No Linux shell: wsl.exe is not installed, and c590-remote.sh only runs under Linux bash.");

        Console.WriteLine($"REMOTE_SHELL_START platform={Environment.OSVersion.Platform}; executable={executable}; " +
            $"input-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)))}");
        using var phase = TestTiming.BeginPhase(TestPhaseKind.ProcessWait, "wait");
        var (exit, output, error) = windows ? RunWindowsShell(executable, input) : RunBash(executable, input);
        Console.WriteLine(output);
        Console.WriteLine(error);
        return (exit, output);
    }

    private static (int Exit, string Output, string Error) RunBash(string executable, string input)
    {
        var start = new ProcessStartInfo(executable) { ArgumentList = { "-s" } };
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
        var elapsed = Stopwatch.StartNew();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        // cleanup-probe consumer is fourteen full fixture runs and does not sleep.
        // Idle Linux 2026-10-29: script wall 29.4s (finalize 16.9s, workspace
        // init 4.3s), then 57.0s on the same host once it was busy. Loaded
        // server2 passed the test at 58.7s and this wait killed it at 60s.
        // 120s is twice that 58.7s observation (provisional; the samples are
        // those three timings).
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("the Linux shell harness did not finish in 120s");
        }

        process.WaitForExit();
        Console.WriteLine($"REMOTE_SHELL_EXIT exit={process.ExitCode}; elapsed-ms={elapsed.ElapsedMilliseconds}");
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    private static (int Exit, string Output, string Error) RunWindowsShell(string executable, string input)
    {
        var scratch = Directory.CreateTempSubdirectory("antiphon-remote-shell-");
        try
        {
            var inputPath = Path.Combine(scratch.FullName, "stdin");
            var logPath = Path.Combine(scratch.FullName, "shell.log");
            File.WriteAllText(inputPath, input, new UTF8Encoding(false));
            // Join a private job before WSL can start workers. A finished command can still
            // leave console or WSL sidecars attached to the outer qualification job.
            var hooks = new NativeProcessHooks { StandardInputPath = inputPath, TerminateDescendantsOnRootExit = true };
            // Keep the existing 120s script allowance plus the provisional 60s for WSL startup.
            var result = NativeProcessOwner.Start(executable, ["-e", "bash", "-s"], Environment.CurrentDirectory,
                180_000, logPath, null, hooks).Completion.GetAwaiter().GetResult();
            Console.WriteLine(FormattableString.Invariant(
                $"REMOTE_SHELL_EXIT exit={result.ExitCode}; launch-ms={result.LaunchMilliseconds:F0}; execution-ms={result.ExecutionMilliseconds:F0}; cleanup-ms={result.CleanupMilliseconds:F0}; drain-ms={result.DrainMilliseconds:F0}; terminated-job={result.TerminationRequested}"));
            if (result.TimedOut)
                throw new TimeoutException("the WSL shell harness exceeded its launch or output-drain budget: " + result.Error);
            if (!result.CleanupComplete || !string.IsNullOrEmpty(result.Error))
                throw new InvalidOperationException("WSL shell custody failed: " + result.Error);

            var log = File.ReadAllBytes(logPath);
            var outputBytes = checked((int)result.StandardOutputBytes);
            return (result.ExitCode, ReadText(log, 0, outputBytes), ReadText(log, outputBytes, log.Length - outputBytes));
        }
        finally
        {
            scratch.Delete(recursive: true);
        }
    }

    private static string ReadText(byte[] log, int offset, int length)
    {
        using var stream = new MemoryStream(log, offset, length);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
