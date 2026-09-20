# CARD-0490: pin QEMU/guest/offline package inputs. Does not download secrets.
param(
    [Parameter(Mandatory = $true)][string] $LockFile = 'tests/fixtures/card0490-linux/assets.lock.json'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Write-Host "Prepare CARD-0490 native assets using $LockFile (operator supplies prequalified binaries)."
if (-not (Test-Path -LiteralPath $LockFile)) {
    throw "Asset lock not found: $LockFile"
}
exit 0
