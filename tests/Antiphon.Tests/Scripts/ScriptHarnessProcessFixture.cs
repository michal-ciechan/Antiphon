using System.Diagnostics;
using Antiphon.Tests.Application;
using Shouldly;

namespace Antiphon.Tests.Scripts;

internal static class ScriptHarnessProcessFixture
{
    internal static string ScriptPath => Path.Combine(DelegateScriptRunner.RepoRoot, "tests", "Antiphon.Tests",
        "Scripts", "Fixtures", "script-harness-process.ps1");
    internal static string HelperPath => Path.Combine(AppContext.BaseDirectory, "script-harness-host",
        "Antiphon.ScriptHarnessHost.dll");

    internal static ScriptHarnessOptions Options(string? payload = null, Action<ScriptProcessRequest>? observe = null) =>
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), ScriptPath,
            OperatingSystem.IsWindows() ? ResolveInstalledPowerShell() : null,
            payload is null ? ["-HelperPath", HelperPath] : ["-HelperPath", HelperPath, "-Payload", payload],
            request =>
            {
                observe?.Invoke(request);
                return OperatingSystem.IsWindows() ? new WindowsScriptHarnessProcess(request) :
                    new LinuxScriptHarnessProcess(request);
            });

    private static string ResolveInstalledPowerShell()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.GetFullPath(Path.Combine(directory, "pwsh.exe"));
            if (candidate.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(candidate) || (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0)
                continue;
            return candidate;
        }
        throw new FileNotFoundException("Windows qualification requires a regular installed pwsh.exe on PATH; App Execution Aliases are not supported.");
    }

    internal static async Task<ObservedTree> WaitReadyAsync(Func<ScriptProcessRequest?> request, Task invocation)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            var directory = request()?.ResultsDirectory;
            if (directory is not null && File.Exists(Path.Combine(directory, "ready")))
            {
                var tree = new ObservedTree(
                    Read(Path.Combine(directory, "root")),
                    Read(Path.Combine(directory, "child")),
                    Read(Path.Combine(directory, "grandchild")), directory);
                if (!tree.AllExecuting())
                {
                    tree.EmergencyStop();
                    throw new InvalidOperationException($"Fixture readiness requires three executing processes; root={tree.Root.Executing()} child={tree.Child.Executing()} grandchild={tree.Grandchild.Executing()}.");
                }
                return tree;
            }
            if (invocation.IsCompleted)
            {
                var error = await CaptureAsync(invocation);
                throw new InvalidOperationException("ScriptHarness completed before fixture readiness.", error);
            }
            await Task.Delay(20);
        }
        var terminal = await CaptureAsync(invocation);
        throw new TimeoutException("Fixture readiness was not established within the execution budget.", terminal);
    }

    internal static async Task<Exception?> CaptureAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(30)); return null; }
        catch (Exception ex) { return ex; }
    }

    private static ProcessIdentity Read(string path)
    {
        var fields = File.ReadAllText(path).Split(' ');
        return new ProcessIdentity(int.Parse(fields[0]), long.Parse(fields[1]), long.Parse(fields[2]));
    }

    internal readonly record struct ProcessIdentity(int Pid, long StartTicks, long NativeStartTicks,
        ScriptHarnessWindowsProcessFixture.ObservedProcess? WindowsObservation = null)
    {
        internal bool Executing()
        {
            if (WindowsObservation is not null) return WindowsObservation.Executing();
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    var stat = File.ReadAllText($"/proc/{Pid}/stat");
                    var end = stat.LastIndexOf(") ", StringComparison.Ordinal);
                    if (end < 0) return false;
                    var fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    return long.Parse(fields[19]) == NativeStartTicks && fields[0][0] is not ('Z' or 'X');
                }
                using var process = Process.GetProcessById(Pid);
                return process.StartTime.ToUniversalTime().Ticks == StartTicks && !process.HasExited;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal void EmergencyStop()
        {
            if (WindowsObservation is not null)
            {
                if (WindowsObservation.Executing()) WindowsObservation.Terminate();
                WindowsObservation.Join(TimeSpan.FromSeconds(5));
                return;
            }
            if (!Executing()) return;
            using var process = Process.GetProcessById(Pid);
            if (Executing())
                process.Kill(entireProcessTree: false);
        }
    }

    // The watchdog is independent of the harness's 5+2-second budgets. Task.Run
    // also keeps a stuck synchronous launch from blocking the watchdog itself.
    internal static async Task WithInvocationAsync(Func<Invocation, Task> body, int watchdogSeconds = 30,
        WindowsScriptHarnessHooks? hooks = null, bool requireWindows = false, CancellationToken watchdogToken = default)
    {
        var invocation = new Invocation(hooks, requireWindows);
        Exception? primary = null;
        try { await body(invocation).WaitAsync(TimeSpan.FromSeconds(watchdogSeconds), watchdogToken); }
        catch (Exception ex) { primary = ex; throw; }
        finally
        {
            try { await invocation.DisposeAsync(); }
            catch (Exception cleanup) when (primary is not null)
            {
                throw new AggregateException("Native assertion and fixture cleanup both failed.", primary, cleanup);
            }
        }
    }

    internal sealed class Invocation : IAsyncDisposable
    {
        private readonly CancellationTokenSource _cancel = new();
        private Task? _run;
        internal ScriptHarnessWindowsProcessFixture? Windows { get; }
        internal ScriptProcessRequest? Request { get; private set; }
        internal ObservedTree? Tree { get; private set; }
        internal Invocation(WindowsScriptHarnessHooks? hooks, bool requireWindows)
        {
            if (requireWindows && !OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows qualification requires Windows.");
            if (!File.Exists(HelperPath)) throw new FileNotFoundException("Native fixture helper not staged.");
            if (OperatingSystem.IsWindows()) Windows = new ScriptHarnessWindowsProcessFixture(hooks);
        }
        internal ScriptHarnessOptions Options(string? payload = null) =>
            ScriptHarnessProcessFixture.Options(payload, value => Request = value) with
            {
                AdditionalArguments = Windows?.Arguments(payload) ?? (payload is null ?
                    ["-HelperPath", HelperPath] : ["-HelperPath", HelperPath, "-Payload", payload]),
                OwnerFactory = value =>
                {
                    Request = value;
                    return Windows is null ? new LinuxScriptHarnessProcess(value) : new WindowsScriptHarnessProcess(value, Windows.Hooks);
                }
            };
        internal Task<ScriptHarnessResult> Start(string caseName, string? payload = null, CancellationToken token = default,
            Action<ScriptHarnessResult>? validate = null)
        {
            if (_run is not null) throw new InvalidOperationException("One invocation per fixture.");
            var run = Task.Run(async () =>
            {
                return await ScriptHarnessProcess.RunAsync("fixture", "C806", caseName, ScriptPath, Options(payload),
                    token.CanBeCanceled ? token : _cancel.Token, validate);
            });
            _run = run;
            return run;
        }
        internal Task StartHarness(string caseName)
        {
            if (_run is not null) throw new InvalidOperationException("One invocation per fixture.");
            return _run = Task.Run(() => ScriptHarness.RunHarnessCaseAsync("fixture", "C806", caseName, 1,
                ["C806 C806 fixture passed"], Options(), _cancel.Token));
        }
        internal async Task<ObservedTree> WaitReadyAsync(Task run)
        {
            if (Windows is null) return Tree = await ScriptHarnessProcessFixture.WaitReadyAsync(() => Request, run);
            var clock = Stopwatch.StartNew();
            while (Request is null)
            {
                if (run.IsCompleted) throw new InvalidOperationException("Launch failed before request.", await CaptureAsync(run));
                if (clock.Elapsed >= TimeSpan.FromSeconds(5)) throw new TimeoutException("No fixture request.");
                await Task.Delay(20);
            }
            return Tree = await Windows.WaitReadyAsync(Request, run);
        }
        internal void AssertClean()
        {
            Tree?.AssertDeadBeforeEmergencySweep();
            Windows?.AssertStoppedBeforeDispose();
            if (Request is not null)
            {
                Directory.Exists(Request.ResultsDirectory).ShouldBeFalse();
                Directory.Exists(Request.ControlDirectory).ShouldBeFalse();
            }
        }
        public async ValueTask DisposeAsync()
        {
            var clock = Stopwatch.StartNew();
            _cancel.Cancel();
            try
            {
                if (_run is not null)
                {
                    try { await _run.WaitAsync(TimeSpan.FromSeconds(5)); }
                    catch (Exception) when (_run.IsCompleted) { /* Primary outcome belongs to the test body. */ }
                }
            }
            finally
            {
                try
                {
                    if (Windows is not null) Windows.Dispose(TimeSpan.FromSeconds(5) - clock.Elapsed);
                    else Tree?.EmergencyStop();
                }
                finally { _cancel.Dispose(); }
            }
        }
    }

    internal sealed record ObservedTree(ProcessIdentity Root, ProcessIdentity Child,
        ProcessIdentity Grandchild, string ResultsDirectory)
    {
        internal bool AllExecuting() => Root.Executing() && Child.Executing() && Grandchild.Executing();
        internal void EmergencyStop()
        {
            Root.EmergencyStop();
            Child.EmergencyStop();
            Grandchild.EmergencyStop();
        }
        internal void AssertDeadBeforeEmergencySweep()
        {
            Exception? primary = null;
            try
            {
                Root.Executing().ShouldBeFalse("Script root remained executing after cleanup.");
                Child.Executing().ShouldBeFalse("Fixture child remained executing after cleanup.");
                Grandchild.Executing().ShouldBeFalse("Fixture grandchild remained executing after cleanup.");
                Directory.Exists(ResultsDirectory).ShouldBeFalse("Owned results path survived confirmed cleanup.");
            }
            catch (Exception ex) { primary = ex; throw; }
            finally
            {
                try { EmergencyStop(); }
                catch (Exception cleanup) when (primary is not null)
                {
                    throw new AggregateException("Native death assertion and emergency sweep both failed.", primary, cleanup);
                }
            }
        }
    }
}
