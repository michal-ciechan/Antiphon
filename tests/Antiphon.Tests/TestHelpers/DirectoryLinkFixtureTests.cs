using Antiphon.Tests.Scripts;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

[Category("Unit")]
public sealed class DirectoryLinkFixtureTests
{
    [Test]
    public async Task C1061_Command_budgets_cover_exit_and_both_pipes()
    {
        DirectoryLinkCommand.DefaultOptions.ExecutionBudget.ShouldBe(TimeSpan.FromSeconds(30), "Fixture.ExecutionBudget");
        DirectoryLinkCommand.DefaultOptions.CleanupBudget.ShouldBe(TimeSpan.FromSeconds(10), "Fixture.CleanupBudget");
        foreach (var held in new[] { "root", "stdout", "stderr" })
        {
            await WithFakeAsync(async fixture =>
            {
                var run = fixture.Start();
                await fixture.Owner.BothStartedAsync();
                fixture.Owner.Output.Started.Task.IsCompleted.ShouldBeTrue("Fixture.ConcurrentDrains");
                fixture.Owner.Error.Started.Task.IsCompleted.ShouldBeTrue("Fixture.ConcurrentDrains");
                if (held != "stdout") fixture.Owner.Output.Release();
                if (held != "stderr") fixture.Owner.Error.Release();
                fixture.Clock.Advance(TimeSpan.FromSeconds(29));
                if (held != "root") fixture.Owner.Root.TrySetResult(0);
                run.IsCompleted.ShouldBeFalse("Fixture.OneDeadline: no early timeout");
                fixture.Clock.Advance(TimeSpan.FromSeconds(1));
                (await CaptureAsync(run)).ShouldBeOfType<TimeoutException>("Fixture.OneDeadline");
                fixture.AssertJoined();
            });
        }
    }

    [Test]
    public async Task C1061_Command_timeout_and_pipe_fault_remain_visible()
    {
        await WithFakeAsync(async fixture =>
        {
            var run = fixture.Start();
            await fixture.Owner.BothStartedAsync();
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            (await CaptureAsync(run)).ShouldBeOfType<TimeoutException>("Fixture.TimeoutVisible");
            fixture.AssertJoined();
        });
        foreach (var stdout in new[] { true, false })
        {
            await WithFakeAsync(async fixture =>
            {
                var original = new IOException("fixture pipe sentinel");
                var run = fixture.Start();
                await fixture.Owner.BothStartedAsync();
                (stdout ? fixture.Owner.Output : fixture.Owner.Error).Fault(original);
                ReferenceEquals(await CaptureAsync(run), original).ShouldBeTrue("Fixture.PipeFaultVisible");
                fixture.Clock.GetTimestamp().ShouldBe(fixture.InitialTimestamp);
                fixture.AssertJoined();
            });
        }
        // Launch errors also cross the same owner finalization boundary.
        await WithFakeAsync(async fixture =>
        {
            var original = new IOException("fixture launch sentinel");
            fixture.Owner.StartError = original;
            ReferenceEquals(await CaptureAsync(fixture.Start()), original).ShouldBeTrue();
            fixture.AssertJoined();
        });
    }

    [Test]
    public async Task C1061_Command_cancellation_joins_before_return()
    {
        foreach (var preCanceled in new[] { true, false })
        {
            await WithFakeAsync(async fixture =>
            {
                using var caller = new CancellationTokenSource();
                if (preCanceled) caller.Cancel();
                var run = fixture.Start(caller.Token);
                if (!preCanceled)
                {
                    await fixture.Owner.BothStartedAsync();
                    caller.Cancel();
                }
                // A fake-clock probe bounds a lost cancellation without leaving its work pending.
                var probe = Task.Delay(TimeSpan.FromSeconds(1), fixture.Clock);
                var completed = Task.WhenAny(run, probe);
                for (var i = 0; i < 100 && !run.IsCompleted; i++) await Task.Yield();
                fixture.Clock.Advance(TimeSpan.FromSeconds(1));
                await completed;
                fixture.Owner.ReleaseAll();
                var error = await CaptureAsync(run);
                error.ShouldBeAssignableTo<OperationCanceledException>("Fixture.CallerCancellation");
                ((OperationCanceledException)error!).CancellationToken.ShouldBe(caller.Token, "Fixture.CallerCancellation");
                if (preCanceled) fixture.OwnerCalls.ShouldBe(0, "Fixture.CallerCancellation: pre-canceled starts no owner");
                else
                {
                    fixture.Owner.CleanupToken.CanBeCanceled.ShouldBeTrue();
                    fixture.Owner.CleanupToken.IsCancellationRequested.ShouldBeFalse("Fixture.CallerCancellation: fresh cleanup token");
                    fixture.AssertJoined();
                }
            });
        }
    }

    [Test]
    public async Task C1061_Command_cleanup_failure_is_not_null()
    {
        await WithFakeAsync(async fixture =>
        {
            fixture.Owner.DeathError = new IOException("death sentinel");
            fixture.Owner.ReleaseAll();
            var error = await CaptureAsync(fixture.Start());
            error.ShouldBeOfType<IOException>("Fixture.CleanupVisible");
            error!.Message.ShouldContain("death sentinel");
            fixture.AssertRetained(error.Message);
            fixture.AssertJoined();
        });
        await WithFakeAsync(async fixture =>
        {
            fixture.Owner.HoldCleanup = true;
            var run = fixture.Start();
            await fixture.Owner.BothStartedAsync();
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            await fixture.Owner.TerminationEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Clock.Advance(TimeSpan.FromSeconds(9));
            run.IsCompleted.ShouldBeFalse("Fixture.CleanupVisible: separate cleanup allowance");
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            var error = await CaptureAsync(run);
            error.ShouldBeOfType<TimeoutException>("Fixture.CleanupVisible: primary timeout preserved");
            fixture.AssertRetained(error!.Message);
            error.Message.ShouldContain("terminate:");
            error.Message.ShouldContain("death confirmation:");
            error.Message.ShouldContain("pipe drain:");
        });
        await WithFakeAsync(async fixture =>
        {
            fixture.Owner.DeathError = new IOException("secondary death sentinel");
            var run = fixture.Start();
            await fixture.Owner.BothStartedAsync();
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            var error = await CaptureAsync(run);
            error.ShouldBeOfType<TimeoutException>("Fixture.CleanupVisible");
            error!.Message.ShouldContain("secondary death sentinel");
            fixture.AssertRetained(error.Message);
            fixture.AssertJoined();
        });
    }

    [Test]
    public void C1061_Command_result_requires_zero_exit_and_link()
    {
        var root = Directory.CreateTempSubdirectory("c1061-result-").FullName;
        try
        {
            var observations = 0;
            DirectoryLink.Complete(Path.Combine(root, "absent"), new(37, "", ""), _ => { observations++; return true; })
                .ShouldBeNull("Fixture.NonzeroRefused");
            observations.ShouldBe(0);
            DirectoryLink.Complete(root, new(0, "", "")).ShouldBeNull("Fixture.ReparseRequired");
            DirectoryLink.Complete(Path.Combine(root, "absent"), new(0, "", "")).ShouldBeNull("Fixture.ReparseRequired");
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<Exception?> CaptureAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); return null; }
        catch (Exception error) when (task.IsCompleted) { return error; }
    }

    private static async Task WithFakeAsync(Func<FakeInvocation, Task> body)
    {
        await using var fixture = new FakeInvocation();
        await body(fixture);
    }

    private sealed class FakeInvocation : IAsyncDisposable
    {
        internal FakeTimeProvider Clock { get; } = new();
        internal FakeOwner Owner { get; } = new();
        internal long InitialTimestamp { get; }
        internal int OwnerCalls;
        private ScriptProcessRequest? _request;
        private Task? _run;
        internal FakeInvocation() => InitialTimestamp = Clock.GetTimestamp();
        internal Task<DirectoryLink?> Start(CancellationToken ct = default)
        {
            var options = DirectoryLinkCommand.DefaultOptions with
            {
                ExecutablePath = "fake-pwsh", Clock = Clock,
                OwnerFactory = request =>
                {
                    OwnerCalls++;
                    _request = request;
                    Directory.CreateDirectory(request.ResultsDirectory);
                    Directory.CreateDirectory(request.ControlDirectory);
                    return Owner;
                },
            };
            var run = DirectoryLink.TryCreateWindowsAsync("absent-fixture-link", "absent-fixture-target", options, ct: ct);
            _run = run;
            return run;
        }
        internal void AssertJoined()
        {
            Owner.Terminations.ShouldBe(1);
            Owner.Confirmations.ShouldBe(1);
            Owner.Output.Completed.Task.IsCompleted.ShouldBeTrue();
            Owner.Error.Completed.Task.IsCompleted.ShouldBeTrue();
            Owner.Closed.ShouldBeTrue();
            Owner.Disposed.Task.IsCompleted.ShouldBeTrue();
        }
        internal void AssertRetained(string diagnostic)
        {
            _request.ShouldNotBeNull();
            diagnostic.ShouldContain(_request.ResultsDirectory);
            diagnostic.ShouldContain(_request.ControlDirectory);
            Directory.Exists(_request.ResultsDirectory).ShouldBeTrue();
            Directory.Exists(_request.ControlDirectory).ShouldBeTrue();
        }
        public async ValueTask DisposeAsync()
        {
            Owner.ReleaseAll();
            Clock.Advance(TimeSpan.FromSeconds(40));
            if (_run is not null) await CaptureAsync(_run);
            await Owner.JoinAsync();
            if (_request is not null)
                foreach (var path in new[] { _request.ResultsDirectory, _request.ControlDirectory })
                    if (Directory.Exists(path)) Directory.Delete(path, true);
        }
    }

    private sealed class FakeOwner : IOwnedScriptProcess
    {
        internal TaskCompletionSource<int> Root { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal HeldStream Output { get; } = new();
        internal HeldStream Error { get; } = new();
        internal TaskCompletionSource TerminationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _cleanup = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _termination;
        private Task? _confirmation;
        internal int Terminations, Confirmations;
        internal bool HoldCleanup, Closed;
        internal Exception? DeathError, StartError;
        internal CancellationToken CleanupToken;
        public StreamReader Stdout { get; }
        public StreamReader Stderr { get; }
        internal FakeOwner() { Stdout = new(Output); Stderr = new(Error); }
        public Task<int> StartAndWaitForRootAsync(CancellationToken cancellationToken) =>
            StartError is null ? Root.Task.WaitAsync(cancellationToken) : Task.FromException<int>(StartError);
        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            Terminations++;
            CleanupToken = cancellationToken;
            TerminationEntered.TrySetResult();
            if (!HoldCleanup) ReleaseAll();
            return _termination = HoldCleanup ? _cleanup.Task : Task.CompletedTask;
        }
        public Task ConfirmDeadAsync(CancellationToken cancellationToken)
        {
            Confirmations++;
            return _confirmation = DeathError is not null ? Task.FromException(DeathError)
                : HoldCleanup ? _cleanup.Task : Task.CompletedTask;
        }
        public void CloseStreams()
        {
            Closed = true;
            Output.Release(); Error.Release();
            Stdout.Dispose(); Stderr.Dispose();
        }
        public void Dispose() => Disposed.TrySetResult();
        internal Task BothStartedAsync() => Task.WhenAll(Output.Started.Task, Error.Started.Task).WaitAsync(TimeSpan.FromSeconds(3));
        internal void ReleaseAll()
        {
            Root.TrySetResult(0); Output.Release(); Error.Release(); _cleanup.TrySetResult();
        }
        internal async Task JoinAsync()
        {
            foreach (var task in new[] { _termination, _confirmation })
                if (task is not null) await CaptureAsync(task);
            if (Output.Started.Task.IsCompleted) await Output.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (Error.Started.Task.IsCompleted) await Error.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            if (Terminations != 0) await Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private sealed class HeldStream : Stream
    {
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try { return await _read.Task; }
            finally { Completed.TrySetResult(); }
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        internal void Release() => _read.TrySetResult(0);
        internal void Fault(Exception error) => _read.TrySetException(error);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
