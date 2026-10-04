param([string]$Repo)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('c913-wrapper-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$sha = (& git -C $Repo rev-parse HEAD).Trim()
$global:c913Calls = [Collections.Generic.List[object]]::new()
$global:c913Fault = 'none'
$global:c913Creates = 0
# Git is an observed process boundary. A deliberate edit in a later Mutation
# snapshot must reach the wrapper guard being tested, rather than fail setup.
function global:git {
    $global:LASTEXITCODE = 0
    $command = @($args | ForEach-Object { [string]$_ }) -join ' '
    if ($command -ceq 'rev-parse HEAD') { return $sha }
    if ($command -ceq 'status --porcelain --untracked-files=no') {
        if ($global:c913Fault -eq 'dirty') { return ' M owned-fixture' }
        return
    }
    throw "Unexpected Git observation: $command"
}
function global:docker {
    $a = @($args | ForEach-Object { [string]$_ })
    $global:c913Calls.Add($a)
    $global:LASTEXITCODE = 0
    if ($a[0] -eq 'version') { return '27.5.1' }
    if ($a[0] -eq 'image') {
        if ($a[-1] -like 'c913-test:*' -and $global:c913Fault -ne 'revision' -and $global:c913Calls.FindAll({ param($x) $x[0] -eq 'build' }).Count -eq 0) {
            $global:LASTEXITCODE = 1; return
        }
        if (($a -join ' ') -like '*org.opencontainers.image.revision*') {
            if ($global:c913Fault -eq 'revision' -and $a[-1] -like 'c913-test:*') { return 'c' * 40 }
            return $sha
        }
        if ($global:c913Fault -eq 'image-id') { return 'not-an-image' }
        return 'sha256:' + ('b' * 64)
    }
    if ($a[0] -eq 'volume' -and $a[1] -eq 'create') {
        $global:c913Creates++
        if ($global:c913Fault -eq 'partial-create' -and $global:c913Creates -eq 2) { $global:LASTEXITCODE = 1; return }
        return $a[-1]
    }
    if ($a[0] -eq 'run') {
        $at = [Array]::IndexOf($a, '/c660/verify-codex-image.sh')
        if ($at -ge 0) {
            $row = $a[$at + 1]
            if ($row -eq 'net9-offline') {
                switch ($global:c913Fault) {
                    'child-exit' { $global:LASTEXITCODE = 17; return "C660_ROW $row ok native" }
                    'wrong-row' { return 'C660_ROW wrong ok native' }
                    'duplicate-row' { return @("C660_ROW $row ok native", "C660_ROW $row ok native") }
                }
            }
            return "C660_ROW $row ok native"
        }
    }
}
function Check([bool]$condition, [string]$label) {
    $witness = switch -Wildcard ($label) {
        no-sensitive-child-mounts { 'isolated-child-allowlist' }
        revision-refused { 'revision-mismatch-refused' }
        child-exit-refused { 'child-exit-required' }
        wrong-row-refused { 'matching-row-required' }
        duplicate-row-refused { 'matching-row-required' }
        '*-owned-cleanup' { 'cleanup-owned-volumes-only' }
        default { $label }
    }
    if (-not $condition) { throw "FAIL $witness [case=$label]" }
    Write-Output "PASS $label"
}
function Run-Wrapper([string]$fault) {
    $global:c913Calls.Clear(); $global:c913Creates = 0; $global:c913Fault = $fault
    & (Join-Path $Repo 'scripts/verify-card0660-codex-image.ps1') -Target session-testing -Image "c913-test:$fault" -SourceRevision $sha -ResultsRoot (Join-Path $root $fault) -SkipBuild:($fault -eq 'revision') | Out-Null
    return $LASTEXITCODE
}
try {
    $code = Run-Wrapper none
    Check ($code -eq 0) wrapper-success
    $probe = @($global:c913Calls | Where-Object { $_[-1] -eq 'net9-offline' })[0]
    $mounts = @(); for ($i = 0; $i -lt $probe.Count - 1; $i++) { if ($probe[$i] -eq '--mount') { $mounts += $probe[$i + 1] } }
    Check (@($mounts | Where-Object { $_ -cmatch '^type=volume,source=c660q-[a-zA-Z0-9-]+-packages,target=/home/app/\.nuget/packages,volume-nocopy$' }).Count -eq 1) package-mount-contract
    Check (@($mounts | Where-Object { $_ -cmatch '^type=volume,source=c660q-[a-zA-Z0-9-]+-scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy$' }).Count -eq 1) private-scratch-contract
    Check ($probe[[Array]::IndexOf($probe, '--network') + 1] -ceq 'none') network-none
    Check ($probe[[Array]::IndexOf($probe, '--user') + 1] -ceq '1654:1654') uid-1654
    Check (($probe -contains 'PhoneHome__Enabled=false') -and ($probe -join ' ') -cnotmatch '(docker.sock|/state/codex|target=/state[, ]|target=/work[, ])') no-sensitive-child-mounts
    $initializers = @($global:c913Calls | Where-Object { ($_ -join ' ') -match 'chown 1654:1654' })
    Check ($initializers.Count -eq 1 -and $initializers[0][-1] -ceq 'set -eu; for p in /home/app/.nuget/packages /var/cache/antiphon/nuget-scratch; do test -d "$p"; test -z "$(find "$p" -mindepth 1 -print -quit)"; chown 1654:1654 "$p"; chmod 0700 "$p"; done') owned-roots-only
    $initMounts = @(); $init = $initializers[0]
    for ($i = 0; $i -lt $init.Count - 1; $i++) { if ($init[$i] -eq '--mount') { $initMounts += $init[$i + 1] } }
    $createdPair = @($global:c913Calls | Where-Object { $_[0] -eq 'volume' -and $_[1] -eq 'create' -and $_[-1] -match '-(packages|scratch)$' } | ForEach-Object { $_[-1] })
    Check ($createdPair.Count -eq 2 -and $initMounts.Count -eq 2 -and
        $initMounts[0] -ceq "type=volume,source=$($createdPair[0]),target=/home/app/.nuget/packages,volume-nocopy" -and
        $initMounts[1] -ceq "type=volume,source=$($createdPair[1]),target=/var/cache/antiphon/nuget-scratch,volume-nocopy") owned-init-only
    foreach ($fault in @('child-exit', 'wrong-row', 'duplicate-row', 'image-id', 'partial-create', 'revision', 'dirty')) {
        $code = Run-Wrapper $fault
        Check ($code -ne 0) "$fault-refused"
        if ($fault -eq 'dirty') { Check ($global:c913Calls.Count -eq 0) dirty-source-no-docker }
        $created = @($global:c913Calls | Where-Object { $_[0] -eq 'volume' -and $_[1] -eq 'create' } | ForEach-Object { $_[-1] })
        if ($fault -eq 'partial-create') { $created = @($created | Select-Object -First 1) }
        $removed = @($global:c913Calls | Where-Object { $_[0] -eq 'volume' -and $_[1] -eq 'rm' } | ForEach-Object { $_[-1] })
        Check (($removed -join ',') -ceq ($created -join ',')) "$fault-owned-cleanup"
    }
    Write-Output 'PASS wrapper-boundaries-complete'
} finally {
    Remove-Item Function:docker -ErrorAction SilentlyContinue
    Remove-Item Function:git -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force
}
