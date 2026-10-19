using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Tests.TestHelpers;

/// <summary>
/// CARD-1362 D-6: a runner that never answers until the test says so. The four reads the runtime
/// bounds (<see cref="ListAsync"/>, <see cref="GetAsync"/>, <see cref="GetBufferAsync"/>,
/// <see cref="GetSnapshotAsync"/>) hand back <see cref="TaskCompletionSource"/> tasks, ignore the
/// token they receive (so the runtime's own deadline must be what gives up) and record it, so a
/// test can prove the deadline reached the client call. Release methods answer; <see cref="FaultAll"/>
/// fails every pending read.
/// </summary>
internal sealed class BlockingSessionRunnerClient : ISessionRunnerClient
{
    private readonly TaskCompletionSource<IReadOnlyList<SessionRunnerSessionDto>> _list =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SessionRunnerSessionDto> _get =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SessionRunnerBufferDto> _buffer =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<SessionRunnerSnapshotDto> _snapshot =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>How many times <see cref="ListAsync"/> was entered; read with <c>Volatile.Read</c>.</summary>
    public int ListCalls;

    /// <summary>How many times <see cref="GetSnapshotAsync"/> was entered; read with <c>Volatile.Read</c>.</summary>
    public int SnapshotCalls;

    /// <summary>The token the most recent <see cref="ListAsync"/> received.</summary>
    public CancellationToken LastListToken { get; private set; }

    /// <summary>The token the most recent <see cref="GetSnapshotAsync"/> received.</summary>
    public CancellationToken LastSnapshotToken { get; private set; }

    public void ReleaseList(IReadOnlyList<SessionRunnerSessionDto> sessions) => _list.TrySetResult(sessions);

    public void ReleaseGet(SessionRunnerSessionDto session) => _get.TrySetResult(session);

    public void ReleaseBuffer(SessionRunnerBufferDto buffer) => _buffer.TrySetResult(buffer);

    public void ReleaseSnapshot(SessionRunnerSnapshotDto snapshot) => _snapshot.TrySetResult(snapshot);

    public void FaultAll(Exception exception)
    {
        _list.TrySetException(exception);
        _get.TrySetException(exception);
        _buffer.TrySetException(exception);
        _snapshot.TrySetException(exception);
    }

    public Task<IReadOnlyList<SessionRunnerSessionDto>> ListAsync(CancellationToken ct)
    {
        LastListToken = ct;
        Interlocked.Increment(ref ListCalls);
        return _list.Task;
    }

    public Task<SessionRunnerSessionDto> GetAsync(Guid sessionId, CancellationToken ct) => _get.Task;

    public Task<SessionRunnerBufferDto> GetBufferAsync(Guid sessionId, CancellationToken ct) => _buffer.Task;

    public Task<SessionRunnerSnapshotDto> GetSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        LastSnapshotToken = ct;
        Interlocked.Increment(ref SnapshotCalls);
        return _snapshot.Task;
    }

    public Task<SessionRunnerTranscriptDto> GetTranscriptAsync(Guid sessionId, CancellationToken ct) =>
        Task.FromResult(new SessionRunnerTranscriptDto(sessionId, [], 0));

    public async IAsyncEnumerable<SessionRunnerEvent> StreamEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<SessionRunnerSessionDto> StartAsync(Guid sessionId, AgentLaunchSpec spec, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task SendInputAsync(Guid sessionId, string input, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ClearLiveBufferAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task ResizeAsync(Guid sessionId, int cols, int rows, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<SessionRunnerSessionDto> KillAsync(Guid sessionId, CancellationToken ct) =>
        throw new NotSupportedException();
}
