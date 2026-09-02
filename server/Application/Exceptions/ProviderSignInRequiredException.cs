namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0324: a registry-Grok create/retry whose <c>GROK_HOME</c> has no usable session.
/// HTTP 409, <c>code: provider_sign_in_required</c>.
/// </summary>
public sealed class ProviderSignInRequiredException : HttpException
{
    public const string ErrorCode = "provider_sign_in_required";

    public string GrokHome { get; }

    public ProviderSignInRequiredException(string grokHome)
        : base(
            409,
            "Grok is not signed in on this host. Run `grok login` as the Windows user that runs "
            + "the session-runner, pick another agentKind, or re-send with "
            + "allowUnauthenticatedProvider=true to queue anyway.",
            ErrorCode,
            BuildExtensions(grokHome))
    {
        GrokHome = grokHome;
    }

    private static IReadOnlyDictionary<string, object?> BuildExtensions(string grokHome) =>
        new Dictionary<string, object?>
        {
            ["agentKind"] = "Grok",
            ["grokHome"] = grokHome,
            ["remedy"] = "grok login",
        };
}
