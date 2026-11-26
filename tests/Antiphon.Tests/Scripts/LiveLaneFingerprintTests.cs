using System.Diagnostics;
using Antiphon.LiveLane;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2a-1: the live-lane input fingerprint is complete, canonical and shadow-only (V-1..V-7 of the S2a/S2b test
// design). W2 slice S22: the cases of the deleted scripts/test-live-lane-fingerprint.ps1 run in process against the C#
// fingerprint (LiveLaneFingerprint) over the CLI's git and rc project graph seams; each method builds its own fixture
// repository. One seam pair per method shares the parsed-document cache across its fingerprints (keyed by blob id), as the
// driver's -DocumentCache did.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneFingerprintTests
{
    private const string Lane = "fixture-lane";

    private const string PtyTestsProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0\" /><ProjectReference Include=\"../../src/Agents.Pty/Agents.Pty.csproj\" /><Compile Include=\"../Shared/LiveClaude/Shared.cs\" /><Content Include=\"../Shared/Data/*.json\" /></ItemGroup></Project>";
    private const string MainTestsProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0\" /><ProjectReference Include=\"../../server/Server.csproj\" /><Compile Include=\"../Shared/LiveClaude/Shared.cs\" /></ItemGroup></Project>";
    private const string ServerProject = "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"../build/Server.targets\" /><ItemGroup><ProjectReference Include=\"../src/SessionRunner/SessionRunner.csproj\" /></ItemGroup></Project>";

    [Test]
    public void StableCanonicalInputs() => Run(nameof(StableCanonicalInputs), w =>
    {
        var b = w.Base;
        w.Put("docs/unrelated.md", "changed\n");
        var c = w.Commit();
        // The reader's selection, before any fingerprint shares its cache.
        var selectionBefore = (string)Antiphon.Nightly.RcProjectSelection.Select(w.Root, b, c)["digest"]!;
        var f1 = w.Fp(b);
        var f2 = w.Fp(b);
        var j1 = LiveLaneData.Canonical(f1.ToJson());
        w.Cases.Check("deterministic", j1 == LiveLaneData.Canonical(f2.ToJson()) && f1.Digest is { Length: 64 });
        var fc = w.Fp(c);
        var jc = LiveLaneData.Canonical(fc.ToJson());
        var leaks = new[] { b, c, "runId", w.Results, w.Root, System.Text.Json.JsonSerializer.Serialize(w.Root).Trim('"') };
        w.Cases.Check("sha-free", Same(f1, fc) && !leaks.Any(leak => j1.Contains(leak) || jc.Contains(leak)));
        var clone = Path.Combine(w.Results, "second clone");
        LiveLaneRepo.RunGit(w.Results, w.Repo.Hooks, "clone", "-q", w.Root, clone);
        w.Cases.Check("path-free", Same(w.Fp(b, Lane, clone), f1));
        w.Put("src/SessionRunner/S.cs", "");
        var x = w.Commit();
        w.Drop("src/SessionRunner/S.cs");
        var y = w.Commit();
        var fx = w.Fp(x);
        var fy = w.Fp(y);
        w.Cases.Check("absent-vs-empty", fx.Digest != fc.Digest && fy.Digest != fc.Digest && fx.Digest != fy.Digest && fx.Digest != null && fy.Digest != null);
        w.WriteDeclaration(new LiveLaneMap((Lane, Declaration()), ("fixture-lane-2", Declaration())));
        var d = w.Commit();
        w.Cases.Check("domain", Differs(w.Fp(d, Lane), w.Fp(d, "fixture-lane-2")));
        var withMissing = Declaration();
        ((List<object>)withMissing["minimumRoots"]!).Add("src/Missing/");
        w.WriteDeclaration(new LiveLaneMap((Lane, withMissing)));
        var missing = w.Commit();
        w.Cases.Check("missing-root", Unknown(w.Fp(missing), "declared-root-empty:src/Missing/"));
        w.WriteDeclaration(new LiveLaneMap((Lane, Declaration())));
        var restored = w.Commit();
        var status = w.Status(restored);
        var doc = status.Document;
        w.Cases.Check("shadow-only", status.ExitCode == 0 && LiveLaneData.Same(doc["effectiveMode"], "Full") && LiveLaneData.Same(doc["activation"], "shadow-only")
            && doc["gateCredit"] is false && doc["executionEvidence"] is false && LiveLaneData.Same(doc["state"], "DUE")
            && (string)Antiphon.Nightly.RcProjectSelection.Select(w.Root, b, c)["digest"]! == selectionBefore, LiveLaneData.Canonical(doc));
        var before = w.Fp(restored);
        w.Repo.Git("update-index", "--chmod=+x", "src/Agents.Pty/A.cs");
        var executable = w.Repo.CommitIndex();
        w.Cases.Check("mode", Differs(w.Fp(executable), before, "repositoryClosure"));
        var repository = LiveLaneTestSources.RepoRoot;
        try
        {
            var head = LiveLaneRepo.RunGit(repository, w.Repo.Hooks, "rev-parse", "HEAD");
            var real1 = w.Fp(head, "live-claude-core-windows", repository);
            var real2 = LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), repository, head, "live-claude-core-windows");
            var declaration = (LiveLaneMap)((LiveLaneMap)((LiveLaneMap)LiveLaneData.Parse(LiveLaneRepo.RunGit(repository, w.Repo.Hooks, "show", head + ":tests/live-lane-inputs.json"))!)["lanes"]!)["live-claude-core-windows"]!;
            var paths = real1.Inventory.Select(entry => entry.Path).ToList();
            var empty = new List<string>();
            foreach (string root in ((List<object>)declaration["minimumRoots"]!).Concat((List<object>)declaration["runtimeRoots"]!))
                if (!paths.Any(path => path.StartsWith(root, StringComparison.Ordinal))) empty.Add(root);
            foreach (string pattern in (List<object>)declaration["contractFiles"]!)
            {
                var regex = new System.Text.RegularExpressions.Regex("^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", "[^/]*") + "$");
                if (!paths.Any(path => regex.IsMatch(path))) empty.Add(pattern);
            }
            w.Cases.Check("real-head", real1.Digest is { Length: 64 } && real1.Digest == real2.Digest && empty.Count == 0
                && !real1.UnknownReasons.Any(reason => reason.StartsWith("declared-root-empty:", StringComparison.Ordinal)), "empty=" + string.Join(",", empty));
            // Unit 60: the core lane is the one dedicated project, so the closure holds no scripts/ project (the nightly core and
            // the CLI stay out of the hash) and nothing under scripts/ is a runtime root. The sibling test trees (SessionRunner,
            // PtyHost and, since the PTY canaries moved, the PTY tests) are declared roots so their namespaces are hashed. Two
            // fixture strings outside the inventory read as namespace declarations and stay unknown on purpose: the reasons are
            // exactly those two, so a new unknown input fails here until the declaration covers it.
            var tooling = paths.Where(path => path.StartsWith("scripts/lib/Antiphon.Nightly/", StringComparison.Ordinal)
                || path.StartsWith("scripts/lib/Antiphon.Scripts/", StringComparison.Ordinal)).ToList();
            var fixtureStrings = new[]
            {
                "namespace-outside-inputs:tests/Antiphon.Tests/Scripts/DerivedImpactIndexTests.cs",
                "namespace-outside-inputs:tools/Antiphon.NativeLinuxMigrationAudit/NativeLinuxWindowsReasonAudit.cs",
            };
            w.Cases.Check("real-declaration", real1.Status == "unknown" && real1.UnknownReasons.SequenceEqual(fixtureStrings)
                && real1.SelectedProjects.SequenceEqual(["tests/Antiphon.LiveClaude.Tests/Antiphon.LiveClaude.Tests.csproj"])
                && !real1.ClosureProjects.Any(project => project.StartsWith("scripts/", StringComparison.Ordinal)) && tooling.Count == 0,
                "reasons=" + string.Join(",", real1.UnknownReasons) + " closure=" + string.Join(",", real1.ClosureProjects.Where(p => p.StartsWith("scripts/", StringComparison.Ordinal)))
                + " tooling=" + string.Join(",", tooling.Take(5)));
        }
        catch (Exception e) when (e is LiveLaneRefusal or InvalidOperationException)
        {
            w.Cases.Check("real-head", false, e.Message);
            w.Cases.Check("real-declaration", false, e.Message);
        }
    }, "deterministic", "sha-free", "path-free", "absent-vs-empty", "domain", "missing-root", "shadow-only", "mode", "real-head", "real-declaration");

    /// <summary>
    /// Unit 60 (the owner: "the source files for this digest should all be in an isolated project dedicated to running real
    /// Claude"): with the core rows in a host test project that references the nightly core and the CLI, an edit to either
    /// moves the fingerprint; with the core rows in a dedicated project that references only the product, the same edits
    /// leave it where it was, and an edit to the dedicated project's own tests still moves it.
    /// </summary>
    [Test]
    public void DedicatedProjectKeepsToolingOutOfTheHash() => Run(nameof(DedicatedProjectKeepsToolingOutOfTheHash), w =>
    {
        w.Put("scripts/lib/Nightly/Nightly.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        w.Put("scripts/lib/Nightly/N.cs", "namespace Antiphon.Nightly;\n");
        w.Put("scripts/lib/Cli/Cli.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Nightly/Nightly.csproj\" /></ItemGroup></Project>");
        w.Put("scripts/lib/Cli/C.cs", "namespace Antiphon.Scripts;\n");
        w.Put("tests/Main.Tests/Main.Tests.csproj", MainTestsProject.Replace("</ItemGroup>",
            "<ProjectReference Include=\"../../scripts/lib/Nightly/Nightly.csproj\" /><ProjectReference Include=\"../../scripts/lib/Cli/Cli.csproj\" /></ItemGroup>"));
        w.Put("tests/Live.Tests/Live.Tests.csproj", PtyTestsProject.Replace("<Compile Include=\"../Shared/LiveClaude/Shared.cs\" /><Content Include=\"../Shared/Data/*.json\" />", ""));
        w.Put("tests/Live.Tests/L.cs", "namespace Antiphon.LiveClaude.Tests;\n");
        w.WriteCatalog("tests/Pty.Tests/Pty.Tests.csproj", "tests/Main.Tests/Main.Tests.csproj", "tests/Live.Tests/Live.Tests.csproj", "tests/Other/Other.csproj");
        // No runtime root over the tooling in either layout (as the real declaration since unit 60), so only the project graph
        // decides what the tooling contributes.
        var hostedDeclaration = Declaration();
        hostedDeclaration["runtimeRoots"] = new List<object> { "scripts/hooks/" };
        w.WriteDeclaration(new LiveLaneMap((Lane, hostedDeclaration)));
        var hosted = w.Commit();
        // The dedicated layout as the real one: the sibling PTY test tree is a declared root and the namespace roots are the
        // product's and the live project's.
        var declaration = Declaration();
        declaration["runtimeRoots"] = new List<object> { "scripts/hooks/" };
        ((List<object>)declaration["minimumRoots"]!).Add("tests/Pty.Tests/");
        declaration["namespaceRoots"] = new List<object> { "Antiphon.Agents.Pty", "Antiphon.PtyHost", "Antiphon.SessionRunner", "Antiphon.Server", "Antiphon.LiveClaude.Tests" };
        w.WriteDeclaration(new LiveLaneMap((Lane, declaration)));
        w.WriteCoreRows(("Live.Tests", "PtyLive"), ("Live.Tests", "MainLive"));
        var dedicated = w.Commit();
        string Edited(string from)
        {
            w.Repo.Checkout(from);
            w.Put("scripts/lib/Nightly/N.cs", "namespace Antiphon.Nightly;\n// edited\n");
            w.Put("scripts/lib/Cli/C.cs", "namespace Antiphon.Scripts;\n// edited\n");
            return w.Commit();
        }
        var hostedEdit = Edited(hosted);
        var dedicatedEdit = Edited(dedicated);
        var hostedFp = w.Fp(hosted);
        var dedicatedFp = w.Fp(dedicated);
        w.Cases.Check("hosted-moves", hostedFp.Status == "complete" && Differs(w.Fp(hostedEdit), hostedFp, "repositoryClosure")
            && hostedFp.ClosureProjects.Contains("scripts/lib/Nightly/Nightly.csproj"), string.Join(",", hostedFp.UnknownReasons.Concat(hostedFp.ClosureProjects)));
        w.Cases.Check("dedicated-stable", dedicatedFp.Status == "complete" && Same(w.Fp(dedicatedEdit), dedicatedFp)
            && dedicatedFp.SelectedProjects.SequenceEqual(["tests/Live.Tests/Live.Tests.csproj"])
            && !dedicatedFp.ClosureProjects.Any(project => project.StartsWith("scripts/", StringComparison.Ordinal)),
            string.Join(",", dedicatedFp.UnknownReasons.Concat(dedicatedFp.ClosureProjects)));
        w.Repo.Checkout(dedicated);
        w.Put("tests/Live.Tests/L.cs", "namespace Antiphon.LiveClaude.Tests;\n// edited\n");
        w.Cases.Check("dedicated-own-source", Differs(w.Fp(w.Commit()), dedicatedFp, "testSources"));
    }, "hosted-moves", "dedicated-stable", "dedicated-own-source");

    [Test]
    public void ReferenceClosureInvalidates() => Run(nameof(ReferenceClosureInvalidates), w =>
    {
        var fb = w.Fp(w.Base);
        w.Put("src/Agents.Pty/A.cs", "namespace Antiphon.Agents.Pty;\n// edit\n");
        var fc = w.Fp(w.Commit());
        w.Cases.Check("direct-edge", Differs(fc, fb, "liveRoots", "repositoryClosure"));
        w.Put("src/Protocol/P.cs", "namespace Antiphon.PtyHost.Protocol;\n// edit\n");
        var d = w.Commit();
        var fd = w.Fp(d);
        var graph = Antiphon.Nightly.RcProjectSelection.Read(w.Root, d);
        var selected = new[] { "tests/Pty.Tests/Pty.Tests.csproj", "tests/Main.Tests/Main.Tests.csproj" };
        var expected = graph.Projects.Keys.Where(key => Antiphon.Nightly.RcProjectSelection.Downstream(graph, [key]).Any(selected.Contains)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        w.Cases.Check("transitive-edge", Differs(fd, fc) && expected.SequenceEqual(fd.ClosureProjects.OrderBy(k => k, StringComparer.Ordinal)) && expected.Count == 6,
            string.Join(",", fd.ClosureProjects));
        w.Put("tests/Pty.Tests/Pty.Tests.csproj", PtyTestsProject.Replace("<ProjectReference Include=\"../../src/Agents.Pty/Agents.Pty.csproj\" />", ""));
        var fe = w.Fp(w.Commit());
        var beforeEntry = fd.Inventory.FirstOrDefault(entry => entry.Path == "tests/Pty.Tests/Pty.Tests.csproj");
        var afterEntry = fe.Inventory.FirstOrDefault(entry => entry.Path == "tests/Pty.Tests/Pty.Tests.csproj");
        w.Cases.Check("edge-removed", Differs(fe, fd) && beforeEntry != null && afterEntry != null && beforeEntry.Blob != afterEntry.Blob);
        w.Put("tests/Pty.Tests/Pty.Tests.csproj", PtyTestsProject);
        var fr = w.Fp(w.Commit());
        w.Put("src/Unrelated/U.cs", "namespace Antiphon.Unrelated;\n// stable\n");
        var fu = w.Fp(w.Commit());
        w.Cases.Check("unrelated-stable", Same(fu, fr));
        w.Put("src/Unrelated/U.cs", "namespace Antiphon.Unrelated;\n// before edge\n");
        var fu0 = w.Fp(w.Commit());
        w.Put("tests/Pty.Tests/Pty.Tests.csproj", PtyTestsProject.Replace("</ItemGroup>", "<ProjectReference Include=\"../../src/Unrelated/Unrelated.csproj\" /></ItemGroup>"));
        var fedge = w.Fp(w.Commit());
        w.Put("src/Unrelated/U.cs", "namespace Antiphon.Unrelated;\n// after edge\n");
        var fu1 = w.Fp(w.Commit());
        w.Cases.Check("edge-added", Same(fu0, fu) && Differs(fedge, fu0) && Differs(fu1, fedge));
        w.Put("tests/Pty.Tests/Pty.Tests.csproj", PtyTestsProject);
        var digests = new List<string?> { w.Fp(w.Commit()).Digest };
        foreach (var file in new[] { "Directory.Build.props", "build/Server.targets", "global.json", "NuGet.Config", "Directory.Packages.props" })
        {
            w.Put(file, w.Repo.Read(file) + "\n");
            digests.Add(w.Fp(w.Commit()).Digest);
        }
        w.Cases.Check("shared-build-inputs", digests.Distinct(StringComparer.Ordinal).Count() == 6 && digests.All(digest => digest is { Length: 64 }));
        var fm = w.Fp(w.Repo.Git("rev-parse", "HEAD"));
        w.Repo.Git("mv", "tests/Pty.Tests/Other.cs", "tests/Pty.Tests/Renamed.cs");
        w.Cases.Check("rename", Differs(w.Fp(w.Repo.CommitIndex()), fm));
    }, "direct-edge", "transitive-edge", "edge-removed", "unrelated-stable", "edge-added", "shared-build-inputs", "rename");

    [Test]
    public void TestAndFixtureInvalidates() => Run(nameof(TestAndFixtureInvalidates), w =>
    {
        var fb = w.Fp(w.Base);
        w.Put("tests/Pty.Tests/Other.cs", "namespace Antiphon.Agents.Pty.Tests;\n// edited\n");
        var fc = w.Fp(w.Commit());
        w.Cases.Check("test-source", Differs(fc, fb, "testSources"));
        w.Put("tests/Shared/LiveClaude/Shared.cs", "namespace Antiphon.Tests.Shared;\n// edited\n");
        var fd = w.Fp(w.Commit());
        w.Cases.Check("linked-shared", Differs(fd, fc));
        w.Put("tests/Shared/Data/a.json", "{\"a\":2}");
        var fe = w.Fp(w.Commit());
        w.Put("tests/Shared/Data/b.json", "{\"b\":1}");
        var ff = w.Fp(w.Commit());
        w.Cases.Check("content-glob", Differs(fe, fd) && Differs(ff, fe));
        w.Drop("tests/Main.Tests/M.cs");
        var fg = w.Fp(w.Commit());
        w.Cases.Check("deletion", Differs(fg, ff));
        w.Put("tests/Other/O.cs", "namespace Antiphon.Unrelated.Tests;\n// edited\n");
        var fh = w.Fp(w.Commit());
        w.Cases.Check("other-test-project", Same(fh, fg));
        w.Repo.Git("mv", "tests/Pty.Tests/Pty.Tests.csproj", "tests/Pty.Tests/PtyTests.csproj");
        w.Cases.Check("core-project-map", Unknown(w.Fp(w.Repo.CommitIndex()), "core-project-missing:Pty.Tests"));
    }, "test-source", "linked-shared", "content-glob", "deletion", "other-test-project", "core-project-map");

    [Test]
    public void UnknownDependencyRefuses() => Run(nameof(UnknownDependencyRefuses), w =>
    {
        bool Refused(string sha, Func<string, bool> reason)
        {
            var state = LiveLaneStatus.Plain(w.Sources, w.Root, Lane, sha, null, out var fp);
            return fp.Status == "unknown" && LiveLaneData.Same(state["state"], "DUE") && ((List<object>)state["reasons"]!).Contains("unknown-input")
                && fp.UnknownReasons.Any(reason) && state["gateCredit"] is false;
        }
        bool Complete(string sha, Func<string, bool> observation)
        {
            var fp = w.Fp(sha);
            return fp.Status == "complete" && fp.UnknownReasons.Count == 0 && fp.Observations.Any(observation);
        }
        w.Put("server/Server.csproj", ServerProject.Replace("</Project>", "<Import Project=\"$(Root)/x.targets\" /></Project>"));
        w.Cases.Check("dynamic-import", Refused(w.Commit(), r => r.StartsWith("graph:server/Server.csproj:unresolved-input", StringComparison.Ordinal)));
        w.Reset();
        w.Put("tests/Main.Tests/Main.Tests.csproj", MainTestsProject.Replace("</ItemGroup>", "<Compile Include=\"../Shared/Gone.cs\" /></ItemGroup>"));
        w.Cases.Check("missing-linked", Refused(w.Commit(), r => r.Contains("missing-linked-input")));
        w.Reset();
        w.Put("src/Protocol/Protocol.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Agents.Pty/Agents.Pty.csproj\" /></ItemGroup></Project>");
        w.Cases.Check("cycle", Refused(w.Commit(), r => r.StartsWith("project-cycle:", StringComparison.Ordinal)));
        w.Reset();
        w.Put("tests/Main.Tests/Main.Tests.csproj", MainTestsProject.Replace("</ItemGroup>", "<Compile Include=\"../../../../x.cs\" /></ItemGroup>"));
        w.Cases.Check("outside-root", Refused(w.Commit(), r => r.Contains("outside-input")));
        w.Reset();
        w.Repo.StageLink("src/Agents.Pty/link.cs", "A.cs");
        w.Cases.Check("symlink", Refused(w.Repo.CommitIndex(), r => r == "symlink:src/Agents.Pty/link.cs"));
        w.Reset();
        w.Repo.StageGitlink("src/Agents.Pty/sub", w.Base);
        w.Cases.Check("submodule", Refused(w.Repo.CommitIndex(), r => r == "gitlink:src/Agents.Pty/sub"));
        w.Reset();
        w.Put("tests/Copy/Pty.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        w.Cases.Check("ambiguous-project", Refused(w.Commit(), r => r == "core-project-ambiguous:Pty.Tests"));
        w.Reset();
        w.Drop("tests/live-lanes.json");
        var status = w.Status(w.Commit());
        w.Cases.Check("missing-manifest", status.ExitCode == 2 && LiveLaneData.Same(status.Document["error"], "manifest-missing"), LiveLaneData.Canonical(status.Document));
        w.Reset();
        var extra = Declaration();
        extra["extra"] = "x";
        w.WriteDeclaration(new LiveLaneMap((Lane, extra)));
        var s1 = w.Commit();
        w.Reset();
        w.WriteDeclaration(new LiveLaneMap((Lane, Declaration())), "antiphon-live-lane-inputs-v2");
        var s2 = w.Commit();
        var status1 = w.Status(s1);
        var status2 = w.Status(s2);
        w.Cases.Check("declaration-schema", status1.ExitCode == 2 && LiveLaneData.Same(status1.Document["error"], "declaration-invalid")
            && status2.ExitCode == 2 && LiveLaneData.Same(status2.Document["error"], "declaration-invalid"));
        w.Reset();
        w.WriteCatalog("tests/Pty.Tests/Pty.Tests.csproj", "tests/Main.Tests/Main.Tests.csproj");
        w.Cases.Check("catalog-only", Complete(w.Commit(), o => o == "unbatched-test-project:tests/Other/Other.csproj"));
        w.Reset();
        w.Put("src/Unrelated/Unrelated.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"$(Root)/x.targets\" /></Project>");
        w.Cases.Check("fault-outside-closure", Complete(w.Commit(), o => o.StartsWith("graph:src/Unrelated/Unrelated.csproj:", StringComparison.Ordinal)));
    }, "dynamic-import", "missing-linked", "cycle", "outside-root", "symlink", "submodule", "ambiguous-project", "missing-manifest", "declaration-schema", "catalog-only",
        "fault-outside-closure");

    [Test]
    public void RuntimeScriptsIncluded() => Run(nameof(RuntimeScriptsIncluded), w =>
    {
        var fb = w.Fp(w.Base);
        w.Put("scripts/hooks/hook.ps1", "# hook edited\n");
        var fc = w.Fp(w.Commit());
        w.Cases.Check("hook", Differs(fc, fb, "runtimeInputs"));
        w.Put("scripts/lib/live-lane-contract.ps1", "# contract edited\n");
        var fd = w.Fp(w.Commit());
        w.Cases.Check("contract-scripts", Differs(fd, fc, "laneContract"));
        w.WriteManifest("\n");
        var fe = w.Fp(w.Commit());
        w.WriteDeclaration(new LiveLaneMap((Lane, Declaration())), suffix: "\n");
        var ff = w.Fp(w.Commit());
        w.Cases.Check("manifest-and-declaration", Differs(fe, fd, "laneContract") && Differs(ff, fe, "laneContract"));
        w.Put("tests/Shared/LiveClaude/Fixtures/witness.mjs", "export const witness = 2;\n");
        var fg = w.Fp(w.Commit());
        w.Cases.Check("fixture-assets", Differs(fg, ff));
        var current = w.Repo.Git("rev-parse", "HEAD");
        w.Put("tests/Pty.Tests/Uses.cs", "class Uses { const string Tool = \"scripts/other/tool.ps1\"; void M() => ScriptHarness.RunHarnessCaseAsync(\"tool2.ps1\", \"C1\", \"C1_X\", 1); }\n");
        var fh = w.Fp(w.Commit());
        w.Repo.Checkout(current);
        w.Put("tests/Pty.Tests/Uses.cs", "class Uses { const string Tool = \"scripts/lib/tool.ps1\"; void M() => ScriptHarness.RunHarnessCaseAsync(\"lib/tool2.ps1\", \"C1\", \"C1_X\", 1); }\n");
        var fcontrol = w.Fp(w.Commit());
        w.Cases.Check("undeclared-runtime-literal", Unknown(fh, "undeclared-runtime:scripts/other/tool.ps1") && fh.UnknownReasons.Contains("undeclared-runtime:scripts/tool2.ps1")
            && fcontrol.Status == "complete" && fcontrol.UnknownReasons.Count == 0, string.Join(",", fh.UnknownReasons.Concat(fcontrol.UnknownReasons)));
        w.Put("docs/unrelated.md", "unrelated edited\n");
        w.Cases.Check("unrelated-file", Same(w.Fp(w.Commit()), fcontrol));
    }, "hook", "contract-scripts", "manifest-and-declaration", "fixture-assets", "undeclared-runtime-literal", "unrelated-file");

    [Test]
    public void RelevantNamespaceOutsideInputsRefuses() => Run(nameof(RelevantNamespaceOutsideInputsRefuses), w =>
    {
        var fb = w.Fp(w.Base);
        w.Put("tools/Stray/Stray.cs", "namespace Antiphon.SessionRunner.Stray;\n");
        w.Cases.Check("file-scoped", Unknown(w.Fp(w.Commit()), "namespace-outside-inputs:tools/Stray/Stray.cs"));
        w.Reset();
        w.Put("tools/Stray/Stray.cs", "namespace Antiphon.Agents.Pty.Extra\n{\n}\n");
        w.Cases.Check("block-scoped", Unknown(w.Fp(w.Commit()), "namespace-outside-inputs:tools/Stray/Stray.cs"));
        w.Reset();
        w.Put("tools/T.cs", "namespace Antiphon.Tests.Helpers;\n");
        w.Cases.Check("test-namespace", Unknown(w.Fp(w.Commit()), "namespace-outside-inputs:tools/T.cs"));
        w.Reset();
        w.Put("tools/Stray/Stray.cs", "namespace Antiphon.Tools;\n");
        w.Put("tools/Stray/Other.cs", "namespace Antiphon.ServerTools\n{\n}\n");
        var fs = w.Fp(w.Commit());
        w.Cases.Check("foreign-namespace", fs.Status == "complete" && fs.UnknownReasons.Count == 0 && fs.Digest is { Length: 64 }, string.Join(",", fs.UnknownReasons));
        w.Reset();
        w.Put("src/Agents.Pty/New.cs", "namespace Antiphon.Agents.Pty;\n");
        fs = w.Fp(w.Commit());
        w.Cases.Check("inside-inputs", fs.Status == "complete" && fs.UnknownReasons.Count == 0 && Differs(fs, fb));
    }, "file-scoped", "block-scoped", "test-namespace", "foreign-namespace", "inside-inputs");

    [Test]
    public void LoadedInstructionsOutsideInputsRefuse() => Run(nameof(LoadedInstructionsOutsideInputsRefuse), w =>
    {
        var fb = w.Fp(w.Base);
        w.Put("CLAUDE.md", "@AGENTS.md\nedited\n");
        var fc = w.Fp(w.Commit());
        w.Put("AGENTS.md", "# Agents edited\n@docs/imported.md\n");
        var fd = w.Fp(w.Commit());
        w.Cases.Check("root-instructions", Differs(fc, fb, "instructions") && Differs(fd, fc, "instructions"));
        w.Put("docs/imported.md", "imported edited\n");
        var fe = w.Fp(w.Commit());
        w.Cases.Check("import-chain", Differs(fe, fd, "instructions"));
        w.Put("sub/dir/CLAUDE.md", "nested\n");
        var ff = w.Fp(w.Commit());
        w.Put(".claude/skills/x/SKILL.md", "skill edited\n");
        var fg = w.Fp(w.Commit());
        w.Put(".mcp.json", "{\"servers\":{}}");
        var fh = w.Fp(w.Commit());
        w.Cases.Check("nested-and-claude-tree", Differs(ff, fe) && Differs(fg, ff) && Differs(fh, fg));
        w.Reset();
        w.Put("AGENTS.md", "# Agents\n@docs/imported.md\n@../outside.md\n");
        w.Cases.Check("import-escape", Unknown(w.Fp(w.Commit()), "instruction-import-outside:AGENTS.md"));
        w.Reset();
        w.Put("AGENTS.md", "# Agents\n@docs/imported.md\n@~/.claude/notes.md\n");
        w.Cases.Check("import-home", Unknown(w.Fp(w.Commit()), "instruction-import-user:AGENTS.md"));
        w.Reset();
        w.Put("AGENTS.md", "# Agents\n@docs/imported.md\n@docs/gone.md\n");
        w.Cases.Check("import-missing", Unknown(w.Fp(w.Commit()), "instruction-import-missing:docs/gone.md"));
        w.Reset();
        w.Put("AGENTS.md", "# Agents\n@docs/imported.md\n@docs/a.md\n");
        w.Put("docs/a.md", "@b.md\n");
        w.Put("docs/b.md", "@a.md\n");
        var s = w.Commit();
        var watch = Stopwatch.StartNew();
        var fs = w.Fp(s);
        watch.Stop();
        var paths = fs.Inventory.Select(entry => entry.Path).ToList();
        w.Cases.Check("import-cycle", fs.Status == "complete" && paths.Contains("docs/a.md") && paths.Contains("docs/b.md") && watch.Elapsed.TotalSeconds < 30,
            string.Join(",", fs.UnknownReasons));
        w.Reset();
        var entry = Declaration();
        entry["instructionPatterns"] = new List<object> { "CLAUDE.md", "CLAUDE.local.md", "AGENTS.md", ".claude/**" };
        w.WriteDeclaration(new LiveLaneMap((Lane, entry)));
        w.Cases.Check("undeclared-instruction", Unknown(w.Fp(w.Commit()), "instruction-outside-inputs:.mcp.json"));
    }, "root-instructions", "import-chain", "nested-and-claude-tree", "import-escape", "import-home", "import-missing", "import-cycle", "undeclared-instruction");

    /// <summary>The fixture lane's declaration (the driver's New-LaneDeclaration).</summary>
    private static LiveLaneMap Declaration() => new(
        ("manifest", "tests/live-lanes.json"),
        ("contractFiles", new List<object> { "tests/live-lanes.json", "tests/live-lane-inputs.json", "scripts/lib/live-lane-*.ps1" }),
        ("minimumRoots", new List<object> { "src/Agents.Pty/", "src/SessionRunner/", "server/", "tests/Shared/LiveClaude/Fixtures/" }),
        ("runtimeRoots", new List<object> { "scripts/hooks/", "scripts/lib/" }),
        ("namespaceRoots", new List<object> { "Antiphon.Agents.Pty", "Antiphon.PtyHost", "Antiphon.SessionRunner", "Antiphon.Server", "Antiphon.Tests", "Antiphon.Agents.Pty.Tests" }),
        ("instructionPatterns", new List<object> { "CLAUDE.md", "CLAUDE.local.md", "AGENTS.md", ".claude/**", ".mcp.json" }));

    /// <summary>One method's fixture repository (the driver's New-FingerprintFixture) and case ids.</summary>
    private sealed class World
    {
        public World(string method, string results)
        {
            Cases = new C1545Cases(method);
            Results = results;
            Repo = new LiveLaneRepo(Path.Combine(results, "repository with spaces"));
            WriteCatalog("tests/Pty.Tests/Pty.Tests.csproj", "tests/Main.Tests/Main.Tests.csproj", "tests/Other/Other.csproj");
            Put("Directory.Build.props", "<Project />");
            Put("global.json", "{\"sdk\":{\"version\":\"10.0.100\"}}");
            Put("NuGet.Config", "<configuration />");
            Put("Directory.Packages.props", "<Project />");
            Put("build/Server.targets", "<Project />");
            Put("src/Protocol/Protocol.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/Protocol/P.cs", "namespace Antiphon.PtyHost.Protocol;\n");
            Put("src/Agents.Pty/Agents.Pty.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Protocol/Protocol.csproj\" /></ItemGroup></Project>");
            Put("src/Agents.Pty/A.cs", "namespace Antiphon.Agents.Pty;\n");
            Put("src/SessionRunner/SessionRunner.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/SessionRunner/S.cs", "namespace Antiphon.SessionRunner;\n");
            Put("server/Server.csproj", ServerProject);
            Put("server/Server.cs", "namespace Antiphon.Server;\n");
            Put("tests/Pty.Tests/Pty.Tests.csproj", PtyTestsProject);
            Put("tests/Pty.Tests/T.cs", "namespace Antiphon.Agents.Pty.Tests;\nclass T { const string Contract = \"scripts/lib/live-lane-contract.ps1\"; }\n");
            Put("tests/Pty.Tests/Other.cs", "namespace Antiphon.Agents.Pty.Tests;\n// other\n");
            Put("tests/Main.Tests/Main.Tests.csproj", MainTestsProject);
            Put("tests/Main.Tests/M.cs", "namespace Antiphon.Tests;\n");
            Put("tests/Shared/LiveClaude/Shared.cs", "namespace Antiphon.Tests.Shared;\n");
            Put("tests/Shared/LiveClaude/Fixtures/witness.mjs", "export const witness = 1;\n");
            Put("tests/Shared/Data/a.json", "{\"a\":1}");
            Put("src/Unrelated/Unrelated.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/Unrelated/U.cs", "namespace Antiphon.Unrelated;\n");
            Put("tests/Other/Other.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0\" /><ProjectReference Include=\"../../src/Unrelated/Unrelated.csproj\" /></ItemGroup></Project>");
            Put("tests/Other/O.cs", "namespace Antiphon.Unrelated.Tests;\n");
            Put("scripts/hooks/hook.ps1", "# hook\n");
            Put("scripts/lib/live-lane-contract.ps1", "# contract\n");
            Put("docs/unrelated.md", "unrelated\n");
            Put("CLAUDE.md", "@AGENTS.md\n");
            Put("AGENTS.md", "# Agents\n@docs/imported.md\n");
            Put("docs/imported.md", "imported\n");
            Put(".claude/settings.json", "{}");
            Put(".claude/skills/x/SKILL.md", "skill\n");
            Put(".mcp.json", "{}");
            WriteManifest();
            WriteDeclaration(new LiveLaneMap((Lane, Declaration())));
            Base = Commit();
        }

        public C1545Cases Cases { get; }
        public string Results { get; }
        public LiveLaneRepo Repo { get; }
        public string Root => Repo.Root;
        public string Base { get; }
        public LiveLaneSources Sources { get; } = LiveLaneTestSources.Create();

        public void Put(string path, string text) => Repo.Put(path, text);
        public void Drop(string path) => Repo.Drop(path);
        public string Commit() => Repo.Commit();
        public void Reset() => Repo.Checkout(Base);

        public LiveLaneFingerprintResult Fp(string sha, string lane = Lane, string? root = null) => LiveLaneFingerprint.Compute(Sources, root ?? Root, sha, lane);

        public LiveLaneStatusResult Status(string sha, string lane = Lane) => LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(Root, lane, sha));

        public void WriteDeclaration(LiveLaneMap lanes, string format = "antiphon-live-lane-inputs-v1", string suffix = "") =>
            Put("tests/live-lane-inputs.json", LiveLaneData.Canonical(new LiveLaneMap(("format", format), ("lanes", lanes))) + suffix);

        public void WriteManifest(string suffix = "") => WriteCoreRows(suffix, ("Pty.Tests", "PtyLive"), ("Main.Tests", "MainLive"));

        public void WriteCoreRows(params (string Project, string Class)[] rows) => WriteCoreRows("", rows);

        private void WriteCoreRows(string suffix, params (string Project, string Class)[] rows)
        {
            LiveLaneMap Row(string project, string @class) => new(("project", project), ("namespace", "Antiphon." + project), ("class", @class), ("method", "Live"),
                ("arguments", new List<object>()), ("uid", "Antiphon." + project + "." + @class + ".1.1.Live.1.1.0"),
                ("categories", new List<object> { "Integration", "LiveClaude" }), ("bound", true));
            Put("tests/live-lanes.json", LiveLaneData.Canonical(new LiveLaneMap(("format", "antiphon-live-lanes-v1"),
                ("core", rows.Select(row => (object)Row(row.Project, row.Class)).ToList()), ("gates", new List<object>()), ("additiveContract", new List<object>()))) + suffix);
        }

        public void WriteCatalog(params string[] batches)
        {
            var projects = new LiveLaneMap();
            foreach (var path in new[] { "src/Protocol/Protocol.csproj", "src/Agents.Pty/Agents.Pty.csproj", "src/SessionRunner/SessionRunner.csproj", "server/Server.csproj",
                         "tests/Pty.Tests/Pty.Tests.csproj", "tests/Main.Tests/Main.Tests.csproj", "src/Unrelated/Unrelated.csproj", "tests/Other/Other.csproj" })
                projects[path] = new List<object> { "fixture" };
            // Projects a case adds (unit 60: a dedicated live project and the tooling a host test project references).
            foreach (var path in Directory.EnumerateFiles(Repo.Root, "*.csproj", SearchOption.AllDirectories)
                         .Select(full => Path.GetRelativePath(Repo.Root, full).Replace('\\', '/')).Order(StringComparer.Ordinal).Where(path => !projects.Contains(path)).ToList())
                projects[path] = new List<object> { "fixture" };
            Put("tests/rc-project-categories.json", LiveLaneData.Canonical(new LiveLaneMap(("schemaVersion", 1), ("projects", projects), ("batches", batches.Cast<object>().ToList()))));
        }
    }

    private static void Run(string method, Action<World> body, params string[] expected)
    {
        using var scratch = new LaneScratch("c1545-fingerprint-");
        var world = new World(method, scratch.Root);
        body(world);
        world.Cases.Complete(expected);
    }

    private static bool Differs(LiveLaneFingerprintResult a, LiveLaneFingerprintResult b, params string[] components) =>
        a.Digest is { Length: 64 } && a.Digest != b.Digest
        && components.All(name => a.Components?[name] is string mine && b.Components != null && mine != b.Components[name] as string);

    private static bool Same(LiveLaneFingerprintResult a, LiveLaneFingerprintResult b) => a.Digest is { Length: 64 } && a.Digest == b.Digest;

    private static bool Unknown(LiveLaneFingerprintResult fp, string reason) => fp.Status == "unknown" && fp.UnknownReasons.Contains(reason);
}
