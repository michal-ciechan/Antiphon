using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public class RunnerTerminalSessionGenerationTests
{
    [Test]
    public async Task Cleanup_after_a_Start_that_threw_kills_conditionally_on_the_captured_generation()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var client = new RecordingClient { ThrowOnStart = true };
        var session = new RunnerTerminalSession(client);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            session.StartAsync(Spec(sessionId, generation), CancellationToken.None));

        (await session.KillGenerationAsync(generation, CancellationToken.None)).ShouldBeTrue();
        client.KillGenerationCalls.ShouldBe([(sessionId, generation)]);
        client.KillCalls.ShouldBeEmpty();
    }

    [Test]
    public async Task An_exit_waiter_finishes_as_superseded_when_GET_names_another_generation()
    {
        var sessionId = Guid.NewGuid();
        var generationA = SessionGeneration.Normalize(DateTime.UtcNow);
        var generationB = SessionGeneration.Next(generationA, generationA.AddMinutes(1));
        var client = new RecordingClient
        {
            StartDto = new SessionRunnerSessionDto(
                sessionId, 1, generationA, "Running", null, AgentExitReason.Unknown, 0,
                AcceptedStartedAt: generationA),
        };
        var current = generationA;
        var exitCode = 0;
        client.GetOverride = _ => new SessionRunnerSessionDto(
            sessionId, 1, current, current == generationB && exitCode != 0 ? "Exited" : "Running",
            exitCode == 0 ? null : exitCode, AgentExitReason.KilledByRequest, 0,
            AcceptedStartedAt: current);

        var session = new RunnerTerminalSession(client);
        await session.StartAsync(Spec(sessionId, generationA), CancellationToken.None);
        var waiter = session.Exited;

        current = generationB;
        var result = await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        result.ShouldNotBe(42);

        exitCode = 42;
        current = generationB;
        result.ShouldNotBe(42);
    }

    [Test]
    public async Task C514_Every_maintenance_phase_is_guarded_and_stops_on_mismatch()
    {
        var sessionId = Guid.NewGuid();
        var generation = SessionGeneration.Normalize(DateTime.UtcNow);
        var client = new RecordingClient
        {
            StartDto = new SessionRunnerSessionDto(
                sessionId, 1, generation, "Running", null, AgentExitReason.Unknown, 0,
                AcceptedStartedAt: generation),
        };
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(Spec(sessionId, generation), CancellationToken.None);
        var body = await session.WriteConditionalAsync(
            new RunnerConditionalInputRequest(generation, 0, "/remote-control"), CancellationToken.None);
        body.Outcome.ShouldBe(ConditionalInputOutcomes.Written);
        var mismatch = await session.WriteConditionalAsync(
            new RunnerConditionalInputRequest(generation.AddTicks(SessionGeneration.MicrosecondTicks), 0, "\r"),
            CancellationToken.None);
        mismatch.Outcome.ShouldBe(ConditionalInputOutcomes.GenerationMismatch);
        client.RawInputs.ShouldBeEmpty();
        client.ConditionalInputs.ShouldBe(["/remote-control"]);
    }

    // CARD-0679 R5 repair 2 (review 18f52a40): a remote adapter follows the runner's current connection,
    // so the exit watcher of an adapter the launch loop already released kept polling Get through
    // the replacement connection for the life of the session.
    [Test]
    [Arguments(AgentKind.Raw)]
    [Arguments(AgentKind.ClaudeCode)]
    [Arguments(AgentKind.Codex)]
    [Arguments(AgentKind.OpenCode)]
    [Arguments(AgentKind.Grok)]
    public async Task A_disposed_adapter_stops_its_exit_watcher(AgentKind kind)
    {
        var sessionId = Guid.NewGuid();
        var client = new RecordingClient();
        var adapter = new AgentProtocolAdapterFactory(Options.Create(new AgentRegistrySettings()), client).Create(kind);
        await ((IAttachableProtocolAdapter)adapter).AttachAsync(sessionId, CancellationToken.None);
        // The attach's own Get, then the watcher's polls: the watcher is running.
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (client.GetCalls < 3 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        client.GetCalls.ShouldBeGreaterThanOrEqualTo(3, "the exit watcher polls Get while the adapter is live");

        await adapter.DisposeAsync();
        await Task.Delay(100);
        var afterDispose = client.GetCalls;
        // Four of the watcher's 250 ms poll intervals.
        await Task.Delay(1000);

        client.GetCalls.ShouldBe(afterDispose, "a disposed adapter's exit watcher sends no further Get");
        adapter.Exited.IsCompleted.ShouldBeTrue("the watcher ends when its adapter is disposed");
    }

    private static AgentLaunchSpec Spec(Guid sessionId, DateTime generation) =>
        new("fake", AgentKind.ClaudeCode, "cmd", [], new Dictionary<string, string>(), Path.GetTempPath(), 120, 30,
            SessionId: sessionId, AcceptedStartedAt: generation);

    private sealed class RecordingClient : ISessionRunnerClient
    {
        public bool ThrowOnStart { get; set; }
        public SessionRunnerSessionDto? StartDto { get; set; }
        public Func<Guid, SessionRunnerSessionDto>? GetOverride { get; set; }
        public List<(Guid SessionId, DateTime Expected)> KillGenerationCalls { get; } = [];
        public List<Guid> KillCalls { get; } = [];
        public List<string> ConditionalInputs { get; } = [];
        public List<string> RawInputs { get; } = [];
        public DateTime? BoundGeneration { get; set; }
        private int _getCalls;
        public int GetCalls => Volatile.Read(ref _getCalls);

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        {
            if (ThrowOnStart)
                throw new InvalidOperationException("start failed");
            return Task.FromResult(StartDto ?? new SessionRunnerSessionDto(
                sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0,
                AcceptedStartedAt: spec.AcceptedStartedAt));
        }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct)
        {
            Interlocked.Increment(ref _getCalls);
            return Task.FromResult(GetOverride?.Invoke(sessionId)
                ?? new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));
        }

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            RawInputs.Add(input);
            return Task.CompletedTask;
        }

        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(
            Guid sessionId, RunnerConditionalInputRequest request, CancellationToken ct)
        {
            var bound = BoundGeneration ?? StartDto?.AcceptedStartedAt;
            if (bound is { } g && !SessionGeneration.Equal(g, request.ExpectedAcceptedStartedAt))
            {
                return Task.FromResult(new RunnerConditionalInputResult(
                    sessionId, ConditionalInputOutcomes.GenerationMismatch, g, 0));
            }

            ConditionalInputs.Add(request.Input);
            return Task.FromResult(new RunnerConditionalInputResult(
                sessionId, ConditionalInputOutcomes.Written, request.ExpectedAcceptedStartedAt,
                request.ExpectedLastSequence));
        }
        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;
        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
        {
            KillCalls.Add(sessionId);
            return Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));
        }

        public Task<RunnerKillGenerationResult> KillGenerationAsync(
            Guid sessionId, DateTime expectedAcceptedStartedAt, CancellationToken ct)
        {
            KillGenerationCalls.Add((sessionId, expectedAcceptedStartedAt));
            return Task.FromResult(new RunnerKillGenerationResult(
                sessionId, true, KillGenerationOutcomes.Killed, expectedAcceptedStartedAt));
        }

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
