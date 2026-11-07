using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-1761 / CARD-1758 slice 1. Sequence polls and a single frame replace the ANSI tail.
/// A pty frame is reused only while <c>LastSequence</c> is unchanged (no added staleness).
/// Herdr and an unknown backend still snapshot on every poll.
/// </summary>
[Category("Unit")]
public class Card1761RunnerReadPollTests
{
    [Test]
    public async Task Card1761_Sequence_poll_reads_the_session_not_the_ansi_tail()
    {
        var client = new CountingRunner { Sequence = 11 };
        var terminal = new RunnerTerminalSession(client);
        await terminal.StartAsync(Spec(), CancellationToken.None);
        client.Reset();

        var sequence = await terminal.GetLastSequenceAsync(CancellationToken.None);

        sequence.ShouldBe(11);
        client.SessionGets.ShouldBe(1);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Manual_turn_sequence_reads_the_session_not_the_ansi_tail()
    {
        var client = new CountingRunner { Sequence = 19 };
        var runtime = Runtime(client);
        client.Reset();

        await runtime.SendInteractiveInputAsync(Guid.NewGuid(), "x", CancellationToken.None, trackManualTurn: false);

        client.SessionGets.ShouldBe(1);
        client.BufferReads.ShouldBe(0);
        client.Inputs.ShouldBe(["x"]);
    }

    [Test]
    public async Task Card1761_Codex_turn_wait_takes_one_snapshot_and_no_ansi_tail()
    {
        var client = new CountingRunner { Raw = "codex ready\n", Screen = "idle\n" };
        var adapter = new RunnerCodexAdapter(client, Options.Create(new AgentRegistrySettings
        {
            CodexDoneQuietPeriodMs = 60_000,
            CodexDoneMaxWaitMs = 800,
        }));
        await adapter.StartAsync(Spec(AgentKind.Codex, "codex"), CancellationToken.None);
        client.Reset();

        var turn = await adapter.WaitForTurnCompleteAsync(CancellationToken.None);

        turn.TurnCompleted.ShouldBeFalse();
        client.TranscriptReads.ShouldBeGreaterThan(0);
        client.SnapshotReads.ShouldBe(client.TranscriptReads + 1);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Pty_output_wait_snapshots_only_when_the_sequence_moves()
    {
        var client = new CountingRunner { Backend = SessionBackends.PtyHost, Sequence = 4, Raw = "idle" };
        var terminal = new RunnerTerminalSession(client);
        await terminal.StartAsync(Spec(), CancellationToken.None);
        client.Reset();

        var matched = await terminal.WaitForOutputAsync(_ => false, TimeSpan.FromMilliseconds(250), CancellationToken.None);

        matched.ShouldBeFalse();
        client.SnapshotReads.ShouldBe(1);
        client.SessionGets.ShouldBeGreaterThan(1);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Herdr_output_wait_still_snapshots_every_poll()
    {
        var client = new CountingRunner { Backend = SessionBackends.Herdr, Sequence = 4, Raw = "pane" };
        var terminal = new RunnerTerminalSession(client);
        await terminal.StartAsync(Spec(), CancellationToken.None);
        client.Reset();

        var matched = await terminal.WaitForOutputAsync(_ => false, TimeSpan.FromMilliseconds(250), CancellationToken.None);

        matched.ShouldBeFalse();
        client.SnapshotReads.ShouldBeGreaterThan(2);
        // The exit watcher reads the session record. The output poll must not add a GET per frame.
        client.SessionGets.ShouldBeLessThan(client.SnapshotReads);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Unknown_backend_output_wait_still_snapshots_every_poll()
    {
        var client = new CountingRunner { Backend = null, Sequence = 4, Raw = "old-runner" };
        var terminal = new RunnerTerminalSession(client);
        await terminal.StartAsync(Spec(), CancellationToken.None);
        client.Reset();

        var matched = await terminal.WaitForOutputAsync(_ => false, TimeSpan.FromMilliseconds(250), CancellationToken.None);

        matched.ShouldBeFalse();
        client.SnapshotReads.ShouldBeGreaterThan(2);
        // The exit watcher reads the session record. The output poll must not add a GET per frame.
        client.SessionGets.ShouldBeLessThan(client.SnapshotReads);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Pty_output_wait_refreshes_the_frame_when_the_sequence_moves()
    {
        var client = new CountingRunner { Backend = SessionBackends.PtyHost, Sequence = 4, Raw = "old" };
        var terminal = new RunnerTerminalSession(client);
        await terminal.StartAsync(Spec(), CancellationToken.None);
        client.Reset();
        client.OnSessionGet = runner =>
        {
            if (runner.SessionGets == 3)
            {
                runner.Sequence = 9;
                runner.Raw = "new";
            }
        };

        var matched = await terminal.WaitForOutputAsync(
            text => text.Contains("new", StringComparison.Ordinal),
            TimeSpan.FromMilliseconds(400),
            CancellationToken.None);

        matched.ShouldBeTrue();
        client.SnapshotReads.ShouldBe(2);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Screen_only_live_snapshot_skips_the_ansi_tail()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner { Screen = "rendered", Raw = "raw", Ansi = "ANSI-TAIL" };
        var runtime = Runtime(client);

        var snapshot = await runtime.TryGetLiveSnapshotAsync(sessionId, CancellationToken.None);

        snapshot.ShouldNotBeNull();
        snapshot.RenderedScreen.ShouldBe("rendered");
        snapshot.RawOutput.ShouldBe("raw");
        snapshot.Buffer.ShouldBe(string.Empty);
        client.SnapshotReads.ShouldBe(1);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Dialog_read_still_asks_the_runner_for_a_snapshot()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner();
        var dialogs = new ProviderDialogObservationService(client);

        var observation = await dialogs.ReadAsync(sessionId, CancellationToken.None);

        observation.ShouldBeNull();
        client.SnapshotReads.ShouldBe(1);
        client.BufferReads.ShouldBe(0);
        client.SessionGets.ShouldBe(0);
    }

    private static AgentSessionRuntime Runtime(CountingRunner client)
    {
        var provider = new ServiceCollection().BuildServiceProvider();
        return new AgentSessionRuntime(
            client,
            new MockEventBus(),
            Options.Create(new AgentSessionSettings { SessionLogPath = Path.Combine(Path.GetTempPath(), "card1761-unused") }),
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            NullLogger<AgentSessionRuntime>.Instance);
    }

    private static AgentLaunchSpec Spec(AgentKind kind = AgentKind.ClaudeCode, string name = "claude") => new(
        DefinitionName: name,
        Kind: kind,
        Exe: name,
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 80,
        Rows: 24,
        SessionId: Guid.NewGuid());

    private sealed class CountingRunner : ISessionRunnerClient
    {
        public int SessionGets;
        public int BufferReads;
        public int SnapshotReads;
        public int TranscriptReads;
        public long Sequence = 1;
        public string? Backend = SessionBackends.PtyHost;
        public string Screen = "";
        public string Raw = "";
        public string Ansi = "";
        public Action<CountingRunner>? OnSessionGet;
        public List<string> Inputs { get; } = [];

        public void Reset()
        {
            SessionGets = 0;
            BufferReads = 0;
            SnapshotReads = 0;
            TranscriptReads = 0;
            Inputs.Clear();
        }

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            GetAsync(sessionId, ct);

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            SessionGets++;
            OnSessionGet?.Invoke(this);
            return Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, Sequence,
                Backend: Backend));
        }

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct)
        {
            BufferReads++;
            return Task.FromResult(new SessionRunnerBufferDto(sessionId, Ansi, Sequence));
        }

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
        {
            SnapshotReads++;
            return Task.FromResult(new SessionRunnerSnapshotDto(
                sessionId, Raw, Screen, Sequence, DateTime.UtcNow));
        }

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct)
        {
            TranscriptReads++;
            return Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], Sequence));
        }

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs.Add(input);
            return Task.CompletedTask;
        }

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            GetAsync(sessionId, ct);

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
