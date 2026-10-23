using Antiphon.Agents.Pty;
namespace Antiphon.Server.Application.Services;

/// <summary>Shared rendered-grid RC recognition; no input authority is cached here.</summary>
public static class RemoteControlMenuScreen
{
    public const string HeadingLiteral = ClaudeRemoteControlMenu.HeadingLiteral;
    public const string DisconnectLiteral = ClaudeRemoteControlMenu.DisconnectLiteral;
    public const string ShowQrLiteral = ClaudeRemoteControlMenu.ShowQrLiteral;
    public const string ContinueLiteral = ClaudeRemoteControlMenu.ContinueLiteral;
    public const string FooterLiteral = ClaudeRemoteControlMenu.FooterLiteral;
    public readonly record struct Classification(bool IsPresent, bool HasRemnant)
    {
        public bool IsClear => !IsPresent && !HasRemnant;
    }
    public static Classification Classify(string? screen)
    {
        var result = ClaudeRemoteControlMenu.Classify(screen);
        return new(result.IsPresent, result.HasRemnant);
    }
    public static bool IsPresent(string? screen) => Classify(screen).IsPresent;
    public static bool HasRemnant(string? screen) => Classify(screen).HasRemnant;
    public static bool IsClear(string? screen) => Classify(screen).IsClear;
}