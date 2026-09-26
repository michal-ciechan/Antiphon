using System.Diagnostics;
using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0726 V-5..V-7, V-24, V-25. Journal records are classified and never deleted.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RepositoryChildJournalInspectorTests
{
    [Test]
    [Timeout(30_000)]
    public async Task a_live_record_is_alive_and_never_stale(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c726-live");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var now = DateTimeOffset.UtcNow;
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks), now.AddHours(-1), ct);

        var inspection = await new RepositoryChildJournalInspector(git)
            .InspectAsync(repo.Path, TimeSpan.FromMinutes(5), now, ct);
        var finding = inspection.Findings.Single();
        finding.State.ShouldBe(JournalRecordState.Alive);
        finding.Stale.ShouldBeFalse();
        finding.ProcessId.ShouldBe(Environment.ProcessId);
        inspection.StaleCount.ShouldBe(0);
    }

    [Test]
    [Timeout(30_000)]
    public async Task dead_completed_unknown_and_malformed_records_past_the_threshold_are_stale(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c726-states");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var now = DateTimeOffset.UtcNow;
        var written = now.AddMinutes(-10);
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var paths = new List<string>
        {
            await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks + 1), written, ct),
            await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common, int.MaxValue, ticks), written, ct),
            await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common, null, null, Completed: true), written, ct),
            await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common, null, null), written, ct),
            await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common + "-other", Environment.ProcessId, ticks), written, ct),
        };
        var invalid = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(invalid, "invalid json", ct);
        File.SetLastWriteTimeUtc(invalid, written.UtcDateTime);
        paths.Add(invalid);
        var torn = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json.tmp");
        await File.WriteAllTextAsync(torn, "{", ct);
        File.SetLastWriteTimeUtc(torn, written.UtcDateTime);
        paths.Add(torn);

        var inspection = await new RepositoryChildJournalInspector(git)
            .InspectAsync(repo.Path, TimeSpan.FromMinutes(5), now, ct);
        inspection.Findings.Select(finding => finding.State).ShouldBe(
        [
            JournalRecordState.Dead,
            JournalRecordState.Dead,
            JournalRecordState.Completed,
            JournalRecordState.Unknown,
            JournalRecordState.Unknown,
            JournalRecordState.Malformed,
            JournalRecordState.Malformed,
        ], ignoreOrder: true);
        inspection.Findings.ShouldAllBe(finding => finding.Stale);
        inspection.StaleCount.ShouldBe(7);
        paths.ShouldAllBe(path => File.Exists(path));
        File.Exists(Path.Combine(common, "antiphon", "landing.lock")).ShouldBeFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task age_threshold_is_inclusive_and_an_absent_journal_raises_nothing(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c726-age");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks + 1;
        var path = await PlantAsync(common, new RepositoryChildJournal.ChildRecord(
            1, common, Environment.ProcessId, ticks), DateTimeOffset.UtcNow, ct);
        var stored = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        var inspector = new RepositoryChildJournalInspector(git);

        var young = await inspector.InspectAsync(repo.Path, TimeSpan.FromMinutes(5), stored.Add(new TimeSpan(0, 4, 59)), ct);
        var youngFinding = young.Findings.Single();
        youngFinding.State.ShouldBe(JournalRecordState.Dead);
        youngFinding.Stale.ShouldBeFalse();

        var due = await inspector.InspectAsync(repo.Path, TimeSpan.FromMinutes(5), stored.AddMinutes(5), ct);
        due.Findings.Single().Stale.ShouldBeTrue();

        using var empty = new ScratchGitRepo("c726-absent");
        var emptyInspection = await inspector.InspectAsync(empty.Path, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, ct);
        emptyInspection.Findings.ShouldBeEmpty();
        var emptyCommon = await git.CommonDirectoryAsync(empty.Path, ct);
        Directory.Exists(Path.Combine(emptyCommon, "antiphon", "children")).ShouldBeFalse();
        File.Exists(Path.Combine(emptyCommon, "antiphon", "landing.lock")).ShouldBeFalse();
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_record_whose_process_cannot_be_read_is_unknown_and_stale(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c726-unreadable");
        var git = new UnreadableLivenessGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var now = DateTimeOffset.UtcNow;
        var ticks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks;
        await PlantAsync(common, new RepositoryChildJournal.ChildRecord(1, common, Environment.ProcessId, ticks), now.AddMinutes(-10), ct);

        var finding = (await new RepositoryChildJournalInspector(git)
            .InspectAsync(repo.Path, TimeSpan.FromMinutes(5), now, ct)).Findings.Single();
        finding.State.ShouldBe(JournalRecordState.Unknown);
        finding.Stale.ShouldBeTrue();
    }

    [Test]
    [Timeout(30_000)]
    public async Task a_children_path_that_is_a_file_is_one_malformed_finding(CancellationToken ct)
    {
        using var repo = new ScratchGitRepo("c726-children-file");
        var git = new LandingGit();
        var common = await git.CommonDirectoryAsync(repo.Path, ct);
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(Path.Combine(common, "antiphon"));
        await File.WriteAllTextAsync(children, "not-a-directory", ct);
        var now = DateTimeOffset.UtcNow;
        File.SetLastWriteTimeUtc(children, now.AddMinutes(-10).UtcDateTime);
        var lease = new RepositoryMutationLease(git);

        (await lease.DescribeUnavailableAsync(repo.Path, ct)).ShouldNotBeNull();
        var finding = (await new RepositoryChildJournalInspector(git)
            .InspectAsync(repo.Path, TimeSpan.FromMinutes(5), now, ct)).Findings.Single();
        finding.State.ShouldBe(JournalRecordState.Malformed);
        finding.Stale.ShouldBeTrue();
        LandingGit.PathsEqual(finding.File, children).ShouldBeTrue();
    }

    private static async Task<string> PlantAsync(
        string common, RepositoryChildJournal.ChildRecord record, DateTimeOffset written, CancellationToken ct)
    {
        var children = Path.Combine(common, "antiphon", "children");
        Directory.CreateDirectory(children);
        var path = Path.Combine(children, Guid.NewGuid().ToString("N") + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record), ct);
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }
}

/// <summary>CARD-0726 MS-2: liveness cannot be read, so a record must not be treated as alive.</summary>
internal sealed class UnreadableLivenessGit : LandingGit, ILandingGit
{
    public new Task<bool?> IsProcessAliveAsync(int processId, long startTicks, CancellationToken ct) =>
        Task.FromResult<bool?>(null);
}
