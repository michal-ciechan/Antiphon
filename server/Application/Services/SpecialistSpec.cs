using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Identity of one standing specialist seat (CARD-0330 D3 / CARD-0352 D2). The provisioner and
/// the runner take this; the Check / Distill / Diagnose facades each supply one.
/// </summary>
public sealed record SpecialistSpec(
    AgentTaskRole Role,
    string Slug,
    string WorkingDirectory,
    string Details,
    string BundleKey,
    string ContractVersion,
    string DenyHookStderr,
    AgentIncidentKind UnavailableIncidentKind,
    string DisplayName)
{
    /// <summary>The standing contract text, forwarded from the bundle catalog.</summary>
    public string Contract => InstructionBundles.TextOf(BundleKey);

    /// <summary>
    /// A deny-all <c>PreToolUse</c> hook — the hard half of "use no tools". Same JSON shape as
    /// the check interpreter's original constant; only the stderr line is per-seat.
    /// </summary>
    public string DenyAllToolsSettingsJson => BuildDenyAllToolsSettingsJson(DenyHookStderr);

    /// <summary>Where the hook file goes, relative to the specialist's working directory.</summary>
    public const string DenyHookRelativePath = ".claude/settings.json";

    public static string BuildDenyAllToolsSettingsJson(string stderr) =>
        $$"""
        {
          "hooks": {
            "PreToolUse": [
              {
                "matcher": "*",
                "hooks": [
                  {
                    "type": "command",
                    "command": "powershell -NoProfile -Command \"[Console]::Error.WriteLine('{{stderr.Replace("'", "''")}}'); exit 2\""
                  }
                ]
              }
            ]
          }
        }
        """;
}
