using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Checkpoints;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class WorkspaceDiskAdmissionTests
{
    private const long Floor = 20L << 30;
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 0, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("server2")]
    [Arguments("server2-temp")]
    public void ShippedRunnersHoldOneByteBelowTheAbsoluteFloor(string runnerId)
    {
        var (guard, cache, clock, _) = Create();
        Record(cache, clock.GetUtcNow(), runnerId, Floor - 1);
        guard.HoldReason(runnerId).ShouldStartWith("workspace_disk_low:");
        clock.Advance(TimeSpan.FromSeconds(1));
        Record(cache, clock.GetUtcNow(), runnerId, Floor);
        guard.HoldReason(runnerId).ShouldBeNull("exactly 20 GiB is enough even if the attention row is active");
    }

    [Test]
    [Arguments("server2", "server2-temp")]
    [Arguments("server2-temp", "server2")]
    public void SharedFilesystemHoldsBothRoutesAndRecoversFromEither(string firstRoute, string otherRoute)
    {
        var (guard, cache, clock, log) = Create();
        Record(cache, clock.GetUtcNow(), firstRoute, Floor - 1);
        cache.Latest(otherRoute).ShouldBeNull();
        guard.HoldReason(firstRoute).ShouldStartWith("workspace_disk_low:");
        guard.HoldReason(otherRoute).ShouldStartWith("workspace_disk_low:");
        guard.HoldReason(RunnerPlatformWire.DesktopId).ShouldBeNull();
        log.Messages.ShouldBeEmpty("the peer provides a usable measurement for the same filesystem");

        clock.Advance(TimeSpan.FromSeconds(1));
        Record(cache, clock.GetUtcNow(), otherRoute, Floor);
        guard.HoldReason(firstRoute).ShouldBeNull();
        guard.HoldReason(otherRoute).ShouldBeNull();

        clock.Advance(TimeSpan.FromSeconds(1));
        Record(cache, clock.GetUtcNow(), firstRoute, -1);
        guard.HoldReason(firstRoute).ShouldBeNull();
        log.Messages.ShouldBeEmpty("an invalid sample cannot replace a valid measurement");
    }

    [Test]
    public void MissingStaleAndInvalidSamplesEmitBoundedDiagnostics()
    {
        var (guard, cache, clock, log) = Create();
        const string runnerId = "server2";
        for (var i = 0; i < 16; i++) guard.HoldReason(runnerId).ShouldBeNull();
        log.Messages.Count.ShouldBe(1);
        log.Messages[0].ShouldContain("workspace_disk_sample_unavailable");
        log.Messages[0].ShouldContain("missing");
        log.Messages[0].ShouldContain(runnerId);

        Record(cache, clock.GetUtcNow(), runnerId, Floor - 1);
        guard.HoldReason(runnerId).ShouldStartWith("workspace_disk_low:");
        clock.Advance(TimeSpan.FromHours(1));
        guard.HoldReason(runnerId).ShouldBeNull();
        guard.HoldReason(runnerId).ShouldBeNull();
        log.Messages.Count.ShouldBe(2);
        log.Messages[1].ShouldContain("stale");

        Record(cache, clock.GetUtcNow(), runnerId, -1);
        guard.HoldReason(runnerId).ShouldBeNull();
        guard.HoldReason(runnerId).ShouldBeNull();
        log.Messages.Count.ShouldBe(3);
        log.Messages[2].ShouldContain("invalid");
    }

    private static (WorkspaceDiskAdmission Guard, HostStatsCache Cache, FakeTimeProvider Clock, RecordingLogger Log) Create()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(CheckpointFixtures.RepoRoot, "server", "appsettings.json"))
            .Build();
        var settings = configuration.GetSection(WorkspaceDiskAlarmSettings.SectionName)
            .Get<WorkspaceDiskAlarmSettings>().ShouldNotBeNull();
        var clock = new FakeTimeProvider(Now);
        var cache = new HostStatsCache(new HostStatsSettings(), clock);
        var log = new RecordingLogger();
        using var services = new ServiceCollection()
            .AddSingleton<IOptions<WorkspaceDiskAlarmSettings>>(Options.Create(settings))
            .AddSingleton<TimeProvider>(clock)
            .AddSingleton(cache)
            .AddSingleton<ILogger<WorkspaceDiskAdmission>>(log)
            .BuildServiceProvider();
        return (ActivatorUtilities.CreateInstance<WorkspaceDiskAdmission>(services), cache, clock, log);
    }

    private static void Record(HostStatsCache cache, DateTimeOffset at, string runner, long free)
    {
        // At one tebibyte, 20 GiB crosses the attention percentage and absolute alert floor.
        cache.Record(runner, new RunnerHostStatsDto(at, 5, 60, 4,
            new RunnerHostCurrentDto(null, null, null, null, null, null, null, null, null,
                [new RunnerHostDiskDto("/work", free, 1L << 40)], null, []),
            new Dictionary<string, RunnerHostWindowRollups>(), null));
    }

    private sealed class RecordingLogger : ILogger<WorkspaceDiskAdmission>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
