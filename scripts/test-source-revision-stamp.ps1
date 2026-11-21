#requires -Version 5.1
param([ValidateSet('decoys')][string]$Case)
$ErrorActionPreference = 'Stop'

function Get-UnstampedPowerShellBuild([string]$Text) {
    foreach ($match in [regex]::Matches($Text, 'dotnet\s+(build|pack|publish)\b[^\r\n;]*')) {
        if ($match.Index -gt 0 -and $Text[$match.Index - 1] -in @("'", '"', '`')) { continue }
        if ($match.Value -notmatch 'SourceRevisionId') { $match.Value }
    }
    if ($Text.Contains('dotnet @') -and -not $Text.Contains('-p:SourceRevisionId=')) {
        'splatted dotnet has no stamp'
    }
}

$decoys = [ordered]@{
    comment = 'dotnet build server # -p:SourceRevisionId=$sourceRevision'
    quoted = 'Invoke-Expression "dotnet publish server"'
    logged = 'dotnet pack server; Write-Host "-p:SourceRevisionId=$sourceRevision"'
    splat = '$buildArgs = @("build", "server"); if ($false) { $buildArgs += "-p:SourceRevisionId=$sourceRevision" }; dotnet @buildArgs'
}
$accepted = @'
dotnet build server "-p:SourceRevisionId=$sourceRevision"
Invoke-Expression 'dotnet publish server -p:SourceRevisionId=$sourceRevision'
$buildArgs = @('build', 'server')
$buildArgs += "-p:SourceRevisionId=$sourceRevision"
dotnet @buildArgs
Write-Host "dotnet build example"
'@
$missed = @($decoys.Keys | Where-Object { @(Get-UnstampedPowerShellBuild $decoys[$_]).Count -ne 1 })
if ($missed.Count -gt 0) { throw "Unstamped build missed: $($missed -join ', ')" }
$falsePositives = @(Get-UnstampedPowerShellBuild $accepted)
if ($falsePositives.Count -ne 0) { throw "Valid input rejected: $($falsePositives -join ', ')" }
