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
        $nodes += [pscustomobject]@{
            Uid = $uid
            Assembly = [string]$n.assembly
            Namespace = [string]$n.namespace
            Type = $typeName
            Method = $method
            Signature = [string]$n.signature
            State = [string]$n.state
            ClassName = $(if ([string]::IsNullOrWhiteSpace([string]$n.namespace)) { $typeName } else { '{0}.{1}' -f $n.namespace, $typeName })
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

function ConvertFrom-NightlyDiagnosticLog {
    param([string]$Path, [string]$Kind)
    # Strict text adapter for TUnit diagnostic logs. Accepts golden JSON first; otherwise
    # requires DiscoveredTestNodeStateProperty / terminal state records with UID + method id.
    if (-not (Test-Path -LiteralPath $Path)) { throw ('missing diagnostic {0}' -f $Path) }
    $text = [System.IO.File]::ReadAllText($Path)
    if ($text.Trim().StartsWith('{')) {
        $obj = $text | ConvertFrom-Json
        if ($Kind -eq 'discovery') {
            return (ConvertFrom-NightlyDiscoveryDocument -Object $obj -ExpectedFormat $script:NightlyDiscoveryFormat)
        }
        return $obj
    }
    $uids = @()
    $pattern = 'DiscoveredTestNodeStateProperty.*?TestNodeUid["\s:=]+(?<uid>[^\s",}]+).*?Type["\s:=]+(?<type>[^\s",}]+).*?Method["\s:=]+(?<method>[^\s",}]+)'
    $matches = [regex]::Matches($text, $pattern, 'Singleline')
    if ($matches.Count -eq 0) {
        throw ('unknown diagnostic format {0}' -f $Path)
    }
    $nodes = @()
    $seen = @{}
    foreach ($m in $matches) {
        $uid = $m.Groups['uid'].Value
        if ($seen.ContainsKey($uid)) { throw ('duplicate discovery uid {0}' -f $uid) }
        $seen[$uid] = $true
        $nodes += [pscustomobject]@{
            Uid = $uid
            Type = $m.Groups['type'].Value
            Method = $m.Groups['method'].Value
            State = 'Discovered'
            ClassName = $m.Groups['type'].Value
        }
    }
    return [pscustomobject]@{ Nodes = $nodes }
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
        $reasons += 'zero-terminal-rows'
    }
    $failed = @($TerminalRows | Where-Object { [string]$_.Outcome -eq 'Failed' -or [string]$_.Outcome -eq 'FAIL' })
    $skipped = @($TerminalRows | Where-Object { [string]$_.Outcome -eq 'Skipped' })
    if ($failed.Count -gt 0) {
        $testsPassed = $false
        $reasons += 'failed-row'
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
    $passedCount = @($TerminalRows | Where-Object { [string]$_.Outcome -eq 'Passed' -or [string]$_.Outcome -eq 'pass' }).Count
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
    foreach ($r in @($TerminalRows)) {
        if ($r.Uid) { $terminalUids[[string]$r.Uid] = $true }
        elseif ($r.Identity) { $terminalUids[[string]$r.Identity] = $true }
    }
    $missing = @()
    foreach ($uid in @($RequiredUids)) {
        if (-not $terminalUids.ContainsKey($uid)) { $missing += $uid }
    }
    return [pscustomobject]@{ Ok = ($missing.Count -eq 0); Missing = $missing }
}
