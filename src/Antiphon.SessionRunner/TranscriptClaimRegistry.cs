using System.Collections.Concurrent;

namespace Antiphon.SessionRunner;

/// <summary>
/// Strength of a transcript claim (CARD-0181). Derived from the path and the claimant, never
/// declared by the caller: Exact iff the file's basename is the claimant's own session id.
/// </summary>
public enum ClaimStrength
{
    Heuristic = 0,
    Exact = 1
}

/// <summary>Outcome of <see cref="TranscriptClaimRegistry.TryClaim"/>.</summary>
public readonly record struct ClaimResult(bool Claimed, Guid? Displaced);

/// <summary>
/// Which session is tailing which transcript file, for the lifetime of this runner process
/// (rule C1 of CARD-0006, claim strength CARD-0181). A transcript belongs to exactly one session:
/// two sessions binding to one file means at least one of them is reading somebody else's
/// conversation, and the loser of that race relays the other's turns to whatever channel it is
/// bound to.
///
/// A session claims EVERY path it ever tails and never releases one on a fork switch — the
/// pre-fork file is still that session's history and must stay unadoptable by a sibling. Claims
/// are dropped only when the tailer is disposed. Exact outranks heuristic; nothing outranks
/// exact; heuristic-vs-heuristic stays first-wins.
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
    private readonly ConcurrentDictionary<string, Claim> _claims = new(StringComparer.OrdinalIgnoreCase);

    private readonly record struct Claim(Guid Owner, ClaimStrength Strength);

    /// <summary>
    /// Raised when an Exact claim displaces a Heuristic one. Arguments are
    /// (canonical path, previous owner, new owner).
    /// </summary>
    public event Action<string, Guid, Guid>? ClaimDisplaced;

    /// <summary>
    /// Claims <paramref name="path"/> for <paramref name="sessionId"/>. Strength is derived from
    /// the basename: Exact iff the file is named for this session. True if this session now owns
    /// it (or already did). Atomic, so concurrent launches cannot both win.
    /// </summary>
    public ClaimResult TryClaim(string path, Guid sessionId)
    {
        if (Canonical(path) is not { } key)
            return new(false, null);

        var incoming = IsNamesake(path, sessionId) ? ClaimStrength.Exact : ClaimStrength.Heuristic;

        while (true)
        {
            if (!_claims.TryGetValue(key, out var existing))
            {
                if (_claims.TryAdd(key, new Claim(sessionId, incoming)))
                    return new(true, null);
                continue;
            }

            if (existing.Owner == sessionId)
            {
                if (incoming == ClaimStrength.Exact && existing.Strength != ClaimStrength.Exact)
                    _claims.TryUpdate(key, new Claim(sessionId, ClaimStrength.Exact), existing);
                return new(true, null);
            }

            if (incoming == ClaimStrength.Exact && existing.Strength == ClaimStrength.Heuristic)
            {
                if (_claims.TryUpdate(key, new Claim(sessionId, ClaimStrength.Exact), existing))
                {
                    ClaimDisplaced?.Invoke(key, existing.Owner, sessionId);
                    return new(true, existing.Owner);
                }

                continue;
            }

            return new(false, null);
        }
    }

    /// <summary>True when some OTHER session holds this path.</summary>
    public bool IsClaimedByOther(string path, Guid sessionId) =>
        Canonical(path) is { } key && _claims.TryGetValue(key, out var owner) && owner.Owner != sessionId;

    /// <summary>Who holds this path, if anyone, and at what strength.</summary>
    public (Guid Owner, ClaimStrength Strength)? OwnerOf(string path)
    {
        if (Canonical(path) is not { } key)
            return null;
        return _claims.TryGetValue(key, out var claim) ? (claim.Owner, claim.Strength) : null;
    }

    /// <summary>Drops a claim (only if this session holds it) — the verify half of claim-then-verify.</summary>
    public void Release(string path, Guid sessionId)
    {
        if (Canonical(path) is not { } key)
            return;
        if (_claims.TryGetValue(key, out var existing) && existing.Owner == sessionId)
            _claims.TryRemove(new KeyValuePair<string, Claim>(key, existing));
    }

    /// <summary>Drops every claim held by a session (called when its tailer is disposed).</summary>
    public void ReleaseAll(Guid sessionId)
    {
        foreach (var (path, claim) in _claims)
        {
            if (claim.Owner == sessionId)
                _claims.TryRemove(new KeyValuePair<string, Claim>(path, claim));
        }
    }

    /// <summary>Current claims (diagnostics/tests). Owner only — contract unchanged from CARD-0006.</summary>
    public IReadOnlyDictionary<string, Guid> Snapshot()
    {
        var copy = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, claim) in _claims)
            copy[path] = claim.Owner;
        return copy;
    }

    /// <summary>
    /// Test-only seam: seed a claim the production <see cref="TryClaim"/> could not produce
    /// (an Exact claim whose owner is not the namesake). Used to force the exact-vs-exact
    /// defensive branch.
    /// </summary>
    internal void ForceClaimForTests(string path, Guid owner, ClaimStrength strength)
    {
        if (Canonical(path) is not { } key)
            throw new ArgumentException("Path could not be canonicalised.", nameof(path));
        _claims[key] = new Claim(owner, strength);
    }

    /// <summary>
    /// The session a transcript file is named for, if its basename is a GUID (Claude's
    /// <c>&lt;sessionId&gt;.jsonl</c>). Null for Grok/Codex paths and self-chosen fork names that
    /// are not GUID-shaped.
    /// </summary>
    public static Guid? TryReadNamesake(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return Guid.TryParse(name, out var id) ? id : null;
    }

    public static bool IsNamesake(string path, Guid sessionId) =>
        TryReadNamesake(path) is { } id && id == sessionId;

    private static string? Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try { return Path.GetFullPath(path); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
}
