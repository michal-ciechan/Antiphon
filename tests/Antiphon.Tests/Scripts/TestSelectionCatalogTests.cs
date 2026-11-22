using System.Collections;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313: the shipped policy and selection catalog (project universe, live and other-OS obligations, mixed fake and
/// real delivery), in-process (moved from scripts/test-test-selection.ps1 by the 2026-11-22 plan, unit 3).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionCatalogTests
{
    private static string Repo => DelegateScriptRunner.RepoRoot;

    private static IDictionary Catalog(PolicyFile policy) =>
        TestSelectionCatalog.Read(Path.Combine(Repo, "tests", "test-selection-policy.json"), policy.Object.SelectionCatalog.Hash);

    private static IDictionary Set(IDictionary catalog, string id) => C1313.Dicts(catalog["sets"]).Single(set => set["id"] as string == id);

    [Test]
    public void C1313_ProjectAndCatalogUniverse()
    {
        var policy = NightlyPolicy.Read(Repo, "");
        var census = NightlyPolicy.TestProjectUniverse(Repo, policy.Object);
        census.Ok.ShouldBeTrue("C1313 project-universe: missing=" + string.Join(",", census.Missing) + " extra=" + string.Join(",", census.Extra));
        var roles = policy.Object.ProjectDispositions;
        string[] probePaths = ["scripts/fixtures/nightly/c1375-probe/Probe.csproj", "tests/Antiphon.TestTiming.Probe/Antiphon.TestTiming.Probe.csproj"];
        var ownedProbes = roles.Where(r => probePaths.Contains(r.Project) && r.Role == "child" && !string.IsNullOrEmpty(r.Reason)
            && r.Consumers is { Length: 1 } && r.Consumers[0] == "tests/Antiphon.Tests/Antiphon.Tests.csproj").ToList();
        const string observability = "tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj";
        var hasObservability = File.Exists(Path.Combine(Repo, observability));
        var ownedAssembly = roles.Where(r => r.Project == observability && r.Role == "primary" && !string.IsNullOrEmpty(r.Reason) && r.Consumers is { Length: 0 }).ToList();
        var rc = policy.Object.Profiles["rc"].RequiredSuites;
        var nightly = policy.Object.Profiles["nightly"].RequiredSuites;
        var assemblyOwned = !hasObservability || (ownedAssembly.Count == 1 && rc.Contains("observability") && nightly.Contains("observability"));
        var authority = ReleaseAuthority.PolicyFromBytes(File.ReadAllBytes(Path.Combine(Repo, "tests", "test-execution-policy.json")));
        var extra = hasObservability ? 1 : 0;
        (roles.Count(r => string.Equals(r.Role, "primary", StringComparison.OrdinalIgnoreCase)) == 10 + extra && assemblyOwned && ownedProbes.Count == 2
            && authority.RequiredSuites.Length == 12 + extra && authority.RequiredSuites.Contains("markdown-pdf") && nightly.Length == 10 + extra)
            .ShouldBeTrue("C1313 primary-and-probe-ownership");
        var catalog = Catalog(policy);
        C1313.Dicts(Set(catalog, "core")["members"]).Count().ShouldBe(7, "C1313 seven-method-core");

        using var fixture = NightlyFixture.Create("c1313-universe");
        var root = fixture.PathOf("universe");
        foreach (var path in census.Disk.Append("tests/New.Fixture/New.Fixture.csproj"))
        {
            var file = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "<Project><IsTestProject>true</IsTestProject></Project>");
        }
        var bad = NightlyPolicy.TestProjectUniverse(root, policy.Object);
        (!bad.Ok && bad.Missing.Contains("tests/New.Fixture/New.Fixture.csproj")).ShouldBeTrue("C1313 unclassified-project");

        catalog["catalogHash"] = new string('0', 64);
        NightlyDiagnosticCases.Refusal(() => TestSelectionCatalog.Assert(catalog)).ShouldBe("catalog-hash-stale", "C1313 stale-catalog-refused");
    }

    [Test]
    public void C1313_LiveEligibility()
    {
        var policy = NightlyPolicy.Read(Repo, "");
        var catalog = Catalog(policy);
        string[] classes = ["ClaudeHeadedLongTests", "GrokMidTurnInterruptCanaryTests", "GrokSizeCaptureTests", "HerdrPaneDisposalGuardedLiveTests"];
        var rcExclusions = policy.Object.Profiles["rc"].Exclusions;
        var exclusions = rcExclusions.Where(e => classes.Contains(e.Class)).ToList();
        (exclusions.Count == 4 && exclusions.All(e => string.Equals(e.Owner, "CARD-1313", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(e.Reason)))
            .ShouldBeTrue("C1313 live-obligations-visible");
        var live = C1313.Dicts(catalog["sets"]).Where(s => s["id"] is "provider.claude.tui-live" or "provider.grok.tui-live" or "desktop.herdr").ToList();
        (live.Count == 3 && live.All(s => string.Equals(s["eligibility"] as string, "manual", StringComparison.OrdinalIgnoreCase))
            && live.SelectMany(s => C1313.Dicts(s["members"])).Any(m => m["class"] as string == "Antiphon.Tests.Agents.ClaudeEffortPromptCanaryTests"))
            .ShouldBeTrue("C1313 live-gates-owned");
        var linux = Set(catalog, "pdf.linux");
        var members = C1313.Dicts(linux["members"]).ToList();
        var missing = members.Where(m =>
        {
            var simple = PsValue.Str(m["class"]).Split('.')[^1];
            return rcExclusions.Count(e => e.Suite == "markdown-pdf" && e.Class == simple && e.Methods.Contains(PsValue.Str(m["method"]))) != 1;
        }).ToList();
        (C1313.Strings(linux["platforms"]).Contains("linux") && members.Count == 3 && missing.Count == 0).ShouldBeTrue("C1313 other-os-obligation");

        ((IDictionary)Set(catalog, "core")["resources"]!)["api"] = "unknown";
        catalog["catalogHash"] = TestSelectionCatalog.Hash(catalog);
        NightlyDiagnosticCases.Refusal(() => TestSelectionCatalog.Assert(catalog)).ShouldBe("unknown-resource", "C1313 unknown-resource-ineligible");
    }

    [Test]
    public void C1313_MixedDeliveryInventory()
    {
        var policy = NightlyPolicy.Read(Repo, "");
        var exclusions = NightlyPolicy.ProfileExclusions(policy.Object, "rc", "agents-pty", "windows");
        const string type = "ClaudeVerifiedDeliveryTests";
        var fake = new PlainObject(("Uid", "fake"), ("ClassName", "Antiphon.Agents.Pty.Tests." + type), ("Type", type),
            ("Method", "Fake_evidence_appears_then_submit_completes_a_turn"), ("Categories", Array.Empty<object>()));
        var real = new PlainObject(("Uid", "real"), ("ClassName", "Antiphon.Agents.Pty.Tests." + type), ("Type", type),
            ("Method", "Real_evidence_appears_then_submit_completes_a_turn"), ("Categories", new object[] { "OptIn", "Headed" }));
        (NightlyDiagnosticCases.S(NightlyCensus.Disposition(fake, exclusions, true), "Disposition") == "required"
            && NightlyDiagnosticCases.S(NightlyCensus.Disposition(real, exclusions, true), "Disposition") == "excluded").ShouldBeTrue("C1313 fake-required-real-excluded");
        var catalog = Catalog(policy);
        var oracles = C1313.Dicts(catalog["sets"]).Where(s => s["id"] is "provider.codex.vendor-offline" or "provider.grok.vendor-offline").ToList();
        (oracles.Count == 2 && oracles.All(s =>
        {
            var resources = (IDictionary)s["resources"]!;
            return string.Equals(resources["api"] as string, "none", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(resources["vendorExecutable"] as string, "none", StringComparison.OrdinalIgnoreCase)
                && string.Equals(s["eligibility"] as string, "manual", StringComparison.OrdinalIgnoreCase);
        })).ShouldBeTrue("C1313 vendor-oracles-are-not-fakes");
    }
}
