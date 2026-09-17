namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0544 D-1. Per-card, per-role permission to REQUEST an Interim round. It never makes Interim
/// the default; FullOnly on every new, imported and historical card.
/// </summary>
public enum CardVerificationPolicy
{
    FullOnly = 0,
    AllowInterim = 1,
}
