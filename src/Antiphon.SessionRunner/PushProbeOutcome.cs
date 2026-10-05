using System.Text.RegularExpressions;

namespace Antiphon.SessionRunner;

public enum PushProbeCategory
{
    Authorized,
    CredentialMissing,
    CredentialRejected,
    Forbidden,
    Unreachable,
    Unknown
}

/// <summary>Safe diagnostics for the receive-pack probe; never returns captured stderr.</summary>
public static partial class PushProbeOutcome
{
    public static PushProbeCategory Classify(int exitCode, string stderr)
    {
        if (exitCode == 0)
            return PushProbeCategory.Authorized;
        if (ContainsAny(stderr, "terminal prompts disabled", "could not read Username", "could not read Password"))
            return PushProbeCategory.CredentialMissing;
        if (ContainsAny(stderr, "Authentication failed", "Invalid username or token", "Bad credentials", "error: 401"))
            return PushProbeCategory.CredentialRejected;
        if (PermissionDenied().IsMatch(stderr)
            || ContainsAny(stderr, "error: 403", "Permission denied (publickey)", "deploy key"))
            return PushProbeCategory.Forbidden;
        if (ContainsAny(stderr, "Could not resolve host", "Failed to connect", "Connection timed out",
                "Connection refused", "ssh: connect to host", "SSL"))
            return PushProbeCategory.Unreachable;
        return PushProbeCategory.Unknown;
    }

    public static string Remedy(PushProbeCategory category) => category switch
    {
        PushProbeCategory.Authorized => "",
        PushProbeCategory.CredentialMissing => "the runner has no credential for this repository: provision the token file (docs/agent-credentials.md) or check PhoneHome:AllowedCloneSources",
        PushProbeCategory.CredentialRejected => "GitHub rejected the runner's token (expired, revoked or rotated): rotate it with the ClaudeBot github-push-token skill and refresh the server2 file",
        PushProbeCategory.Forbidden => "the credential cannot push to this repository: grant the account access, or the SSH key is the wrong identity",
        PushProbeCategory.Unreachable => "GitHub was not reachable from server2; the dispatcher retries after its backoff",
        _ => "check the runner log"
    };

    private static bool ContainsAny(string text, params string[] markers) =>
        markers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"Permission to .* denied", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PermissionDenied();
}
