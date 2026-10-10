namespace Antiphon.Server.Domain.Entities;

/// <summary>
/// CARD-0822. One fleet row. The version is the body hash; the revision counts successful writes.
/// A failed write leaves the last good body in place.
/// </summary>
public sealed class OrchestratorInstructionsState
{
    public const string FleetId = "fleet";

    public string Id { get; set; } = FleetId;

    public long Revision { get; set; }

    /// <summary>First 8 hex of the rendered body. Not the stamp line.</summary>
    public string Version { get; set; } = "";

    /// <summary>Rendered Markdown without the stamp line.</summary>
    public string Body { get; set; } = "";

    public string SnapshotJson { get; set; } = "";

    public string? PreviousSnapshotJson { get; set; }

    public DateTime WrittenAt { get; set; }

    public string WrittenPath { get; set; } = "";

    public string? LastReason { get; set; }

    public string? LastWriteError { get; set; }
}
