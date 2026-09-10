using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Antiphon.SessionRunner.Tests;

/// <summary>Protocol-20 fake only. Does not model or establish a guarded backend.</summary>
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

    public HerdrPaneDisposalFixture()
    {
        Client = new(new HerdrSettings { Enabled = true, Session = Fake.Session });
        Runtime = new(Options.Create(Settings), NullLogger<SessionRunnerRuntime>.Instance, Client);
        Service = RecreateService();
        var workspace = Fake.SeedWorkspace("w1", "selected-workspace");
        var tab = Fake.SeedTab(workspace.WorkspaceId, "selected-tab", paneCount: 2);
        PaneId = tab.Panes[0].PaneId;
    }

    public async Task StartAsync()
    {
        Fake.Start();
        await Fake.WaitUntilListeningAsync();
    }

    public HerdrPaneDisposalService RecreateService() =>
        Service = new(Client, Runtime, Options.Create(Settings), Clock);

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
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + Offset;
    }
}
