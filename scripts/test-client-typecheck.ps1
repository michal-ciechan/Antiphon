# CARD-1387 witness for the client typecheck gate.
#
# ExcessProperty runs typecheck-client.ps1 against the fixture outside client/.
# A missing gate script is the S1 red: the harness exits nonzero and says the
# gate did not reject the fixture. It does not start Vitest.
#
# CleanClient runs the real client typecheck and checks that test-client.ps1
# calls the gate before node $vitest, with no npx and no build-slot.ps1.
#
# ASCII-only. Same PowerShell 5.1-safe shape as test-client.ps1.
# This harness does not take a build slot. A checkpoint command row already holds one.

$ErrorActionPreference = 'Continue'

$case = ''
for ($i = 0; $i -lt $args.Count; $i++) {
    if ([string]$args[$i] -eq '-Case' -and ($i + 1) -lt $args.Count) {
        $case = [string]$args[$i + 1]
        $i++
        continue
    }
    Write-Output "unknown argument: $($args[$i])"
    exit 2
}

if ($case -ne 'ExcessProperty' -and $case -ne 'CleanClient') {
    Write-Output "unknown case: $case"
    exit 2
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$typecheckScript = Join-Path $PSScriptRoot 'typecheck-client.ps1'
$testClientScript = Join-Path $PSScriptRoot 'test-client.ps1'

function Get-ScriptHost {
    if (Get-Command pwsh -ErrorAction SilentlyContinue) { return 'pwsh' }
    return 'powershell'
}

function Invoke-ScriptFile {
    param([string]$Path, [string[]]$Arguments)
    $hostExe = Get-ScriptHost
    $lines = @(& $hostExe -NoProfile -NonInteractive -File $Path @Arguments 2>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text = ($lines -join "`n")
    }
}

function Get-TypecheckExit {
    param([string]$Text)
    $found = [regex]::Matches($Text, 'CLIENT TYPECHECK EXIT CODE:\s*(-?\d+)')
    if ($found.Count -eq 0) { return $null }
    return [int]$found[$found.Count - 1].Groups[1].Value
}

function Get-ExecutableLines {
    param([string]$Text)
    $lines = @()
    foreach ($line in ($Text -split "`r?`n")) {
        if ($line.TrimStart().StartsWith('#')) { continue }
        $lines += $line
    }
    return $lines
}

if ($case -eq 'ExcessProperty') {
    if (-not (Test-Path -LiteralPath $typecheckScript)) {
        Write-Output "the typecheck gate did not reject the fixture"
        Write-Output "scripts/typecheck-client.ps1 is missing"
        exit 1
    }

    $project = Join-Path (Join-Path (Join-Path $repoRoot 'tests') 'fixtures') (Join-Path 'card1387-typecheck' 'tsconfig.json')
    $run = Invoke-ScriptFile -Path $typecheckScript -Arguments @('-Project', $project)
    Write-Output $run.Text

    $printed = Get-TypecheckExit -Text $run.Text
    $startedVitest = $run.Text.Contains('CLIENT TESTS EXIT CODE') -or $run.Text.Contains('vitest.mjs')
    if ($run.ExitCode -eq 0 -or $null -eq $printed -or $printed -ne $run.ExitCode -or $printed -eq 0 -or $run.Text.IndexOf('TS2353') -lt 0 -or $startedVitest) {
        Write-Output "the typecheck gate did not reject the fixture"
        Write-Output ("gate exit={0} printed={1}" -f $run.ExitCode, $printed)
        exit 1
    }

    Write-Output "PASS ExcessProperty"
    exit 0
}

if (-not (Test-Path -LiteralPath $typecheckScript)) {
    Write-Output "CLIENT TYPECHECK EXIT CODE was not 0"
    Write-Output "scripts/typecheck-client.ps1 is missing"
    exit 1
}

$clean = Invoke-ScriptFile -Path $typecheckScript -Arguments @()
Write-Output $clean.Text
$cleanPrinted = Get-TypecheckExit -Text $clean.Text
if ($clean.ExitCode -ne 0 -or $cleanPrinted -ne 0) {
    Write-Output "CLIENT TYPECHECK EXIT CODE was not 0"
    Write-Output ("gate exit={0} printed={1}" -f $clean.ExitCode, $cleanPrinted)
    exit 1
}

$clientText = [System.IO.File]::ReadAllText($testClientScript)
$executable = @(Get-ExecutableLines -Text $clientText)
$joined = $executable -join "`n"
$typecheckAt = $joined.IndexOf('typecheck-client.ps1')
$invokeAt = $joined.IndexOf('-File $typecheckScript')
$vitestAt = $joined.IndexOf('node $vitest')
if ($joined.IndexOf('npx') -ge 0 -or $joined.IndexOf('build-slot.ps1') -ge 0 -or $typecheckAt -lt 0 -or $invokeAt -lt 0 -or $vitestAt -lt 0 -or $typecheckAt -ge $vitestAt -or $invokeAt -ge $vitestAt) {
    Write-Output "test-client.ps1 does not invoke the typecheck script before node `$vitest"
    exit 1
}

Write-Output "PASS CleanClient"
exit 0
