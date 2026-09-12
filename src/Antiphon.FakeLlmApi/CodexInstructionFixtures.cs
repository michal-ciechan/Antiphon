namespace Antiphon.FakeLlmApi;

/// <summary>
/// CARD-0497: shared 8,366-character incident fixture (UTF-16) plus sentinels.
/// Both <c>Antiphon.Tests</c> and <c>Antiphon.SessionRunner.Tests</c> consume this.
/// </summary>
public static class CodexInstructionFixtures
{
    public const string StartSentinel = "C0497-START";
    public const string MidSentinel = "C0497-MID";
    public const string EndSentinel = "C0497-END";
    public const int IncidentUtf16Length = 8366;
    public const int ShortControlUtf16Length = 200;

    public static string Incident { get; } = BuildIncident();

    public static string ShortControl { get; } = BuildShortControl();

    static CodexInstructionFixtures()
    {
        if (Incident.Length != IncidentUtf16Length)
        {
            throw new InvalidOperationException(
                $"CodexInstructionFixtures.Incident length {Incident.Length} != {IncidentUtf16Length}.");
        }

        if (ShortControl.Length != ShortControlUtf16Length)
        {
            throw new InvalidOperationException(
                $"CodexInstructionFixtures.ShortControl length {ShortControl.Length} != {ShortControlUtf16Length}.");
        }
    }

    private static string BuildIncident()
    {
        // Quotes, backslashes, CR/LF, café (1 unit for é) and 😀 (2 UTF-16 units) plus the
        // three sentinels. Filler is ASCII so each remaining unit is one character.
        var prefix = StartSentinel + "\nquotes: \"double\" and \\path\\\r\nunicode: café 😀\n";
        var suffix = "\n" + EndSentinel;
        var remaining = IncidentUtf16Length - prefix.Length - suffix.Length - MidSentinel.Length;
        if (remaining < 2)
            throw new InvalidOperationException("Incident fixture hazards overflow the pinned length.");
        var left = remaining / 2;
        var right = remaining - left;
        return prefix + new string('A', left) + MidSentinel + new string('B', right) + suffix;
    }

    private static string BuildShortControl()
    {
        const string head = "C0497-SHORT ";
        return head + new string('x', ShortControlUtf16Length - head.Length);
    }
}
