using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed class QueueRunnerAlarmNotifier(SessionMessageQueueService queue, CompletionNoteFlushQueue flush)
    : IRunnerAlarmNotifier
{
    public async Task<Guid> NotifyAsync(Guid sessionId, string header, string body, CancellationToken ct)
    {
        Guid rowId = Guid.Empty;
        await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct,
            QueuedMessageOrigin.System, noteHeader: header, deliverIfIdle: false, onCreated: created => rowId = created);
        flush.TryEnqueue(sessionId);
        return rowId;
    }
}
