namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// CARD-0544 D-1/D-2. The ordinary-verification contract a Code or Review task was commissioned
/// under. Final is the full affected sweep and the default; Interim is an explicit, card-opted-in,
/// baseline-bound reduced repair round that can never approve a land.
/// </summary>
public enum VerificationRound
{
    Final = 0,
    Interim = 1,
}

/// <summary>
/// CARD-0544 D-5. What ordinary scope a Review report declared it actually executed, capped by the
/// commissioned round. Null on historical rows; <see cref="Unknown"/> for a missing, duplicate or
/// malformed declaration. Neither can establish a baseline or approve a latched owner.
/// </summary>
public enum VerificationScope
{
    Unknown = 0,
    None = 1,
    Interim = 2,
    Full = 3,
}
