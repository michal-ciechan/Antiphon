#requires -Version 7.0
# Recreate only the readonly script mounts, independent of files baked into an image.
# Wrapper startup without a command must print usage and exit 2 before requesting a slot.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path -Parent $PSScriptRoot
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'c590-remote.sh')) -replace '\\\r?\n', ' '
$launches = @($source -split '\r?\n' | Where-Object { $_ -match '--mount "type=bind,source=\$CHECKOUT/scripts/build-slot\.ps1,target=' })
if ($launches.Count -ne 5) { throw "Expected five cache build-slot launch sites, found $($launches.Count)" }
$root = Join-Path ([IO.Path]::GetTempPath()) ('cache-slot-inputs-' + [guid]::NewGuid().ToString('N'))
$passed = 0
$failed = 0
$controls = 0
try {
    for ($i = 0; $i -lt $launches.Count; $i++) {
        $fixture = Join-Path $root $i
        $wrapper = ''
        $adapter = ''
        foreach ($mount in [regex]::Matches($launches[$i], '--mount "type=bind,source=\$CHECKOUT/(?<source>scripts/[^,"]+),target=(?<target>/[^,"]+),readonly"')) {
            $relative = $mount.Groups['source'].Value
            $target = Join-Path $fixture $mount.Groups['target'].Value.TrimStart('/')
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
            Copy-Item -LiteralPath (Join-Path $repo $relative) -Destination $target
            if ($relative -eq 'scripts/build-slot.ps1') { $wrapper = $target }
            if ($relative -eq 'scripts/lib/build-driver-environment.ps1') { $adapter = $target }
        }
        $output = @(& pwsh -NoProfile -File $wrapper 2>&1)
        $code = $LASTEXITCODE
        if ($code -ne 2 -or ($output -join "`n") -notmatch 'usage: pwsh') {
            $failed++
            Write-Host "FAIL launch=$i wrapper-startup exit=$code expected=2"
            continue
        }
        $passed++
        Write-Host "PASS launch=$i wrapper-startup exit=2"
        # Prove this execution detects the dependency, rather than inheriting it from the host.
        if (-not $adapter) { throw "launch=$i did not mount the adapter" }
        Remove-Item -LiteralPath $adapter
        $output = @(& pwsh -NoProfile -File $wrapper 2>&1)
        $code = $LASTEXITCODE
        if ($code -ne 1 -or ($output -join "`n") -notmatch 'build-driver-environment.ps1') {
            throw "launch=$i missing-adapter control did not fail at import (exit=$code)"
        }
        $controls++
        Write-Host "PASS launch=$i missing-adapter-control exit=1"
    }
    Write-Host "CACHE SLOT INPUTS launches=$($launches.Count) passed=$passed failed=$failed controls=$controls"
} finally {
    # This path is generated here, beneath the process temp directory, and owns only copies.
    if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
}
if ($failed) { exit 1 }
exit 0
