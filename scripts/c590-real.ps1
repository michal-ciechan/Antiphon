# CARD-0590. What is left of the live server2 bridge after unit 66 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md
# (W10 part 1) moved its live cases, the retirement coordinator and the desktop origin proof to the verify-docker-stack command
# (scripts/lib/Antiphon.Nightly/C590Bridge.cs, C1609Coordinator.cs, C1609HostProof.cs). Two parts stay, each for a W10 file that
# still uses them:
# - Assert-C994RawManifest and ConvertTo-C994RetiredInstant, which scripts/c1609-manual.ps1 (the CARD-1609 manual retirement
#   entry of retire-runner-volumes.ps1) dot-sources this file for;
# - executing this file with -Cp, which runs one CARD-0590 plan checkpoint (a finished card proof, kept as PowerShell by the
#   owner's 2026-11-26 decision) through scripts/verify-docker-stack.ps1.
# ASCII only.
param(
    [string]$Cp = '',
    [switch]$RefreshClaudeToken
)

$ErrorActionPreference = 'Stop'

function Get-C590RepoRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Get-C590StateDir {
    $dir = Join-Path (Get-C590RepoRoot) '.antiphon\c590-server2'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    return $dir
}

function Get-C590RunId {
    param($Manifest)
    $names = @($Manifest.PSObject.Properties.Name)
    if ($names -contains 'runId' -and $Manifest.runId) {
        return ([string]$Manifest.runId).ToLowerInvariant()
    }
    $path = Join-Path (Get-C590StateDir) 'run-id.txt'
    if (Test-Path -LiteralPath $path) {
        return (Get-Content -LiteralPath $path -Raw).Trim()
    }
    $id = ([guid]::NewGuid().ToString('N')).Substring(0, 12)
    Set-Content -LiteralPath $path -Value $id -Encoding ascii -NoNewline
    return $id
}

function Get-C590Sha {
    param($Manifest)
    $names = @($Manifest.PSObject.Properties.Name)
    if ($names -contains 'sourceSha' -and $Manifest.sourceSha -and [string]$Manifest.sourceSha -match '^[0-9a-f]{40}$') {
        return [string]$Manifest.sourceSha
    }
    return (& git -C (Get-C590RepoRoot) rev-parse HEAD).Trim()
}

function New-C590ManifestFile {
    param(
        [string]$EvidenceRoot,
        [string]$RunId,
        [string]$Sha,
        [string]$Filter = '',
        [string]$Project = '',
        [bool]$NoBuild = $false,
        [int]$MinExecuted = 1,
        [string]$Checkpoint = '',
        [bool]$Broker = $false
    )
    $dir = Split-Path -Parent $EvidenceRoot
    New-Item -ItemType Directory -Force -Path $dir, $EvidenceRoot | Out-Null
    $obj = [ordered]@{
        evidenceRoot = $EvidenceRoot
        sourceSha = $Sha
        runId = $RunId
        filter = $Filter
        project = $Project
        noBuild = $NoBuild
        minExecuted = $MinExecuted
        checkpoint = $Checkpoint
        broker = $Broker
    }
    $path = Join-Path $dir ("manifest-" + $Checkpoint + ".json")
    if (-not $Checkpoint) { $path = Join-Path $dir 'manifest.json' }
    ($obj | ConvertTo-Json -Compress) | Set-Content -LiteralPath $path -Encoding ascii
    return $path
}

function Get-C590PlanRow {
    param([Parameter(Mandatory = $true)][string]$Checkpoint)
    $plan = Join-Path (Get-C590RepoRoot) 'docs\superpowers\plans\2026-09-21-card-0590-self-contained-docker-stack-plan.md'
    $hits = @(Select-String -LiteralPath $plan -Pattern ("^\| " + [regex]::Escape($Checkpoint) + " \|") | ForEach-Object { $_.Line })
    if ($hits.Count -eq 0) { throw "plan row missing: $Checkpoint" }
    $line = $hits[$hits.Count - 1]
    $parts = [regex]::Split($line, '(?<!\\)\|')
    if ($parts.Count -lt 9) { throw "plan row unparsed: $Checkpoint" }
    $filter = $parts[5].Trim().Trim('`').Replace('\|', '|')
    $expect = $parts[7].Trim()
    $minMinutes = 0
    [void][int]::TryParse($parts[8].Trim(), [ref]$minMinutes)
    $minExecuted = 1
    $match = [regex]::Match($expect, '>=\s*(\d+)')
    if (-not $match.Success) { $match = [regex]::Match($expect, '(\d+)\s+executed') }
    if (-not $match.Success) { $match = [regex]::Match($expect, '(\d+)\s+case') }
    if ($match.Success) { $minExecuted = [int]$match.Groups[1].Value }
    return [pscustomobject]@{
        Checkpoint = $Checkpoint
        Group = $parts[4].Trim()
        Filter = $filter
        Expect = $expect
        MinMinutes = $minMinutes
        MinExecuted = $minExecuted
    }
}

function Assert-C994RawManifest {
    param([string]$Json)
    try {
        $document = [System.Text.Json.JsonDocument]::Parse($Json)
        function Assert-UniqueJsonObject($Element) {
            if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
                foreach ($property in $Element.EnumerateObject()) {
                    if (-not $names.Add($property.Name)) { throw 'TempContainerContextInvalid' }
                    Assert-UniqueJsonObject $property.Value
                }
            } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
                foreach ($item in $Element.EnumerateArray()) { Assert-UniqueJsonObject $item }
            }
        }
        Assert-UniqueJsonObject $document.RootElement
        if ($document.RootElement.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
            foreach ($property in $document.RootElement.EnumerateObject()) {
                if ($property.Name -ceq 'tempContainerCleanup') {
                    # Validate the raw string before ConvertFrom-Json turns an unzoned
                    # timestamp into DateTime and normalization invents a UTC offset.
                    $stamp = $property.Value.GetProperty('retiredAt')
                    if ($stamp.ValueKind -ne [System.Text.Json.JsonValueKind]::String) { throw 'TempContainerContextInvalid' }
                    [void](ConvertTo-C994RetiredInstant $stamp.GetString())
                }
            }
        }
    } catch { throw 'TempContainerContextInvalid' }
    finally { if ($null -ne $document) { $document.Dispose() } }
}

function ConvertTo-C994RetiredInstant {
    param($Value)
    $text = if ($Value -is [datetime]) { $Value.ToUniversalTime().ToString('o') }
        elseif ($Value -is [datetimeoffset]) { $Value.ToUniversalTime().ToString('o') }
        elseif ($Value -is [string]) { $Value } else { throw 'TempContainerContextInvalid' }
    if ($text -cnotmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,9})?(Z|[+-]\d{2}:\d{2})$') { throw 'TempContainerContextInvalid' }
    $instant = [datetimeoffset]::MinValue
    if (-not [datetimeoffset]::TryParse($text, [cultureinfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None, [ref]$instant)) { throw 'TempContainerContextInvalid' }
    return $instant.ToUniversalTime().ToString('o')
}

function Invoke-C590PlanCheckpoint {
    param([Parameter(Mandatory = $true)][string]$Checkpoint)
    $row = Get-C590PlanRow -Checkpoint $Checkpoint
    $state = Get-C590StateDir
    $run = Get-C590RunId -Manifest ([pscustomobject]@{})
    $sha = Get-C590Sha -Manifest ([pscustomobject]@{})
    $evidence = Join-Path $state ("evidence-" + $Checkpoint)
    $case = ''
    $project = ''
    $noBuild = $false
    $broker = $false
    if ($row.Filter -match '-Case\s+([A-Za-z0-9-]+)') {
        $case = $Matches[1]
    }
    elseif ($row.Filter -match 'npm --prefix client run lint') {
        $case = 'client-lint'
    }
    elseif ($row.Filter -match 'test-client\.ps1') {
        $case = 'client-tests'
    }
    elseif ($row.Filter -match '/\*') {
        $case = 'dotnet-filter'
        if ($Checkpoint -eq 'CP-19') {
            $project = 'tests/Antiphon.Messaging.Tests'
            $broker = $true
        }
        else {
            $project = 'tests/Antiphon.Tests'
            $noBuild = $Checkpoint -ne 'CP-20'
        }
    }
    else {
        throw "no live dispatcher for $Checkpoint filter"
    }
    $manifestPath = New-C590ManifestFile -EvidenceRoot $evidence -RunId $run -Sha $sha -Filter $row.Filter -Project $project -NoBuild $noBuild -MinExecuted $row.MinExecuted -Checkpoint $Checkpoint -Broker $broker
    $verify = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-docker-stack.ps1'), '-Case', $case, '-Manifest', $manifestPath)
    if ($RefreshClaudeToken) { $verify += '-RefreshClaudeToken' }
    & pwsh @verify
    exit $LASTEXITCODE
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Cp) { throw 'Cp is required when executing c590-real.ps1' }
    Invoke-C590PlanCheckpoint -Checkpoint $Cp
}
