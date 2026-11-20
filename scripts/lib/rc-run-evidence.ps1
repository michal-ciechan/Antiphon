#requires -Version 5.1
# CARD-1836 S6b shadow run evidence. Recording grants no gate credit.
# ASCII-only. Windows PowerShell 5.1 parses this file and records unproven.

if ($PSVersionTable.PSVersion.Major -ge 7) {
    if (-not (Get-Command Test-NightlyCoreAvailable -ErrorAction SilentlyContinue)) {
        . (Join-Path $PSScriptRoot 'nightly-core.ps1')
    }
    # Script scope, and only when absent. A function-scoped dot disappears on return,
    # and a second dot of nightly-coverage.ps1 returns from the caller.
    if (-not (Get-Command Read-NightlyTrxRunSummary -ErrorAction SilentlyContinue)) {
        . (Join-Path $PSScriptRoot 'nightly-watchdog-admission.ps1')
    }
    if (-not (Get-Command ConvertTo-NightlyCoverageVerdict -ErrorAction SilentlyContinue)) {
        . (Join-Path $PSScriptRoot 'nightly-coverage.ps1')
    }
}

function New-RcRunEvidenceIdle {
    param([string]$Recording)
    return [pscustomobject]@{
        effectiveMode = 'Full'
        gateCredit = $false
        coverageComplete = $false
        bindingState = 'diagnostic-only'
        recording = $Recording
        publish = $false
        joined = $false
        skipped = $false
        suite = ''
        state = 'open'
        sequence = 0
        pending = 'none'
        digest = ''
        outputs = @()
    }
}

function Test-RcRunEvidenceSuppressed {
    if ($env:ANTIPHON_RC_RUN_EVIDENCE -eq 'record') { return $false }
    $seams = $script:NightlySeams
    if (-not $seams) { return $false }
    if ($null -ne $seams.StartProcess) { return $true }
    if ($null -ne $seams.Git) { return $true }
    return $false
}

function Resolve-RcRunEvidenceRoot {
    param([string]$EvidenceRoot, [string]$LogRoot)
    if (-not [string]::IsNullOrWhiteSpace($EvidenceRoot)) { return $EvidenceRoot }
    if (-not [string]::IsNullOrWhiteSpace($LogRoot)) { return (Join-Path $LogRoot 'rc-run-evidence') }
    return ''
}

function Invoke-RcRunEvidenceCore {
    param([scriptblock]$Body, [switch]$Strict)
    if ($env:ANTIPHON_RC_RUN_EVIDENCE -eq 'off') { return (New-RcRunEvidenceIdle -Recording 'off') }
    if (Test-RcRunEvidenceSuppressed) { return (New-RcRunEvidenceIdle -Recording 'suppressed') }
    try {
        if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'nightly-core-required' }
        if (-not (Get-Command Test-NightlyCoreAvailable -ErrorAction SilentlyContinue)) { throw 'nightly-core-required' }
        if (-not (Test-NightlyCoreAvailable)) { throw 'nightly-core-required' }
        $result = & $Body
        if ($null -eq $result) { throw 'nightly-core-required' }
        if ($result -is [string]) { return ($result | ConvertFrom-Json) }
        return $result
    } catch {
        $ex = $_.Exception
        while ($ex.InnerException -and $ex.InnerException.Message) { $ex = $ex.InnerException }
        $msg = [string]$ex.Message
        if ($Strict) { throw $msg }
        Write-RcRunEvidenceWarning $msg
        return (New-RcRunEvidenceIdle -Recording 'unproven')
    }
}

function New-RcSourceSnapshot {
    param([string]$Repository, [string]$EvidenceRoot, [string[]]$InputPaths, [string]$ExpectedSha, [string]$LogRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $inputs = @($InputPaths)
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::SourceSnapshot([string]$root, [string]$Repository, [string[]]$inputs, [string]$ExpectedSha)
    }.GetNewClosure())
}

function Register-NightlyRcBuildEvidence {
    param([string]$EvidenceRoot, [string]$Suite, [string]$Command, [string]$OutputDirectory, [string]$Entry, [string]$SourceDigest, [string]$LogRoot, [string]$RepoRoot, [string]$Sha, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Build([string]$root, [string]$Suite, [string]$Command, [string]$OutputDirectory, [string]$Entry, [string]$SourceDigest, [string]$RepoRoot, [string]$Sha)
    }.GetNewClosure())
}

function Register-NightlyRcDiscoveryEvidence {
    param([string]$EvidenceRoot, [string]$Suite, [string]$LogRoot, [string]$RepoRoot, [string]$DiscoveryPath, [switch]$Carry, [switch]$Skip, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $carry = [bool]$Carry
    $skip = [bool]$Skip
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Discovery([string]$root, [string]$Suite, [string]$DiscoveryPath, $carry, $skip)
    }.GetNewClosure())
}

function Register-NightlyRcLaunchEvidence {
    param([string]$EvidenceRoot, [string]$Suite, [string]$Chunk, [string]$OutputDirectory, [string[]]$Arguments, [string]$BuildDigest, [string]$Repository, [string]$ExpectedSha, [string]$LogRoot, [string]$RepoRoot, [string]$Sha, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $repo = $Repository
    if ([string]::IsNullOrWhiteSpace($repo)) { $repo = $RepoRoot }
    $expected = $ExpectedSha
    if ([string]::IsNullOrWhiteSpace($expected)) { $expected = $Sha }
    $launchArguments = @($Arguments)
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Launch([string]$root, [string]$Suite, [string]$Chunk, [string]$OutputDirectory, [string[]]$launchArguments, [string]$BuildDigest, [string]$repo, [string]$expected)
    }.GetNewClosure())
}

function Register-NightlyRcChunkRevision {
    param([string]$EvidenceRoot, [string]$Suite, [string]$Chunk, [string]$Outcome, [string]$Reason, [string]$LogRoot, [switch]$Interrupt, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    $interrupt = [bool]$Interrupt
    $reasonText = [string]$Reason
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::ChunkRevision([string]$root, [string]$Suite, [string]$Chunk, [string]$Outcome, $interrupt, [string]$reasonText)
    }.GetNewClosure())
}

function Assert-RcRunInputsCurrent {
    param([string]$Repository, [string]$EvidenceRoot, [string]$ExpectedSha, [string]$LogRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::AssertInputs([string]$root, [string]$Repository, [string]$ExpectedSha)
    }.GetNewClosure())
}

function Read-RcRunEvidence {
    param([string]$EvidenceRoot, [string]$LogRoot, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::Read([string]$root)
    }.GetNewClosure())
}

function Protect-NightlyOwnedCheckoutEvidence {
    param([string]$Phase, [string]$CheckoutRoot, [string]$LogRoot, [string]$Sha, [string]$EvidenceRoot, [string[]]$InputPaths, [switch]$Strict)
    $root = Resolve-RcRunEvidenceRoot $EvidenceRoot $LogRoot
    if ($Phase -eq 'closure') {
        return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
            [Antiphon.Nightly.RcRunEvidence]::AssertInputs([string]$root, [string]$CheckoutRoot, [string]$Sha)
        }.GetNewClosure())
    }
    $inputs = @()
    foreach ($relative in @($InputPaths)) {
        if (-not [string]::IsNullOrWhiteSpace([string]$relative)) { $inputs += [string]$relative }
    }
    return (Invoke-RcRunEvidenceCore -Strict:$Strict -Body {
        [Antiphon.Nightly.RcRunEvidence]::SourceSnapshot([string]$root, [string]$CheckoutRoot, [string[]]$inputs, [string]$Sha)
    }.GetNewClosure())
}

function Write-RcRunEvidenceWarning {
    param([string]$Reason)
    try {
        $text = [string]$Reason
        if ([string]::IsNullOrWhiteSpace($text)) { $text = 'unproven' }
        $text = ($text -split "[\r\n]")[0].Trim()
        if ($text.Length -gt 120) { $text = $text.Substring(0, 120) }
        if (-not $script:RcRunEvidenceWarned) { $script:RcRunEvidenceWarned = @{} }
        if ($script:RcRunEvidenceWarned.ContainsKey($text)) { return }
        $script:RcRunEvidenceWarned[$text] = $true
        $line = 'rc-run-evidence: ' + $text
        if (Get-Command Write-NightlyRunLine -ErrorAction SilentlyContinue) { Write-NightlyRunLine $line }
        else { Write-Host $line }
    } catch { }
}

function Test-NightlyRcOutcomeReadersReady {
    return ((Get-Command Read-NightlyTrxRunSummary -ErrorAction SilentlyContinue) -and (Get-Command ConvertTo-NightlyCoverageVerdict -ErrorAction SilentlyContinue))
}

function Test-NightlyRcWholeNumber {
    param($Value, [ref]$Parsed)
    if ($null -eq $Value -or $Value -is [bool] -or $Value -is [System.Array]) { return $false }
    $number = 0
    $ok = [int]::TryParse([string]$Value, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$number)
    if ($ok) { $Parsed.Value = $number }
    return $ok
}

function ConvertTo-NightlyRcCheckedOutcome {
    param($Rows, [int]$ReportedTotal, [int]$ReportedExecuted, [int]$ReportedFailed, [bool]$CallerCoverageComplete, $ExpectedSkipRoster, [string]$Platform)
    # A null roster tolerates nothing. Do not load the production roster from Platform alone.
    $roster = @()
    if ($null -ne $ExpectedSkipRoster) { $roster = @($ExpectedSkipRoster) }
    $verdict = ConvertTo-NightlyCoverageVerdict -TerminalRows @($Rows) -ProcessExit 0 -Sha '' -ExpectedSha '' -GitRef '' -ExpectedRef '' -PolicyHash '' -ExpectedPolicyHash '' -Platform $Platform -ExpectedSkipRoster $roster
    $executedRows = [int]$verdict.passed + [int]$verdict.failed
    if ($ReportedExecuted -ne $executedRows -or $ReportedFailed -ne [int]$verdict.failed) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-counter-mismatch' }
    }
    if (-not (Test-NightlyCounterAgreement -Verdict $verdict -ReportedTotal $ReportedTotal -ReportedFailed $ReportedFailed)) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-counter-mismatch' }
    }
    if ([bool]$verdict.testsPassed -and [bool]$verdict.coverageComplete -and $CallerCoverageComplete -and $ReportedFailed -eq 0) {
        return [pscustomobject]@{ Outcome = 'Passed'; Reason = '' }
    }
    $first = ''
    foreach ($reason in @($verdict.reasons)) {
        $text = [string]$reason
        if (-not [string]::IsNullOrWhiteSpace($text)) { $first = $text; break }
    }
    if ([string]::IsNullOrWhiteSpace($first)) { $first = 'coverage-incomplete' }
    return [pscustomobject]@{ Outcome = 'Failed'; Reason = $first }
}

function Get-NightlyRcTrxChunkOutcome {
    param([string]$ResultsPath, $ExpectedSkipRoster, [string]$Platform)
    if (-not (Test-NightlyRcOutcomeReadersReady)) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
    }
    $doc = New-Object System.Xml.XmlDocument
    $doc.XmlResolver = $null
    try { $doc.Load($ResultsPath) } catch {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unparseable' }
    }
    if ($null -eq $doc.DocumentElement) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unparseable' }
    }
    $summary = Read-NightlyTrxRunSummary -Path $ResultsPath
    if (-not $summary.Present -or [string]::IsNullOrWhiteSpace([string]$summary.Outcome)) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    $counters = $doc.SelectSingleNode('//*[local-name()="Counters"]')
    if ($null -eq $counters -or [string]::IsNullOrWhiteSpace($counters.GetAttribute('executed')) -or [string]::IsNullOrWhiteSpace($counters.GetAttribute('failed'))) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    if ([string]$summary.Outcome -cne 'Completed') {
        return [pscustomobject]@{ Outcome = 'Failed'; Reason = 'summary-outcome' }
    }
    try { $rows = @(Read-NightlyTrxIdentities -TrxPath $ResultsPath) } catch {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
    }
    try {
        return ConvertTo-NightlyRcCheckedOutcome -Rows $rows -ReportedTotal ([int]$summary.Total) -ReportedExecuted ([int]$summary.Executed) -ReportedFailed ([int]$summary.Failed) -CallerCoverageComplete $true -ExpectedSkipRoster $ExpectedSkipRoster -Platform $Platform
    } catch {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
    }
}

function Get-NightlyRcJsonChunkOutcome {
    param($Parsed, $ExpectedSkipRoster, [string]$Platform)
    if ($null -eq $Parsed -or $Parsed -is [System.Array]) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    $names = @($Parsed.PSObject.Properties.Name)
    foreach ($required in @('executed', 'failed', 'coverageComplete', 'rows')) {
        if ($names -notcontains $required) {
            return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
        }
    }
    if ($Parsed.coverageComplete -isnot [bool]) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    $executed = 0
    $failed = 0
    if (-not (Test-NightlyRcWholeNumber -Value $Parsed.executed -Parsed ([ref]$executed))) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    if (-not (Test-NightlyRcWholeNumber -Value $Parsed.failed -Parsed ([ref]$failed))) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    if ($null -eq $Parsed.rows) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    if (-not (Test-NightlyRcOutcomeReadersReady)) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
    }
    $terminal = New-Object System.Collections.Generic.List[object]
    foreach ($row in @($Parsed.rows)) {
        if ($null -eq $row) { return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' } }
        $rowNames = @($row.PSObject.Properties.Name)
        if ($rowNames -notcontains 'Outcome' -or $rowNames -notcontains 'Identity') {
            return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
        }
        $outcome = [string]$row.Outcome
        $identity = [string]$row.Identity
        if ([string]::IsNullOrWhiteSpace($outcome) -or [string]::IsNullOrWhiteSpace($identity)) {
            return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
        }
        $trace = ''
        if ($rowNames -contains 'DebugTrace') { $trace = [string]$row.DebugTrace }
        $className = ''
        $methodName = ''
        $dot = $identity.LastIndexOf('.')
        if ($dot -gt 0 -and $dot -lt ($identity.Length - 1)) {
            $className = $identity.Substring(0, $dot)
            $methodName = $identity.Substring($dot + 1)
        }
        [void]$terminal.Add([pscustomobject]@{
            Outcome = $outcome
            Identity = $identity
            ClassName = $className
            MethodName = $methodName
            DebugTrace = $trace
            Display = $identity
            TestId = ''
            Uid = ''
        })
    }
    if ($terminal.Count -eq 0) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-summary-unreadable' }
    }
    try {
        return ConvertTo-NightlyRcCheckedOutcome -Rows $terminal.ToArray() -ReportedTotal $terminal.Count -ReportedExecuted $executed -ReportedFailed $failed -CallerCoverageComplete ([bool]$Parsed.coverageComplete) -ExpectedSkipRoster $ExpectedSkipRoster -Platform $Platform
    } catch {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unreadable' }
    }
}

function Get-NightlyRcChunkOutcome {
    # Passed uses the results-check inputs and grants no gate credit. Unreadable input is NotRun.
    param($Run, [string]$ResultsPath, $ExpectedSkipRoster = $null, [string]$Platform = '')
    $exit = 1
    if ($null -ne $Run -and $null -ne $Run.ExitCode) { $exit = [int]$Run.ExitCode }
    if ($exit -ne 0) { return [pscustomobject]@{ Outcome = 'Failed'; Reason = ('exit-' + $exit) } }
    $drain = $false
    if ($null -ne $Run) {
        if ($Run -is [System.Collections.IDictionary]) {
            if ($Run.Contains('OutputDrainTimedOut')) { $drain = [bool]$Run['OutputDrainTimedOut'] }
        } elseif ($Run.PSObject.Properties.Name -contains 'OutputDrainTimedOut') {
            $drain = [bool]$Run.OutputDrainTimedOut
        }
    }
    if ($drain) { return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'drain-timeout' } }
    if ([string]::IsNullOrWhiteSpace($ResultsPath) -or -not (Test-Path -LiteralPath $ResultsPath -PathType Leaf)) {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-missing' }
    }
    $text = ''
    try { $text = [IO.File]::ReadAllText($ResultsPath) } catch {
        return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unparseable' }
    }
    if ($text -match '<TestRun[\s>]') {
        return Get-NightlyRcTrxChunkOutcome -ResultsPath $ResultsPath -ExpectedSkipRoster $ExpectedSkipRoster -Platform $Platform
    }
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        try { $parsed = $text | ConvertFrom-Json } catch {
            return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unparseable' }
        }
        return Get-NightlyRcJsonChunkOutcome -Parsed $parsed -ExpectedSkipRoster $ExpectedSkipRoster -Platform $Platform
    }
    return [pscustomobject]@{ Outcome = 'NotRun'; Reason = 'results-unparseable' }
}


