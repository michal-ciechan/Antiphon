using Antiphon.PtyHost.Client;
using Antiphon.PtyHost.Protocol;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;
using static Antiphon.Tests.TestHelpers.TestOwnedPtyHost;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class TestOwnedPtyHostIdentityTests
{
    [Test]
    [Arguments("owned")]
    [Arguments("foreign-root")]
    [Arguments("prefix-sibling")]
    [Arguments("dotdot-escape")]
    [Arguments("symlink-outside")]
    [Arguments("platform-casing")]
    [Arguments("foreign-session")]
    [Arguments("stale-generation")]
    [Arguments("zero-pid")]
    [Arguments("unknown-start")]
    [Arguments("foreign-image")]
    [Arguments("unrelated-descendant")]
    public void Identity_requires_the_owned_root_session_and_process_generation(string scenario)
    {
        using var fixture = new OwnedPolicyFixture();
        var candidate = fixture.Record(10);
        var expected = scenario == "owned" || scenario == "platform-casing" && OperatingSystem.IsWindows();
        candidate = scenario switch
        {
            "foreign-root" => candidate with { Root = fixture.Root + "-foreign" },
            "prefix-sibling" => candidate with { ManifestPath = Path.Combine(fixture.Root + "-sibling", "manifests", fixture.SessionId.ToString("N") + ".json") },
            "dotdot-escape" => candidate with { ManifestPath = Path.Combine(fixture.Root, "..", "outside", "manifests", fixture.SessionId.ToString("N") + ".json") },
            "platform-casing" => candidate with { Root = fixture.Root.ToUpperInvariant() },
            "foreign-session" => candidate with { SessionId = Guid.NewGuid() },
            "stale-generation" => candidate with { Process = candidate.Process with { Generation = 9000 } },
            "zero-pid" => candidate with { Process = candidate.Process with { Pid = 0 } },
            "unknown-start" => candidate with { Process = candidate.Process with { Generation = 0 } },
            "foreign-image" => candidate with { Process = candidate.Process with { Image = Path.Combine(fixture.Root, "bin", "foreign.exe") } },
            "unrelated-descendant" => candidate with { Host = false, Attributed = false },
            _ => candidate,
        };
        if (scenario == "symlink-outside")
        {
            var outside = Path.Combine(fixture.Root, "outside");
            Directory.CreateDirectory(outside);
            var link = Path.Combine(fixture.Root, "bin", "linked");
            Directory.CreateSymbolicLink(link, outside);
            candidate = candidate with { Process = candidate.Process with { Image = Path.Combine(link, PtyHostLauncher.HostExeName) } };
        }
        fixture.Manager.Retain(candidate, fixture.SessionId).ShouldBe(expected, "identity-rejected");
        if (expected) fixture.Manager.Force(candidate);
        fixture.Killed.Count.ShouldBe(expected ? 1 : 0, "control-operations-zero");
        if (scenario == "stale-generation")
        {
            var valid = fixture.Record(11);
            fixture.Manager.Retain(valid, fixture.SessionId).ShouldBeTrue();
            fixture.Generations[11]++;
            try { fixture.Manager.Force(valid); } catch (IOException) { }
            fixture.Killed.ShouldBeEmpty("control-operations-zero");
        }
    }
}

[Category("Unit")]
public class TestOwnedPtyHostPolicyTests
{
    [Test]
    [Arguments("replaced")]
    [Arguments("partial-start")]
    [Arguments("manifest-missing")]
    public async Task Capture_retains_every_owned_generation(string scenario)
    {
        using var fixture = new OwnedPolicyFixture();
        fixture.Manifest = fixture.NewManifest(10);
        fixture.Manager.Capture(fixture.SessionId, SessionBackends.PtyHost, false);
        if (scenario == "replaced")
        {
            fixture.Manifest = fixture.NewManifest(11);
            fixture.Manager.Capture(fixture.SessionId, SessionBackends.PtyHost, false);
        }
        if (scenario == "manifest-missing") fixture.Manifest = null;
        if (scenario == "partial-start")
        {
            fixture.Manifest = fixture.NewManifest(11) with { LaunchPending = true, ChildPid = null };
        }
        var capturedAtStop = 0;
        await fixture.Manager.DisposeAsync(true,
            () => fixture.Manager.Capture(fixture.SessionId, SessionBackends.PtyHost, false),
            _ => { capturedAtStop = fixture.Manager.Captured.Count; fixture.Events.Add("stop"); return Task.CompletedTask; },
            () => Task.CompletedTask);
        var expected = scenario == "manifest-missing" ? 1 : 2;
        capturedAtStop.ShouldBe(expected, "all-owned-generations-retained");
        fixture.Killed.Order().ShouldBe(Enumerable.Range(10, expected), "all-owned-generations-retained");
        fixture.Events.IndexOf("captured").ShouldBeLessThan(fixture.Events.IndexOf("stop"), "capture-before-stop");
        fixture.Reads.ShouldAllBe(x => x == PtyHostManifest.PathFor(Path.Combine(fixture.Root, "manifests"), fixture.SessionId));
    }

    [Test]
    [Arguments("normal-grace")]
    [Arguments("post-kill-wait")]
    [Arguments("stop-cancel")]
    [Arguments("shared-deadline")]
    public async Task Cleanup_waits_and_uses_one_deadline_per_phase(string scenario)
    {
        using var fixture = new OwnedPolicyFixture();
        fixture.Manager.Retain(fixture.Record(10), fixture.SessionId).ShouldBeTrue();
        if (scenario == "normal-grace")
        {
            await fixture.Manager.CleanupAsync();
            fixture.KillTimes.Single().ShouldBe(TimeSpan.FromSeconds(2), "grace-before-force");
        }
        else if (scenario == "post-kill-wait")
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Io.Kill = p => fixture.Killed.Add(p.Pid);
            fixture.Io.Wait = async (p, budget, ct) =>
            {
                if (fixture.Killed.Count == 0) { fixture.Clock.Advance(budget); return; }
                entered.TrySetResult();
                await release.Task;
                fixture.Dead.Add(p.Pid);
            };
            var cleanup = fixture.Manager.CleanupAsync();
            try
            {
                await Task.WhenAny(entered.Task, cleanup).WaitAsync(TimeSpan.FromSeconds(5));
                cleanup.IsCompleted.ShouldBeFalse("return-awaits-exit");
            }
            finally { release.TrySetResult(); try { await cleanup; } catch { /* Preserve the decisive assertion above. */ } }
            await cleanup;
        }
        else if (scenario == "stop-cancel")
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationToken observed = default;
            var cleanup = fixture.Manager.DisposeAsync(true, () => { }, async ct =>
            {
                observed = ct;
                entered.TrySetResult();
                await release.Task;
            }, () => Task.CompletedTask);
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                fixture.Clock.Advance(TimeSpan.FromSeconds(2));
                observed.IsCancellationRequested.ShouldBeTrue("stop-token-cancelled");
            }
            finally { release.TrySetResult(); await cleanup; }
        }
        else
        {
            fixture.Manager.Retain(fixture.Record(11), fixture.SessionId).ShouldBeTrue();
            fixture.Io.Kill = p => fixture.Killed.Add(p.Pid);
            fixture.Io.Wait = (p, budget, _) =>
            {
                fixture.Waits.Add((p.Pid, budget));
                fixture.Clock.Advance(fixture.Killed.Count == 0 ? budget : TimeSpan.FromSeconds(Math.Min(3, budget.TotalSeconds)));
                if (fixture.Killed.Count > 0) fixture.Dead.Add(p.Pid);
                return Task.CompletedTask;
            };
            await fixture.Manager.CleanupAsync();
            fixture.Waits.Select(x => x.Budget.TotalSeconds).ShouldBe(new[] { 2d, 5d, 2d }, "phase-deadline-not-reset");
            fixture.Elapsed.ShouldBe(TimeSpan.FromSeconds(7), "phase-deadline-not-reset");
        }
    }

    [Test]
    public async Task Cleanup_reaps_a_captured_descendant_after_host_exit()
    {
        using var fixture = new OwnedPolicyFixture();
        fixture.Manager.Retain(fixture.Record(10), fixture.SessionId).ShouldBeTrue();
        fixture.Manager.Retain(fixture.Record(11) with { Host = false }, fixture.SessionId).ShouldBeTrue();
        fixture.Manager.Retain(fixture.Record(12) with { Host = false, Attributed = false }, fixture.SessionId).ShouldBeFalse();
        fixture.Dead.Add(10);
        await fixture.Manager.CleanupAsync();
        fixture.Killed.SequenceEqual(new[] { 11 }).ShouldBeTrue("surviving-descendant-killed-and-awaited");
        fixture.Waits.ShouldContain(x => x.Pid == 11 && fixture.Dead.Contains(x.Pid), "surviving-descendant-killed-and-awaited");
    }

    [Test]
    [Arguments("probe-denied")]
    [Arguments("enumeration-denied")]
    [Arguments("kill-denied")]
    [Arguments("survivor")]
    public async Task Cleanup_unknown_or_failed_operations_never_report_success(string scenario)
    {
        using var fixture = new OwnedPolicyFixture();
        fixture.Manifest = fixture.NewManifest(10);
        if (scenario == "enumeration-denied") fixture.Io.Descendants = _ => throw new UnauthorizedAccessException("enumeration denied");
        fixture.Manager.Capture(fixture.SessionId, SessionBackends.PtyHost, false);
        fixture.Manager.Retain(fixture.Record(11), fixture.SessionId).ShouldBeTrue();
        var observe = fixture.Io.Observe;
        var kill = fixture.Io.Kill;
        if (scenario == "probe-denied") fixture.Io.Observe = p => p.Pid == 10 ? new(State.Unknown, p.Generation, "denied") : observe(p);
        if (scenario == "kill-denied") fixture.Io.Kill = p =>
        {
            kill(p); // It exits independently; only the denied operation makes the result unresolved.
            if (p.Pid == 10) throw new UnauthorizedAccessException("kill denied");
        };
        if (scenario == "survivor") fixture.Io.Kill = p => { if (p.Pid == 11) kill(p); else fixture.Killed.Add(p.Pid); };
        var error = await Should.ThrowAsync<AggregateException>(() => fixture.Manager.CleanupAsync(), "cleanup-unresolved");
        error.Message.ShouldContain("cleanup-unresolved", customMessage: "cleanup-unresolved");
        error.ToString().ShouldContain("elapsed=", customMessage: "cleanup-unresolved");
        error.ToString().ShouldContain(scenario == "enumeration-denied" ? "session=" : "pid=10", customMessage: "cleanup-unresolved");
        fixture.Killed.ShouldContain(11, "proven-owned other identity still attempted");
        if (scenario == "probe-denied") fixture.Killed.ShouldNotContain(10, "unknown never authorizes kill");
    }

    [Test]
    [Arguments("cleanup-throws")]
    [Arguments("runtime-dispose-throws")]
    public async Task Dispose_failure_still_releases_runtime_and_handles(string scenario)
    {
        using var fixture = new OwnedPolicyFixture();
        fixture.Manager.Retain(fixture.Record(10), fixture.SessionId).ShouldBeTrue();
        var runtimeCalls = 0;
        var fault = new IOException("unique cleanup failure");
        if (scenario == "cleanup-throws") fixture.Io.Kill = _ => throw fault;
        var error = await Should.ThrowAsync<AggregateException>(() => fixture.Manager.DisposeAsync(true, () => { },
            _ => Task.CompletedTask, () => { runtimeCalls++; return scenario == "runtime-dispose-throws" ? Task.FromException(fault) : Task.CompletedTask; }));
        runtimeCalls.ShouldBe(1, "runtime-dispose-attempted-once");
        fixture.Released.SequenceEqual(new[] { 10 }).ShouldBeTrue("all-observer-handles-released-once");
        Contains(error, fault).ShouldBeTrue("original error retained");
    }

    [Test]
    [Arguments("ordinary-pty")]
    [Arguments("herdr")]
    [Arguments("verification-pty")]
    [Arguments("verification-herdr")]
    public async Task Cleanup_eligibility_keeps_herdr_and_custody_separate(string scenario)
    {
        using var fixture = new OwnedPolicyFixture();
        fixture.Manifest = fixture.NewManifest(10);
        var backend = scenario.EndsWith("herdr") ? SessionBackends.Herdr : SessionBackends.PtyHost;
        var bound = scenario.StartsWith("verification");
        fixture.Manager.Capture(fixture.SessionId, backend, bound);
        await fixture.Manager.CleanupAsync();
        fixture.Killed.Count.ShouldBe(scenario == "ordinary-pty" ? 1 : 0, "fallback-actions-zero");
        if (scenario != "ordinary-pty") fixture.Reads.ShouldBeEmpty("fallback-actions-zero");
        Eligible(backend, bound).ShouldBe(scenario == "ordinary-pty", "fallback-eligible");
    }

    [Test]
    [Arguments("missing-host")]
    [Arguments("missing-child")]
    [Arguments("missing-windows-console")]
    public void Witness_validation_rejects_incomplete_process_evidence(string scenario)
    {
        var roles = new[] { "host", "child", "console" };
        var missing = scenario == "missing-windows-console" ? "console" : scenario[8..];
        Should.Throw<IOException>(() => ValidateWitnesses(roles.Where(x => x != missing).Select(x => (x, true)), true),
            "incomplete-witness-rejected");
    }

    [Test]
    public async Task Body_and_cleanup_failures_are_both_preserved()
    {
        var body = new InvalidOperationException("body sentinel");
        var cleanup = new IOException("cleanup sentinel");
        var calls = 0;
        Exception? error = null;
        try { await ScopeAsync(() => Task.FromException(body), () => { calls++; return Task.FromException(cleanup); }); }
        catch (Exception ex) { error = ex; }
        error.ShouldNotBeNull("both-original-exceptions-retained");
        Contains(error, body).ShouldBeTrue("both-original-exceptions-retained");
        Contains(error, cleanup).ShouldBeTrue("both-original-exceptions-retained");
        calls.ShouldBe(1);
    }

    private static bool Contains(Exception tree, Exception expected) => ReferenceEquals(tree, expected)
        || tree is AggregateException aggregate && aggregate.InnerExceptions.Any(x => Contains(x, expected))
        || tree.InnerException is { } inner && Contains(inner, expected);
}

internal sealed class OwnedPolicyFixture : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "c1020-policy-" + Guid.NewGuid().ToString("N"));
    internal Guid SessionId { get; } = Guid.NewGuid();
    internal FakeTimeProvider Clock { get; } = new();
    internal Operations Io { get; }
    internal TestOwnedPtyHost Manager { get; }
    internal List<int> Killed { get; } = [];
    internal List<int> Released { get; } = [];
    internal List<string> Events { get; } = [];
    internal List<string> Reads { get; } = [];
    internal List<TimeSpan> KillTimes { get; } = [];
    internal List<(int Pid, TimeSpan Budget)> Waits { get; } = [];
    internal HashSet<int> Dead { get; } = [];
    internal Dictionary<int, long> Generations { get; } = [];
    internal PtyHostManifest? Manifest { get; set; }
    private readonly long _start;
    internal TimeSpan Elapsed => Clock.GetElapsedTime(_start);

    internal OwnedPolicyFixture()
    {
        Directory.CreateDirectory(Path.Combine(Root, "bin"));
        _start = Clock.GetTimestamp();
        Io = new()
        {
            Clock = Clock, VerifyHostCommand = false,
            ReadManifest = path => { Reads.Add(path); return Manifest; },
            Capture = pid => Record(pid).Process,
            Descendants = _ => [],
            Observe = p => new(Dead.Contains(p.Pid) ? State.Dead : State.Alive, Generations.GetValueOrDefault(p.Pid, 100)),
            Kill = p => { Killed.Add(p.Pid); KillTimes.Add(Elapsed); Dead.Add(p.Pid); },
            Release = p => Released.Add(p.Pid),
            Record = (operation, _) => Events.Add(operation),
            Wait = (p, budget, _) => { Waits.Add((p.Pid, budget)); if (!Dead.Contains(p.Pid)) Clock.Advance(budget); return Task.CompletedTask; },
        };
        Manager = new(Root, Io);
    }

    internal Owned Record(int pid)
    {
        Generations.TryAdd(pid, 100);
        return new(Root, SessionId, PtyHostManifest.PathFor(Path.Combine(Root, "manifests"), SessionId),
            new(pid, Generations[pid], Clock.GetUtcNow().UtcDateTime, Path.Combine(Root, "bin", "generation", PtyHostLauncher.HostExeName)), true, true);
    }
    internal PtyHostManifest NewManifest(int pid) => new()
    {
        SessionId = SessionId, HostPid = pid, HostStartTimeUtc = Clock.GetUtcNow().UtcDateTime, PipeName = "owned-policy",
    };
    public void Dispose() => Directory.Delete(Root, true);
}
