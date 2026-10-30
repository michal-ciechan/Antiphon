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
        await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var answer = h.DecideAsync(episode: episode);
        release.TrySetResult();
        await finish;
        (await Should.ThrowAsync<ConflictException>(() => answer)).Code.ShouldBe("ceiling_episode_stale");
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
}
