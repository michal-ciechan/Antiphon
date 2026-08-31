<#
.SYNOPSIS
    Restart and verify the local Aspire stack, including its EF migration history.

.DESCRIPTION
    This is the orchestrator-triggered machine-global canonical-stack deploy action,
    never an isolated linked-worktree validation. It deliberately
    composes restart-apphost.ps1 instead of reproducing its lock, teardown, or
    health-waiting behaviour. Once that restart succeeds, it verifies the full
    dev stack (without a browser smoke) and confirms every source migration is
    recorded in the live antiphon-postgres database.

    The last line is always the deploy result. It is intentionally printed by
    this script, not inferred from a caller's wrapping pipeline.

.PARAMETER NoBuild
    Pass -NoBuild to restart-apphost.ps1.

.PARAMETER TimeoutSec
    Pass the AppHost health wait timeout to restart-apphost.ps1.

.PARAMETER AllowWorktree
    Intentionally allow a linked worktree to control the shared local stack.

.OUTPUTS
    DEPLOY VERDICT: ok
    DEPLOY VERDICT: failed <detail>
    DEPLOY VERDICT: refused <detail>

.NOTES
    Keep this file ASCII-only: it may run under Windows PowerShell 5.1, which
    reads no-BOM .ps1 files as CP1252 and mangles non-ASCII characters.
#>
param(
    [switch]$NoBuild,
    [int]$TimeoutSec = 150,
    [switch]$AllowWorktree
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'apphost-common.ps1')

$worktree = Get-AppHostWorktreeClassification -SourceRoot $repoRoot
if (-not $worktree.Verified -or (-not $worktree.IsMainWorktree -and -not $AllowWorktree)) {
    $detail = (Format-AppHostWorktreeGuardMessage -Classification $worktree) -join ' '
    Write-Output "DEPLOY VERDICT: refused $detail"
    exit 3
}
if (-not $worktree.IsMainWorktree -and $AllowWorktree) {
    Format-AppHostWorktreeGuardMessage -Classification $worktree -AllowWorktree | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}

$restartScript = Join-Path $PSScriptRoot 'restart-apphost.ps1'
$verifyScript = Join-Path $repoRoot 'verify-dev-stack.ps1'
$migrationDirectory = Join-Path $repoRoot 'server\Migrations'

function Get-SourceMigrationIds {
    param([string]$Directory)

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        throw "Migration directory not found: $Directory"
    }

    @(
        Get-ChildItem -LiteralPath $Directory -Filter '*.cs' -File |
            Where-Object {
                $_.Name -ne 'AppDbContextModelSnapshot.cs' -and
                $_.Name -notlike '*.Designer.cs'
            } |
            ForEach-Object { $_.BaseName } |
            Sort-Object -Unique
    )
}

function Get-RecordedMigrationIds {
    $recorded = @(
        & docker exec antiphon-postgres psql -X -U antiphon -d antiphon -At -v ON_ERROR_STOP=1 `
            -c 'SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId";' |
            ForEach-Object { $_.ToString().Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    $psqlExitCode = $LASTEXITCODE
    if ($psqlExitCode -ne 0) {
        throw "Could not read __EFMigrationsHistory from antiphon-postgres (docker exec exit $psqlExitCode)"
    }

    $recorded
}

function Invoke-ChildPowerShell {
    param(
        [string]$ScriptPath,
        [string]$Name,
        [string[]]$Arguments = @()
    )

    if (-not (Test-Path -LiteralPath $ScriptPath -PathType Leaf)) {
        throw "$Name script not found: $ScriptPath"
    }

    & pwsh -NoProfile -File $ScriptPath @Arguments
    $childExitCode = $LASTEXITCODE
    if ($childExitCode -ne 0) {
        throw "$Name exited $childExitCode"
    }
}

function Format-FailureDetail {
    param([System.Management.Automation.ErrorRecord]$ErrorRecord)

    $detail = $ErrorRecord.Exception.Message -replace '\s+', ' '
    if ([string]::IsNullOrWhiteSpace($detail)) { return 'unknown error' }
    $detail.Trim()
}

$failure = $null
try {
    $restartArguments = @('-TimeoutSec', $TimeoutSec)
    if ($NoBuild) { $restartArguments += '-NoBuild' }
    if ($AllowWorktree) { $restartArguments += '-AllowWorktree' }
    Invoke-ChildPowerShell -ScriptPath $restartScript -Name 'restart-apphost.ps1' -Arguments $restartArguments

    # The built client can still be finishing its first post-restart build after
    # restart-apphost.ps1 has confirmed the backend. Allow that readiness probe
    # longer than verify-dev-stack.ps1's interactive default.
    Invoke-ChildPowerShell -ScriptPath $verifyScript -Name 'verify-dev-stack.ps1' -Arguments @('-SkipBrowser', '-TimeoutSec', '30')

    $sourceMigrationIds = @(Get-SourceMigrationIds -Directory $migrationDirectory)
    $recordedMigrationIds = @(Get-RecordedMigrationIds)
    $pendingMigrationIds = @($sourceMigrationIds | Where-Object { $_ -notin $recordedMigrationIds })
    $unknownMigrationIds = @($recordedMigrationIds | Where-Object { $_ -notin $sourceMigrationIds })

    if ($pendingMigrationIds.Count -gt 0 -or $unknownMigrationIds.Count -gt 0) {
        $parts = @()
        if ($pendingMigrationIds.Count -gt 0) {
            $parts += "pending migrations: $($pendingMigrationIds -join ', ')"
        }
        if ($unknownMigrationIds.Count -gt 0) {
            $parts += "database-only migrations: $($unknownMigrationIds -join ', ')"
        }
        throw ($parts -join '; ')
    }

    Write-Host "EF migrations verified: $($sourceMigrationIds.Count) source and database history entries."
} catch {
    $failure = Format-FailureDetail -ErrorRecord $_
}

if ($failure) {
    Write-Output "DEPLOY VERDICT: failed $failure"
    exit 1
}

Write-Output 'DEPLOY VERDICT: ok'
exit 0
