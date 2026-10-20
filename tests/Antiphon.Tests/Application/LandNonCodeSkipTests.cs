using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandNonCodeSkipTests
{
    [Test]
    public async Task C1380_DocsOnlyRebasedLandRecordsNonCodeSkip()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await SeedGraphAsync(h);
        await h.AddDocsOnlySourceAsync();
        var target = await h.Fixture.PushIndependentAsync("advanced-target");
        await h.RequestAsync();
        await h.RunQueuedAsync();

        h.Verifier.Calls.ShouldBe(0, "a docs-only rebased candidate must not invoke the verifier or its build slot");
        var op = (await h.OperationAsync())!;
        op.TargetBeforeSha.ShouldBe(target);
        op.RebasedSourceSha.ShouldNotBe(op.OriginalSourceSha);
        op.VerificationPassed.ShouldBeFalse();
        op.VerificationSkipReason.ShouldBe("non-code");
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        await using var db = h.CreateContext();
        var row = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == h.Fixture.TaskId && o.Stage == OrchestrationStage.Verify);
        row.Outcome.ShouldBe(StageOutcomeKind.Skipped);
        row.Detail.ShouldBe("non-code");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(op.RebasedSourceSha);
    }

    [Test]
    public async Task C1380_CSharpChangeBuilds()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "server/Feature.cs", "public class Feature {}");
        await LandAndAssertAsync(h, build: true);
    }

    [Test]
    public async Task C1380_FailedBuildRefusesPublication()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "server/Broken.cs", "not valid C#");
        var target = await h.Fixture.PushIndependentAsync("advanced-target");
        h.Verifier.Passed = false;
        h.Fixture.Git.Trace.Clear();
        await h.RequestAsync();
        await h.RunQueuedAsync();
        h.Verifier.Calls.ShouldBe(1);
        var op = (await h.OperationAsync())!;
        op.VerificationPassed.ShouldBeFalse();
        op.VerificationSkipReason.ShouldBeNull();
        op.LastReason.ShouldBe("verification_failed");
        op.Publication.ShouldBe(LandPublicationOutcome.Refused);
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim().ShouldBe(target);
        await using var db = h.CreateContext();
        var verify = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == h.Fixture.TaskId && o.Stage == OrchestrationStage.Verify);
        verify.Outcome.ShouldBe(StageOutcomeKind.Found);
        verify.Detail.ShouldContain("build failed");
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push");
    }

    [Test]
    [Arguments("server/Bundles/new.md")]
    [Arguments("docs/data.json")]
    [Arguments("tests/Antiphon.Tests/fixture.md")]
    [Arguments("tools/Example/fixture.md")]
    [Arguments("notes.md")]
    [Arguments("client/src/index.ts")]
    [Arguments("new.csproj")]
    [Arguments("new.props")]
    [Arguments("new.targets")]
    [Arguments("new.sln")]
    [Arguments("Directory.Build.rsp")]
    [Arguments("global.json")]
    [Arguments("packages.lock.json")]
    [Arguments("NuGet.config")]
    [Arguments("package-lock.json")]
    [Arguments("scripts/new.ps1")]
    public async Task C1380_NonAllowlistedInputsBuild(string path)
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, path, "changed build input");
        await LandAndAssertAsync(h, build: true);
    }

    [Test]
    [Arguments("add")]
    [Arguments("edit")]
    [Arguments("delete")]
    [Arguments("rename")]
    [Arguments("README")]
    [Arguments("empty")]
    public async Task C1380_RegularDocumentationChangesSkip(string variant)
    {
        await using var h = await CreateAsync();
        if (variant is "edit" or "delete" or "rename")
        {
            await h.Fixture.PushIndependentAsync("existing-doc", "docs/existing.md", "original documentation\n");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "fetch", "origin", "master");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "FETCH_HEAD");
        }
        switch (variant)
        {
            case "delete": File.Delete(Path.Combine(h.Fixture.Source, "docs/existing.md")); break;
            case "rename":
                await h.Fixture.RequiredAsync(h.Fixture.Source, "mv", "docs/existing.md", "docs/renamed.md"); break;
            case "empty": break;
            default:
                var path = variant == "README" ? "README.md" : variant == "edit" ? "docs/existing.md" : "docs/new.md";
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(h.Fixture.Source, path))!);
                await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, path), "updated documentation\n");
                break;
        }
        await CommitAsync(h);
        await LandAndAssertAsync(h, build: false);
    }

    [Test]
    [Arguments("into-docs")]
    [Arguments("out-of-docs")]
    [Arguments("symlink")]
    [Arguments("mode")]
    public async Task C1380_RenameBothNamesAndFileModesAreChecked(string variant)
    {
        await using var h = await CreateAsync();
        var original = variant == "into-docs" ? "feature.txt" : "docs/existing.md";
        await h.Fixture.PushIndependentAsync("existing-file", original, "unchanged content\n");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "fetch", "origin", "master");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "FETCH_HEAD");
        Directory.CreateDirectory(Path.Combine(h.Fixture.Source, "docs"));
        if (variant == "into-docs")
            await h.Fixture.RequiredAsync(h.Fixture.Source, "mv", original, "docs/renamed.md");
        else if (variant == "out-of-docs")
            await h.Fixture.RequiredAsync(h.Fixture.Source, "mv", original, "feature.txt");
        else if (variant == "symlink")
        {
            var path = Path.Combine(h.Fixture.Source, original);
            File.Delete(path);
            File.CreateSymbolicLink(path, "../keep.txt");
        }
        else
        {
            // core.fileMode=false keeps the checkout portable while the committed mode is explicit.
            await h.Fixture.RequiredAsync(h.Fixture.Source, "config", "core.fileMode", "false");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "update-index", "--chmod=+x", original);
        }
        await CommitAsync(h);
        await LandAndAssertAsync(h, build: true);
    }

    [Test]
    [Arguments("force")]
    [Arguments("filter")]
    [Arguments("rc")]
    public async Task C1380_OverridesAndReleaseTargetsBuild(string variant)
    {
        await using var h = await CreateAsync(force: variant == "force");
        var target = "refs/heads/master";
        if (variant == "rc")
        {
            target = "refs/heads/rc/fixture";
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "branch", "rc/fixture", "master");
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "master:" + target);
            await h.SetRoleAsync(AgentTaskRole.Code, target);
        }
        await h.AddDocsOnlySourceAsync();
        await LandAndAssertAsync(h, build: true, filter: variant == "filter" ? "/*/*/Selected/*" : null, target: target);
        if (variant == "filter") h.Verifier.Invocations.Single().Filter.ShouldBe("/*/*/Selected/*");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C1380_TargetGraphDriftAndNewProjectsBuild(bool newProject)
    {
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        var path = newProject ? "server/extra.csproj" : "server/Antiphon.Server.csproj";
        await h.Fixture.PushIndependentAsync("graph-drift", path,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><EmbeddedResource Include=\"../docs/**/*.md\" /></ItemGroup></Project>");
        await LandAndAssertAsync(h, build: true);
        (await h.OperationAsync())!.VerificationCommand.ShouldNotBeNull().ShouldContain("unrecognized-build-graph");
    }

    [Test]
    public async Task C1380_UnknownRepositoryBuilds()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddDocsOnlySourceAsync();
        await LandAndAssertAsync(h, build: true);
        (await h.OperationAsync())!.VerificationCommand.ShouldNotBeNull().ShouldContain("unrecognized-build-graph");
    }

    [Test]
    [Arguments("failed")]
    [Arguments("truncated")]
    [Arguments("malformed")]
    [Arguments("graph-failed")]
    public async Task C1380_UnreadableOrMalformedGitCannotSkip(string variant)
    {
        await using var h = await CreateAsync();
        await h.AddDocsOnlySourceAsync();
        var intercepted = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == (variant == "graph-failed" ? "ls-tree" : "diff-tree"))
            {
                intercepted = true;
                return Task.FromResult<LandingGitResult?>(variant switch
                {
                    "truncated" => new(0, ":000000 100644 " + new string('0', 40) + " " + new string('a', 40) + " A\0docs/new.md", ""),
                    "malformed" => new(0, "invalid\0docs/new.md\0", ""),
                    _ => new(1, "", "unreadable")
                });
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        await LandAndAssertAsync(h, build: true);
        intercepted.ShouldBeTrue();
    }

    [Test]
    public async Task C1380_UnchangedBaseStillBuildsCode()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "Feature.cs", "public class Feature {}");
        await LandAndAssertAsync(h, build: true);
        var op = (await h.OperationAsync())!;
        op.RebasedSourceSha.ShouldBe(op.OriginalSourceSha);
    }

    [Test]
    public async Task C1380_TargetContainedCodeIsExcludedFromTheActualRebasedDiff()
    {
        await using var h = await CreateAsync();
        await ChangeAsync(h, "Feature.cs", "public class Feature {}");
        var code = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        await h.AddDocsOnlySourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", code + ":refs/heads/master");
        await h.Fixture.PushIndependentAsync("advanced-target");
        await LandAndAssertAsync(h, build: false);
        var op = (await h.OperationAsync())!;
        h.Fixture.Git.Trace.ShouldContain(a => a[0] == "diff-tree"
            && a.Contains(op.TargetBeforeSha) && a.Contains(op.RebasedSourceSha!));
    }

    [Test]
    public async Task C1380_AlreadyPresentDoesNotPromiseANewBuildEvenWhenForced()
    {
        await using var h = new LandingSafetyHarness();
        h.ConfigureServices = services => services.AddSingleton<IOptions<LandingSettings>>(
            Options.Create(new LandingSettings { ForceVerificationBuild = true }));
        await h.InitializeAsync();
        await h.RequestAsync();
        await h.RunQueuedAsync();
        h.Verifier.Calls.ShouldBe(0);
        (await h.OperationAsync())!.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
    }

    private static async Task<LandingSafetyHarness> CreateAsync(bool force = false)
    {
        var h = new LandingSafetyHarness();
        h.ConfigureServices = services => services.AddSingleton<IOptions<LandingSettings>>(
            Options.Create(new LandingSettings { ForceVerificationBuild = force }));
        try
        {
            await h.InitializeAsync();
            await SeedGraphAsync(h);
            return h;
        }
        catch { await h.DisposeAsync(); throw; }
    }

    private static async Task ChangeAsync(LandingSafetyHarness h, string path, string content)
    {
        var full = Path.Combine(h.Fixture.Source, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content);
        await CommitAsync(h);
    }

    private static async Task CommitAsync(LandingSafetyHarness h)
    {
        await h.Fixture.RequiredAsync(h.Fixture.Source, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "candidate change");
    }

    private static async Task LandAndAssertAsync(LandingSafetyHarness h, bool build, string? filter = null,
        string target = "refs/heads/master")
    {
        await h.RequestAsync(filter);
        await h.RunQueuedAsync(filter);
        h.Verifier.Calls.ShouldBe(build ? 1 : 0);
        var op = (await h.OperationAsync())!;
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        op.VerificationPassed.ShouldBe(build);
        if (build) op.VerificationSkipReason.ShouldBeNull();
        else op.VerificationSkipReason.ShouldBe("non-code");
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", target)).Trim().ShouldBe(op.RebasedSourceSha);
        await using var db = h.CreateContext();
        var verify = await db.StageOutcomes.SingleAsync(o => o.SubjectTaskId == h.Fixture.TaskId && o.Stage == OrchestrationStage.Verify);
        verify.Outcome.ShouldBe(build ? StageOutcomeKind.Clean : StageOutcomeKind.Skipped);
        if (!build) verify.Detail.ShouldBe("non-code");
    }

    // Copy real audited build entrypoints into the scratch repo; do not inject a policy verdict.
    private static async Task SeedGraphAsync(LandingSafetyHarness h)
    {
        var root = DelegateScriptRunner.RepoRoot;
        var paths = (await h.Fixture.RequiredAsync(root, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var graphPaths = new List<string>();
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var extension = Path.GetExtension(path);
            if (!(extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                || new[] { ".props", ".targets", ".sln", ".slnx", ".slnf", ".rsp", ".ps1", ".sh", ".cmd", ".bat", ".py" }.Contains(extension.ToLowerInvariant())
                || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
                || new[] { "global.json", "nuget.config", "packages.lock.json", "packages.config", "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml", ".gitmodules", ".gitattributes", "dotnet-tools.json" }.Contains(name.ToLowerInvariant()))) continue;
            graphPaths.Add(path);
            var destination = Path.Combine(h.Fixture.Repository, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(root, path), destination);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(Path.Combine(root, path)));
        }
        // The real tracked inventory includes historical scripts beneath ignored .antiphon/.
        // Stage only these exact copied inputs, including ones the scratch fixture's ignore rules hide.
        await h.Fixture.RequiredAsync(h.Fixture.Repository, ["add", "-f", "--", .. graphPaths]);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "audited build graph");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "master");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "master");
    }
}
