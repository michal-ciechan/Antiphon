using System.Collections;
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
/// CARD-0599 V-2, V-3 and R-4. The credit cases (C599_CreditIdentity, C599_CreditVerdicts, C599_ReportLane) call the
/// PRODUCTION credit kind, verdict, complete-green predicate and state paths in process with the row names of the
/// <c>Test-C599_*</c> cases of the deleted <c>scripts/test-release-gate.ps1</c> (unit 15 of the 2026-11-22 plan), on
/// private roots; C599_SharedLock and C599_Continuation call the compiled shared lock
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
    /// (in process on ReleaseCandidate.Run since unit 13 of 2026-11-22-remaining-ps1-to-csharp.md, over the fake remote of
    /// ReleaseCandidateTests: an unmarked clone and the shared tree refuse before any git call).
    /// </summary>
    [Test]
    public void C599_CloneOwnership()
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
        using (var fx = NightlyFixture.Create("c599-ownership-candidate"))
        {
            var git = new FakeCandidateRemote(new string('b', 40));
            var releaseRoot = fx.PathOf("releases");
            var unmarked = fx.PathOf("unmarked");
            Directory.CreateDirectory(Path.Combine(unmarked, ".git"));
            var r1 = ReleaseCandidateCases.Cut(fx, git, releaseRoot, unmarked, CandidateRef);
            rows.Check(r1.Refusal == "unmarked-clone", "C599 CloneOwnership coordinator refuses an unmarked clone", r1.Refusal);
            var r2 = ReleaseCandidateCases.Cut(fx, git, releaseRoot, @"C:\src\Antiphon", CandidateRef);
            rows.Check(r2.Refusal == "shared-tree" && git.Trace.Count == 0, "C599 CloneOwnership coordinator refuses the shared tree", r2.Refusal + " " + string.Join("|", git.Trace));
            var r3 = ReleaseCandidateCases.Cut(fx, git, releaseRoot, ReleaseCandidateCases.OwnedClone(fx), CandidateRef);
            rows.Check(r3.ExitCode == 0, "C599 CloneOwnership control owned clone is accepted", r3.Refusal);
        }
        rows.ShouldAllPass(5, @"C599 CloneOwnership rc refuses C:\src\Antiphon", @"C599 CloneOwnership rc refuses C:\Antiphon\worktrees\card-task-0233729c",
            "C599 CloneOwnership coordinator refuses an unmarked clone", "C599 CloneOwnership coordinator refuses the shared tree",
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
    public void C599_CreditIdentity()
    {
        // credit kind x profile x trigger x ref, plus malformed/foreign identity rows (ReleaseGateCredit.CreditKind).
        var rows = new NightlyCaseRows();
        foreach (var p in new[] { "nightly", "rc" })
            foreach (var t in new[] { "scheduled", "manual", "rc" })
                foreach (var r in new[] { "master", "origin/master", "feat/x", CandidateRef })
                {
                    var kind = ReleaseGateCredit.CreditKind(p, t, r);
                    var expected = p == "nightly" && t == "scheduled" && (r == "master" || r == "origin/master") ? "master-scheduled"
                        : p == "rc" && t == "rc" && r == CandidateRef ? "rc-release" : "";
                    rows.Check(kind == expected, "C599 CreditIdentity " + p + "/" + t + "/" + r + " earns [" + expected + "]", "got=" + kind);
                }
        // Malformed and foreign identity rows earn nothing.
        foreach (var (p, t, r) in new[]
                 {
                     ("turbo", "scheduled", "master"), ("nightly", "scheduled", "refs/heads/master"), ("rc", "rc", "release/current"),
                     ("rc", "rc", "release/rc-2026-09-23T083000Z"), ("rc", "rc", "release/rc-20261332T083000Z"), ("", "rc", CandidateRef), ("rc", "", CandidateRef),
                     ("nightly", "feature", "master"),
                 })
        {
            var kind = ReleaseGateCredit.CreditKind(p, t, r);
            rows.Check(string.IsNullOrWhiteSpace(kind), "C599 CreditIdentity malformed [" + p + "/" + t + "/" + r + "] earns nothing", "got=" + kind);
        }
        // An absent profile is the pre-CARD-0599 master lane, unchanged.
        var legacy = ReleaseGateCredit.CreditKind("", "scheduled", "master");
        rows.Check(legacy == "master-scheduled", "C599 CreditIdentity absent profile is still the master lane", legacy);
        // The refs/heads/ prefix is accepted for a candidate but not for master.
        var prefixed = ReleaseGateCredit.CreditKind("rc", "rc", "refs/heads/" + CandidateRef);
        rows.Check(prefixed == "rc-release", "C599 CreditIdentity refs/heads candidate ref is accepted", prefixed);
        rows.ShouldAllPass(34,
            "C599 CreditIdentity nightly/scheduled/master earns [master-scheduled]",
            "C599 CreditIdentity rc/rc/release/rc-20260923T083000Z earns [rc-release]",
            "C599 CreditIdentity rc/rc/master earns []",
            "C599 CreditIdentity nightly/scheduled/release/rc-20260923T083000Z earns []",
            "C599 CreditIdentity malformed [rc/rc/release/current] earns nothing",
            "C599 CreditIdentity absent profile is still the master lane");
    }

    [Test]
    public void C599_CreditVerdicts() => CreditVerdictRows().ShouldAllPass(24,
        "C599 CreditVerdicts control master green earns master credit",
        "C599 CreditVerdicts master green never earns release credit",
        "C599 CreditVerdicts control rc green earns release credit",
        "C599 CreditVerdicts rc green never earns master credit",
        "C599 CreditVerdicts coverageComplete as the STRING \"true\" earns nothing",
        "C599 CreditVerdicts rc sha mismatch earns nothing",
        "C599 CreditVerdicts an invented credit kind earns nothing");

    // New-C599State: a run state as the run core writes it (a PowerShell-read map: names ignore case), green by default.
    private static Hashtable State(string profile = "nightly", string trigger = "scheduled", string reference = "master", int exitCode = 0, string sha = "",
        string expectedSha = "", string candidate = "", string runId = "run-1", string policyHash = "hash-1") => new(StringComparer.OrdinalIgnoreCase)
    {
        ["runId"] = runId, ["policyHash"] = policyHash, ["profile"] = profile, ["trigger"] = trigger, ["ref"] = reference, ["coverageComplete"] = true,
        ["testsPassed"] = true, ["reportDelivered"] = true, ["exitCode"] = exitCode, ["sha"] = sha, ["expectedSha"] = expectedSha, ["candidateId"] = candidate,
    };

    // The production credit verdict over the state's plain values (ReleaseGateCredit.Verdict, the run core's decision): the
    // credit kind ("" for none) and the reasons. Unit 15 removed its PowerShell-shaped twin (CreditVerdict) and the
    // complete-green predicate over it (NightlyRunParts.CompleteGreenPredicate), whose last caller was the deleted driver.
    private static (string Kind, string Reasons) Verdict(Hashtable state)
    {
        var verdict = ReleaseGateCredit.Verdict(Plain(state));
        return (verdict.CreditKind ?? "", string.Join(";", verdict.Reasons ?? []));
    }

    // Test-NightlyCompleteGreenPredicate's question asked of that decision: does the state earn the named credit kind (master
    // when none is named)? A missing state earns nothing.
    private static bool Earns(Hashtable? state, string kind)
    {
        var verdict = ReleaseGateCredit.Verdict(state == null ? null! : Plain(state));
        return verdict.Ok && verdict.CreditKind == (string.IsNullOrWhiteSpace(kind) ? "master-scheduled" : kind.Trim().ToLowerInvariant());
    }

    private static Dictionary<string, object> Plain(Hashtable state) =>
        state.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => e.Value!, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Test-C599_CreditVerdicts' rows (also ReleaseGateActivationTests.C599_MasterDueBoundary): one invalid field at a time
    /// against a valid control, per credit kind, on the production credit verdict and complete-green predicate.
    /// </summary>
    internal static NightlyCaseRows CreditVerdictRows()
    {
        var rows = new NightlyCaseRows();
        var shaB = new string('b', 40);
        var shaC = new string('c', 40);
        var master = State();
        var v = Verdict(master);
        rows.Check(v.Kind == "master-scheduled", "C599 CreditVerdicts control master green earns master credit", v.Reasons);
        rows.Check(Earns(master, ""), "C599 CreditVerdicts predicate default asks the master question");
        rows.Check(!Earns(master, "rc-release"), "C599 CreditVerdicts master green never earns release credit");

        var rc = State("rc", "rc", CandidateRef, sha: shaB, expectedSha: shaB, candidate: CandidateId);
        var vr = Verdict(rc);
        rows.Check(vr.Kind == "rc-release", "C599 CreditVerdicts control rc green earns release credit", vr.Reasons);
        rows.Check(!Earns(rc, "master-scheduled"), "C599 CreditVerdicts rc green never earns master credit");

        foreach (var field in new[] { "coverageComplete", "testsPassed", "reportDelivered" })
        {
            var bad = State();
            bad[field] = false;
            rows.Check(Verdict(bad).Kind == "", "C599 CreditVerdicts " + field + " false earns nothing");
            var text = State();
            text[field] = "true";
            var sv = Verdict(text);
            rows.Check(sv.Kind == "", "C599 CreditVerdicts " + field + " as the STRING \"true\" earns nothing", sv.Reasons);
            rows.Check(sv.Reasons.Contains("not-true-boolean:" + field, StringComparison.Ordinal), "C599 CreditVerdicts " + field + " string is named as not-true-boolean", sv.Reasons);
        }

        rows.Check(Verdict(State(exitCode: 1)).Kind == "", "C599 CreditVerdicts non-zero exit earns nothing");
        var noExit = State();
        noExit["exitCode"] = null;
        rows.Check(Verdict(noExit).Kind == "", "C599 CreditVerdicts missing exitCode earns nothing");
        rows.Check(Verdict(State(runId: "")).Kind == "", "C599 CreditVerdicts missing runId earns nothing");
        rows.Check(Verdict(State(policyHash: "")).Kind == "", "C599 CreditVerdicts missing policyHash earns nothing");

        var mismatch = Verdict(State("rc", "rc", CandidateRef, sha: shaB, expectedSha: shaC, candidate: CandidateId));
        rows.Check(mismatch.Kind == "" && mismatch.Reasons.Contains("rc-sha-mismatch", StringComparison.Ordinal), "C599 CreditVerdicts rc sha mismatch earns nothing", mismatch.Reasons);
        rows.Check(Verdict(State("rc", "rc", CandidateRef, sha: "bbbbbbb", expectedSha: "bbbbbbb", candidate: CandidateId)).Kind == "", "C599 CreditVerdicts rc short sha earns nothing");
        rows.Check(Verdict(State("rc", "rc", CandidateRef, sha: shaB, expectedSha: shaB, candidate: "")).Kind == "", "C599 CreditVerdicts rc without a candidate id earns nothing");
        rows.Check(Verdict(State("rc", "rc", CandidateRef, sha: shaB, expectedSha: "", candidate: CandidateId)).Kind == "",
            "C599 CreditVerdicts rc without a pinned expectedSha earns nothing");

        rows.Check(!Earns(master, "invented-kind"), "C599 CreditVerdicts an invented credit kind earns nothing");
        rows.Check(!Earns(null, "master-scheduled"), "C599 CreditVerdicts a null state earns nothing");
        return rows;
    }

    /// <summary>
    /// Test-C599_ReportLane's rows (also ReleaseGateActivationTests.C599_CompletionHandoffs), R-4: an RC incident identity can
    /// never be the master nightly incident. CARD-1340: private roots; the production Windows defaults stay in the producers.
    /// </summary>
    internal static NightlyCaseRows ReportLaneRows()
    {
        var rows = new NightlyCaseRows();
        using var fx = NightlyFixture.Create("c599-report-lane");
        var state = fx.PathOf("state");
        var release = fx.PathOf("releases");
        // The credit store (ReleaseGateCredit.CreditStore, StatePaths' plain twin that NightlyRun writes green through).
        var rcGreen = ReleaseGateCredit.CreditStore("rc-release", state, release, CandidateId).GreenPath;
        var masterGreen = ReleaseGateCredit.CreditStore("master-scheduled", state, "", "").GreenPath;
        rows.Check(rcGreen != masterGreen, "C599 ReportLane the two lanes write different green files", rcGreen + " vs " + masterGreen);
        rows.Check(masterGreen == Path.Combine(state, "last-complete-green.json"), "C599 ReportLane master green path is unchanged", masterGreen);
        rows.Check(rcGreen == Path.Combine(release, "candidates", CandidateId, "complete-green.json"), "C599 ReportLane rc green lands under its candidate root", rcGreen);
        var missing = "";
        try { ReleaseGateCredit.CreditStore("rc-release", "x", "", ""); }
        catch (Exception e) { missing = e.Message; }
        rows.Check(missing.Contains("candidate id", StringComparison.OrdinalIgnoreCase), "C599 ReportLane rc credit without a candidate id throws", missing);
        var rcKind = Verdict(State("rc", "rc", CandidateRef, sha: new string('b', 40), expectedSha: new string('b', 40), candidate: CandidateId)).Kind;
        rows.Check(rcKind != Verdict(State()).Kind, "C599 ReportLane the two lanes never share a credit kind", rcKind);
        return rows;
    }

    [Test]
    public void C599_ReportLane() => ReportLaneRows().ShouldAllPass(5,
        "C599 ReportLane the two lanes write different green files",
        "C599 ReportLane master green path is unchanged",
        "C599 ReportLane rc green lands under its candidate root",
        "C599 ReportLane the two lanes never share a credit kind");
}
