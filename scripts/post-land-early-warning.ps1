# CARD-1784. Linux post-land early warning.
# -Plan prints the decision and does not detach, lock, or start dotnet.
# Run mode refuses a non-Linux host before a build slot. ASCII only.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Range,
    [string]$ReceiptRoot,
    [string]$Repo,
    [string]$Table,
    [string]$FileList,
    [string]$RunId,
    [switch]$Plan,
    [switch]$Force
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/post-land-early-warning.ps1')

$resolved = Resolve-PostLandRange $Range
if (-not $resolved.Ok) {
    [Console]::Error.WriteLine($resolved.Error)
    exit 2
}

if (-not $Plan) {
    $linuxHost = $false
    if ($PSVersionTable.PSVersion.Major -ge 6) {
        $linuxHost = $IsLinux
    }
    $seam = [Environment]::GetEnvironmentVariable('POST_LAND_WARNING_PLATFORM')
    $allowed = $false
    if (-not [string]::IsNullOrEmpty($seam)) {
        if ($seam -eq 'linux') { $allowed = $true }
    }
    elseif ($linuxHost) {
        $allowed = $true
    }
    if (-not $allowed) {
        [Console]::Error.WriteLine('refused=not-linux: run this lane on the Linux host. The desktop does not run it.')
        exit 3
    }
}

if ([string]::IsNullOrWhiteSpace($Repo)) { $Repo = $script:PostLandProductRoot }
$Repo = [IO.Path]::GetFullPath($Repo)
if ([string]::IsNullOrWhiteSpace($ReceiptRoot)) {
    $ReceiptRoot = Join-Path $Repo '.antiphon/post-land-warning'
}
$ReceiptRoot = [IO.Path]::GetFullPath($ReceiptRoot)

$base = Resolve-PostLandSha $Repo $resolved.Base
$tip = Resolve-PostLandSha $Repo $resolved.Tip
if ([string]::IsNullOrWhiteSpace($base) -or [string]::IsNullOrWhiteSpace($tip)) {
    [Console]::Error.WriteLine('refused=bad-range')
    exit 2
}

if (-not $Plan) {
    $head = Resolve-PostLandSha $Repo 'HEAD'
    if ($head -ne $tip) {
        [Console]::Error.WriteLine('refused=head-mismatch')
        exit 2
    }
    $dirty = Invoke-PostLandGit $Repo @('status', '--porcelain', '--untracked-files=no')
    if ($dirty.Exit -ne 0 -or -not [string]::IsNullOrWhiteSpace($dirty.Stdout)) {
        [Console]::Error.WriteLine('refused=dirty')
        exit 2
    }
}

if ($Plan) {
    $decision = Get-PostLandDecision -Repo $Repo -ReceiptRoot $ReceiptRoot -BaseSha $base -TipSha $tip -FileList $FileList -Table $Table -Force:([bool]$Force)
    Write-PostLandJson $decision.Payload
    exit $decision.Exit
}

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = [guid]::NewGuid().ToString('N')
}
if ($RunId -notmatch '^[0-9a-fA-F]{32}$') {
    [Console]::Error.WriteLine('refused=bad-run-id')
    exit 2
}

$marker = Join-Path $ReceiptRoot ('lane-' + $RunId + '.json')
$lockPath = Join-Path $ReceiptRoot 'lock.json'

if ([Environment]::GetEnvironmentVariable('POST_LAND_WARNING_CHILD') -eq '1') {
    $acquired = Enter-PostLandLock -Path $lockPath -RunId $RunId
    if (-not $acquired) {
        $busy = Get-PostLandDecision -Repo $Repo -ReceiptRoot $ReceiptRoot -BaseSha $base -TipSha $tip -FileList $FileList -Table $Table -Force:([bool]$Force)
        $busy.Payload.finishedAt = [datetime]::UtcNow.ToString('o')
        Write-PostLandReceipt $ReceiptRoot $busy.Payload
        Write-PostLandRunMarker $marker $RunId 'skipped-busy'
        [Console]::Out.WriteLine([string]$busy.Payload.summaryLine)
        exit 0
    }
    try {
        Write-PostLandRunMarker $marker $RunId 'started'
        $decision = Get-PostLandDecision -Repo $Repo -ReceiptRoot $ReceiptRoot -BaseSha $base -TipSha $tip -FileList $FileList -Table $Table -Force:([bool]$Force)
        if ([string]$decision.Payload.state -ne 'run') {
            $decision.Payload.finishedAt = [datetime]::UtcNow.ToString('o')
            Write-PostLandReceipt $ReceiptRoot $decision.Payload
            [Console]::Out.WriteLine([string]$decision.Payload.summaryLine)
            exit 0
        }
        $manifest = Read-PostLandManifest (Join-Path $script:PostLandProductRoot 'tests/post-land-unit-shards.json')
        $code = Invoke-PostLandLane -Repo $Repo -ReceiptRoot $ReceiptRoot -Payload $decision.Payload -Manifest $manifest
        exit $code
    }
    finally {
        Exit-PostLandLock -Path $lockPath
    }
}

$tipDir = Join-Path $ReceiptRoot $tip
$null = [IO.Directory]::CreateDirectory($tipDir)
$stdoutLog = Join-Path $tipDir ('lane-' + $RunId + '.log')
$stderrLog = Join-Path $tipDir ('lane-' + $RunId + '.err')
$forward = @('-Range', ($base + '..' + $tip), '-Repo', $Repo, '-ReceiptRoot', $ReceiptRoot, '-RunId', $RunId)
if (-not [string]::IsNullOrWhiteSpace($FileList)) { $forward += @('-FileList', $FileList) }
if (-not [string]::IsNullOrWhiteSpace($Table)) { $forward += @('-Table', $Table) }
if ($Force) { $forward += '-Force' }
$env:POST_LAND_WARNING_CHILD = '1'
$childArgs = @('-NoProfile', '-NonInteractive', '-File', $PSCommandPath) + $forward
$setsid = Get-Command setsid -ErrorAction SilentlyContinue
if ($null -ne $setsid) {
    $null = Start-Process -FilePath 'setsid' -ArgumentList (@('pwsh') + $childArgs) -WorkingDirectory $Repo -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
}
else {
    $null = Start-Process -FilePath 'pwsh' -ArgumentList $childArgs -WorkingDirectory $Repo -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
}
$deadline = [datetime]::UtcNow.AddSeconds(20)
while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $marker) {
        [Console]::Out.WriteLine('runId=' + $RunId)
        [Console]::Out.WriteLine([IO.File]::ReadAllText($marker))
        exit 0
    }
    Start-Sleep -Milliseconds 200
}
[Console]::Error.WriteLine('refused=detach')
exit 2
