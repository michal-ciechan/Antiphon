using System.Diagnostics;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Resolves and AUTHORISES the directory a delegated task runs in. A task's working directory is a
/// property of the task, not inherited from its parent — that is what makes cross-repo orchestration
/// (an agent per repo) work — which is exactly why it needs a boundary: without one, an agent that
/// can delegate could point a task at any path the server user can read and run a fresh Claude there.
///
/// Pure path logic is static and side-effect free (<see cref="IsWithinRoot"/>,
/// <see cref="NormalizeSeparators"/>) so it can be unit-tested without a filesystem.
/// </summary>
public sealed class DelegationWorkspaceResolver
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);
    private readonly ILogger<DelegationWorkspaceResolver> _logger;

    public DelegationWorkspaceResolver(ILogger<DelegationWorkspaceResolver> logger) => _logger = logger;

    public sealed record Resolution(string WorkingDirectory, string? RepoPath);

    public sealed class RejectedException(string message) : Exception(message);

    /// <summary>
    /// Resolve the requested directory (or fall back to the parent's), verify it exists and sits
    /// under an allowed root, and derive the git toplevel.
    /// </summary>
    public async Task<Resolution> ResolveAsync(
        string? requestedDirectory,
        string parentDirectory,
        IReadOnlyList<string> allowedRoots,
        CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(requestedDirectory) ? parentDirectory : requestedDirectory.Trim();
        if (string.IsNullOrWhiteSpace(target))
            throw new RejectedException("No working directory was given and the caller has none to inherit.");

        string full;
        try
        {
            full = Path.GetFullPath(target);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new RejectedException($"'{target}' is not a usable directory path.");
        }

        if (!Directory.Exists(full))
            throw new RejectedException($"Directory does not exist: {full}");

        // The parent's OWN tree is always allowed — that's the inherit case, and it must keep
        // working even when no roots are configured. Anything else must be explicitly permitted.
        var roots = new List<string>(allowedRoots.Where(r => !string.IsNullOrWhiteSpace(r)));
        if (!string.IsNullOrWhiteSpace(parentDirectory))
            roots.Add(parentDirectory);

        if (!roots.Any(root => IsWithinRoot(full, root)))
        {
            throw new RejectedException(
                $"Directory '{full}' is outside the allowed roots. Add it to Delegation:AllowedRoots to permit it.");
        }

        return new Resolution(full, await GetRepoToplevelAsync(full, ct));
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is <paramref name="root"/> or sits beneath it.
    /// Compares normalised, separator-terminated paths so "C:\src\antiphon-evil" is NOT treated as
    /// being inside "C:\src\antiphon" — a plain StartsWith would let that through.
    /// </summary>
    public static bool IsWithinRoot(string candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
            return false;

        string fullCandidate, fullRoot;
        try
        {
            fullCandidate = NormalizeSeparators(Path.GetFullPath(candidate));
            fullRoot = NormalizeSeparators(Path.GetFullPath(root));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(fullCandidate, fullRoot, comparison))
            return true;

        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(prefix, comparison);
    }

    /// <summary>Trailing-separator-trimmed, single-separator-style path for comparison.</summary>
    public static string NormalizeSeparators(string path)
    {
        var normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return normalized.Length > 3 && normalized.EndsWith(Path.DirectorySeparatorChar)
            ? normalized.TrimEnd(Path.DirectorySeparatorChar)
            : normalized;
    }

    /// <summary>The repo toplevel for a directory, or null when it isn't inside a git repo.</summary>
    public async Task<string?> GetRepoToplevelAsync(string directory, CancellationToken ct)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = directory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("rev-parse");
            psi.ArgumentList.Add("--show-toplevel");

            using var process = Process.Start(psi);
            if (process is null)
                return null;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(GitTimeout);
            var stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token);

            var top = stdout.Trim();
            return process.ExitCode == 0 && top.Length > 0 ? Path.GetFullPath(top) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // A non-repo directory is legal in Shared mode — never surface this as an error.
            _logger.LogDebug(ex, "git rev-parse --show-toplevel failed in {Dir}", directory);
            return null;
        }
    }
}
