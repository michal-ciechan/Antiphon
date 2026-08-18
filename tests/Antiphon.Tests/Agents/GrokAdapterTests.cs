using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.Pty;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

[Category("Unit")]
public sealed class GrokAdapterTests
{
    [Test]
    public async Task Factory_returns_RunnerGrokAdapter_for_Grok()
    {
        var factory = new AgentProtocolAdapterFactory(
            Options.Create(new AgentRegistrySettings()),
            new ThrowingSessionRunnerClient());
        await using var adapter = factory.Create(AgentKind.Grok);
        adapter.ShouldBeOfType<RunnerGrokAdapter>();
    }

    [Test]
    public void Session_runner_request_enables_the_grok_transcript_for_Grok()
    {
        // CARD-0080 S2: Grok sessions tail their ACP updates.jsonl. The format travels with the
        // request so the runner picks GrokTranscriptTailer instead of the Claude discovery tailer.
        SessionRunnerHttpClient.TranscriptEnabledFor(AgentKind.Grok).ShouldBeTrue();
        SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.Grok)
            .ShouldBe(Antiphon.SessionRunner.Contracts.TranscriptFormats.Grok);

        // Claude keeps its pre-Grok shape: enabled, NULL format — an old runner in front of a new
        // server must keep doing exactly what it already did.
        SessionRunnerHttpClient.TranscriptEnabledFor(AgentKind.ClaudeCode).ShouldBeTrue();
        SessionRunnerHttpClient.TranscriptFormatFor(AgentKind.ClaudeCode).ShouldBeNull();

        // No structured transcript exists for the others.
        SessionRunnerHttpClient.TranscriptEnabledFor(AgentKind.Codex).ShouldBeFalse();
    }

    private sealed class ThrowingSessionRunnerClient : ISessionRunnerClient
    {
        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
