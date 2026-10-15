using System.Text.RegularExpressions;
using Antiphon.Server.Application.Services;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Unit")]
public sealed class D2AdvisoryDocumentationTests
{
    private const string OwnerPath = "docs/orchestration-loop.md";
    private const string SectionName = "D-2 advisory policy";

    [Test]
    public void DelegatePointerResolvesToTheD2PolicySection()
    {
        var section = ResolveSection(Basics(), Owner());
        foreach (var phrase in new[]
                 {
                     "A summary does not replace this policy.",
                     "No Review rerun solely for advisory docs/evidence corrections",
                     "retain the incomplete gate",
                     "publish the valid reviewed tip first",
                     "excluding instructions, policy, test selections, schemas",
                     "Comment exemptions remain refused",
                     "incomplete verification never authorizes land",
                 })
            section.ShouldContain(phrase, Case.Sensitive, "D-2 owner policy, not the bundle summary");
    }

    [Test]
    public void CheckpointObligationsNameTheirStageContracts()
    {
        var section = ResolveSection(Basics(), Owner());
        section.ShouldContain("Required checkpoint obligations in the Code and Review stage contracts remain enforced.",
            Case.Sensitive, "relocated checkpoint obligations must identify their source");
        section.ShouldNotContain("Required checkpoint defects above", Case.Sensitive);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public void MissingOrRenamedSectionFailsTheSameGuard(bool rename)
    {
        var document = Owner();
        var section = ResolveSection(Basics(), document);
        var broken = rename
            ? document.Replace("## " + SectionName, "## Renamed advisory policy", StringComparison.Ordinal)
            : document.Replace(section, "", StringComparison.Ordinal);
        var failure = Should.Throw<ShouldAssertException>(() => ResolveSection(Basics(), broken));
        failure.Message.ShouldContain("D-2 section heading must resolve");
        Console.WriteLine($"Expected guard failure ({(rename ? "renamed" : "removed")} section): {failure.Message}");
    }

    [Test]
    [Arguments("docs/missing-policy.md", SectionName, "D-2 pointer owner path")]
    [Arguments(OwnerPath, "Unrelated policy", "D-2 pointer section name")]
    public void BrokenPointerFailsTheSameGuard(string path, string section, string diagnostic)
    {
        var broken = Basics().Replace($"{OwnerPath}, {SectionName}.", $"{path}, {section}.", StringComparison.Ordinal);
        var failure = Should.Throw<ShouldAssertException>(() => ResolveSection(broken, Owner()));
        failure.Message.ShouldContain(diagnostic);
    }

    private static string Basics() => InstructionBundles.TextOf(InstructionBundles.DelegateBasics);

    private static string Owner() => File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, OwnerPath))
        .ReplaceLineEndings("\n");

    private static string ResolveSection(string basics, string document)
    {
        var pointer = Regex.Match(basics, @"Code/Review: read ([^,\r\n]+), ([^.\r\n]+)\.");
        pointer.Success.ShouldBeTrue("delegate-basics must carry the D-2 owner pointer");
        pointer.Groups[1].Value.ShouldBe(OwnerPath, "D-2 pointer owner path");
        pointer.Groups[2].Value.ShouldBe(SectionName, "D-2 pointer section name");
        var heading = "## " + pointer.Groups[2].Value + " (CARD-1281)";
        var matches = Regex.Matches(document, "^" + Regex.Escape(heading) + "$", RegexOptions.Multiline);
        matches.Count.ShouldBe(1, "D-2 section heading must resolve exactly once in " + pointer.Groups[1].Value);
        var start = matches[0].Index;
        var end = document.IndexOf("\n## ", start, StringComparison.Ordinal);
        return end < 0 ? document[start..] : document[start..end];
    }
}
