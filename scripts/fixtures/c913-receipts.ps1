param([string]$Repo)
$ErrorActionPreference = 'Stop'
$sha = (& git -C $Repo rev-parse HEAD).Trim()
$global:c913Evidence = [Collections.Generic.HashSet[string]]::new()
$global:c913Left = '3'; $global:c913Right = '3'; $global:c913Fault = 'none'
function Contract([string]$schema) {
    switch ($schema) {
        'legacy' { "schema=legacy`nkind=full`ndigest-type=payload-sha256`ndigest=$('a'*64)`nsmoke=passed`n" }
        '2' { "schema=2`nkind=cold`ndigest-type=none`ndigest=none`nsmoke=not-run`n" }
        '3' { "schema=3`nkind=full`ndigest-type=manifest-sha256`ndigest=$('a'*64)`nsmoke=passed`n" }
    }
}
function global:pwsh {
    param([switch]$NoProfile, [string]$File, [string]$Case, [string]$Manifest)
    $m = Get-Content -Raw -LiteralPath $Manifest | ConvertFrom-Json
    [void]$global:c913Evidence.Add((Split-Path -Parent $m.evidenceRoot))
    $dir = Join-Path $m.evidenceRoot $Case
    [void](New-Item -ItemType Directory -Path $dir -Force)
    $schema = if ($m.runnerId -eq 'server2-temp') { $global:c913Right } else { $global:c913Left }
    $contract = Contract $schema
    if ($m.runnerId -eq 'server2-temp') {
        switch ($global:c913Fault) {
            'duplicate' { $contract += "schema=$schema`n" }
            'missing' { $contract = $contract -replace 'digest-type=.*\n','' }
            'type' { $contract = $contract -replace 'manifest-sha256','payload-sha256' }
            'digest' { $contract = $contract -replace ('a'*64),('b'*64) }
            'smoke' { $contract = $contract -replace 'smoke=passed','smoke=not-run' }
            'toxic' { $contract += "credential=DO_NOT_EXPORT_C913`n" }
        }
    }
    $private = if ($m.runnerId -eq 'server2-temp') { 'antiphon-runner-temp' } else { 'antiphon-runner' }
    $mounts = "volume antiphon-runner-cache-nuget-packages /home/app/.nuget/packages true`nvolume antiphon-runner-cache-nuget-scratch /var/cache/antiphon/nuget-scratch true`nvolume antiphon-runner-cache-npm-content /home/app/.npm/_cacache true`nvolume ${private}_runner-tmp /tmp true`n"
    if ($global:c913Fault -eq 'mount') { $mounts = $mounts -replace '/var/cache/antiphon/nuget-scratch','/wrong' }
    $files = @{
        'c590-result.json' = '{"accepted":true,"exit":0}'
        'status.json' = (@{sessions=0; runnerSessions=0; queuedTasks=0; buildVersion=$sha; dispatchEligible=$true; acceptingNewWork=$true; draining=$false} | ConvertTo-Json -Compress)
        'seed-kind.txt' = $(if ($schema -eq '2') { "cold`n" } else { "full`n" })
        'seed-hash.txt' = ('a'*64) + "`n"
        'seed-contract.txt' = $contract
        'smoke-summary.txt' = "C849_SMOKE runner=$($m.runnerId) uid=1654 restore=0 build=0 run=0 stdout=CARD0849_APPHOST_OK`n"
        'runner-mounts.txt' = $mounts
    }
    if ($global:c913Fault -eq 'smoke-evidence') { $files['smoke-summary.txt'] = 'wrong' }
    foreach ($entry in $files.GetEnumerator()) { [IO.File]::WriteAllText((Join-Path $dir $entry.Key), $entry.Value) }
    $global:LASTEXITCODE = 0
}
function Check([bool]$value, [string]$label) { if (-not $value) { throw "FAIL $label" }; Write-Output "PASS $label" }
function Invoke-Front {
    & (Join-Path $Repo 'scripts/verify-card0849-caches.ps1') -Case Both -Sha $sha | Out-String
}
try {
    $text = Invoke-Front
    Check ($text -cmatch 'schema=3' -and $text -cmatch 'digestType=manifest-sha256') schema3-receipt-accepted
    foreach ($left in @('legacy','2','3')) {
        foreach ($right in @('legacy','2','3')) {
            $global:c913Left=$left; $global:c913Right=$right
            $accepted=$false
            try { $text=Invoke-Front; $accepted=$true } catch { $text=$_.Exception.Message }
            Check ($accepted -eq ($left -ceq $right)) "tuple-$left-$right"
            Check (-not $text.Contains('DO_NOT_EXPORT_C913')) privacy
        }
    }
    $global:c913Left='3'; $global:c913Right='3'
    foreach ($fault in @('duplicate','missing','type','digest','smoke','toxic','mount','smoke-evidence')) {
        $global:c913Fault=$fault; $accepted=$false
        try { $text=Invoke-Front; $accepted=$true } catch { $text=$_.Exception.Message }
        Check (-not $accepted) "$fault-refused"
        Check (-not $text.Contains('DO_NOT_EXPORT_C913')) privacy
    }
    Write-Output 'PASS receipts-complete'
} finally {
    foreach ($path in $global:c913Evidence) { Remove-Item -LiteralPath $path -Recurse -Force }
    Remove-Item Function:pwsh -ErrorAction SilentlyContinue
}
