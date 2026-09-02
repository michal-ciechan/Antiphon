namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Eisenhower cell of a card, derived from importance and effective urgency. Named <c>Clear</c>
/// rather than Delegate — that word already means something else in this codebase.
/// </summary>
public enum CardQuadrant
{
    DoFirst = 0,
    Schedule = 1,
    Clear = 2,
    Someday = 3
}
