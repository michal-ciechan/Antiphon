#requires -Version 7.0
<#
.SYNOPSIS
    Reproduce or verify the Linux fake-provider apphost/directory collision.
    Invoke through scripts/build-slot.ps1; all builds run in the foreground.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Pty', 'E2E')][string]$Project,
    [Parameter(Mandatory)][ValidateSet('Red', 'Green')][string]$Phase,
    [Parameter(Mandatory)][ValidatePattern('^bin-[A-Za-z0-9][A-Za-z0-9-]*/$')][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
if (-not $IsLinux) { throw 'This witness requires Linux extensionless apphosts.' }
$repo = Split-Path $PSScriptRoot -Parent
$name = if ($Project -eq 'Pty') { 'Antiphon.Agents.Pty.Tests' } else { 'Antiphon.E2E' }
$providers = if ($Project -eq 'Pty') { @('fakeclaude', 'fakegrok') } else { @('fakegrok') }
$consumer = Join-Path $repo "tests/$name/$OutputPath"
if (Test-Path -LiteralPath $consumer) { throw "Fresh output required: $consumer" }
Push-Location $repo
try {
    $iterations = if ($Phase -eq 'Red') { 1 } else { 2 }
    for ($iteration = 1; $iteration -le $iterations; $iteration++) {
        # Preserve SDK defaults: no UseAppHost=false or shared absolute output.
        $lines = @(& dotnet build "tests/$name" "--property:OutputPath=$OutputPath" --nologo -maxcpucount:1 -nodeReuse:false 2>&1)
        $buildExit = $LASTEXITCODE
        $lines | Write-Output
        if ($Phase -eq 'Red') {
            $provider = $providers[0]
            $destination = [regex]::Escape($OutputPath + $provider + '/')
            if ($buildExit -eq 0 -or -not ($lines -match "error MSB3021:.*$destination") -or
                -not (Test-Path -LiteralPath (Join-Path $consumer $provider) -PathType Leaf)) {
                throw 'Expected a failed copy into a provider directory occupied by an apphost.'
            }
            $otherErrors = @($lines | Where-Object { $_ -match '\berror\s+[A-Za-z]+\d+\s*:' -and $_ -notmatch '\berror MSB302[17]:' })
            if ($otherErrors.Count) { throw 'Unrelated build errors do not prove the collision.' }
            Write-Output "LAYOUT $Project red witnessed buildExit=$buildExit"
        } else {
            if ($buildExit -ne 0) { throw "Build $iteration failed: $buildExit" }
            foreach ($provider in $providers) {
                $directory = Join-Path $consumer $provider
                if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Missing directory: $directory" }
                foreach ($suffix in @('', '.dll', '.deps.json', '.runtimeconfig.json')) {
                    $file = Join-Path $directory "$provider$suffix"
                    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing staged output: $file" }
                }
            }
            if (-not (Test-Path -LiteralPath (Join-Path $consumer "$name.dll") -PathType Leaf)) { throw 'Missing test assembly.' }
            if ($Project -eq 'Pty' -and -not (Test-Path -LiteralPath (Join-Path $consumer 'fakeclaude.dll') -PathType Leaf)) {
                throw 'The managed fakeclaude reference must remain available at runtime.'
            }
            Write-Output "LAYOUT $Project build=$iteration passed"
        }
    }
} finally { Pop-Location }
