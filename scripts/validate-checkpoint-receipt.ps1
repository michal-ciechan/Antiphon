#requires -Version 7.0
# CARD-0835 offline receipt qualification. Keep this file ASCII-only.
param(
    [Parameter(Mandatory = $true)] [string]$Evidence,
    [Parameter(Mandatory = $true)] [string]$ExpectedSourceSha,
    [string[]]$Rows
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot (Join-Path 'lib' 'checkpoint-source.ps1'))

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

try {
    if ($ExpectedSourceSha -cnotmatch '^([0-9a-f]{40}|[0-9a-f]{64})$') { throw 'expected_sha_invalid' }
    $raw = [System.IO.File]::ReadAllText([System.IO.Path]::GetFullPath($Evidence))
    $document = [System.Text.Json.JsonDocument]::Parse($raw)
    try { Assert-NoDuplicateProperties $document.RootElement }
    finally { $document.Dispose() }
    $data = $raw | ConvertFrom-Json
    if ($null -ne $data.rows) {
        if ($data.schemaVersion -ne 2 -or $null -eq $data.source) { throw 'legacy_report' }
        $selected = @($data.rows)
        if (@($Rows).Count -gt 0) {
            $ids = @(@($Rows) | ForEach-Object { ([string]$_).Split(',') } | Where-Object { $_ })
            $selected = @($selected | Where-Object { $_.id -cin $ids })
            if ($selected.Count -ne $ids.Count) { throw 'selected_rows_missing' }
        }
        if ($selected.Count -eq 0) { throw 'selected_rows_missing' }
        foreach ($row in $selected) {
            if ($row.exitCode -ne 0 -or $row.state -cne 'green') { throw 'row_failed' }
            if ($null -eq $row.source -or $row.source.state -cne 'clean' -or $row.source.buildSource -cne 'verified') { throw 'row_source_ineligible' }
            if ($row.source.start.commit -cne $data.source.start.commit -or $row.source.start.fingerprint -cne $data.source.start.fingerprint) { throw 'row_heading_disagreement' }
            $fields = Get-ReceiptFields ([string]$row.line)
            if ($fields.source -cne $ExpectedSourceSha -or $fields.sourceState -cne 'clean' -or $fields.buildSource -cne 'verified' -or $fields.dirty -cne '0') { throw 'receipt_disagreement' }
        }
        if (-not (Test-CheckpointSourceEvidence $data.source $ExpectedSourceSha)) { throw 'source_ineligible' }
    } else {
        if (-not (Test-CheckpointSourceEvidence $data $ExpectedSourceSha)) { throw 'source_ineligible' }
        if ($data.exitCode -ne 0 -or $data.failed -ne 0 -or $data.executed -lt 1 -or $data.passed -ne $data.executed -or $data.skipped -ne 0) { throw 'receipt_failed' }
        $fields = Get-ReceiptFields ([string]$data.receipt)
        if ($data.receipt -cnotmatch ('^CHECKPOINT ' + [regex]::Escape([string]$data.name) + ' ')) { throw 'receipt_name' }
        if ($fields.commit -cne $ExpectedSourceSha -or $fields.source -cne $ExpectedSourceSha -or $fields.dirty -cne '0' -or $fields.sourceState -cne 'clean' -or $fields.buildSource -cne 'verified') { throw 'receipt_disagreement' }
        foreach ($key in @('executed', 'passed', 'failed', 'skipped')) {
            if ($fields[$key] -cne [string]$data.$key) { throw 'receipt_counts' }
        }
    }
    Write-Host ('CHECKPOINT SOURCE VALID source={0} rows={1}' -f $ExpectedSourceSha, $(if ($null -ne $data.rows) { $selected.Count } else { 1 }))
    exit 0
} catch {
    Write-Host ('CHECKPOINT SOURCE INVALID reason={0}' -f $_.Exception.Message)
    exit 2
}
