using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S17: the nightly tests run (NightlyTests.Run) on S15's parity scenarios, C# only, against recorded
/// expectations. S15 ran the PowerShell (Invoke-AntiphonNightlyTests, scripts/lib/nightly-tests-impl.ps1, on the C487 seams)
/// and NightlyTests.Run side by side; before S17 deleted the PowerShell, one last three-way run held the entry, the C# run
/// (its options through the command line, the active-chunk identity bound as the command binds it) and the C487 seams'
/// in-process hop (scripts/lib/c487-nightly-tests.ps1) equal on every scenario, and recorded the entry's result, trace and
/// every file under the log root (scripts/fixtures/nightly/tests-regression/&lt;scenario&gt;.json). Each run here must match its
/// recording once volatile values are folded: the world's root, this machine's temp directory and command interpreter,
/// timestamps, measured seconds and milliseconds, 32-hex ids, 64-hex digests, the writer environment, key order, line
/// endings and the one-element arrays the DockerInfo and DiskFree seams left in preflight. npm resolves to a launcher in
/// each world, never to this machine's install. The recordings are the Windows entry's (paths, the npm launch through
/// ComSpec): the regression runs on Windows. A deliberate change re-records (NIGHTLY_TESTS_REGRESSION_RECORD=&lt;dir&gt; writes
/// this run's outputs there) and the diff is reviewed.
/// </summary>
[Category("Integration")]
[NotInParallel("QualificationTimingTests")]
public sealed class NightlyTestsParityTests
{
    private const string RunId = "parity-run-01";
    private static readonly string Sha = NightlyTestsWorld.Sha;

    private static readonly NightlyTrxRow[] PassRows = [new("uid-1", "Ns.SampleClass", "SampleMethod")];
    private static readonly NightlyTrxRow[] FailRows = [new("uid-1", "Ns.SampleClass", "SampleMethod", "Failed")];
    private static readonly NightlyTestNode[] Nodes = [new("uid-1", "SampleClass", "SampleMethod", "Ns")];

    private static string Recordings => Path.Combine(NightlyPlanCases.Repo, "scripts", "fixtures", "nightly", "tests-regression");

    /// <summary>One scenario: its run arguments, its world's behaviour and any setup of its clone.</summary>
    private sealed class Scenario
    {
        public required string Name { get; init; }
        public Dictionary<string, object?> Args { get; } = new(StringComparer.OrdinalIgnoreCase);
        public NightlyTrxRow[]? Rows { get; init; }
        public bool OmitDiscovery { get; init; }
        public Func<NightlyLaunch, NightlyLaunchResult>? Behaviour { get; init; }
        public bool Npm { get; init; }
        public Action<NightlyTestsWorld>? Setup { get; init; }
        public Action<NightlyTestsWorld, Dictionary<string, object?>>? Bind { get; init; }
        public bool Compare { get; init; } = true;
        public string ExportHistory { get; init; } = "";
        public string ExportSeed { get; init; } = "";
        public NightlyTestsWorld? World { get; set; }
    }

    [Test]
    public void CSharp_runs_match_the_recorded_entry_on_the_C487_scenarios()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("the recorded expectations are the Windows entry's (paths, the npm launch through ComSpec)");
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
                Behaviour = launch => NightlyTestsWorld.Result(Regex.IsMatch(string.Join(" ", launch.Arguments), "(^| )run build($| )") ? 2 : 0, ""),
            },
            new()
            {
                Name = "toolchain-floor", Rows = PassRows, Args = { ["Suites"] = new[] { "antiphon" } },
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
                Setup = world => world.Probe.DockerRow = new NightlyProbeRow { ExitCode = 1, Version = "fake", Detail = "down" },
            },
            new() { Name = "whatif", Args = { ["Suites"] = new[] { "antiphon" }, ["WhatIf"] = true } },
            new() { Name = "unknown-suite", Args = { ["Suites"] = new[] { "typo-suite" } } },
        };

        var record = Environment.GetEnvironmentVariable("NIGHTLY_TESTS_REGRESSION_RECORD");
        var rows = new NightlyCaseRows();
        try
        {
            foreach (var scenario in scenarios)
            {
                // The prep world carries the entry's side name: the exported history names its TRX by path.
                scenario.World = NewWorld(scenario, "ps");
                var differences = new List<string>();
                NightlyTestsResult? result = null;
                try { result = Run(scenario); }
                catch (Exception ex) { differences.Add("c# threw: " + ex.Message); }
                if (scenario.ExportHistory.Length > 0) Export(scenario);
                if (!scenario.Compare) continue;
                if (result != null)
                {
                    if (!string.IsNullOrEmpty(record)) Record(scenario, result, record);
                    else Compare(scenario, result, differences);
                    // The intake copies an accepted history or seed into the log root; a fallback copies nothing.
                    if (scenario.Name is "failed-first" or "stopped-seed" && !File.Exists(Path.Combine(scenario.World.Logs, "prior-failures.json")))
                        differences.Add("the run fell back instead of reading " + scenario.Args["PriorFailuresPath"]);
                }
                rows.Check(differences.Count == 0, "S15 parity " + scenario.Name, string.Join("\n  ", differences.Take(25)));
            }
            rows.Check(File.Exists(history) && File.Exists(seed), "S15 parity prep exported a history and a seed");
            rows.ShouldAllPass(scenarios.Count(s => s.Compare) + 1);
        }
        finally
        {
            foreach (var scenario in scenarios) scenario.World?.Dispose();
        }
    }

    /// <summary>
    /// The exact roster's refusals (a rebuilt assembly, an unclassified UID, an existing output, a member the discovery does
    /// not hold) from NightlyTests.ExactRoster: the reasons Invoke-NightlyExactRoster gave (S15 held the two equal), nothing
    /// launched, no receipt.
    /// </summary>
    [Test]
    public void The_exact_roster_refuses_as_the_entry_did()
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
        var cases = new (string Name, string Assembly, string Class, string Method, string[]? Classified, string Results, string Reason)[]
        {
            ("rebuilt", rebuilt, "C487.Probe.Ordinary", "Plain", null, fixture.PathOf("out-rebuilt"), "discovery-build-mismatch"),
            ("unclassified", assembly, "C487.Probe.Ordinary", "Plain", [], fixture.PathOf("out-unclassified"), "new-uid-required: classification required before launch"),
            ("existing", assembly, "C487.Probe.Ordinary", "Plain", null, existing, "execution-output-already-exists"),
            ("missing-member", assembly, "C487.Probe.Absent", "Plain", null, fixture.PathOf("out-missing"), "missing-exact-member:C487.Probe.Absent.Plain"),
        };
        var nodes = NightlyDiagnosticCases.ReadNodes(diag, "discovery");
        var doc = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, NightlyDiagnosticLog.AssemblyHash(assembly));
        var launcher = new RecordingLauncher();
        var host = fixture.Host(launcher: launcher);
        var rows = new NightlyCaseRows();
        foreach (var c in cases)
        {
            var refusal = NightlyDiagnosticCases.Refusal(() => NightlyTests.ExactRoster(host, c.Assembly, doc, [new PlainObject(("class", c.Class), ("method", c.Method))], c.Results, c.Classified));
            var receipt = File.Exists(Path.Combine(c.Results, "receipt.json"));
            rows.Check(refusal == c.Reason && !receipt, "S15 exact roster parity " + c.Name, "c#=" + refusal + " entry=" + c.Reason + " receipt=" + receipt);
        }
        rows.Check(launcher.Launches.Count == 0, "S15 exact roster parity nothing launched", launcher.Launches.Count.ToString());
        rows.ShouldAllPass(5);
    }

    // ---- the worlds ----------------------------------------------------------------------------------------------

    private static NightlyTestsWorld NewWorld(Scenario scenario, string side)
    {
        NightlyTestsWorld world;
        if (scenario.Npm) world = NightlyTestsRunTests.NpmWorld(out _, out _);
        else
        {
            world = new NightlyTestsWorld("s15-parity-" + side);
            // npm resolves under the world (as NpmWorld's does), never to this machine's install.
            var launcherDir = world.Fixture.PathOf("Program Files", "nodejs");
            Directory.CreateDirectory(launcherDir);
            File.WriteAllText(Path.Combine(launcherDir, "npm.cmd"), "@echo off\r\nexit /b 0\r\n");
            world.Variable("PATH", launcherDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        }
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

    /// <summary>
    /// The scenario's run as the nightly-tests command receives it: its options through the command line, the evidence
    /// recorder suppressed as under the seams, the active-chunk identity the heartbeat's (NightlyTestsBindings.Production's).
    /// </summary>
    private static NightlyTestsResult Run(Scenario scenario)
    {
        var world = scenario.World!;
        var options = NightlyTestsOptions.Parse(Options(Arguments(scenario, world)).ToArguments());
        var host = world.Host();
        var bindings = NightlyTestsWorld.SeamedBindings();
        bindings.ActiveChunk = QualificationHeartbeat.ActiveChunkWriter(text => host.Error.WriteLine("WARNING: " + text));
        return NightlyTests.Run(options, host, bindings);
    }

    // The history and seed exports of the prep run. The native fake's execution document names each node's type by its row's
    // qualified class; the history export maps it to the discovery node, whose type is bare: only this run's evidence is made
    // to agree, as the entry's prep run did.
    private static void Export(Scenario scenario)
    {
        var world = scenario.World!;
        foreach (var diag in Directory.EnumerateFiles(world.Logs, "log.diag", SearchOption.AllDirectories))
        {
            var doc = JsonNode.Parse(File.ReadAllText(diag)) as JsonObject;
            if (doc == null || (string?)doc["format"] != "antiphon-tunit-execution-v1") continue;
            foreach (var node in doc["nodes"]?.AsArray() ?? new JsonArray())
                if (node is JsonObject n && n["type"] is JsonValue type && type.TryGetValue<string>(out var text)) n["type"] = text.Split('.')[^1];
            File.WriteAllText(diag, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        PriorFailureHistory.Export(world.Logs, scenario.ExportHistory);
        StoppedSeedFiles.Export(world.Logs, scenario.ExportSeed, "parity-prep-01", "parity-source", Sha, Path.Combine(Path.GetDirectoryName(world.Logs)!, "timing-parity-prep-01"));
    }

    /// <summary>The probe of the toolchain scenario: Probe's answers, but the SDK version read throws (as the seam did).</summary>
    private sealed class ThrowingSdkProbe(FakeHostProbe inner) : INightlyHostProbe
    {
        public string Platform() => inner.Platform();
        public NightlyProbeRow Docker() => inner.Docker();
        public NightlyProbeRow DiskFree() => inner.DiskFree();
        public string ToolVersion(string tool) => tool == "git" ? inner.ToolVersion(tool) : throw new InvalidOperationException("sdk-probe-down");
    }

    // ---- the recordings and the comparisons ------------------------------------------------------------------------

    private sealed record Observed(JsonObject Result, string[] Trace, Dictionary<string, string> Files);

    private static Observed Observe(Scenario scenario, NightlyTestsResult result)
    {
        var world = scenario.World!;
        var json = Fold(JsonNode.Parse(JsonSerializer.Serialize(result, new JsonSerializerOptions { Converters = { new PlainObjectConverter() } })), world.Root) as JsonObject ?? new JsonObject();
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Directory.Exists(world.Logs))
            foreach (var path in Directory.EnumerateFiles(world.Logs, "*", SearchOption.AllDirectories))
                files[FoldText(Path.GetRelativePath(world.Logs, path).Replace('\\', '/'), world.Root)] = Content(path, world.Root);
        var trace = world.TraceText.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => FoldText(l, world.Root)).ToArray();
        return new Observed(json, trace, files);
    }

    private static void Record(Scenario scenario, NightlyTestsResult result, string directory)
    {
        var seen = Observe(scenario, result);
        var files = new JsonObject();
        foreach (var name in seen.Files.Keys.OrderBy(k => k, StringComparer.Ordinal)) files[name] = seen.Files[name];
        var record = new JsonObject
        {
            ["scenario"] = scenario.Name,
            ["result"] = seen.Result,
            ["trace"] = new JsonArray(seen.Trace.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
            ["files"] = files,
        };
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, scenario.Name + ".json"), record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static void Compare(Scenario scenario, NightlyTestsResult result, List<string> differences)
    {
        var path = Path.Combine(Recordings, scenario.Name + ".json");
        if (!File.Exists(path)) { differences.Add("no recording " + path); return; }
        var expected = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var seen = Observe(scenario, result);
        // Each field the entry returned (a refusal returned few); the C# result names them in any case.
        foreach (var pair in expected["result"]!.AsObject())
        {
            var match = seen.Result.FirstOrDefault(p => string.Equals(p.Key, pair.Key, StringComparison.OrdinalIgnoreCase));
            if (match.Key == null) { differences.Add("result." + pair.Key + " missing"); continue; }
            var a = Canonical(pair.Value);
            var b = Canonical(match.Value);
            if (a != b) differences.Add("result." + pair.Key + ": recorded=" + Clip(a) + " c#=" + Clip(b));
        }
        var trace = expected["trace"]!.AsArray().Select(l => l!.GetValue<string>()).ToArray();
        for (var i = 0; i < Math.Max(trace.Length, seen.Trace.Length); i++)
        {
            var a = i < trace.Length ? trace[i] : "<none>";
            var b = i < seen.Trace.Length ? seen.Trace[i] : "<none>";
            if (a != b) { differences.Add("trace line " + (i + 1) + ": recorded=" + Clip(a) + " c#=" + Clip(b)); break; }
        }
        var files = expected["files"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>(), StringComparer.Ordinal);
        foreach (var name in files.Keys.Except(seen.Files.Keys)) differences.Add("file only recorded: " + name);
        foreach (var name in seen.Files.Keys.Except(files.Keys)) differences.Add("file only in c#: " + name);
        foreach (var name in files.Keys.Intersect(seen.Files.Keys))
            if (files[name] != seen.Files[name]) differences.Add("file " + name + " differs: " + FirstDifference(files[name], seen.Files[name]));
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
    private static readonly Regex Volatile = new("(?i)(seconds|milliseconds|duration|elapsed|ticks|bytesRead|wallClock)", RegexOptions.CultureInvariant);

    // A measured number inside JSON text (a journal envelope's payload string): "name":<number>, the quotes escaped or not.
    private static readonly Regex EmbeddedVolatile = new(
        @"((?:\\u0022|\\""|"")[A-Za-z]*(?i:seconds|milliseconds|duration|elapsed|ticks)(?:\\u0022|\\""|"")\s*:\s*)-?[0-9][0-9.eE+-]*", RegexOptions.CultureInvariant);

    // This machine's temp directory and command interpreter, in each escaping a document nests them in.
    private static readonly string Temp = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static readonly string ComSpec = Environment.GetEnvironmentVariable("ComSpec") ?? "";

    private static string FoldEscaped(string text, string value, string token)
    {
        if (string.IsNullOrEmpty(value)) return text;
        foreach (var escaped in new[] { value.Replace("\\", "\\\\\\\\"), value.Replace("\\", "\\\\"), value })
            text = text.Replace(escaped, token, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    private static string FoldText(string text, string root)
    {
        text = text.Replace(root, "<ROOT>", StringComparison.OrdinalIgnoreCase).Replace(root.Replace("\\", "\\\\"), "<ROOT>", StringComparison.OrdinalIgnoreCase);
        text = FoldEscaped(FoldEscaped(text, ComSpec, "<COMSPEC>"), Temp, "<TMP>");
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
                    // The DockerInfo and DiskFree seams answered through Invoke(): a one-element list where production writes the row.
                    if (pair.Key is "docker" or "disk" && value is JsonArray { Count: 1 } single) value = single[0];
                    copy[pair.Key] = pair.Key == "writerEnvironment" ? JsonValue.Create("<ENV>") : Fold(value?.DeepClone(), root, pair.Key);
                }
                return copy;
            }
            case JsonArray array:
                return new JsonArray(array.Select(item => Fold(item?.DeepClone(), root, key)).ToArray());
            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(FoldText(text, root));
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
        return "at " + i + ": recorded=..." + Clip(a.Substring(from, Math.Min(200, a.Length - from))) + " c#=..." + Clip(b.Substring(from, Math.Min(200, b.Length - from)));
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
}
