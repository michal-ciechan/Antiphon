using System.Diagnostics;
using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-0589 V-9 (S2+S3): the cross-process proof. Two real <c>scripts/build-slot.ps1</c> wrappers
/// share one broker (<see cref="BuildSlotTestHost"/>, budget 1, pointed at by
/// <c>ANTIPHON_BUILD_SLOTS_URL</c>): the second prints <c>BUILD SLOT waiting</c> and runs only after
/// the first released, and a wrapper killed mid-hold has its lease reaped by pid death so the waiter
/// is granted. Nothing here reaches a production runner.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BuildSlotEndToEndTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    [Test]
    public async Task Two_wrappers_on_a_budget_of_one_run_one_after_the_other()
    {
        await using var host = await StartHostAsync();
        using var dir = new TempDir();
        var marks = Path.Combine(dir.Path, "marks.txt");

        await using var a = Wrapper.Start(host, dir, "a", marks, holdSeconds: 5);
        await WaitUntilAsync(() => host.Broker.List().Leases.Any(l => l.Label == "a"), "a holds the only slot", a);
        await using var b = Wrapper.Start(host, dir, "b", marks, holdSeconds: 1);

        (await a.ExitAsync()).ShouldBe(0, a.Output);
        (await b.ExitAsync()).ShouldBe(0, b.Output);

        a.Output.ShouldContain("BUILD SLOT granted");
        a.Output.ShouldContain("BUILD SLOT released");
        b.Output.ShouldContain("BUILD SLOT waiting label=b position=1 occupied=1/1");
        b.Output.ShouldContain("BUILD SLOT granted");
        var m = Marks(marks);
        m.Keys.ShouldBe(["a start", "a end", "b start", "b end"], ignoreOrder: true);
        m["a end"].ShouldBeLessThanOrEqualTo(m["b start"], "the two holds must not overlap:\n" + File.ReadAllText(marks));
        host.Broker.List().Occupied.ShouldBe(0);
    }

    [Test]
    public async Task A_wrapper_killed_mid_hold_is_reaped_and_the_waiter_is_granted()
    {
        await using var host = await StartHostAsync();
        using var dir = new TempDir();
        var marks = Path.Combine(dir.Path, "marks.txt");

        await using var a = Wrapper.Start(host, dir, "a", marks, holdSeconds: 120);
        await WaitUntilAsync(() => File.Exists(marks) && File.ReadAllText(marks).Contains("a start"), "a is running under its lease", a);
        await using var b = Wrapper.Start(host, dir, "b", marks, holdSeconds: 1);
        await WaitUntilAsync(() => host.Broker.List().Waiters.Any(w => w.Label == "b"), "b is queued behind a", b);

        a.Kill();

        (await b.ExitAsync()).ShouldBe(0, b.Output);
        b.Output.ShouldContain("BUILD SLOT waiting");
        b.Output.ShouldContain("BUILD SLOT granted");
        var m = Marks(marks);
        m.ShouldContainKey("b end");
        m.ShouldNotContainKey("a end");
        host.Broker.List().Leases.ShouldNotContain(l => l.Label == "a");
    }

    private static Task<BuildSlotTestHost> StartHostAsync() => BuildSlotTestHost.StartAsync(new()
    {
        ["SessionRunner:BuildSlots:MaxConcurrent"] = "1",
        ["SessionRunner:BuildSlots:MaxCpuCount"] = "2",
        ["SessionRunner:BuildSlots:MinAvailableMemoryMb"] = "0",
        ["SessionRunner:BuildSlots:RetryAfterMs"] = "250",
        ["SessionRunner:BuildSlots:SweepIntervalMs"] = "1000",
    });

    private static Dictionary<string, long> Marks(string path) =>
        File.ReadAllLines(path)
            .Select(l => l.Split(' '))
            .ToDictionary(p => p[0] + " " + p[1], p => long.Parse(p[2]));

    private static async Task WaitUntilAsync(Func<bool> condition, string what, Wrapper owner)
    {
        var until = DateTime.UtcNow + Deadline;
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException($"timed out waiting until {what}\n{owner.Output}");
            await Task.Delay(100);
        }
    }

    private sealed class Wrapper : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();

        private Wrapper(Process process) => _process = process;

        public string Output { get { lock (_output) return _output.ToString(); } }

        public static Wrapper Start(BuildSlotTestHost host, TempDir dir, string label, string marks, int holdSeconds)
        {
            var hold = Path.Combine(dir.Path, "hold.ps1");
            if (!File.Exists(hold))
                File.WriteAllText(hold, """
                    param([string]$Name, [string]$File, [int]$Seconds)
                    Add-Content -LiteralPath $File -Value ('{0} start {1}' -f $Name, [DateTime]::UtcNow.Ticks)
                    Start-Sleep -Seconds $Seconds
                    Add-Content -LiteralPath $File -Value ('{0} end {1}' -f $Name, [DateTime]::UtcNow.Ticks)
                    """);
            var info = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = RepoRoot,
            };
            foreach (var arg in new[]
                     {
                         "-NoProfile", "-NonInteractive", "-File", Path.Combine(RepoRoot, "scripts", "build-slot.ps1"),
                         "-Label", label, "-SlotWaitMinutes", "2", "--",
                         "pwsh", "-NoProfile", "-NonInteractive", "-File", hold, label, marks, holdSeconds.ToString(),
                     })
                info.ArgumentList.Add(arg);
            info.Environment["ANTIPHON_BUILD_SLOTS_URL"] = host.SlotsUrl;
            foreach (var seam in new[] { "C589_SLOT_SHIM", "C589_SLOT_SCRIPT", "C589_SLOT_WAIT_SECONDS", "C589_SLOT_RETRY_MS", "C589_SLOT_GRACE_SECONDS" })
                info.Environment.Remove(seam);

            var process = new Process { StartInfo = info };
            var wrapper = new Wrapper(process);
            process.OutputDataReceived += (_, e) => wrapper.Append(e.Data);
            process.ErrorDataReceived += (_, e) => wrapper.Append(e.Data);
            process.Start().ShouldBeTrue();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return wrapper;
        }

        private void Append(string? line)
        {
            if (line is null) return;
            lock (_output) _output.AppendLine(line);
        }

        public async Task<int> ExitAsync()
        {
            using var timeout = new CancellationTokenSource(Deadline);
            try
            {
                await _process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException("wrapper did not exit\n" + Output);
            }
            _process.WaitForExit(); // drain the async output readers
            return _process.ExitCode;
        }

        public void Kill()
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(15_000).ShouldBeTrue("killed wrapper did not exit");
        }

        public ValueTask DisposeAsync()
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            _process.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = Directory.CreateDirectory(System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "c589-e2e-" + Guid.NewGuid().ToString("N"))).FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
