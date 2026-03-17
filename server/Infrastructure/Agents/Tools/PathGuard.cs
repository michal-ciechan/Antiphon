namespace Antiphon.Server.Infrastructure.Agents.Tools;

/// <summary>
/// Validates that file paths are scoped to the worktree root and blocks path traversal (NFR8).
/// All file-based tools must use this guard before any I/O operation.
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// Resolves a relative path against the worktree root and validates it is within bounds.
    /// Throws InvalidOperationException if path traversal is detected.
    /// </summary>
    public static string Resolve(string worktreeRoot, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(worktreeRoot))
            throw new ArgumentException("Worktree root must be specified.", nameof(worktreeRoot));

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Path must be specified.", nameof(relativePath));

        // Normalize the worktree root to a full path with trailing separator
        var normalizedRoot = Path.GetFullPath(worktreeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        // Combine and resolve to catch ".." traversal
        var combined = Path.Combine(worktreeRoot, relativePath);
        var resolved = Path.GetFullPath(combined);

        if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal blocked: '{relativePath}' resolves outside the worktree root.");
        }

        return resolved;
    }

    /// <summary>
    /// Validates that an already-absolute path is within the worktree root.
    /// </summary>
    public static void Validate(string worktreeRoot, string absolutePath)
    {
        var normalizedRoot = Path.GetFullPath(worktreeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(absolutePath);

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Path traversal blocked: '{absolutePath}' is outside the worktree root.");
        }
    }
}
