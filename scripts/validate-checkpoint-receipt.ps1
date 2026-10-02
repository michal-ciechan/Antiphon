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
    [xml]$trx = Get-Content -Raw -LiteralPath $TrxPath
    $actual = Get-CheckpointRepeatEvidence -Document $trx -Requested $ExpectedRepeat -Nonce ([string]$Repeat.nonce) -MinExecuted 1 -Expect @()
    if (($actual | ConvertTo-Json -Compress -Depth 20) -cne ($Repeat | ConvertTo-Json -Compress -Depth 20)) {
        throw 'repeat_trx_disagreement'
    }
}

try {
    if ($ExpectedSourceSha -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { throw 'expected_sha_invalid' }
    if ($ExpectedRepeat -lt 1) { throw 'expected_repeat_invalid' }
    $raw = [System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath($Evidence))
    $document = [System.Text.Json.JsonDocument]::Parse($raw)
    try { Assert-NoDuplicateProperties $document.RootElement }
    finally { $document.Dispose() }
    $data = $raw | ConvertFrom-Json
    if ($null -ne $data.rows) {
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
            if ($row.exitCode -ne 0 -or $row.state -cne 'green' -or
                (-not $command -and ($row.executed -lt 1 -or $row.passed -ne $row.executed -or $row.failed -ne 0 -or $row.skipped -ne 0))) { throw 'row_failed' }
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
