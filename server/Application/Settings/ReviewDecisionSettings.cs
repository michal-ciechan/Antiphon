using System.Text.Json.Serialization;

namespace Antiphon.Server.Application.Settings;

/// <summary>
/// Review engine decision providers and chains (design 7.3, 7.4), section <c>Review:Decisions</c>.
/// A provider names its API key by configuration key only (<see cref="ReviewDecisionProvider.ApiKeyConfigKey"/>);
/// Program.cs resolves it into <see cref="ReviewDecisionProvider.ApiKey"/>, which is never serialised or logged.
/// </summary>
public sealed class ReviewDecisionSettings
{
    public const string SectionName = "Review:Decisions";

    /// <summary>Providers by id.</summary>
    public Dictionary<string, ReviewDecisionProvider> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Chains by name, for example <c>decisions-default</c>: ordered provider entries.</summary>
    public Dictionary<string, List<ReviewDecisionChainEntry>> Chains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The one backoff before retrying a 429/529 on the same entry.</summary>
    public int RateLimitBackoffMilliseconds { get; set; } = 2000;

    /// <summary>
    /// Fills the design's defaults where configuration gave none: Jev over the enterprise LiteLLM route
    /// (D-29), then the Luna, Haiku and Grok Low seats (design 7.4). Jev is not configured until a base URL
    /// and key are set; the seats are not configured until standing seats exist.
    /// </summary>
    public void ApplyDefaults()
    {
        if (Providers.Count == 0)
        {
            Providers["jev"] = new ReviewDecisionProvider
            {
                Dialect = JevDialects.LiteLlmSystemOne,
                Model = "jev-latest",
                ApiKeyConfigKey = "Review:Secrets:JevApiKey",
            };
            Providers["luna-seat"] = new ReviewDecisionProvider { Dialect = SeatDialect, Model = "gpt-5.6-luna" };
            Providers["haiku-seat"] = new ReviewDecisionProvider { Dialect = SeatDialect, Model = "claude-haiku" };
            Providers["grok-low-seat"] = new ReviewDecisionProvider { Dialect = SeatDialect, Model = "grok-low" };
        }

        if (Chains.Count == 0)
        {
            Chains["decisions-default"] =
            [
                new() { Provider = "jev" },
                new() { Provider = "luna-seat", Transport = "Seat" },
                new() { Provider = "haiku-seat", Transport = "Seat" },
                new() { Provider = "grok-low-seat", Transport = "Seat" },
            ];
        }
    }

    public const string SeatDialect = "seat";

    /// <summary>One-shot <c>claude -p</c> on the local Claude Code sign-in: needs a model, no base URL and no key.</summary>
    public const string ClaudeCliDialect = "claude-cli";
}

public static class JevDialects
{
    public const string TypeSafeNative = "typesafe-native";
    public const string LiteLlmSystemOne = "litellm-systemone";
    public const string LiteLlmPassthrough = "litellm-passthrough";
    public const string OpenRouterSystemOne = "openrouter-systemone";

    public static readonly IReadOnlyList<string> All = [TypeSafeNative, LiteLlmSystemOne, LiteLlmPassthrough, OpenRouterSystemOne];
}

public sealed class ReviewDecisionProvider
{
    /// <summary>A <see cref="JevDialects"/> value, <c>seat</c>, or <c>claude-cli</c> (no base URL or key).</summary>
    public string Dialect { get; set; } = "";

    /// <summary>The proxy or API root. Optional for <c>typesafe-native</c> and <c>openrouter-systemone</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>The model to ask for: a proxy alias (<c>jev-latest</c>) or a pinned model.</summary>
    public string? Model { get; set; }

    /// <summary>The configuration key that holds the API key. Never the key itself.</summary>
    public string? ApiKeyConfigKey { get; set; }

    /// <summary>Resolved from <see cref="ApiKeyConfigKey"/> at startup by Program.cs. Never serialised or logged.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary>Answers below this confidence escalate that question to the next entry (design 7.4, 7.5).</summary>
    public double ConfidenceThreshold { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>The proxy retries for us (<c>retry: proxy-managed</c>): no backoff retry on 429/529.</summary>
    public bool ProxyManagedRetry { get; set; }

    /// <summary>Price table when the response carries no cost header. TypeSafe: $0.042 per million input tokens.</summary>
    public decimal InputPricePerMillionUsd { get; set; } = 0.042m;

    public decimal OutputPricePerMillionUsd { get; set; }

    public override string ToString() => $"{Dialect} {BaseUrl} {Model}";
}

public sealed class ReviewDecisionChainEntry
{
    public string Provider { get; set; } = "";

    /// <summary><c>Http</c>, <c>Seat</c> or <c>OneShot</c>.</summary>
    public string Transport { get; set; } = "Http";
}

/// <summary>
/// Review engine completion providers and chains (design 7.1, 7.4), section <c>Review:Completions</c>.
/// A provider names its API key by configuration key only (<see cref="ReviewCompletionProvider.ApiKeyConfigKey"/>);
/// Program.cs resolves it into <see cref="ReviewCompletionProvider.ApiKey"/>, which is never serialised or logged.
/// <para>
/// <see cref="ApplyDefaults"/> adds four providers: <c>litellm-chat</c> (OpenAI-compatible chat over the
/// enterprise LiteLLM proxy, with Jev's key), <c>anthropic</c> (the Messages API, Claude Haiku 5.5),
/// and the delegate seats <c>haiku-delegate</c> and <c>luna-delegate</c> (a Low delegate task). A chain
/// that is not configured is built from the HTTP providers that have their URL, key and model: with
/// none of them the chain is empty and verify is skipped. Seats join a chain only by configuration.
/// </para>
/// </summary>
public sealed class ReviewCompletionSettings
{
    public const string SectionName = "Review:Completions";
    public const string DefaultChain = "completions-cheap";

    public Dictionary<string, ReviewCompletionProvider> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Chains by name, for example <c>completions-cheap</c>: ordered provider entries.</summary>
    public Dictionary<string, List<ReviewDecisionChainEntry>> Chains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in providers, filled in where configuration left a field blank.</summary>
    public void ApplyDefaultProviders()
    {
        Fill("litellm-chat", new ReviewCompletionProvider
        {
            Dialect = ReviewCompletionDialects.OpenAiChat,
            ApiKeyConfigKey = "Review:Secrets:JevApiKey",
        });
        Fill("anthropic", new ReviewCompletionProvider
        {
            Dialect = ReviewCompletionDialects.AnthropicMessages,
            Model = "claude-haiku-5-5",
            ApiKeyConfigKey = "Review:Secrets:AnthropicApiKey",
            InputPricePerMillionUsd = 0.10m,
            OutputPricePerMillionUsd = 0.50m,
        });
        Fill("haiku-delegate", new ReviewCompletionProvider
        {
            Dialect = ReviewCompletionDialects.Delegation,
            Model = "claude-code-low",
            AgentKind = Domain.Enums.AgentKind.ClaudeCode,
            ModelLevel = Domain.Enums.AgentModelLevel.Low,
            TimeoutSeconds = 240,
        });
        Fill("luna-delegate", new ReviewCompletionProvider
        {
            Dialect = ReviewCompletionDialects.Delegation,
            Model = "codex-low",
            AgentKind = Domain.Enums.AgentKind.Codex,
            ModelLevel = Domain.Enums.AgentModelLevel.Low,
            TimeoutSeconds = 240,
        });
    }

    /// <summary>
    /// <c>completions-cheap</c>, when configuration names no chain of that name: the HTTP providers that
    /// are ready (key, model, and a base URL where the dialect needs one), LiteLLM first. Call after the keys are resolved.
    /// </summary>
    public void ApplyDefaultChain()
    {
        if (Chains.ContainsKey(DefaultChain))
            return;
        var ready = new[] { "litellm-chat", "anthropic" }
            .Where(id => Providers.TryGetValue(id, out var p) && p.IsHttpReady)
            .Select(id => new ReviewDecisionChainEntry { Provider = id, Transport = "Http" })
            .ToList();
        if (ready.Count > 0)
            Chains[DefaultChain] = ready;
    }

    private void Fill(string id, ReviewCompletionProvider template)
    {
        if (!Providers.TryGetValue(id, out var provider))
        {
            Providers[id] = template;
            return;
        }

        if (string.IsNullOrWhiteSpace(provider.Dialect))
            provider.Dialect = template.Dialect;
        provider.Model ??= template.Model;
        provider.ApiKeyConfigKey ??= template.ApiKeyConfigKey;
        provider.AgentKind ??= template.AgentKind;
        provider.ModelLevel ??= template.ModelLevel;
        if (provider.InputPricePerMillionUsd == 0 && provider.OutputPricePerMillionUsd == 0)
        {
            provider.InputPricePerMillionUsd = template.InputPricePerMillionUsd;
            provider.OutputPricePerMillionUsd = template.OutputPricePerMillionUsd;
        }
    }
}

/// <summary>The completion dialects the built-in <c>ICompletionModel</c>s serve.</summary>
public static class ReviewCompletionDialects
{
    /// <summary>OpenAI-compatible <c>/v1/chat/completions</c>: LiteLLM, OpenRouter, OpenAI.</summary>
    public const string OpenAiChat = "openai-chat";

    /// <summary>The Anthropic Messages API, <c>/v1/messages</c>.</summary>
    public const string AnthropicMessages = "anthropic-messages";

    /// <summary>A small delegate task answers (the subscription seat path).</summary>
    public const string Delegation = "delegation";

    /// <summary>One-shot <c>claude -p</c> with no tools on the local Claude Code sign-in: needs a model, no base URL and no key.</summary>
    public const string ClaudeCli = "claude-cli";
}

public sealed class ReviewCompletionProvider
{
    /// <summary>A <see cref="ReviewCompletionDialects"/> value, or a test dialect.</summary>
    public string Dialect { get; set; } = "";

    /// <summary>The model to ask for (a proxy alias or a pinned id); for a delegate seat, a label for the record.</summary>
    public string? Model { get; set; }

    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// The API root. <c>openai-chat</c> needs it (the LiteLLM proxy, the same root as Jev);
    /// <c>anthropic-messages</c> defaults to <c>https://api.anthropic.com</c>. A root ending in <c>/v1</c> is accepted.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The configuration key that holds the API key. Never the key itself.</summary>
    public string? ApiKeyConfigKey { get; set; }

    /// <summary>Resolved from <see cref="ApiKeyConfigKey"/> at startup by Program.cs. Never serialised or logged.</summary>
    [JsonIgnore]
    public string? ApiKey { get; set; }

    /// <summary><c>anthropic-messages</c>: <c>x-api-key</c> (default) or <c>bearer</c> for a gateway that takes a bearer token.</summary>
    public string? AuthScheme { get; set; }

    /// <summary><c>anthropic-messages</c>: the <c>anthropic-version</c> header; absent is 2026-10-21.</summary>
    public string? ApiVersion { get; set; }

    /// <summary>The output cap sent with the request; 0 sends none (<c>anthropic-messages</c> then sends 16000).</summary>
    public int MaxOutputTokens { get; set; }

    /// <summary><c>anthropic-messages</c>: <c>output_config.effort</c> (low, medium, high, xhigh, max); absent sends none.</summary>
    public string? Effort { get; set; }

    /// <summary>The price table when the response carries no cost header (USD per million tokens).</summary>
    public decimal InputPricePerMillionUsd { get; set; }

    public decimal OutputPricePerMillionUsd { get; set; }

    /// <summary><c>delegation</c>: the agent program of the answering task.</summary>
    public Domain.Enums.AgentKind? AgentKind { get; set; }

    /// <summary><c>delegation</c>: the task's level (Low).</summary>
    public Domain.Enums.AgentModelLevel? ModelLevel { get; set; }

    /// <summary><c>delegation</c>: the read-only task's directory; absent, a scratch directory under the server's temp path.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>True when an HTTP provider has its key, its model and, for <c>openai-chat</c>, its base URL.</summary>
    [JsonIgnore]
    public bool IsHttpReady => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Model)
        && (Dialect.Equals(ReviewCompletionDialects.AnthropicMessages, StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(BaseUrl));

    public override string ToString() => $"{Dialect} {BaseUrl} {Model}";
}

/// <summary>The review engine's own settings, section <c>Review</c>.</summary>
public sealed class ReviewSettings
{
    public const string SectionName = "Review";

    /// <summary>
    /// Enables <c>POST /api/review-runs/demo</c>. Absent, it follows the environment: on in
    /// Development, off elsewhere (Program.cs).
    /// </summary>
    public bool DemoSeedEnabled { get; set; }

    /// <summary>
    /// The review viewer while sign-in is off (api.md <c>GET /api/review-me</c>). Absent, the
    /// current user's name is used. Mutations record this viewer as the actor.
    /// </summary>
    public string? DefaultViewerLogin { get; set; }

    /// <summary>
    /// Lets the <c>X-Review-Viewer</c> header pick the viewer. Development only: absent, it follows
    /// the environment like <see cref="DemoSeedEnabled"/>.
    /// </summary>
    public bool AllowViewerHeader { get; set; }

    /// <summary>Display names and teams by login, until org sync exists (design 12).</summary>
    public Dictionary<string, ReviewViewerSettings> Viewers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The instance check catalogue: a folder of <c>&lt;category&gt;/&lt;id&gt;.md</c> check files (design 9).
    /// Repo checks at the base SHA override it by id; with neither, the built-in seed set is used.
    /// </summary>
    public string? InstanceChecksPath { get; set; }

    /// <summary>The Antiphon web root for run links in published reviews and commit statuses. An absent or unsafe URL omits the link.</summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Sign-in (<c>Review:Auth</c>); off by default.</summary>
    public ReviewAuthSettings Auth { get; set; } = new();
}

public sealed class ReviewViewerSettings
{
    public string? DisplayName { get; set; }
    public List<string> Teams { get; set; } = [];
}
