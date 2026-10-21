# Serializes client npm ci across test-client.ps1, typecheck-client.ps1, and
# lint-client.ps1. Two checkpoint rows on a cold worktree used to install into
# the same client/ at once (CARD-1394, CARD-1398).
#
# The lock file is logs/client-npm-ci.lock. logs/ is gitignored. FileShare.None
# is released when the holder exits, including a crash. Waiters retry for 15
# minutes. After the lock is held, a second look skips npm when the binaries
# are already present.
#
# ASCII-only. Parses under Windows PowerShell 5.1.

$ErrorActionPreference = 'Continue'

function Invoke-WithClientNpmCiLock {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    $logDir = Join-Path $RepoRoot 'logs'
    if (-not (Test-Path -LiteralPath $logDir)) {
        New-Item -ItemType Directory -Path $logDir | Out-Null
    }
    $lockPath = Join-Path $logDir 'client-npm-ci.lock'
    $deadline = (Get-Date).ToUniversalTime().AddMinutes(15)
    $stream = $null
    while ($null -eq $stream) {
        try {
            $stream = [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
        }
        catch {
            if ((Get-Date).ToUniversalTime() -gt $deadline) {
                Write-Output "timed out waiting for the client npm ci lock at $lockPath"
                return 1
            }
            Start-Sleep -Seconds 2
        }
    }

    try {
        & $Action
        return 0
    }
    finally {
        $stream.Dispose()
    }
}

function Install-ClientNpmDependencies {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string[]]$RequiredPaths
    )

    $missing = $false
    foreach ($required in $RequiredPaths) {
        if (-not (Test-Path -LiteralPath $required)) { $missing = $true }
    }
    if (-not $missing) { return 0 }

    # GetNewClosure captures RequiredPaths and RepoRoot. A bare scriptblock
    # would look those up in the lock function and skip the install.
    $state = @{ Code = $null }
    $action = {
        $stillMissing = $false
        foreach ($required in $RequiredPaths) {
            if (-not (Test-Path -LiteralPath $required)) { $stillMissing = $true }
        }
        if (-not $stillMissing) { return }

        Write-Output "client dependencies are not installed - installing once under the npm ci lock."
        $clientDir = Join-Path $RepoRoot 'client'
        $npmExe = 'npm'
        if (Get-Command npm.cmd -ErrorAction SilentlyContinue) { $npmExe = 'npm.cmd' }
        $installVerb = 'install'
        if (Test-Path -LiteralPath (Join-Path $clientDir 'package-lock.json')) { $installVerb = 'ci' }
        Push-Location $clientDir
        try {
            & $npmExe $installVerb
            $state.Code = $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
        if ($null -eq $state.Code) { $state.Code = 1 }
        if ($state.Code -ne 0) {
            Write-Output "npm $installVerb exited with code $($state.Code)."
        }
    }.GetNewClosure()

    $lockCode = Invoke-WithClientNpmCiLock -RepoRoot $RepoRoot -Action $action
    if ($lockCode -ne 0) { return $lockCode }
    if ($null -eq $state.Code) { return 0 }
    return $state.Code
}
