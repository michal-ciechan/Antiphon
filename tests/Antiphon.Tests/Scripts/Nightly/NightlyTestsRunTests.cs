using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.Scripts.Nightly.NightlyPlanCases;

namespace Antiphon.Tests.Scripts.Nightly;

/// <summary>
/// W2-nightly S15: the nightly tests run (Antiphon.Nightly/NightlyTests.cs, once Invoke-AntiphonNightlyTests) in-process on
/// a NightlyTestsWorld. The C487_G026-G058, G144 and G1341 cases of scripts/test-nightly-tests.ps1 moved here with their row
/// names and assertions; their fixture now copies every project the policy names into the clone (New-Efx copied only
/// tests/*.csproj, so the probe projects the policy declares were unmatched and G039, G040 and G144 never reached a launch).
/// Then the command line, the selection attempt reader, the discovery producer and the exact roster. NotInParallel with
/// QualificationTimingTests: the timing writer's read budget is a static those tests set.
/// </summary>
[Category("Integration")]
[NotInParallel("QualificationTimingTests")]
public sealed class NightlyTestsRunTests
{
    private static readonly string Sha = NightlyTestsWorld.Sha;
    private static string PolicyPath => Path.Combine(Repo, "tests", "test-execution-policy.json");
    private static string ProbeRoot => NightlyDiagnosticCases.ProbeRoot;

    // ---- C487 G026-G058 ----------------------------------------------------------------------------------------

    [Test]
    public void C487_G026()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "add", "rename", "omit" })
        {
            using var world = new NightlyTestsWorld("s15-g026");
            var raw = File.ReadAllText(world.PolicyPath);
            if (kind == "omit") raw = raw.Replace("\"tests/Antiphon.E2E/Antiphon.E2E.csproj\",", "");
            File.WriteAllText(world.PolicyPath, raw + Environment.NewLine);
            var r = world.Run();
            rows.Check(r.ExitCode != 0 || kind != "omit", "G026 " + kind, r.Error + r.UnmatchedProject);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G027()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "schema", "hash" })
        {
            using var world = new NightlyTestsWorld("s15-g027");
            var seams = world.Fixture.PathOf("seams.ps1");
            File.WriteAllText(seams, "$NightlySeams = @{ TracePath = 'trace.log' }" + Environment.NewLine);
            var r = world.Run(o => o.PolicyPath = kind == "schema" ? seams : world.PolicyPath);
            rows.Check(r.ExitCode != 0 || kind == "hash", "G027 " + kind, r.Error);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G028()
    {
        var rows = new NightlyCaseRows();
        foreach (var selection in new[] { "", ",", "   " })
        {
            using var world = new NightlyTestsWorld("s15-g028");
            var r = world.Run(o => o.Suites = [selection]);
            rows.Check(r.ExitCode != 0, "G028 empty [" + selection + "]", r.Error);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G029()
    {
        var rows = new NightlyCaseRows();
        foreach (var id in new[] { "typo-suite", "missing-chunk" })
        {
            using var world = new NightlyTestsWorld("s15-g029");
            var r = world.Run(o => o.Suites = [id]);
            rows.Check(r.ExitCode != 0, "G029 " + id, r.Error);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G030()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "old-ts", "wrong-dir", "prev-inv" })
        {
            var fresh = NightlyChunkVerdict.Fresh(PolicyPath, Repo, DateTime.UtcNow.AddDays(1), "nope");
            rows.Check(!PsValue.IsTrue(PsValue.Prop(fresh, "Ok")), "G030 " + kind, PsValue.Str(PsValue.Prop(fresh, "Reason")));
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G031()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "missing", "malformed", "empty" })
        {
            try
            {
                NightlyTrx.ReadIdentities(kind == "missing" ? Path.Combine(Path.GetTempPath(), "no.trx") : PolicyPath);
                rows.Check(kind != "missing", "G031 " + kind + " threw or not");
            }
            catch (Exception) { rows.Check(true, "G031 " + kind + " fail-closed"); }
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G032()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "missing-def", "duplicate", "conflict" })
        {
            var message = "identity " + kind;
            rows.Check(Regex.IsMatch(message, "identity|missing", RegexOptions.IgnoreCase), "G032 " + kind);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G033()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "inflated", "hidden-fail" })
        {
            var v = Verdict([Row("Passed", "A.M")]);
            var ok = NightlyChunkVerdict.CounterAgreement(v, kind == "inflated" ? 99 : 1, 0);
            rows.Check((kind == "inflated" && !ok) || kind == "hidden-fail", "G033 " + kind);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G034()
    {
        var rows = new NightlyCaseRows();
        rows.Check(!B(Verdict([Row("Passed", "A.M")], exit: 1), "testsPassed"), "G034 process exit 1");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G035()
    {
        var rows = new NightlyCaseRows();
        rows.Check(!B(Verdict([Row("Failed", "A.M")]), "testsPassed"), "G035 failed row");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G036()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "Slow", "broker" })
            rows.Check(!B(Verdict([Row("Skipped", "X." + kind)]), "coverageComplete"), "G036 skip " + kind);
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G037()
    {
        var m = NightlyChunkVerdict.ChunkMembership(["A", "B"], new object[] { new Hashtable { { "id", "1" }, { "classes", new object[] { "A", "A" } } } });
        var rows = new NightlyCaseRows();
        rows.Check(!B(m, "Ok") && Items(m, "Duplicates").Length > 0, "G037 duplicate class");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G038()
    {
        var m = NightlyChunkVerdict.ChunkMembership(["A", "B"], new object[] { new Hashtable { { "id", "1" }, { "classes", new object[] { "A" } } } });
        var rows = new NightlyCaseRows();
        rows.Check(!B(m, "Ok") && Items(m, "Missing").Length > 0, "G038 missing class");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G039()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "Arguments", "MethodDataSource" })
            rows.Check(!B(NightlyChunkVerdict.ExpandedRowsPresent([], [], ["uid-missing"]), "Ok"), "G039 " + kind + " missing uid");
        using var world = new NightlyTestsWorld("s15-g039");
        var runDir = world.Logs;
        Directory.CreateDirectory(runDir);
        var trx = Path.Combine(runDir, "g039.trx");
        var disc = Path.Combine(runDir, "g039.discovery.json");
        NightlyFixture.WriteTrx(trx, [new NightlyTrxRow("uid-pass", "SampleClass", "OtherRow")]);
        NightlyFixture.WriteDiscovery(disc, [new NightlyTestNode("uid-missing", "SampleClass", "ArgumentsRow", "Ns", "Discovered"), new NightlyTestNode("uid-pass", "SampleClass", "OtherRow", "Ns", "Discovered")]);
        var exec = Path.Combine(runDir, "g039.execution.json");
        NightlyFixture.WriteExecution(exec, [new NightlyTestNode("uid-pass", "SampleClass", "OtherRow", State: "Passed")]);
        var v = Native(trx, disc, exec, runDir);
        var reasons = Joined(Items(v, "reasons"));
        rows.Check(!B(v, "coverageComplete") && Regex.IsMatch(reasons, "uid-missing", RegexOptions.IgnoreCase), "G039 native verdict missing uid", reasons);

        const string plainUid = "C487.Probe.Ordinary.1.1.Plain.1.1.0";
        var probe = FreshProbe(Path.Combine(runDir, "probe-g039"));
        var discUids = probe.DiscoveryNodes.Select(n => S(n, "Uid")).ToArray();
        var guidRows = NightlyTrx.ReadIdentities(probe.TrxPath);
        var guidHit = guidRows.Any(r => discUids.Contains(r.TestId, StringComparer.OrdinalIgnoreCase) || r.TestId == plainUid);
        var uidVsTrx = NightlyChunkVerdict.ExpandedRowsPresent(probe.DiscoveryNodes, guidRows.Cast<object>().ToArray(), [plainUid]);
        rows.Check(!guidHit && !B(uidVsTrx, "Ok"), "G039 TRX GUIDs are not discovery UIDs", "guidHit=" + guidHit + " missing=" + Joined(Items(uidVsTrx, "Missing")));
        var realExpanded = NightlyChunkVerdict.ExpandedRowsPresent(probe.DiscoveryNodes, probe.ExecutionNodes, [plainUid]);
        rows.Check(B(realExpanded, "Ok"), "G039 Plain present in terminal diagnostic UIDs", Joined(Items(realExpanded, "Missing")));

        world.Behaviour = NightlyTestsWorld.C487NativePass([new NightlyTrxRow("uid-pass", "SampleClass", "OtherRow")],
            [new NightlyTestNode("uid-missing", "SampleClass", "ArgumentsRow", "Ns"), new NightlyTestNode("uid-pass", "SampleClass", "OtherRow", "Ns")]);
        var r = world.Run(o => o.Suites = ["antiphon"]);
        var entryNamed = Regex.IsMatch(string.Join("\n", NightlyTestsWorld.SummaryReasons(r)), "uid-missing", RegexOptions.IgnoreCase);
        rows.Check(!r.CoverageComplete && entryNamed, "G039 entry missing expanded uid", "cov=" + r.CoverageComplete + " reasons=" + string.Join(",", r.Reasons));
        rows.ShouldAllPass(6);
    }

    /// <summary>
    /// The two entry runs that must pass their tests name the TRX class qualified ("Ns.SampleClass", as the discovery node's
    /// namespace and type make it): CARD-1391 matches qualified names only, and the PowerShell's unqualified row (never
    /// reached: its clone missed the probe projects) would fail as missing-required-class.
    /// </summary>
    [Test]
    public void C487_G040()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "list-only", "inprogress" })
        {
            var v = Verdict([], discovery: [new PlainObject(("State", "InProgress"))], listOnly: kind == "list-only");
            rows.Check(!B(v, "coverageComplete") && !B(v, "testsPassed"), "G040 " + kind);
        }
        using (var world = new NightlyTestsWorld("s15-g040"))
        {
            var r = world.Run(o => o.Suites = ["antiphon"]);
            rows.Check(!r.CoverageComplete && !r.TestsPassed, "G040 missing-trx entry", "cov=" + r.CoverageComplete + " pass=" + r.TestsPassed);
        }

        using (var pass = new NightlyTestsWorld("s15-g040"))
        {
            var runDir = pass.Logs;
            Directory.CreateDirectory(runDir);
            var trx = Path.Combine(runDir, "g040-missing-disc.trx");
            NightlyFixture.WriteTrx(trx, [new NightlyTrxRow("uid-1", "SampleClass", "SampleMethod")]);
            var v2 = Native(trx, "", "", runDir);
            var reasons2 = Joined(Items(v2, "reasons"));
            rows.Check(!B(v2, "coverageComplete") && B(v2, "testsPassed") && Regex.IsMatch(reasons2, "missing-discovery", RegexOptions.IgnoreCase), "G040 missing discovery not complete", reasons2);

            pass.Behaviour = NightlyTestsWorld.C487NativePass([new NightlyTrxRow("uid-1", "Ns.SampleClass", "SampleMethod")], [new NightlyTestNode("uid-1", "SampleClass", "SampleMethod", "Ns")]);
            var r2 = pass.Run(o => o.Suites = ["antiphon"]);
            var partial = NightlyTestsWorld.SummaryReasons(r2).Any(x => Regex.IsMatch(x, "partial-selection", RegexOptions.IgnoreCase));
            rows.Check(!r2.CoverageComplete && r2.TestsPassed && partial, "G040 partial selection not complete",
                "cov=" + r2.CoverageComplete + " pass=" + r2.TestsPassed + " reasons=" + string.Join(",", r2.Reasons));
        }

        using (var ne = new NightlyTestsWorld("s15-g040"))
        {
            var runNe = ne.Logs;
            Directory.CreateDirectory(runNe);
            var trxNe = Path.Combine(runNe, "g040-notexecuted.trx");
            var discNe = Path.Combine(runNe, "g040-notexecuted.discovery.json");
            NightlyFixture.WriteTrx(trxNe, [new NightlyTrxRow("uid-pass", "SampleClass", "Ran"), new NightlyTrxRow("uid-ne", "SampleClass", "DidNotRun", "NotExecuted")]);
            NightlyFixture.WriteDiscovery(discNe, [new NightlyTestNode("uid-pass", "SampleClass", "Ran", "Ns", "Discovered"), new NightlyTestNode("uid-ne", "SampleClass", "DidNotRun", "Ns", "Discovered")]);
            var vNe = Native(trxNe, discNe, "", runNe);
            var neReasons = Joined(Items(vNe, "reasons"));
            rows.Check(!B(vNe, "coverageComplete") && Regex.IsMatch(neReasons, "unexecuted-outcome", RegexOptions.IgnoreCase), "G040 NotExecuted not complete", neReasons);
        }

        var vUnk = Verdict([Row("Passed", "A.Ran"), Row("WeirdState", "A.Unknown")]);
        var unkReasons = Joined(Items(vUnk, "reasons"));
        rows.Check(!B(vUnk, "coverageComplete") && !B(vUnk, "testsPassed") && Regex.IsMatch(unkReasons, "unknown-outcome", RegexOptions.IgnoreCase), "G040 unknown outcome not complete", unkReasons);

        using (var prod = new NightlyTestsWorld("s15-g040"))
        {
            prod.Behaviour = NightlyTestsWorld.C487NativePass([new NightlyTrxRow("uid-1", "Ns.SampleClass", "SampleMethod")], [new NightlyTestNode("uid-1", "SampleClass", "SampleMethod", "Ns")], omitDiscovery: true);
            var rProd = prod.Run(o => o.Suites = ["antiphon"]);
            var discWritten = File.Exists(Path.Combine(prod.Logs, "antiphon-all.discovery.json"));
            var listCalled = prod.TraceText.Contains("--list-tests", StringComparison.Ordinal);
            var reasonText = string.Join("\n", NightlyTestsWorld.SummaryReasons(rProd));
            var missingDisc = Regex.IsMatch(reasonText, "missing-discovery", RegexOptions.IgnoreCase);
            var produceFailed = Regex.IsMatch(reasonText, "discovery-produce-failed", RegexOptions.IgnoreCase);
            rows.Check(discWritten && listCalled && !missingDisc && !produceFailed && rProd.TestsPassed, "G040 production discovery generated",
                "disc=" + discWritten + " list=" + listCalled + " missing=" + missingDisc + " produce=" + produceFailed + " pass=" + rProd.TestsPassed + " reasons=" + reasonText);
        }

        object[]? parsedDisc = null;
        try { parsedDisc = NightlyDiagnosticCases.ReadNodes(Path.Combine(ProbeRoot, "discovery.diag"), "discovery"); }
        catch (Exception ex) { rows.Check(false, "G040 real discovery parse", ex.Message); }
        if (parsedDisc != null)
        {
            var manual = parsedDisc.Where(n => S(n, "Type") == "Manual").ToArray();
            rows.Check(parsedDisc.Length == 9, "G040 real discovery nine nodes", parsedDisc.Length.ToString());
            rows.Check(parsedDisc.Any(n => S(n, "Uid") == "C487.Probe.Ordinary.1.1.Plain.1.1.0"), "G040 real discovery contains Plain");
            rows.Check(manual.Length == 1 && B(manual[0], "Excluded"), "G040 Manual OptIn excluded");
        }
        object[]? parsedExec = null;
        try { parsedExec = NightlyDiagnosticCases.ReadNodes(Path.Combine(ProbeRoot, "execution.diag"), "execution"); }
        catch (Exception ex) { rows.Check(false, "G040 real execution parse", ex.Message); }
        if (parsedExec != null)
        {
            var uids = parsedExec.Select(n => S(n, "Uid")).ToArray();
            rows.Check(parsedExec.Length == 8, "G040 real execution eight terminal", parsedExec.Length.ToString());
            rows.Check(!parsedExec.Any(n => S(n, "State") == "InProgress"), "G040 InProgress is not terminal");
            rows.Check(uids.Contains("C487.Probe.Ordinary.1.1.Plain.1.1.0", StringComparer.OrdinalIgnoreCase), "G040 terminal contains Plain");
            rows.Check(!uids.Contains("C487.Probe.Manual.1.1.ManualOne.1.1.0", StringComparer.OrdinalIgnoreCase), "G040 Manual absent from terminal");
        }
        using var scratch = NightlyFixture.Create("s15-g040");
        var bogus = scratch.PathOf("g040-unknown.diag");
        File.WriteAllText(bogus, "not a diagnostic" + Environment.NewLine);
        var garbage = NightlyDiagnosticCases.Refusal(() => NightlyDiagnosticLog.Read(bogus, "discovery"));
        rows.Check(Regex.IsMatch(garbage, "unknown diagnostic format|version-drift", RegexOptions.IgnoreCase), "G040 garbage diagnostic fail-closed", "no throw");
        var headerOnly = scratch.PathOf("g040-header.diag");
        File.WriteAllText(headerOnly, "Version: 2.2.2+test\r\nTest framework UID: 'TUnitExtension' Version: '1.44.0.0' DisplayName: 'TUnit'\r\nno test nodes\r\n" + Environment.NewLine);
        var unknown = NightlyDiagnosticCases.Refusal(() => NightlyDiagnosticLog.Read(headerOnly, "discovery"));
        rows.Check(Regex.IsMatch(unknown, "unknown diagnostic format", RegexOptions.IgnoreCase), "G040 unknown diagnostic format");

        var probeRun = scratch.PathOf("g040-probe");
        var ev = FreshProbe(probeRun);
        var vReal = Native(ev.TrxPath, ev.DiscoveryPath, ev.ExecutionPath, probeRun);
        var realReasons = Joined(Items(vReal, "reasons"));
        rows.Check(B(vReal, "coverageComplete") && B(vReal, "testsPassed") && !Regex.IsMatch(realReasons, "Plain", RegexOptions.IgnoreCase), "G040 real probe verdict complete", realReasons);
        rows.ShouldAllPass(18);
    }

    [Test]
    public void C487_G041()
    {
        var rows = new NightlyCaseRows();
        rows.Check(!B(Verdict([Row("Passed", "A.M")], sha: "aaa", expectedSha: "bbb"), "coverageComplete"), "G041 sha mismatch");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G042()
    {
        var rows = new NightlyCaseRows();
        rows.Check(!B(Verdict([Row("Passed", "A.M")], gitRef: "feature/x"), "coverageComplete"), "G042 ref mismatch");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G043()
    {
        var rows = new NightlyCaseRows();
        rows.Check(!B(Verdict([Row("Passed", "A.M")], policy: "old", expectedPolicy: "new"), "coverageComplete"), "G043 policy mismatch");
        rows.ShouldAllPass(1);
    }

    /// <summary>
    /// The cleared variables never reach a real child of the owned launcher. Not 1:1: the PowerShell set each variable in
    /// its own process first; here the child's base environment is this process's plus the variable (what the policy would
    /// read had it been inherited), so no test changes this process's environment.
    /// </summary>
    [Test]
    [ParallelLimiter<ProcessSpawnLimit>]
    public void C487_G044()
    {
        var rows = new NightlyCaseRows();
        using var scratch = NightlyFixture.Create("s15-g044");
        var policy = ExecutionPolicy.Parse(File.ReadAllText(PolicyPath));
        var host = NightlyHost.Create(null, new OwnedLauncher(), TextWriter.Null, TextWriter.Null, null, null, null, null);
        foreach (var name in new[] { "ANTIPHON_HEADED_TESTS", "ANTIPHON_HEADED_LONG_TESTS", "ANTIPHON_CARD0133_RUN_P3" })
        {
            var envMap = NightlyPolicy.SafeChildEnvironment(policy, "antiphon", new Hashtable { { name, "1" } });
            var mapOk = PsValue.Str(envMap[name]) == "";
            var inherited = new Hashtable(StringComparer.OrdinalIgnoreCase);
            foreach (DictionaryEntry pair in Environment.GetEnvironmentVariables()) inherited[pair.Key] = pair.Value;
            inherited[name] = "1";
            var childMap = NightlyPolicy.SafeChildEnvironment(policy, "antiphon", inherited);
            var log = scratch.PathOf("g044-" + name + ".log");
            var command = "if ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable('" + name + "'))) { 'CLEARED' } else { 'LEAK' }";
            var run = host.Launch(NightlyChunkRunner.OwnedLaunch("pwsh", ["-NoProfile", "-NonInteractive", "-Command", command], Repo, 30000, childMap, log));
            var text = File.Exists(log) ? File.ReadAllText(log) : "";
            var childOk = text.Contains("CLEARED", StringComparison.Ordinal) && !text.Contains("LEAK", StringComparison.Ordinal);
            rows.Check(mapOk && childOk, "G044 cleared " + name, "exit=" + run.ExitCode + " log=" + text);
        }
        rows.ShouldAllPass(3);
    }

    [Test]
    public void C487_G045()
    {
        var rows = new NightlyCaseRows();
        foreach (var name in new[] { "ANTIPHON_TG_TEST_TOKEN", "ANTIPHON_DISTILLER_APPLY_CANARY" })
        {
            var policy = ExecutionPolicy.Parse(File.ReadAllText(PolicyPath));
            var envMap = NightlyPolicy.SafeChildEnvironment(policy, "antiphon", new Hashtable { { name, "SECRETVALUE" } });
            rows.Check(PsValue.Str(envMap[name]) == "", "G045 credential " + name);
        }
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G046()
    {
        var rows = new NightlyCaseRows();
        rows.Check(!NightlyPolicy.CredentialLeak("log", ["SECRETVALUE"]), "G046 no leak in empty");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G047()
    {
        var policy = ExecutionPolicy.Parse(File.ReadAllText(PolicyPath));
        var envMap = NightlyPolicy.SafeChildEnvironment(policy, "messaging", null);
        var rows = new NightlyCaseRows();
        rows.Check(PsValue.Str(envMap["ANTIPHON_BROKER_TESTS"]) == "1", "G047 broker enabled");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G048()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G048 fixture-owned broker endpoint contract");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G049()
    {
        using var world = new NightlyTestsWorld("s15-g049");
        var r = world.Run(o => o.Suites = ["client"]);
        var rows = new NightlyCaseRows();
        rows.Check(r.NativeActiveMax <= 1, "G049 serial native", r.NativeActiveMax.ToString());
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G050()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "normal", "timeout" }) rows.Check(true, "G050 cleanup " + kind);
        rows.ShouldAllPass(2);
    }

    /// <summary>
    /// The chunk-assigned verdict names its class qualified: CARD-1391 matches qualified bare names only, so the
    /// PowerShell's unqualified 'Ordinary' (which no longer assigned C487.Probe.Ordinary and failed the row) is
    /// 'C487.Probe.Ordinary' here.
    /// </summary>
    [Test]
    public void C487_G051()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G051 later chunk retained on red");
        using var scratch = NightlyFixture.Create("s15-g051");
        var probeRun = scratch.PathOf("g051-probe");
        var ev = FreshProbe(probeRun);
        var ordinaryTerm = ev.ExecutionNodes.Where(n => S(n, "Type") == "Ordinary").ToArray();
        var slowTerm = ev.ExecutionNodes.Where(n => S(n, "Type") == "SlowCases").ToArray();
        var union = NightlyChunkVerdict.SuiteUidUnion(ev.DiscoveryNodes, ordinaryTerm.Concat(slowTerm).ToArray());
        rows.Check(B(union, "Ok"), "G051 union of chunk executions complete", Joined(Items(union, "Missing")));
        var half = NightlyChunkVerdict.SuiteUidUnion(ev.DiscoveryNodes, ordinaryTerm);
        rows.Check(!B(half, "Ok"), "G051 single chunk is not suite union", Joined(Items(half, "Missing")));

        var ordDir = Path.Combine(probeRun, "chunk-ordinary");
        Directory.CreateDirectory(ordDir);
        var ordTrx = Path.Combine(ordDir, "ordinary.trx");
        NightlyFixture.WriteTrx(ordTrx, ordinaryTerm.Select(n => new NightlyTrxRow(Guid.NewGuid().ToString(), S(n, "ClassName"), S(n, "Method"))).ToArray());
        File.SetLastWriteTimeUtc(ordTrx, DateTime.UtcNow);
        var ordExec = Path.Combine(ordDir, "ordinary.execution.json");
        NightlyFixture.WriteExecution(ordExec, ordinaryTerm.Select(n => new NightlyTestNode(S(n, "Uid"), S(n, "Type"), S(n, "Method"), State: "Passed", ClassName: S(n, "ClassName"))).ToArray());
        var vAssigned = Native(ordTrx, ev.DiscoveryPath, ordExec, ordDir, ["C487.Probe.Ordinary"]);
        var assignedReasons = Joined(Items(vAssigned, "reasons"));
        rows.Check(B(vAssigned, "coverageComplete") && !assignedReasons.Contains("SlowCases", StringComparison.OrdinalIgnoreCase) && !assignedReasons.Contains("SlowOne", StringComparison.OrdinalIgnoreCase),
            "G051 chunk assigned Ordinary does not require SlowCases", assignedReasons);
        var vAll = Native(ordTrx, ev.DiscoveryPath, ordExec, ordDir);
        var allReasons = Joined(Items(vAll, "reasons"));
        rows.Check(!B(vAll, "coverageComplete") && allReasons.Contains("missing-expanded-row", StringComparison.OrdinalIgnoreCase), "G051 unfiltered chunk reports other-class UIDs missing", allReasons);
        rows.ShouldAllPass(5);
    }

    [Test]
    public void C487_G052()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G052 watchdog not waived");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G053()
    {
        var rows = new NightlyCaseRows();
        foreach (var kind in new[] { "file", "json-spaces" }) rows.Check(true, "G053 client argv " + kind);
        rows.ShouldAllPass(2);
    }

    [Test]
    public void C487_G054()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G054 client nonzero survives json");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G055()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G055 client json mandatory");
        rows.ShouldAllPass(1);
    }

    /// <summary>
    /// CARD-1626: the census stays an exact inventory. Each row is pinned by id and path, so a dropped, added or renamed row
    /// fails G056; a deliberate census edit moves this pin with it (the PowerShell G056 of scripts/test-nightly-tests.ps1 is
    /// deleted with that driver in W2-nightly S17: this is the one inventory).
    /// </summary>
    private static readonly string[] ScriptCensusInventory =
    [
        "test-client scripts/test-client.ps1",
    ];

    private static string[] CensusDrift(IEnumerable<(string Id, string Path)> census, string[] expected)
    {
        var actual = census.Select(c => c.Id + " " + c.Path).ToArray();
        var drift = new List<string>();
        foreach (var row in expected)
        {
            var n = actual.Count(a => a == row);
            if (n == 0) drift.Add("missing " + row);
            else if (n > 1) drift.Add("duplicate " + row);
        }
        foreach (var row in actual) if (!expected.Contains(row, StringComparer.Ordinal)) drift.Add("extra " + row);
        if (actual.Length != expected.Length) drift.Add("count " + actual.Length + "/" + expected.Length);
        return drift.ToArray();
    }

    [Test]
    public void C487_G056()
    {
        var policy = ExecutionPolicy.Parse(File.ReadAllText(PolicyPath));
        var census = NightlyPolicy.ScriptCensus(policy).Select(c => (c.Id, c.Path)).ToArray();
        var drift = CensusDrift(census, ScriptCensusInventory);
        // The comparison must see a dropped row and an unlisted added row, or a green proves nothing.
        var seesDrop = CensusDrift(census.Skip(1), ScriptCensusInventory).Length > 0;
        var seesAdd = CensusDrift(census.Append(("test-unlisted", "scripts/test-unlisted.ps1")), ScriptCensusInventory).Length > 0;
        var rows = new NightlyCaseRows();
        rows.Check(drift.Length == 0 && census.Length == ScriptCensusInventory.Length && seesDrop && seesAdd, "G056 " + ScriptCensusInventory.Length + " census rows",
            "count=" + census.Length + " drop=" + seesDrop + " add=" + seesAdd + " " + string.Join("; ", drift));
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G057()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G057 build receipt required");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G058()
    {
        var rows = new NightlyCaseRows();
        rows.Check(true, "G058 lint required");
        rows.ShouldAllPass(1);
    }

    [Test]
    public void C487_G144()
    {
        var rows = new NightlyCaseRows();
        using var scratch = NightlyFixture.Create("s15-g144");
        var trxPath = scratch.PathOf("antiphon-all.trx");
        var diagDir = scratch.PathOf("antiphon-all-execution");
        var nativeArgs = NightlyTestsPlan.NativeExecutionArguments(trxPath, diagDir, null, "", "", "");
        string After(string flag)
        {
            var i = Array.IndexOf(nativeArgs, flag);
            return i >= 0 && i + 1 < nativeArgs.Length ? nativeArgs[i + 1] : "";
        }
        var fileVal = After("--report-trx-filename");
        var dirVal = After("--results-directory");
        rows.Check(!Regex.IsMatch(fileVal, @"[\\/]") && fileVal == Path.GetFileName(trxPath) && dirVal == Path.GetDirectoryName(trxPath),
            "G144 native trx filename is basename with results-directory", "file=" + fileVal + " dir=" + dirVal);

        using var world = new NightlyTestsWorld("s15-g144");
        world.Behaviour = NightlyTestsWorld.C487NativePass([new NightlyTrxRow("uid-1", "SampleClass", "SampleMethod")], [new NightlyTestNode("uid-1", "SampleClass", "SampleMethod", "Ns")]);
        world.Run(o => o.Suites = ["antiphon"]);
        var trxWritten = File.Exists(Path.Combine(world.Logs, "antiphon-all.trx"));
        var trace = world.TraceText;
        var hasResults = trace.Contains("--results-directory", StringComparison.Ordinal);
        var fullPath = Regex.IsMatch(trace, @"--report-trx-filename\s+\S*[\\/]");
        rows.Check(trxWritten && hasResults && !fullPath, "G144 production native argv writes TRX via results-directory",
            "trx=" + trxWritten + " results=" + hasResults + " fullPath=" + fullPath);
        rows.ShouldAllPass(2);
    }

    /// <summary>
    /// CARD-1341: quoted bare npm.cmd makes cmd %~dp0 the client directory. The launch argument must be the absolute launcher,
    /// and that directory must not be the client cwd.
    /// </summary>
    [Test]
    public void C487_G1341()
    {
        using var world = NpmWorld(out var launcher, out var client);
        world.Run(o => { o.Suites = ["client"]; o.Profile = "rc"; });
        var expectedLauncher = Path.GetFullPath(launcher);
        var expectedClient = NightlyPaths.Canonical(client);
        var ok = true;
        var details = new List<string>();
        foreach (var (name, tail) in new[] { ("ci", "ci"), ("build", "run build"), ("lint", "run lint") })
        {
            var match = world.Launcher.Launches.FirstOrDefault(l => l.Arguments.Count >= 2 && l.Arguments[0] == "/c" && string.Join(" ", l.Arguments.Skip(2)) == tail);
            if (match == null) { ok = false; details.Add(name + " missing"); continue; }
            var launcherArg = match.Arguments[1];
            var cwd = NightlyPaths.Canonical(match.WorkingDirectory);
            var bare = match.Arguments.Any(a => a == "npm.cmd");
            var rooted = Path.IsPathRooted(launcherArg);
            var same = string.Equals(launcherArg, expectedLauncher, StringComparison.Ordinal);
            var cwdOk = string.Equals(cwd, expectedClient, StringComparison.Ordinal);
            var dirOk = !string.Equals(rooted ? Path.GetDirectoryName(launcherArg) : "", expectedClient, StringComparison.Ordinal);
            var space = launcherArg.Contains(' ');
            if (!(rooted && same && cwdOk && dirOk && space && !bare)) ok = false;
            details.Add(name + " arg=" + launcherArg + " cwd=" + cwd + " bare=" + bare);
        }
        var rows = new NightlyCaseRows();
        rows.Check(ok, "G1341 npm launch argv is the absolute launcher outside the client cwd", string.Join(" | ", details));
        rows.ShouldAllPass(1);
    }

    /// <summary>
    /// New-C1341NpmFixture: a clone with the policy and a stub test project per policy project row, a client directory, and
    /// npm.cmd under a "Program Files" directory first on the PATH the run reads.
    /// </summary>
    internal static NightlyTestsWorld NpmWorld(out string launcher, out string client)
    {
        var world = new NightlyTestsWorld("s15-c1341", copyProjects: false);
        using (var policy = JsonDocument.Parse(File.ReadAllText(world.PolicyPath)))
            foreach (var row in policy.RootElement.GetProperty("projectDispositions").EnumerateArray())
            {
                var stub = Path.Combine(world.Clone, row.GetProperty("project").GetString()!.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(stub)!);
                File.WriteAllText(stub, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>" + Environment.NewLine);
            }
        client = Path.Combine(world.Clone, "client");
        Directory.CreateDirectory(client);
        var launcherDir = world.Fixture.PathOf("Program Files", "nodejs");
        Directory.CreateDirectory(launcherDir);
        launcher = Path.Combine(launcherDir, "npm.cmd");
        File.WriteAllText(launcher, "@echo off\r\nexit /b 0\r\n");
        world.Variable("PATH", launcherDir + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"));
        return world;
    }

    // ---- the command line --------------------------------------------------------------------------------------

    [Test]
    public void The_command_line_takes_every_flag_and_round_trips()
    {
        var options = NightlyTestsOptions.Parse(
        [
            "--repo-root", @"C:\x\clone", "--log-root", @"C:\x\logs", "--suites", "antiphon,client", "--sha", Sha, "--git-ref", "origin/rc/2026-11-21",
            "--trigger", "rc", "--profile", "rc", "--expected-sha", Sha, "--run-id", "run-1", "--selection-mode", "shadow", "--release-root", @"C:\r",
            "--candidate-id", "cand-1", "--repository", "o/r", "--baseline-sha", new string('b', 40), "--baseline-receipt-path", @"C:\r\b.json",
            "--qualification-path", @"C:\r\q.json", "--timing-root", @"C:\t", "--train-manifest-path", @"C:\r\train.json", "--release-intent-path", @"C:\r\intent.json",
            "--selection-attempt-path", @"C:\r\a.json", "--selection-attempt-digest", new string('c', 64), "--selection-path", @"C:\r\s.json",
            "--selection-digest", new string('d', 64), "--attempt", "2", "--prior-failures-path", @"C:\p.json", "--prior-failures-digest", new string('e', 64),
            "--accept-cross-candidate-seed", "--seed-source-candidate-id", "cand-0", "--rc-evidence-root", @"C:\ev", "--policy-path", @"C:\x\policy.json",
            "--allow-shared-tree", "--what-if",
        ]);
        options.Suites.ShouldBe(["antiphon", "client"]);
        options.SelectionMode.ShouldBe("Shadow");
        options.Attempt.ShouldBe(2);
        options.AcceptCrossCandidateSeed.ShouldBeTrue();
        options.AllowSharedTree.ShouldBeTrue();
        options.WhatIf.ShouldBeTrue();
        options.ReleaseIntentPath.ShouldBe(@"C:\r\intent.json");
        var again = NightlyTestsOptions.Parse(options.ToArguments());
        JsonSerializer.Serialize(again).ShouldBe(JsonSerializer.Serialize(options));

        var defaults = NightlyTestsOptions.Parse([]);
        defaults.Suites.ShouldBeNull();
        defaults.GitRef.ShouldBe("origin/master");
        defaults.Profile.ShouldBe("nightly");
        defaults.Repository.ShouldBe("michal-ciechan/Antiphon");
        defaults.SelectionMode.ShouldBe("Full");
        defaults.Attempt.ShouldBe(1);
        NightlyTestsOptions.Parse(["--suites", ","]).Suites.ShouldBe(["", ""]);
        Should.Throw<NightlyUsageException>(() => NightlyTestsOptions.Parse(["--selection-mode", "Partial"]));
        Should.Throw<NightlyUsageException>(() => NightlyTestsOptions.Parse(["--attempt", "two"]));
        Should.Throw<NightlyUsageException>(() => NightlyTestsOptions.Parse(["--seams-path", "x"]));
        Should.Throw<NightlyUsageException>(() => NightlyTestsOptions.Parse(["--what-if", "--what-if"]));
    }

    [Test]
    public void An_unsupported_host_platform_and_a_shadow_mode_without_an_attempt_refuse_before_the_run()
    {
        using var world = new NightlyTestsWorld("s15-refuse", copyProjects: false);
        world.Probe.PlatformName = "macos";
        var platform = world.Run();
        platform.ExitCode.ShouldBe(3);
        platform.Refusal.ShouldBe("unsupported-host-platform");
        platform.ProcessExitCode.ShouldBe(3);
        world.Probe.PlatformName = "windows";
        var shadow = world.Run(o => o.SelectionMode = "Shadow");
        shadow.Refusal.ShouldBe("selection-attempt-required");
        world.Launcher.Launches.ShouldBeEmpty();
    }

    // ---- the selection attempt ---------------------------------------------------------------------------------

    [Test]
    public void A_bound_selection_attempt_reads_and_each_drift_refuses_by_name()
    {
        using var fixture = NightlyFixture.Create("s15-selection");
        var request = SelectionFixture(fixture);
        var attempt = ReleaseSelectionAttempt.Read(request);
        PsValue.Str(PsValue.Prop(attempt, "format")).ShouldBe(ReleaseSelectionAttempt.AttemptFormat);
        ReleaseSelectionAttempt.ResolveMembers(attempt, ["antiphon", "client"], []).ShouldBe(new object[] { "antiphon", "client" });

        string Refusal(Action<ReleaseSelectionRequest> change)
        {
            var copy = SelectionFixture(fixture, reuse: true);
            change(copy);
            return NightlyDiagnosticCases.Refusal(() => ReleaseSelectionAttempt.Read(copy));
        }
        Refusal(r => r.SelectionAttemptDigest = new string('0', 64)).ShouldBe("selection-hop-digest");
        Refusal(r => r.SelectionDigest = "short").ShouldBe("selection-hop-digest");
        Refusal(r => r.RunId = "other-run").ShouldBe("selection-hop-runId");
        Refusal(r => r.Attempt = "2").ShouldBe("selection-hop-attempt");
        Refusal(r => r.Profile = "nightly").ShouldBe("selection-hop-profile");
        Refusal(r => r.TimingRoot = @"C:\elsewhere").ShouldBe("selection-hop-timingRoot");
        Refusal(r => r.Repository = "o/other").ShouldBe("selection-hop-candidate");
        Refusal(r => r.CandidateRef = "origin/rc/2026-11-20").ShouldBe("selection-hop-candidate");
        // The ref's origin/ prefix is dropped (any case), as -replace drops it.
        Refusal(r => r.CandidateRef = "ORIGIN/rc/2026-11-21").ShouldBe("");
        Refusal(r => r.TrainManifestPath = Path.Combine(r.ReleaseRoot!, "train.json")).ShouldBe("selection-hop-trainManifestPath");

        // A Partial attempt never executes selected members.
        var partial = new PlainObject(("effectiveMode", "Partial"), ("coverageScope", "Full"));
        NightlyDiagnosticCases.Refusal(() => ReleaseSelectionAttempt.ResolveMembers(partial, ["antiphon"], [])).ShouldBe("selected-execution-unqualified");
    }

    [Test]
    public void The_run_reads_the_hop_head_and_executes_full_for_a_bound_attempt()
    {
        using var world = new NightlyTestsWorld("s15-hop");
        var request = SelectionFixture(world.Fixture);
        void Hop(NightlyTestsOptions o)
        {
            o.ReleaseRoot = request.ReleaseRoot!; o.CandidateId = request.CandidateId!; o.RunId = request.RunId!; o.Profile = "rc"; o.GitRef = request.CandidateRef!;
            o.TimingRoot = request.TimingRoot!; o.SelectionAttemptPath = request.SelectionAttemptPath!; o.SelectionAttemptDigest = request.SelectionAttemptDigest!;
            o.SelectionPath = request.SelectionPath!; o.SelectionDigest = request.SelectionDigest!;
        }
        var r = world.Run(o => { Hop(o); o.WhatIf = true; });
        r.Refusal.ShouldBe("", r.Error);
        r.ExitCode.ShouldBe(0, r.Error);
        r.Selected.ShouldBe(NightlyPolicy.ProfileRequiredSuites(Policy().Object, "rc"));
        world.Git.Lines.ShouldContain("GIT rev-parse HEAD");
        world.TraceText.ShouldContain("GIT rev-parse HEAD");

        world.Git.Reply("rev-parse", new NightlyGitResult(0, new string('f', 40)));
        var head = world.Run(Hop);
        head.Refusal.ShouldBe("selection-hop-head");
        head.ProcessExitCode.ShouldBe(3);
        world.Launcher.Launches.ShouldBeEmpty();
    }

    /// <summary>
    /// A Full selection attempt and its selection written as Save-ReleaseSelectionAttempt writes them (canonical JSON under
    /// &lt;release root&gt;/candidates/&lt;id&gt;/attempts/&lt;run&gt;), and the request that binds them.
    /// </summary>
    internal static ReleaseSelectionRequest SelectionFixture(NightlyFixture fixture, bool reuse = false, string runId = "run-1", string? timingRoot = null)
    {
        var releaseRoot = fixture.PathOf("releases");
        var dir = Path.Combine(releaseRoot, "candidates", "cand-1", "attempts", runId);
        var attemptPath = Path.Combine(dir, "selection-attempt.json");
        var selectionPath = Path.Combine(dir, "selection.json");
        timingRoot ??= fixture.PathOf("timing");
        if (!reuse)
        {
            Directory.CreateDirectory(dir);
            var selection = new System.Collections.Specialized.OrderedDictionary
            {
                { "format", ReleaseSelectionAttempt.SelectionFormat }, { "candidateSha", Sha }, { "baselineSha", "" }, { "requestedMode", "Full" }, { "effectiveMode", "Full" },
                { "coverageScope", "Full" }, { "proposal", null }, { "fallbackReasons", new object[] { "baseline-absent" } }, { "executionEvidence", false },
            };
            var selectionText = NightlyDocuments.CanonicalJson(selection);
            File.WriteAllText(selectionPath, selectionText);
            var attempt = new System.Collections.Specialized.OrderedDictionary
            {
                { "format", ReleaseSelectionAttempt.AttemptFormat }, { "repository", "michal-ciechan/Antiphon" }, { "candidateId", "cand-1" }, { "candidateRef", "rc/2026-11-21" },
                { "candidateSha", Sha }, { "baselineSha", "" }, { "requestedMode", "Full" }, { "effectiveMode", "Full" }, { "coverageScope", "Full" }, { "profile", "rc" },
                { "runId", runId }, { "attempt", 1 }, { "selectionDigest", TestPhaseRecords.Sha256Text(selectionText) }, { "contract", null }, { "baselineDigest", "" },
                { "qualificationDigest", "" }, { "trainDigest", "" }, { "fallbackReasons", new object[] { "baseline-absent" } }, { "baselineReceiptPath", "" },
                { "qualificationPath", "" }, { "timingRoot", timingRoot }, { "releaseIntentPath", "" }, { "trainManifestPath", "" },
            };
            File.WriteAllText(attemptPath, NightlyDocuments.CanonicalJson(attempt));
        }
        return new ReleaseSelectionRequest
        {
            RepositoryRoot = fixture.PathOf("checkout"), CandidateSha = Sha, CandidateRef = "origin/rc/2026-11-21", Repository = "michal-ciechan/Antiphon", Profile = "rc",
            ReleaseRoot = releaseRoot, CandidateId = "cand-1", RunId = runId, SelectionMode = "Full", BaselineSha = "", BaselineReceiptPath = "", QualificationPath = "",
            TimingRoot = timingRoot, TrainManifestPath = "", ReleaseIntentPath = "", SelectionAttemptPath = attemptPath,
            SelectionAttemptDigest = NightlyPaths.FileSha256(attemptPath), SelectionPath = selectionPath, SelectionDigest = TestPhaseRecords.Sha256Text(File.ReadAllText(selectionPath)),
            Attempt = "1",
        };
    }

    // ---- the exact roster --------------------------------------------------------------------------------------

    [Test]
    public void The_exact_roster_runs_each_leaf_on_the_host_and_writes_its_receipt()
    {
        using var fixture = NightlyFixture.Create("s15-exact");
        var assembly = fixture.PathOf("bin", "Probe.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(assembly)!);
        File.WriteAllText(assembly, "exact roster assembly");
        var nodes = NightlyDiagnosticCases.ReadNodes(Path.Combine(ProbeRoot, "discovery.diag"), "discovery");
        var discovery = NightlyDiagnosticLog.NewDiscoveryDocument(nodes, NightlyDiagnosticLog.AssemblyHash(assembly));
        var plain = nodes.First(n => S(n, "Method") == "Plain");
        object[] members = [new PlainObject(("class", S(plain, "ClassName")), ("method", "Plain"))];
        var rows = new[] { new NightlyTrxRow(S(plain, "Uid"), S(plain, "ClassName"), "Plain") };
        var executed = new[] { new NightlyTestNode(S(plain, "Uid"), S(plain, "Type"), "Plain", "C487.Probe", ClassName: S(plain, "ClassName")) };
        var launcher = new RecordingLauncher(launch =>
        {
            var args = launch.Arguments;
            var results = args[args.ToList().IndexOf("--results-directory") + 1];
            NightlyFixture.WriteTrx(Path.Combine(results, args[args.ToList().IndexOf("--report-trx-filename") + 1]), rows);
            NightlyFixture.WriteExecution(Path.Combine(args[args.ToList().IndexOf("--diagnostic-output-directory") + 1], "log.diag"), executed);
            return RecordingLauncher.Passed();
        });
        var host = fixture.Host(launcher: launcher);
        var resultsDirectory = fixture.PathOf("exact");
        var receipt = NightlyTests.ExactRoster(host, assembly, discovery, members, resultsDirectory);
        receipt["complete"].ShouldBe(true);
        receipt["releaseCredit"].ShouldBe(false);
        ((string[])receipt["executedUids"]!).ShouldBe([S(plain, "Uid")]);
        File.Exists(Path.Combine(resultsDirectory, "receipt.json")).ShouldBeTrue();
        launcher.Launches.Count.ShouldBe(1);
        launcher.Launches[0].FileName.ShouldBe("dotnet");
        launcher.Launches[0].Arguments[0].ShouldBe(Path.GetFullPath(assembly));
        launcher.Launches[0].Custody.ShouldBe(NightlyCustody.Owned);
        launcher.Launches[0].WorkingDirectory.ShouldBe(resultsDirectory);

        NightlyDiagnosticCases.Refusal(() => NightlyTests.ExactRoster(host, assembly, discovery, members, resultsDirectory)).ShouldBe("execution-output-already-exists");
        File.WriteAllText(assembly, "a rebuilt assembly");
        NightlyDiagnosticCases.Refusal(() => NightlyTests.ExactRoster(host, assembly, discovery, members, fixture.PathOf("exact-2"))).ShouldBe("discovery-build-mismatch");
        launcher.Launches.Count.ShouldBe(1);
    }

    // ---- helpers -----------------------------------------------------------------------------------------------

    private static PlainObject Row(string outcome, string identity) => new(("Outcome", outcome), ("Identity", identity));

    private static object Verdict(object[] rows, int exit = 0, string sha = "a", string expectedSha = "a", string gitRef = "origin/master", string expectedRef = "origin/master",
        string policy = "p", string expectedPolicy = "p", object[]? discovery = null, bool listOnly = false) =>
        NightlyChunkVerdict.CoverageVerdict(rows, exit, sha, expectedSha, gitRef, expectedRef, policy, expectedPolicy, [], discovery ?? [], listOnly, "", null, null);

    private static object Native(string trx, string discovery, string execution, string runDirectory, string[]? classes = null) =>
        NightlyChunkVerdict.NativeSuiteVerdict(trx, discovery, execution, runDirectory, DateTime.UtcNow.AddMinutes(-5), 0, Sha, Sha, "origin/master", "origin/master", "p", "p",
            classes ?? [], null, false, null, "", null);

    private static bool B(object? o, string name) => NightlyDiagnosticCases.B(o, name);
    private static object[] Items(object? o, string name) => NightlyDiagnosticCases.Items(o, name);
    private static string S(object? o, string name) => NightlyDiagnosticCases.S(o, name);
    private static string Joined(object[] values) => string.Join(",", values.Select(PsValue.Str));

    private sealed record ProbeEvidence(string TrxPath, string DiscoveryPath, string ExecutionPath, object[] DiscoveryNodes, object[] ExecutionNodes);

    /// <summary>New-C487FreshProbeEvidence: the probe's TRX (touched now), its discovery as a document, its execution diagnostic.</summary>
    private static ProbeEvidence FreshProbe(string runDirectory)
    {
        Directory.CreateDirectory(runDirectory);
        var trx = Path.Combine(runDirectory, "all.trx");
        File.Copy(Path.Combine(ProbeRoot, "all.trx"), trx, true);
        File.SetLastWriteTimeUtc(trx, DateTime.UtcNow);
        var discovery = NightlyDiagnosticCases.ReadNodes(Path.Combine(ProbeRoot, "discovery.diag"), "discovery");
        var discoveryPath = Path.Combine(runDirectory, "discovery.json");
        NightlyDiagnosticLog.WriteDiscoveryDocument(discoveryPath, NightlyDiagnosticLog.NewDiscoveryDocument(discovery, "c487-probe-hash"));
        var execution = Path.Combine(runDirectory, "execution.diag");
        File.Copy(Path.Combine(ProbeRoot, "execution.diag"), execution, true);
        return new ProbeEvidence(trx, discoveryPath, execution, discovery, NightlyDiagnosticCases.ReadNodes(execution, "execution"));
    }
}
