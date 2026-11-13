using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using System.Diagnostics;
using System.Text;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Unit")]
public sealed class OwnedGitProcessContractTests
{
    [Test]
    public void Shared_owner_contract_lives_in_the_git_assembly()
    {
        // Baseline witness: the three callers have no shared invocation owner.
        var owner = typeof(ServerGitProcess).Assembly.GetType(
            "Antiphon.Server.Infrastructure.Git.OwnedGitProcess");
        owner.ShouldNotBeNull("CARD-1850 S1 needs an invocation owner independent of the server");
        typeof(IDisposable).IsAssignableFrom(owner).ShouldBeTrue();
    }

    [Test]
    public void Lifecycle_rejects_use_before_start_and_a_second_launch()
    {
        using var operations = new FakeOperations();
        using var owner = new OwnedGitProcess(operations);
        owner.State.ShouldBe(OwnedGitLifecycle.Prepared);
        owner.Root.ShouldBeNull();
        Should.Throw<InvalidOperationException>(() => _ = owner.StandardInput);
        Should.Throw<InvalidOperationException>(() => owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1)));
        owner.Start(StartInfo()).State.ShouldBe(OwnedGitLaunchState.Started);
        owner.State.ShouldBe(OwnedGitLifecycle.Started);
        Should.Throw<InvalidOperationException>(() => owner.Start(StartInfo()));
        operations.Starts.ShouldBe(1);
    }

    [Test]
    [Arguments("absent")]
    [Arguments("empty")]
    [Arguments("value")]
    public void Marker_is_set_before_launch_only_on_child_environment(string input)
    {
        var workerValue = Environment.GetEnvironmentVariable(OwnedGitProcess.MarkerVariable);
        using var operations = new FakeOperations();
        using var owner = new OwnedGitProcess(operations);
        var start = StartInfo();
        if (input == "absent")
        {
            start.Environment.Remove(OwnedGitProcess.MarkerVariable);
            start.Environment.ContainsKey(OwnedGitProcess.MarkerVariable).ShouldBeFalse();
        }
        else
        {
            start.Environment[OwnedGitProcess.MarkerVariable] = input == "empty" ? "" : "prior-invocation";
            start.Environment[OwnedGitProcess.MarkerVariable].ShouldBe(input == "empty" ? "" : "prior-invocation");
        }
        start.Environment["GIT_CONFIG_COUNT"] = "2";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        owner.Start(start);
        operations.MarkerAtStart.ShouldNotBeNullOrWhiteSpace();
        Guid.TryParseExact(operations.MarkerAtStart, "N", out _).ShouldBeTrue();
        operations.MarkerAtStart.ShouldNotBe("prior-invocation");
        start.Environment["GIT_CONFIG_COUNT"].ShouldBe("2");
        start.Environment["GIT_OPTIONAL_LOCKS"].ShouldBe("0");
        Environment.GetEnvironmentVariable(OwnedGitProcess.MarkerVariable).ShouldBe(workerValue);
    }

    [Test]
    public void Invocations_have_distinct_markers_and_immutable_root_identity()
    {
        using var first = new FakeOperations();
        using var second = new FakeOperations();
        using var owner = new OwnedGitProcess(first);
        using var other = new OwnedGitProcess(second);
        owner.Start(StartInfo());
        other.Start(StartInfo());
        first.MarkerAtStart.ShouldNotBe(second.MarkerAtStart);
        first.Launch = new(OwnedGitLaunchState.Started, new(99, null, 999));
        owner.Root!.Pid.ShouldBe(42);
        owner.Root.LinuxStartTicks.ShouldBe(120L);
    }

    [Test]
    public async Task Streams_exit_code_and_root_wait_come_from_the_same_owner()
    {
        var operations = new FakeOperations();
        await using var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        owner.StandardInput.ShouldBeSameAs(operations.StandardInput);
        owner.StandardOutput.ShouldBeSameAs(operations.StandardOutput);
        owner.StandardError.ShouldBeSameAs(operations.StandardError);
        await owner.StandardInput.WriteAsync("λ\0");
        await owner.StandardInput.FlushAsync();
        operations.Input.ToArray().ShouldBe(Encoding.UTF8.GetBytes("λ\0"));
        (await owner.StandardOutput.ReadToEndAsync()).ShouldBe("out");
        (await owner.StandardError.ReadToEndAsync()).ShouldBe("err");
        owner.ExitCode.ShouldBe(7);
        owner.RootExited.ShouldBe(false);
        using var token = new CancellationTokenSource();
        await owner.WaitForRootExitAsync(token.Token);
        operations.WaitToken.ShouldBe(token.Token);
    }

    [Test]
    public async Task Known_no_child_is_safe_without_signaling_or_observation()
    {
        var operations = new FakeOperations { Launch = new(OwnedGitLaunchState.NoChild, null) };
        await using var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo()).State.ShouldBe(OwnedGitLaunchState.NoChild);
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1));
        cleanup.Joined.ShouldBeTrue();
        cleanup.Reason.ShouldBe(OwnedGitReason.NoChild);
        cleanup.SignalsAttempted.ShouldBe(0);
        operations.Signals.ShouldBe(0);
        operations.Probes.ShouldBe(0);
    }

    [Test]
    [Arguments("shell")]
    [Arguments("verb")]
    [Arguments("credential")]
    public async Task Unsupported_options_refuse_before_any_creation(string option)
    {
        var operations = new FakeOperations();
        await using var owner = new OwnedGitProcess(operations);
        var start = StartInfo();
        if (option == "shell") start.UseShellExecute = true;
        if (option == "verb") start.Verb = "runas";
        if (option == "credential") start.UserName = "other-user";
        owner.Start(start).State.ShouldBe(OwnedGitLaunchState.NoChild);
        (await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1))).Joined.ShouldBeTrue();
        operations.Starts.ShouldBe(0);
        operations.Signals.ShouldBe(0);
    }

    [Test]
    public async Task Ambiguous_creation_is_never_known_no_child_even_with_empty_members()
    {
        var clock = new ManualClock();
        var operations = new FakeOperations { StartFailure = new IOException("sensitive launch detail") };
        operations.Probe = (_, _, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(2));
            return ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
        };
        await using var owner = new OwnedGitProcess(operations, clock);
        owner.Start(StartInfo()).State.ShouldBe(OwnedGitLaunchState.Ambiguous);
        owner.State.ShouldBe(OwnedGitLifecycle.Unconfirmed);
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromMilliseconds(1));
        cleanup.Joined.ShouldBeFalse();
        cleanup.RootExited.ShouldBeFalse();
        operations.Signals.ShouldBe(1);
        cleanup.ToString().ShouldNotContain("sensitive launch detail");
    }

    [Test]
    public void Missing_root_or_contradictory_no_child_are_ambiguous()
    {
        using var missing = new OwnedGitProcess(new FakeOperations { Launch = new(OwnedGitLaunchState.Started, null) });
        missing.Start(StartInfo()).State.ShouldBe(OwnedGitLaunchState.Ambiguous);
        using var contradiction = new OwnedGitProcess(new FakeOperations { Launch = new(OwnedGitLaunchState.NoChild, new(42, null, 120)) });
        contradiction.Start(StartInfo()).State.ShouldBe(OwnedGitLaunchState.Ambiguous);
    }

    [Test]
    [Arguments(1, true)]
    [Arguments(2, false)]
    [Arguments(0, false)]
    public async Task Root_exit_requires_a_complete_empty_membership(int membership, bool joined)
    {
        var state = (OwnedGitMembershipState)membership;
        var clock = new ManualClock();
        var operations = new FakeOperations();
        operations.Probe = (_, _, _) =>
        {
            if (state != OwnedGitMembershipState.Empty) clock.Advance(TimeSpan.FromMilliseconds(2));
            return ValueTask.FromResult(new OwnedGitMembers(state, state == OwnedGitMembershipState.Live ? 3 : 0));
        };
        await using var owner = new OwnedGitProcess(operations, clock);
        owner.Start(StartInfo());
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromMilliseconds(1));
        cleanup.RootExited.ShouldBeTrue();
        cleanup.Joined.ShouldBe(joined);
        owner.State.ShouldBe(joined ? OwnedGitLifecycle.Joined : OwnedGitLifecycle.Unconfirmed);
        if (state == OwnedGitMembershipState.Live) cleanup.Members.Survivors.ShouldBe(3);
        operations.Events.ShouldBe(["signal", "wait", "probe"]);
    }

    [Test]
    public async Task Empty_membership_does_not_replace_root_exit()
    {
        var clock = new ManualClock();
        var operations = new FakeOperations { Wait = _ => Task.FromResult(false) };
        operations.Probe = (_, _, _) =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(2));
            return ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
        };
        await using var owner = new OwnedGitProcess(operations, clock);
        owner.Start(StartInfo());
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromMilliseconds(1));
        cleanup.Joined.ShouldBeFalse();
        cleanup.RootExited.ShouldBeFalse();
    }

    [Test]
    public async Task Concurrent_termination_shares_task_and_ignores_request_cancellation()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FakeOperations { Signal = (_, _, _) => new(release.Task) };
        await using var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        var first = owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1), new CancellationToken(true));
        var second = owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(6));
        try
        {
            second.ShouldBeSameAs(first);
            owner.State.ShouldBe(OwnedGitLifecycle.Terminating);
        }
        finally { release.TrySetResult(4); }
        var result = await first.WaitAsync(TimeSpan.FromSeconds(2));
        result.Joined.ShouldBeTrue();
        result.SignalsAttempted.ShouldBe(4);
        operations.Signals.ShouldBe(1);
        owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(6)).ShouldBeSameAs(first);
    }

    [Test]
    public async Task Dispose_during_cleanup_defers_exactly_one_close_without_signaling_again()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FakeOperations { Signal = (_, _, _) => new(release.Task) };
        var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        var cleanup = owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1));
        try
        {
            owner.Dispose();
            owner.Dispose();
            owner.State.ShouldBe(OwnedGitLifecycle.Disposed);
            operations.Closes.ShouldBe(0, "in-flight cleanup still owns the streams and platform resources");
        }
        finally
        {
            release.TrySetResult(1);
            await owner.DisposeAsync();
        }
        (await cleanup).Joined.ShouldBeTrue();
        operations.Closes.ShouldBe(1);
        operations.Signals.ShouldBe(1);
        operations.Input.CanWrite.ShouldBeFalse();
        operations.StandardOutput.BaseStream.CanRead.ShouldBeFalse();
        operations.StandardError.BaseStream.CanRead.ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => _ = owner.StandardInput);
    }

    [Test]
    public async Task Completed_termination_and_repeated_disposal_close_once()
    {
        var operations = new FakeOperations();
        var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        var cleanup = owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1));
        (await cleanup).Joined.ShouldBeTrue();
        operations.Closes.ShouldBe(0);
        owner.Dispose();
        await owner.DisposeAsync();
        owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1)).ShouldBeSameAs(cleanup);
        operations.Closes.ShouldBe(1);
        operations.Signals.ShouldBe(1);
        owner.State.ShouldBe(OwnedGitLifecycle.Disposed);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void Dispose_preserves_crash_fencing_and_never_implies_termination(bool launched)
    {
        var operations = new FakeOperations();
        var owner = new OwnedGitProcess(operations);
        if (launched) owner.Start(StartInfo());
        owner.Dispose();
        owner.Dispose();
        operations.Signals.ShouldBe(0);
        operations.Closes.ShouldBe(1);
        owner.State.ShouldBe(OwnedGitLifecycle.Disposed);
        Should.Throw<ObjectDisposedException>(() => owner.Start(StartInfo()));
        Should.Throw<ObjectDisposedException>(() => owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1)));
    }

    [Test]
    public async Task One_deadline_covers_signals_wait_and_probe_and_rejects_late_empty()
    {
        var clock = new ManualClock();
        var remaining = new List<TimeSpan>();
        var operations = new FakeOperations();
        operations.Signal = (_, deadline, _) =>
        {
            remaining.Add(deadline.Remaining);
            clock.Advance(TimeSpan.FromSeconds(2));
            return ValueTask.FromResult(2);
        };
        operations.Wait = _ => { clock.Advance(TimeSpan.FromSeconds(2)); return Task.FromResult(true); };
        operations.Probe = (_, deadline, _) =>
        {
            remaining.Add(deadline.Remaining);
            clock.Advance(TimeSpan.FromSeconds(2));
            return ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
        };
        await using var owner = new OwnedGitProcess(operations, clock);
        owner.Start(StartInfo());
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(6));
        remaining.ShouldBe([TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(2)]);
        cleanup.Joined.ShouldBeFalse();
        cleanup.Members.State.ShouldBe(OwnedGitMembershipState.Unknown);
        cleanup.Reason.ShouldBe(OwnedGitReason.Deadline);
        cleanup.Duration.ShouldBe(TimeSpan.FromSeconds(6));
        cleanup.SignalDuration.ShouldBe(TimeSpan.FromSeconds(2));
        cleanup.RootWaitDuration.ShouldBe(TimeSpan.FromSeconds(2));
        cleanup.ProbeDuration.ShouldBe(TimeSpan.FromSeconds(2));
        cleanup.SignalsAttempted.ShouldBe(2);
    }

    [Test]
    public async Task Bounded_cleanup_cancels_a_cooperative_blocked_operation()
    {
        var operations = new FakeOperations();
        operations.Signal = async (_, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        };
        await using var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromMilliseconds(20)).WaitAsync(TimeSpan.FromSeconds(2));
        cleanup.Joined.ShouldBeFalse();
        cleanup.Reason.ShouldBe(OwnedGitReason.Deadline);
        operations.Probes.ShouldBe(0);
        operations.Signals.ShouldBe(1);
    }

    [Test]
    public async Task A_later_empty_pass_is_required_after_a_live_observation()
    {
        var operations = new FakeOperations();
        operations.Probe = (_, _, _) => ValueTask.FromResult(operations.Probes == 1
            ? new OwnedGitMembers(OwnedGitMembershipState.Live, 1)
            : new OwnedGitMembers(OwnedGitMembershipState.Empty));
        await using var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1));
        cleanup.Joined.ShouldBeTrue();
        operations.Probes.ShouldBe(2);
        cleanup.SignalsAttempted.ShouldBe(2);
    }

    [Test]
    [Arguments("signal")]
    [Arguments("wait")]
    [Arguments("probe")]
    public async Task Observation_failures_keep_an_unknown_verdict_and_sanitized_reason(string phase)
    {
        const string detail = "command-and-environment-secret";
        var operations = new FakeOperations();
        if (phase == "signal") operations.Signal = (_, _, _) => throw new IOException(detail);
        if (phase == "wait") operations.Wait = _ => throw new UnauthorizedAccessException(detail);
        if (phase == "probe") operations.Probe = (_, _, _) => throw new IOException(detail);
        await using var owner = new OwnedGitProcess(operations);
        owner.Start(StartInfo());
        var cleanup = await owner.TerminateAndJoinAsync(TimeSpan.FromSeconds(1));
        cleanup.Joined.ShouldBeFalse();
        cleanup.Members.State.ShouldBe(OwnedGitMembershipState.Unknown);
        cleanup.Reason.ShouldBe(OwnedGitReason.ObservationFailed);
        cleanup.ToString().ShouldNotContain(detail);
    }

    private static ProcessStartInfo StartInfo() => new("git") { UseShellExecute = false };

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        internal void Advance(TimeSpan duration) => _ticks += duration.Ticks;
    }

    private sealed class FakeOperations : IOwnedGitProcessOperations
    {
        internal readonly MemoryStream Input = new();
        internal readonly List<string> Events = [];
        internal OwnedGitLaunch Launch = new(OwnedGitLaunchState.Started, new(42, new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc), 120));
        internal Exception? StartFailure;
        internal string? MarkerAtStart;
        internal int Starts, Signals, Probes, Closes;
        internal CancellationToken WaitToken;
        internal Func<OwnedGitRoot?, OwnedGitDeadline, CancellationToken, ValueTask<int>> Signal = (_, _, _) => ValueTask.FromResult(1);
        internal Func<OwnedGitRoot?, OwnedGitDeadline, CancellationToken, ValueTask<OwnedGitMembers>> Probe =
            (_, _, _) => ValueTask.FromResult(new OwnedGitMembers(OwnedGitMembershipState.Empty));
        internal Func<CancellationToken, Task<bool>> Wait = _ => Task.FromResult(true);
        public StreamWriter StandardInput { get; }
        public StreamReader StandardOutput { get; } = new(new MemoryStream(Encoding.UTF8.GetBytes("out")));
        public StreamReader StandardError { get; } = new(new MemoryStream(Encoding.UTF8.GetBytes("err")));
        public bool? RootExited => false;
        public int ExitCode => 7;

        internal FakeOperations() => StandardInput = new StreamWriter(Input, new UTF8Encoding(false));
        public OwnedGitLaunch Start(ProcessStartInfo start)
        {
            Starts++;
            MarkerAtStart = start.Environment[OwnedGitProcess.MarkerVariable];
            if (StartFailure is not null) throw StartFailure;
            return Launch;
        }
        public Task<bool> WaitForRootExitAsync(CancellationToken cancellationToken)
        {
            Events.Add("wait");
            WaitToken = cancellationToken;
            return Wait(cancellationToken);
        }
        public ValueTask<OwnedGitMembers> ProbeMembersAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
            CancellationToken cancellationToken)
        {
            Events.Add("probe");
            Probes++;
            return Probe(root, deadline, cancellationToken);
        }
        public ValueTask<int> TerminateMembersAsync(OwnedGitRoot? root, OwnedGitDeadline deadline,
            CancellationToken cancellationToken)
        {
            Events.Add("signal");
            Signals++;
            return Signal(root, deadline, cancellationToken);
        }
        public void Dispose()
        {
            Closes++;
            StandardInput.Dispose();
            StandardOutput.Dispose();
            StandardError.Dispose();
        }
    }
}
