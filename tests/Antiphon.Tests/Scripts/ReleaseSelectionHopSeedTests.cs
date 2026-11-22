using System.Diagnostics;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The seeded rc tests-hop witness. W2-nightly S17: the tests hop is the nightly-tests command, never the selection hop
/// (Invoke-ReleaseSelectionHop and its switch helpers are deleted), so SeededRcHopBinds and SwitchDriftGuard are in-process
/// on the command's own argument builder (NightlyRunParts.TestsHopOptions) and parser (NightlyTestsOptions); since W2-nightly
/// S20 the production hop of SeededRcHopBinds is the C# run core's (NightlyRun.Run, in-process), and HopFailureRecord is
/// in-process on NightlyRun.WriteHopFailure and the run's catch (scripts/test-release-selection-hop-seed.ps1 is deleted). The
/// operator list is the Windows Full launch (Profile rc, Trigger rc, Ref, ExpectedSha, CandidateId, PriorFailuresPath,
/// PriorFailuresDigest, AcceptCrossCandidateSeed, SeedSourceCandidateId) plus the selection the rc profile binds.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseSelectionHopSeedTests
{
    private const string PriorPath = @"C:\Users\lndco\antiphon-seeds\d47848cc.json";
    private const string PriorDigest = "bb3c427c280930fba3c372fed4bf8a8416b0726e1c23e33c63717e40fee0b72e";
    private const string SeedSource = "rc-2026-10-27-cde8def41638";
    private const string Candidate = "rc-2026-11-01-87f4a868a86c";
    private const string Sha = "87f4a868a86ce594d6dc9d1f1365315cb3f86df9";
    private const string RunId = "8dc9e695e8f445498bdb7fdd660028c2";

    /// <summary>The seeded rc hop's request (New-HsRcTestsArguments' values).</summary>
    private static TestsHopRequest Request(string root) => new()
    {
        CheckoutRoot = @"C:\Antiphon\releases\checkout-250a07fb", RunDir = @"C:\Antiphon\releases\logs\2026-11-01-1025-8dc9e695", Sha = Sha,
        GitRefLabel = "origin/rc/2026-11-01", Trigger = "rc", Profile = "rc", RunId = RunId, Repository = "michal-ciechan/Antiphon", SelectionMode = "Full",
        BaselineSha = "", BaselineReceiptPath = Path.Combine(root, "baseline.json"), QualificationPath = Path.Combine(root, "qualification.json"),
        TimingRoot = Path.Combine(root, "timing"), TrainManifestPath = Path.Combine(root, "train.json"), ReleaseIntentPath = Path.Combine(root, "intent.json"),
        SelectionAttemptPath = Path.Combine(root, "selection-attempt.json"), SelectionAttemptDigest = new string('a', 64), SelectionPath = Path.Combine(root, "selection.json"),
        SelectionDigest = new string('b', 64), Attempt = 1, ReleaseRoot = @"C:\Antiphon\releases", CandidateId = Candidate, ExpectedSha = Sha,
        PriorFailuresPath = PriorPath, PriorFailuresDigest = PriorDigest, AcceptCrossCandidateSeed = true, SeedSourceCandidateId = SeedSource,
    };

    private static bool Sequence(IReadOnlyList<string> list, params string[] expected)
    {
        for (var i = 0; i + expected.Length <= list.Count; i++)
            if (expected.Select((e, j) => string.Equals(list[i + j], e, StringComparison.Ordinal)).All(same => same)) return true;
        return false;
    }

    /// <summary>
    /// The argument builder's shape, then the production hop in-process (W2-nightly S20; it was nightly-run-impl.ps1's
    /// Invoke-NightlyTestsHop in a pwsh child): NightlyRun.Run of a -NoSync rc run with the seed flags over a committed
    /// candidate (NightlyRcChain, the selection bound by the staged CLI), its tests hop recorded as `dotnet exec &lt;cli&gt;
    /// nightly-tests ...` and parsed with the command's own parser. The seed input is a fixture path that does not exist,
    /// so the run keeps it as given (the failed-first fallback) and the hop must still forward it.
    /// </summary>
    [Test, Timeout(180_000)]
    public void SeededRcHopBinds(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("hs-seeded");
        var rows = new NightlyCaseRows();
        var flags = NightlyRunParts.TestsHopCliArguments(Request(fixture.Root));
        var shape = Sequence(flags, "--trigger", "rc", "--profile", "rc") && Sequence(flags, "--expected-sha", Sha) && Sequence(flags, "--candidate-id", Candidate)
            && Sequence(flags, "--git-ref", "origin/rc/2026-11-01")
            && Sequence(flags, "--prior-failures-path", PriorPath, "--prior-failures-digest", PriorDigest, "--seed-source-candidate-id", SeedSource)
            && flags.Contains("--accept-cross-candidate-seed");
        rows.Check(shape, "HS SeededRcHopBinds argument shape", string.Join(" | ", flags));

        using var chain = new NightlyRcChain("hs-seeded-run", "rc/2026-11-01").Publish();
        chain.TestsAnswer = _ => 0;
        var prior = chain.Fixture.PathOf("seeds", "d47848cc.json");
        var run = chain.Run(chain.Options(RunId, o =>
        {
            o.WhatIf = false; o.NoReport = true; o.PriorFailuresPath = prior; o.PriorFailuresDigest = PriorDigest; o.AcceptCrossCandidateSeed = true; o.SeedSourceCandidateId = SeedSource;
        }));
        var launch = chain.Launcher.Launches.FirstOrDefault(NightlyRunHops.IsTestsHop);
        rows.Check(launch != null && launch.Arguments[1] == NightlyRunWorld.StagedCli, "HS SeededRcHopBinds the run launches the nightly-tests command",
            run.ExitCode + " " + run.Phase + " " + run.Refusal + " " + string.Join(" | ", chain.Launcher.Lines));
        var bound = launch == null ? null : NightlyRunHops.TestsOptions(launch);
        rows.Check(bound?.PriorFailuresPath == prior, "HS SeededRcHopBinds PriorFailuresPath", bound?.PriorFailuresPath ?? "");
        rows.Check(bound?.PriorFailuresDigest == PriorDigest, "HS SeededRcHopBinds PriorFailuresDigest", bound?.PriorFailuresDigest ?? "");
        rows.Check(bound?.AcceptCrossCandidateSeed == true, "HS SeededRcHopBinds AcceptCrossCandidateSeed", bound?.AcceptCrossCandidateSeed.ToString() ?? "");
        rows.Check(bound?.SeedSourceCandidateId == SeedSource, "HS SeededRcHopBinds SeedSourceCandidateId", bound?.SeedSourceCandidateId ?? "");
        rows.Check(bound != null && bound.Profile == "rc" && bound.Trigger == "rc" && bound.CandidateId == chain.CandidateId() && bound.ExpectedSha == chain.Sha
            && bound.GitRef == "origin/rc/2026-11-01", "HS SeededRcHopBinds rc identity", bound == null ? "" : bound.Profile + "/" + bound.Trigger + "/" + bound.CandidateId);
        rows.ShouldAllPass(7, "HS SeededRcHopBinds argument shape", "HS SeededRcHopBinds PriorFailuresPath", "HS SeededRcHopBinds PriorFailuresDigest",
            "HS SeededRcHopBinds AcceptCrossCandidateSeed", "HS SeededRcHopBinds SeedSourceCandidateId");
    }

    /// <summary>
    /// A bare switch never takes the next token as its value: the hop's switches are the parser's own list (derived), a
    /// hand list that lacks one refuses it as a usage error instead of swallowing the next flag, and every switch of the
    /// nightly-tests and nightly-run commands keeps the value of the flag after it.
    /// </summary>
    [Test]
    public void SwitchDriftGuard()
    {
        var rows = new NightlyCaseRows();
        var all = new NightlyTestsOptions { AcceptCrossCandidateSeed = true, AllowSharedTree = true, WhatIf = true };
        var emitted = all.ToArguments().Where(a => a.StartsWith("--", StringComparison.Ordinal) && !NightlyTestsOptions.Valued.Contains(a)).ToArray();
        rows.Check(emitted.Length == NightlyTestsOptions.Switches.Length && emitted.All(NightlyTestsOptions.Switches.Contains), "HS SwitchDriftGuard classifier is derived",
            string.Join(",", emitted));

        var handRefused = false;
        var handDetail = "";
        try
        {
            NightlyArgs.Parse(["--accept-cross-candidate-seed", "--seed-source-candidate-id", SeedSource], NightlyTestsOptions.Valued, ["--allow-shared-tree", "--what-if"]);
            handDetail = "the hand list accepted --accept-cross-candidate-seed";
        }
        catch (NightlyUsageException ex) { handRefused = true; handDetail = ex.Message; }
        rows.Check(handRefused, "HS SwitchDriftGuard refuses the hand list", handDetail);

        var walkOk = true;
        var walkDetail = "";
        foreach (var name in NightlyTestsOptions.Switches)
        {
            var parsed = NightlyTestsOptions.Parse([name, "--repo-root", "SENTINEL"]);
            var set = name switch
            {
                "--accept-cross-candidate-seed" => parsed.AcceptCrossCandidateSeed,
                "--allow-shared-tree" => parsed.AllowSharedTree,
                "--what-if" => parsed.WhatIf,
                _ => false,
            };
            if (!set || parsed.RepoRoot != "SENTINEL") { walkOk = false; walkDetail = "nightly-tests " + name + " repo=" + parsed.RepoRoot; }
        }
        foreach (var name in NightlyRunOptions.Switches)
        {
            var parsed = NightlyRunOptions.Parse([name, "--checkout-root", "SENTINEL"]);
            var set = name switch
            {
                "--no-report" => parsed.NoReport,
                "--no-sync" => parsed.NoSync,
                "--accept-cross-candidate-seed" => parsed.AcceptCrossCandidateSeed,
                "--what-if" => parsed.WhatIf,
                _ => false,
            };
            if (!set || parsed.CheckoutRoot != "SENTINEL") { walkOk = false; walkDetail = "nightly-run " + name + " checkout=" + parsed.CheckoutRoot; }
        }
        rows.Check(walkOk, "HS SwitchDriftGuard every target switch keeps its value", walkDetail);
        rows.ShouldAllPass(3, "HS SwitchDriftGuard refuses the hand list", "HS SwitchDriftGuard every target switch keeps its value");
    }

    /// <summary>
    /// In-process since W2-nightly S20 (was the scripts/test-release-selection-hop-seed.ps1 case on Write-NightlyHopFailure):
    /// NightlyRun.WriteHopFailure puts the exception and the command in the log directory's hop-failure.txt and in the state
    /// (error, command, exitCode 1), and the run's catch writes it: a tests hop that throws leaves both, with the hop's command.
    /// </summary>
    [Test]
    public void HopFailureRecord()
    {
        using var fixture = NightlyFixture.Create("hs-hop-failure");
        var rows = new NightlyCaseRows();
        var log = fixture.PathOf("empty-log");
        var statePath = fixture.PathOf("last-run.json");
        var state = new System.Collections.Specialized.OrderedDictionary(StringComparer.OrdinalIgnoreCase) { ["phase"] = "tests", ["runId"] = RunId, ["exitCode"] = 1 };
        const string message = "Exception calling \"GetBytes\" with \"1\" argument(s): \"Value cannot be null. (Parameter 'chars')\"";
        var command = @"antiphon-scripts nightly-tests --repo-root C:\Antiphon\releases\checkout-250a07fb --seed-source-candidate-id " + SeedSource + " --accept-cross-candidate-seed";
        NightlyRun.WriteHopFailure(fixture.Host(), state, statePath, log, message, command);
        var failurePath = Path.Combine(log, "hop-failure.txt");
        var text = File.Exists(failurePath) ? File.ReadAllText(failurePath) : "";
        rows.Check(text.Contains(message, StringComparison.Ordinal), "HS HopFailureRecord log has the exception", text);
        rows.Check(text.Contains(command, StringComparison.Ordinal), "HS HopFailureRecord log has the command", text);
        using (var written = JsonDocument.Parse(File.ReadAllText(statePath)))
        {
            var root = written.RootElement;
            rows.Check(root.GetProperty("error").GetString() == message, "HS HopFailureRecord last-run error", root.GetProperty("error").GetString() ?? "");
            rows.Check(root.GetProperty("command").GetString() == command, "HS HopFailureRecord last-run command", root.GetProperty("command").GetString() ?? "");
            rows.Check(root.GetProperty("exitCode").GetInt32() == 1, "HS HopFailureRecord last-run exit", root.GetProperty("exitCode").ToString());
        }

        // The run's catch: the tests hop throws; the state names the error and the hop's command, and the run directory has the log.
        using var w = new NightlyRunWorld("hs-hop-catch") { Behaviour = launch => throw new InvalidOperationException(message) };
        var run = w.Run(o => o.NoSync = true);
        var last = NightlyRunWorld.Json(w.LastRunPath);
        var hop = w.Launcher.Launches.FirstOrDefault(NightlyRunHops.IsTestsHop);
        var logDir = last?.GetProperty("logDir").GetString() ?? "";
        rows.Check(run.ExitCode == 1 && hop != null && last?.GetProperty("error").GetString() == message && last?.GetProperty("command").GetString() == hop.CommandText
            && File.Exists(Path.Combine(logDir, "hop-failure.txt")) && File.ReadAllText(Path.Combine(logDir, "hop-failure.txt")).Contains(hop.CommandText, StringComparison.Ordinal),
            "HS HopFailureRecord catch writes the failure", run.ExitCode + " " + last?.ToString());
        rows.ShouldAllPass(6, "HS HopFailureRecord log has the exception", "HS HopFailureRecord log has the command", "HS HopFailureRecord last-run error",
            "HS HopFailureRecord catch writes the failure");
    }
}
