using Antiphon.SessionRunner.Contracts;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-1677 D-2. What this runner itself did to a workspace path recently: the dispatcher stamps every
/// admitted <c>WorkspaceMirror</c> (its <c>&lt;worktree root&gt;/&lt;name&gt;</c>) and every admitted
/// <c>Launch</c> (its cwd, with the session id) under the mutation lock, and retention phase A, under the same
/// lock, keeps a tree that a stamp covers. That closes the window between the server's reread and the move:
/// a task re-dispatched in it has its mirror stamped before (or its retain refused after) the move, never
/// moved away under the new session. In memory only: after a runner restart the holder scan still keeps a
/// tree a surviving process uses.
/// </summary>
public sealed class RunnerWorkspaceActivity
{
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _recent = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _sessions = [];
    private readonly TimeProvider _time;

    public RunnerWorkspaceActivity(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    public void RecordMirror(string path)
    {
        lock (_gate)
            _recent[Normalize(path)] = _time.GetUtcNow();
    }

    public void RecordLaunch(Guid sessionId, string cwd)
    {
        var path = Normalize(cwd);
        lock (_gate)
        {
            _recent[path] = _time.GetUtcNow();
            _sessions[sessionId] = path;
        }
    }

    /// <summary>
    /// The kept reason for <paramref name="path"/>, or null: <c>workspace_in_use</c> when one of
    /// <paramref name="liveSessions"/> was launched at or under it, <c>recently_mirrored</c> when a mirror or a
    /// launch at or under it is younger than <paramref name="window"/>.
    /// </summary>
    public (string Reason, string Detail)? Covers(string path, TimeSpan window, IEnumerable<Guid> liveSessions)
    {
        var target = Normalize(path);
        var now = _time.GetUtcNow();
        var live = liveSessions.ToHashSet();
        lock (_gate)
        {
            // A session that is no longer live holds nothing; its launch stamp still counts for the window.
            foreach (var gone in _sessions.Keys.Where(id => !live.Contains(id)).ToList())
                _sessions.Remove(gone);
            foreach (var (id, cwd) in _sessions)
                if (AtOrUnder(cwd, target))
                    return (WorkspaceRetentionCodes.WorkspaceInUse, "session " + id.ToString("D") + " runs in " + cwd);
            foreach (var stale in _recent.Where(entry => now - entry.Value >= window).Select(entry => entry.Key).ToList())
                _recent.Remove(stale);
            foreach (var (stamped, at) in _recent)
                if (AtOrUnder(stamped, target))
                    return (WorkspaceRetentionCodes.RecentlyMirrored,
                        $"{stamped} was prepared {(long)(now - at).TotalSeconds} s ago (window {(long)window.TotalMinutes} min)");
        }

        return null;
    }

    private static bool AtOrUnder(string candidate, string target) =>
        string.Equals(candidate, target, StringComparison.Ordinal)
        || candidate.StartsWith(target + "/", StringComparison.Ordinal);

    private static string Normalize(string path) => (path ?? "").Replace('\\', '/').TrimEnd('/');
}
