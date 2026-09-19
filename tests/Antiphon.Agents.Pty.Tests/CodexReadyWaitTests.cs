using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Agents.Pty.Tests;

[Category("Unit")]
public class CodexReadyWaitTests
{
    [Test]
    public async Task One_observation_never_establishes_readiness()
    {
        var time = new FakeTimeProvider();
        var second = new TaskCompletionSource<CodexStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        var gate = CodexReadyWait.WaitAsync(
            async ct =>
            {
                var n = Interlocked.Increment(ref reads);
                if (n == 1)
                    return Snap(CodexStartupFixtures.P3);
                secondStarted.TrySetResult();
                return await second.Task.WaitAsync(ct);
            },
            Options(time, settleMs: 1000, maxMs: 60_000));

        await WaitUntilAsync(() => reads >= 1);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(1000));
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.IsCompleted.ShouldBeFalse("R-18: after a full settle when only the first positive snapshot has completed and the second read is held");
        second.TrySetResult(Snap(CodexStartupFixtures.P3));
        (await gate).ShouldBeTrue("R-18: releasing the second fresh positive frame permits success");
    }

    [Test]
    public async Task A_new_wait_and_generation_start_without_a_candidate()
    {
        var time = new FakeTimeProvider();
        (await WaitReadyAsync(time, CodexStartupFixtures.P3, settleMs: 50, maxMs: 5_000)).ShouldBeTrue();

        var secondHold = new TaskCompletionSource<CodexStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondWait = CodexReadyWait.WaitAsync(
            ct =>
            {
                started.TrySetResult();
                return secondHold.Task.WaitAsync(ct);
            },
            Options(time, settleMs: 1000, maxMs: 60_000));
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            secondWait.IsCompleted.ShouldBeFalse("R-21: secondWait.IsCompleted.ShouldBeFalse() before its own settle");
        }
        finally
        {
            secondHold.TrySetResult(null);
            time.Advance(TimeSpan.FromMilliseconds(60_000));
            try { await secondWait; }
            catch (Exception) { /* deadline or unavailable snapshot */ }
        }
    }

    [Test]
    public async Task Deadline_or_zero_total_budget_never_authorizes_input()
    {
        var time = new FakeTimeProvider();
        (await CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.N1)),
            Options(time, settleMs: 1000, maxMs: 0)))
            .ShouldBeFalse("R-22: zero total wait");

        var blocked = CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.N1)),
            Options(time, settleMs: 1000, maxMs: 1_000));
        await WaitUntilAsync(() => blocked.IsCompleted is false);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(1000));
        (await blocked).ShouldBeFalse("R-22: permanent blockers");

        var atExpiry = CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3)),
            Options(time, settleMs: 1000, maxMs: 500));
        await WaitUntilAsync(() => !atExpiry.IsCompleted);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(500));
        (await atExpiry).ShouldBeFalse("R-22: ready-at-expiry");
    }

    [Test]
    public async Task Trust_consumes_the_original_total_budget()
    {
        var time = new FakeTimeProvider();
        var origin = time.GetUtcNow();
        var writes = new List<string>();
        var reads = 0;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                var elapsed = time.GetUtcNow() - origin;
                if (writes.Count == 0 && elapsed >= TimeSpan.FromMilliseconds(600))
                {
                    return Task.FromResult<CodexStartupSnapshot?>(Snap(
                        CodexStartupFixtures.InsertBeforeComposer(
                            CodexStartupFixtures.P3,
                            "Do you trust the contents of this directory? Yes, continue")));
                }

                if (writes.Count == 0)
                {
                    return Task.FromResult<CodexStartupSnapshot?>(Snap(
                        CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "loading")));
                }

                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            Options(time, settleMs: 1000, maxMs: 1500),
            (input, _) =>
            {
                writes.Add(input);
                return Task.CompletedTask;
            });
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(600));
        await WaitUntilAsync(() => writes.Count > 0);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(900));
        (await gate).ShouldBeFalse("R-23: result.ShouldBeFalse() when trust leaves less than a settle");
        writes.ShouldBe(["\r"]);
    }

    [Test]
    public async Task A_stalled_snapshot_finishes_at_the_gate_deadline()
    {
        var time = new FakeTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            async ct =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return Snap(CodexStartupFixtures.P3);
            },
            Options(time, settleMs: 1000, maxMs: 2_000),
            ct: cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(2_000));
        var finished = await Task.WhenAny(gate, Task.Delay(TimeSpan.FromSeconds(2)));
        finished.ShouldBe(gate, "R-25: gate.IsCompleted.ShouldBeTrue() at virtual deadline with no caller cancellation");
        (await gate).ShouldBeFalse("R-25");
        cts.IsCancellationRequested.ShouldBeFalse("R-25: no caller cancellation");
    }

    [Test]
    public async Task A_stalled_trust_write_finishes_at_the_gate_deadline()
    {
        var time = new FakeTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(
                CodexStartupFixtures.InsertBeforeComposer(
                    CodexStartupFixtures.P3,
                    "Do you trust the contents of this directory? Yes, continue"))),
            Options(time, settleMs: 1000, maxMs: 2_000),
            async (_, ct) =>
            {
                entered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            },
            ct: cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(2_000));
        var finished = await Task.WhenAny(gate, Task.Delay(TimeSpan.FromSeconds(2)));
        finished.ShouldBe(gate, "R-26: gate.IsCompleted.ShouldBeTrue() at virtual deadline");
        (await gate).ShouldBeFalse("R-26");
    }

    [Test]
    public async Task Cancellation_at_entry_prevents_reads_and_input()
    {
        var time = new FakeTimeProvider();
        var reads = 0;
        var writes = new List<string>();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(async () =>
            await CodexReadyWait.WaitAsync(
                _ =>
                {
                    Interlocked.Increment(ref reads);
                    return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
                },
                Options(time),
                (input, _) =>
                {
                    writes.Add(input);
                    return Task.CompletedTask;
                },
                ct: cts.Token));
        reads.ShouldBe(0, "R-27: readCalls.ShouldBe(0)");
        writes.ShouldBeEmpty("R-27: writes.ShouldBeEmpty()");
    }

    [Test]
    public async Task Cancellation_during_read_prevents_trust_input()
    {
        var time = new FakeTimeProvider();
        var writes = new List<string>();
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                cts.Cancel();
                return Task.FromResult<CodexStartupSnapshot?>(Snap(
                    CodexStartupFixtures.InsertBeforeComposer(
                        CodexStartupFixtures.P3,
                        "Do you trust the contents of this directory? Yes, continue")));
            },
            Options(time),
            (input, _) =>
            {
                writes.Add(input);
                return Task.CompletedTask;
            },
            ct: cts.Token);
        await Should.ThrowAsync<OperationCanceledException>(async () => await gate);
        writes.ShouldBeEmpty("R-28: writes.ShouldBeEmpty() when the snapshot delegate cancels the caller then returns trust");
    }

    [Test]
    public async Task Cancellation_on_the_final_snapshot_cannot_return_ready()
    {
        var time = new FakeTimeProvider();
        var reads = 0;
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                var n = Interlocked.Increment(ref reads);
                if (n >= 2)
                    cts.Cancel();
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            Options(time, settleMs: 50, maxMs: 5_000),
            ct: cts.Token);
        await WaitUntilAsync(() => reads >= 1);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(50));
        await Should.ThrowAsync<OperationCanceledException>(async () => await gate, "R-29");
    }

    [Test]
    public async Task Exited_process_never_receives_startup_input()
    {
        var time = new FakeTimeProvider();
        var writes = new List<string>();
        var result = await CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(
                CodexStartupFixtures.InsertBeforeComposer(
                    CodexStartupFixtures.P3,
                    "Do you trust the contents of this directory? Yes, continue"))),
            Options(time),
            (input, _) =>
            {
                writes.Add(input);
                return Task.CompletedTask;
            },
            isExited: () => true);
        writes.ShouldBeEmpty("R-30");
        result.ShouldBeFalse("R-30");
    }

    [Test]
    public async Task Exit_on_the_final_snapshot_cannot_return_ready()
    {
        var time = new FakeTimeProvider();
        var reads = 0;
        var exited = false;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                var n = Interlocked.Increment(ref reads);
                if (n >= 2)
                    exited = true;
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            Options(time, settleMs: 50, maxMs: 5_000),
            isExited: () => exited);
        await WaitUntilAsync(() => reads >= 1);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(50));
        (await gate).ShouldBeFalse("R-31");
    }

    [Test]
    public async Task Unavailable_or_failed_snapshot_cannot_reuse_a_ready_frame()
    {
        var time = new FakeTimeProvider();
        var injected = new InvalidOperationException("snapshot failed");
        var result = CodexReadyWait.WaitAsync(
            _ => throw injected,
            Options(time, settleMs: 50, maxMs: 1_000));
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await result);
        ex.ShouldBe(injected, "R-32: snapshot exception remains the injected exception");

        var reads = 0;
        var nulls = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult<CodexStartupSnapshot?>(
                    reads == 1 ? Snap(CodexStartupFixtures.P3) : null);
            },
            Options(time, settleMs: 50, maxMs: 200));
        await WaitUntilAsync(() => reads >= 1);
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(200));
        (await nulls).ShouldBeFalse("R-32: no successful result");
    }

    [Test]
    public async Task Trust_response_forces_a_new_snapshot_and_full_settle()
    {
        var time = new FakeTimeProvider();
        var writes = 0;
        var readyHold = new TaskCompletionSource<CodexStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = CodexReadyWait.WaitAsync(
            ct =>
            {
                if (Volatile.Read(ref writes) == 0)
                {
                    return Task.FromResult<CodexStartupSnapshot?>(Snap(
                        CodexStartupFixtures.InsertBeforeComposer(
                            CodexStartupFixtures.P3,
                            "Do you trust the contents of this directory? Yes, continue")));
                }

                readyStarted.TrySetResult();
                return readyHold.Task.WaitAsync(ct);
            },
            Options(time, settleMs: 1000, maxMs: 60_000),
            (_, _) =>
            {
                Interlocked.Exchange(ref writes, 1);
                return Task.CompletedTask;
            });
        await WaitUntilAsync(() => Volatile.Read(ref writes) == 1);
        readyHold.TrySetResult(Snap(CodexStartupFixtures.P3));
        await readyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(999));
        gate.IsCompleted.ShouldBeFalse("R-37: ready.IsCompleted.ShouldBeFalse() until the post-action full settle");
        await AdvanceAfterWaitStartedAsync(time, TimeSpan.FromMilliseconds(50));
        (await gate).ShouldBeTrue();
    }

    private static CodexReadyWaitOptions Options(
        FakeTimeProvider time, int settleMs = 1000, int maxMs = 60_000) => new()
    {
        TimeProvider = time,
        Settle = TimeSpan.FromMilliseconds(settleMs),
        MaxWait = TimeSpan.FromMilliseconds(maxMs),
        PollInterval = TimeSpan.FromMilliseconds(50),
        BootStatusThreshold = TimeSpan.FromMilliseconds(10_000),
    };

    private static CodexStartupSnapshot Snap(string screen) => new(screen, screen);

    private static async Task<bool> WaitReadyAsync(
        FakeTimeProvider time, string screen, int settleMs, int maxMs)
    {
        var reads = 0;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(screen));
            },
            Options(time, settleMs, maxMs));
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1);
        await Task.Delay(25);
        time.Advance(TimeSpan.FromMilliseconds(settleMs + 50));
        return await gate;
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        var start = DateTime.UtcNow;
        while (!predicate())
        {
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
                throw new TimeoutException("condition not met");
            await Task.Yield();
        }
    }

    private static async Task AdvanceAfterWaitStartedAsync(FakeTimeProvider time, TimeSpan delta)
    {
        await Task.Delay(25);
        time.Advance(delta);
    }
}
