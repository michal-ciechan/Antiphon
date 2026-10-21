# CARD-1393 witness for the client lint gate.
#
# ControlRegex runs lint-client.ps1 against the fixture outside client/.
# A missing gate script is the S1 red: the harness exits nonzero and says the
# gate did not reject the fixture. It does not start Vitest.
#
# CleanClient runs the real client lint and checks that test-client.ps1
# calls the gate after typecheck and before node $vitest, with no npx and
# no build-slot.ps1.
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

if ($case -ne 'ControlRegex' -and $case -ne 'CleanClient') {
    Write-Output "unknown case: $case"
    exit 2
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$lintScript = Join-Path $PSScriptRoot 'lint-client.ps1'
$testClientScript = Join-Path $PSScriptRoot 'test-client.ps1'
$fixture = Join-Path (Join-Path (Join-Path $repoRoot 'tests') 'fixtures') (Join-Path 'card1393-lint' 'bad.test.ts')

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

function Get-LintExit {
    param([string]$Text)
    $found = [regex]::Matches($Text, 'CLIENT LINT EXIT CODE:\s*(-?\d+)')
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

if ($case -eq 'ControlRegex') {
    if (-not (Test-Path -LiteralPath $fixture)) {
        Write-Output "the lint gate did not reject the fixture"
        Write-Output "fixture is missing"
        exit 1
    }
    $fixtureText = [System.IO.File]::ReadAllText($fixture)
    if ($fixtureText.IndexOf('\x1b') -lt 0) {
        Write-Output "the lint gate did not reject the fixture"
        Write-Output "fixture has no control-regex witness"
        exit 1
    }
    if (-not (Test-Path -LiteralPath $lintScript)) {
        Write-Output "the lint gate did not reject the fixture"
        Write-Output "scripts/lint-client.ps1 is missing"
        exit 1
    }

    $run = Invoke-ScriptFile -Path $lintScript -Arguments @('-Path', $fixture)
    Write-Output $run.Text

    $printed = Get-LintExit -Text $run.Text
    $startedVitest = $run.Text.Contains('CLIENT TESTS EXIT CODE') -or $run.Text.Contains('vitest.mjs')
    if ($run.ExitCode -eq 0 -or $null -eq $printed -or $printed -ne $run.ExitCode -or $printed -eq 0 -or $run.Text.IndexOf('no-control-regex') -lt 0 -or $startedVitest) {
        Write-Output "the lint gate did not reject the fixture"
        Write-Output ("gate exit={0} printed={1}" -f $run.ExitCode, $printed)
        exit 1
    }

    Write-Output "PASS ControlRegex"
    exit 0
}

if (-not (Test-Path -LiteralPath $lintScript)) {
    Write-Output "CLIENT LINT EXIT CODE was not 0"
    Write-Output "scripts/lint-client.ps1 is missing"
    exit 1
}

$clean = Invoke-ScriptFile -Path $lintScript -Arguments @()
Write-Output $clean.Text
$cleanPrinted = Get-LintExit -Text $clean.Text
if ($clean.ExitCode -ne 0 -or $cleanPrinted -ne 0) {
    Write-Output "CLIENT LINT EXIT CODE was not 0"
    Write-Output ("gate exit={0} printed={1}" -f $clean.ExitCode, $cleanPrinted)
    exit 1
}

$clientText = [System.IO.File]::ReadAllText($testClientScript)
$executable = @(Get-ExecutableLines -Text $clientText)
$joined = $executable -join "`n"
$typecheckAt = $joined.IndexOf('typecheck-client.ps1')
$lintAt = $joined.IndexOf('lint-client.ps1')
$lintInvokeAt = $joined.IndexOf('-File $lintScript')
$vitestAt = $joined.IndexOf('node $vitest')
$eslintAt = $joined.IndexOf('eslint.js')
if ($joined.IndexOf('npx') -ge 0 -or $joined.IndexOf('build-slot.ps1') -ge 0 -or $typecheckAt -lt 0 -or $lintAt -lt 0 -or $lintInvokeAt -lt 0 -or $vitestAt -lt 0 -or $eslintAt -lt 0 -or $typecheckAt -ge $lintAt -or $lintAt -ge $vitestAt -or $lintInvokeAt -ge $vitestAt -or $eslintAt -ge $lintInvokeAt) {
    Write-Output "test-client.ps1 does not invoke the lint script after typecheck and before node `$vitest"
    exit 1
}

$lintText = [System.IO.File]::ReadAllText($lintScript)
$lintExecutable = @(Get-ExecutableLines -Text $lintText)
$lintJoined = $lintExecutable -join "`n"
if ($lintJoined.IndexOf('npx') -ge 0 -or $lintJoined.IndexOf('build-slot.ps1') -ge 0 -or $lintJoined.IndexOf('eslint.js') -lt 0 -or $lintJoined.IndexOf('--max-warnings 0') -lt 0 -or $lintJoined.IndexOf('node $eslint . --max-warnings 0') -lt 0) {
    Write-Output "lint-client.ps1 does not run eslint . --max-warnings 0"
    exit 1
}

Write-Output "PASS CleanClient"
exit 0
