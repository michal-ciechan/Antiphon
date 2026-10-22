using Antiphon.Server.Application.Dtos;
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
/// CARD-1472. A Reply to a task that is Blocked only by a runner sync refusal reopens it as a
/// new model turn. The retained substantive report must survive that turn: a token-only
/// completion never replaces <c>Result</c>, the handoff or the deliverable, the reopened wire
/// restates the complete-report contract, and a complete replacement report still settles once.
/// The fixture is the measured shape: a runner Code task whose complete marked report was
/// settled through the real reply service while the desktop repository lease was busy.
/// </summary>
[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class BlockedReplyReportRetentionTests
{
    private const string RetainedBody =
        "Implemented CARD-1472 in the runner mirror and pushed; checkpoint rows green; details in the plan.";

    [Test]
    [Timeout(240_000)]
    public async Task Bare_done_after_blocked_reply_preserves_retained_report()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;

        await ReplyAsync(world, "Accepted. The report is final. Stop now and do not take another turn.");
        world.Task.Status.ShouldBe(AgentTaskStatus.Working, "a Reply reopens the sync-blocked task");
        world.Task.RepliedAtSequence.ShouldNotBeNull();
        var wire = await ReplyWireAsync(world);

        // The lease is free now, so a complete report would settle. The child sends only the token.
        await world.SettleAsync(
            DelegationReportFormatter.ReportToken(world.TaskId, "done"), prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldNotBe(AgentTaskStatus.Succeeded, Why(after) + " a bare token is not a new successful report");
        after.Result.ShouldBe(retained.Result, "the retained report body stands");
        after.NextStage.ShouldBe(retained.NextStage, "the retained handoff kind stands");
        after.NextHandoff.ShouldBe(retained.NextHandoff, "the retained handoff sentence stands");
        after.DeliverablePath.ShouldBe(retained.DeliverablePath, "the retained artifact pointer stands");
        after.DeliverableRef.ShouldBe(retained.DeliverableRef);
        after.ResultFilePath.ShouldBe(retained.ResultFilePath);
        after.Status.ShouldBe(AgentTaskStatus.Working, Why(after) + " the task keeps awaiting the complete report");
        after.ReportNudgedAt.ShouldNotBeNull("the one bounded ask for the complete report is recorded");

        var events = await world.EventsAsync();
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0, "no Succeeded publication");
        events.ShouldContain(
            e => e.Type == AgentTaskEventType.Warning && e.Detail.Contains("retained report", StringComparison.Ordinal),
            "the later turn is recorded as adding nothing");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Blocked_reply_wire_requires_a_complete_report_and_closing_token()
    {
        await using var world = await BlockedBySyncAsync();
        const string message = "Accepted. Stopping here.";

        await ReplyAsync(world, message);

        var wire = await ReplyWireAsync(world);
        wire.Status.ShouldBe(QueuedMessageStatus.Pending);
        var body = wire.Body;
        body.ShouldStartWith(DelegationReportFormatter.TaskMarker(world.TaskId), Case.Sensitive);
        body.ShouldContain(message, Case.Sensitive, "the caller's text still rides the wire");
        var done = DelegationReportFormatter.ReportToken(world.TaskId, "done");
        var blocked = DelegationReportFormatter.ReportToken(world.TaskId, "blocked");
        body.ShouldContain(done, Case.Sensitive, "the exact task-specific done line is restated");
        body.ShouldContain(blocked, Case.Sensitive, "the exact task-specific blocked line is restated");
        body.ShouldContain("complete", Case.Insensitive, "the wire asks for the complete report, not a token");
        body.ShouldContain("report", Case.Insensitive);
        body.IndexOf(done, StringComparison.Ordinal).ShouldBeGreaterThan(
            body.IndexOf(message, StringComparison.Ordinal), "the contract follows the caller's text");

        var replied = (await world.EventsAsync()).Single(e => e.Type == AgentTaskEventType.Replied);
        replied.InputBody.ShouldBe(body, "the Replied event stores the typed wire");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Complete_replacement_report_settles_once()
    {
        await using var world = await BlockedBySyncAsync();
        await ReplyAsync(world, "Accepted. Resend the complete report.");
        var wire = await ReplyWireAsync(world);
        const string replacement = "Replacement report: implemented and pushed; nothing changed since the first report.";

        await world.SettleAsync(RunnerSettlementWorld.Report(replacement), prompt: wire.Body);

        var settled = world.Task;
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded, Why(settled));
        settled.Result.ShouldNotBeNull().ShouldStartWith(replacement, Case.Sensitive);
        settled.NextStage.ShouldBe(PipelineHandoffKind.Review);
        var token = settled.ConcurrencyToken;
        var events = await world.EventsAsync();
        events.Count(e => e.Type == AgentTaskEventType.Completed).ShouldBeGreaterThanOrEqualTo(1);

        // The same boundary observed again settles nothing more.
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .OnTurnEndAsync(world.SessionId, CancellationToken.None);
        await world.ReloadAsync();
        world.Task.ConcurrencyToken.ShouldBe(token, "a second observation changes nothing");
        (await world.EventsAsync()).Count.ShouldBe(events.Count, "a second observation adds no event");
    }

    [Test]
    [Timeout(240_000)]
    public async Task Wrong_task_token_cannot_settle_the_reopened_task()
    {
        await using var world = await BlockedBySyncAsync();
        var retained = world.Task;
        await ReplyAsync(world, "Accepted. Stop.");
        var wire = await ReplyWireAsync(world);
        var foreign = DelegationReportFormatter.ReportToken(Guid.NewGuid(), "done");

        await world.SettleAsync("Acknowledged.\n" + foreign, prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Working, Why(after) + " another task's token is not this task's verdict");
        after.Result.ShouldBe(retained.Result);
        after.NextStage.ShouldBe(retained.NextStage);
        after.NextHandoff.ShouldBe(retained.NextHandoff);
        after.ReportNudgedAt.ShouldNotBeNull("the unmarked answer gets the one closing-line ask");
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Completed).ShouldBe(0);
    }

    [Test]
    [Timeout(240_000)]
    public async Task Genuine_marked_blocked_reply_remains_blocked()
    {
        await using var world = await BlockedBySyncAsync();
        await ReplyAsync(world, "Accepted. Continue.");
        var wire = await ReplyWireAsync(world);
        const string question = "Which base should the follow-up branch use: master or the rc line?";

        await world.SettleAsync(
            question + "\n" + DelegationReportFormatter.ReportToken(world.TaskId, "blocked"),
            prompt: wire.Body, closingVerdict: false);

        var after = world.Task;
        after.Status.ShouldBe(AgentTaskStatus.Blocked, Why(after));
        after.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Marked);
        after.Result.ShouldBe(question, "a complete blocked report replaces the retained body");
        (await world.EventsAsync()).Count(e => e.Type == AgentTaskEventType.Blocked).ShouldBe(2,
            "the sync block and the genuine blocked verdict");
    }

    /// <summary>
    /// A runner Code task whose complete marked report was settled while the desktop repository
    /// lease was busy: Blocked on <c>runner_sync_lease_busy</c> with the report retained.
    /// </summary>
    private static async Task<RunnerSettlementWorld> BlockedBySyncAsync()
    {
        var world = await RunnerSettlementWorld.CreateAsync(controlledSyncClock: true);
        try
        {
            await world.Git.RunnerPushAsync("work.txt", "runner work");
            await using (var held = await world.Git.Leases.TryAcquireAsync(world.Git.Desktop, CancellationToken.None))
            {
                held.ShouldNotBeNull("fixture: hold the desktop common-directory lease");
                var settle = world.SettleAsync(RunnerSettlementWorld.Report(RetainedBody));
                (await Task.WhenAny(world.LeaseBusy.First, settle)).ShouldBe(world.LeaseBusy.First,
                    "fixture: the settlement must wait on the busy lease");
                world.SyncClock!.Advance(TimeSpan.FromSeconds(new DelegationSettings().RunnerSyncBudgetSeconds));
                await settle;
            }

            var blocked = world.Task;
            blocked.Status.ShouldBe(AgentTaskStatus.Blocked, "fixture: " + Why(blocked));
            world.Evidence()!.RemoteSync!.Reason.ShouldBe(RemoteSettlementSyncReasons.LeaseBusy, "fixture");
            blocked.Result.ShouldNotBeNull().ShouldStartWith(RetainedBody, Case.Sensitive, "fixture: the report is retained");
            blocked.NextStage.ShouldBe(PipelineHandoffKind.Decide, "fixture: the sync block owns the handoff");
            blocked.NextHandoff.ShouldNotBeNullOrWhiteSpace("fixture");
            return world;
        }
        catch
        {
            await world.DisposeAsync();
            throw;
        }
    }

    private static async Task ReplyAsync(RunnerSettlementWorld world, string message)
    {
        await world.Services.GetRequiredService<AgentTaskReplyService>()
            .AnswerAsync(world.TaskId, message, AnswerOrigin.Cli, round: null, CancellationToken.None);
        await world.ReloadAsync();
    }

    /// <summary>The wire the Reply actually queued for the delegate session.</summary>
    private static async Task<SessionQueuedMessage> ReplyWireAsync(RunnerSettlementWorld world)
    {
        await using var db = world.CreateContext();
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == world.SessionId && m.Origin == QueuedMessageOrigin.Delegation)
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync();
    }

    private static string Why(AgentTask task) =>
        $"status={task.Status} evidence={task.ReportEvidence} reason={task.FailureReason} next={task.NextStage} "
        + $"result-length={task.Result?.Length ?? 0} sync={task.CompletionProgressEvidenceJson}";
}
