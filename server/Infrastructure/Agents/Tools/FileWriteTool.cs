using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Writes content to a file in the worktree. Creates directories as needed.
/// Path-scoped to prevent traversal (NFR8).
/// </summary>
public sealed class FileWriteTool : IAgentTool
{
    private readonly string _worktreeRoot;

    public FileWriteTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "file_write";

    public string Description => "Writes content to a file at the given path relative to the worktree root. Creates the file and parent directories if they do not exist.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path to the file within the worktree." },
            "content": { "type": "string", "description": "Content to write to the file." }
          },
          "required": ["path", "content"]
        }
        """;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonInput);
        var path = doc.RootElement.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter.");
        var content = doc.RootElement.GetProperty("content").GetString()
            ?? throw new ArgumentException("Missing 'content' parameter.");

        var resolved = PathGuard.Resolve(_worktreeRoot, path);

        var directory = Path.GetDirectoryName(resolved);
        if (directory is not null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(resolved, content, ct);
        return $"Successfully wrote {content.Length} characters to {path}";
    }
}
