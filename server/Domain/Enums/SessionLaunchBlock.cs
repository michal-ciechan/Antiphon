namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Why a session failed its ready wait, when the adapter named it (CARD-0324).
/// Null on the row means no block was recorded (every pre-existing session, and every
/// launch that became ready).
/// </summary>
public enum SessionLaunchBlock
{
    None = 0,
    ProviderSignInRequired = 1,
    TrustDialogNotCleared = 2,
}
