using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Protocol-20 wire fake plus explicit OS observation seam; live tests own the OS evidence.</summary>
internal sealed class HerdrPaneDisposalFixture : IAsyncDisposable
{
    public FakeHerdrServer Fake { get; } = new();
    public SessionRunnerSettings Settings { get; } = new()
    {
        SessionLogPath = Path.Combine(Path.GetTempPath(), "antiphon-c461-" + Guid.NewGuid().ToString("N")),
    };
    public OffsetClock Clock { get; } = new();
    public HerdrClient Client { get; }
    public SessionRunnerRuntime Runtime { get; }
    public HerdrPaneDisposalService Service { get; private set; }
    public string PaneId { get; }
    public Guid SessionId { get; } = Guid.NewGuid();
    public TestProcesses Processes { get; } = new();
    public TestBackend Backend { get; private set; } = null!;

    public HerdrPaneDisposalFixture()
    {
        Client = new(new HerdrSettings { Enabled = true, Session = Fake.Session });
        Runtime = new(Options.Create(Settings), NullLogger<SessionRunnerRuntime>.Instance, Client);
        var workspace = Fake.SeedWorkspace("w1", "selected-workspace");
        var tab = Fake.SeedTab(workspace.WorkspaceId, "selected-tab", paneCount: 2);
        PaneId = tab.Panes[0].PaneId;
        tab.Panes[0].Tokens = new() { ["antiphon-session"] = SessionId.ToString("D") };
        Fake.SetPaneProcessInfo(PaneId, 4242, Array.Empty<(int, string)>());
        Service = RecreateService();
    }

    public async Task StartAsync()
    {
        Fake.Start();
        await Fake.WaitUntilListeningAsync();
    }

    public HerdrPaneDisposalService RecreateService(IHerdrDisposalReceiptStore? store = null)
    {
        Backend = new(new HerdrDisposalBackend(Client, Runtime, new(Settings.SessionLogPath), Processes));
        return Service = new(Runtime, Settings.SessionLogPath, Clock, Backend, store);
    }

    public HerdrPaneDisposalRequest Request(HerdrPaneDisposalPreview preview) =>
        new(Guid.NewGuid(), preview.PreviewId, "dispose exact reviewed leftover", "antiphon-best-effort");

    public void Occupied(string kind = "grok", bool native = true)
    {
        Fake.SeedDetectedAgent(PaneId, kind);
        Fake.SetPaneProcessInfo(PaneId, 4242,
            [(4243, kind + ".exe", native ? [kind, "--session-id", SessionId.ToString("D")] : [kind], null)]);
    }

    public Task<HerdrPaneDisposalPreview> PreviewAsync() =>
        Service.PreviewAsync(new(PaneId, SessionId), CancellationToken.None);

    public string[] Methods => Fake.Requests.Select(r => r.GetProperty("method").GetString()!).ToArray();

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        await Fake.DisposeAsync();
        var resolved = Path.GetFullPath(Settings.SessionLogPath);
        if (!resolved.StartsWith(Path.Combine(Path.GetTempPath(), "antiphon-c461-"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Fixture cleanup escaped its owned root.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
    }

    public sealed class OffsetClock : TimeProvider
    {
        public TimeSpan Offset { get; set; }
        public DateTimeOffset? Now { get; set; }
        public override DateTimeOffset GetUtcNow() => (Now ?? DateTimeOffset.UtcNow) + Offset;
    }

    internal sealed class TestProcesses : IHerdrDisposalProcessInspector
    {
        public DateTime Started { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public bool Complete { get; set; } = true;
        public bool? Alive { get; set; } = false;
        public List<HerdrPaneDisposalProcess> Background { get; } = [];
        public HerdrDisposalProcessSnapshot Inspect(HerdrPaneProcessInfo p)
        {
            var shell = new HerdrPaneDisposalProcess(p.ShellPid ?? 0, "pwsh.exe", Started);
            var fg = p.ForegroundProcesses?.Select(f => new HerdrPaneDisposalProcess(f.Pid,
                Path.GetFileName(f.Name), Started.AddSeconds(1), shell.Pid, HerdrDisposalIdentity.NativeIds(f.Argv))).ToArray();
            return new(shell, fg, new[] { shell }.Concat(fg ?? []).Concat(Background).ToArray(), Complete);
        }
        public bool? IsSameProcessAlive(HerdrPaneDisposalProcess p) => Alive;
    }

    internal sealed class TestBackend(IHerdrDisposalBackend inner) : IHerdrDisposalBackend
    {
        public int Inspections { get; private set; }
        public int Closes { get; private set; }
        public Func<int, Task>? BeforeInspect { get; set; }
        public Func<HerdrDisposalObservation, HerdrDisposalObservation>? Transform { get; set; }
        public Func<Task>? BeforeClose { get; set; }
        public bool DropAfterClose { get; set; }
        public bool DropBeforeClose { get; set; }
        public HerdrPaneDisposalPreview? Queried { get; private set; }
        public async Task<HerdrDisposalObservation> InspectAsync(string pane, bool labels, CancellationToken ct)
        {
            if (BeforeInspect is not null) await BeforeInspect(++Inspections);
            else Inspections++;
            var o = await inner.InspectAsync(pane, labels, ct);
            return Transform?.Invoke(o) ?? o;
        }
        public async Task CloseAsync(string pane, CancellationToken ct)
        {
            Closes++;
            if (BeforeClose is not null) await BeforeClose();
            if (DropBeforeClose) throw new IOException("test before backend commit");
            await inner.CloseAsync(pane, ct);
            if (DropAfterClose) throw new IOException("test lost close reply");
        }
        public Task<HerdrDisposalPresence> PresenceAsync(HerdrPaneDisposalPreview p, CancellationToken ct)
        { Queried = p; return inner.PresenceAsync(p, ct); }
    }
}
