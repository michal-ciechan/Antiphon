using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S15: the two implementations of the nightly tests run side by side, for this slice only (S17 deletes the
/// PowerShell and this class). One pwsh child runs Invoke-AntiphonNightlyTests (scripts/lib/nightly-tests-impl.ps1, still
/// production) on the C487 seams (Write-C487Seams / Write-C487NativePassSeams of scripts/lib/c487-harness.ps1) for every
/// scenario; then NightlyTests.Run runs each in-process on the twin world (NightlyTestsWorld). The result, the reasons, the
/// trace (launch order and argv, git calls) and every file under the log root must match once volatile values are folded:
/// each side's root, timestamps, measured seconds and milliseconds, 32-hex ids, 64-hex digests, the writer environment,
/// key order, line endings, PowerShell's method-call wrapper around a refusal and the one-element arrays the DockerInfo and
/// DiskFree seams leave in preflight. A launch is a launch only in the trace: the seams start nothing.
/// </summary>
[Category("Integration")]
[NotInParallel("QualificationTimingTests")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyTestsParityTests
{
    private const string RunId = "parity-run-01";
    private static readonly string Sha = NightlyTestsWorld.Sha;

    private static readonly NightlyTrxRow[] PassRows = [new("uid-1", "Ns.SampleClass", "SampleMethod")];
    private static readonly NightlyTrxRow[] FailRows = [new("uid-1", "Ns.SampleClass", "SampleMethod", "Failed")];
    private static readonly NightlyTestNode[] Nodes = [new("uid-1", "SampleClass", "SampleMethod", "Ns")];

    // The ToolchainVersions seam both sides answer (FakeHostProbe's defaults).
    private const string ToolchainSeam = "$NightlySeams.ToolchainVersions = { param($tool) if ($tool -eq 'git') { 'git version 2.42.0' } else { '10.0.204 [/fake/sdk]' } }";

    /// <summary>One scenario: its run arguments, its seams on both sides, and any setup of the two clones.</summary>
    private sealed class Scenario
    {
        public required string Name { get; init; }
        public Dictionary<string, object?> Args { get; } = new(StringComparer.OrdinalIgnoreCase);
        public NightlyTrxRow[]? Rows { get; init; }
        public bool OmitDiscovery { get; init; }
        public string Extra { get; init; } = "";
        public Func<NightlyLaunch, NightlyLaunchResult>? Behaviour { get; init; }
        public bool Npm { get; init; }
        public Action<NightlyTestsWorld>? Setup { get; init; }
        public Action<NightlyTestsWorld, Dictionary<string, object?>>? Bind { get; init; }
        public bool Compare { get; init; } = true;
        public string ExportHistory { get; init; } = "";
        public string ExportSeed { get; init; } = "";
        public NightlyTestsWorld? Ps { get; set; }
        public NightlyTestsWorld? Cs { get; set; }
    }

    [Test]
    [Timeout(1_500_000)]
    public async Task PowerShell_and_CSharp_runs_match_on_the_C487_seams(CancellationToken cancellationToken)
    {
        using var shared = NightlyFixture.Create("s15-parity");
        var history = shared.PathOf("history", "prior-failures.json");
        var seed = shared.PathOf("seed", "stopped-seed.json");
        Directory.CreateDirectory(Path.GetDirectoryName(history)!);
        Directory.CreateDirectory(Path.GetDirectoryName(seed)!);
        var scenarios = new List<Scenario>
        {
            new() { Name = "pass", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" } } },
            new() { Name = "failure", Rows = FailRows, Args = { ["Suites"] = new[] { "antiphon" } } },
            new() { Name = "rc-profile", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" }, ["Profile"] = "rc" } },
            new() { Name = "production-discovery", Rows = PassRows, OmitDiscovery = true, Args = { ["Suites"] = new[] { "antiphon" } } },
            // The history and the seed are exported from an rc run: the export reads the suite's profile discovery.
            // The seed export takes the run's timing root as timing-<run id> beside its log directory, as a production run lays them out.
            new()
            {
                Name = "prep", Rows = FailRows, Compare = false, ExportHistory = history, ExportSeed = seed, Args = { ["Suites"] = new[] { "antiphon" }, ["Profile"] = "rc" },
                Bind = (world, args) => args["TimingRoot"] = Path.Combine(Path.GetDirectoryName(world.Logs)!, "timing-parity-prep-01"),
            },
            new() { Name = "failed-first", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" }, ["Profile"] = "rc", ["PriorFailuresPath"] = history } },
            new()
            {
                Name = "stopped-seed", Rows = PassRows,
                Args = { ["Suites"] = new[] { "antiphon" }, ["Profile"] = "rc", ["PriorFailuresPath"] = seed, ["AcceptCrossCandidateSeed"] = true, ["SeedSourceCandidateId"] = "parity-source" },
            },
            new()
            {
                Name = "selection-attempt", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" }, ["Profile"] = "rc" },
                Bind = (world, args) => BindSelection(world, args),
            },
            new() { Name = "selection-whatif", Args = { ["Profile"] = "rc", ["WhatIf"] = true }, Bind = (world, args) => BindSelection(world, args) },
            new()
            {
                Name = "prerequisite-gate", Npm = true, Args = { ["Suites"] = new[] { "client" } },
                Extra = "$NightlySeams.StartProcess = { param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)\n"
                    + "    Add-Content -LiteralPath '__TRACE__' -Value (('EXEC {0} {1}' -f $FilePath, (($ArgumentList) -join ' '))) -Encoding ASCII\n"
                    + "    $code = 0; if ((@($ArgumentList) -join ' ') -match '(^| )run build($| )') { $code = 2 }\n"
                    + "    return @{ ExitCode = $code; TimedOut = $false; Pid = 2; ChildrenExited = $true; LogText = '' } }",
                Behaviour = launch => NightlyTestsWorld.Result(Regex.IsMatch(string.Join(" ", launch.Arguments), "(^| )run build($| )") ? 2 : 0, ""),
            },
            new()
            {
                Name = "toolchain-floor", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" } },
                Extra = "$NightlySeams.ToolchainVersions = { param($tool) if ($tool -eq 'git') { 'git version 2.42.0' } else { throw 'sdk-probe-down' } }",
                Setup = world =>
                {
                    Directory.CreateDirectory(Path.Combine(world.Clone, "docker"));
                    File.Copy(Path.Combine(NightlyPlanCases.Repo, "docker", "git-minimum-version.sh"), Path.Combine(world.Clone, "docker", "git-minimum-version.sh"), true);
                    File.Copy(Path.Combine(NightlyPlanCases.Repo, "global.json"), Path.Combine(world.Clone, "global.json"), true);
                    world.ProbeOverride = new ThrowingSdkProbe(world.Probe);
                },
            },
            new()
            {
                Name = "docker-preflight", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" } },
                Extra = "$NightlySeams.DockerInfo = { return @{ exitCode = 1; version = 'fake'; detail = 'down' } }",
                Setup = world => world.Probe.DockerRow = new NightlyProbeRow { ExitCode = 1, Version = "fake", Detail = "down" },
            },
            new() { Name = "whatif", Args = { ["Suites"] = new[] { "antiphon" }, ["WhatIf"] = true } },
            new() { Name = "unknown-suite", Args = { ["Suites"] = new[] { "typo-suite" } } },
        };

        try
        {
            foreach (var scenario in scenarios)
            {
                scenario.Ps = NewWorld(scenario, shared, "ps");
                scenario.Cs = NewWorld(scenario, shared, "cs");
            }
            var request = new JsonObject { ["scenarios"] = new JsonArray(scenarios.Select(PsScenario).ToArray()) };
            var requestPath = shared.PathOf("request.json");
            var resultPath = shared.PathOf("ps-results.json");
            File.WriteAllText(requestPath, request.ToJsonString());
            var runner = shared.PathOf("parity-runner.ps1");
            File.WriteAllText(runner, RunnerScript.Replace("__LIB__", Path.Combine(NightlyPlanCases.Repo, "scripts", "lib")));
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = shared.Root };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", runner, "-RequestPath", requestPath, "-ResultPath", resultPath })
                start.ArgumentList.Add(argument);
            var ran = await C1514Support.RunProcess(start, cancellationToken);
            File.Exists(resultPath).ShouldBeTrue("the PowerShell runner wrote no results (exit " + ran.ExitCode + ")\n" + ran.Text);
            using var psResults = JsonDocument.Parse(File.ReadAllText(resultPath));

            var rows = new NightlyCaseRows();
            // The failed-first and stopped-seed scenarios read a real history and seed (exported from the prep run), never the fallback.
            rows.Check(File.Exists(history) && File.Exists(seed), "S15 parity prep exported a history and a seed", psResults.RootElement.GetProperty("prep").GetRawText());
            foreach (var scenario in scenarios.Where(s => s.Compare))
            {
                var ps = psResults.RootElement.GetProperty(scenario.Name);
                var differences = new List<string>();
                if (!ps.GetProperty("ok").GetBoolean()) differences.Add("powershell threw: " + ps.GetProperty("error").GetString());
                var args = Arguments(scenario, scenario.Cs!);
                NightlyTestsResult? result = null;
                try
                {
                    var options = Options(args);
                    var host = scenario.Cs!.Host();
                    var bindings = NightlyTestsWorld.SeamedBindings();
                    result = NightlyTests.Run(options, host, bindings);
                }
                catch (Exception ex) { differences.Add("c# threw: " + ex.Message); }
                if (result != null && ps.GetProperty("ok").GetBoolean())
                {
                    CompareResult(ps.GetProperty("result"), result, scenario, differences);
                    CompareTrace(scenario, differences);
                    CompareLogRoot(scenario, differences);
                    // The intake copies an accepted history or seed into the log root; a fallback copies nothing.
                    if (scenario.Name is "failed-first" or "stopped-seed" && !File.Exists(Path.Combine(scenario.Cs!.Logs, "prior-failures.json")))
                        differences.Add("the C# run fell back instead of reading " + scenario.Args["PriorFailuresPath"]);
                }
                rows.Check(differences.Count == 0, "S15 parity " + scenario.Name, string.Join("\n  ", differences.Take(25)));
            }
            Console.WriteLine(ran.Text);
            rows.ShouldAllPass(scenarios.Count(s => s.Compare) + 1);
        }
        finally
        {
            foreach (var scenario in scenarios)
            {
                scenario.Ps?.Dispose();
                scenario.Cs?.Dispose();
            }
        }
    }

    /// <summary>
    /// The exact roster's refusals (a rebuilt assembly, an unclassified UID, an existing output, a member the discovery does
    /// not hold) from Invoke-NightlyExactRoster and NightlyTests.ExactRoster: the same reason, nothing launched, no receipt.
    /// A run that launches is not compared: the PowerShell starts the real test host (CheckpointProcessRunner), not a seam.
    /// </summary>
    [Test]
    [Timeout(600_000)]
    public async Task The_exact_roster_refuses_alike_in_both(CancellationToken cancellationToken)
    {
        using var fixture = NightlyFixture.Create("s15-exact-parity");
        var assembly = fixture.PathOf("bin", "Probe.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        File.WriteAllText(assembly, "exact roster assembly");
        var rebuilt = fixture.PathOf("bin", "Rebuilt.dll");
        File.WriteAllText(rebuilt, "a rebuilt assembly");
        var existing = fixture.PathOf("existing");
        Directory.CreateDirectory(existing);
        var diag = Path.Combine(NightlyDiagnosticCases.ProbeRoot, "discovery.diag");
        var cases = new (string Name, string Assembly, string Class, string Method, string[]? Classified, string Results)[]
        {
            ("rebuilt", rebuilt, "C487.Probe.Ordinary", "Plain", null, fixture.PathOf("out-rebuilt")),
            ("unclassified", assembly, "C487.Probe.Ordinary", "Plain", [], fixture.PathOf("out-unclassified")),
            ("existing", assembly, "C487.Probe.Ordinary", "Plain", null, existing),
            ("missing-member", assembly, "C487.Probe.Absent", "Plain", null, fixture.PathOf("out-missing")),
        };
        var script = new StringBuilder();
        script.AppendLine("$lib = '" + Path.Combine(NightlyPlanCases.Repo, "scripts", "lib") + "'");
        script.AppendLine(". (Join-Path $lib 'nightly-common.ps1'); . (Join-Path $lib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable");
        script.AppendLine(". (Join-Path $lib 'nightly-coverage.ps1'); . (Join-Path $lib 'release-gate.ps1'); . (Join-Path $lib 'nightly-tests-impl.ps1')");
        script.AppendLine("$nodes = @(([Antiphon.Nightly.NightlyDiagnosticLog]::Read('" + diag + "', 'discovery')).Nodes)");
        script.AppendLine("$doc = [Antiphon.Nightly.NightlyDiagnosticLog]::NewDiscoveryDocument($nodes, [Antiphon.Nightly.NightlyDiagnosticLog]::AssemblyHash('" + assembly + "'))");
        script.AppendLine("$out = [ordered]@{}");
        foreach (var c in cases)
        {
            var classified = c.Classified == null ? "$null" : "@()";
            script.AppendLine("try { $null = Invoke-NightlyExactRoster -AssemblyPath '" + c.Assembly + "' -Discovery $doc -Members @(@{ class = '" + c.Class + "'; method = '" + c.Method
                + "' }) -ResultsDirectory '" + c.Results + "' -ClassifiedUids " + classified + "; $out['" + c.Name + "'] = 'ok' } catch { $out['" + c.Name + "'] = $_.Exception.Message }");
        }
        var resultPath = fixture.PathOf("ps-exact.json");
        script.AppendLine("$out | ConvertTo-Json | Set-Content -LiteralPath '" + resultPath + "' -Encoding UTF8");
        var scriptPath = fixture.PathOf("exact.ps1");
        File.WriteAllText(scriptPath, script.ToString());
        var start = new ProcessStartInfo("pwsh") { WorkingDirectory = fixture.Root };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", scriptPath }) start.ArgumentList.Add(argument);
        var ran = await C1514Support.RunProcess(start, cancellationToken);
        File.Exists(resultPath).ShouldBeTrue(ran.Text);
        using var ps = JsonDocument.Parse(File.ReadAllText(resultPath));

        var nodes = NightlyDiagnosticCases.ReadNodes(diag, "discovery");
        var doc = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, NightlyDiagnosticLog.AssemblyHash(assembly));
        var launcher = new RecordingLauncher();
        var host = fixture.Host(launcher: launcher);
        var rows = new NightlyCaseRows();
        foreach (var c in cases)
        {
            var csharp = NightlyDiagnosticCases.Refusal(() => NightlyTests.ExactRoster(host, c.Assembly, doc, [new PlainObject(("class", c.Class), ("method", c.Method))], c.Results, c.Classified));
            var powershell = Unwrap(ps.RootElement.GetProperty(c.Name).GetString() ?? "");
            var receipt = File.Exists(Path.Combine(c.Results, "receipt.json"));
            rows.Check(csharp.Length > 0 && csharp == powershell && !receipt, "S15 exact roster parity " + c.Name, "c#=" + csharp + " ps=" + powershell + " receipt=" + receipt);
        }
        rows.Check(launcher.Launches.Count == 0, "S15 exact roster parity nothing launched", launcher.Launches.Count.ToString());
        rows.ShouldAllPass(5);
    }

    // ---- the worlds ----------------------------------------------------------------------------------------------

    private static NightlyTestsWorld NewWorld(Scenario scenario, NightlyFixture shared, string side)
    {
        NightlyTestsWorld world;
        if (scenario.Npm) world = NightlyTestsRunTests.NpmWorld(out _, out _);
        else world = new NightlyTestsWorld("s15-parity-" + side);
        if (scenario.Rows != null) world.Behaviour = NightlyTestsWorld.C487NativePass(scenario.Rows, Nodes, scenario.OmitDiscovery);
        if (scenario.Behaviour != null) world.Behaviour = scenario.Behaviour;
        scenario.Setup?.Invoke(world);
        return world;
    }

    private static void BindSelection(NightlyTestsWorld world, Dictionary<string, object?> args)
    {
        var request = NightlyTestsRunTests.SelectionFixture(world.Fixture, runId: RunId, timingRoot: Path.Combine(world.Logs, "timing-selection"));
        args["ReleaseRoot"] = request.ReleaseRoot;
        args["CandidateId"] = request.CandidateId;
        args["GitRef"] = request.CandidateRef;
        args["TimingRoot"] = request.TimingRoot;
        args["SelectionAttemptPath"] = request.SelectionAttemptPath;
        args["SelectionAttemptDigest"] = request.SelectionAttemptDigest;
        args["SelectionPath"] = request.SelectionPath;
        args["SelectionDigest"] = request.SelectionDigest;
    }

    /// <summary>Invoke-E's arguments for a world, then the scenario's.</summary>
    private static Dictionary<string, object?> Arguments(Scenario scenario, NightlyTestsWorld world)
    {
        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["RepoRoot"] = world.Clone, ["LogRoot"] = world.Logs, ["PolicyPath"] = world.PolicyPath, ["Sha"] = Sha, ["GitRef"] = "origin/master",
            ["Trigger"] = "scheduled", ["RunId"] = scenario.Name == "prep" ? "parity-prep-01" : RunId, ["AllowSharedTree"] = true,
        };
        foreach (var pair in scenario.Args) args[pair.Key] = pair.Value;
        scenario.Bind?.Invoke(world, args);
        Directory.CreateDirectory(world.Logs);
        return args;
    }

    private static NightlyTestsOptions Options(Dictionary<string, object?> args)
    {
        var options = new NightlyTestsOptions();
        foreach (var pair in args)
        {
            var property = typeof(NightlyTestsOptions).GetProperties().FirstOrDefault(p => string.Equals(p.Name, pair.Key, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("no option " + pair.Key);
            property.SetValue(options, pair.Value is int or bool or string[] ? pair.Value : Convert.ChangeType(pair.Value, property.PropertyType, System.Globalization.CultureInfo.InvariantCulture));
        }
        return options;
    }

    private static JsonObject PsScenario(Scenario scenario)
    {
        var world = scenario.Ps!;
        var args = Arguments(scenario, world);
        args["SeamsPath"] = world.Fixture.PathOf("seams.ps1");
        var jsonArgs = new JsonObject();
        foreach (var pair in args) jsonArgs[pair.Key] = JsonSerializer.SerializeToNode(pair.Value);
        var extra = ToolchainSeam + "\n" + scenario.Extra.Replace("__TRACE__", world.TracePath);
        var node = new JsonObject
        {
            ["name"] = scenario.Name,
            ["seams"] = world.Fixture.PathOf("seams.ps1"),
            ["trace"] = world.TracePath,
            ["native"] = scenario.Rows != null,
            ["omit"] = scenario.OmitDiscovery,
            ["extra"] = extra,
            ["args"] = jsonArgs,
            ["exportHistory"] = scenario.ExportHistory,
            ["exportSeed"] = scenario.ExportSeed,
        };
        if (scenario.Rows != null)
        {
            node["rows"] = new JsonArray(scenario.Rows.Select(r => (JsonNode)new JsonObject { ["Id"] = r.Id, ["ClassName"] = r.ClassName, ["MethodName"] = r.MethodName, ["Outcome"] = r.Outcome }).ToArray());
            node["nodes"] = new JsonArray(Nodes.Select(n => (JsonNode)new JsonObject { ["uid"] = n.Uid, ["type"] = n.Type, ["method"] = n.Method, ["namespace"] = n.Namespace }).ToArray());
        }
        if (scenario.Npm) node["path"] = world.Host().Variables("PATH");
        return node;
    }

    /// <summary>The probe of the toolchain scenario: Probe's answers, but the SDK version read throws (as the seam does).</summary>
    private sealed class ThrowingSdkProbe(FakeHostProbe inner) : INightlyHostProbe
    {
        public string Platform() => inner.Platform();
        public NightlyProbeRow Docker() => inner.Docker();
        public NightlyProbeRow DiskFree() => inner.DiskFree();
        public string ToolVersion(string tool) => tool == "git" ? inner.ToolVersion(tool) : throw new InvalidOperationException("sdk-probe-down");
    }

    // ---- the comparisons -----------------------------------------------------------------------------------------

    private static void CompareResult(JsonElement ps, NightlyTestsResult result, Scenario scenario, List<string> differences)
    {
        using var cs = JsonDocument.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions { Converters = { new PlainObjectConverter() } }));
        foreach (var property in ps.EnumerateObject())
        {
            var match = cs.RootElement.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (match.Value.ValueKind == JsonValueKind.Undefined) { differences.Add("result." + property.Name + " missing in C#"); continue; }
            var left = Fold(JsonNode.Parse(property.Value.GetRawText()), scenario.Ps!.Root);
            var right = Fold(JsonNode.Parse(match.Value.GetRawText()), scenario.Cs!.Root);
            if (property.Name.Equals("Error", StringComparison.OrdinalIgnoreCase) || property.Name.Equals("Refusal", StringComparison.OrdinalIgnoreCase))
                left = JsonValue.Create(Unwrap(left?.GetValue<string>() ?? ""));
            var a = Canonical(left);
            var b = Canonical(right);
            if (a != b) differences.Add("result." + property.Name + ": ps=" + Clip(a) + " c#=" + Clip(b));
        }
    }

    private static void CompareTrace(Scenario scenario, List<string> differences)
    {
        var ps = TraceLines(scenario.Ps!);
        var cs = TraceLines(scenario.Cs!);
        if (string.Join("\n", ps) == string.Join("\n", cs)) return;
        for (var i = 0; i < Math.Max(ps.Length, cs.Length); i++)
        {
            var a = i < ps.Length ? ps[i] : "<none>";
            var b = i < cs.Length ? cs[i] : "<none>";
            if (a != b) { differences.Add("trace line " + (i + 1) + ": ps=" + Clip(a) + " c#=" + Clip(b)); break; }
        }
    }

    private static string[] TraceLines(NightlyTestsWorld world) =>
        world.TraceText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => FoldText(l, world.Root)).ToArray();

    // Files the C# run does not write yet, by design: the active-chunk identity is the heartbeat's (W2-nightly S10), bound by a
    // caller (NightlyTestsBindings.ActiveChunk), and the PowerShell writes it from its own heartbeat library.
    private static readonly string[] NotCompared = ["active-chunk.json"];

    private static void CompareLogRoot(Scenario scenario, List<string> differences)
    {
        var ps = LogFiles(scenario.Ps!);
        var cs = LogFiles(scenario.Cs!);
        foreach (var name in ps.Keys.Except(cs.Keys)) differences.Add("file only in ps: " + name);
        foreach (var name in cs.Keys.Except(ps.Keys)) differences.Add("file only in c#: " + name);
        foreach (var name in ps.Keys.Intersect(cs.Keys))
        {
            var a = Content(ps[name], scenario.Ps!.Root);
            var b = Content(cs[name], scenario.Cs!.Root);
            if (a != b) differences.Add("file " + name + " differs: " + FirstDifference(a, b));
        }
    }

    private static Dictionary<string, string> LogFiles(NightlyTestsWorld world)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(world.Logs)) return files;
        foreach (var path in Directory.EnumerateFiles(world.Logs, "*", SearchOption.AllDirectories))
        {
            if (NotCompared.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)) continue;
            var relative = FoldText(Path.GetRelativePath(world.Logs, path).Replace('\\', '/'), world.Root);
            files[relative] = path;
        }
        return files;
    }

    private static string Content(string path, string root)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            try { return Canonical(Fold(JsonNode.Parse(text), root)); }
            catch (JsonException) { }
        }
        var lines = text.Split('\n');
        if (lines.Length > 1 && lines.Where(l => l.Length > 0).All(l => l.TrimStart().StartsWith('{')))
        {
            try { return string.Join("\n", lines.Where(l => l.Length > 0).Select(l => Canonical(Fold(JsonNode.Parse(l), root)))); }
            catch (JsonException) { }
        }
        return FoldText(text, root);
    }

    private static readonly Regex Stamp = new(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?(Z|[+-]\d\d:\d\d)?", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new("(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])", RegexOptions.CultureInvariant);
    private static readonly Regex Hex32 = new("(?<![0-9a-fA-F])[0-9a-fA-F]{32}(?![0-9a-fA-F])", RegexOptions.CultureInvariant);
    private static readonly Regex Wrapper = new("^Exception calling \"[^\"]+\" with \"\\d+\" argument\\(s\\): \"(.*)\"$", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex Volatile = new("(?i)(seconds|milliseconds|duration|elapsed|ticks|bytesRead|wallClock)", RegexOptions.CultureInvariant);

    private static string Unwrap(string message)
    {
        var m = Wrapper.Match(message);
        return m.Success ? m.Groups[1].Value : message;
    }

    // A measured number inside JSON text (a journal envelope's payload string): "name":<number>, the quotes escaped or not.
    private static readonly Regex EmbeddedVolatile = new(
        @"((?:\\u0022|\\""|"")[A-Za-z]*(?i:seconds|milliseconds|duration|elapsed|ticks)(?:\\u0022|\\""|"")\s*:\s*)-?[0-9][0-9.eE+-]*", RegexOptions.CultureInvariant);

    private static string FoldText(string text, string root)
    {
        text = text.Replace(root, "<ROOT>", StringComparison.OrdinalIgnoreCase).Replace(root.Replace("\\", "\\\\"), "<ROOT>", StringComparison.OrdinalIgnoreCase);
        text = EmbeddedVolatile.Replace(text, "${1}0");
        text = Stamp.Replace(text, "<TS>");
        text = Hex64.Replace(text, "<H64>");
        return Hex32.Replace(text, "<H32>");
    }

    private static JsonNode? Fold(JsonNode? node, string root, string key = "")
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var value = pair.Value;
                    // The DockerInfo and DiskFree seams answer through Invoke(): a one-element list where production writes the row.
                    if (pair.Key is "docker" or "disk" && value is JsonArray { Count: 1 } single) value = single[0];
                    copy[pair.Key] = pair.Key == "writerEnvironment" ? JsonValue.Create("<ENV>") : Fold(value?.DeepClone(), root, pair.Key);
                }
                return copy;
            }
            case JsonArray array:
                return new JsonArray(array.Select(item => Fold(item?.DeepClone(), root, key)).ToArray());
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(FoldText(Unwrap(text), root));
            case JsonValue value when Volatile.IsMatch(key) && value.GetValueKind() == JsonValueKind.Number:
                return JsonValue.Create(0);
            default:
                return node?.DeepClone();
        }
    }

    private static string Canonical(JsonNode? node) => node == null ? "null" : node.ToJsonString();

    private static string FirstDifference(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        var from = Math.Max(0, i - 60);
        return "at " + i + ": ps=..." + Clip(a.Substring(from, Math.Min(200, a.Length - from))) + " c#=..." + Clip(b.Substring(from, Math.Min(200, b.Length - from)));
    }

    private static string Clip(string text) => text.Length > 400 ? text[..400] + "..." : text;

    /// <summary>Writes a PlainObject (the selection document) as its named values.</summary>
    private sealed class PlainObjectConverter : System.Text.Json.Serialization.JsonConverter<PlainObject>
    {
        public override PlainObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, PlainObject value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            foreach (var entry in value.Entries)
            {
                writer.WritePropertyName(entry.Key);
                JsonSerializer.Serialize(writer, entry.Value, entry.Value?.GetType() ?? typeof(object), options);
            }
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// The PowerShell side: every scenario's seams (Write-C487NativePassSeams or Write-C487Seams, then the scenario's lines),
    /// its PATH, Invoke-AntiphonNightlyTests with its arguments, and the exports a later scenario reads.
    /// </summary>
    private const string RunnerScript = """
        param([string]$RequestPath, [string]$ResultPath)
        $ErrorActionPreference = 'Continue'
        $lib = '__LIB__'
        . (Join-Path $lib 'nightly-common.ps1')
        . (Join-Path $lib 'nightly-core.ps1'); $null = Test-NightlyCoreAvailable
        . (Join-Path $lib 'nightly-coverage.ps1')
        . (Join-Path $lib 'release-gate.ps1')
        . (Join-Path $lib 'release-selection.ps1')
        . (Join-Path $lib 'nightly-tests-impl.ps1')
        . (Join-Path $lib 'c487-harness.ps1')
        $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json -AsHashtable
        $results = [ordered]@{}
        foreach ($scenario in $request.scenarios) {
            $name = [string]$scenario.name
            $savedPath = $env:PATH
            try {
                if ($scenario.native) {
                    $rows = @($scenario.rows | ForEach-Object { @{ Id = $_.Id; ClassName = $_.ClassName; MethodName = $_.MethodName; Outcome = $_.Outcome } })
                    $nodes = @($scenario.nodes | ForEach-Object { @{ uid = $_.uid; type = $_.type; method = $_.method; namespace = $_.namespace } })
                    Write-C487NativePassSeams -Path $scenario.seams -TracePath $scenario.trace -Rows $rows -DiscoveryNodes $nodes -OmitDiscovery:([bool]$scenario.omit)
                } else {
                    Write-C487Seams -Path $scenario.seams -TracePath $scenario.trace
                }
                Add-Content -LiteralPath $scenario.seams -Value ([string]$scenario.extra) -Encoding ASCII
                if ($scenario.path) { $env:PATH = [string]$scenario.path }
                $invoke = @{}
                foreach ($key in $scenario.args.Keys) {
                    $value = $scenario.args[$key]
                    if ($key -eq 'Suites') { $value = [string[]]@($value) }
                    $invoke[$key] = $value
                }
                New-Item -ItemType Directory -Path $invoke.LogRoot -Force | Out-Null
                $result = Invoke-AntiphonNightlyTests @invoke
                $results[$name] = [ordered]@{ ok = $true; result = $result }
                if ($scenario.exportHistory) {
                    # The native seam's execution document names each node's type by its row's qualified class; the history
                    # export maps it to the discovery node, whose type is bare. Only this prep run's evidence is made to agree.
                    foreach ($diag in @(Get-ChildItem -LiteralPath $invoke.LogRoot -Recurse -File -Filter 'log.diag')) {
                        $doc = Get-Content -LiteralPath $diag.FullName -Raw | ConvertFrom-Json
                        if ($doc.format -ne 'antiphon-tunit-execution-v1') { continue }
                        foreach ($n in @($doc.nodes)) { $n.type = ([string]$n.type -split '\.')[-1] }
                        $doc | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $diag.FullName -Encoding UTF8
                    }
                }
                if ($scenario.exportHistory) { $null = [Antiphon.Nightly.PriorFailureHistory]::Export($invoke.LogRoot, [string]$scenario.exportHistory) }
                if ($scenario.exportSeed) { $null = [Antiphon.Nightly.StoppedSeedFiles]::Export($invoke.LogRoot, [string]$scenario.exportSeed, [string]$invoke.RunId, 'parity-source', [string]$invoke.Sha, [string]$invoke.TimingRoot) }
            } catch {
                $results[$name] = [ordered]@{ ok = $false; error = (Get-NightlyRefusalText $_) }
            } finally {
                $env:PATH = $savedPath
            }
        }
        $results | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
        """;
}
