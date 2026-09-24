namespace Antiphon.Server.Application.Exceptions;

/// <summary>
/// CARD-0324: a registry-Grok create/retry whose <c>GROK_HOME</c> has no usable session.
/// HTTP 409, <c>code: provider_sign_in_required</c>. CARD-0660 D-9 adds the runner-bound Codex
/// shape through <see cref="ForCodex"/>; the Grok constructor, properties and extensions are
/// unchanged.
/// </summary>
public sealed class ProviderSignInRequiredException : HttpException
{
    public const string ErrorCode = "provider_sign_in_required";

    /// <summary>The Codex remedy: an operator-run device login inside the runner (D-5).</summary>
    public const string CodexRemedy = "codex login --device-auth";

    /// <summary>The Grok store that has no session; null for another provider.</summary>
    public string? GrokHome { get; }

    /// <summary>The refused agent kind, as the problem document's <c>agentKind</c> spells it.</summary>
    public string AgentKind { get; }

    /// <summary>The runner's Codex home (never a desktop one); null for another provider.</summary>
    public string? CodexHome { get; }

    public string? RunnerId { get; }

    public ProviderSignInRequiredException(string grokHome, string? runnerId = null)
        : base(409, MessageFor(grokHome, runnerId), ErrorCode, BuildExtensions(grokHome, runnerId))
    {
        GrokHome = grokHome;
        AgentKind = "Grok";
        RunnerId = string.IsNullOrWhiteSpace(runnerId) ? null : runnerId;
    }

    private ProviderSignInRequiredException(string codexHome, string runnerId, bool _)
        : base(409, CodexMessageFor(codexHome, runnerId), ErrorCode, BuildCodexExtensions(codexHome, runnerId))
    {
        CodexHome = codexHome;
        AgentKind = "Codex";
        RunnerId = runnerId;
    }

    /// <summary>
    /// CARD-0660 D-9: runner <paramref name="runnerId"/> reported no Codex login in
    /// <paramref name="codexHome"/>. There is no desktop form: a local Codex create is never probed.
    /// </summary>
    public static ProviderSignInRequiredException ForCodex(string codexHome, string runnerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);
        return new ProviderSignInRequiredException(codexHome, runnerId, true);
    }

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

    private static string CodexMessageFor(string codexHome, string runnerId) =>
        "Codex is not signed in on runner '" + runnerId + "' (CODEX_HOME=" + codexHome
        + "). An operator must run `" + CodexRemedy + "` inside that runner as its session user, "
        + "pick another agentKind, or re-send with allowUnauthenticatedProvider=true to queue anyway.";

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

    private static IReadOnlyDictionary<string, object?> BuildCodexExtensions(string codexHome, string runnerId) =>
        new Dictionary<string, object?>
        {
            ["agentKind"] = "Codex",
            ["codexHome"] = codexHome,
            ["runnerId"] = runnerId,
            ["remedy"] = CodexRemedy,
        };
}
