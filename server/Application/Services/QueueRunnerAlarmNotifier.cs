using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

public sealed class QueueRunnerAlarmNotifier(SessionMessageQueueService queue, CompletionNoteFlushQueue flush)
    : IRunnerAlarmNotifier
{
    public Task<Guid> NotifyAsync(Guid sessionId, string header, string body, CancellationToken ct)
    {
        _ = (queue, flush, sessionId, header, body, ct);
        return Task.FromResult(Guid.Empty);
    }
}
