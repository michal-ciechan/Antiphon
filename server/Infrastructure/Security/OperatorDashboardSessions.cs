using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// CARD-0658: one-time dashboard login links and the dashboard sessions they create. Only a
/// header-authenticated operator can issue a nonce, so the store is bounded by operator activity;
/// expired entries are swept on every call. Only SHA-256 hashes of nonces and session ids are
/// kept, so a memory dump yields no live cookie. Sessions do not survive a restart: re-login is
/// one command (<c>scripts/hangfire-dashboard.ps1</c>). Nothing here logs a value.
/// </summary>
public sealed class OperatorDashboardSessions
{
    public const string CookieName = "antiphon-operator-dashboard";
    public static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _nonces = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);

    public OperatorDashboardSessions(TimeProvider clock) => _clock = clock;

    /// <summary>Issue a single-use login nonce (64 hex) valid for <see cref="NonceLifetime"/>.</summary>
    public string IssueNonce()
    {
        var now = _clock.GetUtcNow();
        Sweep(now);
        var nonce = NewSecret();
        _nonces[Hash(nonce)] = now + NonceLifetime;
        return nonce;
    }

    /// <summary>
    /// Redeem a nonce exactly once. Returns a new session id, or null when the nonce is unknown,
    /// already redeemed or expired.
    /// </summary>
    public string? Redeem(string? nonce)
    {
        var now = _clock.GetUtcNow();
        Sweep(now);
        if (string.IsNullOrEmpty(nonce))
            return null;
        if (!_nonces.TryRemove(Hash(nonce), out var expiresAt) || expiresAt <= now)
            return null;
        var session = NewSecret();
        _sessions[Hash(session)] = now + SessionLifetime;
        return session;
    }

    public bool IsLive(string? sessionId)
    {
        var now = _clock.GetUtcNow();
        Sweep(now);
        return !string.IsNullOrEmpty(sessionId)
            && _sessions.TryGetValue(Hash(sessionId), out var expiresAt)
            && expiresAt > now;
    }

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (key, expiresAt) in _nonces)
            if (expiresAt <= now)
                _nonces.TryRemove(key, out _);
        foreach (var (key, expiresAt) in _sessions)
            if (expiresAt <= now)
                _sessions.TryRemove(key, out _);
    }

    private static string NewSecret() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
