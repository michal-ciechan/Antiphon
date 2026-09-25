using System.Runtime.InteropServices;

namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0710. Canonical platform strings shared by local capabilities, phone-home registration
/// and launch requests. Unknown operating systems are null, never inferred as Windows.
/// </summary>
public static class RunnerPlatformWire
{
    public const string Windows = "windows";
    public const string Linux = "linux";
    public const string Feature = "required-platform-v1";
    public const string DesktopId = "desktop";

    public static string? FromOperatingSystem()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Windows;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Linux;
        return null;
    }

    /// <summary>Canonical wire value, or null when the text is blank or not windows/linux.</summary>
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        if (trimmed.Equals(Windows, StringComparison.OrdinalIgnoreCase))
            return Windows;
        if (trimmed.Equals(Linux, StringComparison.OrdinalIgnoreCase))
            return Linux;
        return null;
    }

    public static bool IsSpecific(string? value) => Normalize(value) is not null;

    public static bool Matches(string? required, string? observed)
    {
        var want = Normalize(required);
        var have = Normalize(observed);
        return want is not null && have is not null && string.Equals(want, have, StringComparison.Ordinal);
    }
}
