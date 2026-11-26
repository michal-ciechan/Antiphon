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
}
