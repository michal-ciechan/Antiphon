using System.Collections;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.ReleaseSelectionFixtures;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313 S6-A: a change-aware selection is intent only - every admitted attempt executes Full, a baseline must be an
/// archived full publication, and the attempt binds every hop parameter. In process since the 2026-11-22 plan (unit 8):
/// the cases of the deleted scripts/test-change-aware-release.ps1 run on ReleaseSelectionAttempt.Create, the hop reader
/// (ReleaseSelectionAttempt.Read, which the tests hop runs before any launch) and the member rule (ResolveMembers, which
/// the tests run applies), on an rc chain's seed repository (NightlyRcChain).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChangeAwareReleaseTests
{
    private static readonly object[] FullMembers = ["antiphon", "session-runner", "client", "scripts"];

    /// <summary>The chain's seed with a baseline B (client/src/App.tsx) and a candidate A (an offline codex provider test).</summary>
    private static (NightlyRcChain Chain, string B, string A, string Release) Fixture(string prefix)
    {
        var chain = new NightlyRcChain(prefix, "rc/2026-10-19");
        chain.Write("client/src/App.tsx", "baseline client");
        var b = chain.Commit("baseline");
        chain.Write("tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs", "candidate provider");
        var a = chain.Commit("candidate");
        var release = chain.Fixture.PathOf("releases λ");
        Directory.CreateDirectory(release);
        return (chain, b, a, release);
    }

    private static ReleaseSelectionRequest Request(NightlyRcChain chain, string release, string candidate, string baseline, string mode, string? run = null) =>
        new()
        {
            RepositoryRoot = chain.Seed, Repository = "fixture/repository", ReleaseRoot = release, CandidateId = chain.CandidateId(candidate), CandidateRef = chain.Ref,
            CandidateSha = candidate, BaselineSha = baseline, SelectionMode = mode, Profile = "rc", RunId = run ?? Guid.NewGuid().ToString("N"), Attempt = "1",
            BaselineReceiptPath = "", QualificationPath = "", TimingRoot = Path.Combine(release, "timing root"), ReleaseIntentPath = "", TrainManifestPath = "",
        };

    // A commit on top of A with one more path (a copy of an existing blob), made with plumbing in the seed.
    private static string WithPath(NightlyRcChain chain, string a, string path)
    {
        var blob = NightlyRcChain.Git(chain.Seed, "rev-parse", a + ":client/src/App.tsx").Trim();
        NightlyRcChain.Git(chain.Seed, "read-tree", a);
        NightlyRcChain.Git(chain.Seed, "update-index", "--add", "--cacheinfo", "100644," + blob + "," + path);
        var tree = NightlyRcChain.Git(chain.Seed, "write-tree").Trim();
        var sha = NightlyRcChain.Git(chain.Seed, "-c", "user.name=C1313", "-c", "user.email=c1313@example.invalid", "commit-tree", tree, "-p", a, "-m", "fallback " + path).Trim();
        NightlyRcChain.Git(chain.Seed, "read-tree", "HEAD");
        return sha;
    }

    [Test]
    public void C1313_ModeCredit()
    {
        var rows = new NightlyCaseRows();
        var rejected = true;
        foreach (object scope in new object[] { "ChangeAware", "invented", "", new object[] { "Full" } })
        {
            var state = new Dictionary<string, object>
            {
                ["profile"] = "rc", ["trigger"] = "rc", ["ref"] = "rc/2026-10-19", ["sha"] = new string('a', 40), ["expectedSha"] = new string('a', 40), ["candidateId"] = "fixture",
                ["runId"] = "fixture", ["policyHash"] = "fixture", ["exitCode"] = 0L, ["coverageComplete"] = true, ["testsPassed"] = true, ["reportDelivered"] = true, ["coverageScope"] = scope,
            };
            if (ReleaseGateCredit.Verdict(state).Ok) rejected = false;
        }
        rows.Check(rejected, "C1313 unqualified-no-credit");
        var launcherPath = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts/release-cut.ps1");
        var launcher = File.ReadAllText(launcherPath);
        using (var world = ReleaseCutWorld.Real("c1313-default-full"))
        {
            // Omit --selection-mode and exercise the cut through attempt sealing and the nightly-run handoff.
            var options = ReleaseCutOptions.Parse(
                ["--ref", ReleaseCutWorld.Ref, "--release-root", world.Release, "--remote-url", world.Origin,
                    "--coordination-root", world.Fixture.PathOf("coord"), "--repository", "fixture/repository", "--skip-publish"],
                Path.GetDirectoryName(launcherPath)!, _ => null);
            var cut = ReleaseCut.Run(options, world.Host());
            var run = world.Runs.Count == 1 ? world.Runs[0] : null;
            rows.Check(launcher.Contains("[ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full'", StringComparison.Ordinal)
                && launcher.Contains("'--selection-mode', $SelectionMode", StringComparison.Ordinal) && options.SelectionMode == "Full"
                && cut.ExitCode == 0 && cut.Tested
                && run is { SelectionMode: "Full", RequestedSelectionMode: "", IncrementalApprovalDigest: "" }
                && File.Exists(run.SelectionAttemptPath)
                && cut.Selection is IDictionary attempt && Field(attempt, "requestedMode") == "Full"
                && Field(attempt, "effectiveMode") == "Full" && Field(attempt, "coverageScope") == "Full"
                && File.Exists(run.SelectionPath) && ReadJson(run.SelectionPath)["executionEvidence"] is false,
                "C1313 default-full", $"parsed={options.SelectionMode} cut={cut.ExitCode}/{cut.Refusal} run={run?.SelectionMode}/{run?.RequestedSelectionMode}");
        }

        var (chain, b, a, release) = Fixture("c1313-mode");
        using (chain)
        {
            var qualification = Path.Combine(release, "qualification", "active.json");
            Directory.CreateDirectory(Path.GetDirectoryName(qualification)!);
            File.WriteAllText(qualification, "{\"status\":\"active\",\"diagnostic\":true}");
            var before = Tree(Path.GetDirectoryName(qualification)!);
            foreach (var (mode, label) in new[] { ("Full", "explicit-full"), ("Shadow", "shadow-full"), ("ChangeAware", "unqualified-full") })
            {
                var request = Request(chain, release, a, b, mode);
                request.QualificationPath = qualification;
                var bound = Bind(request);
                var attempt = ReleaseSelectionAttempt.Read(request);
                var selection = ReadJson(bound.SelectionPath);
                var proposal = selection["proposal"];
                var sets = proposal is IDictionary map && map["selectedSets"] is object[] selected ? selected.Select(s => Field(s, "id")).OrderBy(s => s, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
                var members = ReleaseSelectionAttempt.ResolveMembers(attempt, FullMembers, [FullMembers[0]]);
                rows.Check(Field(attempt, "requestedMode") == mode && Field(attempt, "effectiveMode") == "Full" && Field(attempt, "coverageScope") == "Full"
                    && Field(attempt, "qualificationDigest") == NightlyPaths.FileSha256(qualification) && members.SequenceEqual(FullMembers)
                    && (mode != "Shadow" || (Field(proposal, "mode") == "narrow" && sets.SequenceEqual(new[] { "core", "provider.codex.offline" }))),
                    "C1313 " + label, "proposal=" + Field(proposal, "mode") + " sets=" + string.Join(",", sets) + " reasons=" + string.Join(",", Fields(selection, "fallbackReasons")));
            }
            rows.Check(Tree(Path.GetDirectoryName(qualification)!) == before, "C1313 qualification-root-unchanged");
            var fallback = true;
            var detail = "";
            foreach (var path in new[] { "notes/unmapped.txt", "scripts/lib/release-authority.ps1", "Directory.Build.props" })
            {
                var sha = WithPath(chain, a, path);
                var bound = Bind(Request(chain, release, sha, b, "Shadow"));
                var selection = ReadJson(bound.SelectionPath);
                if (Field(selection["proposal"], "mode") != "full" || Fields(selection, "fallbackReasons").Length < 2 || Field(selection, "effectiveMode") != "Full")
                {
                    fallback = false;
                    detail += path + ": " + Field(selection["proposal"], "mode") + " " + string.Join(",", Fields(selection, "fallbackReasons")) + "; ";
                }
            }
            rows.Check(fallback, "C1313 selector-fallback-full", detail);
        }
        rows.ShouldAllPass(7);
    }

    [Test]
    public void C1313_BaselineAuthority()
    {
        var rows = new NightlyCaseRows();
        var (chain, b, a, release) = Fixture("c1313-baseline");
        using (chain)
        {
            var absent = Bind(Request(chain, release, a, b, "ChangeAware"));
            rows.Check(Field(absent.Selection, "effectiveMode") == "Full" && Fields(absent.Selection, "fallbackReasons").Contains("baseline-absent"), "C1313 baseline-absent-full");
            var archive = WriteReceipt(chain.Seed, release, b, "full-b", repository: "fixture/repository", candidateRef: chain.Ref);
            var request = Request(chain, release, a, b, "ChangeAware");
            request.BaselineReceiptPath = archive;
            var r = Bind(request);
            var other = WriteReceipt(chain.Seed, release, a, "full-a", repository: "fixture/repository", candidateRef: chain.Ref);
            NightlySelectionBinding? auto = null;
            try { auto = Bind(Request(chain, release, a, b, "Shadow")); }
            catch (NightlyRefusal) { }
            File.Delete(other);
            var resolved = Bind(Request(chain, release, a, "", "Shadow"));
            rows.Check(r.BaselineSha == b && Field(r.Selection, "baselineDigest") == NightlyPaths.FileSha256(archive) && auto?.BaselineSha == b && resolved.BaselineSha == b,
                "C1313 baseline-full-bound", "auto=" + auto?.BaselineSha + " resolved=" + resolved.BaselineSha);
            // A later commit on the candidate ref changes nothing: the proof is the archived publication and its receipt.
            NightlyRcChain.Git(chain.Seed, "-c", "user.name=C1313", "-c", "user.email=c1313@example.invalid", "commit", "-q", "--allow-empty", "-m", "advanced");
            request = Request(chain, release, a, "", "Shadow");
            request.BaselineReceiptPath = archive;
            NightlySelectionBinding? historical = null;
            try { historical = Bind(request); }
            catch (NightlyRefusal) { }
            rows.Check(historical != null && historical.BaselineSha == b && !Fields(historical.Selection, "fallbackReasons").Contains("baseline-absent"), "C1313 historical-baseline-accepted");

            var saved = File.ReadAllText(archive);
            foreach (var (variant, reason, label) in new[]
            {
                ("selected", "baseline-full-only", "baseline-full-only"), ("readiness", "baseline-full-only", "baseline-readiness-refused"),
                ("legacy", "baseline-unqualified", "baseline-legacy-refused"), ("foreign", "baseline-repository", "baseline-foreign-refused"),
                ("platform", "baseline-platform", "baseline-platform-refused"), ("digest", "baseline-digest", "baseline-corrupt-refused"),
                ("selector", "baseline-contract", "contract-hash-bound"),
            })
            {
                EditJson(archive, receipt =>
                {
                    switch (variant)
                    {
                        case "selected": receipt["coverageScope"] = "ChangeAware"; break;
                        case "readiness": receipt["profile"] = "nightly"; break;
                        case "legacy": receipt["format"] = "unqualified"; break;
                        case "foreign": receipt["repository"] = "foreign/repo"; break;
                        case "platform": receipt["platform"] = "other"; break;
                        case "digest": ((IDictionary)receipt["files"]!)["authority/ledger.json"] = new string('0', 64); break;
                        case "selector": ((IDictionary)receipt["contract"]!)["selectorHash"] = new string('0', 64); break;
                    }
                });
                var q = Request(chain, release, a, "", "Shadow");
                q.BaselineReceiptPath = archive;
                rows.Check(Refuses(q, reason), "C1313 " + label, Refusal(q));
                File.WriteAllText(archive, saved);
            }
            var gitRefused = true;
            foreach (var field in new[] { "CandidateSha", "BaselineSha" })
            {
                var q = Request(chain, release, a, b, "Shadow");
                if (field == "CandidateSha") q.CandidateSha = new string('0', 40); else q.BaselineSha = new string('0', 40);
                if (!Refuses(q, "selection-commit-unavailable")) gitRefused = false;
            }
            rows.Check(gitRefused, "C1313 git-object-refused");
            var tree = NightlyRcChain.Git(chain.Seed, "rev-parse", a + "^{tree}").Trim();
            var unrelated = NightlyRcChain.Git(chain.Seed, "-c", "user.name=C1313", "-c", "user.email=c1313@example.invalid", "commit-tree", tree, "-m", "unrelated").Trim();
            var notAncestor = Request(chain, release, a, unrelated, "Shadow");
            // An explicit absent receipt keeps a different claimed B from resolving out of the archive.
            notAncestor.BaselineReceiptPath = Path.Combine(release, "absent.json");
            rows.Check(Refuses(notAncestor, "baseline-not-ancestor"), "C1313 baseline-ancestor-required", Refusal(notAncestor));

            var incompatible = true;
            foreach (var variant in new[] { "policy", "runtime" })
            {
                NightlyRcChain.Git(chain.Seed, "checkout", "-q", "--detach", a);
                if (variant == "runtime") chain.Write("global.json", "{\"sdk\":{\"version\":\"99.0.100\"}}");
                else
                {
                    var policy = (IDictionary)TestSelectionJson.Parse(File.ReadAllText(Path.Combine(chain.Seed, "tests", "test-execution-policy.json")));
                    ((IDictionary)((IDictionary)policy["profiles"]!)["rc"]!)["description"] += " different contract";
                    policy["policyHash"] = NightlyPolicy.HashText(SelectionRepository.Json(policy));
                    chain.Write("tests/test-execution-policy.json", SelectionRepository.Json(policy));
                }
                var changed = chain.Commit(variant);
                var q = Request(chain, release, changed, "", "ChangeAware");
                q.BaselineReceiptPath = archive;
                var bound = Bind(q);
                if (Field(bound.Selection, "effectiveMode") != "Full" || !Fields(bound.Selection, "fallbackReasons").Contains("baseline-incompatible")) incompatible = false;
            }
            rows.Check(incompatible, "C1313 baseline-incompatible-full");
            var publish = Path.Combine(release, "archive", "full-b", "publish.json");
            EditJson(publish, p => p["steps"] = new[] { "intent" });
            EditJson(archive, receipt => ((IDictionary)receipt["files"]!)["publish.json"] = NightlyPaths.FileSha256(publish));
            var unpublished = Request(chain, release, a, "", "Shadow");
            unpublished.BaselineReceiptPath = archive;
            rows.Check(Refuses(unpublished, "baseline-publication-required"), "C1313 baseline-publication-required", Refusal(unpublished));
        }
        rows.ShouldAllPass(14);
    }

    /// <summary>
    /// The attempt binds every hop parameter. The hop itself (the options the release cut hands the run, the self-update and
    /// tests hops, the child consuming the bound selection) is C1313_ParameterHopsInProcess; here the attempt: the bound
    /// selection executes Full, an identical request resumes it, a changed one conflicts, a retry keeps the prior attempt,
    /// and the hop reader refuses a changed train manifest, a changed selection file or another candidate.
    /// </summary>
    [Test]
    public void C1313_ParameterHops()
    {
        var rows = new NightlyCaseRows();
        var (chain, b, a, release) = Fixture("c1313-hops");
        using (chain)
        {
            var q = Request(chain, release, a, b, "Shadow", "hop-run");
            q.BaselineReceiptPath = Path.Combine(release, "absent receipt.json");
            q.QualificationPath = Path.Combine(release, "qualification", "claim.json");
            Directory.CreateDirectory(Path.Combine(release, "qualification"));
            File.WriteAllText(q.QualificationPath, "{\"status\":\"unqualified\"}");
            q.ReleaseIntentPath = Path.Combine(release, "future intent.json");
            q.TrainManifestPath = Path.Combine(release, "future train.json");
            File.WriteAllText(q.TrainManifestPath, "{\"fixture\":\"opaque input only\"}");
            var bound = Bind(q);
            var consumed = ReleaseSelectionAttempt.Read(q);
            rows.Check(ReleaseSelectionAttempt.ResolveMembers(consumed, FullMembers, [FullMembers[0]]).SequenceEqual(FullMembers), "C1313 hop-executed-full");
            var same = ReleaseSelectionAttempt.Create(Copy(q));
            rows.Check(same.SelectionAttemptDigest == bound.SelectionAttemptDigest, "C1313 attempt-identical-resume");
            var original = NightlyPaths.FileSha256(bound.SelectionAttemptPath);
            var modeConflict = Refuses(Copy(q, c => c.SelectionMode = "ChangeAware"), "selection-attempt-conflict");
            var trainText = File.ReadAllText(q.TrainManifestPath);
            File.AppendAllText(q.TrainManifestPath, " ");
            var trainConflict = Refuses(Copy(q), "selection-attempt-conflict");
            var trainHop = NightlyDiagnosticCases.Refusal(() => ReleaseSelectionAttempt.Read(q));
            File.WriteAllText(q.TrainManifestPath, trainText);
            rows.Check(modeConflict && trainConflict && trainHop == "selection-hop-trainDigest" && NightlyPaths.FileSha256(bound.SelectionAttemptPath) == original,
                "C1313 attempt-conflict-refused", "train=" + trainHop);
            NightlySelectionBinding? next = null;
            try { next = ReleaseSelectionAttempt.Create(Copy(q, c => { c.RunId = "retry-run"; c.Attempt = "2"; })); }
            catch (NightlyRefusal) { }
            rows.Check(next != null && next.SelectionAttemptPath != bound.SelectionAttemptPath && NightlyPaths.FileSha256(bound.SelectionAttemptPath) == original, "C1313 prior-attempt-kept");
            var saved = File.ReadAllText(bound.SelectionPath);
            File.AppendAllText(bound.SelectionPath, " ");
            var digestHop = NightlyDiagnosticCases.Refusal(() => ReleaseSelectionAttempt.Read(q));
            File.WriteAllText(bound.SelectionPath, saved);
            rows.Check(digestHop == "selection-hop-digest", "C1313 hop-digest-refused", digestHop);
            var shaHop = NightlyDiagnosticCases.Refusal(() => ReleaseSelectionAttempt.Read(Copy(q, c => c.CandidateSha = b)));
            rows.Check(shaHop == "selection-hop-candidateSha", "C1313 hop-sha-refused", shaHop);
            // The attempt lives under the release root, outside the checkout: cleaning the checkout leaves it.
            chain.Write("bin-fixture/output.txt", "disposable");
            NightlyRcChain.Git(chain.Seed, "clean", "-fdx");
            rows.Check(NightlyPaths.FileSha256(bound.SelectionAttemptPath) == original && !Directory.Exists(Path.Combine(chain.Seed, "bin-fixture")), "C1313 attempt-survives-cleanup");
        }
        rows.ShouldAllPass(7);
    }

    /// <summary>
    /// The hop from the release cut into the run. Unit 15 of the 2026-11-22 plan moved the body of release-cut.ps1 (which
    /// splatted its $runArgs into the scripts/nightly-run.ps1 launcher, checked here by an AST probe) to ReleaseCut, which
    /// runs NightlyRun in process. Here ReleaseCut.Run on an rc chain (NightlyRcChain; the release root is the chain's root,
    /// so the cut's checkout is the chain's clone): the candidate journalled from the real bare origin, a Shadow selection
    /// with a baseline sealed by the cut, then NightlyRun.Run of the options the cut hands it, through the sync, the
    /// self-update hop and the tests hop. Every value the cut hands the run is a flag of the nightly-run command
    /// (hop-cut-options-are-run-flags), the run gets exactly the attempt the cut sealed (hop-cut-hands-the-sealed-attempt),
    /// and the child's summary consumes exactly the bound selection (hop-baseline-bound). Nothing publishes (SkipPublish).
    /// </summary>
    [Test, Timeout(1_200_000)]
    public void C1313_ParameterHopsInProcess(CancellationToken token)
    {
        var rows = new NightlyCaseRows();
        using var chain = new NightlyRcChain("c1313-hops", "rc/2026-10-19");
        chain.Write("client/src/App.tsx", "baseline client");
        var b = chain.Commit("baseline");
        chain.Write("tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs", "candidate provider");
        var a = chain.Commit("candidate");
        chain.Publish();
        var release = chain.Root;
        var claims = Path.Combine(release, "qualification λ");
        Directory.CreateDirectory(claims);
        File.WriteAllText(Path.Combine(claims, "claim.json"), "{\"status\":\"unqualified\"}");
        File.WriteAllText(Path.Combine(release, "future train.json"), "{\"fixture\":\"opaque input only\"}");
        var id = chain.CandidateId(a);
        var options = new ReleaseCutOptions
        {
            ReleaseRoot = release, RemoteUrl = chain.Origin, Ref = chain.Ref, Repository = "fixture/repository", RunId = "hop-run", SelectionMode = "Shadow",
            BaselineSha = b, BaselineReceiptPath = Path.Combine(release, "absent receipt.json"), QualificationPath = Path.Combine(claims, "claim.json"),
            TimingRoot = Path.Combine(release, "timing root"), TrainManifestPath = Path.Combine(release, "future train.json"),
            ReleaseIntentPath = Path.Combine(release, "future intent.json"), CoordinationRoot = chain.Fixture.PathOf("coord"), SkipPublish = true,
        };
        NightlyRunOptions? handed = null;
        NightlyRunResult? run = null;
        var github = new FakeReleaseGitHub();
        var cut = ReleaseCut.Run(options, new ReleaseCutHost
        {
            Nightly = chain.Host(),
            Files = new System.IO.Abstractions.FileSystem(),
            CandidateRemote = ReleaseGateRemote.Production(),
            Run = o =>
            {
                handed = o;
                return run = chain.Run(o);
            },
            Publisher = new ReleaseGateHost
            {
                Out = chain.Fixture.Out, Clock = TimeProvider.System, Seams = new ReleaseGateSeams { Git = new FakeCandidateRemote(a).Run, GitHub = github.Run },
                CardReader = (_, _) => throw new InvalidOperationException("no release card readback in a hop test"),
            },
        });
        var detail = "cut=" + cut.ExitCode + "/" + cut.Refusal + "\n" + chain.Fixture.Out + chain.Fixture.Error;
        var attemptPath = Path.Combine(ReleaseGateCredit.CandidateRoot(release, id), "attempts", "hop-run", "selection-attempt.json");
        var selectionPath = Path.Combine(ReleaseGateCredit.CandidateRoot(release, id), "attempts", "hop-run", "selection.json");
        rows.Check(handed != null && System.Text.Json.JsonSerializer.Serialize(NightlyRunOptions.Parse(handed.ToArguments())) == System.Text.Json.JsonSerializer.Serialize(handed)
            && handed.ToArguments().Length >= 40 && handed.Profile == "rc" && handed.Trigger == "rc" && handed.ExpectedSha == a && handed.CandidateId == id,
            "C1313 hop-cut-options-are-run-flags", handed == null ? detail : string.Join(" ", handed.ToArguments()));
        rows.Check(handed != null && handed.SelectionAttemptPath == attemptPath && handed.SelectionAttemptDigest == NightlyPaths.FileSha256(attemptPath)
            && handed.SelectionPath == selectionPath && handed.SelectionDigest == NightlyPaths.FileSha256(selectionPath) && handed.BaselineSha == b
            && handed.TimingRoot == options.TimingRoot && handed.SelectionMode == "Shadow", "C1313 hop-cut-hands-the-sealed-attempt", detail);
        var summaries = Directory.GetFiles(Path.Combine(release, "candidates"), "summary.json", SearchOption.AllDirectories)
            .Select(f => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f)))
            .Where(n => n is System.Text.Json.Nodes.JsonObject o && o.ContainsKey("runId")).ToArray();
        var consumed = summaries.Length == 1 ? summaries[0]!["selection"] : null;
        string C(string name) => consumed?[name]?.GetValue<string>() ?? "";
        var train = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(options.TrainManifestPath))).ToLowerInvariant();
        rows.Check(consumed != null && C("baselineSha") == b && C("candidateSha") == a && C("runId") == "hop-run" && C("timingRoot") == options.TimingRoot
            && C("baselineReceiptPath") == options.BaselineReceiptPath && C("qualificationPath") == options.QualificationPath
            && C("releaseIntentPath") == options.ReleaseIntentPath && C("trainManifestPath") == options.TrainManifestPath && C("trainDigest") == train
            && C("selectionDigest") == handed?.SelectionDigest && run?.Phase == "hop" && chain.SelfHops.Count == 1 && chain.TestsHops.Count == 1
            && github.Trace.Count == 0,
            "C1313 hop-baseline-bound", "phase=" + run?.Phase + " exit=" + run?.ExitCode + " refusal=" + run?.Refusal + " summaries=" + summaries.Length + " consumed="
            + consumed?.ToJsonString() + "\n" + detail);
        rows.ShouldAllPass(3, "C1313 hop-cut-options-are-run-flags", "C1313 hop-cut-hands-the-sealed-attempt", "C1313 hop-baseline-bound");
    }

    // A refusal of Create naming the reason, with no attempt written for the request's run.
    private static bool Refuses(ReleaseSelectionRequest request, string reason)
    {
        var attempt = Path.Combine(request.ReleaseRoot!, "candidates", request.CandidateId!, "attempts", request.RunId!, "selection-attempt.json");
        var existed = File.Exists(attempt);
        var refusal = Refusal(request);
        return refusal.StartsWith(ReleaseSelectionAttempt.RefusedPrefix, StringComparison.Ordinal) && refusal.Contains(reason, StringComparison.Ordinal) && File.Exists(attempt) == existed;
    }
}
