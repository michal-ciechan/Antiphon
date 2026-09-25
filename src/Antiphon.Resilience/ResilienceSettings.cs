using Microsoft.Extensions.Options;

namespace Antiphon.Resilience;

public sealed class ResilienceSettings
{
    public const string SectionName = "Resilience";
    public const int HardTotalTimeoutSeconds = 120;

    public bool Enabled { get; set; } = true;

    public int TotalTimeoutSeconds { get; set; } = HardTotalTimeoutSeconds;

    public int AttemptTimeoutSeconds { get; set; } = 10;

    public int BaseDelayMilliseconds { get; set; } = 250;

    public int MaxDelayMilliseconds { get; set; } = 5_000;

    public int MaxRetryAttempts { get; set; } = 6;

    public bool UseJitter { get; set; } = true;

    public ResilienceCircuitBreakerSettings CircuitBreaker { get; set; } = new();

    public ResilienceHttpSettings Http { get; set; } = new();

    public ResilienceDatabaseSettings Database { get; set; } = new();

    public Dictionary<string, ResilienceProfileSettings> Profiles { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ResilienceCircuitBreakerSettings
{
    public double FailureRatio { get; set; } = 0.5;

    public int MinimumThroughput { get; set; } = 10;

    public int SamplingDurationSeconds { get; set; } = 30;

    public int BreakDurationSeconds { get; set; } = 15;
}

public sealed class ResilienceHttpSettings
{
    public const int DefaultMaxConcurrentAttempts = 32;

    public static readonly int[] DefaultStatusCodes = [408, 429, 502, 503, 504];

    public static readonly string[] DefaultSocketErrors =
    [
        "ConnectionRefused",
        "ConnectionReset",
        "ConnectionAborted",
        "TimedOut",
        "NetworkDown",
        "NetworkUnreachable",
        "HostDown",
        "HostUnreachable",
        "TryAgain",
    ];

    public int MaxConcurrentAttempts { get; set; } = DefaultMaxConcurrentAttempts;

    public int[] AllowedStatusCodes { get; set; } = [.. DefaultStatusCodes];

    public string[] AllowedSocketErrors { get; set; } = [.. DefaultSocketErrors];

    public bool HonorRetryAfter { get; set; } = true;

    public int MaxAuthorityPipelines { get; set; } = 128;
}

public sealed class ResilienceDatabaseSettings
{
    public const int DefaultMaxConcurrentAttempts = 8;

    public static readonly string[] DefaultSqlStates =
    [
        "08000",
        "08001",
        "08003",
        "08006",
        "57P01",
        "57P02",
        "57P03",
        "53300",
        "40001",
        "40P01",
    ];

    public int MaxConcurrentAttempts { get; set; } = DefaultMaxConcurrentAttempts;

    public string[] AllowedSqlStates { get; set; } = [.. DefaultSqlStates];
}

public sealed class ResilienceProfileSettings
{
    public int? TotalTimeoutSeconds { get; set; }

    public int? AttemptTimeoutSeconds { get; set; }

    public bool? Enabled { get; set; }
}

public sealed class ResilienceSettingsValidator : IValidateOptions<ResilienceSettings>
{
    public ValidateOptionsResult Validate(string? name, ResilienceSettings options)
    {
        var failures = new List<string>();
        if (options.TotalTimeoutSeconds is < 1 or > ResilienceSettings.HardTotalTimeoutSeconds)
            failures.Add("Resilience:TotalTimeoutSeconds must be an integer from 1 to 120.");
        if (options.AttemptTimeoutSeconds < 1 || options.AttemptTimeoutSeconds > options.TotalTimeoutSeconds)
            failures.Add("Resilience:AttemptTimeoutSeconds must be positive and not greater than TotalTimeoutSeconds.");
        if (options.BaseDelayMilliseconds < 1 || options.BaseDelayMilliseconds > options.MaxDelayMilliseconds)
            failures.Add("Resilience:BaseDelayMilliseconds must be positive and not greater than MaxDelayMilliseconds.");
        if (options.MaxDelayMilliseconds < 1
            || options.MaxDelayMilliseconds > options.TotalTimeoutSeconds * 1000)
            failures.Add("Resilience:MaxDelayMilliseconds must be positive and not greater than the total timeout.");
        if (options.MaxRetryAttempts is < 1 or > 100)
            failures.Add("Resilience:MaxRetryAttempts must be from 1 to 100. Disable with Resilience:Enabled.");
        if (!options.UseJitter)
            failures.Add("Resilience:UseJitter must be true.");

        var breaker = options.CircuitBreaker ?? new ResilienceCircuitBreakerSettings();
        if (breaker.FailureRatio is <= 0 or > 1)
            failures.Add("Resilience:CircuitBreaker:FailureRatio must be greater than 0 and at most 1.");
        if (breaker.MinimumThroughput < 2)
            failures.Add("Resilience:CircuitBreaker:MinimumThroughput must be at least 2.");
        if (breaker.SamplingDurationSeconds < 1
            || breaker.SamplingDurationSeconds < options.AttemptTimeoutSeconds * 2)
            failures.Add("Resilience:CircuitBreaker:SamplingDurationSeconds must be positive and at least twice AttemptTimeoutSeconds.");
        if (breaker.BreakDurationSeconds is < 1 or > ResilienceSettings.HardTotalTimeoutSeconds)
            failures.Add("Resilience:CircuitBreaker:BreakDurationSeconds must be from 1 to 120.");

        var http = options.Http ?? new ResilienceHttpSettings();
        if (http.MaxConcurrentAttempts < 1)
            failures.Add("Resilience:Http:MaxConcurrentAttempts must be positive.");
        if (http.MaxAuthorityPipelines < 1)
            failures.Add("Resilience:Http:MaxAuthorityPipelines must be a positive integer.");
        RejectUnknown(failures, "Resilience:Http:AllowedStatusCodes", http.AllowedStatusCodes.Select(static code => code.ToString()),
            ResilienceHttpSettings.DefaultStatusCodes.Select(static code => code.ToString()));
        RejectUnknown(failures, "Resilience:Http:AllowedSocketErrors", http.AllowedSocketErrors, ResilienceHttpSettings.DefaultSocketErrors);

        var database = options.Database ?? new ResilienceDatabaseSettings();
        if (database.MaxConcurrentAttempts < 1)
            failures.Add("Resilience:Database:MaxConcurrentAttempts must be positive.");
        RejectUnknown(failures, "Resilience:Database:AllowedSqlStates", database.AllowedSqlStates, ResilienceDatabaseSettings.DefaultSqlStates);

        foreach (var (profileName, profile) in options.Profiles)
        {
            if (!ResilienceProfiles.IsKnown(profileName))
            {
                failures.Add($"Resilience:Profiles:{profileName} is not a known profile.");
                continue;
            }

            var owner = ResilienceProfiles.OwnerCaps[profileName];
            if (profile.TotalTimeoutSeconds is int total
                && (total < 1 || total > Math.Min(options.TotalTimeoutSeconds, (int)owner.TotalSeconds)))
            {
                failures.Add($"Resilience:Profiles:{profileName}:TotalTimeoutSeconds must narrow the parent and the owner deadline.");
            }

            if (profile.AttemptTimeoutSeconds is int attempt
                && (attempt < 1 || attempt > (profile.TotalTimeoutSeconds ?? options.TotalTimeoutSeconds)))
            {
                failures.Add($"Resilience:Profiles:{profileName}:AttemptTimeoutSeconds must be positive and within the profile budget.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RejectUnknown(
        List<string> failures,
        string key,
        IEnumerable<string>? configured,
        IEnumerable<string> allowed)
    {
        if (configured is null)
            return;
        var permitted = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var entry in configured)
        {
            if (!permitted.Contains(entry))
                failures.Add($"{key} cannot add '{entry}'. Configuration may only remove approved entries.");
        }
    }
}
