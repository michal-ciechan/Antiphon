namespace Antiphon.Server.Domain.Enums;

/// <summary>Optional native-import state, independent of mandatory file projection.</summary>
public enum PinImportStatus
{
    None = 0,
    PendingNextStart = 1,
    Ready = 2,
    TargetTracked = 3,
    TargetNotIgnored = 4,
    ScopeConflict = 5,
    Failed = 6
}
