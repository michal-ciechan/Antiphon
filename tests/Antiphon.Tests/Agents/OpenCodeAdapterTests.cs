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
public sealed class OpenCodeAdapterTests
{
    [Test]
    [Arguments("Run a\u001b[1;6Hbc?\r\nDone.")]
    [Arguments("Run \u001b[1Cabc?\r\nDone.")]
    public async Task Cursor_moves_in_echo_keep_the_plain_stripping_verdict(string raw)
    {
        var turn = await ReadTurn("Run abc?", raw);
        turn.ResponseText.ShouldBe("Done.", "C907-D1-opencode-echo");
        turn.IsAskingQuestion.ShouldBeFalse("C907-D1-opencode-question");
        turn.TurnCompleted.ShouldBeTrue();
        turn.RawSnapshot.ShouldBe(raw);
    }

    [Test]
    public async Task Cursor_moves_in_answer_keep_plain_stripping()
    {
        const string raw = "Run\r\nAl\u001b[1;3Hpha\u001b[1Cbeta?";
        var turn = await ReadTurn("Run", raw);
        turn.ResponseText.ShouldBe("Alphabeta?", "C907-D1-opencode-answer");
        turn.IsAskingQuestion.ShouldBeTrue();
        turn.TurnCompleted.ShouldBeTrue();
        turn.RawSnapshot.ShouldBe(raw);
    }

    private static async Task<AgentTurnResult> ReadTurn(string prompt, string raw)
    {
        var client = new ScriptedCodexRunnerClient { RawOutput = raw };
        await using var adapter = new RunnerOpenCodeAdapter(client, Options.Create(new AgentRegistrySettings
        {
            OpenCodeDoneQuietPeriodMs = 0,
            OpenCodeDoneMaxWaitMs = 15_000,
        }));
        await adapter.StartAsync(new AgentLaunchSpec(
            DefinitionName: "opencode", Kind: AgentKind.OpenCode, Exe: "unused",
            Args: [], Env: new Dictionary<string, string>(), Cwd: Path.GetTempPath(),
            Cols: 120, Rows: 30, SessionId: Guid.NewGuid()), CancellationToken.None);
        await adapter.SendPromptAsync(prompt, CancellationToken.None);
        return await adapter.WaitForTurnCompleteAsync(CancellationToken.None);
    }

    [Test]
    public async Task Factory_returns_RunnerOpenCodeAdapter_for_OpenCode()
    {
        var factory = new AgentProtocolAdapterFactory(
            Options.Create(new AgentRegistrySettings()),
            new ThrowingSessionRunnerClient());
        await using var adapter = factory.Create(AgentKind.OpenCode);
        adapter.ShouldBeOfType<RunnerOpenCodeAdapter>();
    }

    [Test]
    public void Session_runner_request_keeps_transcript_disabled_for_OpenCode()
    {
        var kind = AgentKind.OpenCode;
        var transcriptEnabled = kind == AgentKind.ClaudeCode;
        transcriptEnabled.ShouldBeFalse();
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
