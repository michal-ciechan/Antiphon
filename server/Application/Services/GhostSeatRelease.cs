using Antiphon.Server.Application.Dtos;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1719. The recorded mirror on a Failed task, and the idle bound before a clean
/// origin-equal seat may leave through the runner slot release. MirrorPushed is not a gate:
/// settlement can record the commit unpushed while the mirror relation is already equal.
/// </summary>
internal static class GhostSeatRelease
{
    public const int IdleMinutes = 10;
    public const int MaxReleaseAttemptsPerSweep = 5;

    public enum MirrorDisposition
    {
        CleanEqual,
        Dirty,
        Unpushed,
        Unknown,
    }

    public static bool IdleElapsed(DateTime? since, DateTime now) =>
        since is DateTime at && now - at >= TimeSpan.FromMinutes(IdleMinutes);

    public static MirrorDisposition Read(RemoteSyncEvidence? sync)
    {
        if (sync is null || sync.MirrorDirty is null)
            return MirrorDisposition.Unknown;
        if (!string.IsNullOrWhiteSpace(sync.MirrorInspection)
            && !string.Equals(sync.MirrorInspection, "ok", StringComparison.OrdinalIgnoreCase))
            return MirrorDisposition.Unknown;
        if (string.IsNullOrWhiteSpace(sync.MirrorRelation)
            || string.Equals(sync.MirrorRelation, "unknown", StringComparison.OrdinalIgnoreCase))
            return MirrorDisposition.Unknown;
        if (sync.MirrorDirty == true)
            return MirrorDisposition.Dirty;
        return string.Equals(sync.MirrorRelation, "equal", StringComparison.OrdinalIgnoreCase)
            ? MirrorDisposition.CleanEqual
            : MirrorDisposition.Unpushed;
    }

    public static string AttentionToken(MirrorDisposition disposition) => disposition switch
    {
        MirrorDisposition.CleanEqual => "mirror=clean-equal",
        MirrorDisposition.Dirty => "mirror=dirty",
        MirrorDisposition.Unpushed => "mirror=unpushed",
        _ => "mirror=unknown",
    };
}
