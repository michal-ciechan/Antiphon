using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel("MessageQueue")]
public class PhoneHomeSchedulePreviewTests
{
    [Test]
    public async Task Before_first_List_preview_remains_queueable_without_writes()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        var before = await h.DurableStateAsync();
        var preview = await h.PreviewAsync();
        preview.Target.AgentLive.ShouldBe(true, "an active bound session is unknown until the first authoritative List");
        preview.Effect.ShouldContain("WhenIdle");
        preview.Spend.ShouldBe("none");
        preview.WillStartSession.ShouldBeFalse();
        preview.Warnings.ShouldBeEmpty();
        (await h.DurableStateAsync()).ShouldBe(before);
        h.Host.Local.Calls.ShouldNotContain("start");
    }
    [Test]
    public async Task Pending_preview_and_fire_agree_after_warm_cache_miss()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync(warmEmpty: true);
        h.Runtime.ListLiveOrUnknownSessions().ShouldNotContain(h.SessionId);
        var before = await h.DurableStateAsync();
        var preview = await h.PreviewAsync();
        preview.Target.AgentLive.ShouldBe(true);
        preview.Warnings.ShouldBeEmpty();
        (await h.DurableStateAsync()).ShouldBe(before);
        var schedule = await PhoneHomeStrandedQueueTests.SeedSkipWhenDownPromptAsync(h.Schema.ConnectionString, h.Bridge.AgentId, "preview and fire agree");
        await PhoneHomeStrandedQueueTests.FireNowAsync(h.Bridge, schedule);
        var row = (await h.RowsAsync()).ShouldHaveSingleItem();
        row.Status.ShouldBe(QueuedMessageStatus.Pending);
        row.SourceScheduleId.ShouldBe(schedule);
        await using var db = h.Db();
        (await db.ScheduleFires.SingleAsync(f => f.ScheduleId == schedule)).Outcome.ShouldBe(ScheduleFireOutcome.Enqueued);
        h.Probe.Reads.ShouldBe(1);
    }

    [Test]
    public async Task Authoritative_absence_changes_preview_to_down()
    {
        await using var h = await PhoneHomeOutageHarness.CreateAsync();
        (await h.PreviewAsync()).Target.AgentLive.ShouldBe(true);
        await using var peer = await h.RecoverAsync(includeTarget: false);
        var before = await h.DurableStateAsync();
        var skip = await h.PreviewAsync();
        skip.Target.AgentLive.ShouldBe(false);
        skip.Warnings.ShouldContain(w => w.Contains("SkippedNoSession", StringComparison.Ordinal));
        var queue = await h.PreviewAsync(ScheduleWhenTargetDown.Queue);
        queue.Target.AgentLive.ShouldBe(false);
        queue.Warnings.ShouldContain(w => w.Contains("wait on the persistent session", StringComparison.Ordinal));
        (await h.DurableStateAsync()).ShouldBe(before);
    }

    [Test]
    public async Task Local_missing_and_terminal_preview_controls_remain_unchanged()
    {
        foreach (var state in new[] { "local-live", "local-down", "missing", "terminal" })
        {
            await using var h = await PhoneHomeOutageHarness.CreateAsync(remote: false);
            if (state == "local-down") h.Runtime.TryRemove(h.SessionId, out _);
            await using var db = h.Db();
            if (state == "missing")
                await db.Agents.Where(a => a.Id == h.Bridge.AgentId).ExecuteUpdateAsync(u => u.SetProperty(a => a.PersistentSessionId, (string?)null));
            if (state == "terminal")
                await db.AgentSessions.Where(s => s.Id == h.SessionId).ExecuteUpdateAsync(u => u.SetProperty(s => s.Status, SessionStatus.Stopped));
            var before = await h.DurableStateAsync();
            var preview = await h.PreviewAsync();
            preview.Target.AgentLive.ShouldBe(state == "local-live");
            preview.Warnings.Any().ShouldBe(state != "local-live");
            preview.WillStartSession.ShouldBeFalse();
            preview.Spend.ShouldBe("none");
            (await h.DurableStateAsync()).ShouldBe(before);
        }
    }
}
