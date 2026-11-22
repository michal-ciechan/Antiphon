using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ChangeAwareReleaseTests
{
    internal ScriptHarnessOptions HarnessOptions { get; init; } = ScriptHarnessOptions.Default;

    [Test]
    public Task C1313_ModeCredit() => Run("C1313_ModeCredit", 7, "unqualified-no-credit", "default-full", "explicit-full", "shadow-full", "unqualified-full", "qualification-root-unchanged", "selector-fallback-full");

    [Test]
    public Task C1313_BaselineAuthority() => Run("C1313_BaselineAuthority", 14, "baseline-absent-full", "baseline-full-bound", "historical-baseline-accepted", "baseline-full-only", "baseline-readiness-refused", "baseline-legacy-refused", "baseline-foreign-refused", "baseline-platform-refused", "baseline-corrupt-refused", "contract-hash-bound", "git-object-refused", "baseline-ancestor-required", "baseline-incompatible-full", "baseline-publication-required");

    /// <summary>
    /// W2-nightly S20: release-cut.ps1's hop into the launcher. An AST check (a pwsh child parses both scripts): every key of
    /// release-cut's $runArgs splat is a parameter of scripts/nightly-run.ps1 and, but -PassThru and -SeamsPath (the
    /// launcher's own), a flag of the nightly-run command; release-cut calls the launcher once with that splat and reads
    /// .ExitCode and .Refusal; the launcher dot-sources only lib/antiphon-cli.ps1. Then the hop in-process: a Shadow
    /// selection with a baseline bound for the candidate, that splat's values as the command's options, NightlyRun.Run
    /// through the sync, the self-update hop and the tests hop (NightlyRcChain), and the child's summary consumes exactly the
    /// bound selection (hop-baseline-bound). The rest of the case (the attempt and hop refusals, the roster on the bound
    /// selection) stays in test-change-aware-release.ps1 (C1313_ParameterHops, nine rows); this part is
    /// C1313_ParameterHopsInProcess.
    /// </summary>
    [Test]
    public Task C1313_ParameterHops() => Run("C1313_ParameterHops", 9, "hop-executed-full", "attempt-identical-resume", "attempt-conflict-refused", "prior-attempt-kept",
        "hop-digest-refused", "hop-sha-refused", "pwsh-required-before-work", "full-without-pwsh", "attempt-survives-cleanup");

    /// <summary>The release-cut hop of C1313_ParameterHops, in-process since W2-nightly S20 (see C1313_ParameterHops).</summary>
    [Test, Timeout(1_200_000)]
    public async Task C1313_ParameterHopsInProcess(CancellationToken token)
    {
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var scripts = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts");
        var probe = Path.Combine(Path.GetTempPath(), "c1313-splat-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(probe, SplatProbe);
        System.Text.Json.JsonElement ast;
        try
        {
            var start = new System.Diagnostics.ProcessStartInfo("pwsh");
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", probe, "-Cut", Path.Combine(scripts, "release-cut.ps1"), "-Run", Path.Combine(scripts, "nightly-run.ps1") })
                start.ArgumentList.Add(argument);
            var ran = await C1514Support.RunProcess(start, token);
            var line = ran.Text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith("{\"keys\"", StringComparison.Ordinal));
            line.ShouldNotBeNull("the AST probe printed nothing (exit " + ran.ExitCode + ")\n" + ran.Text);
            using var document = System.Text.Json.JsonDocument.Parse(line);
            ast = document.RootElement.Clone();
        }
        finally { File.Delete(probe); }
        var keys = ast.GetProperty("keys").EnumerateArray().Select(k => k.GetString()!).ToArray();
        var parameters = ast.GetProperty("params").EnumerateArray().Select(k => k.GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var flags = Antiphon.Nightly.NightlyRunOptions.Valued.Concat(Antiphon.Nightly.NightlyRunOptions.Switches).ToHashSet(StringComparer.Ordinal);
        var launcherOnly = new[] { "PassThru", "SeamsPath" };
        var unmapped = keys.Where(k => !parameters.Contains(k) || (!launcherOnly.Contains(k) && !flags.Contains(Antiphon.Tests.Scripts.Nightly.NightlyRunHops.Kebab("-" + k)))).ToArray();
        rows.Check(keys.Length >= 20 && unmapped.Length == 0, "C1313 hop-splat-keys-are-launcher-parameters", "keys=" + string.Join(",", keys) + " unmapped=" + string.Join(",", unmapped));
        var cut = File.ReadAllText(Path.Combine(scripts, "release-cut.ps1"));
        rows.Check(ast.GetProperty("calls").GetInt32() == 1 && cut.Contains("[int]$run.ExitCode", StringComparison.Ordinal) && cut.Contains("[string]$run.Refusal", StringComparison.Ordinal),
            "C1313 hop-release-cut-reads-the-launcher-result", "calls=" + ast.GetProperty("calls").GetInt32());
        var dots = ast.GetProperty("dot").EnumerateArray().Select(d => d.GetString()!).ToArray();
        rows.Check(dots.Length == 1 && dots[0].Contains("antiphon-cli.ps1", StringComparison.Ordinal), "C1313 hop-launcher-dot-sources-only-the-cli-launcher", string.Join(" | ", dots));

        using (var chain = new Antiphon.Tests.Scripts.Nightly.NightlyRcChain("c1313-hops", "rc/2026-10-19"))
        {
            chain.Write("client/src/App.tsx", "baseline client");
            var b = chain.Commit("baseline");
            chain.Write("tests/Antiphon.Tests/Agents/CodexResponseAnalyzerTests.cs", "candidate provider");
            var a = chain.Commit("candidate");
            chain.Publish();
            var release = chain.Fixture.PathOf("releases λ");
            Directory.CreateDirectory(Path.Combine(release, "qualification"));
            File.WriteAllText(Path.Combine(release, "qualification", "claim.json"), "{\"status\":\"unqualified\"}");
            File.WriteAllText(Path.Combine(release, "future train.json"), "{\"fixture\":\"opaque input only\"}");
            var id = chain.CandidateId(a);
            var q = new System.Collections.Specialized.OrderedDictionary(StringComparer.OrdinalIgnoreCase)
            {
                ["RepositoryRoot"] = chain.Clone, ["CandidateSha"] = a, ["CandidateRef"] = chain.Ref, ["Repository"] = "fixture/repository", ["Profile"] = "rc",
                ["ReleaseRoot"] = release, ["CandidateId"] = id, ["RunId"] = "hop-run", ["SelectionMode"] = "Shadow", ["BaselineSha"] = b,
                ["BaselineReceiptPath"] = Path.Combine(release, "absent receipt.json"), ["QualificationPath"] = Path.Combine(release, "qualification", "claim.json"),
                ["TimingRoot"] = Path.Combine(release, "timing root"), ["TrainManifestPath"] = Path.Combine(release, "future train.json"),
                ["ReleaseIntentPath"] = Path.Combine(release, "future intent.json"), ["SelectionAttemptPath"] = "", ["SelectionAttemptDigest"] = "", ["SelectionPath"] = "",
                ["SelectionDigest"] = "", ["Attempt"] = 1,
            };
            var bound = Antiphon.Nightly.NightlyRun.SelectionWorker(Antiphon.Tests.Scripts.Nightly.NightlyRunWorld.StagedCli, Path.Combine(scripts, "lib"),
                Environment.GetEnvironmentVariable("PATH"))(q);
            // release-cut's $runArgs, through the launcher's translation (-Key value to --kebab-key value) to the command's options.
            var candidateRoot = Antiphon.Nightly.ReleaseGateCredit.CandidateRoot(release, id);
            var runArgs = new (string Key, string Value)[]
            {
                ("RunId", "hop-run"), ("Repository", "fixture/repository"), ("SelectionMode", "Shadow"), ("BaselineSha", bound.BaselineSha),
                ("BaselineReceiptPath", (string)q["BaselineReceiptPath"]!), ("QualificationPath", (string)q["QualificationPath"]!), ("TimingRoot", (string)q["TimingRoot"]!),
                ("TrainManifestPath", (string)q["TrainManifestPath"]!), ("ReleaseIntentPath", (string)q["ReleaseIntentPath"]!), ("SelectionAttemptPath", bound.SelectionAttemptPath),
                ("SelectionAttemptDigest", bound.SelectionAttemptDigest), ("SelectionPath", bound.SelectionPath), ("SelectionDigest", bound.SelectionDigest), ("Attempt", "1"),
                ("CheckoutRoot", chain.Clone), ("StateRoot", Path.Combine(candidateRoot, "state")), ("LogRoot", Path.Combine(candidateRoot, "logs")), ("Ref", chain.Ref),
                ("RemoteUrl", chain.Origin), ("Trigger", "rc"), ("Profile", "rc"), ("ExpectedSha", a), ("CandidateId", id), ("ReleaseRoot", release),
                ("CoordinationRoot", chain.Fixture.PathOf("coord")),
            };
            var options = Antiphon.Nightly.NightlyRunOptions.Parse(Antiphon.Tests.Scripts.Nightly.NightlyRunHops.LauncherFlags(runArgs.SelectMany(p => new[] { "-" + p.Key, p.Value }).ToArray()));
            var run = chain.Run(options);
            var summaries = Directory.GetFiles(Path.Combine(release, "candidates"), "summary.json", SearchOption.AllDirectories)
                .Select(f => System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(f)))
                .Where(n => n is System.Text.Json.Nodes.JsonObject o && o.ContainsKey("runId")).ToArray();
            var consumed = summaries.Length == 1 ? summaries[0]!["selection"] : null;
            string C(string name) => consumed?[name]?.GetValue<string>() ?? "";
            var train = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes((string)q["TrainManifestPath"]!))).ToLowerInvariant();
            rows.Check(consumed != null && C("baselineSha") == b && C("candidateSha") == a && C("runId") == "hop-run" && C("timingRoot") == (string)q["TimingRoot"]!
                && C("baselineReceiptPath") == (string)q["BaselineReceiptPath"]! && C("qualificationPath") == (string)q["QualificationPath"]!
                && C("releaseIntentPath") == (string)q["ReleaseIntentPath"]! && C("trainManifestPath") == (string)q["TrainManifestPath"]! && C("trainDigest") == train
                && C("selectionDigest") == bound.SelectionDigest && run.Phase == "hop" && chain.SelfHops.Count == 1 && chain.TestsHops.Count == 1,
                "C1313 hop-baseline-bound", "phase=" + run.Phase + " exit=" + run.ExitCode + " refusal=" + run.Refusal + " summaries=" + summaries.Length + " consumed="
                + consumed?.ToJsonString() + "\n" + chain.Fixture.Out + chain.Fixture.Error);
        }
        rows.ShouldAllPass(4, "C1313 hop-splat-keys-are-launcher-parameters", "C1313 hop-release-cut-reads-the-launcher-result",
            "C1313 hop-launcher-dot-sources-only-the-cli-launcher", "C1313 hop-baseline-bound");
    }

    // The AST of release-cut.ps1's $runArgs splat and its launcher call, and of the launcher's parameter block and dot-sources.
    private const string SplatProbe = """
        param([string]$Cut, [string]$Run)
        $ErrorActionPreference = 'Stop'
        $tokens = $null; $errors = $null
        $cutAst = [Management.Automation.Language.Parser]::ParseFile($Cut, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw ($errors | Out-String) }
        $assign = @($cutAst.FindAll({ param($n) $n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text -eq '$runArgs' }, $true))[0]
        $keys = @($assign.Right.Expression.KeyValuePairs | ForEach-Object { [string]$_.Item1.Value })
        $calls = @($cutAst.FindAll({ param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.Extent.Text -match "nightly-run\.ps1'\) @runArgs$" }, $true)).Count
        $runAst = [Management.Automation.Language.Parser]::ParseFile($Run, [ref]$tokens, [ref]$errors)
        if ($errors.Count) { throw ($errors | Out-String) }
        $params = @($runAst.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
        $dot = @($runAst.FindAll({ param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.InvocationOperator -eq 'Dot' }, $true) | ForEach-Object { $_.Extent.Text })
        [Console]::Out.WriteLine((ConvertTo-Json -Compress -Depth 4 -InputObject ([ordered]@{ keys = $keys; params = $params; calls = $calls; dot = $dot })))
        """;

    // CARD-1590: a direct run must not leave its nested fixture repositories behind,
    // on the failure path (no -FilterHost: fixture-discovery-failed after New-Fx) or on success.
    [Test]
    public Task C1590_FailedRunRemovesFixtureRoots() => RunOwnedRoot("C1313_ModeCredit", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldNotBe(0, output);
        output.ShouldContain("fixture-discovery-failed", Case.Sensitive, output);
        Directory.Exists(Path.Combine(root, "discovery")).ShouldBeTrue(output);
        AssertNoFixtureRepository(root, output);
    });

    // A dedicated passing case: one fixture plus one real Full workload in its release clone.
    [Test]
    public Task C1590_PassedRunRemovesFixtureRoots() => RunOwnedRoot("C1590_WorkloadPassed", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
        output.ShouldContain("PASS C1313 workload-fixture", Case.Sensitive, output);
        AssertNoFixtureRepository(root, output);
    });

    // CARD-1691 C8: a case that fails before New-Fx reaches the cleanup with no fixture root;
    // the caller's results directory and its contents must survive and the original error stays.
    [Test]
    public Task C1590_UnknownCaseKeepsResultsDirectory() => RunOwnedRoot("Bogus", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldNotBe(0, output);
        output.ShouldContain("Test-Bogus", Case.Sensitive, output);
        Directory.Exists(root).ShouldBeTrue(output);
        File.ReadAllText(Path.Combine(root, SentinelName)).ShouldBe(SentinelName, output);
    }, WriteSentinel);

    // CARD-1691 C4/C12: the first of two fixture roots is gone before the finally runs. The
    // passed run still exits 0, the failed run still reports its own error, and both remove the
    // second root.
    [Test]
    public Task C1590_PassedRunToleratesGoneFixtureRoot() => RunOwnedRoot("C1590_GoneRootPassed", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldBe(0, output);
        output.ShouldContain("C487 HARNESS EXIT CODE: 0", Case.Sensitive, output);
        output.ShouldContain("PASS C1313 gone-root-fixture", Case.Sensitive, output);
        AssertNoFixtureRepository(root, output);
    });

    [Test]
    public Task C1590_FailedRunToleratesGoneFixtureRoot() => RunOwnedRoot("C1590_GoneRootFailed", [], ScriptHarnessOptions.Default.ExecutionBudget, (result, root) =>
    {
        var output = result.Stdout + result.Stderr;
        result.ExitCode.ShouldNotBe(0, output);
        output.ShouldContain("c1590-original-failure", Case.Sensitive, output);
        AssertNoFixtureRepository(root, output);
    });

    private static readonly string[] FilterHostArguments =
        ["-FilterHost", Path.Combine(AppContext.BaseDirectory, "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll")];

    private const string SentinelName = "c1590-caller-sentinel.txt";

    // The caller owns the results directory before the harness starts, so a pre-existing file must survive.
    private static void WriteSentinel(string root)
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SentinelName), SentinelName);
    }

    private static void AssertNoFixtureRepository(string root, string output)
    {
        Directory.GetDirectories(root, "space *").ShouldBeEmpty(output);
        Directory.EnumerateFileSystemEntries(root, ".git", SearchOption.AllDirectories).ShouldBeEmpty(output);
    }

    // The caller-owned root is inspected after the harness exits and before ScriptHarness removes it.
    private static Task RunOwnedRoot(string name, string[] arguments, TimeSpan budget, Action<ScriptHarnessResult, string> validate,
        Action<string>? prepare = null)
    {
        string? root = null;
        var options = ScriptHarnessOptions.Default with
        {
            ExecutionBudget = budget,
            AdditionalArguments = arguments,
            OwnerFactory = request =>
            {
                root = request.ResultsDirectory;
                prepare?.Invoke(root);
                return OperatingSystem.IsWindows() ? new WindowsScriptHarnessProcess(request) :
                    OperatingSystem.IsLinux() ? new LinuxScriptHarnessProcess(request) :
                    throw new PlatformNotSupportedException("ScriptHarness requires Windows or Linux process ownership.");
            },
        };
        return ScriptHarnessProcess.RunAsync("test-change-aware-release.ps1", "C1313", name,
            Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "test-change-aware-release.ps1"), options,
            CancellationToken.None, result => validate(result, root!));
    }

    private Task Run(string name, int rows, params string[] labels) =>
        ScriptHarness.RunHarnessCaseAsync("test-change-aware-release.ps1", "C1313", name, rows,
            labels.Select(label => "C1313 " + label).ToArray(),
            HarnessOptions with
            {
                AdditionalArguments = FilterHostArguments,
                // CARD-1722: BaselineAuthority took 358 s on loaded server2 (2026-11-03,
                // n=1, provisional; formerly also the C1590 passed-run workload).
                // Investigate 5c10955b's five rc samples: median 263.002 s, sample p95
                // 294.207 s. Round 2x the loaded sample to 720 s. ParameterHops took
                // 183.8 s in Windows g120 (n=1, provisional); 600 s is the commissioned
                // minimum for host load. Retain the next ordinary Linux/Windows samples.
                ExecutionBudget = name switch
                {
                    "C1313_BaselineAuthority" => TimeSpan.FromSeconds(720),
                    "C1313_ParameterHops" => TimeSpan.FromSeconds(600),
                    _ => HarnessOptions.ExecutionBudget,
                },
            }, CancellationToken.None);
}
