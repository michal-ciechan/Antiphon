using System.Collections.Concurrent;

namespace Antiphon.SessionRunner;

/// <summary>
/// Which session is tailing which transcript file, for the lifetime of this runner process
/// (rule C1 of CARD-0006). A transcript belongs to exactly one session: two sessions binding to
/// one file means at least one of them is reading somebody else's conversation, and the loser of
/// that race relays the other's turns to whatever channel it is bound to.
///
/// A session claims EVERY path it ever tails and never releases one on a fork switch — the
/// pre-fork file is still that session's history and must stay unadoptable by a sibling. Claims
/// are dropped only when the tailer is disposed.
///
/// <para><b>Known limitation:</b> the registry is per runner PROCESS. Two runners sharing one
/// <c>~/.claude</c> (the manual-mode 17283 runner beside the 17204 daemon) do not see each
/// other's claims — that configuration is already unsupported. Claims survive a runner restart
/// only because they are rebuilt from the transcript sidecars before adoption runs
/// (see <see cref="TranscriptSidecar"/> and <c>SessionRunnerRuntime.AdoptOrphanedHostsAsync</c>).</para>
/// </summary>
public sealed class TranscriptClaimRegistry
{
    // OrdinalIgnoreCase: Windows paths. Keyed by the fully-qualified path so "a\b.jsonl" and
    // "a\.\b.jsonl" cannot both be claimed.
    private readonly ConcurrentDictionary<string, Guid> _claims = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Claims <paramref name="path"/> for <paramref name="sessionId"/>. True if this session now
    /// owns it (or already did). Atomic, so concurrent launches cannot both win — no extra locking
    /// is needed around the claim-then-verify sequence in the tailer.
    /// </summary>
    public bool TryClaim(string path, Guid sessionId)
    {
        if (Canonical(path) is not { } key)
            return false;
        return _claims.GetOrAdd(key, sessionId) == sessionId;
    }

    /// <summary>True when some OTHER session holds this path.</summary>
    public bool IsClaimedByOther(string path, Guid sessionId) =>
        Canonical(path) is { } key && _claims.TryGetValue(key, out var owner) && owner != sessionId;

    /// <summary>Drops a claim (only if this session holds it) — the verify half of claim-then-verify.</summary>
    public void Release(string path, Guid sessionId)
    {
        if (Canonical(path) is { } key)
            ((ICollection<KeyValuePair<string, Guid>>)_claims).Remove(new KeyValuePair<string, Guid>(key, sessionId));
    }

    /// <summary>Drops every claim held by a session (called when its tailer is disposed).</summary>
    public void ReleaseAll(Guid sessionId)
    {
        foreach (var (path, owner) in _claims)
        {
            if (owner == sessionId)
                _claims.TryRemove(new KeyValuePair<string, Guid>(path, owner));
        }
    }

    /// <summary>Current claims (diagnostics/tests).</summary>
    public IReadOnlyDictionary<string, Guid> Snapshot() => new Dictionary<string, Guid>(_claims, StringComparer.OrdinalIgnoreCase);

    private static string? Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
