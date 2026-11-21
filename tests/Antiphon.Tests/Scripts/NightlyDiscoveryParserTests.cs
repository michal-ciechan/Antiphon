using System.Reflection;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
public sealed class NightlyDiscoveryParserTests
{
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1044_MultilineDiscovery() => RunCaseAsync(nameof(C1044_MultilineDiscovery), 8,
        "C1044 LF complete inventory", "C1044 LF exact metadata", "C1044 LF dispositions", "C1044 LF sorted digest",
        "C1044 CRLF complete inventory", "C1044 CRLF exact metadata", "C1044 CRLF dispositions", "C1044 CRLF sorted digest");

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1044_DiscoveryReconciliation() => RunCaseAsync(nameof(C1044_DiscoveryReconciliation), 8,
        "C1044 prefix orphan refuses", "C1044 suffix orphan refuses",
        "C1044 partial cannot borrow state", "C1044 partial cannot borrow identity", "C1044 duplicate refuses",
        "C1044 producer absent output stays absent", "C1044 producer sentinel unchanged",
        "C1044 producer publishes complete multiline inventory");

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1044_FixtureTypeName() => RunCaseAsync(nameof(C1044_FixtureTypeName), 3,
        "C1044 fixture and generic TypeName accepted",
        "C1044 fixture file stateless record refuses",
        "C1044 empty TypeName still truncated");

    /// <summary>
    /// W2-nightly S11, in-process: RC profile chunks are one class filter per discovered class. TUnit's treenode path uses
    /// the metadata class name, so a constructor suffix must not reach the filter, the exact roster, policy class rows, or
    /// TRX keys.
    /// </summary>
    [Test]
    public void C1044_FixtureClassChunks()
    {
        using var fixture = NightlyFixture.Create("c1044-chunks");
        const string fixtureType = "CardFilePolicyApiTests(CardFileSyncEndpointWebAppFactory)";
        const string agent = "AgentTuiApiTests(AgentTuiApiWebAppFactory)";
        var records = new[]
        {
            DiscoveryRecord("lists-uid", fixtureType, "ListsFiles"), DiscoveryRecord("other-uid", fixtureType, "Other"),
            DiscoveryRecord("agent-uid", agent, "Shows"), DiscoveryRecord("plain-uid", "Ordinary", "Plain"),
        };
        var path = fixture.PathOf("fixture-chunks.diag");
        File.WriteAllText(path, "Version: '1.44.0.0'\nVersion: 2.2.2\n" + string.Join("\n", records));
        var nodes = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.Read(path, "discovery"), "Nodes"));
        object[] hold = [new PlainObject(("Class", "CardFilePolicyApiTests"), ("Methods", new object[] { "Other" }), ("Reason", "held method"), ("Owner", "C1044"))];
        var chunks = LaunchPlanner.ProfileExecutionChunks(nodes, AllChunk(), hold);
        var invalid = new List<string>();
        var launched = new List<string>();
        foreach (var chunk in chunks)
        {
            try
            {
                launched.Add(string.Join(" ", NightlyTestsPlan.NativeExecutionArguments(fixture.PathOf("chunk.trx"), fixture.Root, Classes(chunk),
                    Str(chunk, "exactFilter"), "", "")));
            }
            catch (NightlyRefusal ex) { invalid.Add(ex.Message); }
        }
        var joined = string.Join(" || ", launched);
        var paren = joined.Contains('(');
        var sawAgent = joined.Contains("/AgentTuiApiTests/");
        var sawLists = joined.Contains("/CardFilePolicyApiTests/ListsFiles");
        var sawPlain = joined.Contains("/Ordinary/");
        var sawOther = joined.Contains("/Other");
        var rosterError = "";
        var filterOk = false;
        try
        {
            var doc = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, "fixture-hash");
            var roster = NightlyChunkVerdict.ExactRoster(doc, [new PlainObject(("class", "Antiphon.Tests.Application." + fixtureType), ("method", "ListsFiles"))], null);
            foreach (var row in PsValue.AsArray(PsValue.Prop(roster, "Chunks")))
                if (Str(row, "Filter") == "/*/Antiphon.Tests.Application/CardFilePolicyApiTests/ListsFiles") filterOk = true;
        }
        catch (Exception ex) { rosterError = ex.Message; }
        var fixtureNode = nodes.First(n => Str(n, "Uid") == "lists-uid");
        var policy = NightlyPolicy.NodeExcluded(fixtureNode,
            new object[] { new PlainObject(("Class", "CardFilePolicyApiTests"), ("Methods", Array.Empty<object>()), ("Reason", "held class"), ("Owner", "C1044")) });
        object[] trx = [new PlainObject(("ClassName", "Antiphon.Tests.Application.CardFilePolicyApiTests"), ("MethodName", "ListsFiles"), ("Outcome", "Passed"))];
        object[] terminal = [new PlainObject(("ClassName", "Antiphon.Tests.Application." + fixtureType), ("Type", fixtureType), ("Method", "ListsFiles"), ("Outcome", "Passed"), ("Uid", "lists-uid"))];
        var cross = NightlyVerdicts.CrossCheck(trx, terminal, ["Antiphon.Tests.Application.CardFilePolicyApiTests"]);
        var crossOk = PsValue.IsTrue(PsValue.Prop(cross, "Ok"));
        var classInvalid = invalid.Count(m => m == "invalid-class-filter" || m == "invalid-exact-filter");
        var ok = nodes.Length == 4 && chunks.Length >= 3 && classInvalid == 0 && !paren && sawAgent && sawLists && sawPlain && !sawOther && filterOk && policy && crossOk
            && string.IsNullOrWhiteSpace(rosterError);
        var rows = new NightlyCaseRows();
        rows.Check(ok, "C1044 fixture discovery rc chunks and exact roster have zero invalid-class-filter",
            "nodes=" + nodes.Length + " chunks=" + chunks.Length + " invalid=" + string.Join(",", invalid) + " paren=" + paren + " agent=" + sawAgent + " lists=" + sawLists
            + " plain=" + sawPlain + " other=" + sawOther + " roster=" + filterOk + " rosterError=" + rosterError + " policy=" + policy + " cross=" + crossOk + " launched=" + joined);
        rows.ShouldAllPass(1);
    }

    /// <summary>
    /// G087 already runs from the report harness default loop. These cases must run unattended, not only from -Case: the
    /// tests and run harness default loops run the fixture type-name and prerequisite cases, and the fixture class chunks
    /// (in-process since W2-nightly S11) are a test of this class.
    /// </summary>
    [Test]
    public void C1044_UnattendedCases()
    {
        var scripts = Path.Combine(Repo, "scripts");
        var testsElse = ElseText(Path.Combine(scripts, "test-nightly-tests.ps1"), "Get-C487CaseFunctions -Prefix 'C487_G0'");
        var runElse = ElseText(Path.Combine(scripts, "test-nightly-run.ps1"), "foreach ($fn in $cases)");
        var missing = new List<string>();
        foreach (var name in new[] { "Test-C1044_FixtureTypeName", "Test-C487_PrerequisiteGate" })
            if (testsElse.IndexOf(name, StringComparison.Ordinal) < 0) missing.Add(name);
        if (runElse.IndexOf("Test-C487_PrerequisiteSettle", StringComparison.Ordinal) < 0) missing.Add("Test-C487_PrerequisiteSettle");
        var chunksCase = typeof(NightlyDiscoveryParserTests).GetMethod(nameof(C1044_FixtureClassChunks));
        if (chunksCase?.GetCustomAttribute<TestAttribute>() == null) missing.Add(nameof(C1044_FixtureClassChunks));
        var rows = new NightlyCaseRows();
        rows.Check(missing.Count == 0, "C1044 unattended harness loop runs fixture and prerequisite cases", string.Join(",", missing));
        rows.ShouldAllPass(1);
    }

    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public Task C1044_ParserCompatibility() => RunCaseAsync(nameof(C1044_ParserCompatibility), 17,
        "C1044 golden discovery inventory", "C1044 golden discovery metadata", "C1044 golden terminal inventory",
        "C1044 nonterminal updates filtered", "C1044 prefixed envelopes accepted", "C1044 multiline terminal inventory",
        "C1044 discovery JSON roundtrip", "C1044 default OptIn excluded", "C1044 profile OptIn required",
        "C1044 explicit profile exclusion", "C1044 unsupported TUnit refuses", "C1044 unsupported MTP refuses",
        "C1044 compatibility duplicate refuses", "C1044 missing type refuses", "C1044 missing method refuses",
        "C1044 garbage refuses", "C1044 header only refuses");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-nightly-tests.ps1", "C1044", caseName, expectedRows, requiredRows);

    /// <summary>New-C1044DiscoveryRecord: one TUnit 1.44 discovery update line for a TypeName and method.</summary>
    private static string DiscoveryRecord(string uid, string typeName, string method) =>
        "TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = "
        + uid + " }, DisplayName = " + method + ", Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Antiphon.Tests.Application, TypeName = "
        + typeName + ", MethodName = " + method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, DiscoveredTestNodeStateProperty { Explanation =  } ] } }";

    /// <summary>Get-C1044ElseText: the last "} else {" branch (up to Write-C487Evidence) that contains the marker.</summary>
    private static string ElseText(string path, string marker)
    {
        var text = File.ReadAllText(path);
        var cursor = 0;
        var found = "";
        while (cursor < text.Length)
        {
            var idx = text.IndexOf("} else {", cursor, StringComparison.Ordinal);
            if (idx < 0) break;
            var end = text.IndexOf("Write-C487Evidence", idx, StringComparison.Ordinal);
            if (end > idx)
            {
                var branch = text.Substring(idx, end - idx);
                if (branch.IndexOf(marker, StringComparison.Ordinal) >= 0) found = branch;
            }
            cursor = idx + 8;
        }
        return found;
    }
}
