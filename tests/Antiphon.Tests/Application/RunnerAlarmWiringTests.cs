using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Server.Infrastructure.Orchestration;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0726 V-36. Program wires the loop, both observers, and the default exclusion.</summary>
[NotInParallel]
[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]
[ParallelLimiter<ProcessSpawnLimit>]
[Category("Integration")]
public sealed class RunnerAlarmWiringTests
{
    private readonly AntiphonWebAppFactory _factory;
    public RunnerAlarmWiringTests(AntiphonWebAppFactory factory) => _factory = factory;

    [Test]
    [Timeout(60_000)]
    public async Task program_wires_the_loop_the_observers_and_the_default_exclusion()
    {
        var hosted = _factory.Services.GetServices<IHostedService>().OfType<RunnerAlarmHostedService>().ToList();
        hosted.Count.ShouldBe(1);
        _factory.Services.GetRequiredService<IRunnerAlarmExclusion>().ShouldBeOfType<NeverExcluded>();
        _factory.Services.GetRequiredService<IRunnerEligibilitySnapshotSource>().ShouldBeOfType<PhoneHomeRunnerDirectory>();
        var directory = _factory.Services.GetRequiredService<PhoneHomeRunnerDirectory>();
        directory.Observer.ShouldBeSameAs(_factory.Services.GetRequiredService<AlarmWakeQueue>());
        var alarms = _factory.Services.GetRequiredService<IOptions<AlarmSettings>>().Value;
        alarms.RunnerGraceSeconds.ShouldBe(180);
        alarms.SweepMinutes.ShouldBe(15);
        alarms.JournalStaleMinutes.ShouldBe(5);

        using var repo = new ScratchGitRepo("c726-wiring");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, CancellationToken.None);
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + 1;
        var path = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks)));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
        var lease = _factory.Services.GetRequiredService<IRepositoryMutationLease>();
        (await lease.TryAcquireAsync(repo.Path, CancellationToken.None)).ShouldBeNull();
        var state = _factory.Services.GetRequiredService<RunnerAlarmState>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline
            && !state.Current.Journals.Any(finding => LandingGit.PathsEqual(finding.CommonDirectory, common)))
            await Task.Delay(50);
        state.Current.Journals.ShouldContain(finding => LandingGit.PathsEqual(finding.CommonDirectory, common));
    }
}
