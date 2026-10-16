param([Parameter(Mandatory)][string]$Repo)
$ErrorActionPreference = 'Stop'
. (Join-Path $Repo 'scripts/c590-real.ps1')
$fixtureRoot = Join-Path $Repo ('.antiphon/bridge-model-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    function ssh {
        if ($args[-1] -like '*bash /home/mc/antiphon-c590/c590-remote.sh*') {
            'CacheRecoveryInvalid credential=TOKEN_SENTINEL_BRIDGE'
            'DIAGNOSIS=FixturePrunePreviewFailed'
            'password=TOKEN_SENTINEL_BRIDGE'
            1..300 | ForEach-Object { 'CacheRecoveryInvalid' }
            $global:LASTEXITCODE = if ($script:scenario -eq 'inner') { 2 } else { 255 }
        } else { $global:LASTEXITCODE = 0 }
    }
    function scp {
        if ($args -contains '-r' -and $script:scenario -ne 'missing') {
            $caseDir = Join-Path $script:caseRoot 'runner-cache-fixture'
            New-Item -ItemType Directory -Force -Path $caseDir | Out-Null
            @{ accepted=($script:scenario -eq 'transport'); diagnosis='FixturePrunePreviewFailed'; exit=2 } |
                ConvertTo-Json | Set-Content (Join-Path $caseDir 'c590-result.json')
        }
        $global:LASTEXITCODE = 0
    }
    function Write-C590Result {
        param($EvidenceRoot, $Accepted, $Diagnosis, $ExitCode)
        $script:observed = [pscustomobject]@{ accepted=$Accepted; diagnosis=$Diagnosis; exit=$ExitCode }
        throw 'MODEL_RESULT'
    }
    foreach ($script:scenario in @('inner', 'transport', 'missing')) {
        $script:caseRoot = Join-Path $fixtureRoot $script:scenario
        $manifest = [pscustomobject]@{ evidenceRoot=$script:caseRoot; sourceSha=('a'*40); runId='c849aaaaaaaaaaaaaaaa0' }
        try { Invoke-C590LiveCase -Case runner-cache-fixture -Manifest $manifest }
        catch { if ($_.Exception.Message -ne 'MODEL_RESULT') { throw } }
        if ($script:observed.accepted -or $script:observed.exit -eq 0) { throw 'transport accepted' }
        $log = (Get-Content -LiteralPath (Join-Path $script:caseRoot 'remote-output.txt') -Raw).Replace("`r`n", "`n")
        if ($log -notmatch '(?m)^CacheRecoveryInvalid$' -or $log -match 'TOKEN_SENTINEL|password|credential') { throw 'safe diagnostic not retained' }
        $expectedExit = if ($script:scenario -eq 'inner') { 2 } else { 255 }
        if ($log -notmatch "(?m)^transport-exit=$expectedExit$") { throw 'numeric exit not retained' }
        if ($log.Length -gt 32768) { throw 'unbounded diagnostic output' }
        if ($script:scenario -eq 'inner' -and $script:observed.diagnosis -ne 'FixturePrunePreviewFailed') { throw 'outer diagnostic lost' }
        if ($script:scenario -eq 'transport' -and $script:observed.diagnosis -ne 'RemoteTransportFailed') { throw 'transport failure hidden' }
        if ($script:scenario -eq 'missing' -and $script:observed.diagnosis -ne 'RemoteResultMissing exit=255') { throw 'missing result exit contaminated' }
    }
    'BRIDGE_COMPLETE'
} finally {
    # Exact fresh fixture-owned directory, never a caller-supplied deletion path.
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
