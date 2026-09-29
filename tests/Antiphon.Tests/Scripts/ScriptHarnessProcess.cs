using System.Runtime.ExceptionServices;
using System.Text;

namespace Antiphon.Tests.Scripts;

internal sealed record ScriptHarnessOptions(
    TimeSpan ExecutionBudget,
    TimeSpan CleanupBudget,
    string? ScriptPath = null,
    string? ExecutablePath = null,
    IReadOnlyList<string>? AdditionalArguments = null,
    Func<ScriptProcessRequest, IOwnedScriptProcess>? OwnerFactory = null,
    TimeProvider? Clock = null)
{
    internal static ScriptHarnessOptions Default => new(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(10));
}

internal sealed record ScriptProcessRequest(string Executable, string Script, string CaseName,
    string ResultsDirectory, string ControlDirectory, TimeSpan ExecutionBudget, TimeSpan CleanupBudget,
    IReadOnlyList<string>? AdditionalArguments, bool KeepStdinOpen = false);

internal sealed record ScriptHarnessResult(int ExitCode, string Stdout, string Stderr);

internal interface IOwnedScriptProcess : IDisposable
{
    StreamReader Stdout { get; }
    StreamReader Stderr { get; }
    Task<int> StartAndWaitForRootAsync(CancellationToken cancellationToken);
    Task TerminateAsync(CancellationToken cancellationToken);
    Task ConfirmDeadAsync(CancellationToken cancellationToken);
    void CloseStreams();
}

internal static class ScriptHarnessProcess
{
    internal static async Task<ScriptHarnessResult> RunAsync(string harness, string prefix, string caseName,
        string script, ScriptHarnessOptions options, CancellationToken callerToken,
        Action<ScriptHarnessResult>? validate = null)
    {
        ValidateBudget(options.ExecutionBudget, nameof(options.ExecutionBudget));
        ValidateBudget(options.CleanupBudget, nameof(options.CleanupBudget));
        callerToken.ThrowIfCancellationRequested();

        var invocation = Guid.NewGuid().ToString("N");
        var results = Path.Combine(Path.GetTempPath(), prefix.ToLowerInvariant() + "-nightly-" + invocation);
        var control = Path.Combine(Path.GetTempPath(), "antiphon-script-control-" + invocation);
        var executable = options.ExecutablePath ?? ResolvePowerShell();
        var request = new ScriptProcessRequest(executable, script, caseName, results, control,
            options.ExecutionBudget, options.CleanupBudget, options.AdditionalArguments);
        var clock = options.Clock ?? TimeProvider.System;
        var started = clock.GetTimestamp();
        var owner = (options.OwnerFactory ?? CreateOwner)(request);
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Task? stdoutPump = null;
        Task? stderrPump = null;
        Task<int>? root = null;
        Exception? primary = null;
        var exitCode = -1;
        var cleanupErrors = new List<string>();
        try
        {
            var remaining = options.ExecutionBudget - clock.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero)
                throw Timeout(harness, caseName, options.ExecutionBudget);
            using var executionBudget = new CancellationTokenSource(remaining, clock);
            using var execution = CancellationTokenSource.CreateLinkedTokenSource(callerToken, executionBudget.Token);
            // StartAndWaitForRootAsync begins the launch, but does not wait for EOF.
            root = owner.StartAndWaitForRootAsync(execution.Token);
            stdoutPump = PumpAsync(owner.Stdout, stdout);
            stderrPump = PumpAsync(owner.Stderr, stderr);
            try
            {
                // WhenAny observes a faulted reader before the other reader reaches EOF.
                var pending = new List<Task> { root, stdoutPump, stderrPump };
                while (pending.Count != 0)
                {
                    var completed = await Task.WhenAny(pending).WaitAsync(execution.Token);
                    await completed;
                    pending.Remove(completed);
                }
                exitCode = await root;
                validate?.Invoke(new ScriptHarnessResult(exitCode, stdout.ToString(), stderr.ToString()));
            }
            catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
            {
                throw new TimeoutException(Timeout(harness, caseName, options.ExecutionBudget).Message +
                    $" completion root={root.IsCompleted} stdout={stdoutPump.IsCompleted} stderr={stderrPump.IsCompleted}");
            }
            catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
            {
                throw new OperationCanceledException($"ScriptHarness {harness} case {caseName} canceled by caller.", callerToken);
            }
        }
        catch (Exception ex)
        {
            primary = ex;
        }
        finally
        {
            // Cleanup has a fresh, single budget, independent of a canceled caller.
            using var cleanup = new CancellationTokenSource(options.CleanupBudget, clock);
            try { await owner.TerminateAsync(cleanup.Token).WaitAsync(cleanup.Token); }
            catch (Exception ex) { cleanupErrors.Add("terminate: " + ex.Message); }
            try { await owner.ConfirmDeadAsync(cleanup.Token).WaitAsync(cleanup.Token); }
            catch (Exception ex) { cleanupErrors.Add("death confirmation: " + ex.Message); }
            try
            {
                if (stdoutPump is not null && stderrPump is not null)
                    await Task.WhenAll(stdoutPump, stderrPump).WaitAsync(cleanup.Token);
            }
            catch (Exception ex) { cleanupErrors.Add("pipe drain: " + ex.Message); }
            try { owner.CloseStreams(); }
            catch (Exception ex) { cleanupErrors.Add("stream close: " + ex.Message); }
            try { await Task.Run(owner.Dispose).WaitAsync(cleanup.Token); }
            catch (Exception ex) { cleanupErrors.Add("dispose: " + ex.Message); }
            ObserveLateFault(stdoutPump);
            ObserveLateFault(stderrPump);
            ObserveLateFault(root);
            if (cleanupErrors.Count == 0)
            {
                try
                {
                    if (Directory.Exists(results)) Directory.Delete(results, true);
                    if (Directory.Exists(control)) Directory.Delete(control, true);
                }
                catch (Exception ex) { cleanupErrors.Add("owned-path deletion: " + ex.Message); }
            }
        }

        var diagnostic = $"stdout:\n{Clip(stdout)}\nstderr:\n{Clip(stderr)}\n" +
            (cleanupErrors.Count == 0 ? "" : "cleanup: " + string.Join("; ", cleanupErrors) +
             $"\nretained results={results} control={control}");
        if (primary is TimeoutException)
            throw new TimeoutException(primary.Message + "\n" + diagnostic, primary);
        if (primary is not null)
        {
            primary.Data["ScriptHarnessDiagnostics"] = diagnostic;
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
        if (cleanupErrors.Count != 0)
            throw new IOException("ScriptHarness cleanup failed: " + diagnostic);
        return new ScriptHarnessResult(exitCode, stdout.ToString(), stderr.ToString());
    }

    private static IOwnedScriptProcess CreateOwner(ScriptProcessRequest request) =>
        OperatingSystem.IsWindows() ? new WindowsScriptHarnessProcess(request) :
        OperatingSystem.IsLinux() ? new LinuxScriptHarnessProcess(request) :
        throw new PlatformNotSupportedException("ScriptHarness requires Windows or Linux process ownership.");

    private static void ValidateBudget(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero || value == System.Threading.Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(name, "Budget must be finite and positive.");
    }

    private static TimeoutException Timeout(string harness, string caseName, TimeSpan budget) =>
        new($"ScriptHarness {harness} case {caseName} exceeded {budget} execution budget.");

    private static async Task PumpAsync(StreamReader reader, StringBuilder output)
    {
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer);
            if (count == 0) return;
            output.Append(buffer, 0, count);
        }
    }

    private static void ObserveLateFault(Task? task)
    {
        if (task is null) return;
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    private static string Clip(StringBuilder output)
    {
        const int edge = 4096;
        if (output.Length <= edge * 2) return output.ToString();
        return output.ToString(0, edge) + $"\n... {output.Length - edge * 2} characters omitted ...\n" +
               output.ToString(output.Length - edge, edge);
    }

    internal static string ResolvePowerShell()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var name = OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException($"A real {name} executable was not found on PATH.");
    }
}
