namespace Antiphon.Server.Application.Dtos;

/// <summary>Proof that an interrupted adoption still has the pinned old checkout.</summary>
public sealed record LandRecoveryCheckoutInspection(bool Accepted, string? Reason = null)
{
    public static LandRecoveryCheckoutInspection Refused(string reason = "recovery_checkout_unproven") => new(false, reason);
}
