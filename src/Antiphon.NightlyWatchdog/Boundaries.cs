namespace Antiphon.NightlyWatchdog;

/// <summary>D-3 probes. Every member is one bounded HTTP call; failures are classified, never thrown as success.</summary>
public interface IWindmillApi
{
    Task<WindmillCall<string>> GetVersionAsync(CancellationToken ct);
    Task<WindmillCall<bool>> WhoAmIAsync(CancellationToken ct);
    Task<WindmillCall<ScriptInfo>> GetScriptAsync(CancellationToken ct);
    Task<WindmillCall<ScheduleInfo>> GetScheduleAsync(CancellationToken ct);
    Task<WindmillCall<IReadOnlyList<WorkerPing>>> ListWorkersAsync(CancellationToken ct);
    Task<WindmillCall<IReadOnlyList<WindmillJob>>> ListJobsAsync(CancellationToken ct);
    Task<WindmillCall<System.Text.Json.Nodes.JsonObject?>> GetCompletedResultAsync(string jobId, CancellationToken ct);
    Task<WindmillCall<string>> GetLogsAsync(string jobId, CancellationToken ct);
}

/// <summary>D-5 transport. Tier-1 acceptance only; never evidence of receipt.</summary>
public interface INotificationTransport
{
    Task<TransportResult> SendAsync(OutboundMessage message, CancellationToken ct);
}

/// <summary>D-6 recipient-side reader of the authorized destination's history. Never sends.</summary>
public interface IRecipientReader
{
    Task<ReaderReadResult> ReadAsync(DateTime floorUtc, CancellationToken ct);
}
