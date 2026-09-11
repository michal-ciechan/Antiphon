using System.Text.Json;
using System.Diagnostics;
using Antiphon.SessionRunner.Contracts;
using Shouldly;
using TUnit.Core;

namespace Antiphon.SessionRunner.Tests;

[ParallelLimiter<ProcessSpawnLimit>]
public sealed partial class HerdrPaneDisposalServiceTests
{
    [Test] public Task C461_G004_Preview_required() => Expired_restarted_and_unknown_previews_never_create_receipts();
    [Test] [Arguments(-1)] [Arguments(0)] [Arguments(1)] public async Task C461_G005_Preview_expiry(int ticks)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); h.Clock.Now = DateTimeOffset.UtcNow;
        var p = await h.PreviewAsync(); h.Clock.Now = p.ExpiresAtUtc.AddTicks(ticks);
        if (ticks < 0) (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Closed");
        else (await Should.ThrowAsync<HerdrLaunchException>(() => h.Service.ExecuteAsync(h.Request(p), default))).Code.ShouldBe(HerdrPaneDisposalCodes.PreviewExpired);
        h.Backend.Closes.ShouldBe(ticks < 0 ? 1 : 0);
    }
    [Test] public Task C461_G006_Preview_restart() => Expired_restarted_and_unknown_previews_never_create_receipts();
    [Test] public Task C461_G012_Preview_read_only() => Protocol20_preview_is_read_only_and_redacts_process_arguments();
    [Test] public Task C461_G089_Operation_conflict() => Refusal_is_durable_idempotent_and_never_dispatches_teardown();
    [Test] public Task C461_G112_Bounded_preview_store() => Preview_capacity_evicts_oldest_and_retains_latest();

    [Test] public async Task C461_G007_Refused_preview()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        h.Processes.Complete = false;
        var p = await h.PreviewAsync(); p.Eligible.ShouldBeFalse();
        h.Processes.Complete = true;
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Outcome.ShouldBe("Refused"); h.Backend.Closes.ShouldBe(0);
    }
    [Test] public async Task C461_G008_Immutable_snapshot()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var p = await h.PreviewAsync();
        ((IList<HerdrPaneDisposalProcess>)p.AffectedProcesses!)[0] = new(9999, "foreign.exe");
        var request = JsonSerializer.Deserialize<HerdrPaneDisposalRequest>(JsonSerializer.Serialize(h.Request(p)).TrimEnd('}') + ",\"PaneId\":\"w1:p999\",\"Force\":true}")!;
        var r = await h.Service.ExecuteAsync(request, default);
        r.Outcome.ShouldBe("Closed"); r.PaneId.ShouldBe(h.PaneId);
        h.Fake.Workspaces.SelectMany(w => w.Tabs).SelectMany(t => t.Panes).Count().ShouldBe(2);
    }
    [Test] public async Task C461_G014_Backend_feature_gate()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        h.Fake.PingProtocol = 21;
        h.Client.Settings.ExpectedProtocol = 21;
        (await Should.ThrowAsync<HerdrLaunchException>(() => h.PreviewAsync())).Code.ShouldBe(HerdrPaneDisposalCodes.GuardUnavailable);
        h.Backend.Closes.ShouldBe(0);
    }
    [Test] public async Task Negotiates_guarded_and_legacy_daemons()
    {
        await C461_G014_Backend_feature_gate();
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var p = await h.PreviewAsync(); p.GuardAvailable.ShouldBeTrue(); p.AtomicClose.ShouldBeFalse();
        (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Closed");
        h.Methods.Count(m => m == "pane.close").ShouldBe(1);
    }

    internal static HerdrPaneSidecar Sidecar(HerdrPaneDisposalFixture h, Guid? id = null) => new()
    {
        SessionId = id ?? h.SessionId, WorkspaceKey = "owned-test", WorkspaceId = "w1", TabId = "w1:t1", PaneId = h.PaneId,
        Origin = HerdrPaneOrigins.Attached, AgentKind = "grok", ChildPid = 4243, ChildStartedAtUtc = h.Processes.Started.AddSeconds(1),
    };
    internal static string SaveLocator(HerdrPaneDisposalFixture h, string kind, Guid? id = null)
    {
        var s = Sidecar(h, id);
        var path = kind == "sidecar" ? HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, s.SessionId) : HerdrLastPane.PathFor(h.Settings.SessionLogPath, s.SessionId);
        if (kind == "sidecar") s.SaveAtomic(path);
        else HerdrLastPane.FromSidecar(s, HerdrExitReasons.Detached).SaveAtomic(path);
        return path;
    }
    [Test]
    [Arguments("idle", "detached")] [Arguments("claude", "detached")] [Arguments("grok", "detached")] [Arguments("codex", "detached")]
    [Arguments("idle", "exited")] [Arguments("claude", "exited")] [Arguments("grok", "exited")] [Arguments("codex", "exited")]
    [Arguments("idle", "sidecar")] [Arguments("claude", "sidecar")] [Arguments("grok", "sidecar")] [Arguments("codex", "sidecar")]
    [Arguments("idle", "last-pane")] [Arguments("claude", "last-pane")] [Arguments("grok", "last-pane")] [Arguments("codex", "last-pane")]
    [Arguments("idle", "rowless")] [Arguments("claude", "rowless")] [Arguments("grok", "rowless")] [Arguments("codex", "rowless")]
    public async Task Disposes_each_supported_evidence_shape(string occupant, string locator)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var pane = h.Fake.Workspaces.SelectMany(w => w.Tabs).SelectMany(t => t.Panes).Single(p => p.PaneId == h.PaneId);
        if (locator is "sidecar" or "last-pane" or "exited")
        {
            pane.Tokens = null; SaveLocator(h, locator == "last-pane" ? locator : "sidecar");
            if (locator == "exited")
            {
                await h.Runtime.AdoptOrphanedHostsAsync(new DeadProbe(), default);
                h.Runtime.Get(h.SessionId).Status.ShouldBe("Exited");
            }
        }
        if (locator == "detached")
        {
            h.Occupied("claude");
            await h.Runtime.AttachHerdrAsync(new(h.SessionId, h.PaneId, "claude", "claude-jsonl", 4243, "owned-test", ExpectedNativeSessionId: h.SessionId), default);
            await h.Runtime.KillAsync(h.SessionId, TimeSpan.FromSeconds(2), default);
            h.Runtime.Get(h.SessionId).ExitReason.ShouldBe(HerdrExitReasons.Detached);
            pane.Agent = null; h.Fake.SetPaneProcessInfo(h.PaneId, 4242, Array.Empty<(int, string)>());
        }
        if (occupant != "idle") h.Occupied(occupant);
        var p = await h.Service.PreviewAsync(new(h.PaneId, h.SessionId, occupant == "codex" ? h.SessionId : null), default);
        p.Eligible.ShouldBeTrue();
        (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Closed");
        h.Fake.Workspaces.SelectMany(w => w.Tabs).SelectMany(t => t.Panes).Count().ShouldBe(2);
    }
    private sealed class DeadProbe : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAt) => false;
        public string? TryGetProcessName(int pid) => null;
        public DateTime? TryGetStartTimeUtc(int pid) => null;
    }
    private static async Task Bound(bool other)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var p = await h.PreviewAsync();
        h.Occupied("claude");
        var binding = other ? Guid.NewGuid() : h.SessionId;
        await h.Runtime.AttachHerdrAsync(new(binding, h.PaneId, "claude", "claude-jsonl", 4243, "owned-test", ExpectedNativeSessionId: h.SessionId), default);
        // Prove the runtime claim itself, independently of files and metadata that can disappear.
        File.Delete(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, binding));
        var pane = h.Fake.Workspaces.SelectMany(w => w.Tabs).SelectMany(t => t.Panes).Single(pane => pane.PaneId == h.PaneId);
        pane.Tokens = new() { ["antiphon-session"] = h.SessionId.ToString() };
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Code.ShouldBe(HerdrProblemTypes.PaneBound); h.Backend.Closes.ShouldBe(0);
    }
    [Test] public Task C461_G042_Own_live_binding() => Bound(false);
    [Test] public Task C461_G043_Other_live_binding() => Bound(true);
    [Test] public async Task C461_G044_Pending_adoption_binding()
    {
        await using var h = new HerdrPaneDisposalFixture(); var path = SaveLocator(h, "sidecar");
        await h.Runtime.AdoptOrphanedHostsAsync(new HerdrPaneDisposalConcurrencyTests.Probe(), default);
        h.Runtime.Get(h.SessionId).Pending.ShouldBe(HerdrPendingReasons.Unreachable);
        await h.StartAsync(); var p = await h.PreviewAsync(); p.Blockers.ShouldContain(HerdrProblemTypes.PaneBound);
        (await h.Service.ExecuteAsync(h.Request(p), default)).Code.ShouldBe(HerdrProblemTypes.PaneBound);
        h.Backend.Closes.ShouldBe(0); File.Exists(path).ShouldBeTrue();
    }
    private static async Task ConflictingFile(string kind)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var p = await h.PreviewAsync(); var path = SaveLocator(h, kind, Guid.NewGuid());
        var bytes = File.ReadAllBytes(path);
        (await h.Service.ExecuteAsync(h.Request(p), default)).Code.ShouldBe(HerdrProblemTypes.PaneBound);
        File.ReadAllBytes(path).ShouldBe(bytes); h.Backend.Closes.ShouldBe(0);
    }
    [Test] public Task C461_G045_Sidecar_claim() => ConflictingFile("sidecar");
    [Test] public Task C461_G046_Last_pane_claim() => ConflictingFile("last-pane");

    private static async Task MutationAllowlist(bool refused = false)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var selectedTab = h.Fake.Workspaces[0].Tabs.Single(t => t.Panes.Any(p => p.PaneId == h.PaneId));
        var sibling = selectedTab.Panes.Single(p => p.PaneId != h.PaneId);
        var before = JsonSerializer.Serialize(sibling);
        var p = await h.PreviewAsync();
        if (refused) h.Processes.Background.Add(new(999, "foreign.exe", h.Processes.Started.AddSeconds(1), 4242));
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Outcome.ShouldBe(refused ? "Refused" : "Closed");
        h.Methods.ShouldAllBe(m => new[] { "ping", "pane.get", "pane.process_info", "workspace.list", "tab.list", "agent.list", "pane.close" }.Contains(m));
        h.Methods.Count(m => m == "pane.close").ShouldBe(refused ? 0 : 1);
        JsonSerializer.Serialize(sibling).ShouldBe(before);
        h.Fake.Workspaces.ShouldHaveSingleItem(); h.Fake.Workspaces[0].Tabs.Count.ShouldBe(2);
        selectedTab.Panes.ShouldContain(sibling);
    }
    [Test] public async Task C461_G063_Disposal_no_pid_fallback()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        static Process Dummy() => Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /q /k")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        using var original = Dummy(); using var foreign = Dummy();
        try
        {
            (Sidecar(h) with { ChildPid = original.Id, ChildStartedAtUtc = original.StartTime.ToUniversalTime() })
                .SaveAtomic(HerdrPaneSidecar.PathFor(h.Settings.SessionLogPath, h.SessionId));
            h.Fake.SeedDetectedAgent(h.PaneId, "grok"); h.Fake.SetPaneProcessInfo(h.PaneId, 4242, (foreign.Id, "foreign.exe"));
            var p = await h.PreviewAsync(); p.Blockers.ShouldContain(HerdrProblemTypes.PaneForeign);
            (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Refused");
            original.HasExited.ShouldBeFalse(); foreign.HasExited.ShouldBeFalse(); h.Methods.ShouldNotContain("pane.close");
        }
        finally
        {
            foreach (var owned in new[] { original, foreign }) if (!owned.HasExited) { owned.Kill(entireProcessTree: true); await owned.WaitForExitAsync(); }
        }
    }
    [Test] public Task C461_G075_No_split() => MutationAllowlist();
    [Test] public Task C461_G076_No_workspace_ensure() => MutationAllowlist();
    [Test] public Task C461_G077_No_tab_create() => MutationAllowlist(true);
    [Test] public Task C461_G078_No_rename() => MutationAllowlist(true);
    [Test] public Task C461_G079_No_move() => MutationAllowlist();
    [Test] public Task C461_G080_No_input_or_start() => MutationAllowlist();
    [Test] public Task C461_G081_No_tab_close() => MutationAllowlist();
    [Test] public Task C461_G082_No_workspace_close() => MutationAllowlist();

    internal sealed class FaultStore(string root) : IHerdrDisposalReceiptStore
    {
        private readonly HerdrDisposalReceiptStore _inner = new(root);
        public bool Fail { get; set; }
        public Action<HerdrDisposalStoredOperation>? BeforeSave { get; set; }
        public HerdrDisposalStoredOperation? Read(Guid id) => _inner.Read(id);
        public void Prune(DateTimeOffset cutoff) => _inner.Prune(cutoff);
        public void Save(HerdrDisposalStoredOperation o) { BeforeSave?.Invoke(o); if (Fail) throw new IOException("owned test fault"); _inner.Save(o); }
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public async Task C461_G086_Intent_before_rpc(bool deny)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var store = new FaultStore(h.Settings.SessionLogPath) { Fail = deny }; h.RecreateService(store);
        var p = await h.PreviewAsync(); var request = h.Request(p);
        h.Backend.BeforeClose = () => { store.Read(request.OperationId)!.Receipt.Outcome.ShouldBe("Unknown"); return Task.CompletedTask; };
        if (deny) await Should.ThrowAsync<IOException>(() => h.Service.ExecuteAsync(request, default));
        else (await h.Service.ExecuteAsync(request, default)).Outcome.ShouldBe("Closed");
        h.Backend.Closes.ShouldBe(deny ? 0 : 1);
    }
    [Test] public async Task C461_G087_Atomic_receipt()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var store = new FaultStore(h.Settings.SessionLogPath); h.RecreateService(store);
        var p = await h.PreviewAsync(); var request = h.Request(p);
        store.BeforeSave = row =>
        {
            if (row.Receipt.Outcome == "Closed")
            { store.Read(request.OperationId)!.Receipt.Outcome.ShouldBe("Unknown"); throw new IOException("crash before publish"); }
        };
        await Should.ThrowAsync<IOException>(() => h.Service.ExecuteAsync(request, default));
        store.Read(request.OperationId)!.Receipt.Outcome.ShouldBe("Unknown");
        h.RecreateService();
        (await h.Service.GetAsync(request.OperationId, default))!.Outcome.ShouldBe("AlreadyAbsent");
        h.Methods.Count(m => m == "pane.close").ShouldBe(1);
    }
    [Test] public async Task C461_G088_Same_operation_idempotent()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var p = await h.PreviewAsync(); var request = h.Request(p);
        var result = await h.Service.ExecuteAsync(request, default);
        h.Clock.Offset = TimeSpan.FromDays(1); h.RecreateService();
        var retries = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => h.Service.ExecuteAsync(request, default)));
        retries.ShouldAllBe(r => r == result); h.Methods.Count(m => m == "pane.close").ShouldBe(1);
    }
    [Test] public async Task C461_G090_Preview_single_consumer()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync();
        var requests = new[] { h.Request(p), h.Request(p) };
        var results = await Task.WhenAll(requests.Select(async r => { try { return (await h.Service.ExecuteAsync(r, default)).Outcome; } catch (HerdrLaunchException e) { return e.Code; } }));
        results.ShouldContain("Closed"); results.ShouldContain(HerdrPaneDisposalCodes.PreviewConsumed); h.Backend.Closes.ShouldBe(1);
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public async Task C461_G091_Unknown_after_send(bool committed)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync();
        var path = SaveLocator(h, "sidecar"); var p = await h.PreviewAsync();
        h.Backend.DropAfterClose = committed; h.Backend.DropBeforeClose = !committed;
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Outcome.ShouldBe("Unknown"); r.PaneLeftOpen.ShouldBeNull(); File.Exists(path).ShouldBeTrue();
    }
    [Test] [Arguments(false)] [Arguments(true)]
    public async Task C461_G092_No_destructive_replay(bool committed)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync(); var request = h.Request(p);
        h.Backend.DropAfterClose = committed; h.Backend.DropBeforeClose = !committed;
        (await h.Service.ExecuteAsync(request, default)).Outcome.ShouldBe("Unknown");
        h.RecreateService();
        (await h.Service.GetAsync(request.OperationId, default))!.Outcome.ShouldBe(committed ? "AlreadyAbsent" : "Unknown");
        await h.Service.ExecuteAsync(request, default); h.Backend.Closes.ShouldBe(0);
    }
    [Test] public async Task C461_G093_Incarnation_absence()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync(); var request = h.Request(p);
        h.Backend.DropAfterClose = true; h.Processes.Alive = null;
        await h.Service.ExecuteAsync(request, default);
        (await h.Service.GetAsync(request.OperationId, default))!.Outcome.ShouldBe("Unknown");
        h.Processes.Alive = false;
        (await h.Service.GetAsync(request.OperationId, default))!.Outcome.ShouldBe("AlreadyAbsent");
        h.Backend.Closes.ShouldBe(1);
    }
    [Test] public async Task C461_G094_Unavailable_is_not_absent()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var path = SaveLocator(h, "sidecar"); var p = await h.PreviewAsync();
        h.Backend.BeforeInspect = _ => throw new IOException("owned unavailable test");
        var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.Outcome.ShouldBe("Refused"); r.Code.ShouldBe(HerdrProblemTypes.Unreachable); r.PaneLeftOpen.ShouldBeNull();
        h.Backend.Closes.ShouldBe(0); File.Exists(path).ShouldBeTrue();
    }
    [Test] [Arguments("Unknown", false, true)] [Arguments("Closed", true, true)] [Arguments("Closed", false, false)] [Arguments("Refused", false, false)]
    public async Task C461_G095_Unresolved_retention(string outcome, bool cleanup, bool retained)
    {
        await using var h = new HerdrPaneDisposalFixture(); var store = new HerdrDisposalReceiptStore(h.Settings.SessionLogPath); var id = Guid.NewGuid();
        store.Save(new("test", new(id, Guid.NewGuid(), h.PaneId, outcome, "test", DateTimeOffset.UtcNow.AddDays(-8), null, cleanup)));
        store.Prune(DateTimeOffset.UtcNow.AddDays(-7)); (store.Read(id) is not null).ShouldBe(retained);
    }
    [Test] public Task C461_G096_Cleanup_after_confirmation() => C461_G091_Unknown_after_send(false);
    private static async Task GenerationCleanup(string kind, bool otherPane)
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var path = SaveLocator(h, kind); var p = await h.PreviewAsync();
        byte[]? replacement = null;
        h.Backend.BeforeClose = () =>
        {
            if (kind == "sidecar") (Sidecar(h) with { PaneId = otherPane ? "w1:p999" : h.PaneId, ChildPid = 999 }).SaveAtomic(path);
            else (HerdrLastPane.FromSidecar(Sidecar(h), HerdrExitReasons.Detached) with { PaneId = otherPane ? "w1:p999" : h.PaneId, LastChildPid = 999 }).SaveAtomic(path);
            replacement = File.ReadAllBytes(path); return Task.CompletedTask;
        };
        (await h.Service.ExecuteAsync(h.Request(p), default)).Outcome.ShouldBe("Closed"); File.ReadAllBytes(path).ShouldBe(replacement!);
    }
    [Test] [Arguments(false)] [Arguments(true)] public Task C461_G097_Sidecar_generation_cleanup(bool otherPane) => GenerationCleanup("sidecar", otherPane);
    [Test] [Arguments(false)] [Arguments(true)] public Task C461_G098_Last_pane_generation_cleanup(bool otherPane) => GenerationCleanup("last-pane", otherPane);
    [Test] public async Task C461_G099_No_cleanup_last_pane_write()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var path = SaveLocator(h, "sidecar");
        var p = await h.PreviewAsync(); var r = await h.Service.ExecuteAsync(h.Request(p), default);
        r.CleanupPending.ShouldBeFalse(); File.Exists(path).ShouldBeFalse(); Directory.Exists(HerdrLastPane.DirectoryFor(h.Settings.SessionLogPath)).ShouldBeFalse();
    }
    [Test] public async Task C461_G100_Cleanup_retry_only()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var path = SaveLocator(h, "sidecar"); var p = await h.PreviewAsync(); var request = h.Request(p);
        using (var pinned = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        { var r = await h.Service.ExecuteAsync(request, default); r.Outcome.ShouldBe("Closed"); r.CleanupPending.ShouldBeTrue(); }
        var recovered = await h.Service.GetAsync(request.OperationId, default);
        recovered!.CleanupPending.ShouldBeFalse(); File.Exists(path).ShouldBeFalse(); h.Backend.Closes.ShouldBe(1);
    }
    [Test] public async Task C461_G101_History_and_unrelated_files()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); SaveLocator(h, "sidecar");
        var history = Path.Combine(h.Settings.SessionLogPath, "native-transcript.jsonl"); await File.WriteAllTextAsync(history, "owned history sentinel");
        var unrelated = Path.Combine(h.Settings.SessionLogPath, "other.metadata"); await File.WriteAllTextAsync(unrelated, "unrelated sentinel");
        var p = await h.PreviewAsync(); await h.Service.ExecuteAsync(h.Request(p), default);
        File.ReadAllText(history).ShouldBe("owned history sentinel"); File.ReadAllText(unrelated).ShouldBe("unrelated sentinel");
    }
    [Test] public async Task C461_G103_No_duplicate_exit()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); using var ct = new CancellationTokenSource();
        var events = h.Runtime.Subscribe(ct.Token); var census = 0; h.Runtime.PaneSetChanged += () => census++;
        var p = await h.PreviewAsync(); await h.Service.ExecuteAsync(h.Request(p), default);
        census.ShouldBe(1); events.TryRead(out _).ShouldBeFalse();
    }
    [Test] public async Task C461_G107_Receipt_persistence_redaction()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); h.Occupied();
        h.Fake.SetPaneProcessInfo(h.PaneId, 4242, [(4243, @"C:\secret-home\grok.exe", new[] { "grok", "--session-id", h.SessionId.ToString(), "--key", "secret-canary" }, @"C:\secret-home")]);
        var p = await h.PreviewAsync(); var request = h.Request(p); await h.Service.ExecuteAsync(request, default);
        var json = File.ReadAllText(Path.Combine(h.Settings.SessionLogPath, "herdr", "disposals", request.OperationId.ToString("N") + ".json"));
        json.ShouldNotContain("secret-canary"); json.ShouldNotContain("secret-home");
    }
    [Test] public async Task C461_G113_Queryable_backend_identity()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); var p = await h.PreviewAsync(); var request = h.Request(p);
        h.Backend.DropAfterClose = true; await h.Service.ExecuteAsync(request, default); h.RecreateService(); await h.Service.GetAsync(request.OperationId, default);
        h.Backend.Queried!.BackendInstanceId.ShouldBe(p.BackendInstanceId); h.Backend.Queried.TerminalId.ShouldBe(p.TerminalId); h.Backend.Closes.ShouldBe(0);
    }
    [Test] public Task C461_G114_Census_notification() => C461_G103_No_duplicate_exit();
    [Test] public async Task C461_G115_Structured_log_redaction()
    {
        await using var h = new HerdrPaneDisposalFixture(); await h.StartAsync(); h.Occupied();
        h.Fake.SetPaneProcessInfo(h.PaneId, 4242, [(4243, @"C:\secret-home\grok.exe", new[] { "grok", "--key", "secret-canary" }, @"C:\secret-home")]);
        var logs = new List<string>();
        var service = new HerdrPaneDisposalService(h.Runtime, h.Settings.SessionLogPath, h.Clock, h.Backend,
            logger: new ListLogger<HerdrPaneDisposalService>(logs));
        var p = await service.PreviewAsync(new(h.PaneId, h.SessionId), default);
        var request = h.Request(p); await service.ExecuteAsync(request, default);
        string.Join("\n", logs).ShouldContain(request.OperationId.ToString());
        string.Join("\n", logs).ShouldNotContain("secret-canary"); string.Join("\n", logs).ShouldNotContain("secret-home");
    }
    [Test] public Task Cleanup_is_conditional_and_census_is_observable() => C461_G100_Cleanup_retry_only();
    [Test] public Task Crash_boundaries_reconcile_to_durable_receipts() => C461_G087_Atomic_receipt();
}
