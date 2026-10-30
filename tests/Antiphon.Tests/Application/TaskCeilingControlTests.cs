using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public sealed class TaskCeilingControlTests
{
    [Test]
    [Arguments("continue")]
    [Arguments("cancel")]
    public async Task C1610_Concurrent_decisions_preserve_the_terminal_outcome(string other)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.SweepAsync();
        var episode = (await h.TaskAsync()).CeilingHoldEpisodeId;
        async Task<bool> ContinueAsync()
        {
            try { await h.DecideAsync(episode: episode); return true; }
            catch (ConflictException ex) when (ex.Code == "ceiling_episode_stale") { return false; }
        }
        var first = ContinueAsync();
        if (other == "continue")
        {
            var results = await Task.WhenAll(first, ContinueAsync());
            results.Count(ok => ok).ShouldBe(1);
            (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Working);
            h.Stopper.Killed.ShouldBeEmpty();
        }
        else
        {
            await using var scope = h.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<AgentTaskService>().CancelAsync(h.TaskId, CancellationToken.None);
            await first;
            (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Canceled);
            (await ContinueAsync()).ShouldBeFalse();
            await h.SweepAsync();
            (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Canceled);
            h.Stopper.Killed.ShouldHaveSingleItem().ShouldBe(h.SessionId);
        }
    }

    [Test]
    public async Task C1610_Working_query_cost_is_measured()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await using var db = h.Context();
        var samples = new List<double>();
        for (var i = 0; i < 30; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            (await SessionMessageQueueService.IsWorkingAsync(db, h.SessionId, CancellationToken.None)).ShouldBeTrue();
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }
        samples.Sort();
        Console.WriteLine($"C1610 working-query samples=30 p50_ms={samples[14]:F3} p95_ms={samples[28]:F3} max_ms={samples[^1]:F3}");
    }
    [Test]
    public async Task C1610_Continue_keeps_report_floor()
    {
        await using var h = await TaskCeilingHarness.CreateAsync(ageMinutes: 240);
        await h.SweepAsync();
        var before = await h.TaskAsync();
        before.CeilingHoldEpisodeId.ShouldNotBeNull();
        await h.DecideAsync();
        var after = await h.TaskAsync();
        after.NextCeilingMinutes.ShouldBe(480);
        after.RepliedAt.ShouldBe(before.RepliedAt);
        after.RepliedAtSequence.ShouldBe(before.RepliedAtSequence);
        after.Attempt.ShouldBe(before.Attempt);
        after.AgentSessionId.ShouldBe(before.AgentSessionId);
        after.CeilingAcknowledgedAt.ShouldNotBeNull();
        await using (var db = h.Context())
            (await db.SessionQueuedMessages.CountAsync(m => m.AgentSessionId == h.SessionId)).ShouldBe(0);
        await h.FinishAsync();
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Succeeded, "the original brief still attributes its report");
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("valid")]
    [Arguments("cap")]
    [Arguments("invalid")]
    public async Task C1610_Extend_validates_and_rearms(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(ageMinutes: 240);
        await h.SweepAsync();
        var before = await h.TaskAsync();
        if (arm == "valid")
        {
            await h.DecideAsync(CeilingAction.Extend, 60);
            (await h.TaskAsync()).NextCeilingMinutes.ShouldBe(300);
        }
        else
        {
            if (arm == "invalid") await Should.ThrowAsync<ValidationException>(() => h.DecideAsync(CeilingAction.Extend, 0));
            else (await Should.ThrowAsync<ConflictException>(() => h.DecideAsync(CeilingAction.Extend, 500))).Code.ShouldBe("ceiling_cap_reached");
            var after = await h.TaskAsync();
            after.ConcurrencyToken.ShouldBe(before.ConcurrencyToken);
            after.CeilingAcknowledgedAt.ShouldBeNull();
            after.NextCeilingMinutes.ShouldBe(before.NextCeilingMinutes);
        }
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("episode")]
    [Arguments("attempt")]
    public async Task C1610_Stale_answer_is_refused(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.SweepAsync();
        var before = await h.TaskAsync();
        if (arm == "attempt") await h.MutateAsync(t => t.Attempt++);
        var ex = await Should.ThrowAsync<ConflictException>(() => h.DecideAsync(episode:
            arm == "episode" ? Guid.NewGuid() : before.CeilingHoldEpisodeId));
        ex.Code.ShouldBe("ceiling_episode_stale");
        (await h.TaskAsync()).CeilingAcknowledgedAt.ShouldBeNull();
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C1610_Answer_races_report_without_reprompt()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.SweepAsync();
        var episode = (await h.TaskAsync()).CeilingHoldEpisodeId;
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Replies.DelayAfterOpenTaskLoadedAsync = async (_, ct) => { loaded.TrySetResult(); await release.Task.WaitAsync(ct); };
        var finish = h.FinishAsync();
        try
        {
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var answer = h.DecideAsync(episode: episode);
            release.TrySetResult();
            await finish;
            (await Should.ThrowAsync<ConflictException>(() => answer)).Code.ShouldBe("ceiling_episode_stale");
        }
        finally { release.TrySetResult(); await finish; }
        var settled = await h.TaskAsync();
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.RepliedAtSequence.ShouldBeNull();
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments(TranscriptKinds.CompactBoundary)]
    [Arguments(TranscriptKinds.TurnTitle)]
    [Arguments(TranscriptKinds.QueueEnqueue)]
    public async Task C1610_No_phase_tail_rearms(string kind)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(ageMinutes: 240, kind: kind, lastAgeMinutes: 40);
        await h.SweepAsync();
        var first = await h.TaskAsync();
        first.CeilingHoldEpisodeId.ShouldNotBeNull();
        await using (var db = h.Context())
        {
            var facts = await CeilingHoldFacts.DescribeAsync(db, first, h.Clock.GetUtcNow().UtcDateTime,
                new(), CancellationToken.None);
            facts.ShouldNotBeNull();
            facts.PhaseDeadlineAt.ShouldBeNull();
            facts.Question.ShouldContain("No phase deadline covers this tail");
        }
        await h.DecideAsync();
        (await h.TaskAsync()).NextCeilingMinutes.ShouldBe(480);
        h.Clock.Advance(TimeSpan.FromMinutes(240));
        await h.SweepAsync();
        var second = await h.TaskAsync();
        second.Status.ShouldBe(AgentTaskStatus.Working);
        second.CeilingHoldEpisodeId.ShouldNotBe(first.CeilingHoldEpisodeId);
        second.CeilingAcknowledgedAt.ShouldBeNull();
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    [Arguments("ordinary-refusal")]
    [Arguments("unauthorized-operator")]
    [Arguments("authenticated-rearm")]
    public async Task C1610_Final_cap_requires_operator_extend(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync(ageMinutes: 725,
            kind: TranscriptKinds.CompactBoundary, lastAgeMinutes: 40);
        await h.SweepAsync();
        var first = await h.TaskAsync();
        first.CeilingHoldEpisodeId.ShouldNotBeNull();
        if (arm == "ordinary-refusal")
        {
            (await Should.ThrowAsync<ConflictException>(() => h.DecideAsync())).Code.ShouldBe("ceiling_cap_reached");
            (await Should.ThrowAsync<ConflictException>(() => h.DecideAsync(CeilingAction.Extend, 1))).Code.ShouldBe("ceiling_cap_reached");
        }
        else if (arm == "unauthorized-operator")
            (await Should.ThrowAsync<ForbiddenException>(() => h.DecideAsync(CeilingAction.Extend, 60, true,
                reason: "authorized extra inspection"))).Code.ShouldBe("operator_token_required");
        else
        {
            await h.DecideAsync(CeilingAction.Extend, 60, true, true, "authorized extra inspection");
            var rearmed = await h.TaskAsync();
            rearmed.NextCeilingMinutes.ShouldBe(785);
            (await Should.ThrowAsync<ConflictException>(() => h.DecideAsync(CeilingAction.Extend, 60, true, true,
                "replay", first.CeilingHoldEpisodeId))).Code.ShouldBe("ceiling_episode_stale");
            h.Clock.Advance(TimeSpan.FromMinutes(60));
            await h.SweepAsync();
            var again = await h.TaskAsync();
            again.CeilingAcknowledgedAt.ShouldBeNull();
            again.CeilingHoldEpisodeId.ShouldNotBe(first.CeilingHoldEpisodeId);
            (await Should.ThrowAsync<ConflictException>(() => h.DecideAsync())).Code.ShouldBe("ceiling_cap_reached");
        }
        (await h.TaskAsync()).Status.ShouldBe(AgentTaskStatus.Working);
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C1610_Retry_resets_hold_but_preserves_requested_ceiling()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await h.MutateAsync(t => { t.RequestedCeilingMinutes = 300; t.RequestedCeilingReason = "long inspection"; });
        h.Clock.Advance(TimeSpan.FromMinutes(60));
        await h.AppendAsync(TranscriptKinds.ToolCall, "new tool");
        await h.SweepAsync();
        (await h.TaskAsync()).CeilingHoldEpisodeId.ShouldNotBeNull();
        using var scope = h.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AgentTaskService>().RetryAsync(h.TaskId, CancellationToken.None);
        var retried = await h.TaskAsync();
        retried.Status.ShouldBe(AgentTaskStatus.Queued);
        retried.Attempt.ShouldBe(2);
        retried.CeilingHoldEpisodeId.ShouldBeNull();
        retried.NextCeilingMinutes.ShouldBeNull();
        retried.ResolvedCeilingMinutes.ShouldBeNull();
        retried.RequestedCeilingMinutes.ShouldBe(300);
        retried.CeilingOperatorExtended.ShouldBeFalse();
        h.Stopper.Killed.ShouldContain(h.SessionId, "Retry retains its explicit stop semantics");
    }

    [Test]
    [Arguments("same-role")]
    [Arguments("different-role")]
    [Arguments("no-card")]
    [Arguments("pre-hold-successor")]
    [Arguments("own-child")]
    [Arguments("own-grandchild")]
    public async Task C1610_Successor_fence_uses_card_and_role(string arm)
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        if (arm != "no-card") await SeedCardAsync(h);
        Guid successor = arm == "pre-hold-successor" ? await SeedSuccessorAsync(h) : Guid.Empty;
        if (arm == "pre-hold-successor") h.Clock.Advance(TimeSpan.FromMinutes(1));
        await h.SweepAsync();
        (await h.TaskAsync()).CeilingHoldEpisodeId.ShouldNotBeNull();
        if (arm != "pre-hold-successor")
            successor = await SeedSuccessorAsync(h, arm == "different-role" ? AgentTaskRole.Debug : AgentTaskRole.Code);
        var descendant = arm is "own-child" or "own-grandchild";
        if (descendant)
        {
            await using var db = h.Context();
            var child = await db.AgentTasks.SingleAsync(t => t.Id == successor);
            child.RootTaskId = h.TaskId;
            child.ParentTaskId = h.TaskId;
            if (arm == "own-grandchild")
            {
                var grandchild = new AgentTask { Id = Guid.NewGuid(), RootTaskId = h.TaskId, ParentTaskId = child.Id,
                    CardId = child.CardId, ProjectId = child.ProjectId, Role = child.Role, Title = "Own grandchild",
                    Goal = child.Goal, WorkingDirectory = child.WorkingDirectory, CreatedAt = child.CreatedAt,
                    Status = AgentTaskStatus.Succeeded };
                db.AgentTasks.Add(grandchild);
            }
            await db.SaveChangesAsync();
        }
        await h.FinishAsync();
        var settled = await h.TaskAsync();
        settled.Status.ShouldBe(AgentTaskStatus.Succeeded);
        settled.Result.ShouldContain("Completed the authorized work");
        settled.NextStage.ShouldBe(arm == "different-role" || descendant ? PipelineHandoffKind.Review : PipelineHandoffKind.Decide,
            "a held result must not launch a second same-card/role chain; missing lineage needs a decision");
        if (arm != "different-role" && arm != "no-card" && !descendant) settled.NextHandoff.ShouldContain(successor.ToString("D"));
        await using (var db = h.Context())
        {
            var note = await db.SessionQueuedMessages.SingleAsync(m => m.SourceTaskId == h.TaskId);
            note.Body.Split('\n')[0].ShouldContain(arm == "different-role" || descendant ? "next=review" : "next=decide");
        }
        h.Stopper.Killed.ShouldBeEmpty();
    }

    [Test]
    public async Task C1610_Replacement_racing_handoff_is_held()
    {
        await using var h = await TaskCeilingHarness.CreateAsync();
        await SeedCardAsync(h);
        await h.SweepAsync();
        await h.FinishAsync();
        var settled = await h.TaskAsync();
        settled.NextStage.ShouldBe(PipelineHandoffKind.Review);
        Guid settlement;
        await using (var db = h.Context())
            settlement = await db.AgentTaskEvents.Where(e => e.AgentTaskId == h.TaskId && e.Type == AgentTaskEventType.Completed)
                .Select(e => e.Id).SingleAsync();
        await SeedSuccessorAsync(h);
        await using (var db = h.Context())
        {
            var service = new PipelineHandoffActionService(db, h.Clock);
            var error = await Should.ThrowAsync<ConflictException>(() => service.RecordAsync(h.TaskId,
                new PipelineHandoffActionRequest(settled.Attempt, settlement, AgentTaskRole.Review, 0,
                    "eligible", "Attempt automatic successor reservation"), "operator", CancellationToken.None));
            error.Code.ShouldBe("handoff_successor_held");
        }
        await using (var db = h.Context())
        {
            (await db.PipelineHandoffActions.CountAsync()).ShouldBe(0);
            (await db.AgentTasks.CountAsync()).ShouldBe(2);
        }
        h.Stopper.Killed.ShouldBeEmpty();
    }

    private static async Task SeedCardAsync(TaskCeilingHarness h)
    {
        await using var db = h.Context();
        var now = h.Clock.GetUtcNow().UtcDateTime;
        var project = new Project { Id = Guid.NewGuid(), Name = "Ceiling successor", CreatedAt = now, UpdatedAt = now };
        var board = new Board { Id = Guid.NewGuid(), ProjectId = project.Id, Name = "Ceiling successor" };
        var column = new BoardColumn { Id = Guid.NewGuid(), BoardId = board.Id, Name = "Doing", StateKey = "doing" };
        var card = new Card { Id = Guid.NewGuid(), BoardId = board.Id, BoardColumnId = column.Id,
            Identifier = "C1610-" + Guid.NewGuid().ToString("N")[..8], Title = "Ceiling successor" };
        db.AddRange(project, board, column, card);
        var task = await db.AgentTasks.SingleAsync(t => t.Id == h.TaskId);
        task.CardId = card.Id;
        task.ProjectId = project.Id;
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> SeedSuccessorAsync(TaskCeilingHarness h, AgentTaskRole role = AgentTaskRole.Code)
    {
        await using var db = h.Context();
        var task = await db.AgentTasks.AsNoTracking().SingleAsync(t => t.Id == h.TaskId);
        var id = Guid.NewGuid();
        db.AgentTasks.Add(new AgentTask { Id = id, RootTaskId = id, CardId = task.CardId, ProjectId = task.ProjectId,
            Role = role, Title = "Separate replacement", Goal = "Replacement work", WorkingDirectory = task.WorkingDirectory,
            CreatedAt = h.Clock.GetUtcNow().UtcDateTime, Status = AgentTaskStatus.Succeeded });
        await db.SaveChangesAsync();
        return id;
    }
}
