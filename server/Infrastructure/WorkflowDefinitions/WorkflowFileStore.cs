using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;

namespace Antiphon.Server.Infrastructure.WorkflowDefinitions;

public sealed class WorkflowFileStore : IWorkflowFileStore
{
    public string? GetWorkflowFilePath(Board board)
    {
        if (string.IsNullOrWhiteSpace(board.Project.LocalRepositoryPath))
            return null;

        return Path.GetFullPath(Path.Combine(
            board.Project.LocalRepositoryPath,
            ".antiphon",
            "boards",
            board.Id.ToString("N"),
            WorkflowDefinitionLoader.WorkflowFileName));
    }

    public async Task<string?> ReadAsync(Board board, CancellationToken ct)
    {
        var path = GetWorkflowFilePath(board);
        if (path is null || !File.Exists(path))
            return null;

        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task WriteAsync(Board board, string content, CancellationToken ct)
    {
        var path = GetWorkflowFilePath(board);
        if (path is null)
        {
            throw new ValidationException(
                nameof(board.Project.LocalRepositoryPath),
                "Project local repository path is required to save WORKFLOW.md.");
        }

        var repositoryPath = Path.GetFullPath(board.Project.LocalRepositoryPath!);
        if (!Directory.Exists(repositoryPath))
        {
            throw new ValidationException(
                nameof(board.Project.LocalRepositoryPath),
                "Project local repository path does not exist.");
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
            throw new ValidationException("workflow", "Workflow file path is invalid.");

        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(path, content, ct);
    }
}
