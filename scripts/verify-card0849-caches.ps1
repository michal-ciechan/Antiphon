# CARD-0849 desktop front door. Explicit cases only; no case starts a rollout.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Inventory', 'Fixture', 'Seed', 'Reset', 'Both', 'Retired', 'PrunePreview', 'Prune')]
    [string]$Case,
    [string]$Sha = '',
    [string]$Preview = '',
    [string]$SavedDonor = '',
    [switch]$Cold
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Sha) { $Sha = $env:C849_DEPLOY_SHA }
if ($Sha -cnotmatch '^[0-9a-f]{40}$') { throw 'C849_DEPLOY_SHA must be the reviewed full lowercase SHA' }
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $Sha) { throw 'C849 source SHA does not equal checkout HEAD' }
if ($Case -eq 'Prune' -and -not $Preview) { throw 'Prune requires -Preview receipt path' }
if ($Case -ne 'Prune' -and $Preview) { throw 'Preview belongs to Prune only' }
if ($SavedDonor -and $Case -ne 'Seed') { throw 'SavedDonor belongs to Seed only' }
if ($Cold -and $Case -ne 'Seed') { throw 'CacheColdModeInvalid' }
if ($Cold -and $SavedDonor) { throw 'CacheDonorSourceConflict' }
if ($SavedDonor -and ($SavedDonor -cnotmatch '^/[A-Za-z0-9._/-]{1,500}$' -or
        $SavedDonor.Contains('..') -or $SavedDonor.Contains('//'))) { throw 'CacheSavedDonorPathInvalid' }
if ($Preview) {
    $candidate = [System.IO.Path]::GetFullPath($Preview)
    $base = [System.IO.Path]::GetFullPath((Join-Path $repo '.antiphon'))
    if (-not $candidate.StartsWith($base + [System.IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
        throw 'CachePreviewInvalid'
    }
    $Preview = $candidate
}
$previewRunId = ''
if ($Case -eq 'Prune') {
    $previewText = [System.IO.File]::ReadAllText($Preview)
    $runMatch = [regex]::Match($previewText, '(?m)^run=(c849[0-9a-f]{16}0)$')
    $shaMatch = [regex]::Match($previewText, '(?m)^source-sha=([0-9a-f]{40})$')
    if (-not $runMatch.Success -or -not $shaMatch.Success -or $shaMatch.Groups[1].Value -cne $Sha -or
        [System.IO.Path]::GetFileName($Preview) -cne 'preview.txt' -or
        [System.IO.Path]::GetFileName((Split-Path -Parent $Preview)) -cne 'runner-cache-prune-preview') {
        throw 'CachePreviewInvalid'
    }
    $previewRunId = $runMatch.Groups[1].Value
}
$runId = 'c849' + [guid]::NewGuid().ToString('N').Substring(0, 16)
$evidence = Join-Path $repo ('.antiphon/c849-' + $runId)
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$map = @{
    Inventory = 'runner-cache-inventory'
    Fixture = 'runner-cache-fixture'
    Seed = 'runner-cache-seed'
    Reset = 'runner-cache-reset'
    Retired = 'verify-runner-caches-retired'
    PrunePreview = 'runner-cache-prune-preview'
    Prune = 'runner-cache-prune'
}
$cases = @(if ($Case -eq 'Both') { @('verify-runner-caches', 'verify-runner-caches') } else { @($map[$Case]) })
$runners = if ($Case -eq 'Both') { @('server2', 'server2-temp') } else { @('') }
function Read-C849Receipt {
    param([int]$Index, [string]$Name)
    $path = Join-Path (Join-Path (Join-Path $evidence $Index) $cases[$Index]) $Name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "C849 receipt missing: $Name" }
    return [System.IO.File]::ReadAllText($path)
}
function Assert-C849Status {
    param([string]$Text, [bool]$Accepting, [bool]$RequireIdle = $false)
    $status = $Text | ConvertFrom-Json
    if ($null -eq $status.sessions -or $null -eq $status.runnerSessions -or $null -eq $status.queuedTasks -or
        $status.sessions -lt 0 -or $status.runnerSessions -lt 0 -or $status.queuedTasks -lt 0) {
        throw 'C849 unknown runner counters'
    }
    if ($RequireIdle -and ($status.sessions -ne 0 -or $status.runnerSessions -ne 0 -or $status.queuedTasks -ne 0)) {
        throw 'C849 runner is busy'
    }
    if ($Accepting -and ($status.buildVersion -cne $Sha -or $status.dispatchEligible -ne $true -or
            $status.acceptingNewWork -ne $true -or $status.draining -ne $false)) {
        throw 'C849 runner acceptance receipt invalid'
    }
    return $status
}
for ($i = 0; $i -lt $cases.Count; $i++) {
    $remoteCase = $cases[$i]
    $manifest = [ordered]@{
        evidenceRoot = (Join-Path $evidence $i)
        sourceSha = $Sha
        runId = "$runId$i"
        c604Branch = 'master'
    }
    if ($runners[$i]) { $manifest.runnerId = $runners[$i] }
    if ($Case -eq 'Both' -or $Case -eq 'Retired') { $manifest.expectAccepting = $true }
    if ($Preview) { $manifest.preview = $Preview }
    if ($previewRunId) { $manifest.previewRunId = $previewRunId }
    if ($SavedDonor) { $manifest.savedDonor = $SavedDonor }
    if ($Cold) { $manifest.coldSeed = $true }
    New-Item -ItemType Directory -Path $manifest.evidenceRoot -Force | Out-Null
    $manifestPath = Join-Path $manifest.evidenceRoot 'manifest.json'
    $manifest | ConvertTo-Json -Compress | Set-Content -LiteralPath $manifestPath -Encoding ascii
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'verify-docker-stack.ps1') -Case $remoteCase -Manifest $manifestPath
    if ($LASTEXITCODE -ne 0) { throw "C849 case failed: $remoteCase runner=$($runners[$i]) evidence=$($manifest.evidenceRoot)" }
    $verdict = (Read-C849Receipt $i 'c590-result.json') | ConvertFrom-Json
    if ($verdict.accepted -ne $true -or $verdict.exit -ne 0) { throw "C849 case receipt refused: $remoteCase" }
}
switch ($Case) {
    'Fixture' {
        $summary = Read-C849Receipt 0 'fixture-summary.txt'
        foreach ($line in @('source-sha=' + $Sha, 'inventories=2', 'groups=9', 'controls=26',
                'expected-red=26', 'production-mutations=0')) {
            if (($summary -split "`n" | ForEach-Object Trim) -cnotcontains $line) { throw "C849 fixture receipt missing $line" }
        }
        if ($summary -cnotmatch '(?m)^image-id=sha256:[0-9a-f]{64}\r?$') { throw 'C849 fixture image receipt invalid' }
        $groups = @((Read-C849Receipt 0 'fixture-groups.txt') -split "`n" | Where-Object { $_ -match '^PASS F-[1-9]$' })
        $controls = @((Read-C849Receipt 0 'fixture-controls.txt') -split "`n" | Where-Object { $_ -match '^CONTROL PC-[0-9]{2} expected-red observed=' })
        if ($groups.Count -ne 9 -or $controls.Count -ne 26) { throw 'C849 fixture roster incomplete' }
        Write-Output 'C849_FIXTURE groups=9 controls=26 expectedRed=26 inventories=2 failures=0 productionMutations=0'
    }
    'Seed' {
        $seed = Read-C849Receipt 0 'seed.txt'
        if ($seed.Trim() -ceq 'ready=true kind=cold writable=3') {
            $status = Read-C849Receipt 0 'status.json' | ConvertFrom-Json
            $main = Read-C849Receipt 0 'main-status.json' | ConvertFrom-Json
            if ($null -eq $status.retiredAt -or $status.available -ne $false -or
                $status.dispatchEligible -ne $false -or $status.acceptingNewWork -ne $false -or
                $status.draining -ne $true -or $status.retireWhenIdle -ne $true -or
                $status.redirectTo -cne 'server2' -or $status.sessions -ne 0 -or
                $status.queuedTasks -ne 0 -or ($null -ne $status.runnerSessions -and $status.runnerSessions -ne 0) -or
                $null -eq $main.sessions -or $null -eq $main.queuedTasks) { throw 'C849 cold seed status invalid' }
            Write-Output 'C849_SEED kind=cold ready=true writable=3'
            break
        }
        if ($Cold) { throw 'C849 cold seed receipt invalid' }
        if ($seed -match '(?m)^ready=true donor=saved(?: |\r?$)') {
            $status = Read-C849Receipt 0 'status.json' | ConvertFrom-Json
            if ($status.sessions -ne 0 -or $status.queuedTasks -ne 0 -or
                ($null -ne $status.runnerSessions -and $status.runnerSessions -ne 0) -or
                $status.draining -ne $true -or $status.retireWhenIdle -ne $true -or
                $status.redirectTo -ne 'server2') { throw 'C849 saved donor status invalid' }
            Write-Output 'C849_SEED donor=saved ready=true smoke=passed recovery=retained'
            break
        }
        if ($seed -match '(?m)^ready=true donor=\r?$') {
            Write-Output 'C849_SEED donor=none ready=true recovery=retained'
            break
        }
        $status = Assert-C849Status (Read-C849Receipt 0 'status.json') $false $true
        if ($seed -notmatch '(?m)^ready=true donor=[0-9a-f]{64}(?:\r)?$' -and
            $seed -notmatch '(?m)^ready=true donor=[0-9a-f]{12,64} (?:.*)$') { throw 'C849 seed receipt invalid' }
        if ($status.draining -ne $true -or $status.acceptingNewWork -ne $false -or
            $status.dispatchEligible -ne $true -or $status.retireWhenIdle -ne $true -or
            $status.redirectTo -ne 'server2') { throw 'C849 donor reconnect receipt invalid' }
        Write-Output 'C849_SEED donor=server2-temp ready=true smoke=passed recovery=retained'
    }
    'Reset' {
        if ((Read-C849Receipt 0 'reset.txt').Trim() -cne 'reset=true volumes=3 marker=absent') {
            throw 'C849 reset receipt invalid'
        }
        Write-Output 'C849_RESET volumes=3 marker=absent failures=0'
    }
    'Both' {
        $hashes = @()
        $kinds = @((Read-C849Receipt 0 'seed-kind.txt').Trim(), (Read-C849Receipt 1 'seed-kind.txt').Trim())
        if ($kinds[0] -cnotin @('full', 'cold') -or $kinds[1] -cnotin @('full', 'cold')) { throw 'C849 marker kind invalid' }
        if ($kinds[0] -cne $kinds[1]) { throw 'C849 mixed marker kinds' }
        for ($i = 0; $i -lt 2; $i++) {
            [void](Assert-C849Status (Read-C849Receipt $i 'status.json') $true)
            $kind = $kinds[$i]
            if ($kind -eq 'full') {
                $smoke = Read-C849Receipt $i 'smoke-summary.txt'
                if ($smoke -notmatch 'uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK') { throw 'C849 smoke receipt invalid' }
            }
            $mounts = Read-C849Receipt $i 'runner-mounts.txt'
            foreach ($volume in @('antiphon-runner-cache-nuget-packages', 'antiphon-runner-cache-nuget-scratch', 'antiphon-runner-cache-npm-content')) {
                if ($mounts -cnotmatch [regex]::Escape($volume)) { throw "C849 shared mount missing: $volume" }
            }
            $private = if ($i -eq 0) { 'antiphon-runner_runner-tmp' } else { 'antiphon-runner-temp_runner-tmp' }
            if ($mounts -cnotmatch [regex]::Escape($private + ' /tmp true')) { throw 'C849 private tmp mount invalid' }
            if ($kind -eq 'full') { $hashes += (Read-C849Receipt $i 'seed-hash.txt').Trim() }
        }
        if ($kinds[0] -eq 'cold') {
            Write-Output 'C849_BOTH kind=cold runners=2 writableVolumes=3 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0'
            break
        }
        if ($hashes[0] -cnotmatch '^[0-9a-f]{64}$' -or $hashes[0] -cne $hashes[1]) { throw 'C849 seed payload differs' }
        Write-Output 'C849_BOTH runners=2 smokes=2 sharedVolumes=3 privateTmpVolumes=2 tmpMode=1777 failures=0'
    }
    'Retired' {
        [void](Assert-C849Status (Read-C849Receipt 0 'status.json') $true)
        $kind = (Read-C849Receipt 0 'seed-kind.txt').Trim()
        if ($kind -cnotin @('full', 'cold')) { throw 'C849 marker kind invalid' }
        $mounts = Read-C849Receipt 0 'runner-mounts.txt'
        $rollback = Read-C849Receipt 0 'rollback.txt'
        if ($mounts -cnotmatch 'antiphon-runner_runner-tmp /tmp true' -or
            $rollback -cnotmatch '^rollback-image=sha256:[0-9a-f]{64}\r?\n?$') {
            throw 'C849 retired receipt invalid'
        }
        if ($kind -eq 'cold') {
            Write-Output 'C849_RETIRED kind=cold externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true rollback=retained failures=0'
            break
        }
        $smoke = Read-C849Receipt 0 'smoke-summary.txt'
        $hash = (Read-C849Receipt 0 'seed-hash.txt').Trim()
        if ($smoke -notmatch 'uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK' -or
            $hash -cnotmatch '^[0-9a-f]{64}$') { throw 'C849 retired receipt invalid' }
        Write-Output 'C849_RETIRED externalVolumes=3 tempPrivateVolumes=0 mainTmpRetained=true smokes=1 rollback=retained failures=0'
    }
    'PrunePreview' {
        $previewReceipt = Read-C849Receipt 0 'preview.txt'
        if ($previewReceipt -cnotmatch '(?m)^schema=1\r?$' -or
            $previewReceipt -cnotmatch '(?m)^volume-sha256=[0-9a-f]{64}\r?$') { throw 'C849 preview receipt invalid' }
        Write-Output "C849_PRUNE_PREVIEW source=$Sha evidence=$evidence"
    }
    'Prune' {
        $prune = Read-C849Receipt 0 'prune.txt'
        if ($prune -cnotmatch 'pruned=[1-3] .*refill=passed smokes=[12] admission=held') { throw 'C849 prune receipt invalid' }
        Write-Output "C849_PRUNE source=$Sha evidence=$evidence"
    }
    'Inventory' {
        [void](Read-C849Receipt 0 'server2-status.json')
        [void](Read-C849Receipt 0 'server2-temp-status.json')
        Write-Output "C849_INVENTORY runners=2 source=$Sha evidence=$evidence"
    }
}
Write-Output "C849_RECEIPT case=$Case source=$Sha evidence=$evidence"
