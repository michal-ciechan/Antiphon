using Antiphon.Server.Application.Interfaces;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>
/// CARD-1432 S5 G1/G2 (CARD-1462 option A, slice S4): the land's per-boundary rechecks and recovery pins keep
/// every check and every refusal reason, in fewer git processes. Real temp repositories; the process counts
/// come from the operation-scope profile (CARD-1432 S4) and the fixture trace.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandingGitBatchingTests
{
    /// <summary>G1: three recovery pins and the source branch are read by one <c>show-ref --verify</c>.</summary>
    [Test]
    public async Task C1432_recheck_reads_pins_and_branch_in_one_process()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        ILandingGit git = fixture.Git;
        var prefix = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}";
        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "prepared");
        var prepared = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim();
        foreach (var (name, sha) in new[] { ("source", fixture.SeedSha), ("target-before", fixture.SeedSha), ("prepared", prepared) })
            (await git.PinAsync(fixture.Repository, prefix + "/" + name, sha, CancellationToken.None)).Succeeded.ShouldBeTrue(name);
        string[] refs = [prefix + "/source", prefix + "/target-before", prefix + "/prepared", fixture.SourceRef];

        fixture.Git.Trace.Clear();
        IReadOnlyDictionary<string, string> read;
        using (var scope = git.BeginOperationScope())
        {
            read = await git.ReadRefsAsync(fixture.Repository, refs, CancellationToken.None);
            var histogram = scope.Profile.Histogram;
            histogram.GetValueOrDefault("show-ref --verify").ShouldBe(1, "one recheck is one show-ref --verify, not one per pin plus one for the branch");
            scope.Profile.Processes.ShouldBe(1, "a recheck read spawns exactly one git process");
        }
        fixture.Git.Trace.Count(a => a[0] == "show-ref").ShouldBe(1);
        read.Count.ShouldBe(4);
        read[prefix + "/source"].ShouldBe(fixture.SeedSha);
        read[prefix + "/target-before"].ShouldBe(fixture.SeedSha);
        read[prefix + "/prepared"].ShouldBe(prepared);
        read[fixture.SourceRef].ShouldBe(prepared);

        // A missing ref ends the read there: everything before it is reported, nothing after it is guessed.
        await fixture.RequiredAsync(fixture.Repository, "update-ref", "-d", prefix + "/target-before");
        var partial = await git.ReadRefsAsync(fixture.Repository, refs, CancellationToken.None);
        partial.Keys.ShouldBe([prefix + "/source"]);
        await fixture.AssertRemoteSourceAsync();
    }

    /// <summary>A recovery pin deleted between boundaries still refuses <c>recovery_pin_changed</c> through the protocol.</summary>
    [Test]
    [Arguments("source")]
    [Arguments("prepared")]
    public async Task C1432_missing_pin_reports_recovery_pin_changed(string pin)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await SetVerifyFilterAsync(h);
        h.Verifier.Barrier = async () =>
        {
            var op = (await h.OperationAsync())!;
            await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "-d", op.RecoveryRefPrefix + "/" + pin);
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        (await h.OperationAsync())!.LastReason.ShouldBe("recovery_pin_changed");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        await h.Fixture.AssertRemoteSourceAsync();
    }

    /// <summary>
    /// The branch half of the batched read keeps today's reasons: a branch that moved during verification is
    /// <c>source_changed</c>; a branch deleted during verification is still <c>source_ref_missing</c>, classified
    /// by the single-ref read on the refusal path only.
    /// </summary>
    [Test]
    [Arguments("moved", "source_changed")]
    [Arguments("missing", "source_ref_missing")]
    public async Task C1432_moved_or_missing_branch_keeps_its_reason(string change, string reason)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await SetVerifyFilterAsync(h);
        h.Verifier.Barrier = async () =>
        {
            if (change == "moved")
                await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "--allow-empty", "-m", "moved during verification");
            else
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", "-d", h.Fixture.SourceRef);
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        (await h.OperationAsync())!.LastReason.ShouldBe(reason);
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("--ff-only") || a[0] == "push" || a.Contains("remove"));
        if (change == "moved") await h.Fixture.AssertRemoteSourceAsync();
    }

    /// <summary>
    /// G2: a pin is one expected-old-zero <c>update-ref</c> after the name and commit checks; git refuses an existing
    /// ref, and only that refusal reads the ref back to tell a same-id repeat (success) from <c>recovery_ref_collision</c>.
    /// </summary>
    [Test]
    public async Task C1432_pin_create_refuses_existing_ref_with_other_sha()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var pin = $"refs/antiphon/land/{fixture.TaskId:N}/{Guid.NewGuid():N}/source";

        fixture.Git.Trace.Clear();
        (await fixture.Git.PinAsync(fixture.Repository, pin, fixture.SeedSha, CancellationToken.None)).Succeeded.ShouldBeTrue();
        fixture.Git.Trace.Select(a => a[0]).ShouldBe(["check-ref-format", "rev-parse", "update-ref"],
            "a fresh pin is created without reading the ref first");
        fixture.Git.Trace.Single(a => a[0] == "update-ref").ShouldBe(new[] { "update-ref", pin, fixture.SeedSha, new string('0', fixture.SeedSha.Length) });

        await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "new source");
        var newer = (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).Trim();
        fixture.Git.Trace.Clear();
        (await fixture.Git.PinAsync(fixture.Repository, pin, newer, CancellationToken.None)).Diagnostic.ShouldBe("recovery_ref_collision");
        fixture.Git.Trace.Select(a => a[0]).ShouldBe(["check-ref-format", "rev-parse", "update-ref", "show-ref"],
            "git refused the create; one read-back classifies the refusal");
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", pin)).Trim().ShouldBe(fixture.SeedSha);

        fixture.Git.Trace.Clear();
        (await fixture.Git.PinAsync(fixture.Repository, pin, fixture.SeedSha, CancellationToken.None)).Succeeded.ShouldBeTrue("the same id is a repeat, not a collision");
        fixture.Git.Trace.Select(a => a[0]).ShouldBe(["check-ref-format", "rev-parse", "update-ref", "show-ref"]);
        await fixture.AssertRemoteSourceAsync();
    }

    private static async Task SetVerifyFilterAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        task.LandVerifyFilter = "/*/*/Fixture/*";
        await db.SaveChangesAsync();
    }
}
