# Offline zero-kill witness for claudebot-preserve-sessions.patch (CARD-1477).
# Copies the live ClaudeBot updater scripts into a fresh temp layout, applies the patch there,
# swaps in a stub launcher, and runs the mocked harness against both copies. It never writes
# to the ClaudeBot checkout and never stops, starts or elevates a real process.
#   pwsh -NoProfile -NonInteractive -File docs\investigations\evidence\card-1477\run-witness.ps1
param(
    [string]$ClaudeBotRoot = 'C:\src\ClaudeBot',
    [int]$CaseTimeoutSec = 60
)
$ErrorActionPreference = 'Stop'
$work = Join-Path ([IO.Path]::GetTempPath()) ('card-1477-witness-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
foreach ($side in 'orig', 'patched') {
    New-Item -ItemType Directory -Force (Join-Path $work "$side\scripts"), (Join-Path $work "$side\claude-home\skills\launch-remote") | Out-Null
    foreach ($f in 'update-claude.ps1', 'daily-update.ps1') {
        Copy-Item -LiteralPath (Join-Path $ClaudeBotRoot "scripts\$f") -Destination (Join-Path $work "$side\scripts\$f")
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'stub\claude-home\skills\launch-remote\launch-claude.ps1') `
        -Destination (Join-Path $work "$side\claude-home\skills\launch-remote\launch-claude.ps1")
}
& git -C (Join-Path $work 'patched') apply --whitespace=nowarn (Join-Path $PSScriptRoot 'claudebot-preserve-sessions.patch')
if ($LASTEXITCODE -ne 0) { throw "patch did not apply to the copies of $ClaudeBotRoot\scripts" }
foreach ($f in 'update-claude.ps1', 'daily-update.ps1') {
    $errs = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile((Join-Path $work "patched\scripts\$f"), [ref]$null, [ref]$errs)
    "patched $f parse errors: $($errs.Count)"
    if ($errs.Count) { exit 1 }
}
New-Item -ItemType Directory -Force (Join-Path $work 'test') | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'test\*.ps1') (Join-Path $work 'test')
"witness layout: $work"
& (Get-Process -Id $PID).Path -NoProfile -NonInteractive -File (Join-Path $work 'test\harness.ps1') -CaseTimeoutSec $CaseTimeoutSec
exit $LASTEXITCODE
