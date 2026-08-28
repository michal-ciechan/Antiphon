namespace Antiphon.Server.Application.Settings;

/// <summary>Bounds the intentionally small card representations used by list surfaces.</summary>
public sealed class CardsSettings
{
    public int SummaryPreviewChars { get; set; } = 200;
    public int MaxListResults { get; set; } = 500;
}
