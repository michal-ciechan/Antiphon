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
        var life = new RecordingLifetime();
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
        var life = new RecordingLifetime();
        var logs = new CapturingLoggerProvider();
        using var factory = LoggerFactory.Create(b => b.AddProvider(logs));
        var coordinator = NewCoordinator(
            drain, life, new OperatorSettings { ShutdownDrainSeconds = 1 }, factory);
        var started = DateTime.UtcNow;

        await coordinator.StopAsync("test");

        life.StopCalls.ShouldBe(1);
        (DateTime.UtcNow - started).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromSeconds(1));
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

    private sealed class DelayDrain(TimeSpan delay) : ILaunchDrain
    {
        public bool Completed { get; private set; }

        public async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct)
        {
            await Task.Delay(delay, ct);
            Completed = true;
        }
    }

    private sealed class NeverDrain : ILaunchDrain
    {
        public async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            await Task.Delay(Timeout.Infinite, cts.Token);
        }
    }

    private sealed class RecordingLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();
        public int StopCalls { get; private set; }
        public bool StoppedAfterDrain { get; private set; }
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
            StopCalls++;
            StoppedAfterDrain = true;
            _stopping.Cancel();
        }
    }
}
