#requires -Version 7.0
# CARD-1836 S6a. Thin wrapper. The implementation is Antiphon.Nightly.RcDirtyOrder.
# Shadow launch order only. effectiveMode stays Full and gateCredit stays false.
# Containment is the ancestor rule, otherwise one exact cherry-pick trailer line.
# ASCII-only.

if (-not (Get-Command Test-NightlyCoreAvailable -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-core.ps1')
}
if (-not (Test-NightlyCoreAvailable)) { throw 'nightly-core-required' }

function Invoke-RcDirtyCore {
    param([scriptblock]$Body)
    try {
        return (& $Body)
    } catch {
        $ex = $_.Exception
        while ($ex.InnerException -and $ex.InnerException.Message) { $ex = $ex.InnerException }
        throw ([string]$ex.Message)
    }
}

function ConvertTo-RcCamelCase {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -or $Value.GetType().IsValueType) { return $Value }
    if ($Value -is [Collections.IDictionary]) { return $Value }
    if ($Value -is [Collections.IEnumerable]) {
        $copy = [Collections.Generic.List[object]]::new()
        foreach ($item in @($Value)) { $copy.Add((ConvertTo-RcCamelCase $item)) }
        return ,$copy.ToArray()
    }
    $out = [ordered]@{}
    foreach ($property in @($Value.PSObject.Properties)) {
        $kind = [string]$property.MemberType
        if ($kind -ne 'Property' -and $kind -ne 'NoteProperty') { continue }
        $name = [string]$property.Name
        if ([string]::IsNullOrEmpty($name)) { continue }
        $camel = $name.Substring(0, 1).ToLowerInvariant() + $name.Substring(1)
        $out[$camel] = ConvertTo-RcCamelCase $property.Value
    }
    return [pscustomobject]$out
}

function Get-RcDirtyLaunchOrder {
    param(
        [AllowEmptyCollection()][AllowNull()][object[]]$Obligations = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Attempts = @(),
        [AllowEmptyCollection()][AllowNull()][object[]]$Fixes = @(),
        [string]$RepositoryRoot = '',
        [string]$CandidateSha = '')
    if ($null -eq $Obligations) { $Obligations = @() }
    if ($null -eq $Attempts) { $Attempts = @() }
    if ($null -eq $Fixes) { $Fixes = @() }
    $result = Invoke-RcDirtyCore {
        [Antiphon.Nightly.RcDirtyOrder]::Launch($Obligations, $Attempts, $Fixes, $RepositoryRoot, $CandidateSha)
    }.GetNewClosure()
    return (ConvertTo-RcCamelCase $result)
}
