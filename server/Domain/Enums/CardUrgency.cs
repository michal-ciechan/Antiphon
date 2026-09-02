namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Human rating of the cost of delay. A near or passed <c>DueAt</c> can escalate this at read
/// time (<c>CardRanking.EffectiveUrgency</c>); the stored value does not decay on its own.
/// Integer order is higher = more, and never appears on the wire. Default is <see cref="Normal"/>.
/// </summary>
public enum CardUrgency
{
    Normal = 0,
    Soon = 1,
    Now = 2
}
