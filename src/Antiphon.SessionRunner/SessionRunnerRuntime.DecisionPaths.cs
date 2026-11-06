using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

public sealed partial class SessionRunnerRuntime
{
    internal async Task<DecisionPathInspectionResult?> InspectDecisionPathsAsync(DecisionPathInspectionRequest request, CancellationToken ct)
    {
        var gate = _launchLocks.GetOrAdd(request.SessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            DecisionPathInspectionResult Held(string reason) => new(false, reason, request);
            if (!_sessions.TryGetValue(request.SessionId, out var session) || session.HasExited
                || request.RunnerStoreId != RunnerStoreId
                || !SessionGeneration.Equal(request.AcceptedStartedAt, session.AcceptedStartedAt))
                return Held("decision_execution_changed");
            // The caller's root is a comparison, never the source of an arbitrary file lookup.
            if (string.IsNullOrEmpty(session.CheckoutPath) || request.Root != session.CheckoutPath)
                return Held("decision_cwd_changed");
            if (request.Paths is not { Count: > 0 and <= 64 } || request.Root.Length > 4096)
                return Held("decision_paths_invalid");
            var verifier = new DecisionPathContainment();
            if (!verifier.Contains(session.CheckoutPath, request.Paths, ct))
                return Held("decision_path_not_contained");
            if (!_sessions.TryGetValue(request.SessionId, out var current) || !ReferenceEquals(current, session)
                || session.HasExited || request.RunnerStoreId != RunnerStoreId
                || !SessionGeneration.Equal(request.AcceptedStartedAt, session.AcceptedStartedAt)
                || request.Root != session.CheckoutPath)
                return Held("decision_execution_changed");
            return new(true, null, request);
        }
        finally { gate.Release(); }
    }
}

/// <summary>No contents are read. Links are conservatively refused, including links inside the root.
/// This observation has the same point-in-time limit as local decision checks; it is no custody receipt.</summary>
internal sealed class DecisionPathContainment
{
    public bool Contains(string root, IReadOnlyList<string> paths, CancellationToken ct)
    {
        try
        {
            if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root)) return false;
            var directory = new DirectoryInfo(root);
            for (var parent = directory; parent is not null; parent = parent.Parent)
                if (parent.LinkTarget is not null || (parent.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            foreach (var relative in paths)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(relative) || relative.Length > 1024
                    || relative.Any(char.IsControl) || relative.Contains('\\') || relative.Contains(':')
                    || Path.IsPathRooted(relative)) return false;
                var parts = relative.Split('/');
                if (parts.Any(p => p is "" or "." or "..")) return false;
                var current = root;
                for (var i = 0; i < parts.Length; i++)
                {
                    current = Path.Combine(current, parts[i]);
                    FileSystemInfo item = i == parts.Length - 1 ? new FileInfo(current) : new DirectoryInfo(current);
                    if (item.LinkTarget is not null || !item.Exists
                        || (item.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                    var fromRoot = Path.GetRelativePath(root, current);
                    if (fromRoot == ".." || fromRoot.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                        || Path.IsPathRooted(fromRoot)) return false;
                }
                if (Directory.Exists(current)) return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { return false; }
    }
}
