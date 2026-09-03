using System.Threading.Channels;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// One diagnose request handed from create (job 1) or the card sweep (job 2) to the serial
/// drainer (CARD-0352 S3). Same shape as <see cref="AgentTaskCheckQueue"/>: unbounded so the
/// producer never blocks, single reader because the seat answers one turn at a time.
/// </summary>
public sealed class DiagnoseQueue
{
    private readonly Channel<DiagnoseRequest> _channel = Channel.CreateUnbounded<DiagnoseRequest>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>Hand a request to the worker. Never blocks.</summary>
    public bool TryEnqueue(DiagnoseRequest request) => _channel.Writer.TryWrite(request);

    /// <summary>Requests in arrival order, until the token is cancelled.</summary>
    public IAsyncEnumerable<DiagnoseRequest> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);

    /// <summary>How many requests are waiting — for tests. Unbounded readers may not support Count.</summary>
    public int PendingCount => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <summary>Take one request without waiting; false when the queue is empty (tests).</summary>
    public bool TryDequeue(out DiagnoseRequest request) => _channel.Reader.TryRead(out request!);
}

/// <summary>One diagnose job. Title requests carry <see cref="TaskId"/>; card requests carry <see cref="CardId"/>.</summary>
public sealed record DiagnoseRequest(DiagnosisKind Kind, Guid? TaskId, Guid? CardId, bool Forced)
{
    public static DiagnoseRequest ForTitle(Guid taskId) =>
        new(DiagnosisKind.Title, taskId, null, false);

    public static DiagnoseRequest ForCard(Guid cardId, bool force = false) =>
        new(DiagnosisKind.Labels, null, cardId, force);
}
