#requires -Version 5.1
# CARD-1514: opt-in phase timing sidecars. ASCII-only. Unset phase root is a no-op.
# TRX stays authoritative. This file never edits TRX, LogRoot summary.json, receipts,
# selection, the cost cache, or the release manifest.

# Visibility, not the script flag. A function-scoped dot-source used to set the
# flag and drop Resolve-QualificationTimingRoot when that call returned, and the
# next lock decision then failed with command-not-found. The probe is a function
# only this file defines: the path helpers (test-phase-paths.ps1) load on their
# own in processes that need nothing else from here.
if ($script:TestPhaseEvidenceLoading) { return }
$script:TestPhaseEvidenceResolver = $null
try { $script:TestPhaseEvidenceResolver = Get-Command Invoke-TestPhaseRunSummary -ErrorAction SilentlyContinue } catch { $script:TestPhaseEvidenceResolver = $null }
if ($script:TestPhaseEvidenceResolver) {
    $script:TestPhaseEvidenceLoaded = $true
    return
}
$script:TestPhaseEvidenceLoading = $true

if (-not (Get-Command Write-NightlyAtomicJson -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'nightly-common.ps1')
}
. (Join-Path $PSScriptRoot 'nightly-core.ps1')

$script:TestPhaseVariableNames = @(
    'ANTIPHON_TEST_PHASE_ROOT',
    'ANTIPHON_TEST_PHASE_RUN_ID',
    'ANTIPHON_TEST_PHASE_GROUP_ID',
    'ANTIPHON_TEST_PHASE_ATTEMPT',
    'ANTIPHON_TEST_PHASE_SUITE'
)

function Remove-TestPhaseVariables {
    param($Environment)
    if ($null -eq $Environment) { return }
    foreach ($name in $script:TestPhaseVariableNames) {
        if ($Environment -is [System.Collections.IDictionary]) {
            [void]$Environment.Remove($name)
        }
    }
}

function Enable-TestPhaseExecutionEnvironment {
    param(
        [hashtable]$Environment,
        [string]$LogRoot,
        [string]$RunId,
        [string]$GroupId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Profile,
        [string]$TimingRoot = ''
    )
    if ($null -eq $Environment) { $Environment = @{} }
    $explicit = [Environment]::GetEnvironmentVariable('ANTIPHON_TEST_PHASE_ROOT')
    $qualification = ($Profile -eq 'rc') -or ($Profile -eq 'nightly')
    $enable = $qualification -or (-not [string]::IsNullOrWhiteSpace($explicit)) -or (-not [string]::IsNullOrWhiteSpace($TimingRoot))
    if (-not $enable) {
        Remove-TestPhaseVariables -Environment $Environment
        return $Environment
    }
    $root = $TimingRoot
    if ([string]::IsNullOrWhiteSpace($root)) { $root = $explicit }
    if ([string]::IsNullOrWhiteSpace($root)) {
        $root = Join-Path $LogRoot 'timing'
    }
    $Environment['ANTIPHON_TEST_PHASE_ROOT'] = [string]$root
    $Environment['ANTIPHON_TEST_PHASE_RUN_ID'] = [string]$RunId
    $Environment['ANTIPHON_TEST_PHASE_GROUP_ID'] = [string]$GroupId
    $Environment['ANTIPHON_TEST_PHASE_ATTEMPT'] = [string]$Attempt
    $Environment['ANTIPHON_TEST_PHASE_SUITE'] = [string]$Suite
    return $Environment
}

function ConvertTo-TestPhaseMarkdownCell {
    param($Value)
    $text = ''
    if ($null -ne $Value) { $text = [string]$Value }
    $text = $text.Replace('\', '\\')
    $text = $text.Replace('|', '\|')
    $text = $text.Replace("`r", ' ')
    $text = $text.Replace("`n", ' ')
    return $text
}

function Write-TestPhaseTextAtomic {
    # During a nightly run every write is queued on the evidence file system (nightly-core.ps1). -Deferred is
    # kept for callers written before that.
    param([string]$Path, [string]$Text, [switch]$Deferred)
    if (Submit-NightlyDeferredEvidenceWrite $Path $Text $true) { return }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -eq 'unsupported' -or $identity.Kind -eq 'unreadable') { return }
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-NightlyPathExists $dir)) {
        [void][System.IO.Directory]::CreateDirectory($dir)
    }
    $tmp = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($tmp, $Text, $utf8)
    if ($identity.Kind -eq 'regular') {
        $bak = $Path + '.bak'
        [System.IO.File]::Replace($tmp, $Path, $bak)
        Remove-NightlyFileQuietly $bak
    } else {
        [System.IO.File]::Move($tmp, $Path)
    }
}

function ConvertTo-TestPhaseRelativePath {
    param([string]$LogRoot, [string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or [string]::IsNullOrWhiteSpace($LogRoot)) { return '' }
    $root = [System.IO.Path]::GetFullPath($LogRoot).TrimEnd('\', '/')
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return '' }
    $rel = $full.Substring($root.Length).TrimStart('\', '/')
    return ($rel.Replace('\', '/'))
}

function Get-TestPhaseClassBasisMap {
    $map = @{}
    $path = [string]$script:NightlyClassCostPath
    if ([string]::IsNullOrWhiteSpace($path)) {
        $path = Join-Path $PSScriptRoot 'nightly-class-costs.json'
    }
    if (-not (Test-Path -LiteralPath $path)) { return $map }
    # Parsed once per process and file content (Antiphon.Nightly/ClassCostCache.cs, shared with
    # Get-NightlyClassCostTable); every call returns its own copy.
    $entry = $null
    $full = ''
    if ($script:NightlyCoreAvailable -or (Test-NightlyCoreAvailable)) {
        $full = [System.IO.Path]::GetFullPath($path)
        $entry = [Antiphon.Nightly.ClassCostCache]::Find($full)
    }
    if ($null -ne $entry -and $null -ne $entry.Basis) { return $entry.Basis.Clone() }
    if ($null -ne $entry) { $doc = $entry.Document }
    elseif ($full) {
        $before = [Antiphon.Nightly.ClassCostCache]::Digest($full)
        $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        $entry = [Antiphon.Nightly.ClassCostCache]::Store($full, $before, $doc)
    }
    else { $doc = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json }
    if ($null -ne $doc.classes) {
        foreach ($row in @($doc.classes.PSObject.Properties)) {
            $map[[string]$row.Name] = [string]$row.Value.basis
        }
    }
    if ($null -ne $entry) { $entry.Basis = $map.Clone() }
    return $map
}

function Get-TestPhaseTimingDirectory {
    param([string]$RunDirectory)
    if (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) {
        return [string]$script:QualificationTimingRoot
    }
    return (Join-Path $RunDirectory 'timing')
}

function Read-TestPhaseTrxAttribute {
    param([string]$Attrs, [string]$Name)
    if ([string]::IsNullOrEmpty($Attrs) -or [string]::IsNullOrEmpty($Name)) { return '' }
    $pattern = '\b' + [regex]::Escape($Name) + '\s*=\s*"([^"]*)"'
    $match = [regex]::Match($Attrs, $pattern)
    if ($match.Success) { return [string]$match.Groups[1].Value }
    $pattern = '\b' + [regex]::Escape($Name) + "\s*=\s*'([^']*)'"
    $match = [regex]::Match($Attrs, $pattern)
    if ($match.Success) { return [string]$match.Groups[1].Value }
    return ''
}

function Get-TestPhaseStdOutText {
    param([string]$Body)
    if ([string]::IsNullOrEmpty($Body)) { return '' }
    $open = $Body.IndexOf('<StdOut', [StringComparison]::Ordinal)
    if ($open -lt 0) { return '' }
    $gt = $Body.IndexOf('>', $open)
    if ($gt -lt 0) { return '' }
    $close = $Body.IndexOf('</StdOut>', $gt, [StringComparison]::Ordinal)
    if ($close -lt 0) { return '' }
    $inner = $Body.Substring($gt + 1, $close - ($gt + 1))
    if ($inner.StartsWith('<![CDATA[', [StringComparison]::Ordinal) -and $inner.EndsWith(']]>', [StringComparison]::Ordinal) -and $inner.Length -ge 12) {
        return $inner.Substring(9, $inner.Length - 12)
    }
    return $inner
}

function Read-TestPhaseTrxRows {
    param(
        [string]$Path,
        $Watch = $null,
        [int]$DeadlineMilliseconds = 200
    )
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    $none = [pscustomobject]@{ Rows = @(); Complete = $false; Refused = $true }
    if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Read-TestPhaseTrxRows' }
    if ([string]::IsNullOrWhiteSpace($Path)) { return $none }
    if ($null -eq $Watch -and $null -ne $script:TestPhaseTrxReadWatch) { $Watch = $script:TestPhaseTrxReadWatch }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular') { return $none }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { return $none }
    $opened = Open-TestPhaseReadStream -Path $Path
    if ($opened.Refused -or $null -eq $opened.Stream) { return $none }
    try {
        $read = [Antiphon.TestPhase.TrxTimingScanner]::Read($opened.Stream, $Watch, $DeadlineMilliseconds, 262144, 16384)
        if ($null -eq $read -or -not $read.Complete) { return $none }
        return [pscustomobject]@{ Rows = [Antiphon.Nightly.NightlyTrx]::TimingRows($read, $Path); Complete = $true; Refused = $false }
    } catch {
        return $none
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

. (Join-Path $PSScriptRoot 'test-phase-paths.ps1')
Initialize-TestPhaseLinuxIdentity

function Initialize-TestPhaseStreamReaders {
    if ('Antiphon.TestPhase.TrxTimingScanner' -as [type]) { return }
    # Antiphon.Nightly carries these types (Antiphon.Nightly/TestPhaseStreamReaders.cs); without it (Windows PowerShell 5.1,
    # ANTIPHON_NIGHTLY_CORE=off) the same source is compiled here.
    if (Test-NightlyCoreAvailable) { return }
    Add-Type -Path (Join-Path $PSScriptRoot 'Antiphon.Nightly/TestPhaseStreamReaders.cs')
}

Initialize-TestPhaseStreamReaders

function Read-TestPhaseStreamBytes {
    param($Stream, [int]$MaxBytes, [int]$DeadlineMilliseconds, $Watch)
    $buffer = New-Object byte[] 8192
    $ms = New-Object System.IO.MemoryStream
    $complete = $true
    while ($ms.Length -lt $MaxBytes) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { $complete = $false; break }
        $room = $MaxBytes - [int]$ms.Length
        $want = 8192
        if ($want -gt $room) { $want = $room }
        $count = $Stream.Read($buffer, 0, $want)
        if ($count -le 0) { break }
        $ms.Write($buffer, 0, $count)
    }
    if ($complete -and $ms.Length -ge $MaxBytes) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
            $complete = $false
        } else {
            $extra = $Stream.Read($buffer, 0, 1)
            if ($extra -gt 0) { $complete = $false }
        }
    }
    return [pscustomobject]@{ Bytes = $ms.ToArray(); Complete = [bool]$complete; Refused = $false; Kind = 'regular' }
}

function Open-TestPhaseReadStream {
    param([string]$Path)
    $refused = [pscustomobject]@{ Stream = $null; Refused = $true; Kind = 'unreadable' }
    $stream = $null
    $handle = $null
    $fd = -1
    $owned = $true
    Sync-NightlyDeferredEvidenceWrites $Path
    try {
        if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            $share = [IO.FileShare]([int][IO.FileShare]::ReadWrite -bor [int][IO.FileShare]::Delete)
            $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, $share)
        } else {
            # O_RDONLY | O_NONBLOCK | O_CLOEXEC | O_NOFOLLOW. A FIFO open returns
            # immediately; fstat then refuses anything that is not a regular file.
            Initialize-TestPhaseLinuxIdentity
            $fd = [Antiphon.TestPhase.FileIdentity]::Open($Path, 657408)
            if ($fd -lt 0) { return $refused }
            $stat = New-Object Antiphon.TestPhase.LinuxStat
            $code = [Antiphon.TestPhase.FileIdentity]::FStat($fd, [ref]$stat)
            if ($code -ne 0) { return $refused }
            $kind = ([uint32]$stat.Mode) -band 61440
            if ($kind -ne 32768) {
                return [pscustomobject]@{ Stream = $null; Refused = $true; Kind = 'unsupported' }
            }
            $handle = New-Object Microsoft.Win32.SafeHandles.SafeFileHandle ([IntPtr]$fd, $true)
            $fd = -1
            $stream = New-Object System.IO.FileStream ($handle, [IO.FileAccess]::Read, 8192, $false)
            $handle = $null
        }
        $owned = $false
        return [pscustomobject]@{ Stream = $stream; Refused = $false; Kind = 'regular' }
    } catch {
        return $refused
    } finally {
        if ($owned) {
            if ($null -ne $stream) { try { $stream.Dispose() } catch { } }
            elseif ($null -ne $handle) { try { $handle.Dispose() } catch { } }
            elseif ($fd -ge 0) { try { [void][Antiphon.TestPhase.FileIdentity]::Close($fd) } catch { } }
        }
    }
}

function Read-TestPhaseBoundedBytes {
    param(
        [string]$Path,
        [int]$MaxBytes = 262144,
        [int]$DeadlineMilliseconds = 200,
        $Watch = $null
    )
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    $refused = [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = 'unreadable' }
    if ($MaxBytes -lt 1) { $MaxBytes = 1 }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    # A failed deferred write is a refusal on every host. The failure stays stored for the run summary.
    if ($script:NightlyDeferEvidenceWrites) {
        [Antiphon.Nightly.EvidenceFiles]::Wait($Path)
        if ([Antiphon.Nightly.EvidenceFiles]::PathFailed($Path)) {
            return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = 'deferred-write-failed' }
        }
    }
    # Compiled for a real stopwatch; a scripted one (tests drive the budget through it) stays here.
    if ($Watch -is [Diagnostics.Stopwatch] -and [Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
        if (-not $script:NightlyCoreAvailable) { Assert-NightlyCoreAvailable 'Read-TestPhaseBoundedBytes' }
        return [Antiphon.Nightly.TestPhaseFiles]::BoundedRead($Path, $MaxBytes, $DeadlineMilliseconds, $Watch)
    }
    $identity = Get-TestPhasePathIdentity -Path $Path
    if ($identity.Kind -ne 'regular') {
        return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = [string]$identity.Kind }
    }
    if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) {
        return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = 'budget' }
    }
    $opened = Open-TestPhaseReadStream -Path $Path
    if ($opened.Refused -or $null -eq $opened.Stream) {
        $kind = [string]$opened.Kind
        if ([string]::IsNullOrWhiteSpace($kind)) { $kind = 'unreadable' }
        return [pscustomobject]@{ Bytes = $null; Complete = $false; Refused = $true; Kind = $kind }
    }
    try {
        return (Read-TestPhaseStreamBytes -Stream $opened.Stream -MaxBytes $MaxBytes -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch)
    } catch {
        return $refused
    } finally {
        try { $opened.Stream.Dispose() } catch { }
    }
}

function ConvertTo-TestPhaseJsonlRecords {
    param($Read, [int]$MaxLines = 4000, [int]$DeadlineMilliseconds = 200, $Watch = $null)
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    $records = New-Object System.Collections.Generic.List[object]
    $torn = 0
    $malformed = 0
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    if ($DeadlineMilliseconds -lt 1) { $DeadlineMilliseconds = 1 }
    if ($null -eq $Read -or [bool]$Read.Refused -or $null -eq $Read.Bytes) {
        return [pscustomobject]@{ Records = @(); Torn = 1; Malformed = 0; Complete = $false }
    }
    $complete = [bool]$Read.Complete
    if (-not $complete) { $torn = 1 }
    $text = [Text.Encoding]::UTF8.GetString($Read.Bytes)
    if ((-not $complete) -and (-not $text.EndsWith("`n"))) {
        $cut = $text.LastIndexOf([char]10)
        if ($cut -lt 0) { $text = '' }
        else { $text = $text.Substring(0, $cut + 1) }
    }
    if ($MaxLines -lt 1) { $MaxLines = 1 }
    $parts = $text.Split([string[]]@("`n"), [System.StringSplitOptions]::None)
    $limit = $parts.Length
    if ($limit -gt 0 -and $text.EndsWith("`n")) { $limit = $limit - 1 }
    $lines = 0
    for ($i = 0; $i -lt $limit; $i++) {
        if ($Watch.ElapsedMilliseconds -ge $DeadlineMilliseconds) { $complete = $false; $torn = 1; break }
        if ($lines -ge $MaxLines) { $complete = $false; $torn = 1; break }
        $line = $parts[$i].TrimEnd("`r")
        $lines++
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parsed = $null
        $ok = $true
        try { $parsed = $line | ConvertFrom-Json } catch { $ok = $false }
        if ($ok -and $null -ne $parsed -and -not [string]::IsNullOrWhiteSpace([string]$parsed.recordKind)) {
            $records.Add($parsed)
            continue
        }
        $lastContent = $true
        for ($j = $i + 1; $j -lt $limit; $j++) {
            if (-not [string]::IsNullOrWhiteSpace($parts[$j].TrimEnd("`r"))) { $lastContent = $false; break }
        }
        if ($lastContent) { $torn++ } else { $malformed++ }
    }
    if ($lines -ge $MaxLines) { $complete = $false; if ($torn -lt 1) { $torn = 1 } }
    return [pscustomobject]@{ Records = @($records.ToArray()); Torn = $torn; Malformed = $malformed; Complete = [bool]$complete }
}

function Read-TestPhaseJsonlFile {
    param([string]$Path)
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 262144 -DeadlineMilliseconds 200 -Watch $watch
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines 4000 -DeadlineMilliseconds 200 -Watch $watch
    return [pscustomobject]@{ Records = @($parsed.Records); Torn = [int]$parsed.Torn; Malformed = [int]$parsed.Malformed; Path = $Path }
}

# A6 stated aggregate. The former per-shard 5000 ms ceiling is now the whole
# summary re-read budget, so 387 shards cannot delay the receipt by about 32 minutes.
# The 200 ms / 262144 inline bound stays on its own watch. The measured cost is recorded.
function Get-TestPhaseSummaryBudgetMilliseconds { return 5000 }

function Read-TestPhaseJsonlForSummary {
    param([string]$Path, $BudgetState = $null)
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    $inline = [Diagnostics.Stopwatch]::StartNew()
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes 262144 -DeadlineMilliseconds 200 -Watch $inline
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines 4000 -DeadlineMilliseconds 200 -Watch $inline
    if (-not [bool]$parsed.Complete) {
        $watch = $null
        $deadline = Get-TestPhaseSummaryBudgetMilliseconds
        $maxBytes = 8388608
        if ($null -ne $BudgetState) {
            $watch = $BudgetState.Watch
            if ($null -ne $BudgetState.Milliseconds) { $deadline = [int]$BudgetState.Milliseconds }
            if ($null -ne $BudgetState.BytesRemaining) { $maxBytes = [int]$BudgetState.BytesRemaining }
        }
        if ($null -eq $watch) { $watch = New-Object System.Diagnostics.Stopwatch }
        if ($maxBytes -gt 0 -and $watch.ElapsedMilliseconds -lt $deadline) {
            if (-not $watch.IsRunning) { $watch.Start() }
            try {
                $full = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes $maxBytes -DeadlineMilliseconds $deadline -Watch $watch
                $parsed = ConvertTo-TestPhaseJsonlRecords -Read $full -MaxLines 20000 -DeadlineMilliseconds $deadline -Watch $watch
                if ($null -ne $BudgetState -and $null -ne $full -and $null -ne $full.Bytes) {
                    $left = [int]$BudgetState.BytesRemaining - [int]$full.Bytes.Length
                    if ($left -lt 0) { $left = 0 }
                    $BudgetState.BytesRemaining = $left
                }
            } finally {
                if ($watch.IsRunning) { $watch.Stop() }
            }
            if ($null -ne $BudgetState -and $null -ne $BudgetState.Watch -and $BudgetState.Watch.PSObject.Methods.Name -contains 'OnFullRead') {
                $BudgetState.Watch.OnFullRead()
            }
        }
    }
    return [pscustomobject]@{ Records = @($parsed.Records); Torn = [int]$parsed.Torn; Malformed = [int]$parsed.Malformed; Path = $Path }
}

function Read-TestPhaseJsonlBounded {
    param(
        [string]$Path,
        [int]$MaxBytes = 262144,
        [int]$MaxLines = 4000,
        [int]$DeadlineMilliseconds = 200,
        $Watch = $null
    )
    if (-not $script:TestPhaseReadWarmed) { Initialize-TestPhaseReadWarmup }
    if ($null -eq $Watch) { $Watch = [Diagnostics.Stopwatch]::StartNew() }
    $read = Read-TestPhaseBoundedBytes -Path $Path -MaxBytes $MaxBytes -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
    $parsed = ConvertTo-TestPhaseJsonlRecords -Read $read -MaxLines $MaxLines -DeadlineMilliseconds $DeadlineMilliseconds -Watch $Watch
    return [pscustomobject]@{ Records = @($parsed.Records); Complete = [bool]$parsed.Complete; Path = $Path }
}

function Write-TestPhasePlannedManifest {
    param(
        [string]$LogRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$SourceDigest,
        [string]$AssemblyDigest,
        [string]$PolicyDigest,
        [string]$CostFileDigest,
        [string]$AssemblyId,
        $Groups,
        $DiscoveryNodes,
        $ProfileExclusions,
        $DiscoveryIndex = $null,
        [string]$DiscoveryPath = ''
    )
    Assert-NightlyCoreAvailable 'Write-TestPhasePlannedManifest'
    $allPlanned = New-Object System.Collections.Generic.List[string]
    $entry = [pscustomobject]@{
        Document = [pscustomobject]@{ Nodes = @($DiscoveryNodes) }
        InProgress = @()
        Views = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    }
    if ($null -ne $DiscoveryIndex -and -not [string]::IsNullOrWhiteSpace($DiscoveryPath)) {
        $entry = [Antiphon.Nightly.NightlyDiscoveryIndex]::EntryOf($DiscoveryIndex, $DiscoveryPath)
    }
    $view = [Antiphon.Nightly.NightlyDiscoveryIndex]::View($entry, $ProfileExclusions, $true)
    $basisMap = Get-TestPhaseClassBasisMap
    $groupRows = @()
    foreach ($group in @($Groups)) {
        $ids = New-Object System.Collections.Generic.List[string]
        $carried = @()
        if ($group.PSObject.Properties.Name -contains 'nodes' -and $null -ne $group.nodes) {
            $carried = @($group.nodes)
        }
        if ($carried.Count -gt 0) {
            foreach ($node in $carried) {
                $uid = [string]$node.Uid
                if (-not [string]::IsNullOrWhiteSpace($uid)) { $ids.Add($uid) }
            }
        } else {
            $classes = @()
            foreach ($c in @($group.classes)) { $classes += [string]$c }
            [Antiphon.Nightly.PlannedManifestCheck]::AddStrings($ids, @([Antiphon.Nightly.NightlyDiscoveryIndex]::RequiredUids($view, $classes)))
        }
        $allPlanned.AddRange($ids)
        $bases = @{}
        foreach ($className in @($group.classes)) {
            $key = [string]$className
            $basis = 'model'
            if ($basisMap.ContainsKey($key)) { $basis = [string]$basisMap[$key] }
            elseif ($null -ne $script:NightlyClassCostTable -and $script:NightlyClassCostTable.ContainsKey($key)) { $basis = 'measured' }
            $bases[$basis] = $true
        }
        $pricing = 'model'
        $basisNames = @($bases.Keys)
        if ($basisNames.Count -eq 1) { $pricing = [string]$basisNames[0] }
        elseif ($basisNames.Count -gt 1) { $pricing = 'mixed' }
        $filter = ''
        if ($group.PSObject.Properties.Name -contains 'exactFilter' -and -not [string]::IsNullOrWhiteSpace([string]$group.exactFilter)) {
            $filter = [string]$group.exactFilter
        } elseif ($group.PSObject.Properties.Name -contains 'groupFilter') {
            $filter = [string]$group.groupFilter
        }
        $seconds = 0
        if ($group.PSObject.Properties.Name -contains 'seconds' -and $null -ne $group.seconds) {
            $seconds = [double]$group.seconds
        }
        $groupRows += [ordered]@{
            chunk = [string]$group.id
            classes = @($group.classes | ForEach-Object { [string]$_ })
            filter = $filter
            plannedTestIds = @($ids.ToArray())
            plannedSeconds = $seconds
            pricingBasis = $pricing
            sharedManifestOrder = $true
        }
    }
    # Union of planned UIDs (OrdinalIgnoreCase, as the former @{}), missing in RequiredUids order, extra
    # against an InvariantCultureIgnoreCase set, extra in first-planned order (compiled).
    $partition = [Antiphon.Nightly.PlannedManifestCheck]::Check($allPlanned, @($view.RequiredUids))
    $duplicate = [bool]$partition.Duplicate
    $missing = $partition.Missing
    $extra = $partition.Extra
    $manifest = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suite = [string]$Suite
        sourceDigest = [string]$SourceDigest
        assemblyDigest = [string]$AssemblyDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assembly = [string]$AssemblyId
        requiredPartitionExact = ((-not $duplicate) -and $missing.Count -eq 0 -and $extra.Count -eq 0)
        missingIds = @($missing)
        extraIds = @($extra)
        groups = @($groupRows)
    }
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
    $text = [Antiphon.Nightly.TestPhaseJson]::Text($manifest, 0)
    $path = Join-Path $dir 'groups-planned.json'
    Write-TestPhaseTextAtomic -Path $path -Text $text
    # Keep the singleton for existing readers. Also retain every allowlisted suite.
    if ($Suite -match '^[a-z0-9][a-z0-9-]{0,40}$') {
        $suiteFile = 'groups-planned-' + $Suite + '.json'
        Write-TestPhaseTextAtomic -Path (Join-Path $dir $suiteFile) -Text $text
        $filters = New-Object System.Collections.Generic.List[string]
        foreach ($plannedRow in @($groupRows)) {
            if ($null -eq $plannedRow) { continue }
            $filterText = ''
            if ($plannedRow.PSObject.Properties.Name -contains 'filter' -and $null -ne $plannedRow.filter) { $filterText = [string]$plannedRow.filter }
            $filters.Add($filterText)
        }
        Update-TestPhasePlannedIndex -Directory $dir -Suite $Suite -FileName $suiteFile -RunId $RunId -Attempt $Attempt -AssemblyId $AssemblyId -SourceDigest $SourceDigest -PolicyDigest $PolicyDigest -CostFileDigest $CostFileDigest -AssemblyDigest $AssemblyDigest -Filters @($filters.ToArray())
    }
    return $path
}

function Write-TestPhaseFailureMarker {
    param([string]$RunDirectory, [string]$Code)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    try {
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        $path = Join-Path $dir 'phase-summary-failed'
        Write-NightlyEvidenceText $path $Code
    } catch {
        # An unwritable timing directory must not change the caller's verdict.
    }
}

function ConvertTo-TestPhaseExecutedChunk {
    param($Item, [string]$Chunk, [string]$RunId, [string]$Attempt, [string]$Suite, [double]$HostWallSeconds, [bool]$TrxMissing, [int]$PassedCount, $Outcomes, [string]$Trx, [string]$TrxDigest, $ExitCode, $CoverageComplete, $TestsPassed)
    Assert-NightlyCoreAvailable 'ConvertTo-TestPhaseExecutedChunk'
    # One {testId, outcome} row per test (TestPhaseSummary.OutcomeRows).
    $priorOutcomes = New-Object System.Collections.Generic.List[object]
    if ($null -ne $Outcomes) {
        $priorOutcomes.AddRange([Antiphon.Nightly.TestPhaseSummary]::OutcomeRows([object[]]@($Outcomes), 'testId', 'outcome', $true))
    }
    $chunkId = $Chunk
    $run = $RunId
    $attempt = $Attempt
    $suite = $Suite
    $wall = $HostWallSeconds
    $missing = $TrxMissing
    $passed = $PassedCount
    $trx = $Trx
    $digest = $TrxDigest
    $exitCode = $ExitCode
    $coverage = $CoverageComplete
    $tests = $TestsPassed
    if ($null -ne $Item) {
        if ($Item.PSObject.Properties.Name -contains 'chunk') { $chunkId = [string]$Item.chunk }
        if ($Item.PSObject.Properties.Name -contains 'runId') { $run = [string]$Item.runId }
        if ($Item.PSObject.Properties.Name -contains 'attempt') { $attempt = [string]$Item.attempt }
        if ($Item.PSObject.Properties.Name -contains 'suite') { $suite = [string]$Item.suite }
        if ($Item.PSObject.Properties.Name -contains 'hostWallSeconds' -and $null -ne $Item.hostWallSeconds) { $wall = [double]$Item.hostWallSeconds }
        if ($Item.PSObject.Properties.Name -contains 'trxMissing') { $missing = [bool]$Item.trxMissing }
        if ($Item.PSObject.Properties.Name -contains 'passedCount' -and $null -ne $Item.passedCount) { $passed = [int]$Item.passedCount }
        if ($Item.PSObject.Properties.Name -contains 'trx') { $trx = [string]$Item.trx }
        if ($Item.PSObject.Properties.Name -contains 'trxDigest') { $digest = [string]$Item.trxDigest }
        if ($Item.PSObject.Properties.Name -contains 'exitCode') { $exitCode = $Item.exitCode }
        if ($Item.PSObject.Properties.Name -contains 'coverageComplete') { $coverage = $Item.coverageComplete }
        if ($Item.PSObject.Properties.Name -contains 'testsPassed') { $tests = $Item.testsPassed }
        $priorOutcomes = New-Object System.Collections.Generic.List[object]
        if ($Item.PSObject.Properties.Name -contains 'outcomes' -and $null -ne $Item.outcomes) {
            $priorOutcomes.AddRange([Antiphon.Nightly.TestPhaseSummary]::OutcomeRows([object[]]@($Item.outcomes), 'testId', 'outcome', $true))
        }
    }
    return [ordered]@{
        chunk = [string]$chunkId
        runId = [string]$run
        attempt = [string]$attempt
        suite = [string]$suite
        hostWallSeconds = $wall
        trxMissing = [bool]$missing
        manufacturedPassed = $false
        passedCount = $passed
        outcomes = @($priorOutcomes.ToArray())
        trx = [string]$trx
        trxDigest = [string]$digest
        exitCode = $exitCode
        coverageComplete = $coverage
        testsPassed = $tests
    }
}

function ConvertTo-TestPhaseB64 {
    param([string]$Text)
    return [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes([string]$Text))
}

function ConvertFrom-TestPhaseB64 {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    return [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([string]$Text))
}

function Get-TestPhaseSha256Text {
    param([string]$Text)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]$Text)
        $hash = $sha.ComputeHash($bytes)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        $sha.Dispose()
    }
}

function Get-TestPhaseSha256File {
    param([string]$Path)
    if ($script:NightlyDeferEvidenceWrites) { return [Antiphon.Nightly.EvidenceFiles]::Sha256($Path) }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $hash = $sha.ComputeHash($stream)
        $sb = New-Object System.Text.StringBuilder
        foreach ($b in $hash) { [void]$sb.Append($b.ToString('x2')) }
        return $sb.ToString()
    } finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Find-TestPhaseTimeZoneById {
    param([string]$Id)
    if ($null -ne $script:TestPhaseTimeZoneFinder) {
        $found = & $script:TestPhaseTimeZoneFinder $Id
        if ($found -is [System.TimeZoneInfo]) { return $found }
        return $null
    }
    try { return [System.TimeZoneInfo]::FindSystemTimeZoneById([string]$Id) } catch { return $null }
}

function Test-TestPhaseTimeZoneMethod {
    param([string]$Name)
    foreach ($method in @([System.TimeZoneInfo].GetMethods())) {
        if ([string]$method.Name -eq $Name) { return $true }
    }
    return $false
}

function ConvertTo-TestPhaseWindowsTimeZoneId {
    param([string]$IanaId)
    if (-not (Test-TestPhaseTimeZoneMethod -Name 'TryConvertIanaIdToWindowsId')) { return '' }
    $windows = ''
    $ok = $false
    try { $ok = [System.TimeZoneInfo]::TryConvertIanaIdToWindowsId([string]$IanaId, [ref]$windows) } catch { return '' }
    if ($ok) { return [string]$windows }
    return ''
}

function ConvertTo-TestPhaseIanaTimeZoneId {
    param([string]$WindowsId)
    if (-not (Test-TestPhaseTimeZoneMethod -Name 'TryConvertWindowsIdToIanaId')) { return '' }
    $iana = ''
    $ok = $false
    try { $ok = [System.TimeZoneInfo]::TryConvertWindowsIdToIanaId([string]$WindowsId, [ref]$iana) } catch { return '' }
    if ($ok) { return [string]$iana }
    return ''
}

function Resolve-TestPhaseTimeZone {
    param([string]$Id)
    if ([string]::IsNullOrWhiteSpace($Id)) { return [System.TimeZoneInfo]::Local }
    $direct = Find-TestPhaseTimeZoneById -Id $Id
    if ($direct -is [System.TimeZoneInfo]) { return $direct }
    $windows = ConvertTo-TestPhaseWindowsTimeZoneId -IanaId $Id
    if (-not [string]::IsNullOrWhiteSpace($windows)) {
        $mapped = Find-TestPhaseTimeZoneById -Id $windows
        if ($mapped -is [System.TimeZoneInfo]) { return $mapped }
    }
    $iana = ConvertTo-TestPhaseIanaTimeZoneId -WindowsId $Id
    if (-not [string]::IsNullOrWhiteSpace($iana)) {
        $mapped = Find-TestPhaseTimeZoneById -Id $iana
        if ($mapped -is [System.TimeZoneInfo]) { return $mapped }
    }
    return $null
}

function Get-TestPhasePinnedTimeZone {
    $requested = ''
    try { $requested = [string][Environment]::GetEnvironmentVariable('TZ') } catch { $requested = '' }
    if ([string]::IsNullOrWhiteSpace($requested)) { return [System.TimeZoneInfo]::Local }
    $resolved = Resolve-TestPhaseTimeZone -Id $requested
    if ($resolved -is [System.TimeZoneInfo]) { return $resolved }
    return [System.TimeZoneInfo]::Local
}

function Get-TestPhaseWriterEnvironment {
    $edition = 'Desktop'
    try { if ($PSVersionTable.PSEdition) { $edition = [string]$PSVersionTable.PSEdition } } catch { }
    $framework = ''
    try { $framework = [string][System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription } catch { $framework = [string][Environment]::Version }
    $culture = ''
    $ui = ''
    try { $culture = [string][System.Threading.Thread]::CurrentThread.CurrentCulture.Name } catch { }
    try { $ui = [string][System.Threading.Thread]::CurrentThread.CurrentUICulture.Name } catch { }
    $tz = [System.TimeZoneInfo]::Local
    $offset = $tz.GetUtcOffset([datetime]::Now)
    $sign = '+'
    if ($offset.Ticks -lt 0) { $sign = '-' }
    $off = $sign + ([math]::Abs($offset.Hours)).ToString('00') + ':' + ([math]::Abs($offset.Minutes)).ToString('00')
    return ($edition + '|' + [string]$PSVersionTable.PSVersion + '|' + $framework + '|' + $culture + '|' + $ui + '|' + [string]$tz.Id + '|' + $off)
}

function Test-TestPhaseSuiteChunkEqual {
    param([string]$LeftSuite, [string]$LeftChunk, [string]$RightSuite, [string]$RightChunk)
    $cmp = [System.StringComparer]::InvariantCultureIgnoreCase
    if (-not $cmp.Equals([string]$LeftSuite, [string]$RightSuite)) { return $false }
    return $cmp.Equals([string]$LeftChunk, [string]$RightChunk)
}

function Test-TestPhaseOrdinalEqual {
    param([string]$Left, [string]$Right)
    return [System.StringComparer]::Ordinal.Equals([string]$Left, [string]$Right)
}

function Test-TestPhaseUnsafeDirectory {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $true }
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $true }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $true }
    if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) { return $false }
    Initialize-TestPhaseLinuxIdentity
    if (-not ('Antiphon.TestPhase.FileIdentity' -as [type])) { return $true }
    $stat = New-Object Antiphon.TestPhase.LinuxStat
    $code = [Antiphon.TestPhase.FileIdentity]::LStat($Path, [ref]$stat)
    if ($code -ne 0) { return $true }
    $kind = ([uint32]$stat.Mode) -band 61440
    if ($kind -eq 16384) { return $false }
    return $true
}

function Wait-TestPhaseCommitBarrier {
    param([string]$Stage)
    $barrier = $script:TestPhaseCommitBarrier
    if ($null -eq $barrier) { return }
    $name = ''
    if ($barrier.PSObject.Properties.Name -contains 'Stage') { $name = [string]$barrier.Stage }
    if ($name -ne $Stage) { return }
    $signal = ''
    $release = ''
    if ($barrier.PSObject.Properties.Name -contains 'Signal') { $signal = [string]$barrier.Signal }
    if ($barrier.PSObject.Properties.Name -contains 'Release') { $release = [string]$barrier.Release }
    if (-not [string]::IsNullOrWhiteSpace($signal)) {
        $utf8 = New-Object System.Text.UTF8Encoding $false
        $parent = Split-Path -Parent $signal
        if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        [System.IO.File]::WriteAllText($signal, $Stage, $utf8)
    }
    if ([string]::IsNullOrWhiteSpace($release)) { return }
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    while (-not [System.IO.File]::Exists($release)) {
        if ($watch.ElapsedMilliseconds -gt 120000) { throw 'commit-barrier-timeout' }
        Start-Sleep -Milliseconds 20
    }
}

function Test-TestPhaseCommitFault {
    param([string]$Stage)
    $fault = $script:TestPhaseCommitFault
    if ($null -eq $fault) { return $false }
    $name = ''
    if ($fault.PSObject.Properties.Name -contains 'Stage') { $name = [string]$fault.Stage }
    if ($name -ne $Stage) { return $false }
    $remaining = 0
    if ($fault.PSObject.Properties.Name -contains 'Remaining') { $remaining = [int]$fault.Remaining }
    if ($remaining -le 0) { return $false }
    $fault.Remaining = $remaining - 1
    return $true
}

function New-TestPhaseRefusedContext {
    param([string]$Reason)
    return [pscustomobject]@{
        Refused = $true
        Reason = [string]$Reason
        Closed = $true
        Id = [guid]::NewGuid().ToString('n')
        Lock = $null
        TimingRoot = ''
        Generation = ''
        LogRoot = ''
        RunId = ''
        Attempt = ''
        Suite = ''
        Source = ''
        InvocationId = ''
        HasSeed = $false
        HeaderReady = $false
        HeaderRun = ''
        HeaderAttempt = ''
        HeaderSuite = ''
        HeaderAttemptSeen = $false
        Records = (New-Object System.Collections.Generic.List[object])
        SeedRows = (New-Object System.Collections.Generic.List[object])
        SeedPath = ''
        LastCommitted = 0
        NextSequence = 1
        Unresolved = $false
        CorruptionReason = ''
        Gaps = (New-Object System.Collections.Generic.List[string])
        ProjectOnly = $false
        WriterEnvironment = ''
        NeedsRecovery = $false
        RescanAttempted = $false
        PendingGaps = (New-Object System.Collections.Generic.List[object])
    }
}

function New-TestPhaseOpenContext {
    param([string]$TimingRoot, [string]$LogRoot, [string]$RunId, [string]$Attempt, [string]$Suite, [string]$Source, [string]$InvocationId, $Lock)
    $ctx = New-TestPhaseRefusedContext -Reason ''
    $ctx.Refused = $false
    $ctx.Closed = $false
    $ctx.Lock = $Lock
    $ctx.TimingRoot = $TimingRoot
    $ctx.LogRoot = $LogRoot
    $ctx.RunId = $RunId
    $ctx.Attempt = $Attempt
    $ctx.Suite = $Suite
    $ctx.Source = $Source
    $ctx.InvocationId = $InvocationId
    $ctx.WriterEnvironment = Get-TestPhaseWriterEnvironment
    return $ctx
}

function Resolve-TestPhaseExecutedTimingRoot {
    param([string]$LogRoot, [string]$TimingRoot)
    $selected = ''
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { $selected = $TimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) { $selected = [string]$script:QualificationTimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace($LogRoot)) { $selected = (Join-Path $LogRoot 'timing') }
    if ([string]::IsNullOrWhiteSpace($selected)) { return '' }
    return [System.IO.Path]::GetFullPath($selected)
}

function Get-TestPhaseJournalRoot {
    param([string]$TimingRoot)
    return (Join-Path $TimingRoot 'groups-executed.journal')
}

function Read-TestPhaseJsonFile {
    param([string]$Path)
    if (-not (Test-NightlyEvidencePath $Path)) { return $null }
    try { return ((Read-NightlyEvidenceText $Path) | ConvertFrom-Json) } catch { return $null }
}

function Test-TestPhaseRecordDigest {
    param([string]$Text)
    $needle = '"digest":"'
    $start = $Text.IndexOf($needle)
    if ($start -lt 0) { return $false }
    $hexStart = $start + $needle.Length
    $end = $Text.IndexOf('"', $hexStart)
    if ($end -lt 0) { return $false }
    $hex = $Text.Substring($hexStart, $end - $hexStart).ToLowerInvariant()
    $probe = $Text.Remove($start, ($end + 1) - $start).Insert($start, '"digest":""')
    $actual = Get-TestPhaseSha256Text -Text $probe
    return ($actual -eq $hex)
}

# Test-Path -LiteralPath for the per-chunk directory checks: a drive-qualified path (X:\...) is answered by
# .NET (the same answer for a file, a directory, a link and a missing path, without the cmdlet's per-call
# cost); any other form (relative, provider-qualified, UNC, empty) is still the cmdlet's to resolve or refuse,
# and so is a path .NET does not find that ends in a separator (the cmdlet finds a file named that way) or
# holds a NUL (the cmdlet throws).
function Test-TestPhasePathExists {
    param([string]$Path)
    if ($null -ne $Path -and $Path.Length -ge 3 -and [char]::IsLetter($Path[0]) -and $Path[1] -eq [char]58 -and ($Path[2] -eq [char]92 -or $Path[2] -eq [char]47)) {
        if ([System.IO.Directory]::Exists($Path) -or [System.IO.File]::Exists($Path)) { return $true }
        $last = $Path[$Path.Length - 1]
        if ($last -ne [char]92 -and $last -ne [char]47 -and $Path.IndexOf([char]0) -lt 0) { return $false }
    }
    return (Test-Path -LiteralPath $Path)
}

function Write-TestPhaseDirectory {
    param([string]$Path)
    if (-not (Test-TestPhasePathExists $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Copy-TestPhaseBytes {
    param([string]$Source, [string]$Destination)
    Write-TestPhaseDirectory -Path (Split-Path -Parent $Destination)
    Sync-NightlyDeferredEvidenceWrites
    [System.IO.File]::Copy($Source, $Destination, $true)
}

function Save-TestPhaseMalformedLegacy {
    param([string]$TimingRoot, [string]$Generation)
    $flat = Join-Path $TimingRoot 'groups-executed.json'
    if (-not (Test-NightlyEvidencePath $flat)) { return }
    $dest = Join-Path (Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $TimingRoot) 'predecessors') $Generation) 'groups-executed.json'
    if (Test-NightlyEvidencePath $dest) { return }
    Copy-TestPhaseBytes -Source $flat -Destination $dest
}

function Import-TestPhaseSeedRows {
    param($Parsed)
    $rows = New-Object System.Collections.Generic.List[object]
    $prior = @()
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'chunks' -and $null -ne $Parsed.chunks) { $prior = @($Parsed.chunks) }
    elseif ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'chunk') { $prior = @($Parsed) }
    foreach ($item in $prior) {
        if ($null -eq $item) { continue }
        $settled = ConvertTo-TestPhaseExecutedChunk -Item $item
        $rows.Add([pscustomobject]@{
            SettledJson = ([Antiphon.Nightly.TestPhaseJson]::Text($settled, 0))
            SettledSuite = [string]$settled.suite
            SettledChunk = [string]$settled.chunk
        })
    }
    $run = ''
    $attempt = ''
    $suite = ''
    $attemptSeen = $false
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'runId') { $run = [string]$Parsed.runId }
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'attempt') {
        $attempt = [string]$Parsed.attempt
        $attemptSeen = $true
    }
    if ($null -ne $Parsed -and $Parsed.PSObject.Properties.Name -contains 'suite') { $suite = [string]$Parsed.suite }
    return [pscustomobject]@{ Rows = $rows; Run = $run; Attempt = $attempt; Suite = $suite; AttemptSeen = $attemptSeen }
}

function Set-TestPhaseSeedHeader {
    param($Context, $Cache)
    $Context.HeaderReady = $true
    $Context.HeaderRun = [string]$Cache.Run
    $Context.HeaderAttempt = [string]$Cache.Attempt
    $Context.HeaderSuite = [string]$Cache.Suite
    $Context.HeaderAttemptSeen = [bool]$Cache.AttemptSeen
    $Context.SeedRows = $Cache.Rows
    $Context.HasSeed = $true
}

function Write-TestPhaseSeedCache {
    param($Context)
    if (-not $Context.HasSeed) { return }
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    Write-TestPhaseDirectory -Path $genDir
    $lines = New-Object System.Collections.Generic.List[string]
    $flag = '0'
    if ($Context.HeaderAttemptSeen) { $flag = '1' }
    $lines.Add('H' + "`t" + (ConvertTo-TestPhaseB64 ([string]$Context.HeaderRun)) + "`t" + (ConvertTo-TestPhaseB64 ([string]$Context.HeaderAttempt)) + "`t" + (ConvertTo-TestPhaseB64 ([string]$Context.HeaderSuite)) + "`t" + $flag)
    foreach ($row in $Context.SeedRows) {
        $lines.Add((ConvertTo-TestPhaseB64 ([string]$row.SettledSuite)) + "`t" + (ConvertTo-TestPhaseB64 ([string]$row.SettledChunk)) + "`t" + [string]$row.SettledJson)
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    Write-NightlyEvidenceLines (Join-Path $genDir 'seed-cache.txt') $lines.ToArray()
}

function Read-TestPhaseSeedCache {
    param($Context, [string]$Generation)
    $path = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Generation) 'seed-cache.txt'
    if (-not (Test-NightlyEvidencePath $path)) { return $false }
    $lines = @(Read-NightlyEvidenceLines $path)
    if ($lines.Count -lt 1) { return $false }
    $header = [string]$lines[0]
    if (-not $header.StartsWith('H' + "`t")) { return $false }
    $parts = $header.Split([char]9)
    if ($parts.Length -lt 5) { return $false }
    $rows = New-Object System.Collections.Generic.List[object]
    for ($i = 1; $i -lt $lines.Count; $i++) {
        $line = [string]$lines[$i]
        if ([string]::IsNullOrEmpty($line)) { continue }
        $first = $line.IndexOf([char]9)
        if ($first -lt 0) { return $false }
        $second = $line.IndexOf([char]9, $first + 1)
        if ($second -lt 0) { return $false }
        $rows.Add([pscustomobject]@{
            SettledJson = $line.Substring($second + 1)
            SettledSuite = (ConvertFrom-TestPhaseB64 $line.Substring(0, $first))
            SettledChunk = (ConvertFrom-TestPhaseB64 $line.Substring($first + 1, $second - $first - 1))
        })
    }
    $Context.HeaderReady = $true
    $Context.HeaderRun = (ConvertFrom-TestPhaseB64 ([string]$parts[1]))
    $Context.HeaderAttempt = (ConvertFrom-TestPhaseB64 ([string]$parts[2]))
    $Context.HeaderSuite = (ConvertFrom-TestPhaseB64 ([string]$parts[3]))
    $Context.HeaderAttemptSeen = ([string]$parts[4] -eq '1')
    $Context.SeedRows = $rows
    $Context.HasSeed = $true
    $Context.SeedPath = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Generation) 'seed.json'
    return $true
}

function New-TestPhaseEnvelopeText {
    param(
        [string]$Kind,
        [int]$Sequence,
        [string]$PublicationId,
        $Context,
        [string]$Reason,
        [string]$Suite,
        [string]$Chunk,
        [string]$SettledSuite,
        [string]$SettledChunk,
        [string]$HeaderRun,
        [string]$HeaderAttempt,
        [string]$HeaderSuite,
        [string]$FreshJson,
        [string]$SettledJson
    )
    # The journal envelope with its own SHA-256 in "digest" (TestPhaseRecords.Envelope).
    Assert-NightlyCoreAvailable 'New-TestPhaseEnvelopeText'
    return [Antiphon.Nightly.TestPhaseRecords]::Envelope($Kind, $Sequence, $PublicationId, [string]$Context.Generation, [string]$Context.TimingRoot,
        $Reason, $Suite, $Chunk, $SettledSuite, $SettledChunk, $HeaderRun, $HeaderAttempt, $HeaderSuite, $FreshJson, $SettledJson, [string]$Context.WriterEnvironment)
}

function Read-TestPhaseGenerationPrefix {
    param([string]$GenerationDir)
    # Every record is re-read from disk and its digest re-checked in the core (TestPhaseJournalRead.ReadPrefix),
    # which reads the prefix up to the first missing record or the first break (unreadable-record, bad-digest,
    # malformed-record, sequence-mismatch). Only our own writer produces these records.
    Assert-NightlyCoreAvailable 'Read-TestPhaseGenerationPrefix'
    $loaded = New-Object System.Collections.Generic.List[object]
    $step = [Antiphon.Nightly.TestPhaseJournalRead]::ReadPrefix($GenerationDir, 1, [bool]$script:NightlyDeferEvidenceWrites, $loaded)
    $last = 0
    if ($loaded.Count -gt 0) { $last = [int]$loaded[$loaded.Count - 1].Sequence }
    return [pscustomobject]@{
        Records = $loaded
        NextSequence = $step.Expected
        Corrupt = [bool]$step.Corrupt
        Reason = [string]$step.Reason
        LastSequence = $last
    }
}

function Write-TestPhaseGenerationMetadata {
    param($Context, [string]$ParentGeneration, [string]$SeedDigest)
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    Write-TestPhaseDirectory -Path $genDir
    $meta = [ordered]@{
        version = 1
        generation = [string]$Context.Generation
        runId = [string]$Context.RunId
        attempt = [string]$Context.Attempt
        suite = [string]$Context.Suite
        source = [string]$Context.Source
        invocationId = [string]$Context.InvocationId
        writerEnvironment = [string]$Context.WriterEnvironment
        seedDigest = [string]$SeedDigest
        parentGeneration = [string]$ParentGeneration
        unresolved = [bool]$Context.Unresolved
        corruptionReason = [string]$Context.CorruptionReason
    }
    $path = Join-Path $genDir 'metadata.json'
    Write-TestPhaseTextAtomic -Path $path -Text ([Antiphon.Nightly.TestPhaseJson]::Text($meta, 0))
    $digest = Get-TestPhaseSha256File -Path $path
    $pointer = [ordered]@{
        version = 1
        timingRoot = [string]$Context.TimingRoot
        generation = [string]$Context.Generation
        metadataDigest = [string]$digest
    }
    $current = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) 'current.json'
    Write-TestPhaseTextAtomic -Path $current -Text ([Antiphon.Nightly.TestPhaseJson]::Text($pointer, 0))
}

function Initialize-TestPhaseEmptyGeneration {
    param($Context, [string]$ParentGeneration)
    $Context.Generation = [guid]::NewGuid().ToString('n')
    $Context.HasSeed = $false
    $Context.SeedPath = ''
    $Context.SeedRows = New-Object System.Collections.Generic.List[object]
    $Context.Records = New-Object System.Collections.Generic.List[object]
    $Context.LastCommitted = 0
    $Context.NextSequence = 1
    $Context.HeaderReady = $false
    Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $ParentGeneration -SeedDigest ''
}

function Initialize-TestPhaseSeedGeneration {
    param($Context, [string]$LegacyPath, [string]$ParentGeneration, [bool]$Malformed)
    $Context.Generation = [guid]::NewGuid().ToString('n')
    $Context.Records = New-Object System.Collections.Generic.List[object]
    $Context.LastCommitted = 0
    $Context.NextSequence = 1
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    Write-TestPhaseDirectory -Path $genDir
    if ($Malformed) {
        $Context.Unresolved = $true
        if ([string]::IsNullOrWhiteSpace($Context.CorruptionReason)) { $Context.CorruptionReason = 'malformed-legacy' }
        Save-TestPhaseMalformedLegacy -TimingRoot $Context.TimingRoot -Generation $Context.Generation
        $Context.HasSeed = $false
        $Context.HeaderReady = $false
        Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $ParentGeneration -SeedDigest ''
        return
    }
    $seedPath = Join-Path $genDir 'seed.json'
    Copy-TestPhaseBytes -Source $LegacyPath -Destination $seedPath
    $Context.SeedPath = $seedPath
    $Context.HasSeed = $true
    $parsed = Read-TestPhaseJsonFile -Path $LegacyPath
    $cache = Import-TestPhaseSeedRows -Parsed $parsed
    Set-TestPhaseSeedHeader -Context $Context -Cache $cache
    Write-TestPhaseSeedCache -Context $Context
    $digest = Get-TestPhaseSha256File -Path $seedPath
    Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $ParentGeneration -SeedDigest $digest
}

function Get-TestPhaseProjectedDocument {
    param($Context)
    $reduced = [Antiphon.Nightly.TestPhaseJournalRead]::Reduce($Context)
    if ($reduced.ChunkCount -eq 0) {
        if ($Context.HasSeed -and -not [string]::IsNullOrWhiteSpace([string]$Context.SeedPath) -and (Test-NightlyEvidencePath $Context.SeedPath)) {
            return (Read-NightlyEvidenceText ([string]$Context.SeedPath))
        }
        return $null
    }
    $parts = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $reduced.Entries.Count; $i++) {
        if ($i -eq ($reduced.Entries.Count - 1)) { $parts.Add([string]$reduced.Entries[$i].Fresh) }
        else { $parts.Add([string]$reduced.Entries[$i].Json) }
    }
    $run = [Antiphon.Nightly.TestPhaseJson]::String(([string]$reduced.HeaderRun))
    $attempt = [Antiphon.Nightly.TestPhaseJson]::String(([string]$reduced.HeaderAttempt))
    $suite = [Antiphon.Nightly.TestPhaseJson]::String(([string]$reduced.HeaderSuite))
    return '{"version":1,"runId":' + $run + ',"attempt":' + $attempt + ',"suite":' + $suite + ',"manufacturedPassed":false,"chunks":[' + ($parts.ToArray() -join ',') + ']}'
}

function Import-TestPhaseGapMemory {
    param($Context, $Records)
    foreach ($rec in $Records) {
        if ($null -eq $rec) { continue }
        if ([string]$rec.Kind -eq 'gap') {
            if (-not $Context.Gaps.Contains('publication-gap')) { [void]$Context.Gaps.Add('publication-gap') }
        }
    }
}

function Import-TestPhaseExistingGeneration {
    param($Context, [string]$Generation, [bool]$ProjectOnly, [bool]$Recover)
    $journal = Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot
    $genDir = Join-Path $journal $Generation
    $meta = Read-TestPhaseJsonFile -Path (Join-Path $genDir 'metadata.json')
    if ($null -eq $meta) {
        $Context.Unresolved = $true
        $Context.CorruptionReason = 'metadata-unreadable'
        Initialize-TestPhaseEmptyGeneration -Context $Context -ParentGeneration $Generation
        return
    }
    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir $genDir
    $same = (Test-TestPhaseOrdinalEqual ([string]$meta.runId) ([string]$Context.RunId)) -and (Test-TestPhaseOrdinalEqual ([string]$meta.attempt) ([string]$Context.Attempt)) -and (Test-TestPhaseOrdinalEqual ([string]$meta.source) ([string]$Context.Source)) -and (Test-TestPhaseOrdinalEqual ([string]$meta.writerEnvironment) ([string]$Context.WriterEnvironment))
    $sealed = Test-NightlyEvidencePath (Join-Path $genDir 'seal.json')
    $resumeSealed = [bool]$Recover -and $sealed -and -not $prefix.Corrupt -and $same
    if ($ProjectOnly) {
        $Context.Generation = $Generation
        $Context.Records = $prefix.Records
        $Context.LastCommitted = $prefix.LastSequence
        $Context.NextSequence = $prefix.NextSequence
        $Context.Unresolved = [bool]$meta.unresolved -or $prefix.Corrupt
        $Context.CorruptionReason = [string]$meta.corruptionReason
        if ($prefix.Corrupt -and [string]::IsNullOrWhiteSpace($Context.CorruptionReason)) { $Context.CorruptionReason = $prefix.Reason }
        [void](Read-TestPhaseSeedCache -Context $Context -Generation $Generation)
        Import-TestPhaseGapMemory -Context $Context -Records $prefix.Records
        return
    }
    if (($prefix.Corrupt -or -not $same -or $sealed) -and -not $resumeSealed) {
        $snapshot = New-TestPhaseRefusedContext -Reason ''
        $snapshot.Refused = $false
        $snapshot.TimingRoot = $Context.TimingRoot
        $snapshot.Generation = $Generation
        $snapshot.Records = $prefix.Records
        $snapshot.WriterEnvironment = [string]$Context.WriterEnvironment
        [void](Read-TestPhaseSeedCache -Context $snapshot -Generation $Generation)
        $parentText = Get-TestPhaseProjectedDocument -Context $snapshot
        $reduced = [Antiphon.Nightly.TestPhaseJournalRead]::Reduce($snapshot)
        $Context.Unresolved = [bool]$meta.unresolved -or $prefix.Corrupt
        if ($prefix.Corrupt) { $Context.CorruptionReason = $prefix.Reason }
        elseif ([bool]$meta.unresolved) { $Context.CorruptionReason = [string]$meta.corruptionReason }
        else { $Context.CorruptionReason = '' }
        if ([string]::IsNullOrWhiteSpace($parentText)) {
            Initialize-TestPhaseEmptyGeneration -Context $Context -ParentGeneration $Generation
            return
        }
        $Context.Generation = [guid]::NewGuid().ToString('n')
        $genNew = Join-Path $journal $Context.Generation
        Write-TestPhaseDirectory -Path $genNew
        $newSeed = Join-Path $genNew 'seed.json'
        $utf8 = New-Object System.Text.UTF8Encoding $false
        Write-NightlyEvidenceText $newSeed $parentText
        $Context.SeedPath = $newSeed
        $Context.HasSeed = $true
        $Context.Records = New-Object System.Collections.Generic.List[object]
        $Context.LastCommitted = 0
        $Context.NextSequence = 1
        $rows = New-Object System.Collections.Generic.List[object]
        for ($i = 0; $i -lt $reduced.Entries.Count; $i++) {
            $emitted = [string]$reduced.Entries[$i].Json
            if ($i -eq ($reduced.Entries.Count - 1)) { $emitted = [string]$reduced.Entries[$i].Fresh }
            $rows.Add([pscustomobject]@{
                SettledJson = $emitted
                SettledSuite = [string]$reduced.Entries[$i].Suite
                SettledChunk = [string]$reduced.Entries[$i].Chunk
            })
        }
        $Context.SeedRows = $rows
        $Context.HeaderReady = $true
        $Context.HeaderRun = [string]$reduced.HeaderRun
        $Context.HeaderAttempt = [string]$reduced.HeaderAttempt
        $Context.HeaderSuite = [string]$reduced.HeaderSuite
        $Context.HeaderAttemptSeen = $true
        Write-TestPhaseSeedCache -Context $Context
        $header = [pscustomobject]@{ Run = [string]$Context.HeaderRun; Attempt = [string]$Context.HeaderAttempt; Suite = [string]$Context.HeaderSuite }
        Update-TestPhaseContextHeader -Context $Context -Header $header
        Write-TestPhaseGenerationMetadata -Context $Context -ParentGeneration $Generation -SeedDigest (Get-TestPhaseSha256File -Path $newSeed)
        return
    }
    $Context.Generation = $Generation
    $Context.Records = $prefix.Records
    $Context.LastCommitted = $prefix.LastSequence
    $Context.NextSequence = $prefix.NextSequence
    $Context.Unresolved = [bool]$meta.unresolved
    $Context.CorruptionReason = [string]$meta.corruptionReason
    [void](Read-TestPhaseSeedCache -Context $Context -Generation $Generation)
    Import-TestPhaseGapMemory -Context $Context -Records $prefix.Records
    if ($prefix.Records.Count -gt 0) {
        $last = $null
        foreach ($rec in $prefix.Records) {
            if ($rec.Kind -eq 'chunk') { $last = $rec }
        }
        if ($null -ne $last) {
            $mini = [ordered]@{ runId = [string]$last.HeaderRun; attempt = [string]$last.HeaderAttempt; suite = [string]$last.HeaderSuite }
            $settled = ([Antiphon.Nightly.TestPhaseJson]::Text($mini, 0)) | ConvertFrom-Json
            $Context.HeaderReady = $true
            $Context.HeaderRun = [string]$settled.runId
            $Context.HeaderAttempt = [string]$settled.attempt
            $Context.HeaderSuite = [string]$settled.suite
            $Context.HeaderAttemptSeen = $true
        }
    }
}

function Open-TestPhaseExecutedContext {
    param(
        [string]$LogRoot,
        [string]$TimingRoot,
        [string]$RunId,
        [string]$Attempt,
        [string]$Suite,
        [string]$Source,
        [string]$InvocationId,
        [switch]$ProjectOnly,
        [switch]$Recover
    )
    if ([string]::IsNullOrWhiteSpace($Source)) { $Source = 'unknown' }
    $timing = Resolve-TestPhaseExecutedTimingRoot -LogRoot $LogRoot -TimingRoot $TimingRoot
    if ([string]::IsNullOrWhiteSpace($timing)) { return (New-TestPhaseRefusedContext -Reason 'timing-root-missing') }
    $probe = $timing
    if (-not [string]::IsNullOrWhiteSpace($TimingRoot)) { $probe = $TimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace([string]$script:QualificationTimingRoot)) { $probe = [string]$script:QualificationTimingRoot }
    elseif (-not [string]::IsNullOrWhiteSpace($LogRoot)) { $probe = (Join-Path $LogRoot 'timing') }
    if (Test-TestPhaseUnsafeDirectory -Path $probe) { return (New-TestPhaseRefusedContext -Reason 'unsafe-root') }
    if (Test-NightlyEvidencePath $timing 'Leaf') { return (New-TestPhaseRefusedContext -Reason 'timing-root-is-file') }
    Write-TestPhaseDirectory -Path $timing
    $journal = Get-TestPhaseJournalRoot -TimingRoot $timing
    Write-TestPhaseDirectory -Path $journal
    $lockPath = Join-Path $journal 'writer.lock'
    $lock = $null
    try {
        $lock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch {
        return (New-TestPhaseRefusedContext -Reason 'writer-lock-held')
    }
    $ctx = New-TestPhaseOpenContext -TimingRoot $timing -LogRoot $LogRoot -RunId $RunId -Attempt $Attempt -Suite $Suite -Source $Source -InvocationId $InvocationId -Lock $lock
    $ctx.ProjectOnly = [bool]$ProjectOnly
    $currentPath = Join-Path $journal 'current.json'
    $current = Read-TestPhaseJsonFile -Path $currentPath
    if ($null -eq $current) {
        $flat = Join-Path $timing 'groups-executed.json'
        if ((Get-TestPhasePathIdentity -Path $flat).Kind -eq 'regular') {
            $parsed = Read-TestPhaseJsonFile -Path $flat
            $valid = $false
            if ($null -ne $parsed -and $parsed.PSObject.Properties.Name -contains 'version' -and [int]$parsed.version -eq 1) {
                if (($parsed.PSObject.Properties.Name -contains 'chunks') -or ($parsed.PSObject.Properties.Name -contains 'chunk')) { $valid = $true }
            }
            if ($valid) { Initialize-TestPhaseSeedGeneration -Context $ctx -LegacyPath $flat -ParentGeneration '' -Malformed $false }
            else { Initialize-TestPhaseSeedGeneration -Context $ctx -LegacyPath $flat -ParentGeneration '' -Malformed $true }
        } else {
            Initialize-TestPhaseEmptyGeneration -Context $ctx -ParentGeneration ''
        }
    } else {
        $bound = (Test-TestPhaseOrdinalEqual ([string]$current.timingRoot) ([string]$timing)) -and (-not [string]::IsNullOrWhiteSpace([string]$current.generation))
        if (-not $bound) {
            $ctx.Unresolved = $true
            $ctx.CorruptionReason = 'pointer-binding'
            Initialize-TestPhaseEmptyGeneration -Context $ctx -ParentGeneration ([string]$current.generation)
        } else {
            Import-TestPhaseExistingGeneration -Context $ctx -Generation ([string]$current.generation) -ProjectOnly:([bool]$ProjectOnly) -Recover:([bool]$Recover)
        }
    }
    if (-not $ProjectOnly) { $script:TestPhaseExecutedContext = $ctx }
    return $ctx
}

function Test-TestPhaseJournalWriteFailure {
    param($Context, [string[]]$Failures)
    $journal = ''
    try { $journal = [IO.Path]::GetFullPath((Get-TestPhaseJournalRoot -TimingRoot ([string]$Context.TimingRoot))) } catch { return $true }
    if ([string]::IsNullOrWhiteSpace($journal)) { return $true }
    $prefix = $journal.TrimEnd([char]92, [char]47) + [IO.Path]::DirectorySeparatorChar
    foreach ($item in @($Failures)) {
        $text = [string]$item
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $path = $text
        $split = $text.IndexOf(': ')
        if ($split -gt 0) { $path = $text.Substring(0, $split) }
        try { $path = [IO.Path]::GetFullPath($path) } catch { }
        if ($path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Close-TestPhaseExecutedContext {
    param($Context)
    if ($null -eq $Context) { return }
    if ($script:NightlyDeferEvidenceWrites -and $null -ne $Context.Lock) {
        $failed = @([Antiphon.Nightly.EvidenceFiles]::Flush())
        $named = @($failed | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) })
        if ($named.Count -gt 0) {
            $Context.Reason = 'commit-unpublished'
            $Context.Unresolved = $true
            if (-not $Context.Gaps.Contains('deferred-evidence-write-failed')) {
                [void]$Context.Gaps.Add('deferred-evidence-write-failed')
            }
            # A failed journal write keeps writer.lock. Any other path is already retained for the
            # verdict, so release the lock and let a later chunk in this process open.
            if (Test-TestPhaseJournalWriteFailure -Context $Context -Failures $named) { return }
        }
    }
    if ($null -ne $Context.Lock) {
        try { $Context.Lock.Dispose() } catch { }
        $Context.Lock = $null
    }
    $Context.Closed = $true
    if ($null -ne $script:TestPhaseExecutedContext -and [string]$script:TestPhaseExecutedContext.Id -eq [string]$Context.Id) {
        $script:TestPhaseExecutedContext = $null
    }
}

function Get-TestPhaseNextHeader {
    param($Context, [string]$RunId, [string]$Attempt, [string]$Suite)
    $run = [string]$RunId
    $attempt = [string]$Attempt
    $suite = [string]$Suite
    if ($Context.HeaderReady) {
        if (-not [string]::IsNullOrWhiteSpace([string]$Context.HeaderRun)) { $run = [string]$Context.HeaderRun }
        if ($Context.HeaderAttemptSeen) { $attempt = [string]$Context.HeaderAttempt }
        if (-not [string]::IsNullOrWhiteSpace([string]$Context.HeaderSuite)) { $suite = [string]$Context.HeaderSuite }
    }
    return [pscustomobject]@{ Run = $run; Attempt = $attempt; Suite = $suite }
}

function Update-TestPhaseContextHeader {
    param($Context, $Header)
    $mini = [ordered]@{ runId = [string]$Header.Run; attempt = [string]$Header.Attempt; suite = [string]$Header.Suite }
    $settled = ([Antiphon.Nightly.TestPhaseJson]::Text($mini, 0)) | ConvertFrom-Json
    $Context.HeaderReady = $true
    $Context.HeaderRun = [string]$settled.runId
    $Context.HeaderAttempt = [string]$settled.attempt
    $Context.HeaderSuite = [string]$settled.suite
    $Context.HeaderAttemptSeen = $true
}

function Write-TestPhaseCommitFile {
    param([string]$FinalPath, [string]$Text)
    $dir = Split-Path -Parent $FinalPath
    Write-TestPhaseDirectory -Path $dir
    # During a nightly run with no commit seam set, the record is queued on the evidence file system
    # (nightly-core.ps1): the same refusal when it exists and the same read-back, without a temporary file
    # and rename per record. The seams drive the on-disk steps below.
    if ($script:NightlyDeferEvidenceWrites -and $null -eq $script:TestPhaseCommitBarrier -and $null -eq $script:TestPhaseCommitFault) {
        if (Test-NightlyEvidencePath $FinalPath 'Leaf') { throw 'commit-exists' }
        Write-NightlyEvidenceText $FinalPath $Text
        $unpublished = @([Antiphon.Nightly.EvidenceFiles]::Flush($FinalPath))
        if ($unpublished.Count -gt 0) { throw ('commit-unpublished ' + ($unpublished -join '; ')) }
        $onDisk = [System.IO.File]::ReadAllText($FinalPath)
        if ($onDisk -ne $Text) { throw 'commit-unreadable' }
        return
    }
    Wait-TestPhaseCommitBarrier -Stage 'BeforeTempComplete'
    if (Test-TestPhaseCommitFault -Stage 'CreateTemp') { throw 'commit-fault-temp' }
    $tmp = Join-Path $dir ('.' + [guid]::NewGuid().ToString('n') + '.tmp')
    $stream = $null
    try {
        $stream = [System.IO.File]::Open($tmp, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes($Text)
        $stream.Write($bytes, 0, $bytes.Length)
        if (Test-TestPhaseCommitFault -Stage 'Flush') { throw 'commit-fault-flush' }
        # Flushed to the operating system, not forced to the disk: after a crash the latest record may be
        # missing or empty, which the prefix reader reports as a shorter prefix or as corruption.
        $stream.Flush()
        $stream.Dispose()
        $stream = $null
        Wait-TestPhaseCommitBarrier -Stage 'AfterFlushBeforeRename'
        if (Test-TestPhaseCommitFault -Stage 'Rename') { throw 'commit-fault-rename' }
        if ([System.IO.File]::Exists($FinalPath)) { throw 'commit-exists' }
        [System.IO.File]::Move($tmp, $FinalPath)
        $tmp = ''
        Wait-TestPhaseCommitBarrier -Stage 'AfterRenameBeforeAck'
        if (Test-TestPhaseCommitFault -Stage 'Acknowledge') { throw 'commit-fault-ack' }
        $readBack = [System.IO.File]::ReadAllText($FinalPath)
        if ($readBack -ne $Text) { throw 'commit-unreadable' }
    } catch {
        if ($null -ne $stream) { try { $stream.Dispose() } catch { } }
        if (-not [string]::IsNullOrWhiteSpace($tmp) -and [System.IO.File]::Exists($tmp)) {
            try { [System.IO.File]::Delete($tmp) } catch { }
        }
        throw
    }
}

function Add-TestPhaseJournalEnvelope {
    param($Context, [string]$Text, [string]$PublicationId, [int]$Sequence)
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    $final = Join-Path $genDir ($Sequence.ToString('00000000') + '.json')
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $attempt = 0
    $lastError = ''
    while ($attempt -lt 2) {
        if ($attempt -gt 0 -and $watch.Elapsed.TotalSeconds -gt 2) { break }
        $attempt++
        try {
            if (Test-NightlyEvidencePath $final 'Leaf') {
                $existing = Read-NightlyEvidenceText $final
                if ((Test-TestPhaseRecordDigest -Text $existing) -and $existing.Contains($PublicationId)) { return $true }
                throw 'commit-conflict'
            }
            Write-TestPhaseCommitFile -FinalPath $final -Text $Text
            return $true
        } catch {
            $lastError = [string]$_.Exception.Message
        }
    }
    $Context.Reason = $lastError
    return $false
}

function Invoke-TestPhasePrefixRescan {
    param($Context)
    if (-not $Context.NeedsRecovery) { return }
    if ($Context.RescanAttempted) { return }
    $Context.RescanAttempted = $true
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir $genDir
    $Context.Records = $prefix.Records
    $Context.LastCommitted = $prefix.LastSequence
    $Context.NextSequence = $prefix.NextSequence
    Import-TestPhaseGapMemory -Context $Context -Records $prefix.Records
    if ($prefix.Corrupt) {
        $Context.Unresolved = $true
        if ([string]::IsNullOrWhiteSpace([string]$Context.CorruptionReason)) { $Context.CorruptionReason = [string]$prefix.Reason }
    }
}

function Add-TestPhaseGapRecord {
    param(
        $Context,
        [string]$Reason,
        [string]$Suite = '',
        [string]$Chunk = '',
        [string]$RunId = '',
        [string]$Attempt = '',
        [string]$HeaderRun = '',
        [string]$HeaderAttempt = '',
        [string]$HeaderSuite = '',
        [string]$PublicationId = ''
    )
    $Context.NeedsRecovery = $true
    if (-not $Context.Gaps.Contains('publication-gap')) { [void]$Context.Gaps.Add('publication-gap') }
    if ([string]::IsNullOrWhiteSpace($HeaderRun)) { $HeaderRun = [string]$RunId }
    if ([string]::IsNullOrWhiteSpace($HeaderAttempt)) { $HeaderAttempt = [string]$Attempt }
    if ([string]::IsNullOrWhiteSpace($HeaderSuite)) { $HeaderSuite = [string]$Suite }
    if ([string]::IsNullOrWhiteSpace($PublicationId)) { $PublicationId = [guid]::NewGuid().ToString('n') }
    $sequence = [int]$Context.NextSequence
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation
    $final = Join-Path $genDir ($sequence.ToString('00000000') + '.json')
    $envelope = New-TestPhaseEnvelopeText -Kind 'gap' -Sequence $sequence -PublicationId $PublicationId -Context $Context -Reason $Reason -Suite $Suite -Chunk $Chunk -SettledSuite $Suite -SettledChunk $Chunk -HeaderRun $HeaderRun -HeaderAttempt $HeaderAttempt -HeaderSuite $HeaderSuite -FreshJson '' -SettledJson ''
    $ok = Add-TestPhaseJournalEnvelope -Context $Context -Text $envelope -PublicationId $PublicationId -Sequence $sequence
    if (-not $ok) {
        $already = $false
        if ($null -eq $Context.PendingGaps) { $Context.PendingGaps = New-Object System.Collections.Generic.List[object] }
        foreach ($pending in $Context.PendingGaps) {
            if ([string]$pending.PublicationId -eq $PublicationId) { $already = $true; break }
        }
        if (-not $already) {
            [void]$Context.PendingGaps.Add([pscustomobject]@{
                Reason = [string]$Reason
                Suite = [string]$Suite
                Chunk = [string]$Chunk
                RunId = [string]$RunId
                Attempt = [string]$Attempt
                HeaderRun = [string]$HeaderRun
                HeaderAttempt = [string]$HeaderAttempt
                HeaderSuite = [string]$HeaderSuite
                PublicationId = [string]$PublicationId
            })
        }
        return $false
    }
    $Context.Records.Add([pscustomobject]@{
        Kind = 'gap'
        Sequence = $sequence
        FreshJson = ''
        SettledJson = ''
        FreshSuite = [string]$Suite
        FreshChunk = [string]$Chunk
        SettledSuite = [string]$Suite
        SettledChunk = [string]$Chunk
        HeaderRun = [string]$HeaderRun
        HeaderAttempt = [string]$HeaderAttempt
        HeaderSuite = [string]$HeaderSuite
        PublicationId = [string]$PublicationId
    })
    $Context.LastCommitted = $sequence
    $Context.NextSequence = $sequence + 1
    return $true
}

function Submit-TestPhasePendingGaps {
    param($Context)
    if ($null -eq $Context.PendingGaps) { return $true }
    while ($Context.PendingGaps.Count -gt 0) {
        $pending = $Context.PendingGaps[0]
        $ok = Add-TestPhaseGapRecord -Context $Context -Reason ([string]$pending.Reason) -Suite ([string]$pending.Suite) -Chunk ([string]$pending.Chunk) -RunId ([string]$pending.RunId) -Attempt ([string]$pending.Attempt) -HeaderRun ([string]$pending.HeaderRun) -HeaderAttempt ([string]$pending.HeaderAttempt) -HeaderSuite ([string]$pending.HeaderSuite) -PublicationId ([string]$pending.PublicationId)
        if (-not $ok) { return $false }
        if ($Context.PendingGaps.Count -gt 0 -and [string]$Context.PendingGaps[0].PublicationId -eq [string]$pending.PublicationId) {
            $Context.PendingGaps.RemoveAt(0)
        }
    }
    return $true
}

function Add-TestPhaseExecutedChunk {
    param($Context, $Fresh, [string]$Suite, [string]$Chunk, [string]$RunId, [string]$Attempt)
    if ($Context.Closed -or $Context.Refused) { throw 'context-closed' }
    Invoke-TestPhasePrefixRescan -Context $Context
    $current = Read-TestPhaseJsonFile -Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) 'current.json')
    if ($null -eq $current -or -not (Test-TestPhaseOrdinalEqual ([string]$current.generation) ([string]$Context.Generation))) { throw 'stale-context' }
    if ($null -ne $Context.PendingGaps -and $Context.PendingGaps.Count -gt 0) {
        $drained = Submit-TestPhasePendingGaps -Context $Context
        if (-not $drained) {
            Write-TestPhaseFailureMarker -RunDirectory $Context.LogRoot -Code 'phase-summary-failed'
            return $false
        }
    }
    $header = Get-TestPhaseNextHeader -Context $Context -RunId $RunId -Attempt $Attempt -Suite $Suite
    $freshJson = [Antiphon.Nightly.TestPhaseJson]::Text($Fresh, 0)
    $parsed = $freshJson | ConvertFrom-Json
    $settled = ConvertTo-TestPhaseExecutedChunk -Item $parsed
    $settledJson = [Antiphon.Nightly.TestPhaseJson]::Text($settled, 0)
    $publicationId = [guid]::NewGuid().ToString('n')
    $sequence = [int]$Context.NextSequence
    $envelope = New-TestPhaseEnvelopeText -Kind 'chunk' -Sequence $sequence -PublicationId $publicationId -Context $Context -Reason '' -Suite $Suite -Chunk $Chunk -SettledSuite ([string]$settled.suite) -SettledChunk ([string]$settled.chunk) -HeaderRun ([string]$header.Run) -HeaderAttempt ([string]$header.Attempt) -HeaderSuite ([string]$header.Suite) -FreshJson $freshJson -SettledJson $settledJson
    $ok = Add-TestPhaseJournalEnvelope -Context $Context -Text $envelope -PublicationId $publicationId -Sequence $sequence
    if (-not $ok) {
        Write-TestPhaseFailureMarker -RunDirectory $Context.LogRoot -Code 'phase-summary-failed'
        Add-TestPhaseGapRecord -Context $Context -Reason ([string]$Context.Reason) -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HeaderRun ([string]$header.Run) -HeaderAttempt ([string]$header.Attempt) -HeaderSuite ([string]$header.Suite) | Out-Null
        return $false
    }
    Wait-TestPhaseCommitBarrier -Stage 'AfterAckBeforeProjection'
    $Context.Records.Add([pscustomobject]@{
        Kind = 'chunk'
        Sequence = $sequence
        FreshJson = $freshJson
        SettledJson = $settledJson
        FreshSuite = [string]$Suite
        FreshChunk = [string]$Chunk
        SettledSuite = [string]$settled.suite
        SettledChunk = [string]$settled.chunk
        HeaderRun = [string]$header.Run
        HeaderAttempt = [string]$header.Attempt
        HeaderSuite = [string]$header.Suite
        PublicationId = $publicationId
    })
    $Context.LastCommitted = $sequence
    $Context.NextSequence = $sequence + 1
    $Context.NeedsRecovery = $false
    $Context.RescanAttempted = $false
    Update-TestPhaseContextHeader -Context $Context -Header $header
    return $true
}

function Get-TestPhasePublicationStateText {
    param($Context, $ProjectionText)
    $quality = New-Object System.Collections.Generic.List[string]
    if ($Context.Unresolved) {
        if (-not $quality.Contains('publication-corruption')) { $quality.Add('publication-corruption') }
    }
    $pending = 0
    if ($null -ne $Context.PendingGaps) { $pending = $Context.PendingGaps.Count }
    if ($Context.Gaps.Count -gt 0 -or $pending -gt 0) {
        if (-not $quality.Contains('publication-gap')) { $quality.Add('publication-gap') }
    }
    $complete = ($quality.Count -eq 0)
    $digest = ''
    if ($null -ne $ProjectionText) { $digest = Get-TestPhaseSha256Text -Text $ProjectionText }
    $state = [ordered]@{
        version = 1
        generation = [string]$Context.Generation
        sequence = [int]$Context.LastCommitted
        root = [string]$Context.TimingRoot
        publicationComplete = [bool]$complete
        unresolved = [bool]$Context.Unresolved
        reason = [string]$Context.CorruptionReason
        projectionDigest = [string]$digest
        quality = @($quality.ToArray())
    }
    return ([Antiphon.Nightly.TestPhaseJson]::Text($state, 0))
}

function Write-TestPhasePublicationState {
    param($Context, [string]$ProjectionText)
    $text = Get-TestPhasePublicationStateText -Context $Context -ProjectionText $ProjectionText
    $path = Join-Path $Context.TimingRoot 'groups-executed.state.json'
    Write-TestPhaseTextAtomic -Path $path -Text $text
    return (Get-TestPhaseSha256File -Path $path)
}

function Complete-TestPhaseExecutedProjection {
    param($Context, [string]$LogRoot, [string]$TimingRoot, [string]$Terminal)
    $owned = $false
    if ($null -eq $Context) {
        $Context = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId '' -Attempt '' -Suite '' -Source 'unknown' -ProjectOnly
        $owned = $true
    }
    if ($null -eq $Context -or $Context.Refused) { return $Context }
    try {
        if ($null -ne $Context.PendingGaps -and $Context.PendingGaps.Count -gt 0) {
            Submit-TestPhasePendingGaps -Context $Context | Out-Null
        }
        $text = Get-TestPhaseProjectedDocument -Context $Context
        $terminalSet = -not [string]::IsNullOrWhiteSpace($Terminal)
        if ($null -eq $text -and -not $Context.Unresolved -and $Context.Gaps.Count -eq 0 -and -not $terminalSet) { return $Context }
        if ($null -ne $text) {
            $flat = Join-Path $Context.TimingRoot 'groups-executed.json'
            Write-TestPhaseTextAtomic -Path $flat -Text $text
        }
        $stateDigest = Write-TestPhasePublicationState -Context $Context -ProjectionText $text
        if ($terminalSet) {
            $projectionDigest = ''
            if ($null -ne $text) { $projectionDigest = Get-TestPhaseSha256Text -Text $text }
            $seal = [ordered]@{
                version = 1
                generation = [string]$Context.Generation
                sequence = [int]$Context.LastCommitted
                terminal = [string]$Terminal
                projectionDigest = [string]$projectionDigest
                stateDigest = [string]$stateDigest
            }
            $sealPath = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $Context.TimingRoot) $Context.Generation) 'seal.json'
            Write-TestPhaseTextAtomic -Path $sealPath -Text ([Antiphon.Nightly.TestPhaseJson]::Text($seal, 0))
        }
        return $Context
    } finally {
        if ($owned) { Close-TestPhaseExecutedContext -Context $Context }
    }
}

function Read-TestPhaseExecutedPrefix {
    param([string]$LogRoot, [string]$TimingRoot)
    $timing = Resolve-TestPhaseExecutedTimingRoot -LogRoot $LogRoot -TimingRoot $TimingRoot
    $empty = [pscustomobject]@{
        Generation = ''
        LastSequence = 0
        RecordKind = 'none'
        Quality = @()
        Complete = $false
        PublicationComplete = $false
        Chunks = @()
        Refused = $false
        Reason = ''
        Unresolved = $false
        Terminal = ''
    }
    if ([string]::IsNullOrWhiteSpace($timing)) { $empty.Refused = $true; $empty.Reason = 'timing-root-missing'; return $empty }
    $current = Read-TestPhaseJsonFile -Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json')
    if ($null -eq $current) { return $empty }
    $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation)
    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir $genDir
    $kind = 'none'
    if ($prefix.Records.Count -gt 0) { $kind = [string]$prefix.Records[$prefix.Records.Count - 1].Kind }
    $ids = New-Object System.Collections.Generic.List[string]
    foreach ($rec in $prefix.Records) {
        if ($rec.Kind -eq 'chunk') { $ids.Add([string]$rec.FreshChunk) }
    }
    $quality = New-Object System.Collections.Generic.List[string]
    if ($prefix.Corrupt) { $quality.Add('publication-corruption') }
    $terminal = ''
    $seal = Read-TestPhaseJsonFile -Path (Join-Path $genDir 'seal.json')
    if ($null -ne $seal -and $seal.PSObject.Properties.Name -contains 'terminal') { $terminal = [string]$seal.terminal }
    return [pscustomobject]@{
        Generation = [string]$current.generation
        LastSequence = [int]$prefix.LastSequence
        RecordKind = $kind
        Quality = @($quality.ToArray())
        Complete = (-not $prefix.Corrupt -and $prefix.Records.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($terminal))
        PublicationComplete = (-not $prefix.Corrupt)
        Chunks = @($ids.ToArray())
        Refused = $false
        Reason = [string]$prefix.Reason
        Unresolved = [bool]$prefix.Corrupt
        Terminal = $terminal
    }
}

function Invoke-TestPhaseExecutedRecovery {
    param([string]$LogRoot, [string]$TimingRoot, [string]$RunId, [string]$Attempt, [string]$Suite, [string]$Source)
    $timing = Resolve-TestPhaseExecutedTimingRoot -LogRoot $LogRoot -TimingRoot $TimingRoot
    $run = [string]$RunId
    $att = [string]$Attempt
    $src = [string]$Source
    if (-not [string]::IsNullOrWhiteSpace($timing)) {
        $current = Read-TestPhaseJsonFile -Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json')
        if ($null -ne $current -and -not [string]::IsNullOrWhiteSpace([string]$current.generation)) {
            $metaPath = Join-Path (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation)) 'metadata.json'
            $meta = Read-TestPhaseJsonFile -Path $metaPath
            if ($null -ne $meta) {
                if ([string]::IsNullOrWhiteSpace($run)) { $run = [string]$meta.runId }
                if ([string]::IsNullOrWhiteSpace($att)) { $att = [string]$meta.attempt }
                if ([string]::IsNullOrWhiteSpace($src)) { $src = [string]$meta.source }
            }
        }
    }
    if ([string]::IsNullOrWhiteSpace($src)) { $src = 'unknown' }
    $ctx = Open-TestPhaseExecutedContext -LogRoot $LogRoot -TimingRoot $TimingRoot -RunId $run -Attempt $att -Suite $Suite -Source $src -Recover
    if ($null -eq $ctx -or $ctx.Refused) { return $ctx }
    try {
        $genDir = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $ctx.TimingRoot) $ctx.Generation
        $seal = Read-TestPhaseJsonFile -Path (Join-Path $genDir 'seal.json')
        if ($null -ne $seal -and -not $ctx.Unresolved) {
            $preview = Get-TestPhaseProjectedDocument -Context $ctx
            $projectionDigest = ''
            if ($null -ne $preview) { $projectionDigest = Get-TestPhaseSha256Text -Text $preview }
            $stateText = Get-TestPhasePublicationStateText -Context $ctx -ProjectionText $preview
            $stateDigest = Get-TestPhaseSha256Text -Text $stateText
            $projectionMatch = Test-TestPhaseOrdinalEqual $projectionDigest ([string]$seal.projectionDigest)
            $stateMatch = Test-TestPhaseOrdinalEqual $stateDigest ([string]$seal.stateDigest)
            if (-not $projectionMatch -or -not $stateMatch) {
                $ctx.Reason = 'seal-digest-mismatch'
                return $ctx
            }
        }
        Complete-TestPhaseExecutedProjection -Context $ctx | Out-Null
        return $ctx
    } finally {
        Close-TestPhaseExecutedContext -Context $ctx
    }
}

function Add-TestPhaseDeferredEvidenceToken {
    param($Tokens)
    if ($null -eq $Tokens) { return }
    try {
        if (-not ('Antiphon.Nightly.EvidenceFiles' -as [type])) { return }
        $pending = @([Antiphon.Nightly.EvidenceFiles]::PeekFailures())
        $any = $false
        foreach ($item in $pending) {
            if (-not [string]::IsNullOrWhiteSpace([string]$item)) { $any = $true; break }
        }
        if ($any -and -not $Tokens.Contains('deferred-evidence-write-failed')) {
            [void]$Tokens.Add('deferred-evidence-write-failed')
        }
    } catch { }
}

function Get-TestPhasePublicationQuality {
    param([string]$RunDirectory, $KnownPrefix)
    $tokens = New-Object System.Collections.Generic.List[string]
    try {
        $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
        $flat = Join-Path $timing 'groups-executed.json'
        $currentPath = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json'
        $hasFlat = (Get-TestPhasePathIdentity -Path $flat).Kind -eq 'regular'
        $hasJournal = (Get-TestPhasePathIdentity -Path $currentPath).Kind -eq 'regular'
        if (-not $hasFlat -and -not $hasJournal) {
            Add-TestPhaseDeferredEvidenceToken $tokens
            return @($tokens.ToArray())
        }
        $statePath = Join-Path $timing 'groups-executed.state.json'
        if ((Get-TestPhasePathIdentity -Path $statePath).Kind -ne 'regular') {
            $tokens.Add('publication-state-missing')
            Add-TestPhaseDeferredEvidenceToken $tokens
            return @($tokens.ToArray())
        }
        $state = Read-TestPhaseJsonFile -Path $statePath
        if ($null -eq $state) {
            $tokens.Add('publication-state-mismatch')
            Add-TestPhaseDeferredEvidenceToken $tokens
            return @($tokens.ToArray())
        }
        $mismatch = $false
        if ($hasJournal) {
            $current = Read-TestPhaseJsonFile -Path $currentPath
            if ($null -eq $current -or -not (Test-TestPhaseOrdinalEqual ([string]$current.generation) ([string]$state.generation))) { $mismatch = $true }
            else {
                $prefix = $KnownPrefix
                if ($null -eq $prefix) {
                    $prefix = Read-TestPhaseGenerationPrefix -GenerationDir (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation))
                }
                if ([int]$state.sequence -ne [int]$prefix.LastSequence) { $mismatch = $true }
                if ([bool]$prefix.Corrupt -and -not $tokens.Contains('publication-corruption')) { $tokens.Add('publication-corruption') }
            }
        }
        $rootFull = ''
        try { $rootFull = [System.IO.Path]::GetFullPath($timing) } catch { $rootFull = $timing }
        if (-not (Test-TestPhaseOrdinalEqual ([string]$state.root) ([string]$rootFull))) { $mismatch = $true }
        if ($hasFlat) {
            $digest = Get-TestPhaseSha256File -Path $flat
            if (-not (Test-TestPhaseOrdinalEqual ([string]$state.projectionDigest) ([string]$digest))) { $mismatch = $true }
        }
        if ($mismatch) { $tokens.Add('publication-state-mismatch') }
        if ($state.PSObject.Properties.Name -contains 'quality' -and $null -ne $state.quality) {
            foreach ($token in @($state.quality)) {
                $text = [string]$token
                if ([string]::IsNullOrWhiteSpace($text)) { continue }
                if (-not $tokens.Contains($text)) { $tokens.Add($text) }
            }
        }
    } catch {
        if (-not $tokens.Contains('publication-state-mismatch')) { $tokens.Add('publication-state-mismatch') }
    }
    Add-TestPhaseDeferredEvidenceToken $tokens
    return @($tokens.ToArray())
}

function Write-TestPhaseExecutedManifest {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    $rows = @()
    $trxIdentity = Get-TestPhasePathIdentity -Path $TrxPath
    $trxMissing = ($trxIdentity.Kind -ne 'regular')
    if ($trxIdentity.Kind -eq 'regular') {
        $trxRead = Read-TestPhaseTrxRows -Path $TrxPath
        if ($null -ne $trxRead -and $trxRead.Complete -and -not $trxRead.Refused) {
            $rows = @($trxRead.Rows)
        }
    }
    Assert-NightlyCoreAvailable 'Write-TestPhaseExecutedManifest'
    # One row per test (about 19,000 in the main suite): NightlyCore.cs TestPhaseSummary.OutcomeRows.
    $outcomeRows = [Antiphon.Nightly.TestPhaseSummary]::OutcomeRows([object[]]$rows, 'TestId', 'Outcome', $false)
    $passed = [Antiphon.Nightly.TestPhaseSummary]::PassedCount($outcomeRows)
    $digest = ''
    if ($trxIdentity.Kind -eq 'regular') {
        try { $digest = Get-NightlyFileSha256 -Path $TrxPath } catch { $digest = '' }
    }
    Write-TestPhaseOwnerObservation -Verdict $Verdict -LogRoot $LogRoot -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
    $exitCode = $null
    $coverageComplete = $null
    $testsPassed = $null
    if ($null -ne $Verdict) {
        if ($Verdict.PSObject.Properties.Name -contains 'ExitCode') { $exitCode = $Verdict.ExitCode }
        if ($Verdict.PSObject.Properties.Name -contains 'coverageComplete') { $coverageComplete = [bool]$Verdict.coverageComplete }
        if ($Verdict.PSObject.Properties.Name -contains 'testsPassed') { $testsPassed = [bool]$Verdict.testsPassed }
    }
    $fresh = ConvertTo-TestPhaseExecutedChunk -Chunk $Chunk -RunId $RunId -Attempt $Attempt -Suite $Suite -HostWallSeconds $HostWallSeconds -TrxMissing $trxMissing -PassedCount $passed -Outcomes @($outcomeRows) -Trx (ConvertTo-TestPhaseRelativePath -LogRoot $LogRoot -Path $TrxPath) -TrxDigest $digest -ExitCode $exitCode -CoverageComplete $coverageComplete -TestsPassed $testsPassed
    $persistent = $false
    $bound = $null
    if ($null -ne $script:TestPhaseExecutedContext) {
        $existingCtx = $script:TestPhaseExecutedContext
        if (-not $existingCtx.Closed -and -not $existingCtx.Refused) {
            $bound = $existingCtx
            $persistent = $true
        }
    }
    $savedContext = $script:TestPhaseExecutedContext
    $savedTiming = $script:QualificationTimingRoot
    $opened = $null
    try {
        if (-not $persistent) {
            $opened = Open-TestPhaseExecutedContext -LogRoot $LogRoot -RunId $RunId -Attempt $Attempt -Suite $Suite -Source 'unknown'
            if ($null -eq $opened -or $opened.Refused) {
                Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
                return
            }
            $bound = $opened
        }
        $committed = $false
        try {
            $committed = Add-TestPhaseExecutedChunk -Context $bound -Fresh $fresh -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt
        } catch {
            Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
            $committed = $false
        }
        if (-not $persistent) {
            $project = $committed -or $bound.HasSeed -or $bound.Unresolved -or ($bound.Gaps.Count -gt 0) -or ($bound.Records.Count -gt 0)
            if ($project) {
                Complete-TestPhaseExecutedProjection -Context $bound | Out-Null
            }
        }
    } finally {
        if ($null -ne $opened) {
            Close-TestPhaseExecutedContext -Context $opened
            $script:TestPhaseExecutedContext = $savedContext
            $script:QualificationTimingRoot = $savedTiming
        }
    }
}

function Complete-TestPhaseLaunch {
    param(
        $Verdict,
        [string]$LogRoot,
        [string]$TrxPath,
        [string]$Suite,
        [string]$Chunk,
        [string]$RunId,
        [string]$Attempt,
        [double]$HostWallSeconds = 0
    )
    try {
        if ($script:TestPhaseSeams -and $script:TestPhaseSeams.Summary) {
            & $script:TestPhaseSeams.Summary
        }
        Write-TestPhaseExecutedManifest -Verdict $Verdict -LogRoot $LogRoot -TrxPath $TrxPath -Suite $Suite -Chunk $Chunk -RunId $RunId -Attempt $Attempt -HostWallSeconds $HostWallSeconds
        $live = $script:TestPhaseExecutedContext
        if ($null -ne $live -and -not $live.Closed -and -not $live.Refused) {
            Complete-TestPhaseExecutedProjection -Context $live | Out-Null
        }
        $planned = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $LogRoot) 'groups-planned.json'
        if (Test-NightlyEvidencePath $planned) {
            Invoke-TestPhaseRunSummary -RunDirectory $LogRoot -Top 50
        }
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $LogRoot -Code 'phase-summary-failed'
    }
    return $Verdict
}

function ConvertTo-TestPhaseSummaryRows {
    param($Planned, [string]$RunDirectory)
    # The row loops are compiled (NightlyCore.cs TestPhaseSummary, TestPhaseJournalRead, TestPhaseSummaryFast).
    Assert-NightlyCoreAvailable 'ConvertTo-TestPhaseSummaryRows'
    $quality = New-Object System.Collections.Generic.List[string]
    $torn = 0
    $malformed = 0
    $watch = New-Object System.Diagnostics.Stopwatch
    if ($null -ne $script:TestPhaseSummaryReReadWatch) { $watch = $script:TestPhaseSummaryReReadWatch }
    $budget = [pscustomobject]@{
        Watch = $watch
        Milliseconds = [int](Get-TestPhaseSummaryBudgetMilliseconds)
        BytesRemaining = 8388608
    }
    $plannedDocs = @(Read-TestPhasePlannedDocuments -RunDirectory $RunDirectory)
    $shards = @()
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    Sync-NightlyDeferredEvidenceWrites
    if (Test-Path -LiteralPath $timing) {
        $shards = @(Get-ChildItem -LiteralPath $timing -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
        foreach ($chunkDir in @(Get-ChildItem -LiteralPath $timing -Force -Filter 'chunk-*' -ErrorAction SilentlyContinue)) {
            $kind = [Antiphon.Nightly.QualificationTiming]::DirectoryKind($chunkDir.FullName)
            if ($kind -eq 'symlink') {
                if (-not $quality.Contains('chunk-link-refused')) { $quality.Add('chunk-link-refused') }
                continue
            }
            if ($kind -ne 'directory') { continue }
            $shards += @(Get-ChildItem -LiteralPath $chunkDir.FullName -Filter 'cases-*.jsonl' -File -ErrorAction SilentlyContinue)
        }
    }
    $shards = @($shards | Sort-Object { [string]$_.Name })
    $completes = @{}
    $starts = @{}
    $fixtures = @()
    $seenComplete = @{}
    foreach ($shard in $shards) {
        $parsed = Read-TestPhaseJsonlForSummary -Path $shard.FullName -BudgetState $budget
        $torn += [int]$parsed.Torn
        $malformed += [int]$parsed.Malformed
        $header = $null
        foreach ($rec in @($parsed.Records)) {
            if ([string]$rec.recordKind -eq 'header') { $header = $rec; break }
        }
        $planForShard = $Planned
        $headerSuite = ''
        if ($null -ne $header -and $header.PSObject.Properties.Name -contains 'suite') { $headerSuite = [string]$header.suite }
        if (-not [string]::IsNullOrWhiteSpace($headerSuite)) {
            foreach ($plannedDoc in @($plannedDocs)) {
                if ($null -eq $plannedDoc) { continue }
                $docSuite = ''
                if ($plannedDoc.PSObject.Properties.Name -contains 'suite') { $docSuite = [string]$plannedDoc.suite }
                if ($docSuite -eq $headerSuite) { $planForShard = $plannedDoc; break }
            }
        }
        $stale = $false
        if ($null -ne $header -and $null -ne $planForShard) {
            if (-not [string]::IsNullOrWhiteSpace([string]$planForShard.runId) -and [string]$header.runId -ne [string]$planForShard.runId) { $stale = $true }
            if (-not [string]::IsNullOrWhiteSpace([string]$planForShard.assembly) -and [string]$header.assembly -ne [string]$planForShard.assembly) { $stale = $true }
        }
        if ($stale) {
            if (-not $quality.Contains('stale-input-not-joined')) { $quality.Add('stale-input-not-joined') }
            continue
        }
        foreach ($rec in @($parsed.Records)) {
            $kind = [string]$rec.recordKind
            if ($kind -eq 'fixture') { $fixtures += $rec; continue }
            $id = [string]$rec.testId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $evidenceKey = Get-TestPhaseEvidenceKey -Header $header -TestId $id
            if ($kind -eq 'start') { $starts[$evidenceKey] = $rec; continue }
            if ($kind -ne 'complete') { continue }
            if ($seenComplete.ContainsKey($evidenceKey)) {
                $prior = $completes[$evidenceKey]
                if ([string]$prior.Record.outcome -ne [string]$rec.outcome) {
                    $prior.Conflict = $true
                    if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
                }
            } else {
                $seenComplete[$evidenceKey] = $true
                $completes[$evidenceKey] = [pscustomobject]@{ Record = $rec; Header = $header; Conflict = $false }
            }
        }
    }
    if ($torn -gt 0 -and -not $quality.Contains('torn-tail')) { $quality.Add('torn-tail') }
    if ($malformed -gt 0 -and -not $quality.Contains('malformed-line')) { $quality.Add('malformed-line') }

    $trxGroups = @{}
    $trxSuites = @{}
    $journalPrefix = $null
    $currentPath = Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) 'current.json'
    $hasJournal = (Get-TestPhasePathIdentity -Path $currentPath).Kind -eq 'regular'
    if ($hasJournal) {
        $current = Read-TestPhaseJsonFile -Path $currentPath
        if ($null -ne $current -and -not [string]::IsNullOrWhiteSpace([string]$current.generation)) {
            $journalPrefix = Read-TestPhaseGenerationPrefix -GenerationDir (Join-Path (Get-TestPhaseJournalRoot -TimingRoot $timing) ([string]$current.generation))
            # Use the validated seed and retained records, including predecessor rows.
            # This read-only context never acquires writer ownership or projects files.
            $snapshot = New-TestPhaseRefusedContext -Reason ''
            $snapshot.Refused = $false
            $snapshot.TimingRoot = $timing
            $snapshot.Generation = [string]$current.generation
            $snapshot.Records = $journalPrefix.Records
            [void](Read-TestPhaseSeedCache -Context $snapshot -Generation ([string]$current.generation))
            $reduced = [Antiphon.Nightly.TestPhaseJournalRead]::Reduce($snapshot)
            for ($entryIndex = 0; $entryIndex -lt $reduced.Entries.Count; $entryIndex++) {
                $entry = $reduced.Entries[$entryIndex]
                $rowJson = [string]$entry.Json
                if ($entryIndex -eq ($reduced.Entries.Count - 1)) { $rowJson = [string]$entry.Fresh }
                if ([string]::IsNullOrWhiteSpace($rowJson)) { continue }
                # Only trx, chunk and suite are read, from a strict parse of the document; a document the core
                # cannot read (not one strict JSON object, a non-string value) is skipped like an unparseable one.
                $fields = [Antiphon.Nightly.TestPhaseJournalRead]::ChunkFields($rowJson)
                if ($null -eq $fields) { continue }
                $trxRel = [string]$fields[0]
                if ([string]::IsNullOrWhiteSpace($trxRel)) { continue }
                $fullTrx = [System.IO.Path]::GetFullPath((Join-Path $RunDirectory $trxRel))
                $trxGroups[$fullTrx] = [string]$fields[1]
                $trxSuites[$fullTrx] = [string]$fields[2]
            }
        }
    }
    $executedPath = Join-Path $timing 'groups-executed.json'
    if ($null -eq $journalPrefix -and (Get-TestPhasePathIdentity -Path $executedPath).Kind -eq 'regular') {
        $executed = (Read-NightlyEvidenceText $executedPath) | ConvertFrom-Json
        foreach ($chunk in @($executed.chunks)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$chunk.trx)) {
                $fullTrx = [System.IO.Path]::GetFullPath((Join-Path $RunDirectory ([string]$chunk.trx)))
                $trxGroups[$fullTrx] = [string]$chunk.chunk
                $suiteText = ''
                if ($chunk.PSObject.Properties.Name -contains 'suite') { $suiteText = [string]$chunk.suite }
                $trxSuites[$fullTrx] = $suiteText
            }
        }
    }
    $trxRows = New-Object System.Collections.Generic.List[object]
    $trxFiles = @(Get-ChildItem -LiteralPath $RunDirectory -Filter '*.trx' -File -ErrorAction SilentlyContinue)
    foreach ($trx in $trxFiles) {
        if ((Get-TestPhasePathIdentity -Path $trx.FullName).Kind -ne 'regular') { continue }
        $trxRead = Read-TestPhaseTrxRows -Path $trx.FullName
        if ($null -eq $trxRead -or -not $trxRead.Complete -or $trxRead.Refused) { continue }
        $group = ''
        if ($trxGroups.ContainsKey($trx.FullName)) { $group = [string]$trxGroups[$trx.FullName] }
        $suiteText = ''
        if ($trxSuites.ContainsKey($trx.FullName)) { $suiteText = [string]$trxSuites[$trx.FullName] }
        [Antiphon.Nightly.TestPhaseSummary]::TagRows(@($trxRead.Rows), $group, $suiteText)
        $trxRows.AddRange([object[]]@($trxRead.Rows))
    }
    $nativeMap = [Antiphon.Nightly.TestPhaseSummary]::MapNative($trxRows.ToArray())

    $tests = New-Object System.Collections.Generic.List[object]
    $joinedTrx = @{}
    $completeTestIds = @{}
    foreach ($completeKey in @($completes.Keys)) {
        $completeTestIds[[string]$completes[$completeKey].Record.testId] = $true
    }
    foreach ($completeKey in @($completes.Keys)) {
        $bag = $completes[$completeKey]
        $id = [string]$bag.Record.testId
        $headerSuite = ''
        if ($null -ne $bag.Header -and $bag.Header.PSObject.Properties.Name -contains 'suite') {
            $headerSuite = [string]$bag.Header.suite
        }
        $matches = @()
        if ($nativeMap.ContainsKey([string]$id)) { $matches = @($nativeMap[[string]$id].ToArray()) }
        $filtered = New-Object System.Collections.Generic.List[object]
        foreach ($match in $matches) {
            $trxSuite = ''
            if ($match.PSObject.Properties.Name -contains 'Suite') { $trxSuite = [string]$match.Suite }
            $bothSuites = (-not [string]::IsNullOrWhiteSpace($headerSuite)) -and (-not [string]::IsNullOrWhiteSpace($trxSuite))
            if ($bothSuites -and ($headerSuite -ne $trxSuite)) { continue }
            $filtered.Add($match)
        }
        $matches = @($filtered.ToArray())
        $distinct = @{}
        foreach ($match in $matches) { $distinct[[string]$match.TestId + '|' + [string]$match.Path] = $match }
        $matchRows = @($distinct.Values)
        $joined = $matchRows.Count -eq 1 -and -not $bag.Conflict
        $trxOutcome = ''
        $phaseOutcome = ''
        $duration = $null
        if ($bag.Conflict) {
            $phaseOutcome = 'conflict'
            if (-not $quality.Contains('conflicting-duplicate')) { $quality.Add('conflicting-duplicate') }
        } else {
            $phaseOutcome = [string]$bag.Record.outcome
        }
        if ($matchRows.Count -gt 1) {
            $joined = $false
            if (-not $quality.Contains('ambiguous-id')) { $quality.Add('ambiguous-id') }
        } elseif ($matchRows.Count -eq 1) {
            $trxOutcome = [string]$matchRows[0].Outcome
            $duration = [string]$matchRows[0].Duration
            $joinedTrx[[string]$matchRows[0].TestId + '|' + [string]$matchRows[0].Path] = $true
        }
        $outcome = 'incomplete'
        $incomplete = $true
        if ($joined) {
            if ($trxOutcome -eq $phaseOutcome -and -not [string]::IsNullOrWhiteSpace($trxOutcome)) {
                $outcome = $trxOutcome
                $incomplete = $false
            } else {
                $outcome = 'incomplete'
                if (-not $quality.Contains('outcome-disagreement')) { $quality.Add('outcome-disagreement') }
            }
        } else {
            if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        }
        $src = $bag.Record
        $ex = $src.exclusive
        $tests.Add([ordered]@{
            testId = [string]$id
            suite = [string]$headerSuite
            className = [string]$src.className
            method = [string]$src.method
            duration = $duration
            groupId = [string]$bag.Header.groupId
            outcome = $outcome
            trxOutcome = $trxOutcome
            phaseOutcome = $phaseOutcome
            joined = [bool]$joined
            incomplete = [bool]$incomplete
            countsKnown = $true
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
            overlapMs = $(if ($src.overlapMs) { [long]$src.overlapMs } else { 0 })
            remainderMs = $(if ($src.remainderMs) { [long]$src.remainderMs } else { 0 })
            droppedPhaseCount = $(if ($src.droppedPhaseCount) { [int]$src.droppedPhaseCount } else { 0 })
        })
    }
    foreach ($startKey in @($starts.Keys)) {
        if ($completes.ContainsKey([string]$startKey)) { continue }
        $startRec = $starts[$startKey]
        $startId = [string]$startRec.testId
        $startSuite = ''
        if (-not $quality.Contains('missing-terminal-remains-incomplete')) { $quality.Add('missing-terminal-remains-incomplete') }
        $tests.Add([ordered]@{
            testId = [string]$startId
            suite = [string]$startSuite
            className = ''
            method = ''
            duration = $null
            groupId = $null
            outcome = 'incomplete'
            trxOutcome = ''
            phaseOutcome = ''
            joined = $false
            incomplete = $true
            countsKnown = $false
            db = 0; host = 0; process = 0; fs = 0; network = 0; fixture = 0
            overlapMs = 0; remainderMs = 0; droppedPhaseCount = 0
        })
    }
    $unjoined = [Antiphon.Nightly.TestPhaseSummary]::UnjoinedTrxRows($trxRows.ToArray(), $joinedTrx, $completeTestIds)
    $tests.AddRange([object[]]@($unjoined.Rows))
    if ($unjoined.PassedWithoutPhase -and -not $quality.Contains('missing-phase-unknown')) { $quality.Add('missing-phase-unknown') }
    $shared = @()
    foreach ($fix in $fixtures) {
        $ex = $fix.exclusive
        $shared += [ordered]@{
            fixtureId = [string]$fix.fixtureId
            db = $(if ($ex) { [long]$ex.db } else { 0 })
            host = $(if ($ex) { [long]$ex.host } else { 0 })
            process = $(if ($ex) { [long]$ex.process } else { 0 })
            fs = $(if ($ex) { [long]$ex.fs } else { 0 })
            network = $(if ($ex) { [long]$ex.network } else { 0 })
            fixture = $(if ($ex) { [long]$ex.fixture } else { 0 })
        }
    }
    foreach ($token in @(Get-TestPhasePublicationQuality -RunDirectory $RunDirectory -KnownPrefix $journalPrefix)) {
        if (-not $quality.Contains([string]$token)) { $quality.Add([string]$token) }
    }
    $complete = $quality.Count -eq 0
    if ([Antiphon.Nightly.TestPhaseSummaryFast]::AnyIncomplete($tests)) { $complete = $false }
    $spent = 0
    if ($null -ne $budget.Watch) { $spent = [int]$budget.Watch.ElapsedMilliseconds }
    return [pscustomobject]@{
        Quality = @($quality.ToArray())
        Torn = $torn
        Malformed = $malformed
        Tests = $tests.ToArray()
        Shared = @($shared)
        Complete = [bool]$complete
        SummaryAggregationBudgetMilliseconds = [int]$budget.Milliseconds
        SummaryAggregationMilliseconds = [int]$spent
    }
}

function Write-TestPhaseSummaryFiles {
    param($Planned, $Report, [string]$RunDirectory, [int]$Top)
    # The sorts, summary.json and the tests.csv rows are compiled (TestPhaseSummaryWrite.cs, TestPhaseSummaryFast).
    Assert-NightlyCoreAvailable 'Write-TestPhaseSummaryFiles'
    $timing = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    if (-not (Test-Path -LiteralPath $timing)) {
        New-Item -ItemType Directory -Path $timing -Force | Out-Null
    }
    # @($Report.Tests | Sort-Object { [string]$_.testId })
    $orderedTests = [Antiphon.Nightly.TestPhaseSummaryWrite]::SortByTestId($Report.Tests)
    $aggregationBudget = Get-TestPhaseSummaryBudgetMilliseconds
    $aggregationSpent = 0
    if ($null -ne $Report -and $Report.PSObject.Properties.Name -contains 'SummaryAggregationBudgetMilliseconds' -and $null -ne $Report.SummaryAggregationBudgetMilliseconds) {
        $aggregationBudget = [int]$Report.SummaryAggregationBudgetMilliseconds
    }
    if ($null -ne $Report -and $Report.PSObject.Properties.Name -contains 'SummaryAggregationMilliseconds' -and $null -ne $Report.SummaryAggregationMilliseconds) {
        $aggregationSpent = [int]$Report.SummaryAggregationMilliseconds
    }
    $summary = [ordered]@{
        version = 1
        complete = [bool]$Report.Complete
        quality = @($Report.Quality)
        tornLines = [int]$Report.Torn
        malformedLines = [int]$Report.Malformed
        summaryAggregationBudgetMilliseconds = [int]$aggregationBudget
        summaryAggregationMilliseconds = [int]$aggregationSpent
        tests = @($orderedTests)
        sharedFixtures = @($Report.Shared)
    }
    # Diagnostic summary only: avoid hundreds of thousands of PowerShell
    # recursive serializer calls for a complete native roster. Authority writers
    # retain their original canonical serializer and exact bytes.
    # ConvertTo-Json -InputObject $summary -Depth 50 -Compress
    Write-TestPhaseTextAtomic -Path (Join-Path $timing 'summary.json') -Text ([Antiphon.Nightly.TestPhaseSummaryWrite]::SummaryJson($summary))

    $headers = @('testId','duration','groupId','outcome','trxOutcome','phaseOutcome','joined','incomplete','db','host','process','fs','network','fixture','overlapMs','remainderMs','droppedPhaseCount')
    $lines = New-Object System.Collections.Generic.List[string]
    $headCells = @()
    foreach ($h in $headers) { $headCells += ([Antiphon.Nightly.TestPhaseSummary]::Cell($h)) }
    $lines.Add(($headCells -join ','))
    $lines.AddRange([Antiphon.Nightly.TestPhaseSummaryFast]::Rows($orderedTests, [string[]]$headers))
    Write-NightlyEvidenceLines (Join-Path $timing 'tests.csv') $lines.ToArray()

    $limit = $Top
    if ($limit -lt 1) { $limit = 1 }
    # @($orderedTests | Sort-Object { -1 * ([long]$_.db + ... + [long]$_.remainderMs) })
    $ranked = [Antiphon.Nightly.TestPhaseSummaryWrite]::RankByTotal($orderedTests)
    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('# Phase timing')
    $md.Add('')
    $md.Add('TRX remains authoritative. Sidecars are private and are not gate inputs.')
    $md.Add('')
    $md.Add('| test | outcome | exclusive+overlap+remainder |')
    $md.Add('| --- | --- | --- |')
    $shown = 0
    foreach ($test in $ranked) {
        if ($shown -ge $limit) { break }
        $shown++
        $total = [long]$test.db + [long]$test.host + [long]$test.process + [long]$test.fs + [long]$test.network + [long]$test.fixture + [long]$test.overlapMs + [long]$test.remainderMs
        $md.Add('| ' + (ConvertTo-TestPhaseMarkdownCell -Value $test.testId) + ' | ' + (ConvertTo-TestPhaseMarkdownCell -Value $test.outcome) + ' | ' + (ConvertTo-TestPhaseMarkdownCell -Value $total) + ' |')
    }
    $md.Add('')
    $md.Add('Shared fixture rows are separate from per-test totals.')
    Write-NightlyEvidenceLines (Join-Path $timing 'summary.md') $md.ToArray()
}

function Invoke-TestPhaseRunSummary {
    param([string]$RunDirectory, [int]$Top = 50)
    $plannedPath = Join-Path (Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory) 'groups-planned.json'
    $docs = @(Read-TestPhasePlannedDocuments -RunDirectory $RunDirectory)
    if ($docs.Count -lt 1) { return }
    try {
        # A historical singleton is one suite. It stays the stale-check document
        # and is not treated as the membership of every other suite.
        $planned = $docs[0]
        if ((Get-TestPhasePathIdentity -Path $plannedPath).Kind -eq 'regular') {
            $planned = (Read-NightlyEvidenceText $plannedPath) | ConvertFrom-Json
        }
        $report = ConvertTo-TestPhaseSummaryRows -Planned $planned -RunDirectory $RunDirectory
        Write-TestPhaseSummaryFiles -Planned $planned -Report $report -RunDirectory $RunDirectory -Top $Top
    } catch {
        Write-TestPhaseFailureMarker -RunDirectory $RunDirectory -Code 'phase-summary-failed'
    }
}

function Get-TestPhaseEvidenceKey {
    param($Header, [string]$TestId)
    $suite = ''
    if ($null -ne $Header -and $Header.PSObject.Properties.Name -contains 'suite') {
        $suite = [string]$Header.suite
    }
    if (-not [string]::IsNullOrWhiteSpace($suite)) { return ($suite + '|' + [string]$TestId) }
    return [string]$TestId
}

function Write-TestPhaseOwnerObservation {
    param($Verdict, [string]$LogRoot, [string]$Suite, [string]$Chunk, [string]$RunId = '', [string]$Attempt = '')
    try {
        Assert-NightlyCoreAvailable 'Write-TestPhaseOwnerObservation'
        # The document (or null: nothing to write) from TestPhaseRecords.OwnerDocument; naming, the
        # existing-file refusal and the write stay here.
        $doc = [Antiphon.Nightly.TestPhaseRecords]::OwnerDocument($Verdict, $Suite, $Chunk, $RunId, $Attempt)
        if ($null -eq $doc) { return }
        $dir = Get-TestPhaseTimingDirectory -RunDirectory $LogRoot
        $path = Join-Path $dir ('owner-' + $Suite + '-' + $Chunk + '-' + [string]$doc['runId'] + '-' + [string]$doc['attempt'] + '.json')
        if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') { return }
        Write-TestPhaseTextAtomic -Path $path -Text ([Antiphon.Nightly.TestPhaseJson]::Text($doc, 0))
    } catch {
        # Owner stamps are diagnostic. A write failure must not change the verdict.
    }
}

function Update-TestPhasePlannedIndex {
    param(
        [string]$Directory,
        [string]$Suite,
        [string]$FileName,
        [string]$RunId,
        [string]$Attempt,
        [string]$AssemblyId,
        [string]$SourceDigest = '',
        [string]$PolicyDigest = '',
        [string]$CostFileDigest = '',
        [string]$AssemblyDigest = '',
        $Filters = $null
    )
    if ($Suite -notmatch '^[a-z0-9][a-z0-9-]{0,40}$') { return }
    if ($FileName -notmatch '^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$') { return }
    $path = Join-Path $Directory 'groups-planned-index.json'
    $kept = New-Object System.Collections.Generic.List[object]
    if ((Get-TestPhasePathIdentity -Path $path).Kind -eq 'regular') {
        $existing = (Read-NightlyEvidenceText $path) | ConvertFrom-Json
        if ($existing.PSObject.Properties.Name -contains 'suites' -and $null -ne $existing.suites) {
            foreach ($row in @($existing.suites)) {
                if ($null -eq $row) { continue }
                if ([string]$row.suite -eq $Suite) { continue }
                $keptFilters = New-Object System.Collections.Generic.List[string]
                if ($row.PSObject.Properties.Name -contains 'filters' -and $null -ne $row.filters) {
                    foreach ($item in @($row.filters)) { $keptFilters.Add([string]$item) }
                }
                $keptSource = ''
                $keptPolicy = ''
                $keptCost = ''
                $keptAssemblyDigest = ''
                if ($row.PSObject.Properties.Name -contains 'sourceDigest' -and $null -ne $row.sourceDigest) { $keptSource = [string]$row.sourceDigest }
                if ($row.PSObject.Properties.Name -contains 'policyDigest' -and $null -ne $row.policyDigest) { $keptPolicy = [string]$row.policyDigest }
                if ($row.PSObject.Properties.Name -contains 'costFileDigest' -and $null -ne $row.costFileDigest) { $keptCost = [string]$row.costFileDigest }
                if ($row.PSObject.Properties.Name -contains 'assemblyDigest' -and $null -ne $row.assemblyDigest) { $keptAssemblyDigest = [string]$row.assemblyDigest }
                $kept.Add([ordered]@{
                    suite = [string]$row.suite
                    file = [string]$row.file
                    runId = [string]$row.runId
                    attempt = [string]$row.attempt
                    assembly = [string]$row.assembly
                    sourceDigest = $keptSource
                    policyDigest = $keptPolicy
                    costFileDigest = $keptCost
                    assemblyDigest = $keptAssemblyDigest
                    filters = @($keptFilters.ToArray())
                })
            }
        }
    }
    $freshFilters = New-Object System.Collections.Generic.List[string]
    if ($null -ne $Filters) {
        foreach ($item in @($Filters)) { $freshFilters.Add([string]$item) }
    }
    $kept.Add([ordered]@{
        suite = [string]$Suite
        file = [string]$FileName
        runId = [string]$RunId
        attempt = [string]$Attempt
        assembly = [string]$AssemblyId
        sourceDigest = [string]$SourceDigest
        policyDigest = [string]$PolicyDigest
        costFileDigest = [string]$CostFileDigest
        assemblyDigest = [string]$AssemblyDigest
        filters = @($freshFilters.ToArray())
    })
    $doc = [ordered]@{
        version = 1
        runId = [string]$RunId
        attempt = [string]$Attempt
        suites = @($kept.ToArray())
    }
    Write-TestPhaseTextAtomic -Path $path -Text ([Antiphon.Nightly.TestPhaseJson]::Text($doc, 0))
}

function Read-TestPhasePlannedDocuments {
    param([string]$RunDirectory)
    $dir = Get-TestPhaseTimingDirectory -RunDirectory $RunDirectory
    $docs = New-Object System.Collections.Generic.List[object]
    $indexPath = Join-Path $dir 'groups-planned-index.json'
    if ((Get-TestPhasePathIdentity -Path $indexPath).Kind -eq 'regular') {
        $index = (Read-NightlyEvidenceText $indexPath) | ConvertFrom-Json
        if ($index.PSObject.Properties.Name -contains 'suites' -and $null -ne $index.suites) {
            foreach ($row in @($index.suites)) {
                if ($null -eq $row) { continue }
                $file = ''
                if ($row.PSObject.Properties.Name -contains 'file') { $file = [string]$row.file }
                if ($file -notmatch '^groups-planned-[a-z0-9][a-z0-9-]{0,40}\.json$') { continue }
                $docPath = Join-Path $dir $file
                if ((Get-TestPhasePathIdentity -Path $docPath).Kind -ne 'regular') { continue }
                $docs.Add(((Read-NightlyEvidenceText $docPath) | ConvertFrom-Json))
            }
        }
    }
    if ($docs.Count -eq 0) {
        $singleton = Join-Path $dir 'groups-planned.json'
        if ((Get-TestPhasePathIdentity -Path $singleton).Kind -eq 'regular') {
            $docs.Add(((Read-NightlyEvidenceText $singleton) | ConvertFrom-Json))
        }
    }
    return @($docs.ToArray())
}

function Initialize-TestPhaseReadWarmup {
    # Compile the bounded readers before a measured call. A fresh process
    # otherwise spends the read budget on the first invocation. The chunk
    # timing readers (Antiphon.Nightly.QualificationTiming) warm themselves.
    if ($script:TestPhaseReadWarmed) { return }
    $script:TestPhaseReadWarmed = $true
    $dir = $null
    try {
        $dir = Join-Path ([IO.Path]::GetTempPath()) ('antiphon-timing-warm-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
        $path = Join-Path $dir 'cases-0-warm.jsonl'
        $bytes = New-Object byte[] 262145
        [IO.File]::WriteAllBytes($path, $bytes)
        $null = Read-TestPhaseJsonlBounded -Path $path -MaxBytes 262144 -MaxLines 4000 -DeadlineMilliseconds 200
        $trx = Join-Path $dir 'warm.trx'
        [System.IO.File]::WriteAllText($trx, '<TestRun><Results><UnitTestResult testName="warm" duration="00:00:01" outcome="Passed" /></Results></TestRun>')
        $null = Read-TestPhaseTrxRows -Path $trx
    } catch {
    } finally {
        if (-not [string]::IsNullOrWhiteSpace($dir) -and (Test-Path -LiteralPath $dir)) {
            Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# The warm-up runs on first use, not at load: each reader that starts a read deadline calls
# Initialize-TestPhaseReadWarmup before its clock starts. A process that loads this file and never reads evidence
# (the heartbeat, most nightly helpers) does not spend time on it.
if (-not (Get-Variable -Name TestPhaseReadWarmed -Scope Script -ErrorAction SilentlyContinue)) { $script:TestPhaseReadWarmed = $false }

$script:TestPhaseEvidenceLoading = $false
$script:TestPhaseEvidenceLoaded = $true
