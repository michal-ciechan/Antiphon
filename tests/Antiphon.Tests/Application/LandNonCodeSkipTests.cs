using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
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

    // Copy real audited build entrypoints into the scratch repo; do not inject a policy verdict.
    private static async Task SeedGraphAsync(LandingSafetyHarness h)
    {
        var root = DelegateScriptRunner.RepoRoot;
        var paths = (await h.Fixture.RequiredAsync(root, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var path in paths)
        {
            var name = Path.GetFileName(path);
            var extension = Path.GetExtension(path);
            if (!(extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
                || new[] { ".props", ".targets", ".sln", ".slnx", ".slnf", ".rsp", ".ps1", ".sh", ".cmd", ".bat", ".py" }.Contains(extension.ToLowerInvariant())
                || name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase)
                || new[] { "global.json", "nuget.config", "packages.lock.json", "packages.config", "package.json", "package-lock.json", "yarn.lock", "pnpm-lock.yaml", ".gitmodules", ".gitattributes", "dotnet-tools.json" }.Contains(name.ToLowerInvariant()))) continue;
            var destination = Path.Combine(h.Fixture.Repository, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(root, path), destination);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(destination, File.GetUnixFileMode(Path.Combine(root, path)));
        }
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "add", ".");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "-m", "audited build graph");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", "master");
        await h.Fixture.RequiredAsync(h.Fixture.Source, "rebase", "master");
    }
}
