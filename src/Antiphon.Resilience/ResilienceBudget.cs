namespace Antiphon.Resilience;

/// <summary>
/// One immutable deadline for a logical read. Nested calls and later pages reuse the same instance.
/// </summary>
public sealed class ResilienceBudget
{
    private readonly long _startedAt;
    private readonly TimeSpan _duration;

    private ResilienceBudget(TimeProvider time, TimeSpan duration)
    {
        Time = time;
        _startedAt = time.GetTimestamp();
        _duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }

    public TimeProvider Time { get; }

    public TimeSpan Duration => _duration;

    public TimeSpan Elapsed => Time.GetElapsedTime(_startedAt);

    public TimeSpan Remaining
    {
        get
        {
            var remaining = _duration - Elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public bool Expired => Remaining <= TimeSpan.Zero;

    public static ResilienceBudget Start(
        TimeProvider time,
        ResilienceSettings settings,
        string? profile,
        TimeSpan? ownerDeadline = null)
    {
        var duration = TimeSpan.FromSeconds(Math.Clamp(settings.TotalTimeoutSeconds, 1, ResilienceSettings.HardTotalTimeoutSeconds));
        if (ownerDeadline is { } owner && owner < duration)
            duration = owner < TimeSpan.Zero ? TimeSpan.Zero : owner;
        if (!string.IsNullOrEmpty(profile))
        {
            if (ResilienceProfiles.OwnerCaps.TryGetValue(profile, out var ownerCap) && ownerCap < duration)
                duration = ownerCap;
            if (settings.Profiles.TryGetValue(profile, out var configured))
            {
                if (configured.Enabled == false)
                    duration = TimeSpan.Zero;
                else if (configured.TotalTimeoutSeconds is int seconds)
                {
                    var narrowed = TimeSpan.FromSeconds(seconds);
                    if (narrowed < duration)
                        duration = narrowed;
                }
            }
        }

        return new ResilienceBudget(time, duration);
    }

    public TimeSpan AttemptTimeout(ResilienceSettings settings)
    {
        var attempt = TimeSpan.FromSeconds(Math.Max(1, settings.AttemptTimeoutSeconds));
        var remaining = Remaining;
        return attempt < remaining ? attempt : remaining;
    }
}
