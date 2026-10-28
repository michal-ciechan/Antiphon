namespace Antiphon.Server.Application.Services;

/// <summary>
/// What a consume writes. <see cref="Launch"/> is the claim receipt.
/// <see cref="Admitted"/> writes the same row <c>RedeemAsync</c> writes, and no launch receipt.
/// </summary>
public enum CapacityConsumeReceipt
{
    Launch = 0,
    Admitted = 1,
}
