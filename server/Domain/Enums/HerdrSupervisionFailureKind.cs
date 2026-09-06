namespace Antiphon.Server.Domain.Enums;

/// <summary>Classified terminal evidence for one Herdr process attempt; null evidence is unknown.</summary>
public enum HerdrSupervisionFailureKind
{
    NonQualifying = 0,
    PaneClosed = 1,
    ChildGone = 2,
    DetectTimeout = 3,
}
