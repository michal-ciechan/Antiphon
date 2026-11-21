using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Shouldly;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1514 actual run: the staged timing probe discovered and run under native custody (OwnedLauncher, the twin of
/// Invoke-NightlyOwnedProcess), its planned manifest, launch completion and run summary from the production C#
/// (TestPhaseManifests, TestPhaseRunSummary), in-process since W2-nightly S09. Windows only (native custody).
/// </summary>
internal static class C1514NativeProbe
{
    private const string Filter = "/*/*/(TimingProbeTests*|TimingPlainProbeTests*|TimingAsyncProbeTests*)/*";

    internal static Task VerifyAsync(CancellationToken cancellationToken)
    {
        var work = C1514Support.FreshRoot();
        Console.WriteLine("C1514_ACTUAL_RUN " + work);
        var probe = C1514Support.StagedProbe();
        File.Exists(probe).ShouldBeTrue("timing probe must be staged by the parent build");
        var scope = JournalFixtures.Scope();
        var launcher = new OwnedLauncher();
        var environment = NightlyPolicy.SafeChildEnvironment(null!, "probe", null!);
        TestPhaseEnvironment.Remove(environment);

        var diag = Path.Combine(work, "discovery");
        Directory.CreateDirectory(diag);
        var discovery = launcher.Run(new NightlyLaunch("dotnet", [probe, "--list-tests", "--no-progress", "--no-ansi", "--diagnostic", "--diagnostic-output-directory", diag],
            work, 30000, Map(environment), Path.Combine(work, "discovery.log"), NightlyCustody.Owned));
        (discovery.ExitCode == 0 && !discovery.TimedOut).ShouldBeTrue("probe discovery failed: " + Log(work, "discovery.log"));
        var parsed = NightlyDiagnosticLog.Read(NightlyDiagnosticLog.Latest(diag), "discovery");
        var nodes = PsValue.AsArray(PsValue.Prop(parsed, "Nodes"));
        nodes.Length.ShouldBe(9, "probe discovery count");
        object[] classes = ["TimingProbeTests", "TimingPlainProbeTests", "TimingAsyncProbeTests"];
        object[] groups = [new PlainObject(("id", "probe"), ("classes", classes), ("nodes", nodes), ("exactFilter", Filter), ("seconds", 1))];
        TestPhaseManifests.WritePlanned(new TestPhasePlannedRequest
        {
            LogRoot = work, RunId = "run-native", Attempt = "1", Suite = "probe", SourceDigest = "c1514-native-probe",
            AssemblyDigest = NightlyClassNames.FileSha256(probe), AssemblyId = "Antiphon.TestTiming.Probe", Groups = groups, DiscoveryNodes = nodes,
            ProfileExclusions = Array.Empty<object>(),
        }, scope);

        TestPhaseEnvironment.Enable(environment, work, "run-native", "probe", "1", "probe", "rc", Path.Combine(work, "timing"), "");
        var wall = Stopwatch.StartNew();
        var run = launcher.Run(new NightlyLaunch("dotnet", [probe, "--treenode-filter", Filter, "--report-trx", "--report-trx-filename", "probe.trx", "--results-directory", work],
            work, 30000, Map(environment), Path.Combine(work, "probe.log"), NightlyCustody.Owned));
        wall.Stop();
        (!run.TimedOut && run.ChildrenExited).ShouldBeTrue("probe custody failed: " + Log(work, "probe.log"));
        TestPhaseManifests.CompleteLaunch(new PlainObject(("ExitCode", run.ExitCode)), work, Path.Combine(work, "probe.trx"), "probe", "probe", "run-native", "1",
            wall.Elapsed.TotalSeconds, null, scope);
        TestPhaseRunSummary.Run(work, 50, scope);

        foreach (var name in new[] { "groups-planned.json", "groups-executed.json", "summary.json", "tests.csv", "summary.md" })
            File.Exists(Path.Combine(work, "timing", name)).ShouldBeTrue("actual-run-five-artifacts: " + name);
        // The command over the same run directory writes the same summary.
        var before = File.ReadAllText(Path.Combine(work, "timing", "summary.json"));
        JournalFixtures.Summarize(work);
        File.ReadAllText(Path.Combine(work, "timing", "summary.json")).ShouldBe(before, "rerun summary differs");
        using var summary = JsonDocument.Parse(before);
        summary.RootElement.GetProperty("complete").GetBoolean().ShouldBeTrue("actual-run-complete");
        var tests = summary.RootElement.GetProperty("tests");
        tests.GetArrayLength().ShouldBe(9, "actual-run-exact-roster");
        foreach (var row in tests.EnumerateArray())
        {
            row.GetProperty("joined").GetBoolean().ShouldBeTrue("actual-run-joined");
            row.GetProperty("duration").GetString().ShouldNotBeNullOrWhiteSpace("actual-run-duration");
            row.GetProperty("groupId").GetString().ShouldBe("probe", "actual-run-group");
        }
        // Import-Csv's view of tests.csv: nine rows, each with a duration, the group and joined True.
        var lines = File.ReadAllLines(Path.Combine(work, "timing", "tests.csv")).Where(line => line.Length > 0).ToList();
        var header = Cells(lines[0]);
        var rows = lines.Skip(1).Select(Cells).ToList();
        rows.Count.ShouldBe(9, "actual CSV count");
        foreach (var cells in rows)
        {
            cells[header.IndexOf("duration")].ShouldNotBeNullOrWhiteSpace("actual CSV duration");
            cells[header.IndexOf("groupId")].ShouldBe("probe", "actual CSV group");
            cells[header.IndexOf("joined")].ShouldBe("True", "actual CSV join");
        }
        return Task.CompletedTask;
    }

    private static Dictionary<string, string> Map(System.Collections.IDictionary environment)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry pair in environment) map[(string)pair.Key] = pair.Value?.ToString() ?? "";
        return map;
    }

    private static string Log(string work, string name)
    {
        var path = Path.Combine(work, name);
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    // One CSV line's cells (quoted, quotes doubled).
    private static List<string> Cells(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else cell.Append(c);
        }
        cells.Add(cell.ToString());
        return cells;
    }
}
