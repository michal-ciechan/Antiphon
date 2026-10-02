namespace Antiphon.Server.Application.Dtos;

/// <summary>Fail-closed proof that an interrupted adoption still has the old checkout.</summary>
public sealed record LandRecoveryCheckoutInspection(bool Accepted, string Reason)
{
    public static LandRecoveryCheckoutInspection Proven { get; } = new(true, "recovery_checkout_proven");
    public static LandRecoveryCheckoutInspection Refused(string reason) => new(false, reason);
}
