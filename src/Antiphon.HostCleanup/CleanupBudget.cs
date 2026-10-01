namespace Antiphon.HostCleanup;

public sealed record CleanupLimits(
    int MaxAttempts = 10,
    long MaxReservedBytes = 10L * 1024 * 1024 * 1024,
    int MaxCandidates = 10_000,
    int MaxDescendantsPerCandidate = 100_000,
    TimeSpan? MaxPassDuration = null)
{
    public TimeSpan EffectiveMaxPassDuration => MaxPassDuration ?? TimeSpan.FromMinutes(5);

    public void Validate()
    {
        if (MaxAttempts <= 0 || MaxReservedBytes <= 0 || MaxCandidates <= 0 ||
            MaxDescendantsPerCandidate <= 0 || EffectiveMaxPassDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(CleanupLimits), "Cleanup limits must be positive.");
    }
}

public sealed class CleanupBudget
{
    private readonly CleanupLimits limits;
    public int Attempts { get; private set; }
    public long ReservedBytes { get; private set; }

    public CleanupBudget(CleanupLimits limits)
    {
        limits.Validate();
        this.limits = limits;
    }

    public bool TryReserve(long bytes, out string reason)
    {
        if (Attempts >= limits.MaxAttempts)
        {
            reason = "count_cap";
            return false;
        }
        if (bytes < 0 || bytes > limits.MaxReservedBytes - ReservedBytes)
        {
            reason = "byte_cap";
            return false;
        }
        Attempts++;
        ReservedBytes += bytes;
        reason = "eligible";
        return true;
    }
}
