using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Finds files matching a glob pattern within the worktree (FR15).
/// Path-scoped to prevent traversal (NFR8).
/// </summary>
public sealed class GlobTool : IAgentTool
{
    private readonly string _worktreeRoot;

    public GlobTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "glob";

    public string Description => "Finds files matching a glob pattern within the worktree. Returns matching file paths relative to the worktree root.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "Glob pattern (e.g., '**/*.cs', 'src/**/*.ts')." },
            "path": { "type": "string", "description": "Optional subdirectory to search in, relative to worktree root." }
          },
          "required": ["pattern"]
        }
        """;

    public Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonInput);
        var pattern = doc.RootElement.GetProperty("pattern").GetString()
            ?? throw new ArgumentException("Missing 'pattern' parameter.");

        var searchRoot = _worktreeRoot;
        if (doc.RootElement.TryGetProperty("path", out var pathProp) && pathProp.GetString() is { } subPath)
        {
            searchRoot = PathGuard.Resolve(_worktreeRoot, subPath);
        }

        if (!Directory.Exists(searchRoot))
            return Task.FromResult($"Error: Directory not found: {searchRoot}");

        ct.ThrowIfCancellationRequested();

        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher();
        matcher.AddInclude(pattern);

        var result = matcher.Execute(
            new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(
                new DirectoryInfo(searchRoot)));

        var files = result.Files
            .Select(f => f.Path)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (files.Count == 0)
            return Task.FromResult("No files matched the pattern.");

        // Limit output
        var displayed = files.Take(500).ToList();
        var output = string.Join('\n', displayed);
        if (files.Count > 500)
            output += $"\n... and {files.Count - 500} more files";

        return Task.FromResult(output);
    }
}
