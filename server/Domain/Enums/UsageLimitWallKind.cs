namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Subclass of a structurally-classified Wall (CARD-0022). The classifier stays
/// <see cref="ApiErrorClassification.Wall"/>; this second parse decides recovery.
/// </summary>
public enum UsageLimitWallKind
{
    /// <summary>Stub states a reset time. Pause the model until that instant (+ padding).</summary>
    SessionLimit = 0,

    /// <summary>Per-model cap, or an unparseable reset. Pause until cleared. No timed resume.</summary>
    ModelCap = 1,
}
