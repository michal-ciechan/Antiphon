namespace Antiphon.FakeGrok;

/// <summary>
/// Opt-in collapse of a bracketed paste to <c>[Pasted text #N +M lines]</c>. Default off.
/// </summary>
internal sealed class PastePlaceholderModel
{
    public const string ModeVar = "ANTIPHON_FAKE_PASTE_PLACEHOLDER";
    public const string MinLinesVar = "ANTIPHON_FAKE_PASTE_PLACEHOLDER_MIN_LINES";

    private int _pasteIndex;

    public int MinLines { get; }

    internal PastePlaceholderModel(int minLines = 6) => MinLines = Math.Max(1, minLines);

    public static PastePlaceholderModel? FromEnvironment()
    {
        var mode = (Environment.GetEnvironmentVariable(ModeVar) ?? string.Empty).Trim().ToLowerInvariant();
        if (mode is "" or "0" or "off" or "false") return null;
        if (mode is not ("1" or "on" or "true" or "yes"))
            throw new ArgumentException($"{ModeVar}='{mode}' is not a mode. Use 1 (on) or leave it unset.");

        return new PastePlaceholderModel(
            int.TryParse(Environment.GetEnvironmentVariable(MinLinesVar), out var n) && n > 0 ? n : 6);
    }

    public string Describe() => $"PASTEPLACEHOLDER:minLines={MinLines}";

    public string Render(string pasted)
    {
        var lines = pasted.TrimEnd('\n').Split('\n').Length;
        if (lines < MinLines)
            return pasted.Replace("\n", "\r\n");

        return $"[Pasted text #{++_pasteIndex} +{lines - 1} lines]";
    }
}
