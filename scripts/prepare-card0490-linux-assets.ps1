# CARD-0490: pin QEMU/guest/offline package inputs. Does not download secrets.
param(
    [string] $LockFile = 'tests/fixtures/card0490-linux/assets.lock.json',
    [string] $QemuDir = '',
    [string] $BootDisk = '',
    [switch] $WriteLock
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-FileSha256([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Asset not found: $path"
    }
    $hash = Get-FileHash -LiteralPath $path -Algorithm SHA256
    return $hash.Hash.ToLowerInvariant()
}

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($LockFile)) {
    $LockFile = Join-Path $repoRoot $LockFile
}
if (-not (Test-Path -LiteralPath $LockFile)) {
    throw "Asset lock not found: $LockFile"
}

if ([string]::IsNullOrWhiteSpace($QemuDir)) {
    $QemuDir = $env:CARD0490_QEMU_DIR
}
if ([string]::IsNullOrWhiteSpace($BootDisk)) {
    $BootDisk = $env:CARD0490_BOOT_DISK
}

Write-Host "Prepare CARD-0490 native assets using $LockFile (operator supplies prequalified binaries)."

$lock = Get-Content -LiteralPath $LockFile -Raw | ConvertFrom-Json
$qemuExe = $null
$qemuImg = $null
if (-not [string]::IsNullOrWhiteSpace($QemuDir) -and (Test-Path -LiteralPath $QemuDir)) {
    $qemuExe = Get-ChildItem -LiteralPath $QemuDir -Filter 'qemu-system-x86_64.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    $qemuImg = Get-ChildItem -LiteralPath $QemuDir -Filter 'qemu-img.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}

if ($qemuExe) {
    $sha = Get-FileSha256 $qemuExe.FullName
    Write-Host ("qemu {0} sha256={1} path={2}" -f $lock.qemu.version, $sha, $qemuExe.FullName)
    $lock.qemu.sha256 = $sha
}
else {
    Write-Host "qemu-system-x86_64.exe not found under QemuDir; lock sha256 remains $($lock.qemu.sha256)"
}

if ($qemuImg) {
    $sha = Get-FileSha256 $qemuImg.FullName
    Write-Host ("qemu-img {0} sha256={1} path={2}" -f $lock.'qemu-img'.version, $sha, $qemuImg.FullName)
    $lock.'qemu-img'.sha256 = $sha
}
else {
    Write-Host "qemu-img.exe not found under QemuDir; lock sha256 remains $($lock.'qemu-img'.sha256)"
}

if (-not [string]::IsNullOrWhiteSpace($BootDisk) -and (Test-Path -LiteralPath $BootDisk -PathType Leaf)) {
    $sha = Get-FileSha256 $BootDisk
    Write-Host ("bootDisk sha256={0} path={1}" -f $sha, $BootDisk)
    $lock.bootDisk.sha256 = $sha
}
else {
    Write-Host "bootDisk not supplied; lock sha256 remains $($lock.bootDisk.sha256)"
}

if ($WriteLock) {
    $lock | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $LockFile -Encoding utf8
    Write-Host "Wrote $LockFile"
}

exit 0
