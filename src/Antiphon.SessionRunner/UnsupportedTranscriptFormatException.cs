namespace Antiphon.SessionRunner;

/// <summary>Returned as HTTP 400 when a launch names a tailer this runner binary does not contain.</summary>
public sealed class UnsupportedTranscriptFormatException : ArgumentException
{
    public UnsupportedTranscriptFormatException(string requested, IReadOnlyList<string> supported)
        : base($"Unsupported transcript format '{requested}'. This session runner supports: {string.Join(", ", supported)}.")
    {
    }
}
