using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0726 V-4. A journal fence names the common directory; a busy lock does not.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryFenceObserverTests
{
    [Test]
    [Timeout(30_000)]
    public async Task a_fenced_acquire_and_a_describe_name_the_common_directory_once_each(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c726-fence");
        var git = new LandingGit();
        var recorder = new FenceRecorder();
        var lease = new RepositoryMutationLease(git, recorder);
        var common = await git.CommonDirectoryAsync(repo.Path, ct);

        await using (var held = await lease.TryAcquireAsync(repo.Path, ct))
        {
            held.ShouldNotBeNull();
            var blocked = await lease.TryAcquireAsync(repo.Path, ct);
            blocked.ShouldBeNull();
        }

        recorder.Commons.ShouldBeEmpty();

        var path = await PlantDeadAsync(common, ct);
        var fenced = await lease.TryAcquireAsync(repo.Path, ct);
        fenced.ShouldBeNull();
        recorder.Commons.Count.ShouldBe(1);
        LandingGit.PathsEqual(recorder.Commons[0], common).ShouldBeTrue();

        (await lease.DescribeUnavailableAsync(repo.Path, ct)).ShouldNotBeNull();
        recorder.Commons.Count.ShouldBe(2);
        LandingGit.PathsEqual(recorder.Commons[1], common).ShouldBeTrue();

        File.Delete(path);
        await using var acquired = await lease.TryAcquireAsync(repo.Path, ct);
        acquired.ShouldNotBeNull();
        (await lease.DescribeUnavailableAsync(repo.Path, ct)).ShouldBeNull();
        recorder.Commons.Count.ShouldBe(2);
    }

    private static async Task<string> PlantDeadAsync(string common, CancellationToken ct)
    {
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + 1;
        var path = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(
            new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks)), ct);
        return path;
    }

    private sealed class FenceRecorder : IRepositoryFenceObserver
    {
        public List<string> Commons { get; } = [];

        public void Fenced(string commonDirectory) => Commons.Add(commonDirectory);
    }
}
