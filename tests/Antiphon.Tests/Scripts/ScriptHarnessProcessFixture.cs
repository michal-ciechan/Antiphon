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
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), ScriptPath, null,
            payload is null ? ["-HelperPath", HelperPath] : ["-HelperPath", HelperPath, "-Payload", payload],
            request =>
            {
                observe?.Invoke(request);
                return OperatingSystem.IsWindows() ? new WindowsScriptHarnessProcess(request) :
                    new LinuxScriptHarnessProcess(request);
            });

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

    internal readonly record struct ProcessIdentity(int Pid, long StartTicks, long NativeStartTicks)
    {
        internal bool Executing()
        {
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
            catch (System.ComponentModel.Win32Exception) { return false; }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }

        internal void EmergencyStop()
        {
            if (!Executing()) return;
            using var process = Process.GetProcessById(Pid);
            if (Executing())
                process.Kill(entireProcessTree: false);
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
            try
            {
                Root.Executing().ShouldBeFalse("Script root remained executing after cleanup.");
                Child.Executing().ShouldBeFalse("Fixture child remained executing after cleanup.");
                Grandchild.Executing().ShouldBeFalse("Fixture grandchild remained executing after cleanup.");
                Directory.Exists(ResultsDirectory).ShouldBeFalse("Owned results path survived confirmed cleanup.");
            }
            finally
            {
                EmergencyStop();
            }
        }
    }
}
