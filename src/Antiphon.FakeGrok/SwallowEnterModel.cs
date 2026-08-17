namespace Antiphon.FakeGrok;

/// <summary>
/// Opt-in model of a submitting Enter that does not submit while the screen still redraws.
/// Same contract as FakeClaude: the composer keeps the body so an Enter-only retry is safe.
/// Unset / <c>0</c> = submit normally.
/// </summary>
internal sealed class SwallowEnterModel
{
    public const string CountVar = "ANTIPHON_FAKE_SWALLOW_ENTER";

    private int _remaining;

    internal SwallowEnterModel(int count) => _remaining = Math.Max(0, count);

    public int Remaining => _remaining;

    public static SwallowEnterModel? FromEnvironment()
    {
        var raw = (Environment.GetEnvironmentVariable(CountVar) ?? string.Empty).Trim();
        if (raw.Length == 0 || raw == "0")
            return null;
        if (!int.TryParse(raw, out var count) || count < 0)
        {
            throw new ArgumentException(
                $"{CountVar}='{raw}' is not a count. Use a non-negative integer or leave it unset.");
        }

        return count == 0 ? null : new SwallowEnterModel(count);
    }

    public string Describe() => $"SWALLOWENTER:count={_remaining}";

    public bool ShouldSwallow()
    {
        if (_remaining <= 0)
            return false;

        _remaining--;
        return true;
    }
}
