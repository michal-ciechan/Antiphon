using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Searches file contents for a regex pattern within the worktree (FR15).
/// Path-scoped to prevent traversal (NFR8).
/// </summary>
public sealed class GrepTool : IAgentTool
{
    private readonly string _worktreeRoot;
    private const int MaxResults = 200;

    public GrepTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "grep";

    public string Description => "Searches file contents for a regex pattern within the worktree. Returns matching lines with file paths and line numbers.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "pattern": { "type": "string", "description": "Regex pattern to search for." },
            "glob": { "type": "string", "description": "Optional file glob filter (e.g., '*.cs')." },
            "path": { "type": "string", "description": "Optional subdirectory to search in, relative to worktree root." }
          },
          "required": ["pattern"]
        }
        """;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
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
            return $"Error: Directory not found.";

        Regex regex;
        try
        {
            regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException ex)
        {
            return $"Error: Invalid regex pattern: {ex.Message}";
        }

        // Find files to search
        var globPattern = "**/*";
        if (doc.RootElement.TryGetProperty("glob", out var globProp) && globProp.GetString() is { } globStr)
        {
            globPattern = $"**/{globStr}";
        }

        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher();
        matcher.AddInclude(globPattern);
        var matchResult = matcher.Execute(
            new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(
                new DirectoryInfo(searchRoot)));

        var results = new List<string>();
        foreach (var file in matchResult.Files)
        {
            ct.ThrowIfCancellationRequested();

            if (results.Count >= MaxResults)
                break;

            var fullPath = Path.Combine(searchRoot, file.Path);
            if (!File.Exists(fullPath))
                continue;

            // Skip binary files (simple heuristic)
            try
            {
                var lines = await File.ReadAllLinesAsync(fullPath, ct);
                for (var i = 0; i < lines.Length && results.Count < MaxResults; i++)
                {
                    if (regex.IsMatch(lines[i]))
                    {
                        var relativePath = Path.GetRelativePath(_worktreeRoot, fullPath).Replace('\\', '/');
                        results.Add($"{relativePath}:{i + 1}: {lines[i].TrimEnd()}");
                    }
                }
            }
            catch (Exception)
            {
                // Skip files that can't be read (binary, locked, etc.)
            }
        }

        if (results.Count == 0)
            return "No matches found.";

        var output = string.Join('\n', results);
        if (results.Count >= MaxResults)
            output += $"\n... (results truncated at {MaxResults})";

        return output;
    }
}
