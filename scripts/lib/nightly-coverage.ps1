#requires -Version 5.1
# CARD-0487: TRX, diagnostic-discovery and coverage reconciliation.
# ASCII-only. Discovery is never execution evidence.

if ($script:AntiphonNightlyCoverageLoaded) { return }
$script:AntiphonNightlyCoverageLoaded = $true

$script:NightlyDiscoveryFormat = 'antiphon-tunit-discovery-v1'
$script:NightlyExecutionFormat = 'antiphon-tunit-execution-v1'
$script:NightlySupportedTunit = '1.44.0'
$script:NightlySupportedMtp = '2.2.2'

function Read-NightlyTrxIdentities {
    param([string]$TrxPath)
    if (-not (Test-Path -LiteralPath $TrxPath)) {
        throw ('missing TRX {0}' -f $TrxPath)
    }
    try {
        [xml]$doc = Get-Content -Raw -LiteralPath $TrxPath
    } catch {
        throw ('malformed TRX {0}: {1}' -f $TrxPath, $_.Exception.Message)
    }
    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $nsm = $doc.DocumentElement.NamespaceURI
    if ($nsm) { $ns.AddNamespace('t', $nsm) }
    function Select-Ns([string]$xpath) {
        if ($nsm) { return $doc.SelectNodes($xpath, $ns) }
        return $doc.SelectNodes(($xpath -replace 't:', ''))
    }
    $definitions = @{}
    foreach ($unit in (Select-Ns '//t:TestDefinitions/t:UnitTest')) {
        $id = $unit.GetAttribute('id')
        if (-not $id) { continue }
        $method = $null
        foreach ($child in $unit.ChildNodes) {
            if ($child.LocalName -eq 'TestMethod') { $method = $child; break }
        }
        $className = $null
        $methodName = $null
        if ($method) {
            $className = $method.GetAttribute('className')
            $methodName = $method.GetAttribute('name')
        }
        $definitions[$id] = @{ ClassName = $className; MethodName = $methodName; Display = $unit.GetAttribute('name') }
    }
    $rows = @()
    $results = @(Select-Ns '//t:Results/t:UnitTestResult')
    if ($results.Count -eq 0) { $results = @(Select-Ns '//t:UnitTestResult') }
    $seenIds = @{}
    foreach ($n in $results) {
        $testId = $n.GetAttribute('testId')
        $outcome = $n.GetAttribute('outcome')
        $display = $n.GetAttribute('testName')
        $def = $null
        if ($testId -and $definitions.ContainsKey($testId)) { $def = $definitions[$testId] }
        if (-not $def) {
            throw ('missing definition for testId={0} display={1}' -f $testId, $display)
        }
        if ($seenIds.ContainsKey($testId)) {
            throw ('duplicate identity testId={0}' -f $testId)
        }
        $seenIds[$testId] = $true
        $className = [string]$def.ClassName
        $methodName = [string]$def.MethodName
        if ([string]::IsNullOrWhiteSpace($className) -or [string]::IsNullOrWhiteSpace($methodName)) {
            throw ('unresolved identity testId={0} display={1}' -f $testId, $display)
        }
        $rows += [pscustomobject]@{
            TestId = $testId
            Uid = ''
            ClassName = $className
            MethodName = $methodName
            Display = $display
            Outcome = $outcome
            Identity = ('{0}.{1}' -f $className, $methodName)
        }
    }
    return $rows
}

function ConvertFrom-NightlyDiscoveryDocument {
    param($Object, [string]$ExpectedFormat)
    if ($null -eq $Object) { throw 'empty discovery document' }
    $format = [string]$Object.format
    if ($format -ne $ExpectedFormat) {
        throw ('unknown discovery format {0}' -f $format)
    }
    $tunit = [string]$Object.tunitVersion
    $mtp = [string]$Object.mtpVersion
    if ($tunit -ne $script:NightlySupportedTunit -or $mtp -ne $script:NightlySupportedMtp) {
        throw ('version-drift tunit={0} mtp={1}' -f $tunit, $mtp)
    }
    if ([string]::IsNullOrWhiteSpace([string]$Object.assemblyHash)) {
        throw 'missing assemblyHash'
    }
    $nodes = @()
    $uids = @{}
    foreach ($n in @($Object.nodes)) {
        $uid = [string]$n.uid
        if ([string]::IsNullOrWhiteSpace($uid)) { throw 'truncated discovery record: missing uid' }
        if ($uids.ContainsKey($uid)) { throw ('duplicate discovery uid {0}' -f $uid) }
        $uids[$uid] = $true
        $typeName = [string]$n.type
        $method = [string]$n.method
        if ([string]::IsNullOrWhiteSpace($typeName) -or [string]::IsNullOrWhiteSpace($method)) {
            throw ('truncated discovery record uid={0}' -f $uid)
        }
        $categories = @()
        if ($n.categories) { foreach ($c in @($n.categories)) { $categories += [string]$c } }
        $excluded = $false
        if ($n.PSObject.Properties.Name -contains 'excluded') { $excluded = [bool]$n.excluded }
        foreach ($c in $categories) {
            if ($c -eq 'OptIn' -or $c -eq 'Explicit') { $excluded = $true }
        }
        $nodes += [pscustomobject]@{
            Uid = $uid
            Assembly = [string]$n.assembly
            Namespace = [string]$n.namespace
            Type = $typeName
            Method = $method
            Signature = [string]$n.signature
            State = [string]$n.state
            ClassName = $(if ([string]::IsNullOrWhiteSpace([string]$n.namespace)) { $typeName } else { '{0}.{1}' -f $n.namespace, $typeName })
            Excluded = $excluded
            Categories = $categories
        }
    }
    return [pscustomobject]@{
        Format = $format
        TunitVersion = $tunit
        MtpVersion = $mtp
        AssemblyHash = [string]$Object.assemblyHash
        Nodes = $nodes
    }
}

function Read-NightlyDiscoveryDocument {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw ('missing discovery {0}' -f $Path) }
    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { throw ('empty discovery {0}' -f $Path) }
    $obj = $raw | ConvertFrom-Json
    return (ConvertFrom-NightlyDiscoveryDocument -Object $obj -ExpectedFormat $script:NightlyDiscoveryFormat)
}

function Read-NightlyExecutionDocument {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw ('missing execution inventory {0}' -f $Path) }
    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { throw ('empty execution inventory {0}' -f $Path) }
    $obj = $raw | ConvertFrom-Json
    $format = [string]$obj.format
    if ($format -ne $script:NightlyExecutionFormat) {
        throw ('unknown execution format {0}' -f $format)
    }
    $tunit = [string]$obj.tunitVersion
    $mtp = [string]$obj.mtpVersion
    if ($tunit -ne $script:NightlySupportedTunit -or $mtp -ne $script:NightlySupportedMtp) {
        throw ('version-drift tunit={0} mtp={1}' -f $tunit, $mtp)
    }
    $nodes = @()
    $uids = @{}
    foreach ($n in @($obj.nodes)) {
        $uid = [string]$n.uid
        $state = [string]$n.state
        if ([string]::IsNullOrWhiteSpace($uid)) { throw 'truncated execution record' }
        if ($uids.ContainsKey($uid)) { throw ('duplicate execution uid {0}' -f $uid) }
        $uids[$uid] = $true
        $nodes += [pscustomobject]@{ Uid = $uid; State = $state }
    }
    return [pscustomobject]@{
        Format = $format
        AssemblyHash = [string]$obj.assemblyHash
        Nodes = $nodes
    }
}

function Test-NightlyPinnedDiagnosticVersions {
    param([string]$Text)
    $tunitOk = ($Text -match "Version:\s+'1\.44\.0") -or ($Text -match 'TUnit v1\.44\.0')
    $mtpOk = ($Text -match 'Version:\s+2\.2\.2')
    if (-not $tunitOk -or -not $mtpOk) {
        throw ('version-drift tunit/mtp header missing or unsupported')
    }
}

function Get-NightlyDiagnosticStateName {
    param([string]$Record)
    if ($Record -match 'PassedTestNodeStateProperty') { return 'Passed' }
    if ($Record -match 'FailedTestNodeStateProperty') { return 'Failed' }
    if ($Record -match 'SkippedTestNodeStateProperty') { return 'Skipped' }
    if ($Record -match 'DiscoveredTestNodeStateProperty') { return 'Discovered' }
    if ($Record -match 'InProgressTestNodeStateProperty') { return 'InProgress' }
    return ''
}

function ConvertFrom-NightlyDiagnosticRecord {
    param([string]$Record, [string]$State)
    if ($Record -notmatch 'TestNodeUid \{ Value = (?<uid>[^}]+?) \}') {
        throw 'truncated diagnostic record: missing uid'
    }
    $uid = $Matches['uid'].Trim()
    $typeName = ''
    $method = ''
    $ns = ''
    $assembly = ''
    if ($Record -match '(?<![A-Za-z])TypeName = (?<type>[A-Za-z0-9_\.]+)') { $typeName = $Matches['type'] }
    if ($Record -match '(?<![A-Za-z])MethodName = (?<method>[A-Za-z0-9_]+)') { $method = $Matches['method'] }
    if ($Record -match '(?<![A-Za-z])Namespace = (?<ns>[A-Za-z0-9_\.]+)') { $ns = $Matches['ns'] }
    if ($Record -match 'AssemblyFullName = (?<asm>[^,]+)') { $assembly = $Matches['asm'].Trim() }
    if ([string]::IsNullOrWhiteSpace($uid)) { throw 'truncated diagnostic record: missing uid' }
    if ([string]::IsNullOrWhiteSpace($typeName) -or [string]::IsNullOrWhiteSpace($method)) {
        throw ('truncated diagnostic record uid={0}' -f $uid)
    }
    $categories = @()
    foreach ($m in [regex]::Matches($Record, 'TestMetadataProperty \{ Key = (?<key>[A-Za-z0-9_]+)')) {
        $categories += $m.Groups['key'].Value
    }
    $excluded = $false
    foreach ($c in $categories) {
        if ($c -eq 'OptIn' -or $c -eq 'Explicit') { $excluded = $true }
    }
    $className = $(if ([string]::IsNullOrWhiteSpace($ns)) { $typeName } else { '{0}.{1}' -f $ns, $typeName })
    return [pscustomobject]@{
        Uid = $uid
        Assembly = $assembly
        Namespace = $ns
        Type = $typeName
        Method = $method
        Signature = ''
        State = $State
        Outcome = $State
        ClassName = $className
        Excluded = $excluded
        Categories = $categories
    }
}

function ConvertFrom-NightlyExecutionJson {
    param($Object)
    $format = [string]$Object.format
    if ($format -ne $script:NightlyExecutionFormat) {
        throw ('unknown execution format {0}' -f $format)
    }
    $tunit = [string]$Object.tunitVersion
    $mtp = [string]$Object.mtpVersion
    if ($tunit -ne $script:NightlySupportedTunit -or $mtp -ne $script:NightlySupportedMtp) {
        throw ('version-drift tunit={0} mtp={1}' -f $tunit, $mtp)
    }
    $nodes = @()
    $uids = @{}
    foreach ($n in @($Object.nodes)) {
        $uid = [string]$n.uid
        $state = [string]$n.state
        if ([string]::IsNullOrWhiteSpace($uid)) { throw 'truncated execution record' }
        $norm = ConvertTo-NightlyNormalizedOutcome -Outcome $state
        if ($state -eq 'InProgress' -or $state -eq 'Discovered' -or $norm -eq 'NotExecuted') { continue }
        if ($uids.ContainsKey($uid)) { throw ('duplicate execution uid {0}' -f $uid) }
        $uids[$uid] = $true
        $typeName = [string]$n.type
        $method = [string]$n.method
        $ns = [string]$n.namespace
        $className = [string]$n.className
        if ([string]::IsNullOrWhiteSpace($className)) {
            $className = $(if ([string]::IsNullOrWhiteSpace($ns)) { $typeName } else { '{0}.{1}' -f $ns, $typeName })
        }
        $nodes += [pscustomobject]@{
            Uid = $uid
            State = $state
            Outcome = $state
            Type = $typeName
            Method = $method
            Namespace = $ns
            ClassName = $className
            Excluded = $false
            Categories = @()
        }
    }
    return [pscustomobject]@{ Nodes = $nodes }
}

function ConvertFrom-NightlyDiagnosticLog {
    param([string]$Path, [string]$Kind)
    # Strict adapter for pinned TUnit 1.44 / MTP 2.2 diagnostic ToString() records.
    # Accepts golden JSON first; otherwise TestNodeUid { Value = ... } plus state properties.
    if (-not (Test-Path -LiteralPath $Path)) { throw ('missing diagnostic {0}' -f $Path) }
    $text = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($text)) { throw ('empty diagnostic {0}' -f $Path) }
    $trimmed = $text.Trim()
    if ($trimmed.StartsWith('{')) {
        $obj = $text | ConvertFrom-Json
        if ($Kind -eq 'discovery') {
            return (ConvertFrom-NightlyDiscoveryDocument -Object $obj -ExpectedFormat $script:NightlyDiscoveryFormat)
        }
        if ($Kind -eq 'execution') {
            return (ConvertFrom-NightlyExecutionJson -Object $obj)
        }
        return $obj
    }
    Test-NightlyPinnedDiagnosticVersions -Text $text
    $recognized = $false
    $nodes = @()
    $seen = @{}
    foreach ($line in ($text -split "`r?`n")) {
        if ($line -notmatch 'TestNode \{ Uid = TestNodeUid') { continue }
        $state = Get-NightlyDiagnosticStateName -Record $line
        if ([string]::IsNullOrWhiteSpace($state)) { continue }
        $recognized = $true
        if ($Kind -eq 'discovery') {
            if ($state -ne 'Discovered') { continue }
        } elseif ($Kind -eq 'execution') {
            if ($state -eq 'InProgress' -or $state -eq 'Discovered') { continue }
        }
        $node = ConvertFrom-NightlyDiagnosticRecord -Record $line -State $state
        if ($seen.ContainsKey($node.Uid)) {
            throw ('duplicate {0} uid {1}' -f $Kind, $node.Uid)
        }
        $seen[$node.Uid] = $true
        $nodes += $node
    }
    if (-not $recognized) {
        throw ('unknown diagnostic format {0}' -f $Path)
    }
    return [pscustomobject]@{ Nodes = $nodes }
}

function ConvertTo-NightlyNormalizedOutcome {
    param([string]$Outcome)
    $raw = ([string]$Outcome).Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($raw)) { return 'Unknown' }
    switch ($raw) {
        'passed' { return 'Passed' }
        'pass' { return 'Passed' }
        'failed' { return 'Failed' }
        'fail' { return 'Failed' }
        'error' { return 'Failed' }
        'skipped' { return 'Skipped' }
        'notexecuted' { return 'NotExecuted' }
        'notrunnable' { return 'NotExecuted' }
        'pending' { return 'NotExecuted' }
        default { return 'Unknown' }
    }
}

function ConvertTo-NightlyDiscoveryDocument {
    param(
        [object[]]$Nodes,
        [string]$AssemblyHash
    )
    if ([string]::IsNullOrWhiteSpace($AssemblyHash)) { throw 'missing assemblyHash' }
    $mapped = @()
    foreach ($n in @($Nodes)) {
        $uid = [string]$n.uid
        $typeName = [string]$n.type
        $method = [string]$n.method
        $ns = [string]$n.namespace
        $assembly = [string]$n.assembly
        $signature = [string]$n.signature
        $state = [string]$n.state
        if ([string]::IsNullOrWhiteSpace($state)) { $state = 'Discovered' }
        $categories = @()
        if ($n.categories) { foreach ($c in @($n.categories)) { $categories += [string]$c } }
        $excluded = $false
        if ($n.PSObject.Properties.Name -contains 'excluded') { $excluded = [bool]$n.excluded }
        $mapped += [ordered]@{
            uid = $uid
            assembly = $assembly
            namespace = $ns
            type = $typeName
            method = $method
            signature = $signature
            state = $state
            excluded = $excluded
            categories = $categories
        }
    }
    return [ordered]@{
        format = $script:NightlyDiscoveryFormat
        tunitVersion = $script:NightlySupportedTunit
        mtpVersion = $script:NightlySupportedMtp
        assemblyHash = $AssemblyHash
        nodes = $mapped
    }
}

function Write-NightlyDiscoveryDocument {
    param(
        [string]$Path,
        $Document
    )
    Write-NightlyAtomicJson -Path $Path -Object $Document
}

function Get-NightlyLatestDiagnosticLog {
    param([string]$Directory)
    if ([string]::IsNullOrWhiteSpace($Directory) -or -not (Test-Path -LiteralPath $Directory)) {
        return $null
    }
    $files = @(Get-ChildItem -LiteralPath $Directory -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
    if ($files.Count -eq 0) { return $null }
    $diag = @($files | Where-Object { [string]$_.Extension -eq '.diag' })
    if ($diag.Count -gt 0) { return $diag[0].FullName }
    return $files[0].FullName
}

function Get-NightlyAssemblyHash {
    param([string]$ExePath)
    $dll = [System.IO.Path]::ChangeExtension($ExePath, '.dll')
    if (Test-Path -LiteralPath $dll) { return (Get-NightlyFileSha256 -Path $dll) }
    if (Test-Path -LiteralPath $ExePath) { return (Get-NightlyFileSha256 -Path $ExePath) }
    return (Get-NightlySha256Text -Text ([string]$ExePath))
}

function Invoke-NightlyProduceDiscovery {
    param(
        [string]$ExePath,
        [string]$OutputPath,
        [string]$DiagnosticDirectory,
        [string]$LogPath,
        [string]$WorkingDirectory,
        [hashtable]$Environment = $null,
        [int]$TimeoutMilliseconds = 600000
    )
    if ([string]::IsNullOrWhiteSpace($ExePath)) { throw 'missing assembly exe' }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw 'missing discovery output' }
    if ([string]::IsNullOrWhiteSpace($DiagnosticDirectory)) { throw 'missing diagnostic directory' }
    New-Item -ItemType Directory -Path $DiagnosticDirectory -Force | Out-Null
    $args = @(
        '--list-tests',
        '--no-progress',
        '--no-ansi',
        '--diagnostic',
        '--diagnostic-output-directory',
        $DiagnosticDirectory
    )
    $run = Invoke-NightlyOwnedProcess -FilePath $ExePath -ArgumentList $args `
        -WorkingDirectory $WorkingDirectory -TimeoutMilliseconds $TimeoutMilliseconds `
        -Environment $Environment -LogPath $LogPath
    if ([bool]$run.TimedOut) { throw 'discovery list-tests timed out' }
    $diagPath = Get-NightlyLatestDiagnosticLog -Directory $DiagnosticDirectory
    if ([string]::IsNullOrWhiteSpace($diagPath)) {
        throw ('missing diagnostic discovery log in {0}' -f $DiagnosticDirectory)
    }
    $parsed = ConvertFrom-NightlyDiagnosticLog -Path $diagPath -Kind discovery
    $nodes = @($parsed.Nodes)
    if ($nodes.Count -eq 0) { throw 'empty discovery' }
    $hash = Get-NightlyAssemblyHash -ExePath $ExePath
    $doc = ConvertTo-NightlyDiscoveryDocument -Nodes $nodes -AssemblyHash $hash
    Write-NightlyDiscoveryDocument -Path $OutputPath -Document $doc
    return $doc
}

function Test-NightlyFreshEvidence {
    param(
        [string]$Path,
        [string]$RunDirectory,
        [datetime]$NotBeforeUtc,
        [string]$InvocationId
    )
    if (-not (Test-Path -LiteralPath $Path)) {
        return [pscustomobject]@{ Ok = $false; Reason = ('missing {0}' -f $Path) }
    }
    $full = ConvertTo-NightlyCanonicalPath -Path $Path
    $root = ConvertTo-NightlyCanonicalPath -Path $RunDirectory
    if (-not (Test-NightlyPathContained -Inner $full -Outer $root)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'wrong-run-directory' }
    }
    $item = Get-Item -LiteralPath $Path
    if ($item.LastWriteTimeUtc -lt $NotBeforeUtc.AddSeconds(-1)) {
        return [pscustomobject]@{ Ok = $false; Reason = 'stale-timestamp' }
    }
    if (-not [string]::IsNullOrWhiteSpace($InvocationId)) {
        $raw = [System.IO.File]::ReadAllText($Path)
        if ($raw -notmatch [regex]::Escape($InvocationId)) {
            return [pscustomobject]@{ Ok = $false; Reason = 'previous-invocation' }
        }
    }
    return [pscustomobject]@{ Ok = $true; Reason = '' }
}

function Get-NightlyClassNameFromIdentity {
    param([string]$ClassName)
    if ([string]::IsNullOrWhiteSpace($ClassName)) { return $ClassName }
    $parts = $ClassName.Split('.')
    return $parts[$parts.Length - 1]
}

function Test-NightlyClassAssigned {
    param([string]$ClassName, [string]$TypeName, [string[]]$RequiredClasses)
    if ($null -eq $RequiredClasses -or @($RequiredClasses).Count -eq 0) { return $true }
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ClassName)) {
        $candidates += $ClassName
        $candidates += (Get-NightlyClassNameFromIdentity -ClassName $ClassName)
    }
    if (-not [string]::IsNullOrWhiteSpace($TypeName)) { $candidates += $TypeName }
    foreach ($c in @($RequiredClasses)) {
        $want = [string]$c
        if ([string]::IsNullOrWhiteSpace($want)) { continue }
        foreach ($have in $candidates) {
            if ([string]::Equals($want, $have, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

function Test-NightlyDiscoveryExcluded {
    param($Node)
    if ($null -eq $Node) { return $false }
    if ([bool]$Node.Excluded) { return $true }
    foreach ($c in @($Node.Categories)) {
        if ([string]$c -eq 'OptIn' -or [string]$c -eq 'Explicit') { return $true }
    }
    return $false
}

function Get-NightlyRequiredDiscoveryUids {
    param([object[]]$DiscoveryNodes, [string[]]$RequiredClasses = @())
    $uids = @()
    $seen = @{}
    foreach ($n in @($DiscoveryNodes)) {
        if (Test-NightlyDiscoveryExcluded -Node $n) { continue }
        if (-not (Test-NightlyClassAssigned -ClassName ([string]$n.ClassName) -TypeName ([string]$n.Type) -RequiredClasses $RequiredClasses)) {
            continue
        }
        $uid = [string]$n.Uid
        if ([string]::IsNullOrWhiteSpace($uid)) { continue }
        if ($seen.ContainsKey($uid)) { continue }
        $seen[$uid] = $true
        $uids += $uid
    }
    return $uids
}

function Get-NightlyIdentityBagKey {
    param([string]$ClassName, [string]$TypeName, [string]$MethodName)
    $simple = Get-NightlyClassNameFromIdentity -ClassName $(if ([string]::IsNullOrWhiteSpace($ClassName)) { $TypeName } else { $ClassName })
    return (('{0}.{1}' -f $simple, $MethodName).ToLowerInvariant())
}

function Test-NightlyChunkMembership {
    param([string[]]$EligibleClasses, $Chunks)
    $assigned = @{}
    $dup = @()
    $missing = @()
    foreach ($chunk in @($Chunks)) {
        foreach ($c in @($chunk.classes)) {
            $name = [string]$c
            if ($assigned.ContainsKey($name)) { $dup += $name }
            else { $assigned[$name] = [string]$chunk.id }
        }
    }
    foreach ($c in @($EligibleClasses)) {
        if (-not $assigned.ContainsKey($c)) { $missing += $c }
    }
    return [pscustomobject]@{
        Ok = ($dup.Count -eq 0 -and $missing.Count -eq 0)
        Duplicates = $dup
        Missing = $missing
    }
}

function ConvertTo-NightlyCoverageVerdict {
    param(
        [object[]]$TerminalRows,
        [int]$ProcessExit,
        [string]$Sha,
        [string]$ExpectedSha,
        [string]$GitRef,
        [string]$ExpectedRef,
        [string]$PolicyHash,
        [string]$ExpectedPolicyHash,
        [string[]]$RequiredSkips = @(),
        [object[]]$DiscoveryNodes = @(),
        [bool]$UsedDiscoveryAsExecution = $false
    )
    $coverageComplete = $true
    $testsPassed = $true
    $reasons = @()
    if ($UsedDiscoveryAsExecution) {
        $coverageComplete = $false
        $reasons += 'discovery-as-execution'
    }
    $inProgress = @($DiscoveryNodes | Where-Object { $_.State -eq 'InProgress' })
    if ($inProgress.Count -gt 0 -and ($null -eq $TerminalRows -or $TerminalRows.Count -eq 0)) {
        $coverageComplete = $false
        $reasons += 'in-progress-only'
    }
    if ($null -eq $TerminalRows -or $TerminalRows.Count -eq 0) {
        $coverageComplete = $false
        $testsPassed = $false
        $reasons += 'zero-terminal-rows'
    }
    $failed = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Failed' })
    $skipped = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Skipped' })
    $unexecuted = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'NotExecuted' })
    $unknown = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Unknown' })
    if ($failed.Count -gt 0) {
        $testsPassed = $false
        $reasons += 'failed-row'
    }
    if ($unexecuted.Count -gt 0) {
        $coverageComplete = $false
        $reasons += ('unexecuted-outcome:{0}' -f (($unexecuted | ForEach-Object { $_.Identity }) -join ','))
    }
    if ($unknown.Count -gt 0) {
        $coverageComplete = $false
        $testsPassed = $false
        $reasons += ('unknown-outcome:{0}' -f (($unknown | ForEach-Object { $_.Identity }) -join ','))
    }
    if ($ProcessExit -ne 0) {
        $testsPassed = $false
        $reasons += ('process-exit-{0}' -f $ProcessExit)
    }
    if ($skipped.Count -gt 0) {
        $requiredHit = @($skipped | Where-Object {
            $id = [string]$_.Identity
            $RequiredSkips -contains $id -or $id -match 'Slow' -or $id -match 'broker'
        })
        if ($requiredHit.Count -gt 0 -or $skipped.Count -gt 0) {
            $coverageComplete = $false
            $reasons += ('skipped:{0}' -f (($skipped | ForEach-Object { $_.Identity }) -join ','))
        }
    }
    if (-not [string]::Equals([string]$Sha, [string]$ExpectedSha, [StringComparison]::OrdinalIgnoreCase)) {
        $coverageComplete = $false
        $reasons += ('sha-mismatch {0} vs {1}' -f $Sha, $ExpectedSha)
    }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRef) -and
        -not [string]::Equals([string]$GitRef, [string]$ExpectedRef, [StringComparison]::OrdinalIgnoreCase)) {
        $coverageComplete = $false
        $reasons += ('ref-mismatch {0} vs {1}' -f $GitRef, $ExpectedRef)
    }
    if (-not [string]::Equals([string]$PolicyHash, [string]$ExpectedPolicyHash, [StringComparison]::OrdinalIgnoreCase)) {
        $coverageComplete = $false
        $reasons += ('policy-mismatch {0} vs {1}' -f $PolicyHash, $ExpectedPolicyHash)
    }
    $passedCount = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Passed' }).Count
    return [pscustomobject]@{
        coverageComplete = [bool]$coverageComplete
        testsPassed = [bool]$testsPassed
        reasons = $reasons
        passed = $passedCount
        failed = $failed.Count
        skipped = $skipped.Count
        total = @($TerminalRows).Count
        skippedIdentities = @($skipped | ForEach-Object { $_.Identity })
        failedIdentities = @($failed | ForEach-Object { $_.Identity })
    }
}

function Test-NightlyCounterAgreement {
    param($Verdict, [int]$ReportedTotal, [int]$ReportedFailed)
    if ($ReportedTotal -ne $Verdict.total) { return $false }
    if ($ReportedFailed -ne $Verdict.failed) { return $false }
    return $true
}

function Test-NightlyExpandedRowsPresent {
    param([object[]]$DiscoveryNodes, [object[]]$TerminalRows, [string[]]$RequiredUids)
    $terminalUids = @{}
    $executed = @('Passed', 'Failed', 'Skipped')
    foreach ($r in @($TerminalRows)) {
        $outcome = ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$r.Outcome)
        if ([string]::IsNullOrWhiteSpace($outcome) -and $r.State) {
            $outcome = ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$r.State)
        }
        if ($executed -notcontains $outcome) { continue }
        $uid = [string]$r.Uid
        if (-not [string]::IsNullOrWhiteSpace($uid)) { $terminalUids[$uid] = $true }
    }
    $missing = @()
    foreach ($uid in @($RequiredUids)) {
        $key = [string]$uid
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        if (-not $terminalUids.ContainsKey($key)) { $missing += $key }
    }
    return [pscustomobject]@{ Ok = ($missing.Count -eq 0); Missing = $missing }
}

function Test-NightlyTrxDiagnosticCrossCheck {
    param([object[]]$TrxRows, [object[]]$TerminalNodes, [string[]]$RequiredClasses = @())
    $trxBag = @{}
    $diagBag = @{}
    $extra = @()
    foreach ($r in @($TrxRows)) {
        $assigned = Test-NightlyClassAssigned -ClassName ([string]$r.ClassName) -TypeName '' -RequiredClasses $RequiredClasses
        if (-not $assigned) {
            if ($RequiredClasses -and @($RequiredClasses).Count -gt 0) {
                $extra += ('{0}.{1}' -f $r.ClassName, $r.MethodName)
            }
            continue
        }
        $key = Get-NightlyIdentityBagKey -ClassName ([string]$r.ClassName) -TypeName '' -MethodName ([string]$r.MethodName)
        if (-not $trxBag.ContainsKey($key)) { $trxBag[$key] = 0 }
        $trxBag[$key]++
    }
    foreach ($n in @($TerminalNodes)) {
        if (-not (Test-NightlyClassAssigned -ClassName ([string]$n.ClassName) -TypeName ([string]$n.Type) -RequiredClasses $RequiredClasses)) {
            continue
        }
        $method = [string]$n.Method
        if ([string]::IsNullOrWhiteSpace($method)) { $method = [string]$n.MethodName }
        $key = Get-NightlyIdentityBagKey -ClassName ([string]$n.ClassName) -TypeName ([string]$n.Type) -MethodName $method
        if (-not $diagBag.ContainsKey($key)) { $diagBag[$key] = 0 }
        $diagBag[$key]++
    }
    $mismatch = @()
    $allKeys = @{}
    foreach ($k in @($trxBag.Keys)) { $allKeys[$k] = $true }
    foreach ($k in @($diagBag.Keys)) { $allKeys[$k] = $true }
    foreach ($k in @($allKeys.Keys)) {
        $t = 0; $d = 0
        if ($trxBag.ContainsKey($k)) { $t = [int]$trxBag[$k] }
        if ($diagBag.ContainsKey($k)) { $d = [int]$diagBag[$k] }
        if ($t -ne $d) { $mismatch += ('{0} trx={1} diag={2}' -f $k, $t, $d) }
    }
    return [pscustomobject]@{
        Ok = ($mismatch.Count -eq 0 -and $extra.Count -eq 0)
        Mismatch = $mismatch
        ExtraClasses = $extra
    }
}

function Test-NightlySuiteUidUnion {
    param([object[]]$DiscoveryNodes, [object[]]$ChunkTerminalNodes)
    $required = @(Get-NightlyRequiredDiscoveryUids -DiscoveryNodes $DiscoveryNodes -RequiredClasses @())
    return (Test-NightlyExpandedRowsPresent -DiscoveryNodes $DiscoveryNodes -TerminalRows $ChunkTerminalNodes -RequiredUids $required)
}

function Test-NightlyRequiredClassesPresent {
    param([object[]]$TerminalRows, [string[]]$RequiredClasses)
    if ($null -eq $RequiredClasses -or @($RequiredClasses).Count -eq 0) {
        return [pscustomobject]@{ Ok = $true; Missing = @() }
    }
    $present = @{}
    foreach ($r in @($TerminalRows)) {
        $cn = [string]$r.ClassName
        if ([string]::IsNullOrWhiteSpace($cn)) { continue }
        $present[$cn] = $true
        $present[(Get-NightlyClassNameFromIdentity -ClassName $cn)] = $true
    }
    $missing = @()
    foreach ($c in @($RequiredClasses)) {
        $name = [string]$c
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        if (-not $present.ContainsKey($name)) { $missing += $name }
    }
    return [pscustomobject]@{ Ok = ($missing.Count -eq 0); Missing = $missing }
}

function ConvertTo-NightlyNativeSuiteVerdict {
    param(
        [string]$TrxPath,
        [string]$DiscoveryPath = '',
        [string]$ExecutionDiagnosticPath = '',
        [string]$RunDirectory,
        [datetime]$NotBeforeUtc,
        [int]$ProcessExit,
        [string]$Sha,
        [string]$ExpectedSha,
        [string]$GitRef,
        [string]$ExpectedRef,
        [string]$PolicyHash,
        [string]$ExpectedPolicyHash,
        [string[]]$RequiredClasses = @()
    )
    $reasons = @()
    if ([string]::IsNullOrWhiteSpace($TrxPath) -or -not (Test-Path -LiteralPath $TrxPath)) {
        return [pscustomobject]@{
            coverageComplete = $false
            testsPassed = $false
            reasons = @('missing-trx')
            rows = @()
        }
    }
    $fresh = Test-NightlyFreshEvidence -Path $TrxPath -RunDirectory $RunDirectory -NotBeforeUtc $NotBeforeUtc -InvocationId ''
    if (-not $fresh.Ok) {
        return [pscustomobject]@{
            coverageComplete = $false
            testsPassed = $false
            reasons = @($fresh.Reason)
            rows = @()
        }
    }
    $rows = @()
    try {
        $rows = @(Read-NightlyTrxIdentities -TrxPath $TrxPath)
    } catch {
        return [pscustomobject]@{
            coverageComplete = $false
            testsPassed = $false
            reasons = @($_.Exception.Message)
            rows = @()
        }
    }
    $discoveryNodes = @()
    $discoveryPresent = $false
    if ([string]::IsNullOrWhiteSpace($DiscoveryPath) -or -not (Test-Path -LiteralPath $DiscoveryPath)) {
        $reasons += 'missing-discovery'
    } else {
        try {
            $disc = Read-NightlyDiscoveryDocument -Path $DiscoveryPath
            $discoveryNodes = @($disc.Nodes)
            if ($discoveryNodes.Count -eq 0) {
                $reasons += 'empty-discovery'
            } else {
                $discoveryPresent = $true
            }
        } catch {
            return [pscustomobject]@{
                coverageComplete = $false
                testsPassed = $false
                reasons = @($_.Exception.Message)
                rows = $rows
                terminalNodes = @()
                discoveryNodes = @()
            }
        }
    }
    $terminalNodes = @()
    $execPresent = $false
    $execPath = $ExecutionDiagnosticPath
    if (-not [string]::IsNullOrWhiteSpace($execPath) -and (Test-Path -LiteralPath $execPath) -and (Test-Path -LiteralPath $execPath -PathType Container)) {
        $execPath = Get-NightlyLatestDiagnosticLog -Directory $execPath
    }
    if ([string]::IsNullOrWhiteSpace($execPath) -or -not (Test-Path -LiteralPath $execPath)) {
        if ($discoveryPresent) { $reasons += 'missing-execution-diagnostic' }
    } else {
        try {
            $exec = ConvertFrom-NightlyDiagnosticLog -Path $execPath -Kind execution
            $terminalNodes = @($exec.Nodes)
            $execPresent = $true
        } catch {
            $reasons += $_.Exception.Message
        }
    }
    $verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $rows -ProcessExit $ProcessExit `
        -Sha $Sha -ExpectedSha $ExpectedSha -GitRef $GitRef -ExpectedRef $ExpectedRef `
        -PolicyHash $PolicyHash -ExpectedPolicyHash $ExpectedPolicyHash `
        -DiscoveryNodes $discoveryNodes -UsedDiscoveryAsExecution $false
    $coverageComplete = [bool]$verdict.coverageComplete
    $testsPassed = [bool]$verdict.testsPassed
    foreach ($r in @($verdict.reasons)) { $reasons += $r }
    if (-not $discoveryPresent) {
        $coverageComplete = $false
    } else {
        $requiredUids = @(Get-NightlyRequiredDiscoveryUids -DiscoveryNodes $discoveryNodes -RequiredClasses $RequiredClasses)
        if (-not $execPresent) {
            $coverageComplete = $false
        } else {
            $expanded = Test-NightlyExpandedRowsPresent -DiscoveryNodes $discoveryNodes -TerminalRows $terminalNodes -RequiredUids $requiredUids
            if (-not $expanded.Ok) {
                $coverageComplete = $false
                $reasons += ('missing-expanded-row {0}' -f ($expanded.Missing -join ','))
            }
            $cross = Test-NightlyTrxDiagnosticCrossCheck -TrxRows $rows -TerminalNodes $terminalNodes -RequiredClasses $RequiredClasses
            if (-not $cross.Ok) {
                $coverageComplete = $false
                if ($cross.Mismatch.Count -gt 0) {
                    $reasons += ('trx-diagnostic-mismatch {0}' -f ($cross.Mismatch -join ','))
                }
                if ($cross.ExtraClasses.Count -gt 0) {
                    $reasons += ('extra-class-in-chunk {0}' -f ($cross.ExtraClasses -join ','))
                }
            }
        }
        $classReq = @()
        if ($RequiredClasses -and @($RequiredClasses).Count -gt 0) {
            foreach ($c in @($RequiredClasses)) { $classReq += [string]$c }
        } else {
            $seenClass = @{}
            foreach ($n in $discoveryNodes) {
                if (Test-NightlyDiscoveryExcluded -Node $n) { continue }
                $t = [string]$n.Type
                if ([string]::IsNullOrWhiteSpace($t)) { $t = Get-NightlyClassNameFromIdentity -ClassName ([string]$n.ClassName) }
                if ([string]::IsNullOrWhiteSpace($t) -or $seenClass.ContainsKey($t)) { continue }
                $seenClass[$t] = $true
                $classReq += $t
            }
        }
        if ($classReq.Count -gt 0) {
            $member = Test-NightlyRequiredClassesPresent -TerminalRows $rows -RequiredClasses $classReq
            if (-not $member.Ok) {
                $coverageComplete = $false
                $testsPassed = $false
                $reasons += ('missing-required-class {0}' -f ($member.Missing -join ','))
            }
        }
    }
    return [pscustomobject]@{
        coverageComplete = $coverageComplete
        testsPassed = $testsPassed
        reasons = $reasons
        rows = $rows
        terminalNodes = $terminalNodes
        discoveryNodes = $discoveryNodes
    }
}
