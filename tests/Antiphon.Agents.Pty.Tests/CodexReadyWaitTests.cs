using System.Diagnostics;
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
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            async ct =>
            {
                var n = Interlocked.Increment(ref reads);
                if (n == 1)
                    return Snap(CodexStartupFixtures.P3);
                secondStarted.TrySetResult();
                return await second.Task.WaitAsync(ct);
            },
            Options(time, settleMs: 1000, maxMs: 60_000), ct: cts.Token);

        try
        {
            await AdvanceUntilAsync(time, () => secondStarted.Task.IsCompleted, 20, "R-18 second read");
            time.Advance(TimeSpan.FromMilliseconds(1000));
            gate.IsCompleted.ShouldBeFalse("R-18: after a full settle when only the first positive snapshot has completed and the second read is held");
            second.TrySetResult(Snap(CodexStartupFixtures.P3));
            await AdvanceUntilAsync(time, () => gate.IsCompleted, 20, "R-18 readiness after second read");
            (await gate.WaitAsync(RealTimeout)).ShouldBeTrue("R-18: releasing the second fresh positive frame permits success");
        }
        finally
        {
            second.TrySetResult(null);
            cts.Cancel();
            await DrainAfterCleanupAsync(gate, cts.Token);
        }
    }

    [Test]
    public async Task A_new_wait_and_generation_start_without_a_candidate()
    {
        var time = new FakeTimeProvider();
        (await WaitReadyAsync(time, CodexStartupFixtures.P3, settleMs: 50, maxMs: 5_000)).ShouldBeTrue();

        var secondHold = new TaskCompletionSource<CodexStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var secondWait = CodexReadyWait.WaitAsync(
            ct =>
            {
                started.TrySetResult();
                return secondHold.Task.WaitAsync(ct);
            },
            Options(time, settleMs: 1000, maxMs: 60_000), ct: cts.Token);
        try
        {
            await started.Task.WaitAsync(RealTimeout);
            secondWait.IsCompleted.ShouldBeFalse("R-21: secondWait.IsCompleted.ShouldBeFalse() before its own settle");
        }
        finally
        {
            secondHold.TrySetResult(null);
            cts.Cancel();
            await DrainAfterCleanupAsync(secondWait, cts.Token);
        }
    }

    [Test]
    public async Task Deadline_or_zero_total_budget_never_authorizes_input()
    {
        var time = new FakeTimeProvider();
        (await CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.N1)),
            Options(time, settleMs: 1000, maxMs: 0)).WaitAsync(RealTimeout))
            .ShouldBeFalse("R-22: zero total wait");

        var blockedReads = 0;
        var blocked = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref blockedReads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.N1));
            },
            Options(time, settleMs: 1000, maxMs: 1_000));
        await WaitUntilAsync(() => Volatile.Read(ref blockedReads) >= 1, "R-22 blocked first read");
        await AdvanceUntilAsync(time, () => blocked.IsCompleted, 22, "R-22 permanent blocker deadline");
        (await blocked.WaitAsync(RealTimeout)).ShouldBeFalse("R-22: permanent blockers");

        var expiryReads = 0;
        var atExpiry = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref expiryReads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            Options(time, settleMs: 1000, maxMs: 500));
        await WaitUntilAsync(() => Volatile.Read(ref expiryReads) >= 1, "R-22 ready first read");
        await AdvanceUntilAsync(time, () => atExpiry.IsCompleted, 12, "R-22 ready-at-expiry deadline");
        (await atExpiry.WaitAsync(RealTimeout)).ShouldBeFalse("R-22: ready-at-expiry");
    }

    [Test]
    public async Task Trust_consumes_the_original_total_budget()
    {
        var time = new FakeTimeProvider();
        var origin = time.GetUtcNow();
        var writes = new List<string>();
        var writeCount = 0;
        var reads = 0;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                var elapsed = time.GetUtcNow() - origin;
                if (Volatile.Read(ref writeCount) == 0 && elapsed >= TimeSpan.FromMilliseconds(600))
                {
                    return Task.FromResult<CodexStartupSnapshot?>(Snap(
                        CodexStartupFixtures.InsertBeforeComposer(
                            CodexStartupFixtures.P3,
                            "Do you trust the contents of this directory? Yes, continue")));
                }

                if (Volatile.Read(ref writeCount) == 0)
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
                Interlocked.Increment(ref writeCount);
                return Task.CompletedTask;
            });
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "R-23 first read");
        time.Advance(TimeSpan.FromMilliseconds(600));
        await AdvanceUntilAsync(time, () => Volatile.Read(ref writeCount) > 0, 17, "R-23 trust write");
        (time.GetUtcNow() - origin).ShouldBeLessThan(TimeSpan.FromMilliseconds(1500),
            "R-23: trust input must precede the original deadline");
        var remainingSteps = (int)Math.Ceiling((origin + TimeSpan.FromMilliseconds(1500) - time.GetUtcNow()).TotalMilliseconds / 50) + 2;
        await AdvanceUntilAsync(time, () => gate.IsCompleted, remainingSteps, "R-23 original total deadline");
        (await gate.WaitAsync(RealTimeout)).ShouldBeFalse("R-23: result.ShouldBeFalse() when trust leaves less than a settle");
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
        try
        {
            await entered.Task.WaitAsync(RealTimeout);
            time.Advance(TimeSpan.FromMilliseconds(2_000));
            (await gate.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeFalse("R-25: gate finishes at the virtual deadline");
            cts.IsCancellationRequested.ShouldBeFalse("R-25: no caller cancellation");
        }
        finally
        {
            cts.Cancel();
            await DrainAfterCleanupAsync(gate, cts.Token);
        }
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
        try
        {
            await entered.Task.WaitAsync(RealTimeout);
            time.Advance(TimeSpan.FromMilliseconds(2_000));
            (await gate.WaitAsync(TimeSpan.FromSeconds(2))).ShouldBeFalse("R-26: gate finishes at the virtual deadline");
            cts.IsCancellationRequested.ShouldBeFalse("R-26: no caller cancellation");
        }
        finally
        {
            cts.Cancel();
            await DrainAfterCleanupAsync(gate, cts.Token);
        }
    }

    [Test]
    public async Task Cancellation_at_entry_prevents_reads_and_input()
    {
        var time = new FakeTimeProvider();
        var reads = 0;
        var writes = new List<string>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
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
                ct: cts.Token).WaitAsync(RealTimeout));
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
        await Should.ThrowAsync<OperationCanceledException>(async () => await gate.WaitAsync(RealTimeout));
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
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "R-29 first read");
        await AdvanceUntilAsync(time, () => gate.IsCompleted, 20, "R-29 final snapshot cancellation");
        Volatile.Read(ref reads).ShouldBeGreaterThanOrEqualTo(2, "R-29: final snapshot must cancel the caller");
        await Should.ThrowAsync<OperationCanceledException>(async () => await gate.WaitAsync(RealTimeout), "R-29");
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
            isExited: () => true).WaitAsync(RealTimeout);
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
                    Volatile.Write(ref exited, true);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            Options(time, settleMs: 50, maxMs: 5_000),
            isExited: () => Volatile.Read(ref exited));
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "R-31 first read");
        await AdvanceUntilAsync(time, () => gate.IsCompleted, 20, "R-31 final snapshot exit");
        Volatile.Read(ref reads).ShouldBeGreaterThanOrEqualTo(2, "R-31: the final snapshot must observe exit");
        (await gate.WaitAsync(RealTimeout)).ShouldBeFalse("R-31");
    }

    [Test]
    public async Task Unavailable_or_failed_snapshot_cannot_reuse_a_ready_frame()
    {
        var time = new FakeTimeProvider();
        var injected = new InvalidOperationException("snapshot failed");
        var result = CodexReadyWait.WaitAsync(
            _ => throw injected,
            Options(time, settleMs: 50, maxMs: 1_000));
        var ex = await Should.ThrowAsync<InvalidOperationException>(async () => await result.WaitAsync(RealTimeout));
        ex.ShouldBe(injected, "R-32: snapshot exception remains the injected exception");

        var reads = 0;
        var nulls = CodexReadyWait.WaitAsync(
            _ =>
            {
                var read = Interlocked.Increment(ref reads);
                return Task.FromResult<CodexStartupSnapshot?>(
                    read == 1 ? Snap(CodexStartupFixtures.P3) : null);
            },
            Options(time, settleMs: 50, maxMs: 200));
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "R-32 first ready read");
        var started = time.GetUtcNow();
        await AdvanceUntilAsync(time, () => Volatile.Read(ref reads) >= 2, 3, "R-32 null observation");
        (time.GetUtcNow() - started).ShouldBeLessThan(TimeSpan.FromMilliseconds(200),
            "R-32: a null snapshot must be observed before expiry");
        await AdvanceUntilAsync(time, () => nulls.IsCompleted, 5, "R-32 null snapshot deadline");
        (await nulls.WaitAsync(RealTimeout)).ShouldBeFalse("R-32: no successful result");
    }

    [Test]
    public async Task Trust_response_forces_a_new_snapshot_and_full_settle()
    {
        var time = new FakeTimeProvider();
        var writes = 0;
        var readyHold = new TaskCompletionSource<CodexStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var readyReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            async ct =>
            {
                if (Volatile.Read(ref writes) == 0)
                {
                    return Snap(
                        CodexStartupFixtures.InsertBeforeComposer(
                            CodexStartupFixtures.P3,
                            "Do you trust the contents of this directory? Yes, continue"));
                }

                readyStarted.TrySetResult();
                var snapshot = await readyHold.Task.WaitAsync(ct);
                readyReturned.TrySetResult();
                return snapshot;
            },
            Options(time, settleMs: 1000, maxMs: 60_000),
            (_, _) =>
            {
                Interlocked.Exchange(ref writes, 1);
                return Task.CompletedTask;
            }, ct: cts.Token);
        try
        {
            await WaitUntilAsync(() => Volatile.Read(ref writes) == 1, "R-37 trust write");
            await readyStarted.Task.WaitAsync(RealTimeout);
            readyHold.TrySetResult(Snap(CodexStartupFixtures.P3));
            await readyReturned.Task.WaitAsync(RealTimeout);
            time.Advance(TimeSpan.FromMilliseconds(999));
            gate.IsCompleted.ShouldBeFalse("R-37: ready.IsCompleted.ShouldBeFalse() until the post-action full settle");
            await AdvanceUntilAsync(time, () => gate.IsCompleted, 40, "R-37 post-trust full settle");
            (await gate.WaitAsync(RealTimeout)).ShouldBeTrue();
        }
        finally
        {
            readyHold.TrySetResult(null);
            cts.Cancel();
            await DrainAfterCleanupAsync(gate, cts.Token);
        }
    }

    [Test]
    [Arguments("0.156.1")]
    [Arguments("0.158.0")]
    public async Task Update_modal_mid_wait_is_skipped_once_then_requires_fresh_readiness(string version)
    {
        var time = new FakeTimeProvider();
        var modal = version == "0.156.1"
            ? CodexStartupFixtures.V0156UpdateModal
            : CodexStartupFixtures.V0158UpdateModal;
        var loading = CodexStartupFixtures.ReplaceModelValue(CodexStartupFixtures.P3, "loading");
        var writes = new List<string>();
        var reads = 0;
        var postWriteReads = 0;
        DateTimeOffset? readyAt = null;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                var read = Interlocked.Increment(ref reads);
                var screen = read == 1 ? loading : modal;
                if (writes.Count > 0)
                {
                    // A stale modal frame may survive the Escape write. The next poll clears it.
                    screen = Interlocked.Increment(ref postWriteReads) == 1
                        ? modal
                        : CodexStartupFixtures.P3;
                    if (screen == CodexStartupFixtures.P3)
                        readyAt ??= time.GetUtcNow();
                }

                return Task.FromResult<CodexStartupSnapshot?>(Snap(screen));
            },
            Options(time, settleMs: 100, maxMs: 1000),
            (input, _) =>
            {
                writes.Add(input);
                return Task.CompletedTask;
            });

        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "update picker first read");
        await AdvanceUntilAsync(time, () => Volatile.Read(ref reads) >= 2, 10, "update picker second read");
        Volatile.Read(ref reads).ShouldBeGreaterThanOrEqualTo(2,
            "the update picker must be read after the loading frame");
        gate.IsCompleted.ShouldBeFalse("the update picker cannot be considered ready");

        await AdvanceUntilAsync(time, () => gate.IsCompleted, 20, "update picker fresh readiness");

        (await gate.WaitAsync(RealTimeout)).ShouldBeTrue("without an Escape write, the picker remains until the deadline");
        writes.ShouldBe(["\u001b"]);
        postWriteReads.ShouldBeGreaterThanOrEqualTo(2, "a stale modal is polled after Escape");
        readyAt.ShouldNotBeNull();
        (time.GetUtcNow() - readyAt.Value).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(100),
            "the new ready frame needs a full settle");
    }

    [Test]
    public async Task Bare_continue_prompt_does_not_send_escape()
    {
        var time = new FakeTimeProvider();
        var screen = CodexStartupFixtures.InsertBeforeComposer(
            CodexStartupFixtures.P3, "Press enter to continue");
        var writes = new List<string>();
        var reads = 0;
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(screen));
            },
            Options(time, settleMs: 50, maxMs: 200),
            (input, _) =>
            {
                writes.Add(input);
                return Task.CompletedTask;
            });

        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "bare continue first read");
        await AdvanceUntilAsync(time, () => gate.IsCompleted, 6, "bare continue deadline");
        (await gate.WaitAsync(RealTimeout)).ShouldBeFalse();
        writes.ShouldBeEmpty();
    }

    [Test]
    public async Task Deadline_immediately_after_escape_reports_blocking_update()
    {
        var time = new FakeTimeProvider();
        var diagnostics = new List<string>();
        var writes = new List<string>();
        var gate = CodexReadyWait.WaitAsync(
            _ => Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.V0158UpdateModal)),
            new CodexReadyWaitOptions
            {
                TimeProvider = time,
                MaxWait = TimeSpan.FromMilliseconds(200),
                OnDiagnostic = diagnostics.Add,
            },
            (input, _) =>
            {
                writes.Add(input);
                time.Advance(TimeSpan.FromMilliseconds(200));
                return Task.CompletedTask;
            });

        (await gate.WaitAsync(RealTimeout)).ShouldBeFalse();
        writes.ShouldBe(["\u001b"]);
        string.Join('\n', diagnostics).ShouldContain("update-picker escape-sent");
        string.Join('\n', diagnostics).ShouldContain("not-ready reason=BlockingUpdate");
    }

    [Test]
    public async Task Not_ready_hands_the_last_frame_over_once_and_keeps_it_out_of_the_diagnostic()
    {
        // CARD-0777: a timeout must leave the screen that explains it; the log line stays screen-free.
        var time = new FakeTimeProvider();
        var modal = CodexStartupFixtures.V0158UpdateModal;
        var frames = new List<CodexStartupSnapshot?>();
        var diagnostics = new List<string>();
        var reads = 0;
        var maxWait = TimeSpan.FromMilliseconds(1_000);
        var gate = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref reads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(modal));
            },
            new CodexReadyWaitOptions
            {
                TimeProvider = time,
                Settle = TimeSpan.FromMilliseconds(50),
                MaxWait = maxWait,
                PollInterval = TimeSpan.FromMilliseconds(50),
                OnDiagnostic = diagnostics.Add,
                OnNotReadyFrame = frames.Add,
            });
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "frame diagnostic first read");
        await AdvanceUntilAsync(time, () => Volatile.Read(ref reads) >= 2, 10, "frame diagnostic second read");
        Volatile.Read(ref reads).ShouldBeGreaterThanOrEqualTo(2,
            "the update picker must be classified before the timeout diagnostic");
        time.Advance(maxWait);
        await AdvanceUntilAsync(time, () => gate.IsCompleted, 2, "frame diagnostic deadline");

        (await gate.WaitAsync(RealTimeout)).ShouldBeFalse();
        frames.Count.ShouldBe(1);
        frames[0].ShouldNotBeNull().RenderedScreen.ShouldBe(modal);
        string.Join('\n', diagnostics).ShouldContain("reason=BlockingUpdate");
        string.Join('\n', diagnostics).ShouldNotContain("Skip until next version");

        var readyFrames = new List<CodexStartupSnapshot?>();
        var readyReads = 0;
        var ready = CodexReadyWait.WaitAsync(
            _ =>
            {
                Interlocked.Increment(ref readyReads);
                return Task.FromResult<CodexStartupSnapshot?>(Snap(CodexStartupFixtures.P3));
            },
            new CodexReadyWaitOptions
            {
                TimeProvider = time,
                Settle = TimeSpan.FromMilliseconds(50),
                MaxWait = TimeSpan.FromMilliseconds(5_000),
                PollInterval = TimeSpan.FromMilliseconds(50),
                OnNotReadyFrame = readyFrames.Add,
            });
        await WaitUntilAsync(() => Volatile.Read(ref readyReads) >= 1, "ready frame first read");
        await AdvanceUntilAsync(time, () => ready.IsCompleted, 20, "ready frame full settle");
        (await ready.WaitAsync(RealTimeout)).ShouldBeTrue();
        readyFrames.ShouldBeEmpty();
    }

    [Test]
    public async Task Snapshot_released_after_the_first_clock_advance_still_reaches_readiness()
    {
        var time = new FakeTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TaskCompletionSource<CodexStartupSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completedReads = 0;
        using var cts = new CancellationTokenSource();
        var gate = CodexReadyWait.WaitAsync(
            async ct =>
            {
                CodexStartupSnapshot? snapshot;
                if (Volatile.Read(ref completedReads) == 0)
                {
                    entered.TrySetResult();
                    snapshot = await first.Task.WaitAsync(ct);
                }
                else
                {
                    snapshot = Snap(CodexStartupFixtures.P3);
                }

                Interlocked.Increment(ref completedReads);
                return snapshot;
            },
            Options(time, settleMs: 50, maxMs: 5_000), ct: cts.Token);

        try
        {
            await entered.Task.WaitAsync(RealTimeout);
            time.Advance(TimeSpan.FromMilliseconds(50));
            gate.IsCompleted.ShouldBeFalse("the first snapshot is still held when the clock advances");
            first.TrySetResult(Snap(CodexStartupFixtures.P3));
            await AdvanceUntilAsync(time, () => gate.IsCompleted, 20, "late first snapshot readiness");
            (await gate.WaitAsync(RealTimeout)).ShouldBeTrue();
            Volatile.Read(ref completedReads).ShouldBeGreaterThanOrEqualTo(2,
                "readiness requires two completed positive snapshots after the held first read");
        }
        finally
        {
            first.TrySetResult(null);
            cts.Cancel();
            await DrainAfterCleanupAsync(gate, cts.Token);
        }
    }

    private static TimeSpan RealTimeout => TimeSpan.FromSeconds(5);
    private static TimeSpan PollStep => TimeSpan.FromMilliseconds(50);

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
        await WaitUntilAsync(() => Volatile.Read(ref reads) >= 1, "shared readiness first read");
        await AdvanceUntilAsync(time, () => gate.IsCompleted, 20, "shared readiness completion");
        return await gate.WaitAsync(RealTimeout);
    }

    private static async Task AdvanceUntilAsync(
        FakeTimeProvider time, Func<bool> predicate, int maxSteps, string phase)
    {
        var watch = Stopwatch.StartNew();
        var virtualStart = time.GetUtcNow();
        for (var step = 0; step < maxSteps; step++)
        {
            if (predicate())
                return;

            time.Advance(PollStep);
            await DelayWithinPhaseAsync(watch, phase, step + 1, time.GetUtcNow() - virtualStart, 25);
            if (predicate())
                return;
        }

        await WaitForPredicateAsync(predicate, watch, phase, maxSteps, time.GetUtcNow() - virtualStart);
    }

    private static Task WaitUntilAsync(Func<bool> predicate, string phase) =>
        WaitForPredicateAsync(predicate, Stopwatch.StartNew(), phase, 0, TimeSpan.Zero);

    private static async Task WaitForPredicateAsync(
        Func<bool> predicate, Stopwatch watch, string phase, int steps, TimeSpan fakeElapsed)
    {
        while (!predicate())
            await DelayWithinPhaseAsync(watch, phase, steps, fakeElapsed, 10);
    }

    private static async Task DelayWithinPhaseAsync(
        Stopwatch watch, string phase, int steps, TimeSpan fakeElapsed, int delayMs)
    {
        var remaining = RealTimeout - watch.Elapsed;
        if (remaining <= TimeSpan.Zero)
            throw new TimeoutException($"{phase}: no progress after {steps} clock steps and {fakeElapsed.TotalMilliseconds} fake ms");

        try
        {
            await Task.Delay(delayMs).WaitAsync(remaining);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"{phase}: no progress after {steps} clock steps and {fakeElapsed.TotalMilliseconds} fake ms");
        }
    }

    private static async Task DrainAfterCleanupAsync(Task<bool> gate, CancellationToken caller)
    {
        try
        {
            await gate.WaitAsync(RealTimeout);
        }
        catch (OperationCanceledException) when (caller.IsCancellationRequested)
        {
            // The dedicated caller token was canceled only to release a held test callback.
        }
    }
}
