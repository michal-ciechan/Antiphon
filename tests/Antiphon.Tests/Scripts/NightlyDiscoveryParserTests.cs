using System.Reflection;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts;

[Category("Integration")]
public sealed class NightlyDiscoveryParserTests
{
    /// <summary>W2-nightly S15, in-process (was the scripts/test-nightly-tests.ps1 case): LF and CRLF multiline records parse completely.</summary>
    [Test]
    public void C1044_MultilineDiscovery()
    {
        using var fixture = NightlyFixture.Create("c1044-multiline");
        var expected = NightlyDiagnosticCases.C1044ExpectedNodes;
        // The census digest's UID order is the compiled SortCensusUids (OrdinalIgnoreCase, Ordinal tie-break).
        var expectedDigest = TestPhaseRecords.Sha256Text(NightlyDocuments.CanonicalJson(new System.Collections.Specialized.OrderedDictionary
        {
            { "required", PlannedManifestCheck.SortCensusUids(expected.Where(n => n.Type != "Manual").Select(n => n.Uid).ToArray()) },
            { "excluded", PlannedManifestCheck.SortCensusUids(expected.Where(n => n.Type == "Manual").Select(n => n.Uid).ToArray()) },
        }));
        var rows = new NightlyCaseRows();
        foreach (var variant in new[] { "LF", "CRLF" })
        {
            var path = NightlyDiagnosticCases.WriteDiagnostic(fixture.Root, variant, MultilineText(variant == "LF" ? "\n" : "\r\n"));
            object[] nodes = [];
            var error = "";
            try { nodes = NightlyDiagnosticCases.ReadNodes(path, "discovery"); }
            catch (Exception ex) { error = ex.Message; }
            rows.Check(NightlyDiagnosticCases.C1044Inventory(nodes), "C1044 " + variant + " complete inventory", error);
            rows.Check(Metadata(nodes), "C1044 " + variant + " exact metadata");
            var census = NightlyCensus.Census(nodes, null, false);
            var excluded = NightlyDiagnosticCases.Items(census, "Excluded");
            rows.Check(NightlyDiagnosticCases.B(census, "Ok") && PsValue.Str(PsValue.Prop(census, "RequiredCount")) == "8" && PsValue.Str(PsValue.Prop(census, "ExcludedCount")) == "1"
                && excluded.Length > 0 && Str(excluded[0], "uid") == "C487.Probe.Manual.1.1.ManualOne.1.1.0", "C1044 " + variant + " dispositions");
            rows.Check(Str(census, "Digest") == expectedDigest, "C1044 " + variant + " sorted digest");
        }
        rows.ShouldAllPass(8);
    }

    /// <summary>
    /// W2-nightly S15, in-process (was the scripts/test-nightly-tests.ps1 case): orphan, partial and duplicate records refuse,
    /// and the discovery producer (NightlyDiscoveryProducer, the C# twin of Invoke-NightlyProduceDiscovery) leaves an absent
    /// output absent and an existing one unchanged when it refuses, and publishes a complete multiline inventory.
    /// </summary>
    [Test]
    public void C1044_DiscoveryReconciliation()
    {
        using var fixture = NightlyFixture.Create("c1044-reconcile");
        var rows = new NightlyCaseRows();
        var golden = File.ReadAllText(Path.Combine(NightlyDiagnosticCases.ProbeRoot, "discovery.diag"));
        const string orphan = "DiscoveredTestNodeStateProperty { Explanation = orphan }";
        // Orphans first: other malformed-record checks must not mask PC-2.
        Rejects(fixture, rows, "prefix orphan refuses", orphan + "\n" + golden, "discovery-count-mismatch discovered=10 parsed=9 path=");
        Rejects(fixture, rows, "suffix orphan refuses", golden + "\n" + orphan, "discovery-count-mismatch discovered=10 parsed=9 path=");
        const string partial = "TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }";
        Rejects(fixture, rows, "partial cannot borrow state", partial + "\n" + golden, "malformed discovery record: missing state");
        Rejects(fixture, rows, "partial cannot borrow identity", partial + ", DiscoveredTestNodeStateProperty { }" + "\n" + golden, "truncated diagnostic record uid=truncated");
        var record = Regex.Split(golden, "\r?\n").First(l => l.Contains("DisplayName = Plain,", StringComparison.Ordinal));
        Rejects(fixture, rows, "duplicate refuses", golden + "\n" + record, "duplicate discovery uid");

        var producerText = "";
        var launcher = new RecordingLauncher(launch =>
        {
            var args = launch.Arguments.ToList();
            File.WriteAllText(Path.Combine(args[args.IndexOf("--diagnostic-output-directory") + 1], "fixture.diag"), producerText);
            return new NightlyLaunchResult { ExitCode = 0, TimedOut = false, ChildrenExited = true };
        });
        var host = fixture.Host(launcher: launcher);
        var exe = fixture.PathOf("fixture.dll");
        File.WriteAllText(exe, "private assembly hash fixture");
        var output = fixture.PathOf("producer.json");
        var request = new NightlyDiscoveryRequest(exe, [], "", output, fixture.PathOf("producer-diag"), fixture.PathOf("producer.log"), fixture.Root, null);
        producerText = orphan + "\n" + golden;
        var error = NightlyDiagnosticCases.Refusal(() => NightlyDiscoveryProducer.Produce(host, request));
        rows.Check(Regex.IsMatch(error, "discovery-count-mismatch discovered=10 parsed=9", RegexOptions.IgnoreCase) && !File.Exists(output), "C1044 producer absent output stays absent", error);
        File.WriteAllText(output, "sentinel-existing-output");
        error = NightlyDiagnosticCases.Refusal(() => NightlyDiscoveryProducer.Produce(host, request));
        rows.Check(Regex.IsMatch(error, "discovery-count-mismatch discovered=10 parsed=9", RegexOptions.IgnoreCase) && File.ReadAllText(output) == "sentinel-existing-output",
            "C1044 producer sentinel unchanged", error);
        producerText = MultilineText("\n");
        NightlyDiscoveryProducer.Produce(host, request);
        var published = PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.ReadDiscovery(output), "Nodes"));
        rows.Check(Metadata(published), "C1044 producer publishes complete multiline inventory");
        rows.ShouldAllPass(8, "C1044 prefix orphan refuses", "C1044 suffix orphan refuses", "C1044 partial cannot borrow state", "C1044 partial cannot borrow identity",
            "C1044 duplicate refuses", "C1044 producer absent output stays absent", "C1044 producer sentinel unchanged", "C1044 producer publishes complete multiline inventory");
    }

    /// <summary>
    /// W2-nightly S15, in-process (was the scripts/test-nightly-tests.ps1 case): TUnit 1.44 GetClassTypeName puts constructor
    /// fixtures in TypeName, and generic or nested classes add '+', a backtick, and comma-separated arguments.
    /// </summary>
    [Test]
    public void C1044_FixtureTypeName()
    {
        using var fixture = NightlyFixture.Create("c1044-typename");
        const string fixtureType = "CardFilePolicyApiTests(CardFileSyncEndpointWebAppFactory)";
        const string generic = "Outer+Inner`1(FactoryA, FactoryB)";
        string[] records = [DiscoveryRecord("fixture-uid", fixtureType, "ListsFiles"), DiscoveryRecord("generic-uid", generic, "Runs")];
        var parsed = NightlyDiagnosticCases.Parse(fixture.Root, "fixture-typename", NightlyDiagnosticCases.StateText(records), "discovery");
        var fixtureNode = parsed.Nodes.Where(n => Str(n, "Uid") == "fixture-uid").ToArray();
        var genericNode = parsed.Nodes.Where(n => Str(n, "Uid") == "generic-uid").ToArray();
        var rows = new NightlyCaseRows();
        rows.Check(parsed.Nodes.Length == 2 && fixtureNode.Length == 1 && Str(fixtureNode[0], "Type") == fixtureType && Str(fixtureNode[0], "Method") == "ListsFiles"
            && genericNode.Length == 1 && Str(genericNode[0], "Type") == generic, "C1044 fixture and generic TypeName accepted", parsed.Error);
        var malformed = NightlyDiagnosticCases.StateText([.. records, "TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }"]);
        Rejects(fixture, rows, "fixture file stateless record refuses", malformed, "malformed discovery record: missing state");
        Rejects(fixture, rows, "empty TypeName still truncated", NightlyDiagnosticCases.StateText(DiscoveryRecord("empty-type", "", "Plain")), "truncated diagnostic record");
        rows.ShouldAllPass(3, "C1044 fixture and generic TypeName accepted", "C1044 fixture file stateless record refuses", "C1044 empty TypeName still truncated");
    }

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
    /// G087 already runs from the report harness default loop. These cases must run unattended, not only from -Case: the run
    /// harness default loop runs the prerequisite settle case, and the fixture class chunks (in-process since W2-nightly
    /// S11), the fixture type name (S15) and the prerequisite gate (S15, NightlyPrerequisiteTests) are tests.
    /// </summary>
    [Test]
    public void C1044_UnattendedCases()
    {
        var scripts = Path.Combine(Repo, "scripts");
        var runElse = ElseText(Path.Combine(scripts, "test-nightly-run.ps1"), "foreach ($fn in $cases)");
        var missing = new List<string>();
        if (runElse.IndexOf("Test-C487_PrerequisiteSettle", StringComparison.Ordinal) < 0) missing.Add("Test-C487_PrerequisiteSettle");
        foreach (var (type, name) in new[] { (typeof(NightlyDiscoveryParserTests), nameof(C1044_FixtureClassChunks)), (typeof(NightlyDiscoveryParserTests), nameof(C1044_FixtureTypeName)),
                     (typeof(NightlyPrerequisiteTests), nameof(NightlyPrerequisiteTests.C487_PrerequisiteGate)) })
            if (type.GetMethod(name)?.GetCustomAttribute<TestAttribute>() == null) missing.Add(name);
        var rows = new NightlyCaseRows();
        rows.Check(missing.Count == 0, "C1044 unattended harness loop runs fixture and prerequisite cases", string.Join(",", missing));
        rows.ShouldAllPass(1);
    }

    /// <summary>W2-nightly S15, in-process (was the scripts/test-nightly-tests.ps1 case): the parser against the golden probe and its variants.</summary>
    [Test]
    public void C1044_ParserCompatibility()
    {
        using var fixture = NightlyFixture.Create("c1044-compat");
        var root = NightlyDiagnosticCases.ProbeRoot;
        var rows = new NightlyCaseRows();
        var golden = File.ReadAllText(Path.Combine(root, "discovery.diag"));
        var nodes = NightlyDiagnosticCases.ReadNodes(Path.Combine(root, "discovery.diag"), "discovery");
        rows.Check(NightlyDiagnosticCases.C1044Inventory(nodes), "C1044 golden discovery inventory");
        rows.Check(Metadata(nodes), "C1044 golden discovery metadata");
        var execText = File.ReadAllText(Path.Combine(root, "execution.diag"));
        var execution = NightlyDiagnosticCases.ReadNodes(Path.Combine(root, "execution.diag"), "execution");
        rows.Check(NightlyDiagnosticCases.C1044Inventory(execution, execution: true), "C1044 golden terminal inventory");
        var withDiscovery = NightlyDiagnosticCases.WriteDiagnostic(fixture.Root, "nonterminal", execText + "\n" + golden);
        var filtered = NightlyDiagnosticCases.ReadNodes(withDiscovery, "execution");
        rows.Check(NightlyDiagnosticCases.C1044Inventory(filtered, execution: true) && !filtered.Any(n => Str(n, "State") is "InProgress" or "Discovered"), "C1044 nonterminal updates filtered");
        var prefixed = Regex.Replace(golden, "(?m)^TestNodeUpdateMessage", "2026-10-04T12:00:00.000+00:00 Pinned.Logger INFORMATION TestNodeUpdateMessage");
        rows.Check(Metadata(NightlyDiagnosticCases.ReadNodes(NightlyDiagnosticCases.WriteDiagnostic(fixture.Root, "prefixed", prefixed), "discovery")), "C1044 prefixed envelopes accepted");
        var multilineExec = execText.Replace("DisplayName = Plain,", "DisplayName = Plain { },\n\ncontinued [, name,").TrimEnd('\r', '\n');
        rows.Check(NightlyDiagnosticCases.C1044Inventory(NightlyDiagnosticCases.ReadNodes(NightlyDiagnosticCases.WriteDiagnostic(fixture.Root, "execution-multiline", multilineExec), "execution"),
            execution: true), "C1044 multiline terminal inventory");
        var jsonPath = fixture.PathOf("roundtrip.json");
        NightlyDiagnosticLog.WriteDiscoveryDocument(jsonPath, NightlyDiagnosticLog.NewDiscoveryDocument(nodes, "fixture-hash"));
        var roundtrip = NightlyDiagnosticLog.Read(jsonPath, "discovery");
        rows.Check(Metadata(PsValue.AsArray(PsValue.Prop(roundtrip, "Nodes"))) && Str(roundtrip, "AssemblyHash") == "fixture-hash", "C1044 discovery JSON roundtrip");
        var manual = nodes.First(n => Str(n, "Type") == "Manual");
        rows.Check(Str(NightlyCensus.Disposition(manual, null, false), "Disposition") == "excluded", "C1044 default OptIn excluded");
        rows.Check(Str(NightlyCensus.Disposition(manual, null, true), "Disposition") == "required", "C1044 profile OptIn required");
        object[] exclusions = [new PlainObject(("Class", "Manual"), ("Methods", new object[] { "ManualOne" }), ("Reason", "manual fixture"), ("Owner", "C1044"))];
        var disposition = NightlyCensus.Disposition(manual, exclusions, true);
        rows.Check(Str(disposition, "Disposition") == "excluded" && Str(disposition, "Reason") == "manual fixture" && Str(disposition, "Owner") == "C1044", "C1044 explicit profile exclusion");
        Rejects(fixture, rows, "unsupported TUnit refuses", golden.Replace("Version: '1.44.0.0'", "Version: '9.0.0.0'"), "version-drift");
        Rejects(fixture, rows, "unsupported MTP refuses", golden.Replace("2.2.2", "9.0.0"), "version-drift");
        var record = Regex.Split(golden, "\r?\n").First(l => l.Contains("DisplayName = Plain,", StringComparison.Ordinal));
        Rejects(fixture, rows, "compatibility duplicate refuses", golden + "\n" + record, "duplicate discovery uid");
        Rejects(fixture, rows, "missing type refuses", golden.Replace("TypeName = Ordinary", "TypeName = "), "truncated diagnostic record");
        Rejects(fixture, rows, "missing method refuses", golden.Replace("MethodName = Plain", "MethodName = "), "truncated diagnostic record");
        var headers = string.Join("\n", Regex.Split(golden, "\r?\n").Where(l => !l.Contains("TestNodeUpdateMessage", StringComparison.Ordinal)));
        Rejects(fixture, rows, "garbage refuses", headers + "\n" + "garbage payload", "unknown diagnostic format");
        Rejects(fixture, rows, "header only refuses", headers, "unknown diagnostic format");
        rows.ShouldAllPass(17);
    }

    /// <summary>W2-nightly S12, in-process: qualified bare names only; a shared short name must not assign the other namespace.</summary>
    [Test]
    public void C1391_ClassMatch()
    {
        const string appSuffix = "Antiphon.Tests.Application.Widget(Factory)";
        const string agentsSuffix = "Antiphon.Tests.Agents.Widget(Factory)";
        const string requiredApp = "Antiphon.Tests.Application.Widget";
        const string requiredAgents = "Antiphon.Tests.Agents.Widget";
        var sameNs = NightlyCensus.ClassAssigned(appSuffix, "", [requiredApp]);
        var otherNs = NightlyCensus.ClassAssigned(agentsSuffix, "", [requiredApp]);
        var unqualified = NightlyCensus.ClassAssigned(appSuffix, "", ["Widget"]);
        var present = NightlyChunkVerdict.RequiredClassesPresent([new PlainObject(("ClassName", appSuffix))], [requiredApp, requiredAgents]);
        var missing = NightlyDiagnosticCases.Items(present, "Missing").Select(PsValue.Str).ToArray();
        var presentOk = NightlyDiagnosticCases.B(present, "Ok");
        var ok = !otherNs && !unqualified && sameNs && !presentOk && missing.Contains(requiredAgents, StringComparer.Ordinal) && !missing.Contains(requiredApp, StringComparer.Ordinal);
        var rows = new NightlyCaseRows();
        rows.Check(ok, "C1391 class match uses the qualified bare name",
            "sameNs=" + sameNs + " otherNs=" + otherNs + " unqualified=" + unqualified + " ok=" + presentOk + " missing=" + string.Join(",", missing));
        rows.ShouldAllPass(1);
    }

    /// <summary>W2-nightly S12, in-process: records on one line split, and a truncated record cannot borrow the next one's fields.</summary>
    [Test]
    public void C1391_SplicedRecord()
    {
        using var fixture = NightlyFixture.Create("c1391");
        var first = NightlyDiagnosticCases.DiscoveryRecord("uid-a", "Alpha", "One");
        var second = NightlyDiagnosticCases.DiscoveryRecord("uid-b", "Beta", "Two");
        var discovery = NightlyDiagnosticCases.Parse(fixture.Root, "c1391-two-discovery", NightlyDiagnosticCases.Header + first + second, "discovery");
        var discoveryOk = discovery.Error == "" && discovery.Nodes.Length == 2
            && discovery.Nodes.Count(n => Str(n, "Uid") == "uid-a" && Str(n, "Type") == "Alpha") == 1 && discovery.Nodes.Count(n => Str(n, "Uid") == "uid-b" && Str(n, "Type") == "Beta") == 1;
        var passedA = first.Replace("DiscoveredTestNodeStateProperty", "PassedTestNodeStateProperty");
        var passedB = second.Replace("DiscoveredTestNodeStateProperty", "PassedTestNodeStateProperty");
        var exec = NightlyDiagnosticCases.Parse(fixture.Root, "c1391-two-passed", NightlyDiagnosticCases.Header + passedA + passedB);
        var execOk = exec.Error == "" && exec.Nodes.Length == 2
            && exec.Nodes.Count(n => Str(n, "Uid") == "uid-a" && Str(n, "Outcome") == "Passed") == 1 && exec.Nodes.Count(n => Str(n, "Uid") == "uid-b" && Str(n, "Outcome") == "Passed") == 1;
        const string truncated = "TestNodeUpdateMessage { TestNode = TestNode { Uid = TestNodeUid { Value = truncated }";
        var spliced = NightlyDiagnosticCases.Parse(fixture.Root, "c1391-splice", NightlyDiagnosticCases.Header + truncated + first, "discovery");
        var borrowed = spliced.Nodes.Count(n => Str(n, "Uid") == "truncated" || Str(n, "Type") == "Alpha");
        var spliceOk = System.Text.RegularExpressions.Regex.IsMatch(spliced.Error, "truncated diagnostic record|malformed discovery record", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            && spliced.Nodes.Length == 0 && borrowed == 0;
        var rows = new NightlyCaseRows();
        rows.Check(discoveryOk && execOk && spliceOk, "C1391 same-line records split and a truncated splice does not borrow",
            "discovery=" + discoveryOk + " discErr=" + discovery.Error + " nodes=" + discovery.Nodes.Length + " exec=" + execOk + " execErr=" + exec.Error + " execNodes=" + exec.Nodes.Length
            + " splice=" + spliceOk + " spliceErr=" + spliced.Error + " spliceNodes=" + spliced.Nodes.Length);
        rows.ShouldAllPass(1);
    }

    /// <summary>W2-nightly S12, in-process: an unbalanced or bare-comma TypeName refuses; balanced fixture and generic names parse.</summary>
    [Test]
    public void C1391_OddTypeName()
    {
        using var fixture = NightlyFixture.Create("c1391");
        var rows = new NightlyCaseRows();
        void Rejects(string name, string text, string pattern)
        {
            var path = NightlyDiagnosticCases.WriteDiagnostic(fixture.Root, name.Replace(' ', '-'), text);
            var error = NightlyDiagnosticCases.Refusal(() => NightlyDiagnosticLog.Read(path, "discovery"));
            rows.Check(System.Text.RegularExpressions.Regex.IsMatch(error, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase), "C1044 " + name, error);
        }
        Rejects("C1391 unbalanced X(Fac refuses", NightlyDiagnosticCases.StateText(NightlyDiagnosticCases.DiscoveryRecord("odd-open", "X(Fac", "Runs")), "truncated diagnostic record");
        Rejects("C1391 lone comma refuses", NightlyDiagnosticCases.StateText(NightlyDiagnosticCases.DiscoveryRecord("odd-comma", ",", "Runs")), "truncated diagnostic record");
        const string balanced = "X(Fac)";
        const string generic = "Outer+Inner`1(FactoryA, FactoryB)";
        var parsed = NightlyDiagnosticCases.Parse(fixture.Root, "c1391-odd-balanced", NightlyDiagnosticCases.StateText(
            NightlyDiagnosticCases.DiscoveryRecord("balanced-uid", balanced, "Runs"), NightlyDiagnosticCases.DiscoveryRecord("generic-uid", generic, "Runs")), "discovery");
        var balancedNode = parsed.Nodes.Where(n => Str(n, "Uid") == "balanced-uid").ToArray();
        var genericNode = parsed.Nodes.Where(n => Str(n, "Uid") == "generic-uid").ToArray();
        rows.Check(parsed.Nodes.Length == 2 && balancedNode.Length == 1 && Str(balancedNode[0], "Type") == balanced && genericNode.Length == 1 && Str(genericNode[0], "Type") == generic,
            "C1391 balanced fixture TypeName still parses", parsed.Error);
        rows.ShouldAllPass(3, "C1044 C1391 unbalanced X(Fac refuses", "C1044 C1391 lone comma refuses", "C1391 balanced fixture TypeName still parses");
    }

    /// <summary>Get-C1044MultilineText: Plain follows SlowTwo: adjacent multiline records, blank continuation, punctuation and braces, and Manual at EOF without a newline.</summary>
    private static string MultilineText(string newline)
    {
        var text = File.ReadAllText(Path.Combine(NightlyDiagnosticCases.ProbeRoot, "discovery.diag"));
        foreach (var name in new[] { "SlowTwo", "Plain", "ManualOne" })
            text = text.Replace("DisplayName = " + name + ",", "DisplayName = " + name + " { punctuation }," + newline + newline + "continued }, [, name,");
        return text.TrimEnd('\r', '\n');
    }

    /// <summary>Test-C1044Metadata: the complete inventory with each node's type, method, namespace, class, assembly, state and categories.</summary>
    private static bool Metadata(object[] nodes)
    {
        if (!NightlyDiagnosticCases.C1044Inventory(nodes)) return false;
        foreach (var expected in NightlyDiagnosticCases.C1044ExpectedNodes)
        {
            var node = nodes.First(n => Str(n, "Uid") == expected.Uid);
            if (Str(node, "Type") != expected.Type || Str(node, "Method") != expected.Method || Str(node, "Namespace") != "C487.Probe"
                || Str(node, "ClassName") != "C487.Probe." + expected.Type || Str(node, "Assembly") != "Probe" || Str(node, "State") != "Discovered"
                || string.Join("|", NightlyDiagnosticCases.Items(node, "Categories").Select(PsValue.Str)) != expected.Category) return false;
        }
        return true;
    }

    /// <summary>Assert-C1044Rejects: the text, written as &lt;name&gt;.diag, refuses with a message matching the pattern.</summary>
    private static void Rejects(NightlyFixture fixture, NightlyCaseRows rows, string name, string text, string pattern)
    {
        var path = NightlyDiagnosticCases.WriteDiagnostic(fixture.Root, name.Replace(' ', '-'), text);
        var error = NightlyDiagnosticCases.Refusal(() => NightlyDiagnosticLog.Read(path, "discovery"));
        rows.Check(Regex.IsMatch(error, pattern, RegexOptions.IgnoreCase), "C1044 " + name, error);
    }

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
