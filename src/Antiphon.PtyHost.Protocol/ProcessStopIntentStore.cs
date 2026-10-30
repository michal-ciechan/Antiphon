using Antiphon.SessionRunner.Contracts;

namespace Antiphon.PtyHost.Protocol;

/// <summary>Compile-compatible S4 seam. Issuer and diagnostic implementation follows red witnesses.</summary>
public sealed class ProcessStopIntentStore(string root,
    Func<ProcessStopIntentDto, CancellationToken, Task<bool>>? writer = null,
    TimeProvider? clock = null, Action<string>? warning = null)
{
    public Guid BootId { get; } = Guid.NewGuid();
    public Task<bool> LastRecord { get; private set; } = Task.FromResult(false);
    public ProcessStopIntentDto? Snapshot(Guid sessionId, DateTime generation, int? signal = null) => null;
    public ProcessStopIntentDto Attempt(ProcessStopIntentDto request, Func<bool?> send)
    {
        send();
        return request;
    }
    public Task<bool> TryRecordAsync(ProcessStopIntentDto intent, CancellationToken ct = default) => Task.FromResult(false);
    public static string FileNameFor(ProcessStopIntentDto intent) => $"{intent.SessionId:N}-{intent.AcceptedStartedAt.Ticks}-{intent.RequestId:N}.json";
}
