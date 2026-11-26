using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Antiphon.TestSupport;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using Antiphon.Tests.Scripts;
using static Antiphon.Tests.Scripts.DerivedImpactIndexTests;

namespace Antiphon.Tests.Invariants;

[Category("Unit")]
[Category("RepositoryInvariant")]
[ParallelLimiter<ProcessSpawnLimit>]
[Timeout(600_000)]
public sealed class DerivedImpactIndexInvariantTests
{
    [Test]
    public async Task committed_index_matches_the_builder()
    {
        var root = RepoRoot();
        var started = Stopwatch.StartNew();
        var index = DerivedImpactIndexBuilder.Build(root);
        started.Stop();
        started.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(60), "derived index rebuild");
        index.Cap.ShouldBe(DerivedImpactIndexBuilder.Cap);
        index.Allowlist.ShouldBe(DerivedImpactIndexBuilder.Allowlist);
        index.Utf8.Length.ShouldBeLessThanOrEqualTo(DerivedImpactIndexBuilder.ByteLimit);

        // Hand windows:only classes are not derived windowsOnly. The override list and the
        // class-level LinuxOnly declaration are.
        index.WindowsOnly.ShouldContain("CodexAdapterLocalShellTests");
        index.WindowsOnly.ShouldNotContain("RepositoryLeaseWindowsBoundaryTests");
        index.LinuxOnly.ShouldContain("PlatformTaxonomyClassFilterTests");
        index.LinuxOnly.ShouldNotContain("PlatformTaxonomyFilterTests");
        index.WindowsOnly.ShouldNotContain("PlatformTaxonomyFilterTests");
        index.LinuxOnly.ShouldNotContain("CodexAdapterLocalShellTests");

        var path = Path.Combine(root, "tests", "derived-impact-index.json");
        File.Exists(path).ShouldBeTrue(path);
        var committed = await File.ReadAllBytesAsync(path);
        using (var committedJson = JsonDocument.Parse(committed))
        {
            var windowsOnly = Strings(committedJson.RootElement, "windowsOnly");
            windowsOnly.ShouldContain("CodexAdapterLocalShellTests");
            windowsOnly.ShouldNotContain("RepositoryLeaseWindowsBoundaryTests");
            var linuxOnly = Strings(committedJson.RootElement, "linuxOnly");
            linuxOnly.ShouldContain("PlatformTaxonomyClassFilterTests");
            linuxOnly.ShouldNotContain("PlatformTaxonomyFilterTests");
            var committedText = System.Text.Encoding.UTF8.GetString(committed);
            var blinded = committedText.Replace(
                "\"PlatformTaxonomyClassFilterTests\"",
                "\"PlatformTaxonomyClassFilterTestsBlind\"",
                StringComparison.Ordinal);
            blinded.ShouldNotBe(committedText);
            using var broken = JsonDocument.Parse(blinded);
            Strings(broken.RootElement, "linuxOnly")
                .ShouldNotContain("PlatformTaxonomyClassFilterTests");
        }

        committed.ShouldBe(index.Utf8);

        foreach (var broad in DerivedImpactIndexBuilder.Allowlist)
            index.Edges.Any(edge => edge.Path == broad).ShouldBeFalse(broad);

        committed.Any(b => b == (byte)'\r').ShouldBeFalse("committed index contains CR");

        // A capped path keeps its 25 most specific classes. The remainder is owed, not dropped.
        Capped(index, LandingGitPath);
        Capped(index, LeakSweepPath);
        Capped(index, NotificationServicePath);
        Edge(index, LandingGitPath).ShouldContain("DispatchBaseNotificationTests");
        Edge(index, NotificationServicePath).ShouldContain("PostLandMutationDeliveryTests");
        Edge(index, LeakSweepPath).ShouldContain("PtyHostAdoptionTests");
        Edge(index, AuditCommandPath).ShouldContain("LandGraphEvaluationTests");
    }

    // CARD-1838. The Merge process names the regenerate command as the last step before
    // DerivedImpactIndexInvariantTests and the push, and the Merge guard union names the two missed rows.
    [Test]
    public void merge_process_names_the_regenerate_step()
    {
        var root = RepoRoot();
        var loop = File.ReadAllText(Path.Combine(root, "docs", "orchestration-loop.md"));
        var guide = File.ReadAllText(Path.Combine(root, "docs", "testing-and-build.md"));
        AssertRegenDocs(loop, guide);
        File.Exists(Path.Combine(root, "scripts", "regenerate-derived-impact-index.ps1")).ShouldBeFalse("the regeneration is a command (unit 41)");

        var blinded = loop.Replace(
            RegenStep,
            "The last step before the push is a hand edit of tests/derived-impact-index.json.",
            StringComparison.Ordinal);
        blinded.ShouldNotBe(loop);
        Should.Throw<ShouldAssertException>(() => AssertRegenDocs(blinded, guide));

        var guardBlind = guide.Replace(
            "Resolve repository invariant checkpoint rows with",
            "Omit the invariant selection",
            StringComparison.Ordinal);
        guardBlind.ShouldNotBe(guide);
        Should.Throw<ShouldAssertException>(() => AssertRegenDocs(loop, guardBlind));
    }

    // rc run ae84478d g131. regenerate_command_reports_changed_then_unchanged runs the driver with
    // --no-build. The qualification builds only Antiphon.sln, and the Merge step builds the driver
    // itself, so only this read sees a driver that the solution build does not produce.
    [Test]
    public void solution_build_produces_the_regenerate_driver()
    {
        var solution = File.ReadAllText(Path.Combine(RepoRoot(), "Antiphon.sln"));
        AssertSolutionBuildsDriver(solution);

        var unbuilt = solution.Replace(".Debug|Any CPU.Build.0 = Debug|Any CPU", ".Debug|Any CPU.Skip = Debug|Any CPU", StringComparison.Ordinal);
        unbuilt.ShouldNotBe(solution);
        Should.Throw<ShouldAssertException>(() => AssertSolutionBuildsDriver(unbuilt));
    }

    private static void AssertSolutionBuildsDriver(string solution)
    {
        const string driver = "\"scripts\\regenerate-derived-impact-index\\Antiphon.DerivedImpactIndex.csproj\"";
        var line = solution.Split('\n').SingleOrDefault(l => l.StartsWith("Project(", StringComparison.Ordinal) && l.Contains(driver, StringComparison.Ordinal));
        line.ShouldNotBeNull("Antiphon.sln does not list the regenerate driver");
        var guid = line.TrimEnd('\r').Split(", ")[^1].Trim('"');
        solution.ShouldContain(guid + ".Debug|Any CPU.Build.0 = Debug|Any CPU", Case.Sensitive);
    }

    // Unit 41: the step is the CLI command; scripts/regenerate-derived-impact-index.ps1 is gone.
    private const string RegenStep =
        "The last step before DerivedImpactIndexInvariantTests and the push is `scripts/antiphon regenerate-derived-impact-index`";

    private static void AssertRegenDocs(string loop, string guide)
    {
        const string heading = "### Merge process (CARD-1783)";
        var start = loop.IndexOf(heading, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0);
        var end = loop.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = end < 0 ? loop[start..] : loop[start..end];
        section.ShouldContain(RegenStep, Case.Sensitive);
        section.ShouldContain("DerivedImpactIndexBuilder.Build", Case.Sensitive);
        section.ShouldContain("DerivedImpactIndexBuilder.Write", Case.Sensitive);
        section.ShouldContain("commits nothing", Case.Sensitive);
        section.ShouldContain(
            "DerivedImpactIndexInvariantTests.committed_index_matches_the_builder keeps the full-byte compare",
            Case.Sensitive);
        section.ShouldContain("so a Merge that skips the command still fails", Case.Sensitive);

        const string guard = "Resolve repository invariant checkpoint rows with";
        guide.ShouldContain(guard, Case.Sensitive);
        guide.ShouldContain(Antiphon.Nightly.NightlyTestsPlan.RepositoryInvariantFilter, Case.Sensitive);
        foreach (var type in new[] { typeof(C1514LandingSplitBudgetTests), typeof(DerivedImpactIndexInvariantTests) })
            type.GetCustomAttributesData().Any(attribute => attribute.AttributeType == typeof(CategoryAttribute)
                && attribute.ConstructorArguments.FirstOrDefault().Value as string == "RepositoryInvariant")
                .ShouldBeTrue(type.FullName);
    }
}
