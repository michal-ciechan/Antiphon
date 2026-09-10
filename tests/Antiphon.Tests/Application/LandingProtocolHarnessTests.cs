using System.Text.RegularExpressions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class LandingProtocolHarnessTests
{
    private static readonly Regex Arguments = new(@"\[Arguments\((.*)\)\]", RegexOptions.Compiled);
    [Test]
    public void C475_CoverageAllocationMatchesTheLegacyTupleManifest()
    {
        C475LegacyLandTuples.All.Count.ShouldBe(169);
        var expected = C475LegacyLandTuples.All.Where(r => r.Layer == "controlled")
            .Concat(C475LegacyLandTuples.RealCapstones).ToList();
        expected.Count.ShouldBe(200);
        expected.Count(r => r.Layer == "controlled").ShouldBe(140);
        C475LegacyLandTuples.RealCapstones.Count.ShouldBe(60);
        C475LegacyLandTuples.All.Select(r => (r.OriginalClass, r.OriginalMethod, r.Arguments)).Distinct().Count().ShouldBe(169);
        foreach (var group in expected.GroupBy(r => r.DestinationClass))
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "Application", group.Key + ".cs"));
            var actualMethods = Regex.Matches(source, @"public async Task (C448_\w+)\(")
                .Select(m => m.Groups[1].Value).ToList();
            actualMethods.Order().ShouldBe(group.Select(r => r.DestinationMethod).Distinct().Order());
            foreach (var method in actualMethods)
                AttributeTuples(group.Key + ".cs", method).Order().ShouldBe(
                    group.Where(r => r.DestinationMethod == method).Select(r => r.Arguments).Order());
        }
    }

    [Test]
    public async Task C475_AllServicesUseTheRegisteredGit()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        ReferenceEquals(h.RegisteredGit, h.Git).ShouldBeTrue();
        await using var scope = h.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Infrastructure.Data.AppDbContext>();
        var land = h.CreateLand(db, scope.ServiceProvider);
        var protocol = scope.ServiceProvider.GetRequiredService<Antiphon.Server.Application.Services.AgentTaskLandingProtocol>();
        var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        ReferenceEquals(land.GetType().GetField("_landingGit", fields)!.GetValue(land), h.Git).ShouldBeTrue();
        var gitField = protocol.GetType().GetFields(fields).Single(f => f.FieldType == typeof(ILandingGit));
        ReferenceEquals(gitField.GetValue(protocol), h.Git).ShouldBeTrue();
        await h.AddSourceAsync();
        (await h.RunAsync()).ShouldBe(Antiphon.Server.Application.Services.LandRunResult.Complete);
        h.Git.Trace.ShouldContain(a => a[0] == "push");
        h.Git.Trace.ShouldContain(a => a.Contains("remove"));
        h.Git.NativeProcessStarts.ShouldBe(0);
        h.Git.OwnedTrace.ShouldContain(a => a[0] == "push");
        Console.WriteLine("C475_MODEL_TRACE:" + System.Text.Json.JsonSerializer.Serialize(new
        {
            model = h.RegisteredGit.GetType().FullName, commands = h.Git.Trace, owned = h.Git.OwnedTrace,
            verifier = h.Verifier.GetType().FullName, verifierCalls = h.Verifier.Calls,
        }));
    }

    [Test]
    public async Task C475_AcknowledgedHookReadsCommittedPhase()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        LandPhase? seen = null;
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase != LandPhase.Prepared) return;
            await using var db = h.CreateContext();
            var op = await db.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.TaskId == h.Git.TaskId && o.Active);
            seen = op.Phase;
        };
        await h.RunAsync();
        seen.ShouldBe(LandPhase.Prepared);
    }

    [Test]
    public async Task C475_SourceAliasContendsOnRepositoryLease()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        await using var first = await leases.TryAcquireAsync(h.Git.Repository, CancellationToken.None);
        first.ShouldNotBeNull();
        (await leases.TryAcquireAsync(h.Git.Source, CancellationToken.None)).ShouldBeNull();
        leases.Owns(first, await h.Git.CommonDirectoryAsync(h.Git.Source, CancellationToken.None)).ShouldBeTrue();
    }

    [Test]
    public async Task C475_CleanupUsesGuardedRemoval()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Git.Source, ".antiphon", "owned.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "keep");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        File.ReadAllText(sentinel).ShouldBe("keep");
        Directory.Exists(h.Git.Source).ShouldBeTrue();
    }

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public async Task C475_RecreationPreservesCommittedModeAndAttempts(string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await PrepareModeAsync(h, mode);
        var before = await h.OperationAsync();
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Git.TaskId);
        await h.RestartServicesAsync();
        var recreated = await h.OperationAsync();
        (recreated?.Id).ShouldBe(before?.Id);
        var retained = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Git.TaskId);
        retained.LandAttempt.ShouldBe(task.LandAttempt);
        retained.LandRequestedAt.ShouldBe(task.LandRequestedAt);
        if (mode == "CleanupRetry") File.Delete(Path.Combine(h.Git.Source, ".antiphon", "report.md"));
        (await h.RunAsync()).ShouldBe(Antiphon.Server.Application.Services.LandRunResult.Complete);
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        if (before is not null) after.Id.ShouldBe(before.Id);
        (await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.Git.TaskId)).LandAttempt.ShouldBe(task.LandAttempt + 1);
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete);
    }

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public async Task C475_ModesAreReachedThroughProtocol(string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await PrepareModeAsync(h, mode);
        if (mode == "CleanupRetry") File.Delete(Path.Combine(h.Git.Source, ".antiphon", "report.md"));
        var phases = new List<LandPhase>();
        h.Fault.AfterAcknowledged = phase => { phases.Add(phase); return Task.CompletedTask; };
        (await h.RunAsync()).ShouldBe(Antiphon.Server.Application.Services.LandRunResult.Complete);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new Antiphon.Server.Application.Services.AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        if (mode == "Fresh")
        {
            phases.ShouldContain(LandPhase.Prepared);
            phases.ShouldContain(LandPhase.LocalTargetAdvanced);
            h.Git.Trace.ShouldContain(a => a[0] == "push");
        }
        if (mode == "AlreadyPresent") h.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase"));
        if (mode == "CleanupRetry") h.Git.Trace.ShouldNotContain(a => a[0] == "push");
    }

    [Test]
    public async Task C475_CleanupRetryRequiresFreshRemoteContainment()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "preserve");
        await h.RunAsync();
        await h.RepostAsync();
        h.Git.RewriteRemoteAwayFromSource();
        h.Git.Trace.Clear();
        await h.RunAsync();
        h.Git.Trace.ShouldContain(a => a[0] == "fetch");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe("remote_no_longer_contains_source");
        File.Exists(sentinel).ShouldBeTrue();
    }

    [Test]
    [Arguments("Fresh")]
    [Arguments("AlreadyPresent")]
    [Arguments("ResumePublication")]
    [Arguments("CleanupRetry")]
    public async Task C475_LandingKeepsLeaseThroughProtocol(string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await PrepareModeAsync(h, mode);
        var leases = h.Services.GetRequiredService<IRepositoryMutationLease>();
        var acquired = new List<bool>();
        var commonQueries = 0;
        bool? entryAcquired = null;
        var probing = false;
        h.Git.BeforeCommonDirectory = async () =>
        {
            // This isolated fixture has no other task/claim: query one acquires the
            // service lease, query two is the protocol's first query, before Owns.
            if (probing || ++commonQueries != 2) return;
            probing = true;
            try
            {
                await using var contender = await leases.TryAcquireAsync(h.Git.Repository, CancellationToken.None);
                entryAcquired = contender is not null;
            }
            finally { probing = false; }
        };
        h.Git.BeforeInspection = async () =>
        {
            await using var contender = await leases.TryAcquireAsync(h.Git.Repository, CancellationToken.None);
            acquired.Add(contender is not null);
        };
        if (mode == "CleanupRetry") File.Delete(Path.Combine(h.Git.Source, ".antiphon", "report.md"));
        Exception? failure = null;
        try { await h.RunAsync(); } catch (Exception ex) { failure = ex; }
        entryAcquired.ShouldBe(false, "the first protocol query must still hold the service lease");
        acquired.Count.ShouldBeGreaterThanOrEqualTo(2, "admission and an in-protocol inspection must both be observed");
        acquired.ShouldAllBe(value => !value);
        failure.ShouldBeNull();
    }

    private static async Task PrepareModeAsync(LandingProtocolHarness h, string mode)
    {
        if (mode != "AlreadyPresent") await h.AddSourceAsync();
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.LocalTargetAdvanced;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
            h.Fault.Triggered.ShouldBeTrue();
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            op.Phase.ShouldBe(LandPhase.LocalTargetAdvanced);
            op.RemoteConfirmedAt.ShouldBeNull();
        }
        if (mode == "CleanupRetry")
        {
            var sentinel = Path.Combine(h.Git.Source, ".antiphon", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            await File.WriteAllTextAsync(sentinel, "preserve");
            var phases = new List<LandPhase>();
            h.Fault.AfterAcknowledged = phase => { phases.Add(phase); return Task.CompletedTask; };
            await h.RunAsync();
            phases.ShouldContain(LandPhase.Prepared);
            phases.ShouldContain(LandPhase.LocalTargetAdvanced);
            h.Git.Trace.ShouldContain(a => a[0] == "push");
            var op = (await h.OperationAsync()).ShouldNotBeNull();
            op.RemoteConfirmedAt.ShouldNotBeNull();
            op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
            File.ReadAllText(sentinel).ShouldBe("preserve");
            await h.RepostAsync();
            (await h.OperationAsync())!.Id.ShouldBe(op.Id);
            h.Fault.AfterAcknowledged = null;
        }
        h.Git.Trace.Clear();
    }

    private static List<string> AttributeTuples(string file, string method)
    {
        var path = Path.Combine(RepoRoot, "tests", "Antiphon.Tests", "Application", file);
        var lines = File.ReadAllLines(path);
        var tuples = new List<string>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("public async Task " + method, StringComparison.Ordinal)) continue;
            for (var j = i - 1; j >= 0; j--)
            {
                var m = Arguments.Match(lines[j]);
                if (m.Success)
                {
                    tuples.Insert(0, Normalize(m.Groups[1].Value));
                    continue;
                }
                if (lines[j].Contains("[Test]", StringComparison.Ordinal)) break;
            }
            if (tuples.Count == 0) tuples.Add("");
            break;
        }
        return tuples;
    }

    private static string Normalize(string raw) =>
        raw.Replace("\"", "").Replace(" ", "").Replace("false", "False").Replace("true", "True");

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
        }
    }
}
