#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0273 regression: linked worktrees cannot control the local AppHost by default.

.DESCRIPTION
    Creates its own disposable linked worktree and exercises only the real entry
    points' refusal paths. It never invokes an allowed entry point, Docker,
    process, port, lock, teardown, or launch operation against the live stack.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')

$script:passed = 0
$script:failed = 0
$script:failures = @()

function Write-Pass {
    param([string]$Name)
    $script:passed++
    Write-Host "PASS $Name"
}

function Write-Fail {
    param([string]$Name, [string]$Detail)
    $script:failed++
    $script:failures += "$Name : $Detail"
    Write-Host "FAIL $Name - $Detail"
}

function Assert-True {
    param([bool]$Condition, [string]$Name, [string]$Detail = '')
    if ($Condition) { Write-Pass $Name }
    else { Write-Fail $Name $Detail }
}

function Invoke-RefusalEntry {
    param([string]$ScriptPath, [string[]]$Arguments)
    $output = @(& pwsh -NoProfile -File $ScriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($output -join "`n") }
}

$sourceRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('antiphon-apphost-worktree-guard-' + [guid]::NewGuid().ToString('N'))
$linkedRoot = Join-Path $temporaryRoot 'linked'
$nonGitRoot = Join-Path $temporaryRoot 'not-a-repository'
$worktreeAdded = $false

try {
    $sourceClassification = Get-AppHostWorktreeClassification -SourceRoot $sourceRoot
    Assert-True $sourceClassification.Verified 'source checkout classifier verifies' $sourceClassification.Failure
    if (-not $sourceClassification.Verified) { throw 'Cannot create a disposable worktree without a verified source checkout.' }

    $mainClassification = Get-AppHostWorktreeClassification -SourceRoot $sourceClassification.MainWorktreeRoot
    Assert-True $mainClassification.Verified 'main classifier verifies' $mainClassification.Failure
    Assert-True $mainClassification.IsMainWorktree 'main classifier reports IsMainWorktree true' ("root={0}; main={1}" -f $mainClassification.ScriptWorktreeRoot, $mainClassification.MainWorktreeRoot)

    New-Item -ItemType Directory -Force $temporaryRoot | Out-Null
    $head = (& git -C $sourceRoot rev-parse HEAD 2>&1 | Select-Object -First 1).ToString().Trim()
    if (-not $head) { throw 'Could not resolve source checkout HEAD.' }
    & git -C $sourceRoot worktree add --detach $linkedRoot $head 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "git worktree add failed with exit $LASTEXITCODE" }
    $worktreeAdded = $true

    $linkedClassification = Get-AppHostWorktreeClassification -SourceRoot $linkedRoot
    Assert-True $linkedClassification.Verified 'linked classifier verifies' $linkedClassification.Failure
    Assert-True (-not $linkedClassification.IsMainWorktree) 'linked classifier reports IsMainWorktree false' ("root={0}; main={1}" -f $linkedClassification.ScriptWorktreeRoot, $linkedClassification.MainWorktreeRoot)
    Assert-True ([string]::Equals($linkedClassification.MainWorktreeRoot, $mainClassification.MainWorktreeRoot, [System.StringComparison]::OrdinalIgnoreCase)) 'linked classifier reports canonical main worktree root' ("expected={0}; actual={1}" -f $mainClassification.MainWorktreeRoot, $linkedClassification.MainWorktreeRoot)

    $restart = Invoke-RefusalEntry -ScriptPath (Join-Path $linkedRoot 'scripts\restart-apphost.ps1') -Arguments @('-NoBuild', '-TimeoutSec', '1')
    Assert-True ($restart.ExitCode -eq 3) 'restart entry exits 3 from linked worktree' ("exit=$($restart.ExitCode); output=$($restart.Text)")
    Assert-True ($restart.Text -match 'REFUSED: this AppHost command is rooted in a linked Git worktree\.') 'restart entry prints REFUSED' $restart.Text
    Assert-True ($restart.Text.Contains($linkedClassification.ScriptWorktreeRoot) -and $restart.Text.Contains($linkedClassification.MainWorktreeRoot)) 'restart entry names both worktree roots' $restart.Text
    Assert-True ($restart.Text -match [regex]::Escape('-AllowWorktree')) 'restart entry names -AllowWorktree' $restart.Text
    Assert-True (-not ($restart.Text -match 'Restarting Antiphon AppHost')) 'restart entry exits before restart banner' $restart.Text
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $linkedRoot 'logs\apphost.restart.lock'))) 'restart entry leaves no restart lock'

    $dev = Invoke-RefusalEntry -ScriptPath (Join-Path $linkedRoot 'dev-aspire.ps1') -Arguments @('-NoBuild', '-NoBrowser')
    Assert-True ($dev.ExitCode -eq 3) 'direct dev entry exits 3 from linked worktree' ("exit=$($dev.ExitCode); output=$($dev.Text)")
    Assert-True ($dev.Text -match 'REFUSED: this AppHost command is rooted in a linked Git worktree\.') 'direct dev entry prints REFUSED' $dev.Text
    Assert-True (-not ($dev.Text -match 'Testing Docker network health|Ensuring Postgres|Starting Aspire AppHost')) 'direct dev entry exits before Docker or launch activity' $dev.Text
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $linkedRoot 'logs\apphost.launch.lock'))) 'direct dev entry leaves no launch lock'

    $deploy = Invoke-RefusalEntry -ScriptPath (Join-Path $linkedRoot 'scripts\deploy-local.ps1') -Arguments @('-NoBuild', '-TimeoutSec', '1')
    Assert-True ($deploy.ExitCode -eq 3) 'deploy entry exits 3 from linked worktree' ("exit=$($deploy.ExitCode); output=$($deploy.Text)")
    Assert-True ($deploy.Text -match 'DEPLOY VERDICT: refused') 'deploy entry prints its refused verdict' $deploy.Text
    Assert-True (-not ($deploy.Text -match 'Restarting Antiphon AppHost')) 'deploy entry does not invoke restart' $deploy.Text

    $warning = (Format-AppHostWorktreeGuardMessage -Classification $linkedClassification -AllowWorktree) -join "`n"
    Assert-True ($warning -match '^WARNING: this AppHost command is rooted in a linked Git worktree\.') 'explicit override formatter prints WARNING' $warning
    Assert-True ($warning.Contains($linkedClassification.ScriptWorktreeRoot) -and $warning.Contains($linkedClassification.MainWorktreeRoot)) 'explicit override formatter names both roots' $warning
    Assert-True ($warning -match 'shared local ports are not isolated') 'explicit override formatter names shared ports' $warning

    New-Item -ItemType Directory -Force $nonGitRoot | Out-Null
    $nonGit = Get-AppHostWorktreeClassification -SourceRoot $nonGitRoot
    Assert-True (-not $nonGit.Verified) 'non-Git directory is an unverifiable structured failure' ("verified=$($nonGit.Verified); failure=$($nonGit.Failure)")
    Assert-True (-not [string]::IsNullOrWhiteSpace($nonGit.Failure)) 'non-Git directory includes failed Git step' $nonGit.Failure
}
catch {
    Write-Fail 'test setup or execution' $_.Exception.Message
}
finally {
    if ($worktreeAdded) {
        & git -C $sourceRoot worktree remove --force $linkedRoot 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Fail 'disposable worktree cleanup' "git worktree remove failed with exit $LASTEXITCODE" }
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host ('CARD-0273 main-worktree guard: {0} passed, {1} failed' -f $script:passed, $script:failed)
if ($script:failed -gt 0) {
    foreach ($line in $script:failures) { Write-Host ('  ' + $line) }
    Write-Host 'APPHOST MAIN-WORKTREE GUARD TESTS EXIT CODE: 1  (FAIL - do not report this run as green)'
    exit 1
}
Write-Host 'APPHOST MAIN-WORKTREE GUARD TESTS EXIT CODE: 0  (PASS)'
exit 0
