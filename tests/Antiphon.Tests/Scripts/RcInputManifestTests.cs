using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S3a: the object-only, conservative input inventory of two commits, in-process (moved from
/// test-rc-input-manifest.ps1). A throwaway repository with three suites (server, runner and an unrelated other)
/// commits one edit at a time: product, helper, linked, wildcard-owned, config, fixture, script and lock inputs
/// dirty their readers; an unowned path or a dynamic import is flagged, never broadened; a removed edge keeps the
/// old graph; the working tree never stands in for a commit; and a derived index only adds suites.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(120_000)]
public sealed class RcInputManifestTests : IDisposable
{
    private static readonly Regex FullId = new("^[a-f0-9]{40}$", RegexOptions.CultureInvariant);

    private const string Owners = """
        {
          "schemaVersion": 1,
          "suites": [
            { "id": "server", "project": "tests/Server/Server.csproj" },
            { "id": "runner", "project": "tests/Runner/Runner.csproj" },
            { "id": "other", "project": "tests/Other/Other.csproj" }
          ],
          "roots": [
            { "path": "tests/Shared/**", "consumers": ["server", "runner"], "reason": "shared-helper" },
            { "path": "scripts/**", "consumers": ["server", "runner"], "reason": "harness" },
            { "path": "tools/**", "consumers": ["server", "runner"], "reason": "compiled-helper" },
            { "path": "build/**", "consumers": ["*"], "reason": "build-import" },
            { "path": "docs/**", "consumers": ["server"], "reason": "scanner-docs" },
            { "path": "Dockerfile", "consumers": ["server"], "reason": "container" },
            { "path": "client/**", "consumers": ["server"], "reason": "browser-client" },
            { "path": "*.json", "consumers": ["*"], "reason": "root-config" },
            { "path": "*.props", "consumers": ["*"], "reason": "build-config" },
            { "path": "NuGet.config", "consumers": ["*"], "reason": "packages" },
            { "path": "scripts/lib/rc-input-owners.json", "consumers": ["*"], "reason": "ownership-contract" }
          ]
        }
        """;

    private readonly RcInputFixtureRepository repo = new("rim-");
    private string seeded = "";

    public void Dispose() => repo.Dispose();

    [Before(Test)]
    public void Seed()
    {
        repo.Init();
        repo.Put(RcInputInventory.MapPath, Owners);
        repo.Put("src/Core/Core.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        repo.Put("src/Core/Queue.cs", "old core");
        repo.Put("server/Server.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"../build/common.props\" /><ItemGroup><ProjectReference Include=\"../src/Core/Core.csproj\" /></ItemGroup></Project>");
        repo.Put("server/Application/SessionMessageQueueService.cs", "old product");
        repo.Put("build/common.props", "<Project><ItemGroup><Compile Include=\"../linked/Shared.cs\" /></ItemGroup></Project>");
        repo.Put("linked/Shared.cs", "linked source");
        repo.Put("build/wildcard-only.txt", "owned only by the wildcard rule");
        repo.Put("tests/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../server/Server.csproj\" /></ItemGroup></Project>");
        repo.Put("tests/Server/SessionMessageQueueServiceTests.cs", "unchanged test");
        repo.Put("tests/Runner/Runner.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Core/Core.csproj\" Condition=\"false\" /></ItemGroup></Project>");
        repo.Put("tests/Other/Other.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        repo.Put("tests/Other/Other.cs", "unrelated");
        repo.Put("tests/Shared/Helper.cs", "old helper");
        repo.Put("tests/Server/appsettings.test.json", "{}");
        repo.Put("tests/Server/Fixtures/input.txt", "fixture");
        repo.Put("scripts/fixture.ps1", "fixture script");
        repo.Put("tools/Helper/Helper.csproj", "<Project />");
        repo.Put("Directory.Packages.props", "<Project />");
        repo.Put("NuGet.config", "<configuration />");
        repo.Put("global.json", "{}");
        repo.Put("client/package-lock.json", "{}");
        repo.Put("Dockerfile", "FROM pinned@sha256:fixture");
        repo.Put("docs/contract.md", "consumed docs");
        seeded = repo.Commit();
    }

    private RcInputManifest Select(string baseline, string candidate, RcDerivedIndex? index = null) => RcInputInventory.Manifest(repo.Root, baseline, candidate, index);

    private static RcInputSuite? Suite(RcInputManifest s, string id, int snapshot = 1) => s.Snapshots[snapshot].Suites.FirstOrDefault(x => x.Id == id);

    // One present record of the path in the candidate's suite, with a full object id and a reason.
    private static bool Has(RcInputManifest s, string id, string path)
    {
        if (s.Snapshots.Length != 2) return false;
        var inputs = Suite(s, id)?.Inputs ?? [];
        return inputs.Count(r => r.Path == path && r.Present && r.ObjectId != null && FullId.IsMatch(r.ObjectId) && !string.IsNullOrEmpty(r.Reason)) == 1;
    }

    private static string[] Sorted(IEnumerable<string> values) =>
        values.Where(v => v != null).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    [Test]
    public void ProductAndHelperFanout()
    {
        var baseline = seeded;
        RcInputManifest s = null!;
        var a = "";
        foreach (var path in new[] { "server/Application/SessionMessageQueueService.cs", "src/Core/Queue.cs", "tests/Shared/Helper.cs", "linked/Shared.cs" })
        {
            // Compare only this edit: earlier product edits must not dirty a missing helper owner.
            baseline = repo.Head();
            repo.Put(path, "changed " + path);
            a = repo.Commit();
            s = Select(baseline, a);
            (s.Changes.Length == 1 && s.Changes[0].Path == path).ShouldBeTrue("isolated-fanout-change:" + path);
            s.DirtySuites.ShouldContain("server", "product-or-helper-dirty:" + path);
            Has(s, "server", path).ShouldBeTrue("product-or-helper-inventory:" + path);
            if (path is "src/Core/Queue.cs" or "tests/Shared/Helper.cs")
            {
                s.DirtySuites.ShouldContain("runner", "fanout:" + path);
                Has(s, "runner", path).ShouldBeTrue("fanout-inventory:" + path);
            }
            s.Unresolved.ShouldBeEmpty("fanout-no-fallback:" + path);
            s.DirtySuites.ShouldNotContain("other", "proven-unrelated:" + path);
        }
        repo.Git("rev-parse", baseline + ":tests/Server/SessionMessageQueueServiceTests.cs")
            .ShouldBe(repo.Git("rev-parse", a + ":tests/Server/SessionMessageQueueServiceTests.cs"), "unchanged-test-shape");
        s.Snapshots.Length.ShouldBe(2, "both-committed-inventories");
        Suite(s, "server")!.Edges.Where(e => e.Reason == "ProjectReference" && e.To == "src/Core/Core.csproj").ShouldNotBeEmpty("transitive-edge-reason");

        // No project/import reaches this input. Only build/** -> * can fan it out.
        baseline = repo.Head();
        repo.Put("build/wildcard-only.txt", "changed wildcard input");
        s = Select(baseline, repo.Commit());
        (s.Changes.Length == 1 && s.Changes[0].Path == "build/wildcard-only.txt").ShouldBeTrue("wildcard-isolated-change");
        s.Unresolved.ShouldBeEmpty("wildcard-no-fallback");
        foreach (var id in new[] { "server", "runner", "other" })
        {
            s.DirtySuites.ShouldContain(id, "wildcard-dirty:" + id);
            Has(s, id, "build/wildcard-only.txt").ShouldBeTrue("wildcard-inventory:" + id);
            foreach (var snapshot in s.Snapshots)
                snapshot.Suites.First(x => x.Id == id).Edges.Where(e => e.To == "build/wildcard-only.txt").ShouldBeEmpty("wildcard-not-project-reached:" + id);
        }

        // Shipped map consumers are source-repo suite ids. Pasting those bytes into this fixture throws
        // unknown-input-consumer, so the check stays structural.
        using var committed = JsonDocument.Parse(RcInputFixtureRepository.ReadSource(RcInputInventory.MapPath));
        var map = committed.RootElement;
        map.GetProperty("schemaVersion").GetInt32().ShouldBe(1, "committed-map-schema");
        map.GetProperty("suiteProjects").GetString().ShouldBe("tests/**/*.csproj", "committed-map-suite-projects");
        var roots = map.GetProperty("roots").EnumerateArray().ToArray();
        static string? Text(JsonElement row, string name) =>
            row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        static string[] Consumers(JsonElement row) =>
            row.TryGetProperty("consumers", out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(c => c.GetString()!).ToArray() : [];
        roots.Where(r => Text(r, "reason") == "repository-scanners-conservative-all-inputs").ShouldBeEmpty("committed-map-not-blanket");
        // unmatched-path-fallback used to be the one ** row with consumers *. That row is gone: an unmatched path is
        // unowned, not assigned to every suite.
        roots.Where(r => Text(r, "reason") == "unmatched-path-fallback" || (Text(r, "path") == "**" && Consumers(r).Contains("*")))
            .ShouldBeEmpty("committed-map-no-broad-fallback");
        void Root(string path, string reason, string label)
        {
            var rule = roots.Where(r => Text(r, "path") == path).ToArray();
            rule.Length.ShouldBe(1, label + ":" + path);
            string.Join(";", Sorted(Consumers(rule[0]))).ShouldBe("*", label + "-star:" + path);
            Text(rule[0], "reason").ShouldBe(reason, label + "-reason:" + path);
        }
        foreach (var path in new[] { "docs/**", "**/*.md", "scripts/**", "docker/**", ".claude/**", "**/docker-compose*.yml", "**/Dockerfile*", "client/**" })
            Root(path, "repository-reader-non-code", "committed-map-reader");
        foreach (var path in new[] { "antiphon.areas.json", ".editorconfig", ".github/**", "tests/derived-impact-index.json", "tests/impacted-classes.json" })
            Root(path, "forced-full-root", "committed-map-forced");
        Root("**/Directory.Build.*", "forced-full-build-graph", "committed-map-build");
        var failures = new List<string>();
        foreach (var rule in roots)
        {
            try { RcInputInventory.Pattern(Text(rule, "path")); }
            catch (RcInputRefusal) { failures.Add("committed-map-pattern:" + Text(rule, "path")); }
            foreach (var id in Consumers(rule))
                if (id != "*" && !File.Exists(Path.Combine(RcInputFixtureRepository.SourceRoot, id.Replace('/', Path.DirectorySeparatorChar))))
                    failures.Add("committed-map-consumer-exists:" + id);
        }
        failures.ShouldBeEmpty();
    }

    [Test]
    public void DynamicAndUnownedAreDirty()
    {
        var baseline = seeded;
        // Replaces the assertion that an unknown path dirties every suite. unresolved-conservative-all-inputs no
        // longer copies the closure onto other.
        repo.Put("unowned/new.txt", "unknown");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("unknown-selects-nothing");
        s.NeedsOrchestratorDecision.ShouldBeTrue("unknown-needs-decision");
        s.CreditBlockedBy.ShouldBe("unowned-paths", "unknown-credit");
        Has(s, "other", "unowned/new.txt").ShouldBeFalse("unknown-not-an-input");
        var rows = (s.Unowned ?? []).Where(r => r != null && r.Path == "unowned/new.txt").ToArray();
        rows.Length.ShouldBe(1, "unknown-hint");
        rows[0].Hint.ShouldBe("unknown", "unknown-hint");
        repo.Put("server/Server.csproj", "<Project><Import Project=\"$(UnknownRoot)/dynamic.props\" /></Project>");
        s = Select(baseline, repo.Commit());
        s.Unresolved.Where(u => u.Contains("unresolved-import", StringComparison.OrdinalIgnoreCase)).ShouldNotBeEmpty("dynamic-import-flagged:" + string.Join(",", s.Unresolved));
        Has(s, "other", "src/Core/Queue.cs").ShouldBeFalse("unresolved-does-not-broaden");
        s.DirtySuites.ShouldContain("server", "dynamic-dirties-changed-project");
        s.DirtySuites.ShouldNotContain("other", "dynamic-does-not-dirty-other:" + string.Join(",", s.DirtySuites));
        s.CreditBlockedBy.ShouldBe("unowned-paths", "dynamic-credit-blocked");
        // Old graph dependencies cannot disappear when an edge is removed in the candidate.
        repo.Put("tests/Runner/Runner.csproj", "<Project />");
        repo.Put("src/Core/Queue.cs", "old edge consumer");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldContain("runner", "old-graph-retained");
    }

    // Each path still gets its own manifest comparison. One family per case bounds the git work one case must finish.
    private static readonly IReadOnlyDictionary<string, string[]> InputFamilies = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ConfigAndFixtures"] = ["tests/Server/appsettings.test.json", "tests/Server/Fixtures/input.txt", "global.json"],
        ["ScriptAndBuildInputs"] = ["scripts/fixture.ps1", "tools/Helper/Helper.csproj", "Directory.Packages.props", "NuGet.config"],
        ["ClientContainerAndDocs"] = ["client/package-lock.json", "Dockerfile", "docs/contract.md"],
    };

    [Test]
    [Arguments("ConfigAndFixtures")]
    [Arguments("ScriptAndBuildInputs")]
    [Arguments("ClientContainerAndDocs")]
    public void ConfigFixtureScriptAndLockInputs(string family)
    {
        var baseline = seeded;
        RcInputManifest s = null!;
        foreach (var path in InputFamilies[family])
        {
            var content = path.EndsWith(".props", StringComparison.Ordinal) ? "<Project><PropertyGroup><FixtureValue>changed</FixtureValue></PropertyGroup></Project>"
                : path.EndsWith(".csproj", StringComparison.Ordinal) ? "<Project Sdk=\"Microsoft.NET.Sdk\" />"
                : "changed " + path;
            repo.Put(path, content);
            s = Select(baseline, repo.Commit());
            (s.Changes.Length == 1 && s.Changes[0].Path == path).ShouldBeTrue("family-isolated-change:" + path);
            Has(s, "server", path).ShouldBeTrue("family-inventory:" + path);
            s.DirtySuites.ShouldContain("server", "family-dirty:" + path);
            s.Unresolved.ShouldBeEmpty("family-closure-is-known:" + path);
            baseline = s.CandidateSha;
        }
        if (family != "ConfigAndFixtures") return;
        repo.Put("tests/Server/Fixtures/added.json", "{}");
        s = Select(baseline, repo.Commit());
        Has(s, "server", "tests/Server/Fixtures/added.json").ShouldBeTrue("new-input-not-old-glob-only");
        repo.Remove("tests/Server/Fixtures/input.txt");
        s = Select(baseline, repo.Commit());
        s.Changes.Count(c => c.Path == "tests/Server/Fixtures/input.txt" && c.Status == "D").ShouldBe(1, "deleted-input-retained");
        s.Snapshots.Length.ShouldBe(2, "absence-record");
        Suite(s, "server")!.Inputs.Count(r => r.Path == "tests/Server/Fixtures/input.txt" && !r.Present).ShouldBe(1, "absence-record");
        // Dirty working bytes must not stand in for either Git commit.
        repo.Put("src/Core/Queue.cs", "uncommitted replacement");
        JsonSerializer.Serialize(s).ShouldBe(JsonSerializer.Serialize(Select(baseline, s.CandidateSha)), "historical-object-only");
    }

    [Test]
    public void PartialIndexNeverSubtracts()
    {
        var baseline = seeded;
        repo.Put("src/Core/Queue.cs", "index non-hit");
        var a = repo.Commit();
        var plain = Select(baseline, a);
        (plain.Unresolved.Length == 0 && !plain.DirtySuites.Contains("other")).ShouldBeTrue("known-conservative-baseline");
        // The script's second index also carried capped=[src/Core/Queue.cs]; the library never read it and the typed
        // index has no such field.
        var indexes = new[]
        {
            null,
            new RcDerivedIndex { Version = 1, Edges = [] },
            new RcDerivedIndex { Version = 0, Edges = [] },
            new RcDerivedIndex { Version = 1, Edges = [new RcDerivedIndexEdge { Path = "src/Core/Queue.cs", Suites = ["other"] }] },
        };
        RcInputManifest s = null!;
        foreach (var index in indexes)
        {
            s = Select(baseline, a, index);
            (s.DirtySuites.Contains("server") && s.DirtySuites.Contains("runner")).ShouldBeTrue("partial-index-keeps-conservative-consumers");
            Has(s, "server", "src/Core/Queue.cs").ShouldBeTrue("partial-index-keeps-inputs");
            foreach (var id in plain.DirtySuites) s.DirtySuites.ShouldContain(id, "index-cannot-subtract:" + id);
        }
        s.DirtySuites.ShouldContain("other", "positive-index-broadens");
        Has(s, "other", "src/Core/Queue.cs").ShouldBeTrue("positive-index-unions-input-record");
        (!s.ExecutionEvidence && !s.GateCredit && s.Activation == "shadow-only").ShouldBeTrue("no-execution-authority");
    }
}
