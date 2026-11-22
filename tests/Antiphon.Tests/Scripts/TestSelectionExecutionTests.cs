using System.Collections;
using System.Diagnostics;
using System.Text;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1313: an exact roster runs exactly its expanded members on a real test host, coverage is an exact multiset,
/// and a newly discovered unclassified uid widens the roster before anything launches. In-process on the core
/// (NightlyTests.ExactRoster, NightlyChunkVerdict) against the staged checkpoint filter host; moved from
/// scripts/test-test-selection.ps1 by the 2026-11-22 plan, unit 3.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class TestSelectionExecutionTests
{
    private const string Offline = "Antiphon.Tests.FilterHost.C1313Offline";

    private static string FilterHost => Path.Combine(AppContext.BaseDirectory, "checkpoint-filter-host", "Antiphon.Checkpoints.FilterHost.dll");

    [Test]
    public void C1313_ExactExpandedExecution()
    {
        using var fixture = NightlyFixture.Create("c1313-exact");
        var discovery = Discovery(fixture);
        var expected = Nodes(discovery).Where(n => S(n, "type") == "C1313Offline" && S(n, "method") is "One" or "Rows").Select(n => S(n, "uid")).ToList();
        var directory = fixture.PathOf("execution");
        var sentinel = fixture.PathOf("forbidden.txt");
        object[] members = [new PlainObject(("class", Offline), ("method", "One")), new PlainObject(("class", Offline), ("method", "Rows"))];
        var log = new StringWriter();
        NightlyTests.ExactRoster(NightlyHost.Production(log, log), FilterHost, discovery, members, directory, null, new Hashtable { ["C1313_SENTINEL"] = sentinel });
        using var receipt = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "receipt.json")));
        var executed = receipt.RootElement.GetProperty("executedUids").EnumerateArray().Select(e => e.GetString()!).ToList();
        (expected.Count == 4 && receipt.RootElement.GetProperty("complete").GetBoolean() && executed.Count == 4 && C1313.Same(executed, expected))
            .ShouldBeTrue("C1313 exact-uid-union: " + string.Join(",", executed) + " " + log);
        File.Exists(sentinel).ShouldBeFalse("C1313 forbidden-not-launched");
    }

    [Test]
    public void C1313_CoverageRefusal()
    {
        string[] required = ["one", "row-1", "row-2", "row-3"];
        object[] Passed() => required.Select(uid => (object)new PlainObject(("Uid", uid), ("Outcome", "Passed"))).ToArray();
        NightlyDiagnosticCases.B(NightlyChunkVerdict.ExactCoverage(required, Passed()), "Ok").ShouldBeTrue("C1313 coverage-control");
        foreach (var fault in new[] { "missing-uid", "extra-uid", "duplicate-uid", "required-skip", "zero-required" })
        {
            var rows = Passed().ToList();
            switch (fault)
            {
                case "missing-uid": rows.RemoveAt(3); break;
                case "extra-uid": rows.Add(new PlainObject(("Uid", "forbidden"), ("Outcome", "Passed"))); break;
                case "duplicate-uid": rows.Add(rows[0]); break;
                case "required-skip": rows[0] = new PlainObject(("Uid", "one"), ("Outcome", "Skipped")); break;
                case "zero-required": rows.Clear(); break;
            }
            var verdict = NightlyChunkVerdict.ExactCoverage(required, rows.ToArray());
            var reasons = NightlyDiagnosticCases.Items(verdict, "Reasons").Select(PsValue.Str).ToList();
            (!NightlyDiagnosticCases.B(verdict, "Ok") && reasons.Contains(fault)).ShouldBeTrue("C1313 " + fault + ": " + string.Join(",", reasons));
        }
    }

    [Test]
    public void C1313_NewUidRemainder()
    {
        using var fixture = NightlyFixture.Create("c1313-remainder");
        var discovery = Discovery(fixture);
        var nodes = Nodes(discovery).ToList();
        object[] members = [new PlainObject(("class", Offline), ("method", "One"))];
        var all = nodes.Select(n => S(n, "uid")).ToArray();
        var control = NightlyChunkVerdict.ExactRoster(discovery, members, all);
        (!NightlyDiagnosticCases.B(control, "Widened") && NightlyDiagnosticCases.Items(control, "RequiredUids").Length == 1).ShouldBeTrue("valid remainder control");
        var added = S(nodes.First(n => S(n, "type") == "C1313Offline" && S(n, "method") == "Rows"), "uid");
        var known = all.Where(uid => uid != added).ToArray();
        var widened = NightlyChunkVerdict.ExactRoster(discovery, members, known);
        var requiredUids = NightlyDiagnosticCases.Items(widened, "RequiredUids").Select(PsValue.Str).ToList();
        var unlaunched = fixture.PathOf("must-not-launch");
        var refused = NightlyDiagnosticCases.Refusal(() => NightlyTests.ExactRoster(NightlyHost.Production(TextWriter.Null, TextWriter.Null), FilterHost, discovery, members,
            unlaunched, known));
        (NightlyDiagnosticCases.B(widened, "Widened") && requiredUids.Contains(added) && requiredUids.Count == nodes.Count
            && refused.StartsWith("new-uid-required", StringComparison.Ordinal) && !Directory.Exists(unlaunched)).ShouldBeTrue("C1313 new-uid-required: " + refused);
    }

    private static string S(object node, string name) => NightlyDiagnosticCases.S(node, name);

    private static IEnumerable<object> Nodes(object discovery) => NightlyDiagnosticCases.Items(discovery, "nodes");

    /// <summary>Get-C1313FilterDiscovery: the filter host's own --list-tests discovery as a discovery document.</summary>
    private static object Discovery(NightlyFixture fixture)
    {
        File.Exists(FilterHost).ShouldBeTrue("filter host must be staged by the parent build: " + FilterHost);
        var directory = fixture.PathOf("discovery");
        Directory.CreateDirectory(directory);
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = fixture.Root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        start.Environment["C1313_SENTINEL"] = fixture.PathOf("forbidden.txt");
        foreach (var argument in new[] { FilterHost, "--list-tests", "--no-progress", "--no-ansi", "--diagnostic", "--diagnostic-output-directory", directory,
                     "--results-directory", directory }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("filter host did not start");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new InvalidOperationException("owned filter child timeout");
        }
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, "discovery child failed: " + stdout.Result + stderr.Result);
        var nodes = NightlyDiagnosticCases.ReadNodes(NightlyDiagnosticLog.Latest(directory), "discovery");
        return NightlyDiagnosticLog.NewDiscoveryDocument(nodes, NightlyDiagnosticLog.AssemblyHash(FilterHost));
    }
}
