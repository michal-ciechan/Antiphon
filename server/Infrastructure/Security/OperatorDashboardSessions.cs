namespace Antiphon.Server.Infrastructure.Security;

/// <summary>
/// CARD-0658 S1 seam: inert until S2.
/// </summary>
public sealed class OperatorDashboardSessions
{
    public const string CookieName = "antiphon-operator-dashboard";
    public static readonly TimeSpan NonceLifetime = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private readonly TimeProvider _clock;

    public OperatorDashboardSessions(TimeProvider clock) => _clock = clock;

    public string IssueNonce() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    public string? Redeem(string? nonce) => null;

    public bool IsLive(string? sessionId) => false;
}
