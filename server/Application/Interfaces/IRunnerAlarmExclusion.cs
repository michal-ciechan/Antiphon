namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0726 D-5: a reason this runner must not alarm (draining, retired), or null.
/// CARD-0727 fills the row-backed implementation; this card ships <c>NeverExcluded</c>.
/// </summary>
public interface IRunnerAlarmExclusion
{
    string? Excluded(string runnerId);
}
