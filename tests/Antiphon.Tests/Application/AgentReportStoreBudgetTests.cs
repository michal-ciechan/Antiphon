using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Files;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1605. Production defaults only, except the cases that name an override.
/// V-1 and V-2 are the baseline red witnesses: today's single two-second token
/// expires while Git is still queued or still answering.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class AgentReportStoreBudgetTests
{
    [Test]
    public async Task Gate_wait_longer_than_write_budget_still_preserves_report()
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var gate = new GitProcessGate(1);
        var store = workspace.CreateStore(gate);
        var task = workspace.Task("é🙂\r\nreport budget");
        var lease = await gate.EnterAsync(CancellationToken.None);
        var early = 0;
        var pending = store.StoreAsync(task, CancellationToken.None);
        _ = pending.ContinueWith(
            _ => Volatile.Write(ref early, 1),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        try
        {
            var sawQueue = await WaitUntilAsync(() => gate.Waiting == 1, TimeSpan.FromSeconds(5));
            sawQueue.ShouldBeTrue("the store must enter the private gate queue");
            await Task.Delay(TimeSpan.FromMilliseconds(2500));
        }
        finally { lease.Dispose(); }

        var completedEarly = Volatile.Read(ref early) == 1;
        var result = await pending;
        result.Path.ShouldBe(workspace.Expected(task), result.Reason);
        completedEarly.ShouldBeFalse("the store finished before the 2.5s gate hold was released");
        (await File.ReadAllBytesAsync(result.Path!)).ShouldBe(
            new System.Text.UTF8Encoding(false, true).GetBytes(task.Result!));
        gate.Waiting.ShouldBe(0);
        gate.InFlight.ShouldBe(0);
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task Slow_git_response_does_not_spend_write_budget()
    {
        NativeLinuxGate.Require("CARD-1605 delayed executable requires native Linux");
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var marker = Path.Combine(workspace.Root, "git-entered");
        var executable = workspace.CreateDelayedGit(marker);
        var store = workspace.CreateStore(gitSettings: new GitSettings { ExecutableName = executable });
        var task = workspace.Task("é🙂\r\nslow git");
        var result = await store.StoreAsync(task, CancellationToken.None);
        result.Path.ShouldBe(workspace.Expected(task), result.Reason);
        File.Exists(marker).ShouldBeTrue("the delayed git wrapper must have been entered");
        var check = await RunGitAsync(workspace.Main, "check-ignore", "--no-index", "-q", "--", result.Path!);
        check.ShouldBe(0, "custody must be a real git check-ignore, not a skipped proof");
        (await File.ReadAllBytesAsync(result.Path!)).ShouldBe(
            new System.Text.UTF8Encoding(false, true).GetBytes(task.Result!));
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task Write_stall_keeps_two_second_refusal()
    {
        AgentReportStore.DefaultIoBudget.ShouldBe(TimeSpan.FromSeconds(2));
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var store = workspace.CreateStore();
        store.IoBudget.ShouldBe(AgentReportStore.DefaultIoBudget);
        var task = workspace.Task("write stall body");
        DateTime? ioStart = null;
        DateTime? cancelledAt = null;
        var cancelObserved = false;
        store.BeforeIoAsync = async (phase, token) =>
        {
            if (phase == "existing-read" && ioStart is null)
                ioStart = DateTime.UtcNow;
            if (phase != "write") return;
            try { await Task.Delay(TimeSpan.FromSeconds(2.5), token); }
            catch (OperationCanceledException)
            {
                cancelObserved = true;
                cancelledAt = DateTime.UtcNow;
                throw;
            }
        };

        var result = await store.StoreAsync(task, CancellationToken.None);
        result.Path.ShouldBeNull(result.Reason);
        result.Reason.ShouldBe("io-budget-expired");
        cancelObserved.ShouldBeTrue("the write delay must be cut short by the I/O token");
        ioStart.ShouldNotBeNull("existing-read is the I/O boundary immediately after the two-second timer");
        (cancelledAt!.Value - ioStart!.Value).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1900));
        store.IoBudget.ShouldBe(AgentReportStore.DefaultIoBudget);
        File.Exists(workspace.Expected(task)).ShouldBeFalse();
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> ready, TimeSpan budget)
    {
        var until = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < until)
        {
            if (ready()) return true;
            await Task.Delay(20);
        }
        return ready();
    }

    private static async Task<int> RunGitAsync(string cwd, params string[] args)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo(ReportBudgetWorkspace.ResolveGitExecutable())
        {
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
