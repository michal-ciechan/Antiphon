using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1320 S1. Section 9 is the stored setting. An unused list does not change the file.
/// </summary>
[Category("Unit")]
public sealed class KeepSixProjectionTests
{
    [Test]
    public void all_off_omits_section_9_and_leaves_the_rendered_body_unchanged()
    {
        var bare = Sample();
        var empty = bare with { KeepSix = [] };
        var allOff = bare with
        {
            KeepSix =
            [
                new KeepSixInstructionRow(Guid.NewGuid(), "Ada", false, 6, null, null),
                new KeepSixInstructionRow(Guid.NewGuid(), "Bea", false, 6, null, null),
            ],
        };

        var bareBody = OrchestratorInstructionsRenderer.Render(bare);
        OrchestratorInstructionsRenderer.Render(empty).ShouldBe(bareBody);
        OrchestratorInstructionsRenderer.Render(allOff).ShouldBe(bareBody);
        bareBody.ShouldNotContain("## 9");
    }

    [Test]
    public void an_enabled_row_appends_section_9_after_section_8_without_live_counts()
    {
        var snapshot = Sample() with
        {
            KeepSix = [new KeepSixInstructionRow(Guid.NewGuid(), "Ada", true, 7, "quota", "2026-10-20T00:00:00Z")],
        };
        var body = OrchestratorInstructionsRenderer.Render(snapshot);
        var section8 = body.IndexOf("## 8. Operator standing instructions", StringComparison.Ordinal);
        var section9 = body.IndexOf("## 9. In-flight target", StringComparison.Ordinal);
        section8.ShouldBeGreaterThanOrEqualTo(0);
        section9.ShouldBeGreaterThan(section8);
        body[section9..].TrimEnd().ShouldBe("""
            ## 9. In-flight target
            - Ada: on; target 7; hold quota until 2026-10-20T00:00:00Z
            Off: 0
            """);
        body[section9..].ShouldNotContain("shortfall");
        body[section9..].ShouldNotContain("countedOpen");
    }

    [Test]
    public void delta_names_the_orchestrator_when_the_setting_changes_and_is_empty_when_only_occupancy_changes()
    {
        var runner = new RunnerInstructionRow(
            "desktop", "windows", true, 2, 2, "configured", false, false, ["pty"], Occupied: 1, ObservedAt: "t0");
        var before = Sample() with { Runners = [runner] };
        var ada = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var enabled = before with
        {
            KeepSix = [new KeepSixInstructionRow(ada, "Ada", true, 6, null, null)],
        };

        OrchestratorInstructionsDelta.Format(before, enabled)
            .ShouldBe("keep-six: Ada on target 6 hold none until none");

        var occupied = enabled with
        {
            Runners = [runner with { Occupied = 99, ObservedAt = "later" }],
        };
        OrchestratorInstructionsDelta.Format(enabled, occupied).ShouldBe("");
    }

    [Test]
    public void a_hold_is_rendered_and_an_off_count_is_one_line()
    {
        var snapshot = Sample() with
        {
            KeepSix =
            [
                new KeepSixInstructionRow(Guid.NewGuid(), "Ada", true, 6, null, null),
                new KeepSixInstructionRow(Guid.NewGuid(), "Bea", false, 6, "maintenance", "2026-10-21T00:00:00Z"),
                new KeepSixInstructionRow(Guid.NewGuid(), "Cal", false, 6, null, null),
                new KeepSixInstructionRow(Guid.NewGuid(), "Dee", false, 6, null, null),
            ],
        };
        var body = OrchestratorInstructionsRenderer.Render(snapshot);
        var section9 = body.IndexOf("## 9. In-flight target", StringComparison.Ordinal);
        section9.ShouldBeGreaterThanOrEqualTo(0);
        var section = body[section9..].TrimEnd();
        section.ShouldBe("""
            ## 9. In-flight target
            - Ada: on; target 6; hold none until none
            - Bea: off; target 6; hold maintenance until 2026-10-21T00:00:00Z
            Off: 2
            """);
        section.Split('\n').Count(line => line.StartsWith("Off:", StringComparison.Ordinal)).ShouldBe(1);
        section.ShouldNotContain("Cal");
        section.ShouldNotContain("Dee");
    }

    private static OrchestratorInstructionsSnapshot Sample() => new(
        new PipelineCapsSection(2, "configured", 6, "Worktree", "High", []),
        [],
        new RunnerDefaultsSection(1, "Human", null, [], null),
        [],
        [],
        [],
        ["prefer the live file"]);
}
