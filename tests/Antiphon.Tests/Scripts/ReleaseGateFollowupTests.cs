using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateFollowupTests
{
    private const string Ref = "rc/2026-10-18";
    private const string LegacyRef = "release/rc-20260923T083000Z";

    /// <summary>
    /// CARD-1336 N, in-process since W2-nightly S20 (was the card1336-release-gate-followups.ps1 case on the PowerShell run
    /// core and its seams): the rc candidate identity on NightlyRun.Run over a real candidate chain (NightlyRcChain: two
    /// commits on rc/2026-10-18, a legacy release/rc stamp branch, a bare origin and an owned clone), the self-update hop and
    /// the tests hop answered in this process. The omitted, whitespace, full-ref, repeated and explicit ids derive the same
    /// identity; another pin derives another; a foreign id, a missing, short or invalid sha and a bad ref refuse before
    /// anything is written; a legacy stamp keeps its id; a synced run binds the selection, hops with the identity, runs the
    /// workload on the bound selection and earns rc credit in the candidate's own store; -NoSync binds without fetching;
    /// -NoReport claims no delivery; a moved candidate refuses; the master default state root refuses; the launcher's help
    /// names the identity rules; nothing creates publication authority.
    /// </summary>
    [Test, Timeout(900_000)]
    public void C1336_NightlyCandidateIdentity(CancellationToken token)
    {
        using var chain = new NightlyRcChain("c1336", Ref);
        var a = chain.Sha;
        chain.Write("fixture.txt", "B");
        var b = chain.Commit("candidate B");
        NightlyRcChain.Git(chain.Seed, "branch", "fixture-b", b);
        NightlyRcChain.Git(chain.Seed, "branch", LegacyRef, a);
        NightlyRcChain.Git(chain.Seed, "reset", "-q", "--hard", a);
        chain.Publish();
        // The synthetic workload reports optimistically, as the C1336 seams did; only the run's own report decides delivery.
        chain.AfterTests = result =>
        {
            if (string.IsNullOrEmpty(result.SummaryPath) || !File.Exists(result.SummaryPath)) return;
            var summary = JsonNode.Parse(File.ReadAllText(result.SummaryPath))!.AsObject();
            summary["reportDelivered"] = true;
            File.WriteAllText(result.SummaryPath, summary.ToJsonString());
        };
        var master = chain.Fixture.PathOf("private-master");
        Directory.CreateDirectory(master);
        foreach (var name in new[] { "last-run.json", "last-complete-green.json", "last-monitor.json", "interim-qualification-receipt.json" })
            File.WriteAllText(Path.Combine(master, name), "{\"sentinel\":\"" + name + "\"}");
        var masterBefore = Directory.GetFiles(master).ToDictionary(f => f, File.ReadAllText);
        var expected = "rc-2026-10-18-" + a[..12];
        var rows = new NightlyCaseRows();
        var stateRoots = new List<string>();

        Candidate Run(string name, Action<NightlyRunOptions>? extra = null)
        {
            var options = chain.Options(name, extra);
            var traceBefore = chain.TraceText.Length;
            var outBefore = chain.Fixture.Out.ToString().Length;
            var result = chain.Run(options);
            if (options.StateRoot != NightlyRun.DefaultStateRoot) stateRoots.Add(options.StateRoot);
            var lines = chain.Fixture.Out.ToString()[outBefore..].Replace("\r\n", "\n").Split('\n');
            var terminal = lines.LastOrDefault(l => l.StartsWith("{\"nativeRunId\":", StringComparison.Ordinal));
            return new Candidate(options, result, terminal == null ? null : JsonNode.Parse(terminal), chain.TraceText[traceBefore..]);
        }

        bool EarlyRefusal(Candidate run, string reason) =>
            run.Result.ExitCode == 3 && run.Result.Phase == "profile" && run.Result.Refusal == reason && run.Trace.Length == 0
            && !File.Exists(Path.Combine(run.Options.StateRoot, "last-run.json")) && !File.Exists(Path.Combine(run.Options.StateRoot, "run.lock"))
            && !Directory.Exists(run.Options.ReleaseRoot) && !Directory.Exists(run.Options.LogRoot) && !Directory.Exists(run.Options.CoordinationRoot);

        var omitted = Run("omitted");
        rows.Check(omitted.Result.CandidateId == expected, "C1336 N omitted", "CandidateId expected=" + expected + " actual=" + omitted.Result.CandidateId + "; refusal=" + omitted.Result.Refusal);
        if (omitted.Result.ExitCode != 0) throw new InvalidOperationException("Omitted downstream admission failed: " + omitted.Result.Refusal + " " + chain.Fixture.Out + chain.Fixture.Error);
        foreach (var (name, extra) in new (string, Action<NightlyRunOptions>?)[]
                 {
                     ("whitespace", o => o.CandidateId = " \t "), ("full-ref", o => o.Ref = "refs/heads/" + Ref), ("repeat", null), ("explicit", o => o.CandidateId = expected),
                 })
        {
            var run = Run(name, extra);
            rows.Check(run.Result.CandidateId == expected && run.Result.ExitCode == 0, "C1336 N " + name, run.Result.CandidateId + " " + run.Result.ExitCode + " " + run.Result.Refusal);
        }
        NightlyRcChain.Git(chain.Clone, "reset", "-q", "--hard", b);
        NightlyRcChain.Git(chain.Origin, "update-ref", "refs/heads/" + Ref, b);
        var pinned = Run("different-pin", o => o.ExpectedSha = b);
        var other = "rc-2026-10-18-" + b[..12];
        rows.Check(pinned.Result.CandidateId == other && other != expected && pinned.Result.ExitCode == 0, "C1336 N different-pin", pinned.Result.CandidateId);
        NightlyRcChain.Git(chain.Clone, "reset", "-q", "--hard", a);
        NightlyRcChain.Git(chain.Origin, "update-ref", "refs/heads/" + Ref, a);
        foreach (var (name, extra, reason) in new (string, Action<NightlyRunOptions>, string)[]
                 {
                     ("foreign", o => o.CandidateId = "foreign", "rc-candidate-id-mismatch"),
                     ("missing-sha", o => { o.ExpectedSha = ""; o.CandidateId = "foreign"; }, "rc-expected-sha"),
                     ("short-sha", o => { o.ExpectedSha = a[..7]; o.CandidateId = "foreign"; }, "rc-expected-sha"),
                     ("invalid-sha", o => { o.ExpectedSha = new string('z', 40); o.CandidateId = "foreign"; }, "rc-expected-sha"),
                     ("bad-ref", o => { o.Ref = "rc/2026-02-30"; o.ExpectedSha = ""; o.CandidateId = "foreign"; }, "rc-ref"),
                 })
        {
            var run = Run(name, extra);
            rows.Check(EarlyRefusal(run, reason), "C1336 N " + name, run.Result.ExitCode + "/" + run.Result.Phase + "/" + run.Result.Refusal + " trace=" + run.Trace);
        }
        var legacy = Run("legacy-omitted", o => o.Ref = LegacyRef);
        var legacyExplicit = Run("legacy-explicit", o => { o.Ref = LegacyRef; o.CandidateId = "rc-20260923T083000Z"; });
        rows.Check(legacy.Result.CandidateId == "rc-20260923T083000Z" && legacy.Result.ExitCode == 0 && legacyExplicit.Result.CandidateId == "rc-20260923T083000Z"
            && legacyExplicit.Result.ExitCode == 0, "C1336 N legacy", legacy.Result.CandidateId + "/" + legacy.Result.ExitCode + " " + legacyExplicit.Result.CandidateId + "/" + legacyExplicit.Result.ExitCode);

        var sync = Run("sync", o => { o.NoSync = false; o.WhatIf = false; });
        var selected = Selection(sync, expected, a);
        rows.Check(selected.Ok && sync.Result.ExitCode == 0 && Regex.IsMatch(sync.Trace, "GIT fetch origin " + Regex.Escape(Ref), RegexOptions.IgnoreCase), "C1336 N sync-selection",
            sync.Result.ExitCode + " " + sync.Result.Phase + " " + sync.Result.Refusal + "\n" + chain.Fixture.Error);
        var hop = chain.SelfHops.FirstOrDefault(h => h.ContinueRunId == "sync");
        var workload = chain.TestsHops.FirstOrDefault(t => t.RunId == "sync");
        var state = NightlyRcChain.Json(Path.Combine(sync.Options.StateRoot, "last-run.json"));
        var summaryPath = S(state, "summaryPath");
        var summary = summaryPath.Length > 0 ? NightlyRcChain.Json(summaryPath) : null;
        var hopOk = hop != null && hop.CandidateId == expected && hop.ExpectedSha == a && hop.Ref == Ref && hop.Profile == "rc" && hop.ContinueRunId == "sync" && hop.NoSync
            && hop.ContinueParentPid > 0 && hop.ContinueParentStartedAt.Length > 0 && selected.Ok && summary != null && sync.Result.ExitCode == 0
            && hop.StateRoot == sync.Options.StateRoot && hop.LogRoot == sync.Options.LogRoot && hop.ReleaseRoot == sync.Options.ReleaseRoot
            && hop.CoordinationRoot == sync.Options.CoordinationRoot && hop.CheckoutRoot == sync.Options.CheckoutRoot;
        rows.Check(hopOk, "C1336 N hop-child", hop == null ? "no hop" : string.Join(" ", hop.ToArguments()));
        var selection = summary?["selection"];
        var workloadOk = workload != null && workload.CandidateId == expected && workload.ExpectedSha == a && workload.RunId == "sync" && workload.Attempt == 1 && selected.Ok
            && workload.SelectionAttemptPath == selected.AttemptPath && workload.SelectionPath == selected.SelectionPath
            && workload.SelectionAttemptDigest == Sha256(selected.AttemptPath) && workload.SelectionDigest == Sha256(selected.SelectionPath)
            && S(selection, "candidateId") == expected && S(selection, "runId") == "sync" && Int(selection, "attempt") == 1 && S(summary, "sha") == a
            && Bool(summary, "testsPassed") && Bool(summary, "coverageComplete") && sync.Result.ExitCode == 0;
        rows.Check(workloadOk, "C1336 N workload-child", (workload == null ? "no workload" : string.Join(" ", workload.ToArguments())) + "\n" + summary?.ToJsonString());
        var t = sync.Terminal;
        rows.Check(sync.Result.ExitCode == 0 && Int(t, "exitCode") == 0 && S(t, "nativeRunId") == "sync" && S(t, "candidateId") == expected && S(t, "sha") == a
            && S(state, "runId") == "sync" && S(state, "candidateId") == expected && S(state, "sha") == a && S(summary, "runId") == "sync" && S(summary, "sha") == a
            && Bool(t, "coverageComplete") && Bool(t, "testsPassed") && Bool(t, "reportDelivered"), "C1336 N completion", t?.ToJsonString() ?? "no record");
        var greenPath = Path.Combine(sync.Options.ReleaseRoot, "candidates", expected, "complete-green.json");
        var green = NightlyRcChain.Json(greenPath);
        var greenFiles = Directory.Exists(sync.Options.ReleaseRoot) ? Directory.GetFiles(sync.Options.ReleaseRoot, "*green.json", SearchOption.AllDirectories) : [];
        rows.Check(green != null && S(green, "creditKind") == "rc-release" && S(green, "candidateId") == expected && S(green, "sha") == a && S(green, "runId") == "sync"
            && Int(green["selection"], "attempt") == 1 && greenFiles.Length == 1 && greenFiles[0] == greenPath
            && !File.Exists(Path.Combine(sync.Options.StateRoot, "last-complete-green.json")), "C1336 N rc-green", green?.ToJsonString() ?? "no green");

        var noSync = Run("nosync", o => o.WhatIf = false);
        var noSyncSelection = Selection(noSync, expected, a);
        var noSyncWorkload = chain.TestsHops.FirstOrDefault(w => w.RunId == "nosync");
        rows.Check(noSyncSelection.Ok && noSync.Result.ExitCode == 0 && Bool(noSync.Terminal, "testsPassed") && noSyncWorkload?.SelectionAttemptDigest == Sha256(noSyncSelection.AttemptPath)
            && !Regex.IsMatch(noSync.Trace, "GIT (fetch|reset)") && !chain.SelfHops.Any(h => h.ContinueRunId == "nosync"), "C1336 N nosync-selection",
            noSync.Result.ExitCode + " " + noSync.Result.Refusal);

        // The PowerShell seams moved the master default to a private root; the C# default is a constant, refused before any write.
        var masterRun = Run("master-root", o => o.StateRoot = NightlyRun.DefaultStateRoot);
        rows.Check(masterRun.Result.ExitCode == 3 && masterRun.Result.Phase == "profile" && masterRun.Result.Refusal == "rc-master-state-root" && masterRun.Trace.Length == 0
            && !Directory.Exists(masterRun.Options.ReleaseRoot) && !Directory.Exists(masterRun.Options.CoordinationRoot), "C1336 N master-root", masterRun.Result.Refusal);
        var noReport = Run("noreport", o => { o.WhatIf = false; o.NoReport = true; });
        var noReportState = NightlyRcChain.Json(Path.Combine(noReport.Options.StateRoot, "last-run.json"));
        var noReportSummaryPath = S(noReportState, "summaryPath");
        var noReportSummary = noReportSummaryPath.Length > 0 ? NightlyRcChain.Json(noReportSummaryPath) : null;
        rows.Check(noReport.Result.ExitCode == 0 && Bool(noReportSummary, "reportDelivered") && !noReport.Result.ReportDelivered && !Bool(noReportState, "reportDelivered")
            && !Bool(noReport.Terminal, "reportDelivered") && string.IsNullOrEmpty(noReport.Result.CreditKind) && !File.Exists(noReportSummaryPath + ".report-receipt")
            && (!Directory.Exists(noReport.Options.ReleaseRoot) || Directory.GetFiles(noReport.Options.ReleaseRoot, "*green.json", SearchOption.AllDirectories).Length == 0),
            "C1336 N noreport", noReport.Result.ExitCode + " " + noReport.Result.CreditKind);

        NightlyRcChain.Git(chain.Origin, "update-ref", "refs/heads/" + Ref, b);
        var moved = Run("moved-sync", o => { o.NoSync = false; o.WhatIf = false; });
        rows.Check(moved.Result.ExitCode == 3 && moved.Result.Refusal == "candidate-moved" && moved.Result.Sha == b && !chain.TestsHops.Any(w => w.RunId == "moved-sync")
            && !Directory.Exists(moved.Options.ReleaseRoot), "C1336 N moved-sync", moved.Result.Refusal + " " + moved.Result.Sha);
        var movedNoSync = Run("moved-nosync", o => o.WhatIf = false);
        var movedIndex = chain.TestsHops.FindIndex(w => w.RunId == "moved-nosync");
        var movedResult = movedIndex >= 0 ? chain.TestsResults[movedIndex] : null;
        var movedSelection = Selection(movedNoSync, expected, b);
        rows.Check(movedNoSync.Result.ExitCode != 0 && movedResult is { ExitCode: 2 } && (movedResult.Error ?? "").StartsWith("expected-sha-mismatch", StringComparison.Ordinal)
            && movedSelection.Ok && !Regex.IsMatch(movedNoSync.Trace, "EXEC .*--report-trx")
            && (!Directory.Exists(movedNoSync.Options.ReleaseRoot) || Directory.GetFiles(movedNoSync.Options.ReleaseRoot, "*green.json", SearchOption.AllDirectories).Length == 0),
            "C1336 N moved-nosync", movedNoSync.Result.ExitCode + " " + movedResult?.Error);
        rows.Check(Directory.GetFiles(master).All(f => masterBefore.TryGetValue(f, out var text) && File.ReadAllText(f) == text)
            && stateRoots.All(root => root.StartsWith(chain.Root, StringComparison.OrdinalIgnoreCase)), "C1336 N master-unchanged");
        var help = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1")).Split("#>")[0];
        var helpOk = help.Contains("rc-YYYY-MM-DD-<first 12 lowercase SHA hex>", StringComparison.Ordinal) && help.Contains("rc-YYYYMMDDTHHMMSSZ", StringComparison.Ordinal)
            && help.Contains("pinned full SHA", StringComparison.Ordinal) && help.Contains("Trigger rc requires Profile rc", StringComparison.Ordinal)
            && help.Contains("-Profile rc -Trigger rc", StringComparison.Ordinal) && help.Contains("does not create a candidate", StringComparison.Ordinal)
            && help.Contains("not master readiness", StringComparison.Ordinal)
            && new[] { "StateRoot", "LogRoot", "ReleaseRoot", "CoordinationRoot" }.All(n => help.Contains("-" + n + @" C:\scratch\", StringComparison.Ordinal));
        rows.Check(helpOk, "C1336 N help");
        var authority = Directory.GetFiles(chain.Root, "*", SearchOption.AllDirectories)
            .Where(f => new[] { "candidate.json", "publish.json", "authority.json", "publication-authority.json", "release-manifest.json" }.Contains(Path.GetFileName(f))).ToArray();
        rows.Check(authority.Length == 0, "C1336 N no-authority", string.Join(",", authority));
        rows.ShouldAllPass(25,
            "C1336 N omitted", "C1336 N whitespace", "C1336 N full-ref",
            "C1336 N repeat", "C1336 N different-pin", "C1336 N explicit",
            "C1336 N foreign", "C1336 N missing-sha", "C1336 N short-sha",
            "C1336 N invalid-sha", "C1336 N bad-ref", "C1336 N legacy",
            "C1336 N sync-selection", "C1336 N nosync-selection",
            "C1336 N hop-child", "C1336 N workload-child", "C1336 N completion",
            "C1336 N rc-green", "C1336 N master-unchanged", "C1336 N master-root",
            "C1336 N noreport", "C1336 N moved-sync", "C1336 N moved-nosync",
            "C1336 N help", "C1336 N no-authority");
    }

    private sealed record Candidate(NightlyRunOptions Options, NightlyRunResult Result, JsonNode? Terminal, string Trace);

    private sealed record SelectionFiles(bool Ok, string AttemptPath, string SelectionPath);

    /// <summary>Get-C1336Selection: the run's attempt and selection files under its release root, bound to the id, the sha and attempt 1.</summary>
    private static SelectionFiles Selection(Candidate run, string id, string sha)
    {
        var root = Path.Combine(run.Options.ReleaseRoot, "candidates", id, "attempts", run.Options.RunId);
        var attemptPath = Path.Combine(root, "selection-attempt.json");
        var selectionPath = Path.Combine(root, "selection.json");
        var attempt = NightlyRcChain.Json(attemptPath);
        var selection = NightlyRcChain.Json(selectionPath);
        var ok = attempt != null && selection != null && S(attempt, "candidateId") == id && S(attempt, "candidateSha") == sha && S(attempt, "runId") == run.Options.RunId
            && Int(attempt, "attempt") == 1 && S(selection, "candidateSha") == sha && S(attempt, "selectionDigest") == Sha256(selectionPath)
            && !Directory.Exists(Path.Combine(run.Options.ReleaseRoot, "candidates", "foreign"));
        return new SelectionFiles(ok, attemptPath, selectionPath);
    }

    private static string S(JsonNode? node, string name) => node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";

    private static bool Bool(JsonNode? node, string name) => node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<bool>(out var flag) && flag;

    private static int Int(JsonNode? node, string name) => node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<int>(out var number) ? number : int.MinValue;

    private static string Sha256(string path) =>
        File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() : "";

    [Test]
    public Task C1336_WrapperRef() =>
        ScriptHarness.RunHarnessCaseAsync("card1336-release-gate-followups.ps1", "C1336",
            "C1336_WrapperRef", 9,
            [
                "C1336 W own-0", "C1336 W own-23",
                "C1336 W missing-0", "C1336 W missing-23",
                "C1336 W foreign-0", "C1336 W foreign-23",
                "C1336 W schedule", "C1336 W master-unchanged", "C1336 W documentation"
            ],
            ScriptHarnessOptions.Default with
            {
                ScriptPath = Path.Combine(DelegateScriptRunner.RepoRoot,
                    "tests", "fixtures", "card1336-release-gate-followups.ps1")
            }, CancellationToken.None);
}
