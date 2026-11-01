param([Parameter(Mandatory)][string]$Repo)
$ErrorActionPreference = 'Stop'
. (Join-Path $Repo 'scripts/c590-real.ps1')
$fixtureRoot = Join-Path $Repo ('.antiphon/bridge-model-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
try {
    function ssh {
        # The live bridge quotes the remote script: bash '/home/mc/.../c590-remote.sh'.
        $remote = [string]$args[-1]
        if ($remote -match 'bash ([''"])?/home/mc/antiphon-c590/c590-remote\.sh\1') {
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
    function Assert-BridgeDiagnosticRetained {
        param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][int]$ExpectedExit)
        $log = $Text.Replace("`r`n", "`n")
        if ($log -notmatch '(?m)^CacheRecoveryInvalid$' -or $log -match 'TOKEN_SENTINEL|password|credential') {
            throw 'safe diagnostic not retained'
        }
        if ($log -notmatch "(?m)^transport-exit=$ExpectedExit$") { throw 'numeric exit not retained' }
    }
    function Assert-BridgeDiagnosticRefused {
        param([Parameter(Mandatory)][string]$Text, [Parameter(Mandatory)][int]$ExpectedExit)
        $refused = $false
        try { Assert-BridgeDiagnosticRetained -Text $Text -ExpectedExit $ExpectedExit }
        catch {
            if ($_.Exception.Message -ne 'safe diagnostic not retained' -and $_.Exception.Message -ne 'numeric exit not retained') { throw }
            $refused = $true
        }
        if (-not $refused) { throw 'leaking or missing diagnostic was accepted' }
    }
    foreach ($script:scenario in @('inner', 'transport', 'missing')) {
        $script:caseRoot = Join-Path $fixtureRoot $script:scenario
        $manifest = [pscustomobject]@{ evidenceRoot=$script:caseRoot; sourceSha=('a'*40); runId='c849aaaaaaaaaaaaaaaa0' }
        try { Invoke-C590LiveCase -Case runner-cache-fixture -Manifest $manifest }
        catch { if ($_.Exception.Message -ne 'MODEL_RESULT') { throw } }
        if ($script:observed.accepted -or $script:observed.exit -eq 0) { throw 'transport accepted' }
        $log = Get-Content -LiteralPath (Join-Path $script:caseRoot 'remote-output.txt') -Raw
        if ($log.Length -gt 32768) { throw 'unbounded diagnostic output' }
        $expectedExit = if ($script:scenario -eq 'inner') { 2 } else { 255 }
        Assert-BridgeDiagnosticRetained -Text $log -ExpectedExit $expectedExit
        if ($script:scenario -eq 'inner' -and $script:observed.diagnosis -ne 'FixturePrunePreviewFailed') { throw 'outer diagnostic lost' }
        if ($script:scenario -eq 'transport' -and $script:observed.diagnosis -ne 'RemoteTransportFailed') { throw 'transport failure hidden' }
        if ($script:scenario -eq 'missing' -and $script:observed.diagnosis -ne 'RemoteResultMissing exit=255') { throw 'missing result exit contaminated' }
    }
    # Retained text shaped like a Windows receipt: CRLF, a drive path, native wording.
    $windows = "C:\Antiphon\worktrees\card-task-fbd9be0e\scripts\c590-real.ps1: CacheRecoveryInvalid`r`n" +
        "ssh.exe : CacheRecoveryInvalid`r`n" +
        "The system cannot find the file specified`r`n" +
        "transport-exit=2`r`n"
    Assert-BridgeDiagnosticRetained -Text $windows -ExpectedExit 2
    Assert-BridgeDiagnosticRetained -Text "CacheRecoveryInvalid`rtransport-exit=2`r" -ExpectedExit 2
    Assert-BridgeDiagnosticRetained -Text "CacheRecoveryInvalid`ntransport-exit=2`n" -ExpectedExit 2
    Assert-BridgeDiagnosticRefused -Text "The system cannot find the file specified`ntransport-exit=2`n" -ExpectedExit 2
    Assert-BridgeDiagnosticRefused -Text "CacheRecoveryInvalid credential=TOKEN_SENTINEL_BRIDGE`ntransport-exit=2`n" -ExpectedExit 2
    Assert-BridgeDiagnosticRefused -Text "password=TOKEN_SENTINEL_BRIDGE`nCacheRecoveryInvalid`ntransport-exit=2`n" -ExpectedExit 2
    'BRIDGE_COMPLETE'
} finally {
    # Exact fresh fixture-owned directory, never a caller-supplied deletion path.
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
