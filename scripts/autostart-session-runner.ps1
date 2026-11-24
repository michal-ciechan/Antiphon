#requires -Version 5.1
<#
.SYNOPSIS
    Thin launcher for `antiphon-scripts autostart-session-runner` (scripts/lib/Antiphon.Nightly/SessionRunnerAutostart.cs): the
    logon auto-start of the session-runner daemon (http://localhost:17204). When C:\Antiphon\runner\phone-home.json exists
    the same start runs the phone-home runner; a bad, unreadable or unloadable configuration is refused (one line in
    logs/session-runner.log, exit 2) and nothing starts. -PhoneHomeConfigPath names the phase 1 file
    (phone-home.phase1.json), which may use port 17214.

    Unit 36 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: kept because the per-user Scheduled Task
    "Antiphon Session Runner" (`scripts\antiphon.cmd install-autostart`) starts this file by path at logon, and the phase 1
    clone (C:\Antiphon\runner\app) starts it the same way by hand. Delete it when that registration names the command.

    This process is the supervisor the logon task and the AppHost record in logs/session-runner.supervisor.pid, so it
    passes its own PID as --supervisor-pid: the command records it, runs the run-daemon supervisor in process (build, the
    BUILT executable, restart on exit) and stops supervising once this process is gone. -PhoneHomeConfigPath is passed only
    when given (an explicit path may name port 17214). The command's exit code is returned; 9 when the CLI cannot be built
    or started (one stderr line). ASCII-only: parseable under Windows PowerShell 5.1.
#>
param(
    [string]$PhoneHomeConfigPath = 'C:\Antiphon\runner\phone-home.json'
)

$ErrorActionPreference = 'Continue'
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'antiphon-cli.ps1')
$arguments = @('--supervisor-pid', [string]$PID)
if ($PSBoundParameters.ContainsKey('PhoneHomeConfigPath')) { $arguments += @('--phone-home-config-path', $PhoneHomeConfigPath) }
exit (Invoke-AntiphonCli -Command 'autostart-session-runner' -Arguments $arguments)
