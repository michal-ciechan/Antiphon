using Antiphon.Server.Application.Dtos;
namespace Antiphon.Server.Application.Interfaces;

public sealed record WorktreeProbeTarget(string Root, string CommonDirectory, string GitDirectory);

public interface IWorktreeDeleteAccessProbe
{
    Task<WorktreeNativeSnapshot> ObserveAsync(WorktreeProbeTarget target,
        IReadOnlyList<WorktreeLockOwner> owners, CancellationToken ct);
}
