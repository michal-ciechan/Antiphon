namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-0497: runner 409 problem types for Codex launch refusals.</summary>
public static class CodexLaunchProblemTypes
{
    public const string CommandLineTooLong = "codex_command_line_too_long";
    public const string LauncherUnavailable = "codex_launcher_unavailable";
    public const string LauncherUnsupported = "codex_launcher_unsupported";

    /// <summary>Windows CreateProcessW-shaped ceiling the runner will never raise.</summary>
    public const int TransportCeilingChars = 30_000;

    /// <summary>
    /// Conservative remaining-batch ceiling (8,191 minus 1,191 reserve). Not a safe payload length.
    /// </summary>
    public const int BatchCeilingChars = 7_000;
}
