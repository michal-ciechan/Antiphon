namespace Antiphon.Server.Domain.Enums;

/// <summary>CARD-0710. Which rule chose the task's host. Null on rows written before the card.</summary>
public enum RunnerSelectionSource
{
    Explicit = 0,
    ExistingProcess = 1,
    KindDefault = 2,
    GlobalDefault = 3,
    Fallback = 4,
}
