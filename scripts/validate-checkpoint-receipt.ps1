#requires -Version 7.0
# CARD-0835 offline receipt qualification. Keep this file ASCII-only.
param(
    [Parameter(Mandatory = $true)] [string]$Evidence,
    [Parameter(Mandatory = $true)] [string]$ExpectedSourceSha,
    [string[]]$Rows,
    [int]$ExpectedRepeat = 1
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-source.ps1'))
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-repeat.ps1'))

function Assert-NoDuplicateProperties {
    param([System.Text.Json.JsonElement]$Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'duplicate_json_property' }
            Assert-NoDuplicateProperties $property.Value
        }
    } elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-NoDuplicateProperties $item }
    }
}

function Get-ReceiptFields {
    param([string]$Line)
    $fields = @{}
    foreach ($match in [regex]::Matches($Line, '(?:^|\s)([A-Za-z][A-Za-z0-9]*)=([^\s]*)')) {
        $key = $match.Groups[1].Value
        if ($fields.ContainsKey($key)) { throw 'duplicate_receipt_token' }
        $fields[$key] = $match.Groups[2].Value
    }
    return $fields
}

function Assert-WindowsPlacementSkips {
    param([string]$HostOs, [string]$TrxPath, [int]$Executed, [int]$Passed, [int]$Skipped)
    # Judge the recorded execution host, never the machine qualifying the receipt.
    # These are the OSDescription forms emitted by the supported non-Windows hosts.
    if ($HostOs -cnotmatch '^(?:Linux|Debian GNU/Linux|Ubuntu|Alpine Linux|Fedora Linux|Rocky Linux|AlmaLinux|CentOS Linux|CentOS Stream|Red Hat Enterprise Linux|Arch Linux|openSUSE Leap|openSUSE Tumbleweed|SUSE Linux Enterprise Server|Amazon Linux|Oracle Linux Server|NixOS|Gentoo Linux|Kali GNU/Linux|Darwin)(?: |$)') { throw 'skip_host_ineligible' }
    if ($ExpectedRepeat -ne 1) { throw 'skip_repeat_ineligible' }
    if ([string]::IsNullOrWhiteSpace($TrxPath)) { throw 'skip_trx_missing' }

    # The roster is authority only at the receipt's exact source commit. Never
    # evaluate C# or consult mutable checkout bytes. Unsupported syntax refuses.
    $rosterBytes = Get-CheckpointGitBytes (Get-Location).Path @('show', ($ExpectedSourceSha + ':tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs'))
    $roster = [Text.UTF8Encoding]::new($false, $true).GetString($rosterBytes)
    $arrays = [regex]::Matches($roster, '(?s)internal static readonly Entry\[\] WindowsOnly\s*=\s*\[(.*?)\];')
    if ($arrays.Count -ne 1) { throw 'skip_roster_invalid' }
    $remaining = $arrays[0].Groups[1].Value
    $allowed = [Collections.Generic.Dictionary[string,int]]::new([StringComparer]::Ordinal)
    $entryPattern = '^\s*new\("([A-Za-z_][A-Za-z0-9_.]*)",\s*"([A-Za-z_][A-Za-z0-9_]*)",\s*"([A-Za-z_][A-Za-z0-9_]*)",\s*([1-9][0-9]*),\s*(?:"(?:[^"\\]|\\.)*"|[A-Za-z_][A-Za-z0-9_]*)\),'
    while (-not [string]::IsNullOrWhiteSpace($remaining)) {
        $entry = [regex]::Match($remaining, $entryPattern)
        if (-not $entry.Success) { throw 'skip_roster_invalid' }
        $identity = $entry.Groups[1].Value + '.' + $entry.Groups[2].Value + '.' + $entry.Groups[3].Value
        if ($allowed.ContainsKey($identity)) { throw 'skip_roster_invalid' }
        $allowed.Add($identity, [int]$entry.Groups[4].Value)
        $remaining = $remaining.Substring($entry.Length)
    }

    $settings = [Xml.XmlReaderSettings]::new()
    $settings.CheckCharacters = $true
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.StringReader]::new((Read-CheckpointStrictText $TrxPath)), $settings)
    $trx = [Xml.XmlDocument]::new()
    $trx.XmlResolver = $null
    try { $trx.Load($reader) } finally { $reader.Dispose() }
    $ns = [Xml.XmlNamespaceManager]::new($trx.NameTable)
    $ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    $definitions = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($unit in $trx.SelectNodes('/t:TestRun/t:TestDefinitions/t:UnitTest', $ns)) {
        $methods = @($unit.SelectNodes('t:TestMethod', $ns))
        $id = $unit.GetAttribute('id')
        if (-not $id -or $methods.Count -ne 1 -or $definitions.ContainsKey($id)) { throw 'skip_trx_identity' }
        $definitions.Add($id, ($methods[0].GetAttribute('className') + '.' + $methods[0].GetAttribute('name')))
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $actualPassed = 0
    $actualSkipped = 0
    foreach ($result in $trx.SelectNodes('/t:TestRun/t:Results/t:UnitTestResult', $ns)) {
        $id = $result.GetAttribute('testId')
        if (-not $id -or -not $seen.Add($id) -or -not $definitions.ContainsKey($id)) { throw 'skip_trx_identity' }
        switch -CaseSensitive ($result.GetAttribute('outcome')) {
            'Passed' { $actualPassed++ }
            'NotExecuted' {
                $identity = $definitions[$id]
                if (-not $allowed.ContainsKey($identity) -or $allowed[$identity] -lt 1) { throw 'skip_identity_unlisted' }
                $allowed[$identity]--
                $actualSkipped++
            }
            default { throw 'skip_trx_outcome' }
        }
    }
    if ($actualPassed -ne $Passed -or $actualPassed -ne $Executed -or $actualSkipped -ne $Skipped) { throw 'skip_trx_counts' }
    $counters = @($trx.SelectNodes('/t:TestRun/t:ResultSummary/t:Counters', $ns))
    if ($counters.Count -ne 1) { throw 'skip_trx_counts' }
    $expected = @{ total = ($Executed + $Skipped); executed = $Executed; passed = $Passed; failed = 0; notExecuted = $Skipped }
    foreach ($key in $expected.Keys) {
        if ($counters[0].GetAttribute($key) -cne [string]$expected[$key]) { throw 'skip_trx_counts' }
    }
}

function Assert-RepeatReceipt {
    param($Repeat, $Fields, [string]$TrxPath, [int]$Executed, [int]$Passed, [int]$Failed, [int]$Skipped)
    if ($ExpectedRepeat -eq 1) {
        if ($null -ne $Repeat) { throw 'repeat_requires_expected_repeat' }
        return
    }
    if ($Fields.repeat -cne [string]$ExpectedRepeat -or
        $Fields.repetitions -cne ('{0}/{0}' -f $ExpectedRepeat) -or
        $Fields.hostInvocations -cne '1') { throw 'repeat_receipt_disagreement' }
    if (-not (Test-CheckpointRepeatSummary $Repeat $Executed $Passed $Failed $Skipped $ExpectedRepeat)) {
        throw 'repeat_summary_invalid'
    }
    if (-not (Test-Path -LiteralPath $TrxPath)) { throw 'repeat_trx_missing' }
    $rawTrx = Read-CheckpointText $TrxPath
    # The PowerShell XML cast admits illegal numeric references. Validate first,
    # then project the original text so existing literal newline behavior stays.
    $settings = [Xml.XmlReaderSettings]::new()
    $settings.CheckCharacters = $true
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create([IO.StringReader]::new($rawTrx), $settings)
    try { while ($reader.Read()) {} }
    finally { $reader.Dispose() }
    [xml]$trx = $rawTrx
    $actual = Get-CheckpointRepeatEvidence -Document $trx -Requested $ExpectedRepeat -Nonce ([string]$Repeat.nonce) -MinExecuted 1 -Expect @()
    if (($actual | ConvertTo-Json -Compress -Depth 20) -cne ($Repeat | ConvertTo-Json -Compress -Depth 20)) {
        throw 'repeat_trx_disagreement'
    }
}

try {
    if ($ExpectedSourceSha -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { throw 'expected_sha_invalid' }
    if ($ExpectedRepeat -lt 1) { throw 'expected_repeat_invalid' }
    $raw = (Read-CheckpointStrictText ([System.IO.Path]::GetFullPath($Evidence)))
    $document = [System.Text.Json.JsonDocument]::Parse($raw)
    try { Assert-NoDuplicateProperties $document.RootElement }
    finally { $document.Dispose() }
    $data = $raw | ConvertFrom-Json
    if ($null -ne $data.rows) {
        if ($data.cleanupRefusals -isnot [Array] -or
            @($data.cleanupRefusals | Where-Object { $_ -isnot [string] -or [string]::IsNullOrWhiteSpace($_) }).Count -gt 0) {
            throw 'cleanup_refusals_invalid'
        }
        if ($data.cleanupRefusals.Count -gt 0) { throw 'cleanup_refused' }
        if ($data.exitCode -ne 0 -or $data.verdict -cne 'GREEN' -or -not [string]::IsNullOrWhiteSpace([string]$data.reason)) { throw 'report_failed' }
        if ($data.schemaVersion -notin @(2, 3) -or $null -eq $data.source -or
            ($data.schemaVersion -eq 2 -and @($data.rows | Where-Object { $null -ne $_.repeat }).Count -gt 0) -or
            ($data.schemaVersion -eq 3 -and @($data.rows | Where-Object { $null -ne $_.repeat }).Count -eq 0)) { throw 'legacy_report' }
        $selected = @($data.rows)
        if ($null -ne $Rows -and @($Rows).Count -gt 0) {
            $ids = @(@($Rows) | ForEach-Object { ([string]$_).Split(',') } | Where-Object { $_ })
            $selected = @($selected | Where-Object { $_.id -cin $ids })
            if ($selected.Count -ne $ids.Count) { throw 'selected_rows_missing' }
        }
        if ($selected.Count -eq 0) { throw 'selected_rows_missing' }
        foreach ($row in $selected) {
            $command = -not [string]::IsNullOrWhiteSpace([string]$row.command)
            if ($row.exitCode -ne 0 -or $row.state -cne 'green') { throw 'row_failed' }
            if (-not $command -and ($row.executed -lt 1 -or $row.passed -ne $row.executed -or $row.failed -ne 0 -or $row.skipped -ne 0)) {
                if ($row.executed -lt 0 -or $row.passed -ne $row.executed -or $row.failed -ne 0 -or $row.skipped -le 0) { throw 'row_failed' }
                Assert-WindowsPlacementSkips -HostOs ([string]$data.host.os) -TrxPath ([string]$row.trx) `
                    -Executed $row.executed -Passed $row.passed -Skipped $row.skipped
            }
            if (-not (Test-CheckpointSourceEvidence $row.source $ExpectedSourceSha -AllowCommand:$command)) { throw 'row_source_ineligible' }
            if ($row.source.start.commit -cne $data.source.start.commit -or $row.source.start.fingerprint -cne $data.source.start.fingerprint -or
                ($command -and $row.source.buildSource -cne 'notApplicable') -or
                (-not $command -and $row.source.buildSource -cne $data.source.buildSource)) { throw 'row_heading_disagreement' }
            $fields = Get-ReceiptFields ([string]$row.line)
            if ($row.line -cnotmatch ('^CHECKPOINT ' + [regex]::Escape([string]$row.id) + ' ') -or
                $fields.commit -cne $ExpectedSourceSha -or $fields.source -cne $ExpectedSourceSha -or
                $fields.sourceState -cne 'clean' -or $fields.buildSource -cne $row.source.buildSource -or $fields.dirty -cne '0') { throw 'receipt_disagreement' }
            if (-not $command) {
                foreach ($key in @('executed', 'passed', 'failed', 'skipped')) {
                    if ($fields[$key] -cne [string]$row.$key) { throw 'receipt_counts' }
                }
                if ($row.skipped -gt 0 -and $row.line -cnotmatch (' trx=' + [regex]::Escape([string]$row.trx) + ' slot=')) { throw 'receipt_trx_disagreement' }
                Assert-RepeatReceipt -Repeat $row.repeat -Fields $fields -TrxPath ([string]$row.trx) `
                    -Executed $row.executed -Passed $row.passed -Failed $row.failed -Skipped $row.skipped
            }
        }
        if (-not (Test-CheckpointSourceEvidence $data.source $ExpectedSourceSha -AllowCommand)) { throw 'source_ineligible' }
    } else {
        if ($data.version -eq 2) {
            if ($ExpectedRepeat -eq 1 -or -not (Test-CheckpointSourceEvidence $data.source $ExpectedSourceSha)) { throw 'source_ineligible' }
        } elseif ($ExpectedRepeat -ne 1 -or -not (Test-CheckpointSourceEvidence $data $ExpectedSourceSha)) { throw 'source_ineligible' }
        if ($data.exitCode -ne 0 -or $data.failed -ne 0 -or $data.executed -lt 1 -or $data.passed -ne $data.executed -or $data.skipped -ne 0) { throw 'receipt_failed' }
        $fields = Get-ReceiptFields ([string]$data.receipt)
        if ($data.receipt -cnotmatch ('^CHECKPOINT ' + [regex]::Escape([string]$data.name) + ' ')) { throw 'receipt_name' }
        if ($fields.commit -cne $ExpectedSourceSha -or $fields.source -cne $ExpectedSourceSha -or $fields.dirty -cne '0' -or $fields.sourceState -cne 'clean' -or $fields.buildSource -cne 'verified') { throw 'receipt_disagreement' }
        foreach ($key in @('executed', 'passed', 'failed', 'skipped')) {
            if ($fields[$key] -cne [string]$data.$key) { throw 'receipt_counts' }
        }
        if ($data.version -eq 2) {
            Assert-RepeatReceipt -Repeat $data.repeat -Fields $fields -TrxPath ([string]$fields.trx) `
                -Executed $data.executed -Passed $data.passed -Failed $data.failed -Skipped $data.skipped
        }
    }
    Write-Host ('CHECKPOINT SOURCE VALID source={0} rows={1}' -f $ExpectedSourceSha, $(if ($null -ne $data.rows) { $selected.Count } else { 1 }))
    exit 0
} catch {
    Write-Host ('CHECKPOINT SOURCE INVALID reason={0}' -f $_.Exception.Message)
    exit 2
}
