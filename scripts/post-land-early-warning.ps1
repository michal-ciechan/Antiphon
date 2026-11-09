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
        [Console]::Error.WriteLine('refused=not-linux')
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

$decision = Get-PostLandDecision -Repo $Repo -ReceiptRoot $ReceiptRoot -BaseSha $base -TipSha $tip -FileList $FileList -Table $Table -Force:([bool]$Force)
if ($Plan) {
    Write-PostLandJson $decision.Payload
    exit $decision.Exit
}

if ([string]$decision.Payload.state -ne 'run') {
    $decision.Payload.finishedAt = [datetime]::UtcNow.ToString('o')
    Write-PostLandReceipt $ReceiptRoot $decision.Payload
    [Console]::Out.WriteLine([string]$decision.Payload.summaryLine)
    exit 0
}

if ([Environment]::GetEnvironmentVariable('POST_LAND_WARNING_CHILD') -eq '1') {
    $manifest = Read-PostLandManifest (Join-Path $script:PostLandProductRoot 'tests/post-land-unit-shards.json')
    $code = Invoke-PostLandLane -Repo $Repo -ReceiptRoot $ReceiptRoot -Payload $decision.Payload -Manifest $manifest
    exit $code
}

$tipDir = Join-Path $ReceiptRoot $tip
$null = [IO.Directory]::CreateDirectory($tipDir)
$stdoutLog = Join-Path $tipDir 'lane.log'
$stderrLog = Join-Path $tipDir 'lane.err'
$forward = @('-Range', ($base + '..' + $tip), '-Repo', $Repo, '-ReceiptRoot', $ReceiptRoot)
if (-not [string]::IsNullOrWhiteSpace($FileList)) { $forward += @('-FileList', $FileList) }
if (-not [string]::IsNullOrWhiteSpace($Table)) { $forward += @('-Table', $Table) }
if ($Force) { $forward += '-Force' }
$env:POST_LAND_WARNING_CHILD = '1'
$argumentList = @('pwsh', '-NoProfile', '-NonInteractive', '-File', $PSCommandPath) + $forward
$null = Start-Process -FilePath 'setsid' -ArgumentList $argumentList -WorkingDirectory $Repo -RedirectStandardOutput $stdoutLog -RedirectStandardError $stderrLog -PassThru
$marker = Join-Path $ReceiptRoot 'lane-started.json'
$deadline = [datetime]::UtcNow.AddSeconds(20)
while ([datetime]::UtcNow -lt $deadline) {
    if (Test-Path -LiteralPath $marker) { exit 0 }
    Start-Sleep -Milliseconds 200
}
[Console]::Error.WriteLine('refused=detach')
exit 2
