#requires -Version 5.1
<#
.SYNOPSIS
Observe remote master publication with git ls-remote; never fetch or change the checkout.
.DESCRIPTION
Bounded re-armable watcher. -From is the previously observed full master SHA, not a
claim that any later movement contains a particular fix. ASCII-only, PowerShell 5.1 safe.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$From,
    [string]$Repository = (Join-Path $PSScriptRoot '../..'),
    [string]$Remote = 'origin',
    [ValidateRange(1, 300)][int]$PollSeconds = 30,
    [ValidateRange(1, 29)][int]$MaxMinutes = 28,
    [switch]$Once)
$ErrorActionPreference = 'Stop'
$deadline = [datetime]::UtcNow.AddMinutes($MaxMinutes)
do {
    try {
        $lines = @(git -C $Repository ls-remote $Remote refs/heads/master 2>&1)
        if ($LASTEXITCODE -ne 0) { throw 'git ls-remote failed.' }
        $sha = $null
        foreach ($line in $lines) {
            if ([string]$line -match '^([0-9a-fA-F]{40})\s+refs/heads/master$') { $sha = $Matches[1] }
        }
        if (-not $sha) { throw 'Remote returned no valid master ref.' }
        if ($sha -ne $From) { "MASTER MOVED $sha"; break }
    } catch { "MASTER-WATCH-ERROR $($_.Exception.Message)" }
    if ($Once -or [datetime]::UtcNow -ge $deadline) { break }
    Start-Sleep -Seconds $PollSeconds
} while ([datetime]::UtcNow -lt $deadline)
'master watch done; re-arm if still waiting'
