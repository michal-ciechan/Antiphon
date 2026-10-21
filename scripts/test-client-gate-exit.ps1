# CARD-1398 / CARD-1394 witness for test-client.ps1's failing exit.
#
# -Case Wrapper:
#   While test-client.ps1 does not forward -TypecheckProject and -LintPath,
#   exit 1 and say the wrapper did not reject the fixture error before Vitest.
#   Do not start test-client.ps1: that would typecheck, lint, and run Vitest.
#   Once those switches exist, invoke test-client.ps1 twice (a TS2353 fixture
#   and a no-control-regex fixture) and require each exit before Vitest.
#   Also require incremental/noEmit, the ignored eslint cache, and a lock
#   whose critical sections do not overlap.
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

if ($case -ne 'Wrapper') {
    Write-Output "unknown case: $case"
    exit 2
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$testClient = Join-Path $PSScriptRoot 'test-client.ps1'
$lintScript = Join-Path $PSScriptRoot 'lint-client.ps1'
$libScript = Join-Path (Join-Path $PSScriptRoot 'lib') 'client-npm-install.ps1'
$badTypeProject = Join-Path (Join-Path (Join-Path $repoRoot 'tests') 'fixtures') (Join-Path 'card1387-typecheck' 'tsconfig.json')
$okTypeProject = Join-Path (Join-Path (Join-Path $repoRoot 'tests') 'fixtures') (Join-Path 'card1398-typecheck-ok' 'tsconfig.json')
$badLint = Join-Path (Join-Path (Join-Path $repoRoot 'tests') 'fixtures') (Join-Path 'card1393-lint' 'bad.test.ts')

function Get-ScriptHost {
    if (Get-Command pwsh -ErrorAction SilentlyContinue) { return 'pwsh' }
    return 'powershell'
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

function Invoke-ScriptFile {
    param([string]$Path, [string[]]$Arguments)
    $hostExe = Get-ScriptHost
    $lines = @(& $hostExe -NoProfile -NonInteractive -File $Path @Arguments 2>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Text = ($lines -join "`n")
    }
}

function Get-ClientTestsExit {
    param([string]$Text)
    $found = [regex]::Matches($Text, 'CLIENT TESTS EXIT CODE:\s*(-?\d+)')
    if ($found.Count -eq 0) { return $null }
    return [int]$found[$found.Count - 1].Groups[1].Value
}

function Test-VitestStayedDown {
    param([string]$Text)
    if ($Text.IndexOf('vitest.mjs') -ge 0) { return $false }
    if ($Text.IndexOf('Test Files') -ge 0) { return $false }
    return $true
}

$clientText = [System.IO.File]::ReadAllText($testClient)
$clientExecutable = @(Get-ExecutableLines -Text $clientText)
$clientJoined = $clientExecutable -join "`n"
if ($clientJoined.IndexOf('-TypecheckProject') -lt 0 -or $clientJoined.IndexOf('-LintPath') -lt 0) {
    Write-Output "the wrapper did not reject the fixture error before Vitest"
    exit 1
}

function Fail-Wrapper {
    param([string]$Reason, [string]$Detail)
    Write-Output "the wrapper did not reject the fixture error before Vitest"
    Write-Output $Reason
    if (-not [string]::IsNullOrWhiteSpace($Detail)) { Write-Output $Detail }
    exit 1
}

foreach ($name in @('tsconfig.app.json', 'tsconfig.node.json')) {
    $path = Join-Path (Join-Path $repoRoot 'client') $name
    if (-not (Test-Path -LiteralPath $path)) { Fail-Wrapper "missing $name" '' }
    $text = [System.IO.File]::ReadAllText($path)
    if ($text.IndexOf('"incremental": true') -lt 0 -or $text.IndexOf('"noEmit": true') -lt 0 -or $text.IndexOf('tsBuildInfoFile') -lt 0 -or $text.IndexOf('node_modules/.tmp') -lt 0) {
        Fail-Wrapper "$name is not an incremental noEmit project" ''
    }
}

if (-not (Test-Path -LiteralPath $lintScript)) { Fail-Wrapper 'scripts/lint-client.ps1 is missing' '' }
$lintText = [System.IO.File]::ReadAllText($lintScript)
$lintExecutable = @(Get-ExecutableLines -Text $lintText)
$lintJoined = $lintExecutable -join "`n"
if ($lintJoined.IndexOf('--max-warnings 0') -lt 0 -or $lintJoined.IndexOf('--cache') -lt 0 -or $lintJoined.IndexOf('--cache-location') -lt 0 -or $lintJoined.IndexOf('node_modules') -lt 0 -or $lintJoined.IndexOf('.tmp') -lt 0) {
    Fail-Wrapper 'lint-client.ps1 does not cache eslint under node_modules/.tmp' ''
}

$typecheckScript = Join-Path $PSScriptRoot 'typecheck-client.ps1'
foreach ($path in @($testClient, $typecheckScript, $lintScript)) {
    $text = [System.IO.File]::ReadAllText($path)
    $joined = (@(Get-ExecutableLines -Text $text)) -join "`n"
    if ($joined.IndexOf('Install-ClientNpmDependencies') -lt 0 -or $joined.IndexOf('& $npmExe') -ge 0) {
        Fail-Wrapper "npm ci is not locked in $path" ''
    }
}

if (-not (Test-Path -LiteralPath $badTypeProject) -or -not (Test-Path -LiteralPath $okTypeProject) -or -not (Test-Path -LiteralPath $badLint)) {
    Fail-Wrapper 'a fixture is missing' ''
}

$typeRun = Invoke-ScriptFile -Path $testClient -Arguments @('-TypecheckProject', $badTypeProject)
Write-Output $typeRun.Text
$typePrinted = Get-ClientTestsExit -Text $typeRun.Text
if ($typeRun.ExitCode -ne 2 -or $typePrinted -ne 2 -or $typeRun.Text.IndexOf('TS2353') -lt 0 -or $typeRun.Text.IndexOf('vitest was not started') -lt 0 -or $typeRun.Text.IndexOf('CLIENT LINT EXIT CODE') -ge 0 -or -not (Test-VitestStayedDown -Text $typeRun.Text)) {
    Fail-Wrapper ("type wrapper exit={0} printed={1}" -f $typeRun.ExitCode, $typePrinted) ''
}

$lintRun = Invoke-ScriptFile -Path $testClient -Arguments @('-TypecheckProject', $okTypeProject, '-LintPath', $badLint)
Write-Output $lintRun.Text
$lintPrinted = Get-ClientTestsExit -Text $lintRun.Text
if ($lintRun.ExitCode -ne 1 -or $lintPrinted -ne 1 -or $lintRun.Text.IndexOf('no-control-regex') -lt 0 -or $lintRun.Text.IndexOf('vitest was not started') -lt 0 -or $lintRun.Text.IndexOf('CLIENT TYPECHECK EXIT CODE: 0') -lt 0 -or -not (Test-VitestStayedDown -Text $lintRun.Text)) {
    Fail-Wrapper ("lint wrapper exit={0} printed={1}" -f $lintRun.ExitCode, $lintPrinted) ''
}

if (-not (Test-Path -LiteralPath $libScript)) { Fail-Wrapper 'scripts/lib/client-npm-install.ps1 is missing' '' }

$stampRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('card1398-lock-' + [guid]::NewGuid().ToString('n'))
New-Item -ItemType Directory -Path $stampRoot | Out-Null
$childPath = Join-Path $stampRoot 'child.ps1'
$childBody = @'
$ErrorActionPreference = 'Continue'
$env:CARD1398_ID = [string]$args[0]
. $env:CARD1398_LIB
$lockCode = Invoke-WithClientNpmCiLock -RepoRoot $env:CARD1398_ROOT -Action {
    $startPath = Join-Path $env:CARD1398_DIR ($env:CARD1398_ID + '.start')
    $endPath = Join-Path $env:CARD1398_DIR ($env:CARD1398_ID + '.end')
    [System.IO.File]::WriteAllText($startPath, [string][DateTime]::UtcNow.Ticks)
    Start-Sleep -Seconds 2
    [System.IO.File]::WriteAllText($endPath, [string][DateTime]::UtcNow.Ticks)
}
if ($lockCode -ne 0) { exit $lockCode }
exit 0
'@
[System.IO.File]::WriteAllText($childPath, $childBody.Replace("`r`n", "`n"))
$env:CARD1398_LIB = $libScript
$env:CARD1398_ROOT = $stampRoot
$env:CARD1398_DIR = $stampRoot
$outA = Join-Path $stampRoot 'a.out'
$errA = Join-Path $stampRoot 'a.err'
$outB = Join-Path $stampRoot 'b.out'
$errB = Join-Path $stampRoot 'b.err'
$procA = Start-Process -FilePath (Get-ScriptHost) -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $childPath, 'A') -PassThru -NoNewWindow -RedirectStandardOutput $outA -RedirectStandardError $errA
$procB = Start-Process -FilePath (Get-ScriptHost) -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $childPath, 'B') -PassThru -NoNewWindow -RedirectStandardOutput $outB -RedirectStandardError $errB
$waited = Wait-Process -Id $procA.Id, $procB.Id -Timeout 30 -PassThru -ErrorAction SilentlyContinue
if ($null -eq $waited -or @($waited).Count -lt 2) {
    foreach ($proc in @($procA, $procB)) {
        if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }
    Fail-Wrapper 'npm ci lock holders did not finish' ''
}
$startAPath = Join-Path $stampRoot 'A.start'
$endAPath = Join-Path $stampRoot 'A.end'
$startBPath = Join-Path $stampRoot 'B.start'
$endBPath = Join-Path $stampRoot 'B.end'
if (-not (Test-Path -LiteralPath $startAPath) -or -not (Test-Path -LiteralPath $endAPath) -or -not (Test-Path -LiteralPath $startBPath) -or -not (Test-Path -LiteralPath $endBPath)) {
    Fail-Wrapper 'npm ci lock did not record both critical sections' ''
}
$startA = [int64]([System.IO.File]::ReadAllText($startAPath).Trim())
$endA = [int64]([System.IO.File]::ReadAllText($endAPath).Trim())
$startB = [int64]([System.IO.File]::ReadAllText($startBPath).Trim())
$endB = [int64]([System.IO.File]::ReadAllText($endBPath).Trim())
$separated = ($endA -le $startB) -or ($endB -le $startA)
if (-not $separated -or $procA.ExitCode -ne 0 -or $procB.ExitCode -ne 0) {
    Fail-Wrapper ("npm ci lock overlapped A={0}-{1} B={2}-{3} exit={4},{5}" -f $startA, $endA, $startB, $endB, $procA.ExitCode, $procB.ExitCode) ''
}
Remove-Item -LiteralPath $stampRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Output 'PASS Wrapper'
exit 0
