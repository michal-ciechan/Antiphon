using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// Operator 2026-11-07: per-task Final does not include the whole Unit lane. Nightly and rc own
/// that lane. This witness fails while a task-facing instruction or template still requires it.
/// Nightly and rc documents on <see cref="Allowlist"/> may still describe the lane as their run.
/// PC-1 restores the sentence "ordinary scope: the whole Unit lane" in
/// <c>DelegationReportFormatter.VerificationProfileBlock</c>.
/// Restoring "Unit plus the named affected integration classes" on the stage-code SCOPE line is the same failure.
/// </summary>
[Category("Unit")]
public sealed class PerTaskFinalUnitLaneWitnessTests
{
    private const string Scope =
        "the plan's closed checkpoint rows, the touched and named classes, the batched guards (policy sweep, mechanical contract, TaskPlatformGuidanceTests, layering rows), evidence-diff, merge-tree, and the red/green plus mutant evidence";

    private const string Ownership =
        "The whole Unit lane is owned by the nightly backstop and the rc qualification run, not by per-task Final; a failure there gets a Debug task under the rc run-group repair loop.";

    private static readonly string[] RequiringPhrases =
    [
        "Final (default, first round): whole Unit lane",
        "ordinary scope: the whole Unit lane",
        "Execute the Unit lane.",
        "runs the Unit lane",
        "run the Unit lane",
        "Whole-Unit and affected-class ordinary V/R stay in Final.",
        "(a whole Unit lane)",
        "Unit plus the named affected integration classes",
    ];

    /// <summary>Task-facing instructions and templates. Historical plans and investigations are not in this list.</summary>
    private static readonly string[] TaskFacing =
    [
        "server/Bundles/stage-code.md",
        "server/Bundles/stage-review.md",
        "server/Bundles/orchestrator.md",
        "server/Bundles/delegate-basics.md",
        "server/Application/Core/DelegationReportFormatter.cs",
        "docs/orchestration-loop.md",
        "docs/testing-and-build.md",
        "docs/release-gates.md",
        "TESTING.md",
        "AGENTS.md",
        ".claude/skills/antiphon-orchestrator/SKILL.md",
        ".claude/skills/antiphon-delegate/SKILL.md",
    ];

    /// <summary>Nightly and rc docs. They may name the lane as their own run and are not scanned.</summary>
    private static readonly string[] Allowlist =
    [
        "docs/nightly-watchdog.md",
        ".claude/skills/antiphon-rc-qualification/SKILL.md",
    ];

    private static readonly string[] ScopeOwners =
    [
        "server/Application/Core/DelegationReportFormatter.cs",
        "docs/orchestration-loop.md",
        "docs/testing-and-build.md",
    ];

    [Test]
    public void per_task_final_does_not_require_the_whole_unit_lane()
    {
        foreach (var relative in Allowlist)
        {
            TaskFacing.ShouldNotContain(relative, "nightly/rc allowlist is outside the task-facing scan");
            File.Exists(Path.Combine(DelegateScriptRunner.RepoRoot, relative))
                .ShouldBeTrue($"allowlist path missing: {relative}");
        }

        var hits = new List<string>();
        foreach (var relative in TaskFacing)
        {
            var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));
            foreach (var phrase in RequiringPhrases)
            {
                if (text.Contains(phrase, StringComparison.Ordinal))
                    hits.Add($"{relative}: {phrase}");
            }
        }

        hits.ShouldBeEmpty("a task-facing instruction still requires the whole Unit lane in per-task Final");

        foreach (var relative in ScopeOwners)
        {
            var text = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, relative));
            text.ShouldContain(Scope, Case.Sensitive, relative);
            text.ShouldContain(Ownership, Case.Sensitive, relative);
        }

        var stage = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "server/Bundles/stage-code.md"));
        stage.ShouldContain("closed rows, named classes, not the whole lane", Case.Sensitive, "stage-code Final clause");
        stage.ShouldNotContain("whole Unit lane", Case.Sensitive, "stage-code must not require the lane");
    }
}
