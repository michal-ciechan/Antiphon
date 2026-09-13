namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// Optional Claude native-import enhancement only (CARD-0262 D4). Never gates mandatory
/// per-agent files. Default <see cref="Unverified"/>.
/// </summary>
public enum PinClaudeImportMode
{
    Unverified = 0,
    Dedicated = 1,
    Disabled = 2
}
