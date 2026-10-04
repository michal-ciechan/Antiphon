namespace Antiphon.SessionRunner;

public sealed class CodexCliVersionSettings
{
    public const string SectionName = "SessionRunner:CodexCliVersion";
    public string Executable { get; set; } = OperatingSystem.IsWindows() ? "codex.cmd" : "codex";
    public string? ResolutionCwd { get; set; }
    public double RefreshIntervalMinutes { get; set; } = 5;
    // Runner-local cache bound. Public display uses its own fixed fifteen-minute threshold.
    public int MaxAgeMinutes { get; set; } = 15;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Executable) || MaxAgeMinutes is < 1 or > 60
            || !double.IsFinite(RefreshIntervalMinutes) || RefreshIntervalMinutes <= 0
            || RefreshIntervalMinutes >= MaxAgeMinutes)
            throw new InvalidOperationException("Invalid Codex CLI version probe settings.");
    }
}
