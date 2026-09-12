namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0502: the accepted launch timestamp is an opaque equality token
/// (<c>(SessionId, AcceptedStartedAt)</c>), stored at PostgreSQL microsecond precision.
/// It is not a clock-skew or elapsed-time test.
/// </summary>
public static class SessionGeneration
{
    public const long MicrosecondTicks = 10;

    public const string NotEchoed = "session_generation_not_echoed";
    public const string BindingMismatch = "session_generation_binding_mismatch";

    public static DateTime Normalize(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
        return new DateTime(utc.Ticks - utc.Ticks % MicrosecondTicks, DateTimeKind.Utc);
    }

    /// <summary>
    /// Same-row resume generation: <c>max(normalized now, prior generation + 1 microsecond)</c>.
    /// </summary>
    public static DateTime Next(DateTime prior, DateTime now)
    {
        var normalizedNow = Normalize(now);
        var min = Normalize(prior).AddTicks(MicrosecondTicks);
        return normalizedNow > min ? normalizedNow : min;
    }

    public static bool Equal(DateTime? left, DateTime? right)
    {
        if (left is null || right is null)
            return false;
        return Normalize(left.Value) == Normalize(right.Value);
    }

    public static int Compare(DateTime left, DateTime right) =>
        Normalize(left).CompareTo(Normalize(right));
}

public static class KillGenerationOutcomes
{
    public const string Killed = "killed";
    public const string Mismatch = "mismatch";
    public const string Missing = "missing";
    public const string AlreadyExited = "already-exited";
}

public sealed record RunnerKillGenerationRequest(DateTime ExpectedAcceptedStartedAt);

public sealed record RunnerKillGenerationResult(
    Guid SessionId,
    bool Killed,
    string Outcome,
    DateTime? AcceptedStartedAt = null);
