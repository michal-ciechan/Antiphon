using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Exact settled-report custody. Implementations never trust existence alone.</summary>
public interface IAgentReportStore
{
    Task<AgentReportStorageResult> StoreAsync(AgentTask task, CancellationToken ct);
    Task<bool> IsUsableAsync(string? path, string raw, CancellationToken ct);
}

public sealed record AgentReportStorageResult(string? Path, string? Reason)
{
    public bool Succeeded => Path is not null;
}
