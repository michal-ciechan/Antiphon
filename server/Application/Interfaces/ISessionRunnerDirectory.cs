using Antiphon.Server.Application.Dtos;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Interfaces;

public sealed record SessionRunnerOwner(string RunnerId, Guid RunnerStoreId, string RunnerCwd);

public abstract record RunnerInventory
{
    public sealed record Available(IReadOnlyList<SessionRunnerSessionDto> Sessions) : RunnerInventory;
    public sealed record Unavailable(string Reason) : RunnerInventory;
}

public abstract record SessionRunnerBinding
{
    public sealed record Missing : SessionRunnerBinding
    {
        public static readonly Missing Instance = new();
    }

    public sealed record Local : SessionRunnerBinding
    {
        public static readonly Local Instance = new();
    }

    public sealed record Remote(SessionRunnerOwner Owner) : SessionRunnerBinding;
}

public interface ISessionRunnerDirectory
{
    ISessionRunnerClient Local { get; }
    ISessionRunnerClient Resolve(string? runnerId);
    Task<SessionRunnerOwner?> GetOwnerAsync(Guid sessionId, CancellationToken ct);
    Task<SessionRunnerBinding> GetBindingAsync(Guid sessionId, CancellationToken ct);
    Task<RunnerInventory> GetInventoryAsync(string? runnerId, CancellationToken ct);
    IReadOnlyList<string> KnownRunnerIds { get; }
    Guid? LiveStoreId { get; }

    /// <summary>
    /// CARD-0653: seats the connected runner declared, or null when this directory cannot say.
    /// A null answer does not hold; an unavailable runner is a separate gate.
    /// </summary>
    int? DeclaredCapacity(string runnerId) => null;

    /// <summary>
    /// CARD-0679 D-10: sessions a connected remote runner holds live, from the connection's cached
    /// inventory. Never an RPC; empty when no remote runner is connected and recovered.
    /// </summary>
    IReadOnlyCollection<Guid> LiveRemoteSessionIds() => [];
}
