using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyDiagnosticCases;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// The D13 native execution diagnostic cases (TUnit 1.44 / MTP 2.2.2 Error, Timeout and Cancelled terminals, the
/// CARD-1325 explanation and state-property boundaries), in-process since W2-nightly S12: the scripts/test-nightly-tests.ps1
/// cases of the same names, their rows unchanged, against the compiled diagnostic reader and native verdict.
/// </summary>
[Category("Integration")]
public sealed class NightlyExecutionDiagnosticTests
{
    private static object Verdict(string trx, string discovery, string execution, string root, int exit, string sha) =>
        NightlyChunkVerdict.NativeSuiteVerdict(trx, discovery, execution, root, DateTime.UtcNow.AddMinutes(-1), exit, sha, sha, "test", "test", "test", "test",
            [], null, false, null, "", null);

    [Test]
    public void D13_RealTimeoutCancelled()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        foreach (var real in C1375Fixtures())
        {
            var state = real.State;
            var decoded = new Dictionary<string, string>();
            var bytesOk = true;
            foreach (var name in new[] { "MtpHeader", "TunitHeader", "Record", "Results", "Definitions" })
            {
                var part = real.Parts[name];
                var bytes = Convert.FromBase64String(part.Base64);
                var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                var text = Encoding.UTF8.GetString(bytes);
                bytesOk = bytesOk && bytes.Length == part.Length && hash == part.Sha256 && Convert.ToBase64String(Encoding.UTF8.GetBytes(text)) == part.Base64;
                decoded[name] = text;
            }
            rows.Check(bytesOk, $"D13 real {state} pinned bytes");
            // Independent semantic oracle: never derive expectations from the parser or from the captured record being tested.
            const string cls = "C1375.Probe.C1375Producer";
            var timeout = state == "Timeout";
            var uid = timeout ? "C1375.Probe.C1375Producer.1.1.TimeoutProbe(System.Threading.CancellationToken).1.1.0" : "C1375.Probe.C1375Producer.1.1.CancelledProbe.1.1.0";
            var method = timeout ? "TimeoutProbe" : "CancelledProbe";
            var property = timeout ? "TimeoutTestNodeStateProperty" : "CancelledTestNodeStateProperty";
            var expectedMessage = timeout ? "[Timeout] Test timed out after 100ms" : null;
            rows.Check(decoded["Record"].Contains(property + " {") && decoded["Record"].Contains("TestNodeUid { Value = " + uid + " }")
                && decoded["Record"].Contains("Namespace = C1375.Probe, TypeName = C1375Producer, MethodName = " + method + ","), $"D13 real {state} native identity");
            var path = WriteDiagnostic(fixture.Root, "c1375-" + state, decoded["MtpHeader"] + decoded["TunitHeader"] + decoded["Record"]);
            var nodes = ReadNodes(path, "execution");
            rows.Check(nodes.Length == 1 && S(nodes[0], "Uid") == uid && S(nodes[0], "ClassName") == cls && S(nodes[0], "Method") == method && S(nodes[0], "Outcome") == "Failed",
                $"D13 real {state} parsed identity");
            var messageOk = nodes.Length == 1 && (expectedMessage == null ? !HasProperty(nodes[0], "Message", caseSensitive: true) : S(nodes[0], "Message") == expectedMessage);
            rows.Check(messageOk, $"D13 real {state} exact message");
            var trx = fixture.PathOf("c1375-" + state + ".trx");
            File.WriteAllText(trx, "<TestRun xmlns=\"http://microsoft.com/schemas/VisualStudio/TeamTest/2010\">" + decoded["Results"] + decoded["Definitions"] + "</TestRun>");
            var trxRows = NightlyTrx.ReadIdentities(trx);
            var discovery = WriteOneNodeDiscovery(fixture.PathOf("c1375-" + state + ".json"), uid, "C1375.Probe", "C1375Producer", method);
            var verdict = Verdict(trx, discovery, path, fixture.Root, 2, "c1375");
            var terminal = Items(verdict, "terminalNodes");
            rows.Check(trxRows.Length == 1 && S(trxRows[0], "ClassName") == cls && S(trxRows[0], "MethodName") == method && S(trxRows[0], "Outcome") == "Failed"
                && B(verdict, "coverageComplete") && !B(verdict, "testsPassed") && Contains(verdict, "reasons", "failed-row")
                && terminal.Length == 1 && S(terminal[0], "Uid") == uid && S(terminal[0], "Outcome") == "Failed",
                $"D13 real {state} complete failed verdict", string.Join(",", Items(verdict, "reasons").Select(PsValue.Str)));
        }
        rows.ShouldAllPass(10, new[] { "Timeout", "Cancelled" }.SelectMany(state => new[]
        {
            $"D13 real {state} pinned bytes", $"D13 real {state} native identity", $"D13 real {state} parsed identity",
            $"D13 real {state} exact message", $"D13 real {state} complete failed verdict",
        }).ToArray());
    }

    [Test]
    public void D13_ErrorTerminal()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        const string uid = "Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0";
        const string cls = "Antiphon.E2E.AgentTaskLandDeliveryE2ETests";
        const string method = "C488_ApprovalPollingCannotConfirm";
        foreach (var style in new[] { "LF", "CRLF" })
        {
            var nl = style == "LF" ? "\n" : "\r\n";
            var text = D13FailureText().Replace("\r\n", "\n").Replace("\n", nl);
            var path = WriteDiagnostic(fixture.Root, "d13-" + style, text);
            var parsed = ReadNodes(path, "execution");
            rows.Check(parsed.Length == 1 && S(parsed[0], "Uid") == uid && S(parsed[0], "ClassName") == cls && S(parsed[0], "Method") == method && S(parsed[0], "Outcome") == "Failed",
                "D13 " + style + " failure identity");
            var expected = "[Test Failure] ShouldAssertException: OperatingSystem.IsWindows()" + nl + "    should be" + nl + "True" + nl + "    but was" + nl + "False";
            rows.Check(parsed.Length == 1 && S(parsed[0], "Message") == expected, "D13 " + style + " failure message");
            var trx = fixture.PathOf("d13-" + style + ".trx");
            File.WriteAllText(trx, D13FailureTrx);
            var trxRows = NightlyTrx.ReadIdentities(trx);
            rows.Check(trxRows.Length == 1 && S(trxRows[0], "ClassName") == cls && S(trxRows[0], "MethodName") == method && S(trxRows[0], "Outcome") == "Failed",
                "D13 " + style + " real TRX identity");
            var discovery = WriteOneNodeDiscovery(fixture.PathOf("d13-" + style + ".json"), uid, "Antiphon.E2E", "AgentTaskLandDeliveryE2ETests", method);
            var v = Verdict(trx, discovery, path, fixture.Root, 2, "d13");
            var terminal = Items(v, "terminalNodes");
            rows.Check(B(v, "coverageComplete") && !B(v, "testsPassed") && Contains(v, "reasons", "failed-row") && terminal.Length == 1 && S(terminal[0], "Message") == expected,
                "D13 " + style + " complete failed verdict");
            // Even a misleading successful process exit cannot make the failure pass.
            var direct = NightlyChunkVerdict.CoverageVerdict(parsed, 0, "d13", "d13", "test", "test", "test", "test", [], [], false, "", null, null);
            rows.Check(!B(direct, "testsPassed") && Contains(direct, "reasons", "failed-row"), "D13 " + style + " error alone stays red");
        }
        rows.ShouldAllPass(10, "D13 LF failure identity", "D13 LF failure message", "D13 LF complete failed verdict",
            "D13 CRLF failure identity", "D13 CRLF failure message", "D13 CRLF complete failed verdict");
    }

    [Test]
    public void D13_ExecutionCompatibility()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        var execution = Path.Combine(ProbeRoot, "execution.diag");
        var text = File.ReadAllText(execution);
        var pass = ReadNodes(execution, "execution");
        var skip = ReadNodes(WriteDiagnostic(fixture.Root, "d13-skips", text.Replace("PassedTestNodeStateProperty", "SkippedTestNodeStateProperty")), "execution");
        rows.Check(pass.Length == 8 && skip.Length == 8 && pass.All(n => S(n, "Outcome") == "Passed") && skip.All(n => S(n, "Outcome") == "Skipped")
            && C1044Inventory(pass, execution: true) && C1044Inventory(skip, execution: true), "D13 pass and skip unchanged");
        var legacy = ReadNodes(WriteDiagnostic(fixture.Root, "d13-legacy", D13FailureText().Replace("ErrorTestNodeStateProperty", "FailedTestNodeStateProperty")), "execution");
        rows.Check(legacy.Length == 1 && S(legacy[0], "Outcome") == "Failed" && S(legacy[0], "Method") == "C488_ApprovalPollingCannotConfirm", "D13 legacy Failed retained");
        var discovery = File.ReadAllText(Path.Combine(ProbeRoot, "discovery.diag"));
        var mixed = ReadNodes(WriteDiagnostic(fixture.Root, "d13-mixed", text + "\n" + discovery + "\n" + D13FailureText()), "execution");
        rows.Check(mixed.Length == 9 && mixed.Count(n => string.Equals(S(n, "Outcome"), "Failed", StringComparison.OrdinalIgnoreCase)) == 1
            && !mixed.Any(n => new[] { "Discovered", "InProgress" }.Contains(S(n, "Outcome"), StringComparer.OrdinalIgnoreCase)), "D13 nonterminal updates omitted");
        rows.ShouldAllPass(3, "D13 pass and skip unchanged", "D13 legacy Failed retained", "D13 nonterminal updates omitted");
    }

    [Test]
    public void D13_ExecutionGuards()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        var text = D13FailureText();
        foreach (var variant in new[] { "duplicate", "version" })
        {
            var changed = variant == "duplicate" ? text + "\n" + text : text.Replace("2.2.2", "9.0.0");
            var path = WriteDiagnostic(fixture.Root, "d13-" + variant, changed);
            var error = Refusal(() => NightlyDiagnosticLog.Read(path, "execution"));
            if (variant == "duplicate") rows.Check(Regex.IsMatch(error, "duplicate execution uid", RegexOptions.IgnoreCase), "D13 duplicate Error refuses", error);
            else rows.Check(Regex.IsMatch(error, "version-drift", RegexOptions.IgnoreCase), "D13 unsupported version refuses", error);
        }
        var parsed = ReadNodes(WriteDiagnostic(fixture.Root, "d13-guard-error", text), "execution");
        const string requiredUid = "Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.C488_ApprovalPollingCannotConfirm.1.1.0";
        const string omittedUid = "Antiphon.E2E.AgentTaskLandDeliveryE2ETests.1.1.OtherMethod.1.1.0";
        var nodes = ReadNodes(WriteDiagnostic(fixture.Root, "d13-guard-omit", text.Replace(requiredUid, omittedUid)), "execution");
        var missing = NightlyChunkVerdict.ExpandedRowsPresent([], nodes, [requiredUid]);
        var missingUids = Items(missing, "Missing");
        rows.Check(nodes.Length == 1 && S(nodes[0], "Uid") == omittedUid && !B(missing, "Ok") && missingUids.Length == 1 && PsValue.Str(missingUids[0]) == requiredUid,
            "D13 missing failed UID refuses");
        var extra = NightlyVerdicts.CrossCheck(Array.Empty<object>(), parsed, []);
        rows.Check(!B(extra, "Ok") && Items(extra, "Mismatch").Length == 1, "D13 extra failed identity refuses");
        rows.ShouldAllPass(4, "D13 duplicate Error refuses", "D13 unsupported version refuses", "D13 missing failed UID refuses", "D13 extra failed identity refuses");
    }

    [Test]
    public void D13_StateClassification()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        string Detail((object[] Nodes, string Error) parsed) => "error=" + parsed.Error + " outcome=" + (parsed.Nodes.Length > 0 ? S(parsed.Nodes[0], "Outcome") : "")
            + " message=" + (parsed.Nodes.Length > 0 ? S(parsed.Nodes[0], "Message") : "");

        const string timeoutUid = "Probe.TimeoutCases.1.1.Only.1.1.0";
        var timeout = Parse(fixture.Root, "d13-timeout-only", StateText(D13StateRecord(timeoutUid, "TimeoutCases", "Only", "Timeout", "timed out", "System.TimeoutException: timed out")));
        rows.Check(timeout.Error == "" && timeout.Nodes.Length == 1 && S(timeout.Nodes[0], "Uid") == timeoutUid && S(timeout.Nodes[0], "Outcome") == "Failed"
            && S(timeout.Nodes[0], "Message") == "timed out", "D13 timeout-only is failed", Detail(timeout));

        const string cancelledUid = "Probe.CancelledCases.1.1.Only.1.1.0";
        var cancelled = Parse(fixture.Root, "d13-cancelled-only", StateText(D13StateRecord(cancelledUid, "CancelledCases", "Only", "Cancelled", "cancelled by the run",
            "System.OperationCanceledException: cancelled")));
        rows.Check(cancelled.Error == "" && cancelled.Nodes.Length == 1 && S(cancelled.Nodes[0], "Uid") == cancelledUid && S(cancelled.Nodes[0], "Outcome") == "Failed"
            && S(cancelled.Nodes[0], "Message") == "cancelled by the run", "D13 cancelled-only is failed", Detail(cancelled));

        const string passUid = "Probe.Ordinary.1.1.Plain.1.1.0";
        const string mixedUid = "Probe.SlowCases.1.1.SlowOne.1.1.0";
        var mixed = Parse(fixture.Root, "d13-mixed-timeout", StateText(
            D13StateRecord(passUid, "Ordinary", "Plain", "Passed", "ok", "none"),
            D13StateRecord(mixedUid, "SlowCases", "SlowOne", "Timeout", "slow timed out", "System.TimeoutException: slow timed out")));
        var passNode = mixed.Nodes.Where(n => S(n, "Uid") == passUid).ToArray();
        var timeoutNode = mixed.Nodes.Where(n => S(n, "Uid") == mixedUid).ToArray();
        rows.Check(mixed.Error == "" && mixed.Nodes.Length == 2 && passNode.Length == 1 && S(passNode[0], "Outcome") == "Passed"
            && timeoutNode.Length == 1 && S(timeoutNode[0], "Outcome") == "Failed" && S(timeoutNode[0], "Message") == "slow timed out", "D13 mixed pass and timeout keeps both",
            "error=" + mixed.Error + " count=" + mixed.Nodes.Length + " uids=" + string.Join(",", mixed.Nodes.Select(n => S(n, "Uid"))));

        const string quoted = "saw PassedTestNodeStateProperty in the text";
        const string misUid = "Probe.Ordinary.1.1.Mislabel.1.1.0";
        var mis = Parse(fixture.Root, "d13-passed-quote", StateText(D13StateRecord(misUid, "Ordinary", "Mislabel", "Error", quoted, "System.Exception: quoted")));
        rows.Check(mis.Error == "" && mis.Nodes.Length == 1 && S(mis.Nodes[0], "Outcome") == "Failed" && S(mis.Nodes[0], "Message") == quoted,
            "D13 error quoting PassedTestNodeStateProperty stays failed", Detail(mis));

        const string full = "[Test Failure] boom, Exception = INNER still the explanation";
        const string cutUid = "Probe.Ordinary.1.1.Truncated.1.1.0";
        var cut = Parse(fixture.Root, "d13-exception-text", StateText(D13StateRecord(cutUid, "Ordinary", "Truncated", "Error", full,
            "TUnit.Engine.Exceptions.TestFailedException: boom, Exception = INNER still the explanation")));
        rows.Check(cut.Error == "" && cut.Nodes.Length == 1 && S(cut.Nodes[0], "Outcome") == "Failed" && S(cut.Nodes[0], "Message") == full,
            "D13 explanation keeps embedded exception delimiters exactly", "message=" + (cut.Nodes.Length > 0 ? S(cut.Nodes[0], "Message") : ""));

        const string unknownUid = "Probe.UnknownCases.1.1.Only.1.1.0";
        var unknown = Parse(fixture.Root, "d13-unknown-state", StateText(D13StateRecord(unknownUid, "UnknownCases", "Only", "NotStarted", "not started", "System.Exception: not started")));
        rows.Check(unknown.Error == "" && unknown.Nodes.Length == 1 && S(unknown.Nodes[0], "Uid") == unknownUid && S(unknown.Nodes[0], "Outcome") == "Failed"
            && S(unknown.Nodes[0], "Message") == "not started", "D13 unknown state is failed", Detail(unknown));
        rows.ShouldAllPass(6, "D13 timeout-only is failed", "D13 cancelled-only is failed", "D13 mixed pass and timeout keeps both",
            "D13 error quoting PassedTestNodeStateProperty stays failed", "D13 explanation keeps embedded exception delimiters exactly", "D13 unknown state is failed");
    }

    /// <summary>Assert-C1325Fixture: the fixture's identity, then its message (absent when none is expected).</summary>
    private static void CheckC1325(NightlyCaseRows rows, string root, C1325Fixture fixture, string group)
    {
        var parsed = Parse(root, "c1325-" + fixture.Id, StateText(fixture.Record));
        var nodes = parsed.Nodes;
        rows.Check(parsed.Error == "" && nodes.Length == 1 && S(nodes[0], "Uid") == fixture.Uid && S(nodes[0], "ClassName") == fixture.ClassName
            && S(nodes[0], "Method") == fixture.Method && S(nodes[0], "Outcome") == fixture.Outcome, "D13 " + group + " " + fixture.Id + " identity", parsed.Error);
        var messageOk = false;
        if (nodes.Length == 1)
        {
            var present = HasProperty(nodes[0], "Message");
            messageOk = fixture.ExpectedMessage == null ? !present : present && string.Equals(S(nodes[0], "Message"), fixture.ExpectedMessage, StringComparison.Ordinal);
        }
        rows.Check(messageOk, "D13 " + group + " " + fixture.Id + " message",
            "expected=" + fixture.ExpectedMessage + " actual=" + (nodes.Length > 0 ? S(nodes[0], "Message") : ""));
    }

    [Test]
    public void D13_ExplanationBoundary()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        foreach (var c1325 in C1325ExplanationFixtures()) CheckC1325(rows, fixture.Root, c1325, "EB");
        rows.ShouldAllPass(22, Enumerable.Range(1, 11).SelectMany(id => new[] { $"D13 EB E{id:00} identity", $"D13 EB E{id:00} message" }).ToArray());
    }

    [Test]
    public void D13_StatePropertyBoundary()
    {
        using var fixture = NightlyFixture.Create("d13");
        var rows = new NightlyCaseRows();
        var fixtures = C1325StateFixtures();
        foreach (var c1325 in fixtures) CheckC1325(rows, fixture.Root, c1325, "SB");
        var ambiguous = Parse(fixture.Root, "c1325-ambiguous-state", StateText(C1325AmbiguousStateRecord()));
        rows.Check(ambiguous.Error == "" && ambiguous.Nodes.Length == 1 && S(ambiguous.Nodes[0], "Uid") == C1325Uid
            && S(ambiguous.Nodes[0], "ClassName") == "Antiphon.E2E.AgentTaskLandDeliveryE2ETests" && S(ambiguous.Nodes[0], "Method") == "C488_ApprovalPollingCannotConfirm"
            && S(ambiguous.Nodes[0], "Outcome") == "Failed" && !HasProperty(ambiguous.Nodes[0], "Message"), "D13 SB ambiguous envelope stays failed", ambiguous.Error);
        var missing = Parse(fixture.Root, "c1325-missing-uid", StateText(fixtures[0].Record.Replace("Value = " + C1325Uid + " }", "Value =  }")));
        rows.Check(missing.Error == "truncated diagnostic record: missing uid", "D13 SB missing UID refuses", missing.Error);
        var path = WriteDiagnostic(fixture.Root, "c1325-joined", StateText(fixtures[0].Record));
        var trx = fixture.PathOf("c1325-joined.trx");
        File.WriteAllText(trx, D13FailureTrx);
        var discovery = WriteOneNodeDiscovery(fixture.PathOf("c1325-joined.json"), C1325Uid, "Antiphon.E2E", "AgentTaskLandDeliveryE2ETests", "C488_ApprovalPollingCannotConfirm");
        var verdict = Verdict(trx, discovery, path, fixture.Root, 0, "c1325");
        var terminal = Items(verdict, "terminalNodes");
        var reasons = Items(verdict, "reasons").Select(PsValue.Str).ToArray();
        rows.Check(B(verdict, "coverageComplete") && !B(verdict, "testsPassed") && Contains(verdict, "reasons", "failed-row") && terminal.Length == 1
            && S(terminal[0], "Uid") == C1325Uid && S(terminal[0], "Outcome") == "Failed" && string.Equals(S(terminal[0], "Message"), fixtures[0].ExpectedMessage, StringComparison.Ordinal)
            && !reasons.Any(r => Regex.IsMatch(r, "missing-expanded-row|trx-diagnostic-mismatch", RegexOptions.IgnoreCase)), "D13 SB failed TRX owns verdict", string.Join(",", reasons));
        rows.ShouldAllPass(27, Enumerable.Range(1, 12).SelectMany(id => new[] { $"D13 SB S{id:00} identity", $"D13 SB S{id:00} message" })
            .Concat(["D13 SB ambiguous envelope stays failed", "D13 SB missing UID refuses", "D13 SB failed TRX owns verdict"]).ToArray());
    }
}
