using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandWarmArtifactsTests
{
    [Test]
    public async Task C1380_WarmPathIsReusedAcrossTwoRebasedLands()
    {
        await using var h = new LandingSafetyHarness();
        var compiler = new Compiler();
        h.ConfigureServices = services => services.AddSingleton<ILandingVerifier>(
            new LandingVerifier { RunProcess = compiler.RunAsync });
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await h.Fixture.PushIndependentAsync("advanced-target");
        var pushes = 0;
        h.Fixture.Git.BeforeCommand = async (_, args) =>
        {
            if (args[0] == "push" && pushes++ == 0)
                await h.Fixture.PushIndependentAsync("second-target");
            return null;
        };

        await h.RequestAsync();
        await h.RunQueuedAsync();

        await using var db = h.CreateContext();
        var operations = await db.AgentTaskLandings.Where(o => o.TaskId == h.Fixture.TaskId)
            .OrderBy(o => o.CreatedAt).ToListAsync();
        operations.Count.ShouldBe(2);
        operations.ShouldAllBe(o => o.VerificationPassed);
        operations[1].Publication.ShouldBe(LandPublicationOutcome.Landed);
        compiler.BuildPaths.Count.ShouldBe(2);
        compiler.BuildPaths[1].ShouldBe(compiler.BuildPaths[0],
            "successive rebased candidates in one repository must reuse owned artifacts");
        File.Exists(Path.Combine(compiler.BuildPaths[0], "owner.json")).ShouldBeTrue();
    }

    private sealed class Compiler
    {
        public List<string> BuildPaths { get; } = [];
        public List<string> ResultPaths { get; } = [];
        public bool BuildOk { get; set; } = true;
        public bool WriteReport { get; set; } = true;
        public Task<AgentTaskLandService.ProcessResult> RunAsync(string cwd, ILandingChildObserver? observer,
            CancellationToken ct, string file, string[] args)
        {
            ct.ThrowIfCancellationRequested();
            var output = args[Array.IndexOf(args, "--artifacts-path") + 1];
            if (args[0] == "build")
            {
                BuildPaths.Add(output);
                return Task.FromResult(new AgentTaskLandService.ProcessResult(BuildOk, "", BuildOk ? "" : "compiler failed"));
            }
            var resultsIndex = Array.IndexOf(args, "--results-directory");
            var results = resultsIndex < 0 ? output : args[resultsIndex + 1];
            ResultPaths.Add(results);
            if (WriteReport)
                File.WriteAllText(Path.Combine(results, "landing-verification.trx"),
                    """<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>""");
            return Task.FromResult(new AgentTaskLandService.ProcessResult(true, "", ""));
        }
    }
}
