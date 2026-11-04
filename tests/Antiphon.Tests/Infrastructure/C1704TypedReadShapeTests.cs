using System.Reflection;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1704 S1 (T-0..T-3b): the nine typed reads issue exactly today's argv, go through the virtual
/// <see cref="LandingGit.RunAsync(string, IReadOnlyList{string}, CancellationToken)"/>, no implementor hides them,
/// and a real harness land issues the same git trace as the base.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class C1704TypedReadShapeTests
{
    private static readonly string[] TypedReads =
    [
        nameof(ILandingGit.ReadCommitAsync), nameof(ILandingGit.ReadAncestryAsync), nameof(ILandingGit.ReadRefAsync),
        nameof(ILandingGit.ReadSymbolicRefAsync), nameof(ILandingGit.ReadGitDirectoryAsync), nameof(ILandingGit.ReadConfigAsync),
        nameof(ILandingGit.ReadTreeZAsync), nameof(ILandingGit.ReadDiffTreeZAsync), nameof(ILandingGit.ReadWorktreeStatusAsync),
    ];

    /// <summary>T-0 (G-3): every implementor answers each typed read through the interface default or LandingGit's own.</summary>
    [Test]
    [Timeout(15_000)]
    public async Task Typed_reads_map_to_the_interface_or_LandingGit(CancellationToken ct)
    {
        var assemblies = new[] { typeof(LandingGit).Assembly, typeof(C1704TypedReadShapeTests).Assembly };
        var implementors = assemblies.SelectMany(LoadableTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && typeof(ILandingGit).IsAssignableFrom(t))
            .ToList();
        // LandingGit, TaskProgressGit and the test doubles of this assembly (EvidenceGit is censused by the E2E class).
        implementors.Count.ShouldBeGreaterThanOrEqualTo(51, string.Join(", ", implementors.Select(t => t.Name)));
        implementors.ShouldContain(typeof(TaskProgressGit));
        var offenders = new List<string>();
        foreach (var type in implementors)
        {
            var map = type.GetInterfaceMap(typeof(ILandingGit));
            foreach (var name in TypedReads)
            {
                var index = Array.FindIndex(map.InterfaceMethods, m => m.Name == name);
                index.ShouldBeGreaterThanOrEqualTo(0, name);
                var target = map.TargetMethods[index].DeclaringType;
                var expected = typeof(LandingGit).IsAssignableFrom(type) ? typeof(LandingGit) : typeof(ILandingGit);
                if (target != expected) offenders.Add($"{type.FullName}.{name} -> {target?.FullName}");
            }
        }
        offenders.ShouldBeEmpty("a typed read hidden or implicitly implemented by a double answers through other code");
        await Task.CompletedTask;
    }

    private static IEnumerable<Type> LoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }

    /// <summary>T-1 (G-1): each interface default issues today's exact argv and parses git's exits.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("commit")]
    [Arguments("ancestry")]
    [Arguments("ref")]
    [Arguments("symbolic-ref")]
    [Arguments("git-dir")]
    [Arguments("config")]
    [Arguments("tree")]
    [Arguments("diff-tree")]
    [Arguments("status")]
    public async Task Default_reads_issue_todays_argv(string method, CancellationToken ct)
    {
        var git = new DirectFake();
        foreach (var (exit, output) in new[] { (0, C1704Reads.Oid + "\n"), (1, ""), (2, ""), (128, "") })
        {
            git.Calls.Clear();
            git.Next = new LandingGitResult(exit, output, exit == 0 ? "" : $"git_exit_{exit}");
            var read = await C1704Reads.CallAsync(git, method, ct);
            git.Calls.Count.ShouldBe(1, method);
            git.Calls[0].ShouldBe(C1704Reads.ExpectedArgv(method), method);
            C1704Reads.ResultOf(read).ShouldBeSameAs(git.Next, "the record carries git's own result");
            AssertParse(method, read, exit, output);
        }
    }

    private static void AssertParse(string method, object read, int exit, string output)
    {
        switch (read)
        {
            case GitCommitRead commit: commit.Sha.ShouldBe(exit == 0 ? C1704Reads.Oid : null); break;
            case GitAncestryRead ancestry: ancestry.IsAncestor.ShouldBe(exit switch { 0 => true, 1 => false, _ => (bool?)null }); break;
            case GitRefRead reference:
                reference.State.ShouldBe(exit switch { 0 => GitRefState.Exists, 2 => GitRefState.Missing, _ => GitRefState.Error }); break;
            case GitSymbolicRefRead symbolic:
                symbolic.Target.ShouldBe(exit == 0 ? C1704Reads.Oid : null);
                symbolic.NotSymbolic.ShouldBe(exit == 1); break;
            case GitDirectoryRead directory: directory.Path.ShouldBe(exit == 0 ? C1704Reads.Oid : null); break;
            case GitConfigRead config:
                config.Value.ShouldBe(exit == 0 ? C1704Reads.Oid : null);
                config.Missing.ShouldBe(exit == 1); break;
            case GitListingRead listing: listing.Output.ShouldBe(exit == 0 ? output : null, "a -z listing is never trimmed"); break;
            default: throw new InvalidOperationException(method + " returned " + read.GetType().Name);
        }
    }

    /// <summary>T-2 (G-2): LandingGit's typed reads reach the virtual RunAsync with the same argv and start no process.</summary>
    [Test]
    [Timeout(15_000)]
    [Arguments("commit")]
    [Arguments("ancestry")]
    [Arguments("ref")]
    [Arguments("symbolic-ref")]
    [Arguments("git-dir")]
    [Arguments("config")]
    [Arguments("tree")]
    [Arguments("diff-tree")]
    [Arguments("status")]
    public async Task LandingGit_reads_go_through_virtual_RunAsync(string method, CancellationToken ct)
    {
        var git = new RecordingGit();
        await C1704Reads.CallAsync(git, method, ct);
        git.Calls.Count.ShouldBe(1, method);
        git.Calls[0].ShouldBe(C1704Reads.ExpectedArgv(method), method);
    }

    /// <summary>T-3 (G-4, G-5): a real harness land issues the base's git trace (reads as a multiset, mutations in order and channel).</summary>
    [Test]
    [Timeout(180_000)]
    public async Task Harness_land_trace_equals_the_base_golden(CancellationToken ct)
    {
        await using var land = new LandingSafetyHarness();
        await land.InitializeAsync();
        await land.AddSourceAsync();
        ClearTrace(land.Fixture);
        var result = await land.RunAsync();
        result.ShouldBe(LandRunResult.Complete);
        var actual = C1704LandTrace.Normalise(land.Fixture);
        if (!File.Exists(C1704LandTrace.GoldenPath))
        {
            var recorded = Path.Combine(Path.GetTempPath(), $"c1704-land-trace-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(recorded, actual, ct);
            throw new InvalidOperationException("c1704_golden_missing recorded=" + recorded);
        }
        var golden = (await File.ReadAllLinesAsync(C1704LandTrace.GoldenPath, ct)).Where(l => l.Length > 0).ToList();
        var difference = C1704LandTrace.Compare(golden, actual);
        difference.ShouldBeNull();
    }

    /// <summary>T-3b (G-32): the land's profile line names the backend and the S1 counters, and processes stay git processes.</summary>
    [Test]
    [Timeout(180_000)]
    public async Task Harness_land_profile_line_names_the_backend(CancellationToken ct)
    {
        var log = new C1704Log();
        await using var land = new LandingSafetyHarness { Logger = log.For<AgentTaskLandService>() };
        await land.InitializeAsync();
        await land.AddSourceAsync();
        ClearTrace(land.Fixture);
        await land.RunAsync();
        var line = log.Entries.Select(e => e.Message).Single(m => m.StartsWith("Land git profile", StringComparison.Ordinal));
        line.ShouldContain(" backend=Cli ");
        line.ShouldContain(" fallbacks=0 ");
        line.ShouldContain(" mismatches=0 ");
        line.ShouldMatch(@" inProcess=\d+ ");
        var processes = long.Parse(System.Text.RegularExpressions.Regex.Match(line, @" processes=(\d+) ").Groups[1].Value);
        int started;
        lock (land.Fixture.Git.Started)
            started = land.Fixture.Git.Started.Count;
        Console.WriteLine($"c1704 profile processes={processes} started={started} line={line}");
        processes.ShouldBeGreaterThan(0);
        processes.ShouldBeLessThanOrEqualTo(started, "the profile counts only git processes the land started");
    }

    private static void ClearTrace(LandingGitFixture fixture)
    {
        fixture.Git.Trace.Clear();
        fixture.Git.Commands.Clear();
        fixture.Git.Channels.Clear();
        lock (fixture.Git.Started) fixture.Git.Started.Clear();
    }
}
