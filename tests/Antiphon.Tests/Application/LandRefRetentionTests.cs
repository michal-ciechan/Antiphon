using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-1432 S2 (R2, V-3..V-5): a land deletes its task's land and progress refs at the source.</summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRefRetentionTests
{
    [Test]
    public async Task C1432_completed_land_deletes_task_land_and_progress_refs()
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var task = h.Git.TaskId;
        var other = Guid.NewGuid();
        var sha = h.Git.SeedSha;
        string[] own =
        [
            $"refs/antiphon/land/{task:N}/{Guid.NewGuid():N}/source-observed/{Guid.NewGuid():N}", // request-id subtree
            $"refs/antiphon/land/{task:N}/{Guid.NewGuid():N}/mirror-observed", // mirror subtree
            $"refs/antiphon/progress/{task:N}/baseline-1",
            $"refs/antiphon/progress/{task:N}/observe-2",
        ];
        string[] foreign =
        [
            $"refs/antiphon/land/{other:N}/{Guid.NewGuid():N}/source",
            $"refs/antiphon/progress/{other:N}/baseline-1",
            "refs/heads/feat/c1432-keep",
        ];
        foreach (var name in own.Concat(foreign)) h.Git.SetPin(name, sha);

        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.Landed);
        h.Git.Trace.ShouldContain(a => a[0] == "update-ref" && a.Any(x => x.StartsWith($"refs/antiphon/land/{task:N}/", StringComparison.Ordinal)),
            "the land pinned its own recovery refs");
        h.Git.Refs.Keys.Where(k => k.StartsWith($"refs/antiphon/land/{task:N}/", StringComparison.Ordinal)
                || k.StartsWith($"refs/antiphon/progress/{task:N}/", StringComparison.Ordinal))
            .ShouldBeEmpty("land refs still present after a completed land of an unprotected task");
        foreach (var name in foreign) h.Git.Refs[name].ShouldBe(sha, "another task's refs are untouched");
    }
}
