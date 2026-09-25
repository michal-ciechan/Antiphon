using System.Diagnostics;
using System.Runtime.InteropServices;
using Antiphon.Checkpoints;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Checkpoints;

[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class DetachedLauncherTests
{
    [Test]
    public async Task executor_survives_its_starter()
    {
        if (!OperatingSystem.IsLinux())
        {
            Skip.Test("DetachedLauncher setsid proof runs on Linux; Review runs the Windows half.");
            return;
        }

        var dir = CheckpointFixtures.TempDir();
        var marker = Path.Combine(dir, "done");
        var dll = typeof(DetachedLauncher).Assembly.Location;
        var starter = Process.Start(new ProcessStartInfo("setsid", $"dotnet \"{dll}\" smoke-detach \"{marker}\"")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = dir,
        })!;
        string? line = null;
        var read = Task.Run(async () =>
        {
            while (await starter.StandardOutput.ReadLineAsync() is string next)
            {
                if (next.StartsWith("executor=", StringComparison.Ordinal))
                {
                    line = next;
                    break;
                }
            }
        });
        var completed = await Task.WhenAny(read, Task.Delay(15000));
        completed.ShouldBe(read, "starter did not print the executor pid");
        line.ShouldNotBeNull();
        var executorPid = int.Parse(line!["executor=".Length..]);
        sys_kill(-starter.Id, 15);
        sys_kill(executorPid, 0).ShouldBe(0);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(marker) && DateTime.UtcNow < deadline)
            await Task.Delay(50);
        File.Exists(marker).ShouldBeTrue("the executor died with the starter process group");
        try { sys_kill(-starter.Id, 9); } catch { /* group already gone */ }
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
    private static extern int sys_kill(int pid, int sig);
}
