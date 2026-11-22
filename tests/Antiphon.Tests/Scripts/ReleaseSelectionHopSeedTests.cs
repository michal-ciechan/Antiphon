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
/// on the command's own argument builder (NightlyRunParts.TestsHopOptions) and parser (NightlyTestsOptions); the child of
/// SeededRcHopBinds is the production hop (Invoke-NightlyTestsHop of nightly-run-impl.ps1) through the checkout's launcher
/// (antiphon-cli.ps1) to the script harness host echoing the argv it received (CLI_ECHO_COMMAND). HopFailureRecord is the
/// scripts/test-release-selection-hop-seed.ps1 case (Write-NightlyHopFailure is nightly-run-impl.ps1's until S20). The
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

    private static Task Run(string name, int rows, CancellationToken token, params string[] required) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-selection-hop-seed.ps1", "HS", name, rows, required,
            new ScriptHarnessOptions(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10)), token);

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

    // The production hop in a pwsh child: nightly-run-impl.ps1's Invoke-NightlyTestsHop with the built flags, in the checkout.
    private const string Driver = """
        param([string]$Request)
        $ErrorActionPreference = 'Stop'
        $r = [IO.File]::ReadAllText($Request) | ConvertFrom-Json
        $lib = [string]$r.lib
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
        . (Join-Path $lib 'nightly-run-impl.ps1')
        $flags = New-Object System.Collections.Generic.List[string]
        foreach ($a in @($r.arguments)) { $flags.Add([string]$a) }
        exit (Invoke-NightlyTestsHop -ArgumentList $flags.ToArray() -CheckoutRoot ([string]$r.checkout))
        """;

    [Test, Timeout(120_000)]
    public async Task SeededRcHopBinds(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("hs-seeded");
        var rows = new NightlyCaseRows();
        var flags = NightlyRunParts.TestsHopCliArguments(Request(fixture.Root));
        var shape = Sequence(flags, "--trigger", "rc", "--profile", "rc") && Sequence(flags, "--expected-sha", Sha) && Sequence(flags, "--candidate-id", Candidate)
            && Sequence(flags, "--git-ref", "origin/rc/2026-11-01")
            && Sequence(flags, "--prior-failures-path", PriorPath, "--prior-failures-digest", PriorDigest, "--seed-source-candidate-id", SeedSource)
            && flags.Contains("--accept-cross-candidate-seed");
        rows.Check(shape, "HS SeededRcHopBinds argument shape", string.Join(" | ", flags));

        // The checkout the hop runs in holds its own launcher, so the run copy (bin-launcher) stays in this fixture.
        var checkout = fixture.PathOf("checkout");
        Directory.CreateDirectory(Path.Combine(checkout, "scripts", "lib"));
        File.Copy(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib", "antiphon-cli.ps1"), Path.Combine(checkout, "scripts", "lib", "antiphon-cli.ps1"));
        var request = fixture.PathOf("request.json");
        File.WriteAllText(request, JsonSerializer.Serialize(new { lib = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "lib"), checkout, arguments = flags }));
        var driver = fixture.PathOf("drive.ps1");
        File.WriteAllText(driver, Driver);
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", driver, "-Request", request }) start.ArgumentList.Add(argument);
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = Path.Combine(AppContext.BaseDirectory, "script-harness-host", "Antiphon.ScriptHarnessHost.dll");
        start.Environment["CLI_ECHO_COMMAND"] = "nightly-tests";
        start.Environment["CLI_ECHO_EXIT"] = "0";
        var ran = await C1514Support.RunProcess(start, token);
        var line = ran.Text.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.StartsWith("{\"arguments\"", StringComparison.Ordinal));
        rows.Check(ran.ExitCode == 0 && line != null, "HS SeededRcHopBinds child exit", ran.ExitCode + " " + ran.Text);
        NightlyTestsOptions? bound = null;
        if (line != null)
        {
            using var echo = JsonDocument.Parse(line);
            bound = NightlyTestsOptions.Parse(echo.RootElement.GetProperty("arguments").EnumerateArray().Select(a => a.GetString()!).ToArray());
        }
        rows.Check(bound?.PriorFailuresPath == PriorPath, "HS SeededRcHopBinds PriorFailuresPath", bound?.PriorFailuresPath ?? "");
        rows.Check(bound?.PriorFailuresDigest == PriorDigest, "HS SeededRcHopBinds PriorFailuresDigest", bound?.PriorFailuresDigest ?? "");
        rows.Check(bound?.AcceptCrossCandidateSeed == true, "HS SeededRcHopBinds AcceptCrossCandidateSeed", bound?.AcceptCrossCandidateSeed.ToString() ?? "");
        rows.Check(bound?.SeedSourceCandidateId == SeedSource, "HS SeededRcHopBinds SeedSourceCandidateId", bound?.SeedSourceCandidateId ?? "");
        rows.Check(bound is { Profile: "rc", Trigger: "rc", CandidateId: Candidate, ExpectedSha: Sha, GitRef: "origin/rc/2026-11-01" },
            "HS SeededRcHopBinds rc identity", bound == null ? "" : bound.Profile + "/" + bound.Trigger + "/" + bound.CandidateId);
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

    [Test, Timeout(90_000)]
    public Task HopFailureRecord(CancellationToken token) => Run(nameof(HopFailureRecord), 6, token,
        "HS HopFailureRecord log has the exception",
        "HS HopFailureRecord log has the command",
        "HS HopFailureRecord last-run error",
        "HS HopFailureRecord catch writes the failure");
}
