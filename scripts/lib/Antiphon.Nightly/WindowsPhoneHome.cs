// CARD-0716, CARD-1221: the Windows phone-home runner's configuration as the desktop AppHost commands see it (unit 31 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). Only the launch refusal is here so far: it is
// Get-WindowsPhoneHomeLaunchRefusal of scripts/lib/windows-phone-home-env.ps1, which restart-apphost used. That library
// stays for its other callers (autostart-session-runner.ps1, start-windows-phone-home-runner.ps1,
// desktop-win-phase1-secrets.ps1 and dev-aspire.ps1) until their own unit, which grows this class with the rest of it; the
// refusal text is the same in both until then. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;

namespace Antiphon.Nightly
{
    /// <summary>The Windows phone-home runner's configuration.</summary>
    public static class WindowsPhoneHome
    {
        /// <summary>The configuration file a phone-home desktop runner reads.</summary>
        public const string DefaultConfigPath = @"C:\Antiphon\runner\phone-home.json";

        /// <summary>
        /// The refusal of a desktop AppHost launch while a phone-home configuration is present (the Linux server serves, so the
        /// desktop stack must not start against it), or null when there is none. A blank path names the default file.
        /// </summary>
        public static string LaunchRefusal(bool configPresent, string configPath)
        {
            if (!configPresent) return null;
            if (string.IsNullOrWhiteSpace(configPath)) configPath = DefaultConfigPath;
            return "REFUSED: phone-home-config-present" + Environment.NewLine
                + "If the Linux server is serving, this refusal is correct: do NOT delete the file. Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear: delete "
                + configPath + ", run scripts\\antiphon.cmd restart-session-runner --hard, then retry.";
        }
    }
}
