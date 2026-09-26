using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

public sealed class QueueRunnerAlarmNotifier(
    SessionMessageQueueService queue,
    CompletionNoteFlushQueue flush,
    ILogger<QueueRunnerAlarmNotifier> logger)
    : IRunnerAlarmNotifier
{
    public async Task<Guid> NotifyAsync(Guid sessionId, string header, string body, CancellationToken ct)
    {
        Guid rowId = Guid.Empty;
        try
        {
            await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.System, noteHeader: header, deliverIfIdle: false, onCreated: created => rowId = created);
        }
        catch (Exception ex) when (rowId != Guid.Empty)
        {
            logger.LogWarning(ex, "Alarm note {RowId} was saved for session {SessionId}; post-insert refresh failed", rowId, sessionId);
            flush.TryEnqueue(sessionId);
            return rowId;
        }

        flush.TryEnqueue(sessionId);
        return rowId;
    }
}
