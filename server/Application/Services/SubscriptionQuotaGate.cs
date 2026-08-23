using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0136: one rule, two HTTP hooks. Reads the latest sample and either passes, refuses
/// with <see cref="SubscriptionQuotaLowException"/>, or returns the tripped verdict so the
/// caller can record an explicit override. Never throws on missing or stale data.
/// </summary>
public sealed class SubscriptionQuotaGate
{
    private readonly SubscriptionUsageReader _reader;
    private readonly SubscriptionQuotaGateSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionQuotaGate> _logger;

    public SubscriptionQuotaGate(
        SubscriptionUsageReader reader,
        IOptions<SubscriptionQuotaGateSettings> settings,
        TimeProvider time,
        ILogger<SubscriptionQuotaGate> logger)
    {
        _reader = reader;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Null = pass. Never throws on missing/stale data.</summary>
    public async Task<SubscriptionQuotaVerdict?> EvaluateAsync(
        AgentKind provider, string subscriptionKey, CancellationToken ct)
    {
        if (!_settings.Enabled)
            return null;

        var snapshot = await _reader.GetLatestAsync(provider, subscriptionKey, ct);
        var now = _time.GetUtcNow().UtcDateTime;
        if (snapshot is not null
            && snapshot.Age > TimeSpan.FromMinutes(_settings.MaxSampleAgeMinutes))
        {
            _logger.LogDebug(
                "Subscription quota sample for {Provider}/{Key} is stale (age {Age}); passing through",
                provider, subscriptionKey, snapshot.Age);
        }

        return SubscriptionQuotaPolicy.Evaluate(snapshot, _settings, now);
    }

    /// <summary>
    /// Throws <see cref="SubscriptionQuotaLowException"/> unless <paramref name="ignore"/>;
    /// returns the verdict (null = clean pass, non-null = tripped-but-overridden) so the
    /// caller can record the override.
    /// </summary>
    public async Task<SubscriptionQuotaVerdict?> EnforceAsync(
        AgentKind provider,
        string subscriptionKey,
        bool ignore,
        string launchDescription,
        CancellationToken ct)
    {
        var verdict = await EvaluateAsync(provider, subscriptionKey, ct);
        if (verdict is null)
            return null;

        if (ignore)
        {
            _logger.LogWarning(
                "Subscription quota override: {Launch} on {Provider}/{Key} at {Percent}% remaining, resets in {Reset}",
                launchDescription,
                verdict.Provider,
                verdict.SubscriptionKey,
                SubscriptionQuotaPolicy.FormatPercent(verdict.RemainingPercent),
                SubscriptionQuotaPolicy.FormatTimeToReset(verdict.TimeToReset));
            return verdict;
        }

        _logger.LogInformation(
            "Refusing {Launch}: {Sentence}",
            launchDescription,
            SubscriptionQuotaPolicy.FormatSentence(verdict));
        throw new SubscriptionQuotaLowException(verdict);
    }
}
