using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// A runner inventory the recovery tests can list without starting a process.
/// Kill, start, release and compaction-stop are counted separately. Release does not
/// fall through to <see cref="KillAsync"/>.
/// </summary>
internal sealed class ListedInventoryRunner : ISessionRunnerClient
{
    public List<SessionRunnerSessionDto> Sessions { get; } = [];
    public int Lists { get; private set; }
    public int Starts { get; private set; }
    public int Kills { get; private set; }
    public int Releases { get; private set; }
    public int CompactionStops { get; private set; }
    public int Inputs { get; private set; }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        Lists++;
        return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>(Sessions.ToArray());
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct)
    {
        Starts++;
        return Task.FromResult(Session(sessionId, "Running"));
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(Sessions.FirstOrDefault(s => s.SessionId == sessionId) ?? Session(sessionId, "Running"));

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerBufferDto(sessionId, "> ", 0));

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerSnapshotDto(sessionId, "", "", 0, DateTime.UtcNow));

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct)
    {
        Inputs++;
        return Task.CompletedTask;
    }

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) => Task.CompletedTask;

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) => Task.CompletedTask;

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct)
    {
        Kills++;
        return Task.FromResult(Session(sessionId, "Exited"));
    }

    public Task<SessionRunnerSessionDto> ReleaseSlotAsync(Guid sessionId, string reason, CancellationToken ct)
    {
        Releases++;
        return Task.FromResult(Session(sessionId, "Exited"));
    }

    public Task<CompactionContinuationStopResult> StopCompactionContinuationAsync(
        Guid sessionId, CompactionContinuationStopRequest request, CancellationToken ct)
    {
        CompactionStops++;
        return Task.FromResult(new CompactionContinuationStopResult(
            sessionId, request.AttemptId, false, CompactionStopOutcomes.Unsupported, null));
    }

    public IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(CancellationToken ct) => Empty();

    private static async IAsyncEnumerable<SessionRunnerEvent> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static SessionRunnerSessionDto Session(Guid sessionId, string status) =>
        new(sessionId, Pid: 1, StartedAt: DateTime.UtcNow, Status: status, ExitCode: null,
            ExitReason: AgentExitReason.Unknown, LastSequence: 0);
}
