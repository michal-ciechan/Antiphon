namespace Antiphon.Server.Application.Settings;

/// <summary>CARD-0179 Report-bug bundle limits and log locations.</summary>
public sealed class DiagnosticsSettings
{
    public const int DefaultMaxScreenshotBytes = 8 * 1024 * 1024;
    public const int DefaultMaxLogBytes = 2 * 1024 * 1024;
    public const int DefaultMaxLogLines = 2000;
    public const int DefaultMaxConsoleEntries = 200;

    /// <summary>Relative to content root unless rooted. Default matches Serilog:LogPath.</summary>
    public string ServerLogDirectory { get; set; } = "logs";

    public string ServerLogPattern { get; set; } = "antiphon-*.log";

    /// <summary>Default matches the session-runner Serilog path under %TEMP%.</summary>
    public string RunnerLogDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "antiphon-logs");

    public string RunnerLogPattern { get; set; } = "session-runner-*.log";

    public int MaxLogLines { get; set; } = DefaultMaxLogLines;

    public int MaxLogBytes { get; set; } = DefaultMaxLogBytes;

    public int MaxScreenshotBytes { get; set; } = DefaultMaxScreenshotBytes;

    public int MaxConsoleEntries { get; set; } = DefaultMaxConsoleEntries;
}
