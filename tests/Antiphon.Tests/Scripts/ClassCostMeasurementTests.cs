using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1842 S1 witnesses (the cases of the retired scripts/test-class-cost-measurement.ps1), in-process since W2-nightly
/// S11. The refresh cases run the refresh-nightly-class-costs command line in this process (ClassCostRefresh.Run; until
/// 2026-11-23 they ran scripts/refresh-nightly-class-costs.ps1 under pwsh) on fixture files; the packer case reads the
/// fixture through NightlyTestsPlan. No network, no git, no process, no qualification launch.
/// </summary>
[Category("Integration")]
public sealed class ClassCostMeasurementTests
{
    private const string CostFixture = """
        {
          "format": "antiphon-nightly-class-costs-v1",
          "platform": "windows",
          "source": "document-source",
          "refresh": "old-refresh",
          "classes": {
            "Antiphon.Tests.AbsentTests": {
              "nodes": 3,
              "seconds": 30,
              "basis": "pace",
              "source": "absent-row-source",
              "hostId": "old-host",
              "os": "windows",
              "measuredRunId": "old-run"
            },
            "Antiphon.Tests.KeptTests": {
              "nodes": 4,
              "seconds": 100,
              "basis": "measured",
              "source": "kept-row-source"
            }
          }
        }
        """;

    private sealed class Contract
    {
        public List<string> Errors { get; } = new();
        public void Note(bool ok, string detail) { if (!ok) Errors.Add(detail); }
        public void Assert(string name)
        {
            var rows = new NightlyCaseRows();
            rows.Check(Errors.Count == 0, "CM " + name + ".contract", string.Join("; ", Errors));
            rows.ShouldAllPass(1);
        }
    }

    private static void WriteFixture(string path, string body)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = body.Replace("\r\n", "\n");
        if (!text.EndsWith('\n')) text += "\n";
        File.WriteAllText(path, text);
    }

    private static void WriteTrx(string path, params (string Class, string Method, string Duration)[] rows)
    {
        var defs = new StringBuilder();
        var results = new StringBuilder();
        var index = 0;
        foreach (var row in rows)
        {
            var id = "t" + ++index;
            defs.Append($"<UnitTest id=\"{id}\" name=\"{row.Class}.{row.Method}\"><TestMethod className=\"{row.Class}\" name=\"{row.Method}\" /></UnitTest>");
            results.Append($"<UnitTestResult testId=\"{id}\" testName=\"{row.Class}.{row.Method}\" duration=\"{row.Duration}\" outcome=\"Passed\" />");
        }
        WriteFixture(path, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun><TestDefinitions>" + defs + "</TestDefinitions><Results>" + results + "</Results></TestRun>");
    }

    // The refresh-nightly-class-costs command line in this process, as the CLI runs it (a usage error is exit 2 and its
    // reason), on a host whose stdout and stderr are captured and whose environment answers every name with decoy.
    private static (int Code, string Text) Refresh(string? decoy, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            var options = RefreshNightlyClassCostsOptions.Parse(args, NightlyPlanCases.Repo, NightlyPlanCases.Repo);
            var host = NightlyHost.Create(null, new RecordingLauncher(), output, error, null, null, null, null);
            var code = ClassCostRefresh.Run(options, decoy == null ? host : new NightlyHost
            {
                Clock = host.Clock, Git = host.Git, Launcher = host.Launcher, Processes = host.Processes, Probe = host.Probe,
                Out = host.Out, Error = host.Error, Trace = host.Trace, Variables = _ => decoy,
            });
            return (code, output + "\n" + error);
        }
        catch (NightlyUsageException e)
        {
            return (2, "usage: antiphon-scripts refresh-nightly-class-costs: " + e.Message);
        }
    }

    private static JsonElement? Row(JsonElement doc, string name) =>
        doc.TryGetProperty("classes", out var classes) && classes.ValueKind == JsonValueKind.Object && classes.TryGetProperty(name, out var row) ? row : null;

    private static string Text(JsonElement? row, string name) =>
        row is { } r && r.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()) : "";

    private static bool Has(JsonElement? row, string name) => row is { } r && r.ValueKind == JsonValueKind.Object && r.TryGetProperty(name, out _);

    private static bool Near(JsonElement? row, string name, double expected) =>
        row is { } r && r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && Math.Abs(v.GetDouble() - expected) < 0.001;

    private static int Int(JsonElement? row, string name) =>
        row is { } r && r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : 0;

    private static JsonElement ReadCost(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    [Test, Timeout(120_000)]
    public void RefreshKeepsRowSource(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-keeps-source");
        var cost = fixture.PathOf("costs.json");
        var trx = fixture.PathOf("sample.trx");
        var output = fixture.PathOf("out.json");
        WriteFixture(cost, CostFixture);
        WriteTrx(trx, ("Antiphon.Tests.KeptTests", "One", "00:00:02"), ("Antiphon.Tests.KeptTests", "Two", "00:00:03"), ("Antiphon.Tests.FreshTests", "Only", "00:00:09"));
        File.Copy(cost, output);
        var c = new Contract();
        var run = Refresh(null, "--trx-path", trx, "--output-path", output, "--host-id", "fixture-host-c1842", "--os", "windows", "--measured-run-id", "run-9");
        c.Note(run.Code == 0, "refresh exit " + run.Code + " " + run.Text);
        c.Note(run.Text.StartsWith("refreshed 2 classes from 1 TRX files -> " + output, StringComparison.Ordinal), "summary line " + run.Text);
        if (run.Code == 0 && File.Exists(output))
        {
            var doc = ReadCost(output);
            c.Note(Text(doc, "format") == "antiphon-nightly-class-costs-v1", "format " + Text(doc, "format"));
            c.Note(Text(doc, "source") == "TRX durations: sample.trx", "document source " + Text(doc, "source"));
            c.Note(Text(doc, "refresh") == ClassCostRefresh.RefreshCommand, "refresh " + Text(doc, "refresh"));
            c.Note(Text(doc, "platform") == "windows", "platform " + Text(doc, "platform"));
            var kept = Row(doc, "Antiphon.Tests.KeptTests");
            var absent = Row(doc, "Antiphon.Tests.AbsentTests");
            var fresh = Row(doc, "Antiphon.Tests.FreshTests");
            c.Note(kept != null, "kept row missing");
            c.Note(absent != null, "absent row missing");
            c.Note(fresh != null, "fresh row missing");
            if (kept != null)
            {
                c.Note(Has(kept, "source") && Text(kept, "source") == "kept-row-source", "kept source " + Text(kept, "source"));
                c.Note(Int(kept, "nodes") == 2, "kept nodes " + Text(kept, "nodes"));
                c.Note(Near(kept, "seconds", 5), "kept seconds " + Text(kept, "seconds"));
                c.Note(Text(kept, "basis") == "trx", "kept basis " + Text(kept, "basis"));
                c.Note(Text(kept, "hostId") == "fixture-host-c1842", "kept host " + Text(kept, "hostId"));
                c.Note(Text(kept, "os") == "windows", "kept os " + Text(kept, "os"));
                c.Note(Text(kept, "measuredRunId") == "run-9", "kept run " + Text(kept, "measuredRunId"));
            }
            if (absent != null)
            {
                c.Note(Text(absent, "source") == "absent-row-source", "absent source " + Text(absent, "source"));
                c.Note(Int(absent, "nodes") == 3, "absent nodes " + Text(absent, "nodes"));
                c.Note(Near(absent, "seconds", 30), "absent seconds " + Text(absent, "seconds"));
                c.Note(Text(absent, "basis") == "pace", "absent basis " + Text(absent, "basis"));
                c.Note(Text(absent, "hostId") == "old-host", "absent host " + Text(absent, "hostId"));
                c.Note(Text(absent, "os") == "windows", "absent os " + Text(absent, "os"));
                c.Note(Text(absent, "measuredRunId") == "old-run", "absent run " + Text(absent, "measuredRunId"));
            }
            if (fresh != null)
            {
                c.Note(!Has(fresh, "source"), "fresh row invented a source");
                c.Note(Int(fresh, "nodes") == 1, "fresh nodes " + Text(fresh, "nodes"));
                c.Note(Near(fresh, "seconds", 9), "fresh seconds " + Text(fresh, "seconds"));
                c.Note(Text(fresh, "basis") == "trx", "fresh basis " + Text(fresh, "basis"));
                c.Note(Text(fresh, "hostId") == "fixture-host-c1842", "fresh host " + Text(fresh, "hostId"));
                c.Note(Text(fresh, "os") == "windows", "fresh os " + Text(fresh, "os"));
                c.Note(Text(fresh, "measuredRunId") == "run-9", "fresh run " + Text(fresh, "measuredRunId"));
            }
        }
        c.Assert(nameof(RefreshKeepsRowSource));
    }

    [Test, Timeout(120_000)]
    public void RefreshRequiresHostOsAndRun(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-requires-identity");
        var cost = fixture.PathOf("costs.json");
        var trx = fixture.PathOf("sample.trx");
        WriteFixture(cost, CostFixture);
        WriteTrx(trx, ("Antiphon.Tests.KeptTests", "One", "00:00:05"));
        // The host's environment answers every name (COMPUTERNAME, HOSTNAME) with a decoy: the host id must come from
        // --host-id alone.
        const string decoy = "decoy-machine-c1842";
        var c = new Contract();
        (int Code, string Text) RunOn(string name, params string[] args)
        {
            var output = fixture.PathOf(name + ".json");
            File.Copy(cost, output);
            var run = Refresh(decoy, new[] { "--trx-path", trx, "--output-path", output }.Concat(args).ToArray());
            // A refusal writes nothing.
            if (run.Code != 0) c.Note(File.ReadAllText(output) == File.ReadAllText(cost), name + " changed the file");
            return run;
        }
        var omitHost = RunOn("omit-host", "--os", "windows", "--measured-run-id", "run-9");
        c.Note(omitHost.Code == 2 && omitHost.Text.Contains("missing --host-id"), "omitted --host-id " + omitHost.Code + " " + omitHost.Text);
        var omitOs = RunOn("omit-os", "--host-id", "fixture-host-c1842", "--measured-run-id", "run-9");
        c.Note(omitOs.Code == 2 && omitOs.Text.Contains("missing --os"), "omitted --os " + omitOs.Code + " " + omitOs.Text);
        var omitRun = RunOn("omit-run", "--host-id", "fixture-host-c1842", "--os", "windows");
        c.Note(omitRun.Code == 2 && omitRun.Text.Contains("missing --measured-run-id"), "omitted --measured-run-id " + omitRun.Code + " " + omitRun.Text);
        var badOs = RunOn("bad-os", "--host-id", "fixture-host-c1842", "--os", "unix", "--measured-run-id", "run-9");
        c.Note(badOs.Code == 1 && badOs.Text.Contains("class-cost-os"), "unix os " + badOs.Code + " " + badOs.Text);
        var caseOs = RunOn("case-os", "--host-id", "fixture-host-c1842", "--os", "Windows", "--measured-run-id", "run-9");
        c.Note(caseOs.Code == 1 && caseOs.Text.Contains("class-cost-os"), "Windows os " + caseOs.Code + " " + caseOs.Text);
        var blank = RunOn("blank-host", "--host-id", " ", "--os", "windows", "--measured-run-id", "run-9");
        c.Note(blank.Code == 1 && blank.Text.Contains("class-cost-host"), "blank host " + blank.Code + " " + blank.Text);
        var blankRun = RunOn("blank-run", "--host-id", "fixture-host-c1842", "--os", "windows", "--measured-run-id", " ");
        c.Note(blankRun.Code == 1 && blankRun.Text.Contains("class-cost-run"), "blank run " + blankRun.Code + " " + blankRun.Text);
        var run = RunOn("out", "--host-id", "fixture-host-c1842", "--os", "windows", "--measured-run-id", "run-9");
        c.Note(run.Code == 0, "refresh exit " + run.Code + " " + run.Text);
        var outPath = fixture.PathOf("out.json");
        if (run.Code == 0 && File.Exists(outPath))
        {
            var doc = ReadCost(outPath);
            var kept = Row(doc, "Antiphon.Tests.KeptTests");
            c.Note(kept != null, "kept row missing");
            if (kept != null)
            {
                c.Note(Text(kept, "hostId") == "fixture-host-c1842", "host " + Text(kept, "hostId"));
                c.Note(Text(kept, "os") == "windows", "os " + Text(kept, "os"));
                c.Note(Text(kept, "measuredRunId") == "run-9", "run " + Text(kept, "measuredRunId"));
                c.Note(Text(kept, "hostId") != "decoy-machine-c1842", "host followed COMPUTERNAME " + Text(kept, "hostId"));
                c.Note(Text(kept, "hostId") != Environment.MachineName, "host followed machine name " + Text(kept, "hostId"));
                c.Note(Near(kept, "seconds", 5), "seconds " + Text(kept, "seconds"));
            }
            c.Note(Text(doc, "platform") == "windows", "platform " + Text(doc, "platform"));
        }
        c.Assert(nameof(RefreshRequiresHostOsAndRun));
    }

    [Test, Timeout(120_000)]
    public void ForeignOsDoesNotReplaceSeconds(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-foreign-os");
        var trx = fixture.PathOf("sample.trx");
        var output = fixture.PathOf("out.json");
        WriteFixture(fixture.PathOf("costs.json"), CostFixture);
        File.Copy(fixture.PathOf("costs.json"), output);
        WriteTrx(trx, ("Antiphon.Tests.KeptTests", "One", "00:00:08"), ("Antiphon.Tests.FreshTests", "Only", "00:00:09"));
        var c = new Contract();
        var run = Refresh(null, "--trx-path", trx, "--output-path", output, "--host-id", "linux-host-c1842", "--os", "linux", "--measured-run-id", "linux-run");
        c.Note(run.Code == 0, "refresh exit " + run.Code + " " + run.Text);
        if (run.Code == 0 && File.Exists(output))
        {
            var doc = ReadCost(output);
            c.Note(Text(doc, "platform") == "windows", "platform " + Text(doc, "platform"));
            var kept = Row(doc, "Antiphon.Tests.KeptTests");
            var absent = Row(doc, "Antiphon.Tests.AbsentTests");
            var fresh = Row(doc, "Antiphon.Tests.FreshTests");
            c.Note(kept != null, "kept row missing");
            c.Note(absent != null, "absent row missing");
            c.Note(fresh == null, "foreign refresh added a primary row");
            if (kept != null)
            {
                c.Note(Near(kept, "seconds", 100), "primary seconds " + Text(kept, "seconds"));
                c.Note(Int(kept, "nodes") == 4, "primary nodes " + Text(kept, "nodes"));
                c.Note(Text(kept, "basis") == "measured", "primary basis " + Text(kept, "basis"));
                c.Note(Text(kept, "source") == "kept-row-source", "primary source " + Text(kept, "source"));
                c.Note(!Has(kept, "hostId"), "foreign refresh wrote primary hostId");
                c.Note(!Has(kept, "measuredRunId"), "foreign refresh wrote primary measuredRunId");
                JsonElement? sample = Has(kept, "otherSample") ? kept.Value.GetProperty("otherSample") : null;
                c.Note(sample != null, "otherSample missing");
                if (sample != null)
                {
                    c.Note(Near(sample, "seconds", 8), "other seconds " + Text(sample, "seconds"));
                    c.Note(Int(sample, "nodes") == 1, "other nodes " + Text(sample, "nodes"));
                    c.Note(Text(sample, "basis") == "trx", "other basis " + Text(sample, "basis"));
                    c.Note(Text(sample, "hostId") == "linux-host-c1842", "other host " + Text(sample, "hostId"));
                    c.Note(Text(sample, "os") == "linux", "other os " + Text(sample, "os"));
                    c.Note(Text(sample, "measuredRunId") == "linux-run", "other run " + Text(sample, "measuredRunId"));
                }
            }
            if (absent != null)
            {
                c.Note(Near(absent, "seconds", 30), "absent seconds " + Text(absent, "seconds"));
                c.Note(Text(absent, "source") == "absent-row-source", "absent source " + Text(absent, "source"));
                c.Note(Text(absent, "hostId") == "old-host", "absent host " + Text(absent, "hostId"));
                c.Note(!Has(absent, "otherSample"), "absent row gained otherSample");
            }
        }
        c.Assert(nameof(ForeignOsDoesNotReplaceSeconds));
    }

    [Test, Timeout(120_000)]
    public void RefreshMergesTrxFilesIntoTheCanonicalFile(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-merge");
        var output = fixture.PathOf("out.json");
        // Untouched rows keep their members byte for byte (30.0 stays 30.0); Agents and API sort ordinally.
        WriteFixture(output, """
            {
              "format": "antiphon-nightly-class-costs-v1",
              "platform": "windows",
              "source": "document-source",
              "refresh": "old-refresh",
              "dropped": "a member the file does not keep",
              "classes": {
                "Antiphon.Tests.Agents": { "nodes": 3, "seconds": 30.0, "basis": "pace", "note": "kept <as> it was" },
                "Antiphon.Tests.API": { "nodes": 1, "seconds": 4, "basis": "measured", "os": "windows", "otherSample": { "seconds": 1 }, "extra": [1, 2] }
              }
            }
            """);
        var first = fixture.PathOf("first.trx");
        var second = fixture.PathOf("second.trx");
        // A parameterised class is cut at '('; a class name matches the row ignoring case and the row keeps its spelling;
        // a result with no duration, or for an id with no definition, counts nothing; ids match ignoring case.
        WriteFixture(first, "<?xml version=\"1.0\" encoding=\"utf-8\"?><TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\"><TestDefinitions>"
            + "<UnitTest id=\"T1\"><TestMethod className=\"antiphon.tests.api\" name=\"One\" /></UnitTest>"
            + "<UnitTest id=\"t2\"><TestMethod className=\"Antiphon.Tests.ParamTests(1, &quot;x&quot;)\" name=\"Two\" /></UnitTest>"
            + "<UnitTest id=\"t3\"><TestMethod className=\"Antiphon.Tests.Tiny\" name=\"Three\" /></UnitTest>"
            + "</TestDefinitions><Results>"
            + "<UnitTestResult testId=\"t1\" duration=\"00:00:01.2500000\" outcome=\"Passed\" />"
            + "<UnitTestResult testId=\"t2\" duration=\"00:00:02\" outcome=\"Passed\" />"
            + "<UnitTestResult testId=\"t2\" outcome=\"NotExecuted\" />"
            + "<UnitTestResult testId=\"t9\" duration=\"00:00:09\" outcome=\"Passed\" />"
            + "<UnitTestResult testId=\"t3\" duration=\"00:00:00.0100000\" outcome=\"Passed\" />"
            + "</Results></TestRun>");
        WriteTrx(second, ("Antiphon.Tests.ParamTests(2)", "Two", "00:00:03"), ("Antiphon.Tests.API", "Four", "00:00:00.0500000"));
        File.Copy(output, fixture.PathOf("before.json"));
        var c = new Contract();
        var run = Refresh(null, "--trx-path", first + ", " + second, "--output-path", output, "--host-id", "h", "--os", "windows",
            "--measured-run-id", "r", "--source", "merge <fixture>");
        c.Note(run.Code == 0, "refresh exit " + run.Code + " " + run.Text);
        c.Note(run.Text.StartsWith("refreshed 3 classes from 2 TRX files -> " + output, StringComparison.Ordinal), "summary line " + run.Text);
        var text = File.ReadAllText(output);
        c.Note(text == Lf("""
            {
              "format": "antiphon-nightly-class-costs-v1",
              "platform": "windows",
              "source": "merge <fixture>",
              "refresh": "scripts/antiphon refresh-nightly-class-costs --trx-path <a.trx,b.trx> --host-id <host> --os <windows|linux> --measured-run-id <run>",
              "classes": {
                "Antiphon.Tests.API": {
                  "nodes": 2,
                  "seconds": 1.3,
                  "basis": "trx",
                  "hostId": "h",
                  "os": "windows",
                  "measuredRunId": "r",
                  "otherSample": {
                    "seconds": 1
                  },
                  "extra": [
                    1,
                    2
                  ]
                },
                "Antiphon.Tests.Agents": {
                  "nodes": 3,
                  "seconds": 30.0,
                  "basis": "pace",
                  "note": "kept <as> it was"
                },
                "Antiphon.Tests.ParamTests": {
                  "nodes": 2,
                  "seconds": 5,
                  "basis": "trx",
                  "hostId": "h",
                  "os": "windows",
                  "measuredRunId": "r"
                },
                "Antiphon.Tests.Tiny": {
                  "nodes": 1,
                  "seconds": 0.1,
                  "basis": "trx",
                  "hostId": "h",
                  "os": "windows",
                  "measuredRunId": "r"
                }
              }
            }

            """), "written file:\n" + text);
        c.Note(!File.ReadAllBytes(output).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "byte-order mark");

        // --set-platform moves the platform only when every row's os is --os.
        var foreign = fixture.PathOf("foreign.json");
        File.Copy(fixture.PathOf("before.json"), foreign);
        var linux = Refresh(null, "--trx-path", second, "--output-path", foreign, "--host-id", "l", "--os", "linux", "--measured-run-id", "lr", "--set-platform");
        c.Note(linux.Code == 0 && Text(ReadCost(foreign), "platform") == "windows", "set-platform with rows of other os " + linux.Text);
        var api = Row(ReadCost(foreign), "Antiphon.Tests.API");
        c.Note(Near(api, "seconds", 4) && api is { } a && Near(a.GetProperty("otherSample"), "seconds", 0.1)
            && Text(a.GetProperty("otherSample"), "os") == "linux", "foreign sample replaced in place " + api);
        var fresh = fixture.PathOf("fresh.json");
        var created = Refresh(null, "--trx-path", second, "--output-path", fresh, "--host-id", "l", "--os", "linux", "--measured-run-id", "lr", "--set-platform");
        var createdDoc = ReadCost(fresh);
        c.Note(created.Code == 0 && Text(createdDoc, "platform") == "linux" && Text(createdDoc, "source") == "TRX durations: second.trx",
            "a missing file starts empty on --os " + created.Text);

        // A file that is not a class-cost file and a TRX that is not XML are refused, and nothing is written.
        var other = fixture.PathOf("other.json");
        WriteFixture(other, "{\"format\":\"antiphon-something-else\",\"classes\":{}}");
        var refused = Refresh(null, "--trx-path", second, "--output-path", other, "--host-id", "h", "--os", "windows", "--measured-run-id", "r");
        c.Note(refused.Code == 1 && refused.Text.Contains("refresh-nightly-class-costs: class-cost-format") && File.ReadAllText(other).Contains("something-else"),
            "foreign format " + refused.Code + " " + refused.Text);
        var broken = fixture.PathOf("broken.trx");
        WriteFixture(broken, "<TestRun><Results>");
        var unchanged = File.ReadAllText(output);
        var malformed = Refresh(null, "--trx-path", broken, "--output-path", output, "--host-id", "h", "--os", "windows", "--measured-run-id", "r");
        c.Note(malformed.Code == 1 && File.ReadAllText(output) == unchanged, "malformed TRX " + malformed.Code + " " + malformed.Text);
        c.Note(Refresh(null, "--output-path", output, "--host-id", "h", "--os", "windows", "--measured-run-id", "r").Text.Contains("missing --trx-path"), "missing --trx-path");
        c.Assert(nameof(RefreshMergesTrxFilesIntoTheCanonicalFile));
    }

    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    [Test]
    public void CommittedFileNamesItsRefresh()
    {
        // The committed class-cost file names the command that writes it, and is a file that command accepts.
        var path = Path.Combine(NightlyPlanCases.Repo, "scripts/lib/nightly-class-costs.json");
        var doc = ReadCost(path);
        var c = new Contract();
        c.Note(Text(doc, "refresh") == ClassCostRefresh.RefreshCommand, "refresh " + Text(doc, "refresh"));
        c.Note(Text(doc, "format") == NightlyTestsPlan.ClassCostFormat && Text(doc, "platform") is "windows" or "linux", "format or platform");
        c.Note(File.Exists(Path.Combine(NightlyPlanCases.Repo, "scripts/lib/Antiphon.Nightly/ClassCostRefresh.cs")), "the refresh source moved");
        c.Assert(nameof(CommittedFileNamesItsRefresh));
    }

    [Test]
    public void PackerStillReadsPrimarySeconds()
    {
        using var fixture = NightlyFixture.Create("cm-packer");
        var cost = fixture.PathOf("costs.json");
        WriteFixture(cost, """
            {
              "format": "antiphon-nightly-class-costs-v1",
              "platform": "windows",
              "source": "document-source",
              "refresh": "old-refresh",
              "classes": {
                "Antiphon.Tests.SampleTests": {
                  "nodes": 2,
                  "seconds": 100,
                  "basis": "trx",
                  "source": "row-source",
                  "hostId": "fixture-host-c1842",
                  "os": "windows",
                  "measuredRunId": "run-1",
                  "otherSample": { "nodes": 2, "seconds": 999, "basis": "trx", "hostId": "linux-host", "os": "linux", "measuredRunId": "linux-run" }
                }
              }
            }
            """);
        var c = new Contract();
        var table = NightlyTestsPlan.ClassCostTable(cost);
        var per = table.ContainsKey("Antiphon.Tests.SampleTests") ? (double?)PsValue.Double(table["Antiphon.Tests.SampleTests"]) : null;
        c.Note(per is { } p && Math.Abs(p - 50) < 0.001, "table per-node " + per);
        var node = new PlainObject(("Categories", Array.Empty<object>()));
        var priced = NightlyTestsPlan.LaunchClassCost(new object[] { node, node }, "SampleTests", "Antiphon.Tests.SampleTests", table);
        c.Note(PsValue.Double(PsValue.Prop(priced, "NodeCount")) == 2, "node count " + PsValue.Str(PsValue.Prop(priced, "NodeCount")));
        c.Note(PsValue.Prop(priced, "MeasuredPerNode") is { } measured && Math.Abs(PsValue.Double(measured) - 50) < 0.001,
            "measured per-node " + PsValue.Str(PsValue.Prop(priced, "MeasuredPerNode")));
        c.Note(Math.Abs(PsValue.Double(PsValue.Prop(priced, "Seconds")) - 100) < 0.001, "priced seconds " + PsValue.Str(PsValue.Prop(priced, "Seconds")));
        c.Assert(nameof(PackerStillReadsPrimarySeconds));
    }
}
