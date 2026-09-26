namespace Antiphon.Server.Application.Interfaces;

/// <summary>CARD-0726 D-7. Returns the queued row id after the insert commits.</summary>
public interface IRunnerAlarmNotifier
{
    Task<Guid> NotifyAsync(Guid sessionId, string header, string body, CancellationToken ct);
}
