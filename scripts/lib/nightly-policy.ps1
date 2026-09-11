#requires -Version 5.1
# CARD-0487: test-execution-policy load, hash and project-universe census.
# ASCII-only.

if ($script:AntiphonNightlyPolicyLoaded) { return }
$script:AntiphonNightlyPolicyLoaded = $true

$script:NightlyPolicySchemaVersion = 1

function Get-NightlyRepoRootFromScript {
    param([string]$StartPath)
    if ([string]::IsNullOrWhiteSpace($StartPath)) { $StartPath = $PSScriptRoot }
    $dir = Get-Item -LiteralPath $StartPath
    while ($null -ne $dir) {
        $sln = Join-Path $dir.FullName 'Antiphon.sln'
        if (Test-Path -LiteralPath $sln) { return $dir.FullName }
        $dir = $dir.Parent
    }
    throw 'Could not locate Antiphon.sln'
}

function Get-NightlyPolicyPath {
    param([string]$RepoRoot)
    return (Join-Path $RepoRoot (Join-Path 'tests' 'test-execution-policy.json'))
}

function ConvertTo-NightlyPolicyMap {
    param($Object)
    $map = New-Object 'System.Collections.Specialized.OrderedDictionary'
    if ($null -eq $Object) { return $map }
    if ($Object -is [System.Collections.IDictionary]) {
        foreach ($k in @($Object.Keys)) {
            $name = [string]$k
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if (-not $map.Contains($name)) { $map.Add($name, $Object[$k]) }
        }
        return $map
    }
    foreach ($p in @($Object.PSObject.Properties)) {
        if ($p.MemberType -ne 'NoteProperty') { continue }
        $name = [string]$p.Name
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if (-not $map.Contains($name)) { $map.Add($name, $p.Value) }
    }
    return $map
}

function Get-NightlyPolicyCanonicalPayload {
    param($Object)
    $map = ConvertTo-NightlyPolicyMap -Object $Object
    $copy = New-Object 'System.Collections.Specialized.OrderedDictionary'
    $dict = [System.Collections.IDictionary]$map
    $enum = $dict.GetEnumerator()
    while ($enum.MoveNext()) {
        $name = [string]$enum.Key
        if ($name -eq 'policyHash') { continue }
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if (-not $copy.Contains($name)) { $copy.Add($name, $enum.Value) }
    }
    return (ConvertTo-NightlyCanonicalJson -Object $copy)
}

function Get-NightlyPolicyHash {
    param($Object)
    $canonical = Get-NightlyPolicyCanonicalPayload -Object $Object
    return (Get-NightlySha256Text -Text $canonical)
}

function Read-NightlyExecutionPolicy {
    param([string]$RepoRoot, [string]$PolicyPath = '')
    if ([string]::IsNullOrWhiteSpace($PolicyPath)) {
        $PolicyPath = Get-NightlyPolicyPath -RepoRoot $RepoRoot
    }
    if (-not (Test-Path -LiteralPath $PolicyPath)) {
        throw ('policy missing: {0}' -f $PolicyPath)
    }
    $raw = [System.IO.File]::ReadAllText($PolicyPath)
    $obj = $raw | ConvertFrom-Json
    $version = 0
    if ($obj.schemaVersion) { $version = [int]$obj.schemaVersion }
    if ($version -ne $script:NightlyPolicySchemaVersion) {
        throw ('unsupported policy schema {0}' -f $version)
    }
    $computed = Get-NightlyPolicyHash -Object $obj
    $stated = [string]$obj.policyHash
    if ([string]::IsNullOrWhiteSpace($stated) -or -not [string]::Equals($stated, $computed, [StringComparison]::OrdinalIgnoreCase)) {
        throw ('stale policy hash stated={0} computed={1}' -f $stated, $computed)
    }
    return [pscustomobject]@{
        Path = $PolicyPath
        Object = $obj
        Hash = $computed
        SchemaVersion = $version
    }
}

function Get-NightlyIsTestProjectPaths {
    param([string]$RepoRoot)
    $root = ConvertTo-NightlyCanonicalPath -Path $RepoRoot
    $files = Get-ChildItem -Path $root -Filter '*.csproj' -Recurse -File -ErrorAction SilentlyContinue
    $hits = @()
    foreach ($f in $files) {
        $full = $f.FullName
        $relParts = $full.Substring($root.Length).Split([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $skip = $false
        foreach ($p in $relParts) {
            if ($p -eq 'bin' -or $p -eq 'obj' -or $p.StartsWith('bin-')) { $skip = $true; break }
        }
        if ($skip) { continue }
        $text = [System.IO.File]::ReadAllText($full)
        if ($text -match '(?is)<IsTestProject>\s*true\s*</IsTestProject>') {
            $rel = $full.Substring($root.Length).TrimStart('\', '/')
            $hits += ($rel -replace '\\', '/')
        }
    }
    return @($hits | Sort-Object)
}

function Get-NightlyPolicyProjectUniverse {
    param($PolicyObject)
    $ids = @()
    if ($PolicyObject.suites) {
        foreach ($p in $PolicyObject.suites.PSObject.Properties) {
            $suite = $p.Value
            if ($suite.project) {
                $ids += ([string]$suite.project -replace '\\', '/')
            }
        }
    }
    if ($PolicyObject.projects) {
        foreach ($p in $PolicyObject.projects) {
            $ids += ([string]$p -replace '\\', '/')
        }
    }
    return @($ids | Select-Object -Unique | Sort-Object)
}

function Test-NightlyPolicyProjectUniverse {
    param([string]$RepoRoot, $PolicyObject)
    $disk = @(Get-NightlyIsTestProjectPaths -RepoRoot $RepoRoot)
    $policy = @(Get-NightlyPolicyProjectUniverse -PolicyObject $PolicyObject)
    $missing = @($disk | Where-Object { $policy -notcontains $_ })
    $extra = @($policy | Where-Object { $disk -notcontains $_ })
    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        return [pscustomobject]@{ Ok = $true; Missing = @(); Extra = @(); Disk = $disk; Policy = $policy }
    }
    $unmatched = @($missing + $extra)
    return [pscustomobject]@{
        Ok = $false
        Missing = $missing
        Extra = $extra
        Unmatched = $unmatched
        Disk = $disk
        Policy = $policy
        Message = ('unmatched project {0}' -f ($unmatched -join ', '))
    }
}

function Get-NightlySuiteMap {
    param($PolicyObject)
    $map = @{}
    foreach ($p in $PolicyObject.suites.PSObject.Properties) {
        $map[$p.Name] = $p.Value
    }
    return $map
}

function Get-NightlyFallbackSuiteUniverse {
    return @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging', 'client', 'scripts')
}

function Get-NightlyRequiredSuiteUniverse {
    param($PolicyObject)
    $selected = @()
    if ($PolicyObject -and $PolicyObject.defaultSuites) {
        foreach ($id in @($PolicyObject.defaultSuites)) {
            $name = [string]$id
            if (-not [string]::IsNullOrWhiteSpace($name)) { $selected += $name }
        }
    }
    if ($selected.Count -eq 0) {
        $selected = @(Get-NightlyFallbackSuiteUniverse)
    }
    $map = @{}
    if ($PolicyObject) { $map = Get-NightlySuiteMap -PolicyObject $PolicyObject }
    $required = @()
    foreach ($id in $selected) {
        $suite = $null
        if ($map.ContainsKey($id)) { $suite = $map[$id] }
        if ($suite -and [string]$suite.mode -eq 'manual') { continue }
        $required += $id
    }
    return $required
}

function Test-NightlyRequiredSuitesPresent {
    param([string[]]$Selected, [string[]]$Required)
    $sel = @{}
    foreach ($s in @($Selected)) {
        $name = [string]$s
        if (-not [string]::IsNullOrWhiteSpace($name)) { $sel[$name] = $true }
    }
    $missing = @()
    foreach ($r in @($Required)) {
        $name = [string]$r
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if (-not $sel.ContainsKey($name)) { $missing += $name }
    }
    return [pscustomobject]@{ Ok = ($missing.Count -eq 0); Missing = $missing }
}

function Resolve-NightlySelectedSuites {
    param($PolicyObject, [string[]]$Suites)
    $map = Get-NightlySuiteMap -PolicyObject $PolicyObject
    $valid = @($map.Keys)
    $selected = @()
    if ($null -eq $Suites -or $Suites.Count -eq 0) {
        $selected = @(Get-NightlyRequiredSuiteUniverse -PolicyObject $PolicyObject)
        if ($selected.Count -eq 0) {
            $selected = @(Get-NightlyFallbackSuiteUniverse)
        }
    } else {
        $seen = @{}
        $emptyParts = 0
        foreach ($suite in $Suites) {
            foreach ($part in @($suite -split ',')) {
                $n = $part.Trim().ToLowerInvariant()
                if ([string]::IsNullOrWhiteSpace($n)) { $emptyParts++; continue }
                if ($valid -notcontains $n) {
                    throw ('unknown suite {0}' -f $n)
                }
                if (-not $seen.ContainsKey($n)) {
                    $seen[$n] = $true
                    $selected += $n
                }
            }
        }
        if ($selected.Count -eq 0) {
            throw 'empty suite selection'
        }
        if ($emptyParts -gt 0 -and $selected.Count -eq 0) {
            throw 'empty suite selection'
        }
    }
    return $selected
}

function Get-NightlyScriptCensus {
    param($PolicyObject)
    $items = @()
    if ($PolicyObject.scriptCensus) {
        foreach ($row in @($PolicyObject.scriptCensus)) { $items += $row }
    }
    return $items
}

function Get-NightlySafeChildEnvironment {
    param($PolicyObject, [string]$SuiteId, [hashtable]$BaseEnvironment = $null)
    $envMap = @{}
    if ($null -ne $BaseEnvironment) {
        foreach ($k in $BaseEnvironment.Keys) { $envMap[$k] = $BaseEnvironment[$k] }
    } else {
        foreach ($entry in [System.Environment]::GetEnvironmentVariables().GetEnumerator()) {
            $envMap[[string]$entry.Key] = [string]$entry.Value
        }
    }
    $clear = @()
    if ($PolicyObject.safeEnvironment -and $PolicyObject.safeEnvironment.clear) {
        foreach ($n in @($PolicyObject.safeEnvironment.clear)) { $clear += [string]$n }
    }
    foreach ($name in $clear) {
        if ($envMap.ContainsKey($name)) { $envMap.Remove($name) }
        $envMap[$name] = ''
    }
    $set = $null
    if ($PolicyObject.safeEnvironment -and $PolicyObject.safeEnvironment.set) {
        $set = $PolicyObject.safeEnvironment.set
    }
    if ($set) {
        foreach ($p in $set.PSObject.Properties) {
            $envMap[$p.Name] = [string]$p.Value
        }
    }
    if ($SuiteId -eq 'messaging') {
        $envMap['ANTIPHON_BROKER_TESTS'] = '1'
    }
    return $envMap
}

function Test-NightlyCredentialLeak {
    param([string]$Text, [string[]]$Sentinels)
    foreach ($s in @($Sentinels)) {
        if ([string]::IsNullOrWhiteSpace($s)) { continue }
        if ($Text.Contains($s)) { return $true }
    }
    return $false
}
