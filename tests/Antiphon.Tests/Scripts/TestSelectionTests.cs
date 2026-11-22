using System.Collections;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313: the change-aware selection proposal (Antiphon.Nightly.TestSelection and the select-test-rosters command)
/// on fixture repositories, in-process. The 2026-11-22 plan (2026-11-22-remaining-ps1-to-csharp.md, unit 3) moved these
/// cases here from scripts/test-test-selection.ps1, deleted with scripts/lib/test-selection.ps1 and
/// scripts/select-test-rosters.ps1. A case that ran the entry script as a child now runs TestSelection.Run on a host.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionTests
{
    [Test]
    public void C1313_InputRefusal()
    {
        using var fx = C1313SelectionRepository.Create();
        fx.Select(fx.B)["mode"].ShouldBe("narrow");
        var blob = fx.Git("rev-parse", fx.B + ":tests/test-selection-policy.json");
        var accepted = new List<string>();
        foreach (var bad in new[] { "HEAD", fx.B[..8], new string('0', 40), blob, "--help" })
        {
            var run = fx.Command(fx.B, bad, json: true);
            if (run.Exit != 2 || run.Out.Length > 0 || !run.Error.StartsWith("selection refused: ", StringComparison.Ordinal)) accepted.Add(bad);
        }
        accepted.ShouldBeEmpty("C1313 invalid-intent-refused");

        fx.B = fx.Candidate(["notes/nested/file.txt"]);
        fx.Git("read-tree", fx.B);
        fx.Git("update-index", "--force-remove", "--", "notes/nested/file.txt");
        var a = fx.Git("commit-tree", fx.Git("write-tree"), "-p", fx.B, "-m", "remove subtree");
        fx.Select(a)["mode"].ShouldBe("full");
        fx.RemoveObject(fx.Git("rev-parse", fx.B + ":notes"));
        var refused = fx.Command(a, fx.B, json: true);
        (refused.Exit == 2 && refused.Out.Length == 0 && refused.Error.Contains("git-read-failed:diff", StringComparison.Ordinal))
            .ShouldBeTrue("C1313 git-read-refused: " + refused.Error);
    }

    [Test]
    public void C1313_CoreAndUnknownFallback()
    {
        using var fx = C1313SelectionRepository.Create();
        var empty = fx.Select(fx.B);
        string[] seven = ["C1313_ProjectAndCatalogUniverse", "C1313_CoreAndUnknownFallback", "C1044_DiscoveryReconciliation", "D13_ExecutionGuards",
            "C599_ExpandedCoverage", "Caps_concurrent_process_spawning_tests_at_one", "Process_spawning_classes_carry_the_limiter"];
        (empty["mode"] as string == "narrow" && C1313.Same(C1313.SetIds(empty), ["core"]) && C1313.Same(C1313.Members(empty, "method"), seven)
            && empty["executionEvidence"] is false && empty["activation"] as string == "unqualified").ShouldBeTrue("C1313 core-seven");

        var full = fx.Select(fx.Candidate(["notes/unmapped.txt"]));
        (full["mode"] as string == "full" && C1313.Same(C1313.SetIds(full), C1313.CatalogSetIds(fx.Catalog))
            && C1313.Strings(full["unselectedSets"]).Length == 0 && C1313.Dicts(full["unresolvedObligations"]).Any()
            && C1313.Strings(full["fallbackReasons"]).Contains("unknown-path:notes/unmapped.txt")).ShouldBeTrue("C1313 unknown-full");
    }

    [Test]
    public void C1313_PinnedPolicy()
    {
        using var fx = C1313SelectionRepository.Create();
        var a = fx.Candidate(["client/src/App.tsx"]);
        // The working tree never counts: only the candidate's blobs are read.
        fx.Write(TestSelection.CatalogPath, "not json");
        fx.Write(TestSelection.PolicyPath, "not json");
        var good = fx.Select(a);
        (good["mode"] as string == "narrow" && C1313.SetIds(good).Contains("client") && good["catalogHash"] as string == fx.Catalog["catalogHash"] as string)
            .ShouldBeTrue("C1313 candidate-blob");
        var policy = C1313SelectionRepository.PolicyJson();
        policy["policyHash"] = new string('0', 64);
        var stale = fx.Command(fx.Candidate([TestSelection.PolicyPath], policy.ToJsonString(C1313SelectionRepository.Indented)), fx.B, json: true);
        (stale.Exit == 2 && stale.Error.Contains("policy-hash-stale", StringComparison.Ordinal)).ShouldBeTrue("C1313 stale-policy-refused: " + stale.Error);
    }

    [Test]
    [Arguments("delete")]
    [Arguments("case-rename")]
    [Arguments("nul-paths")]
    public void C1313_PathIdentity(string variant)
    {
        using var fx = C1313SelectionRepository.Create();
        const string path = "tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs";
        if (variant == "delete")
        {
            // The deleted path's old owner (the baseline catalog) and its new owner (the candidate's) both count.
            var catalog = C1313SelectionRepository.CatalogJson();
            var rules = catalog["rules"]!.AsArray();
            foreach (var rule in rules.Where(r => r!["paths"] is JsonArray paths && paths.Any(p => (string?)p == path)).ToList()) rules.Remove(rule);
            rules.Add(new JsonObject { ["id"] = "P-TEST-NEW-OWNER", ["paths"] = new JsonArray(path), ["sets"] = new JsonArray("main.remainder"), ["full"] = false });
            fx.PolicyCandidate(catalog);
            fx.Git("update-index", "--force-remove", "--", path);
            var r = fx.Select(fx.Git("commit-tree", fx.Git("write-tree"), "-p", fx.B, "-m", "delete"));
            var old = C1313.Dicts(r["changedPaths"]).Where(c => c["path"] as string == path && c["role"] as string == "old").ToList();
            (old.Count == 1 && C1313.Strings(old[0]["sets"]).Contains("provider.codex.offline") && C1313.SetIds(r).Contains("provider.codex.offline")
                && C1313.Dicts(r["removedMembers"]).Any(m => ((IDictionary)m["member"]!)["class"] as string == "Antiphon.Tests.Agents.CodexResponseAnalyzerTests")
                && !C1313.Members(r, "class").Contains("Antiphon.Tests.Agents.CodexResponseAnalyzerTests")).ShouldBeTrue("C1313 old-new-union");
            return;
        }
        if (variant == "case-rename")
        {
            const string renamed = "tests/Antiphon.Tests/Agents/codexResponseAnalyzerTests.cs";
            var blob = fx.Git("rev-parse", fx.B + ":" + path);
            fx.Git("read-tree", fx.B);
            fx.Git("update-index", "--force-remove", "--", path);
            fx.Git("update-index", "--add", "--cacheinfo", "100644," + blob + "," + renamed);
            var r = fx.Select(fx.Git("commit-tree", fx.Git("write-tree"), "-p", fx.B, "-m", "case"));
            (C1313.Same(C1313.Dicts(r["changedPaths"]).Select(c => (string)c["path"]!), [path, renamed]) && r["mode"] as string == "full")
                .ShouldBeTrue("C1313 ordinal-paths");
            return;
        }
        // Index objects make newline and case identity independent of Windows filename restrictions; Git for Windows
        // rejects control characters even for index-only entries unless core.protectNTFS is off for these writes.
        var policyBlob = fx.Git("rev-parse", fx.B + ":" + TestSelection.CatalogPath);
        var paths = new List<string> { "notes/white space.txt", "notes/line\nbreak.txt", "notes/uni-λ.txt" };
        fx.Git("read-tree", fx.B);
        foreach (var p in paths) fx.Git("-c", "core.protectNTFS=false", "update-index", "--add", "--cacheinfo", "100644," + policyBlob + "," + p);
        fx.Git("update-index", "--add", "--cacheinfo", "160000," + fx.B + ",notes/gitlink");
        fx.Git("update-index", "--add", "--cacheinfo", "120000," + policyBlob + ",notes/link");
        paths.Add("notes/gitlink");
        paths.Add("notes/link");
        var odd = fx.Select(fx.Git("commit-tree", fx.Git("write-tree"), "-p", fx.B, "-m", "paths"));
        (C1313.Same(C1313.Dicts(odd["changedPaths"]).Select(c => (string)c["path"]!), paths) && odd["mode"] as string == "full")
            .ShouldBeTrue("C1313 ordinal-paths: " + string.Join("|", C1313.Dicts(odd["changedPaths"]).Select(c => (string)c["path"]!)));
    }

    [Test]
    public void C1313_AuthorityChanges()
    {
        using var fx = C1313SelectionRepository.Create();
        var bad = new List<string>();
        foreach (var path in new[] { "scripts/lib/release-authority.ps1", "Directory.Build.props" })
        {
            var r = fx.Select(fx.Candidate([path]));
            if (r["mode"] as string != "full" || !C1313.Strings(r["fallbackReasons"]).Contains("selection-authority-changed")) bad.Add(path);
        }
        if (!C1313.Strings(fx.Select(fx.PolicyCandidate(C1313SelectionRepository.CatalogJson()))["fallbackReasons"]).Contains("selection-authority-changed")) bad.Add("catalog");
        bad.ShouldBeEmpty("C1313 authority-full");
        var other = fx.Git("commit-tree", fx.Git("rev-parse", fx.B + "^{tree}"), "-m", "unrelated");
        var unrelated = fx.Select(fx.B, other);
        (unrelated["mode"] as string == "full" && C1313.Strings(unrelated["fallbackReasons"]).Contains("baseline-not-ancestor")).ShouldBeTrue("C1313 nonancestor-full");
    }

    [Test]
    public void C1313_DeterministicReasons()
    {
        using var fx = C1313SelectionRepository.Create();
        (string Path, string Rule, string Mode, string Set)[] cases =
        [
            ("server/Program.cs", "P-SERVER", "full", "main.remainder"), ("serverish/Program.cs", "P-UNKNOWN", "full", "core"),
            ("client/src/App.tsx", "P-CLIENT", "narrow", "e2e.browser"), ("clientish/src/App.tsx", "P-UNKNOWN", "full", "core"),
            ("src/Antiphon.Messaging.Service/Program.cs", "P-MSG", "narrow", "main.outbound-broker"), ("src/Antiphon.MessagingExtra/Program.cs", "P-UNKNOWN", "full", "core"),
            ("tools/Antiphon.MarkdownPdf/MarkdownPdfRenderer.cs", "P-PDF", "narrow", "pdf.browser-linux"), ("tools/Antiphon.MarkdownPdfExtra/Renderer.cs", "P-UNKNOWN", "full", "core"),
            ("scripts/deploy-server2.ps1", "P-DOCKER", "narrow", "docker.operations"), ("scripts/deploy-server20-unknown.ps1", "P-UNKNOWN", "full", "core"),
            ("notes/release-authority.ps1", "P-UNKNOWN", "full", "core"), ("notes/Directory.Build.props.txt", "P-UNKNOWN", "full", "core"),
        ];
        var bad = new List<string>();
        foreach (var c in cases)
        {
            var r = fx.Select(fx.Candidate([c.Path]));
            if (r["mode"] as string != c.Mode || !C1313.Dicts(r["changedPaths"]).SelectMany(p => C1313.Strings(p["ruleIds"])).Contains(c.Rule) || !C1313.SetIds(r).Contains(c.Set))
                bad.Add(c.Path);
        }
        var a = fx.Candidate(["client/src/App.tsx"]);
        var one = fx.Select(a);
        var two = fx.Select(a);
        var browser = C1313.Dicts(one["selectedSets"]).Single(s => s["id"] as string == "e2e.browser");
        string[] names = ["AgentE2ETests", "BoardE2ETests", "DirectoryAutocompleteE2ETests", "DispatchConcurrencySettingsE2ETests", "OrchestratorE2ETests",
            "PipelineGlanceE2ETests", "ProjectDeleteE2ETests", "SmokeE2ETests", "WorkflowDeleteTests", "WorkflowOutputTests"];
        if (one["digest"] as string != two["digest"] as string || TestSelectionJson.Canonical(one) != TestSelectionJson.Canonical(two)
            || !C1313.Same(C1313.Dicts(browser["members"]).Select(m => (string)m["class"]!), names.Select(n => "Antiphon.E2E." + n))) bad.Add("digest/browser-roster");
        bad.ShouldBeEmpty("C1313 deterministic-reasons");
    }

    [Test]
    public void C1313_SelectorLineEndings()
    {
        using var fx = C1313SelectionRepository.Create();
        var crlf = fx.SelectorCandidate(lf => lf.Replace("\n", "\r\n"));
        TestSelection.SelectorFiles.All(path => TestSelectionGit.Blob(fx.Root, crlf, path).Contains("\r\n")).ShouldBeTrue("C1313 selector-blob-has-cr");
        var expected = C1313SelectionRepository.LfSelectorHash();
        var accepted = fx.Command(crlf, fx.B, json: true);
        (accepted.Exit == 0 && accepted.Out.Length > 0).ShouldBeTrue("C1313 crlf-selector-accepted: " + accepted.Error);
        var document = (IDictionary)TestSelectionJson.Parse(accepted.Out);
        (document["selectorHash"] as string).ShouldBe(expected, "C1313 crlf-selector-hash-is-lf");
        (TestSelection.Contract(fx.Root, crlf, "rc")["selectorHash"] as string).ShouldBe(expected, "C1313 crlf-producer-hash-is-lf");

        // An edit of the candidate's selector source (content, case, a space, a lone CR) is a different selector.
        foreach (var (label, mutate) in new (string, Func<string, string>)[]
        {
            ("selector-content-refused", lf => "X" + lf.Replace("\n", "\r\n")),
            ("selector-case-refused", lf => lf.Replace("public", "PUBLIC").Replace("\n", "\r\n")),
            ("selector-space-refused", lf => (lf[..1] + " " + lf[1..]).Replace("\n", "\r\n")),
            ("embedded-cr-refused", lf => lf.Insert(1, "\r")),
        })
        {
            var run = fx.Command(fx.SelectorCandidate(mutate), fx.B, json: true);
            (run.Exit == 2 && run.Error.Contains("selector-source-mismatch", StringComparison.Ordinal)).ShouldBeTrue("C1313 " + label + ": " + run.Error);
        }

        // The running selector's own source (embedded in the core) is the checkout's, as LF text.
        foreach (var path in TestSelection.SelectorFiles)
            TestSelectionJson.LfText(TestSelection.RunningSource(path)).ShouldBe(C1313SelectionRepository.LfRepoText(path), "C1313 running-source-is-the-checkout " + path);
        // A CRLF copy of the running source is the same selector; an edited one is not.
        string Pinned(string path) => C1313SelectionRepository.LfRepoText(path);
        string RunningCrlf(string path) => C1313SelectionRepository.LfRepoText(path).Replace("\n", "\r\n");
        TestSelection.SelectorFiles.All(path => RunningCrlf(path).Contains("\r\n")).ShouldBeTrue("C1313 disk-selector-has-cr");
        var diskHash = NightlyDiagnosticCases.Refusal(() => TestSelection.SelectorHash(Pinned, RunningCrlf));
        diskHash.ShouldBe("", "C1313 crlf-disk-selector-accepted");
        TestSelection.SelectorHash(Pinned, RunningCrlf).ShouldBe(expected, "C1313 crlf-disk-selector-hash-is-lf");
        string Edited(string path)
        {
            var text = RunningCrlf(path);
            return path == TestSelection.SelectorFiles[0] ? text.Insert(text.IndexOf('\n') + 1, "// c1313-disk-edit\r\n") : text;
        }
        NightlyDiagnosticCases.Refusal(() => TestSelection.SelectorHash(Pinned, Edited)).ShouldBe("selector-source-mismatch", "C1313 crlf-disk-selector-refused");
    }

    [Test]
    public void C1313_CommandOutputs()
    {
        using var fx = C1313SelectionRepository.Create();
        var a = fx.Candidate(["client/src/App.tsx"]);
        var selection = fx.Select(a);
        var json = fx.Command(a, fx.B, json: true);
        json.Exit.ShouldBe(0, json.Error);
        json.Out.ShouldBe(TestSelectionJson.Canonical(selection), "C1313 command-json");

        var text = fx.Command(a, fx.B, json: false);
        var lines = text.Out.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToArray();
        lines[0].ShouldBe("mode=narrow activation=unqualified executionEvidence=false digest=" + selection["digest"]);
        lines.ShouldContain("client eligibility=unattended platforms=any");
        lines.ShouldContain(l => l.StartsWith("new client/src/App.tsx: P-CLIENT", StringComparison.Ordinal) || l.StartsWith("old client/src/App.tsx: P-CLIENT", StringComparison.Ordinal));
        lines[^1].ShouldStartWith("prerequisites: ");

        // Written once: a second run never overwrites an older intent.
        var output = fx.Fixture.PathOf("selection.json");
        fx.Command(a, fx.B, json: false, outputPath: output).Exit.ShouldBe(0);
        File.ReadAllText(output).ShouldBe(TestSelectionJson.Canonical(selection) + "\n");
        fx.Command(a, fx.B, json: false, outputPath: output).Exit.ShouldBe(2, "C1313 output-create-new");

        // Timing estimates ride along without changing the digest or the sets.
        var roots = fx.Fixture.PathOf("timing");
        Directory.CreateDirectory(roots);
        var timed = (IDictionary)TestSelectionJson.Parse(fx.Command(a, fx.B, json: true, timingRoots: [roots]).Out);
        timed["digest"].ShouldBe(selection["digest"]);
        C1313.SetIds(timed).ShouldBe(C1313.SetIds(selection));
        C1313.Dicts(timed["estimates"]).Select(e => (string)e["setId"]!).ShouldBe(C1313.SetIds(selection));
        C1313.Strings(timed["budgetObservations"]).ShouldBe(["budget-observed"]);

        Should.Throw<NightlyUsageException>(() => TestSelectionOptions.Parse(["--baseline-sha", fx.B], fx.Root)).Message.ShouldBe("missing --candidate-sha");
        Should.Throw<NightlyUsageException>(() => TestSelectionOptions.Parse(["--baseline-sha", fx.B, "--candidate-sha", a, "--Json"], fx.Root));
        var parsed = TestSelectionOptions.Parse(["--baseline-sha", fx.B, "--candidate-sha", a, "--timing-roots", "x, y"], fx.Root);
        (parsed.Profile == "rc" && parsed.RepositoryRoot == fx.Root && !parsed.Json && parsed.TimingRoots.SequenceEqual(["x", "y"])).ShouldBeTrue();
    }
}

/// <summary>
/// New-C1313GitFixture: a repository whose baseline B carries both policies, global.json, the selector identity
/// (TestSelection.SelectorFiles, from this checkout) and a stub for every catalog source input. Candidates are commits on
/// B built in the index (a changed path, a rewritten catalog and policy, a selector edit). Git writes go through a plain
/// git child; the selection reads through TestSelectionGit.
/// </summary>
internal sealed class C1313SelectionRepository : IDisposable
{
    internal static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private C1313SelectionRepository(NightlyFixture fixture)
    {
        Fixture = fixture;
        Root = fixture.PathOf("repository");
    }

    public static string Repo => DelegateScriptRunner.RepoRoot;

    public NightlyFixture Fixture { get; }

    public string Root { get; }

    public string B { get; set; } = "";

    public IDictionary Catalog { get; private set; } = null!;

    public static C1313SelectionRepository Create()
    {
        var fx = new C1313SelectionRepository(NightlyFixture.Create("c1313-selection"));
        Directory.CreateDirectory(fx.Root);
        fx.Git("init", "--quiet");
        fx.Git("config", "user.name", "C1313 fixture");
        fx.Git("config", "user.email", "c1313@example.invalid");
        fx.Git("config", "core.autocrlf", "false");
        foreach (var file in new[] { TestSelection.PolicyPath, TestSelection.CatalogPath, "global.json" }) fx.Write(file, RepoText(file));
        foreach (var file in TestSelection.SelectorFiles) fx.Write(file, LfRepoText(file));
        fx.Catalog = (IDictionary)TestSelectionJson.Parse(RepoText(TestSelection.CatalogPath));
        foreach (var path in C1313.SourceInputs(fx.Catalog)) fx.Write(path, "fixture source");
        fx.Git("add", "--all");
        fx.Git("commit", "--quiet", "-m", "baseline");
        fx.B = fx.Git("rev-parse", "HEAD");
        return fx;
    }

    public static string RepoText(string path) => File.ReadAllText(Path.Combine(Repo, path.Replace('/', Path.DirectorySeparatorChar)));

    public static string LfRepoText(string path) => TestSelectionJson.LfText(RepoText(path));

    /// <summary>Get-C1313LfSelectorHash: the selector hash of this checkout's selector sources as LF text.</summary>
    public static string LfSelectorHash() => TestSelection.SelectorHash(LfRepoText, LfRepoText);

    public static JsonObject CatalogJson() => JsonNode.Parse(RepoText(TestSelection.CatalogPath))!.AsObject();

    public static JsonObject PolicyJson() => JsonNode.Parse(RepoText(TestSelection.PolicyPath))!.AsObject();

    public void Write(string path, string text)
    {
        var file = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    public string Git(params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        foreach (var name in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE" }) start.Environment.Remove(name);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("git " + arguments[0] + " timed out");
        }
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("git " + string.Join(" ", arguments) + " exit " + process.ExitCode + ": " + stderr.Result);
        return stdout.Result.Trim();
    }

    /// <summary>New-C1313Candidate: B with each path written (content) and staged.</summary>
    public string Candidate(string[] paths, string content = "changed")
    {
        Git("read-tree", B);
        foreach (var path in paths)
        {
            Write(path, content);
            Git("add", "--", path);
        }
        return Git("commit-tree", Git("write-tree"), "-p", B, "-m", "candidate");
    }

    /// <summary>
    /// New-C1313PolicyCandidate: B with the catalog rewritten (its hash recomputed) and the policy pinning it (its hash
    /// recomputed). The index is left at that tree, so a caller can stage more before its own commit.
    /// </summary>
    public string PolicyCandidate(JsonObject catalog)
    {
        var hash = TestSelectionCatalog.Hash(TestSelectionJson.Parse(catalog.ToJsonString()));
        catalog["catalogHash"] = hash;
        var policy = PolicyJson();
        policy["selectionCatalog"]!["hash"] = hash;
        policy["policyHash"] = NightlyPolicy.HashText(policy.ToJsonString());
        Git("read-tree", B);
        Write(TestSelection.CatalogPath, catalog.ToJsonString(Indented));
        Write(TestSelection.PolicyPath, policy.ToJsonString(Indented));
        Git("add", "--", TestSelection.CatalogPath, TestSelection.PolicyPath);
        return Git("commit-tree", Git("write-tree"), "-p", B, "-m", "policy");
    }

    /// <summary>Writes path ("changed") into the index as it stands (after PolicyCandidate), then commits that tree on B.</summary>
    public string CommitStaged(string path, string message)
    {
        Write(path, "changed");
        Git("add", "--", path);
        return Git("commit-tree", Git("write-tree"), "-p", B, "-m", message);
    }

    /// <summary>New-C1313SelectorCandidate: B with each selector source's LF text mutated, stored byte for byte (no filters).</summary>
    public string SelectorCandidate(Func<string, string> mutate)
    {
        Git("read-tree", B);
        foreach (var path in TestSelection.SelectorFiles)
        {
            var bytesPath = Fixture.PathOf("selector-" + Guid.NewGuid().ToString("N"));
            File.WriteAllBytes(bytesPath, new UTF8Encoding(false).GetBytes(mutate(LfRepoText(path))));
            var blob = Git("hash-object", "-w", "--no-filters", "--", bytesPath);
            Git("update-index", "--add", "--cacheinfo", "100644," + blob + "," + path);
            File.Delete(bytesPath);
        }
        return Git("commit-tree", Git("write-tree"), "-p", B, "-m", "selector bytes");
    }

    /// <summary>TestSelection.Select of candidate against baseline (B when null).</summary>
    public System.Collections.Specialized.OrderedDictionary Select(string candidate, string? baseline = null, string profile = "rc") =>
        TestSelection.Select(Root, baseline ?? B, candidate, profile);

    /// <summary>The select-test-rosters command in-process: its exit code, stdout and stderr (trimmed).</summary>
    public (int Exit, string Out, string Error) Command(string candidate, string baseline, bool json, string outputPath = "", string[]? timingRoots = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var options = new TestSelectionOptions
        {
            BaselineSha = baseline, CandidateSha = candidate, RepositoryRoot = Root, Json = json, OutputPath = outputPath, TimingRoots = timingRoots ?? [],
        };
        var exit = TestSelection.Run(options, NightlyHost.Create(null, new RecordingLauncher(), output, error, null, null, null, null));
        return (exit, output.ToString().Trim(), error.ToString().Trim());
    }

    /// <summary>Removes a loose object (Git loose objects are read-only; this one belongs only to the fixture).</summary>
    public void RemoveObject(string sha)
    {
        var path = Path.Combine(Root, ".git", "objects", sha[..2], sha[2..]);
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    public void Dispose() => Fixture.Dispose();
}

/// <summary>Readers of a selection document (TestSelection's plain values) for the C1313 cases.</summary>
internal static class C1313
{
    public static IEnumerable<IDictionary> Dicts(object? value) =>
        value is IEnumerable items and not string ? items.Cast<object>().Select(item => (IDictionary)item) : [];

    public static string[] Strings(object? value) =>
        value is IEnumerable items and not string ? items.Cast<object>().Select(PsValue.Str).ToArray() : [];

    public static string[] SetIds(IDictionary selection) => Dicts(selection["selectedSets"]).Select(set => PsValue.Str(set["id"])).ToArray();

    public static string[] Members(IDictionary selection, string field) =>
        Dicts(selection["selectedSets"]).SelectMany(set => Dicts(set["members"])).Select(member => PsValue.Str(member[field])).ToArray();

    public static string[] CatalogSetIds(IDictionary catalog) => Dicts(catalog["sets"]).Select(set => PsValue.Str(set["id"])).ToArray();

    /// <summary>$catalog.sets.sourceInputs + $catalog.sets.members.source, distinct and non-empty, in ordinal order.</summary>
    public static string[] SourceInputs(IDictionary catalog) =>
        TestSelectionJson.Sorted(Dicts(catalog["sets"]).SelectMany(set => Strings(set["sourceInputs"]).Concat(Dicts(set["members"]).Select(m => PsValue.Str(m["source"]))))
            .Where(path => path.Length > 0).ToArray());

    /// <summary>Same-C1313Set: the same values (with their repeats), in any order.</summary>
    public static bool Same(IEnumerable<string> actual, IEnumerable<string> expected) =>
        actual.OrderBy(v => v, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(v => v, StringComparer.Ordinal), StringComparer.Ordinal);
}
