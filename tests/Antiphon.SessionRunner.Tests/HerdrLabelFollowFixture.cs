using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace Antiphon.SessionRunner.Tests;

internal sealed class HerdrLabelFollowFixture : IAsyncDisposable
{
    public FakeHerdrServer Fake { get; } = new();
    public HerdrClient Client { get; }
    public SessionRunnerSettings Settings { get; }
    public HerdrSettings HerdrSettings { get; }
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero));
    public HerdrPlacementCoordinator Coordinator { get; } = new();
    public FakeHerdrServer.WorkspaceState Workspace { get; }
    public FakeHerdrServer.TabState Tab { get; }
    public FakeHerdrServer.PaneState Pane => Tab.Panes[0];
    public HerdrPaneSidecar Binding { get; set; }
    public HerdrPaneChild Child { get; private set; } = null!;
    public LabelReader Reader { get; }
    public string Path => HerdrPaneSidecar.PathFor(Settings.SessionLogPath, Binding.SessionId);
    public string[] Methods => Fake.Requests.Select(r => r.GetProperty("method").GetString()!).ToArray();
    public int GetterCount => Methods.Count(m => m == "tab.get");
    public Task FollowAsync(CancellationToken ct = default) => Child.FollowLabelsAsync(Clock, () => true, ct);
    public Task<HerdrLabelObservation?> ReadAsync() => Child.ReadLabelObservationAsync(Clock, () => true, CancellationToken.None);
    public HerdrPaneSidecar Saved => HerdrPaneSidecar.TryLoad(Path)!;

    public async Task<SessionRunnerRuntime> AdoptRuntimeAsync()
    {
        await Child.DisposeAsync();
        var runtime = new SessionRunnerRuntime(Microsoft.Extensions.Options.Options.Create(Settings),
            NullLogger<SessionRunnerRuntime>.Instance, Client, new DenyProcesses(), timeProvider: Clock);
        await runtime.AdoptOrphanedHostsAsync(new DenyProcesses(), CancellationToken.None);
        return runtime;
    }

    public static async Task WaitAsync(Func<bool> condition)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition()) await Task.Delay(5, limit.Token);
    }

    public HerdrLabelFollowFixture(string? tabPin = "Old", string? workspacePin = "Old workspace",
        string provenance = HerdrWorkspaceSelection.UniqueUntaggedLabel)
    {
        Settings = new() { SessionLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antiphon-c462-" + Guid.NewGuid().ToString("N")) };
        HerdrSettings = new() { Enabled = true, Session = Fake.Session };
        Client = new(HerdrSettings);
        Reader = new(Client);
        Workspace = Fake.SeedWorkspace("w1", "New workspace");
        Tab = Workspace.Tabs[0];
        Tab.Label = "New";
        Fake.SetPaneProcessInfo(Pane.PaneId, 4242, [(4243, "grok.exe")]);
        Binding = new()
        {
            SessionId = Guid.NewGuid(), WorkspaceKey = "none", WorkspaceId = Workspace.WorkspaceId,
            TabId = Tab.TabId, PaneId = Pane.PaneId, ChildPid = 4243, ShellPid = 4242,
            Origin = HerdrPaneOrigins.Launched, AcceptedStartedAt = Clock.GetUtcNow().UtcDateTime,
            LaunchedAtUtc = Clock.GetUtcNow().UtcDateTime, TabLabel = tabPin, WorkspaceLabel = workspacePin,
            LabelFollow = new(1, new(1, Guid.NewGuid(), Guid.NewGuid(), workspacePin, tabPin), provenance),
        };
    }

    public async Task StartAsync()
    {
        Fake.Start();
        await Fake.WaitUntilListeningAsync();
        Binding.SaveAtomic(Path);
        await RecreateChildAsync();
    }

    public async Task RecreateChildAsync()
    {
        if (Child is not null) await Child.DisposeAsync();
        Child = new(Client, Settings, NullLogger.Instance, () => [], new DenyProcesses(), Coordinator);
        await Child.AttachExistingAsync(HerdrPaneSidecar.TryLoad(Path)!, CancellationToken.None);
    }

    public async Task<HerdrLabelCandidate> CollectAsync(StringComparer? comparer = null)
    {
        var before = Methods.Length;
        var bytes = await File.ReadAllBytesAsync(Path);
        var result = await new HerdrLabelObserver(Reader, comparer ?? StringComparer.OrdinalIgnoreCase)
            .CollectAsync(Binding, CancellationToken.None);
        AssertReadOnly(before);
        (await File.ReadAllBytesAsync(Path)).ShouldBe(bytes);
        return result;
    }

    public void AssertReadOnly(int start)
    {
        var allowed = new[] { "pane.get", "pane.process_info", "pane.read", "tab.get", "workspace.get", "tab.list", "pane.list", "workspace.list", "ping", "events.subscribe" };
        Methods.Skip(start).ShouldAllBe(m => allowed.Contains(m));
    }

    public async ValueTask DisposeAsync()
    {
        if (Child is not null) await Child.DisposeAsync();
        await Fake.DisposeAsync();
        var root = System.IO.Path.GetFullPath(Settings.SessionLogPath);
        if (!root.StartsWith(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "antiphon-c462-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsafe fixture cleanup");
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    internal sealed class DenyProcesses : IProcessLivenessProbe
    {
        public bool IsAlive(int pid, DateTime startedAtUtc) => false;
        public DateTime? TryGetStartTimeUtc(int pid) => null;
        public string? TryGetProcessName(int pid) => "pwsh.exe";
    }

    internal sealed class LabelReader(HerdrClient client) : IHerdrLabelReader
    {
        public Func<HerdrPaneInfo, int, HerdrPaneInfo>? Pane { get; set; }
        public Func<HerdrTabInfo, int, HerdrTabInfo>? Tab { get; set; }
        public Func<HerdrWorkspaceInfo, int, HerdrWorkspaceInfo>? Workspace { get; set; }
        public Func<IReadOnlyList<HerdrTabInfo>, IReadOnlyList<HerdrTabInfo>>? Tabs { get; set; }
        public Func<IReadOnlyList<HerdrPaneInfo>, IReadOnlyList<HerdrPaneInfo>>? Panes { get; set; }
        public Func<IReadOnlyList<HerdrWorkspaceInfo>, IReadOnlyList<HerdrWorkspaceInfo>>? Workspaces { get; set; }
        private int _paneReads, _tabReads, _workspaceReads;
        public async Task<HerdrPaneInfo> PaneGetAsync(string id, CancellationToken ct)
        { var p = await client.PaneGetAsync(id, ct); return Pane?.Invoke(p, ++_paneReads) ?? p; }
        public Task<HerdrPaneProcessInfo> PaneProcessInfoAsync(string id, CancellationToken ct) => client.PaneProcessInfoAsync(id, ct);
        public async Task<HerdrTabInfo> TabGetAsync(string id, CancellationToken ct)
        { var t = await client.TabGetAsync(id, ct); return Tab?.Invoke(t, ++_tabReads) ?? t; }
        public async Task<HerdrWorkspaceInfo> WorkspaceGetAsync(string id, CancellationToken ct)
        { var w = await client.WorkspaceGetAsync(id, ct); return Workspace?.Invoke(w, ++_workspaceReads) ?? w; }
        public async Task<IReadOnlyList<HerdrTabInfo>> TabListAsync(string id, CancellationToken ct)
        { var t = await client.TabListAsync(id, ct); return Tabs?.Invoke(t) ?? t; }
        public async Task<IReadOnlyList<HerdrPaneInfo>> PaneListAsync(string? id, CancellationToken ct)
        { var p = await client.PaneListAsync(id, ct); return Panes?.Invoke(p) ?? p; }
        public async Task<IReadOnlyList<HerdrWorkspaceInfo>> WorkspaceListAsync(CancellationToken ct)
        { var w = await client.WorkspaceListAsync(ct); return Workspaces?.Invoke(w) ?? w; }
    }
}
