using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;
using System.Runtime.InteropServices;

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
            Verifier(compiler));
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

    [Test]
    public async Task C1380_RepositoriesAndToolchainsHaveSeparateOwnedGenerations()
    {
        await using var f = new LandingGitFixture();
        var artifacts = new LandVerificationArtifacts();
        var first = artifacts.Prepare(Path.Combine(f.Root, "repo-a"), "sdk-a|linux-x64|Debug", true, _ => { });
        first.Complete();
        var reused = new LandVerificationArtifacts().Prepare(Path.Combine(f.Root, "repo-a"), "sdk-a|linux-x64|Debug", true, _ => { });
        reused.Path.ShouldBe(first.Path);
        reused.Complete();
        var sdk = artifacts.Prepare(Path.Combine(f.Root, "repo-a"), "sdk-b|linux-x64|Debug", true, _ => { });
        var repository = artifacts.Prepare(Path.Combine(f.Root, "repo-b"), "sdk-a|linux-x64|Debug", true, _ => { });
        sdk.Path.ShouldNotBe(first.Path);
        repository.Path.ShouldNotBe(first.Path);
        File.ReadAllText(Path.Combine(first.Path, "owner.json")).ShouldContain("repo-a");
        File.ReadAllText(Path.Combine(repository.Path, "owner.json")).ShouldContain("repo-b");
    }

    [Test]
    [Arguments("foreign")]
    [Arguments("link")]
    [Arguments("unmarked")]
    public async Task C1380_ForeignAndLinkedRootsAreRetained(string kind)
    {
        await using var f = new LandingGitFixture();
        var artifacts = new LandVerificationArtifacts();
        var common = Path.Combine(f.Root, "common");
        var root = artifacts.RootFor(common, "toolchain");
        Directory.CreateDirectory(root);
        var sentinel = Path.Combine(root, "sentinel");
        File.WriteAllText(sentinel, "keep");
        if (kind == "foreign") File.WriteAllText(Path.Combine(root, "owner.json"), "foreign marker");
        if (kind == "link")
        {
            Directory.Move(root, root + "-target");
            Directory.CreateSymbolicLink(root, root + "-target");
        }
        var diagnostics = new List<string>();
        var cold = artifacts.Prepare(common, "toolchain", true, diagnostics.Add);
        try
        {
            cold.Path.ShouldNotStartWith(root);
            diagnostics.ShouldContain(line => line.Contains("cold reason=unusable-owned-root"));
            File.ReadAllText(sentinel).ShouldBe("keep");
            if (kind == "foreign") File.ReadAllText(Path.Combine(root, "owner.json")).ShouldBe("foreign marker");
            File.Exists(Path.Combine(cold.Path, "owner.json")).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(cold.Path, true); // Exactly the fresh cold generation this test created.
            if (kind == "link") Directory.Delete(root);
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1380_LinkedCacheEntriesCannotBeReused(bool hardLink)
    {
        await using var f = new LandingGitFixture();
        var artifacts = new LandVerificationArtifacts();
        var common = Path.Combine(f.Root, "common");
        var first = artifacts.Prepare(common, "sdk", true, _ => { });
        first.Complete();
        var target = Path.Combine(f.Root, "foreign-file");
        File.WriteAllText(target, "retain");
        var link = Path.Combine(first.Path, "linked-output");
        if (!hardLink) File.CreateSymbolicLink(link, target);
        else if (OperatingSystem.IsWindows()) CreateHardLink(link, target, IntPtr.Zero).ShouldBeTrue();
        else NativeLink(target, link).ShouldBe(0);
        var cold = artifacts.Prepare(common, "sdk", true, _ => { });
        try
        {
            cold.Path.ShouldNotBe(first.Path);
            File.ReadAllText(target).ShouldBe("retain");
            File.Exists(link).ShouldBeTrue();
        }
        finally
        {
            File.Delete(link);
            Directory.Delete(cold.Path, true);
        }
    }

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int NativeLink(string existing, string link);
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string link, string existing, IntPtr security);

    [Test]
    public async Task C1380_StaleTrxCannotPassASecondRun()
    {
        await using var f = new LandingGitFixture();
        var compiler = new Compiler();
        var verifier = Verifier(compiler);
        var correlation = new LandingVerificationCorrelation(Guid.NewGuid(), Guid.NewGuid(), null,
            CommonDirectory: Path.Combine(f.Root, "common"));
        (await verifier.VerifyAsync(f.Root, "/*/*/Selected/*", correlation, CancellationToken.None)).Passed.ShouldBeTrue();
        compiler.WriteReport = false;
        var second = await verifier.VerifyAsync(f.Root, "/*/*/Selected/*", correlation, CancellationToken.None);
        second.Passed.ShouldBeFalse("the first run's retained TRX must not authorize the second run");
        compiler.BuildPaths[1].ShouldBe(compiler.BuildPaths[0]);
        compiler.ResultPaths.Distinct().Count().ShouldBe(2);
        File.Exists(Path.Combine(compiler.ResultPaths[0], "landing-verification.trx")).ShouldBeTrue();
        File.Exists(Path.Combine(compiler.ResultPaths[1], "landing-verification.trx")).ShouldBeFalse();
        compiler.TestArtifactPaths.ShouldBe(compiler.BuildPaths);
        compiler.ResultPaths.ShouldAllBe(p => File.Exists(Path.Combine(p, "owner.json")));
        compiler.WriteReport = true;
        (await verifier.VerifyAsync(f.Root, "/*/*/Selected/*", correlation, CancellationToken.None)).Passed.ShouldBeTrue();
        compiler.BuildPaths[2].ShouldNotBe(compiler.BuildPaths[1], "failed verification quarantines the generation");
    }

    [Test]
    public async Task C1380_ColdOptOutCreatesOwnedSeparateOutputs()
    {
        await using var f = new LandingGitFixture();
        var compiler = new Compiler();
        var verifier = Verifier(compiler, reuse: false);
        var correlation = new LandingVerificationCorrelation(Guid.NewGuid(), Guid.NewGuid(), null,
            CommonDirectory: Path.Combine(f.Root, "common"));
        try
        {
            (await verifier.VerifyAsync(f.Root, null, correlation, CancellationToken.None)).Passed.ShouldBeTrue();
            (await verifier.VerifyAsync(f.Root, null, correlation, CancellationToken.None)).Passed.ShouldBeTrue();
            compiler.BuildPaths.Distinct().Count().ShouldBe(2);
            compiler.BuildPaths.ShouldAllBe(p => File.Exists(Path.Combine(p, "owner.json")));
        }
        finally { foreach (var path in compiler.BuildPaths) Directory.Delete(path, true); }
    }

    [Test]
    public async Task C1380_ExecutionLockHoldsWithoutConsumingAnAttempt()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var provider = new LandExecutionLease(h.Fixture.Git);
        var other = new LandExecutionLease(h.Fixture.Git);
        await using (var held = await provider.TryAcquireAsync(h.Fixture.Repository, CancellationToken.None))
        {
            held.ShouldNotBeNull();
            (await other.TryAcquireAsync(h.Fixture.Source, CancellationToken.None)).ShouldBeNull();
            var requested = await h.RequestAsync();
            (await h.RunQueuedAsync()).ShouldBe(LandRunResult.Held);
            await using var db = h.CreateContext();
            var request = await db.AgentTaskLandRequests.SingleAsync(r => r.Id == requested.RequestId);
            request.Attempt.ShouldBe(0);
            request.HoldReasonCode.ShouldBe("land_execution_busy");
            (await db.AgentTaskLandings.CountAsync()).ShouldBe(0);
        }
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        (await h.OperationAsync())!.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        await using var reacquired = await other.TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        reacquired.ShouldNotBeNull();
    }

    [Test]
    public async Task C1380_FailedBuildRefusesPublicationAndReleasesExecutionLock()
    {
        await using var h = new LandingSafetyHarness();
        var compiler = new Compiler { BuildOk = false };
        h.ConfigureServices = services => services.AddSingleton<ILandingVerifier>(Verifier(compiler));
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var target = await h.Fixture.PushIndependentAsync("advanced-target");
        await h.RequestAsync();
        await h.RunQueuedAsync();
        var operation = (await h.OperationAsync())!;
        operation.VerificationPassed.ShouldBeFalse();
        operation.Publication.ShouldBe(LandPublicationOutcome.Refused);
        operation.LastReason.ShouldBe("verification_failed");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(target);
        File.ReadAllText(Path.Combine(compiler.BuildPaths.Single(), "state")).ShouldBe("building");
        await using var reacquired = await new LandExecutionLease(h.Fixture.Git)
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        reacquired.ShouldNotBeNull();
    }

    [Test]
    public async Task C1380_CancellationQuarantinesCacheAndReleasesBothLocks()
    {
        await using var h = new LandingSafetyHarness();
        using var cancellation = new CancellationTokenSource();
        var compiler = new Compiler { OnBuild = cancellation.Cancel };
        h.ConfigureServices = services => services.AddSingleton<ILandingVerifier>(Verifier(compiler));
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var target = await h.Fixture.PushIndependentAsync("advanced-target");
        await h.RequestAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => h.RunAsync(cancellation.Token));
        File.ReadAllText(Path.Combine(compiler.BuildPaths.Single(), "state")).ShouldBe("building");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(target);
        await using var execution = await new LandExecutionLease(h.Fixture.Git)
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        execution.ShouldNotBeNull();
        await using var mutation = await new RepositoryMutationLease(h.Fixture.Git)
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        mutation.ShouldNotBeNull();
        compiler.OnBuild = null;
        var common = await h.Fixture.Git.CommonDirectoryAsync(h.Fixture.Repository, CancellationToken.None);
        var verification = await Verifier(compiler).VerifyAsync(h.Fixture.Repository, null,
            new(Guid.NewGuid(), Guid.NewGuid(), null, CommonDirectory: common), CancellationToken.None);
        verification.Passed.ShouldBeTrue();
        compiler.BuildPaths[1].ShouldNotBe(compiler.BuildPaths[0]);
    }

    private static LandingVerifier Verifier(Compiler compiler, bool reuse = true) => new(
        artifacts: new LandVerificationArtifacts { SdkIdentity = "fixture-sdk" },
        settings: Options.Create(new LandingSettings { ReuseVerificationArtifacts = reuse }))
        { RunProcess = compiler.RunAsync };

    private sealed class Compiler
    {
        public List<string> BuildPaths { get; } = [];
        public List<string> ResultPaths { get; } = [];
        public List<string> TestArtifactPaths { get; } = [];
        public bool BuildOk { get; set; } = true;
        public bool WriteReport { get; set; } = true;
        public Action? OnBuild { get; set; }
        public Task<AgentTaskLandService.ProcessResult> RunAsync(string cwd, ILandingChildObserver? observer,
            CancellationToken ct, string file, string[] args)
        {
            ct.ThrowIfCancellationRequested();
            var output = args[Array.IndexOf(args, "--artifacts-path") + 1];
            if (args[0] == "build")
            {
                BuildPaths.Add(output);
                OnBuild?.Invoke();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(new AgentTaskLandService.ProcessResult(BuildOk, "", BuildOk ? "" : "compiler failed"));
            }
            var resultsIndex = Array.IndexOf(args, "--results-directory");
            var results = resultsIndex < 0 ? output : args[resultsIndex + 1];
            ResultPaths.Add(results);
            TestArtifactPaths.Add(output);
            if (WriteReport)
                File.WriteAllText(Path.Combine(results, "landing-verification.trx"),
                    """<TestRun><ResultSummary><Counters total="1" executed="1" passed="1" failed="0" /></ResultSummary></TestRun>""");
            return Task.FromResult(new AgentTaskLandService.ProcessResult(true, "", ""));
        }
    }
}
