using Antiphon.Tests.TestHelpers;
using System.Globalization;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Invariants;

/// <summary>
/// CARD-1842 S2. The expected-duration roster is a projection of the class-cost file, and an
/// [ExpectedDuration] application must name a roster class and equal its roster seconds. The export runs in this
/// process (ExpectedDurationRosterFile.Run, the export-expected-duration-roster command); until 2026-11-23 it was
/// scripts/export-expected-duration-roster.ps1 under pwsh.
/// </summary>
[Category("RepositoryInvariant")]
[Category("Integration")]
public sealed class ExpectedDurationSyncTests
{
    [Test, Timeout(120_000)]
    public async Task RosterMatchesCostFile(CancellationToken token)
    {
        // The roster and the cost file it projects, by path (a change to either selects this class).
        ExpectedDurationRosterFile.RelativePath.ShouldBe("tests/Shared/expected-duration-roster.json");
        ExpectedDurationRosterFile.CostFileRelativePath.ShouldBe("scripts/lib/nightly-class-costs.json");
        var roster = ExpectedDurationRosterFile.Load(RepoPath(ExpectedDurationRosterFile.RelativePath));
        var costs = ExpectedDurationRosterFile.ProjectCostFile(RepoPath(ExpectedDurationRosterFile.CostFileRelativePath));
        costs.Classes.Count.ShouldBeGreaterThan(0);
        roster.Classes.Count.ShouldBe(costs.Classes.Count);
        ExpectedDurationRoster.Differences(roster, costs).ShouldBeEmpty(
            "re-run scripts/antiphon export-expected-duration-roster after a class-cost refresh");

        // The comparer is not a constant: one changed roster seconds is one difference.
        var (first, entry) = roster.Classes.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
        var drifted = new Dictionary<string, ExpectedDurationEntry>(roster.Classes, StringComparer.Ordinal)
        {
            [first] = entry with { Seconds = entry.Seconds + 1 },
        };
        ExpectedDurationRoster.Differences(roster with { Classes = drifted }, costs).Count.ShouldBe(1);

        // The committed roster is what the exporter writes from the committed cost file.
        var scratch = Path.Combine(Path.GetTempPath(), "c1842-roster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var output = Path.Combine(scratch, "roster.json");
            var ran = Export(null, output);
            ran.ExitCode.ShouldBe(0, ran.Text);
            ran.Text.ShouldBe("exported " + roster.Classes.Count + " classes -> " + output + Environment.NewLine);
            ExpectedDurationRoster.Differences(ExpectedDurationRosterFile.Load(output), roster).ShouldBeEmpty(ran.Text);
            var written = await File.ReadAllTextAsync(output, token);
            written.ShouldBe(ExpectedDurationRosterFile.Text(roster, ExpectedDurationRosterFile.CostFileRelativePath));
            written.ShouldContain("\"costFile\": \"scripts/lib/nightly-class-costs.json\",\n  \"export\": \"scripts/antiphon export-expected-duration-roster\",\n", Case.Sensitive);
            written.ShouldNotContain("\r", Case.Sensitive);

            // Committed rows carry no measurement identity yet, so a fixture row proves the exporter copies it.
            var fixture = Path.Combine(scratch, "costs.json");
            await File.WriteAllTextAsync(fixture, """
                {"format":"antiphon-nightly-class-costs-v1","platform":"windows","classes":{
                  "Fx.Measured":{"nodes":2,"seconds":12.3,"basis":"trx","source":"s","hostId":"host-a","os":"windows","measuredRunId":"run-a","otherSample":{"seconds":40}},
                  "Fx.Bound":{"nodes":1,"seconds":600,"basis":"conservative-bound"}}}
                """, token);
            var fixtureOutput = Path.Combine(scratch, "fixture-roster.json");
            ran = Export(fixture, fixtureOutput);
            ran.ExitCode.ShouldBe(0, ran.Text);
            var projected = ExpectedDurationRosterFile.ProjectCostFile(fixture);
            projected.Classes["Fx.Measured"].ShouldBe(new ExpectedDurationEntry(12.3, "trx", "host-a", "windows", "run-a"));
            projected.Classes["Fx.Bound"].ShouldBe(new ExpectedDurationEntry(600, "conservative-bound", null, null, null));
            ExpectedDurationRoster.Differences(ExpectedDurationRosterFile.Load(fixtureOutput), projected).ShouldBeEmpty(ran.Text);
            // A cost file outside the checkout is named by its full path; the classes are in ordinal order.
            var fixtureText = await File.ReadAllTextAsync(fixtureOutput, token);
            fixtureText.ShouldContain("\"costFile\": \"" + Path.GetFullPath(fixture).Replace('\\', '/') + "\"", Case.Sensitive);
            fixtureText.IndexOf("\"Fx.Bound\"", StringComparison.Ordinal).ShouldBeLessThan(fixtureText.IndexOf("\"Fx.Measured\"", StringComparison.Ordinal));
            fixtureText.ShouldContain("\"Fx.Bound\": {\n      \"seconds\": 600,\n      \"basis\": \"conservative-bound\",\n      \"hostId\": null,", Case.Sensitive);

            // The export refuses what the projection refuses, and writes nothing.
            foreach (var (name, body, reason) in new[]
                     {
                         ("format", """{"format":"other","platform":"windows","classes":{}}""", "class-cost-format"),
                         ("platform", """{"format":"antiphon-nightly-class-costs-v1","platform":" ","classes":{}}""", "class-cost-platform"),
                         ("classes", """{"format":"antiphon-nightly-class-costs-v1","platform":"windows"}""", "class-cost-classes"),
                         ("seconds", """{"format":"antiphon-nightly-class-costs-v1","platform":"windows","classes":{"Fx.A":{"basis":"trx"}}}""", "class-cost-row: Fx.A"),
                         ("basis", """{"format":"antiphon-nightly-class-costs-v1","platform":"windows","classes":{"Fx.A":{"seconds":1,"basis":" "}}}""", "class-cost-row: Fx.A"),
                         ("host", """{"format":"antiphon-nightly-class-costs-v1","platform":"windows","classes":{"Fx.A":{"seconds":1,"basis":"trx","hostId":3}}}""", "class-cost-hostId: Fx.A"),
                     })
            {
                var bad = Path.Combine(scratch, "bad-" + name + ".json");
                await File.WriteAllTextAsync(bad, body, token);
                var badOutput = Path.Combine(scratch, "bad-" + name + "-roster.json");
                var refused = Export(bad, badOutput);
                refused.ExitCode.ShouldBe(1, refused.Text);
                refused.Text.ShouldStartWith("export-expected-duration-roster: " + reason, Case.Sensitive, refused.Text);
                File.Exists(badOutput).ShouldBeFalse(name);
            }
            Export(Path.Combine(scratch, "missing.json"), Path.Combine(scratch, "missing-roster.json")).ExitCode.ShouldBe(1);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Test]
    public void MovedE2EInvariantHasExpectedDuration()
    {
        const string source = "tests/Antiphon.E2E/Invariants/AntiphonAppFixtureHostPathInvariantTests.cs";
        var tree = CSharpSyntaxTree.ParseText(File.ReadAllText(RepoPath(source)));
        var invariantNamespace = tree.GetRoot().DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().ShouldHaveSingleItem();
        var fixture = invariantNamespace.Members.OfType<ClassDeclarationSyntax>().ShouldHaveSingleItem();
        var name = $"{invariantNamespace.Name}.{fixture.Identifier.ValueText}";

        LoadRoster().Classes.ShouldContainKey(name, $"Preserve the class cost for the invariant fixture in {source}.");
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
            ExpectedDurationRoster.Check(roster, differs).ShouldHaveSingleItem().ShouldContain("differs from roster seconds", Case.Sensitive);
        }

        var computed = ExpectedDurationRoster.FindApplications(Fixture(ns, type, "2 * 300"), "fixture");
        computed.ShouldHaveSingleItem().Seconds.ShouldBeNull();
        ExpectedDurationRoster.Check(roster, computed).ShouldHaveSingleItem().ShouldContain("numeric literal", Case.Sensitive);
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
        ExpectedDurationRoster.Check(roster, applications).ShouldHaveSingleItem().ShouldContain("absent from " + ExpectedDurationRosterFile.RelativePath, Case.Sensitive);
    }

    [Test, Timeout(120_000)]
    public void DeriveStaysAPlatformLabel(CancellationToken token)
    {
        PlatformTaxonomy.Derive([]).ShouldBe(PlatformClassLabel.Both);
        PlatformTaxonomy.Derive([new PlatformMember(true, PlatformDeclaration.LinuxOnly)]).LinuxOnly.ShouldBeTrue();

        // The roster reader (the core's file reader and the test-side attribute check) is not a platform function and
        // does not call Derive.
        foreach (var (path, type) in new[]
                 {
                     ("tests/Shared/ExpectedDurationRoster.cs", "ExpectedDurationRoster"),
                     ("scripts/lib/Antiphon.Nightly/ExpectedDurationRosterFile.cs", "ExpectedDurationRosterFile"),
                 })
        {
            var reader = CSharpSyntaxTree.ParseText(File.ReadAllText(RepoPath(path)));
            var identifiers = reader.GetRoot().DescendantTokens()
                .Where(t => t.IsKind(SyntaxKind.IdentifierToken)).Select(t => t.ValueText).ToHashSet(StringComparer.Ordinal);
            identifiers.ShouldContain(type);
            identifiers.ShouldNotContain("PlatformTaxonomy");
            identifiers.ShouldNotContain("Derive");
        }
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
            section.ShouldContain(sentence, Case.Sensitive);
    }

    // The export-expected-duration-roster command line in this process, on a host whose stdout and stderr are captured.
    private static (int ExitCode, string Text) Export(string? costPath, string outputPath)
    {
        var arguments = new List<string> { "--output-path", outputPath };
        if (costPath is not null)
            arguments.AddRange(["--cost-path", costPath]);
        var output = new StringWriter();
        var error = new StringWriter();
        var options = ExportExpectedDurationRosterOptions.Parse(arguments, RepoRoot, RepoRoot);
        var exit = ExpectedDurationRosterFile.Run(options, NightlyHost.Create(null, new RecordingLauncher(), output, error, null, null, null, null));
        return (exit, output.ToString() + error);
    }

    private static string Fixture(string ns, string type, string seconds) =>
        $"namespace {ns};\n[ExpectedDuration({seconds})]\npublic sealed class {type} {{ }}\n";

    private static string Literal(double seconds) => seconds.ToString("R", CultureInfo.InvariantCulture);

    private static ExpectedDurationRosterDocument LoadRoster() =>
        ExpectedDurationRosterFile.Load(RepoPath(ExpectedDurationRosterFile.RelativePath));

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
