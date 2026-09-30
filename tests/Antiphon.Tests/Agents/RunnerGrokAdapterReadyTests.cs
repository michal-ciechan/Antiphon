using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class RunnerGrokAdapterReadyTests
{
    private static readonly string Ready = GrokStartupFixture.ReadyScreen();
    private const string Trust = "Do you trust the contents of this directory?\nYes, proceed  y\nNo, quit  n";
    private const string SignIn = "Approve in your browser to finish signing in.\nWaiting for approval...";

    [Test]
    public async Task Spinner_sequence_advance_does_not_prevent_positive_ready()
    {
        var client = new ScriptedClient([Ready]);
        await using var adapter = NewAdapter(client, max: 500, settle: 60);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.SnapshotReads.ShouldBeGreaterThanOrEqualTo(2);
        client.BufferReads.ShouldBe(0);
        client.Writes.ShouldBeEmpty();
    }

    [Test]
    public async Task Animated_sign_in_blocks_without_input_and_sets_launch_block()
    {
        var client = new ScriptedClient([SignIn + "\n" + Trust]);
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        adapter.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
        adapter.LaunchBlock.Reason.ShouldContain("grok login");
        client.Writes.ShouldBeEmpty();
        client.SnapshotReads.ShouldBe(1);
    }

    [Test]
    public async Task Current_trust_is_answered_once_before_positive_ready()
    {
        var client = new ScriptedClient([Trust, Trust, Ready, Ready], raw: Trust);
        await using var adapter = NewAdapter(client, max: 600, settle: 50);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        client.Writes.ShouldBe(["y"]);
        var stale = new ScriptedClient([Ready], raw: Trust);
        await using var staleAdapter = NewAdapter(stale, max: 400, settle: 50);
        await staleAdapter.StartAsync(Spec(), CancellationToken.None);
        (await staleAdapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeTrue();
        stale.Writes.ShouldBeEmpty();
        var questionOnly = new ScriptedClient(["Do you trust the contents of this directory?"]);
        await using var questionAdapter = NewAdapter(questionOnly, max: 100);
        await questionAdapter.StartAsync(Spec(), CancellationToken.None);
        (await questionAdapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        questionOnly.Writes.ShouldBeEmpty();
    }

    [Test]
    public async Task Post_trust_blank_or_sign_in_is_not_ready()
    {
        var blank = new ScriptedClient([Trust, ""]);
        await using var adapter = NewAdapter(blank, max: 120, trust: 0);
        await adapter.StartAsync(Spec(), CancellationToken.None);
        (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        blank.Writes.ShouldBe(["y"]);
        var signin = new ScriptedClient([Trust, SignIn]);
        await using var second = NewAdapter(signin, max: 300);
        await second.StartAsync(Spec(), CancellationToken.None);
        (await second.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        second.LaunchBlock!.Kind.ShouldBe(AgentLaunchBlockKind.ProviderSignInRequired);
        signin.Writes.ShouldBe(["y"]);
    }

    [Test]
    public async Task One_deadline_covers_reads_trust_and_minimum_age()
    {
        var frame = new GrokStartupSnapshot(Ready, "historical", 1, DateTime.UtcNow);
        var late = await GrokReadyWait.WaitAsync(async _ =>
        {
            await Task.Delay(100);
            return frame;
        }, new GrokReadyWaitOptions { MaxWait = TimeSpan.FromMilliseconds(30) });
        late.ShouldBeFalse();
        var hung = new TaskCompletionSource<GrokStartupSnapshot?>();
        var began = DateTime.UtcNow;
        (await GrokReadyWait.WaitAsync(_ => hung.Task,
            new GrokReadyWaitOptions { MaxWait = TimeSpan.FromMilliseconds(30) })).ShouldBeFalse();
        (DateTime.UtcNow - began).ShouldBeLessThan(TimeSpan.FromSeconds(1));
        (await GrokReadyWait.WaitAsync(_ => Task.FromResult<GrokStartupSnapshot?>(frame),
            new GrokReadyWaitOptions { MaxWait = TimeSpan.Zero })).ShouldBeFalse();
        (await GrokReadyWait.WaitAsync(_ => Task.FromResult<GrokStartupSnapshot?>(frame),
            new GrokReadyWaitOptions { MaxWait = TimeSpan.FromMilliseconds(70),
                MinimumAgeRemaining = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(10),
                Settle = TimeSpan.Zero })).ShouldBeFalse();
    }

    [Test]
    public async Task Exit_and_cancellation_stop_without_input()
    {
        var reads = 0;
        (await GrokReadyWait.WaitAsync(_ => { reads++; return Task.FromResult<GrokStartupSnapshot?>(null); },
            new GrokReadyWaitOptions(), isExited: () => true)).ShouldBeFalse();
        reads.ShouldBe(0);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => GrokReadyWait.WaitAsync(
            _ => Task.FromResult<GrokStartupSnapshot?>(null), new GrokReadyWaitOptions(), ct: cancel.Token));
    }

    [Test]
    public async Task Timeout_captures_last_frame_and_io_failure_preserves_failure()
    {
        var root = Path.Combine(Path.GetTempPath(), "c778-v12-" + Guid.NewGuid().ToString("N"));
        try
        {
            var client = new ScriptedClient(["", "Starting session…"]);
            await using var adapter = NewAdapter(client, max: 100, captureDirectory: root);
            await adapter.StartAsync(Spec(), CancellationToken.None);
            (await adapter.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
            var file = Directory.GetFiles(root, "grok-startup-*.txt").Single();
            var content = File.ReadAllText(file);
            content.ShouldContain("outcome: Deadline");
            content.ShouldContain($"frameSequence: {client.SnapshotReads}");
            client.BufferReads.ShouldBe(0);
            var blockedPath = Path.Combine(root, "not-a-directory");
            File.WriteAllText(blockedPath, "sentinel");
            var bad = new ScriptedClient([""]);
            await using var second = NewAdapter(bad, max: 50, captureDirectory: blockedPath);
            await second.StartAsync(Spec(), CancellationToken.None);
            (await second.WaitForReadyAsync(CancellationToken.None)).ShouldBeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static RunnerGrokAdapter NewAdapter(ScriptedClient client, int max = 300,
        int settle = 50, int trust = 150, string? captureDirectory = null) => new(client,
        Options.Create(new AgentRegistrySettings
        {
            GrokReadyMaxWaitMs = max, GrokReadyQuietPeriodMs = settle,
            GrokReadyMinTotalWaitMs = 0, GrokTrustPromptSettleMs = trust,
            GrokStartupCaptureDirectory = captureDirectory,
        }));

    private static AgentLaunchSpec Spec() => new("grok", AgentKind.Grok, "grok.exe", [],
        new Dictionary<string, string>(), "/tmp", 120, 30, SessionId: Guid.NewGuid());

    private sealed class ScriptedClient(IReadOnlyList<string> screens, string? raw = null) : ISessionRunnerClient
    {
        private int _index;
        public int SnapshotReads { get; private set; }
        public int BufferReads { get; private set; }
        public List<string> Writes { get; } = [];
        private string Screen => screens[Math.Min(_index, screens.Count - 1)];
        public Task<SessionRunnerSessionDto> StartAsync(Guid id, AgentLaunchSpec spec, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(id, 12, DateTime.UtcNow.AddMinutes(-1), "Running", null,
                AgentExitReason.Unknown, 0));
        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
        public Task<SessionRunnerSessionDto> GetAsync(Guid id, CancellationToken ct) => StartAsync(id, Spec(), ct);
        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid id, CancellationToken ct)
        {
            BufferReads++;
            return Task.FromResult(new SessionRunnerBufferDto(id, Screen, SnapshotReads));
        }
        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid id, CancellationToken ct)
        {
            var screen = Screen;
            _index++;
            SnapshotReads++;
            return Task.FromResult(new SessionRunnerSnapshotDto(id, raw ?? screen, screen,
                SnapshotReads, DateTime.UtcNow.AddMinutes(-1)));
        }
        public Task SendInputAsync(Guid id, string input, CancellationToken ct)
        {
            Writes.Add(input);
            return Task.CompletedTask;
        }
        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(id, [], 0));
        public Task ClearLiveBufferAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid id, int cols, int rows, CancellationToken ct) => Task.CompletedTask;
        public Task<SessionRunnerSessionDto> KillAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(id, null, DateTime.UtcNow, "Exited", 0,
                AgentExitReason.KilledByRequest, SnapshotReads));
        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask; yield break; }
    }
}
