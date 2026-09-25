using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0535 V-12: DispatchHeld attention from stored Held/HeldAged/Dispatched events.</summary>
[Category("Integration")]
public sealed class DispatchHeldAttentionTests
{
    [Test]
    public async Task a_queued_hold_past_warning_is_a_dispatch_held_warning_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var heldAt = now.AddSeconds(-301);
        var (task, cardId) = await SeedQueuedHeldAsync(schema, now, heldAt);
        var items = await ReadAsync(schema, now);
        var item = items.ShouldHaveSingleItem();
        item.Kind.ShouldBe(AttentionKind.DispatchHeld);
        ((int)item.Kind).ShouldBe(42);
        ((int)AgentTaskEventType.HeldAged).ShouldBe(37);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.ConditionKey.ShouldBe($"dispatch-held:{task.Id:N}");
        item.SinceUtc.ShouldBe(heldAt);
        item.Actions.ShouldBe([AttentionAction.OpenDrawer]);
        item.TaskId.ShouldBe(task.Id);
        item.CardId.ShouldBe(cardId);
        item.Title.ShouldBe(task.Title);
        item.Headline.ShouldContain("Queued and held for 301s");
        item.Headline.ShouldContain(DispatchHoldDetails.ConcurrencyCap(1));
        item.Evidence.ShouldContain($"task={task.Id:N}");
        item.Evidence.ShouldContain($"heldSince={heldAt:O}");
    }

    [Test]
    public async Task past_error_threshold_is_error()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var (task, _) = await SeedQueuedHeldAsync(schema, now, now.AddSeconds(-901));
        var item = (await ReadAsync(schema, now)).ShouldHaveSingleItem();
        item.Kind.ShouldBe(AttentionKind.DispatchHeld);
        item.Severity.ShouldBe(AlertSeverity.Error);
        item.TaskId.ShouldBe(task.Id);
    }

    [Test]
    public async Task younger_than_warning_is_absent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        await SeedQueuedHeldAsync(schema, now, now.AddSeconds(-100));
        (await ReadAsync(schema, now)).ShouldBeEmpty();
    }

    [Test]
    public async Task routing_pin_held_is_absent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        await SeedQueuedHeldAsync(
            schema, now, now.AddSeconds(-2000),
            detail: $"{DispatchHoldDetails.RoutingPinPrefix} 2099-01-01T00:00:00Z; dispatch paused (wait).");
        (await ReadAsync(schema, now)).ShouldBeEmpty();
    }

    [Test]
    public async Task dispatched_task_is_absent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var (task, _) = await SeedQueuedHeldAsync(schema, now, now.AddSeconds(-2000));
        await using (var db = CreateContext(schema))
        {
            var row = await db.AgentTasks.SingleAsync(t => t.Id == task.Id);
            row.Status = AgentTaskStatus.Dispatched;
            row.DispatchedAt = now.AddSeconds(-1000);
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Dispatched,
                Detail = "dispatched", At = now.AddSeconds(-1000),
            });
            await db.SaveChangesAsync();
        }

        (await ReadAsync(schema, now)).ShouldBeEmpty();
    }

    [Test]
    public async Task requeue_stint_uses_the_new_held()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var (task, _) = await SeedQueuedHeldAsync(schema, now, now.AddSeconds(-2000));
        await using (var db = CreateContext(schema))
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Dispatched,
                Detail = "prior", At = now.AddSeconds(-1000),
            });
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Held,
                Detail = DispatchHoldDetails.ConcurrencyCap(1), At = now.AddSeconds(-100),
            });
            await db.SaveChangesAsync();
        }

        (await ReadAsync(schema, now)).ShouldBeEmpty();

        var freshHeld = now.AddSeconds(-400);
        await using (var db = CreateContext(schema))
        {
            var recent = await db.AgentTaskEvents.SingleAsync(e =>
                e.AgentTaskId == task.Id && e.Type == AgentTaskEventType.Held && e.At == now.AddSeconds(-100));
            recent.At = freshHeld;
            await db.SaveChangesAsync();
        }

        var item = (await ReadAsync(schema, now)).ShouldHaveSingleItem();
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.SinceUtc.ShouldBe(freshHeld);
    }

    [Test]
    public async Task a_pinned_agent_parked_hold_is_a_dispatch_held_warning_row()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        const string agentName = "pool-alpha";
        var detail = DispatchHoldDetails.PinnedAgentParkedOn(
            agentName, "abcd1234", AgentTaskStatus.Blocked);
        var (task, _) = await SeedQueuedHeldAsync(schema, now, now.AddSeconds(-301), detail: detail);
        var item = (await ReadAsync(schema, now)).ShouldHaveSingleItem();
        item.Kind.ShouldBe(AttentionKind.DispatchHeld);
        item.Severity.ShouldBe(AlertSeverity.Warning);
        item.Headline.ShouldContain(agentName);
        item.ConditionKey.ShouldBe($"dispatch-held:{task.Id:N}");
    }

    [Test]
    public async Task two_reads_share_the_condition_key()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var (task, _) = await SeedQueuedHeldAsync(schema, now, now.AddSeconds(-400));
        var first = (await ReadAsync(schema, now)).ShouldHaveSingleItem().ConditionKey;
        var second = (await ReadAsync(schema, now)).ShouldHaveSingleItem().ConditionKey;
        first.ShouldBe($"dispatch-held:{task.Id:N}");
        second.ShouldBe(first);
    }

    [Test]
    public async Task C672_dispatch_held_item_names_the_dominant_hold_class()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var leaseAt = now.AddSeconds(-350);
        var (task, _) = await SeedQueuedHeldAsync(schema, now.AddSeconds(-400), leaseAt,
            detail: DispatchHoldDetails.LeaseOccupiedUnknown);
        await using (var db = CreateContext(schema))
        {
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(), AgentTaskId = task.Id, Type = AgentTaskEventType.Held,
                Detail = DispatchHoldDetails.RunnerAtCapacity("server2", 1, 1), At = leaseAt.AddSeconds(200),
            });
            await db.SaveChangesAsync();
        }

        var item = (await ReadAsync(schema, now)).ShouldHaveSingleItem();
        item.HoldClass.ShouldBe("lease");
        item.Evidence.ShouldContain("leaseWait=200s");
        item.Evidence.ShouldContain("runnerWait=150s");
        item.Evidence.ShouldContain("class=lease");
        item.Headline.ShouldContain(DispatchHoldDetails.RunnerAtCapacity("server2", 1, 1));
    }

    [Test]
    [Arguments(20, false)]
    [Arguments(301, true)]
    public async Task C672_land_yield_hold_is_not_an_attention_item_before_the_warning_age(int ageSeconds, bool expected)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var now = UtcMs();
        var since = now.AddSeconds(-ageSeconds);
        await using (var db = CreateContext(schema))
        {
            var id = Guid.NewGuid();
            var request = new AgentTaskLandRequest
            {
                Id = Guid.NewGuid(), TaskId = id, RequestedAt = since, LastEvaluatedAt = now, LastProgressAt = since,
                State = LandRequestState.Held, IsPending = true, HeldSince = since,
                HoldReasonCode = AgentTaskLandService.LeaseYieldedToDispatchCode,
                HoldDetail = "Land yields the repository mutation lease to 1 queued dispatch(es): abcd1234 (dispatch); resumes within Delegation:LandSweepSeconds.",
            };
            var task = new AgentTask
            {
                Id = id, RootTaskId = id, Title = "yielding land", Goal = "land", Role = AgentTaskRole.Code,
                AgentKind = AgentKind.ClaudeCode, ModelLevel = AgentModelLevel.Medium, Workspace = WorkspaceMode.Worktree,
                WorkingDirectory = Path.GetTempPath(), Status = AgentTaskStatus.Succeeded, CreatedAt = since.AddMinutes(-10),
                CompletedAt = since,
            };
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            task.LandRequestedAt = since;
            task.CurrentLandRequestId = request.Id;
            db.AgentTaskLandRequests.Add(request);
            await db.SaveChangesAsync();
        }

        var items = await ReadAllAsync(schema, now);
        items.Count(i => i.Kind == AttentionKind.LandHeld).ShouldBe(expected ? 1 : 0);
    }

    private static async Task<(AgentTask Task, Guid CardId)> SeedQueuedHeldAsync(
        IsolatedTestSchema schema, DateTime createdAt, DateTime heldAt, string? detail = null)
    {
        await using var db = CreateContext(schema);
        var card = await RoutingPinServiceTests.SeedCardAsync(db, "CARD-0535");
        var id = Guid.NewGuid();
        var task = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            Title = "held queue",
            Goal = "held queue",
            Role = AgentTaskRole.Docs,
            CardId = card.Id,
            AgentKind = AgentKind.ClaudeCode,
            ModelLevel = AgentModelLevel.Medium,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = Path.GetTempPath(),
            Status = AgentTaskStatus.Queued,
            CreatedAt = createdAt,
        };
        db.AgentTasks.Add(task);
        db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Held,
            Detail = detail ?? DispatchHoldDetails.ConcurrencyCap(1),
            At = heldAt,
        });
        await db.SaveChangesAsync();
        return (task, card.Id);
    }

    private static async Task<List<AttentionItemDto>> ReadAsync(IsolatedTestSchema schema, DateTime now) =>
        (await ReadAllAsync(schema, now)).Where(i => i.Kind == AttentionKind.DispatchHeld).ToList();

    private static async Task<List<AttentionItemDto>> ReadAllAsync(IsolatedTestSchema schema, DateTime now)
    {
        await using var db = CreateContext(schema);
        var clock = new FakeTimeProvider(new DateTimeOffset(now, TimeSpan.Zero));
        var result = await new AttentionService(
            db,
            new AttentionServiceTests.FakeRunnerClient(),
            Options.Create(new SupervisionSettings()),
            Options.Create(new DelegationSettings()),
            clock,
            NullLogger<AttentionService>.Instance)
            .GetAsync(CancellationToken.None, includeProgressProbe: false);
        return result.Items.ToList();
    }

    private static DateTime UtcMs()
    {
        var now = DateTime.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMillisecond));
    }

    private static AppDbContext CreateContext(IsolatedTestSchema schema) =>
        new(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
}
