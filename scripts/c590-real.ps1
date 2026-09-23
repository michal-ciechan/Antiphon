# CARD-0590 live server2 bridge. Dot-source from verify-docker-stack.ps1.
# Executing this file with -Cp runs one plan checkpoint. ASCII only.
param(
    [string]$Cp = ''
)

$ErrorActionPreference = 'Stop'

$script:C590LiveCases = @(
    # CARD-0604 S4 host-lane cases. They run on server2's own shell against the HOST daemon and are
    # the only cases that change standing state there. Their entries in verify-docker-stack.ps1's
    # switch are the STUB boundaries; this roster is what routes a real run to the remote instead.
    'deploy-parent',
    'nested-residue',
    'persistent-restart',
    'server2-independent-handoff',
    'git-credential-smoke',
    'baseline-build-failure',
    'server-image-payload',
    'runner-image-payload',
    'testing-runner-payload',
    # CARD-0604 S9/CP-15. The host-lane containment measurement (V-28 on cgroup v1).
    # Without this entry the case falls through to verify-docker-stack.ps1's default arm and
    # reports 'RealCasePending' -- a red row for a routing reason, not a measured one.
    'custody-containment',
    'parent-native-and-command-session',
    'child-server-image-payload',
    'child-runner-image-payload',
    'test-image-context-and-tools',
    'receipt-runner-image-payload',
    'fixture-image-payload',
    'deployment-state',
    'client-lint',
    'client-tests',
    'messaging-tests',
    'dotnet-filter',
    'session-result-export-and-child-cleanup',
    'session-denied-socket',
    'interrupted-export-cleanup',
    'stock-idle',
    'stock-busy',
    'insert-refused',
    'insert-committed-idle',
    'insert-committed-busy',
    'attempt-committed',
    'body-before-enter',
    'recipient-before-ingestion',
    'transcript-save-fails-release',
    'transcript-save-fails-restart',
    'receipt-before-verdict',
    'response-before-client',
    'receipt-before-manifest',
    'changed-generation',
    'failure-summary',
    'runtime-context-engine',
    'test-context-engine',
    'throwaway-all'
)

function Test-C590LiveCase {
    param([Parameter(Mandatory = $true)][string]$Case)
    return $script:C590LiveCases -contains $Case
}

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

function Invoke-C590Ssh {
    param([Parameter(Mandatory = $true)][string]$Remote)
    & ssh -o BatchMode=yes -o ConnectTimeout=30 -o ServerAliveInterval=30 -o ServerAliveCountMax=240 mc@server2 $Remote
    return $LASTEXITCODE
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

function Invoke-C590LiveCase {
    param(
        [Parameter(Mandatory = $true)][string]$Case,
        [Parameter(Mandatory = $true)]$Manifest
    )
    $root = [string]$Manifest.evidenceRoot
    if (-not $root) { throw 'evidenceRoot is required' }
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    $run = Get-C590RunId -Manifest $Manifest
    $sha = Get-C590Sha -Manifest $Manifest
    $names = @($Manifest.PSObject.Properties.Name)
    $project = ''
    $noBuild = '0'
    $minExecuted = '1'
    $checkpoint = $Case
    $broker = '0'
    if ($names -contains 'project' -and $Manifest.project) { $project = [string]$Manifest.project }
    if ($names -contains 'noBuild' -and [bool]$Manifest.noBuild) { $noBuild = '1' }
    if ($names -contains 'minExecuted' -and $Manifest.minExecuted) { $minExecuted = [string]$Manifest.minExecuted }
    if ($names -contains 'checkpoint' -and $Manifest.checkpoint) { $checkpoint = [string]$Manifest.checkpoint }
    if ($names -contains 'broker' -and [bool]$Manifest.broker) { $broker = '1' }
    # CARD-0604 D-13. The branch the runner's checkout tracks (master once this cut has landed) and
    # the production origin the host lane probes for runner status. Both are manifest-supplied so a
    # pre-land run can point the runner at the task branch without editing this script.
    $c604Branch = 'master'
    $c604Origin = 'https://antiphon.desktop.codeperf.net'
    if ($names -contains 'c604Branch' -and $Manifest.c604Branch) { $c604Branch = [string]$Manifest.c604Branch }
    if ($names -contains 'c604Origin' -and $Manifest.c604Origin) { $c604Origin = [string]$Manifest.c604Origin }
    if ($c604Branch -notmatch '^[A-Za-z0-9._/-]{1,200}$') { throw 'c604 branch rejected' }
    if ($c604Origin -notmatch '^https?://[A-Za-z0-9._:-]{1,200}$') { throw 'c604 origin rejected' }
    if ($project -notmatch '^[A-Za-z0-9./_-]*$') { throw 'project path rejected' }
    if ($checkpoint -notmatch '^[A-Za-z0-9_-]+$') { throw 'checkpoint name rejected' }
    if ($run -notmatch '^[a-z0-9]+$') { throw 'run id rejected' }
    if ($sha -notmatch '^[0-9a-f]{40}$') { throw 'source sha rejected' }

    $tokenCopied = $false
    try {
        if ($Case -eq 'git-credential-smoke') {
            $token = (& gh auth token | Out-String).Trim()
            if (-not $token -or $token.Length -lt 10) {
                Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis 'GitHubTokenUnavailable' -ExitCode 2
            }
            $localToken = Join-Path (Get-C590StateDir) 'gh-token'
            [System.IO.File]::WriteAllText($localToken, $token)
            $token = ''
            [void](Invoke-C590Ssh "mkdir -p /home/mc/antiphon-c590/secrets && chmod 700 /home/mc/antiphon-c590/secrets")
            & scp -o BatchMode=yes $localToken mc@server2:/home/mc/antiphon-c590/secrets/gh-token
            Remove-Item -LiteralPath $localToken -Force
            [void](Invoke-C590Ssh "chmod 600 /home/mc/antiphon-c590/secrets/gh-token")
            $tokenCopied = $true
        }

        if ($names -contains 'filter' -and $Manifest.filter) {
            $filterPath = Join-Path (Get-C590StateDir) 'filter.txt'
            [System.IO.File]::WriteAllText($filterPath, [string]$Manifest.filter)
            & scp -o BatchMode=yes $filterPath ("mc@server2:/tmp/c590-filter-" + $run + ".txt")
            Remove-Item -LiteralPath $filterPath -Force
        }

        [void](Invoke-C590Ssh "mkdir -p /home/mc/antiphon-c590")
        & scp -o BatchMode=yes (Join-Path $PSScriptRoot 'c590-remote.sh') mc@server2:/home/mc/antiphon-c590/c590-remote.sh
        $remote = @(
            "export C590_CASE='$Case'"
            "export C590_SHA='$sha'"
            "export C590_RUN='$run'"
            "export C590_PROJECT='$project'"
            "export C590_NOBUILD='$noBuild'"
            "export C590_MIN='$minExecuted'"
            "export C590_CP='$checkpoint'"
            "export C590_BROKER='$broker'"
            "export C590_FILTER_FILE='/tmp/c590-filter-$run.txt'"
            # CARD-0604: the branch the runner's own checkout tracks (master after land), and the
            # production origin the host lane probes for runner status. Never a secret: the
            # phone-home shared secret is generated on server2 and never crosses this bridge.
            "export C604_BRANCH='$c604Branch'"
            "export C604_SERVER_ORIGIN='$c604Origin'"
            "bash /home/mc/antiphon-c590/c590-remote.sh"
        ) -join '; '
        $code = Invoke-C590Ssh $remote
        $destParent = $root
        & scp -o BatchMode=yes -r ("mc@server2:/work/test-evidence/" + $run + "/" + $Case) $destParent | Out-Null
        $resultPath = Join-Path (Join-Path $root $Case) 'c590-result.json'
        if (-not (Test-Path -LiteralPath $resultPath)) {
            Write-C590Result -EvidenceRoot $root -Accepted $false -Diagnosis ("RemoteResultMissing exit=" + $code) -ExitCode 2
        }
        $result = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
        Write-C590Result -EvidenceRoot $root -Accepted ([bool]$result.accepted) -Diagnosis ([string]$result.diagnosis) -ExitCode ([int]$result.exit)
    }
    finally {
        if ($tokenCopied) {
            [void](Invoke-C590Ssh "rm -f /home/mc/antiphon-c590/secrets/gh-token /home/mc/antiphon-c590/askpass")
        }
    }
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
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'verify-docker-stack.ps1') -Case $case -Manifest $manifestPath
    exit $LASTEXITCODE
}

if ($MyInvocation.InvocationName -ne '.') {
    if (-not $Cp) { throw 'Cp is required when executing c590-real.ps1' }
    Invoke-C590PlanCheckpoint -Checkpoint $Cp
}
