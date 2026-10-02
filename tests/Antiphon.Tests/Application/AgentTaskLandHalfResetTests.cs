using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandHalfResetTests
{
    [Test]
    [Arguments("unstaged")]
    [Arguments("index-only")]
    [Arguments("staged")]
    [Arguments("untracked")]
    [Arguments("ignored")]
    public async Task C883_RealEditsSurviveFreshRequest(string kind)
    {
        await using var fixture = new LandHalfResetFixture();
        var h = fixture.Harness;
        var (local, reviewed, evidence, firstId) = await fixture.CutAfterRefMoveAsync();
        var tracked = Path.Combine(h.Fixture.Source, "feature.txt");
        var sentinel = kind == "ignored" ? Path.Combine(h.Fixture.Source, ".antiphon", "sentinel")
            : kind == "untracked" ? Path.Combine(h.Fixture.Source, "sentinel.txt") : tracked;
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        var changed = System.Text.Encoding.UTF8.GetBytes("real owner edit\n");
        await File.WriteAllBytesAsync(sentinel, changed);
        string? stagedBlob = null;
        if (kind is "index-only" or "staged")
        {
            await h.Fixture.RequiredAsync(h.Fixture.Source, "add", "--", "feature.txt");
            stagedBlob = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim();
            if (kind == "index-only")
                await File.WriteAllTextAsync(tracked, "valuable feature\n");
        }
        var expectedBytes = await File.ReadAllBytesAsync(sentinel);
        var targetBefore = (await h.Fixture.RequiredAsync(h.Fixture.Repository,
            "rev-parse", h.Fixture.TargetRef)).Trim();
        var second = await h.RequestAsync(expectedSourceSha: reviewed, reviewEvidenceId: evidence,
            recoverReviewedSource: true);
        second.RequestId.ShouldNotBe(firstId, "C883: fresh request admitted");
        await h.RunQueuedAsync();
        await using var db = h.CreateContext();
        var row = await db.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.Id == second.RequestId);
        row.SourceRefusalReason.ShouldBe("source_dirty", $"C883 {kind}: dirty source refused");
        (await File.ReadAllBytesAsync(sentinel)).ShouldBe(expectedBytes, $"C883 {kind}: owner bytes preserved");
        if (stagedBlob is not null)
            (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", ":feature.txt")).Trim()
                .ShouldBe(stagedBlob, $"C883 {kind}: staged blob preserved");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, $"C883 {kind}: ref preserved");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.TargetRef)).Trim()
            .ShouldBe(targetBefore, $"C883 {kind}: target not published");
        row.RecoveryAdoptedAt.ShouldBeNull($"C883 {kind}: adoption not acknowledged");
        _ = local;
    }
}
