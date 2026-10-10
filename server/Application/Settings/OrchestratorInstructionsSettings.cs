namespace Antiphon.Server.Application.Settings;

/// <summary>
/// CARD-0822. Fleet-wide live orchestrator instructions file. Restart to change;
/// <c>DelegationSettings</c> stays <c>IOptions</c>.
/// </summary>
public sealed class OrchestratorInstructionsSettings
{
    public const int DefaultMaxBytes = 16_384;
    public const int MinMaxBytes = 4_096;
    public const int MaxMaxBytes = 65_536;
    public const int DefaultSweepSeconds = 60;
    public const int MinSweepSeconds = 10;
    public const int MaxSweepSeconds = 3_600;
    public const int MaxStandingLines = 10;
    public const int MaxStandingLineChars = 300;

    /// <summary>False disables generation, notices, and the launch env vars together.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Absolute override. Empty uses the platform data root.</summary>
    public string Path { get; set; } = "";

    public int MaxBytes { get; set; } = DefaultMaxBytes;

    public int SweepSeconds { get; set; } = DefaultSweepSeconds;

    public OrchestratorInstructionsNotify Notify { get; set; } = OrchestratorInstructionsNotify.All;

    /// <summary>At most 10 lines of 300 characters. No <c>{{key:</c> placeholders.</summary>
    public List<string> StandingInstructions { get; set; } = [];
}

/// <summary>Who receives a settings-changed note. <see cref="All"/> is the default.</summary>
public enum OrchestratorInstructionsNotify
{
    /// <summary>Standing orchestrators and orchestrator task sessions.</summary>
    All = 0,

    /// <summary>Standing orchestrators only.</summary>
    Standing = 1,

    /// <summary>Write the file; queue no note.</summary>
    Off = 2,
}
