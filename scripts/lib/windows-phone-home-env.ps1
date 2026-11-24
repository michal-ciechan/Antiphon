# CARD-1744 S4. What is left of the Windows phone-home PowerShell library: one function its last PowerShell caller
# dot-sources. ASCII, Windows PowerShell 5.1. Dot-source only.
#
# Unit 36 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md moved the rest (the phone-home.json parser, the
# runner environment, the autostart plan and the refusals) to scripts/lib/Antiphon.Nightly/WindowsPhoneHome.cs, which the
# autostart-session-runner, start-windows-phone-home-runner, restart-apphost and dev-aspire commands call (dev-aspire.ps1,
# the last caller of Get-WindowsPhoneHomeLaunchRefusal, became a launcher of the dev-aspire command in unit 37). This one
# stays, a twin of a C# member, until its caller moves:
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
