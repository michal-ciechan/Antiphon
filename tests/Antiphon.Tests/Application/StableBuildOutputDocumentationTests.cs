using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1629 step 1 (operator decision H-3, 2026-11-02): a direct (non-checkpoint) build uses its
/// worktree's NATIVE default output, the project's own bin/ and obj/, with no -o, --output or
/// OutputPath. A new output name per build recompiled 97% of a cold build (docs/investigations
/// 2026-10-31), and the native output is already stable per worktree. The standing guidance must not
/// ask for a fresh output per build, must not teach a custom output for ordinary direct builds, and
/// must state the native-output and one-build-at-a-time rules. Checkpoint and land verification
/// builds keep their own cold outputs.
/// </summary>
[Category("Unit")]
public class StableBuildOutputDocumentationTests
{
    private static readonly Regex FreshOutputPerBuild = new(
        @"fresh\s+(output\s+(directory|dir|name|path)|bin-)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A custom output taught for ordinary direct builds: the retired -StableOutput helper and its
    // per-task bin-<task>/ name, or the old "alternate output path" bullet. Checkpoint rows keep
    // their own bin-<id>/ outputs, so a bare bin-< placeholder is not itself a defect.
    private static readonly Regex CustomDirectBuildOutput = new(
        @"StableOutput|OutputSuffix|bin-<task|alternate\s+output\s+path",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Any concrete OutputPath value: the delegate bundle and the skills never teach one.
    private static readonly Regex AnyOutputPathValue = new(
        @"OutputPath\s*[=:]\s*[^\s`,;)]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Test]
    [Arguments("AGENTS.md")]
    [Arguments("TESTING.md")]
    [Arguments("docs/testing-and-build.md")]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    [Arguments("server/Bundles/orchestrator.md")]
    [Arguments("server/Bundles/stage-code.md")]
    [Arguments("server/Bundles/stage-review.md")]
    [Arguments("server/Bundles/stage-mutation.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    [Arguments("scripts/build-slot.ps1")]
    public void C1629_standing_guidance_no_longer_asks_for_a_fresh_output_name(string relative)
    {
        var text = Read(relative);
        var hits = FreshOutputPerBuild.Matches(text).Select(match => match.Value).ToList();
        hits.ShouldBeEmpty($"{relative} still asks for a fresh output directory per build");
    }

    [Test]
    [Arguments("AGENTS.md")]
    [Arguments("TESTING.md")]
    [Arguments("docs/testing-and-build.md")]
    [Arguments("docs/orchestration-loop.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    [Arguments("server/Bundles/orchestrator.md")]
    [Arguments("server/Bundles/stage-code.md")]
    [Arguments("server/Bundles/stage-review.md")]
    [Arguments("server/Bundles/stage-mutation.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    [Arguments("scripts/build-slot.ps1")]
    [Arguments("scripts/lib/build-slot.ps1")]
    public void C1629_standing_guidance_does_not_teach_a_custom_output_for_direct_builds(string relative)
    {
        var text = Collapse(Read(relative));
        var hits = CustomDirectBuildOutput.Matches(text).Select(match => match.Value).ToList();
        hits.ShouldBeEmpty($"{relative} still teaches a custom output for ordinary direct builds");
    }

    [Test]
    [Arguments("TESTING.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    public void C1629_delegate_facing_guidance_names_no_output_path_value(string relative)
    {
        var text = Read(relative);
        var hits = AnyOutputPathValue.Matches(text).Select(match => match.Value).ToList();
        hits.ShouldBeEmpty($"{relative} names an OutputPath value for an ordinary build");
    }

    [Test]
    [Arguments("docs/testing-and-build.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    public void C1629_build_guidance_states_the_native_output_and_one_build_at_a_time(string relative)
    {
        var text = Collapse(Read(relative));
        text.ShouldContain("native", Case.Insensitive, relative);
        text.ShouldContain("one build at a time", Case.Insensitive, relative);
    }

    // CARD-1627: a mutant loop held one lease for 14 to 38 minutes while 7 to 9 waiters queued.
    [Test]
    [Arguments("docs/testing-and-build.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    public void C1627_build_guidance_says_one_lease_per_build_never_around_a_loop(string relative)
    {
        var text = Collapse(Read(relative)).Replace("> ", "");
        text.ShouldContain("per build", Case.Insensitive, relative);
        text.ShouldContain("never one lease around", Case.Insensitive, relative);
    }

    [Test]
    public void C1629_orchestrator_brief_sentence_uses_the_native_output()
    {
        var text = Collapse(Read(".claude/skills/antiphon-orchestrator/SKILL.md")).Replace("> ", "");
        foreach (var phrase in new[]
                 {
                     "BUILD-SLOT RULE: ONE build-slot lease PER BUILD",
                     "NATIVE default output",
                     "no -o, --output or OutputPath",
                     "one build at a time per worktree",
                     "Checkpoint runs and land verification keep their own cold outputs.",
                 })
            text.ShouldContain(phrase, Case.Sensitive, "antiphon-orchestrator SKILL BUILD-SLOT RULE");
    }

    [Test]
    public void C1629_owner_states_the_hazards_and_the_cold_exceptions()
    {
        var text = Collapse(Read("docs/testing-and-build.md"));
        foreach (var phrase in new[]
                 {
                     "CARD-1629",
                     "native default output",
                     "one build at a time per worktree",
                     "UseAppHost=false",
                     "checkpoint tool and land verification stay cold",
                     "LastWriteTime",
                     "forward slash",
                 })
            text.ShouldContain(phrase, Case.Sensitive, "docs/testing-and-build.md");
    }

    [Test]
    public void C1629_owner_recipes_build_in_the_native_output()
    {
        var text = Collapse(Read("docs/testing-and-build.md"));
        foreach (var retired in new[]
                 {
                     "Build once into a producer-owned isolated output",
                     "Ordinary Code/Review uses `OutputPath=",
                     "OutputPath=bin-c475/",
                     "OutputPath=bin-coverage-tool/",
                 })
            text.ShouldNotContain(retired, Case.Sensitive, "docs/testing-and-build.md ordinary-build recipe");
        text.ShouldContain("Build once in the worktree's native default output", Case.Sensitive, "docs/testing-and-build.md");
    }

    private static string Collapse(string text) => Regex.Replace(text, @"\s+", " ");

    private static string Read(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "docs", "orchestration-loop.md")))
                return File.ReadAllText(Path.Combine(directory, relative));
            directory = Path.GetDirectoryName(directory)!;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
