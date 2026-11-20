# Shared oracle for restart-apphost.ps1 fixture copies.
# ASCII-only for Windows PowerShell 5.1. Dot-source only.
# Recognizes a literal dot-source:
#   . (Join-Path $PSScriptRoot 'relative.ps1')
#   . (Join-Path $PSScriptRoot "relative.ps1")
# A different form is invisible here. The harness that executes the copied
# script still fails when that file is absent.

function Get-AppHostDotSourceRefs {
    param([string]$ScriptText)
    $found = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($ScriptText)) { return @() }
    $patterns = @(
        '(?m)^[ \t]*\.[ \t]*\([ \t]*Join-Path[ \t]+\$PSScriptRoot[ \t]+''([^'']+)''[ \t]*\)',
        '(?m)^[ \t]*\.[ \t]*\([ \t]*Join-Path[ \t]+\$PSScriptRoot[ \t]+"([^"]+)"[ \t]*\)'
    )
    foreach ($pattern in $patterns) {
        $matched = [regex]::Matches($ScriptText, $pattern)
        foreach ($item in $matched) {
            $value = [string]$item.Groups[1].Value
            if (-not [string]::IsNullOrWhiteSpace($value)) { $found.Add(($value -replace '\\', '/')) }
        }
    }
    return @($found.ToArray())
}

function Get-AppHostDotSourceClosure {
    param(
        [Parameter(Mandatory)][string]$ScriptsRoot,
        [Parameter(Mandatory)][string]$EntryRelative
    )
    $ordered = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    $pending = New-Object System.Collections.Generic.List[string]
    $entry = ($EntryRelative -replace '\\', '/')
    $pending.Add($entry)
    $seen[$entry] = $true
    $index = 0
    while ($index -lt $pending.Count) {
        $rel = [string]$pending[$index]
        $index++
        $ordered.Add($rel)
        $path = Join-Path $ScriptsRoot ($rel -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        $text = [System.IO.File]::ReadAllText($path)
        foreach ($child in @(Get-AppHostDotSourceRefs -ScriptText $text)) {
            $norm = [string]$child
            if (-not $seen.ContainsKey($norm)) {
                $seen[$norm] = $true
                $pending.Add($norm)
            }
        }
    }
    return @($ordered.ToArray())
}

function Copy-AppHostDotSourceClosure {
    param(
        [Parameter(Mandatory)][string]$SourceScriptsRoot,
        [Parameter(Mandatory)][string]$DestinationScriptsRoot,
        [Parameter(Mandatory)][string]$EntryRelative
    )
    $closure = @(Get-AppHostDotSourceClosure -ScriptsRoot $SourceScriptsRoot -EntryRelative $EntryRelative)
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $closure) {
        $src = Join-Path $SourceScriptsRoot ($rel -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { $missing.Add($rel) }
    }
    if ($missing.Count -gt 0) {
        throw ('dot-sourced script missing from source: ' + ($missing -join ', '))
    }
    foreach ($rel in $closure) {
        $src = Join-Path $SourceScriptsRoot ($rel -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        $dst = Join-Path $DestinationScriptsRoot ($rel -replace '/', [System.IO.Path]::DirectorySeparatorChar)
        $parent = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $parent)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }
        Copy-Item -LiteralPath $src -Destination $dst -Force
    }
    return $closure
}
