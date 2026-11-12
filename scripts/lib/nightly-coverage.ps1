#requires -Version 5.1
# CARD-0487: TRX, diagnostic-discovery and coverage reconciliation.
# ASCII-only. Discovery is never execution evidence.

if ($script:AntiphonNightlyCoverageLoaded) { return }
$script:AntiphonNightlyCoverageLoaded = $true
. (Join-Path $PSScriptRoot 'nightly-core.ps1')

$script:NightlyDiscoveryFormat = 'antiphon-tunit-discovery-v1'
$script:NightlyExecutionFormat = 'antiphon-tunit-execution-v1'
$script:NightlySupportedTunit = '1.44.0'
$script:NightlySupportedMtp = '2.2.2'
# TUnit 1.44 GetClassTypeName: nested '+', generic backtick arity, constructor
# arguments, and a middle dot where a string argument contained '.'.
# The field ends at ", MethodName = " followed by MethodArity, so a comma
# inside the arguments cannot terminate the name early.
$script:NightlyDiagnosticTypeNameClass = '[A-Za-z0-9_.+\x60()<>,\[\] \u00B7-]'

function Read-NightlyTrxIdentities {
    param([string]$TrxPath)
    if (-not (Test-Path -LiteralPath $TrxPath)) {
        throw ('missing TRX {0}' -f $TrxPath)
    }
    if (Get-Command Get-TestPhasePathIdentity -ErrorAction SilentlyContinue) {
        $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
        if ($trxIdentity.Kind -ne 'regular') {
            throw ('refused TRX {0}' -f $TrxPath)
        }
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
        $debugTrace = ''
        foreach ($child in @($n.ChildNodes)) {
            if ($child.LocalName -cne 'Output') { continue }
            foreach ($outChild in @($child.ChildNodes)) {
                if ($outChild.LocalName -ceq 'DebugTrace') {
                    $debugTrace = [string]$outChild.InnerText
                    break
                }
            }
        }
        $rows += [pscustomobject]@{
            TestId = $testId
            Uid = ''
            ClassName = $className
            MethodName = $methodName
            Display = $display
            Outcome = $outcome
            Identity = ('{0}.{1}' -f $className, $methodName)
            DebugTrace = $debugTrace
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
    if (Test-NightlyCoreAvailable) {
        # Compiled node loop (Antiphon.Nightly/NightlyDocuments.cs): the same rows and the same refusals.
        $compiled = [Antiphon.Nightly.NightlyDocuments]::DiscoveryNodes($Object.nodes)
        if ($compiled.Error) { throw $compiled.Error }
        return [pscustomobject]@{
            Format = $format
            TunitVersion = $tunit
            MtpVersion = $mtp
            AssemblyHash = [string]$Object.assemblyHash
            Nodes = $compiled.Nodes
        }
    }
    $nodes = [Collections.Generic.List[object]]::new()
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
        $nodes.Add([pscustomobject]@{
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
        })
    }
    return [pscustomobject]@{
        Format = $format
        TunitVersion = $tunit
        MtpVersion = $mtp
        AssemblyHash = [string]$Object.assemblyHash
        Nodes = $nodes.ToArray()
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

function Get-NightlyDiagnosticStateProperty {
    param([string]$Record)
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyDiagnostics]::StateProperty($Record) }
    $absent = [pscustomobject]@{ Name = ''; ExplanationStart = -1 }
    # Enter the TestNode's own property list, not the update-message properties
    # or display text. Match the pinned identifier -> optional location -> state
    # transition; state-looking words in free text are not property boundaries.
    $envelope = [regex]::Match($Record, '(?s)TestNode \{ Uid = TestNodeUid \{ Value = [^}]* \}, DisplayName = .*?, Properties = \[ ')
    if (-not $envelope.Success) { return $absent }
    $identifier = 'TestMethodIdentifierProperty \{ AssemblyFullName = [^{}\r\n]+, Namespace = [A-Za-z0-9_.]*, TypeName = ' + $script:NightlyDiagnosticTypeNameClass + '*?, MethodName = [A-Za-z0-9_]*, MethodArity = \d+, ParameterTypeFullNames = \[[^{}\r\n]*\], ReturnTypeFullName = [^{}\r\n]+ \}, '
    $location = '(?:TestFileLocationProperty \{ FilePath = [^\r\n]*?, LineSpan = LinePositionSpan \{ Start = LinePosition \{ Line = \d+, Column = \d+ \}, End = LinePosition \{ Line = \d+, Column = \d+ \} \} \}, )?'
    $transition = [regex]::new($identifier + $location + '(?<name>[A-Za-z]+)TestNodeStateProperty \{ Explanation = ')
    $hit = $transition.Match($Record, $envelope.Index + $envelope.Length)
    if (-not $hit.Success) { return $absent }
    # Unescaped free text can mimic a complete transition. Stop at the second
    # interpretation: neither a terminal success nor an exact message is safe.
    if ($hit.NextMatch().Success) { return $absent }
    return [pscustomobject]@{ Name = $hit.Groups['name'].Value; ExplanationStart = $hit.Index + $hit.Length }
}

function Get-NightlyDiagnosticStateName {
    param([string]$Record, $StateProperty = $null)
    if ($null -eq $StateProperty) { $StateProperty = Get-NightlyDiagnosticStateProperty -Record $Record }
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyDiagnostics]::StateName([string]$StateProperty.Name) }
    switch -CaseSensitive ($StateProperty.Name) {
        'Passed' { return 'Passed' }
        'Skipped' { return 'Skipped' }
        'Discovered' { return 'Discovered' }
        'InProgress' { return 'InProgress' }
        { $_ -cin @('Failed', 'Error', 'Timeout', 'Cancelled') } { return 'Failed' }
        default { return '' }
    }
}

function Get-NightlyDiagnosticFailureMessage {
    param([string]$Record, $StateProperty = $null)
    if ($null -eq $StateProperty) { $StateProperty = Get-NightlyDiagnosticStateProperty -Record $Record }
    if ($StateProperty.ExplanationStart -lt 0 -or $StateProperty.Name -cin @('Passed', 'Skipped', 'Discovered', 'InProgress')) { return $null }
    $tail = $Record.Substring($StateProperty.ExplanationStart)
    # The pinned property is the last item in TestNode.Properties. Remove only
    # its structural suffix, never whitespace belonging to the explanation.
    $suffix = ' \} \] \} \}\s*\z'
    if ($StateProperty.Name -ceq 'Timeout') {
        # Native MTP 2.2.2 prints a null Timeout as empty text.
        $suffix = ', Timeout = (?:(?:\d+\.)?\d{2}:\d{2}:\d{2}(?:\.\d+)?)?' + $suffix
    }
    $close = [regex]::Match($tail, $suffix)
    if (-not $close.Success) { return $null }
    $body = $tail.Substring(0, $close.Index)
    $marker = ', Exception = '
    $candidates = @()
    $offset = 0
    while (($offset = $body.IndexOf($marker, $offset, [StringComparison]::Ordinal)) -ge 0) {
        $message = $body.Substring(0, $offset)
        $exception = $body.Substring($offset + $marker.Length)
        if (Test-NightlyDiagnosticExceptionTail -Message $message -ExceptionText $exception `
            -AllowEmpty ($StateProperty.Name -cin @('Timeout', 'Cancelled'))) {
            $candidates += $message
        }
        $offset += $marker.Length
    }
    # ToString() does not escape messages or stacks. More than one valid split
    # is real ambiguity, not permission to publish the first or last candidate.
    if ($candidates.Count -eq 1 -and $candidates[0].Length -gt 0) { return $candidates[0] }
    return $null
}

function Test-NightlyDiagnosticExceptionTail {
    param([string]$Message, [string]$ExceptionText, [bool]$AllowEmpty = $false)
    if ($AllowEmpty -and $ExceptionText.Length -eq 0) { return $true }
    if ($ExceptionText -ceq '<null>') { return $true }
    $echoType = 'TUnit.Engine.Exceptions.TestFailedException: '
    if ($ExceptionText.StartsWith($echoType, [StringComparison]::Ordinal)) {
        $prefix = '[Test Failure] '
        if (-not $Message.StartsWith($prefix, [StringComparison]::Ordinal)) { return $false }
        $echo = $echoType + $Message.Substring($prefix.Length)
        if (-not $ExceptionText.StartsWith($echo, [StringComparison]::Ordinal)) { return $false }
        $stack = $ExceptionText.Substring($echo.Length)
        return ($stack.Length -eq 0 -or $stack -cmatch '^\r?\n   at ')
    }
    # Reflected Error/Timeout/Cancelled shapes may carry a regular exception
    # rather than TUnit's echo, or <null>. Its first line must be a type/message;
    # any remaining text must start with a stack frame.
    return ($ExceptionText -cmatch '\A[A-Za-z_][A-Za-z0-9_.+`]*Exception: [^\r\n]*(?:\r?\n   at [\s\S]*)?\z')
}

function Test-NightlyDiagnosticTypeNameComplete {
    # A node name is a complete identifier, optionally followed by one balanced
    # constructor-argument suffix. Commas belong inside those arguments.
    # IndexOf plus a balance count stays within Windows PowerShell 5.1.
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $false }
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyDiagnostics]::TypeNameComplete($Name) }
    $openAt = $Name.IndexOf('(')
    $closeAt = $Name.IndexOf(')')
    $commaAt = $Name.IndexOf(',')
    if ($openAt -lt 0 -and $closeAt -lt 0) {
        if ($commaAt -ge 0) { return $false }
        return $true
    }
    if ($openAt -le 0) { return $false }
    if (-not $Name.EndsWith(')', [StringComparison]::Ordinal)) { return $false }
    $depth = 0
    $i = 0
    while ($i -lt $Name.Length) {
        $ch = $Name.Substring($i, 1)
        if ($ch -eq '(') {
            $depth++
        } elseif ($ch -eq ')') {
            $depth--
            if ($depth -lt 0) { return $false }
        } elseif ($ch -eq ',' -and $depth -eq 0) {
            return $false
        }
        $i++
    }
    return ($depth -eq 0)
}

function ConvertFrom-NightlyDiagnosticRecord {
    param([string]$Record, [string]$State, $StateProperty = $null)
    if (Test-NightlyCoreAvailable) {
        $converted = [Antiphon.Nightly.NightlyDiagnostics]::ConvertRecord($Record, $State)
        if ($converted.Error) { throw $converted.Error }
        $node = $converted.Node
        if ($State -eq 'Failed') {
            $message = Get-NightlyDiagnosticFailureMessage -Record $Record -StateProperty $StateProperty
            if ($null -ne $message) {
                $node | Add-Member -NotePropertyName Message -NotePropertyValue $message
            }
        }
        return $node
    }
    if ($Record -notmatch 'TestNodeUid \{ Value = (?<uid>[^}]+?) \}') {
        throw 'truncated diagnostic record: missing uid'
    }
    $uid = $Matches['uid'].Trim()
    $typeName = ''
    $method = ''
    $ns = ''
    $assembly = ''
    $typePattern = '(?<![A-Za-z])TypeName = (?<type>' + $script:NightlyDiagnosticTypeNameClass + '+?), MethodName = '
    if ($Record -match $typePattern) { $typeName = $Matches['type'] }
    if ($Record -match '(?<![A-Za-z])MethodName = (?<method>[A-Za-z0-9_]+)') { $method = $Matches['method'] }
    if ($Record -match '(?<![A-Za-z])Namespace = (?<ns>[A-Za-z0-9_\.]+)') { $ns = $Matches['ns'] }
    if ($Record -match 'AssemblyFullName = (?<asm>[^,]+)') { $assembly = $Matches['asm'].Trim() }
    if ([string]::IsNullOrWhiteSpace($uid)) { throw 'truncated diagnostic record: missing uid' }
    if ([string]::IsNullOrWhiteSpace($typeName) -or [string]::IsNullOrWhiteSpace($method)) {
        throw ('truncated diagnostic record uid={0}' -f $uid)
    }
    if (-not (Test-NightlyDiagnosticTypeNameComplete -Name $typeName)) {
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
    $node = [pscustomobject]@{
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
    # Native MTP failure explanations include the multiline failure message.
    # Timeout and Cancelled are failed states. Only a unique validated field
    # boundary supplies Message; ambiguous or malformed text keeps Failed.
    if ($State -eq 'Failed') {
        $message = Get-NightlyDiagnosticFailureMessage -Record $Record -StateProperty $StateProperty
        if ($null -ne $message) {
            $node | Add-Member -NotePropertyName Message -NotePropertyValue $message
        }
    }
    return $node
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

function Get-NightlyDiagnosticRecords {
    param([string]$Text)
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyDiagnostics]::Records($Text) }
    # A record ends at the next structural node update or logging header, never
    # at display-name punctuation. Keep the original physical line endings.
    # Every TestNodeUpdateMessage envelope starts a record, including a second
    # envelope on the same line. A bare TestNode { Uid in explanation text does not.
    $record = New-Object System.Text.StringBuilder
    $envelopePattern = 'TestNodeUpdateMessage \{[^\r\n]*?TestNode = TestNode \{ Uid = TestNodeUid'
    foreach ($line in [regex]::Split($Text, '(?<=\n)')) {
        $envelopeHits = [regex]::Matches($line, $envelopePattern)
        if ($envelopeHits.Count -ge 2) {
            if ($record.Length -gt 0) { $record.ToString(); [void]$record.Clear() }
            $start = 0
            $hitIndex = 1
            while ($hitIndex -lt $envelopeHits.Count) {
                $at = $envelopeHits[$hitIndex].Index
                if ($at -gt $start) {
                    $piece = $line.Substring($start, $at - $start)
                    if ($piece.Length -gt 0) { $piece }
                }
                $start = $at
                $hitIndex++
            }
            if ($start -lt $line.Length) { [void]$record.Append($line.Substring($start)) }
            continue
        }
        $nodeStart = $line -match '^(?:[^\r\n]*?TestNodeUpdateMessage \{[^\r\n]*?TestNode = |\s*)TestNode \{ Uid = TestNodeUid'
        $headerStart = $line -match '^\d{4}-\d{2}-\d{2}T\S+\s+\S+\s+(?:TRACE|DEBUG|INFORMATION|WARNING|ERROR|CRITICAL)\b' -or
                       $line -match '^\s*version:\s+'
        if ($nodeStart -or $headerStart) {
            if ($record.Length -gt 0) { $record.ToString(); [void]$record.Clear() }
        }
        if ($nodeStart -or ($record.Length -gt 0 -and -not $headerStart)) {
            [void]$record.Append($line)
        }
    }
    if ($record.Length -gt 0) { $record.ToString() }
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
    # Count raw markers independently of framing/acceptance. Even an orphan or
    # an ambiguous marker in display text must prevent a partial inventory.
    if (Test-NightlyCoreAvailable) { $discoveredCount = [Antiphon.Nightly.NightlyDiagnostics]::DiscoveredMarkerCount($text) }
    else { $discoveredCount = [regex]::Matches($text, 'DiscoveredTestNodeStateProperty \{').Count }
    $recognized = $false
    $nodes = [Collections.Generic.List[object]]::new()
    $seen = @{}
    if (Test-NightlyCoreAvailable) {
        # Compiled record loop (NightlyCore.cs): same refusals in the same record order. Failure messages are
        # attached here, as the loop below does per record; they never refuse, so the order is unchanged.
        $parsed = [Antiphon.Nightly.NightlyDiagnostics]::ParseRecords($text, $Kind, $Path)
        if ($parsed.Error) { throw $parsed.Error }
        foreach ($failed in $parsed.Failed) {
            $message = Get-NightlyDiagnosticFailureMessage -Record $failed.Record -StateProperty $failed.StateProperty
            if ($null -ne $message) {
                $failed.Node | Add-Member -NotePropertyName Message -NotePropertyValue $message
            }
        }
        $recognized = [bool]$parsed.Recognized
        $nodes.AddRange([object[]]$parsed.Nodes)
    } else {
    foreach ($record in (Get-NightlyDiagnosticRecords -Text $text)) {
        $stateProperty = Get-NightlyDiagnosticStateProperty -Record $record
        $state = Get-NightlyDiagnosticStateName -Record $record -StateProperty $stateProperty
        if ([string]::IsNullOrWhiteSpace($state)) {
            if ($Kind -eq 'discovery') {
                # Preserve the specific malformed-identity refusal when a
                # truncated discovery record still carries a discovery header.
                # This presence check can only refine a refusal: it never
                # selects state, admits a node, or borrows the next record.
                if ($record -cmatch '(?<![A-Za-z])DiscoveredTestNodeStateProperty \{') {
                    $null = ConvertFrom-NightlyDiagnosticRecord -Record $record -State '' -StateProperty $stateProperty
                }
                throw ('malformed discovery record: missing state {0}' -f $Path)
            }
            # A framed TestNode with no known state is a failure. A log with no
            # TestNode still throws unknown diagnostic format below.
            if ($record -match 'TestNode \{ Uid = TestNodeUid') { $state = 'Failed' }
            else { continue }
        }
        $recognized = $true
        if ($Kind -eq 'discovery') {
            if ($state -ne 'Discovered') { continue }
        } elseif ($Kind -eq 'execution') {
            if ($state -eq 'InProgress' -or $state -eq 'Discovered') { continue }
        }
        $node = ConvertFrom-NightlyDiagnosticRecord -Record $record -State $state -StateProperty $stateProperty
        if ($seen.ContainsKey($node.Uid)) {
            throw ('duplicate {0} uid {1}' -f $Kind, $node.Uid)
        }
        $seen[$node.Uid] = $true
        $nodes.Add($node)
    }
    }
    if (-not $recognized) {
        throw ('unknown diagnostic format {0}' -f $Path)
    }
    if ($Kind -eq 'discovery' -and $discoveredCount -ne $nodes.Count) {
        throw ('discovery-count-mismatch discovered={0} parsed={1} path={2}' -f $discoveredCount, $nodes.Count, $Path)
    }
    return [pscustomobject]@{ Nodes = $nodes.ToArray() }
}

function ConvertTo-NightlyNormalizedOutcome {
    param([string]$Outcome)
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyClassNames]::NormalizedOutcome($Outcome) }
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
    # Compiled node loop (Antiphon.Nightly/NightlyDocuments.cs); the loop below appended one row at a time.
    if (Test-NightlyCoreAvailable) { $mapped = [Antiphon.Nightly.NightlyDocuments]::DocumentNodes($Nodes); $Nodes = @() }
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
        [int]$TimeoutMilliseconds = 600000,
        [string[]]$ArgumentPrefix = @(),
        [string]$AssemblyPath = ''
    )
    if ([string]::IsNullOrWhiteSpace($ExePath)) { throw 'missing assembly exe' }
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw 'missing discovery output' }
    if ([string]::IsNullOrWhiteSpace($DiagnosticDirectory)) { throw 'missing diagnostic directory' }
    New-Item -ItemType Directory -Path $DiagnosticDirectory -Force | Out-Null
    $launchArgs = @()
    if ($ArgumentPrefix) {
        foreach ($prefixArg in @($ArgumentPrefix)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$prefixArg)) { $launchArgs += [string]$prefixArg }
        }
    }
    $launchArgs += @(
        '--list-tests',
        '--no-progress',
        '--no-ansi',
        '--diagnostic',
        '--diagnostic-output-directory',
        $DiagnosticDirectory
    )
    $run = Invoke-NightlyOwnedProcess -FilePath $ExePath -ArgumentList $launchArgs `
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
    $hashSource = $ExePath
    if (-not [string]::IsNullOrWhiteSpace($AssemblyPath)) { $hashSource = $AssemblyPath }
    $hash = Get-NightlyAssemblyHash -ExePath $hashSource
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

function Get-NightlyBareClassIdentity {
    # TUnit 1.44 GetClassTypeName appends constructor arguments, for example
    # AgentTuiApiTests(AgentTuiApiWebAppFactory). The treenode filter path uses
    # the metadata class name and does not include that suffix, so chunking,
    # filters, roster matching, policy rows, and TRX keys share the bare name.
    param([string]$Name)
    if ([string]::IsNullOrWhiteSpace($Name)) { return $Name }
    $open = $Name.IndexOf('(')
    if ($open -gt 0 -and $Name.EndsWith(')', [StringComparison]::Ordinal)) {
        return $Name.Substring(0, $open)
    }
    return $Name
}

function Get-NightlyClassNameFromIdentity {
    param([string]$ClassName)
    if ([string]::IsNullOrWhiteSpace($ClassName)) { return $ClassName }
    $bare = Get-NightlyBareClassIdentity -Name $ClassName
    $parts = $bare.Split('.')
    return $parts[$parts.Length - 1]
}

function Test-NightlyClassAssigned {
    param([string]$ClassName, [string]$TypeName, [string[]]$RequiredClasses)
    if ($null -eq $RequiredClasses -or @($RequiredClasses).Count -eq 0) { return $true }
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyClassNames]::Assigned($ClassName, $TypeName, $RequiredClasses) }
    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($ClassName)) {
        $candidates += $ClassName
        $candidates += (Get-NightlyBareClassIdentity -Name $ClassName)
    }
    if (-not [string]::IsNullOrWhiteSpace($TypeName)) {
        $candidates += $TypeName
        $candidates += (Get-NightlyBareClassIdentity -Name $TypeName)
    }
    foreach ($c in @($RequiredClasses)) {
        $want = [string]$c
        if ([string]::IsNullOrWhiteSpace($want)) { continue }
        $wantBare = Get-NightlyBareClassIdentity -Name $want
        foreach ($have in $candidates) {
            if ([string]::Equals($want, $have, [StringComparison]::OrdinalIgnoreCase)) { return $true }
            if ([string]::Equals($wantBare, $have, [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

function Test-NightlyDiscoveryExcluded {
    <#
      CARD-0599 D-5: in the default nightly lane OptIn/Explicit still excludes, which is
      what -ProfileExclusions absent means. When a profile supplies its dispositions, the
      raw category is preserved on the node but no longer decides: only a declared
      class (or class+method) row excludes. An unclassified node therefore stays required
      and fails the census rather than silently inheriting manual status.
    #>
    param($Node, $ProfileExclusions = $null, [switch]$ProfileAware)
    if ($null -eq $Node) { return $false }
    if ($ProfileAware) {
        return (Test-NightlyProfileNodeExcluded -Node $Node -Exclusions $ProfileExclusions)
    }
    if ([bool]$Node.Excluded) { return $true }
    foreach ($c in @($Node.Categories)) {
        if ([string]$c -eq 'OptIn' -or [string]$c -eq 'Explicit') { return $true }
    }
    return $false
}

function Get-NightlyDiscoveryDisposition {
    <#
      D-5: every discovery UID must resolve to exactly one disposition. Returns
      'required' or 'excluded' carrying the declaring policy row's reason and owner, so
      the manifest can record why a node was left out and under whose ownership.
    #>
    param($Node, $ProfileExclusions = $null, [switch]$ProfileAware)
    if (-not $ProfileAware) {
        if (Test-NightlyDiscoveryExcluded -Node $Node) {
            return [pscustomobject]@{ Disposition = 'excluded'; Reason = 'category-optin'; Owner = 'nightly-default'; Uid = [string]$Node.Uid }
        }
        return [pscustomobject]@{ Disposition = 'required'; Reason = ''; Owner = ''; Uid = [string]$Node.Uid }
    }
    $simple = Get-NightlyClassNameFromIdentity -ClassName ([string]$Node.ClassName)
    if ([string]::IsNullOrWhiteSpace($simple)) { $simple = [string]$Node.Type }
    foreach ($row in @($ProfileExclusions)) {
        $cls = [string]$row.Class
        if ([string]::IsNullOrWhiteSpace($cls)) { continue }
        $match = [string]::Equals($cls, $simple, [StringComparison]::OrdinalIgnoreCase) -or
                 [string]::Equals($cls, [string]$Node.ClassName, [StringComparison]::OrdinalIgnoreCase)
        if (-not $match) { continue }
        $methods = @($row.Methods)
        if ($methods.Count -eq 0) {
            return [pscustomobject]@{ Disposition = 'excluded'; Reason = [string]$row.Reason; Owner = [string]$row.Owner; Uid = [string]$Node.Uid }
        }
        foreach ($m in $methods) {
            if ([string]::Equals([string]$m, [string]$Node.Method, [StringComparison]::OrdinalIgnoreCase)) {
                return [pscustomobject]@{ Disposition = 'excluded'; Reason = [string]$row.Reason; Owner = [string]$row.Owner; Uid = [string]$Node.Uid }
            }
        }
    }
    return [pscustomobject]@{ Disposition = 'required'; Reason = ''; Owner = ''; Uid = [string]$Node.Uid }
}

function Get-NightlyDiscoveryDispositionIndexed {
    <# Get-NightlyDiscoveryDisposition (profile-aware) against a New-NightlyProfileExclusionIndex: only the rows
       that match the node's class are walked, in policy order, so the first declaring row still wins. #>
    param($Index, $Node)
    if (Test-NightlyCoreAvailable) { return $Index.Disposition($Node) }
    $entry = Get-NightlyProfileExclusionEntry -Index $Index -ClassName ([string]$Node.ClassName) -Type ([string]$Node.Type)
    if ($null -ne $entry) {
        foreach ($row in $entry.Rows) {
            $methods = @($row.Methods)
            if ($methods.Count -eq 0) {
                return [pscustomobject]@{ Disposition = 'excluded'; Reason = [string]$row.Reason; Owner = [string]$row.Owner; Uid = [string]$Node.Uid }
            }
            foreach ($m in $methods) {
                if ([string]::Equals([string]$m, [string]$Node.Method, [StringComparison]::OrdinalIgnoreCase)) {
                    return [pscustomobject]@{ Disposition = 'excluded'; Reason = [string]$row.Reason; Owner = [string]$row.Owner; Uid = [string]$Node.Uid }
                }
            }
        }
    }
    return [pscustomobject]@{ Disposition = 'required'; Reason = ''; Owner = ''; Uid = [string]$Node.Uid }
}

function New-NightlyClassAssignmentCache {
    <# Test-NightlyClassAssigned depends only on (ClassName, TypeName) for fixed RequiredClasses: a caller that tests
       many nodes keeps one of these and asks once per class. #>
    param([string[]]$RequiredClasses = @())
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.ClassAssignment]::new($RequiredClasses) }
    return [pscustomobject]@{
        RequiredClasses = $RequiredClasses
        Verdicts = [Collections.Generic.Dictionary[string, bool]]::new([StringComparer]::Ordinal)
    }
}

function Test-NightlyClassAssignedCached {
    param($Cache, [string]$ClassName, [string]$TypeName)
    if (Test-NightlyCoreAvailable) { return $Cache.Assigned($ClassName, $TypeName) }
    $key = $ClassName + [char]0 + $TypeName
    $verdict = $false
    if (-not $Cache.Verdicts.TryGetValue($key, [ref]$verdict)) {
        $verdict = [bool](Test-NightlyClassAssigned -ClassName $ClassName -TypeName $TypeName -RequiredClasses $Cache.RequiredClasses)
        $Cache.Verdicts[$key] = $verdict
    }
    return $verdict
}

function Get-NightlyDiscoveryCensus {
    <#
      D-5: the expanded-UID manifest. Every discovered node gets exactly one
      disposition; duplicates and unclassified nodes are reported, never dropped.
    #>
    param([object[]]$DiscoveryNodes, $ProfileExclusions = $null, [switch]$ProfileAware)
    $required = [Collections.Generic.List[object]]::new()
    $excluded = [Collections.Generic.List[object]]::new()
    $seen = @{}
    $duplicates = [Collections.Generic.List[object]]::new()
    # Profile rows are resolved once per class (same dispositions as Get-NightlyDiscoveryDisposition).
    $exclusionIndex = $null
    if ($ProfileAware) { $exclusionIndex = New-NightlyProfileExclusionIndex -Exclusions $ProfileExclusions }
    if (Test-NightlyCoreAvailable) {
        $rows = [Antiphon.Nightly.NightlyDiscovery]::CensusRows(@($DiscoveryNodes), $exclusionIndex)
        foreach ($row in $rows.Required) { $required.Add($row) }
        foreach ($row in $rows.Excluded) { $excluded.Add($row) }
        foreach ($uid in $rows.Duplicates) { $duplicates.Add($uid) }
        $DiscoveryNodes = @()
    }
    foreach ($n in @($DiscoveryNodes)) {
        $uid = [string]$n.Uid
        if ([string]::IsNullOrWhiteSpace($uid)) { continue }
        if ($seen.ContainsKey($uid)) { $duplicates.Add($uid); continue }
        $seen[$uid] = $true
        if ($ProfileAware) { $d = Get-NightlyDiscoveryDispositionIndexed -Index $exclusionIndex -Node $n }
        else { $d = Get-NightlyDiscoveryDisposition -Node $n -ProfileExclusions $ProfileExclusions -ProfileAware:$ProfileAware }
        $row = [ordered]@{
            uid = $uid
            class = [string]$n.ClassName
            method = [string]$n.Method
            categories = @($n.Categories)
            disposition = $d.Disposition
            reason = $d.Reason
            owner = $d.Owner
        }
        if ($d.Disposition -eq 'required') { $required.Add($row) } else { $excluded.Add($row) }
    }
    $digest = Get-NightlySha256Text -Text (ConvertTo-NightlyCanonicalJson -Object ([ordered]@{
        required = @(@(foreach ($r in $required) { $r.uid }) | Sort-Object)
        excluded = @(@(foreach ($r in $excluded) { $r.uid }) | Sort-Object)
    }))
    return [pscustomobject]@{
        Ok = ($duplicates.Count -eq 0 -and $required.Count -gt 0)
        Required = $required.ToArray()
        Excluded = $excluded.ToArray()
        Duplicates = $duplicates.ToArray()
        Digest = $digest
        RequiredCount = $required.Count
        ExcludedCount = $excluded.Count
    }
}

function Get-NightlyRequiredDiscoveryUids {
    param([object[]]$DiscoveryNodes, [string[]]$RequiredClasses = @(), $ProfileExclusions = $null, [switch]$ProfileAware)
    $uids = [Collections.Generic.List[object]]::new()
    $seen = @{}
    $exclusionIndex = $null
    if ($ProfileAware) { $exclusionIndex = New-NightlyProfileExclusionIndex -Exclusions $ProfileExclusions }
    if (Test-NightlyCoreAvailable) {
        return [Antiphon.Nightly.NightlyDiscovery]::RequiredUids(@($DiscoveryNodes), $RequiredClasses, $exclusionIndex)
    }
    $assignment = New-NightlyClassAssignmentCache -RequiredClasses $RequiredClasses
    foreach ($n in @($DiscoveryNodes)) {
        if ($ProfileAware) {
            if (Test-NightlyProfileExclusionIndexed -Index $exclusionIndex -Node $n) { continue }
        } elseif (Test-NightlyDiscoveryExcluded -Node $n) { continue }
        if (-not (Test-NightlyClassAssignedCached -Cache $assignment -ClassName ([string]$n.ClassName) -TypeName ([string]$n.Type))) {
            continue
        }
        $uid = [string]$n.Uid
        if ([string]::IsNullOrWhiteSpace($uid)) { continue }
        if ($seen.ContainsKey($uid)) { continue }
        $seen[$uid] = $true
        $uids.Add($uid)
    }
    return $uids.ToArray()
}

<#
  CARD-1419: every launch of a native suite is judged against the same discovery,
  so a suite parses and censuses it once. A launch then looks up only its own
  classes instead of walking every discovered node again. Results equal the
  per-node functions above; the index only stops repeating them.
#>
function New-NightlyDiscoveryIndex {
    return [pscustomobject]@{
        Entries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    }
}

function Get-NightlyDiscoveryIndexEntry {
    # Keyed by content, not path: the profile lane reuses one file, the default
    # lane writes the same discovery beside each launch, and a rewrite is new bytes.
    param($Index, [string]$Path)
    $key = ''
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $key = [BitConverter]::ToString($sha.ComputeHash([System.IO.File]::ReadAllBytes($Path))) }
        finally { $sha.Dispose() }
        if ($Index.Entries.ContainsKey($key)) { return $Index.Entries[$key] }
    }
    $document = Read-NightlyDiscoveryDocument -Path $Path
    $inProgress = [Collections.Generic.List[object]]::new()
    foreach ($n in @($document.Nodes)) {
        if ($n.State -eq 'InProgress') { $inProgress.Add($n) }
    }
    $entry = [pscustomobject]@{
        Document = $document
        InProgress = $inProgress.ToArray()
        Views = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    }
    if ($key) { $Index.Entries[$key] = $entry }
    return $entry
}

function Get-NightlyDiscoveryIndexView {
    # One disposition pass per entry and exclusion policy: the census, the
    # all-classes required set, and a class lookup that mirrors
    # Test-NightlyClassAssigned (name or bare name, of ClassName or Type).
    param($Entry, $ProfileExclusions = $null, [switch]$ProfileAware)
    $policyKey = 'default'
    if ($ProfileAware) { $policyKey = 'profile:' + (ConvertTo-Json -InputObject $ProfileExclusions -Depth 8 -Compress) }
    if ($Entry.Views.ContainsKey($policyKey)) { return $Entry.Views[$policyKey] }
    $nodes = @($Entry.Document.Nodes)
    $exclusionIndex = $null
    if ($ProfileAware) { $exclusionIndex = New-NightlyProfileExclusionIndex -Exclusions $ProfileExclusions }
    if (Test-NightlyCoreAvailable) {
        $pass = [Antiphon.Nightly.NightlyDiscovery]::IndexView($nodes, $exclusionIndex)
        $view = [pscustomobject]@{
            Nodes = $nodes
            Excluded = $pass.Excluded
            ByClass = $pass.ByClass
            RequiredUids = $pass.RequiredUids
            Classes = $pass.Classes
            Census = (Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $ProfileExclusions -ProfileAware:$ProfileAware)
        }
        $Entry.Views[$policyKey] = $view
        return $view
    }
    $excluded = New-Object 'bool[]' $nodes.Count
    $byClass = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    $allRequired = [Collections.Generic.List[object]]::new()
    $seenUid = @{}
    $classes = [Collections.Generic.List[object]]::new()
    $seenClass = @{}
    for ($i = 0; $i -lt $nodes.Count; $i++) {
        $n = $nodes[$i]
        $className = [string]$n.ClassName
        $typeName = [string]$n.Type
        $names = [Collections.Generic.List[string]]::new()
        if (-not [string]::IsNullOrWhiteSpace($className)) {
            $names.Add($className)
            $names.Add((Get-NightlyBareClassIdentity -Name $className))
        }
        if (-not [string]::IsNullOrWhiteSpace($typeName)) {
            $names.Add($typeName)
            $names.Add((Get-NightlyBareClassIdentity -Name $typeName))
        }
        foreach ($name in $names) {
            $hits = $null
            if (-not $byClass.TryGetValue($name, [ref]$hits)) {
                $hits = [Collections.Generic.List[int]]::new()
                $byClass[$name] = $hits
            }
            if ($hits.Count -eq 0 -or $hits[$hits.Count - 1] -ne $i) { $hits.Add($i) }
        }
        if ($ProfileAware) { $isExcluded = Test-NightlyProfileExclusionIndexed -Index $exclusionIndex -Node $n }
        else { $isExcluded = Test-NightlyDiscoveryExcluded -Node $n }
        if ($isExcluded) {
            $excluded[$i] = $true
            continue
        }
        $uid = [string]$n.Uid
        if (-not [string]::IsNullOrWhiteSpace($uid) -and -not $seenUid.ContainsKey($uid)) {
            $seenUid[$uid] = $true
            $allRequired.Add($uid)
        }
        $t = Get-NightlyBareClassIdentity -Name $className
        if ([string]::IsNullOrWhiteSpace($t)) { $t = Get-NightlyBareClassIdentity -Name $typeName }
        if (-not [string]::IsNullOrWhiteSpace($t) -and -not $seenClass.ContainsKey($t)) {
            $seenClass[$t] = $true
            $classes.Add($t)
        }
    }
    $view = [pscustomobject]@{
        Nodes = $nodes
        Excluded = $excluded
        ByClass = $byClass
        RequiredUids = $allRequired.ToArray()
        Classes = $classes.ToArray()
        Census = (Get-NightlyDiscoveryCensus -DiscoveryNodes $nodes -ProfileExclusions $ProfileExclusions -ProfileAware:$ProfileAware)
    }
    $Entry.Views[$policyKey] = $view
    return $view
}

function Get-NightlyDiscoveryIndexRequiredUids {
    # Same set and discovery order as Get-NightlyRequiredDiscoveryUids, from the
    # launch's classes alone.
    param($View, [string[]]$RequiredClasses = @())
    if ($null -eq $RequiredClasses -or @($RequiredClasses).Count -eq 0) { return $View.RequiredUids }
    if (Test-NightlyCoreAvailable) {
        return [Antiphon.Nightly.NightlyVerdicts]::IndexRequiredUids($View.ByClass, $View.Excluded, $View.Nodes, $RequiredClasses)
    }
    $selected = [Collections.Generic.SortedSet[int]]::new()
    foreach ($c in @($RequiredClasses)) {
        $want = [string]$c
        if ([string]::IsNullOrWhiteSpace($want)) { continue }
        foreach ($name in @($want, (Get-NightlyBareClassIdentity -Name $want))) {
            $hits = $null
            if ($View.ByClass.TryGetValue($name, [ref]$hits)) {
                foreach ($i in $hits) { [void]$selected.Add($i) }
            }
        }
    }
    $uids = [Collections.Generic.List[object]]::new()
    $seen = @{}
    foreach ($i in $selected) {
        if ($View.Excluded[$i]) { continue }
        $uid = [string]$View.Nodes[$i].Uid
        if ([string]::IsNullOrWhiteSpace($uid) -or $seen.ContainsKey($uid)) { continue }
        $seen[$uid] = $true
        $uids.Add($uid)
    }
    return $uids.ToArray()
}

function Get-NightlyIdentityBagKey {
    param([string]$ClassName, [string]$TypeName, [string]$MethodName)
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyClassNames]::BagKey($ClassName, $TypeName, $MethodName) }
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

function Get-NightlyExpectedSkipRoster {
    param([string]$Path = '')
    $useDefault = [string]::IsNullOrWhiteSpace($Path)
    if ($useDefault -and $null -ne $script:NightlyExpectedSkipRosterCache) {
        return @($script:NightlyExpectedSkipRosterCache)
    }
    if ($useDefault) {
        $repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $Path = Join-Path $repo (Join-Path 'tests' 'expected-platform-skips.json')
    }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw ('expected-skip roster missing {0}' -f $Path)
    }
    $doc = [System.IO.File]::ReadAllText($Path) | ConvertFrom-Json
    if ($null -eq $doc -or $null -eq $doc.entries) { throw 'expected-skip roster has no entries' }
    $entries = @($doc.entries)
    foreach ($entry in $entries) {
        $platformName = [string]$entry.platform
        $test = [string]$entry.test
        $prefix = [string]$entry.reasonPrefix
        if ($platformName -cnotin @('windows', 'linux')) { throw ('expected-skip roster platform {0}' -f $platformName) }
        if ([string]::IsNullOrWhiteSpace($test) -or $test.IndexOf('.') -lt 1) { throw ('expected-skip roster test {0}' -f $test) }
        if ([string]::IsNullOrWhiteSpace($prefix)) { throw ('expected-skip roster reason {0}' -f $test) }
        [void](Get-NightlyExpectedSkipReasonClass -Entry $entry)
    }
    if ($useDefault) { $script:NightlyExpectedSkipRosterCache = $entries }
    return $entries
}

function Get-NightlyExpectedSkipReasonClass {
    param($Entry)
    $value = ''
    if ($null -ne $Entry -and $Entry.PSObject.Properties.Name -contains 'reasonClass') {
        $value = [string]$Entry.reasonClass
    }
    if ([string]::IsNullOrWhiteSpace($value)) { return 'platform' }
    if ($value -cne 'platform' -and $value -cne 'live-context') {
        throw ('expected-skip roster reasonClass {0}' -f $value)
    }
    return $value
}

function Get-NightlyExpectedSkipRosterDigest {
    param($Entries)
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        $className = Get-NightlyExpectedSkipReasonClass -Entry $entry
        $line = ([string]$entry.platform) + "`n" + ([string]$entry.test) + "`n" + $className + "`n" + ([string]$entry.reasonPrefix)
        [void]$lines.Add($line)
    }
    $sorted = $lines.ToArray()
    [Array]::Sort($sorted, [StringComparer]::Ordinal)
    $text = ''
    if ($sorted.Length -gt 0) { $text = [string]::Join("`n", $sorted) }
    return (Get-NightlySha256Text -Text $text)
}

function Get-NightlyExpectedSkipStaleWarnings {
    param([string]$Platform, $Entries, [string[]]$KnownTestIdentities = $null)
    if ($null -eq $KnownTestIdentities) { return @() }
    $knownTests = [System.Collections.Generic.Dictionary[string,bool]]::new([System.StringComparer]::Ordinal)
    foreach ($knownId in @($KnownTestIdentities)) {
        $knownText = [string]$knownId
        if (-not [string]::IsNullOrWhiteSpace($knownText)) { $knownTests[$knownText] = $true }
    }
    $warnings = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.platform -cne $Platform) { continue }
        $entryTest = [string]$entry.test
        if (-not $knownTests.ContainsKey($entryTest)) {
            [void]$warnings.Add('stale-roster:' + $entryTest)
        }
    }
    return $warnings.ToArray()
}

function Get-NightlyDiscoveryKnownIdentities {
    param($Nodes)
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($node in @($Nodes)) {
        if ($null -eq $node) { continue }
        $className = ''
        $method = ''
        if ($node.PSObject.Properties.Name -contains 'ClassName') { $className = [string]$node.ClassName }
        if ($node.PSObject.Properties.Name -contains 'Method') { $method = [string]$node.Method }
        if ([string]::IsNullOrWhiteSpace($className) -or [string]::IsNullOrWhiteSpace($method)) { continue }
        [void]$ids.Add($className + '.' + $method)
    }
    return $ids.ToArray()
}

function ConvertFrom-NightlyCsStringLiteral {
    param([string]$Raw)
    $sb = New-Object System.Text.StringBuilder
    $i = 0
    while ($i -lt $Raw.Length) {
        if ($Raw[$i] -eq '\' -and ($i + 1) -lt $Raw.Length) {
            $nch = $Raw[$i + 1]
            if ($nch -eq 'n') { [void]$sb.Append("`n") }
            elseif ($nch -eq 'r') { [void]$sb.Append("`r") }
            elseif ($nch -eq 't') { [void]$sb.Append("`t") }
            elseif ($nch -eq '"') { [void]$sb.Append('"') }
            elseif ($nch -eq '\') { [void]$sb.Append('\') }
            else { [void]$sb.Append($nch) }
            $i += 2
            continue
        }
        [void]$sb.Append($Raw[$i])
        $i++
    }
    return $sb.ToString()
}

function ConvertTo-NightlyCsStructuralMask {
    # Strings and comments keep their newlines and lose every other character, so braces inside them are not structural.
    param([string]$Text)
    $n = $Text.Length
    if ($n -eq 0) { return '' }
    $chars = $Text.ToCharArray()
    $i = 0
    while ($i -lt $n) {
        $c = $chars[$i]
        if ($c -eq '/' -and ($i + 1) -lt $n -and $chars[$i + 1] -eq '/') {
            while ($i -lt $n -and $chars[$i] -ne "`n") { $chars[$i] = ' '; $i++ }
            continue
        }
        if ($c -eq '/' -and ($i + 1) -lt $n -and $chars[$i + 1] -eq '*') {
            $chars[$i] = ' '; $i++
            if ($i -lt $n) { $chars[$i] = ' '; $i++ }
            while ($i -lt $n) {
                if ($chars[$i] -eq '*' -and ($i + 1) -lt $n -and $chars[$i + 1] -eq '/') {
                    $chars[$i] = ' '; $chars[$i + 1] = ' '; $i += 2
                    break
                }
                if ($chars[$i] -ne "`n") { $chars[$i] = ' ' }
                $i++
            }
            continue
        }
        if ($c -eq "'") {
            $chars[$i] = ' '; $i++
            while ($i -lt $n) {
                $q = $chars[$i]
                $chars[$i] = ' '
                $i++
                if ($q -eq '\') {
                    if ($i -lt $n) { $chars[$i] = ' '; $i++ }
                    continue
                }
                if ($q -eq "'" -or $q -eq "`n") { break }
            }
            continue
        }
        if ($c -eq '@' -and ($i + 1) -lt $n -and $chars[$i + 1] -eq '"') {
            $chars[$i] = ' '; $chars[$i + 1] = ' '; $i += 2
            while ($i -lt $n) {
                if ($chars[$i] -eq '"') {
                    if (($i + 1) -lt $n -and $chars[$i + 1] -eq '"') {
                        $chars[$i] = ' '; $chars[$i + 1] = ' '; $i += 2
                        continue
                    }
                    $chars[$i] = ' '; $i++
                    break
                }
                if ($chars[$i] -ne "`n") { $chars[$i] = ' ' }
                $i++
            }
            continue
        }
        $dollars = 0
        $k = $i
        if ($c -eq '$') {
            while ($k -lt $n -and $chars[$k] -eq '$') { $dollars++; $k++ }
        }
        if ($k -lt $n -and $chars[$k] -eq '"') {
            $qcount = 0
            while ($k -lt $n -and $chars[$k] -eq '"') { $qcount++; $k++ }
            if ($qcount -eq 2 -and $dollars -eq 0) {
                $chars[$i] = ' '; $chars[$i + 1] = ' '; $i += 2
                continue
            }
            if ($qcount -ge 3 -or $qcount -eq 1) {
                while ($i -lt $k) { $chars[$i] = ' '; $i++ }
                if ($qcount -ge 3) {
                    while ($i -lt $n) {
                        if ($chars[$i] -eq '"') {
                            $run = 0
                            $r = $i
                            while ($r -lt $n -and $chars[$r] -eq '"') { $run++; $r++ }
                            if ($run -ge $qcount) {
                                while ($i -lt $r) { $chars[$i] = ' '; $i++ }
                                break
                            }
                        }
                        if ($chars[$i] -ne "`n") { $chars[$i] = ' ' }
                        $i++
                    }
                    continue
                }
                while ($i -lt $n) {
                    $q = $chars[$i]
                    $chars[$i] = ' '
                    $i++
                    if ($q -eq '\') {
                        if ($i -lt $n) { $chars[$i] = ' '; $i++ }
                        continue
                    }
                    if ($q -eq '"' -or $q -eq "`n") { break }
                }
                continue
            }
        }
        $i++
    }
    return -join $chars
}

function Get-NightlyCsMatchEnd {
    param([string]$Mask, [int]$Open, [char]$OpenChar, [char]$CloseChar)
    $depth = 0
    $i = $Open
    $n = $Mask.Length
    while ($i -lt $n) {
        $ch = $Mask[$i]
        if ($ch -eq $OpenChar) { $depth++ }
        elseif ($ch -eq $CloseChar) {
            $depth--
            if ($depth -eq 0) { return $i }
        }
        $i++
    }
    return -1
}

function Initialize-NightlyCsMethodIndex {
    param([string]$RepoRoot)
    if ($script:NightlyCsMethodIndexRoot -eq $RepoRoot -and $null -ne $script:NightlyCsMethodIndex) {
        return $script:NightlyCsMethodIndex
    }
    $script:NightlyCsMethodIndexRoot = $RepoRoot
    $script:NightlyCsMethodIndex = @{
        ClassFiles = @{}
        Methods = @{}
        Parsed = @{}
        Mapped = $false
    }
    return $script:NightlyCsMethodIndex
}

function Update-NightlyCsClassFiles {
    param($Index, [string]$RepoRoot)
    if ($Index.Mapped) { return }
    $testsRoot = Join-Path $RepoRoot 'tests'
    $classRx = [regex]'\b(?:class|struct|record)\s+([A-Za-z_][A-Za-z0-9_]*)\b'
    if (Test-Path -LiteralPath $testsRoot -PathType Container) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($testsRoot)
        while ($pending.Count -gt 0) {
            $dir = $pending.Pop()
            $name = [System.IO.Path]::GetFileName($dir)
            if ($name -eq 'bin' -or $name -eq 'obj' -or $name.StartsWith('bin-', [System.StringComparison]::Ordinal)) { continue }
            foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) { $pending.Push($sub) }
            foreach ($file in [System.IO.Directory]::EnumerateFiles($dir, '*.cs')) {
                $text = [System.IO.File]::ReadAllText($file)
                foreach ($match in $classRx.Matches($text)) {
                    $className = $match.Groups[1].Value
                    if (-not $Index.ClassFiles.ContainsKey($className)) {
                        $Index.ClassFiles[$className] = New-Object System.Collections.Generic.List[string]
                    }
                    if (-not $Index.ClassFiles[$className].Contains($file)) {
                        [void]$Index.ClassFiles[$className].Add($file)
                    }
                }
            }
        }
    }
    $Index.Mapped = $true
}

function Add-NightlyCsMethodRecord {
    param($Index, $Record)
    $key = [string]$Record.Class + '.' + [string]$Record.Method
    if (-not $Index.Methods.ContainsKey($key)) {
        $Index.Methods[$key] = New-Object System.Collections.Generic.List[object]
    }
    [void]$Index.Methods[$key].Add($Record)
}

function Import-NightlyCsFileMethods {
    param($Index, [string]$Path)
    if ($Index.Parsed.ContainsKey($Path)) { return }
    $Index.Parsed[$Path] = $true
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    $text = [System.IO.File]::ReadAllText($Path)
    $mask = ConvertTo-NightlyCsStructuralMask -Text $text
    $n = $mask.Length
    $namespaces = New-Object System.Collections.Generic.List[object]
    $nsRx = [regex]'\bnamespace\s+([A-Za-z_][A-Za-z0-9_\.]*)'
    foreach ($match in $nsRx.Matches($mask)) {
        $nsName = $match.Groups[1].Value
        $j = $match.Index + $match.Length
        while ($j -lt $n -and [char]::IsWhiteSpace($mask[$j])) { $j++ }
        if ($j -ge $n) { continue }
        if ($mask[$j] -eq ';') {
            $start = $j
            $end = $n - 1
            foreach ($prior in $namespaces.ToArray()) {
                if ($prior.FileScoped -and $prior.End -ge $start) { $prior.End = $start - 1 }
            }
            [void]$namespaces.Add([pscustomobject]@{ Name = $nsName; Start = $start; End = $end; FileScoped = $true })
        } elseif ($mask[$j] -eq '{') {
            $end = Get-NightlyCsMatchEnd -Mask $mask -Open $j -OpenChar '{' -CloseChar '}'
            if ($end -lt 0) { $end = $n - 1 }
            [void]$namespaces.Add([pscustomobject]@{ Name = $nsName; Start = $j; End = $end; FileScoped = $false })
        }
    }
    $classes = New-Object System.Collections.Generic.List[object]
    $classRx = [regex]'\b(?:class|struct|record)\s+([A-Za-z_][A-Za-z0-9_]*)\b'
    foreach ($match in $classRx.Matches($mask)) {
        $className = $match.Groups[1].Value
        $j = $match.Index + $match.Length
        $paren = 0
        $angle = 0
        $bodyOpen = -1
        while ($j -lt $n) {
            $ch = $mask[$j]
            if ($ch -eq '(') { $paren++ }
            elseif ($ch -eq ')') { if ($paren -gt 0) { $paren-- } }
            elseif ($ch -eq '<') { $angle++ }
            elseif ($ch -eq '>') { if ($angle -gt 0) { $angle-- } }
            elseif ($ch -eq '{' -and $paren -eq 0 -and $angle -eq 0) { $bodyOpen = $j; break }
            elseif ($ch -eq ';' -and $paren -eq 0 -and $angle -eq 0) { break }
            $j++
        }
        if ($bodyOpen -lt 0) { continue }
        $bodyEnd = Get-NightlyCsMatchEnd -Mask $mask -Open $bodyOpen -OpenChar '{' -CloseChar '}'
        if ($bodyEnd -lt 0) { continue }
        $ns = ''
        $nsStart = -1
        foreach ($item in $namespaces.ToArray()) {
            if ($match.Index -ge $item.Start -and $match.Index -le $item.End -and $item.Start -ge $nsStart) {
                $ns = [string]$item.Name
                $nsStart = $item.Start
            }
        }
        [void]$classes.Add([pscustomobject]@{ Name = $className; Namespace = $ns; Open = $bodyOpen; End = $bodyEnd })
    }
    $mod = 'public|private|internal|protected|static|async|override|virtual|sealed|new|unsafe|partial|extern|required|file'
    $methodRx = [regex]::new('(?m)^[ \t]*(?:\[[^\]]*\][ \t\r\n]*)*(?:(?:' + $mod + ')[ \t]+)+', 'Compiled')
    foreach ($match in $methodRx.Matches($mask)) {
        $owner = $null
        $ownerOpen = -1
        foreach ($classInfo in $classes.ToArray()) {
            if ($match.Index -gt $classInfo.Open -and $match.Index -lt $classInfo.End -and $classInfo.Open -ge $ownerOpen) {
                $owner = $classInfo
                $ownerOpen = $classInfo.Open
            }
        }
        if ($null -eq $owner) { continue }
        $pos = $match.Index + $match.Length
        $limit = $pos + 8000
        if ($limit -gt $n) { $limit = $n }
        $parenAt = -1
        $sigParen = 0
        $sigAngle = 0
        while ($pos -lt $limit) {
            $ch = $mask[$pos]
            if ($ch -eq '(' -and $sigAngle -eq 0) { $parenAt = $pos; break }
            if ($ch -eq '<') { $sigAngle++ }
            elseif ($ch -eq '>') { if ($sigAngle -gt 0) { $sigAngle-- } }
            elseif (($ch -eq '{' -or $ch -eq ';' -or $ch -eq '=') -and $sigAngle -eq 0 -and $sigParen -eq 0) { break }
            $pos++
        }
        if ($parenAt -lt 0) { continue }
        $j = $parenAt - 1
        while ($j -ge 0 -and [char]::IsWhiteSpace($mask[$j])) { $j-- }
        if ($j -ge 0 -and $mask[$j] -eq '>') {
            $angle = 1
            $j--
            while ($j -ge 0 -and $angle -gt 0) {
                if ($mask[$j] -eq '>') { $angle++ }
                elseif ($mask[$j] -eq '<') { $angle-- }
                $j--
            }
            while ($j -ge 0 -and [char]::IsWhiteSpace($mask[$j])) { $j-- }
        }
        if ($j -lt 0) { continue }
        $nameEnd = $j
        while ($j -ge 0 -and ([char]::IsLetterOrDigit($mask[$j]) -or $mask[$j] -eq '_')) { $j-- }
        if ($nameEnd -le $j) { continue }
        $methodName = $mask.Substring($j + 1, $nameEnd - $j)
        if ($methodName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') { continue }
        $closeParen = Get-NightlyCsMatchEnd -Mask $mask -Open $parenAt -OpenChar '(' -CloseChar ')'
        if ($closeParen -lt 0) { continue }
        $k = $closeParen + 1
        while ($k -lt $n -and [char]::IsWhiteSpace($mask[$k])) { $k++ }
        $bodyStart = $k
        $bodyEnd = $k
        if ($k -lt $n -and $mask[$k] -eq '{') {
            $braceEnd = Get-NightlyCsMatchEnd -Mask $mask -Open $k -OpenChar '{' -CloseChar '}'
            if ($braceEnd -lt 0) { continue }
            $bodyEnd = $braceEnd + 1
        } elseif ($k + 1 -lt $n -and $mask[$k] -eq '=' -and $mask[$k + 1] -eq '>') {
            $bodyStart = $k
            $pDepth = 0
            $bDepth = 0
            while ($k -lt $n) {
                $ch = $mask[$k]
                if ($ch -eq '(') { $pDepth++ }
                elseif ($ch -eq ')') { if ($pDepth -gt 0) { $pDepth-- } }
                elseif ($ch -eq '{') { $bDepth++ }
                elseif ($ch -eq '}') { if ($bDepth -gt 0) { $bDepth-- } }
                elseif ($ch -eq ';' -and $pDepth -eq 0 -and $bDepth -eq 0) { $bodyEnd = $k + 1; break }
                $k++
            }
            if ($bodyEnd -le $bodyStart) { continue }
        } elseif ($k -lt $n -and $mask[$k] -eq ';') {
            $bodyEnd = $k + 1
        } else {
            continue
        }
        $sliceEnd = $bodyEnd
        if ($sliceEnd -gt $text.Length) { $sliceEnd = $text.Length }
        $record = [pscustomobject]@{
            Namespace = [string]$owner.Namespace
            Class = [string]$owner.Name
            Method = $methodName
            File = $Path
            Text = $text.Substring($match.Index, $sliceEnd - $match.Index)
            Body = $text.Substring($bodyStart, $sliceEnd - $bodyStart)
            BodyMask = $mask.Substring($bodyStart, $sliceEnd - $bodyStart)
        }
        Add-NightlyCsMethodRecord -Index $Index -Record $record
    }
}

function Get-NightlyCsMethods {
    param($Index, [string]$RepoRoot, [string]$ClassName, [string]$MethodName, [string]$Namespace = '')
    Update-NightlyCsClassFiles -Index $Index -RepoRoot $RepoRoot
    if ($Index.ClassFiles.ContainsKey($ClassName)) {
        foreach ($file in $Index.ClassFiles[$ClassName].ToArray()) {
            Import-NightlyCsFileMethods -Index $Index -Path ([string]$file)
        }
    }
    $key = $ClassName + '.' + $MethodName
    if (-not $Index.Methods.ContainsKey($key)) { return @() }
    $all = $Index.Methods[$key].ToArray()
    if ([string]::IsNullOrWhiteSpace($Namespace)) { return $all }
    $matched = @($all | Where-Object { $_.Namespace -ceq $Namespace })
    return $matched
}

function Get-NightlyCsSkipLiterals {
    param([string]$Text, [bool]$FoldRefusal)
    $found = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrEmpty($Text)) { return @() }
    $rx = [regex]'Skip\.Test\(\s*"((?:\\.|[^"\\])*)"|SkipTestException\(\s*"((?:\\.|[^"\\])*)"|NativeLinuxGate\.Require\(\s*"((?:\\.|[^"\\])*)"|NativeLinuxOnly\(\s*"((?:\\.|[^"\\])*)"|new\s+LiveClaudeRefusal\(\s*"((?:\\.|[^"\\])*)"'
    foreach ($match in $rx.Matches($Text)) {
        $group = 0
        if ($match.Groups[1].Success) { $group = 1 }
        elseif ($match.Groups[2].Success) { $group = 2 }
        elseif ($match.Groups[3].Success) { $group = 3 }
        elseif ($match.Groups[4].Success) { $group = 4 }
        elseif ($match.Groups[5].Success) { $group = 5 }
        if ($group -eq 0) { continue }
        $literal = ConvertFrom-NightlyCsStringLiteral -Raw $match.Groups[$group].Value
        if ($group -eq 5) {
            if ($FoldRefusal) { [void]$found.Add('20 ' + $literal) }
        } else {
            [void]$found.Add($literal)
        }
    }
    return $found.ToArray()
}

function Test-NightlyLiveClaudeRefusalFolds {
    param([string]$RepoRoot)
    if ($null -ne $script:NightlyLiveClaudeRefusalFolds -and $script:NightlyLiveClaudeRefusalRoot -eq $RepoRoot) {
        return [bool]$script:NightlyLiveClaudeRefusalFolds
    }
    $path = Join-Path $RepoRoot (Join-Path 'tests' (Join-Path 'Shared' (Join-Path 'LiveClaude' 'LiveClaudeLaunchContext.cs')))
    $folds = $false
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $source = [System.IO.File]::ReadAllText($path)
        if ($source.Contains('"20 " + reason')) { $folds = $true }
    }
    $script:NightlyLiveClaudeRefusalRoot = $RepoRoot
    $script:NightlyLiveClaudeRefusalFolds = $folds
    return $folds
}

function Get-NightlyCsCalls {
    param([string]$BodyMask)
    $calls = New-Object System.Collections.Generic.List[object]
    if ([string]::IsNullOrEmpty($BodyMask)) { return @() }
    $keywords = @{
        if = $true; for = $true; while = $true; switch = $true; catch = $true; using = $true
        lock = $true; return = $true; new = $true; throw = $true; foreach = $true; sizeof = $true
        typeof = $true; nameof = $true; when = $true; fixed = $true; checked = $true; unchecked = $true
    }
    $n = $BodyMask.Length
    for ($i = 0; $i -lt $n; $i++) {
        if ($BodyMask[$i] -ne '(') { continue }
        $j = $i - 1
        while ($j -ge 0 -and [char]::IsWhiteSpace($BodyMask[$j])) { $j-- }
        if ($j -ge 0 -and $BodyMask[$j] -eq '>') {
            $depth = 1
            $j--
            while ($j -ge 0 -and $depth -gt 0) {
                if ($BodyMask[$j] -eq '>') { $depth++ }
                elseif ($BodyMask[$j] -eq '<') { $depth-- }
                $j--
            }
            while ($j -ge 0 -and [char]::IsWhiteSpace($BodyMask[$j])) { $j-- }
        }
        if ($j -lt 0 -or -not ([char]::IsLetterOrDigit($BodyMask[$j]) -or $BodyMask[$j] -eq '_')) { continue }
        $end = $j
        while ($j -ge 0 -and ([char]::IsLetterOrDigit($BodyMask[$j]) -or $BodyMask[$j] -eq '_')) { $j-- }
        $name = $BodyMask.Substring($j + 1, $end - $j)
        if ($keywords.ContainsKey($name)) { continue }
        $qual = ''
        $k = $j
        while ($k -ge 0 -and [char]::IsWhiteSpace($BodyMask[$k])) { $k-- }
        if ($k -ge 0 -and $BodyMask[$k] -eq '.') {
            $k--
            while ($k -ge 0 -and [char]::IsWhiteSpace($BodyMask[$k])) { $k-- }
            if ($k -ge 0 -and ([char]::IsLetterOrDigit($BodyMask[$k]) -or $BodyMask[$k] -eq '_')) {
                $qEnd = $k
                while ($k -ge 0 -and ([char]::IsLetterOrDigit($BodyMask[$k]) -or $BodyMask[$k] -eq '_')) { $k-- }
                $qual = $BodyMask.Substring($k + 1, $qEnd - $k)
            }
        }
        if ($qual -eq 'this') { $qual = '' }
        [void]$calls.Add([pscustomobject]@{ Name = $name; Qualifier = $qual })
    }
    return $calls.ToArray()
}

function Get-NightlyReachableSkipTexts {
    param([string]$RepoRoot, [string]$Identity)
    $index = Initialize-NightlyCsMethodIndex -RepoRoot $RepoRoot
    $methodDot = $Identity.LastIndexOf('.')
    if ($methodDot -lt 1) { return @() }
    $method = $Identity.Substring($methodDot + 1)
    $head = $Identity.Substring(0, $methodDot)
    $classDot = $head.LastIndexOf('.')
    if ($classDot -lt 1) { return @() }
    $className = $head.Substring($classDot + 1)
    $namespace = $head.Substring(0, $classDot)
    $starts = @(Get-NightlyCsMethods -Index $index -RepoRoot $RepoRoot -ClassName $className -MethodName $method -Namespace $namespace)
    $fold = Test-NightlyLiveClaudeRefusalFolds -RepoRoot $RepoRoot
    $texts = New-Object System.Collections.Generic.List[string]
    $visited = @{}
    $queue = New-Object System.Collections.Generic.Queue[object]
    foreach ($start in @($starts)) {
        if ($null -eq $start) { continue }
        $queue.Enqueue([pscustomobject]@{ Method = $start; Depth = 0 })
    }
    # Same-class helpers and gate calls (Require*, Resolve, CreateAsync) keep a separate
    # budget so a long test body cannot crowd out the skip chain. Other calls stay at 12.
    $followedOther = 0
    $followedSame = 0
    $followedGate = 0
    while ($queue.Count -gt 0) {
        $item = $queue.Dequeue()
        $current = $item.Method
        if ($null -eq $current) { continue }
        $visit = [string]$current.File + '|' + [string]$current.Namespace + '|' + [string]$current.Class + '|' + [string]$current.Method + '|' + [string]$current.Text.Length
        if ($visited.ContainsKey($visit)) { continue }
        $visited[$visit] = $true
        foreach ($literal in @(Get-NightlyCsSkipLiterals -Text ([string]$current.Text) -FoldRefusal $fold)) {
            [void]$texts.Add([string]$literal)
        }
        if ([int]$item.Depth -ge 3) { continue }
        foreach ($call in @(Get-NightlyCsCalls -BodyMask ([string]$current.BodyMask))) {
            $callName = [string]$call.Name
            $sameClass = [string]::IsNullOrWhiteSpace([string]$call.Qualifier)
            $gate = $false
            if ($callName -ceq 'Resolve' -or $callName -ceq 'CreateAsync' -or $callName.StartsWith('Require')) { $gate = $true }
            if ($sameClass) {
                if ($followedSame -ge 24) { continue }
            } elseif ($gate) {
                if ($followedGate -ge 12) { continue }
            } else {
                if ($followedOther -ge 12) { continue }
            }
            $targets = @()
            if ($sameClass) {
                $targets = @(Get-NightlyCsMethods -Index $index -RepoRoot $RepoRoot -ClassName ([string]$current.Class) -MethodName $callName -Namespace ([string]$current.Namespace))
            } else {
                $targets = @(Get-NightlyCsMethods -Index $index -RepoRoot $RepoRoot -ClassName ([string]$call.Qualifier) -MethodName $callName -Namespace '')
            }
            foreach ($target in @($targets)) {
                if ($null -eq $target) { continue }
                if ($sameClass) {
                    if ($followedSame -ge 24) { break }
                    $followedSame++
                } elseif ($gate) {
                    if ($followedGate -ge 12) { break }
                    $followedGate++
                } else {
                    if ($followedOther -ge 12) { break }
                    $followedOther++
                }
                $queue.Enqueue([pscustomobject]@{ Method = $target; Depth = ([int]$item.Depth + 1) })
            }
        }
    }
    return $texts.ToArray()
}

function Get-NightlyExpectedSkipReasonGaps {
    # The named method's reachable skip text must equal reasonPrefix. Existence stays on Get-NightlyExpectedSkipSourceGaps.
    param([string]$RepoRoot, [string]$Platform, $Entries = $null)
    if ($null -eq $Entries) { $Entries = @(Get-NightlyExpectedSkipRoster) }
    $gaps = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.platform -cne $Platform) { continue }
        $test = [string]$entry.test
        $prefix = [string]$entry.reasonPrefix
        $texts = @(Get-NightlyReachableSkipTexts -RepoRoot $RepoRoot -Identity $test)
        $found = $false
        foreach ($candidate in @($texts)) {
            if ([string]$candidate -ceq $prefix) { $found = $true; break }
        }
        if (-not $found) { [void]$gaps.Add($test) }
    }
    return $gaps.ToArray()
}

function Get-NightlyExpectedSkipRosterDrift {
    param([string]$RepoRoot, $Entries)
    $path = Join-Path $RepoRoot (Join-Path 'tests' (Join-Path 'Antiphon.Tests' (Join-Path 'Scripts' 'WindowsUnitSkipRoster.cs')))
    $reasonsById = @{}
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        $text = [System.IO.File]::ReadAllText($path)
        $constants = @{}
        $constRx = [regex]'const string ([A-Za-z_][A-Za-z0-9_]*) = "((?:\\.|[^"\\])*)"'
        foreach ($match in $constRx.Matches($text)) {
            $constants[$match.Groups[1].Value] = ConvertFrom-NightlyCsStringLiteral -Raw $match.Groups[2].Value
        }
        $newRx = [regex]'new\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*-?\d+\s*,\s*(?:([A-Za-z_][A-Za-z0-9_]*)|"((?:\\.|[^"\\])*)")'
        foreach ($match in $newRx.Matches($text)) {
            $id = $match.Groups[1].Value + '.' + $match.Groups[2].Value + '.' + $match.Groups[3].Value
            $reason = ''
            if ($match.Groups[4].Success -and -not [string]::IsNullOrWhiteSpace($match.Groups[4].Value)) {
                $token = $match.Groups[4].Value
                if ($constants.ContainsKey($token)) { $reason = [string]$constants[$token] }
            } elseif ($match.Groups[5].Success) {
                $reason = ConvertFrom-NightlyCsStringLiteral -Raw $match.Groups[5].Value
            }
            if (-not $reasonsById.ContainsKey($id)) {
                $reasonsById[$id] = New-Object System.Collections.Generic.List[string]
            }
            [void]$reasonsById[$id].Add($reason)
        }
    }
    $overlap = New-Object System.Collections.Generic.List[string]
    $mismatches = New-Object System.Collections.Generic.List[string]
    $seen = @{}
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        $id = [string]$entry.test
        if ([string]::IsNullOrWhiteSpace($id) -or $seen.ContainsKey($id)) { continue }
        if (-not $reasonsById.ContainsKey($id)) { continue }
        $seen[$id] = $true
        [void]$overlap.Add($id)
        $prefix = [string]$entry.reasonPrefix
        $hit = $false
        foreach ($reason in $reasonsById[$id].ToArray()) {
            if ([string]$reason -ceq $prefix) { $hit = $true; break }
        }
        if (-not $hit) { [void]$mismatches.Add($id) }
    }
    return [pscustomobject]@{
        Overlap = $overlap.ToArray()
        Mismatches = $mismatches.ToArray()
    }
}

function Get-NightlyExpectedSkipSourceGaps {
    # A roster test whose class or method is absent from tests/. Not a coverage failure.
    param([string]$RepoRoot, [string]$Platform, $Entries = $null)
    if ($null -eq $Entries) { $Entries = @(Get-NightlyExpectedSkipRoster) }
    $wanted = @()
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.platform -cne $Platform) { continue }
        $wanted += $entry
    }
    if ($wanted.Count -eq 0) { return @() }
    $testsRoot = Join-Path $RepoRoot 'tests'
    $byClass = @{}
    foreach ($entry in $wanted) {
        $test = [string]$entry.test
        $methodDot = $test.LastIndexOf('.')
        if ($methodDot -lt 1) { continue }
        $method = $test.Substring($methodDot + 1)
        $head = $test.Substring(0, $methodDot)
        $classDot = $head.LastIndexOf('.')
        $className = if ($classDot -lt 0) { $head } else { $head.Substring($classDot + 1) }
        if (-not $byClass.ContainsKey($className)) {
            $byClass[$className] = @{ methods = @{}; seenClass = $false }
        }
        $byClass[$className].methods[$method] = $false
    }
    if (Test-Path -LiteralPath $testsRoot -PathType Container) {
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($testsRoot)
        while ($pending.Count -gt 0) {
            $dir = $pending.Pop()
            $name = [System.IO.Path]::GetFileName($dir)
            if ($name -eq 'bin' -or $name -eq 'obj' -or $name.StartsWith('bin-', [System.StringComparison]::Ordinal)) { continue }
            foreach ($sub in [System.IO.Directory]::EnumerateDirectories($dir)) { $pending.Push($sub) }
            foreach ($file in [System.IO.Directory]::EnumerateFiles($dir, '*.cs')) {
                $text = [System.IO.File]::ReadAllText($file)
                foreach ($className in @($byClass.Keys)) {
                    $bucket = $byClass[$className]
                    # Both patterns hold the name literally (case-sensitive), so a file without it cannot match:
                    # the ordinal search only skips files the regex would reject.
                    if ($text.IndexOf($className, [System.StringComparison]::Ordinal) -lt 0) { continue }
                    if (-not [regex]::IsMatch($text, ('\bclass\s+' + [regex]::Escape($className) + '\b'))) { continue }
                    $bucket.seenClass = $true
                    foreach ($method in @($bucket.methods.Keys)) {
                        if ($bucket.methods[$method]) { continue }
                        if ([regex]::IsMatch($text, ('\b' + [regex]::Escape($method) + '\s*\('))) {
                            $bucket.methods[$method] = $true
                        }
                    }
                }
            }
        }
    }
    $gaps = @()
    foreach ($entry in $wanted) {
        $test = [string]$entry.test
        $methodDot = $test.LastIndexOf('.')
        if ($methodDot -lt 1) { $gaps += $test; continue }
        $method = $test.Substring($methodDot + 1)
        $head = $test.Substring(0, $methodDot)
        $classDot = $head.LastIndexOf('.')
        $className = if ($classDot -lt 0) { $head } else { $head.Substring($classDot + 1) }
        $bucket = $byClass[$className]
        if ($null -eq $bucket -or -not $bucket.seenClass -or -not $bucket.methods[$method]) { $gaps += $test }
    }
    return $gaps
}

function Find-NightlyExpectedSkipEntry {
    param($Row, [string]$Platform, [object[]]$Entries)
    if ($null -eq $Row -or [string]::IsNullOrWhiteSpace($Platform)) { return $null }
    if ((ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$Row.Outcome)) -cne 'NotExecuted') { return $null }
    $trace = ''
    if ($Row.PSObject.Properties.Name -contains 'DebugTrace') { $trace = [string]$Row.DebugTrace }
    $identity = [string]$Row.Identity
    foreach ($entry in @($Entries)) {
        if ($null -eq $entry) { continue }
        if ([string]$entry.test -cne $identity) { continue }
        $reasonClass = Get-NightlyExpectedSkipReasonClass -Entry $entry
        $entryPlatform = [string]$entry.platform
        if ($entryPlatform -cne 'windows' -and $entryPlatform -cne 'linux') { continue }
        if ($reasonClass -ceq 'platform' -and $entryPlatform -cne $Platform) { continue }
        $expected = 'Skipped: ' + [string]$entry.reasonPrefix
        if ($trace -ceq $expected) { return $entry }
    }
    return $null
}

function Test-NightlyRowIsExpectedSkip {
    param($Row, [string]$Platform, [object[]]$Entries)
    return ($null -ne (Find-NightlyExpectedSkipEntry -Row $Row -Platform $Platform -Entries $Entries))
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
        [bool]$UsedDiscoveryAsExecution = $false,
        [string]$Platform = '',
        $ExpectedSkipRoster = $null,
        [string[]]$KnownTestIdentities = $null
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
    $passedCount = $null
    if (Test-NightlyCoreAvailable) {
        # One compiled pass classifies every row once (the filters below normalise each row five times).
        $classified = [Antiphon.Nightly.NightlyVerdicts]::ClassifyOutcomes($TerminalRows)
        $failed = @($classified.Failed); $skipped = @($classified.Skipped)
        $unexecuted = @($classified.NotExecuted); $unknown = @($classified.Unknown)
        $passedCount = [int]$classified.Passed
    } else {
    $failed = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Failed' })
    $skipped = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Skipped' })
    $unexecuted = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'NotExecuted' })
    $unknown = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Unknown' })
    }
    # Expected platform skips stay out of the unexecuted bucket. Outcome=Skipped is unchanged.
    $rosterEntries = @()
    if ($null -ne $ExpectedSkipRoster) { $rosterEntries = @($ExpectedSkipRoster) }
    elseif (-not [string]::IsNullOrWhiteSpace($Platform)) { $rosterEntries = @(Get-NightlyExpectedSkipRoster) }
    $expectedSkipRows = New-Object System.Collections.Generic.List[object]
    $stillUnexecuted = New-Object System.Collections.Generic.List[object]
    $toleratedSkips = New-Object System.Collections.Generic.List[object]
    foreach ($row in @($unexecuted)) {
        $matchedEntry = Find-NightlyExpectedSkipEntry -Row $row -Platform $Platform -Entries $rosterEntries
        if ($null -ne $matchedEntry) {
            [void]$expectedSkipRows.Add($row)
            [void]$toleratedSkips.Add([pscustomobject]@{
                identity = [string]$row.Identity
                reasonClass = (Get-NightlyExpectedSkipReasonClass -Entry $matchedEntry)
            })
        } else {
            [void]$stillUnexecuted.Add($row)
        }
    }
    $unexecuted = $stillUnexecuted.ToArray()
    $presentRows = @($TerminalRows | Where-Object { $null -ne $_ })
    $allExpectedSkips = ($presentRows.Count -gt 0 -and $expectedSkipRows.Count -eq $presentRows.Count)
    $processExitTolerated = $false
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
        if ($ProcessExit -eq 8 -and $allExpectedSkips) { $processExitTolerated = $true }
        else {
            $testsPassed = $false
            $reasons += ('process-exit-{0}' -f $ProcessExit)
        }
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
    if ($null -eq $passedCount) {
        $passedCount = @($TerminalRows | Where-Object { (ConvertTo-NightlyNormalizedOutcome -Outcome ([string]$_.Outcome)) -eq 'Passed' }).Count
    }
    $skipWarnings = @(Get-NightlyExpectedSkipStaleWarnings -Platform $Platform -Entries $rosterEntries -KnownTestIdentities $KnownTestIdentities)
    $rosterDigest = Get-NightlyExpectedSkipRosterDigest -Entries $rosterEntries
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
        expectedSkips = $expectedSkipRows.Count
        warnings = @($skipWarnings)
        processExitTolerated = [bool]$processExitTolerated
        toleratedSkips = @($toleratedSkips.ToArray())
        rosterDigest = [string]$rosterDigest
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
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyVerdicts]::ExpandedRowsPresent($TerminalRows, $RequiredUids) }
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
    $missing = [Collections.Generic.List[object]]::new()
    foreach ($uid in @($RequiredUids)) {
        $key = [string]$uid
        if ([string]::IsNullOrWhiteSpace($key)) { continue }
        if (-not $terminalUids.ContainsKey($key)) { $missing.Add($key) }
    }
    return [pscustomobject]@{ Ok = ($missing.Count -eq 0); Missing = $missing.ToArray() }
}

function Test-NightlyTrxDiagnosticCrossCheck {
    param([object[]]$TrxRows, [object[]]$TerminalNodes, [string[]]$RequiredClasses = @())
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyVerdicts]::CrossCheck($TrxRows, $TerminalNodes, $RequiredClasses) }
    $trxBag = @{}
    $diagBag = @{}
    $extra = @()
    $assignment = New-NightlyClassAssignmentCache -RequiredClasses $RequiredClasses
    foreach ($r in @($TrxRows)) {
        $assigned = Test-NightlyClassAssignedCached -Cache $assignment -ClassName ([string]$r.ClassName) -TypeName ''
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
        if (-not (Test-NightlyClassAssignedCached -Cache $assignment -ClassName ([string]$n.ClassName) -TypeName ([string]$n.Type))) {
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
    if (Test-NightlyCoreAvailable) { return [Antiphon.Nightly.NightlyVerdicts]::RequiredClassesPresent($TerminalRows, $RequiredClasses) }
    $present = @{}
    foreach ($r in @($TerminalRows)) {
        $cn = [string]$r.ClassName
        if ([string]::IsNullOrWhiteSpace($cn)) { continue }
        $present[$cn] = $true
        $present[(Get-NightlyBareClassIdentity -Name $cn)] = $true
    }
    $missing = @()
    foreach ($c in @($RequiredClasses)) {
        $name = [string]$c
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $wantBare = Get-NightlyBareClassIdentity -Name $name
        if (-not $present.ContainsKey($name) -and -not $present.ContainsKey($wantBare)) {
            $missing += $name
        }
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
        [string[]]$RequiredClasses = @(),
        $ProfileExclusions = $null,
        [switch]$ProfileAware,
        $DiscoveryIndex = $null,
        [string]$Platform = '',
        $ExpectedSkipRoster = $null
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
    if (Get-Command Get-TestPhasePathIdentity -ErrorAction SilentlyContinue) {
        $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
        if ($trxIdentity.Kind -ne 'regular') {
            return [pscustomobject]@{
                coverageComplete = $false
                testsPassed = $false
                reasons = @('refused-trx')
                rows = @()
            }
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
    $discoveryEntry = $null
    $discoveryView = $null
    $discoveryPresent = $false
    if ([string]::IsNullOrWhiteSpace($DiscoveryPath) -or -not (Test-Path -LiteralPath $DiscoveryPath)) {
        $reasons += 'missing-discovery'
    } else {
        try {
            if ($null -eq $DiscoveryIndex) { $DiscoveryIndex = New-NightlyDiscoveryIndex }
            $discoveryEntry = Get-NightlyDiscoveryIndexEntry -Index $DiscoveryIndex -Path $DiscoveryPath
            $discoveryNodes = @($discoveryEntry.Document.Nodes)
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
    # The coverage verdict reads discovery only for its InProgress nodes.
    $inProgressNodes = @()
    if ($discoveryEntry) { $inProgressNodes = @($discoveryEntry.InProgress) }
    $verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows $rows -ProcessExit $ProcessExit `
        -Sha $Sha -ExpectedSha $ExpectedSha -GitRef $GitRef -ExpectedRef $ExpectedRef `
        -PolicyHash $PolicyHash -ExpectedPolicyHash $ExpectedPolicyHash `
        -DiscoveryNodes $inProgressNodes -UsedDiscoveryAsExecution $false `
        -Platform $Platform -ExpectedSkipRoster $ExpectedSkipRoster
    $coverageComplete = [bool]$verdict.coverageComplete
    $testsPassed = [bool]$verdict.testsPassed
    foreach ($r in @($verdict.reasons)) { $reasons += $r }
    if (-not $discoveryPresent) {
        $coverageComplete = $false
    } else {
        $discoveryView = Get-NightlyDiscoveryIndexView -Entry $discoveryEntry -ProfileExclusions $ProfileExclusions -ProfileAware:$ProfileAware
        $requiredUids = @(Get-NightlyDiscoveryIndexRequiredUids -View $discoveryView -RequiredClasses $RequiredClasses)
        $censusCheck = $discoveryView.Census
        if ($censusCheck.Duplicates.Count -gt 0) {
            $coverageComplete = $false
            $reasons += ('duplicate-discovery-uid {0}' -f ($censusCheck.Duplicates -join ','))
        }
        if ($ProfileAware -and $censusCheck.RequiredCount -eq 0) {
            # D-5: a profile lane never reports green on an empty required set.
            $coverageComplete = $false
            $testsPassed = $false
            $reasons += 'zero-required-uid'
        }
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
            $classReq = @($discoveryView.Classes)
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
    $censusOut = $null
    if ($discoveryPresent) {
        $censusOut = $discoveryView.Census
    }
    return [pscustomobject]@{
        coverageComplete = $coverageComplete
        testsPassed = $testsPassed
        reasons = $reasons
        rows = $rows
        terminalNodes = $terminalNodes
        discoveryNodes = $discoveryNodes
        census = $censusOut
        processExitTolerated = [bool]$verdict.processExitTolerated
        warnings = @($verdict.warnings)
        toleratedSkips = @($verdict.toleratedSkips)
        rosterDigest = [string]$verdict.rosterDigest
    }
}

# CARD-1313: exact membership is a multiset contract over expanded UIDs.
function Test-NightlyExactCoverage {
    param([string[]]$RequiredUids, [object[]]$TerminalNodes)
    $required = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $reasons = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    foreach ($uid in $RequiredUids) {
        if (-not $uid -or -not $required.Add($uid)) { [void]$reasons.Add('invalid-required') }
    }
    if ($required.Count -eq 0 -or @($TerminalNodes).Count -eq 0) { [void]$reasons.Add('zero-required') }
    foreach ($node in $TerminalNodes) {
        $uid = [string]$node.Uid
        if (-not $seen.Add($uid)) { [void]$reasons.Add('duplicate-uid') }
        if (-not $required.Contains($uid)) { [void]$reasons.Add('extra-uid') }
        $outcome = ConvertTo-NightlyNormalizedOutcome ([string]$node.Outcome)
        if ($outcome -eq 'Skipped') { [void]$reasons.Add('required-skip') }
        elseif ($outcome -ne 'Passed') { [void]$reasons.Add('failed-outcome') }
    }
    foreach ($uid in $required) { if (-not $seen.Contains($uid)) { [void]$reasons.Add('missing-uid') } }
    return [pscustomobject]@{ Ok = ($reasons.Count -eq 0); Reasons = @($reasons); Required = $required.Count; Executed = @($TerminalNodes).Count }
}

function New-NightlyExactRoster {
    param($Discovery, [object[]]$Members, [string[]]$ClassifiedUids = $null)
    $parsed = ConvertFrom-NightlyDiscoveryDocument $Discovery $script:NightlyDiscoveryFormat
    if (@($parsed.Nodes).Count -eq 0) { throw 'empty-discovery' }
    $selected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($member in $Members) {
        $class = Get-NightlyBareClassIdentity -Name ([string]$member.class)
        $method = [string]$member.method
        if ($class -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$' -or ($method -and $method -notmatch '^[A-Za-z_][A-Za-z0-9_]*$')) { throw 'unresolvable-exact-member' }
        $matches = @($parsed.Nodes | Where-Object { (Get-NightlyBareClassIdentity -Name ([string]$_.ClassName)) -ceq $class -and (-not $method -or $_.Method -ceq $method) })
        if ($matches.Count -eq 0) { throw ('missing-exact-member:' + $class + '.' + $method) }
        foreach ($node in $matches) { [void]$selected.Add($node.Uid) }
    }
    $unresolved = @()
    if ($null -ne $ClassifiedUids) {
        $known = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($uid in $ClassifiedUids) { [void]$known.Add($uid) }
        $unresolved = @($parsed.Nodes | Where-Object { -not $known.Contains($_.Uid) } | ForEach-Object { $_.Uid })
        if ($unresolved.Count -gt 0) {
            # Widen the owning assembly before launch. Unknown resources cannot be run unattended.
            foreach ($node in $parsed.Nodes) { [void]$selected.Add($node.Uid) }
        }
    }
    if ($selected.Count -eq 0) { throw 'zero-required' }
    $groups = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($node in $parsed.Nodes) {
        if (-not $selected.Contains($node.Uid)) { continue }
        $bareType = Get-NightlyBareClassIdentity -Name ([string]$node.Type)
        foreach ($part in @($node.Namespace, $bareType, $node.Method)) {
            if ($part -notmatch '^[A-Za-z_][A-Za-z0-9_.]*$') { throw 'unresolvable-filter-identity' }
        }
        $filter = '/*/{0}/{1}/{2}' -f $node.Namespace, $bareType, $node.Method
        if (-not $groups.ContainsKey($filter)) { $groups.Add($filter, [Collections.Generic.List[string]]::new()) }
        $groups[$filter].Add($node.Uid)
    }
    $chunks = @()
    foreach ($filter in $groups.Keys) {
        $uids = [string[]]$groups[$filter].ToArray(); [Array]::Sort($uids, [StringComparer]::Ordinal)
        $chunks += [pscustomobject]@{ Filter = $filter; RequiredUids = $uids }
    }
    return [pscustomobject]@{ AssemblyHash = $parsed.AssemblyHash; Chunks = $chunks; RequiredUids = @($selected); Widened = ($unresolved.Count -gt 0); UnresolvedUids = $unresolved }
}
