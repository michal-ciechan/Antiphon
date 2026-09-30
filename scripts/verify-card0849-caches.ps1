# CARD-0849 desktop front door. Explicit cases only; no case starts a rollout.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Inventory', 'Fixture', 'Seed', 'Both', 'Retired', 'PrunePreview', 'Prune')]
    [string]$Case,
    [string]$Sha = '',
    [string]$Preview = ''
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Sha) { $Sha = $env:C849_DEPLOY_SHA }
if ($Sha -cnotmatch '^[0-9a-f]{40}$') { throw 'C849_DEPLOY_SHA must be the reviewed full lowercase SHA' }
$head = (& git -C $repo rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $Sha) { throw 'C849 source SHA does not equal checkout HEAD' }
if ($Case -eq 'Prune' -and -not $Preview) { throw 'Prune requires -Preview receipt path' }
if ($Case -ne 'Prune' -and $Preview) { throw 'Preview belongs to Prune only' }
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
    Retired = 'verify-runner-caches-retired'
    PrunePreview = 'runner-cache-prune-preview'
    Prune = 'runner-cache-prune'
}
$cases = if ($Case -eq 'Both') { @('verify-runner-caches', 'verify-runner-caches') } else { @($map[$Case]) }
$runners = if ($Case -eq 'Both') { @('server2', 'server2-temp') } else { @('') }
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
    New-Item -ItemType Directory -Path $manifest.evidenceRoot -Force | Out-Null
    $manifestPath = Join-Path $manifest.evidenceRoot 'manifest.json'
    $manifest | ConvertTo-Json -Compress | Set-Content -LiteralPath $manifestPath -Encoding ascii
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'verify-docker-stack.ps1') -Case $remoteCase -Manifest $manifestPath
    if ($LASTEXITCODE -ne 0) { throw "C849 case failed: $remoteCase runner=$($runners[$i]) evidence=$($manifest.evidenceRoot)" }
}
Write-Output "C849 case=$Case source=$Sha evidence=$evidence"
