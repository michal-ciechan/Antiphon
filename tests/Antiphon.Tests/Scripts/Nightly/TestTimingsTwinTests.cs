using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S03: scripts/lib/test-timings.ps1 stays for run-checkpoint and select-test-rosters, and TestTimings.cs is the
/// compiled twin of its antiphon-test-timing-v1 journal for nightly-tests-impl.ps1. The same events go through
/// Write-TestTimingEvent, through the twin from PowerShell (the nightly-tests-impl call, an [ordered] literal), and
/// through the twin from C#: the three journals must hold the same bytes and each step the same outcome. Read-TestTimingEvents
/// and ReadEvents then read every journal, the ps1's and the twin's, and a hand-made one with corrupt, blank, duplicate,
/// CRLF and torn lines: the same records, warnings and corrupt counts.
/// One difference is deliberate: the identical replay of an event is a no-op in the twin, where the ps1 reports
/// event-conflict when the stored ISO timestamp does not survive its ConvertFrom-Json/ConvertTo-Json round trip (a DateTime
/// drops trailing zero fraction digits and takes the local offset), so its re-serialized line no longer matches.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestTimingsTwinTests
{
    private sealed record Val(object Spec, object? Value);

    private sealed record Step(string Name, bool Rooted, (string Key, Val Value)[] Fields, string? Expected = null, bool Ps1Conflicts = false);

    private static Val S(string value) => new(new { t = "s", v = Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) }, value);
    private static Val I(int value) => new(new { t = "int", v = value }, value);
    private static Val L(long value) => new(new { t = "long", v = value.ToString(CultureInfo.InvariantCulture) }, value);
    private static Val D(double value) => new(new { t = "double", v = value.ToString("R", CultureInfo.InvariantCulture) }, value);
    private static Val B(bool value) => new(new { t = "bool", v = value }, value);
    private static readonly Val N = new(new { t = "null" }, null);
    private static Val A(params Val[] items) => new(new { t = "array", v = items.Select(item => item.Spec).ToArray() }, items.Select(item => item.Value).ToArray());
    private static Val O(params (string Key, Val Value)[] fields)
    {
        var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields) map[key] = value.Value;
        return new Val(new { t = "object", v = fields.Select(field => new { k = field.Key, v = field.Value.Spec }).ToArray() }, map);
    }

    private static (string, Val)[] Nightly(string eventId, string kind, string stamp, string filter, string producer = "nightly", params (string, Val)[] more) =>
    [
        ("schemaVersion", S(TestTimings.Schema)), ("eventId", S(eventId)), ("eventKind", S(kind)), ("runId", S("nightly")), ("attempt", I(1)),
        ("producer", S(producer)), ("setKind", S("chunk")), ("setId", S(filter)), ("outcome", S(kind == "run-started" ? "started" : "passed")),
        ("startedAt", S(stamp)), .. more, ("timingPersistence", S("complete")),
    ];

    [Test]
    [Timeout(300_000)]
    public async Task Ps1_and_twin_write_the_same_bytes_and_read_the_same_records(CancellationToken cancellationToken)
    {
        using var fixture = NightlyFixture.Create("test-timings-twin");
        var repo = DelegateScriptRunner.RepoRoot;
        var roots = new Dictionary<string, string> { ["ps1"] = fixture.PathOf("ps1"), ["glue"] = fixture.PathOf("glue"), ["twin"] = fixture.PathOf("twin"), ["crafted"] = fixture.PathOf("crafted") };
        foreach (var root in roots.Values) Directory.CreateDirectory(root);
        const string Filter = "/*/Antiphon.Tests/Class/(One*)|(Two*)";
        const string Stamp = "2026-11-21T05:06:07.1234567+00:00";
        const string Padded = "2026-11-21T05:06:07.1200000+00:00";
        var text = "quote\" back\\ tab\t nl\n cr\r ff\f bs\b ctl\u001f nel\u0085 ls\u2028 ps\u2029 del\u007f html<>&' slash/ e\u00e9 omega\u03a9 face\ud83d\ude00";
        var report = new (string, Val)[]
        {
            ("eventId", S("report-1")), ("eventKind", S("set-completed")), ("runId", S("wrapper")), ("attempt", S("2")), ("producer", S("wrapper")),
            ("setKind", S("class")), ("setId", S("Antiphon.Tests.Sample")), ("startedAt", S("2026-11-20 10:00:00Z")), ("caseWindowSeconds", D(1.5)),
            ("wallSeconds", D(2.0)), ("buildSeconds", D(1e-7)), ("concurrency", I(8)), ("sequence", L(3)), ("big", L(long.MaxValue)), ("negative", D(-0.25)),
            ("purposeKnown", B(true)), ("retry", B(false)), ("unavailableReason", N), ("args", A(I(1), S("x"), N)), ("exclusive", O(("db", I(1)), ("host", D(0.5)))),
            ("text", S(text)),
        };
        var changed = report.Select(field => field.Item1 == "wallSeconds" ? (field.Item1, D(3.0)) : field).ToArray();
        var steps = new List<Step>
        {
            new("nightly run-started", true, Nightly("nightly-start-1", "run-started", Stamp, Filter)),
            new("nightly set-completed", true, Nightly("nightly-done-1", "set-completed", Stamp, Filter, more: [("exitCode", I(0)), ("endedAt", S(Stamp)), ("category", S("unavailable"))])),
            new("run-checkpoint wrapper", true,
            [
                ("schemaVersion", S(TestTimings.Schema)), ("eventId", S("0f8fad5bd9cb469fa165708679a2c4e1")), ("eventKind", S("set-completed")), ("runId", S("wrapper")),
                ("attempt", I(1)), ("producer", S("wrapper")), ("setKind", S("checkpoint-row")), ("setId", S("CP-1")), ("outcome", S("failed")), ("exitCode", I(3)),
                ("startedAt", S(Stamp)), ("endedAt", S(Stamp)), ("category", S("unavailable")), ("timingPersistence", S("complete")), ("durabilityClass", S("configured-root")),
            ]),
            new("report shape with numbers, nesting and escapes", true, report),
            new("identical replay without an ISO timestamp", true, report, Expected: "ok"),
            new("ISO timestamp with trailing zero digits", true, Nightly("padded-1", "run-started", Padded, Filter)),
            new("identical replay of that timestamp", true, Nightly("padded-1", "run-started", Padded, Filter), Ps1Conflicts: true),
            new("same eventId, another payload", true, changed, Expected: "event-conflict"),
            new("set completed by another producer", true, Nightly("other-done-1", "set-completed", Stamp, Filter, "other")),
            new("run-started by another producer", true, Nightly("other-start-1", "run-started", Stamp, Filter, "other")),
            new("set completed again by its producer", true, Nightly("nightly-done-2", "set-completed", Stamp, Filter)),
            new("offset moves the date", true, Nightly("late-1", "run-started", "2026-11-21T23:30:00.0000000-05:00", Filter)),
            new("runId ..", true, [("eventId", S("bad-1")), ("runId", S("..")), ("attempt", I(1)), ("startedAt", S(Stamp))], Expected: "path-component-refused"),
            new("attempt a/b", true, [("eventId", S("bad-2")), ("runId", S("r")), ("attempt", S("a/b")), ("startedAt", S(Stamp))], Expected: "path-component-refused"),
            new("empty eventId", true, [("eventId", S("")), ("runId", S("r")), ("attempt", I(1)), ("startedAt", S(Stamp))], Expected: "path-component-refused"),
            new("eventId with a backslash", true, [("eventId", S("x\\y")), ("runId", S("r")), ("attempt", I(1)), ("startedAt", S(Stamp))], Expected: "path-component-refused"),
            new("no root writes nothing", false, Nightly("unrooted-1", "run-started", Stamp, Filter), Expected: "ok"),
        };

        WriteCrafted(roots["crafted"]);
        // The twin from C# first, so the ps1 reads its journal too.
        var twinOutcomes = new List<string>();
        foreach (var step in steps)
        {
            var map = new OrderedDictionary(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, value) in step.Fields) map[key] = value.Value;
            try { TestTimings.WriteEvent(step.Rooted ? roots["twin"] : "", map); twinOutcomes.Add("ok"); }
            catch (Exception e) { twinOutcomes.Add(e.Message); }
        }
        var input = fixture.PathOf("steps.json");
        File.WriteAllText(input, JsonSerializer.Serialize(new
        {
            ps1 = roots["ps1"], glue = roots["glue"], crafted = roots["crafted"], twin = roots["twin"],
            steps = steps.Select(step => new { name = step.Name, rooted = step.Rooted, fields = step.Fields.Select(field => new { k = field.Key, v = field.Value.Spec }).ToArray() }),
            joins = JoinCases(fixture.Root).Select(pair => new { parent = pair.Parent, child = pair.Child }),
        }), new UTF8Encoding(false));
        var output = fixture.PathOf("observed.json");
        var script = fixture.PathOf("twin.ps1");
        File.WriteAllText(script, Script, new UTF8Encoding(false));
        await ToolchainFloorTwinTests.RunPwshAsync(script, [repo, input, output], cancellationToken);
        using var observed = JsonDocument.Parse(File.ReadAllText(output));

        // Each step: the same outcome from the ps1, the twin from PowerShell, and the twin from C#.
        var outcomes = observed.RootElement.GetProperty("steps").EnumerateArray().ToArray();
        outcomes.Length.ShouldBe(steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var twin = twinOutcomes[i];
            var ps1 = outcomes[i].GetProperty("ps1").GetString();
            var glue = outcomes[i].GetProperty("glue").GetString();
            glue.ShouldBe(twin, step.Name);
            if (step.Ps1Conflicts)
            {
                ps1.ShouldBe("event-conflict", step.Name);
                twin.ShouldBe("ok", step.Name);
            }
            else
            {
                ps1.ShouldBe(twin, step.Name);
                if (step.Expected is not null) twin.ShouldBe(step.Expected, step.Name);
                else twin.ShouldBe("ok", step.Name);
            }
        }

        // The three journals hold the same files and bytes.
        var ps1Files = Journal(roots["ps1"]);
        ps1Files.Keys.Count.ShouldBe(4, string.Join(", ", ps1Files.Keys));
        foreach (var other in new[] { "glue", "twin" })
        {
            var files = Journal(roots[other]);
            files.Keys.OrderBy(key => key, StringComparer.Ordinal).ShouldBe(ps1Files.Keys.OrderBy(key => key, StringComparer.Ordinal), other);
            foreach (var (path, bytes) in ps1Files)
                Encoding.UTF8.GetString(files[path]).ShouldBe(Encoding.UTF8.GetString(bytes), other + " " + path);
        }
        var nightly = Encoding.UTF8.GetString(ps1Files[Path.Combine("runs", "2026-11-21", "nightly", "1", "events.jsonl")]).Split('\n');
        nightly[0].ShouldBe("{\"schemaVersion\":\"antiphon-test-timing-v1\",\"eventId\":\"nightly-start-1\",\"eventKind\":\"run-started\",\"runId\":\"nightly\",\"attempt\":1,"
            + "\"producer\":\"nightly\",\"setKind\":\"chunk\",\"setId\":\"/*/Antiphon.Tests/Class/(One*)|(Two*)\",\"outcome\":\"started\",\"startedAt\":\"" + Stamp + "\",\"timingPersistence\":\"complete\"}");
        ps1Files.ContainsKey(Path.Combine("runs", "2026-11-22", "nightly", "1", "events.jsonl")).ShouldBeTrue("the UTC date names the directory");

        // Every reader agrees on every journal, the other writer's included.
        var reads = observed.RootElement.GetProperty("reads").EnumerateArray().ToArray();
        var targets = ReadTargets(roots);
        reads.Length.ShouldBe(targets.Length);
        for (var i = 0; i < targets.Length; i++)
        {
            var read = TestTimings.ReadEvents(targets[i]);
            var expected = reads[i];
            var label = string.Join("+", targets[i].Select(Path.GetFileName));
            read.CorruptCount.ShouldBe(expected.GetProperty("corruptCount").GetInt32(), label);
            read.Warnings.ShouldBe(expected.GetProperty("warnings").EnumerateArray().Select(item => item.GetString()!).ToArray(), label);
            var records = expected.GetProperty("events").EnumerateArray().ToArray();
            read.Events.Count.ShouldBe(records.Length, label);
            for (var j = 0; j < records.Length; j++) SameRecord(read.Events[j], records[j], label + " event " + j);
        }
        var crafted = TestTimings.ReadEvents([roots["crafted"]]);
        crafted.CorruptCount.ShouldBe(1);
        crafted.Warnings.ShouldBe(new[] { "torn-tail" });

        // Join-Path's text and the mutex name: the two writers lock the same journal directory.
        var joins = observed.RootElement.GetProperty("joins").EnumerateArray().ToArray();
        var cases = JoinCases(fixture.Root);
        for (var i = 0; i < cases.Length; i++)
        {
            var joined = TestTimings.JoinPath(cases[i].Parent, cases[i].Child);
            joined.ShouldBe(joins[i].GetProperty("path").GetString(), cases[i].Parent);
            TestTimings.MutexName(joined).ShouldBe(joins[i].GetProperty("mutex").GetString(), cases[i].Parent);
        }
    }

    [Test]
    public void Components_are_refused_before_anything_is_written()
    {
        using var fixture = NightlyFixture.Create("test-timings-twin");
        foreach (var bad in new[] { "", " ", ".", "..", "a..b", "a/b", "a\\b", Path.GetPathRoot(fixture.Root)! })
            Should.Throw<InvalidOperationException>(() => TestTimings.RequireComponent(bad)).Message.ShouldBe("path-component-refused", bad);
        TestTimings.RequireComponent("nightly-start-1");
        var map = new OrderedDictionary { ["eventId"] = "e", ["runId"] = "..", ["attempt"] = 1, ["startedAt"] = "2026-11-21T00:00:00Z" };
        Should.Throw<InvalidOperationException>(() => TestTimings.WriteEvent(fixture.Root, map)).Message.ShouldBe("path-component-refused");
        Directory.EnumerateFileSystemEntries(fixture.Root).ShouldBeEmpty();
        TestTimings.WriteEvent(null!, map);
    }

    private static (string Parent, string Child)[] JoinCases(string root) => OperatingSystem.IsWindows()
        ? [("C:/x", "runs"), (@"C:\x\", "runs"), (@"C:\x\\", "runs"), ("rel/dir", "runs"), (@"\\server\share", "runs"), ("C:", "runs"), (@"C:\x", @"\runs"), (root, "runs")]
        : [("/x", "runs"), ("/x/", "runs"), ("rel/dir", "runs"), (@"C:\x\", "runs"), (root, "runs")];

    private static string[][] ReadTargets(Dictionary<string, string> roots) =>
    [
        [roots["ps1"]], [roots["glue"]], [roots["twin"]], [roots["crafted"]], [roots["ps1"], roots["crafted"]],
        [roots["crafted"], roots["twin"], Path.Combine(roots["ps1"], "missing")],
    ];

    private static void WriteCrafted(string root)
    {
        var first = Path.Combine(root, "runs", "2026-11-19", "r", "1");
        Directory.CreateDirectory(first);
        File.WriteAllText(Path.Combine(first, "events.jsonl"), string.Join("\n",
        [
            "{\"schemaVersion\":\"antiphon-test-timing-v1\",\"eventId\":\"crafted-1\",\"eventKind\":\"set-completed\",\"runId\":\"r\",\"attempt\":1,\"startedAt\":\"2026-11-19T01:02:03.0000000+00:00\",\"wallSeconds\":2.0,\"nested\":{\"a\":[1,\"b\\u001f\",null]}}",
            "{not json",
            "",
            "   ",
            "{\"eventId\":\"CRAFTED-1\",\"eventKind\":\"run-started\"}",
            "{\"eventKind\":\"set-completed\",\"setId\":\"no-id\"}",
            "5",
            "{\"eventId\":\"nightly-start-1\",\"eventKind\":\"run-started\",\"startedAt\":\"2026-11-19T00:00:00Z\"}",
            "{\"eventId\":\"tail\",\"eventKind\":\"run-started\"",
        ]), new UTF8Encoding(false));
        var second = Path.Combine(root, "runs", "2026-11-18", "r", "1");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "events.jsonl"), "{\"eventId\":\"crlf-1\",\"n\":1.5}\r\n{\"eventId\":\"crlf-2\",\"s\":\"a\\u001fb\"}\r\n", new UTF8Encoding(false));
    }

    private static Dictionary<string, byte[]> Journal(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

    // A read event against the ps1's record of it: the same fields in order; a DateTime there is the same instant here.
    private static void SameRecord(JsonNode? node, JsonElement expected, string label)
    {
        var kind = expected.GetProperty("kind").GetString();
        if (node is null)
        {
            kind.ShouldBe("null", label);
            return;
        }
        if (node is JsonObject row)
        {
            kind.ShouldBe("object", label);
            var fields = expected.GetProperty("fields").EnumerateArray().ToArray();
            row.Select(pair => pair.Key).ShouldBe(fields.Select(field => field.GetProperty("name").GetString()!), label);
            var index = 0;
            foreach (var pair in row) SameScalar(pair.Value, fields[index++].GetProperty("value"), label + " " + pair.Key);
            return;
        }
        SameScalar(node, expected, label);
    }

    private static void SameScalar(JsonNode? node, JsonElement expected, string label)
    {
        var kind = expected.GetProperty("kind").GetString();
        var text = expected.GetProperty("text").GetString();
        switch (node)
        {
            case null:
                kind.ShouldBe("null", label);
                return;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String && kind == "date":
                DateTimeOffset.Parse(value.GetValue<string>(), CultureInfo.InvariantCulture).UtcTicks.ToString(CultureInfo.InvariantCulture).ShouldBe(text, label);
                return;
            case JsonValue value when value.GetValueKind() == JsonValueKind.String:
                kind.ShouldBe("string", label);
                value.GetValue<string>().ShouldBe(text, label);
                return;
            case JsonValue value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False:
                kind.ShouldBe("bool", label);
                (value.GetValue<bool>() ? "True" : "False").ShouldBe(text, label);
                return;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                kind.ShouldBe("number", label);
                TestTimings.NodeJson(value).ShouldBe(text, label);
                return;
            default:
                kind.ShouldBe("json", label);
                TestTimings.NodeJson(node).ShouldBe(text, label);
                return;
        }
    }

    private const string Script = """
        param([string]$Repo, [string]$InputPath, [string]$OutPath)
        $ErrorActionPreference = 'Stop'
        . (Join-Path $Repo 'scripts/lib/nightly-core.ps1'); Assert-NightlyCoreAvailable 'test-timings-twin'
        . (Join-Path $Repo 'scripts/lib/test-timings.ps1')
        $spec = [IO.File]::ReadAllText($InputPath) | ConvertFrom-Json
        function New-TwinValue($Spec) {
            switch ([string]$Spec.t) {
                'null' { return $null }
                's' { return [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$Spec.v)) }
                'int' { return [int]$Spec.v }
                'long' { return [long]::Parse([string]$Spec.v, [Globalization.CultureInfo]::InvariantCulture) }
                'double' { return [double]::Parse([string]$Spec.v, [Globalization.CultureInfo]::InvariantCulture) }
                'bool' { return [bool]$Spec.v }
                'array' {
                    $items = New-Object System.Collections.Generic.List[object]
                    foreach ($item in @($Spec.v)) { $items.Add((New-TwinValue $item)) }
                    return , $items.ToArray()
                }
                'object' {
                    $map = [ordered]@{}
                    foreach ($field in @($Spec.v)) { $map[[string]$field.k] = New-TwinValue $field.v }
                    return $map
                }
            }
            throw ('unknown spec ' + $Spec.t)
        }
        function Get-TwinFailure($Record) {
            $e = $Record.Exception
            if ($e -is [System.Management.Automation.MethodInvocationException] -and $null -ne $e.InnerException) { $e = $e.InnerException }
            return [string]$e.Message
        }
        $outcomes = New-Object System.Collections.Generic.List[object]
        foreach ($step in @($spec.steps)) {
            $ps1Root = ''; $glueRoot = ''
            if ([bool]$step.rooted) { $ps1Root = [string]$spec.ps1; $glueRoot = [string]$spec.glue }
            $ps1Event = [ordered]@{}
            $glueEvent = [ordered]@{}
            foreach ($field in @($step.fields)) {
                $ps1Event[[string]$field.k] = New-TwinValue $field.v
                $glueEvent[[string]$field.k] = New-TwinValue $field.v
            }
            $ps1 = 'ok'
            try { Write-TestTimingEvent -Root $ps1Root -Event $ps1Event } catch { $ps1 = Get-TwinFailure $_ }
            $glue = 'ok'
            try { [Antiphon.Nightly.TestTimings]::WriteEvent($glueRoot, $glueEvent) } catch { $glue = Get-TwinFailure $_ }
            $outcomes.Add([ordered]@{ name = [string]$step.name; ps1 = $ps1; glue = $glue })
        }
        function ConvertTo-TwinScalar($Value) {
            if ($null -eq $Value) { return [ordered]@{ kind = 'null'; text = '' } }
            if ($Value -is [datetime]) { return [ordered]@{ kind = 'date'; text = ([DateTimeOffset]$Value).UtcTicks.ToString([Globalization.CultureInfo]::InvariantCulture) } }
            if ($Value -is [string]) { return [ordered]@{ kind = 'string'; text = $Value } }
            if ($Value -is [bool]) { return [ordered]@{ kind = 'bool'; text = [string]$Value } }
            if ($Value -is [long] -or $Value -is [int] -or $Value -is [double] -or $Value -is [decimal] -or $Value -is [System.Numerics.BigInteger]) {
                return [ordered]@{ kind = 'number'; text = (ConvertTo-Json -InputObject $Value -Compress) }
            }
            return [ordered]@{ kind = 'json'; text = (ConvertTo-Json -InputObject $Value -Compress -Depth 8) }
        }
        function ConvertTo-TwinRecord($Value) {
            if ($null -eq $Value) { return [ordered]@{ kind = 'null'; text = '' } }
            if ($Value -is [System.Management.Automation.PSCustomObject]) {
                $fields = New-Object System.Collections.Generic.List[object]
                foreach ($p in $Value.PSObject.Properties) { $fields.Add([ordered]@{ name = $p.Name; value = (ConvertTo-TwinScalar $p.Value) }) }
                return [ordered]@{ kind = 'object'; fields = $fields.ToArray() }
            }
            return ConvertTo-TwinScalar $Value
        }
        $targets = @(
            , @([string]$spec.ps1)
            , @([string]$spec.glue)
            , @([string]$spec.twin)
            , @([string]$spec.crafted)
            , @([string]$spec.ps1, [string]$spec.crafted)
            , @([string]$spec.crafted, [string]$spec.twin, (Join-Path ([string]$spec.ps1) 'missing'))
        )
        $reads = New-Object System.Collections.Generic.List[object]
        foreach ($target in $targets) {
            $read = Read-TestTimingEvents -Roots ([string[]]$target)
            $records = New-Object System.Collections.Generic.List[object]
            foreach ($item in $read.events) { $records.Add((ConvertTo-TwinRecord $item)) }
            $reads.Add([ordered]@{ events = $records.ToArray(); warnings = @($read.warnings); corruptCount = [int]$read.corruptCount })
        }
        $joins = New-Object System.Collections.Generic.List[object]
        foreach ($pair in @($spec.joins)) {
            $path = Join-Path ([string]$pair.parent) ([string]$pair.child)
            $mutex = 'antiphon-timing-' + ([BitConverter]::ToString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($path))).Replace('-', '').Substring(0, 32))
            $joins.Add([ordered]@{ path = $path; mutex = $mutex })
        }
        $out = [ordered]@{ steps = $outcomes.ToArray(); reads = $reads.ToArray(); joins = $joins.ToArray() }
        [IO.File]::WriteAllText($OutPath, ($out | ConvertTo-Json -Depth 12))
        """;
}
