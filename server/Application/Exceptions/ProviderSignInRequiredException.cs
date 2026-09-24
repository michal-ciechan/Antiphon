namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0324: a registry-Grok create/retry whose <c>GROK_HOME</c> has no usable session.
/// HTTP 409, <c>code: provider_sign_in_required</c>.
/// </summary>
public sealed class ProviderSignInRequiredException : HttpException
{
    public const string ErrorCode = "provider_sign_in_required";

    public string? GrokHome { get; }

    public string AgentKind { get; } = "Grok";

    public string? CodexHome { get; }

    public string? RunnerId { get; }

    public ProviderSignInRequiredException(string grokHome, string? runnerId = null)
        : base(409, MessageFor(grokHome, runnerId), ErrorCode, BuildExtensions(grokHome, runnerId))
    {
        GrokHome = grokHome;
        RunnerId = string.IsNullOrWhiteSpace(runnerId) ? null : runnerId;
    }

    public static ProviderSignInRequiredException ForCodex(string codexHome, string runnerId) =>
        throw new NotImplementedException();

    private static string MessageFor(string grokHome, string? runnerId)
    {
        if (string.IsNullOrWhiteSpace(runnerId))
            return "Grok is not signed in on this host. Run `grok login` as the Windows user that runs "
                + "the session-runner, pick another agentKind, or re-send with "
                + "allowUnauthenticatedProvider=true to queue anyway.";
        return "Grok is not signed in on runner '" + runnerId + "' (GROK_HOME=" + grokHome
            + "). Run `grok login` inside that runner, pick another agentKind, or re-send with "
            + "allowUnauthenticatedProvider=true to queue anyway.";
    }

    private static IReadOnlyDictionary<string, object?> BuildExtensions(string grokHome, string? runnerId)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["agentKind"] = "Grok",
            ["grokHome"] = grokHome,
            ["remedy"] = "grok login",
        };
        if (!string.IsNullOrWhiteSpace(runnerId))
            extensions["runnerId"] = runnerId;
        return extensions;
    }
}
