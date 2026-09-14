using Antiphon.Server.Application.Dtos;
namespace Antiphon.Server.Application.Interfaces;

public interface IWorktreeLockDiagnostics
{
    Task<WorktreeLockSnapshot> CaptureAsync(string root, CancellationToken ct);
}
