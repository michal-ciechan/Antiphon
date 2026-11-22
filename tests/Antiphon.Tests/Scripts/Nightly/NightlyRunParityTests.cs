using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S20: the nightly run's two production paths side by side, on fixture roots only (no seams: they never cross a
/// process). One is the Windmill entry, scripts/nightly-run.ps1 under Windows PowerShell 5.1 (pwsh elsewhere) with the staged
/// antiphon-scripts build (ANTIPHON_SCRIPTS_DLL), and its -PassThru under pwsh as release-cut.ps1 calls it; the other is
/// NightlyRun.Run in this process on the production host and NightlyRunBindings.Production over the same staged CLI. Each
/// scenario gets a twin fixture per side. Compared: the exit code, the last stdout line (the completion record), the
/// -PassThru object against the result's run-core record, and every file under the state, coordination, release and log
/// roots (folded: each side's root, timestamps, 64-hex digests and the stamp of a run directory). The S19 class compared the
/// deleted PowerShell run core with the C# on the C487 seams; HEAD's PowerShell was compared once more with the launcher
/// before it was deleted (the S20 parity run).
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyRunParityTests
{
    private sealed class Scenario
    {
        public required string Name { get; init; }
        public bool Prepared { get; init; } = true;
        public bool Rc { get; init; }
        public required Func<Side, IReadOnlyList<(string Name, string? Value)>> Parameters { get; init; }
        public Action<Side>? Setup { get; init; }
    }

    /// <summary>One side's fixture: the roots, an origin and (prepared) an owned clone of it.</summary>
    private sealed class Side : IDisposable
    {
        public Side(string prefix, string origin, bool prepared)
        {
            Fixture = NightlyFixture.Create(prefix);
            Origin = origin;
            Directory.CreateDirectory(State);
            if (!prepared) return;
            NightlyRcChain.Git(Fixture.Root, "clone", "-q", origin, Clone);
            NightlyRcChain.Git(Clone, "config", "--local", "core.longpaths", "true");
            File.WriteAllText(Path.Combine(Clone, ".antiphon-nightly-owned"), "{\"origin\":" + JsonSerializer.Serialize(origin) + ",\"kind\":\"nightly-clone\"}");
        }

        public NightlyFixture Fixture { get; }
        public string Root => Fixture.Root;
        public string Origin { get; }
        public string State => Fixture.PathOf("state");
        public string Logs => Fixture.PathOf("logs");
        public string Coord => Fixture.PathOf("coord");
        public string Releases => Fixture.PathOf("releases");
        public string Clone => Fixture.PathOf("checkout");
        public string Sha => NightlyRcChain.Git(Clone, "rev-parse", "HEAD").Trim();
        public void Dispose() => Fixture.Dispose();
    }

    [Test, Timeout(900_000)]
    public async Task Launcher_and_in_process_runs_match_on_fixture_roots(CancellationToken cancellationToken)
    {
        using var origins = NightlyFixture.Create("s20-parity-origins");
        var plain = Origin(origins, "plain", seed => File.WriteAllText(Path.Combine(seed, "readme.txt"), "fixture\n"));
        var rc = Origin(origins, "rc", seed =>
        {
            foreach (var file in new[] { "global.json", "tests/test-execution-policy.json", "tests/test-selection-policy.json", "scripts/select-test-rosters.ps1", "scripts/lib/test-selection.ps1" })
            {
                var target = Path.Combine(seed, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(NightlyPlanCases.Repo, file.Replace('/', Path.DirectorySeparatorChar)), target, true);
            }
        });
        var rcSha = NightlyRcChain.Git(rc, "rev-parse", "HEAD").Trim();
        using var me = Process.GetCurrentProcess();
        var meStart = me.StartTime.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        var scenarios = new List<(Scenario Scenario, string Origin)>
        {
            (new Scenario { Name = "tests-refused", Parameters = s => [("-NoSync", null), ("-NoReport", null), ("-Trigger", "manual")] }, plain),
            (new Scenario { Name = "whatif", Parameters = s => [("-NoSync", null), ("-WhatIf", null), ("-Trigger", "manual")] }, plain),
            (new Scenario
            {
                Name = "shared-lock-refusal", Parameters = s => [("-NoSync", null), ("-NoReport", null)],
                Setup = s =>
                {
                    Directory.CreateDirectory(s.Coord);
                    File.WriteAllText(Path.Combine(s.Coord, "native-run.lock"), "{\"pid\":" + Environment.ProcessId + ",\"processStartedAt\":\"" + meStart
                        + "\",\"runId\":\"other\",\"lane\":\"master\",\"continuationId\":\"\",\"acquiredAt\":\"" + meStart + "\"}");
                },
            }, plain),
            (new Scenario { Name = "candidate-moved", Prepared = false, Parameters = s => [("-NoReport", null), ("-Trigger", "manual"), ("-ExpectedSha", new string('b', 40))] }, plain),
            (new Scenario
            {
                Name = "rc-release-root-required",
                Parameters = s => [("-NoSync", null), ("-WhatIf", null), ("-Profile", "rc"), ("-Trigger", "rc"), ("-Ref", "rc/2026-11-30"), ("-ExpectedSha", new string('a', 40))],
            }, plain),
            (new Scenario
            {
                Name = "rc-whatif-selection", Rc = true,
                Parameters = s => [("-NoSync", null), ("-WhatIf", null), ("-Profile", "rc"), ("-Trigger", "rc"), ("-Ref", "rc/2026-11-30"), ("-ExpectedSha", rcSha),
                    ("-ReleaseRoot", s.Releases), ("-Repository", "fixture/repository")],
            }, rc),
            (new Scenario { Name = "unknown-profile", Parameters = s => [("-NoSync", null), ("-Profile", "turbo")] }, plain),
        };

        var rows = new NightlyCaseRows();
        foreach (var (scenario, origin) in scenarios)
        {
            var differences = new List<string>();
            using var launched = NewSide(scenario, origin, "launcher");
            using var inProcess = NewSide(scenario, origin, "in-process");
            var direct = await Launch(scenario, launched, pwsh: false, cancellationToken);
            var (result, last) = RunInProcess(scenario, inProcess);
            if (direct.ExitCode != result.ExitCode) differences.Add("exit: launcher=" + direct.ExitCode + " in-process=" + result.ExitCode);
            var launchedLast = direct.Stdout.Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1];
            Compare("last line", Canonical(launchedLast, launched.Root), Canonical(last, inProcess.Root), differences);
            CompareFiles(launched, inProcess, differences);
            using var passLaunched = NewSide(scenario, origin, "passthru");
            var pass = await Launch(scenario, passLaunched, pwsh: true, cancellationToken);
            Compare("passthru", Canonical(pass.PassThru ?? "null", passLaunched.Root), Canonical(NightlyJson.Text(result.ToRecord(), 32) ?? "{}", inProcess.Root), differences);
            if (differences.Count > 0) differences.Add("launcher stderr: " + direct.Stderr.Trim());
            rows.Check(differences.Count == 0, "S20 parity " + scenario.Name, string.Join("\n  ", differences.Take(20)));
        }
        rows.ShouldAllPass(scenarios.Count);
    }

    // ---- the sides -----------------------------------------------------------------------------------------------

    private static string Origin(NightlyFixture origins, string name, Action<string> fill)
    {
        var seed = origins.PathOf("seed-" + name);
        Directory.CreateDirectory(seed);
        NightlyRcChain.Git(seed, "init", "-q", "-b", "master");
        NightlyRcChain.Git(seed, "config", "core.autocrlf", "false");
        fill(seed);
        NightlyRcChain.Git(seed, "add", "--all");
        NightlyRcChain.Git(seed, "-c", "user.name=S20 parity", "-c", "user.email=s20@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "seed " + name);
        var origin = origins.PathOf("origin-" + name + ".git");
        NightlyRcChain.Git(origins.Root, "clone", "-q", "--bare", seed, origin);
        return origin;
    }

    private static Side NewSide(Scenario scenario, string origin, string side)
    {
        var s = new Side("s20-parity-" + side, origin, scenario.Prepared);
        scenario.Setup?.Invoke(s);
        return s;
    }

    /// <summary>The scenario's launcher parameters: the side's roots, a fixed run id, then the scenario's own.</summary>
    private static List<(string Name, string? Value)> Parameters(Scenario scenario, Side s)
    {
        var list = new List<(string Name, string? Value)>
        {
            ("-CheckoutRoot", s.Clone), ("-StateRoot", s.State), ("-LogRoot", s.Logs), ("-CoordinationRoot", s.Coord), ("-RemoteUrl", s.Origin), ("-RunId", "s20-" + scenario.Name),
        };
        list.AddRange(scenario.Parameters(s));
        return list;
    }

    private sealed record Launched(int ExitCode, string Stdout, string Stderr, string? PassThru);

    /// <summary>The launcher: -File directly (5.1), or -PassThru from a pwsh caller that splats the same parameters (release-cut's way).</summary>
    private static async Task<Launched> Launch(Scenario scenario, Side s, bool pwsh, CancellationToken token)
    {
        var entry = Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1");
        var parameters = Parameters(scenario, s);
        var start = new ProcessStartInfo(pwsh || !OperatingSystem.IsWindows() ? "pwsh" : "powershell.exe") { WorkingDirectory = s.Root };
        start.Environment["ANTIPHON_SCRIPTS_DLL"] = NightlyRunWorld.StagedCli;
        // The shadow evidence recorder is off on both sides (the in-process side binds an off recorder).
        start.Environment["ANTIPHON_RC_RUN_EVIDENCE"] = "off";
        var passThru = s.Fixture.PathOf("passthru.json");
        if (!pwsh)
        {
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", entry }) start.ArgumentList.Add(argument);
            foreach (var (name, value) in parameters)
            {
                start.ArgumentList.Add(name);
                if (value != null) start.ArgumentList.Add(value);
            }
        }
        else
        {
            var splat = new JsonObject();
            foreach (var (name, value) in parameters) splat[name.TrimStart('-')] = value == null ? JsonValue.Create(true) : JsonValue.Create(value);
            var splatPath = s.Fixture.PathOf("splat.json");
            File.WriteAllText(splatPath, splat.ToJsonString());
            var driver = s.Fixture.PathOf("passthru.ps1");
            File.WriteAllText(driver, """
                param([string]$Entry, [string]$SplatPath, [string]$Out)
                $s = Get-Content -LiteralPath $SplatPath -Raw | ConvertFrom-Json -AsHashtable
                $invoke = @{}
                foreach ($k in $s.Keys) { $v = $s[$k]; if ($v -is [bool]) { $v = [switch]$v }; $invoke[$k] = $v }
                $r = & $Entry @invoke -PassThru
                [IO.File]::WriteAllText($Out, (ConvertTo-Json -InputObject $r -Depth 32))
                exit 0
                """);
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", driver, "-Entry", entry, "-SplatPath", splatPath, "-Out", passThru })
                start.ArgumentList.Add(argument);
        }
        start.UseShellExecute = false;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("the launcher shell did not start");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return new Launched(process.ExitCode, await stdout, await stderr, File.Exists(passThru) ? File.ReadAllText(passThru) : null);
    }

    /// <summary>NightlyRun.Run in this process: the production host and bindings over the staged CLI, the launcher's options.</summary>
    private static (NightlyRunResult Result, string Last) RunInProcess(Scenario scenario, Side s)
    {
        var flags = new List<string>();
        foreach (var (name, value) in Parameters(scenario, s))
        {
            flags.Add(NightlyRunHops.Kebab(name));
            if (value != null) flags.Add(value);
        }
        var options = NightlyRunOptions.Parse(flags);
        var output = new StringWriter();
        var host = NightlyHost.Production(output, new StringWriter());
        var bindings = NightlyRunBindings.Production(host, NightlyRunWorld.StagedCli);
        bindings.RcEvidence = new RcRunEvidenceRecorder("off", false, false, null);
        var result = NightlyRun.Run(options, host, bindings);
        return (result, output.ToString().Replace("\r\n", "\n").TrimEnd('\n').Split('\n')[^1]);
    }

    // ---- the comparisons -----------------------------------------------------------------------------------------

    private static void Compare(string kind, string launcher, string inProcess, List<string> differences)
    {
        if (launcher != inProcess) differences.Add(kind + "\n    launcher=" + Clip(launcher) + "\n    in-process=" + Clip(inProcess));
    }

    private static void CompareFiles(Side a, Side b, List<string> differences)
    {
        var left = Files(a);
        var right = Files(b);
        foreach (var name in left.Keys.Except(right.Keys)) differences.Add("file only from the launcher: " + name);
        foreach (var name in right.Keys.Except(left.Keys)) differences.Add("file only in-process: " + name);
        foreach (var name in left.Keys.Intersect(right.Keys))
            if (left[name] != right[name]) differences.Add("file " + name + "\n    launcher=" + Clip(left[name]) + "\n    in-process=" + Clip(right[name]));
    }

    private static Dictionary<string, string> Files(Side s)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in new[] { s.State, s.Coord, s.Logs, s.Releases })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(path);
                var trimmed = text.TrimStart();
                files[Fold(Path.GetRelativePath(s.Root, path).Replace('\\', '/'), s.Root)] =
                    trimmed.StartsWith('{') || trimmed.StartsWith('[') ? Canonical(text, s.Root) : Fold(text, s.Root);
            }
        }
        return files;
    }

    private static readonly Regex Stamp = new(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?(Z|[+-]\d\d:\d\d)?", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new("(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])", RegexOptions.CultureInvariant);
    private static readonly Regex RunDirectory = new(@"\d{4}-\d\d-\d\d-\d{4}-", RegexOptions.CultureInvariant);

    private static string Fold(string text, string root)
    {
        foreach (var form in new[] { root, root.Replace("\\", "\\\\"), root.Replace('\\', '/') })
            text = text.Replace(form, "<ROOT>", StringComparison.OrdinalIgnoreCase);
        return RunDirectory.Replace(Hex64.Replace(Stamp.Replace(text.Replace("\r\n", "\n"), "<TS>"), "<H64>"), "<STAMP>-");
    }

    private static string Canonical(string json, string root)
    {
        JsonNode? node;
        try { node = JsonNode.Parse(json); }
        catch (JsonException) { return Fold(json, root); }
        return Fold(Sorted(node), root);
    }

    private static string Sorted(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Key) + ":" + Sorted(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Sorted)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    private static string Clip(string text) => text.Length > 600 ? text[..600] + "..." : text;
}
