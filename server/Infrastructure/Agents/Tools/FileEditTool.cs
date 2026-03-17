using System.Text.Json;

namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Performs search-and-replace edits on a file in the worktree.
/// Path-scoped to prevent traversal (NFR8).
/// </summary>
public sealed class FileEditTool : IAgentTool
{
    private readonly string _worktreeRoot;

    public FileEditTool(string worktreeRoot)
    {
        _worktreeRoot = worktreeRoot;
    }

    public string Name => "file_edit";

    public string Description => "Performs a search-and-replace edit on a file. The old_string must match exactly one location in the file.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Relative path to the file within the worktree." },
            "old_string": { "type": "string", "description": "The exact text to find and replace." },
            "new_string": { "type": "string", "description": "The replacement text." }
          },
          "required": ["path", "old_string", "new_string"]
        }
        """;

    public async Task<string> ExecuteAsync(string jsonInput, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(jsonInput);
        var path = doc.RootElement.GetProperty("path").GetString()
            ?? throw new ArgumentException("Missing 'path' parameter.");
        var oldString = doc.RootElement.GetProperty("old_string").GetString()
            ?? throw new ArgumentException("Missing 'old_string' parameter.");
        var newString = doc.RootElement.GetProperty("new_string").GetString()
            ?? throw new ArgumentException("Missing 'new_string' parameter.");

        var resolved = PathGuard.Resolve(_worktreeRoot, path);

        if (!File.Exists(resolved))
            return $"Error: File not found: {path}";

        var content = await File.ReadAllTextAsync(resolved, ct);

        var occurrences = CountOccurrences(content, oldString);
        if (occurrences == 0)
            return $"Error: old_string not found in {path}";
        if (occurrences > 1)
            return $"Error: old_string found {occurrences} times in {path}. It must match exactly once.";

        var updated = content.Replace(oldString, newString);
        await File.WriteAllTextAsync(resolved, updated, ct);
        return $"Successfully edited {path}";
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}
