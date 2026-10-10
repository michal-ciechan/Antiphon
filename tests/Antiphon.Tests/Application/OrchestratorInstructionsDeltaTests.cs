using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class OrchestratorInstructionsDeltaTests
{
    [Test]
    public void Changed_added_and_removed_facts_are_named_per_section()
    {
        var before = Sample();
        var after = before with
        {
            Caps = before.Caps with
            {
                Roles =
                [
                    before.Caps.Roles[0] with { RecommendedInFlight = 5 },
                    before.Caps.Roles[1],
                ],
            },
            Runners =
            [
                before.Runners[0],
                before.Runners[1] with { DispatchEligible = false },
                before.Runners[1] with { RunnerId = "remote-extra" },
            ],
            Defaults = before.Defaults with { GlobalRunnerId = "desktop" },
            Holds = [],
            Pins = [before.Pins[0] with { Candidates = ["sonnet"] }],
        };

        var delta = OrchestratorInstructionsDelta.Format(before, after);
        delta.ShouldContain("caps: Code recommended in-flight 2 → 5");
        delta.ShouldContain("runners: server2 dispatch-eligible yes → no");
        delta.ShouldContain("runners: remote-extra added");
        delta.ShouldContain("defaults: global server2 → desktop");
        delta.ShouldContain("holds: ClaudeCode/fable removed");
        delta.ShouldContain("pins: stage-wide/Code candidates opus → sonnet");
        delta.Split('\n').Length.ShouldBeLessThanOrEqualTo(6);

        var levels = OrchestratorInstructionsDelta.Format(
            before,
            before with
            {
                Levels = [before.Levels[0] with { Alias = "opus" }],
                StandingLines = ["a fresh line"],
            });
        levels.ShouldContain("levels: ClaudeCode/Frontier fable → opus");
        levels.ShouldContain("standing: prefer the live file removed");
        levels.ShouldContain("standing: a fresh line added");
    }

    [Test]
    public void Equal_snapshots_and_occupancy_only_differences_produce_no_delta()
    {
        var snapshot = Sample();
        OrchestratorInstructionsDelta.Format(snapshot, snapshot).ShouldBe("");
        var occupied = snapshot with
        {
            Runners = [snapshot.Runners[0] with { Occupied = 99, ObservedAt = "later" }, snapshot.Runners[1]],
        };
        OrchestratorInstructionsDelta.Format(snapshot, occupied).ShouldBe("");
    }

    [Test]
    public void Line_and_length_caps_keep_six_facts_then_a_more_marker()
    {
        var before = Sample();
        var roles = Enumerable.Range(0, 12).Select(i =>
            new RoleInstructionRow("Role" + i, 1, "High", null, null)).ToList();
        var changed = Enumerable.Range(0, 12).Select(i =>
            new RoleInstructionRow("Role" + i, 2, "High", null, null)).ToList();
        var left = before with { Caps = before.Caps with { Roles = roles } };
        var right = before with { Caps = before.Caps with { Roles = changed } };
        var delta = OrchestratorInstructionsDelta.Format(left, right);
        var lines = delta.Split('\n');
        lines.Length.ShouldBeLessThanOrEqualTo(7);
        lines[^1].ShouldStartWith("+");
        lines[^1].ShouldContain("more");
        foreach (var line in lines)
            line.Length.ShouldBeLessThanOrEqualTo(120);
        delta.Length.ShouldBeLessThanOrEqualTo(700);
    }

    [Test]
    public void Notice_body_names_versions_path_route_refine_and_no_reply_and_stays_under_700_chars()
    {
        var before = Sample();
        var after = before with
        {
            Caps = before.Caps with
            {
                Roles =
                [
                    before.Caps.Roles[0] with { RecommendedInFlight = 5 },
                    before.Caps.Roles[1],
                ],
            },
        };
        var delta = OrchestratorInstructionsDelta.Format(before, after);
        var path = "/tmp/ANTIPHON_ORCHESTRATOR_INSTRUCTIONS.md";
        var url = "http://localhost:17202/api/orchestrator-instructions";
        var body = ChannelPreamble.OrchestratorInstructionsChangedBody(
            delta, path, url, oldVersion: "1a2b3c4d", newVersion: "5e6f7a8b");
        body.ShouldContain("v1a2b3c4d");
        body.ShouldContain("v5e6f7a8b");
        body.ShouldContain(path);
        body.ShouldContain("GET /api/orchestrator-instructions");
        body.ShouldContain("-Refine");
        body.ShouldContain("NO_REPLY");
        body.Length.ShouldBeLessThanOrEqualTo(700);

        var repeated = ChannelPreamble.OrchestratorInstructionsChangedBody(
            delta, path, url, changedMoreThanOnceSince: "aaaa1111");
        repeated.ShouldContain("changed more than once since vaaaa1111");
        repeated.Length.ShouldBeLessThanOrEqualTo(700);
    }

    private static OrchestratorInstructionsSnapshot Sample() => new(
        Caps: new PipelineCapsSection(
            2, "configured", 6, "Worktree", "High",
            [
                new RoleInstructionRow("Code", 2, "Frontier", null, "ClaudeCode"),
                new RoleInstructionRow("Review", 2, "Frontier", null, null),
            ]),
        Runners:
        [
            new RunnerInstructionRow("desktop", "windows", true, 2, 2, "configured", false, false, ["pty"]),
            new RunnerInstructionRow("server2", "linux", true, 10, 10, "declared", false, false, ["pty"]),
        ],
        Defaults: new RunnerDefaultsSection(2, "Human", "server2", [], "operator moved the default"),
        Holds: [new HoldInstructionRow("ClaudeCode", "fable", "Manual", "2099-01-01T00:00:00Z", "session limit")],
        Pins:
        [
            new PinInstructionRow(true, null, null, "Code", ["opus"], [], "require", "human", null, null, "stage pin", DateTimeOffset.Parse("2026-10-01T00:00:00Z")),
        ],
        Levels: [new LevelInstructionRow("ClaudeCode", "Frontier", "fable")],
        StandingLines: ["prefer the live file"]);
}
