using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>One C1325 parser fixture (New-C1325Fixture): its record and the node it must parse to.</summary>
public sealed record C1325Fixture(string Id, string Record, string? ExpectedMessage, string Uid, string ClassName, string Method, string Outcome = "Failed");

/// <summary>One CARD-1375 selected byte range (base64, its length and SHA-256).</summary>
public sealed record C1375Part(string Base64, int Length, string Sha256);

/// <summary>One CARD-1375 fixture: the state and its five byte ranges.</summary>
public sealed record C1375Fixture(string State, IReadOnlyDictionary<string, C1375Part> Parts);

/// <summary>
/// W2-nightly S12: the diagnostic inputs the D13, C1044 and C1391 cases of scripts/test-nightly-tests.ps1 built, in-process.
/// The data (the D13 capture, the C1325 inputs, the C1375 bytes) is scripts/fixtures/nightly/d13-diagnostic-records.json,
/// captured once from the PowerShell fixtures; the C1325 derivation recipe (New-C1325CapturedDerivative and the fixture
/// lists) is ported here with its guards.
/// </summary>
public static class NightlyDiagnosticCases
{
    private static readonly Lazy<JsonElement> Data = new(() =>
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(NightlyPlanCases.Repo, "scripts", "fixtures", "nightly", "d13-diagnostic-records.json")));
        return doc.RootElement.Clone();
    });

    /// <summary>The c487 probe fixtures (discovery.diag, execution.diag, all.trx).</summary>
    public static string ProbeRoot { get; } = Path.Combine(NightlyPlanCases.Repo, "scripts", "fixtures", "nightly", "c487-probe");

    /// <summary>$D13FailureRecord: one native TUnit 1.44/MTP 2.2.2 Error update.</summary>
    public static string D13FailureRecord => Data.Value.GetProperty("d13FailureRecord").GetString()!;

    /// <summary>$D13FailureTrx: its TRX record.</summary>
    public static string D13FailureTrx => Data.Value.GetProperty("d13FailureTrx").GetString()!;

    /// <summary>$C1325OriginalMessage.</summary>
    public static string C1325OriginalMessage => Data.Value.GetProperty("c1325OriginalMessage").GetString()!;

    /// <summary>$C1325Uid.</summary>
    public static string C1325Uid => Data.Value.GetProperty("c1325Uid").GetString()!;

    /// <summary>The TUnit/MTP version header every built diagnostic starts with.</summary>
    public const string Header = "Version: '1.44.0.0'\nVersion: 2.2.2\n";

    /// <summary>Get-D13FailureText.</summary>
    public static string D13FailureText() => Header + D13FailureRecord;

    /// <summary>Get-D13StateText / Get-C1044DiscoveryText: the header and the records, one per line.</summary>
    public static string StateText(params string[] records) => Header + string.Join("\n", records);

    /// <summary>Get-C1375DiagnosticFixtures.</summary>
    public static IReadOnlyList<C1375Fixture> C1375Fixtures() => Data.Value.GetProperty("c1375").EnumerateArray().Select(f => new C1375Fixture(
        f.GetProperty("state").GetString()!,
        f.GetProperty("parts").EnumerateObject().ToDictionary(p => p.Name, p => new C1375Part(p.Value.GetProperty("base64").GetString()!,
            p.Value.GetProperty("length").GetInt32(), p.Value.GetProperty("sha256").GetString()!)))).ToArray();

    /// <summary>New-D13StateRecord: one execution update in the given state (an exception for a failing state, a timeout for Timeout).</summary>
    public static string D13StateRecord(string uid, string typeName, string method, string stateName, string explanation, string exceptionText = "System.Exception: outer")
    {
        var state = stateName + "TestNodeStateProperty { Explanation = " + explanation;
        if (stateName is not ("Passed" or "Skipped" or "Discovered" or "InProgress")) state += ", Exception = " + exceptionText;
        if (stateName == "Timeout") state += ", Timeout = 00:00:00.0250000";
        state += " }";
        return "TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = "
            + uid + " }, DisplayName = " + method + ", Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Probe, TypeName = "
            + typeName + ", MethodName = " + method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, " + state + " ] } }";
    }

    /// <summary>New-C1044DiscoveryRecord: one discovery update for a TypeName and method (namespace Antiphon.Tests.Application).</summary>
    public static string DiscoveryRecord(string uid, string typeName, string method) =>
        "TestNodeUpdateMessage { DisplayName = TestNode update, Description = This data is used to report a TestNode state change., Properties = [], ParentTestNodeUid = <null>, TestNode = TestNode { Uid = TestNodeUid { Value = "
        + uid + " }, DisplayName = " + method + ", Properties = [ TestMethodIdentifierProperty { AssemblyFullName = Probe, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null, Namespace = Antiphon.Tests.Application, TypeName = "
        + typeName + ", MethodName = " + method + ", MethodArity = 0, ParameterTypeFullNames = [], ReturnTypeFullName = System.Void }, DiscoveredTestNodeStateProperty { Explanation =  } ] } }";

    /// <summary>Write-C1044Diagnostic: the text in &lt;root&gt;/&lt;name&gt;.diag (UTF-8 without a byte-order mark).</summary>
    public static string WriteDiagnostic(string root, string name, string text)
    {
        var path = Path.Combine(root, name + ".diag");
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>@((NightlyDiagnosticLog.Read(path, kind)).Nodes).</summary>
    public static object[] ReadNodes(string path, string kind) => PsValue.AsArray(PsValue.Prop(NightlyDiagnosticLog.Read(path, kind), "Nodes"));

    /// <summary>ConvertFrom-D13StateText: the nodes the text parses to, or none and the refusal text.</summary>
    public static (object[] Nodes, string Error) Parse(string root, string name, string text, string kind = "execution")
    {
        var path = WriteDiagnostic(root, name, text);
        try { return (ReadNodes(path, kind), ""); }
        catch (Exception ex) { return ([], ex.Message); }
    }

    /// <summary>Get-NightlyRefusalText of an action: the refusal's text, or "" when it is accepted.</summary>
    public static string Refusal(Action action)
    {
        try { action(); }
        catch (Exception ex) { return ex.Message; }
        return "";
    }

    /// <summary>[string]$node.name.</summary>
    public static string S(object? node, string name) => PsValue.Str(PsValue.Prop(node, name));

    /// <summary>$node.PSObject.Properties.Name -contains name (case-insensitive) / -ccontains (case-sensitive).</summary>
    public static bool HasProperty(object? node, string name, bool caseSensitive = false) =>
        node != null && PsValue.PropertyNames(node).Contains(name, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    /// <summary>@($o.name) as objects.</summary>
    public static object[] Items(object? o, string name) => PsValue.AsArray(PsValue.Prop(o, name));

    /// <summary>$o.name -contains value (case-insensitive).</summary>
    public static bool Contains(object? o, string name, string value) => Items(o, name).Any(item => string.Equals(PsValue.Str(item), value, StringComparison.OrdinalIgnoreCase));

    /// <summary>[bool]$o.name.</summary>
    public static bool B(object? o, string name) => PsValue.IsTrue(PsValue.Prop(o, name));

    /// <summary>A discovery document of one node (Write-C487Discovery with { uid, namespace, type, method, state = Discovered }).</summary>
    public static string WriteOneNodeDiscovery(string path, string uid, string ns, string type, string method)
    {
        NightlyFixture.WriteDiscovery(path, [new NightlyTestNode(uid, type, method, ns, "Discovered")]);
        return path;
    }

    // ---- C1044 expected inventory --------------------------------------------------------------------------------

    /// <summary>Get-C1044ExpectedNodes: the probe's independent identities (Uid, Type, Method, Category).</summary>
    public static IReadOnlyList<(string Uid, string Type, string Method, string Category)> C1044ExpectedNodes { get; } = new[]
    {
        ("SlowCases", "SlowOne", "SlowOne.1.1.0", "Slow"), ("SlowCases", "SlowTwo", "SlowTwo.1.1.0", "Slow"), ("Ordinary", "Plain", "Plain.1.1.0", ""),
        ("Ordinary", "Rows", "Rows(System.Int32).1.1.0", ""), ("Ordinary", "Rows", "Rows(System.Int32).2.1.0", ""), ("Ordinary", "Data", "Data(System.Int32).1.1.0", ""),
        ("Ordinary", "Data", "Data(System.Int32).1.2.0", ""), ("Ordinary", "Inherited", "Inherited.1.1.0", ""), ("Manual", "ManualOne", "ManualOne.1.1.0", "OptIn"),
    }.Select(r => ("C487.Probe." + r.Item1 + ".1.1." + r.Item3, r.Item1, r.Item2, r.Item4)).ToArray();

    /// <summary>Test-C1044Inventory: the nodes' UIDs are exactly the expected ones (without Manual for an execution).</summary>
    public static bool C1044Inventory(object[] nodes, bool execution = false)
    {
        var expected = C1044ExpectedNodes.Where(n => !execution || n.Type != "Manual").Select(n => n.Uid).OrderBy(u => u, NightlyPlanCases.SortObject).ToArray();
        var actual = nodes.Select(n => S(n, "Uid")).OrderBy(u => u, NightlyPlanCases.SortObject).ToArray();
        return actual.Length == expected.Length && string.Join("|", actual) == string.Join("|", expected);
    }

    // ---- the C1325 recipe ----------------------------------------------------------------------------------------

    /// <summary>
    /// New-C1325CapturedDerivative: the D13 capture (LF) with its three message copies replaced; refuses when the capture
    /// no longer has three copies and one outer Error, or the result does not keep all three.
    /// </summary>
    public static string CapturedDerivative(string message)
    {
        var original = C1325OriginalMessage;
        var capture = D13FailureRecord.Replace("\r\n", "\n");
        if (Regex.Matches(capture, Regex.Escape(original)).Count != 3 || Regex.Matches(capture, @"ErrorTestNodeStateProperty \{").Count != 1)
            throw new InvalidOperationException("C1325 capture recipe changed: expected three message copies and one outer Error");
        var record = capture.Replace(original, message);
        if (Regex.Matches(record, Regex.Escape(message)).Count != 3) throw new InvalidOperationException("C1325 derivative did not retain all three message copies");
        return record;
    }

    private static C1325Fixture Fixture(string id, string record, string? expected, bool shortIdentity = false) => shortIdentity
        ? new C1325Fixture(id, record, expected, "Probe.C1325.1.1.Only.1.1.0", "Probe.C1325", "Only")
        : new C1325Fixture(id, record, expected, C1325Uid, "Antiphon.E2E.AgentTaskLandDeliveryE2ETests", "C488_ApprovalPollingCannotConfirm");

    /// <summary>Get-C1325ExplanationFixtures (E01-E11).</summary>
    public static IReadOnlyList<C1325Fixture> C1325ExplanationFixtures()
    {
        var list = new List<C1325Fixture>();
        var message = "ShouldAssertException: boom, Exception = INNER still explanation";
        list.Add(Fixture("E01", CapturedDerivative(message), "[Test Failure] " + message));
        message = "boom, Exception = alpha, Exception = beta\ncontinued";
        list.Add(Fixture("E02", CapturedDerivative(message), "[Test Failure] " + message));
        list.Add(Fixture("E03", CapturedDerivative(message).Replace("\n", "\r\n"), "[Test Failure] " + message.Replace("\n", "\r\n")));
        message = "boom, Exception = TUnit.Engine.Exceptions.TestFailedException: decoy { }";
        list.Add(Fixture("E04", CapturedDerivative(message), "[Test Failure] " + message));
        list.Add(Fixture("E05", D13FailureRecord.Replace("\r\n", "\n"), "[Test Failure] ShouldAssertException: OperatingSystem.IsWindows()\n    should be\nTrue\n    but was\nFalse"));
        foreach (var (id, state, explanation, exception) in new[]
        {
            ("E06", "Error", "null exception body", "<null>"),
            ("E07", "Timeout", "timed out, Exception = detail", "System.TimeoutException: timed out"),
            ("E08", "Cancelled", "cancelled by the run", "System.OperationCanceledException: cancelled"),
        })
            list.Add(Fixture(id, D13StateRecord("Probe.C1325.1.1.Only.1.1.0", "C1325", "Only", state, explanation, exception), explanation, shortIdentity: true));
        message = "ShouldAssertException: boom, Exception = INNER still explanation";
        list.Add(Fixture("E09", CapturedDerivative(message).Replace("Exception = TUnit.Engine.Exceptions.TestFailedException: " + message,
            "Exception = TUnit.Engine.Exceptions.TestFailedException: mismatched echo"), null));
        message = "  boom\t\n\ntail  \r\nlast  ";
        list.Add(Fixture("E10", CapturedDerivative(message), "[Test Failure] " + message));
        // Two valid echo/stack splits: neither unescaped stack tail can prove which delimiter was the field.
        message = "boom, Exception = TUnit.Engine.Exceptions.TestFailedException: boom\n   at Fixture.Frame()";
        list.Add(Fixture("E11", CapturedDerivative(message), null));
        return list;
    }

    /// <summary>Get-C1325StateFixtures (S01-S12).</summary>
    public static IReadOnlyList<C1325Fixture> C1325StateFixtures()
    {
        var list = new List<C1325Fixture>();
        const string quoted = "saw PassedTestNodeStateProperty { Explanation = decoy }\nouter line";
        foreach (var (id, word) in new[] { ("S01", "Passed"), ("S02", "Skipped"), ("S03", "Discovered"), ("S04", "InProgress") })
        {
            var message = quoted.Replace("Passed", word);
            list.Add(Fixture(id, CapturedDerivative(message), "[Test Failure] " + message));
        }
        var inner = "saw FailedTestNodeStateProperty { Explanation = inner";
        list.Add(Fixture("S05", CapturedDerivative(inner), "[Test Failure] " + inner));
        var ordinary = "[Test Failure] " + C1325OriginalMessage;
        var lf = D13FailureRecord.Replace("\r\n", "\n");
        list.Add(Fixture("S06", lf.Replace(" } ] } }", "\n   at Fixture.PassedTestNodeStateProperty { Explanation = decoy } ] } }"), ordinary));
        list.Add(Fixture("S07", lf.Replace("TrxExceptionProperty { Message = ", "TrxExceptionProperty { Message = PassedTestNodeStateProperty { Explanation = decoy }\n"), ordinary));
        list.Add(Fixture("S08", lf.Replace(", DisplayName = C488_ApprovalPollingCannotConfirm, Properties = [",
            ", DisplayName = FailedTestNodeStateProperty { Explanation = decoy }, Properties = ["), ordinary));
        list.Add(Fixture("S09", CapturedDerivative(quoted).Replace("\n", "\r\n"), "[Test Failure] " + quoted.Replace("\n", "\r\n")));
        list.Add(Fixture("S10", D13StateRecord("Probe.C1325.1.1.Only.1.1.0", "C1325", "Only", "Error", "[Test Failure] " + quoted,
            "TUnit.Engine.Exceptions.TestFailedException: " + quoted), "[Test Failure] " + quoted, shortIdentity: true));
        var braces = "ordinary { balanced } and unmatched } { { braces";
        list.Add(Fixture("S11", CapturedDerivative(braces), "[Test Failure] " + braces));
        list.Add(Fixture("S12", "2026-10-04T12:00:00.000+00:00 Pinned.Logger INFORMATION " + CapturedDerivative(quoted), "[Test Failure] " + quoted));
        return list;
    }

    /// <summary>Get-C1325AmbiguousStateRecord: the Error with a complete identifier-to-Passed transition quoted in its stack.</summary>
    public static string C1325AmbiguousStateRecord() =>
        D13FailureRecord.Replace("\r\n", "\n").Replace(" } ] } }", "\n   at Fixture.Frame() quoting " + Data.Value.GetProperty("c1325AmbiguousTransition").GetString() + " } ] } }");
}
