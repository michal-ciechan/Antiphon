using System.Diagnostics;
using System.Text;
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

    [Test]
    [Arguments("publish")]
    [Arguments("readback")]
    public async Task Io_deadline_is_shared_through_verification(string phase)
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var store = workspace.CreateStore();
        var task = workspace.Task("shared deadline é🙂");
        CancellationToken writeToken = default;
        var sawWrite = false;
        DateTime? ioStart = null;
        DateTime? cancelledAt = null;
        store.BeforeIoAsync = async (name, token) =>
        {
            if (name == "existing-read" && ioStart is null)
                ioStart = DateTime.UtcNow;
            if (name == "write")
            {
                sawWrite = true;
                writeToken = token;
                await Task.Delay(TimeSpan.FromMilliseconds(800), token);
                return;
            }
            if (name != phase) return;
            token.ShouldBe(writeToken, "publish and readback share the write-phase token");
            try { await Task.Delay(TimeSpan.FromSeconds(3), token); }
            catch (OperationCanceledException)
            {
                cancelledAt = DateTime.UtcNow;
                throw;
            }
        };

        var result = await store.StoreAsync(task, CancellationToken.None);
        result.Path.ShouldBeNull(result.Reason);
        result.Reason.ShouldBe("io-budget-expired");
        sawWrite.ShouldBeTrue();
        cancelledAt.ShouldNotBeNull();
        ioStart.ShouldNotBeNull();
        var elapsed = cancelledAt.Value - ioStart.Value;
        elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(1900));
        elapsed.ShouldBeLessThan(TimeSpan.FromMilliseconds(2600), "a fresh budget at the later phase would expire later");
        var canonical = workspace.Expected(task);
        if (phase == "publish")
            File.Exists(canonical).ShouldBeFalse();
        else if (File.Exists(canonical))
            (await File.ReadAllBytesAsync(canonical)).ShouldBe(ExactBytes(task.Result!));
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task Preparation_timeout_releases_gate_without_writing()
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var gate = new GitProcessGate(1);
        var store = workspace.CreateStore(gate);
        store.PreparationBudget = TimeSpan.FromMilliseconds(1500);
        var phases = new List<string>();
        store.BeforeIoAsync = (name, _) =>
        {
            phases.Add(name);
            return Task.CompletedTask;
        };
        var task = workspace.Task("preparation expiry");
        var lease = await gate.EnterAsync(CancellationToken.None);
        try
        {
            var pending = store.StoreAsync(task, CancellationToken.None);
            (await WaitUntilAsync(() => gate.Waiting == 1, TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("the store must queue on the held gate");
            var result = await pending;
            result.Path.ShouldBeNull(result.Reason);
            result.Reason.ShouldBe("preparation-budget-expired");
        }
        finally { lease.Dispose(); }

        phases.ShouldNotContain("write");
        phases.ShouldNotContain("existing-read");
        gate.Waiting.ShouldBe(0);
        gate.InFlight.ShouldBe(0);
        File.Exists(workspace.Expected(task)).ShouldBeFalse();
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        using var acquire = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var again = await gate.EnterAsync(acquire.Token);
        gate.InFlight.ShouldBe(1);
    }

    [Test]
    [Arguments("queued")]
    [Arguments("write")]
    [Arguments("publish")]
    [Arguments("readback")]
    public async Task Caller_cancellation_propagates_in_each_budget_phase(string phase)
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var gate = new GitProcessGate(1);
        var store = workspace.CreateStore(gate);
        var task = workspace.Task("caller cancel " + phase);
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable? lease = null;
        try
        {
            if (phase == "queued")
                lease = await gate.EnterAsync(CancellationToken.None);
            store.BeforeIoAsync = async (name, token) =>
            {
                if (phase == "queued") return;
                if (name != phase) return;
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            };
            var pending = store.StoreAsync(task, caller.Token);
            if (phase == "queued")
            {
                (await WaitUntilAsync(() => gate.Waiting == 1, TimeSpan.FromSeconds(5)))
                    .ShouldBeTrue("queued cancellation starts from the gate wait");
                entered.TrySetResult();
            }
            else
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            caller.Cancel();
            await Should.ThrowAsync<OperationCanceledException>(() => pending);
            if (phase == "queued")
                (await WaitUntilAsync(() => gate.Waiting == 0, TimeSpan.FromSeconds(5))).ShouldBeTrue();
        }
        finally { lease?.Dispose(); }

        if (phase != "readback")
            File.Exists(workspace.Expected(task)).ShouldBeFalse();
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        gate.Waiting.ShouldBe(0);
        gate.InFlight.ShouldBe(0);
    }

    [Test]
    [Arguments("existing-read")]
    [Arguments("readback")]
    public async Task Canceled_fast_path_or_late_readback_never_returns_success(string phase)
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var store = workspace.CreateStore();
        var task = workspace.Task("seed");
        task.Result = "";
        var canonical = workspace.Expected(task);
        if (phase == "existing-read")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
            await File.WriteAllBytesAsync(canonical, []);
        }
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reached = false;
        store.BeforeIoAsync = (name, _) =>
        {
            if (name != phase) return Task.CompletedTask;
            reached = true;
            caller.Cancel();
            return Task.CompletedTask;
        };

        await Should.ThrowAsync<OperationCanceledException>(() => store.StoreAsync(task, caller.Token));
        reached.ShouldBeTrue(phase + " must be the phase that cancels");
        (await File.ReadAllBytesAsync(canonical)).ShouldBe([]);
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    [Arguments("queued")]
    [Arguments("write-stall")]
    public async Task Artifact_preservation_observes_store_budget_outcome(string scenario)
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var unrelated = Path.Combine(workspace.Root, "unrelated-root");
        Directory.CreateDirectory(unrelated);
        var roots = new[] { unrelated };
        GitProcessGate? gate = scenario == "queued" ? new GitProcessGate(1) : null;
        var store = workspace.CreateStore(gate);
        var task = workspace.Task("preserved é🙂");
        var preservation = new CardDoneArtifactPreservation(store);
        IDisposable? lease = null;
        try
        {
            if (scenario == "queued")
            {
                lease = await gate!.EnterAsync(CancellationToken.None);
                var pending = preservation.EnsureAsync(task, roots, missingReviewed: false, CancellationToken.None);
                (await WaitUntilAsync(() => gate.Waiting == 1, TimeSpan.FromSeconds(5)))
                    .ShouldBeTrue("preservation must reach the held gate");
                await Task.Delay(TimeSpan.FromMilliseconds(2500));
                lease.Dispose();
                lease = null;
                var reason = await pending;
                reason.ShouldBeNull(reason);
                task.ResultFilePath.ShouldBe(workspace.Expected(task));
                (await File.ReadAllBytesAsync(task.ResultFilePath!)).ShouldBe(ExactBytes(task.Result!));
            }
            else
            {
                store.BeforeIoAsync = async (phase, token) =>
                {
                    if (phase != "write") return;
                    await Task.Delay(TimeSpan.FromSeconds(2.5), token);
                };
                var reason = await preservation.EnsureAsync(task, roots, missingReviewed: false, CancellationToken.None);
                reason.ShouldBe("artifact_unpreserved");
                task.ResultFilePath.ShouldBeNull();
                File.Exists(workspace.Expected(task)).ShouldBeFalse();
            }
        }
        finally { lease?.Dispose(); }

        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    [Arguments("stored")]
    [Arguments("reused")]
    [Arguments("preparation-expired")]
    [Arguments("io-expired")]
    public async Task Budget_diagnostics_identify_phase_and_outcome(string scenario)
    {
        const string sentinel = "SENTINEL-é🙂-report-bytes";
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var logger = new RecordingLogger<AgentReportStore>();
        GitProcessGate? gate = scenario == "preparation-expired" ? new GitProcessGate(1) : null;
        var store = workspace.CreateStore(gate, logger: logger);
        var task = workspace.Task(sentinel);
        IDisposable? lease = null;
        var wall = Stopwatch.StartNew();
        try
        {
            if (scenario == "reused")
            {
                (await store.StoreAsync(task, CancellationToken.None)).Path.ShouldBe(workspace.Expected(task));
                logger.Entries.Clear();
                wall.Restart();
            }
            else if (scenario == "preparation-expired")
            {
                store.PreparationBudget = TimeSpan.FromMilliseconds(1500);
                lease = await gate!.EnterAsync(CancellationToken.None);
            }
            else if (scenario == "io-expired")
            {
                store.IoBudget.ShouldBe(AgentReportStore.DefaultIoBudget);
                store.BeforeIoAsync = async (phase, token) =>
                {
                    if (phase != "write") return;
                    await Task.Delay(TimeSpan.FromSeconds(2.5), token);
                };
            }

            var pending = store.StoreAsync(task, CancellationToken.None);
            if (scenario == "preparation-expired")
                (await WaitUntilAsync(() => gate!.Waiting == 1, TimeSpan.FromSeconds(5))).ShouldBeTrue();
            var result = await pending;
            wall.Stop();
            if (scenario is "stored" or "reused")
            {
                result.Path.ShouldBe(workspace.Expected(task), result.Reason);
                (await File.ReadAllBytesAsync(result.Path!)).ShouldBe(ExactBytes(sentinel));
            }
            else
            {
                result.Path.ShouldBeNull(result.Reason);
                result.Reason.ShouldBe(scenario == "preparation-expired"
                    ? "preparation-budget-expired" : "io-budget-expired");
            }
        }
        finally { lease?.Dispose(); }

        logger.Entries.Count.ShouldBe(1);
        var entry = logger.Entries[0];
        entry.Exception.ShouldBeNull();
        var outcome = scenario switch
        {
            "stored" => "stored",
            "reused" => "reused",
            "preparation-expired" => "preparation-budget-expired",
            _ => "io-budget-expired",
        };
        entry.State["Outcome"]!.ToString().ShouldBe(outcome);
        var phases = entry.State["Phases"]!.ToString()!;
        if (scenario == "stored")
            phases.ShouldBe("root,custody,existing-read,write,publish,readback");
        else if (scenario == "reused")
            phases.ShouldBe("root,custody,existing-read");
        else if (scenario == "preparation-expired")
        {
            phases.ShouldBe("root");
            entry.State.ContainsKey("IoMs").ShouldBeFalse();
        }
        else
        {
            phases.ShouldContain("write");
            entry.State.ContainsKey("IoMs").ShouldBeTrue();
        }
        var preparation = Convert.ToInt64(entry.State["PreparationMs"]);
        var io = entry.State.TryGetValue("IoMs", out var ioValue) && ioValue is not null ? Convert.ToInt64(ioValue) : 0L;
        var cleanup = entry.State.TryGetValue("CleanupMs", out var cleanupValue) && cleanupValue is not null
            ? Convert.ToInt64(cleanupValue) : 0L;
        var total = Convert.ToInt64(entry.State["TotalMs"]);
        preparation.ShouldBeGreaterThanOrEqualTo(0);
        io.ShouldBeGreaterThanOrEqualTo(0);
        cleanup.ShouldBeGreaterThanOrEqualTo(0);
        total.ShouldBeGreaterThanOrEqualTo(0);
        Math.Abs(total - (preparation + io + cleanup)).ShouldBeLessThanOrEqualTo(150);
        var wallMs = wall.ElapsedMilliseconds;
        wallMs.ShouldBeGreaterThanOrEqualTo(total - 50);
        (wallMs - total).ShouldBeLessThan(1000);
        var rendered = entry.Message + "\n" + string.Join("\n", entry.State.Select(pair => pair.Key + "=" + pair.Value));
        rendered.Contains(sentinel, StringComparison.Ordinal).ShouldBeFalse();
        if (scenario is "stored" or "reused")
            entry.Message.ShouldContain("succeeded");
        else
            entry.Message.ShouldContain("unavailable: " + outcome);
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    public async Task Concurrent_stores_preserve_exact_bytes_after_gate_release()
    {
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var gate = new GitProcessGate(8);
        var store = workspace.CreateStore(gate);
        store.PreparationBudget = Timeout.InfiniteTimeSpan;
        store.IoBudget = Timeout.InfiniteTimeSpan;
        var bodies = Enumerable.Range(0, 8).Select(i => $"concurrent-{i}-é🙂\r\n{new string((char)('A' + i), 32)}").ToArray();
        var tasks = bodies.Select(body => workspace.Task(body)).ToArray();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var leases = new List<IDisposable>();
        try
        {
            for (var i = 0; i < 8; i++)
                leases.Add(await gate.EnterAsync(deadline.Token));
            var pending = tasks.Select(task => store.StoreAsync(task, deadline.Token)).ToArray();
            (await WaitUntilAsync(() => gate.Waiting == 8, TimeSpan.FromSeconds(5)))
                .ShouldBeTrue("eight stores must queue before the barrier releases");
            foreach (var lease in leases) lease.Dispose();
            leases.Clear();
            var results = await Task.WhenAll(pending);
            for (var i = 0; i < tasks.Length; i++)
            {
                results[i].Path.ShouldBe(workspace.Expected(tasks[i]), results[i].Reason);
                (await File.ReadAllBytesAsync(results[i].Path!)).ShouldBe(ExactBytes(tasks[i].Result!));
            }
        }
        finally
        {
            foreach (var lease in leases) lease.Dispose();
        }

        gate.Waiting.ShouldBe(0);
        gate.InFlight.ShouldBe(0);
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    [Test]
    [Arguments("root")]
    [Arguments("write")]
    public async Task Unrelated_cancellation_returns_non_budget_refusal(string phase)
    {
        const string sentinel = "unrelated-cancel-é🙂";
        await using var workspace = await ReportBudgetWorkspace.CreateAsync();
        var logger = new RecordingLogger<AgentReportStore>();
        var store = workspace.CreateStore(logger: logger);
        store.PreparationBudget = Timeout.InfiniteTimeSpan;
        store.IoBudget = Timeout.InfiniteTimeSpan;
        var task = workspace.Task(sentinel);
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var reached = false;
        store.BeforeIoAsync = (name, token) =>
        {
            if (name != phase) return Task.CompletedTask;
            reached = true;
            caller.IsCancellationRequested.ShouldBeFalse();
            token.IsCancellationRequested.ShouldBeFalse();
            throw new OperationCanceledException(foreign.Token);
        };

        var result = await store.StoreAsync(task, caller.Token);
        reached.ShouldBeTrue(phase);
        result.Path.ShouldBeNull();
        result.Reason.ShouldBe("canceled");
        caller.IsCancellationRequested.ShouldBeFalse();
        logger.Entries.Count.ShouldBe(1);
        logger.Entries[0].Message.ShouldContain("unavailable: canceled");
        logger.Entries[0].State["Reason"]!.ToString().ShouldBe("canceled");
        logger.Entries[0].State["Outcome"]!.ToString().ShouldBe("canceled");
        var rendered = logger.Entries[0].Message + string.Join("\n", logger.Entries[0].State.Values);
        rendered.Contains(sentinel, StringComparison.Ordinal).ShouldBeFalse();
        rendered.Contains("io-budget-expired", StringComparison.Ordinal).ShouldBeFalse();
        rendered.Contains("preparation-budget-expired", StringComparison.Ordinal).ShouldBeFalse();
        File.Exists(workspace.Expected(task)).ShouldBeFalse();
        Directory.EnumerateFiles(workspace.Main, "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
    }

    private static byte[] ExactBytes(string raw) => new UTF8Encoding(false, true).GetBytes(raw);

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
