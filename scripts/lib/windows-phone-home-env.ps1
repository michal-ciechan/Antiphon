# CARD-1744 S4. What is left of the Windows phone-home PowerShell library: two functions its last two PowerShell
# callers dot-source. ASCII, Windows PowerShell 5.1. Dot-source only.
#
# Unit 36 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved the rest (the phone-home.json parser, the
# runner environment, the autostart plan and the refusals) to scripts/lib/Antiphon.Nightly/WindowsPhoneHome.cs, which the
# autostart-session-runner, start-windows-phone-home-runner and restart-apphost commands call. These two stay, each a twin
# of a C# member, until their callers move:
#   Get-WindowsPhoneHomeLaunchRefusal  dev-aspire.ps1 (WindowsPhoneHome.LaunchRefusal)
#   Join-PhoneHomeChild                desktop-win-phase1-secrets.ps1, the ChildClaude/Codex/GrokHome entries
#                                      (WindowsPhoneHome.ChildPath, which the autostart's PhoneHome__*Home variables use)
# Do not add functions here: a new caller calls the C#.

function Join-PhoneHomeChild([string]$Left, [string]$Right) {
    $rightNorm = $Right
    if ($null -eq $rightNorm) { $rightNorm = '' }
    $rightNorm = $rightNorm.TrimStart('\', '/')
    if ([string]::IsNullOrWhiteSpace($Left)) { return $rightNorm }
    $leftTrim = $Left.TrimEnd('\', '/')
    if ($leftTrim -match '^[A-Za-z]:' -or $leftTrim.Contains('\')) {
        return $leftTrim + '\' + $rightNorm.Replace('/', '\')
    }
    return $leftTrim + '/' + $rightNorm.Replace('\', '/')
}

function Get-WindowsPhoneHomeLaunchRefusal {
    param(
        [bool]$ConfigPresent,
        [string]$ConfigPath
    )
    if (-not $ConfigPresent) { return $null }
    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        $ConfigPath = 'C:\Antiphon\runner\phone-home.json'
    }
    return ('REFUSED: phone-home-config-present' + [Environment]::NewLine + 'If the Linux server is serving, this refusal is correct: do NOT delete the file. Only during ROLLBACK, after the Linux server is stopped and pg_stat_activity is clear: delete ' + $ConfigPath + ', run scripts\antiphon.cmd restart-session-runner --hard, then retry.')
}
