#requires -Version 5.1
# CARD-1514 S1. Real Windows process-owner witness. C1039_PowerShellDescendantCustody
# dot-sources this file and calls the function. Host-stats sampling is not wired.
# Phase-root absent, empty and set is portable and lives in RcFullTimingContractTests.

function Invoke-RcWindowsNativeProcessOwner {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds,
        [string]$LogPath
    )
    return [Antiphon.Nightly.NativeProcessOwner]::Start($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $LogPath, $null, $null)
}
