#requires -Version 5.1
<#!
.SYNOPSIS
    Fixture tests for scripts/worktree-residue.ps1. Local validation only; HTTP
    loopback coverage lives in WorktreeResidueScriptTests.

    ASCII-only: parses under pwsh 7 and Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Continue'
$here = $PSScriptRoot
$script = Join-Path $here 'worktree-residue.ps1'
$script:passed = 0
$script:failed = 0

function Pass { param([string]$Name) $script:passed++; Write-Host "PASS $Name" }
function Fail { param([string]$Name, [string]$Detail) $script:failed++; Write-Host "FAIL $Name - $Detail" }

if (-not (Test-Path -LiteralPath $script)) { throw "missing $script" }

$bytes = [System.IO.File]::ReadAllBytes($script)
$nonAscii = @($bytes | Where-Object { $_ -gt 127 }).Count
if ($nonAscii -eq 0) { Pass 'script is ASCII' } else { Fail 'script is ASCII' "nonAscii=$nonAscii" }

try {
    & $script -Action Release -TaskId 'aaaaaaaa' -ExpectedTaskRevision 'x' -SourceSha ('a' * 40) -Reason 'x' 2>$null
    Fail 'short id refused' 'expected throw'
}
catch {
    Pass 'short id refused'
}

$work = Join-Path $env:TEMP ('c459-residue-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$bad = Join-Path $work 'short.json'
'[{"taskId":"aaaaaaaa","expectedTaskRevision":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","sourceSha":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","reason":"x"}]' |
    Set-Content -LiteralPath $bad -Encoding ASCII
& $script -Action BatchRelease -BaseUrl 'http://127.0.0.1:1' -ManifestPath $bad
if ($LASTEXITCODE -ne 0) { Pass 'batch short id fails' } else { Fail 'batch short id fails' 'expected nonzero' }

Write-Host "passed=$script:passed failed=$script:failed"
if ($script:failed -gt 0) { exit 1 }
exit 0
