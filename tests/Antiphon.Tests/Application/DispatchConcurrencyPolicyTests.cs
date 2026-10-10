using System.Text.Json;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>CARD-0505 V-1. Pure policy decisions against the production resolver.</summary>
[Category("Unit")]
public class DispatchConcurrencyPolicyTests
{
    private static readonly DateTime At = DispatchConcurrencyTestHost.SeedInstant.UtcDateTime;

    [Test]
    public async Task Legacy_counts_queued_as_open()
    {
        var policy = Resolve("""{"maxParallel":3,"roles":{"Plan":{"maxParallel":1}}}""");
        var queuedPlan = Rows((AgentTaskRole.Plan, AgentTaskStatus.Queued, 1));
        var refused = DispatchConcurrencyPolicy.DecideCreate(policy, Count(queuedPlan), AgentTaskRole.Plan, false);
        refused.Admit.ShouldBeFalse("legacy-queued-refusal");
        refused.Population.ShouldBe("open", "legacy-queued-refusal");
        refused.Axis.ShouldBe("role", "legacy-queued-refusal");
        refused.Count.ShouldBe(1, "legacy-queued-refusal");
        refused.Limit.ShouldBe(1, "legacy-queued-refusal");
        refused.Exceeded.ShouldAllBe(item => item.Population == "open", "legacy-queued-refusal");
        policy.MaxQueued.ShouldBeNull("legacy-queued-refusal");

        var full = Rows(
            (AgentTaskRole.Custom, AgentTaskStatus.Queued, 1),
            (AgentTaskRole.Custom, AgentTaskStatus.Dispatched, 2),
            (AgentTaskRole.Custom, AgentTaskStatus.Working, 3));
        var absolute = DispatchConcurrencyPolicy.DecideCreate(policy, Count(full), AgentTaskRole.Custom, false);
        absolute.Admit.ShouldBeFalse("legacy-queued-refusal");
        absolute.Population.ShouldBe("open", "legacy-queued-refusal");
        absolute.Axis.ShouldBe("absolute", "legacy-queued-refusal");
        absolute.Count.ShouldBe(3, "legacy-queued-refusal");
        absolute.Limit.ShouldBe(3, "legacy-queued-refusal");

        var admitted = DispatchConcurrencyPolicy.DecideCreate(
            policy, Count(full.Take(2).ToList()), AgentTaskRole.Custom, false);
        admitted.Admit.ShouldBeTrue("legacy-queued-refusal");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Separate_counts_parallel_and_queue_independently()
    {
        var settings = DispatchConcurrencyTestHost.BoundSettings();
        settings.RolePolicy["Plan"].RecommendedInFlight = null;
        var policy = Resolve(
            """{"mode":"SeparateQueues","maxParallel":3,"maxQueued":4,"roles":{"Code":{"maxParallel":2,"maxQueued":3}}}""",
            settings);
        var running = Rows(
            (AgentTaskRole.Code, AgentTaskStatus.Working, 1),
            (AgentTaskRole.Code, AgentTaskStatus.Dispatched, 2),
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 3),
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 4));
        var create = DispatchConcurrencyPolicy.DecideCreate(policy, Count(running), AgentTaskRole.Code, false);
        create.Admit.ShouldBeTrue();
        var hold = DispatchConcurrencyPolicy.DecideDispatch(policy, Count(running), AgentTaskRole.Code);
        hold.Admit.ShouldBeFalse();
        hold.Population.ShouldBe("parallel");
        hold.Axis.ShouldBe("role");
        hold.Count.ShouldBe(2);
        hold.Limit.ShouldBe(2);
        hold.Remaining.ShouldBe(0);

        var queued = Rows(
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 1),
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 2),
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 3));
        var queue = DispatchConcurrencyPolicy.DecideCreate(policy, Count(queued), AgentTaskRole.Code, false);
        queue.Admit.ShouldBeFalse();
        queue.Population.ShouldBe("queued");
        queue.Count.ShouldBe(3);
        queue.Limit.ShouldBe(3);
        queue.Remaining.ShouldBe(0);

        var room = DispatchConcurrencyPolicy.DecideDispatch(policy, Count(running), AgentTaskRole.Plan);
        room.Admit.ShouldBeTrue();
        var saturated = running.Append(Row(AgentTaskRole.Plan, AgentTaskStatus.Working, 5)).ToList();
        var held = DispatchConcurrencyPolicy.DecideDispatch(policy, Count(saturated), AgentTaskRole.Plan);
        held.Admit.ShouldBeFalse();
        held.Axis.ShouldBe("absolute");
        held.Population.ShouldBe("parallel");
        held.Count.ShouldBe(3);
        held.Limit.ShouldBe(3);
        held.Remaining.ShouldBe(0);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Project_then_global_then_seed()
    {
        var seed = DispatchConcurrencyPolicy.ImportSeed(DispatchConcurrencyTestHost.BoundSettings(), At);
        var global = Parse("""{"maxParallel":8,"roles":{"Code":{"maxParallel":4}}}""");
        var project = Parse("""{"roles":{"Code":{"maxParallel":2}}}""");
        var resolved = DispatchConcurrencyPolicy.Resolve(seed, global, project, 2, 1);
        resolved.MaxParallel.ShouldBe(8, "precedence");
        resolved.ParallelSource.ShouldBe("global", "precedence");
        resolved.Role(AgentTaskRole.Code).MaxParallel.ShouldBe(2, "precedence");
        resolved.Role(AgentTaskRole.Code).ParallelSource.ShouldBe("project", "precedence");
        resolved.Role(AgentTaskRole.Plan).MaxParallel.ShouldBe(3, "precedence");
        resolved.Role(AgentTaskRole.Plan).ParallelSource.ShouldBe("default", "precedence");

        var clearedGlobal = DispatchConcurrencyPolicy.Resolve(seed, Parse("{}"), project, 3, 1);
        clearedGlobal.MaxParallel.ShouldBe(9, "precedence");
        clearedGlobal.ParallelSource.ShouldBe("default", "precedence");
        clearedGlobal.Role(AgentTaskRole.Code).MaxParallel.ShouldBe(2, "precedence");
        clearedGlobal.Role(AgentTaskRole.Code).ParallelSource.ShouldBe("project", "precedence");

        var clearedBoth = DispatchConcurrencyPolicy.Resolve(seed, Parse("{}"), Parse("{}"), 3, 2);
        clearedBoth.MaxParallel.ShouldBe(9, "precedence");
        clearedBoth.Role(AgentTaskRole.Code).MaxParallel.ShouldBe(5, "precedence");
        clearedBoth.Role(AgentTaskRole.Code).ParallelSource.ShouldBe("default", "precedence");
        clearedBoth.Role(AgentTaskRole.Review).MaxParallel.ShouldBe(4, "precedence");
        clearedBoth.Role(AgentTaskRole.Custom).MaxParallel.ShouldBeNull("precedence");
        clearedBoth.Roles.ShouldAllBe(role => role.ParallelSource == "default" && role.QueuedSource == "default", "precedence");

        var projectClearedFirst = DispatchConcurrencyPolicy.Resolve(seed, global, Parse("{}"), 2, 2);
        projectClearedFirst.MaxParallel.ShouldBe(8, "precedence");
        projectClearedFirst.Role(AgentTaskRole.Code).MaxParallel.ShouldBe(4, "precedence");
        projectClearedFirst.Role(AgentTaskRole.Code).ParallelSource.ShouldBe("global", "precedence");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Missing_inherits_null_is_unbounded_zero_pauses_queue()
    {
        var seed = DispatchConcurrencyPolicy.ImportSeed(DispatchConcurrencyTestHost.BoundSettings(), At);
        var emptyRole = DispatchConcurrencyPolicy.Resolve(seed, Parse("""{"roles":{"Code":{}}}"""), null, 1, 0);
        emptyRole.Role(AgentTaskRole.Code).MaxParallel.ShouldBe(5, "null-not-absent");
        emptyRole.Role(AgentTaskRole.Code).ParallelSource.ShouldBe("default", "null-not-absent");

        var explicitNull = DispatchConcurrencyPolicy.Resolve(
            seed, Parse("""{"roles":{"Code":{"maxParallel":null}}}"""), null, 1, 0);
        explicitNull.Role(AgentTaskRole.Code).MaxParallel.ShouldBeNull("null-not-absent");
        explicitNull.Role(AgentTaskRole.Code).ParallelSource.ShouldBe("global", "null-not-absent");
        explicitNull.Role(AgentTaskRole.Plan).MaxParallel.ShouldBe(3, "null-not-absent");

        var paused = DispatchConcurrencyPolicy.Resolve(seed, Parse("""{"maxQueued":0,"maxParallel":2}"""), null, 1, 0);
        var empty = DispatchConcurrencyPolicy.DecideCreate(paused, Count([]), AgentTaskRole.Plan, false);
        empty.Admit.ShouldBeFalse("zero-pauses");
        empty.Population.ShouldBe("queued", "zero-pauses");
        empty.Count.ShouldBe(0, "zero-pauses");
        empty.Limit.ShouldBe(0, "zero-pauses");

        var unboundedRole = DispatchConcurrencyPolicy.Resolve(
            seed, Parse("""{"maxParallel":2,"roles":{"Custom":{"maxParallel":null}}}"""), null, 1, 0);
        var two = Rows(
            (AgentTaskRole.Plan, AgentTaskStatus.Working, 1),
            (AgentTaskRole.Review, AgentTaskStatus.Working, 2));
        var held = DispatchConcurrencyPolicy.DecideDispatch(
            Separate(unboundedRole), Count(two), AgentTaskRole.Custom);
        held.Admit.ShouldBeFalse("null-not-absent");
        held.Axis.ShouldBe("absolute", "null-not-absent");
        held.Count.ShouldBe(2, "null-not-absent");
        held.Limit.ShouldBe(2, "null-not-absent");
        await Task.CompletedTask;
    }

    [Test]
    public async Task Queue_failure_precedes_legacy_override()
    {
        var policy = Resolve(
            """{"maxParallel":3,"maxQueued":2,"roles":{"Code":{"maxParallel":3,"maxQueued":2}}}""");
        var saturated = Rows(
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 1),
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 2),
            (AgentTaskRole.Code, AgentTaskStatus.Working, 3));
        foreach (var ignore in new[] { false, true })
        {
            var decision = DispatchConcurrencyPolicy.DecideCreate(policy, Count(saturated), AgentTaskRole.Code, ignore);
            decision.Admit.ShouldBeFalse();
            decision.Population.ShouldBe("queued");
            decision.Axis.ShouldBe("absolute");
            decision.CanOverride.ShouldBeFalse();
            decision.Exceeded.Select(item => (item.Population, item.Axis)).ShouldBe(
            [
                ("queued", "absolute"),
                ("queued", "role"),
                ("open", "absolute"),
                ("open", "role"),
            ]);
        }

        var openOnly = Rows(
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 1),
            (AgentTaskRole.Code, AgentTaskStatus.Working, 2),
            (AgentTaskRole.Code, AgentTaskStatus.Dispatched, 3));
        var blocked = DispatchConcurrencyPolicy.DecideCreate(policy, Count(openOnly), AgentTaskRole.Code, false);
        blocked.Admit.ShouldBeFalse();
        blocked.Population.ShouldBe("open");
        blocked.CanOverride.ShouldBeTrue();
        var bypassed = DispatchConcurrencyPolicy.DecideCreate(policy, Count(openOnly), AgentTaskRole.Code, true);
        bypassed.Admit.ShouldBeTrue();
        await Task.CompletedTask;
    }

    [Test]
    public async Task Absolute_precedes_role_and_reports_all_failures()
    {
        var policy = Resolve("""{"maxParallel":3,"roles":{"Code":{"maxParallel":2}}}""");
        var mixed = Rows(
            (AgentTaskRole.Code, AgentTaskStatus.Queued, 1),
            (AgentTaskRole.Code, AgentTaskStatus.Working, 2),
            (AgentTaskRole.Plan, AgentTaskStatus.Queued, 3));
        var absolute = DispatchConcurrencyPolicy.DecideCreate(policy, Count(mixed), AgentTaskRole.Code, false);
        absolute.Axis.ShouldBe("absolute");
        absolute.Exceeded.ShouldContain(item => item.Axis == "role" && item.Role == "Code");
        absolute.TotalOccupants.ShouldBe(3);

        var raised = Resolve("""{"maxParallel":4,"roles":{"Code":{"maxParallel":2}}}""");
        var role = DispatchConcurrencyPolicy.DecideCreate(raised, Count(mixed), AgentTaskRole.Code, false);
        role.Axis.ShouldBe("role");
        role.RolePrimary().ShouldBe("Code");

        var above = Resolve("""{"maxParallel":3,"roles":{"Code":{"maxParallel":5}}}""");
        above.Role(AgentTaskRole.Code).MaxParallel.ShouldBe(5);
        above.CombinedParallel(AgentTaskRole.Code).ShouldBe(3);
        var identities = DispatchConcurrencyPolicy.DecideCreate(policy, Count(mixed), AgentTaskRole.Code, false).Exceeded
            .Select(item => $"{item.Population}:{item.Axis}:{item.Role}:{item.Count}:{item.Limit}")
            .ToArray();
        identities.ShouldBe(["open:absolute::3:3", "open:role:Code:2:2"]);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Status_and_specialist_populations_match_contract()
    {
        var project = Guid.NewGuid();
        var other = Guid.NewGuid();
        var rows = new List<TaskPopulationRow>();
        var stamp = 0;
        foreach (var role in DispatchConcurrencyPolicy.OrdinaryRoles.Concat(
                     new[] { AgentTaskRole.Check, AgentTaskRole.Distill, AgentTaskRole.Diagnose }))
        {
            foreach (var status in Enum.GetValues<AgentTaskStatus>())
                rows.Add(Row(role, status, stamp++, project));
        }

        foreach (var copy in new Guid?[] { other, null })
        {
            rows.Add(Row(AgentTaskRole.Code, AgentTaskStatus.Queued, stamp++, copy));
            rows.Add(Row(AgentTaskRole.Code, AgentTaskStatus.Working, stamp++, copy));
        }

        var population = DispatchConcurrencyPolicy.Count(rows, project);
        population.Open.ShouldBe(42);
        population.Parallel.ShouldBe(28);
        population.Queued.ShouldBe(14);
        foreach (var role in DispatchConcurrencyPolicy.OrdinaryRoles)
        {
            population.Roles[role].Open.ShouldBe(3);
            population.Roles[role].Parallel.ShouldBe(2);
            population.Roles[role].Queued.ShouldBe(1);
        }

        rows.Add(Row(AgentTaskRole.Code, AgentTaskStatus.Working, stamp++, project, retained: true));
        rows.Add(Row(AgentTaskRole.Check, AgentTaskStatus.Working, stamp++, project, retained: true));
        var withRetained = DispatchConcurrencyPolicy.Count(rows, project);
        withRetained.Parallel.ShouldBe(29);
        withRetained.Open.ShouldBe(43);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Validate_modes_roles_fields_and_bounds()
    {
        Accept("""{"maxParallel":1}""");
        Accept("""{"maxParallel":512}""");
        Accept("""{"maxQueued":0}""");
        Accept("""{"maxQueued":4096}""");
        Accept("""{"maxQueued":null}""");
        Accept("""{"roles":{"Code":{"maxParallel":null}}}""");
        var roles = string.Join(",", DispatchConcurrencyPolicy.OrdinaryRoles.Select(role => $"\"{role}\":{{}}"));
        Accept("{\"roles\":{" + roles + "}}");

        Reject("""{"maxParallel":0}""", "invalid-policy-refused");
        Reject("""{"maxParallel":-1}""", "invalid-policy-refused");
        Reject("""{"maxParallel":513}""", "invalid-policy-refused");
        Reject("""{"maxQueued":-1}""", "invalid-policy-refused");
        Reject("""{"maxQueued":4097}""", "invalid-policy-refused");
        Reject("""{"maxParallel":null}""", "invalid-policy-refused");
        Reject("""{"maxParallel":1.5}""", "invalid-policy-refused");
        Reject("""{"maxParallel":"1"}""", "invalid-policy-refused");
        Reject("""{"maxParallel":true}""", "invalid-policy-refused");
        Reject("""{"maxParallel":[1]}""", "invalid-policy-refused");
        Reject("""{"mode":null}""", "invalid-policy-refused");
        Reject("""{"mode":1}""", "invalid-policy-refused");
        Reject("""{"mode":"WideOpen"}""", "invalid-policy-refused");
        Reject("""{"roles":{"Check":{"maxParallel":1}}}""", "invalid-policy-refused");
        Reject("""{"roles":{"Distill":{}}}""", "invalid-policy-refused");
        Reject("""{"roles":{"Diagnose":{}}}""", "invalid-policy-refused");
        Reject("""{"roles":{"Wizard":{}}}""", "invalid-policy-refused");
        Reject("""{"roles":{"2":{"maxParallel":1}}}""", "invalid-policy-refused");
        Reject("""{"roles":{"Code":{"maxParallel":1},"Code":{"maxParallel":2}}}""", "invalid-policy-refused");
        Reject("""{"maxParallel":1,"maxParallel":2}""", "invalid-policy-refused");
        Reject("""{"extra":1}""", "invalid-policy-refused");
        Reject("""{"schemaVersion":2}""", "invalid-policy-refused");
        Accept("""{"schemaVersion":1,"mode":"LegacyOpen"}""");
        await Task.CompletedTask;
    }

    private static EffectivePolicy Resolve(string overrides, DelegationSettings? settings = null)
    {
        var seed = DispatchConcurrencyPolicy.ImportSeed(settings ?? DispatchConcurrencyTestHost.BoundSettings(), At);
        return DispatchConcurrencyPolicy.Resolve(seed, Parse(overrides), null, 1, 0);
    }

    private static EffectivePolicy Separate(EffectivePolicy policy) =>
        policy with { Mode = DispatchConcurrencyMode.SeparateQueues, ModeSource = "global" };

    private static DispatchConcurrencyDocument Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        DispatchConcurrencyPolicy.TryParseOverrides(document.RootElement, out var parsed).ShouldBeTrue(json);
        return parsed;
    }

    private static void Accept(string json)
    {
        using var document = JsonDocument.Parse(json);
        DispatchConcurrencyPolicy.ValidateOverrides(document.RootElement).ShouldBeEmpty(json);
    }

    private static void Reject(string json, string message)
    {
        using var document = JsonDocument.Parse(json);
        DispatchConcurrencyPolicy.ValidateOverrides(document.RootElement).ShouldNotBeEmpty(message);
    }

    private static PopulationSnapshot Count(IReadOnlyList<TaskPopulationRow> rows) =>
        DispatchConcurrencyPolicy.Count(rows, null);

    private static List<TaskPopulationRow> Rows(params (AgentTaskRole Role, AgentTaskStatus Status, int Order)[] rows) =>
        rows.Select(row => Row(row.Role, row.Status, row.Order, null)).ToList();

    private static TaskPopulationRow Row(
        AgentTaskRole role, AgentTaskStatus status, int order, Guid? project = null, bool retained = false) =>
        new(Guid.NewGuid(), project, role, status, At.AddSeconds(order), retained, $"{role}-{status}-{order}");
}

file static class DispatchDecisionAssertions
{
    public static string? RolePrimary(this DispatchDecision decision) =>
        decision.Exceeded.First(item => item.Axis == "role").Role;
}
