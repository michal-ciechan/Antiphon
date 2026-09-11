using System.Diagnostics;
using Antiphon.Agents.Pty;
using Porta.Pty;
using Shouldly;
using TUnit.Core;
using SkipTestException = TUnit.Core.Exceptions.SkipTestException;
using Microsoft.Win32.SafeHandles;
using Win32Exception = System.ComponentModel.Win32Exception;

namespace Antiphon.Agents.Pty.Tests;

[Category("Pty")]
[ParallelLimiter<ProcessSpawnLimit>]
public class PtyCustodyTests
{
    [Test]
    public async Task Root_exit_before_subscription_is_replayed_and_closes_input()
    {
        RequireModern();
        Process? child = null;
        var native = new NativeProbe
        {
            OnResumed = () => child!.WaitForExit(15000).ShouldBeTrue(),
        };
        await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
        try
        {
            await runner.StartTrackedAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "exit 7"], AppContext.BaseDirectory, new Dictionary<string, string>(),
                80, 24, 0, new Journal(pid => child = Process.GetProcessById(pid)));
            (await runner.Exited.WaitAsync(TimeSpan.FromSeconds(5))).ShouldBe(7);
            await Should.ThrowAsync<InvalidOperationException>(() => runner.WriteAsync("late input"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            (await runner.SealAndObserveCustodyAsync(timeout.Token)).ShouldBe(new PtyCustodyObservation(0, true));
        }
        finally
        {
            await runner.KillAsync(TimeSpan.FromSeconds(5));
            child?.Dispose();
        }
    }

    [Test]
    public async Task Failed_tracked_attempt_cannot_spawn_again_on_the_same_runner()
    {
        RequireModern();
        var native = new NativeProbe();
        await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
        var journal = new Journal(_ => throw new IOException("tracking write failed"));
        Task Start(IPtyCustodyJournal value) => runner.StartTrackedAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "exit 0"],
            AppContext.BaseDirectory, new Dictionary<string, string>(), 80, 24, 0, value);
        await Should.ThrowAsync<IOException>(() => Start(journal));
        var second = new Journal(_ => { });
        await Should.ThrowAsync<InvalidOperationException>(() => Start(second));
        await Should.ThrowAsync<InvalidOperationException>(() => runner.StartAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/d", "/c", "exit 0"]));
        second.Started.ShouldBeFalse();
        native.ResumeCalls.ShouldBe(0);
    }

    [Test]
    public void Cancellation_after_tracking_does_not_resume_the_suspended_child()
    {
        var dll = RequireModern();
        using var canceled = new CancellationTokenSource();
        var native = new NativeProbe();
        Process? child = null;
        try
        {
            Should.Throw<OperationCanceledException>(() => ModernConPtyConnection.Spawn(dll,
                Options("exit 0"), new Journal(pid =>
                {
                    child = Process.GetProcessById(pid);
                    canceled.Cancel();
                }), native, canceled.Token));
            native.ResumeCalls.ShouldBe(0);
            child.ShouldNotBeNull();
            child.WaitForExit(15000).ShouldBeTrue();
        }
        finally { child?.Dispose(); }
    }

    [Test]
    public async Task Output_drain_cancellation_never_returns_an_exit_observation()
    {
        RequireModern();
        using var outputHeld = new ManualResetEventSlim();
        using var releaseOutput = new ManualResetEventSlim();
        await using var runner = new PtyAgentRunner("modern");
        runner.OnData += text =>
        {
            if (!text.Contains("held-custody-output", StringComparison.Ordinal)) return;
            outputHeld.Set();
            if (!releaseOutput.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("test output barrier");
        };
        try
        {
            await runner.StartTrackedAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "echo held-custody-output"], AppContext.BaseDirectory,
                new Dictionary<string, string>(), 80, 24, 0, new Journal(_ => { }));
            outputHeld.Wait(TimeSpan.FromSeconds(15)).ShouldBeTrue();
            await runner.Exited.WaitAsync(TimeSpan.FromSeconds(15));
            using var canceled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Should.ThrowAsync<OperationCanceledException>(() => runner.SealAndObserveCustodyAsync(canceled.Token));
            releaseOutput.Set();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await runner.SealAndObserveCustodyAsync(timeout.Token);
            result.ShouldBe(new PtyCustodyObservation(0, true));
            runner.SnapshotText().ShouldContain("held-custody-output");
        }
        finally
        {
            releaseOutput.Set();
            await runner.KillAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Test]
    public async Task C478_G202_KillIsNotReceipt()
    {
        RequireModern();
        var native = new NativeProbe { ReportActive = 1 };
        await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
        await runner.StartTrackedAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", "exit 0"], AppContext.BaseDirectory, new Dictionary<string, string>(),
            80, 24, 0, new Journal(_ => { }));
        try
        {
            await runner.KillAsync(TimeSpan.FromSeconds(5));
            runner.CustodyTermination!.Succeeded.ShouldBeTrue();
            var result = await runner.SealAndObserveCustodyAsync();
            result.ActiveProcesses.ShouldBe(1u, "termination success cannot replace OS accounting");
            result.OutputDrained.ShouldBeFalse();
        }
        finally { native.ReportActive = null; await runner.KillAsync(TimeSpan.FromSeconds(5)); }
    }

    [Test]
    public void C478_G191_AtomicMembership()
    {
        var dll = RequireModern();
        var native = new NativeProbe();
        ModernConPtyConnection? connection = null;
        Exception? failure = null;
        try { connection = ModernConPtyConnection.Spawn(dll, Options("exit 0"), new Journal(_ => { }), native); }
        catch (Exception ex) { failure = ex; }
        try
        {
            native.ObservedMembership.ShouldBe(true, "the root must already belong to its job at the first validation boundary");
            failure.ShouldBeNull();
        }
        finally { connection?.Kill(); connection?.Dispose(); }
    }

    [Test]
    public void C478_G192_BreakawayFlag() => Native_job_limits_disallow_both_breakaway_flags(0x00000800u);

    [Test]
    public void C478_G193_SilentBreakaway() => Native_job_limits_disallow_both_breakaway_flags(0x00001000u);

    [Test]
    [Arguments(0x00000800u)]
    [Arguments(0x00001000u)]
    public void Native_job_limits_disallow_both_breakaway_flags(uint forbiddenFlag)
    {
        var dll = RequireModern();
        var native = new NativeProbe();
        ModernConPtyConnection? connection = null;
        Exception? failure = null;
        try { connection = ModernConPtyConnection.Spawn(dll, Options("exit 0"), new Journal(_ => { }), native); }
        catch (Exception ex) { failure = ex; }
        try
        {
            native.ObservedLimits.ShouldNotBeNull();
            (native.ObservedLimits.Value & forbiddenFlag).ShouldBe(0u, "native configuration must forbid escape before resume");
            (native.ObservedLimits.Value & 0x00002000u).ShouldNotBe(0u);
            failure.ShouldBeNull();
        }
        finally { connection?.Kill(); connection?.Dispose(); }
    }

    [Test]
    [Arguments("membership")]
    [Arguments("limits")]
    public void C478_G194_ContainmentValidation(string failure)
    {
        var dll = RequireModern();
        var native = new NativeProbe { FailMembership = failure == "membership", InvalidLimits = failure == "limits" };
        var tracked = false;
        Should.Throw<InvalidOperationException>(() => ModernConPtyConnection.Spawn(dll,
            Options("exit 0"), new Journal(_ => tracked = true), native));
        native.ResumeCalls.ShouldBe(0, "containment failure must precede resume");
        tracked.ShouldBeFalse();
    }

    [Test]
    public async Task C478_G197_QueryFailure()
    {
        RequireModern();
        var native = new NativeProbe();
        await using var runner = new PtyAgentRunner("modern") { CustodyNative = native };
        await runner.StartTrackedAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            ["/d", "/c", "exit 0"], AppContext.BaseDirectory, new Dictionary<string, string>(),
            80, 24, 0, new Journal(_ => { }));
        try
        {
            native.FailQuery = true;
            await Should.ThrowAsync<Win32Exception>(() => runner.SealAndObserveCustodyAsync());
            native.QueryCalls.ShouldBe(1);
        }
        finally { await runner.KillAsync(TimeSpan.FromSeconds(5)); }
    }

    [Test]
    public async Task Explicit_breakaway_is_refused_by_the_actual_job()
    {
        var dll = RequireModern();
        var prefix = "Local\\c478-" + Guid.NewGuid().ToString("N");
        var roles = new[] { "root", "middle", "leaf" };
        var ready = roles.Select(role => new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-" + role)).ToArray();
        var release = roles.Select(role => new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-release-" + role)).ToArray();
        using var denied = new EventWaitHandle(false, EventResetMode.ManualReset, prefix + "-denied");
        ModernConPtyConnection? connection = null;
        Task? pump = null;
        try
        {
            var options = Options("");
            options.App = Path.Combine(AppContext.BaseDirectory, "custody-child", "Antiphon.CustodyTestChild.exe");
            options.CommandLine = ["root", prefix, "16777216"];
            connection = ModernConPtyConnection.Spawn(dll, options, new Journal(_ => { }));
            pump = DrainAsync(connection.ReaderStream);
            denied.WaitOne(TimeSpan.FromSeconds(15)).ShouldBeTrue("CREATE_BREAKAWAY_FROM_JOB must fail with access denied");
            ready[2].WaitOne(0).ShouldBeFalse("no escaped leaf may execute");
            await WaitCountAsync(connection, 1);
        }
        finally
        {
            foreach (var signal in release) signal.Set();
            try
            {
                connection?.Kill();
                if (connection is not null) await WaitCountAsync(connection, 0);
            }
            finally
            {
                connection?.Dispose();
                try { if (pump is not null) await pump.WaitAsync(TimeSpan.FromSeconds(15)); }
                finally { foreach (var signal in ready.Concat(release)) signal.Dispose(); }
            }
        }
    }

    [Test]
    [Arguments(0)]
    [Arguments(256)]
    public async Task Tracked_runner_seals_drains_and_observes_original_job(int memoryLimitMb)
    {
        RequireModern();
        var journal = new Journal(_ => { });
        await using var runner = new PtyAgentRunner("modern");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await runner.StartTrackedAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "echo final-custody-output"], AppContext.BaseDirectory,
                new Dictionary<string, string>(), 80, 24, memoryLimitMb, journal, timeout.Token);
            PtyCustodyObservation observation;
            do
            {
                observation = await runner.SealAndObserveCustodyAsync(timeout.Token);
                journal.Sealed.ShouldBeTrue("the durable seal must precede every accounting observation");
                if (observation.ActiveProcesses != 0) await Task.Delay(20, timeout.Token);
            } while (observation.ActiveProcesses != 0);
            observation.OutputDrained.ShouldBeTrue();
            runner.SnapshotText().ShouldContain("final-custody-output");
            await Should.ThrowAsync<InvalidOperationException>(() => runner.WriteAsync("forbidden", timeout.Token));
            Should.Throw<ObjectDisposedException>(() => runner.Resize(80, 24));
        }
        finally { await runner.KillAsync(TimeSpan.FromSeconds(5)); }
    }

    [Test]
    public async Task Unsupported_backend_never_crosses_native_intent()
    {
        RequireModern();
        var journal = new Journal(_ => throw new Exception("must not resume"));
        await using var runner = new PtyAgentRunner("inbox");
        await Should.ThrowAsync<PlatformNotSupportedException>(() => runner.StartTrackedAsync(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), ["/c", "echo forbidden"],
            AppContext.BaseDirectory, new Dictionary<string, string>(), 80, 24, 0, journal));
        journal.Started.ShouldBeFalse();
        runner.Pid.ShouldBeNull();
    }

    [Test]
    [Arguments(0u, 0)]
    [Arguments(8u, 0)] // DETACHED_PROCESS
    [Arguments(16u, 0)] // CREATE_NEW_CONSOLE
    [Arguments(0u, 256)]
    [Arguments(8u, 256)]
    [Arguments(16u, 256)]
    public async Task C478_G198_NonemptyJob(uint flags, int memoryLimitMb)
    {
        RequireModern();
        var prefix = "Local\\c478-" + Guid.NewGuid().ToString("N");
        var roles = new[] { "root", "middle", "leaf" };
        var ready = roles.Select(role => new EventWaitHandle(false, EventResetMode.ManualReset,
            prefix + "-" + role)).ToArray();
        var release = roles.Select(role => new EventWaitHandle(false, EventResetMode.ManualReset,
            prefix + "-release-" + role)).ToArray();
        var runner = new PtyAgentRunner("modern") { CustodyNative = new NativeProbe() };
        try
        {
            await runner.StartTrackedAsync(
                Path.Combine(AppContext.BaseDirectory, "custody-child", "Antiphon.CustodyTestChild.exe"),
                ["root", prefix, flags.ToString()], AppContext.BaseDirectory,
                new Dictionary<string, string>(), 80, 24, memoryLimitMb, new Journal(_ => { }));
            foreach (var signal in ready)
                signal.WaitOne(TimeSpan.FromSeconds(15)).ShouldBeTrue("every native descendant reaches its barrier contained");
            var initialCount = (await runner.SealAndObserveCustodyAsync()).ActiveProcesses;
            initialCount.ShouldBeGreaterThanOrEqualTo(3u);
            using var root = Process.GetProcessById(runner.Pid!.Value);
            release[0].Set();
            release[1].Set();
            await root.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var orphan = await WaitCountAsync(runner, initialCount - 2);
            orphan.ActiveProcesses.ShouldBeGreaterThan(0u, "root exit is not descendant exit");
            orphan.OutputDrained.ShouldBeFalse();
            release[2].Set();
            (await WaitCountAsync(runner, 0)).OutputDrained.ShouldBeTrue();
        }
        finally
        {
            foreach (var signal in release) signal.Set();
            try { await runner.KillAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                await runner.DisposeAsync();
                foreach (var signal in ready.Concat(release)) signal.Dispose();
            }
        }
    }

    private static async Task<PtyCustodyObservation> WaitCountAsync(PtyAgentRunner runner, uint expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        PtyCustodyObservation observation;
        do
        {
            observation = await runner.SealAndObserveCustodyAsync(timeout.Token);
            if (observation.ActiveProcesses != expected) await Task.Delay(20, timeout.Token);
        } while (observation.ActiveProcesses != expected);
        return observation;
    }

    private static async Task WaitCountAsync(ModernConPtyConnection connection, uint expected)
    {
        var timeout = Stopwatch.StartNew();
        while (connection.QueryActiveProcesses() != expected && timeout.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20);
        connection.QueryActiveProcesses().ShouldBe(expected);
    }

    [Test]
    public async Task C478_G195_TrackingBeforeResume() => await Atomic_launch_resumes_only_after_tracking_callback();

    [Test]
    public async Task C478_V14_RealDescendantContainer()
    {
        await C478_G198_NonemptyJob(0u, 0);
        await C478_G198_NonemptyJob(8u, 0);
        await C478_G198_NonemptyJob(16u, 0);
        await Explicit_breakaway_is_refused_by_the_actual_job();
    }

    [Test]
    public async Task Atomic_launch_resumes_only_after_tracking_callback()
    {
        var dll = RequireModern();
        var marker = Path.Combine(Path.GetTempPath(), "c478-native-" + Guid.NewGuid().ToString("N"));
        var persisted = false;
        using var connection = ModernConPtyConnection.Spawn(dll, Options($"echo ready>{Path.GetFileName(marker)}", Path.GetTempPath()), new Journal(pid =>
        {
            pid.ShouldBeGreaterThan(0);
            File.Exists(marker).ShouldBeFalse("provider instructions must not precede Tracking persistence");
            persisted = true;
        }));
        var pump = DrainAsync(connection.ReaderStream);
        try
        {
            persisted.ShouldBeTrue();
            using var child = Process.GetProcessById(connection.Pid);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            File.ReadAllText(marker).Trim().ShouldBe("ready");
            connection.QueryActiveProcesses().ShouldBe(0u);
        }
        finally
        {
            connection.Kill();
            connection.Dispose();
            await pump.WaitAsync(TimeSpan.FromSeconds(15));
            File.Delete(marker);
        }
    }

    [Test]
    public void Tracking_persistence_failure_prevents_provider_execution()
    {
        var dll = RequireModern();
        var marker = Path.Combine(Path.GetTempPath(), "c478-no-start-" + Guid.NewGuid().ToString("N"));
        Process? child = null;
        try
        {
            Should.Throw<IOException>(() => ModernConPtyConnection.Spawn(dll,
                Options($"echo forbidden>{Path.GetFileName(marker)}", Path.GetTempPath()), new Journal(pid =>
                {
                    child = Process.GetProcessById(pid);
                    throw new IOException("injected Tracking flush failure");
                }))).Message.ShouldBe("injected Tracking flush failure");
            child.ShouldNotBeNull();
            child.WaitForExit(15000).ShouldBeTrue("failed suspended launch must be terminated");
            File.Exists(marker).ShouldBeFalse();
        }
        finally
        {
            child?.Dispose();
            File.Delete(marker);
        }
    }

    private static PtyOptions Options(string command, string? cwd = null) => new()
    {
        Name = "c478-custody", Cols = 80, Rows = 24, Cwd = cwd ?? AppContext.BaseDirectory,
        App = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
        CommandLine = ["/d", "/c", command], Environment = new Dictionary<string, string>(),
    };

    private sealed class Journal(Action<int> tracking) : IPtyCustodyJournal
    {
        public bool Started { get; private set; }
        public bool Sealed { get; private set; }
        public void RecordStartIntent() => Started = true;
        public void RecordTracking(int processId) => tracking(processId);
        public void RecordSeal() => Sealed = true;
    }

    private sealed class NativeProbe : IPtyCustodyNative
    {
        private readonly WindowsPtyCustodyNative _real = new();
        public bool FailMembership { get; init; }
        public bool InvalidLimits { get; init; }
        public bool FailQuery { get; set; }
        public uint? ReportActive { get; set; }
        public Action? OnResumed { get; init; }
        public int ResumeCalls { get; private set; }
        public int QueryCalls { get; private set; }
        public bool? ObservedMembership { get; private set; }
        public uint? ObservedLimits { get; private set; }
        private SafeFileHandle? _launchJob;
        public bool IsInJob(IntPtr process, SafeFileHandle job)
        {
            _launchJob = job;
            ObservedMembership = _real.IsInJob(process, job);
            return !FailMembership && ObservedMembership.Value;
        }
        public uint QueryLimitFlags(SafeFileHandle job)
        {
            ObservedLimits = _real.QueryLimitFlags(job);
            return InvalidLimits ? 0u : ObservedLimits.Value;
        }
        public uint QueryActiveProcesses(SafeFileHandle job)
        {
            ReferenceEquals(_launchJob, job).ShouldBeTrue("observe the original retained launch job");
            QueryCalls++;
            if (FailQuery) throw new Win32Exception(5, "injected accounting failure");
            return ReportActive ?? _real.QueryActiveProcesses(job);
        }
        public uint Resume(IntPtr thread)
        {
            ResumeCalls++;
            var result = _real.Resume(thread);
            OnResumed?.Invoke();
            return result;
        }
        public PtyTerminationObservation Terminate(SafeFileHandle job) => _real.Terminate(job);
    }

    private static string RequireModern()
    {
        if (!OperatingSystem.IsWindows()) throw new SkipTestException("Windows native acceptance pending");
        if (!ConPtyRedistributable.TryLocate(out var dll, out var reason))
            throw new SkipTestException("Shipped modern backend unavailable: " + reason);
        return dll!;
    }

    private static async Task DrainAsync(Stream stream)
    {
        try
        {
            var buffer = new byte[4096];
            while (await stream.ReadAsync(buffer) > 0) { }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }
}
