using System.Collections;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using static Antiphon.Tests.Scripts.ReleaseSelectionFixtures;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313 S6-A: the release selection attempt writer (ReleaseSelectionAttempt.Create) in process. The scenarios of the
/// deleted scripts/test-release-selection-cli.ps1 (2026-11-22 plan, unit 8), which compared the select-release command with
/// the pwsh worker; both are gone, so each scenario now states what the one writer answers: the attempt it writes
/// (requested and effective mode, baseline, digests, reasons, and the hop reader accepting it) or its refusal, which
/// writes nothing.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseSelectionAttemptTests
{
    /// <summary>Without a baseline receipt: Full, ChangeAware, the opaque inputs, resume, a second attempt, a requested B, an empty archive, and the refusals.</summary>
    [Test]
    public void Attempts_without_a_receipt()
    {
        using var repo = new SelectionRepository("rs-attempts");
        var b = repo.Commit("", "baseline", repo.PutContractFiles);
        var a = repo.Commit(b, "candidate", () => repo.Put("src/changed.txt", "candidate"));
        var stale = repo.Commit(a, "stale policy", () =>
            repo.Put(TestSelection.PolicyPath, System.Text.RegularExpressions.Regex.Replace(repo.Read(TestSelection.PolicyPath), "\"policyHash\"\\s*:\\s*\"[0-9a-f]{64}\"", "\"policyHash\": \"" + new string('0', 64) + "\"")));
        var rows = new NightlyCaseRows();
        var n = 0;
        string Release() => repo.PathOf("release " + ++n);

        var release = Release();
        var full = Request(repo.Root, release, a);
        var bound = Bind(full);
        var attempt = (IDictionary)bound.Selection!;
        var selection = ReadJson(bound.SelectionPath);
        rows.Check(Field(attempt, "requestedMode") == "Full" && Field(attempt, "effectiveMode") == "Full" && Field(attempt, "coverageScope") == "Full"
            && Field(attempt, "format") == ReleaseSelectionAttempt.AttemptFormat && Field(selection, "format") == ReleaseSelectionAttempt.SelectionFormat
            && bound.BaselineSha == "" && Fields(attempt, "fallbackReasons").SequenceEqual(new[] { "baseline-absent" }) && selection["proposal"] == null
            && bound.SelectionAttemptPath == Path.Combine(release, "candidates", "rc-2026-11-08-fixture", "attempts", "run-1", "selection-attempt.json")
            && NightlyPaths.FileSha256(bound.SelectionAttemptPath) == bound.SelectionAttemptDigest
            && TestSelectionJson.Sha256Text(File.ReadAllText(bound.SelectionPath)) == bound.SelectionDigest && Field(attempt, "selectionDigest") == bound.SelectionDigest
            && Field(attempt, "attempt") == "1" && Field(attempt, "timingRoot") == full.TimingRoot
            && PsValue.Str(PsValue.Prop(ReleaseSelectionAttempt.Read(full), "candidateSha")) == a, "RS full");

        var changeAware = Bind(Request(repo.Root, Release(), a, r => r.SelectionMode = "ChangeAware"));
        rows.Check(Field(changeAware.Selection, "requestedMode") == "ChangeAware" && Field(changeAware.Selection, "effectiveMode") == "Full"
            && Fields(changeAware.Selection, "fallbackReasons").SequenceEqual(new[] { "baseline-absent", "change-aware-unqualified" }), "RS changeAware",
            string.Join(",", Fields(changeAware.Selection, "fallbackReasons")));

        release = Release();
        Directory.CreateDirectory(Path.Combine(release, "claims"));
        Directory.CreateDirectory(Path.Combine(release, "train"));
        var claim = Path.Combine(release, "claims", "qualification.json");
        var train = Path.Combine(release, "train", "manifest.json");
        File.WriteAllText(claim, "{\"claim\":\"unqualified\",\"rows\":[1,2]}");
        File.WriteAllText(train, "opaque train bytes");
        var inputs = Bind(Request(repo.Root, release, a, r => { r.QualificationPath = claim; r.TrainManifestPath = train; r.ReleaseIntentPath = Path.Combine(release, "intent.json"); }));
        rows.Check(Field(inputs.Selection, "qualificationDigest") == NightlyPaths.FileSha256(claim) && Field(inputs.Selection, "trainDigest") == NightlyPaths.FileSha256(train)
            && Field(inputs.Selection, "releaseIntentPath") == Path.Combine(release, "intent.json") && Field(inputs.Selection, "qualificationPath") == claim
            && File.ReadAllText(claim) == "{\"claim\":\"unqualified\",\"rows\":[1,2]}", "RS inputs");

        release = Release();
        var absent = Bind(Request(repo.Root, release, a, r => { r.QualificationPath = Path.Combine(release, "none.json"); r.TrainManifestPath = Path.Combine(release, "none-train.json"); }));
        rows.Check(Field(absent.Selection, "qualificationDigest") == "" && Field(absent.Selection, "trainDigest") == "", "RS absentInputs");

        release = Release();
        var first = Bind(Request(repo.Root, release, a));
        var tree = Tree(release);
        var resumed = ReleaseSelectionAttempt.Create(Request(repo.Root, release, a));
        rows.Check(resumed.SelectionAttemptDigest == first.SelectionAttemptDigest && resumed.SelectionDigest == first.SelectionDigest && Tree(release) == tree, "RS resume");

        var second = ReleaseSelectionAttempt.Create(Request(repo.Root, release, a, r => { r.RunId = "run-2"; r.Attempt = "2"; }));
        rows.Check(second.SelectionAttemptPath != first.SelectionAttemptPath && Field(second.Selection, "attempt") == "2"
            && NightlyPaths.FileSha256(first.SelectionAttemptPath) == first.SelectionAttemptDigest && Attempts(release) == 2, "RS secondAttempt");

        var baseline = Bind(Request(repo.Root, Release(), a, r => r.BaselineSha = b));
        var proposal = ReadJson(baseline.SelectionPath)["proposal"];
        rows.Check(baseline.BaselineSha == b && Field(proposal, "format") == "antiphon-test-selection-v1" && Field(proposal, "baselineSha") == b
            && ReadJson(baseline.SelectionPath)["projectProposal"] == null && Fields(baseline.Selection, "fallbackReasons").Contains("baseline-absent")
            && Field(baseline.Selection, "baselineDigest") == "", "RS baseline");

        release = Release();
        Directory.CreateDirectory(Path.Combine(release, "baselines"));
        var archive = Bind(Request(repo.Root, release, a));
        rows.Check(archive.BaselineSha == "" && Fields(archive.Selection, "fallbackReasons").Contains("baseline-absent"), "RS baselineArchive");

        // The refusals: each names its reason and writes no attempt.
        void Refused(string name, string reason, Func<string, ReleaseSelectionRequest> prepare)
        {
            var root = Release();
            Directory.CreateDirectory(root);
            var request = prepare(root);
            var before = Tree(root);
            var refusal = Refusal(request);
            rows.Check(refusal == ReleaseSelectionAttempt.RefusedPrefix + reason && Tree(root) == before && Attempts(root) == 0, "RS " + name, refusal);
        }
        Refused("conflict", "selection-attempt-conflict", root =>
        {
            var dir = Path.Combine(root, "candidates", "rc-2026-11-08-fixture", "attempts", "run-1");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "selection.json"), "{\"tampered\":true}");
            return Request(repo.Root, root, a);
        });
        Refused("notCommit", "selection-commit-unavailable", root => Request(repo.Root, root, new string('0', 40)));
        Refused("notFullSha", "selection-commit-required", root => Request(repo.Root, root, a.Substring(0, 12)));
        Refused("stalePolicy", "policy-hash-stale", root => Request(repo.Root, root, stale));
        Refused("qualificationArray", "qualification-invalid", root =>
        {
            File.WriteAllText(Path.Combine(root, "q.json"), "[1]");
            return Request(repo.Root, root, a, r => r.QualificationPath = Path.Combine(root, "q.json"));
        });
        Refused("trainDirectory", "selection-train-input", root =>
        {
            Directory.CreateDirectory(Path.Combine(root, "train-dir"));
            return Request(repo.Root, root, a, r => r.TrainManifestPath = Path.Combine(root, "train-dir"));
        });
        Refused("outsideState", "selection-path-outside-state", root =>
        {
            var outside = repo.PathOf("outside-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(outside, "{}");
            return Request(repo.Root, root, a, r => r.QualificationPath = outside);
        });
        Refused("inCheckout", "selection-path-in-checkout", root => Request(repo.Root, repo.Root, a));
        Refused("badRunId", "selection-run-identity", root => Request(repo.Root, root, a, r => r.RunId = "-bad"));
        Refused("badAttempt", "selection-run-identity", root => Request(repo.Root, root, a, r => r.Attempt = "0"));
        Refused("badCandidateId", "selection-candidate-identity", root => Request(repo.Root, root, a, r => r.CandidateId = "../escape"));
        rows.ShouldAllPass(19);
    }

    /// <summary>
    /// Against a baseline: project add, remove and rename, glob and import edges, Full, policy and catalog changes, the same
    /// commit, a receipt (compatible, incompatible, missing), archive choices, and the refusals (a corrupt receipt, a
    /// baseline that is not an ancestor, a stale baseline policy).
    /// </summary>
    [Test]
    public void Attempts_against_a_baseline()
    {
        using var repo = new SelectionRepository("rs-baselines");
        var p0 = repo.Commit("", "projects", () =>
        {
            repo.PutContractFiles();
            repo.Put("tests/rc-project-categories.json", "{\"schemaVersion\":1,\"projects\":{\"src/A/A.csproj\":[\"app\"],\"src/B/B.csproj\":[\"lib\",\"core\"]},"
                + "\"batches\":[\"src/A/A.csproj\"],\"nonQualificationTests\":{}}");
            repo.Put("build/common.props", "<Project><ItemGroup><AdditionalFiles Include=\"$(MSBuildThisFileDirectory)stylecop.json\" /></ItemGroup></Project>");
            repo.Put("build/stylecop.json", "{}");
            repo.Put("Directory.Build.props", "<Project><Import Project=\"build/common.props\" /></Project>");
            repo.Put("src/A/A.csproj", string.Join("\n",
                "<Project Sdk=\"Microsoft.NET.Sdk\">",
                "  <ItemGroup>",
                "    <ProjectReference Include=\"..\\B\\B.csproj\" />",
                "    <PackageReference Include=\"tunit\" Version=\"1.0.0\" />",
                "    <Compile Include=\"../shared/**/*.cs\" />",
                "    <Compile Include=\"gen/?.cs\" />",
                "    <None Include=\"$(MSBuildThisFileDirectory)assets/*.txt\" />",
                "    <Content Include=\"$(PkgFixture_Tools)\\tools\\x.dll\" />",
                "    <Content Include=\"[lit]/*.md\" />",
                "  </ItemGroup>",
                "  <Target Name=\"Late\"><ItemGroup><Compile Include=\"late/*.cs\" /></ItemGroup></Target>",
                "</Project>"));
            repo.Put("src/A/Program.cs", "class P {}");
            repo.Put("src/A/gen/1.cs", "class G1 {}");
            repo.Put("src/A/gen/12.cs", "class G12 {}");
            repo.Put("src/A/assets/a.txt", "asset");
            repo.Put("src/A/[lit]/n.md", "literal bracket");
            repo.Put("src/shared/deep/S.cs", "class S {}");
            repo.Put("src/B/B.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><Compile Include=\"*.cs\" /></ItemGroup></Project>");
            repo.Put("src/B/B.cs", "class B {}");
        });
        var p1 = repo.Commit(p0, "b", () => repo.Put("src/B/B.cs", "class B { int x; }"));
        var add = repo.Commit(p1, "add", () =>
        {
            repo.Put("src/C/C.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../A/A.csproj\" /><PackageReference Include=\"TUnit\" /></ItemGroup></Project>");
            repo.Put("src/A/Program.cs", "class P { }");
        });
        var remove = repo.Commit(p1, "remove", () => Directory.Delete(Path.Combine(repo.Root, "src", "B"), true));
        var rename = repo.Commit(p1, "rename", () => Directory.Move(Path.Combine(repo.Root, "src", "A"), Path.Combine(repo.Root, "src", "A2")));
        var glob = repo.Commit(p1, "glob", () =>
        {
            repo.Put("src/shared/deep/er/T.cs", "class T {}");
            repo.Put("src/A/gen/2.cs", "class G2 {}");
            repo.Put("src/A/late/L.cs", "class L {}");
            repo.Put("src/A/assets/b.txt", "asset b");
        });
        var policy = repo.Commit(p1, "policy", () => repo.EditPolicy(p => ((IDictionary)((IDictionary)p["profiles"]!)["rc"]!)["description"] += " changed contract", null));
        var catalog = repo.Commit(p1, "catalog", () => repo.EditPolicy(null, c =>
        {
            var set = (IDictionary)((object[])c["sets"]!)[0];
            set["reason"] = PsValue.Str(set["reason"]) + " (edited)";
        }));
        var staleBase = repo.Commit(p0, "stale base", () =>
            repo.Put(TestSelection.PolicyPath, System.Text.RegularExpressions.Regex.Replace(repo.Read(TestSelection.PolicyPath), "\"policyHash\"\\s*:\\s*\"[0-9a-f]{64}\"", "\"policyHash\": \"" + new string('0', 64) + "\"")));
        var staleChild = repo.Commit(staleBase, "restored", () =>
            repo.Put(TestSelection.PolicyPath, File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "tests", "test-execution-policy.json"))));
        var unrelated = repo.Commit("", "unrelated", () => repo.Put("other.txt", "no common history"));
        repo.Git("read-tree", "--reset", "-u", p1);

        var rows = new NightlyCaseRows();
        var n = 0;
        string Release() => repo.PathOf("release " + ++n);
        ReleaseSelectionRequest Against(string release, string candidate, string baseline, string mode, Action<ReleaseSelectionRequest>? more = null) =>
            Request(repo.Root, release, candidate, r => { r.BaselineSha = baseline; r.SelectionMode = mode; more?.Invoke(r); });
        IDictionary Projects(NightlySelectionBinding bound) => (IDictionary)ReadJson(bound.SelectionPath)["projectProposal"]!;
        string Reasons(object? document) => string.Join(",", Fields(document, "fallbackReasons"));

        var added = Bind(Against(Release(), add, p1, "Shadow"));
        var addProjects = Projects(added);
        rows.Check(added.BaselineSha == p1 && Field(added.Selection, "effectiveMode") == "Full" && Field(addProjects, "format") == RcProjectSelection.Format
            && Field(addProjects, "mode") == "Full" && Field(addProjects, "activation") == "shadow-only"
            && Fields(addProjects, "fallbackReasons").Contains("unknown-project:src/C/C.csproj") && Fields(addProjects, "fallbackReasons").Contains("unbatched-test-project:src/C/C.csproj"),
            "RS projectAdd", Reasons(addProjects));
        var removed = Bind(Against(Release(), remove, p1, "ChangeAware"));
        rows.Check(Field(Projects(removed), "mode") == "Full" && Fields(Projects(removed), "fallbackReasons").Contains("stale-project:src/B/B.csproj")
            && Fields(Projects(removed), "fallbackReasons").Contains("graph:src/A/A.csproj:missing-reference:src/A/A.csproj")
            && Fields(removed.Selection, "fallbackReasons").Contains("change-aware-unqualified"), "RS projectRemove", Reasons(Projects(removed)));
        var renamed = Bind(Against(Release(), rename, p1, "Shadow"));
        rows.Check(Field(Projects(renamed), "mode") == "Full" && Fields(Projects(renamed), "fallbackReasons").Contains("unknown-project:src/A2/A.csproj")
            && Fields(Projects(renamed), "fallbackReasons").Contains("stale-project:src/A/A.csproj"), "RS projectRename", Reasons(Projects(renamed)));
        var globbed = Projects(Bind(Against(Release(), glob, p0, "Shadow")));
        var aRow = ((object[])globbed["graphs"]!).Cast<IDictionary>().Where(g => Field(g, "sha") == glob).SelectMany(g => ((object[])g["projects"]!).Cast<IDictionary>())
            .First(p => Field(p, "path") == "src/A/A.csproj");
        var aInputs = Fields(aRow, "inputs");
        rows.Check(Field(globbed, "mode") == "Scoped" && Fields(globbed, "changedProjects").SequenceEqual(new[] { "src/A/A.csproj", "src/B/B.csproj" })
            && new[] { "src/shared/deep/er/T.cs", "src/shared/deep/S.cs", "src/A/gen/1.cs", "src/A/gen/2.cs", "src/A/assets/b.txt", "src/A/[lit]/n.md", "build/stylecop.json" }.All(aInputs.Contains)
            && !aInputs.Contains("src/A/gen/12.cs") && !aInputs.Contains("src/A/late/L.cs") && aInputs.All(i => !i.Contains("x.dll", StringComparison.Ordinal))
            && aRow["hasTests"] is true && Field(aRow, "batchDisposition") == "whole-project" && Fields(aRow, "references").SequenceEqual(new[] { "src/B/B.csproj" }),
            "RS globEdges", Reasons(globbed) + " inputs=" + string.Join(",", aInputs));
        var fullMode = Bind(Against(Release(), glob, p1, "Full"));
        rows.Check(ReadJson(fullMode.SelectionPath)["projectProposal"] == null && ReadJson(fullMode.SelectionPath)["proposal"] != null, "RS fullMode");
        var policyChange = Bind(Against(Release(), policy, p1, "ChangeAware"));
        rows.Check(Fields(Projects(policyChange), "fallbackReasons").Contains("shared-input:tests/test-execution-policy.json")
            && Field(ReadJson(policyChange.SelectionPath)["proposal"], "mode") == "full" && Field(policyChange.Selection, "effectiveMode") == "Full", "RS policyChange",
            Reasons(policyChange.Selection));
        var catalogChange = Bind(Against(Release(), catalog, p1, "Shadow"));
        rows.Check(Fields(Projects(catalogChange), "fallbackReasons").Contains("shared-input:tests/test-selection-policy.json")
            && Field(Projects(catalogChange), "mode") == "Full", "RS catalogChange", Reasons(Projects(catalogChange)));
        var same = Projects(Bind(Against(Release(), p1, p1, "Shadow")));
        rows.Check(Field(same, "mode") == "Scoped" && Fields(same, "changedPaths").Length == 0 && Fields(same, "affectedProjects").Length == 0, "RS sameCommit", Reasons(same));

        var release = Release();
        var receipt = WriteReceipt(repo.Root, release, p1, "full-p1", "receipts");
        var fromReceipt = Bind(Against(release, add, "", "ChangeAware", r => r.BaselineReceiptPath = receipt));
        rows.Check(fromReceipt.BaselineSha == p1 && Field(fromReceipt.Selection, "baselineDigest") == NightlyPaths.FileSha256(receipt)
            && !Fields(fromReceipt.Selection, "fallbackReasons").Contains("baseline-absent") && Field(fromReceipt.Selection, "baselineReceiptPath") == receipt,
            "RS receipt", Reasons(fromReceipt.Selection));
        release = Release();
        receipt = WriteReceipt(repo.Root, release, p1, "full-p1", "receipts");
        var incompatible = Bind(Against(release, policy, "", "Shadow", r => r.BaselineReceiptPath = receipt));
        rows.Check(incompatible.BaselineSha == p1 && Fields(incompatible.Selection, "fallbackReasons").Contains("baseline-incompatible")
            && Field(incompatible.Selection, "effectiveMode") == "Full", "RS receiptIncompatible", Reasons(incompatible.Selection));
        release = Release();
        var missing = Bind(Against(release, add, p1, "Shadow", r => r.BaselineReceiptPath = Path.Combine(release, "absent receipt.json")));
        rows.Check(missing.BaselineSha == p1 && Field(missing.Selection, "baselineDigest") == "" && Fields(missing.Selection, "fallbackReasons").Contains("baseline-absent"),
            "RS receiptMissing");
        release = Release();
        WriteReceipt(repo.Root, release, p0, "full-a");
        var lastByName = WriteReceipt(repo.Root, release, p1, "full-b");
        var chosen = Bind(Against(release, add, "", "Shadow"));
        rows.Check(chosen.BaselineSha == p1 && Field(chosen.Selection, "baselineDigest") == NightlyPaths.FileSha256(lastByName), "RS archiveChoice");
        release = Release();
        var filteredReceipt = WriteReceipt(repo.Root, release, p0, "full-a");
        WriteReceipt(repo.Root, release, p1, "full-b");
        var filtered = Bind(Against(release, add, p0, "Shadow"));
        rows.Check(filtered.BaselineSha == p0 && Field(filtered.Selection, "baselineDigest") == NightlyPaths.FileSha256(filteredReceipt), "RS archiveFiltered");
        release = Release();
        WriteReceipt(repo.Root, release, p1, "full-b");
        var archiveIncompatible = Bind(Against(release, policy, p1, "Shadow"));
        rows.Check(archiveIncompatible.BaselineSha == p1 && Field(archiveIncompatible.Selection, "baselineDigest") == ""
            && Fields(archiveIncompatible.Selection, "fallbackReasons").Contains("baseline-absent"), "RS archiveIncompatible", Reasons(archiveIncompatible.Selection));

        void Refused(string name, string reason, Func<string, ReleaseSelectionRequest> prepare)
        {
            var root = Release();
            Directory.CreateDirectory(root);
            var request = prepare(root);
            var before = Attempts(root);
            var refusal = Refusal(request);
            rows.Check(refusal.StartsWith(ReleaseSelectionAttempt.RefusedPrefix + reason, StringComparison.Ordinal) && Attempts(root) == before && before == 0, "RS " + name, refusal);
        }
        Refused("receiptCorrupt", "baseline-digest:authority/ledger.json", root =>
        {
            var path = WriteReceipt(repo.Root, root, p1, "full-p1", "receipts");
            EditJson(path, r => ((IDictionary)r["files"]!)["authority/ledger.json"] = new string('0', 64));
            return Against(root, add, "", "Shadow", r => r.BaselineReceiptPath = path);
        });
        Refused("notAncestor", "baseline-not-ancestor", root => Against(root, add, unrelated, "Shadow"));
        Refused("staleBaseline", "baseline-policy-hash-stale", root => Against(root, staleChild, staleBase, "Shadow"));
        rows.ShouldAllPass(17);
    }
}
