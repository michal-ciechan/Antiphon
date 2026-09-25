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

    /// <summary>
    /// CARD-0679 (review 87af1bf6): remote sessions whose liveness is unknown, neither confirmed
    /// live nor confirmed gone: the last recovered connection's inventory entries that nothing has
    /// confirmed within the stale bound, and all of them while that connection is recovering,
    /// lease-expired or closed and no newer connection's catch-up List has answered. Only such a
    /// List (from a connected runner) or an exit/kill confirms a session gone. Never an RPC.
    /// Before any connection has answered in this process (<see cref="RemoteInventoryPending"/>),
    /// the sessions the desktop's own record binds to that runner and has not seen end (review
    /// f87b49a7), so a gate that tests membership sees them too.
    /// </summary>
    IReadOnlyCollection<Guid> UnknownRemoteSessionIds() => [];

    /// <summary>
    /// CARD-0679 (review 87af1bf6): true while <paramref name="runnerId"/> is a remote runner no
    /// connection has yet answered for in this process (a desktop restart before the runner is
    /// back): every session bound to it is unknown, since nothing has listed them either way.
    /// </summary>
    bool RemoteInventoryPending(string? runnerId) => false;
}
