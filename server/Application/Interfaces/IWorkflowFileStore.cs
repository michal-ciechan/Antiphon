using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Application.Interfaces;

public interface IWorkflowFileStore
{
    string? GetWorkflowFilePath(Board board);

    Task<string?> ReadAsync(Board board, CancellationToken ct);

    Task WriteAsync(Board board, string content, CancellationToken ct);
}
