// Evidence harness for CARD-0462. Exercises unchanged production code against the
// repository's named-pipe FakeHerdrServer. No real Herdr or provider process is used.
using System.Reflection;
using System.Text.Json;
using Antiphon.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.SessionRunner.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var root = Path.Combine(Path.GetTempPath(), $"antiphon-c462-{Guid.NewGuid():N}");
Directory.CreateDirectory(root);
await using var fake = new FakeHerdrServer();
fake.Start();
await fake.WaitUntilListeningAsync();
var settings = new SessionRunnerSettings { SessionLogPath = root, PtyHostLingerHours = 0.02 };
var herdrSettings = new HerdrSettings { Enabled = true, Session = fake.Session, LaunchDetectTimeoutMs = 5000 };
var client = new HerdrClient(herdrSettings);
// Reports OS processes dead so fake PID loss can never authorize a real OS kill.
await using var runtime = new SessionRunnerRuntime(Options.Create(settings),
    NullLogger<SessionRunnerRuntime>.Instance, client, new DeadProcessProbe());
var ws = fake.SeedWorkspace("w1", "Original workspace", new Dictionary<string, string> { ["antiphon-ws"] = "c462" });
var sessionId = Guid.NewGuid();
var options = new HerdrLaunchOptions("c462", "Original workspace", root, "Evidence agent",
    AgentKind: HerdrAgentKinds.Claude, TabLabel: "Original tab");
var request = new RunnerLaunchRequest(sessionId, @"C:\fake\claude.exe",
    ["--session-id", sessionId.ToString("D")], new Dictionary<string, string>(), root, 120, 30,
    TranscriptEnabled: false, Backend: SessionBackends.Herdr, Herdr: options);
var pump = new HerdrEventPumpService(runtime, client, Options.Create(herdrSettings),
    NullLogger<HerdrEventPumpService>.Instance);
var baseline = typeof(HerdrEventPumpService).GetMethod("BaselineSweepAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
Task Sweep() => (Task)baseline.Invoke(pump, [CancellationToken.None])!;
HerdrPaneSidecar Sidecar() => HerdrPaneSidecar.TryLoad(HerdrPaneSidecar.PathFor(root, sessionId))
    ?? throw new Exception("Expected sidecar");
void Emit(string step, object data) => Console.WriteLine(JsonSerializer.Serialize(new { step, data }));
void Require(bool condition, string assertion)
{
    if (!condition) throw new Exception(assertion);
}

try
{
    await runtime.StartAsync(request, CancellationToken.None);
    var original = Sidecar();
    var tab = ws.Tabs.Single(t => t.TabId == original.TabId);
    Emit("launch", new { sessionId, original.WorkspaceId, original.TabId, original.PaneId,
        original.WorkspaceLabel, original.TabLabel, status = runtime.Get(sessionId).Status });

    // Model an operator rename by changing backend labels, preserving all identities.
    tab.Label = "Renamed tab";
    ws.Label = "Renamed workspace";
    var from = fake.Requests.Count;
    await Sweep();
    var dto = await runtime.GetAsync(sessionId, CancellationToken.None);
    var after = Sidecar();
    var refreshMethods = fake.Requests.Skip(from).Select(r => r.GetProperty("method").GetString())
        .GroupBy(m => m).OrderBy(g => g.Key).ToDictionary(g => g.Key!, g => g.Count());
    Require(dto.Status == "Running" && after.PaneId == original.PaneId && after.TabId == original.TabId,
        "Rename must leave this live session on its original pane and tab");
    Require(after.TabLabel == "Original tab" && after.WorkspaceLabel == "Original workspace",
        "Expected current defect: baseline plus GET leaves stale sidecar labels");
    Emit("baseline-and-get-after-rename", new { dto.Status, after.PaneId, after.TabId,
        sidecarTabLabel = after.TabLabel, liveTabLabel = tab.Label,
        sidecarWorkspaceLabel = after.WorkspaceLabel, liveWorkspaceLabel = ws.Label, refreshMethods });

    var preflight = await runtime.CheckHerdrPlacementAsync(new HerdrPlacementCheckRequest(sessionId, options), CancellationToken.None);
    Require(preflight.Action == "create", "Stale pin must preflight as create");
    Emit("next-start-preflight", preflight);

    // Model natural child exit while its shell remains; real liveness code retires the sidecar.
    fake.SetPaneProcessInfo(original.PaneId, 4242, (4242, "powershell"));
    await runtime.LiveHerdrPanes().Single().Session.VerifyHerdrLivenessAsync(client, CancellationToken.None);
    var last = HerdrLastPane.TryLoad(HerdrLastPane.PathFor(root, sessionId))
        ?? throw new Exception("Expected last-pane record after observed child loss");
    Require(last.PaneId == original.PaneId && last.TabLabel == "Original tab", "Last-pane must retain stale launch label");
    Emit("retired-after-child-exit", new { last.PaneId, last.TabId, last.TabLabel, last.WorkspaceLabel,
        status = runtime.Get(sessionId).Status });

    from = fake.Requests.Count;
    await runtime.StartAsync(request, CancellationToken.None);
    var restarted = Sidecar();
    var creates = fake.Requests.Skip(from).Count(r => r.GetProperty("method").GetString() == "tab.create");
    Require(creates == 1 && restarted.TabId != original.TabId && restarted.PaneId != original.PaneId,
        "Expected current defect: next named launch creates a replacement tab despite last-pane");
    Require(ws.Tabs.Any(t => t.TabId == original.TabId && t.Label == "Renamed tab"), "Renamed tab must remain");
    Emit("restart", new { restarted.PaneId, restarted.TabId, restarted.TabLabel,
        createdTabs = creates, workspaceCount = fake.Workspaces.Count,
        tabs = ws.Tabs.Select(t => new { t.TabId, t.Label, panes = t.Panes.Count }).ToArray() });

    var resolver = new HerdrNamedTabResolver(HerdrNamedTabResolver.HostLabelComparer);
    var tabs = new[] { new HerdrTabInfo("w1:t1", "w1", "new", 1, 1), new HerdrTabInfo("w1:t2", "w1", "NEW", 2, 1) };
    var panes = new[] { new HerdrPaneInfo("w1:p1", "w1:t1", "w1"), new HerdrPaneInfo("w1:p2", "w1:t2", "w1") };
    string Refusal(IReadOnlyList<HerdrTabInfo> ts, IReadOnlyList<HerdrPaneInfo> ps)
    {
        try { resolver.PickUniqueSinglePaneTab(ts, ps, "w1", "new"); return "no refusal"; }
        catch (HerdrLaunchException ex) { return ex.Code; }
    }
    var ambiguous = Refusal(tabs, panes);
    var reportedInvalid = Refusal([tabs[0] with { PaneCount = 2 }], [panes[0]]);
    var enumeratedInvalid = Refusal([tabs[0]], [panes[0], panes[1] with { TabId = "w1:t1" }]);
    Require(ambiguous == HerdrProblemTypes.TabAmbiguous && reportedInvalid == HerdrProblemTypes.TabInvalid
        && enumeratedInvalid == HerdrProblemTypes.TabInvalid, "Existing refusal guards must classify the fixtures");
    Emit("existing-guards-windows", new { ambiguous, reportedInvalid, enumeratedInvalid });
    Emit("verdict", new { reproduced = true, backend = "isolated FakeHerdrServer", liveProviderProcesses = 0 });
}
finally
{
    await runtime.KillAsync(sessionId, TimeSpan.FromSeconds(2), CancellationToken.None);
    await runtime.DisposeAsync();
    var fullRoot = Path.GetFullPath(root);
    var tempRoot = Path.GetFullPath(Path.GetTempPath());
    if (!fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
        || !Path.GetFileName(fullRoot).StartsWith("antiphon-c462-", StringComparison.Ordinal))
        throw new Exception("Unexpected evidence scratch path");
    Directory.Delete(fullRoot, recursive: true);
}
