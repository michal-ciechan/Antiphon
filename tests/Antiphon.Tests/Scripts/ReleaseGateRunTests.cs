using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-0599 V-2, V-3 and R-4. The credit cases run the matching <c>Test-C599_*</c> case of
/// <c>scripts/test-release-gate.ps1</c> against the PRODUCTION credit predicate, using private
/// clone/state/release/coordination roots; C599_SharedLock and C599_Continuation call the compiled shared lock
/// in-process (W2-nightly S18), and the cases that ran the run core (SelectionHopExitCodes, MasterStateIsolation,
/// ParameterHops, the rc half of CloneOwnership, SharedLockDeferredRun) run NightlyRun.Run in-process (W2-nightly S20,
/// NightlyRcChain: a real rc sync with the self-update hop and the tests hop answered in this process).
///
/// <para>The decisive behaviours: master green and RC green are separate kinds of credit that
/// cannot be exchanged, an RC run leaves all four master state files byte-identical, the pinned
/// SHA and profile survive every wrapper and self-reexec hop, and a live lock owner is never
/// stolen on age - a contended lane records <c>deferred-busy</c> instead.</para>
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class ReleaseGateRunTests
{
    /// <summary>
    /// W2-nightly S20, in-process (was the test-release-gate.ps1 case on Invoke-NightlyWatchedCommand): the self-update hop
    /// is a Watched pwsh child (WatchedLauncher, the custody NightlyRun's hops take); its exit code passes through and each
    /// argument keeps its spaces, and an empty value stays an empty value.
    /// </summary>
    [Test]
    public void C599_SelectionHopExitCodes()
    {
        using var fixture = NightlyFixture.Create("c599-hop-exit");
        var child = fixture.PathOf("exit child.ps1");
        File.WriteAllText(child, "param([int]$Code, [string]$Value, [string]$Marker, [string]$SelectionAttemptPath, [string]$Empty = 'unset')\n"
            + "[IO.File]::WriteAllText($Marker, ($Value + '|' + $SelectionAttemptPath + '|' + $Empty))\nexit $Code\n");
        var launcher = new WatchedLauncher();
        var rows = new NightlyCaseRows();
        foreach (var code in new[] { 0, 4, 7 })
        {
            var marker = fixture.PathOf("received-" + code + ".txt");
            var attempt = fixture.PathOf("selection attempt.json");
            var result = launcher.Run(new NightlyLaunch("pwsh",
                ["-NoProfile", "-NonInteractive", "-File", child, "-Code", code.ToString(System.Globalization.CultureInfo.InvariantCulture), "-Value", "value with spaces",
                    "-Marker", marker, "-SelectionAttemptPath", attempt, "-Empty", ""], fixture.Root));
            var received = File.Exists(marker) ? File.ReadAllText(marker) : "";
            var expected = "value with spaces|" + attempt + "|";
            rows.Check(result.ExitCode == code && received == expected, "C599 SelectionHopExitCodes child exit " + code + " and spaced argument survive",
                "expected=" + code + " actual=" + result.ExitCode + " received=[" + received + "]");
        }
        rows.ShouldAllPass(3, "C599 SelectionHopExitCodes child exit 0 and spaced argument survive", "C599 SelectionHopExitCodes child exit 4 and spaced argument survive",
            "C599 SelectionHopExitCodes child exit 7 and spaced argument survive");
    }

    [Test]
    public Task C599_CreditIdentity() => RunCaseAsync("C599_CreditIdentity", 34,
        "C599 CreditIdentity nightly/scheduled/master earns [master-scheduled]",
        "C599 CreditIdentity rc/rc/release/rc-20260923T083000Z earns [rc-release]",
        "C599 CreditIdentity rc/rc/master earns []",
        "C599 CreditIdentity nightly/scheduled/release/rc-20260923T083000Z earns []",
        "C599 CreditIdentity malformed [rc/rc/release/current] earns nothing",
        "C599 CreditIdentity absent profile is still the master lane");

    [Test]
    public Task C599_CreditVerdicts() => RunCaseAsync("C599_CreditVerdicts", 24,
        "C599 CreditVerdicts control master green earns master credit",
        "C599 CreditVerdicts master green never earns release credit",
        "C599 CreditVerdicts control rc green earns release credit",
        "C599 CreditVerdicts rc green never earns master credit",
        "C599 CreditVerdicts coverageComplete as the STRING \"true\" earns nothing",
        "C599 CreditVerdicts rc sha mismatch earns nothing",
        "C599 CreditVerdicts an invented credit kind earns nothing");

    // ---- W2-nightly S20: the cases that ran the PowerShell run core (Invoke-C599Run on New-C599RunFx), in-process ----

    private const string CandidateRef = "release/rc-20260923T083000Z";
    private const string CandidateId = "rc-20260923T083000Z";
    private static readonly string ShaA = new('a', 40);

    /// <summary>Invoke-C599Run on the synthetic owned clone: -NoReport, scheduled master, private state, release and coordination roots.</summary>
    private static NightlyRunResult C599Run(NightlyRunWorld w, Action<NightlyRunOptions> extra) =>
        w.Run(o =>
        {
            o.ReleaseRoot = w.Releases;
            extra(o);
        });

    /// <summary>Invoke-C599Run on the rc chain (New-C599RunFx -Rc): a real sync, the selection bound, the self-update hop and the tests hop in-process.</summary>
    private static NightlyRunResult C599RcRun(NightlyRcChain chain, string runId, string stateRoot, string logRoot, string candidateId)
    {
        var options = chain.Options(runId, o =>
        {
            o.NoSync = false;
            o.WhatIf = false;
            o.NoReport = true;
            o.StateRoot = stateRoot;
            o.LogRoot = logRoot;
            o.ReleaseRoot = chain.Fixture.PathOf("releases");
            o.CoordinationRoot = chain.Fixture.PathOf("coord");
            o.CandidateId = candidateId;
        });
        return chain.Run(options);
    }

    private static bool IsRunSummary(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)) is JsonObject o && o.ContainsKey("runId"); }
        catch (JsonException) { return false; }
    }

    private static string S(JsonNode? node, string name) => node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var text) ? text : "";

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject o => "{" + string.Join(",", o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => JsonSerializer.Serialize(p.Key) + ":" + Canonical(p.Value))) + "}",
        JsonArray a => "[" + string.Join(",", a.Select(Canonical)) + "]",
        null => "null",
        _ => node.ToJsonString(),
    };

    private static string Sha256(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Assert-C599RcChild: the outer run hopped, its child wrote one summary naming the run, with the pinned identity and the bound Full selection.</summary>
    private static void RcChild(NightlyCaseRows rows, NightlyRcChain chain, NightlyRunResult result, string runId, string logRoot, string label)
    {
        rows.Check(result.Phase == "hop" && string.IsNullOrEmpty(result.Refusal) && result.Sha == chain.Sha, "C599 " + label + " outer run reaches self-reexec without refusal",
            "phase=" + result.Phase + " refusal=" + result.Refusal + " exit=" + result.ExitCode + "\n" + chain.Fixture.Out + chain.Fixture.Error);
        var summaries = Directory.Exists(logRoot) ? Directory.GetFiles(logRoot, "summary.json", SearchOption.AllDirectories).Where(IsRunSummary).ToArray() : [];
        var summary = summaries.Length == 1 ? NightlyRcChain.Json(summaries[0]) : null;
        rows.Check(summaries.Length == 1 && S(summary, "runId") == runId, "C599 " + label + " exactly one fresh child summary names this run", "summaries=" + summaries.Length);
        rows.Check(S(summary, "profile") == "rc" && S(summary, "trigger") == "rc" && S(summary, "sha") == chain.Sha && S(summary, "expectedSha") == chain.Sha
            && S(summary, "gitRef") == "origin/" + chain.Ref && S(summary, "runId") == runId, "C599 " + label + " child consumed the pinned RC identity", summary?.ToJsonString() ?? "");
        var selection = summary?["selection"];
        var id = chain.CandidateId();
        rows.Check(S(selection, "candidateSha") == chain.Sha && S(selection, "candidateRef") == chain.Ref && S(selection, "candidateId") == id && S(selection, "runId") == runId
            && S(selection, "profile") == "rc" && S(selection, "requestedMode") == "Full" && S(selection, "effectiveMode") == "Full" && S(selection, "coverageScope") == "Full"
            && S(selection, "baselineSha") == "" && S(summary, "coverageScope") == "Full", "C599 " + label + " child consumed the bound Full selection", selection?.ToJsonString() ?? "");
        var attemptRoot = Path.Combine(chain.Fixture.PathOf("releases"), "candidates", id, "attempts", runId);
        var attemptPath = Path.Combine(attemptRoot, "selection-attempt.json");
        var selectionPath = Path.Combine(attemptRoot, "selection.json");
        var bound = NightlyRcChain.Json(attemptPath);
        var artifact = NightlyRcChain.Json(selectionPath);
        rows.Check(bound != null && artifact != null && S(artifact, "candidateSha") == chain.Sha && S(artifact, "requestedMode") == "Full" && S(artifact, "effectiveMode") == "Full"
            && S(artifact, "coverageScope") == "Full" && S(bound, "selectionDigest") == Sha256(selectionPath) && Canonical(selection) == Canonical(bound),
            "C599 " + label + " child selection matches the immutable bound artifacts", Canonical(selection) + "\n" + Canonical(bound));
    }

    /// <summary>W2-nightly S20, in-process: an RC run leaves every master state file byte-identical, and an RC pointed at the master state root refuses.</summary>
    [Test, Timeout(600_000)]
    public void C599_MasterStateIsolation(CancellationToken token)
    {
        using var chain = new NightlyRcChain("c599-master", CandidateRef).Publish();
        var rows = new NightlyCaseRows();
        var master = chain.Fixture.PathOf("state");
        Directory.CreateDirectory(master);
        var masterFiles = ReleaseGatePublication.MasterStateFiles(master);
        foreach (var file in masterFiles) File.WriteAllText(file, "{\"sentinel\":\"" + Path.GetFileName(file) + "\"}");
        var before = masterFiles.ToDictionary(f => f, Sha256);
        var rcState = chain.Fixture.PathOf("rc-state");
        Directory.CreateDirectory(rcState);
        rows.Check(Directory.GetFiles(chain.Root, "summary.json", SearchOption.AllDirectories).Length == 0, "C599 MasterStateIsolation run starts without child summaries");
        var runId = "c599-" + Guid.NewGuid().ToString("N");
        var r = C599RcRun(chain, runId, rcState, Path.Combine(rcState, "logs"), CandidateId);
        RcChild(rows, chain, r, runId, Path.Combine(rcState, "logs"), "MasterStateIsolation");
        foreach (var file in masterFiles)
            rows.Check(Sha256(file) == before[file], "C599 MasterStateIsolation " + Path.GetFileName(file) + " is byte-identical after an rc run");
        var refuse = chain.Run(chain.Options("master-root", o =>
        {
            o.NoSync = false; o.WhatIf = false; o.NoReport = true; o.CandidateId = CandidateId; o.StateRoot = NightlyRun.DefaultStateRoot;
        }));
        rows.Check(refuse.ExitCode == 3 && refuse.Refusal == "rc-master-state-root", "C599 MasterStateIsolation rc into the master state root refuses", refuse.Refusal);
        rows.ShouldAllPass(11, "C599 MasterStateIsolation run starts without child summaries", "C599 MasterStateIsolation outer run reaches self-reexec without refusal",
            "C599 MasterStateIsolation exactly one fresh child summary names this run", "C599 MasterStateIsolation child consumed the pinned RC identity",
            "C599 MasterStateIsolation child consumed the bound Full selection", "C599 MasterStateIsolation child selection matches the immutable bound artifacts",
            "C599 MasterStateIsolation last-run.json is byte-identical after an rc run", "C599 MasterStateIsolation last-complete-green.json is byte-identical after an rc run",
            "C599 MasterStateIsolation last-monitor.json is byte-identical after an rc run",
            "C599 MasterStateIsolation interim-qualification-receipt.json is byte-identical after an rc run", "C599 MasterStateIsolation rc into the master state root refuses");
    }

    /// <summary>
    /// W2-nightly S20, in-process: refusals that fire only when the identity reached the core; the candidate id derived from
    /// the ref; the identity through the self-update hop and the tests hop of a real rc chain; and the launcher
    /// (scripts/nightly-run.ps1) declares each identity parameter and passes it to the command as its flag.
    /// </summary>
    [Test, Timeout(600_000)]
    public void C599_ParameterHops(CancellationToken token)
    {
        var rows = new NightlyCaseRows();
        using (var w = new NightlyRunWorld("c599-hops"))
        {
            var unknown = C599Run(w, o => o.Profile = "turbo");
            rows.Check(unknown.ExitCode == 3 && unknown.Refusal == "unknown-profile", "C599 ParameterHops unknown profile refuses at the core", unknown.Refusal);
            var badRef = C599Run(w, o => { o.Profile = "rc"; o.Trigger = "rc"; o.Ref = "master"; o.ExpectedSha = ShaA; o.StateRoot = w.Fixture.PathOf("s1"); });
            rows.Check(badRef.Refusal == "rc-ref", "C599 ParameterHops rc with a master ref refuses", badRef.Refusal);
            var noSha = C599Run(w, o => { o.Profile = "rc"; o.Trigger = "rc"; o.Ref = CandidateRef; o.StateRoot = w.Fixture.PathOf("s2"); });
            rows.Check(noSha.Refusal == "rc-expected-sha", "C599 ParameterHops rc without ExpectedSha refuses", noSha.Refusal);
            var shortSha = C599Run(w, o => { o.Profile = "rc"; o.Trigger = "rc"; o.Ref = CandidateRef; o.ExpectedSha = "abcd"; o.StateRoot = w.Fixture.PathOf("s3"); });
            rows.Check(shortSha.Refusal == "rc-expected-sha", "C599 ParameterHops a short ExpectedSha refuses", shortSha.Refusal);
            var rcTrigger = C599Run(w, o => { o.Profile = "nightly"; o.Trigger = "rc"; o.StateRoot = w.Fixture.PathOf("s4"); });
            rows.Check(rcTrigger.Refusal == "trigger-profile-mismatch", "C599 ParameterHops trigger rc without the rc profile refuses", rcTrigger.Refusal);
        }

        using (var chain = new NightlyRcChain("c599-hops-rc", CandidateRef).Publish())
        {
            rows.Check(Directory.GetFiles(chain.Root, "summary.json", SearchOption.AllDirectories).Length == 0, "C599 ParameterHops run starts without child summaries");
            var runId = "c599-" + Guid.NewGuid().ToString("N");
            var logs = chain.Fixture.PathOf("s5-logs");
            var derived = C599RcRun(chain, runId, chain.Fixture.PathOf("s5"), logs, "");
            rows.Check(derived.CandidateId == CandidateId, "C599 ParameterHops candidate id is derived from the ref", derived.CandidateId);
            RcChild(rows, chain, derived, runId, logs, "ParameterHops");
            var hop = chain.Launcher.Launches.FirstOrDefault(NightlyRunHops.IsSelfHop)?.Arguments.ToArray() ?? [];
            string After(string flag) => Array.IndexOf(hop, flag) is var i and >= 0 && i + 1 < hop.Length ? hop[i + 1] : "";
            rows.Check(After("-Profile") == "rc", "C599 ParameterHops the self-reexec hop forwards -Profile", string.Join(" ", hop));
            rows.Check(After("-ExpectedSha") == chain.Sha, "C599 ParameterHops the self-reexec hop forwards -ExpectedSha", string.Join(" ", hop));
            rows.Check(After("-CandidateId") == CandidateId, "C599 ParameterHops the self-reexec hop forwards -CandidateId", string.Join(" ", hop));
            rows.Check(After("-ReleaseRoot") == chain.Fixture.PathOf("releases"), "C599 ParameterHops the self-reexec hop forwards -ReleaseRoot", string.Join(" ", hop));
            rows.Check(After("-CoordinationRoot") == chain.Fixture.PathOf("coord"), "C599 ParameterHops the self-reexec hop forwards -CoordinationRoot", string.Join(" ", hop));
            rows.Check(NightlyTestsOptions.Valued.Contains("--profile") && NightlyTestsOptions.Valued.Contains("--expected-sha"), "C599 ParameterHops nightly-tests accepts --profile and --expected-sha");
            var tests = chain.TestsHops.FirstOrDefault();
            rows.Check(tests is { Profile: "rc" } && tests.ExpectedSha == chain.Sha, "C599 ParameterHops the tests hop forwards -Profile", tests == null ? "no tests hop" : tests.Profile + "/" + tests.ExpectedSha);
        }

        // The launcher declares each parameter and passes it to the nightly-run command as its flag.
        var launcher = File.ReadAllText(Path.Combine(DelegateScriptRunner.RepoRoot, "scripts", "nightly-run.ps1"));
        foreach (var p in new[] { "Profile", "ExpectedSha", "CandidateId", "ReleaseRoot", "CoordinationRoot" })
        {
            rows.Check(Regex.IsMatch(launcher, @"\[string\]\$" + p + " ="), "C599 ParameterHops nightly-run.ps1 declares -" + p);
            rows.Check(launcher.Contains("'" + NightlyRunHops.Kebab("-" + p) + "', $" + p, StringComparison.Ordinal), "C599 ParameterHops nightly-run.ps1 forwards -" + p + " to the command");
        }
        rows.ShouldAllPass(29, "C599 ParameterHops unknown profile refuses at the core", "C599 ParameterHops rc with a master ref refuses",
            "C599 ParameterHops rc without ExpectedSha refuses", "C599 ParameterHops trigger rc without the rc profile refuses", "C599 ParameterHops candidate id is derived from the ref",
            "C599 ParameterHops run starts without child summaries", "C599 ParameterHops outer run reaches self-reexec without refusal",
            "C599 ParameterHops exactly one fresh child summary names this run", "C599 ParameterHops child consumed the pinned RC identity",
            "C599 ParameterHops child consumed the bound Full selection", "C599 ParameterHops child selection matches the immutable bound artifacts",
            "C599 ParameterHops the self-reexec hop forwards -Profile", "C599 ParameterHops the tests hop forwards -Profile");
    }

    /// <summary>
    /// An rc run refuses the shared tree and a card worktree as its checkout (in-process on NightlyRun.Run since W2-nightly
    /// S20; refused before any state is written), and the RC coordinator applies the same ownership rule to its own clone
    /// (the still-PowerShell release-candidate.ps1 rows of test-release-gate.ps1).
    /// </summary>
    [Test]
    public async Task C599_CloneOwnership()
    {
        if (!OperatingSystem.IsWindows()) throw new TUnit.Core.Exceptions.SkipTestException("CARD-1340: Windows canonical roots C:\\src\\Antiphon and C:\\Antiphon\\worktrees");
        var rows = new NightlyCaseRows();
        using (var w = new NightlyRunWorld("c599-ownership"))
        {
            foreach (var path in new[] { @"C:\src\Antiphon", @"C:\Antiphon\worktrees\card-task-0233729c" })
            {
                var state = w.Fixture.PathOf("o" + Guid.NewGuid().ToString("N"));
                var r = C599Run(w, o => { o.CheckoutRoot = path; o.Profile = "rc"; o.Trigger = "rc"; o.Ref = CandidateRef; o.ExpectedSha = ShaA; o.StateRoot = state; });
                rows.Check(r.ExitCode == 3 && !Directory.Exists(state), "C599 CloneOwnership rc refuses " + path, r.Refusal);
            }
        }
        rows.ShouldAllPass(2, @"C599 CloneOwnership rc refuses C:\src\Antiphon", @"C599 CloneOwnership rc refuses C:\Antiphon\worktrees\card-task-0233729c");
        await RunCaseAsync("C599_CloneOwnership", 3,
            "C599 CloneOwnership coordinator refuses an unmarked clone",
            "C599 CloneOwnership coordinator refuses the shared tree",
            "C599 CloneOwnership control owned clone is accepted");
    }

    /// <summary>
    /// D-4 in-process (W2-nightly S18): the shared native-run lock (Antiphon.Nightly/ReleaseGateNativeLock.cs) on a
    /// private coordination root. The contended run that records deferred-busy is C599_SharedLockDeferredRun: the run
    /// core is still PowerShell.
    /// </summary>
    [Test]
    public void C599_SharedLock()
    {
        using var fixture = NightlyFixture.Create("c599-shared-lock");
        var processes = new FakeProcessTable().Start(4242, new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc));
        var native = new ReleaseGateNativeLock(fixture.Host(processes: processes));
        var coord = fixture.PathOf("coord");
        var lockPath = ReleaseGateNativeLock.LockPath(coord);

        var first = native.Enter(coord, "run-master", "master");
        (first.Ok && first.OwnsLock).ShouldBeTrue("C599 SharedLock first lane acquires " + first.Reason);
        File.Exists(lockPath).ShouldBeTrue("C599 SharedLock the lock file exists on disk");

        // A live owner is never stolen, whatever the other lane is.
        var second = native.Enter(coord, "run-rc", "rc");
        (!second.Ok && second.Reason == "deferred-busy").ShouldBeTrue("C599 SharedLock the second lane gets deferred-busy " + second.Reason);
        second.OwnsLock.ShouldBeFalse("C599 SharedLock the deferred lane owns nothing");

        var record = NightlyLockRecord.Read(lockPath)!;
        record.Pid.ShouldBe(processes.CurrentPid, "C599 SharedLock owner identity records the pid " + record.Pid);
        record.Lane.ShouldBe("master", "C599 SharedLock owner identity records the lane " + record.Lane);
        string.IsNullOrWhiteSpace(record.ProcessStartedAt).ShouldBeFalse("C599 SharedLock owner identity records the process start " + record.ProcessStartedAt);

        native.Exit(coord, "run-master");
        File.Exists(lockPath).ShouldBeFalse("C599 SharedLock release removes the lock file");
        var third = native.Enter(coord, "run-rc2", "rc");
        (third.Ok && third.OwnsLock).ShouldBeTrue("C599 SharedLock the other lane acquires once released " + third.Reason);
        native.Exit(coord, "run-rc2");

        // A dead holder is reported stale and left alone (no reclaim on the shared lock).
        File.WriteAllText(lockPath, """{"pid":424599,"processStartedAt":"","runId":"gone","lane":"master","continuationId":"","acquiredAt":""}""");
        processes.Exit(424599);
        var stale = native.Enter(coord, "run-rc3", "rc");
        (!stale.Ok && stale.Reason == "stale-shared-lock" && !stale.OwnsLock).ShouldBeTrue("a dead holder is stale-shared-lock " + stale.Reason);
        NightlyLockRecord.Read(lockPath)!.RunId.ShouldBe("gone", "the stale lock is not reclaimed");
    }

    /// <summary>W2-nightly S20, in-process on NightlyRun.Run: a contended rc run records deferred-busy and stays non-green.</summary>
    [Test]
    public void C599_SharedLockDeferredRun()
    {
        using var w = new NightlyRunWorld("c599-deferred");
        var rows = new NightlyCaseRows();
        Directory.CreateDirectory(w.Coordination);
        var lockPath = ReleaseGateNativeLock.LockPath(w.Coordination);
        File.WriteAllText(lockPath, "{\"pid\":999999,\"processStartedAt\":\"2026-09-01T06:00:00.0000000Z\",\"runId\":\"other-run\",\"lane\":\"master\",\"continuationId\":\"\",\"acquiredAt\":\"2026-09-01T06:00:00.0000000Z\"}");
        rows.Check(NightlyLockRecord.Read(lockPath)?.RunId == "other-run", "C599 SharedLock control: a foreign owner holds the lock");
        var busyState = w.Fixture.PathOf("busy");
        var busy = C599Run(w, o => { o.Profile = "rc"; o.Trigger = "rc"; o.Ref = CandidateRef; o.ExpectedSha = ShaA; o.StateRoot = busyState; });
        rows.Check(busy.ExitCode == 3 && busy.Refusal == "deferred-busy", "C599 SharedLock a contended rc run returns deferred-busy", busy.Refusal);
        var recorded = S(NightlyRcChain.Json(Path.Combine(busyState, "last-run.json")), "phase");
        rows.Check(recorded == "deferred-busy", "C599 SharedLock the deferred attempt is recorded on disk", recorded);
        rows.ShouldAllPass(3, "C599 SharedLock control: a foreign owner holds the lock", "C599 SharedLock a contended rc run returns deferred-busy",
            "C599 SharedLock the deferred attempt is recorded on disk");
    }

    /// <summary>D-4 in-process (W2-nightly S18): a self-reexec hop inherits the shared lock only with the owner's exact identity.</summary>
    [Test]
    public void C599_Continuation()
    {
        using var fixture = NightlyFixture.Create("c599-continuation");
        var processes = new FakeProcessTable().Start(4242, new DateTime(2026, 9, 11, 11, 0, 0, DateTimeKind.Utc)).Exit(999999);
        var native = new ReleaseGateNativeLock(fixture.Host(processes: processes));
        var coord = fixture.PathOf("coord");
        var own = native.Enter(coord, "run-1", "master");
        own.Ok.ShouldBeTrue("C599 Continuation control: the parent owns the shared lock");
        var start = own.Record!.ProcessStartedAt;
        var pid = processes.CurrentPid;

        var hop = native.Enter(coord, "run-1", "master", "", pid, start);
        (hop.Ok && !hop.OwnsLock && hop.Reason == "hop").ShouldBeTrue("C599 Continuation a matching continuation inherits without owning " + hop.Reason);
        var wrongRun = native.Enter(coord, "run-2", "master", "", pid, start);
        (!wrongRun.Ok && wrongRun.Reason == "shared-hop-identity").ShouldBeTrue("C599 Continuation a mismatched run id refuses " + wrongRun.Reason);
        var wrongLane = native.Enter(coord, "run-1", "rc", "", pid, start);
        (!wrongLane.Ok && wrongLane.Reason == "shared-hop-identity").ShouldBeTrue("C599 Continuation a mismatched lane refuses " + wrongLane.Reason);
        var wrongStart = native.Enter(coord, "run-1", "master", "", pid, "2001-01-01T00:00:00.0000000Z");
        (!wrongStart.Ok && wrongStart.Reason == "shared-hop-identity").ShouldBeTrue("C599 Continuation a mismatched process start refuses " + wrongStart.Reason);
        var deadParent = native.Enter(coord, "run-1", "master", "", 999999, start);
        deadParent.Ok.ShouldBeFalse("C599 Continuation a dead continuation parent refuses " + deadParent.Reason);
        native.Exit(coord, "run-1");
        File.Exists(ReleaseGateNativeLock.LockPath(coord)).ShouldBeFalse("the owner's exit removes the lock");
        native.Enter(coord, "run-1", "master", "", pid, start).Reason.ShouldBe("missing-shared-lock", "a continuation without a lock refuses");
    }

    [Test]
    public Task C599_ReportLane() => RunCaseAsync("C599_ReportLane", 5,
        "C599 ReportLane the two lanes write different green files",
        "C599 ReportLane master green path is unchanged",
        "C599 ReportLane rc green lands under its candidate root",
        "C599 ReportLane the two lanes never share a credit kind");

    private static Task RunCaseAsync(string caseName, int expectedRows, params string[] requiredRows) =>
        ScriptHarness.RunHarnessCaseAsync("test-release-gate.ps1", "C599", caseName, expectedRows, requiredRows);
}
