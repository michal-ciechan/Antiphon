using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
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

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(GetOverride?.Invoke(sessionId)
                ?? new SessionRunnerSessionDto(sessionId, 1, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) => Task.CompletedTask;
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
