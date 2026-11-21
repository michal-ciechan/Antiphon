using System.Diagnostics;
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
/// S11. The three refresh cases still run the production scripts/refresh-nightly-class-costs.ps1 (PowerShell) on fixture
/// files; the packer case reads the fixture through NightlyTestsPlan. No network, no git, no qualification launch.
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

    // pwsh -NoProfile -File refresh-nightly-class-costs.ps1 <args> 2>&1: the exit code and the combined output.
    private static async Task<(int Code, string Text)> Refresh(IDictionary<string, string>? environment, CancellationToken token, params string[] args)
    {
        var start = new ProcessStartInfo("pwsh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            WorkingDirectory = NightlyPlanCases.Repo,
        };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-File", Path.Combine(NightlyPlanCases.Repo, "scripts", "refresh-nightly-class-costs.ps1") }.Concat(args))
            start.ArgumentList.Add(a);
        if (environment != null) foreach (var (key, value) in environment) start.Environment[key] = value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("pwsh did not start");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
        return (process.ExitCode, await stdout + "\n" + await stderr);
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
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task RefreshKeepsRowSource(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-keeps-source");
        var cost = fixture.PathOf("costs.json");
        var trx = fixture.PathOf("sample.trx");
        var output = fixture.PathOf("out.json");
        WriteFixture(cost, CostFixture);
        WriteTrx(trx, ("Antiphon.Tests.KeptTests", "One", "00:00:02"), ("Antiphon.Tests.KeptTests", "Two", "00:00:03"), ("Antiphon.Tests.FreshTests", "Only", "00:00:09"));
        File.Copy(cost, output);
        var c = new Contract();
        var run = await Refresh(null, token, "-TrxPath", trx, "-OutputPath", output, "-HostId", "fixture-host-c1842", "-Os", "windows", "-MeasuredRunId", "run-9");
        c.Note(run.Code == 0, "refresh exit " + run.Code + " " + run.Text);
        if (run.Code == 0 && File.Exists(output))
        {
            var doc = ReadCost(output);
            c.Note(Text(doc, "format") == "antiphon-nightly-class-costs-v1", "format " + Text(doc, "format"));
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
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task RefreshRequiresHostOsAndRun(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-requires-identity");
        var cost = fixture.PathOf("costs.json");
        var trx = fixture.PathOf("sample.trx");
        WriteFixture(cost, CostFixture);
        WriteTrx(trx, ("Antiphon.Tests.KeptTests", "One", "00:00:05"));
        // The child's machine name variables are decoys: the host id must come from -HostId alone.
        var decoy = new Dictionary<string, string> { ["COMPUTERNAME"] = "decoy-machine-c1842", ["HOSTNAME"] = "decoy-machine-c1842" };
        var c = new Contract();
        async Task<(int Code, string Text)> RunOn(string name, params string[] args)
        {
            var output = fixture.PathOf(name + ".json");
            File.Copy(cost, output);
            return await Refresh(decoy, token, new[] { "-TrxPath", trx, "-OutputPath", output }.Concat(args).ToArray());
        }
        var omitHost = await RunOn("omit-host", "-Os", "windows", "-MeasuredRunId", "run-9");
        c.Note(omitHost.Code != 0 && omitHost.Text.Contains("HostId"), "omitted HostId " + omitHost.Code + " " + omitHost.Text);
        var omitOs = await RunOn("omit-os", "-HostId", "fixture-host-c1842", "-MeasuredRunId", "run-9");
        c.Note(omitOs.Code != 0 && omitOs.Text.Contains("Os"), "omitted Os " + omitOs.Code + " " + omitOs.Text);
        var omitRun = await RunOn("omit-run", "-HostId", "fixture-host-c1842", "-Os", "windows");
        c.Note(omitRun.Code != 0 && omitRun.Text.Contains("MeasuredRunId"), "omitted MeasuredRunId " + omitRun.Code + " " + omitRun.Text);
        var badOs = await RunOn("bad-os", "-HostId", "fixture-host-c1842", "-Os", "unix", "-MeasuredRunId", "run-9");
        c.Note(badOs.Code != 0 && badOs.Text.Contains("class-cost-os"), "unix os " + badOs.Code + " " + badOs.Text);
        var blank = await RunOn("blank-host", "-HostId", " ", "-Os", "windows", "-MeasuredRunId", "run-9");
        c.Note(blank.Code != 0 && blank.Text.Contains("class-cost-host"), "blank host " + blank.Code + " " + blank.Text);
        var run = await RunOn("out", "-HostId", "fixture-host-c1842", "-Os", "windows", "-MeasuredRunId", "run-9");
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
    [ParallelLimiter<ProcessSpawnLimit>]
    public async Task ForeignOsDoesNotReplaceSeconds(CancellationToken token)
    {
        using var fixture = NightlyFixture.Create("cm-foreign-os");
        var trx = fixture.PathOf("sample.trx");
        var output = fixture.PathOf("out.json");
        WriteFixture(fixture.PathOf("costs.json"), CostFixture);
        File.Copy(fixture.PathOf("costs.json"), output);
        WriteTrx(trx, ("Antiphon.Tests.KeptTests", "One", "00:00:08"), ("Antiphon.Tests.FreshTests", "Only", "00:00:09"));
        var c = new Contract();
        var run = await Refresh(null, token, "-TrxPath", trx, "-OutputPath", output, "-HostId", "linux-host-c1842", "-Os", "linux", "-MeasuredRunId", "linux-run");
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
