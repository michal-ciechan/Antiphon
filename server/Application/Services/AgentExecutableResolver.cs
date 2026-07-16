using Antiphon.Server.Application.Exceptions;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Resolves an agent definition's Exe to a concrete on-disk executable.
///
/// Why this exists: ConPTY (unlike cmd.exe) resolves a bare command name against the session's
/// working directory, not PATH — so "claude.cmd" launched in C:\src\foo becomes
/// C:\src\foo\claude.cmd and fails with Win32 "file not found". Worse, the installed launcher
/// flavor changes over time (npm ships claude.cmd, the native installer ships claude.exe), which
/// once left every agent start failing after a Claude update. Resolving to an absolute path up
/// front — trying sibling extensions when the configured flavor is gone — makes launches immune
/// to both problems, and lets the Start API reject an unresolvable exe BEFORE any state changes.
/// </summary>
public sealed class AgentExecutableResolver
{
    private static readonly string[] WindowsExecutableExtensions = [".exe", ".cmd", ".bat"];

    public static AgentExecutableResolver Default { get; } = new();

    private readonly string _searchPath;

    /// <param name="searchPath">
    /// PATH-style search string. Defaults to the current process PATH; injectable for tests.
    /// </param>
    public AgentExecutableResolver(string? searchPath = null)
    {
        _searchPath = searchPath ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
    }

    /// <summary>
    /// Best-effort resolution to an absolute path. Returns null when nothing matches —
    /// callers decide whether that is fatal (see <see cref="EnsureSpawnable"/>).
    /// </summary>
    public string? TryResolve(string exe)
    {
        if (string.IsNullOrWhiteSpace(exe))
            return null;

        if (Path.IsPathRooted(exe))
            return File.Exists(exe) ? Path.GetFullPath(exe) : null;

        // A relative path with directory separators is cwd-dependent; leave those alone.
        if (exe.Contains(Path.DirectorySeparatorChar) || exe.Contains(Path.AltDirectorySeparatorChar))
            return null;

        var directories = _searchPath
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Pass 1: the exact configured name.
        foreach (var dir in directories)
        {
            var candidate = SafeCombine(dir, exe);
            if (candidate is not null && File.Exists(candidate))
                return candidate;
        }

        // Pass 2: same base name, other executable flavors (claude.cmd → claude.exe when the
        // npm shim was replaced by the native installer, and vice versa).
        if (!OperatingSystem.IsWindows())
            return null;

        var baseName = Path.GetFileNameWithoutExtension(exe);
        foreach (var dir in directories)
        {
            foreach (var extension in WindowsExecutableExtensions)
            {
                var candidate = SafeCombine(dir, baseName + extension);
                if (candidate is not null && File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Throws a user-facing <see cref="ConflictException"/> when <paramref name="exe"/> cannot be
    /// spawned. Call this on user-triggered launch paths BEFORE mutating any agent/session state,
    /// so a misconfigured executable surfaces as an immediate API error instead of an agent that
    /// claims to be Working with no process behind it.
    /// </summary>
    public void EnsureSpawnable(string exe)
    {
        if (Path.IsPathRooted(exe))
        {
            if (!File.Exists(exe))
                throw new ConflictException(
                    $"Agent executable '{exe}' does not exist. Fix Agents:Definitions:*:Exe or install the tool.");
            return;
        }

        if (TryResolve(exe) is null)
            throw new ConflictException(
                $"Agent executable '{exe}' was not found on PATH (also tried "
                + $"{string.Join('/', WindowsExecutableExtensions)} variants). "
                + "Fix Agents:Definitions:*:Exe or install the tool.");
    }

    private static string? SafeCombine(string directory, string fileName)
    {
        try
        {
            return Path.Combine(directory, fileName);
        }
        catch (ArgumentException)
        {
            return null; // malformed PATH entry — skip
        }
    }
}
