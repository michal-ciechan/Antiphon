using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0716 D-2: shutdown waits for the launch drain, then stops the host once.</summary>
[Category("Unit")]
public class OperatorShutdownCoordinatorTests
{
    [Test]
    public async Task Stop_waits_for_the_launch_drain_then_stops_once()
    {
        var drain = new DelayDrain(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;
        var life = new RecordingLifetime(drain, started);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var coordinator = NewCoordinator(drain, life, new OperatorSettings(), factory);

        await coordinator.StopAsync("test");

        drain.Completed.ShouldBeTrue();
        life.StopCalls.ShouldBe(1);
        life.StoppedAfterDrain.ShouldBeTrue();
        var line = logs.Entries.Single(e => e.Level == LogLevel.Information);
        line.Message.ShouldContain("drained=True");
    }

    [Test]
    public async Task Stop_proceeds_when_the_drain_bound_expires()
    {
        var drain = new NeverDrain();
        var started = DateTime.UtcNow;
        var life = new RecordingLifetime(drain, started);
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var coordinator = NewCoordinator(
            drain, life, new OperatorSettings { ShutdownDrainSeconds = 1 }, factory);

        await coordinator.StopAsync("test");

        life.StopCalls.ShouldBe(1);
        life.ElapsedAtStop.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
        var line = logs.Entries.Single(e => e.Level == LogLevel.Information);
        line.Message.ShouldContain("drained=False");
    }

    private static OperatorShutdownCoordinator NewCoordinator(
        ILaunchDrain drain,
        IHostApplicationLifetime life,
        OperatorSettings settings,
        ILoggerFactory factory)
    {
        var services = new ServiceCollection();
        return new OperatorShutdownCoordinator(
            drain,
            life,
            Options.Create(settings),
            factory.CreateLogger<OperatorShutdownCoordinator>(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System);
    }

    private interface IOrderProbe
    {
        bool Completed { get; }
    }

    private sealed class DelayDrain(TimeSpan delay) : ILaunchDrain, IOrderProbe
    {
        public bool Completed { get; private set; }

        public async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            Completed = true;
        }
    }

    private sealed class NeverDrain : ILaunchDrain, IOrderProbe
    {
        public bool Completed { get; private set; }

        public async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await Task.Delay(Timeout.Infinite, cts.Token);
            Completed = true;
        }
    }

    /// <summary>
    /// Samples the drain and the clock inside <see cref="StopApplication"/>, so a stop that
    /// runs before the drain cannot report itself as having waited.
    /// </summary>
    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly IOrderProbe _drain;
        private readonly DateTime _startedUtc;

        public RecordingLifetime(IOrderProbe drain, DateTime startedUtc)
        {
            _drain = drain;
            _startedUtc = startedUtc;
        }

        public int StopCalls { get; private set; }
        public bool StoppedAfterDrain { get; private set; }
        public TimeSpan ElapsedAtStop { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopCalls++;
            StoppedAfterDrain = _drain.Completed;
            ElapsedAtStop = DateTime.UtcNow - _startedUtc;
            _stopping.Cancel();
        }
    }
}
