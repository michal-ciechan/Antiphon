using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Antiphon.Server.Application.Services;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandVerifierTests
{
    [Test]
    public async Task C448_V35_RealVerifierPreservesPreExistingOutput()
    {
        using var fixture = new ScratchGitRepo("antiphon-land-canceled-verifier");
        var privateFile = Path.Combine(fixture.Path, "bin-land", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(privateFile)!);
        await File.WriteAllTextAsync(privateFile, "private bytes before canceled verification");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Exception? failure = null;
        try { await new Antiphon.Server.Infrastructure.Git.LandingVerifier().VerifyAsync(fixture.Path, "fixture-required", canceled.Token); }
        catch (Exception ex) { failure = ex; }
        File.Exists(privateFile).ShouldBeTrue("canceled verification cannot make pre-existing output disposable");
        File.ReadAllText(privateFile).ShouldBe("private bytes before canceled verification");
        failure.ShouldBeAssignableTo<OperationCanceledException>();
    }

    [Test]
    [Arguments("pass")]
    [Arguments("fail")]
    [Arguments("cancel")]
    [Arguments("throw")]
    public async Task C448_V32_VerifierOutcomeNeverMakesPrivateOutputDisposable(string outcome)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            (await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId)).LandVerifyFilter = "fixture-required";
            await db.SaveChangesAsync();
        }
        var privateFile = Path.Combine(h.Fixture.Source, "bin-land", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(privateFile)!);
        await File.WriteAllTextAsync(privateFile, "pre-existing private verifier bytes");
        using var canceled = new CancellationTokenSource();
        h.Verifier.Passed = outcome != "fail";
        if (outcome == "cancel") h.Verifier.Barrier = () =>
        { canceled.Cancel(); return Task.FromCanceled(canceled.Token); };
        if (outcome == "throw") h.Verifier.Barrier = () => Task.FromException(new VerifierFixtureFailure());
        Exception? failure = null;
        h.Fixture.Git.Trace.Clear();
        try { await h.RunAsync(canceled.Token); }
        catch (Exception ex) { failure = ex; }
        h.Verifier.Calls.ShouldBe(1);
        File.ReadAllText(privateFile).ShouldBe("pre-existing private verifier bytes");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("-d"));
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        if (outcome == "pass")
        {
            failure.ShouldBeNull();
            op.Publication.ShouldBe(LandPublicationOutcome.Landed);
            op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        }
        else
        {
            op.RemoteConfirmedAt.ShouldBeNull();
            op.VerifiedAt.ShouldBeNull();
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push");
            (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(h.Fixture.SeedSha);
            if (outcome == "cancel") failure.ShouldBeOfType<TaskCanceledException>();
            else if (outcome == "throw") failure.ShouldBeOfType<VerifierFixtureFailure>();
            else { failure.ShouldBeNull(); op.LastReason.ShouldBe("verification_failed"); }
        }
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(source);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    private sealed class VerifierFixtureFailure : Exception;

    [Test]
    [Arguments("SelectedPass", true)]
    [Arguments("SelectedFailure", false)]
    [Arguments("DoesNotExist", false)]
    public async Task C448_V34_RealTUnitSelectionRequiresExecutedPassingTests(string method, bool expected)
    {
        using var fixture = new ScratchGitRepo("antiphon-land-real-verifier");
        var project = Path.Combine(fixture.Path, "tests", "Antiphon.Tests");
        Directory.CreateDirectory(project);
        await File.WriteAllTextAsync(Path.Combine(fixture.Path, "Fixture.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>
            <EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup></Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(project, "Antiphon.Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net9.0</TargetFramework>
            <OutputType>Exe</OutputType><IsTestProject>true</IsTestProject>
            <EnableMicrosoftTestingPlatformRunner>true</EnableMicrosoftTestingPlatformRunner>
            <ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup>
            <PackageReference Include="TUnit" Version="1.44.0" /></ItemGroup></Project>
            """);
        var marker = Path.Combine(fixture.Path, "selected.txt");
        // A test outside the requested filter must never execute. A real selected method
        // writes a marker independently of the verifier's exit-code/TRX interpretation.
        await File.WriteAllTextAsync(Path.Combine(project, "Tests.cs"), $$"""
            using TUnit.Core;
            [Category("Integration")]
            public sealed class VerificationProbe
            {
                [Test] public void SelectedPass() => File.WriteAllText(@"{{marker}}", "selected");
                [Test] public void SelectedFailure() => throw new Exception("selected failure");
                [Test] public void NeverSelected() => throw new Exception("filter was ignored");
            }
            """);
        var protectedPath = Path.Combine(fixture.Path, "bin-land", "keep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedPath)!);
        await File.WriteAllTextAsync(protectedPath, "pre-existing private bytes");
        var result = await new Antiphon.Server.Infrastructure.Git.LandingVerifier().VerifyAsync(fixture.Path,
            $"/*/*/VerificationProbe/{method}", CancellationToken.None);
        result.Passed.ShouldBe(expected, result.Description);
        var common = await new Antiphon.Server.Infrastructure.Git.LandingGit().CommonDirectoryAsync(fixture.Path, CancellationToken.None);
        Directory.EnumerateFiles(Path.Combine(common, "antiphon", "children")).ShouldBeEmpty("both real verifier children must be acknowledged exited");
        File.Exists(marker).ShouldBe(expected);
        if (expected) result.Description.ShouldContain("tests 1/1");
        File.Exists(protectedPath).ShouldBeTrue("verification success or failure cannot authorize deletion of pre-existing output");
        (await File.ReadAllTextAsync(protectedPath)).ShouldBe("pre-existing private bytes");
        Directory.Exists(Path.Combine(fixture.Path, "obj")).ShouldBeFalse();
        Directory.Exists(Path.Combine(project, "obj")).ShouldBeFalse();
    }
}
