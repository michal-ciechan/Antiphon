using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1446 D-4 (S4c). V-8: the pure lane policy (cooldown, queue, abandon). V-9: <c>AbandonCleanupAsync</c>
/// writes the status and one <c>LandingCleanup</c> event and refuses while a request is pending or the
/// cleanup is not <c>Refused</c>.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeResiduePublicationLaneTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);

    // name, earlier queued rows, cursor NotBefore offset in days (0 = no cursor), cleanup, MaxCleanupRetries, CleanupRetryCooldownDays, expected
    [Test]
    [Arguments("cooldown-active", 0, 1, LandCleanupStatus.Refused, 3, 7, PublicationLaneDecision.Cooldown)]
    [Arguments("two-queued-no-cooldown", 2, 0, LandCleanupStatus.Refused, 3, 7, PublicationLaneDecision.Queue)]
    [Arguments("three-queued-abandons", 3, 1, LandCleanupStatus.Refused, 3, 7, PublicationLaneDecision.Abandon)]
    [Arguments("pending-never-abandons", 3, 0, LandCleanupStatus.Pending, 3, 7, PublicationLaneDecision.Queue)]
    [Arguments("cooldown-expired-queues", 0, -1, LandCleanupStatus.Refused, 3, 7, PublicationLaneDecision.Queue)]
    [Arguments("cap-zero-never-abandons", 5, 0, LandCleanupStatus.Refused, 0, 7, PublicationLaneDecision.Queue)]
    [Arguments("pacing-off-ignores-cursor", 0, 1, LandCleanupStatus.Refused, 3, 0, PublicationLaneDecision.Queue)]
    public void C1446_policy_rows(string name, int queued, int notBeforeDays, LandCleanupStatus cleanup,
        int maxRetries, int cooldownDays, PublicationLaneDecision expected)
    {
        var op = new AgentTaskLanding { Id = Guid.NewGuid(), TaskId = Guid.NewGuid(), Active = true, Cleanup = cleanup,
            Publication = LandPublicationOutcome.Landed, Phase = LandPhase.CleanupStarted };
        var cursor = notBeforeDays == 0 ? null : new WorktreeResidueCandidateCursor
        {
            Id = Guid.NewGuid(), CandidateKey = PublicationLanePolicy.CursorKey(op.Id),
            LastEvaluatedAt = Now.AddDays(-1), NotBefore = Now.AddDays(notBeforeDays),
        };
        var settings = new WorktreeResidueSettings { MaxCleanupRetries = maxRetries, CleanupRetryCooldownDays = cooldownDays };

        PublicationLanePolicy.Decide(op, queued, cursor, Now, settings).ShouldBe(expected, name);
    }

    [Test]
    public async Task C1446_abandon_writes_status_and_event()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "fixture report");
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused, op.LastReason);
        var reason = op.LastReason.ShouldNotBeNull();
        var eventsBefore = await CountAbandonEventsAsync(h);

        await using var scope = h.Services.CreateAsyncScope();
        var lands = scope.ServiceProvider.GetRequiredService<AgentTaskLandService>();

        // A pending request keeps the operation in the lane: the retry it names must run first.
        var pendingId = Guid.NewGuid();
        await using (var db = h.CreateContext())
        {
            db.AgentTaskLandRequests.Add(new AgentTaskLandRequest
            {
                Id = pendingId, TaskId = h.Fixture.TaskId, RequestedAt = DateTime.UtcNow, State = LandRequestState.Queued,
                IsPending = true, LastEvaluatedAt = DateTime.UtcNow, LastProgressAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        (await Should.ThrowAsync<ConflictException>(() => lands.AbandonCleanupAsync(h.Fixture.TaskId, op.Id, 3, CancellationToken.None)))
            .Code.ShouldBe("land_request_pending");
        await using (var db = h.CreateContext())
        {
            await db.AgentTaskLandRequests.Where(r => r.Id == pendingId).ExecuteDeleteAsync();
        }
        (await CountAbandonEventsAsync(h)).ShouldBe(eventsBefore, "a refused abandonment writes nothing");

        await lands.AbandonCleanupAsync(h.Fixture.TaskId, op.Id, 3, CancellationToken.None);

        // Only a Refused cleanup can be abandoned; Abandoned is terminal for the lane.
        (await Should.ThrowAsync<ConflictException>(() => lands.AbandonCleanupAsync(h.Fixture.TaskId, op.Id, 4, CancellationToken.None)))
            .Code.ShouldBe("publication_unconfirmed");

        await using var verify = h.CreateContext();
        var after = await verify.AgentTaskLandings.AsNoTracking().SingleAsync(o => o.Id == op.Id);
        after.Cleanup.ShouldBe(LandCleanupStatus.Abandoned);
        after.Active.ShouldBeTrue();
        after.Phase.ShouldBe(op.Phase, "abandonment is a cleanup status, not a phase");
        after.LastReason.ShouldBe(reason, "the refusal reason is kept");
        var events = (await verify.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandingCleanup).ToListAsync())
            .Where(e => e.Detail.Contains("cleanup abandoned", StringComparison.Ordinal)).ToList();
        var abandoned = events.ShouldHaveSingleItem();
        abandoned.Detail.ShouldContain("after 3 refused retries");
        abandoned.Detail.ShouldContain(reason);
        abandoned.Detail.ShouldContain(op.WorktreePath);
        abandoned.Detail.ShouldContain("-Land");
    }

    private static async Task<int> CountAbandonEventsAsync(LandingSafetyHarness h)
    {
        await using var db = h.CreateContext();
        return (await db.AgentTaskEvents.AsNoTracking()
                .Where(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandingCleanup).ToListAsync())
            .Count(e => e.Detail.Contains("cleanup abandoned", StringComparison.Ordinal));
    }
}
