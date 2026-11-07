namespace Antiphon.IO.WriteBehind;

/// <summary>Settings for a <see cref="WriteBehindFileSystem"/>.</summary>
public sealed class WriteBehindOptions
{
    /// <summary>
    /// Land each write as a temporary file renamed over the target, so another process reading the file sees
    /// the old or the new content, never a partial file. Default true.
    /// </summary>
    public bool AtomicReplace { get; init; } = true;

    /// <summary>
    /// Run the writer on a foreground thread, so the process does not exit while writes are queued.
    /// Default true. A crash or a killed process still loses the queued writes.
    /// </summary>
    public bool KeepProcessAlive { get; init; } = true;

    /// <summary>How paths are compared. Default: case-insensitive on Windows, ordinal elsewhere.</summary>
    public StringComparer PathComparer { get; init; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Called on the writer thread when a write fails. The failure is also raised by the next Flush.</summary>
    public Action<WriteBehindException>? OnWriteFailed { get; init; }
}

/// <summary>A queued write that failed to land.</summary>
public sealed class WriteBehindException : IOException
{
    /// <summary>Creates the failure for one path.</summary>
    public WriteBehindException(string path, Exception inner)
        : base("Write-behind write of '" + path + "' failed: " + inner.Message, inner)
    {
        Path = path;
    }

    /// <summary>The full path whose write failed.</summary>
    public string Path { get; }
}
