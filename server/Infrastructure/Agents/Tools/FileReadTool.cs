using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Reads file content from the worktree. Path-scoped to prevent traversal (NFR8).
/// </summary>
public sealed class FileReadTool : IAgentTool
{
    private readonly string _worktreeRoot;

    public FileReadTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "file_read";

    public string Description => "Reads the contents of a file at the given path relative to the worktree root.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path to the file within the worktree." }
          },
          "required": ["path"]
        }
        """;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonInput);
        var path = doc.RootElement.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter.");

        var resolved = PathGuard.Resolve(_worktreeRoot, path);

        if (!File.Exists(resolved))
            return $"Error: File not found: {path}";

        var content = await File.ReadAllTextAsync(resolved, ct);
        return content;
    }
}
