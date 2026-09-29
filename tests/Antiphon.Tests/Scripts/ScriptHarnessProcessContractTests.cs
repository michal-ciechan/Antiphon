using System.Text;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Unit")]
public sealed class ScriptHarnessProcessContractTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private const string Valid = "PASS C806 C806 fixture passed\nC487 HARNESS EXIT CODE: 0\nC487: 1 passed, 0 failed, 1 rows\n";

    [Test]
    public void Default_options_keep_existing_budgets()
    {
        ScriptHarnessOptions.Default.ExecutionBudget.ShouldBe(TimeSpan.FromSeconds(300));
        ScriptHarnessOptions.Default.CleanupBudget.ShouldBe(TimeSpan.FromSeconds(10));
        var changed = ScriptHarnessOptions.Default with { ExecutionBudget = TimeSpan.FromSeconds(5) };
        changed.ExecutionBudget.ShouldBe(TimeSpan.FromSeconds(5));
        ScriptHarnessOptions.Default.ExecutionBudget.ShouldBe(TimeSpan.FromSeconds(300));
    }

    [Test]
    public async Task Invalid_execution_budgets_never_spawn()
    {
        foreach (var value in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), System.Threading.Timeout.InfiniteTimeSpan })
        {
            var calls = 0;
            var option = Options(new FakeOwner(), new FakeTimeProvider()) with
            { ExecutionBudget = value, OwnerFactory = _ => { calls++; return new FakeOwner(); } };
            (await Capture(Run(option))).ShouldBeOfType<ArgumentOutOfRangeException>();
            calls.ShouldBe(0);
        }
    }

    [Test]
    public async Task Invalid_cleanup_budgets_never_spawn()
    {
        foreach (var value in new[] { TimeSpan.Zero, TimeSpan.FromTicks(-1), System.Threading.Timeout.InfiniteTimeSpan })
        {
            var calls = 0;
            var option = Options(new FakeOwner(), new FakeTimeProvider()) with
            { CleanupBudget = value, OwnerFactory = _ => { calls++; return new FakeOwner(); } };
            (await Capture(Run(option))).ShouldBeOfType<ArgumentOutOfRangeException>();
            calls.ShouldBe(0);
        }
    }

    [Test]
    public async Task Launch_consumes_execution_deadline()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: false);
        var run = Run(Options(owner, clock));
        clock.Advance(Budget);
        var error = await Capture(run);
        error.ShouldBeOfType<TimeoutException>().Message.ShouldContain("fixture case Test");
        owner.Terminations.ShouldBe(1);
    }

    [Test]
    public Task Root_exit_uses_execution_deadline() => DeadlineAsync(rootComplete: false);

    [Test]
    public Task Stdout_uses_execution_deadline() => DeadlineAsync(rootComplete: true, holdStdout: true);

    [Test]
    public Task Stderr_uses_execution_deadline() => DeadlineAsync(rootComplete: true, holdStderr: true);

    [Test]
    public async Task Root_exit_does_not_reset_deadline()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: false, holdStdout: true);
        var run = Run(Options(owner, clock));
        clock.Advance(TimeSpan.FromMilliseconds(800));
        owner.CompleteRoot();
        clock.Advance(TimeSpan.FromMilliseconds(200));
        (await Capture(run)).ShouldBeOfType<TimeoutException>();
        owner.Terminations.ShouldBe(1);
    }

    [Test]
    public async Task Reader_fault_starts_cleanup_promptly()
    {
        foreach (var stdout in new[] { true, false })
        {
            var clock = new FakeTimeProvider();
            var fault = new IOException(stdout ? "stdout fault" : "stderr fault");
            var owner = new FakeOwner(rootComplete: false,
                stdoutText: stdout ? null : "", stderrText: stdout ? "" : null);
            var run = Run(Options(owner, clock));
            owner.FaultStream(stdout, fault);
            var error = await Capture(run);
            ReferenceEquals(error, fault).ShouldBeTrue();
            owner.Terminations.ShouldBe(1);
            clock.GetUtcNow().ShouldBe(new FakeTimeProvider().GetUtcNow());
        }
    }

    [Test]
    public async Task Timeout_preserves_partial_output_and_cleanup_errors()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: false, stdoutText: "stdout-partial", stderrText: "stderr-partial")
        { TerminateError = new IOException("stop denied") };
        var run = Run(Options(owner, clock));
        clock.Advance(Budget);
        var error = await Capture(run);
        error.ShouldBeOfType<TimeoutException>().Message.ShouldContain("stdout-partial");
        error.Message.ShouldContain("stderr-partial");
        error.Message.ShouldContain("stop denied");
    }

    [Test]
    public async Task Caller_cancellation_preserves_token()
    {
        var calls = 0;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var options = Options(new FakeOwner(), new FakeTimeProvider()) with
        { OwnerFactory = _ => { calls++; return new FakeOwner(); } };
        var pre = await Capture(Run(options, canceled.Token));
        ((OperationCanceledException)pre!).CancellationToken.ShouldBe(canceled.Token);
        calls.ShouldBe(0);

        using var later = new CancellationTokenSource();
        var owner = new FakeOwner(rootComplete: false);
        var run = Run(Options(owner, new FakeTimeProvider()), later.Token);
        later.Cancel();
        var error = await Capture(run);
        ((OperationCanceledException)error!).CancellationToken.ShouldBe(later.Token);
        owner.Terminations.ShouldBe(1);
    }

    [Test]
    public async Task Cleanup_uses_fresh_token()
    {
        using var caller = new CancellationTokenSource();
        var owner = new FakeOwner(rootComplete: false);
        var run = Run(Options(owner, new FakeTimeProvider()), caller.Token);
        caller.Cancel();
        (await Capture(run)).ShouldBeAssignableTo<OperationCanceledException>();
        owner.TerminationToken.CanBeCanceled.ShouldBeTrue();
        owner.TerminationToken.IsCancellationRequested.ShouldBeFalse();
    }

    [Test]
    public async Task Cleanup_has_one_total_deadline()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: true) { HoldTermination = true };
        var run = Run(Options(owner, clock));
        await owner.TerminationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(Budget);
        var error = await Capture(run);
        error.ShouldBeOfType<IOException>().Message.ShouldContain("terminate");
        error.Message.ShouldContain("death confirmation");
        owner.ReleaseTermination();
    }

    [Test]
    public async Task Stuck_reader_is_closed_at_cleanup_deadline()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: true, holdStdout: true) { ReleaseOnTerminate = false };
        var run = Run(Options(owner, clock));
        clock.Advance(Budget);
        await owner.TerminationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(Budget);
        var error = await Capture(run);
        error.ShouldBeOfType<TimeoutException>().Message.ShouldContain("pipe drain");
        owner.StreamsClosed.ShouldBeTrue();
    }

    [Test]
    public async Task Stuck_control_write_is_bounded()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: true) { HoldTermination = true };
        var run = Run(Options(owner, clock));
        await owner.TerminationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(Budget);
        (await Capture(run)).ShouldBeOfType<IOException>().Message.ShouldContain("terminate");
        owner.ReleaseTermination();
    }

    [Test]
    public async Task Stuck_dispose_cannot_extend_cleanup_deadline()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: true) { HoldDispose = true };
        var run = Run(Options(owner, clock));
        await owner.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        clock.Advance(Budget);
        (await Capture(run)).ShouldBeOfType<IOException>().Message.ShouldContain("dispose");
        owner.ReleaseDispose();
    }

    [Test]
    public async Task Cleanup_native_error_fails_success()
    {
        var owner = new FakeOwner(stdoutText: Valid) { TerminateError = new IOException("native access denied") };
        var error = await Capture(Run(Options(owner, new FakeTimeProvider())));
        error.ShouldBeOfType<IOException>().Message.ShouldContain("native access denied");
        owner.Terminations.ShouldBe(1);
    }

    [Test]
    public async Task Unconfirmed_death_retains_paths()
    {
        ScriptProcessRequest? request = null;
        var owner = new FakeOwner(stdoutText: Valid) { DeathError = new IOException("accounting unknown") };
        var options = Options(owner, new FakeTimeProvider()) with
        {
            OwnerFactory = value =>
            {
                request = value;
                Directory.CreateDirectory(value.ResultsDirectory);
                Directory.CreateDirectory(value.ControlDirectory);
                return owner;
            }
        };
        try
        {
            var error = await Capture(Run(options));
            error.ShouldBeOfType<IOException>().Message.ShouldContain("accounting unknown");
            Directory.Exists(request!.ResultsDirectory).ShouldBeTrue();
            Directory.Exists(request.ControlDirectory).ShouldBeTrue();
        }
        finally
        {
            if (request is not null)
            {
                Directory.Delete(request.ResultsDirectory, true);
                Directory.Delete(request.ControlDirectory, true);
            }
        }
    }

    [Test]
    public async Task Open_streams_delay_path_deletion()
    {
        ScriptProcessRequest? request = null;
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(holdStdout: true) { ReleaseOnTerminate = false };
        var option = Options(owner, clock) with
        { OwnerFactory = value => { request = value; Directory.CreateDirectory(value.ResultsDirectory); return owner; } };
        try
        {
            var run = Run(option);
            clock.Advance(Budget);
            await owner.TerminationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            clock.Advance(Budget);
            (await Capture(run)).ShouldBeOfType<TimeoutException>();
            Directory.Exists(request!.ResultsDirectory).ShouldBeTrue();
            owner.StreamsClosed.ShouldBeTrue();
        }
        finally { if (request is not null) Directory.Delete(request.ResultsDirectory, true); }
    }

    [Test]
    public async Task Cleanup_deletes_only_invocation_paths()
    {
        var sibling = Path.Combine(Path.GetTempPath(), "c806-sibling-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sibling);
        var sentinel = Path.Combine(sibling, "sentinel");
        File.WriteAllText(sentinel, "untouched");
        ScriptProcessRequest? request = null;
        var owner = new FakeOwner(stdoutText: Valid);
        var option = Options(owner, new FakeTimeProvider()) with
        { OwnerFactory = value => { request = value; Directory.CreateDirectory(value.ResultsDirectory); return owner; } };
        try
        {
            (await Run(option)).ExitCode.ShouldBe(0);
            Directory.Exists(request!.ResultsDirectory).ShouldBeFalse();
            File.ReadAllText(sentinel).ShouldBe("untouched");
        }
        finally { Directory.Delete(sibling, true); }
    }

    [Test]
    public async Task Primary_start_read_and_assertion_errors_are_preserved()
    {
        var startError = new IOException("start sentinel");
        var owner = new FakeOwner { StartError = startError, TerminateError = new IOException("secondary stop") };
        var error = await Capture(Run(Options(owner, new FakeTimeProvider())));
        ReferenceEquals(error, startError).ShouldBeTrue();
        error!.Data["ScriptHarnessDiagnostics"]!.ToString().ShouldContain("secondary stop");

        var readError = new IOException("read sentinel");
        var reader = new FakeOwner(rootComplete: false, stdoutText: null);
        var readRun = Run(Options(reader, new FakeTimeProvider()));
        reader.FaultStream(true, readError);
        ReferenceEquals(await Capture(readRun), readError).ShouldBeTrue();

        var assertionOwner = new FakeOwner(stdoutText: "C487 HARNESS EXIT CODE: 0\n");
        assertionOwner.TerminateError = new IOException("secondary assertion stop");
        var assertion = await Capture(ScriptHarness.RunHarnessCaseAsync("fixture", "C806", "Test", 1,
            ["C806 C806 fixture passed"], Options(assertionOwner, new FakeTimeProvider()), CancellationToken.None));
        assertion.ShouldNotBeNull();
        assertion.Data["ScriptHarnessDiagnostics"]!.ToString().ShouldContain("secondary assertion stop");
    }

    [Test]
    public async Task Repeated_cleanup_is_idempotent()
    {
        var owner = new FakeOwner(stdoutText: Valid);
        (await Run(Options(owner, new FakeTimeProvider()))).ExitCode.ShouldBe(0);
        owner.Terminations.ShouldBe(1);
        owner.DisposeCount.ShouldBe(1);
        owner.StreamsClosed.ShouldBeTrue();
    }

    [Test]
    public async Task Deterministic_completion_deadline_boundary()
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: false, stdoutText: Valid);
        var run = Run(Options(owner, clock));
        clock.Advance(TimeSpan.FromMilliseconds(999));
        owner.CompleteRoot();
        (await run.WaitAsync(TimeSpan.FromSeconds(2))).ExitCode.ShouldBe(0);

        var secondClock = new FakeTimeProvider();
        var pending = new FakeOwner(rootComplete: false);
        var second = Run(Options(pending, secondClock));
        secondClock.Advance(Budget);
        (await Capture(second)).ShouldBeOfType<TimeoutException>();
    }

    [Test]
    public async Task Inventory_failures_remain_failures()
    {
        var invalid = new[]
        {
            (Valid, 37),
            (Valid + "FAIL C806 injected\n", 0),
            ("PASS C806 C806 fixture passed\nC487: 1 passed, 0 failed, 1 rows\n", 0),
            ("PASS C806 C806 fixture passed\nPASS C806 C806 extra\nC487 HARNESS EXIT CODE: 0\nC487: 1 passed, 0 failed, 1 rows\n", 0),
            ("PASS C806 C806 wrong\nC487 HARNESS EXIT CODE: 0\nC487: 1 passed, 0 failed, 1 rows\n", 0),
            ("PASS C806 C806 fixture passed\nC487 HARNESS EXIT CODE: 0\nC487: 9 passed, 0 failed, 9 rows\n", 0)
        };
        foreach (var (output, exit) in invalid)
        {
            var owner = new FakeOwner(stdoutText: output, exitCode: exit);
            var error = await Capture(ScriptHarness.RunHarnessCaseAsync("fixture", "C806", "Test", 1,
                ["C806 C806 fixture passed"], Options(owner, new FakeTimeProvider()), CancellationToken.None));
            error.ShouldNotBeNull($"Invalid inventory must fail: {output}");
            owner.Terminations.ShouldBe(1);
        }
    }

    [Test]
    public void New_spawning_classes_take_process_limiter()
    {
        foreach (var type in new[] { typeof(ScriptHarnessProcessTests), typeof(ScriptHarnessWindowsOwnershipTests),
                     typeof(ScriptHarnessLinuxOwnershipTests) })
        {
            type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name == "CategoryAttribute" &&
                attribute.ConstructorArguments.Any(argument => argument.Value?.ToString() == "Integration")).ShouldBeTrue(type.Name);
            type.GetCustomAttributesData().Any(attribute => attribute.AttributeType.Name.StartsWith("ParallelLimiterAttribute", StringComparison.Ordinal)).ShouldBeTrue(type.Name);
        }
        typeof(ScriptHarnessProcessContractTests).GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.Name == "CategoryAttribute" &&
            attribute.ConstructorArguments.Any(argument => argument.Value?.ToString() == "Unit")).ShouldBeTrue();
        File.ReadAllText(ScriptHarnessProcessFixture.ScriptPath).All(character => character <= 127).ShouldBeTrue();
    }

    [Test]
    public async Task All_terminal_paths_explicitly_terminate_owner()
    {
        var success = new FakeOwner(stdoutText: Valid);
        await Run(Options(success, new FakeTimeProvider()));
        success.Terminations.ShouldBe(1);

        var timeoutClock = new FakeTimeProvider();
        var timeout = new FakeOwner(rootComplete: false);
        var timed = Run(Options(timeout, timeoutClock));
        timeoutClock.Advance(Budget);
        (await Capture(timed)).ShouldBeOfType<TimeoutException>();
        timeout.Terminations.ShouldBe(1);

        using var caller = new CancellationTokenSource();
        var canceled = new FakeOwner(rootComplete: false);
        var cancelRun = Run(Options(canceled, new FakeTimeProvider()), caller.Token);
        caller.Cancel();
        (await Capture(cancelRun)).ShouldBeAssignableTo<OperationCanceledException>();
        canceled.Terminations.ShouldBe(1);

        var reader = new FakeOwner(rootComplete: false, stderrText: null);
        var readerRun = Run(Options(reader, new FakeTimeProvider()));
        reader.FaultStream(false, new IOException("read failed"));
        (await Capture(readerRun)).ShouldBeOfType<IOException>();
        reader.Terminations.ShouldBe(1);

        var launch = new FakeOwner { StartError = new IOException("launch failed") };
        (await Capture(Run(Options(launch, new FakeTimeProvider())))).ShouldBeOfType<IOException>();
        launch.Terminations.ShouldBe(1);
    }

    private static async Task DeadlineAsync(bool rootComplete, bool holdStdout = false, bool holdStderr = false)
    {
        var clock = new FakeTimeProvider();
        var owner = new FakeOwner(rootComplete: rootComplete, holdStdout: holdStdout, holdStderr: holdStderr);
        var run = Run(Options(owner, clock));
        clock.Advance(Budget);
        (await Capture(run)).ShouldBeOfType<TimeoutException>();
        owner.Terminations.ShouldBe(1);
    }

    private static ScriptHarnessOptions Options(FakeOwner owner, FakeTimeProvider clock) =>
        new(Budget, Budget, ExecutablePath: "fake", OwnerFactory: _ => owner, Clock: clock);

    private static Task<ScriptHarnessResult> Run(ScriptHarnessOptions options, CancellationToken token = default) =>
        ScriptHarnessProcess.RunAsync("fixture", "C806", "Test", "fake.ps1", options, token);

    private static async Task<Exception?> Capture(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class FakeOwner : IOwnedScriptProcess
    {
        private readonly TaskCompletionSource<int> _root = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly BlockingStream? _stdoutBlock;
        private readonly BlockingStream? _stderrBlock;
        private readonly TaskCompletionSource _termination = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource TerminationEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource DisposeEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Terminations;
        internal int DisposeCount;
        internal bool StreamsClosed;
        internal bool ReleaseOnTerminate = true;
        internal bool HoldTermination;
        internal bool HoldDispose;
        internal Exception? TerminateError;
        internal Exception? DeathError;
        internal Exception? StartError;
        internal CancellationToken TerminationToken;
        public StreamReader Stdout { get; }
        public StreamReader Stderr { get; }

        internal FakeOwner(bool rootComplete = true, bool holdStdout = false, bool holdStderr = false,
            string? stdoutText = "", string? stderrText = "", int exitCode = 0)
        {
            if (rootComplete) _root.SetResult(exitCode);
            _stdoutBlock = holdStdout || stdoutText is null ? new BlockingStream() : null;
            _stderrBlock = holdStderr || stderrText is null ? new BlockingStream() : null;
            Stdout = new StreamReader((Stream?)_stdoutBlock ?? new MemoryStream(Encoding.UTF8.GetBytes(stdoutText ?? "")));
            Stderr = new StreamReader((Stream?)_stderrBlock ?? new MemoryStream(Encoding.UTF8.GetBytes(stderrText ?? "")));
        }

        public Task<int> StartAndWaitForRootAsync(CancellationToken token) =>
            StartError is null ? _root.Task.WaitAsync(token) : Task.FromException<int>(StartError);
        public async Task TerminateAsync(CancellationToken token)
        {
            Interlocked.Increment(ref Terminations);
            TerminationToken = token;
            TerminationEntered.TrySetResult();
            _root.TrySetResult(0);
            if (ReleaseOnTerminate) { _stdoutBlock?.Release(); _stderrBlock?.Release(); }
            if (HoldTermination) await _termination.Task;
            if (TerminateError is not null) throw TerminateError;
        }
        public Task ConfirmDeadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return DeathError is null ? Task.CompletedTask : Task.FromException(DeathError);
        }
        public void CloseStreams()
        {
            StreamsClosed = true;
            _stdoutBlock?.Release(); _stderrBlock?.Release();
            Stdout.Dispose(); Stderr.Dispose();
        }
        public void Dispose()
        {
            Interlocked.Increment(ref DisposeCount);
            DisposeEntered.TrySetResult();
            if (HoldDispose) _disposal.Task.GetAwaiter().GetResult();
        }
        internal void CompleteRoot() => _root.TrySetResult(0);
        internal void FaultStream(bool stdout, Exception error) =>
            (stdout ? _stdoutBlock : _stderrBlock)!.Fault(error);
        internal void ReleaseTermination() => _termination.TrySetResult();
        internal void ReleaseDispose() => _disposal.TrySetResult();
    }

    private sealed class BlockingStream : Stream
    {
        private readonly TaskCompletionSource<int> _read = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => _read.Task;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token) => new(_read.Task);
        internal void Release() => _read.TrySetResult(0);
        internal void Fault(Exception error) => _read.TrySetException(error);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _read.Task.GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long length) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
