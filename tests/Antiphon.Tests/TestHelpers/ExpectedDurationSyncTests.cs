using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1842 S2. The expected-duration roster is a projection of the class-cost file, and an
/// [ExpectedDuration] application must name a roster class and equal its roster seconds.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ExpectedDurationSyncTests
{
    [Test, Timeout(120_000)]
    public async Task RosterMatchesCostFile(CancellationToken token)
    {
        var roster = ExpectedDurationRoster.Load(RepoPath(ExpectedDurationRoster.RelativePath));
        var costs = ExpectedDurationRoster.ProjectCostFile(RepoPath(ExpectedDurationRoster.CostFileRelativePath));
        costs.Classes.Count.ShouldBeGreaterThan(0);
        roster.Classes.Count.ShouldBe(costs.Classes.Count);
        ExpectedDurationRoster.Differences(roster, costs).ShouldBeEmpty(
            "re-run pwsh -NoProfile -File scripts/export-expected-duration-roster.ps1 after a class-cost refresh");

        // The comparer is not a constant: one changed roster seconds is one difference.
        var (first, entry) = roster.Classes.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var drifted = new Dictionary<string, ExpectedDurationEntry>(roster.Classes, StringComparer.Ordinal)
        {
            [first] = entry with { Seconds = entry.Seconds + 1 },
        };
        ExpectedDurationRoster.Differences(roster with { Classes = drifted }, costs).Count.ShouldBe(1);

        // The committed roster is what the exporter writes from the committed cost file.
        var output = Path.Combine(Path.GetTempPath(), "c1842-roster-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var start = new ProcessStartInfo("pwsh");
            foreach (var argument in new[] { "-NoProfile", "-File", RepoPath("scripts/export-expected-duration-roster.ps1"), "-OutputPath", output })
                start.ArgumentList.Add(argument);
            var ran = await C1514Support.RunProcess(start, token);
            ran.ExitCode.ShouldBe(0, ran.Text);
            ExpectedDurationRoster.Differences(ExpectedDurationRoster.Load(output), roster).ShouldBeEmpty(ran.Text);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Test, Timeout(120_000)]
    public void TreeHasNoExpectedDurationAttribute(CancellationToken token)
    {
        // The scanner sees plain, qualified, and listed applications, and not comments or strings.
        ExpectedDurationRoster.FindApplications(Fixture("Fx", "Plain", "12"), "fixture")
            .ShouldHaveSingleItem().ShouldBe(new ExpectedDurationApplication("Fx.Plain", "fixture:2", 12));
        ExpectedDurationRoster.FindApplications(
            "namespace Fx { [Category(\"Unit\"), Antiphon.TestSupport.ExpectedDurationAttribute(3.5)] public sealed class Listed { } }",
            "fixture").ShouldHaveSingleItem().ClassName.ShouldBe("Fx.Listed");
        ExpectedDurationRoster.FindApplications(
            "// [ExpectedDuration(5)]\nclass C { string s = \"[ExpectedDuration(5)]\"; }", "fixture").ShouldBeEmpty();

        var tree = ScanTree(token);
        tree.ShouldBeEmpty("CARD-1842 applies [ExpectedDuration] to no class: "
            + string.Join("; ", tree.Select(application => application.Location)));
        ExpectedDurationRoster.FindApplications(typeof(ExpectedDurationSyncTests).Assembly).ShouldBeEmpty();
        ExpectedDurationRoster.Check(LoadRoster(), tree).ShouldBeEmpty();
    }

    [Test, Timeout(120_000)]
    public void AttributeAgreesWithRoster(CancellationToken token)
    {
        var roster = LoadRoster();
        var (name, entry) = roster.Classes.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var dot = name.LastIndexOf('.');
        var (ns, type) = (name[..dot], name[(dot + 1)..]);

        var agrees = ExpectedDurationRoster.FindApplications(Fixture(ns, type, Literal(entry.Seconds)), "fixture");
        agrees.ShouldHaveSingleItem().ClassName.ShouldBe(name);
        ExpectedDurationRoster.Check(roster, agrees).ShouldBeEmpty();

        foreach (var delta in new[] { 1.0, 0.5, -1.0 })
        {
            var differs = ExpectedDurationRoster.FindApplications(Fixture(ns, type, Literal(entry.Seconds + delta)), "fixture");
            ExpectedDurationRoster.Check(roster, differs).ShouldHaveSingleItem().ShouldContain("differs from roster seconds");
        }

        var computed = ExpectedDurationRoster.FindApplications(Fixture(ns, type, "2 * 300"), "fixture");
        computed.ShouldHaveSingleItem().Seconds.ShouldBeNull();
        ExpectedDurationRoster.Check(roster, computed).ShouldHaveSingleItem().ShouldContain("numeric literal");
    }

    [Test, Timeout(120_000)]
    public void AttributeOnlyClassFails(CancellationToken token)
    {
        var roster = LoadRoster();
        const string name = "Antiphon.Tests.TestHelpers.ExpectedDurationAttributeOnlyFixture";
        roster.Classes.ContainsKey(name).ShouldBeFalse();
        var seconds = roster.Classes.Values.First().Seconds;

        var applications = ExpectedDurationRoster.FindApplications(
            Fixture("Antiphon.Tests.TestHelpers", "ExpectedDurationAttributeOnlyFixture", Literal(seconds)), "fixture");
        applications.ShouldHaveSingleItem().ClassName.ShouldBe(name);
        ExpectedDurationRoster.Check(roster, applications).ShouldHaveSingleItem().ShouldContain("absent from " + ExpectedDurationRoster.RelativePath);
    }

    [Test, Timeout(120_000)]
    public void DeriveStaysAPlatformLabel(CancellationToken token)
    {
        PlatformTaxonomy.Derive([]).ShouldBe(PlatformClassLabel.Both);
        PlatformTaxonomy.Derive([new PlatformMember(true, PlatformDeclaration.LinuxOnly)]).LinuxOnly.ShouldBeTrue();

        // The roster reader is not a platform function and does not call Derive.
        var reader = CSharpSyntaxTree.ParseText(File.ReadAllText(RepoPath("tests/Shared/ExpectedDurationRoster.cs")));
        var identifiers = reader.GetRoot().DescendantTokens()
            .Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
        identifiers.ShouldContain("ExpectedDurationRoster");
        identifiers.ShouldNotContain("PlatformTaxonomy");
        identifiers.ShouldNotContain("Derive");
    }

    [Test, Timeout(120_000)]
    public void DocPinsTheRosterSentence(CancellationToken token)
    {
        var doc = File.ReadAllText(RepoPath("docs/testing-and-build.md")).ReplaceLineEndings("\n");
        var start = doc.IndexOf("\n## Nightly\n", StringComparison.Ordinal);
        start.ShouldBeGreaterThan(0);
        var end = doc.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = Regex.Replace(end < 0 ? doc[start..] : doc[start..end], @"\s+", " ");
        foreach (var sentence in new[]
                 {
                     "Expected duration is one seconds number on the class-cost row.",
                     "The roster beside PlatformTaxonomy is a projection of that file.",
                     "An ExpectedDuration attribute, when present, must equal the roster seconds for that class.",
                 })
            section.ShouldContain(sentence);
    }

    private static string Fixture(string ns, string type, string seconds) =>
        $"namespace {ns};\n[ExpectedDuration({seconds})]\npublic sealed class {type} {{ }}\n";

    private static string Literal(double seconds) => seconds.ToString("R", CultureInfo.InvariantCulture);

    private static ExpectedDurationRosterDocument LoadRoster() =>
        ExpectedDurationRoster.Load(RepoPath(ExpectedDurationRoster.RelativePath));

    private static List<ExpectedDurationApplication> ScanTree(CancellationToken token)
    {
        var tests = RepoPath("tests");
        var found = new List<ExpectedDurationApplication>();
        var scanned = 0;
        foreach (var path in Directory.EnumerateFiles(tests, "*.cs", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(RepoRoot, path).Replace('\\', '/');
            if (relative.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules" or ".antiphon" or "TestOutput"
                    || segment.StartsWith("bin-", StringComparison.Ordinal)))
                continue;
            scanned++;
            var text = File.ReadAllText(path);
            if (text.Contains("ExpectedDuration", StringComparison.Ordinal))
                found.AddRange(ExpectedDurationRoster.FindApplications(text, relative));
        }

        scanned.ShouldBeGreaterThan(100);
        return found;
    }

    private static string RepoPath(string relative) =>
        Path.Combine([RepoRoot, .. relative.Split('/')]);

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
                dir = dir.Parent;
            return dir?.FullName
                ?? throw new DirectoryNotFoundException("Could not locate repo root (Antiphon.sln).");
        }
    }
}
