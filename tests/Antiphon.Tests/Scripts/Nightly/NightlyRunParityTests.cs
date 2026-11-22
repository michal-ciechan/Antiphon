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
/// W2-nightly S19: the two implementations of the nightly run side by side, for this slice only (S20 deletes the PowerShell
/// and this class). One pwsh child runs the production entry scripts/nightly-run.ps1 (-PassThru, its Write-Host lines
/// captured) on the C487 seams (Write-C487Seams of scripts/lib/c487-harness.ps1 plus each scenario's lines) for every
/// scenario; then NightlyRun.Run runs each in-process on a twin NightlyRunWorld. Compared: the result object and its JSON, the
/// last stdout line (the completion record), the exit code, the launches (the tests hop, the self hop and the report), the
/// git calls, and every file under the state, coordination and release roots and the clone (not its .git). Folded: each
/// side's root, timestamps, the continuing pid, and the hop's spelling: the PowerShell tests hop is the StartProcess seam's
/// antiphon-scripts nightly-tests call (W2-nightly S17: the command, never the selection hop), the C# one
/// `dotnet exec &lt;cli&gt; nightly-tests`, both with --kebab flags and compared as the nightly-tests options they parse to; the
/// self hop drops the PowerShell's -SeamsPath (seams never cross a process in C#); the report is the StartProcess seam's
/// antiphon-scripts nightly-report call on one side and the report binding's call on the other.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyRunParityTests
{
    private const string UtcNow = "2026-09-17T23:35:00Z";
    private const string RcRef = "rc/2026-11-30";
    private const string LivePid = "999999";
    private const string OwnerStart = "2026-09-01T06:00:00.0000000Z";

    private static readonly string Green = NightlyRunWorld.C545Summary(true);
    private static readonly string Red = NightlyRunWorld.C545Summary(false);

    private const string EolLine = "i/lf    w/crlf  attr/text eol=lf \tsel.ps1\n";

    /// <summary>One scenario: the run's options, the seams on both sides and any setup of the two worlds.</summary>
    private sealed class Scenario
    {
        public required string Name { get; init; }
        public Action<NightlyRunOptions, NightlyRunWorld>? Args { get; init; }
        public string? Summary { get; init; }
        public int TestExit { get; init; }
        public int ReportExit { get; init; }
        public int GitExit { get; init; }
        public bool Eol { get; init; }
        public bool Rc { get; init; }
        public bool Hop { get; init; }
        public Action<NightlyRunWorld>? Setup { get; init; }
        public NightlyRunWorld? Ps { get; set; }
        public NightlyRunWorld? Cs { get; set; }
        public string Sha { get; set; } = NightlyRunWorld.Sha;
    }

    [Test]
    [Timeout(900_000)]
    public async Task PowerShell_and_CSharp_runs_match_on_the_C487_seams(CancellationToken cancellationToken)
    {
        var scenarios = new List<Scenario>
        {
            new() { Name = "green", Summary = Green, Args = (o, _) => { o.NoSync = true; o.NoReport = false; } },
            new() { Name = "red-tests", Summary = Green, TestExit = 2, Args = (o, _) => { o.NoSync = true; o.NoReport = false; o.Suites = ["client"]; } },
            new() { Name = "report-failure", Summary = Green, ReportExit = 3, Args = (o, _) => { o.NoSync = true; o.NoReport = false; } },
            new() { Name = "no-summary", TestExit = 0, Args = (o, _) => { o.NoSync = true; o.Attempt = 2; } },
            new()
            {
                Name = "lock-refusal", Summary = Green, Args = (o, _) => o.NoSync = true,
                Setup = w => File.WriteAllText(Path.Combine(w.State, "run.lock"), "{\"pid\":" + LivePid + ",\"startedAt\":\"" + OwnerStart + "\",\"runId\":\"live1\"}"),
            },
            new()
            {
                Name = "shared-lock-refusal", Summary = Green, Args = (o, _) => o.NoSync = true,
                Setup = w =>
                {
                    Directory.CreateDirectory(w.Coordination);
                    File.WriteAllText(Path.Combine(w.Coordination, "native-run.lock"), "{\"pid\":" + LivePid + ",\"processStartedAt\":\"" + OwnerStart
                        + "\",\"runId\":\"other\",\"lane\":\"master\",\"continuationId\":\"\",\"acquiredAt\":\"" + OwnerStart + "\"}");
                },
            },
            new()
            {
                Name = "continuation", Summary = Green,
                Args = (o, _) => { o.NoSync = true; o.ContinueRunId = "parity-continuation"; o.ContinueParentPid = int.Parse(LivePid); o.ContinueParentStartedAt = OwnerStart; },
                Setup = w =>
                {
                    File.WriteAllText(Path.Combine(w.State, "run.lock"), "{\"pid\":" + LivePid + ",\"processStartedAt\":\"" + OwnerStart + "\",\"startedAt\":\"" + OwnerStart
                        + "\",\"runId\":\"parity-continuation\",\"parentPid\":0,\"parentStartedAt\":null}");
                    Directory.CreateDirectory(w.Coordination);
                    File.WriteAllText(Path.Combine(w.Coordination, "native-run.lock"), "{\"pid\":" + LivePid + ",\"processStartedAt\":\"" + OwnerStart
                        + "\",\"runId\":\"parity-continuation\",\"lane\":\"master\",\"continuationId\":\"\",\"acquiredAt\":\"" + OwnerStart + "\"}");
                },
            },
            new() { Name = "sync-failure", Summary = Green, GitExit = 1 },
            new() { Name = "candidate-moved", Summary = Green, Args = (o, _) => o.ExpectedSha = new string('b', 40) },
            new()
            {
                Name = "eol-repair", Summary = Green, Eol = true,
                Setup = w => File.WriteAllText(Path.Combine(w.Clone, "sel.ps1"), "a\r\nb\r\n"),
            },
            new() { Name = "whatif", Summary = Green, Args = (o, _) => { o.WhatIf = true; o.Trigger = "manual"; } },
            new()
            {
                Name = "product-root-hop", Summary = Green, Hop = true, Args = (o, _) => { o.Suites = ["client"]; o.NoReport = false; },
                Setup = w =>
                {
                    Directory.CreateDirectory(Path.Combine(w.Clone, "scripts"));
                    File.WriteAllText(Path.Combine(w.Clone, "scripts", "nightly-run.ps1"), "# the clone's launcher\n");
                },
            },
            new()
            {
                Name = "rc-expected-sha", Rc = true, Summary = Green,
                Args = (o, w) => { o.NoSync = true; o.WhatIf = true; o.Profile = "rc"; o.Trigger = "rc"; o.Ref = RcRef; o.ReleaseRoot = w.Releases; },
            },
            new()
            {
                Name = "selection-hop", Rc = true, Summary = Green,
                Args = (o, w) => { o.NoSync = true; o.Profile = "rc"; o.Trigger = "rc"; o.Ref = RcRef; o.ReleaseRoot = w.Releases; },
            },
        };

        using var shared = NightlyFixture.Create("s19-parity");
        try
        {
            foreach (var scenario in scenarios)
            {
                scenario.Ps = NewWorld(scenario, "ps");
                scenario.Cs = NewWorld(scenario, "cs");
            }
            var request = new JsonObject { ["scenarios"] = new JsonArray(scenarios.Select(PsScenario).ToArray()) };
            var requestPath = shared.PathOf("request.json");
            var resultPath = shared.PathOf("ps-results.json");
            File.WriteAllText(requestPath, request.ToJsonString());
            var runner = shared.PathOf("parity-runner.ps1");
            File.WriteAllText(runner, RunnerScript.Replace("__REPO__", NightlyPlanCases.Repo));
            var start = new ProcessStartInfo("pwsh") { WorkingDirectory = shared.Root };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", runner, "-RequestPath", requestPath, "-ResultPath", resultPath })
                start.ArgumentList.Add(argument);
            var ran = await C1514Support.RunProcess(start, cancellationToken);
            File.Exists(resultPath).ShouldBeTrue("the PowerShell runner wrote no results (exit " + ran.ExitCode + ")\n" + ran.Text);
            using var psResults = JsonDocument.Parse(File.ReadAllText(resultPath));

            var rows = new NightlyCaseRows();
            foreach (var scenario in scenarios)
            {
                var ps = psResults.RootElement.GetProperty(scenario.Name);
                var differences = new List<string>();
                if (!ps.GetProperty("ok").GetBoolean()) differences.Add("powershell threw: " + ps.GetProperty("error").GetString());
                NightlyRunResult? result = null;
                var cs = scenario.Cs!;
                try { result = NightlyRun.Run(Options(scenario, cs), cs.Host(), cs.Bindings()); }
                catch (Exception ex) { differences.Add("c# threw: " + ex); }
                if (result != null && ps.GetProperty("ok").GetBoolean())
                {
                    CompareResult(ps.GetProperty("result"), result, scenario, differences);
                    var psLast = FoldText(ps.GetProperty("last").GetString() ?? "", scenario.Ps!.Root);
                    var csLast = FoldText(cs.OutLines[^1], cs.Root);
                    if (psLast != csLast) differences.Add("last line: ps=" + Clip(psLast) + " c#=" + Clip(csLast));
                    var psExit = ps.GetProperty("exit").GetInt32();
                    if (psExit != result.ExitCode) differences.Add("exit: ps=" + psExit + " c#=" + result.ExitCode);
                    CompareLines("launch", PsLaunches(scenario.Ps!), CsLaunches(cs), differences);
                    CompareLines("git", GitLines(scenario.Ps!), GitLines(cs), differences);
                    CompareFiles(scenario, differences);
                }
                rows.Check(differences.Count == 0, "S19 parity " + scenario.Name, string.Join("\n  ", differences.Take(25)));
            }
            Console.WriteLine(ran.Text);
            rows.ShouldAllPass(scenarios.Count);
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

    // ---- the worlds ----------------------------------------------------------------------------------------------

    private static NightlyRunWorld NewWorld(Scenario scenario, string side)
    {
        var world = new NightlyRunWorld("s19-parity-" + side) { Clock = NightlyRunWorld.At(UtcNow), ReportExit = scenario.ReportExit };
        scenario.Setup?.Invoke(world);
        if (scenario.Rc) scenario.Sha = RcRepository(world);
        world.Git = new FakeGit(scenario.Sha, scenario.GitExit);
        if (scenario.Eol) world.Git.Reply("ls-files", new NightlyGitResult(0, EolLine));
        world.Behaviour = scenario.Summary == null
            ? (launch => NightlyTestsWorld.Result(launch.Arguments.Contains("nightly-tests") ? scenario.TestExit : 0))
            : NightlyRunWorld.TestsHop(scenario.Summary, scenario.TestExit);
        if (scenario.Hop) world.ProductRoot = NightlyPlanCases.Repo;
        return world;
    }

    /// <summary>The C2044 rc fixture: the selection inputs committed in the clone (fixed dates, so both worlds share one sha).</summary>
    private static string RcRepository(NightlyRunWorld world)
    {
        foreach (var file in new[] { "global.json", "tests/test-execution-policy.json", "tests/test-selection-policy.json", "scripts/select-test-rosters.ps1", "scripts/lib/test-selection.ps1" })
        {
            var target = Path.Combine(world.Clone, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(NightlyPlanCases.Repo, file.Replace('/', Path.DirectorySeparatorChar)), target, true);
        }
        NightlyRunWorld.FixtureGit(world.Clone, "init", "-q");
        NightlyRunWorld.FixtureGit(world.Clone, "add", "--", "global.json", "tests/test-execution-policy.json", "tests/test-selection-policy.json", "scripts/select-test-rosters.ps1",
            "scripts/lib/test-selection.ps1");
        NightlyRunWorld.FixtureGit(world.Clone, "-c", "user.name=C2044", "-c", "user.email=c2044@example.invalid", "-c", "core.hooksPath=.git/no-hooks", "-c", "commit.gpgsign=false",
            "commit", "-qm", "rc fixture selection inputs");
        return NightlyRunWorld.FixtureGit(world.Clone, "rev-parse", "HEAD").Trim();
    }

    /// <summary>The scenario's options on a world: Invoke-FxRun's defaults, a fixed run id, then the scenario's.</summary>
    private static NightlyRunOptions Options(Scenario scenario, NightlyRunWorld world)
    {
        var options = world.Options(o => o.RunId = "parity-" + scenario.Name);
        scenario.Args?.Invoke(options, world);
        if (scenario.Rc) options.ExpectedSha = scenario.Sha;
        return options;
    }

    private static JsonObject PsScenario(Scenario scenario)
    {
        var world = scenario.Ps!;
        var options = Options(scenario, world);
        var args = new JsonObject();
        var defaults = new NightlyRunOptions();
        foreach (var property in typeof(NightlyRunOptions).GetProperties())
        {
            if (property.Name == nameof(NightlyRunOptions.ResultFile)) continue;
            var value = property.GetValue(options);
            if (Equals(JsonSerializer.Serialize(value), JsonSerializer.Serialize(property.GetValue(defaults))) && property.Name != nameof(NightlyRunOptions.StateRoot)) continue;
            args[property.Name] = JsonSerializer.SerializeToNode(value);
        }
        var seams = Path.Combine(world.Root, "seams.ps1");
        args["SeamsPath"] = seams;
        var extra = SeamsExtra
            .Replace("__TRACE__", world.TracePath)
            .Replace("__LAUNCHES__", Path.Combine(world.Root, "launches.jsonl"))
            .Replace("__REPORT__", scenario.ReportExit.ToString())
            .Replace("__TESTEXIT__", scenario.TestExit.ToString())
            .Replace("__SUMMARY__", scenario.Summary ?? "")
            .Replace("__UTCNOW__", UtcNow);
        if (scenario.Eol) extra += "\n" + EolGitSeam.Replace("__TRACE__", world.TracePath).Replace("__SHA__", scenario.Sha);
        return new JsonObject
        {
            ["name"] = scenario.Name, ["seams"] = seams, ["trace"] = world.TracePath, ["sha"] = scenario.Sha, ["gitExit"] = scenario.GitExit, ["extra"] = extra, ["args"] = args,
        };
    }

    // ---- the comparisons -----------------------------------------------------------------------------------------

    private static void CompareResult(JsonElement ps, NightlyRunResult result, Scenario scenario, List<string> differences)
    {
        var json = NightlyJson.Text(result.ToRecord(), 32) ?? "{}";
        using var cs = JsonDocument.Parse(json);
        var psNames = ps.EnumerateObject().Select(p => p.Name).ToArray();
        var csNames = cs.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        foreach (var name in psNames.Except(csNames, StringComparer.OrdinalIgnoreCase)) differences.Add("result." + name + " missing in C#");
        foreach (var name in csNames.Except(psNames, StringComparer.OrdinalIgnoreCase)) differences.Add("result." + name + " only in C#");
        foreach (var property in ps.EnumerateObject())
        {
            var match = cs.RootElement.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, property.Name, StringComparison.OrdinalIgnoreCase));
            if (match.Value.ValueKind == JsonValueKind.Undefined) continue;
            var a = Canonical(Fold(JsonNode.Parse(property.Value.GetRawText()), scenario.Ps!.Root));
            var b = Canonical(Fold(JsonNode.Parse(match.Value.GetRawText()), scenario.Cs!.Root));
            if (a != b) differences.Add("result." + property.Name + ": ps=" + Clip(a) + " c#=" + Clip(b));
        }
    }

    private static void CompareLines(string kind, IReadOnlyList<string> ps, IReadOnlyList<string> cs, List<string> differences)
    {
        if (ps.SequenceEqual(cs)) return;
        for (var i = 0; i < Math.Max(ps.Count, cs.Count); i++)
        {
            var a = i < ps.Count ? ps[i] : "<none>";
            var b = i < cs.Count ? cs[i] : "<none>";
            if (a != b) { differences.Add(kind + " " + (i + 1) + " " + FirstDifference(a, b)); break; }
        }
    }

    private static string[] GitLines(NightlyRunWorld world) =>
        world.TraceText.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("GIT ", StringComparison.Ordinal)).Select(l => FoldText(l, world.Root)).ToArray();

    // The PowerShell launches (the StartProcess seam's record: file and argv, one JSON line each).
    private static string[] PsLaunches(NightlyRunWorld world)
    {
        var path = Path.Combine(world.Root, "launches.jsonl");
        if (!File.Exists(path)) return [];
        return File.ReadAllLines(path).Where(l => l.Trim().Length > 0).Select(line =>
        {
            using var doc = JsonDocument.Parse(line);
            var file = doc.RootElement.GetProperty("file").GetString() ?? "";
            var args = doc.RootElement.GetProperty("args").ValueKind == JsonValueKind.Array
                ? doc.RootElement.GetProperty("args").EnumerateArray().Select(a => a.GetString() ?? "").ToArray()
                : [doc.RootElement.GetProperty("args").GetString() ?? ""];
            return NormalLaunch(file, args, world.Root);
        }).ToArray();
    }

    // The C# launches, then the report calls (the report runs after the tests hop).
    private static string[] CsLaunches(NightlyRunWorld world) =>
        world.Launcher.Launches.Select(l => NormalLaunch(l.FileName, l.Arguments.ToArray(), world.Root))
            .Concat(world.Reports.Select(r => FoldText("nightly-report --summary " + r.Summary + (string.IsNullOrWhiteSpace(r.PreviousSummary) ? "" : " --previous-summary " + r.PreviousSummary), world.Root)))
            .ToArray();


    /// <summary>One launch in the spelling both sides share (see the class comment).</summary>
    private static string NormalLaunch(string file, string[] args, string root)
    {
        var fileIndex = Array.IndexOf(args, "-File");
        if (file == "dotnet" && args.Length >= 3 && args[2] == "nightly-tests")
            return FoldText("nightly-tests " + string.Join(" ", NightlyTestsOptions.Parse(args.Skip(3).ToArray()).ToArguments()), root);
        if (file == "antiphon-scripts" && args.Length >= 1 && args[0] == "nightly-tests")
            return FoldText("nightly-tests " + string.Join(" ", NightlyTestsOptions.Parse(args.Skip(1).ToArray()).ToArguments()), root);
        if (file == "antiphon-scripts") return FoldText(string.Join(" ", args), root);
        var kept = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "-SeamsPath") { i++; continue; }
            kept.Add(args[i]);
            if (args[i] == "-ContinueParentPid" && i + 1 < args.Length) { kept.Add("<PID>"); i++; }
        }
        return FoldText(file + " " + string.Join(" ", kept), root);
    }

    private static void CompareFiles(Scenario scenario, List<string> differences)
    {
        var ps = Files(scenario.Ps!);
        var cs = Files(scenario.Cs!);
        foreach (var name in ps.Keys.Except(cs.Keys)) differences.Add("file only in ps: " + name);
        foreach (var name in cs.Keys.Except(ps.Keys)) differences.Add("file only in c#: " + name);
        foreach (var name in ps.Keys.Intersect(cs.Keys))
        {
            var a = Content(ps[name], scenario.Ps!.Root);
            var b = Content(cs[name], scenario.Cs!.Root);
            if (a != b) differences.Add("file " + name + " differs: " + FirstDifference(a, b));
        }
    }

    private static Dictionary<string, string> Files(NightlyRunWorld world)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var root in new[] { world.State, world.Coordination, world.Releases, world.Clone })
        {
            if (!Directory.Exists(root)) continue;
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(world.Root, path).Replace('\\', '/');
                if (relative.StartsWith("checkout/.git/", StringComparison.Ordinal)) continue;
                files[FoldText(relative, world.Root)] = path;
            }
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
        return FoldText(text, root);
    }

    private static readonly Regex Stamp = new(@"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d(\.\d+)?(Z|[+-]\d\d:\d\d)?", RegexOptions.CultureInvariant);

    // A file digest: the attempt's names each side's root, so its digest differs; its bytes are compared folded.
    private static readonly Regex Hex64 = new("(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])", RegexOptions.CultureInvariant);

    private static string FoldText(string text, string root)
    {
        text = text.Replace(root, "<ROOT>", StringComparison.OrdinalIgnoreCase).Replace(root.Replace("\\", "\\\\"), "<ROOT>", StringComparison.OrdinalIgnoreCase)
            .Replace(root.Replace('\\', '/'), "<ROOT>", StringComparison.OrdinalIgnoreCase);
        return Hex64.Replace(Stamp.Replace(text, "<TS>"), "<H64>");
    }

    private static JsonNode? Fold(JsonNode? node, string root)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal)) copy[pair.Key] = Fold(pair.Value?.DeepClone(), root);
                return copy;
            }
            case JsonArray array:
                return new JsonArray(array.Select(item => Fold(item?.DeepClone(), root)).ToArray());
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(FoldText(text, root));
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

    // ---- the PowerShell side ---------------------------------------------------------------------------------------

    /// <summary>
    /// The scenario lines after Write-C487Seams: the pinned clock and the toolchain versions FakeHostProbe answers; a
    /// StartProcess that records each launch (the trace's EXEC line and one JSON line), answers the report with its exit and
    /// the tests hop (antiphon-scripts nightly-tests) by writing the scenario's summary into its --log-root.
    /// </summary>
    private const string SeamsExtra = """
        $NightlySeams.UtcNow = { return [datetime]::Parse('__UTCNOW__', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AdjustToUniversal) }
        $NightlySeams.ToolchainVersions = { param($tool) if ($tool -eq 'git') { 'git version 2.42.0' } else { '10.0.204 [/fake/sdk]' } }
        $NightlySeams.StartProcess = {
            param($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
            $a = @($ArgumentList)
            Add-Content -LiteralPath '__TRACE__' -Value (('EXEC {0} {1}' -f $FilePath, ($a -join ' '))) -Encoding ASCII
            Add-Content -LiteralPath '__LAUNCHES__' -Value (ConvertTo-Json -Compress -InputObject ([ordered]@{ file = [string]$FilePath; args = [string[]]$a })) -Encoding UTF8
            if ([string]$FilePath -eq 'antiphon-scripts' -and $a.Count -ge 1 -and [string]$a[0] -eq 'nightly-tests') {
                $logIdx = [array]::IndexOf($a, '--log-root')
                if ('__SUMMARY__' -and $logIdx -ge 0) {
                    $dir = [string]$a[$logIdx + 1]
                    New-Item -ItemType Directory -Path $dir -Force | Out-Null
                    [System.IO.File]::WriteAllText((Join-Path $dir 'summary.json'), '__SUMMARY__')
                }
                return @{ ExitCode = __TESTEXIT__; TimedOut = $false; Pid = 2 }
            }
            if ([string]$FilePath -eq 'antiphon-scripts') { return @{ ExitCode = __REPORT__; TimedOut = $false; Pid = 2 } }
            return @{ ExitCode = 0; TimedOut = $false; Pid = 2 }
        }
        """;

    /// <summary>The line-ending scenario's git: ls-files names one stale file; everything else as Write-C487Seams answers.</summary>
    private const string EolGitSeam = """
        $NightlySeams.Git = {
            param($WorkingDirectory, $Arguments)
            Add-Content -LiteralPath '__TRACE__' -Value (('GIT {0}' -f ($Arguments -join ' '))) -Encoding ASCII
            if ($Arguments -contains 'ls-files') { return @{ ExitCode = 0; Output = "i/lf    w/crlf  attr/text eol=lf `tsel.ps1`n" } }
            if ($Arguments -contains '--get' -and $Arguments -contains 'core.longpaths') { return @{ ExitCode = 0; Output = 'true' } }
            return @{ ExitCode = 0; Output = '__SHA__' }
        }
        """;

    /// <summary>
    /// Every scenario through the production entry: its seams (Write-C487Seams, then its lines), then
    /// scripts/nightly-run.ps1 @args -PassThru with its Write-Host lines captured; the result, the last line and the exit the
    /// entry would have returned. The selection attempt goes to the release-selection worker (the compiled select-release
    /// would first need a build of the CLI in this pwsh).
    /// </summary>
    private const string RunnerScript = """
        param([string]$RequestPath, [string]$ResultPath)
        $ErrorActionPreference = 'Continue'
        $repo = '__REPO__'
        $entry = Join-Path $repo 'scripts/nightly-run.ps1'
        . (Join-Path $repo 'scripts/lib/c487-harness.ps1')
        $env:ANTIPHON_SCRIPTS_CLI = 'off'
        $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json -AsHashtable -DateKind String
        $results = [ordered]@{}
        foreach ($scenario in $request.scenarios) {
            $name = [string]$scenario.name
            try {
                Write-C487Seams -Path $scenario.seams -TracePath $scenario.trace -GitOutput ([string]$scenario.sha) -GitExit ([int]$scenario.gitExit)
                Add-Content -LiteralPath $scenario.seams -Value ([string]$scenario.extra) -Encoding ASCII
                $invoke = @{}
                foreach ($key in $scenario.args.Keys) {
                    $value = $scenario.args[$key]
                    if ($key -eq 'Suites') { $value = [string[]]@($value) }
                    elseif ($value -is [bool]) { $value = [switch]$value }
                    $invoke[$key] = $value
                }
                $out = @(& $entry @invoke -PassThru 6>&1)
                $lines = @($out | Where-Object { $_ -is [System.Management.Automation.InformationRecord] } | ForEach-Object { [string]$_.MessageData })
                $result = @($out | Where-Object { $_ -isnot [System.Management.Automation.InformationRecord] }) | Select-Object -Last 1
                $code = 1
                if ($null -ne $result -and $null -ne $result.ExitCode) { $code = [int]$result.ExitCode }
                $last = ''
                if ($lines.Count -gt 0) { $last = $lines[$lines.Count - 1] }
                $results[$name] = [ordered]@{ ok = $true; result = $result; last = $last; exit = $code }
            } catch {
                $results[$name] = [ordered]@{ ok = $false; error = ($_ | Out-String) }
            }
        }
        $results | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $ResultPath -Encoding UTF8
        """;
}
