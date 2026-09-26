using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class HostStatsAntiphonCountersTests
{
    private static IReadOnlyList<HostStatsTaskRow> Rows =>
    [
        new(AgentTaskRole.Code, AgentKind.ClaudeCode, null, AgentTaskStatus.Working, false, false),
        new(AgentTaskRole.Code, AgentKind.ClaudeCode, "", AgentTaskStatus.Dispatched, false, false),
        new(AgentTaskRole.Review, AgentKind.Codex, null, AgentTaskStatus.Working, false, false),
        new(AgentTaskRole.Plan, AgentKind.ClaudeCode, null, AgentTaskStatus.Queued, true, false),
        new(AgentTaskRole.Investigate, AgentKind.ClaudeCode, null, AgentTaskStatus.Queued, false, false),
        new(AgentTaskRole.Code, AgentKind.ClaudeCode, null, AgentTaskStatus.Blocked, false, true),
        new(AgentTaskRole.Mutation, AgentKind.Grok, "server2", AgentTaskStatus.Working, false, false),
        new(AgentTaskRole.Review, AgentKind.Grok, "server2", AgentTaskStatus.Blocked, false, false),
    ];

    [Test]
    public void Group_counts_in_flight_by_stage_and_kind_per_host()
    {
        var grouped = HostStatsAntiphonCounters.Group(Rows);
        grouped["desktop"].ByStage["Code"].ShouldBe(2);
        grouped["desktop"].ByStage["Review"].ShouldBe(1);
        grouped["desktop"].ByKind["Codex"].ShouldBe(1);
        grouped["server2"].ByStage["Mutation"].ShouldBe(1);
        grouped["server2"].ByKind["Grok"].ShouldBe(1);
    }

    [Test]
    public void Held_and_lands_count_only_their_flags()
    {
        var grouped = HostStatsAntiphonCounters.Group(Rows);
        grouped["desktop"].Queued.ShouldBe(2);
        grouped["desktop"].Held.ShouldBe(1);
        grouped["desktop"].LandsPending.ShouldBe(1);
        grouped["server2"].LandsPending.ShouldBe(0);
    }

    [Test]
    public void Null_or_empty_runner_is_desktop_and_blocked_is_open_not_in_flight()
    {
        var grouped = HostStatsAntiphonCounters.Group(Rows);
        grouped["desktop"].TasksInFlight.ShouldBe(3);
        grouped["server2"].TasksInFlight.ShouldBe(1);
    }
}
