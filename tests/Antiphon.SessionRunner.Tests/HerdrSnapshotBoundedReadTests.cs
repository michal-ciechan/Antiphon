using System.Text.Json;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

/// <summary>
/// CARD-1365: the herdr snapshot read must not pin the serving thread and must end at the
/// <c>pane.read</c> deadline as the herdr-unreachable class. One single-thread continuation pump
/// models the thread pool: a <c>Snapshot</c> on a stalled child must leave the pump free for a
/// <c>List</c> posted behind it, and advancing the injected clock past the deadline must end the
/// snapshot as 503 <c>herdr_unreachable</c> with the child's token cancelled and the caller token
/// untouched. In-process: no process, socket or pipe, so no spawn limiter.
/// </summary>
[Category("Unit")]
public class HerdrSnapshotBoundedReadTests
{
    [Test]
    public async Task C1365_Stalled_pane_read_frees_the_pump_for_a_list_and_answers_herdr_unreachable_at_the_deadline()
    {
        var root = Path.Combine(Path.GetTempPath(), "c1365-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var settings = new SessionRunnerSettings { SessionLogPath = root };
        var clock = new FakeTimeProvider();
        await using var runtime = new SessionRunnerRuntime(
            Options.Create(settings), NullLogger<SessionRunnerRuntime>.Instance, timeProvider: clock);

        var sessionId = Guid.NewGuid();
        var child = new StallingChild();
        var session = new SessionRunnerRuntime.RunnerSession(
            sessionId, settings, new SessionRunnerEventHub(), NullLogger.Instance);
        session.BindChildForTest(child);
        runtime.Track(session);

        var dispatcher = new PhoneHomeCommandDispatcher(
            new PhoneHomeRuntimeAdapter(runtime, RunnerBuildIdentity.Resolve()),
            new PhoneHomeSettings { Enabled = true, AllowedCwd = root, CapacityStatePath = Path.Combine(root, "capacity") });

        var snapshotFrame = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.Snapshot,
            JsonSerializer.SerializeToElement(new { sessionId }, PhoneHomeFraming.Json));
        var listFrame = new PhoneHomeFrame(PhoneHomeFrameKind.Request, 1, Guid.NewGuid(), PhoneHomeOperation.List);

        using var cts = new CancellationTokenSource();
        await using var pump = new SingleThreadContinuationPump();
        try
        {
            var slow = pump.InvokeAsync(() => dispatcher.DispatchAsync(snapshotFrame, cts.Token));
            var probe = pump.InvokeAsync(() => dispatcher.DispatchAsync(listFrame, cts.Token));

            // Red at base: the pump thread is inside GetResult(), so the probe never runs and this
            // 2 s failure bound throws TimeoutException. Green: the snapshot yields at its first
            // incomplete await and the probe answers at once.
            var list = await probe.WaitAsync(TimeSpan.FromSeconds(2));
            list.Kind.ShouldBe(PhoneHomeFrameKind.Result);
            child.Reads.ShouldBe(1);
            slow.IsCompleted.ShouldBeFalse();
            child.ReadToken.CanBeCanceled.ShouldBeTrue();

            clock.Advance(TimeSpan.FromSeconds(10));
            var reply = await slow.WaitAsync(TimeSpan.FromSeconds(2));
            reply.Kind.ShouldBe(PhoneHomeFrameKind.Error);
            reply.ErrorCode.ShouldBe(HerdrProblemTypes.Unreachable);
            reply.StatusCode.ShouldBe(503);
            child.ReadToken.IsCancellationRequested.ShouldBeTrue();
            cts.IsCancellationRequested.ShouldBeFalse();
        }
        finally
        {
            child.Release();
        }
    }

    /// <summary>A herdr-shaped child whose one screen read stalls until the test releases it.</summary>
    private sealed class StallingChild : ISessionChild
    {
        private readonly TaskCompletionSource<ChildScreen?> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Reads { get; private set; }
        public CancellationToken ReadToken { get; private set; }
        public Task<ChildStarted> LaunchAsync(RunnerLaunchRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task WriteAsync(string input, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> KillAsync(CancellationToken ct) => Task.FromResult(false);
        public Task<ChildScreen?> ReadScreenAsync(CancellationToken ct)
        {
            Reads++;
            ReadToken = ct;
            return _read.Task.WaitAsync(ct);
        }
        public event Action<ChildExit>? Exited;
        public void Exit() => Exited?.Invoke(new(0, "test"));
        public void Release() => _read.TrySetResult(new ChildScreen("released", 1));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
