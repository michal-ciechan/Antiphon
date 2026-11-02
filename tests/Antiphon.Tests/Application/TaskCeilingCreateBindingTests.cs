using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1610 S4a. A declared ceiling reaches the server only through an explicit create: it is
/// validated before any side effect, bound to the Requested columns, echoed, resolved at dispatch,
/// kept by Retry and never inherited. A create without it keeps its handoff digest.
/// </summary>
[Category("Integration")]
public sealed class TaskCeilingCreateBindingTests
{
    // HandoffRequestDigest of the fixed no-ceiling request below, computed at the S4a Code base
    // 5376ac529 (CARD-0824 S1 landed) before any S4a DTO edit (CP-0).
    private const string PinnedBaseDigest = "6ED139471AC87C4438E95882106A33CEB01C0CDA19099C7ECFF9E8F8C682405A";

    private static CreateAgentTaskRequest Request(long? minutes, string? reason, string goal = "c1610 s4a") =>
        new(goal, Role: AgentTaskRole.Review, Workspace: WorkspaceMode.Shared) { CeilingMinutes = minutes, CeilingReason = reason };

    [Test]
    [Arguments("six-hundred", 600)]
    [Arguments("one", 1)]
    [Arguments("cap", 720)]
    public async Task C1610_Create_binds_requested_ceiling(string arm, int minutes)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(Request(minutes, "declared"), kit.Caller, CancellationToken.None);

        var row = (await kit.ReadAsync(created.Id)).Task;
        row.RequestedCeilingMinutes.ShouldBe(minutes, arm);
        row.RequestedCeilingReason.ShouldBe("declared");
        row.ResolvedCeilingMinutes.ShouldBeNull("a declared ceiling resolves only at dispatch");
        created.RequestedCeilingMinutes.ShouldBe(minutes, "the created response echoes the bound value");
    }

    [Test]
    [Arguments("zero")]
    [Arguments("negative")]
    [Arguments("over-cap")]
    [Arguments("overflow")]
    [Arguments("reasonless")]
    [Arguments("blank-reason")]
    [Arguments("reason-only")]
    [Arguments("long-reason")]
    [Arguments("control-char")]
    public async Task C1610_Create_refuses_invalid_ceiling(string arm)
    {
        (long? Minutes, string? Reason, string Field) c = arm switch
        {
            "zero" => (0L, "declared", "ceilingMinutes"),
            "negative" => (-1L, "declared", "ceilingMinutes"),
            "over-cap" => (721L, "declared", "ceilingMinutes"),
            "overflow" => (4_294_967_297L, "declared", "ceilingMinutes"),
            "reasonless" => (600L, null, "ceilingMinutes"),
            "blank-reason" => (600L, "   ", "ceilingMinutes"),
            "reason-only" => (null, "r", "ceilingReason"),
            "long-reason" => (600L, new string('a', 1001), "ceilingMinutes"),
            "control-char" => (600L, "a\u0007b", "ceilingReason"),
            _ => throw new ArgumentOutOfRangeException(nameof(arm), arm, null),
        };
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
        await using var db = kit.Context();

        var ex = await Should.ThrowAsync<ValidationException>(() =>
            kit.Service(db).CreateAsync(Request(c.Minutes, c.Reason), kit.Caller, CancellationToken.None));

        string.Join(",", ex.Errors.Keys).ShouldBe(c.Field, arm);
        (await kit.TaskCountAsync()).ShouldBe(0, "refused before any insert");
        await using var read = kit.Context();
        (await read.AgentTaskEvents.CountAsync()).ShouldBe(0, "refused before any event");
    }

    [Test]
    [Arguments("requested")]
    [Arguments("default")]
    public async Task C1610_Requested_ceiling_resolves_at_dispatch(string arm)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
        kit.Settings.RolePolicy["Review"].TimeoutMinutes = 90;
        await using var db = kit.Context();
        var requested = arm == "requested";
        var created = await kit.Service(db).CreateAsync(
            requested ? Request(600, "long review") : Request(null, null), kit.Caller, CancellationToken.None);

        var row = (await kit.ReadAsync(created.Id)).Task;
        CeilingHoldFacts.Resolve(row, kit.Settings);

        row.ResolvedCeilingMinutes.ShouldBe(requested ? 600 : 90);
        row.ResolvedCeilingSource.ShouldBe(requested ? "requested" : "rolePolicy");
        row.NextCeilingMinutes.ShouldBe(requested ? 600d : 90d);
    }

    [Test]
    [Arguments("explicit")]
    [Arguments("default")]
    public async Task C1610_Retry_keeps_explicit_and_reresolves_default(string arm)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
        kit.Settings.RolePolicy["Review"].TimeoutMinutes = 90;
        var explicitCeiling = arm == "explicit";
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(
            explicitCeiling ? Request(600, "r") : Request(null, null), kit.Caller, CancellationToken.None);
        await using (var fail = kit.Context())
        {
            var dispatched = await fail.AgentTasks.SingleAsync(t => t.Id == created.Id);
            CeilingHoldFacts.Resolve(dispatched, kit.Settings);
            dispatched.Status = AgentTaskStatus.Failed;
            dispatched.DispatchedAt = DateTime.UtcNow.AddMinutes(-5);
            dispatched.FailureReason = "retry me";
            await fail.SaveChangesAsync();
        }

        await using var retryDb = kit.Context();
        await kit.Service(retryDb).RetryAsync(created.Id, CancellationToken.None);

        var retried = (await kit.ReadAsync(created.Id)).Task;
        retried.ResolvedCeilingMinutes.ShouldBeNull();
        retried.ResolvedCeilingSource.ShouldBeNull();
        retried.NextCeilingMinutes.ShouldBeNull();
        retried.RequestedCeilingMinutes.ShouldBe(explicitCeiling ? 600 : null);
        retried.RequestedCeilingReason.ShouldBe(explicitCeiling ? "r" : null);
        kit.Settings.RolePolicy["Review"].TimeoutMinutes = 45;
        CeilingHoldFacts.Resolve(retried, kit.Settings);
        retried.ResolvedCeilingMinutes.ShouldBe(explicitCeiling ? 600 : 45);
        retried.ResolvedCeilingSource.ShouldBe(explicitCeiling ? "requested" : "rolePolicy");
    }

    [Test]
    public void C1610_Absent_ceiling_keeps_handoff_digest()
    {
        var request = new CreateAgentTaskRequest("c1610 digest pin", Title: "pin", Role: AgentTaskRole.Code,
            Workspace: WorkspaceMode.Worktree, ExpectedMinutes: 30);
        AgentTaskService.HandoffRequestDigest(request).ShouldBe(PinnedBaseDigest);

        var element = JsonSerializer.SerializeToElement(request);
        element.TryGetProperty("CeilingMinutes", out _).ShouldBeFalse();
        element.TryGetProperty("CeilingReason", out _).ShouldBeFalse();

        var json = JsonSerializer.Serialize(request);
        var withCeiling = JsonSerializer.Deserialize<CreateAgentTaskRequest>(
            json[..^1] + ",\"CeilingMinutes\":600,\"CeilingReason\":\"r\"}")!;
        AgentTaskService.HandoffRequestDigest(withCeiling).ShouldNotBe(PinnedBaseDigest);
    }

    [Test]
    [Arguments("detail-requested")]
    [Arguments("detail-default")]
    [Arguments("brief-requested")]
    [Arguments("brief-default")]
    public async Task C1610_Detail_and_brief_echo_budget(string arm)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
        var requested = arm.EndsWith("-requested", StringComparison.Ordinal);
        await using var db = kit.Context();
        var created = await kit.Service(db).CreateAsync(
            requested ? Request(600, "long review") : Request(null, null), kit.Caller, CancellationToken.None);

        if (arm.StartsWith("detail-", StringComparison.Ordinal))
        {
            await using var detailDb = kit.Context();
            var detail = await kit.Service(detailDb).GetAsync(created.Id, CancellationToken.None);
            var budget = detail.CeilingBudget.ShouldNotBeNull("the detail always carries the budget");
            if (requested)
                budget.ShouldBe(new CeilingBudgetDto(600, "long review", null, null, null, 720));
            else
            {
                budget.RequestedMinutes.ShouldBeNull();
                budget.RequestedReason.ShouldBeNull();
                budget.AbsoluteCapMinutes.ShouldBe(720);
            }
            return;
        }

        var brief = DelegationReportFormatter.BuildBrief((await kit.ReadAsync(created.Id)).Task, kit.Settings);
        if (requested)
            brief.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
                .Count(l => l == "Declared ceiling: 600 minutes (reason: long review). "
                    + "At it the task is held Working for a decision; nothing is killed.")
                .ShouldBe(1, brief);
        else
            brief.ShouldNotContain("Declared ceiling");
    }

    [Test]
    public async Task C1610_Follow_up_does_not_inherit_ceiling()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var kit = DefaultRunnerKit.Create(schema.ConnectionString, defaultRunnerId: null);
        await using var db = kit.Context();
        var parent = await kit.Service(db).CreateAsync(Request(600, "r", "c1610 parent"), kit.Caller, CancellationToken.None);
        (await kit.ReadAsync(parent.Id)).Task.RequestedCeilingMinutes.ShouldBe(600, "precondition: the parent declared one");
        await using (var done = kit.Context())
        {
            await done.AgentTasks.Where(t => t.Id == parent.Id).ExecuteUpdateAsync(u => u
                .SetProperty(t => t.Status, AgentTaskStatus.Succeeded));
        }

        await using var followDb = kit.Context();
        var follow = await kit.Service(followDb).CreateAsync(
            new CreateAgentTaskRequest("c1610 follow-up", FollowUpOnTask: parent.Id.ToString("D")),
            kit.Caller, CancellationToken.None);

        follow.Id.ShouldNotBe(parent.Id);
        var row = (await kit.ReadAsync(follow.Id)).Task;
        row.RequestedCeilingMinutes.ShouldBeNull();
        row.RequestedCeilingReason.ShouldBeNull();
    }
}
