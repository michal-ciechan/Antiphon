using System.Text.RegularExpressions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1629 step 1: a direct (non-checkpoint) build reuses one stable <c>bin-&lt;task&gt;/</c> per
/// worktree, named by <c>scripts/build-slot.ps1 -StableOutput</c>. A new output name per build
/// recompiled 97% of a cold build (docs/investigations 2026-10-31), so the standing guidance must
/// no longer ask for one. Checkpoint and land verification builds keep their own fresh layout.
/// </summary>
[Category("Unit")]
public class StableBuildOutputDocumentationTests
{
    private static readonly Regex FreshOutputPerBuild = new(
        @"fresh\s+(output\s+(directory|dir|name|path)|bin-)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Test]
    [Arguments("AGENTS.md")]
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
    [Arguments("docs/testing-and-build.md")]
    [Arguments("server/Bundles/delegate-basics.md")]
    [Arguments(".claude/skills/antiphon-delegate/SKILL.md")]
    [Arguments(".claude/skills/antiphon-orchestrator/SKILL.md")]
    [Arguments("scripts/build-slot.ps1")]
    public void C1629_build_guidance_names_the_stable_output_helper(string relative)
    {
        var text = Collapse(Read(relative));
        text.ShouldContain("-StableOutput", Case.Sensitive, relative);
        text.ShouldContain("stable", Case.Insensitive, relative);
    }

    [Test]
    public void C1629_owner_states_the_hazards_and_the_cold_exceptions()
    {
        var text = Collapse(Read("docs/testing-and-build.md"));
        foreach (var phrase in new[]
                 {
                     "CARD-1629",
                     "-OutputSuffix",
                     "one build at a time per worktree",
                     "checkpoint tool and land verification stay cold",
                     "LastWriteTime",
                 })
            text.ShouldContain(phrase, Case.Sensitive, "docs/testing-and-build.md");
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
