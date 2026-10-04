param([string]$Repo)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('c913-wrapper-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$sha = (& git -C $Repo rev-parse HEAD).Trim()
$global:c913Calls = [Collections.Generic.List[object]]::new()
$global:c913Fault = 'none'
$global:c913Creates = 0
function global:docker {
    $a = @($args | ForEach-Object { [string]$_ })
    $global:c913Calls.Add($a)
    $global:LASTEXITCODE = 0
    if ($a[0] -eq 'version') { return '27.5.1' }
    if ($a[0] -eq 'image') {
        if ($a[-1] -like 'c913-test:*' -and $global:c913Calls.FindAll({ param($x) $x[0] -eq 'build' }).Count -eq 0) {
            $global:LASTEXITCODE = 1; return
        }
        if (($a -join ' ') -like '*org.opencontainers.image.revision*') { return $sha }
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
function Check([bool]$condition, [string]$label) { if (-not $condition) { throw "FAIL $label" }; Write-Output "PASS $label" }
function Run-Wrapper([string]$fault) {
    $global:c913Calls.Clear(); $global:c913Creates = 0; $global:c913Fault = $fault
    & (Join-Path $Repo 'scripts/verify-card0660-codex-image.ps1') -Target session-testing -Image "c913-test:$fault" -SourceRevision $sha -ResultsRoot (Join-Path $root $fault) | Out-Null
    return $LASTEXITCODE
}
try {
    $code = Run-Wrapper none
    Check ($code -eq 0) wrapper-success
    $probe = @($global:c913Calls | Where-Object { $_[-1] -eq 'net9-offline' })[0]
    $mounts = @(); for ($i = 0; $i -lt $probe.Count - 1; $i++) { if ($probe[$i] -eq '--mount') { $mounts += $probe[$i + 1] } }
    Check (@($mounts | Where-Object { $_ -cmatch '^type=volume,source=c660q-[a-zA-Z0-9-]+-packages,target=/home/app/\.nuget/packages,volume-nocopy$' }).Count -eq 1) package-mount-contract
    Check (@($mounts | Where-Object { $_ -cmatch '^type=volume,source=c660q-[a-zA-Z0-9-]+-scratch,target=/var/cache/antiphon/nuget-scratch,volume-nocopy$' }).Count -eq 1) scratch-mount-contract
    Check (($probe[[Array]::IndexOf($probe, '--network') + 1] -ceq 'none') -and ($probe[[Array]::IndexOf($probe, '--user') + 1] -ceq '1654:1654')) child-network-uid
    Check (($probe -contains 'PhoneHome__Enabled=false') -and ($probe -join ' ') -cnotmatch '(docker.sock|/state/codex|target=/state[, ]|target=/work[, ])') no-sensitive-child-mounts
    $initializers = @($global:c913Calls | Where-Object { ($_ -join ' ') -match 'chown.*(/home/app/\.nuget/packages|/packages)' })
    Check ($initializers.Count -eq 1 -and ($initializers[0] -join ' ') -notmatch 'chown -R') owned-roots-only
    foreach ($fault in @('child-exit', 'wrong-row', 'duplicate-row', 'image-id', 'partial-create')) {
        $code = Run-Wrapper $fault
        Check ($code -ne 0) "$fault-refused"
        $created = @($global:c913Calls | Where-Object { $_[0] -eq 'volume' -and $_[1] -eq 'create' } | ForEach-Object { $_[-1] })
        if ($fault -eq 'partial-create') { $created = @($created | Select-Object -First 1) }
        $removed = @($global:c913Calls | Where-Object { $_[0] -eq 'volume' -and $_[1] -eq 'rm' } | ForEach-Object { $_[-1] })
        Check (($removed -join ',') -ceq ($created -join ',')) "$fault-owned-cleanup"
    }
    Write-Output 'PASS wrapper-boundaries-complete'
} finally {
    Remove-Item Function:docker -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $root -Recurse -Force
}
