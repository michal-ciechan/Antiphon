#requires -Version 5.1
# CARD-0487: nightly suite execution, evidence and coverage completeness.
# ASCII-only. Does not clone, fetch, reset or change git state.

if ($script:AntiphonNightlyTestsImplLoaded) { return }
$script:AntiphonNightlyTestsImplLoaded = $true

# CARD-0599 D-6: kept as the pre-existing nightly inventory. Native ownership is now
# decided from the suite definition itself (Test-NightlySuiteIsNative) so a profile can
# add e2e without a second hard-coded list drifting out of step.
$script:NightlyNativeGroups = @('antiphon', 'session-runner', 'pty-host', 'agents-pty', 'messaging')

function Get-NightlyExecutionPlatform {
    # The seam replaces only OS observation, never policy or coverage decisions.
    $platform = if ($script:NightlySeams -and $script:NightlySeams.HostPlatform) {
        [string]$script:NightlySeams.HostPlatform.Invoke()
    } elseif ([IO.Path]::DirectorySeparatorChar -eq '\') { 'windows' }
    elseif ($IsLinux) { 'linux' }
    else { '' }
    if ($platform -cnotin @('windows', 'linux')) { throw 'unsupported-host-platform' }
    return $platform
}

function Get-NightlyProfileExecutionChunks {
    param([object[]]$DiscoveryNodes, [object[]]$Chunks, [object[]]$Exclusions)
    $index = 0
    foreach ($chunk in $Chunks) {
        $assigned = @($DiscoveryNodes | Where-Object {
            Test-NightlyClassAssigned -ClassName $_.ClassName -TypeName $_.Type -RequiredClasses @($chunk.classes)
        })
        $tagged = foreach ($node in $assigned) {
            [pscustomobject]@{ Node = $node; Bare = (Get-NightlyBareClassIdentity -Name ([string]$node.ClassName)) }
        }
        foreach ($group in @($tagged | Group-Object Bare)) {
            $required = @($group.Group | Where-Object {
                -not (Test-NightlyDiscoveryExcluded $_.Node $Exclusions -ProfileAware)
            })
            if ($required.Count -eq 0) { continue }
            $bare = [string]$group.Name
            if ($required.Count -eq @($group.Group).Count) {
                [pscustomobject]@{ id = ('{0}-profile-{1}' -f $chunk.id, (++$index)); classes = @($bare); exactFilter = ''; nodes = @() }
            } else {
                # Preserve sibling methods without launching the excluded method.
                # Exact leaves avoid the combined-filter discovery hint ambiguity.
                # The filter type is the bare metadata name: TUnit does not put
                # constructor arguments on the treenode path.
                foreach ($method in @($required | Group-Object { $_.Node.Method })) {
                    $node = @($method.Group)[0].Node
                    $dot = $bare.LastIndexOf('.')
                    if ($dot -lt 1) { throw 'profile-discovery-namespace-required' }
                    $filter = '/*/{0}/{1}/{2}' -f $bare.Substring(0, $dot), $bare.Substring($dot + 1), $node.Method
                    [pscustomobject]@{ id = ('{0}-profile-{1}' -f $chunk.id, (++$index)); classes = @($bare); exactFilter = $filter; nodes = @($method.Group | ForEach-Object { $_.Node }) }
                }
            }
        }
    }
}

function Get-NightlyBareTypeParts {
    param([string]$ClassName)
    $bare = Get-NightlyBareClassIdentity -Name $ClassName
    if ([string]::IsNullOrWhiteSpace($bare)) { return $null }
    $dot = $bare.LastIndexOf('.')
    if ($dot -lt 1) { return $null }
    return [pscustomobject]@{
        Bare = $bare
        Namespace = $bare.Substring(0, $dot)
        Type = $bare.Substring($dot + 1)
    }
}

function Test-NightlyTypeNamePrefixes {
    param([string]$Left, [string]$Right)
    if ([string]::IsNullOrEmpty($Left) -or [string]::IsNullOrEmpty($Right)) { return $false }
    if ($Left.Length -ge $Right.Length) { return $false }
    return $Right.StartsWith($Left, [StringComparison]::Ordinal)
}

function New-NightlyClassGroupFilter {
    param([string]$Namespace, [string[]]$TypeNames)
    $ordered = @($TypeNames | Sort-Object)
    $parts = foreach ($type in $ordered) { '(' + $type + '*)' }
    return ('/*/' + $Namespace + '/' + ($parts -join '|') + '/*')
}

function Test-NightlyClassGroupFilter {
    param([string[]]$Classes, [string]$GroupFilter)
    if ([string]::IsNullOrWhiteSpace($GroupFilter)) { return $false }
    $matched = [regex]::Match($GroupFilter, '^/\*/([A-Za-z_][A-Za-z0-9_.]*)/(\([A-Za-z_][A-Za-z0-9_]*\*\)(\|\([A-Za-z_][A-Za-z0-9_]*\*\))*)/\*$')
    if (-not $matched.Success) { return $false }
    $ns = $matched.Groups[1].Value
    $body = $matched.Groups[2].Value
    $operands = @()
    foreach ($op in [regex]::Matches($body, '\(([A-Za-z_][A-Za-z0-9_]*)\*\)')) {
        $operands += $op.Groups[1].Value
    }
    $classList = @($Classes)
    if ($operands.Count -lt 2 -or $classList.Count -ne $operands.Count) { return $false }
    $seen = @{}
    foreach ($op in $operands) {
        if ($seen.ContainsKey($op)) { return $false }
        $seen[$op] = $true
    }
    for ($i = 0; $i -lt $operands.Count; $i++) {
        for ($j = $i + 1; $j -lt $operands.Count; $j++) {
            if (Test-NightlyTypeNamePrefixes -Left $operands[$i] -Right $operands[$j]) { return $false }
            if (Test-NightlyTypeNamePrefixes -Left $operands[$j] -Right $operands[$i]) { return $false }
        }
    }
    $have = @{}
    foreach ($c in $classList) {
        $parts = Get-NightlyBareTypeParts -ClassName ([string]$c)
        if ($null -eq $parts -or $parts.Namespace -cne $ns) { return $false }
        if (-not $have.ContainsKey($parts.Type)) { $have[$parts.Type] = 0 }
        $have[$parts.Type] = [int]$have[$parts.Type] + 1
    }
    foreach ($op in $operands) {
        if (-not $have.ContainsKey($op) -or [int]$have[$op] -ne 1) { return $false }
    }
    return $true
}

# CARD-1404. An ordinary required node weighs 1. Slow weighs 4. Land*,
# Delivery* and Check* double that. 120 stays near ten minutes of serial
# land-lane work, under watchdogs.antiphon (3600000). A class already over
# the cap still launches, alone; the packer does not split it into methods.
$script:NightlyLaunchGroupNodeCap = 120
$script:NightlyLaunchSlowNodeWeight = 4
$script:NightlyLaunchHeavyFamilyWeight = 2

function Test-NightlyHeavyFamilyType {
    param([string]$TypeName)
    if ([string]::IsNullOrEmpty($TypeName)) { return $false }
    foreach ($prefix in @('Land', 'Delivery', 'Check')) {
        if ($TypeName.StartsWith($prefix, [StringComparison]::Ordinal)) { return $true }
    }
    return $false
}

function Get-NightlyLaunchClassCost {
    param([object[]]$Nodes, [string]$TypeName)
    $count = 0
    $weight = 0
    $heavy = Test-NightlyHeavyFamilyType -TypeName $TypeName
    foreach ($node in @($Nodes)) {
        if ($null -eq $node) { continue }
        $count++
        $unit = 1
        foreach ($category in @($node.Categories)) {
            if ([string]$category -ceq 'Slow') { $unit = [int]$script:NightlyLaunchSlowNodeWeight; break }
        }
        if ($heavy) { $unit = $unit * [int]$script:NightlyLaunchHeavyFamilyWeight }
        $weight += $unit
    }
    if ($count -eq 0) {
        $count = 1
        $weight = $(if ($heavy) { [int]$script:NightlyLaunchHeavyFamilyWeight } else { 1 })
    }
    return [pscustomobject]@{ NodeCount = $count; Weight = $weight }
}

function Get-NightlyPackedItemNodes {
    param($Item)
    if ($Item.PSObject.Properties.Name -contains 'NodeCount') { return [int]$Item.NodeCount }
    return 1
}

function Get-NightlyPackedItemWeight {
    param($Item)
    if ($Item.PSObject.Properties.Name -contains 'Weight') { return [int]$Item.Weight }
    return 1
}

function Split-NightlyClassGroupsAtSize {
    param($ByNamespace, $Unsafe, [int]$MaxSize, [int]$MaxNodes, [int]$MaxWeight)
    $groups = New-Object System.Collections.ArrayList
    foreach ($ns in (@($ByNamespace.Keys) | Sort-Object)) {
        $singles = New-Object System.Collections.ArrayList
        $pool = New-Object System.Collections.ArrayList
        foreach ($item in @($ByNamespace[$ns] | Sort-Object Type, Bare)) {
            $key = $ns + '|' + [string]$item.Type
            if ($Unsafe.Contains($key)) { [void]$singles.Add($item) }
            else { [void]$pool.Add($item) }
        }
        $bins = New-Object System.Collections.ArrayList
        foreach ($item in $pool) {
            $placed = $false
            $addNodes = Get-NightlyPackedItemNodes $item
            $addWeight = Get-NightlyPackedItemWeight $item
            foreach ($bin in @($bins)) {
                if ($bin.Count -ge $MaxSize) { continue }
                if ($bin.Count -gt 0) {
                    $haveNodes = 0
                    $haveWeight = 0
                    foreach ($have in @($bin)) {
                        $haveNodes += Get-NightlyPackedItemNodes $have
                        $haveWeight += Get-NightlyPackedItemWeight $have
                    }
                    if (($haveNodes + $addNodes) -gt $MaxNodes) { continue }
                    if (($haveWeight + $addWeight) -gt $MaxWeight) { continue }
                }
                [void]$bin.Add($item)
                $placed = $true
                break
            }
            if (-not $placed) {
                $bin = New-Object System.Collections.ArrayList
                [void]$bin.Add($item)
                [void]$bins.Add($bin)
            }
        }
        foreach ($bin in @($bins)) { [void]$groups.Add($bin) }
        foreach ($item in @($singles)) {
            $alone = New-Object System.Collections.ArrayList
            [void]$alone.Add($item)
            [void]$groups.Add($alone)
        }
    }
    return [pscustomobject]@{ Groups = $groups }
}

function ConvertTo-NightlyProfileLaunchGroups {
    param([object[]]$Leaves, [object[]]$DiscoveryNodes)
    $exact = New-Object System.Collections.ArrayList
    $classItems = New-Object System.Collections.ArrayList
    $nodesByBare = @{}
    foreach ($node in @($DiscoveryNodes)) {
        if ($null -eq $node) { continue }
        $bare = Get-NightlyBareClassIdentity -Name ([string]$node.ClassName)
        if ([string]::IsNullOrWhiteSpace($bare)) { continue }
        if (-not $nodesByBare.ContainsKey($bare)) {
            $nodesByBare[$bare] = New-Object System.Collections.ArrayList
        }
        [void]$nodesByBare[$bare].Add($node)
    }
    $sourceId = 'all'
    foreach ($leaf in @($Leaves)) {
        $id = [string]$leaf.id
        $mark = $id.IndexOf('-profile-')
        if ($mark -gt 0) { $sourceId = $id.Substring(0, $mark) }
        if (-not [string]::IsNullOrWhiteSpace([string]$leaf.exactFilter)) {
            [void]$exact.Add($leaf)
            continue
        }
        $parts = Get-NightlyBareTypeParts -ClassName ([string]@($leaf.classes)[0])
        if ($null -eq $parts) { throw 'profile-discovery-namespace-required' }
        $known = @()
        if ($nodesByBare.ContainsKey($parts.Bare)) { $known = @($nodesByBare[$parts.Bare].ToArray()) }
        $cost = Get-NightlyLaunchClassCost -Nodes $known -TypeName $parts.Type
        Add-Member -InputObject $parts -NotePropertyName NodeCount -NotePropertyValue ([int]$cost.NodeCount)
        Add-Member -InputObject $parts -NotePropertyName Weight -NotePropertyValue ([int]$cost.Weight)
        [void]$classItems.Add($parts)
    }

    $byNs = @{}
    $universe = @{}
    foreach ($item in @($classItems)) {
        if (-not $byNs.ContainsKey($item.Namespace)) {
            $byNs[$item.Namespace] = New-Object System.Collections.ArrayList
        }
        [void]$byNs[$item.Namespace].Add($item)
        if (-not $universe.ContainsKey($item.Namespace)) {
            $universe[$item.Namespace] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
        [void]$universe[$item.Namespace].Add([string]$item.Type)
    }
    foreach ($node in @($DiscoveryNodes)) {
        $parts = Get-NightlyBareTypeParts -ClassName ([string]$node.ClassName)
        if ($null -eq $parts) { continue }
        if (-not $universe.ContainsKey($parts.Namespace)) {
            $universe[$parts.Namespace] = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        }
        [void]$universe[$parts.Namespace].Add([string]$parts.Type)
    }

    $unsafe = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($ns in @($universe.Keys)) {
        $names = @($universe[$ns] | Sort-Object { $_.Length }, { $_ })
        for ($i = 0; $i -lt $names.Count; $i++) {
            for ($j = $i + 1; $j -lt $names.Count; $j++) {
                if (Test-NightlyTypeNamePrefixes -Left $names[$i] -Right $names[$j]) {
                    [void]$unsafe.Add($ns + '|' + $names[$i])
                    break
                }
            }
        }
    }

    $n = $classItems.Count
    $selected = $null
    if ($n -gt 0) {
        $desired = 50
        $minSize = 1
        $maxSize = $n
        if ($n -ge 40) {
            $minSize = [Math]::Max(1, [int]([Math]::Floor($n / 60.0)))
            $maxSize = [Math]::Max($minSize, [int]([Math]::Ceiling($n / 40.0)))
        } else {
            $maxSize = [Math]::Max(1, [Math]::Min(30, $n))
            $minSize = $maxSize
            $desired = [Math]::Max(1, [int]([Math]::Ceiling($n / [double]$maxSize)))
        }
        $bestScore = [int]::MaxValue
        $cap = [int]$script:NightlyLaunchGroupNodeCap
        foreach ($size in ($minSize..$maxSize)) {
            $candidate = Split-NightlyClassGroupsAtSize -ByNamespace $byNs -Unsafe $unsafe -MaxSize $size -MaxNodes $cap -MaxWeight $cap
            $count = @($candidate.Groups).Count
            $inRange = ($n -lt 40) -or ($count -ge 40 -and $count -le 60)
            $distance = [Math]::Abs($count - $desired)
            $score = if ($inRange) { $distance } else { 1000 + $distance }
            if ($score -lt $bestScore) {
                $bestScore = $score
                $selected = $candidate.Groups
            }
        }
    }

    $groups = New-Object System.Collections.ArrayList
    $index = 0
    if ($null -ne $selected) {
        foreach ($bin in @($selected)) {
            $items = @($bin)
            if ($items.Count -eq 0) { continue }
            $index++
            $ns = [string]$items[0].Namespace
            $types = @($items | ForEach-Object { [string]$_.Type })
            $bares = @($items | ForEach-Object { [string]$_.Bare } | Sort-Object)
            $sumNodes = 0
            $sumWeight = 0
            foreach ($item in $items) {
                $sumNodes += Get-NightlyPackedItemNodes $item
                $sumWeight += Get-NightlyPackedItemWeight $item
            }
            $filter = ''
            if ($types.Count -ge 2) { $filter = New-NightlyClassGroupFilter -Namespace $ns -TypeNames $types }
            [void]$groups.Add([pscustomobject]@{
                id = ('{0}-group-{1}' -f $sourceId, $index)
                classes = $bares
                exactFilter = ''
                groupFilter = $filter
                nodes = @()
                nodeCount = $sumNodes
                weight = $sumWeight
            })
        }
    }
    foreach ($leaf in @($exact)) { [void]$groups.Add($leaf) }
    return @($groups.ToArray())
}

function Get-NightlyProfileLaunchPlan {
    param([object[]]$DiscoveryNodes, [object[]]$Chunks, [object[]]$Exclusions)
    $leaves = @(Get-NightlyProfileExecutionChunks -DiscoveryNodes $DiscoveryNodes -Chunks $Chunks -Exclusions $Exclusions)
    return @(ConvertTo-NightlyProfileLaunchGroups -Leaves $leaves -DiscoveryNodes $DiscoveryNodes)
}

function Resolve-NightlySuiteChunks {
    # Master nightly is not profile-aware and has no exclusions, so it stays
    # the single all chunk. Only a profile that actually excludes packs leaves.
    param(
        [object[]]$DiscoveryNodes,
        [object[]]$Chunks,
        [object[]]$Exclusions,
        [bool]$ProfileAware,
        [bool]$Native
    )
    if ($Native -and $ProfileAware -and @($Exclusions).Count -gt 0) {
        return @(Get-NightlyProfileLaunchPlan -DiscoveryNodes $DiscoveryNodes -Chunks $Chunks -Exclusions $Exclusions)
    }
    return @($Chunks)
}

function Test-NightlySuiteIsNative {
    <#
      A native suite is one that runs a built TUnit assembly: it owns its process tree,
      serialises against every other native suite, and must produce TRX plus discovery
      and execution diagnostics. Project + assembly is the definition; the legacy id
      list is only a fallback for a suite that predates those fields.
    #>
    param($Suite, [string]$SuiteId)
    if ($null -ne $Suite) {
        $project = [string]$Suite.project
        $assembly = [string]$Suite.assembly
        if (-not [string]::IsNullOrWhiteSpace($project) -and -not [string]::IsNullOrWhiteSpace($assembly)) { return $true }
    }
    return ($script:NightlyNativeGroups -contains $SuiteId)
}

function Test-NightlyE2EPrerequisites {
    <#
      D-6: before any E2E chunk runs, the matching Playwright Chromium must be installed
      through the generated playwright.ps1 and must actually launch headlessly. A missing
      browser is red/incomplete, never a skip. Returns every probe's real outcome.
    #>
    param(
        [string]$RepoRoot,
        [string]$LogRoot,
        [hashtable]$Environment = $null,
        [string]$BundlePath = ''
    )
    if ($script:NightlySeams -and $script:NightlySeams.E2EPrerequisites) {
        return $script:NightlySeams.E2EPrerequisites.Invoke($RepoRoot, $LogRoot, $Environment, $BundlePath)
    }
    $rows = @()
    $ok = $true
    $installer = Join-Path $RepoRoot (Join-Path 'tests' (Join-Path 'Antiphon.E2E' (Join-Path 'bin' (Join-Path 'Debug' (Join-Path 'net9.0' 'playwright.ps1')))))
    if (-not (Test-Path -LiteralPath $installer)) {
        $rows += [ordered]@{ name = 'playwright-script'; exitCode = 1; detail = ('missing {0}' -f $installer) }
        $ok = $false
    } else {
        $install = Invoke-NightlyOwnedProcess -FilePath 'pwsh' `
            -ArgumentList @('-NoProfile', '-NonInteractive', '-File', $installer, 'install', 'chromium') `
            -WorkingDirectory $RepoRoot -TimeoutMilliseconds 900000 -Environment $Environment `
            -LogPath (Join-Path $LogRoot 'e2e-playwright-install.log')
        $rows += [ordered]@{ name = 'playwright-install'; exitCode = [int]$install.ExitCode; detail = '' }
        if ([int]$install.ExitCode -ne 0) { $ok = $false }
    }
    if (-not [string]::IsNullOrWhiteSpace($BundlePath)) {
        $present = Test-Path -LiteralPath $BundlePath
        $rows += [ordered]@{ name = 'client-bundle'; exitCode = $(if ($present) { 0 } else { 1 }); detail = $BundlePath }
        if (-not $present) { $ok = $false }
    }
    return [pscustomobject]@{ Ok = $ok; Rows = $rows }
}

function Get-NightlyWatchdogMs {
    param($PolicyObject, [string]$Key, [int]$Fallback)
    if ($PolicyObject.watchdogs -and $PolicyObject.watchdogs.$Key) {
        return [int]$PolicyObject.watchdogs.$Key
    }
    return $Fallback
}

function Invoke-NightlyOwnedProcess {
    param(
        [string]$FilePath,
        [string[]]$ArgumentList,
        [string]$WorkingDirectory,
        [int]$TimeoutMilliseconds,
        [hashtable]$Environment = $null,
        [string]$LogPath
    )
    Write-NightlyTrace -Kind 'proc' -Message ($FilePath + ' ' + (($ArgumentList) -join ' '))
    if ($script:NightlySeams -and $script:NightlySeams.StartProcess) {
        $r = $script:NightlySeams.StartProcess.Invoke($FilePath, $ArgumentList, $WorkingDirectory, $TimeoutMilliseconds, $Environment)
        if ($LogPath -and $r.LogText) {
            Set-Content -LiteralPath $LogPath -Value ([string]$r.LogText) -Encoding UTF8
        }
        return $r
    }
    # Import only at the actual launch boundary. The controlled-I/O seam above
    # remains portable, and cannot earn native custody credit.
    . (Join-Path $PSScriptRoot 'nightly-owned-process.ps1')
    $owner = Start-NightlyNativeOwner -FilePath $FilePath -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory -TimeoutMilliseconds $TimeoutMilliseconds `
        -Environment $Environment -LogPath $LogPath
    return $owner.Completion.GetAwaiter().GetResult()
}

function Wait-NightlyOwnedCleanup {
    param($RunResult)
    if ($script:NightlySeams -and $script:NightlySeams.WaitCleanup) {
        return [bool]$script:NightlySeams.WaitCleanup.Invoke($RunResult)
    }
    # Retain pre-existing controlled-I/O decision fixtures. Their synthetic
    # ChildrenExited bit cannot establish native custody or qualification.
    if ($script:NightlySeams -and $script:NightlySeams.StartProcess -and $null -eq $RunResult.CleanupComplete) {
        return [bool]$RunResult.ChildrenExited
    }
    return [bool]$RunResult.CleanupComplete
}

function Get-NightlyNativeActiveCount {
    if ($script:NightlySeams -and $script:NightlySeams.NativeActiveCount) {
        return [int]$script:NightlySeams.NativeActiveCount.Invoke()
    }
    return 0
}

function Get-NightlyNativeExecutionArguments {
    param(
        [string]$TrxPath,
        [string]$DiagnosticDirectory,
        [string[]]$Classes,
        [string]$ExactFilter = '',
        [string]$GroupFilter = ''
    )
    if ([string]::IsNullOrWhiteSpace($TrxPath)) { throw 'missing trx path' }
    if ([string]::IsNullOrWhiteSpace($DiagnosticDirectory)) { throw 'missing diagnostic directory' }
    $trxName = [System.IO.Path]::GetFileName($TrxPath)
    $resultsDir = [System.IO.Path]::GetDirectoryName($TrxPath)
    if ([string]::IsNullOrWhiteSpace($trxName)) { throw 'missing trx filename' }
    if ($trxName -match '[\\/]') { throw 'trx filename must be a basename' }
    if ([string]::IsNullOrWhiteSpace($resultsDir)) { throw 'missing trx results directory' }
    $nativeArgs = @(
        '--no-progress',
        '--no-ansi',
        '--report-trx',
        '--report-trx-filename', $trxName,
        '--results-directory', $resultsDir,
        '--diagnostic',
        '--diagnostic-output-directory', $DiagnosticDirectory
    )
    if (-not [string]::IsNullOrWhiteSpace($ExactFilter) -and -not [string]::IsNullOrWhiteSpace($GroupFilter)) {
        throw 'ambiguous-chunk-filter'
    }
    if ($ExactFilter) {
        if ($ExactFilter -notmatch '^/\*/[A-Za-z_][A-Za-z0-9_.]*/[A-Za-z_][A-Za-z0-9_]*/[A-Za-z_][A-Za-z0-9_]*$') { throw 'invalid-exact-filter' }
        $nativeArgs += '--treenode-filter'
        $nativeArgs += $ExactFilter
    } elseif (-not [string]::IsNullOrWhiteSpace($GroupFilter)) {
        # CARD-0403: /*/Namespace/(ClassA*)|(ClassB*)/* . A prefix operand is refused.
        if (-not (Test-NightlyClassGroupFilter -Classes $Classes -GroupFilter $GroupFilter)) { throw 'invalid-group-filter' }
        $nativeArgs += '--treenode-filter'
        $nativeArgs += $GroupFilter
    } elseif ($Classes -and @($Classes).Count -gt 0) {
        if (@($Classes).Count -ne 1) { throw 'multiple classes require split exact roster chunks' }
        $class = Get-NightlyBareClassIdentity -Name ([string]$Classes[0])
        if ($class -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') { throw 'invalid-class-filter' }
        $dot = $class.LastIndexOf('.')
        $namespace = if ($dot -lt 0) { '*' } else { $class.Substring(0, $dot) }
        $type = if ($dot -lt 0) { $class } else { $class.Substring($dot + 1) }
        $nativeArgs += '--treenode-filter'
        $nativeArgs += ('/*/{0}/{1}/*' -f $namespace, $type)
    }
    return $nativeArgs
}

function Resolve-NightlyNpmLauncher {
    param([string]$PathValue = $env:PATH)
    if ([string]::IsNullOrWhiteSpace($PathValue)) { throw 'npm launcher not found' }
    $names = @('npm.cmd', 'npm.exe', 'npm')
    $separator = [string][System.IO.Path]::PathSeparator
    foreach ($dir in ($PathValue -split [regex]::Escape($separator))) {
        if ([string]::IsNullOrWhiteSpace($dir)) { continue }
        foreach ($name in $names) {
            $candidate = Join-Path $dir.Trim() $name
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $full = [System.IO.Path]::GetFullPath($candidate)
                if (-not [System.IO.Path]::IsPathRooted($full)) { throw 'npm launcher is not absolute' }
                return $full
            }
        }
    }
    throw 'npm launcher not found'
}

function Get-NightlyNpmLaunch {
    param([Parameter(Mandatory = $true)][string[]]$NpmArguments)
    # A quoted bare npm.cmd makes cmd.exe set %~dp0 to the client cwd (CARD-1341).
    $launcher = Resolve-NightlyNpmLauncher
    return [pscustomobject]@{
        FilePath = "$env:ComSpec"
        ArgumentList = @('/c', $launcher) + @($NpmArguments)
    }
}

. (Join-Path $PSScriptRoot 'release-selection.ps1')

function Invoke-AntiphonNightlyTests {
    param(
        [string]$RepoRoot = '',
        [string]$LogRoot = '',
        [string[]]$Suites,
        [string]$Sha = '',
        [string]$GitRef = 'origin/master',
        [string]$Trigger = 'scheduled',
        [string]$Profile = 'nightly',
        [string]$ExpectedSha = '',
        [string]$RunId = '',
        [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',
        [string]$ReleaseRoot = '',
        [string]$CandidateId = '',
        [string]$Repository = 'michal-ciechan/Antiphon',
        [string]$BaselineSha = '',
        [string]$BaselineReceiptPath = '',
        [string]$QualificationPath = '',
        [string]$TimingRoot = '',
        [string]$TrainManifestPath = '',
        [string]$ReleaseIntentPath = '',
        [string]$SelectionAttemptPath = '',
        [string]$SelectionAttemptDigest = '',
        [string]$SelectionPath = '',
        [string]$SelectionDigest = '',
        [int]$Attempt = 1,
        [string]$SeamsPath = '',
        [string]$PolicyPath = '',
        [switch]$AllowSharedTree,
        [switch]$WhatIf
    )

    Import-NightlySeams -SeamsPath $SeamsPath
    try { $platform = Get-NightlyExecutionPlatform }
    catch { return [pscustomobject]@{ ExitCode = 3; Refusal = 'unsupported-host-platform'; coverageComplete = $false; testsPassed = $false; LaunchCount = 0 } }
    if ([string]::IsNullOrWhiteSpace($Profile)) { $Profile = 'nightly' }
    $Profile = $Profile.Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
        $RepoRoot = Split-Path -Parent $PSScriptRoot
        if ((Split-Path -Leaf $RepoRoot) -eq 'lib') { $RepoRoot = Split-Path -Parent $RepoRoot }
        $RepoRoot = Split-Path -Parent $RepoRoot
    }
    $RepoRoot = ConvertTo-NightlyCanonicalPath -Path $RepoRoot
    if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = New-NightlyRunId }

    $selection = $null
    try {
        [void](Assert-ReleaseSelectionCapability $SelectionMode)
        if ($SelectionAttemptPath) {
            $selectionRequest = @{RepositoryRoot=$RepoRoot;CandidateSha=$Sha;CandidateRef=$GitRef;Repository=$Repository;Profile=$Profile;
            ReleaseRoot=$ReleaseRoot;CandidateId=$CandidateId;RunId=$RunId;
            SelectionMode=$SelectionMode;BaselineSha=$BaselineSha;BaselineReceiptPath=$BaselineReceiptPath;QualificationPath=$QualificationPath;TimingRoot=$TimingRoot;TrainManifestPath=$TrainManifestPath;ReleaseIntentPath=$ReleaseIntentPath;SelectionAttemptPath=$SelectionAttemptPath;SelectionAttemptDigest=$SelectionAttemptDigest;SelectionPath=$SelectionPath;SelectionDigest=$SelectionDigest;Attempt=$Attempt}
            $selection = Read-ReleaseSelectionAttempt $selectionRequest
            # Independently read HEAD at the receiving implementation, not just argv.
            $head = Invoke-NightlyGit -WorkingDirectory $RepoRoot -Arguments @('rev-parse','HEAD')
            if ($head.ExitCode -ne 0 -or $head.Output.Trim() -cne $Sha) { throw 'selection-hop-head' }
        } elseif ($SelectionMode -cne 'Full') { throw 'selection-attempt-required' }
    } catch { return [pscustomobject]@{ExitCode=3;Refusal=$_.Exception.Message;coverageComplete=$false;testsPassed=$false;LaunchCount=0} }

    $startedAt = Get-NightlyUtcNow
    $script:NightlyTestsOutcome = 'green'
    $overallFailed = $false
    $coverageComplete = $true
    $testsPassed = $true
    $selectedSuites = @()
    $policyHash = ''
    $suiteResults = @()
    $buildResults = @()
    $preflight = [ordered]@{}
    $reasons = @()
    $nativeActiveMax = 0

    if (Test-NightlySharedTree -Path $RepoRoot) {
        if (-not $AllowSharedTree) {
            Write-Host 'REFUSED: nightly-tests.ps1 will not run in the shared tree.'
            Write-Host ('  RepoRoot: {0}' -f $RepoRoot)
            Write-Host '  Isolated clone: C:\Antiphon\nightly\checkout'
            Write-Host '  Re-run with -AllowSharedTree only for a deliberate shared-tree run.'
            return [pscustomobject]@{ ExitCode = 3; Refusal = 'shared-tree'; coverageComplete = $false; testsPassed = $false }
        }
    }

    try {
        $policy = Read-NightlyExecutionPolicy -RepoRoot $RepoRoot -PolicyPath $PolicyPath
    } catch {
        Write-Error $_.Exception.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $_.Exception.Message
        }
    }
    $policyHash = $policy.Hash
    # D-5: the selected profile is authoritative for required suites; defaultSuites stays
    # the compatible nightly answer. An unknown profile, or rc asked of a v1 policy, is a
    # hard refusal rather than a silent fall back to the nightly seven.
    try {
        $profileRow = Get-NightlyPolicyProfile -PolicyObject $policy.Object -Profile $Profile
        $requiredSuites = @(Get-NightlyProfileRequiredSuites -PolicyObject $policy.Object -Profile $Profile)
        # Validate every exclusion before any child command; each suite resolves
        # its own rows below so same-named classes in other suites stay required.
        [void](Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile $Profile -SuiteId '' -Platform $platform)
    } catch {
        Write-Error $_.Exception.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $_.Exception.Message
            Profile = $Profile
        }
    }
    $profileAware = ($Profile -ne 'nightly')
    if (-not [string]::IsNullOrWhiteSpace($ExpectedSha) -and -not [string]::IsNullOrWhiteSpace($Sha) -and
        -not [string]::Equals($Sha, $ExpectedSha, [StringComparison]::OrdinalIgnoreCase)) {
        $msg = ('expected-sha-mismatch sha={0} expected={1}' -f $Sha, $ExpectedSha)
        Write-Error $msg
        return [pscustomobject]@{ ExitCode = 2; coverageComplete = $false; testsPassed = $false; Error = $msg; Profile = $Profile }
    }
    $universe = Test-NightlyPolicyProjectUniverse -RepoRoot $RepoRoot -PolicyObject $policy.Object
    if (-not $universe.Ok) {
        Write-Error $universe.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $universe.Message
            UnmatchedProject = ($universe.Unmatched | Select-Object -First 1)
        }
    }

    try {
        if ($null -eq $Suites -or @($Suites).Count -eq 0) {
            $selectedSuites = @($requiredSuites)
            if ($selection) { $selectedSuites = @(Resolve-ReleaseSelectionMembers $selection $requiredSuites @()) }
        } else {
            $joined = ($Suites | ForEach-Object { [string]$_ }) -join ','
            if ($joined -match '^[\s,]*$') { throw 'empty suite selection' }
            $selectedSuites = @(Resolve-NightlySelectedSuites -PolicyObject $policy.Object -Suites $Suites)
        }
    } catch {
        Write-Error $_.Exception.Message
        return [pscustomobject]@{
            ExitCode = 2
            coverageComplete = $false
            testsPassed = $false
            Error = $_.Exception.Message
            LaunchCount = 0
        }
    }

    $suiteUniverse = Test-NightlyRequiredSuitesPresent -Selected $selectedSuites -Required $requiredSuites
    if (-not $suiteUniverse.Ok) {
        $coverageComplete = $false
        $reasons += ('partial-selection missing {0}' -f ($suiteUniverse.Missing -join ','))
    }

    if ($WhatIf) {
        Write-Host ('WhatIf: would run suites {0} in {1}' -f ($selectedSuites -join ','), $RepoRoot)
        return [pscustomobject]@{ ExitCode = 0; Selected = $selectedSuites }
    }

    if ([string]::IsNullOrWhiteSpace($LogRoot)) {
        $stamp = $startedAt.ToString('yyyy-MM-dd-HHmm')
        $LogRoot = Join-Path 'C:\Antiphon\nightly\logs' $stamp
    }
    $LogRoot = ConvertTo-NightlyCanonicalPath -Path $LogRoot
    New-Item -ItemType Directory -Path $LogRoot -Force | Out-Null
    $summaryPath = Join-Path $LogRoot 'summary.json'
    $invocationId = $RunId

    $chunksBySuite = @{}
    if ($policy.Object.chunks) {
        foreach ($p in $policy.Object.chunks.PSObject.Properties) {
            $chunksBySuite[$p.Name] = @($p.Value)
        }
    }

    function Set-NightlyOutcome([string]$Class) {
        $order = @('PREFLIGHT', 'BUILD', 'TESTS', 'TIMEOUT')
        if ($script:NightlyTestsOutcome -eq 'green') { $script:NightlyTestsOutcome = $Class; return }
        $curIdx = [array]::IndexOf($order, $script:NightlyTestsOutcome)
        $newIdx = [array]::IndexOf($order, $Class)
        if ($newIdx -ge 0 -and ($curIdx -lt 0 -or $newIdx -lt $curIdx)) { $script:NightlyTestsOutcome = $Class }
    }

    $dockerOk = $true
    $diskOk = $true
    $npmOk = $true
    $dotnetOk = $true
    $lintOk = $true
    $lintBlocksPipeline = $false
    $activeNative = 0

    try {
        if ($script:NightlySeams -and $script:NightlySeams.DockerInfo) {
            $preflight.docker = $script:NightlySeams.DockerInfo.Invoke()
        } else {
            $preflight.docker = [ordered]@{ exitCode = 0; version = 'test'; detail = 'live docker skipped under seams-default' }
            if (-not $script:NightlySeams -or -not $script:NightlySeams.StartProcess) {
                $preflight.docker = [ordered]@{ exitCode = 0; version = 'unprobed-default'; detail = 'caller host' }
            }
        }
        if ([int]$preflight.docker.exitCode -ne 0) { $dockerOk = $false; $overallFailed = $true; Set-NightlyOutcome PREFLIGHT; $coverageComplete = $false }

        if ($script:NightlySeams -and $script:NightlySeams.DiskFree) {
            $preflight.disk = $script:NightlySeams.DiskFree.Invoke()
        } else {
            $preflight.disk = [ordered]@{ exitCode = 0; freeBytes = 50GB; minimumFreeBytes = 10GB }
        }
        if ([int]$preflight.disk.exitCode -ne 0) { $diskOk = $false; $overallFailed = $true; Set-NightlyOutcome PREFLIGHT; $coverageComplete = $false }

        if ($dockerOk -and $diskOk) {
            $clientDir = Join-Path $RepoRoot 'client'
            $npmCiLaunch = Get-NightlyNpmLaunch -NpmArguments @('ci')
            $npmCi = Invoke-NightlyOwnedProcess -FilePath $npmCiLaunch.FilePath -ArgumentList $npmCiLaunch.ArgumentList `
                -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'npm-ci.log')
            $buildResults += [ordered]@{ name = 'npm ci'; exitCode = $npmCi.ExitCode; timedOut = $npmCi.TimedOut; result = $(if ($npmCi.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
            if ($npmCi.ExitCode -ne 0) {
                $npmOk = $false
                $overallFailed = $true
                $testsPassed = $false
                Set-NightlyOutcome BUILD
                $coverageComplete = $false
                $reasons += 'prerequisite-failed:npm ci'
            }

            if ($npmOk) {
                $npmBuildLaunch = Get-NightlyNpmLaunch -NpmArguments @('run', 'build')
                $npmBuild = Invoke-NightlyOwnedProcess -FilePath $npmBuildLaunch.FilePath -ArgumentList $npmBuildLaunch.ArgumentList `
                    -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'npm-build.log')
                $buildResults += [ordered]@{ name = 'npm run build'; exitCode = $npmBuild.ExitCode; timedOut = $npmBuild.TimedOut; result = $(if ($npmBuild.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($npmBuild.ExitCode -ne 0) {
                    $npmOk = $false
                    $overallFailed = $true
                    $testsPassed = $false
                    Set-NightlyOutcome BUILD
                    $coverageComplete = $false
                    $reasons += 'prerequisite-failed:npm run build'
                }
                else {
                    $dist = Join-Path $clientDir (Join-Path 'dist' 'index.html')
                    $receipt = Join-Path $LogRoot 'client-build-receipt.json'
                    Write-NightlyAtomicJson -Path $receipt -Object ([ordered]@{ sha = $Sha; runId = $invocationId; dist = $dist; builtAt = (Get-NightlyUtcNow).ToString('o') })
                }
            }

            if ($npmOk) {
                $lintLaunch = Get-NightlyNpmLaunch -NpmArguments @('run', 'lint')
                $lint = Invoke-NightlyOwnedProcess -FilePath $lintLaunch.FilePath -ArgumentList $lintLaunch.ArgumentList `
                    -WorkingDirectory $clientDir -TimeoutMilliseconds 600000 -LogPath (Join-Path $LogRoot 'client-lint.log')
                $buildResults += [ordered]@{ name = 'client lint'; exitCode = $lint.ExitCode; timedOut = $lint.TimedOut; result = $(if ($lint.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($lint.ExitCode -ne 0) {
                    $lintOk = $false
                    $overallFailed = $true
                    # Profile nightly records the lint failure and still runs dotnet
                    # and the suites. Every other profile, including rc, stops here.
                    # Do not mark testsPassed yet on nightly: a later suite failure
                    # must be able to set TESTS, and a green suite stays BUILD.
                    if ($Profile -eq 'nightly') {
                        $reasons += 'client-lint-failed'
                    } else {
                        $lintBlocksPipeline = $true
                        $testsPassed = $false
                        Set-NightlyOutcome BUILD
                        $coverageComplete = $false
                        $reasons += 'prerequisite-failed:client lint'
                    }
                }
            }

            if ($npmOk -and -not $lintBlocksPipeline) {
                $dotnetBuild = Invoke-NightlyOwnedProcess -FilePath 'dotnet' -ArgumentList @('build', 'Antiphon.sln', '-c', 'Debug', '--nologo') `
                    -WorkingDirectory $RepoRoot -TimeoutMilliseconds 1200000 -LogPath (Join-Path $LogRoot 'dotnet-build.log')
                $buildResults += [ordered]@{ name = 'dotnet build Antiphon.sln'; exitCode = $dotnetBuild.ExitCode; timedOut = $dotnetBuild.TimedOut; result = $(if ($dotnetBuild.ExitCode -eq 0) { 'pass' } else { 'FAIL' }) }
                if ($dotnetBuild.ExitCode -ne 0) {
                    $dotnetOk = $false
                    $overallFailed = $true
                    $testsPassed = $false
                    Set-NightlyOutcome BUILD
                    $coverageComplete = $false
                    $reasons += 'prerequisite-failed:dotnet build'
                }
            }

            $suiteMap = Get-NightlySuiteMap -PolicyObject $policy.Object
            foreach ($suiteId in $selectedSuites) {
                if (-not $npmOk -or $lintBlocksPipeline -or -not $dotnetOk) { break }
                $suite = $suiteMap[$suiteId]
                $profileExclusions = @(Get-NightlyProfileExclusions -PolicyObject $policy.Object -Profile $Profile -SuiteId $suiteId -Platform $platform)
                $label = [string]$suite.name
                if ([string]::IsNullOrWhiteSpace($label)) { $label = $suiteId }
                $isNative = Test-NightlySuiteIsNative -Suite $suite -SuiteId $suiteId
                $mode = [string]$suite.mode
                # D-6: 'manual' keeps a suite out of the nightly default lane. A suite the
                # selected profile explicitly requires runs; it is never skipped as manual.
                if ($mode -eq 'manual' -and ($requiredSuites -notcontains $suiteId)) {
                    $suiteResults += [ordered]@{
                        id = $suiteId; name = $label; skipped = $true; result = 'manual'; exitCode = 0
                        coverageComplete = $false; detail = 'manual exclusion'
                    }
                    continue
                }

                $chunks = @()
                if ($chunksBySuite.ContainsKey($suiteId) -and @($chunksBySuite[$suiteId]).Count -gt 0) {
                    $chunks = @($chunksBySuite[$suiteId])
                    $eligible = @()
                    if ($suite.eligibleClasses) { $eligible = @($suite.eligibleClasses) }
                    elseif ($chunks.Count -gt 0) {
                        foreach ($ch in $chunks) { foreach ($c in @($ch.classes)) { $eligible += [string]$c } }
                    }
                    $member = Test-NightlyChunkMembership -EligibleClasses $eligible -Chunks $chunks
                    if (-not $member.Ok) {
                        if ($member.Duplicates.Count -gt 0) {
                            throw ('duplicate-class {0}' -f ($member.Duplicates -join ','))
                        }
                        throw ('missing-class {0}' -f ($member.Missing -join ','))
                    }
                } else {
                    $chunks = @([pscustomobject]@{ id = 'all'; classes = @() })
                }

                # The pinned filter parser requires prefix forms for combined operands.
                # Separate exact class chunks so no suffix class can ever be launched.
                $exactChunks = @()
                foreach ($chunk in $chunks) {
                    if (@($chunk.classes).Count -gt 1) {
                        $classIndex = 0
                        foreach ($class in $chunk.classes) {
                            $exactChunks += [pscustomobject]@{ id = ('{0}-class-{1}' -f $chunk.id, (++$classIndex)); classes = @($class) }
                        }
                    } else { $exactChunks += $chunk }
                }
                $chunks = $exactChunks

                if ($suiteId -eq 'e2e') {
                    $bundle = Join-Path $RepoRoot (Join-Path 'client' (Join-Path 'dist' 'index.html'))
                    $prereqEnv = Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId $suiteId
                    $prereq = Test-NightlyE2EPrerequisites -RepoRoot $RepoRoot -LogRoot $LogRoot -Environment $prereqEnv -BundlePath $bundle
                    $preflight.e2e = @($prereq.Rows)
                    if (-not $prereq.Ok) {
                        # D-6: a missing browser or stale bundle is red/incomplete, not a skip.
                        $coverageComplete = $false
                        $testsPassed = $false
                        $overallFailed = $true
                        Set-NightlyOutcome PREFLIGHT
                        $reasons += 'e2e-prerequisite-failed'
                        $suiteResults += [ordered]@{
                            id = $suiteId; name = $label; skipped = $false; result = 'FAIL'; exitCode = 1
                            coverageComplete = $false; detail = 'e2e-prerequisite-failed'
                        }
                        continue
                    }
                }

                $suiteDiscoveryNodes = @()
                $profileDiscovery = $null
                $suiteCensus = $null
                # CARD-1419: one discovery parse and census per suite, shared by every launch verdict.
                $suiteDiscoveryIndex = New-NightlyDiscoveryIndex
                if ($isNative -and $profileAware -and $profileExclusions.Count -gt 0) {
                    $exe = Join-Path $RepoRoot ('tests\{0}\bin\Debug\net9.0\{0}.exe' -f $suite.assembly)
                    $profileDiscoveryPath = Join-Path $LogRoot ($suiteId + '-profile.discovery.json')
                    $profileDiscovery = Invoke-NightlyProduceDiscovery -ExePath $exe -OutputPath $profileDiscoveryPath `
                        -DiagnosticDirectory (Join-Path $LogRoot ($suiteId + '-profile-discovery')) `
                        -LogPath (Join-Path $LogRoot ($suiteId + '-profile-discovery.log')) -WorkingDirectory $RepoRoot `
                        -Environment (Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId $suiteId)
                    $suiteDiscoveryEntry = Get-NightlyDiscoveryIndexEntry -Index $suiteDiscoveryIndex -Path $profileDiscoveryPath
                    $suiteDiscoveryNodes = @($suiteDiscoveryEntry.Document.Nodes)
                    $suiteCensus = (Get-NightlyDiscoveryIndexView -Entry $suiteDiscoveryEntry -ProfileExclusions $profileExclusions -ProfileAware).Census
                    if (-not $suiteCensus.Ok) { throw 'profile-discovery-census-incomplete' }
                    $chunks = @(Resolve-NightlySuiteChunks -DiscoveryNodes $suiteDiscoveryNodes -Chunks $chunks -Exclusions $profileExclusions -ProfileAware:$profileAware -Native:$isNative)
                    if ($chunks.Count -eq 0) { throw 'zero-required-uid' }
                }
                $chunkIndex = 0
                $suiteTerminalNodes = @()
                foreach ($chunk in $chunks) {
                    $chunkIndex++
                    $logPath = Join-Path $LogRoot ('{0}-{1}-tests.log' -f $suiteId, $chunk.id)
                    $trxPath = Join-Path $LogRoot ('{0}-{1}.trx' -f $suiteId, $chunk.id)
                    $jsonPath = Join-Path $LogRoot ('{0}-{1}.json' -f $suiteId, $chunk.id)
                    $envMap = Get-NightlySafeChildEnvironment -PolicyObject $policy.Object -SuiteId $suiteId
                    $envMap['C487_INVOCATION'] = $invocationId
                    $envMap['C487_RUN_DIR'] = $LogRoot

                    if ($isNative) {
                        if ($activeNative -gt 0) {
                            throw 'native overlap'
                        }
                        $activeNative++
                        $cur = Get-NightlyNativeActiveCount
                        if ($cur -gt $nativeActiveMax) { $nativeActiveMax = $cur }
                        if ($activeNative -gt $nativeActiveMax) { $nativeActiveMax = $activeNative }
                    }

                    $run = $null
                    $execDiagDir = ''
                    if ($suiteId -eq 'client') {
                        $wrapper = Join-Path $RepoRoot (Join-Path 'scripts' 'test-client.ps1')
                        $args = @('-NoProfile', '-NoLogo', '-File', $wrapper, '-JsonResultPath', $jsonPath)
                        $run = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList $args -WorkingDirectory $RepoRoot `
                            -TimeoutMilliseconds 1200000 -Environment $envMap -LogPath $logPath
                    } elseif ($suiteId -eq 'scripts') {
                        $census = @(Get-NightlyScriptCensus -PolicyObject $policy.Object)
                        $scriptFail = $false
                        foreach ($row in $census) {
                            $disp = [string]$row.disposition
                            if ($disp -ne 'unattended') { continue }
                            if ([bool]$row.wildcard) { throw 'wildcard script execution refused' }
                            $scriptPath = Join-Path $RepoRoot (([string]$row.path) -replace '/', '\')
                            $sargs = @('-NoProfile', '-NonInteractive', '-File', $scriptPath)
                            if ($row.arguments) { foreach ($a in @($row.arguments)) { $sargs += [string]$a } }
                            $sr = Invoke-NightlyOwnedProcess -FilePath 'pwsh' -ArgumentList $sargs -WorkingDirectory $RepoRoot `
                                -TimeoutMilliseconds 600000 -Environment $envMap -LogPath (Join-Path $LogRoot ('script-{0}.log' -f $row.id))
                            if ($sr.ExitCode -ne 0) { $scriptFail = $true }
                        }
                        $run = [pscustomobject]@{ ExitCode = $(if ($scriptFail) { 1 } else { 0 }); TimedOut = $false; ChildrenExited = $true }
                    } else {
                        $exeName = [string]$suite.assembly
                        if ([string]::IsNullOrWhiteSpace($exeName)) { $exeName = $label }
                        $exe = Join-Path $RepoRoot ('tests\{0}\bin\Debug\net9.0\{0}.exe' -f $exeName)
                        $execDiagDir = Join-Path $LogRoot ('{0}-{1}-execution' -f $suiteId, $chunk.id)
                        New-Item -ItemType Directory -Path $execDiagDir -Force | Out-Null
                        $chunkClasses = @()
                        if ($chunk.classes -and @($chunk.classes).Count -gt 0) { $chunkClasses = @($chunk.classes) }
                        $groupFilter = ''
                        if ($chunk.PSObject.Properties.Name -contains 'groupFilter') { $groupFilter = [string]$chunk.groupFilter }
                        $args = @(Get-NightlyNativeExecutionArguments -TrxPath $trxPath -DiagnosticDirectory $execDiagDir -Classes $chunkClasses -ExactFilter ([string]$chunk.exactFilter) -GroupFilter $groupFilter)
                        $run = Invoke-NightlyOwnedProcess -FilePath $exe -ArgumentList $args -WorkingDirectory $RepoRoot `
                            -TimeoutMilliseconds (Get-NightlyWatchdogMs -PolicyObject $policy.Object -Key $suiteId -Fallback 3600000) `
                            -Environment $envMap -LogPath $logPath
                    }

                    $cleaned = Wait-NightlyOwnedCleanup -RunResult $run
                    if ($isNative) {
                        $activeNative--
                        if (-not $cleaned) { throw 'owned-child still running' }
                    }

                    if (Test-Path -LiteralPath $logPath) {
                        $stamp = 'C487_INVOCATION=' + $invocationId
                        $rawLog = [System.IO.File]::ReadAllText($logPath)
                        if ($rawLog -notmatch [regex]::Escape($invocationId)) {
                            [System.IO.File]::WriteAllText($logPath, ($stamp + [Environment]::NewLine + $rawLog))
                        }
                    } else {
                        Set-Content -LiteralPath $logPath -Value ('C487_INVOCATION=' + $invocationId) -Encoding UTF8
                    }
                    $fresh = Test-NightlyFreshEvidence -Path $logPath -RunDirectory $LogRoot -NotBeforeUtc $startedAt -InvocationId $invocationId
                    $row = [ordered]@{
                        id = $suiteId
                        chunk = [string]$chunk.id
                        name = $label
                        log = $logPath
                        exitCode = $run.ExitCode
                        timedOut = [bool]$run.TimedOut
                        skipped = $false
                        result = $(if ($run.TimedOut) { 'TIMEOUT' } elseif ($run.ExitCode -ne 0) { 'FAIL' } else { 'pass' })
                    }
                    if (-not $fresh.Ok) {
                        $coverageComplete = $false
                        $testsPassed = $false
                        $overallFailed = $true
                        $row.detail = $fresh.Reason
                        $reasons += $fresh.Reason
                    }
                    if ($isNative) {
                        $discPath = Join-Path $LogRoot ('{0}-{1}.discovery.json' -f $suiteId, $chunk.id)
                        if ($profileDiscovery) {
                            $discPath = $profileDiscoveryPath
                            if ($chunk.exactFilter) {
                                # The full discovery remains the suite union authority;
                                # the leaf verdict checks just this method's expansion.
                                $discPath = Join-Path $LogRoot ('{0}-{1}-selection.discovery.json' -f $suiteId, $chunk.id)
                                $leaf = ConvertTo-NightlyDiscoveryDocument -Nodes $chunk.nodes -AssemblyHash $profileDiscovery.assemblyHash
                                Write-NightlyDiscoveryDocument -Path $discPath -Document $leaf
                            }
                        }
                        if ((Test-Path -LiteralPath $trxPath) -and -not (Test-Path -LiteralPath $discPath)) {
                            try {
                                $diagDir = Join-Path $LogRoot ('{0}-{1}-discovery' -f $suiteId, $chunk.id)
                                $discLog = Join-Path $LogRoot ('{0}-{1}-discovery.log' -f $suiteId, $chunk.id)
                                [void](Invoke-NightlyProduceDiscovery -ExePath $exe -OutputPath $discPath `
                                    -DiagnosticDirectory $diagDir -LogPath $discLog -WorkingDirectory $RepoRoot `
                                    -Environment $envMap)
                            } catch {
                                $reasons += ('discovery-produce-failed {0}' -f $_.Exception.Message)
                            }
                        }
                        $required = @()
                        if ($chunk.classes) { foreach ($c in @($chunk.classes)) { $required += [string]$c } }
                        $execDiagPath = Get-NightlyLatestDiagnosticLog -Directory $execDiagDir
                        $nativeVerdict = ConvertTo-NightlyNativeSuiteVerdict -TrxPath $trxPath -DiscoveryPath $discPath `
                            -ExecutionDiagnosticPath $execDiagPath `
                            -RunDirectory $LogRoot -NotBeforeUtc $startedAt -ProcessExit ([int]$run.ExitCode) `
                            -Sha $Sha -ExpectedSha $Sha -GitRef $GitRef -ExpectedRef $GitRef `
                            -PolicyHash $policyHash -ExpectedPolicyHash $policyHash -RequiredClasses $required `
                            -ProfileExclusions $profileExclusions -ProfileAware:$profileAware -DiscoveryIndex $suiteDiscoveryIndex
                        if ($nativeVerdict.discoveryNodes -and @($nativeVerdict.discoveryNodes).Count -gt 0 -and $suiteDiscoveryNodes.Count -eq 0) {
                            $suiteDiscoveryNodes = @($nativeVerdict.discoveryNodes)
                        }
                        if ($nativeVerdict.terminalNodes) {
                            foreach ($tn in @($nativeVerdict.terminalNodes)) { $suiteTerminalNodes += $tn }
                        }
                        $row.trx = $trxPath
                        $row.evidenceReasons = @($nativeVerdict.reasons)
                        $census = if ($suiteCensus) { $suiteCensus } else { $nativeVerdict.census }
                        if ($census) {
                            # D-5: expanded-UID accounting is manifest evidence; record both
                            # counts and the digest, plus every exclusion with its reason.
                            $row.requiredUidCount = [int]$census.RequiredCount
                            $row.excludedUidCount = [int]$census.ExcludedCount
                            $row.censusDigest = [string]$census.Digest
                            $row.exclusions = @($census.Excluded)
                        }
                        if (-not [bool]$nativeVerdict.coverageComplete) {
                            $coverageComplete = $false
                            $row.coverageComplete = $false
                            foreach ($ev in @($nativeVerdict.reasons)) { $reasons += $ev }
                        }
                        if (-not [bool]$nativeVerdict.testsPassed) {
                            $testsPassed = $false
                            $overallFailed = $true
                            $row.result = 'FAIL'
                        }
                    }
                    if ($suiteId -eq 'client') {
                        if (-not (Test-Path -LiteralPath $jsonPath)) {
                            $coverageComplete = $false
                            $testsPassed = $false
                            $overallFailed = $true
                            $reasons += 'missing-client-json'
                            $row.coverageComplete = $false
                        }
                    }
                    if ($run.TimedOut) {
                        $overallFailed = $true
                        $coverageComplete = $false
                        $testsPassed = $false
                        Set-NightlyOutcome TIMEOUT
                        $reasons += ('timeout {0}' -f $suiteId)
                    } elseif ($run.ExitCode -ne 0) {
                        $overallFailed = $true
                        $testsPassed = $false
                        Set-NightlyOutcome TESTS
                    } elseif (-not $testsPassed -or -not $coverageComplete) {
                        $overallFailed = $true
                        Set-NightlyOutcome TESTS
                    }
                    $suiteResults += $row
                    if ($run.TimedOut) { $coverageComplete = $false }
                }
                if ($isNative -and @($suiteDiscoveryNodes).Count -gt 0) {
                    $unionNodes = @()
                    foreach ($dn in @($suiteDiscoveryNodes)) {
                        if (Test-NightlyDiscoveryExcluded -Node $dn -ProfileExclusions $profileExclusions -ProfileAware:$profileAware) { continue }
                        $unionNodes += $dn
                    }
                    $union = Test-NightlySuiteUidUnion -DiscoveryNodes $unionNodes -ChunkTerminalNodes $suiteTerminalNodes
                    if (-not $union.Ok) {
                        $coverageComplete = $false
                        $overallFailed = $true
                        $reasons += ('missing-expanded-row union {0}' -f ($union.Missing -join ','))
                    }
                }
            }
        } else {
            $coverageComplete = $false
            $testsPassed = $false
        }
    } catch {
        $overallFailed = $true
        $coverageComplete = $false
        $preflight.unhandledError = $_.Exception.Message
        Write-NightlyRunLine $_.Exception.Message
        $reasons += $_.Exception.Message
    }

    if (-not $lintOk) {
        $coverageComplete = $false
        $testsPassed = $false
        if ($Profile -eq 'nightly' -and $script:NightlyTestsOutcome -eq 'green') { Set-NightlyOutcome BUILD }
    }
    if ($nativeActiveMax -gt 1) { $coverageComplete = $false; $reasons += 'native-overlap' }

    $coverageScope = 'Full'
    if ($Profile -eq 'rc' -and $platform -eq 'linux') {
        $coverageScope = 'PlatformDiagnostic'
        $coverageComplete = $false
        $reasons += 'windows-rc-obligations-outstanding'
    }

    $completedAt = Get-NightlyUtcNow
    $summary = [ordered]@{
        startedAt = $startedAt.ToString('o')
        completedAt = $completedAt.ToString('o')
        durationSeconds = [math]::Round(($completedAt - $startedAt).TotalSeconds, 3)
        sha = $Sha
        gitRef = $GitRef
        trigger = $Trigger
        profile = $Profile
        expectedSha = $ExpectedSha
        requiredSuites = $requiredSuites
        runId = $invocationId
        clone = $RepoRoot
        logDir = $LogRoot
        selectedSuites = $selectedSuites
        selection = $selection
        platform = $platform
        coverageScope = $coverageScope
        outcome = $script:NightlyTestsOutcome
        succeeded = (-not $overallFailed -and $coverageComplete)
        coverageComplete = [bool]$coverageComplete
        testsPassed = [bool]$testsPassed
        policyHash = $policyHash
        reasons = $reasons
        nativeActiveMax = $nativeActiveMax
        preflight = $preflight
        builds = $buildResults
        suites = $suiteResults
        invocationId = $invocationId
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-NightlyRunLine ('wrote {0}' -f $summaryPath)

    $exit = 0
    if ($overallFailed) { $exit = 1 }
    return [pscustomobject]@{
        ExitCode = $exit
        coverageComplete = $coverageComplete
        testsPassed = $testsPassed
        platform = $platform
        coverageScope = $coverageScope
        reasons = $reasons
        Profile = $Profile
        RequiredSuites = $requiredSuites
        Selection = $selection
        SummaryPath = $summaryPath
        Selected = $selectedSuites
        NativeActiveMax = $nativeActiveMax
        PolicyHash = $policyHash
    }
}

# Exact selected execution front door. Full-profile publication accounting is unchanged;
# a caller cannot promote this receipt to Full or activate ChangeAware with it.
function Invoke-NightlyExactRoster {
    param([string]$AssemblyPath, $Discovery, [object[]]$Members, [string]$ResultsDirectory,
          [string[]]$ClassifiedUids = $null, [hashtable]$Environment = $null)
    $roster = New-NightlyExactRoster -Discovery $Discovery -Members $Members -ClassifiedUids $ClassifiedUids
    if ($roster.AssemblyHash -cne (Get-NightlyAssemblyHash $AssemblyPath)) { throw 'discovery-build-mismatch' }
    if ($roster.UnresolvedUids.Count -gt 0) { throw 'new-uid-required: classification required before launch' }
    if (Test-Path -LiteralPath $ResultsDirectory) { throw 'execution-output-already-exists' }
    [void][IO.Directory]::CreateDirectory($ResultsDirectory)
    . (Join-Path $PSScriptRoot 'checkpoint-process-runner.ps1')
    $terminal = @(); $receipts = @(); $index = 0
    foreach ($chunk in $roster.Chunks) {
        if ($roster.AssemblyHash -cne (Get-NightlyAssemblyHash $AssemblyPath)) { throw 'discovery-build-mismatch' }
        $dir = Join-Path $ResultsDirectory ('leaf-' + (++$index))
        [void][IO.Directory]::CreateDirectory($dir)
        $trx = Join-Path $dir 'run.trx'
        $args = @(Get-NightlyNativeExecutionArguments -TrxPath $trx -DiagnosticDirectory $dir -ExactFilter $chunk.Filter)
        $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
        $start.WorkingDirectory = $ResultsDirectory
        $start.ArgumentList.Add([IO.Path]::GetFullPath($AssemblyPath))
        foreach ($arg in $args) { $start.ArgumentList.Add($arg) }
        if ($Environment) { foreach ($key in $Environment.Keys) { $start.Environment[$key] = [string]$Environment[$key] } }
        $timingRoot = $env:ANTIPHON_TEST_TIMING_ROOT
        if ($timingRoot) {
            try {
                . (Join-Path $PSScriptRoot 'test-timings.ps1')
                $stamp = [DateTimeOffset]::UtcNow.ToString('o')
                Write-TestTimingEvent -Root $timingRoot -Event ([ordered]@{
                    schemaVersion = 'antiphon-test-timing-v1'; eventId = ('nightly-start-' + $index); eventKind = 'run-started'
                    runId = 'nightly'; attempt = 1; producer = 'nightly'; setKind = 'chunk'; setId = [string]$chunk.Filter
                    outcome = 'started'; startedAt = $stamp; timingPersistence = 'complete'
                })
            } catch { }
        }
        $run = [Antiphon.Checkpoints.CheckpointProcessRunner]::Run($start, 600000, $false)
        if ($timingRoot) {
            try {
                . (Join-Path $PSScriptRoot 'test-timings.ps1')
                $stamp = [DateTimeOffset]::UtcNow.ToString('o')
                Write-TestTimingEvent -Root $timingRoot -Event ([ordered]@{
                    schemaVersion = 'antiphon-test-timing-v1'; eventId = ('nightly-done-' + $index); eventKind = 'set-completed'
                    runId = 'nightly'; attempt = 1; producer = 'nightly'; setKind = 'chunk'; setId = [string]$chunk.Filter
                    outcome = $(if ($run.Exit -eq 0) { 'passed' } else { 'failed' }); exitCode = $run.Exit
                    startedAt = $stamp; endedAt = $stamp; category = 'unavailable'; timingPersistence = 'complete'
                })
            } catch { }
        }
        [IO.File]::WriteAllText((Join-Path $dir 'console.log'), $run.Stdout + $run.Stderr)
        if ($run.TimedOut) { throw 'exact-execution-timeout' }
        $actual = ConvertFrom-NightlyDiagnosticLog (Get-NightlyLatestDiagnosticLog $dir) execution
        $trxRows = @(Read-NightlyTrxIdentities $trx)
        $cross = Test-NightlyTrxDiagnosticCrossCheck $trxRows $actual.Nodes
        $verdict = Test-NightlyExactCoverage $chunk.RequiredUids $actual.Nodes
        $receipts += [ordered]@{ filter = $chunk.Filter; requiredUids = @($chunk.RequiredUids); executedUids = @($actual.Nodes.Uid); exitCode = $run.Exit; trx = $trx; complete = ($cross.Ok -and $verdict.Ok -and $run.Exit -eq 0) }
        $terminal += @($actual.Nodes)
        if (-not $cross.Ok -or -not $verdict.Ok -or $run.Exit -ne 0) {
            Write-NightlyAtomicJson (Join-Path $ResultsDirectory 'receipt.json') @{ format='antiphon-exact-roster-v1'; complete=$false; assemblyHash=$roster.AssemblyHash; chunks=$receipts; reasons=@($verdict.Reasons) }
            throw ('exact-execution-incomplete:' + ($verdict.Reasons -join ','))
        }
    }
    $verdict = Test-NightlyExactCoverage $roster.RequiredUids $terminal
    $result = [ordered]@{ format='antiphon-exact-roster-v1'; complete=$verdict.Ok; executionEvidence=$true; releaseCredit=$false; assemblyHash=$roster.AssemblyHash; requiredUids=@($roster.RequiredUids); executedUids=@($terminal.Uid); chunks=$receipts }
    Write-NightlyAtomicJson (Join-Path $ResultsDirectory 'receipt.json') $result
    return $result
}
