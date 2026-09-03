using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Logging;

namespace Antiphon.SessionRunner;

/// <summary>
/// CARD-0341: a Grok launch through the local llm-key-proxy wrapper (<c>gkp.ps1</c>) is refused
/// before the runner touches herdr unless the launch env can route it. Measured failure this
/// prevents: a gkp profile typed into a reused pane with no <c>X_LLM_PROJECT</c> exits 2, and a
/// Grok that starts without <c>GROK_BASE_URL</c> + a dummy key falls through to grok.com OAuth
/// (<c>auth.x.ai</c>) or the default <c>cli-chat-proxy.grok.com</c> — a real provider instead
/// of the local proxy. Keyed on the wrapper file name, so the pool <c>grok.exe</c> path (#28)
/// is untouched.
/// </summary>
internal static class HerdrGkpLaunchGuard
{
    public const string WrapperFileName = "gkp.ps1";
    public const string ProjectMarkerName = "X_LLM_PROJECT";
    public const string ProjectArgument = "--project";
    public const string BaseUrlName = "GROK_BASE_URL";
    public const string ChatProxyUrlName = "GROK_CLI_CHAT_PROXY_BASE_URL";
    public static readonly IReadOnlyList<string> KeyNames = ["XAI_API_KEY", "GROK_CODE_XAI_API_KEY"];

    /// <summary>True when any argument's file name is <see cref="WrapperFileName"/>.</summary>
    public static bool IsGkpLaunch(IReadOnlyList<string>? args)
    {
        if (args is null)
            return false;

        foreach (var arg in args)
        {
            if (string.IsNullOrEmpty(arg))
                continue;
            string fileName;
            try { fileName = Path.GetFileName(arg); }
            catch (ArgumentException) { continue; }
            if (string.Equals(fileName, WrapperFileName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Human-readable list of what a gkp launch is missing: a resolvable project, the base URL,
    /// and a dummy key. Empty when the launch can route.
    /// </summary>
    public static IReadOnlyList<string> MissingRequirements(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env)
    {
        var missing = new List<string>();

        if (!HasProject(args, env))
            missing.Add($"a project ({ProjectMarkerName} in the launch env, or a literal {ProjectArgument} value)");

        if (!HasValue(env, BaseUrlName))
            missing.Add(BaseUrlName);

        if (!KeyNames.Any(name => HasValue(env, name)))
            missing.Add($"a dummy key ({string.Join(" or ", KeyNames)})");

        return missing;
    }

    public static bool HasChatProxyUrl(IReadOnlyDictionary<string, string>? env) =>
        HasValue(env, ChatProxyUrlName);

    /// <summary>
    /// Throws <see cref="HerdrLaunchException"/> (<see cref="HerdrProblemTypes.GkpEnvMissing"/>)
    /// for a gkp launch that cannot route; a non-gkp launch returns immediately.
    /// </summary>
    public static void Require(
        Guid sessionId,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env,
        ILogger logger)
    {
        if (!IsGkpLaunch(args))
            return;

        var missing = MissingRequirements(args, env);
        if (missing.Count > 0)
        {
            throw new HerdrLaunchException(
                $"refusing to type a gkp Grok launch for session {sessionId:D}: the launch env carries no "
                + string.Join(", ", missing)
                + ". gkp.ps1 would exit 2, or grok.exe would fall through to grok.com OAuth / "
                + "cli-chat-proxy.grok.com instead of the local llm-key-proxy. Set them on the agent's "
                + "launchEnv or the project's DefaultLaunchEnv (CARD-0341).",
                HerdrProblemTypes.GkpEnvMissing);
        }

        if (!HasChatProxyUrl(env))
        {
            // Measured (CARD-0341): a gkp-launched Grok with the dummy key still prefetched
            // cli-chat-proxy.grok.com when this name was absent. Not a refusal — the card's gate
            // is GROK_BASE_URL + key — but worth one line the operator can grep for.
            logger.LogWarning(
                "gkp Grok launch for session {SessionId} carries no {Name}; Grok's chat proxy calls will go to its default cli-chat-proxy.grok.com",
                sessionId, ChatProxyUrlName);
        }
    }

    private static bool HasProject(IReadOnlyList<string> args, IReadOnlyDictionary<string, string>? env)
    {
        if (HasValue(env, ProjectMarkerName))
            return true;

        if (!TryReadProjectArgument(args, out var value))
            return false;

        if (HerdrLaunchScript.TryReadEnvTokenName(value, out _))
        {
            return env is not null
                && HerdrLaunchScript.TryResolveEnvToken(value, env, out var resolved)
                && !string.IsNullOrWhiteSpace(resolved);
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary><c>--project value</c> or <c>--project=value</c>; the last occurrence wins.</summary>
    internal static bool TryReadProjectArgument(IReadOnlyList<string> args, out string value)
    {
        value = "";
        var found = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i] ?? "";
            if (string.Equals(arg, ProjectArgument, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count)
                {
                    value = args[i + 1] ?? "";
                    found = true;
                    i++;
                }
                continue;
            }

            if (arg.StartsWith(ProjectArgument + "=", StringComparison.OrdinalIgnoreCase))
            {
                value = arg[(ProjectArgument.Length + 1)..];
                found = true;
            }
        }

        return found;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string>? env, string name)
    {
        if (env is null)
            return false;
        if (env.TryGetValue(name, out var exact))
            return !string.IsNullOrWhiteSpace(exact);
        foreach (var (key, value) in env)
        {
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }
}
