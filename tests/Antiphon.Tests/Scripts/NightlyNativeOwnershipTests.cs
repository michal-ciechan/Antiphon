using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1039 S1n-a and CARD-1045, in-process since W2-nightly S04 (was scripts/test-nightly-native.ps1): the real
/// suspended launcher, job queries, pipes and executable of NativeProcessOwner (Antiphon.Nightly/NativeProcessOwner.cs),
/// started through OwnedLauncher (the twin of Start-NightlyNativeOwner and Invoke-NightlyOwnedProcess) against the staged
/// OwnedChild fixture. The fixture's own NativeJobObserver reads the live private job, not the adapter's constants.
/// The native cases release and join their fixtures even after an assertion failure.
/// NotInParallel: the C1045 cases set parent sentinels in this process's environment, and an inheritable pipe end of a
/// launch must not leak into a child another test starts meanwhile.
/// </summary>
[Category("Integration")]
[NotInParallel]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class NightlyNativeOwnershipTests
{
    [Test]
    public void Idle_capture_identifies_the_live_root_of_a_populated_job()
    {
        const int pid = 42;
        const string reason = "test hosts idle for 0.08 minutes";
        var capture = $"idle-hang-capture\r\n{reason}\r\nroot pid={pid} exited=false active=2\r\n";

        AssertIdleCapture(capture, pid, reason);
        AssertIdleCapture(capture.Replace("active=2", "active=1"), pid, reason);
        Should.Throw<ShouldAssertException>(() => AssertIdleCapture(capture, pid + 1, reason));
        Should.Throw<ShouldAssertException>(() => AssertIdleCapture(capture.Replace("active=2", "active=0"), pid, reason));
        Should.Throw<ShouldAssertException>(() => AssertIdleCapture(capture.Replace("exited=false", "exited=true"), pid, reason));
    }

    private static void AssertIdleCapture(string capture, int pid, string idleReason)
    {
        using var lines = new StringReader(capture);
        lines.ReadLine().ShouldBe("idle-hang-capture");
        lines.ReadLine().ShouldBe(idleReason);
        var root = lines.ReadLine();
        root.ShouldNotBeNull();
        root.ShouldMatch($"^root pid={pid} exited=false active=[1-9][0-9]*$");
    }

    private static void IdleCaptureAndJobTermination(NativeHarness native, CancellationToken cancellationToken)
    {
        var root = native.NewRoot("idle test host");
        var heartbeat = Path.Combine(root, NightlyChunkIdleMonitor.HeartbeatFile);
        var capture = Path.Combine(root, "native.log.hang.txt");
        var trx = Path.Combine(root, "run.trx");
        var hooks = new NativeProcessHooks
        {
            TrxPath = trx,
            DiagnosticDirectory = root,
            HangCapturePath = capture,
            ActiveHeartbeatPath = heartbeat,
            IdleMonitor = new NightlyChunkIdleMonitor(TimeProvider.System, trx, root, 1.0 / 60, null),
        };
        var owner = native.Launcher.Start(new NightlyLaunch(Fixture, [root, "root-held"], root, 30000, null,
            Path.Combine(root, "native.log"), NightlyCustody.Owned, hooks));
        native.Owners.Add(owner);
        native.WaitFor(() => File.Exists(Path.Combine(root, "started")), "test host running");
        var rootHandle = native.Hold(Process.GetProcessById(owner.Observation.Pid));
        var result = owner.Completion.WaitAsync(TimeSpan.FromSeconds(25), cancellationToken).GetAwaiter().GetResult();
        result.IdleHangReason.ShouldNotBeNullOrEmpty();
        (result.TimedOut, result.TerminationSucceeded, result.CleanupComplete, result.CompletedButHung).ShouldBe((true, true, true, false));
        result.ExitCode.ShouldNotBe(0);
        rootHandle.HasExited.ShouldBeTrue();
        File.Exists(trx).ShouldBeFalse();
        result.HangCapturePath.ShouldBe(capture);
        AssertIdleCapture(File.ReadAllText(capture), result.Pid, result.IdleHangReason);
        QualificationHeartbeat.Read(heartbeat).HangSuspected.ShouldBeTrue();
    }

    [Test, Timeout(120_000)]
    public Task C1039_AssignBeforeResume(CancellationToken cancellationToken) => RunAsync(nameof(C1039_AssignBeforeResume), native =>
    {
        string[] tokens = ["path with spaces", "", "a\"b", @"C:\tail\", "/*/*/(A)|(B)/*", "*", "$literal"];
        var r = native.NewRun("suspended root", "descendant", ["assignment"], tokens: tokens);
        native.WaitFor(() => File.Exists(Path.Combine(r.Root, "assignment.entered")), "assignment barrier");
        var rootHandle = native.Hold(Process.GetProcessById(r.Owner.Observation.Pid));
        native.AssertHeld(r, "assignment held");
        native.Assert(!File.Exists(Path.Combine(r.Root, "started")), "assignment held root marker absent");
        native.Assert(!File.Exists(Path.Combine(r.Root, "child.pid")), "assignment held descendant absent");
        native.Release(r, "assignment");
        var child = native.OpenHandle(r, "child");
        var argv = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(r.Root, "argv.json")))!;
        native.Assert(argv.Length == tokens.Length, "literal argv count");
        for (var i = 0; i < tokens.Length; i++) native.Assert(string.Equals(argv[i], tokens[i], StringComparison.Ordinal), "literal argv token " + i);
        native.Release(r, "child");
        native.Complete(r);
        native.Assert(rootHandle.WaitForExit(1000) && child.WaitForExit(1000), "independent root child handles signal");

        r = native.NewRun("assignment refusal", "descendant", ["assignment"], failAssignment: true);
        native.WaitFor(() => File.Exists(Path.Combine(r.Root, "assignment.entered")), "failed assignment barrier");
        var refused = native.Hold(Process.GetProcessById(r.Owner.Observation.Pid));
        native.Release(r, "assignment");
        native.WaitFor(() => r.Owner.Completion.IsCompleted || File.Exists(Path.Combine(r.Root, "started")), "assignment refusal verdict");
        native.Assert(!File.Exists(Path.Combine(r.Root, "started")), "assignment refusal fixture marker absent");
        var result = r.Owner.Completion.GetAwaiter().GetResult();
        native.Assert(result.ExitCode != 0 && Regex.IsMatch(result.Error, "AssignProcessToJobObject", RegexOptions.IgnoreCase), "assignment failure refuses");
        native.Assert(refused.WaitForExit(1000), "assignment refusal exact root handle signalled");
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1039_DescendantExit(CancellationToken cancellationToken) => RunAsync(nameof(C1039_DescendantExit), native =>
    {
        var witnessRoot = native.NewRoot("owner exit witness");
        native.Owners.Add(OwnerExitWitness(Fixture, witnessRoot, 30000, Path.Combine(witnessRoot, "native.log")));

        var r = native.NewRun("child held", "descendant");
        native.WaitFor(() => r.Owner.Observation.Pid > 0, "root identity");
        var rootHandle = native.Hold(Process.GetProcessById(r.Owner.Observation.Pid));
        var child = native.OpenHandle(r, "child");
        native.Assert(rootHandle.WaitForExit(10000), "root exits before child");
        native.AssertHeld(r, "descendant held");
        native.Assert(!child.HasExited && !r.Owner.Observation.ChildrenExited, "live child prevents ChildrenExited");
        native.Release(r, "child");
        var result = native.Complete(r);
        native.Assert(child.WaitForExit(1000) && result.RemainingProcessCount == 0, "independent child handle and zero job count");
        native.Assert(result.DescendantIdentities.Length > 0, "descendant start identities recorded");
        var childImages = result.DescendantIdentities.Select((identity, index) => (identity, index))
            .Where(item => item.identity.StartsWith(child.Id + ":", StringComparison.Ordinal)).Select(item => result.DescendantImages[item.index]).ToArray();
        native.Assert(childImages.Length == 1 && childImages[0] == Path.GetFileNameWithoutExtension(Fixture), "descendant image survives process exit");

        r = native.NewRun("root wait held", "root-held", ["root-wait"], zeroCount: true);
        native.WaitFor(() => File.Exists(Path.Combine(r.Root, "root-wait.entered")), "root wait barrier");
        rootHandle = native.Hold(Process.GetProcessById(r.Owner.Observation.Pid));
        native.AssertHeld(r, "root wait held");
        native.Assert(r.Owner.Observation.RemainingProcessCount == 0 && !rootHandle.HasExited && !r.Owner.Observation.ChildrenExited, "zero count does not replace root wait");
        native.Release(r, "root");
        native.Release(r, "root-wait");
        native.Complete(r);

        r = native.NewRun("breakaway request", "breakaway", ["assignment"]);
        native.WaitFor(() => File.Exists(Path.Combine(r.Root, "assignment.entered")), "private job limit readback");
        var limits = JobObserver.ReadLimitFlags(r.Owner.JobHandle);
        native.Assert((limits & 0x1800) == 0, "independent private job readback forbids both breakaway flags");
        native.Assert((limits & 0x2000) != 0, "independent private job readback enables kill on close");
        native.Release(r, "assignment");
        native.Assert(ReadMarker(Path.Combine(r.Root, "breakaway-result")).Trim() == "refused", "job forbids breakaway request");
        native.Assert(!File.Exists(Path.Combine(r.Root, "breakaway-child.pid")), "no independently live breakaway descendant");
        native.Complete(r);

        IdleCaptureAndJobTermination(native, cancellationToken);
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1039_DrainBeforeReturn(CancellationToken cancellationToken) => RunAsync(nameof(C1039_DrainBeforeReturn), native =>
    {
        foreach (var phase in new[] { "stdout", "stderr", "log-write" })
        {
            var r = native.NewRun(phase + " held", "plain", [phase]);
            native.WaitFor(() => File.Exists(Path.Combine(r.Root, phase + ".entered")), phase + " barrier");
            native.AssertHeld(r, phase);
            native.Assert(!r.Owner.Observation.OutputDrained, phase + " OutputDrained false");
            native.Assert(!r.Owner.Observation.CleanupComplete, phase + " cleanup not complete");
            native.Release(r, phase);
            native.Complete(r);
            var text = File.ReadAllText(r.Log);
            native.Assert(text.Contains("ROOT-STDOUT-END", StringComparison.Ordinal) && text.Contains("ROOT-STDERR-END", StringComparison.Ordinal), phase + " final log has both terminal sentinels");
        }
        // Ordinary production entry must consume this adapter's real custody result.
        var root = native.NewRoot("production entry", track: false);
        var result = native.Launcher.Run(new NightlyLaunch(Fixture, [root, "plain"], root, 30000, null, Path.Combine(root, "entry.log"), NightlyCustody.Owned));
        native.Assert(result.CleanupComplete == true && native.Launcher.WaitCleanup(result), "production entry observes complete native result");
        native.Assert(!native.Launcher.WaitCleanup(new NightlyLaunchResult { ChildrenExited = true, CleanupComplete = false }), "root only result cannot discharge cleanup");
        var seam = new RecordingLauncher();
        native.Assert(seam.WaitCleanup(new NightlyLaunchResult { ChildrenExited = true }), "legacy controlled IO decision seam remains compatible");
        native.Assert(!seam.WaitCleanup(new NightlyLaunchResult { ChildrenExited = true, CleanupComplete = false }), "explicit incomplete cleanup remains false in controlled IO");
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1039_PowerShellDescendantCustody(CancellationToken cancellationToken) => RunAsync(nameof(C1039_PowerShellDescendantCustody), native =>
    {
        var root = native.NewRoot("powershell descendant");
        var childScript = Path.Combine(root, "launch.ps1");
        File.WriteAllText(childScript, """
            param([string]$Root, [string]$Fixture)
            $start = [Diagnostics.ProcessStartInfo]::new($Fixture)
            $start.UseShellExecute = $false
            $start.ArgumentList.Add($Root)
            $start.ArgumentList.Add('child')
            $child = [Diagnostics.Process]::Start($start)
            while (!(Test-Path (Join-Path $Root 'child.pid'))) { Start-Sleep -Milliseconds 10 }
            """);
        var powerShell = OwnedLauncher.FindApplication("pwsh") ?? throw new InvalidOperationException("pwsh is not on PATH");
        var aliases = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        var packaged = Directory.Exists(aliases)
            ? Directory.EnumerateDirectories(aliases, "Microsoft.PowerShell_*").Select(dir => Path.Combine(dir, "pwsh.exe")).FirstOrDefault(File.Exists)
            : null;
        if (packaged is not null) powerShell = packaged;
        string[] arguments = ["-NoProfile", "-File", childScript, "-Root", root, "-Fixture", Fixture];
        var log = Path.Combine(root, "native.log");
        var owner = native.Launcher.Start(new NightlyLaunch(powerShell, arguments, root, 30000, null, log, NightlyCustody.Owned));
        native.Owners.Add(owner);
        var r = new NativeRun(root, owner, log);
        var child = native.OpenHandle(r, "child");
        native.Assert(JobObserver.Contains(child.Handle, owner.JobHandle), "PowerShell descendant belongs to private native job");
        native.AssertHeld(r, "PowerShell descendant held");
        native.Release(r, "child");
        var result = native.Complete(r);
        native.Assert(child.WaitForExit(1000), "PowerShell descendant independently exited");
        native.Assert(result.DescendantIdentities.Count(identity => identity.StartsWith(child.Id + ":", StringComparison.Ordinal)) == 1, "PowerShell descendant generation recorded");
        if (packaged is not null)
        {
            // Bypass the resolver: the native boundary itself must refuse packaged roots before resume.
            var guardRoot = native.NewRoot("packaged admission");
            var raw = NativeProcessOwner.Start(packaged, ["-NoProfile", "-File", childScript, "-Root", guardRoot, "-Fixture", Fixture],
                guardRoot, 30000, Path.Combine(guardRoot, "native.log"), null, null);
            native.Owners.Add(raw);
            native.WaitFor(() => raw.Completion.IsCompleted, "packaged admission refusal");
            var refused = raw.Completion.GetAwaiter().GetResult();
            native.Assert(refused.ExitCode != 0 && refused.Error.Contains("packaged", StringComparison.Ordinal), "raw packaged root refused");
            native.Assert(refused.ChildrenExited && !File.Exists(Path.Combine(guardRoot, "child.pid")), "packaged root never resumes or starts a child");
        }
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1039_RetainedWriterDrainDeadline(CancellationToken cancellationToken) => RunAsync(nameof(C1039_RetainedWriterDrainDeadline), native =>
    {
        var root = native.NewRoot("retained writer");
        var owner = native.Launcher.Start(new NightlyLaunch(Fixture, [root, "retained-writer", Environment.ProcessId.ToString()], root, 30000, null,
            Path.Combine(root, "native.log"), NightlyCustody.Owned));
        native.Owners.Add(owner);
        native.RetainedWriters.Add(long.Parse(ReadMarker(Path.Combine(root, "writer.handle"))));
        native.WaitFor(() => owner.Observation.ChildrenExited, "retained writer root and job exited");
        native.Assert(owner.Completion.Wait(20000), "retained pipe writer cannot outlive independent drain deadline");
        var result = owner.Completion.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(result));
        native.Assert(result.ExitCode != 0 && result.TimedOut && result.OutputDrainTimedOut, "drain deadline reports failure and timeout");
        native.Assert(result.ChildrenExited && !result.OutputDrained && !result.CleanupComplete, "retained writer never earns clean output or cleanup");
        native.Assert(result.Error.Contains("output drain", StringComparison.Ordinal) && result.DrainMilliseconds >= 15000 && result.DrainMilliseconds < 20000,
            "drain has its own measured deadline");
        native.Assert(File.Exists(Path.Combine(root, "native.log.stdout.tmp")) && !File.Exists(Path.Combine(root, "native.log")), "failed drain retains partial evidence without a final log");
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1045_NullEnvironmentInherits(CancellationToken cancellationToken) => RunAsync(nameof(C1045_NullEnvironmentInherits), native =>
    {
        native.SetParentSentinels();
        foreach (var omit in new[] { true, false })
        {
            // Invoke-NightlyOwnedProcess without -Environment and with -Environment $null: both are a null map.
            var observed = native.ReadChildEnvironment("null-" + omit, ["C1045_PARENT_ONLY"], null);
            var entries = Find(observed, "C1045_PARENT_ONLY");
            native.AssertEnvironment(entries.Length == 1 && entries[0].Value == "parent-only-value", "null inherits parent sentinel");
            native.AssertParentEnvironment();
        }
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1045_SuppliedEnvironmentReplaces(CancellationToken cancellationToken) => RunAsync(nameof(C1045_SuppliedEnvironmentReplaces), native =>
    {
        native.SetParentSentinels();
        var value = "supplied spaces=unicode \u03A9";
        var observed = native.ReadChildEnvironment("replacement", ["C1045_PARENT_ONLY", "C1045_OVERRIDE", "C1045_NEW"],
            Map(("c1045_override", "child-override-value"), ("C1045_NEW", value)));
        native.AssertEnvironment(Find(observed, "C1045_PARENT_ONLY").Length == 0, "supplied map excludes parent sentinel");
        var entries = Find(observed, "C1045_OVERRIDE");
        native.AssertEnvironment(entries.Length == 1 && entries[0].Value == "child-override-value", "case-insensitive single override exact value");
        entries = Find(observed, "C1045_NEW");
        native.AssertEnvironment(entries.Length == 1 && entries[0].Value == value, "new value preserves spaces equals and Unicode");
        native.AssertParentEnvironment();
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1045_EmptyEnvironmentDoesNotInherit(CancellationToken cancellationToken) => RunAsync(nameof(C1045_EmptyEnvironmentDoesNotInherit), native =>
    {
        native.SetParentSentinels();
        var observed = native.ReadChildEnvironment("empty", ["C1045_PARENT_ONLY", "C1045_OVERRIDE"], Map());
        native.AssertEnvironment(Find(observed, "C1045_PARENT_ONLY").Length == 0, "empty map excludes parent sentinel");
        native.AssertEnvironment(Find(observed, "C1045_OVERRIDE").Length == 0, "empty map excludes parent override");
        native.AssertParentEnvironment();
    }, cancellationToken);

    [Test, Timeout(120_000)]
    public Task C1045_ClearedEntriesAreAbsent(CancellationToken cancellationToken) => RunAsync(nameof(C1045_ClearedEntriesAreAbsent), native =>
    {
        native.SetParentSentinels();
        var observed = native.ReadChildEnvironment("cleared", ["C1045_EMPTY", "C1045_NULL", "C1045_KEEP"],
            Map(("C1045_EMPTY", ""), ("C1045_NULL", null), ("C1045_KEEP", "retained-value")));
        native.AssertEnvironment(Find(observed, "C1045_EMPTY").Length == 0 && Find(observed, "C1045_NULL").Length == 0, "cleared names absent from child block");
        var entries = Find(observed, "C1045_KEEP");
        native.AssertEnvironment(entries.Length == 1 && entries[0].Value == "retained-value", "retained value exact");
        observed = native.ReadChildEnvironment("all-cleared", ["C1045_PARENT_ONLY", "C1045_EMPTY", "C1045_NULL"], Map(("C1045_EMPTY", ""), ("C1045_NULL", null)));
        native.AssertEnvironment(observed.Length == 0, "all-cleared map excludes parent and cleared names");
        native.AssertParentEnvironment();

        // The real policy map from this process's environment, as the run builds it. The parent opt-in and every name
        // the policy clears are given parent values in the base map (this process's own environment is not changed).
        var headed = Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS");
        var policy = ExecutionPolicy.Parse(File.ReadAllText(NightlyPolicy.PolicyPath(DelegateScriptRunner.RepoRoot)));
        var parent = new System.Collections.Hashtable(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) parent[(string)entry.Key] = entry.Value;
        parent["ANTIPHON_HEADED_TESTS"] = "1";
        foreach (var name in policy.SafeEnvironment.Clear) parent[name] = "c1045-parent-value";
        var map = NightlyPolicy.SafeChildEnvironment(policy, "antiphon", parent);
        var cleared = policy.SafeEnvironment.Clear.Where(name => (map[name] as string ?? "") == "").ToArray();
        native.Assert(cleared.Length > 0, "real policy clear list admitted");
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in map) environment[(string)entry.Key] = entry.Value as string;
        observed = native.ReadChildEnvironment("policy", ["C1045_PARENT_ONLY", .. cleared], environment);
        foreach (var name in cleared)
            native.AssertEnvironment(Find(observed, name).Length == 0, "policy cleared name absent " + name);
        entries = Find(observed, "C1045_PARENT_ONLY");
        native.AssertEnvironment(entries.Length == 1 && entries[0].Value == "parent-only-value", "full policy map retains unrelated parent sentinel");
        native.AssertEnvironment(Environment.GetEnvironmentVariable("ANTIPHON_HEADED_TESTS") == headed, "parent headed opt-in unchanged");
        native.AssertParentEnvironment();
    }, cancellationToken);

    /// <summary>
    /// W2-nightly S04: OwnedLauncher's own contract around the native owner. It refuses a Watched hop before anything
    /// runs; it runs beforeStart, then resolves the application (Get-Command, PATHEXT); prepareChild sees the start
    /// information before the environment block is taken; Run returns the owner's observation and its cleanup verdict.
    /// </summary>
    [Test, Timeout(120_000)]
    public Task Owned_launcher_prepares_the_child_and_refuses_a_watched_hop(CancellationToken cancellationToken) =>
        RunAsync(nameof(Owned_launcher_prepares_the_child_and_refuses_a_watched_hop), native =>
        {
            var starts = 0;
            var prepared = new List<string>();
            var launcher = new OwnedLauncher(() => starts++, start =>
            {
                prepared.Add(start.FileName);
                start.Environment["C1045_PREPARED"] = "prepared-value";
            });
            Should.Throw<NotSupportedException>(() => launcher.Run(new NightlyLaunch(Fixture, [], "", 30000)));
            starts.ShouldBe(0, "a refused custody starts nothing");
            Should.Throw<FileNotFoundException>(() => launcher.Start(new NightlyLaunch("c1039-no-such-application", [], "", 30000, Custody: NightlyCustody.Owned)));
            starts.ShouldBe(1, "beforeStart runs before the application is resolved");

            var root = native.NewRoot("prepared child", track: false);
            var result = launcher.Run(new NightlyLaunch(Fixture, [root, "environment", "C1045_PREPARED", "C1039_FIXTURE_MODE", "PATH"], root, 30000,
                new Dictionary<string, string> { ["C1039_FIXTURE_MODE"] = "kept" }, Path.Combine(root, "entry.log"), NightlyCustody.Owned));
            result.ExitCode.ShouldBe(0, result.Owner?.Error);
            result.Owner.ShouldNotBeNull();
            result.Pid.ShouldBe(result.Owner.Pid);
            result.CleanupComplete.ShouldBe(true);
            launcher.WaitCleanup(result).ShouldBeTrue();
            launcher.NativeActiveCount.ShouldBe(0);
            starts.ShouldBe(2);
            prepared.ShouldBe(new[] { Path.GetFullPath(Fixture) });
            var observed = JsonSerializer.Deserialize<EnvironmentEntry[]>(File.ReadAllText(Path.Combine(root, "environment.json")))!;
            Find(observed, "C1045_PREPARED").Select(entry => entry.Value).ShouldBe(new[] { "prepared-value" });
            Find(observed, "C1039_FIXTURE_MODE").Length.ShouldBe(1);
            Find(observed, "PATH").ShouldBeEmpty("a supplied map replaces the parent environment");

            OwnedLauncher.FindApplication(Path.ChangeExtension(Fixture, null)).ShouldBe(Path.GetFullPath(Fixture), StringCompareShould.IgnoreCase); // PATHEXT completes a path without its extension
            OwnedLauncher.FindApplication("c1039-no-such-application").ShouldBeNull();
            var pwsh = OwnedLauncher.ResolveExecutable("pwsh");
            Path.GetFileName(pwsh).ShouldBe("pwsh.exe", StringCompareShould.IgnoreCase);
            pwsh.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase).ShouldBeFalse("a packaged PowerShell is replaced by Program Files");
            var inherited = OwnedLauncher.ChildStartInfo(Fixture, ["a"], "", null);
            inherited.Environment.ContainsKey("PATH").ShouldBeTrue();
            inherited.ArgumentList.ShouldBe(new[] { "a" });
            var replaced = OwnedLauncher.ChildStartInfo(Fixture, [], root, new Dictionary<string, string> { ["C1045_NULL"] = null! });
            replaced.Environment.Count.ShouldBe(1);
            replaced.Environment["C1045_NULL"].ShouldBe("");
            replaced.WorkingDirectory.ShouldBe(root);
        }, cancellationToken);
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "nightly-owned-child", "OwnedChild.exe");

    /// <summary>
    /// CARD-1514 S1 (was Invoke-RcWindowsOwnerExitWitness in scripts/test-rc-speed-os-witness.ps1). Called on every
    /// C1039_DescendantExit run: the root's descendant outlives it, and the owner still reports the descendant's image after
    /// its exit, the root's exit time, the missed-pid list and an empty job.
    /// </summary>
    private static NativeProcessOwner OwnerExitWitness(string filePath, string workingDirectory, int timeoutMilliseconds, string logPath)
    {
        var owner = NativeProcessOwner.Start(filePath, [workingDirectory, "descendant"], workingDirectory, timeoutMilliseconds, logPath, null, null);
        var childPid = Path.Combine(workingDirectory, "child.pid");
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(childPid))
        {
            if (deadline.ElapsedMilliseconds >= 10000) throw new InvalidOperationException("FAIL C1039 owner witness child pid");
            Thread.Sleep(10);
        }
        Thread.Sleep(300);
        File.WriteAllText(Path.Combine(workingDirectory, "child.release"), "release");
        if (!owner.Completion.Wait(timeoutMilliseconds)) throw new InvalidOperationException("FAIL C1039 owner witness completion");
        var result = owner.Completion.GetAwaiter().GetResult();
        var image = Path.GetFileNameWithoutExtension(filePath);
        if (!result.DescendantImages.Any(one => string.Equals(one, image, StringComparison.Ordinal))) throw new InvalidOperationException("FAIL C1039 descendant image survives process exit");
        if (string.IsNullOrWhiteSpace(result.RootExitedAtUtc)) throw new InvalidOperationException("FAIL C1039 root exit time missing");
        if (result.MissedDescendantPids is null) throw new InvalidOperationException("FAIL C1039 missed descendant pids missing");
        if (!result.JobEmptyObserved) throw new InvalidOperationException("FAIL C1039 job empty not observed");
        return owner;
    }

    private sealed record NativeRun(string Root, NativeProcessOwner Owner, string Log);

    private static string ReadMarker(string path) => LiveFixtureText.ReadPublishedAsync(path).GetAwaiter().GetResult();

    private sealed record EnvironmentEntry(string Name, string? Value);

    private static EnvironmentEntry[] Find(EnvironmentEntry[] observed, string name) =>
        observed.Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static Dictionary<string, string?> Map(params (string Name, string? Value)[] pairs)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in pairs) map[name] = value;
        return map;
    }

    private static Task RunAsync(string name, Action<NativeHarness> body, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new TUnit.Core.Exceptions.SkipTestException("This native checkpoint requires Windows (OwnedChild.exe, pwsh.exe, and a Windows job).");
        File.Exists(Fixture).ShouldBeTrue("The checkpoint build must stage the executable, never compile inside the test.");
        return Task.Run(() =>
        {
            var native = new NativeHarness(Path.Combine(Path.GetTempPath(), "c1039-native-" + Guid.NewGuid().ToString("N")));
            Exception? failure = null;
            try { body(native); }
            catch (Exception e) { failure = e; }
            var cleanup = native.Finish();
            if (failure is not null) throw new ShouldAssertException(failure.Message + "\nRetained evidence: " + native.Results, failure);
            cleanup.ShouldBeEmpty("Retained evidence: " + native.Results);
            Directory.Delete(native.Results, recursive: true);
        }, cancellationToken);
    }

    /// <summary>The driver's state: the owners, held handles, barrier roots, retained writers and parent sentinels its finally joins.</summary>
    private sealed class NativeHarness(string results)
    {
        private readonly Dictionary<string, string?> _parentEnvironment = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Process> _handles = [];
        private readonly List<string> _roots = [];

        public string Results { get; } = Directory.CreateDirectory(results).FullName;

        public OwnedLauncher Launcher { get; } = new();

        public List<NativeProcessOwner> Owners { get; } = [];

        public List<long> RetainedWriters { get; } = [];

        public int Passed { get; private set; }

        public void Assert(bool condition, string label)
        {
            if (!condition) throw new ShouldAssertException("FAIL C1039 " + label);
            Passed++;
        }

        public void AssertEnvironment(bool condition, string label)
        {
            if (!condition) throw new ShouldAssertException("FAIL C1045 " + label);
            Passed++;
        }

        public void WaitFor(Func<bool> condition, string label)
        {
            var clock = Stopwatch.StartNew();
            while (!condition())
            {
                if (clock.ElapsedMilliseconds >= 10000) throw new InvalidOperationException("fixture prerequisite not observed: " + label);
                Thread.Sleep(10);
            }
        }

        public string NewRoot(string name, bool track = true)
        {
            var root = Path.Combine(Results, name);
            Directory.CreateDirectory(root);
            if (track) _roots.Add(root);
            return root;
        }

        public NativeRun NewRun(string name, string mode, string[]? barriers = null, bool failAssignment = false, bool zeroCount = false, string[]? tokens = null)
        {
            var root = NewRoot(name);
            var hooks = new NativeProcessHooks
            {
                BarrierDirectory = root, Barriers = barriers ?? [], FailAssignment = failAssignment, ZeroCountAtRootWait = zeroCount,
            };
            var log = Path.Combine(root, "native.log");
            // Control identity travels independently of argv, so a quoting defect reaches the literal-token assertion
            // rather than breaking fixture startup.
            var environment = new Dictionary<string, string> { ["C1039_FIXTURE_DIRECTORY"] = root, ["C1039_FIXTURE_MODE"] = mode };
            var owner = Launcher.Start(new NightlyLaunch(Fixture, tokens ?? [], root, 30000, environment, log, NightlyCustody.Owned, hooks));
            Owners.Add(owner);
            return new NativeRun(root, owner, log);
        }

        public void Release(NativeRun run, string phase) => File.WriteAllText(Path.Combine(run.Root, phase + ".release"), "release");

        public Process Hold(Process process)
        {
            _ = process.Handle;
            _handles.Add(process);
            return process;
        }

        public Process OpenHandle(NativeRun run, string name)
        {
            var file = Path.Combine(run.Root, name + ".pid");
            var process = Process.GetProcessById(int.Parse(ReadMarker(file)));
            _ = process.Handle;
            _ = process.StartTime;
            _handles.Add(process);
            return process;
        }

        public NativeProcessResult Complete(NativeRun run)
        {
            WaitFor(() => run.Owner.Completion.IsCompleted, "owner completion");
            var result = run.Owner.Completion.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(run.Root, "result.json"), JsonSerializer.Serialize(result));
            Assert(result.ExitCode == 0 && result.Error == "", "native exit zero");
            Assert(result.ChildrenExited && result.OutputDrained && result.CleanupComplete, "root job and streams complete");
            return result;
        }

        public void AssertHeld(NativeRun run, string label)
        {
            // A short observation interval lets an incorrectly resumed or completed worker run.
            Thread.Sleep(300);
            Assert(!run.Owner.Completion.IsCompleted, label + " owner has not returned");
        }

        public void SetParentSentinels()
        {
            SetSentinel("C1045_PARENT_ONLY", "parent-only-value");
            SetSentinel("C1045_OVERRIDE", "parent-override-value");
            SetSentinel("C1045_EMPTY", "parent-empty-value");
            SetSentinel("C1045_NULL", "parent-null-value");
        }

        private void SetSentinel(string name, string value)
        {
            if (!_parentEnvironment.ContainsKey(name)) _parentEnvironment[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void AssertParentEnvironment()
        {
            AssertEnvironment(Environment.GetEnvironmentVariable("C1045_PARENT_ONLY") == "parent-only-value", "parent-only sentinel unchanged");
            AssertEnvironment(Environment.GetEnvironmentVariable("C1045_OVERRIDE") == "parent-override-value", "parent override unchanged");
            AssertEnvironment(Environment.GetEnvironmentVariable("C1045_EMPTY") == "parent-empty-value", "parent empty sentinel unchanged");
            AssertEnvironment(Environment.GetEnvironmentVariable("C1045_NULL") == "parent-null-value", "parent null sentinel unchanged");
        }

        /// <summary>Invoke-NightlyOwnedProcess, the production entry with no controlled-I/O or native-operation seams, over the environment mode.</summary>
        public EnvironmentEntry[] ReadChildEnvironment(string name, string[] names, IReadOnlyDictionary<string, string?>? environment)
        {
            var root = NewRoot(name, track: false);
            var log = Path.Combine(root, "entry.log");
            var map = environment?.ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);
            var result = Launcher.Run(new NightlyLaunch(Fixture, [root, "environment", .. names], root, 30000, map, log, NightlyCustody.Owned));
            File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(result.Owner));
            Assert(result.ExitCode == 0 && result.Owner!.Error == "", "environment fixture exit zero");
            Assert(result.ChildrenExited && result.Owner.OutputDrained && result.Owner.CleanupComplete && Launcher.WaitCleanup(result), "environment fixture cleanup complete");
            var text = File.ReadAllText(log);
            Assert(text.Contains("ROOT-STDOUT-END", StringComparison.Ordinal) && text.Contains("ROOT-STDERR-END", StringComparison.Ordinal), "environment fixture both final output sentinels");
            var observation = Path.Combine(root, "environment.json");
            Assert(File.Exists(observation), "environment fixture observation exists");
            return JsonSerializer.Deserialize<EnvironmentEntry[]>(File.ReadAllText(observation))!;
        }

        /// <summary>The driver's finally: close retained writers, restore the parent, release every barrier, join every owner and handle. Returns what had to be killed.</summary>
        public List<string> Finish()
        {
            var problems = new List<string>();
            foreach (var writer in RetainedWriters) JobObserver.CloseWriter(writer);
            foreach (var (name, value) in _parentEnvironment) Environment.SetEnvironmentVariable(name, value);
            // Release every fixture barrier even when the guarded assertion goes red.
            foreach (var root in _roots)
            {
                foreach (var phase in new[] { "assignment", "root-wait", "stdout", "stderr", "log-write", "root", "child" })
                    File.WriteAllText(Path.Combine(root, phase + ".release"), "finally release");
                // A deliberate breakaway mutant must also be joined by its exact fixture identity.
                var childFile = Path.Combine(root, "breakaway-child.pid");
                if (File.Exists(childFile))
                {
                    try { Hold(Process.GetProcessById(int.Parse(ReadMarker(childFile)))); }
                    catch (ArgumentException) { }
                }
            }
            foreach (var owner in Owners) owner.Completion.GetAwaiter().GetResult();
            foreach (var process in _handles)
            {
                if (!process.WaitForExit(10000))
                {
                    process.Kill();
                    process.WaitForExit();
                    problems.Add("killed " + process.Id);
                }
                process.Dispose();
            }
            return problems;
        }
    }

    /// <summary>The fixture's separately compiled job observer (NativeJobObserver in OwnedChild.dll), read through reflection.</summary>
    private static class JobObserver
    {
        private static readonly Lazy<Type> Observer = new(() =>
            System.Reflection.Assembly.LoadFrom(Path.ChangeExtension(Fixture, ".dll")).GetType("NativeJobObserver", throwOnError: true)!);

        public static bool Contains(IntPtr process, IntPtr job) => (bool)Invoke("Contains", process, job)!;

        public static uint ReadLimitFlags(IntPtr job) => (uint)Invoke("ReadLimitFlags", job)!;

        public static void CloseWriter(long handle) => Invoke("CloseWriter", handle);

        private static object? Invoke(string method, params object[] arguments)
        {
            try { return Observer.Value.GetMethod(method, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, arguments); }
            catch (TargetInvocationException e) when (e.InnerException is not null) { throw e.InnerException; }
        }
    }
}
