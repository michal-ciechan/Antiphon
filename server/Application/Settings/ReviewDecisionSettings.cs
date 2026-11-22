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
    /// <summary>A <see cref="JevDialects"/> value, or <c>seat</c>.</summary>
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
/// There are no defaults: until a chain is configured, a completion node (verify) is skipped and says so.
/// </summary>
public sealed class ReviewCompletionSettings
{
    public const string SectionName = "Review:Completions";

    public Dictionary<string, ReviewCompletionProvider> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Chains by name, for example <c>completions-cheap</c>: ordered provider entries.</summary>
    public Dictionary<string, List<ReviewDecisionChainEntry>> Chains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ReviewCompletionProvider
{
    /// <summary>The dialect an <c>ICompletionModel</c> serves, for example <c>anthropic-messages</c> or <c>seat</c>.</summary>
    public string Dialect { get; set; } = "";

    public string? Model { get; set; }

    public int TimeoutSeconds { get; set; } = 120;

    public override string ToString() => $"{Dialect} {Model}";
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
    /// The review viewer until identity lands (api.md <c>GET /api/review-me</c>). Absent, the
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

    /// <summary>The Antiphon web root that commit statuses link to (<c>{root}/reviews/{runId}</c>); absent, no link.</summary>
    public string? PublicBaseUrl { get; set; }
}

public sealed class ReviewViewerSettings
{
    public string? DisplayName { get; set; }
    public List<string> Teams { get; set; } = [];
}
