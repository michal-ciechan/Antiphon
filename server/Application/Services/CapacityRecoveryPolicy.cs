using System.Security.Cryptography;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>Pure CARD-0412 admission policy. No clock, no database.</summary>
public static class CapacityRecoveryPolicy
{
    public const int DefaultAdmissionIntervalSeconds = 60;
    public const int DefaultJitterSeconds = 30;
    public const int DefaultMaxEpisodeAttempts = 3;
    public const int DefaultBatchSize = 100;

    public static string ActionKey(Guid waitId, int ordinal) =>
        $"{waitId:N}:{ordinal}";

    public static bool IsUnfinished(CapacityRecoveryWaitState state) =>
        state is not CapacityRecoveryWaitState.Progressed
            and not CapacityRecoveryWaitState.Canceled
            and not CapacityRecoveryWaitState.Superseded
            and not CapacityRecoveryWaitState.Exhausted;

    /// <summary>
    /// Grant candidates: Ready, ActionPending, and Admitted-awaiting-revalidation (N-3).
    /// </summary>
    public static bool IsGrantCandidate(CapacityRecoveryWait wait) =>
        wait.State is CapacityRecoveryWaitState.Ready
            or CapacityRecoveryWaitState.ActionPending
        || (wait.State == CapacityRecoveryWaitState.Admitted && wait.NeedsRevalidationGrant);

    public static bool IsRetainedCapacityWait(AgentTask task) =>
        task.Status == AgentTaskStatus.Working
        && task.CapacityWaitRetained
        && task.CapacityWaitId is not null;

    public static int StableJitterSeconds(Guid waitId, int actionOrdinal, int jitterSeconds)
    {
        if (jitterSeconds <= 0)
            return 0;
        Span<byte> payload = stackalloc byte[20];
        waitId.ToByteArray().CopyTo(payload);
        BitConverter.TryWriteBytes(payload[16..], actionOrdinal);
        var hash = SHA256.HashData(payload);
        var n = BitConverter.ToUInt32(hash, 0);
        return (int)(n % ((uint)jitterSeconds + 1));
    }

    public static DateTime EligibleAt(
        DateTime latestRequiredClearOrAvailability,
        Guid waitId,
        int actionOrdinal,
        int jitterSeconds,
        DateTime? laterCrashOrPrerequisiteDeadline = null)
    {
        var jitter = TimeSpan.FromSeconds(StableJitterSeconds(waitId, actionOrdinal, jitterSeconds));
        var due = latestRequiredClearOrAvailability + jitter;
        if (laterCrashOrPrerequisiteDeadline is { } extra && extra > due)
            return extra;
        return due;
    }

    public static int CompareGrantOrder(CapacityRecoveryWait a, CapacityRecoveryWait b)
    {
        var blocked = a.BlockedAt.CompareTo(b.BlockedAt);
        return blocked != 0 ? blocked : a.Id.CompareTo(b.Id);
    }

    public static bool AttemptWouldExhaust(int admissionCount, int maxAttempts) =>
        admissionCount >= maxAttempts;

    /// <summary>
    /// How long an Admitted wait may sit without progress before reconciliation re-arms it,
    /// and how long an unredeemed grant may reserve the provider before being re-armed.
    /// Two admission intervals allow a slower consumer to run while bounding the time a
    /// disappeared consumer can block every other wait for the provider.
    /// </summary>
    public const int StalledAdmissionIntervalMultiplier = 2;

    public static TimeSpan StalledAdmissionTimeout(int admissionIntervalSeconds) =>
        TimeSpan.FromSeconds(Math.Max(1, admissionIntervalSeconds) * StalledAdmissionIntervalMultiplier);

    public static bool HasExecutionReceipt(CapacityRecoveryWait wait) =>
        wait.SelectedMessageId is not null
        || wait.LaunchSessionId is not null
        || wait.LaunchReceipt is not null
        || wait.DispatchAttemptId is not null;

    public static CapacityRecoverySettings ValidateOrThrow(CapacityRecoverySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.AdmissionIntervalSeconds is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(settings), "AdmissionIntervalSeconds must be 1-3600.");
        if (settings.JitterSeconds is < 0 or > 300)
            throw new ArgumentOutOfRangeException(nameof(settings), "JitterSeconds must be 0-300.");
        if (settings.MaxEpisodeAttempts is int attempts && attempts is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(settings), "MaxEpisodeAttempts must be 1-10.");
        if (settings.ReconciliationBatchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(settings), "ReconciliationBatchSize must be 1-1000.");
        return settings;
    }

    public static int EffectiveMaxAttempts(CapacityRecoverySettings settings, int wallDeathCap) =>
        settings.EffectiveMaxEpisodeAttempts(wallDeathCap);
}
