namespace Antiphon.Server.Application.Interfaces;

public sealed record RunnerEligibilitySnapshot(
    string RunnerId,
    string DisplayName,
    bool Enabled,
    bool Eligible,
    string? DisconnectReason,
    DateTimeOffset? LastDisconnectAtUtc,
    long Reconnects);

/// <summary>CARD-0726 D-2: one row per configured phone-home slot, re-read on every wake.</summary>
public interface IRunnerEligibilitySnapshotSource
{
    IReadOnlyList<RunnerEligibilitySnapshot> Snapshots();
}
