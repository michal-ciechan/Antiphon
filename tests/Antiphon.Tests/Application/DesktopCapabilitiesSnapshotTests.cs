using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

// CARD-1424. One failed desktop capabilities probe under load used to blank the desktop row
// (platform, features, dispatch eligibility) until the next good sweep. The directory now serves
// the last good answer for a bounded grace after an ambiguous failure, and goes stale at once on a
// definite one.
[Category("Unit")]
public sealed class DesktopCapabilitiesSnapshotTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly RunnerCapabilitiesDto Windows = new(
        "ConPty", "ConPty", "", false,
        Version: "c1424",
        Features: [RunnerPlatformWire.Feature],
        RunnerStoreId: Guid.Parse("14241424-1424-1424-1424-142414241424"),
        Platform: "windows");

    [Test]
    public async Task A_single_failed_probe_after_a_good_one_keeps_windows_and_dispatch_eligible()
    {
        var (directory, runner, time, _) = Build();
        runner.Answer(Windows);
        var first = await directory.DescribeAsync(null, CancellationToken.None);
        first!.Platform.ShouldBe("windows");

        time.Advance(TimeSpan.FromSeconds(60));
        runner.Fail("deadline", definite: false);
        var second = await directory.DescribeAsync(null, CancellationToken.None);

        second!.Platform.ShouldBe("windows");
        second.DispatchEligible.ShouldBeTrue();
        second.Available.ShouldBeTrue();
        second.Stale.ShouldBeFalse();
        second.Capabilities.ShouldBeSameAs(Windows);
        second.PlatformObservedAt.ShouldBe(T0, "the snapshot's own age, not the failed probe's time");
    }

    [Test]
    public async Task Failures_past_the_grace_window_go_stale()
    {
        var (directory, runner, time, _) = Build();
        runner.Answer(Windows);
        (await directory.DescribeAsync(null, CancellationToken.None))!.Platform.ShouldBe("windows");

        foreach (var at in new[] { 60, 120, 180 })
        {
            time.SetUtcNow(T0.AddSeconds(at));
            runner.Fail("limiter", definite: false);
            var served = await directory.DescribeAsync(null, CancellationToken.None);
            served!.Platform.ShouldBe("windows", $"t0+{at}s is inside the 180 s grace");
            served.DispatchEligible.ShouldBeTrue();
            served.Stale.ShouldBeFalse();
        }

        time.SetUtcNow(T0.AddSeconds(181));
        runner.Fail("limiter", definite: false);
        var stale = await directory.DescribeAsync(null, CancellationToken.None);

        AssertStale(stale);
    }

    [Test]
    public async Task Zero_grace_restores_todays_stale_on_first_failure()
    {
        var (directory, runner, time, _) = Build(graceSeconds: 0);
        runner.Answer(Windows);
        (await directory.DescribeAsync(null, CancellationToken.None))!.Platform.ShouldBe("windows");

        time.Advance(TimeSpan.FromSeconds(1));
        runner.Fail("deadline", definite: false);

        AssertStale(await directory.DescribeAsync(null, CancellationToken.None));
    }

    [Test]
    public async Task Refused_probe_goes_stale_immediately_and_circuit_does_not_revive_it()
    {
        var (directory, runner, time, _) = Build();
        runner.Answer(Windows);
        (await directory.DescribeAsync(null, CancellationToken.None))!.Platform.ShouldBe("windows");

        time.SetUtcNow(T0.AddSeconds(5));
        runner.Fail("refused", definite: true);
        AssertStale(await directory.DescribeAsync(null, CancellationToken.None));

        time.SetUtcNow(T0.AddSeconds(10));
        runner.Fail("circuit", definite: false);
        AssertStale(await directory.DescribeAsync(null, CancellationToken.None));

        time.SetUtcNow(T0.AddSeconds(20));
        runner.Answer(Windows);
        var fresh = await directory.DescribeAsync(null, CancellationToken.None);
        fresh!.Platform.ShouldBe("windows");
        fresh.DispatchEligible.ShouldBeTrue();
        fresh.PlatformObservedAt.ShouldBe(T0.AddSeconds(20));

        time.SetUtcNow(T0.AddSeconds(30));
        runner.Fail("circuit", definite: false);
        (await directory.DescribeAsync(null, CancellationToken.None))!.Platform
            .ShouldBe("windows", "a success clears absence, so an ambiguous failure is served again");
    }

    [Test]
    public async Task A_hung_probe_is_classified_as_deadline()
    {
        var (directory, runner, time, log) = Build();
        runner.Answer(Windows);
        (await directory.DescribeAsync(null, CancellationToken.None))!.Platform.ShouldBe("windows");

        time.Advance(TimeSpan.FromSeconds(60));
        runner.Hang();
        var pending = directory.DescribeAsync(null, CancellationToken.None);
        pending.IsCompleted.ShouldBeFalse("the probe hangs until the directory's own deadline");
        time.Advance(TimeSpan.FromSeconds(2));
        var served = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        served!.Platform.ShouldBe("windows");
        served.Stale.ShouldBeFalse();
        log.Warnings.ShouldHaveSingleItem().ShouldContain("reason deadline");
    }

    [Test]
    public async Task Failure_log_is_rate_limited_and_names_the_reason()
    {
        var (directory, runner, time, log) = Build();
        runner.Answer(Windows);
        await directory.DescribeAsync(null, CancellationToken.None);

        foreach (var at in new[] { 10, 20, 30, 40, 50 })
        {
            time.SetUtcNow(T0.AddSeconds(at));
            runner.Fail("deadline", definite: false);
            await directory.DescribeAsync(null, CancellationToken.None);
        }

        log.Warnings.Count.ShouldBe(1, "one Warning per streak while the reason holds");
        log.Warnings[0].ShouldContain("reason deadline");
        log.Warnings[0].ShouldContain("serving last-good");

        time.SetUtcNow(T0.AddSeconds(60));
        runner.Fail("limiter", definite: false);
        await directory.DescribeAsync(null, CancellationToken.None);
        log.Warnings.Count.ShouldBe(2, "a changed reason logs");
        log.Warnings[1].ShouldContain("reason limiter");

        time.SetUtcNow(T0.AddSeconds(100));
        runner.Fail("limiter", definite: false);
        await directory.DescribeAsync(null, CancellationToken.None);
        log.Warnings.Count.ShouldBe(2, "40 s after the last Warning is inside the 60 s limit");

        time.SetUtcNow(T0.AddSeconds(121));
        runner.Fail("limiter", definite: false);
        await directory.DescribeAsync(null, CancellationToken.None);
        log.Warnings.Count.ShouldBe(3, "61 s after the last Warning");
        log.Warnings[2].ShouldContain("consecutive 8");

        time.SetUtcNow(T0.AddSeconds(150));
        runner.Fail("limiter", definite: true);
        await directory.DescribeAsync(null, CancellationToken.None);
        log.Warnings.Count.ShouldBe(4, "the change to stale always logs");
        log.Warnings[3].ShouldContain("serving stale");

        time.SetUtcNow(T0.AddSeconds(160));
        runner.Answer(Windows);
        await directory.DescribeAsync(null, CancellationToken.None);
        log.Warnings.Count.ShouldBe(4);
        log.Informations.ShouldHaveSingleItem().ShouldContain("recovered after 9 failed probes in 150s");
    }

    private static void AssertStale(RunnerDescriptor? descriptor)
    {
        descriptor!.Platform.ShouldBeNull();
        descriptor.PlatformObservedAt.ShouldBeNull();
        descriptor.Available.ShouldBeFalse();
        descriptor.DispatchEligible.ShouldBeFalse();
        descriptor.Stale.ShouldBeTrue();
        descriptor.Capabilities.ShouldBeNull();
    }

    private static (PhoneHomeRunnerDirectory Directory, ScriptedRunner Runner, FakeTimeProvider Time, CapturingLogger Log) Build(
        int graceSeconds = 180)
    {
        var time = new FakeTimeProvider(T0);
        var runner = new ScriptedRunner();
        var log = new CapturingLogger();
        var directory = new PhoneHomeRunnerDirectory(
            runner,
            Options.Create(new PhoneHomeRunnerSettings { Enabled = false, DesktopCapabilitiesGraceSeconds = graceSeconds }),
            new NoScopeFactory(),
            time,
            inventoryLogger: log);
        return (directory, runner, time, log);
    }

    /// <summary>Answers the next probe from what the test set: capabilities, a classified failure, or a hang.</summary>
    private sealed class ScriptedRunner : ISessionRunnerClient
    {
        private Func<CancellationToken, Task<RunnerCapabilitiesProbe>> _next =
            _ => Task.FromResult(new RunnerCapabilitiesProbe(null, RunnerCapabilitiesProbe.NoAnswer, false));

        public void Answer(RunnerCapabilitiesDto capabilities) =>
            _next = _ => Task.FromResult(new RunnerCapabilitiesProbe(capabilities, null, false));

        public void Fail(string reason, bool definite) =>
            _next = _ => Task.FromResult(new RunnerCapabilitiesProbe(null, reason, definite));

        public void Hang() =>
            _next = async ct =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            };

        public Task<RunnerCapabilitiesProbe> ProbeCapabilitiesAsync(CancellationToken ct) => _next(ct);

        public async Task<RunnerCapabilitiesDto?> GetCapabilitiesAsync(CancellationToken ct) =>
            (await ProbeCapabilitiesAsync(ct)).Capabilities;

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => throw new NotSupportedException();
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => throw new NotSupportedException();
        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<PhoneHomeRunnerDirectory>
    {
        public List<string> Warnings { get; } = [];
        public List<string> Informations { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            if (!message.StartsWith("Desktop runner capabilities", StringComparison.Ordinal))
                return;
            if (logLevel == LogLevel.Warning) Warnings.Add(message);
            else if (logLevel == LogLevel.Information) Informations.Add(message);
        }
    }

    private sealed class NoScopeFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("the desktop descriptor takes no scope");
    }
}
