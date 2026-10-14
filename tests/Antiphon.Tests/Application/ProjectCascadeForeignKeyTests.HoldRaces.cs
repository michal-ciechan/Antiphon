using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class ProjectCascadeForeignKeyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Nudge_inserted_and_claimed_after_hold_check_refuses_delete_and_preserves_hold(bool projectDelete)
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var db = f.Db();
        var graph = await SeedGraphAsync(db);
        var commentId = Guid.NewGuid();
        var generation = await f.GenerationAsync();
        var gate = new BeforeNudgeDelete(async () =>
        {
            // The cascade has checked and locked its existing nudges. A watchdog pass now
            // commits a new audit comment/nudge, then commits its claim before typing.
            await using var writer = f.Db();
            writer.CardComments.Add(new CardComment
            {
                Id = commentId, CardId = graph.Doomed.CardId, Body = "Concurrent watchdog audit",
                CreatedAt = DateTime.UtcNow
            });
            AddNudge(writer, graph.Doomed with { CommentId = commentId }, f.SessionId);
            await writer.SaveChangesAsync();
            (await writer.ExpectationNudges.Where(n => n.Id == graph.Doomed.NudgeId
                    && n.AttemptState == ExpectationAttemptState.None)
                .ExecuteUpdateAsync(u => u.SetProperty(n => n.AttemptState, ExpectationAttemptState.Attempting)
                    .SetProperty(n => n.DestinationGeneration, generation))).ShouldBe(1);
            f.Harness.Adapter.PrimeComposer("Keep the audit evidence until its card is deleted.");
            await AssertComposerHeldAsync(f);
        });
        await using var host = await DeleteHost.StartAsync(f, gate);

        using var response = await host.Client.DeleteAsync(DeleteRoute(graph.Doomed, projectDelete));
        gate.Visits.ShouldBe(1, "the insertion was scheduled between the initial check and nudge deletion");
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("project_delete_blocked_by_expectation_hold");
        problem.RootElement.GetProperty("detail").GetString().ShouldContain(
            $"POST /api/sessions/{f.SessionId:D}/expectation-hold/release");
        await using var verify = f.Db();
        (await verify.ExpectationNudges.SingleAsync(n => n.Id == graph.Doomed.NudgeId))
            .AttemptState.ShouldBe(ExpectationAttemptState.Attempting);
        (await verify.CardComments.SingleAsync(c => c.Id == commentId)).Body.ShouldBe("Concurrent watchdog audit");
        (await verify.Cards.SingleAsync(c => c.Id == graph.Doomed.CardId)).OwnerSessionId.ShouldBe(graph.Doomed.SessionId);
        (await verify.Boards.AnyAsync(b => b.Id == graph.Doomed.BoardId)).ShouldBeTrue();
        (await verify.Projects.AnyAsync(p => p.Id == graph.Doomed.ProjectId)).ShouldBeTrue();
        await AssertComposerHeldAsync(f);
    }

    [Test]
    [Arguments(false, false, ExpectationAttemptState.Attempting)]
    [Arguments(true, false, ExpectationAttemptState.Attempting)]
    [Arguments(false, false, ExpectationAttemptState.Uncertain)]
    [Arguments(true, false, ExpectationAttemptState.Uncertain)]
    [Arguments(false, false, ExpectationAttemptState.Unconfirmed)]
    [Arguments(true, false, ExpectationAttemptState.Unconfirmed)]
    [Arguments(false, true, ExpectationAttemptState.Attempting)]
    [Arguments(true, true, ExpectationAttemptState.Attempting)]
    [Arguments(false, true, ExpectationAttemptState.Uncertain)]
    [Arguments(true, true, ExpectationAttemptState.Uncertain)]
    [Arguments(false, true, ExpectationAttemptState.Unconfirmed)]
    [Arguments(true, true, ExpectationAttemptState.Unconfirmed)]
    public async Task Holding_state_without_a_current_destination_generation_allows_delete(
        bool projectDelete, bool missingSession, ExpectationAttemptState state)
    {
        await using var f = await ExpectationDeliveryFixture.CreateAsync();
        await using var db = f.Db();
        var graph = await SeedGraphAsync(db);
        var destination = missingSession ? await f.AddSessionAsync(false, false) : f.SessionId;
        var generation = await f.GenerationAsync(destination);
        AddNudge(db, graph.Doomed, destination);
        await db.SaveChangesAsync();
        await db.ExpectationNudges.Where(n => n.Id == graph.Doomed.NudgeId).ExecuteUpdateAsync(u => u
            .SetProperty(n => n.AttemptState, state)
            .SetProperty(n => n.DestinationGeneration, missingSession ? generation : generation.AddSeconds(-1)));
        if (missingSession)
            (await db.AgentSessions.Where(s => s.Id == destination).ExecuteDeleteAsync()).ShouldBe(1);
        await using var host = await DeleteHost.StartAsync(f);

        // This is the unreleasable condition from Review: the suggested route has no current hold.
        using var release = new HttpRequestMessage(HttpMethod.Post, ExpectationHoldAudit.ReleaseRoute(destination))
        {
            Content = JsonContent.Create(new ReleaseExpectationHoldRequest("No current-generation hold to release."))
        };
        host.Authorize(release);
        using var released = await host.Client.SendAsync(release);
        released.StatusCode.ShouldBe(missingSession ? HttpStatusCode.NotFound : HttpStatusCode.OK);
        if (!missingSession)
            (await released.Content.ReadFromJsonAsync<ExpectationHoldReleaseResult>())!.ReleasedNudgeIds.ShouldBeEmpty();

        using var deleted = await host.Client.DeleteAsync(DeleteRoute(graph.Doomed, projectDelete));
        deleted.StatusCode.ShouldBe(projectDelete ? HttpStatusCode.NoContent : HttpStatusCode.OK,
            await deleted.Content.ReadAsStringAsync());
        await using var verify = f.Db();
        (await verify.ExpectationNudges.AnyAsync(n => n.Id == graph.Doomed.NudgeId)).ShouldBeFalse();
        (await verify.CardComments.AnyAsync(c => c.Id == graph.Doomed.CommentId)).ShouldBeFalse();
        (await verify.AgentSessions.AnyAsync(s => s.Id == f.SessionId)).ShouldBeTrue();
        await AssertSurvivorsAsync(verify, graph, projectDelete);
        f.Harness.Adapter.Inputs.ShouldBeEmpty();
    }

    private sealed class BeforeNudgeDelete(Func<Task> insertAndClaim) : DbCommandInterceptor
    {
        public int Visits { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            // This existing statement follows the initial hold check and precedes nudge deletion.
            // Scheduling at this boundary also works when the final delete uses RETURNING.
            if (command.CommandText.Contains("DELETE FROM \"CardWorktreeCleanups\"", StringComparison.Ordinal))
            {
                Visits++;
                await insertAndClaim();
            }
            return result;
        }
    }
}
