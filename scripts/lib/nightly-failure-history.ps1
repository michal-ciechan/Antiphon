#requires -Version 5.1
# CARD-1514 S1: retained failure observations are hints, never execution credit.
# ASCII-only. Baseline adapter uses the existing terminal-only parser.
. (Join-Path $PSScriptRoot 'nightly-coverage.ps1')

function Get-NightlyHistoryFailureNodes {
    param([string]$Path, $Discovery = $null)
    return @( (ConvertFrom-NightlyDiagnosticLog -Path $Path -Kind execution).Nodes )
}

function Export-NightlyPriorFailures {
    param([string]$RunDirectory, [string]$OutputPath, [switch]$PassThru)
    $root = [IO.Path]::GetFullPath($RunDirectory)
    $summaryPath = Join-Path $root 'summary.json'
    $summary = [IO.File]::ReadAllText($summaryPath) | ConvertFrom-Json
    $members = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $evidence = [Collections.Generic.List[object]]::new()
    $evidence.Add([ordered]@{ relativePath = 'summary.json'; sha256 = (Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash.ToLowerInvariant(); kind = 'summary' })
    foreach ($chunk in @($summary.suites)) {
        $suite = [string]$chunk.id
        $observed = @()
        if ($chunk.trx -and (Test-Path -LiteralPath (Join-Path $root $chunk.trx))) {
            $p = Join-Path $root $chunk.trx
            $observed += @(Read-NightlyTrxIdentities -TrxPath $p | ForEach-Object {
                [pscustomobject]@{ ClassName = $_.ClassName; Uid = $_.TestId; State = $_.Outcome; Artifact = [string]$chunk.trx; Kind = 'trx' }
            })
            $evidence.Add([ordered]@{ relativePath = [string]$chunk.trx; sha256 = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant(); kind = 'trx' })
        }
        if ($chunk.execution) {
            $p = Join-Path $root $chunk.execution
            $observed += @(Get-NightlyHistoryFailureNodes -Path $p | ForEach-Object {
                [pscustomobject]@{ ClassName = $_.ClassName; Uid = $_.Uid; State = $_.State; Artifact = [string]$chunk.execution; Kind = 'diagnostic' }
            })
            $evidence.Add([ordered]@{ relativePath = [string]$chunk.execution; sha256 = (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash.ToLowerInvariant(); kind = 'diagnostic' })
        }
        foreach ($o in $observed) {
            if ((ConvertTo-NightlyNormalizedOutcome $o.State) -cin @('Passed','Skipped','NotExecuted')) { continue }
            $class = Get-NightlyBareClassIdentity $o.ClassName
            $key = $suite + '/' + $class
            if (-not $members.ContainsKey($key)) {
                $members[$key] = [ordered]@{ suite = $suite; className = $class; reasons = @(); observations = @() }
            }
            $members[$key].reasons += 'prior-failure'
            $members[$key].observations += [ordered]@{ artifact = $o.Artifact; chunk = [string]$chunk.chunk; uid = $o.Uid; state = $o.State; reason = 'prior-failure'; kind = $o.Kind }
        }
    }
    $history = [ordered]@{
        format = 'antiphon-nightly-prior-failures-v1'; sourceRoot = $root
        sourceRunId = [string]$summary.runId; sourceAttempt = 'unknown'; sourceSha = [string]$summary.sha
        profile = [string]$summary.profile; platform = [string]$summary.platform; policyHash = [string]$summary.policyHash
        createdAtUtc = [datetime]::UtcNow.ToString('o'); complete = $true
        evidence = @($evidence.ToArray()); members = @($members.Values | Sort-Object suite,className)
        fallbackChunks = @(); membershipComplete = $true; unresolved = @()
    }
    [IO.File]::WriteAllText($OutputPath, ($history | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    $result = [pscustomobject]@{ Path = $OutputPath; Digest = (Get-FileHash -LiteralPath $OutputPath -Algorithm SHA256).Hash.ToLowerInvariant(); History = ($history | ConvertTo-Json -Depth 30 | ConvertFrom-Json) }
    if ($PassThru) { return $result }
    Write-Host ('HISTORY SHA256 ' + $result.Digest)
}

function Read-NightlyPriorFailures {
    param([string]$Path, [string]$ExpectedDigest = '', [string]$RunDirectory = '')
    return ([IO.File]::ReadAllText($Path) | ConvertFrom-Json)
}

function Resolve-NightlyPriorFailures {
    param($History, [object[]]$Suites, [ValidateSet('Full','PriorFailureSweep')][string]$QualificationMode = 'Full')
    $matches = @()
    foreach ($suite in $Suites) {
        foreach ($member in @($History.members | Where-Object { $_.suite -ceq $suite.id })) {
            $required = @($suite.nodes | Where-Object { (Get-NightlyBareClassIdentity $_.ClassName) -ceq $member.className -and -not (Test-NightlyDiscoveryExcluded -Node $_ -ProfileExclusions @($suite.exclusions) -ProfileAware) })
            if ($required.Count -eq 0) { continue }
            $chunks = @($suite.chunks | Where-Object { @($_.classes) -ccontains $member.className })
            $matches += [pscustomobject]@{ suite = $suite.id; className = $member.className; chunks = @($chunks.id); requiredUids = @($required.Uid) }
        }
    }
    return [pscustomobject]@{ members = @($matches); unresolved = @() }
}
