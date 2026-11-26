using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1820 S3: the committed input owner map, in-process (moved from test-rc-input-owners.ps1). Each case starts
/// from a throwaway Antiphon-shaped repository that carries the working tree's scripts/lib/rc-input-owners.json,
/// commits an edit and reads the input manifest of the two commits. A project closure, a census reader or an
/// explicit root owns a path; a no-op path is silent; every other path is unowned, selects nothing and blocks carry
/// credit. With the test-only policy on, a body edit of test source defers its suites.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(120_000)]
public sealed class RcInputOwnersMapTests : IDisposable
{
    private const string Antiphon = "tests/Antiphon.Tests/Antiphon.Tests.csproj";
    private const string E2E = "tests/Antiphon.E2E/Antiphon.E2E.csproj";
    private const string Resilience = "tests/Antiphon.Resilience.Tests/Antiphon.Resilience.Tests.csproj";
    private const string Messaging = "tests/Antiphon.Messaging.Tests/Antiphon.Messaging.Tests.csproj";
    private const string GitInProcess = "tests/Antiphon.GitInProcess.Tests/Antiphon.GitInProcess.Tests.csproj";
    private const string ServerGit = "tests/Antiphon.Server.Git.Tests/Antiphon.Server.Git.Tests.csproj";
    private const string Runner = "tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj";
    private const string Pty = "tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj";
    private const string HelperProject = "tests/Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj";
    private const string HelperSource = "tests/Antiphon.CustodyTestChild/Program.cs";
    private const string Linked = "tests/Antiphon.Tests/TestHelpers/Linked.cs";
    private const string Sibling = "tests/Antiphon.Tests/TestHelpers/Sibling.cs";
    private const string EmptyProject = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
    private const string ServerReference = "<ProjectReference Include=\"../../server/Server.csproj\" />";
    private const string HelperReference = "<ProjectReference Include=\"../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj\" />";

    private static readonly Lazy<string> SourceMap = new(() => RcInputFixtureRepository.ReadSource(RcInputInventory.MapPath));

    private readonly RcInputFixtureRepository repo = new("rio-");
    private readonly HashSet<string> created = new(StringComparer.Ordinal);
    private string seeded = "";

    public void Dispose() => repo.Dispose();

    private static string Project(params string[] items) =>
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup>" + string.Concat(items) + "</ItemGroup></Project>";

    private static JsonObject SourceMapNode() => JsonNode.Parse(SourceMap.Value)!.AsObject();

    [Before(Test)]
    public void Seed()
    {
        repo.Init();
        repo.Put(RcInputInventory.MapPath, SourceMap.Value);
        // Every suite the committed map names exists, so the map's consumers resolve.
        foreach (var root in SourceMapNode()["roots"]!.AsArray())
            foreach (var consumer in root!["consumers"]!.AsArray())
            {
                var id = (string)consumer!;
                if (id != "*" && id.EndsWith(".csproj", StringComparison.Ordinal) && created.Add(id)) repo.Put(id, EmptyProject);
            }
        repo.Put(Antiphon, Project(ServerReference));
        repo.Put(E2E, Project(ServerReference));
        repo.Put(Resilience, Project("<Compile Include=\"../Antiphon.Tests/TestHelpers/Linked.cs\" />"));
        repo.Put(Messaging, EmptyProject);
        repo.Put("server/Server.csproj", EmptyProject);
        repo.Put("server/App.cs", "server source");
        repo.Put(Linked, "linked helper");
        repo.Put(Sibling, "unlinked sibling");
        repo.Put("tests/Antiphon.Messaging.Tests/Own.cs", "messaging test");
        repo.Put("docs/contract.md", "docs");
        repo.Put("Directory.Build.props", "<Project />");
        repo.Put("global.json", "{}");
        repo.Put("NuGet.config", "<configuration />");
        seeded = repo.Commit();
    }

    private void EnsureProject(string id, string text)
    {
        if (created.Add(id)) repo.Put(id, text);
    }

    private RcInputManifest Select(string baseline, string candidate) => RcInputInventory.Manifest(repo.Root, baseline, candidate, null);

    private static string Shown(RcInputManifest s) => string.Join(",", s.DirtySuites);

    private static string[] Sorted(IEnumerable<string> values) =>
        values.Where(v => v != null).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray();

    private static string[] AllSuites(RcInputManifest s) => s.Snapshots[1].Suites.Select(x => x.Id).ToArray();

    private static void SameSet(RcInputManifest s, IEnumerable<string> ids, string label) => Sorted(s.DirtySuites).ShouldBe(Sorted(ids), label);

    private static RcInputSuite Candidate(RcInputManifest s, string id)
    {
        s.Snapshots.Length.ShouldBe(2, "two snapshots");
        var suites = s.Snapshots[1].Suites.Where(x => x.Id == id).ToArray();
        suites.Length.ShouldBe(1, "one suite " + id);
        return suites[0];
    }

    private static RcInputRecord[] Present(RcInputSuite suite, string path) => suite.Inputs.Where(r => r.Path == path && r.Present).ToArray();

    private static void Owns(RcInputManifest s, string id, string path, string reason, string label)
    {
        var records = Present(Candidate(s, id), path);
        records.Length.ShouldBe(1, label);
        records[0].Reason.Split(';').ShouldContain(reason, label + ": " + records[0].Reason);
    }

    private static void Lacks(RcInputManifest s, string id, string path, string label) => Present(Candidate(s, id), path).ShouldBeEmpty(label);

    private static RcUnownedPath[] UnownedRows(RcInputManifest s, string path) =>
        (s.Unowned ?? []).Where(r => r != null && r.Path == path).ToArray();

    private static string[] Deferred(RcInputManifest s) => s.TestOnlyDeferred ?? [];

    private static void StaysShadow(RcInputManifest s, string label)
    {
        s.GateCredit.ShouldBeFalse(label);
        s.Activation.ShouldBe("shadow-only", label);
    }

    private static string NoopMap(params (string Path, string Reason)[] rows) => new JsonObject
    {
        ["schemaVersion"] = 1,
        ["rows"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject { ["path"] = r.Path, ["reason"] = r.Reason, ["evidence"] = "fixture" }).ToArray()),
    }.ToJsonString();

    // Replaces UnknownPathDirtiesAll. That case required unmatched-path-fallback to select every suite. An unknown
    // path now selects nothing and is flagged.
    [Test]
    public void UnknownPathSelectsNothing()
    {
        var baseline = seeded;
        repo.Put("stray/outside.txt", "unknown");
        var s = Select(baseline, repo.Commit());
        Sorted(s.DirtySuites).SequenceEqual(Sorted(AllSuites(s))).ShouldBeFalse("unknown-does-not-select-all:" + Shown(s));
        s.DirtySuites.ShouldBeEmpty("unknown-selects-nothing");
        Lacks(s, Messaging, "stray/outside.txt", "unknown-not-an-input");
        Lacks(s, Antiphon, "stray/outside.txt", "unknown-not-antiphon");
        var rows = UnownedRows(s, "stray/outside.txt");
        rows.Length.ShouldBe(1, "unknown-hint");
        rows[0].Hint.ShouldBe("unknown", "unknown-hint");
        s.NeedsOrchestratorDecision.ShouldBeTrue("unknown-needs-decision");
        s.CreditBlockedBy.ShouldBe("unowned-paths", "unknown-credit");
        StaysShadow(s, "unknown-stays-shadow");
        s.ExecutionEvidence.ShouldBeFalse("unknown-stays-shadow");
    }

    [Test]
    public void NewCsprojNobodyReferencesIsFlagged()
    {
        var baseline = seeded;
        const string path = "src/BrandNew.Thing/BrandNew.Thing.csproj";
        repo.Put(path, EmptyProject);
        var s = Select(baseline, repo.Commit());
        Lacks(s, Messaging, path, "new-csproj-not-selected");
        Lacks(s, Antiphon, path, "new-csproj-not-antiphon");
        s.DirtySuites.ShouldBeEmpty("new-csproj-selects-nothing");
        var rows = UnownedRows(s, path);
        rows.Length.ShouldBe(1, "new-csproj-hint");
        rows[0].Hint.ShouldBe("new-csproj", "new-csproj-hint");
        s.NeedsOrchestratorDecision.ShouldBeTrue("new-csproj-needs-decision");
    }

    [Test]
    public void NoOpPathSelectsNothingAndIsSilent()
    {
        var baseline = seeded;
        repo.Put(RcInputInventory.NoopPath, NoopMap(("docs/reviews/**", "no test reads review notes")));
        repo.Put("docs/reviews/note.md", "review note");
        baseline = repo.Commit();
        repo.Put("docs/reviews/note.md", "changed review note");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("noop-selects-nothing");
        Lacks(s, Antiphon, "docs/reviews/note.md", "noop-not-an-input");
        UnownedRows(s, "docs/reviews/note.md").ShouldBeEmpty("noop-not-flagged");
        s.NeedsOrchestratorDecision.ShouldBeFalse("noop-stays-silent: unresolved=" + string.Join(",", s.Unresolved)
            + " unowned=" + string.Join(",", (s.Unowned ?? []).Select(r => r.Path)));
        string.IsNullOrEmpty(s.CreditBlockedBy).ShouldBeTrue("noop-credit:" + s.CreditBlockedBy);
    }

    [Test]
    public void DirectoryWalkReadersSelectSuites()
    {
        var baseline = seeded;
        EnsureProject(ServerGit, EmptyProject);
        const string sample = "tests/Antiphon.Tests/Application/StaleWorkingTranscriptWatchdogTests.cs";
        const string migration = "src/Antiphon.Messaging.Service/Migrations/20990101000000_Example.cs";
        repo.Put(sample, "class StaleWorkingTranscriptWatchdogTests { void Body() { int x = 1; } }");
        repo.Put(migration, "migration");
        baseline = repo.Commit();
        repo.Put(sample, "class StaleWorkingTranscriptWatchdogTests { void Body() { int x = 2; } }");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldNotContain(Resilience, "d1a-body-skips-resilience:" + Shown(s));
        s.DirtySuites.ShouldNotContain(ServerGit, "d1a-body-skips-server-git");
        s.DirtySuites.ShouldNotContain(GitInProcess, "d1a-body-skips-git");
        s.DirtySuites.ShouldNotContain(Antiphon, "d1a-body-skips-antiphon");
        Deferred(s).ShouldContain(Resilience + ":" + sample, "d1a-resilience-deferred");
        Deferred(s).ShouldContain(ServerGit + ":" + sample, "d1a-server-git-deferred");
        Deferred(s).ShouldContain(GitInProcess + ":" + sample, "d1a-git-deferred");
        baseline = repo.Head();
        repo.Put(migration, "migration changed");
        s = Select(baseline, repo.Commit());
        Owns(s, Antiphon, migration, "messaging-migration-walk", "d1b-antiphon");
        Lacks(s, Resilience, migration, "d1b-not-resilience");
        s.DirtySuites.ShouldContain(Antiphon, "d1b-dirty:" + Shown(s));
    }

    [Test]
    public void RepoWalkingProjectSelectedForAnyPath()
    {
        var baseline = seeded;
        var map = SourceMapNode();
        var roots = new JsonArray();
        foreach (var row in map["roots"]!.AsArray())
            if ((string?)row!["path"] != "**") roots.Add(row.DeepClone());
        roots.Add(new JsonObject { ["path"] = "**", ["consumers"] = new JsonArray(JsonValue.Create(Messaging)), ["reason"] = "repo-walk" });
        map["roots"] = roots;
        repo.Put(RcInputInventory.MapPath, map.ToJsonString());
        baseline = repo.Commit();
        repo.Put("server/App.cs", "repo walk sees an owned product file");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldContain(Messaging, "repo-walk-any-path:" + Shown(s));
        Owns(s, Messaging, "server/App.cs", "repo-walk", "repo-walk-reason");
        s.DirtySuites.ShouldContain(Antiphon, "repo-walk-keeps-project-owner");
    }

    [Test]
    public void UnownedPathBlocksCarryCredit()
    {
        var baseline = seeded;
        repo.Put("brand-new-area/file.txt", "unowned area");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("credit-selects-nothing");
        s.CreditBlockedBy.ShouldBe("unowned-paths", "credit-blocked");
        s.GateCredit.ShouldBeFalse("credit-gate-stays-false");
        s.NeedsOrchestratorDecision.ShouldBeTrue("credit-needs-decision");
        var rows = UnownedRows(s, "brand-new-area/file.txt");
        rows.Length.ShouldBe(1, "credit-hint");
        rows[0].Hint.ShouldBe("unknown", "credit-hint");
    }

    [Test]
    public void NamedDocSelectsReadingSuite()
    {
        var baseline = seeded;
        const string path = "docs/testing-and-build.md";
        repo.Put(path, "doc contract");
        baseline = repo.Commit();
        repo.Put(path, "doc contract changed");
        var s = Select(baseline, repo.Commit());
        Owns(s, Antiphon, path, "doc-contract-reader", "doc-reader-reason");
        s.DirtySuites.ShouldContain(Antiphon, "doc-reader-dirty:" + Shown(s));
    }

    [Test]
    public void ServerChangeDirtiesAntiphonAndE2E()
    {
        var baseline = seeded;
        repo.Put("server/App.cs", "changed server");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("server-resolved");
        SameSet(s, [Antiphon, E2E], "server-readers-only");
        Owns(s, Antiphon, "server/App.cs", "project-tree:server/Server.csproj", "server-in-antiphon-closure");
        Owns(s, E2E, "server/App.cs", "project-tree:server/Server.csproj", "server-in-e2e-closure");
        Lacks(s, Messaging, "server/App.cs", "server-misses-messaging");
    }

    [Test]
    public void DocsChangeDirtiesRepoReaders()
    {
        var baseline = seeded;
        repo.Put("docs/contract.md", "changed docs");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("docs-resolved");
        SameSet(s, AllSuites(s), "docs-every-suite");
        Owns(s, Antiphon, "docs/contract.md", "repository-reader-non-code", "docs-antiphon-reason");
        Owns(s, E2E, "docs/contract.md", "repository-reader-non-code", "docs-e2e-reason");
        Owns(s, Messaging, "docs/contract.md", "repository-reader-non-code", "docs-messaging-reason");
        Owns(s, Resilience, "docs/contract.md", "repository-reader-non-code", "docs-resilience-reason");
    }

    [Test]
    public void BuildGraphChangeForcesFull()
    {
        var baseline = seeded;
        repo.Put("Directory.Build.props", "<Project><PropertyGroup><Fixture>changed</Fixture></PropertyGroup></Project>");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("build-graph-resolved");
        SameSet(s, AllSuites(s), "build-graph-dirties-all");
        Owns(s, Messaging, "Directory.Build.props", "forced-full-build-graph", "build-graph-forced-reason");
    }

    [Test]
    public void LinkedSourceSelectsIncludingProject()
    {
        var baseline = seeded;
        repo.Put(Linked, "class LinkedHelper { void Body() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(Linked, "class LinkedHelper { void Body() { int x = 2; } }");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("link-resolved");
        // The Compile edge still selects the including project. A method-body edit does not select the walkers.
        SameSet(s, [Resilience], "link-selects-including");
        Owns(s, Resilience, Linked, "Compile", "link-compile-edge");
        s.DirtySuites.ShouldNotContain(ServerGit, "link-body-skips-server-git");
        Lacks(s, Messaging, Linked, "link-misses-messaging");
        Deferred(s).ShouldContain(Antiphon + ":" + Linked, "link-antiphon-deferred");
    }

    [Test]
    public void UnrelatedFileSelectsNothing()
    {
        var baseline = seeded;
        repo.Put(Sibling, "changed unlinked sibling");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("sibling-resolved");
        // A body edit of an unlinked test file selects nothing. The walkers stay deferred.
        s.DirtySuites.ShouldBeEmpty("sibling-selects-nothing");
        foreach (var id in new[] { Antiphon, GitInProcess, Resilience, ServerGit })
            Deferred(s).ShouldContain(id + ":" + Sibling, "sibling-deferred:" + id);
        baseline = repo.Head();
        repo.Put("tests/Antiphon.Messaging.Tests/Own.cs", "changed unrelated");
        s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("unrelated-resolved");
        s.DirtySuites.ShouldBeEmpty("unrelated-selects-nothing");
        Deferred(s).ShouldContain(Messaging + ":tests/Antiphon.Messaging.Tests/Own.cs", "unrelated-messaging-deferred");
        Deferred(s).ShouldContain(Antiphon + ":tests/Antiphon.Messaging.Tests/Own.cs", "unrelated-antiphon-deferred");
        Lacks(s, E2E, "tests/Antiphon.Messaging.Tests/Own.cs", "unrelated-misses-e2e");
    }

    [Test]
    public void RuntimeReaderCensus()
    {
        // AppHost is claimed by Observability's project closure so the all-suite fallback cannot pretend
        // Antiphon.Tests owns Program.cs.
        const string observability = "tests/Antiphon.Observability.Tests/Antiphon.Observability.Tests.csproj";
        using var census = JsonDocument.Parse(RcInputFixtureRepository.ReadSource("scripts/lib/rc-input-reader-census.json"));
        var table = census.RootElement;
        table.GetProperty("schemaVersion").GetInt32().ShouldBe(1, "census-schema");
        var rows = table.GetProperty("rows").EnumerateArray().ToArray();
        rows.Length.ShouldBeGreaterThanOrEqualTo(14, "census-groups");
        EnsureProject(observability, Project("<ProjectReference Include=\"../../Antiphon.AppHost/Antiphon.AppHost.csproj\" />"));
        repo.Put("Antiphon.AppHost/Antiphon.AppHost.csproj", EmptyProject);
        repo.Put("Antiphon.AppHost/Program.cs", "seed program");
        static string[] Strings(JsonElement row, string name) =>
            row.TryGetProperty(name, out var array) ? array.EnumerateArray().Select(item => item.GetString()!).ToArray() : [];
        static string Group(JsonElement row) => row.TryGetProperty("group", out var group) ? group.GetString() ?? "" : "";
        foreach (var row in rows)
        {
            string.IsNullOrWhiteSpace(Group(row)).ShouldBeFalse("census-group-name");
            foreach (var id in Strings(row, "suites")) EnsureProject(id, EmptyProject);
            foreach (var path in Strings(row, "paths"))
            {
                // A csproj seed must stay well-formed or the project walk marks the whole census unresolved.
                if (path.EndsWith(".csproj", StringComparison.Ordinal)) EnsureProject(path, EmptyProject);
                else repo.Put(path, "seed");
            }
        }
        var inventory = RcInputInventory.Read(repo.Root, repo.Commit());
        inventory.Unresolved.ShouldBeEmpty("census-unresolved");
        var missing = new List<string>();
        foreach (var row in rows)
            foreach (var path in Strings(row, "paths"))
                foreach (var id in Strings(row, "suites"))
                {
                    var suite = inventory.Suites.Where(x => x.Id == id).ToArray();
                    if (suite.Length != 1 || !PresentOwned(suite[0], path)) missing.Add("census:" + Group(row) + ":" + id + ":" + path);
                }
        missing.ShouldBeEmpty();
    }

    private static string OwnershipStatus(IEnumerable<RcInputSuite> suites, IEnumerable<string> paths)
    {
        foreach (var suite in suites)
            foreach (var path in paths)
                if (PresentOwned(suite, path)) return "selected";
        return "missed";
    }

    private static string SourceHead()
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = RcInputFixtureRepository.SourceRoot, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var arg in new[] { "rev-parse", "HEAD" }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var output = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException("git rev-parse HEAD did not exit within 60 s");
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git rev-parse HEAD exited " + process.ExitCode);
        return output.Result.Trim();
    }

    /// <summary>
    /// The HistoricalSelectionSoundness case of the deleted test-rc-input-owners.ps1 (upstream 0058c60c5, b18b9ef13,
    /// 39a3d7a19), in-process. tests/fixtures/incremental-selection-soundness.json is replayed through the committed owner
    /// inventory of this checkout's HEAD: a case is selected when its suite owns one of its changed paths as a present
    /// input that is not a fallback. Three rows, each checked: contract (every case's recorded currentMap), unowned (a
    /// path the census lists as unowned is missed by the Pty suite) and composer-inputs (each Composer input is an owned
    /// input of the Pty suite on its own). IncrementalSelectionSoundnessTests.Recorded_owner_map_status_matches_the_committed_map
    /// runs it.
    /// </summary>
    internal static void HistoricalSelectionSoundness()
    {
        const string replay = "RIO HistoricalSelectionSoundness";
        var sourceRoot = RcInputFixtureRepository.SourceRoot;
        var failed = new List<string>();
        var errors = new List<string>();
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(sourceRoot, "tests", "fixtures", "incremental-selection-soundness.json")));
        var cases = corpus.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        if (cases.Length == 0) errors.Add("soundness corpus contains no cases");
        var inventory = RcInputInventory.Read(sourceRoot, SourceHead());
        // The historical fixture contains paths. Replay committed ownership before the manifest examines the contents of
        // the original before/after files.
        foreach (var item in cases)
        {
            var id = item.GetProperty("id").GetString();
            var suiteProject = item.GetProperty("suiteProject").GetString();
            var suites = inventory.Suites.Where(s => s.Project == suiteProject).ToArray();
            if (suites.Length == 0) errors.Add(id + ": missing suite for " + suiteProject);
            var status = OwnershipStatus(suites, item.GetProperty("changedPaths").EnumerateArray().Select(p => p.GetString()!));
            var expected = item.GetProperty("currentMap").GetString();
            if (status != expected) errors.Add(id + ": expected " + expected + ", got " + status);
        }
        if (errors.Count > 0) failed.Add(replay + ".contract: " + string.Join("; ", errors));

        var pty = inventory.Suites.Where(s => s.Project == Pty).ToArray();
        // The inventory census names tracked paths with no owner. Use a current census entry so the witness remains
        // unowned when ownership rules evolve.
        var unownedPath = (inventory.Unowned ?? []).FirstOrDefault()?.Path;
        var unownedStatus = string.IsNullOrEmpty(unownedPath) ? "no unowned path" : OwnershipStatus(pty, [unownedPath]);
        if (!(pty.Length == 1 && unownedStatus == "missed"))
            failed.Add(replay + ".unowned: Pty suites=" + pty.Length + ", expected missed for '" + unownedPath + "', got " + unownedStatus);

        // Check each Composer path independently; siblings can still select the suite.
        string[] composerPaths =
        [
            "src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs",
            "src/Antiphon.Agents.Pty/ComposerInputProbe.cs",
            "src/Antiphon.FakeClaude/Program.cs",
            "tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs",
            "tests/Antiphon.Agents.Pty.Tests/ComposerInputProbeTests.cs",
            "tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs",
        ];
        var missing = composerPaths.Where(path => pty.Length == 0 || !PresentOwned(pty[0], path)).ToArray();
        if (!(pty.Length == 1 && missing.Length == 0))
            failed.Add(replay + ".composer-inputs: Pty suites=" + pty.Length + ", missing present owned inputs: " + string.Join(", ", missing));
        failed.ShouldBeEmpty(string.Join(" | ", failed));
    }

    private static bool PresentOwned(RcInputSuite suite, string path)
    {
        var record = Present(suite, path);
        if (record.Length != 1) return false;
        var reasons = record[0].Reason.Split(';');
        return !reasons.Contains("unmatched-path-fallback") && !reasons.Contains("unresolved-conservative-all-inputs");
    }

    [Test]
    public void ScriptChangeSelectsSessionRunner()
    {
        var baseline = seeded;
        EnsureProject(Runner, EmptyProject);
        repo.Put("scripts/build-slot.ps1", "seed slot");
        baseline = repo.Commit();
        repo.Put("scripts/build-slot.ps1", "changed slot");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("slot-unresolved");
        s.DirtySuites.ShouldContain(Runner, "slot-selects-session-runner:" + Shown(s));
        Owns(s, Runner, "scripts/build-slot.ps1", "repository-reader-non-code", "slot-runner-reason");
        s.DirtySuites.ShouldContain(Antiphon, "slot-still-selects-antiphon");
    }

    [Test]
    public void DockerAndComposeSelectRuntimeSuites()
    {
        var baseline = seeded;
        const string custody = "docker/session-runner-grok/antiphon-custody-enter.sh";
        EnsureProject(Runner, EmptyProject);
        EnsureProject(Pty, EmptyProject);
        repo.Put(custody, "seed custody");
        repo.Put("docker-compose.runner-grok.yml", "seed compose");
        repo.Put("Dockerfile", "seed docker");
        baseline = repo.Commit();
        repo.Put(custody, "changed custody");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("docker-unresolved");
        s.DirtySuites.ShouldContain(Runner, "docker-selects-runner:" + Shown(s));
        s.DirtySuites.ShouldContain(Pty, "docker-selects-pty:" + Shown(s));
        Owns(s, Pty, custody, "repository-reader-non-code", "docker-pty-reason");
        baseline = repo.Head();
        repo.Put("docker-compose.runner-grok.yml", "changed compose");
        s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("compose-unresolved");
        s.DirtySuites.ShouldContain(Runner, "compose-selects-runner:" + Shown(s));
        Owns(s, Runner, "docker-compose.runner-grok.yml", "repository-reader-non-code", "compose-runner-reason");
        baseline = repo.Head();
        repo.Put("Dockerfile", "changed dockerfile");
        s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("dockerfile-unresolved");
        s.DirtySuites.ShouldContain(Runner, "dockerfile-selects-runner:" + Shown(s));
        Owns(s, Runner, "Dockerfile", "repository-reader-non-code", "dockerfile-runner-reason");
    }

    [Test]
    public void ExplicitForcedRootsNotFallback()
    {
        var baseline = seeded;
        var paths = new[] { "antiphon.areas.json", ".editorconfig", ".github/workflows/ci.yml", "tests/derived-impact-index.json", "tests/impacted-classes.json" };
        foreach (var path in paths) repo.Put(path, "seed");
        repo.Commit();
        foreach (var path in paths)
        {
            baseline = repo.Head();
            repo.Put(path, "changed " + path);
            var s = Select(baseline, repo.Commit());
            s.Unresolved.ShouldBeEmpty("forced-unresolved:" + path);
            s.DirtySuites.ShouldContain(Messaging, "forced-dirties-messaging:" + path + ":" + Shown(s));
            Owns(s, Messaging, path, "forced-full-root", "forced-reason:" + path);
            var record = Present(Candidate(s, Messaging), path);
            (record.Length == 1 && !record[0].Reason.Split(';').Contains("unmatched-path-fallback")).ShouldBeTrue("forced-not-fallback:" + path);
        }
    }

    [Test]
    public void NugetConfigIgnoreCaseForcesFull()
    {
        var baseline = seeded;
        repo.Put("server/nuget.config", "<configuration />");
        baseline = repo.Commit();
        repo.Put("server/nuget.config", "<configuration><changed /></configuration>");
        var s = Select(baseline, repo.Commit());
        s.Unresolved.ShouldBeEmpty("nuget-case-unresolved");
        SameSet(s, AllSuites(s), "nuget-case-dirties-all");
        Owns(s, Messaging, "server/nuget.config", "forced-full-build-graph", "nuget-case-forced-reason");
    }

    [Test]
    public void TestOnlyMethodEditSelectsNothing()
    {
        var baseline = seeded;
        // A method-body edit of test source, and a fixture edit, select nothing. tests/Shared stays forced full.
        // The result names every deferred suite.
        repo.Put(Sibling, "class SiblingProbe { void Body() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(Sibling, "class SiblingProbe { void Body() { int x = 2; } }");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("test-only-selects-nothing");
        foreach (var id in new[] { Antiphon, Resilience, GitInProcess, ServerGit })
            Deferred(s).ShouldContain(id + ":" + Sibling, "deferred:" + id);
        const string fixture = "tests/Antiphon.Tests/Fixtures/probe.txt";
        repo.Put(fixture, "fixture");
        baseline = repo.Commit();
        repo.Put(fixture, "fixture changed");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("fixture-selects-nothing");
        Deferred(s).ShouldContain(Antiphon + ":" + fixture, "fixture-deferred");
        const string shared = "tests/Shared/Probe.cs";
        repo.Put(shared, "class SharedProbe { void Body() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(shared, "class SharedProbe { void Body() { int x = 2; } }");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldNotBeEmpty("shared-stays-forced");
        s.DirtySuites.ShouldContain(Messaging, "shared-dirties-messaging");
    }

    [Test]
    public void AddedTestClassSelectsWalkers()
    {
        var baseline = seeded;
        // Adding a class file selects the source walkers. The containing suite stays deferred and the new class is
        // recorded as changed-not-run.
        const string path = "tests/Antiphon.Tests/Application/BrandNewWalkerProbeTests.cs";
        repo.Put(path, "class BrandNewWalkerProbeTests { void Body() { int x = 1; } }");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldContain(Resilience, "add-selects-resilience:" + Shown(s));
        s.DirtySuites.ShouldContain(GitInProcess, "add-selects-git");
        s.DirtySuites.ShouldContain(ServerGit, "add-selects-servergit");
        s.DirtySuites.ShouldNotContain(Antiphon, "add-defers-antiphon:" + Shown(s));
        (s.ChangedNotRun ?? []).ShouldContain(Antiphon + ":" + path + ":BrandNewWalkerProbeTests", "changed-not-run");
    }

    [Test]
    public void ProductEdgeStillReachesSuite()
    {
        var baseline = seeded;
        // A linked Compile edge still selects the including project when the edit is only a method body. A product
        // file still selects its readers.
        repo.Put(Linked, "class LinkedProbe { void Body() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(Linked, "class LinkedProbe { void Body() { int x = 2; } }");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldContain(Resilience, "link-still-selects:" + Shown(s));
        Owns(s, Resilience, Linked, "Compile", "link-compile-edge");
        s.DirtySuites.ShouldNotContain(GitInProcess, "link-body-skips-walker:" + Shown(s));
        s.DirtySuites.ShouldNotContain(Antiphon, "link-body-skips-antiphon");
        baseline = repo.Head();
        repo.Put("server/App.cs", "product edge");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldContain(Antiphon, "product-reaches:" + Shown(s));
        s.DirtySuites.ShouldContain(E2E, "product-reaches:" + Shown(s));
        Owns(s, Antiphon, "server/App.cs", "project-tree:server/Server.csproj", "product-project-edge");
    }

    [Test]
    public void NoOpOwnerConflictFails()
    {
        var baseline = seeded;
        // A no-op row that matches an explicit owner must not win silently.
        const string path = "docs/testing-and-build.md";
        repo.Put(RcInputInventory.NoopPath, NoopMap(("docs/reviews/**", "reviews"), (path, "hides the doc reader")));
        repo.Put(path, "doc");
        baseline = repo.Commit();
        repo.Put(path, "doc changed");
        var s = Select(baseline, repo.Commit());
        var conflicts = s.NoopConflicts ?? [];
        conflicts.Length.ShouldBeGreaterThanOrEqualTo(1, "noop-conflict-count");
        conflicts.Where(c => c.Contains(path, StringComparison.OrdinalIgnoreCase)).ShouldNotBeEmpty("noop-conflict-names-path:" + string.Join(",", conflicts));
        s.DirtySuites.ShouldContain(Antiphon, "noop-owner-wins:" + Shown(s));
    }

    [Test]
    public void UnownedListAboveFortyIsVisible()
    {
        var baseline = seeded;
        // Above 40 unowned paths the census result shows the count, the first 40 paths, and the path of the full
        // list file.
        for (var i = 1; i <= 41; i++) repo.Put("stray/file" + i + ".txt", "x");
        var s = Select(baseline, repo.Commit());
        var listPath = Path.Combine(repo.Root, ".antiphon/rc-unowned-paths.txt");
        var lines = RcInputInventory.UnownedCensusLines(s.Unowned, listPath);
        var count = lines.Where(l => l.StartsWith("UNOWNED-COUNT ", StringComparison.Ordinal)).ToArray();
        count.Length.ShouldBe(1, "unowned-count:" + string.Join(",", count));
        var match = Regex.Match(count[0], "^UNOWNED-COUNT ([0-9]+)$");
        match.Success.ShouldBeTrue("unowned-count:" + count[0]);
        int.Parse(match.Groups[1].Value).ShouldBeGreaterThan(40, "unowned-count:" + count[0]);
        lines.Count(l => l.StartsWith("UNOWNED-PATH ", StringComparison.Ordinal)).ShouldBe(40, "unowned-first-40");
        lines.Where(l => l.StartsWith("UNOWNED-LIST ", StringComparison.Ordinal)).ToArray().ShouldBe(new[] { "UNOWNED-LIST " + listPath }, "unowned-list");
    }

    [Test]
    public void ExistingCsprojIsNotANewCsproj()
    {
        var baseline = seeded;
        // new-csproj is only for a csproj nobody references that this commit adds. The fixture path names no tracked
        // file of this repository, so the runtime-reader census does not read it as a reader of the real tree.
        const string path = "tools/Antiphon.FixtureOnlyAudit/Antiphon.FixtureOnlyAudit.csproj";
        repo.Put(path, EmptyProject);
        baseline = repo.Commit();
        repo.Put(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Fixture>1</Fixture></PropertyGroup></Project>");
        var s = Select(baseline, repo.Commit());
        var rows = UnownedRows(s, path);
        rows.Length.ShouldBe(1, "existing-csproj-unowned");
        rows[0].Hint.ShouldNotBe("new-csproj", "existing-csproj-hint");
        s.DirtySuites.ShouldBeEmpty("existing-csproj-selects-nothing");
    }

    [Test]
    public void HelperProjectBodySelectsReferencers()
    {
        var baseline = seeded;
        // Two suites ProjectReference one helper. A body edit of the helper's Program.cs selects both. A body edit
        // inside a suite's own project still defers.
        repo.Put(HelperProject, EmptyProject);
        repo.Put(HelperSource, "class HelperProgram { void Body() { int x = 1; } }");
        repo.Put(Runner, Project(HelperReference));
        repo.Put(Antiphon, Project(ServerReference, HelperReference));
        baseline = repo.Commit();
        repo.Put(HelperSource, "class HelperProgram { void Body() { int x = 2; } }");
        var s = Select(baseline, repo.Commit());
        SameSet(s, [Antiphon, Runner], "helper-selects-both");
        s.DirtySuites.ShouldNotContain(HelperProject, "helper-own-stays-deferred:" + Shown(s));
        Deferred(s).ShouldContain(HelperProject + ":" + HelperSource, "helper-own-deferred-row");
        StaysShadow(s, "helper-stays-shadow");
        const string own = "tests/Antiphon.Tests/OwnBodyProbe.cs";
        repo.Put(own, "class OwnBodyProbe { void Body() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(own, "class OwnBodyProbe { void Body() { int x = 2; } }");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("own-body-still-defers");
        Deferred(s).ShouldContain(Antiphon + ":" + own, "own-body-deferred-row");
    }

    [Test]
    public void E2EAddedTestSelectsSuites()
    {
        var baseline = seeded;
        // An added E2E file, and an added [Test] method on an existing E2E class, select every suite. A later body
        // edit still defers.
        repo.Put("tests/Antiphon.E2E/AddedProbeTests.cs", "class AddedE2EProbeTests { [Test] void AddedMethod() { int x = 1; } }");
        var s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-file-selects-all");
        s.DirtySuites.ShouldContain(E2E, "e2e-file-selects-e2e");
        StaysShadow(s, "e2e-file-stays-shadow");
        const string existing = "tests/Antiphon.E2E/ExistingProbeTests.cs";
        repo.Put(existing, "class ExistingE2EProbeTests { void OldMethod() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(existing, "class ExistingE2EProbeTests { void OldMethod() { int x = 1; } [Test] void AddedMethod() { int x = 2; } }");
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-method-selects-all");
        baseline = repo.Head();
        repo.Put(existing, "class ExistingE2EProbeTests { void OldMethod() { int x = 1; } [Test] void AddedMethod() { int x = 3; } }");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("e2e-body-still-defers");
    }

    // A net-zero rename, a moved test and an added [Arguments] row must select. The [Test] count does not.
    [Test]
    public void E2EIdentityChangeSelectsSuites()
    {
        // class|method|Arguments identities, not the [Test] count. A net-zero rename, a same-identity move between files
        // and an added [Arguments] row select every suite. A later body edit still defers.
        const string renamed = "tests/Antiphon.E2E/RenameProbeTests.cs";
        repo.Put(renamed, "class RenameE2EProbeTests { [Test] void OriginalMethod() { int x = 1; } }");
        var baseline = repo.Commit();
        repo.Put(renamed, "class RenameE2EProbeTests { [Test] void RenamedMethod() { int x = 1; } }");
        var s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-rename-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-rename-stays-shadow");
        string.IsNullOrEmpty(s.CreditBlockedBy).ShouldBeTrue("e2e-rename-credit:" + s.CreditBlockedBy);
        const string left = "tests/Antiphon.E2E/MoveLeftProbeTests.cs";
        const string right = "tests/Antiphon.E2E/MoveRightProbeTests.cs";
        repo.Put(left, "class MovedE2EProbeTests { [Test] void MovedMethod() { int x = 1; } }");
        repo.Put(right, "class MovedE2EProbeTests { void Holder() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(left, "class MovedE2EProbeTests { void Holder() { int x = 1; } }");
        repo.Put(right, "class MovedE2EProbeTests { [Test] void MovedMethod() { int x = 1; } }");
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-move-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-move-stays-shadow");
        const string rows = "tests/Antiphon.E2E/ArgumentsProbeTests.cs";
        repo.Put(rows, "class ArgumentsE2EProbeTests { [Test] void RowMethod() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(rows, "class ArgumentsE2EProbeTests { [Test] [Arguments(\"added-row\")] void RowMethod() { int x = 1; } }");
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-arguments-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-arguments-stays-shadow");
        string.IsNullOrEmpty(s.CreditBlockedBy).ShouldBeTrue("e2e-arguments-credit:" + s.CreditBlockedBy);
        baseline = repo.Head();
        repo.Put(rows, "class ArgumentsE2EProbeTests { [Test] [Arguments(\"added-row\")] void RowMethod() { int x = 2; } }");
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("e2e-identity-body-still-defers:" + Shown(s));
        // A multi-line [Arguments] row, a new[] attribute and a generic method are roster changes. Dropping the form
        // must still select every suite.
        const string multi = "tests/Antiphon.E2E/MultiLineArgumentsProbeTests.cs";
        repo.Put(multi, "class MultiLineArgumentsE2EProbeTests { [Test] void RowMethod() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(multi, "class MultiLineArgumentsE2EProbeTests {\n    [Test]\n    [Arguments(\n        \"b\")]\n    void RowMethod() { int x = 1; }\n}\n");
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-multiline-arguments-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-multiline-arguments-stays-shadow");
        const string array = "tests/Antiphon.E2E/ArrayArgumentsProbeTests.cs";
        repo.Put(array, "class ArrayArgumentsE2EProbeTests { [Test] [Arguments(new[] { \"a\" })] void OriginalMethod() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(array, "class ArrayArgumentsE2EProbeTests { [Test] [Arguments(new[] { \"a\" })] void RenamedMethod() { int x = 1; } }");
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-array-arguments-rename-selects-all:" + Shown(s));
        const string generic = "tests/Antiphon.E2E/GenericProbeTests.cs";
        repo.Put(generic, "class GenericE2EProbeTests { [Test] Task A<T>() { return Task.CompletedTask; } }");
        baseline = repo.Commit();
        repo.Put(generic, "class GenericE2EProbeTests { [Test] Task B<T>() { return Task.CompletedTask; } }");
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-generic-rename-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-generic-rename-stays-shadow");
    }

    private static string VerbatimProbe(string method, string body) => """"
        class VerbatimRenameE2EProbeTests {
            [Test] void First() {
                string name = "n";
                var pattern = @"^Session 1 - ";
                var quoted = @"say ""hi""";
                var interp = $@"C:\src\{name.Replace(" ", "-")}";
                var swapped = @$"pre {name} post";
                var raw = """
                    hello
                    """;
                var rawInterp = $$$"""
                    {{{suffix}}}
                    Work on {{ issue.title }}.
                    """;
            }
            [Test] void METHOD() { int x = BODY; }
        }

        """".Replace("METHOD", method).Replace("BODY", body);

    private static string CommaProbe(string extra, string body) => """
        class CommaArgumentsE2EProbeTests {
            [Test, Arguments(1)EXTRA] void RowMethod() { int x = BODY; }
        }

        """.Replace("EXTRA", extra).Replace("BODY", body);

    // A rename after a C# verbatim string must select. A later body edit of that method must defer.
    [Test]
    public void VerbatimStringRenameSelectsSuites()
    {
        // A method renamed after @" $@" and @$" strings, a raw """ string and an interpolated raw string must select. A
        // later body edit must defer, so the selection is the parsed identity and not an incomplete file. A comma-list
        // [Test, Arguments(...)] row is part of that identity.
        const string file = "tests/Antiphon.E2E/VerbatimRenameProbeTests.cs";
        repo.Put(file, VerbatimProbe("OriginalMethod", "1"));
        var baseline = repo.Commit();
        repo.Put(file, VerbatimProbe("RenamedMethod", "1"));
        var s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-verbatim-rename-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-verbatim-rename-stays-shadow");
        baseline = repo.Head();
        repo.Put(file, VerbatimProbe("RenamedMethod", "2"));
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("e2e-verbatim-body-still-defers:" + Shown(s));
        const string comma = "tests/Antiphon.E2E/CommaArgumentsProbeTests.cs";
        repo.Put(comma, CommaProbe("", "1"));
        baseline = repo.Commit();
        repo.Put(comma, CommaProbe(", Arguments(2)", "1"));
        s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-comma-arguments-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-comma-arguments-stays-shadow");
        baseline = repo.Head();
        repo.Put(comma, CommaProbe(", Arguments(2)", "2"));
        s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldBeEmpty("e2e-comma-arguments-body-still-defers:" + Shown(s));
    }

    private static string FailClosedProbe(string body) => """
        class FailClosedE2EProbeTests {
            [Test] public int NotAMethod => 1;
            [Test] void RealMethod() { int x = BODY; }
        }

        """.Replace("BODY", body);

    // An unparsed [Test] makes a body edit select. The identity map itself does not change.
    [Test]
    public void FailClosedOnlySelectsSuites()
    {
        // [Test] on a property is not a method. A body edit of the real method keeps the same identity map, so only the
        // incomplete-file rule selects.
        const string file = "tests/Antiphon.E2E/FailClosedProbeTests.cs";
        repo.Put(file, FailClosedProbe("1"));
        var baseline = repo.Commit();
        repo.Put(file, FailClosedProbe("2"));
        var s = Select(baseline, repo.Commit());
        SameSet(s, AllSuites(s), "e2e-fail-closed-selects-all:" + Shown(s));
        StaysShadow(s, "e2e-fail-closed-stays-shadow");
    }

    // Every E2E file's parsed tests match an independent [Test] count, and none is incomplete.
    [Test]
    public void RealE2EParseMatchesRegex()
    {
        // Independent of the scanner's own counters: comment-stripped [Test and Arguments( counts, compared with what
        // the parser matched.
        var tree = Path.GetFullPath(RcInputFixtureRepository.SourceRoot);
        var e2eRoot = Path.Combine(tree, "tests", "Antiphon.E2E");
        var testRegex = new Regex(@"\[Test(?![A-Za-z0-9_])", RegexOptions.CultureInvariant);
        var files = Directory.GetFiles(e2eRoot, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        (files.Length > 0).ShouldBeTrue("e2e-tree-empty");
        var misses = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            var parse = RcTestIdentity.Parse(text);
            var tests = testRegex.Matches(RcTestIdentity.CommentStripped(text)).Count;
            var rel = Path.GetFullPath(file).Substring(tree.Length).TrimStart('\\', '/').Replace('\\', '/');
            if (parse.Matched != tests) misses.Add($"e2e-test-count:{rel}:matched={parse.Matched}:regex={tests}");
            if (parse.Incomplete)
                misses.Add($"e2e-incomplete:{rel}:tests={parse.TestAttributes}/{parse.Matched}:args={parse.ArgumentOpeners}/{parse.CapturedArguments}");
        }
        misses.ShouldBeEmpty(string.Join(" | ", misses));
    }

    // A renamed literal owner path must fail closed. Absence of the census is that miss.
    [Test]
    public void OwnerLiteralPathsFailClosed()
    {
        // Every literal owner path, exceptPaths prefix and owner-glob prefix must exist. Renaming the canary, the E2E
        // directory or tests/Shared fails closed and names the missing path.
        var censusRoot = Path.Combine(repo.Scratch, "owner-literal");
        string Literal(string relative) => Path.Combine(censusRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        foreach (var dir in new[] { "tests/Antiphon.E2E", "tests/Shared", "tests/Antiphon.Tests/Agents", "tests/fixtures", "docs/reviews" })
            Directory.CreateDirectory(Literal(dir));
        const string canaryRel = "tests/Antiphon.Tests/Agents/ClaudeDialogWatchdogCanaryTests.cs";
        File.WriteAllText(Literal(canaryRel), "class Canary {}");
        var json = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["suiteProjects"] = "tests/**/*.csproj",
            ["testOnly"] = new JsonObject
            {
                ["enabled"] = true,
                ["paths"] = new JsonArray("tests/**/*.cs", "tests/fixtures/**"),
                ["exceptPaths"] = new JsonArray("tests/Shared/**"),
                ["exceptReasons"] = new JsonArray("forced-full-root"),
                ["productEdgeReasons"] = new JsonArray("runtime-reader-scan"),
            },
            ["roots"] = new JsonArray(
                new JsonObject { ["path"] = canaryRel, ["consumers"] = new JsonArray("tests/Antiphon.Agents.Pty.Tests/Antiphon.Agents.Pty.Tests.csproj"), ["reason"] = "runtime-reader-scan" },
                new JsonObject { ["path"] = "tests/Antiphon.E2E/**/*.cs", ["consumers"] = new JsonArray("*"), ["reason"] = "runtime-reader-unproven", ["walker"] = true },
                new JsonObject { ["path"] = "docs/**", ["except"] = new JsonArray("docs/reviews/**"), ["consumers"] = new JsonArray("*"), ["reason"] = "repository-reader-non-code" }),
        }.ToJsonString();
        var missing = RcInputInventory.MissingOwnerLiteralPaths(censusRoot, json);
        var intact = missing.Length == 0;
        File.Delete(Literal(canaryRel));
        missing = RcInputInventory.MissingOwnerLiteralPaths(censusRoot, json);
        var message = string.Join(" | ", missing);
        var renamed = missing.Count(m => m.StartsWith("missing-literal-path:", StringComparison.Ordinal) && m.Contains(canaryRel, StringComparison.Ordinal)) >= 1;
        File.WriteAllText(Literal(canaryRel), "class Canary {}");
        Directory.Delete(Literal("tests/Antiphon.E2E"));
        missing = RcInputInventory.MissingOwnerLiteralPaths(censusRoot, json);
        var prefixMessage = string.Join(" | ", missing);
        var prefixGone = missing.Count(m => m.StartsWith("missing-literal-path:", StringComparison.Ordinal) && m.Contains("tests/Antiphon.E2E", StringComparison.Ordinal)) >= 1;
        Directory.CreateDirectory(Literal("tests/Antiphon.E2E"));
        Directory.Delete(Literal("tests/Shared"));
        missing = RcInputInventory.MissingOwnerLiteralPaths(censusRoot, json);
        var exceptMessage = string.Join(" | ", missing);
        var exceptGone = missing.Count(m => m.StartsWith("missing-literal-path:", StringComparison.Ordinal) && m.Contains("tests/Shared", StringComparison.Ordinal)) >= 1;
        intact.ShouldBeTrue("literal-intact-not-quiet");
        renamed.ShouldBeTrue("literal-rename-not-detected:" + message);
        prefixGone.ShouldBeTrue("literal-prefix-not-detected:" + prefixMessage);
        exceptGone.ShouldBeTrue("literal-except-not-detected:" + exceptMessage);
    }

    // The committed owner map is checked against this tree. Renaming the canary must fail that check.
    [Test]
    public void RealOwnerMapLiteralPathsExist()
    {
        // The committed map is checked against this repository, and the census command answers the same. A scratch copy
        // of those literal prefixes must go red when the canary file is renamed.
        var sourceRoot = RcInputFixtureRepository.SourceRoot;
        var json = SourceMap.Value;
        var live = RcInputInventory.MissingOwnerLiteralPaths(sourceRoot, json);
        live.ShouldBeEmpty("real-owner-map:" + string.Join(" | ", live));
        var census = new StringWriter();
        var exit = RcInputCensus.OwnerPaths(sourceRoot, census);
        var censusOut = census.ToString().Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", " | ");
        (exit == 0 && census.ToString().Contains("missing-literal-path=0", StringComparison.Ordinal)).ShouldBeTrue("real-census-command:" + exit + ":" + censusOut);
        var map = SourceMapNode();
        static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;
        var rootPaths = map["roots"]!.AsArray().Select(r => Text(r?["path"])).OfType<string>().ToArray();
        var canary = rootPaths.Where(p => p.EndsWith("ClaudeDialogWatchdogCanaryTests.cs", StringComparison.Ordinal)).ToArray();
        canary.Length.ShouldBe(1, "real-canary-row:" + string.Join(",", canary));
        var scratch = Path.Combine(repo.Scratch, "real-map-canary");
        Directory.CreateDirectory(scratch);
        var patterns = new List<string>();
        if (Text(map["suiteProjects"]) is { } suitePattern) patterns.Add(suitePattern);
        if (map["testOnly"] is JsonObject testOnly)
            foreach (var name in new[] { "paths", "exceptPaths" })
                if (testOnly[name] is JsonArray items)
                    patterns.AddRange(items.Select(Text).OfType<string>());
        patterns.AddRange(rootPaths);
        foreach (var pattern in patterns)
        {
            var prefix = RcInputInventory.LiteralPathPrefix(pattern);
            if (prefix == null) continue;
            var full = Path.Combine(scratch, prefix.Replace('/', Path.DirectorySeparatorChar));
            if (pattern.IndexOfAny(['*', '?']) >= 0)
            {
                Directory.CreateDirectory(full);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (!File.Exists(full) && !Directory.Exists(full)) File.WriteAllText(full, "x");
        }
        var scratchIntact = RcInputInventory.MissingOwnerLiteralPaths(scratch, json);
        scratchIntact.ShouldBeEmpty("scratch-map-intact:" + string.Join(" | ", scratchIntact));
        if (canary.Length == 1)
        {
            var canaryFull = Path.Combine(scratch, canary[0].Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(canaryFull)) File.Move(canaryFull, canaryFull + ".renamed");
            var scratchMissing = RcInputInventory.MissingOwnerLiteralPaths(scratch, json);
            scratchMissing.Count(m => m.StartsWith("missing-literal-path:", StringComparison.Ordinal) && m.Contains(canary[0], StringComparison.Ordinal))
                .ShouldBeGreaterThanOrEqualTo(1, "scratch-canary-rename:" + string.Join(" | ", scratchMissing));
        }
    }

    [Test]
    public void CanaryEditSelectsPtySuite()
    {
        var baseline = seeded;
        // A body edit of the canary file (in the live Claude project since unit 60) selects the Pty suite that reads it.
        // Antiphon.Tests, which owns every test source for the derived-impact scan, still defers.
        const string canary = "tests/Antiphon.LiveClaude.Tests/ClaudeDialogWatchdogCanaryTests.cs";
        repo.Put(canary, "class ClaudeDialogWatchdogCanaryTests { void Body() { int x = 1; } }");
        baseline = repo.Commit();
        repo.Put(canary, "class ClaudeDialogWatchdogCanaryTests { void Body() { int x = 2; } }");
        var s = Select(baseline, repo.Commit());
        s.DirtySuites.ShouldContain(Pty, "canary-selects-pty:" + Shown(s));
        Owns(s, Pty, canary, "runtime-reader-scan", "canary-reader-reason");
        s.DirtySuites.ShouldNotContain(Antiphon, "canary-own-stays-deferred:" + Shown(s));
        StaysShadow(s, "canary-stays-shadow");
    }

    [Test]
    public void EnabledFalseMatchesBaseSelection()
    {
        // enabled=false selects the same suites the enabled closure would have selected before deferral. Selection
        // parity, not manifest bytes.
        RcInputManifest Probe(bool enabled, Action? setup, string path, string before, string after)
        {
            var map = SourceMapNode();
            map["testOnly"]!["enabled"] = enabled;
            repo.Put(RcInputInventory.MapPath, map.ToJsonString());
            setup?.Invoke();
            repo.Put(path, before);
            var @base = repo.Commit(allowEmpty: true);
            repo.Put(path, after);
            return RcInputInventory.Manifest(repo.Root, @base, repo.Commit(), null);
        }
        void ArmHelper()
        {
            repo.Put(HelperProject, EmptyProject);
            repo.Put(Runner, Project(HelperReference));
            repo.Put(Antiphon, Project(ServerReference, HelperReference));
        }
        const string siblingBefore = "class SiblingProbe { void Body() { int x = 1; } }";
        const string siblingAfter = "class SiblingProbe { void Body() { int x = 2; } }";
        var on = Probe(true, null, Sibling, siblingBefore, siblingAfter);
        var off = Probe(false, null, Sibling, siblingBefore, siblingAfter);
        SelectionParity("own-body", on, off);
        on.DirtySuites.ShouldBeEmpty("parity-own-defers");
        off.DirtySuites.ShouldContain(Antiphon, "parity-own-base-selects:" + Shown(off));
        on = Probe(true, null, "server/App.cs", "server source", "server source changed");
        off = Probe(false, null, "server/App.cs", "server source", "server source changed");
        SelectionParity("product", on, off);
        Sorted(on.DirtySuites).ShouldBe(Sorted(off.DirtySuites), "parity-product-equal");
        const string helperBefore = "class HelperProgram { void Body() { int x = 1; } }";
        const string helperAfter = "class HelperProgram { void Body() { int x = 2; } }";
        on = Probe(true, ArmHelper, HelperSource, helperBefore, helperAfter);
        off = Probe(false, ArmHelper, HelperSource, helperBefore, helperAfter);
        SelectionParity("helper", on, off);
        off.DirtySuites.ShouldContain(Antiphon, "parity-helper-base:" + Shown(off));
        off.DirtySuites.ShouldContain(Runner, "parity-helper-base:" + Shown(off));
    }

    // The policy-on selection plus the suites it deferred (testOnlyDeferred is suite:path) is the policy-off
    // selection.
    private static void SelectionParity(string name, RcInputManifest on, RcInputManifest off)
    {
        var union = new HashSet<string>(on.DirtySuites.Where(id => !string.IsNullOrEmpty(id)), StringComparer.Ordinal);
        foreach (var row in Deferred(on))
        {
            var cut = row?.IndexOf(':') ?? -1;
            if (cut > 0) union.Add(row!.Substring(0, cut));
        }
        string.Join(";", Sorted(union)).ShouldBe(string.Join(";", Sorted(off.DirtySuites)), "parity:" + name);
        (!on.GateCredit && !off.GateCredit).ShouldBeTrue("parity-gate:" + name);
        (on.Activation == "shadow-only" && off.Activation == "shadow-only").ShouldBeTrue("parity-shadow:" + name);
    }

    [Test]
    public void ReaderRootsAreEverySuite()
    {
        var roots = SourceMapNode()["roots"]!.AsArray();
        foreach (var path in new[] { "docs/**", "**/*.md", "scripts/**", "docker/**", ".claude/**", "**/docker-compose*.yml", "**/Dockerfile*", "client/**" })
        {
            var rule = roots.Where(r => (string?)r!["path"] == path).ToArray();
            rule.Length.ShouldBe(1, "reader-root:" + path);
            var consumers = rule[0]!["consumers"]!.AsArray().Select(c => (string)c!);
            string.Join(";", Sorted(consumers)).ShouldBe("*", "reader-star:" + path);
            ((string?)rule[0]!["reason"]).ShouldBe("repository-reader-non-code", "reader-reason:" + path);
        }
    }
}
