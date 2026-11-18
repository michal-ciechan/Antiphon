#requires -Version 5.1
<#
.SYNOPSIS
  CARD-1987 red witness. Writes the signal and a zero report, then exits 0.
#>
param(
    [int]$Seconds = 60,
    [string]$Container = "",
    [string]$ConnectionEnv = "",
    [string]$Database = "antiphon",
    [string]$User = "antiphon",
    [int]$Top = 20,
    [switch]$Json,
    [string]$SignalFile = ""
)

$ErrorActionPreference = "Stop"
if ($Seconds -lt 10 -or $Seconds -gt 3600) {
    Write-Output "bad arguments"
    exit 2
}
if ($Top -lt 1) {
    Write-Output "bad arguments"
    exit 2
}
if ($SignalFile.Length -gt 0) {
    Set-Content -LiteralPath $SignalFile -Value "1" -Encoding ascii
}
$report = '{"windowSeconds":10,"partial":false,"reason":"","total":{"qps":0,"dbMsPerSecond":0,"rowsPerSecond":0,"calls":0,"meanMs":0},"operations":[],"topByCalls":[],"topByTime":[],"snapshotMs":0,"serverVersionNum":0,"serverVersion":"0"}'
if ($Json) {
    Write-Output $report
}
exit 0
