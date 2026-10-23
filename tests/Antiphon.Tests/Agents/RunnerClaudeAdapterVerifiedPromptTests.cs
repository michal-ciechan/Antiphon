using Antiphon.Agents.Pty;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Pins verified boot-prompt delivery on the production (runner-client) Claude adapter — the path
/// that sent tonight's card prompt blind (live miss 2026-08-08). SendPromptAsync must require
/// composer evidence before pressing Enter, and a composer that never renders the body must fail
/// the launch loudly with the Enter withheld, instead of stranding the prompt silently.
/// </summary>
[Category("Unit")]
public class RunnerClaudeAdapterVerifiedPromptTests
{
    [Test]
    public async Task Prompt_is_delivered_as_encoded_body_then_separate_enter_when_composer_echoes()
    {
        var client = new ComposerSimulatingRunnerClient(echoToScreen: true);
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await adapter.SendPromptAsync("Work on card CARD-0001\r\nDescription:\r\ndo it", CancellationToken.None);

        client.Inputs.Count.ShouldBe(2);
        client.Inputs[0].ShouldBe("\x1b[200~Work on card CARD-0001\nDescription:\ndo it\x1b[201~");
        client.Inputs[1].ShouldBe("\r");
    }

    [Test]
    public async Task Dead_composer_fails_the_prompt_send_with_enter_withheld()
    {
        var client = new ComposerSimulatingRunnerClient(echoToScreen: false);
        await using var adapter = NewAdapter(client);
        await adapter.StartAsync(NewSpec(), CancellationToken.None);

        await Should.ThrowAsync<PromptDeliveryException>(() =>
            adapter.SendPromptAsync("this prompt never renders", CancellationToken.None));

        client.Inputs.ShouldNotContain("\r", "the Enter must be withheld from a dead composer");
    }

    private static RunnerClaudeAdapter NewAdapter(ISessionRunnerClient client) => new(
        client,
        Options.Create(new AgentRegistrySettings()),
        Options.Create(new SupervisionSettings
        {
            DeliveryVerification = new DeliveryVerificationSettings
            {
                Enabled = true,
                EvidenceTimeoutSeconds = 1,
                PollIntervalMs = 10,
                PostSubmitAdvanceTimeoutSeconds = 1,
            },
        }));

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "claude",
        Kind: AgentKind.ClaudeCode,
        Exe: "claude.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid(), AcceptedStartedAt: DateTime.UnixEpoch);

    /// <summary>
    /// Runner client with a minimal composer model: typed input (except the CR) shows up on the
    /// rendered screen when echoing is on, and every input advances the output sequence.
    /// </summary>
    private sealed class ComposerSimulatingRunnerClient(bool echoToScreen) : ISessionRunnerClient
    {
        private string _screen = "";
        private readonly ScriptedClaudeInputGuard _guard = new();
        private readonly DateTime _startedAt = DateTime.UtcNow;
        private long _sequence;

        public List<string> Inputs { get; } = [];

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
        { _guard.Bind(spec.AcceptedStartedAt); return GetAsync(sessionId, ct); }

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 1234, _startedAt, "Running", null, AgentExitReason.Unknown, _sequence,
                AcceptedStartedAt: _guard.AcceptedStartedAt));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", _sequence));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
        {
            var rendered = ScriptedClaudeInputGuard.Composer(_screen);
            return Task.FromResult(new SessionRunnerSnapshotDto(sessionId, rendered, rendered, _sequence, _startedAt,
                _guard.AcceptedStartedAt, _guard.Observe(sessionId, _sequence, rendered)));
        }

        public Task<RunnerConditionalInputResult> SendConditionalInputAsync(Guid sessionId,
            RunnerConditionalInputRequest request, CancellationToken ct) =>
            _guard.WriteAsync(sessionId, request, _sequence, ScriptedClaudeInputGuard.Composer(_screen),
                (input, token) => SendInputAsync(sessionId, input, token), ct);

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs.Add(input);
            _sequence++;
            if (input != "\r" && echoToScreen)
                _screen += input;
            return Task.CompletedTask;
        }

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, _sequence));

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
