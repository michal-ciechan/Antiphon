using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Agents;

/// <summary>
/// Pins the runner-path prompt delivery encoding (live miss 2026-08-08): RunnerTerminalSession
/// carried interactive/card boot prompts to the session runner RAW, so a CRLF prompt's carriage
/// returns hit the TUI unnormalized and the whole prompt stranded unsubmitted in the composer —
/// the agent "never came back" after its supervised restart. SendLineAsync must write the
/// PtyInputEncoding form (LF-normalized, bracket-wrapped when multi-line) followed by the
/// submitting CR as a SEPARATE write.
/// </summary>
public class RunnerTerminalSessionInputEncodingTests
{
    [Test]
    public async Task SendLineAsync_writes_encoded_body_then_separate_cr()
    {
        var client = new RecordingRunnerClient();
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(NewSpec(), CancellationToken.None);

        await session.SendLineAsync(
            "Work on card CARD-0001: the title\r\n\r\nDescription:\r\ndo the thing", CancellationToken.None);

        client.Inputs.Count.ShouldBe(2);
        client.Inputs[0].ShouldBe(
            "\x1b[200~Work on card CARD-0001: the title\n\nDescription:\ndo the thing\x1b[201~");
        client.Inputs[1].ShouldBe("\r");
    }

    [Test]
    public async Task SendLineAsync_leaves_single_line_prompts_unwrapped()
    {
        var client = new RecordingRunnerClient();
        var session = new RunnerTerminalSession(client);
        await session.StartAsync(NewSpec(), CancellationToken.None);

        await session.SendLineAsync("/rename Antiphon", CancellationToken.None);

        client.Inputs.ShouldBe(["/rename Antiphon", "\r"]);
    }

    private static AgentLaunchSpec NewSpec() => new(
        DefinitionName: "claude",
        Kind: AgentKind.ClaudeCode,
        Exe: "claude.exe",
        Args: [],
        Env: new Dictionary<string, string>(),
        Cwd: Path.GetTempPath(),
        Cols: 120,
        Rows: 30,
        SessionId: Guid.NewGuid());

    private sealed class RecordingRunnerClient : ISessionRunnerClient
    {
        public List<string> Inputs { get; } = [];

        public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 1234, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));

        public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);

        public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, 1234, DateTime.UtcNow, "Running", null, AgentExitReason.Unknown, 0));

        public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerBufferDto(sessionId, "", 0));

        public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));

        public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

        public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
        {
            Inputs.Add(input);
            return Task.CompletedTask;
        }

        public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

        public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
            Task.FromResult(new SessionRunnerSessionDto(
                sessionId, null, DateTime.UtcNow, "Exited", 0, AgentExitReason.KilledByRequest, 0));

        public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
