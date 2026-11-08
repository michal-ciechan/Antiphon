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
/// CARD-1766: those backends read sequence from the buffer route. Each poll reads the runner;
/// a flat session record is not "nothing arrived" while the buffer sequence is still moving.
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
    public async Task Card1761_Herdr_output_wait_reads_each_frame_without_a_session_get()
    {
        var (client, terminal) = await StartedNonPty(SessionBackends.Herdr, "one");

        var matched = await terminal.WaitForOutputAsync(
            text => text.Contains("two", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        matched.ShouldBeTrue();
        client.SnapshotReads.ShouldBe(2);
        client.SessionGets.ShouldBe(0);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Unknown_backend_output_wait_reads_each_frame_without_a_session_get()
    {
        var (client, terminal) = await StartedNonPty(backend: null, "one");

        var matched = await terminal.WaitForOutputAsync(
            text => text.Contains("two", StringComparison.Ordinal),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        matched.ShouldBeTrue();
        client.SnapshotReads.ShouldBe(2);
        client.SessionGets.ShouldBe(0);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1766_Herdr_sequence_poll_reads_the_buffer_each_time()
    {
        var client = new CountingRunner { Backend = SessionBackends.Herdr, Sequence = 4, BufferSequence = 9 };
        var terminal = await Started(client);

        var first = await terminal.GetLastSequenceAsync(CancellationToken.None);
        client.BufferSequence = 12;
        var second = await terminal.GetLastSequenceAsync(CancellationToken.None);

        first.ShouldBe(9);
        second.ShouldBe(12);
        client.BufferReads.ShouldBe(2);
        client.SessionGets.ShouldBe(0);
    }

    [Test]
    public async Task Card1766_Unknown_backend_sequence_poll_reads_the_buffer()
    {
        var client = new CountingRunner { Backend = null, Sequence = 4, BufferSequence = 8 };
        var terminal = await Started(client);

        var sequence = await terminal.GetLastSequenceAsync(CancellationToken.None);

        sequence.ShouldBe(8);
        client.BufferReads.ShouldBe(1);
        client.SessionGets.ShouldBe(0);
    }

    [Test]
    public async Task Card1766_Herdr_quiet_follows_the_buffer_sequence_not_a_flat_session_record()
    {
        var client = new CountingRunner { Backend = SessionBackends.Herdr, Sequence = 10, BufferSequence = 10 };
        client.OnBufferRead = runner =>
        {
            if (runner.BufferReads <= 3)
                runner.BufferSequence = 10 + runner.BufferReads;
        };
        var terminal = await Started(client);

        var quiet = await terminal.WaitForQuietAsync(
            TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(2), CancellationToken.None);

        quiet.ShouldBeTrue();
        client.BufferReads.ShouldBeGreaterThan(3);
        client.SessionGets.ShouldBe(0);
    }

    [Test]
    public async Task Card1766_Listed_herdr_manual_turn_reads_the_buffer_not_the_session()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner
        {
            Listed = [ListedSession(sessionId, SessionBackends.Herdr)],
            Sequence = 4,
            BufferSequence = 9,
        };
        var runtime = Runtime(client);

        await runtime.SendInteractiveInputAsync(sessionId, "x", CancellationToken.None, trackManualTurn: false);

        client.BufferReads.ShouldBe(1);
        client.SessionGets.ShouldBe(0);
        client.Inputs.ShouldBe(["x"]);
    }

    [Test]
    public async Task Card1766_Listed_unknown_backend_manual_turn_reads_the_buffer_not_the_session()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner
        {
            Listed = [ListedSession(sessionId, backend: null)],
            Sequence = 4,
            BufferSequence = 6,
        };
        var runtime = Runtime(client);

        await runtime.SendInteractiveInputAsync(sessionId, "y", CancellationToken.None, trackManualTurn: false);

        client.BufferReads.ShouldBe(1);
        client.SessionGets.ShouldBe(0);
        client.Inputs.ShouldBe(["y"]);
    }

    [Test]
    public async Task Card1766_Listed_pty_manual_turn_still_reads_the_session_record()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner
        {
            Listed = [ListedSession(sessionId, SessionBackends.PtyHost)],
            Sequence = 4,
            BufferSequence = 9,
        };
        var runtime = Runtime(client);

        await runtime.SendInteractiveInputAsync(sessionId, "z", CancellationToken.None, trackManualTurn: false);

        client.SessionGets.ShouldBe(1);
        client.BufferReads.ShouldBe(0);
        client.Inputs.ShouldBe(["z"]);
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
    public async Task Card1761_Empty_screen_live_snapshot_still_reads_the_ansi_tail()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner { Screen = " \t", Raw = "raw", Ansi = "ANSI-TAIL" };
        var runtime = Runtime(client);

        var snapshot = await runtime.TryGetLiveSnapshotAsync(
            sessionId, CancellationToken.None, LiveAnsiTail.WhenScreenEmpty);

        snapshot.ShouldNotBeNull();
        snapshot.Buffer.ShouldBe("ANSI-TAIL");
        client.SnapshotReads.ShouldBe(1);
        client.BufferReads.ShouldBe(1);
    }

    [Test]
    public async Task Card1761_Occupied_screen_live_snapshot_skips_the_ansi_tail()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner { Screen = "rendered", Raw = "raw", Ansi = "ANSI-TAIL" };
        var runtime = Runtime(client);

        var snapshot = await runtime.TryGetLiveSnapshotAsync(
            sessionId, CancellationToken.None, LiveAnsiTail.WhenScreenEmpty);

        snapshot.ShouldNotBeNull();
        snapshot.RenderedScreen.ShouldBe("rendered");
        snapshot.Buffer.ShouldBe(string.Empty);
        client.SnapshotReads.ShouldBe(1);
        client.BufferReads.ShouldBe(0);
    }

    [Test]
    public async Task Card1761_Included_live_snapshot_reads_the_ansi_tail()
    {
        var sessionId = Guid.NewGuid();
        var client = new CountingRunner { Screen = "rendered", Raw = "raw", Ansi = "ANSI-TAIL" };
        var runtime = Runtime(client);

        var snapshot = await runtime.TryGetLiveSnapshotAsync(
            sessionId, CancellationToken.None, LiveAnsiTail.Include);

        snapshot.ShouldNotBeNull();
        snapshot.Buffer.ShouldBe("ANSI-TAIL");
        client.SnapshotReads.ShouldBe(1);
        client.BufferReads.ShouldBe(1);
    }

    [Test]
    public async Task Card1761_Watchdog_asks_for_the_tail_only_when_the_screen_is_empty()
    {
        var text = await File.ReadAllTextAsync(RepoFile("server/Application/Services/WatchdogService.cs"));

        text.ShouldContain("TryGetLiveSnapshotAsync(sessionId, ct, LiveAnsiTail.WhenScreenEmpty)");
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

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(relative);
    }

    private static async Task<(CountingRunner Client, RunnerTerminalSession Terminal)> StartedNonPty(
        string? backend, string firstFrame)
    {
        var client = new CountingRunner { Backend = backend, Sequence = 4, Raw = firstFrame };
        client.OnSnapshot = runner =>
        {
            if (runner.SnapshotReads == 2)
                runner.Raw = "two";
        };
        return (client, await Started(client));
    }

    private static async Task<RunnerTerminalSession> Started(CountingRunner client)
    {
        var terminal = new RunnerTerminalSession(client);
        await terminal.StartAsync(Spec(), CancellationToken.None);
        terminal.StopWatching();
        client.Reset();
        return terminal;
    }

    private static SessionRunnerSessionDto ListedSession(Guid sessionId, string? backend) => new(
        sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 1, Backend: backend);

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
        public long? BufferSequence;
        public string? Backend = SessionBackends.PtyHost;
        public string Screen = "";
        public string Raw = "";
        public string Ansi = "";
        public Action<CountingRunner>? OnSessionGet;
        public Action<CountingRunner>? OnSnapshot;
        public Action<CountingRunner>? OnBufferRead;
        public IReadOnlyList<SessionRunnerSessionDto> Listed { get; set; } = [];
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
            Task.FromResult(Listed);

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
            OnBufferRead?.Invoke(this);
            return Task.FromResult(new SessionRunnerBufferDto(sessionId, Ansi, BufferSequence ?? Sequence));
        }

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
        {
            SnapshotReads++;
            OnSnapshot?.Invoke(this);
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
