namespace Antiphon.Server.Domain.Enums;

/// <summary>CARD-0079 publication lifecycle. Append-only.</summary>
public enum LegacyCheckNoteState
{
    Captured = 0,
    Produced = 1,
    Suppressed = 2,
}
