#requires -Version 5.1
<#
.SYNOPSIS
    CARD-0273 / CARD-0644 regression: a linked worktree whose HEAD is not the
    admitted commit cannot control the local AppHost.

.DESCRIPTION
    CARD-0644 made admission commit-based: a linked worktree whose HEAD equals
    origin/master (or an explicit -ExpectedSha) IS admitted. This harness
    therefore never builds its fixture from the real repository: it uses a
    standalone scratch origin/main/linked set (scripts/lib/card0644-apphost-fixture.ps1)
    and runs every entry point with inert seams, so even a wrongly admitted run
    records its teardown/launch instead of touching the live stack.
    ASCII-only for pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'

. (Join-Path $PSScriptRoot 'apphost-common.ps1')
. (Join-Path (Join-Path $PSScriptRoot 'lib') 'card0644-apphost-fixture.ps1')

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

$fx = $null
$nonGitRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('c644-nongit-' + [guid]::NewGuid().ToString('N').Substring(0, 12))

try {
    $fx = New-C644Fixture
    $mainGit = Get-C644GitPath $fx.Main
    $linkedHead = New-C644Commit $fx.Linked 'unlanded linked commit'

    $mainClassification = Get-AppHostWorktreeClassification -SourceRoot $fx.Main
    Assert-True $mainClassification.Verified 'main classifier verifies' $mainClassification.Failure
    Assert-True $mainClassification.IsMainWorktree 'main classifier reports IsMainWorktree true' ("root={0}; main={1}" -f $mainClassification.ScriptWorktreeRoot, $mainClassification.MainWorktreeRoot)

    $linkedClassification = Get-AppHostWorktreeClassification -SourceRoot $fx.Linked
    Assert-True $linkedClassification.Verified 'linked classifier verifies' $linkedClassification.Failure
    Assert-True (-not $linkedClassification.IsMainWorktree) 'linked classifier reports IsMainWorktree false' ("root={0}; main={1}" -f $linkedClassification.ScriptWorktreeRoot, $linkedClassification.MainWorktreeRoot)
    Assert-True (Test-C644SamePath $linkedClassification.MainWorktreeRoot $mainGit) 'linked classifier reports canonical main worktree root' ("expected={0}; actual={1}" -f $mainGit, $linkedClassification.MainWorktreeRoot)

    $restart = Invoke-C644Entry $fx restart $fx.Linked @('-NoBuild', '-TimeoutSec', '1')
    Assert-True ($restart.ExitCode -eq 3) 'restart entry exits 3 from an unlanded linked worktree' ("exit=$($restart.ExitCode); output=$($restart.Text)")
    Assert-True ($restart.Text -match 'REFUSED') 'restart entry prints REFUSED' $restart.Text
    Assert-True ($restart.Text.Contains($linkedClassification.ScriptWorktreeRoot)) 'restart entry names the source root' $restart.Text
    Assert-True ($restart.Text.Contains($fx.OriginSha) -and $restart.Text.Contains($linkedHead)) 'restart entry names origin/master and HEAD SHAs' $restart.Text
    Assert-True ($restart.Text -match [regex]::Escape('-ExpectedSha') -and $restart.Text -match [regex]::Escape('-AllowWorktree')) 'restart entry names -ExpectedSha and -AllowWorktree' $restart.Text
    Assert-True (-not ($restart.Text -match 'Restarting Antiphon AppHost')) 'restart entry exits before restart banner' $restart.Text
    Assert-True ((Get-C644EffectCount $restart) -eq 0) 'restart entry records no teardown or launch' (($restart.Trace | ForEach-Object { $_.kind }) -join ',')

    $dev = Invoke-C644Entry $fx dev $fx.Linked @('-NoBuild', '-NoBrowser')
    Assert-True ($dev.ExitCode -eq 3) 'direct dev entry exits 3 from an unlanded linked worktree' ("exit=$($dev.ExitCode); output=$($dev.Text)")
    Assert-True ($dev.Text -match 'REFUSED') 'direct dev entry prints REFUSED' $dev.Text
    Assert-True ((Get-C644EffectCount $dev) -eq 0) 'direct dev entry exits before Docker or launch activity' (($dev.Trace | ForEach-Object { $_.kind }) -join ',')

    $deploy = Invoke-C644Entry $fx deploy $fx.Linked @('-NoBuild', '-TimeoutSec', '1')
    Assert-True ($deploy.ExitCode -eq 3) 'deploy entry exits 3 from an unlanded linked worktree' ("exit=$($deploy.ExitCode); output=$($deploy.Text)")
    Assert-True ($deploy.Text -match 'DEPLOY VERDICT: refused') 'deploy entry prints its refused verdict' $deploy.Text
    Assert-True ((Get-C644EffectCount $deploy) -eq 0) 'deploy entry does not invoke restart' (($deploy.Trace | ForEach-Object { $_.kind }) -join ',')

    foreach ($root in $fx.Main, $fx.Linked) {
        foreach ($leaf in 'apphost.restart.lock', 'apphost.launch.lock') {
            $path = Join-Path (Join-Path $root 'logs') $leaf
            Assert-True (-not (Test-Path -LiteralPath $path)) ("refusals leave no {0}" -f $path)
        }
    }

    $override = Get-AppHostSourceAdmission -SourceRoot $fx.Linked -AllowWorktree
    $warning = ($override.Lines) -join "`n"
    Assert-True ($override.Admitted -and $override.Override) 'explicit override admits the linked root at HEAD' $warning
    Assert-True ($warning -match 'WARNING' -and $warning.Contains($linkedHead)) 'explicit override prints WARNING with the admitted HEAD' $warning
    Assert-True ($warning -match 'shared local ports are not isolated') 'explicit override names shared ports' $warning

    New-Item -ItemType Directory -Force $nonGitRoot | Out-Null
    $nonGit = Get-AppHostWorktreeClassification -SourceRoot $nonGitRoot
    Assert-True (-not $nonGit.Verified) 'non-Git directory is an unverifiable structured failure' ("verified=$($nonGit.Verified); failure=$($nonGit.Failure)")
    Assert-True (-not [string]::IsNullOrWhiteSpace($nonGit.Failure)) 'non-Git directory includes failed Git step' $nonGit.Failure
    $nonGitAdmission = Get-AppHostSourceAdmission -SourceRoot $nonGitRoot -AllowWorktree
    Assert-True (-not $nonGitAdmission.Admitted) '-AllowWorktree never admits an unverifiable root' (($nonGitAdmission.Lines) -join ' ')
}
catch {
    Write-Fail 'test setup or execution' $_.Exception.Message
}
finally {
    Remove-C644Fixture $fx
    if (Test-Path -LiteralPath $nonGitRoot) { Remove-Item -LiteralPath $nonGitRoot -Recurse -Force -ErrorAction SilentlyContinue }
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
