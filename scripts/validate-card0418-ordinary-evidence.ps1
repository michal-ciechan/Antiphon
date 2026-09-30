# CARD-0418 R-14: bind each ordinary assertion key to a named native result.
# Run from the repository root after the Final checkpoint and supplemental rows.
param([string]$Root = '.antiphon/checkpoints/r37-final')
$ErrorActionPreference = 'Stop'
$index = Import-Csv 'docs/investigations/2026-09-30-card-0418-ordinary-index.tsv' -Delimiter '|'
$source = Get-Content 'tests/Antiphon.Tests/TestHelpers/ChannelOutboundEvidenceAccounting.cs' -Raw
$required = @([regex]::Matches($source, '"((?:V|R)-\d+/[a-z0-9-]+)"') |
    ForEach-Object { $_.Groups[1].Value })
$issues = [System.Collections.Generic.List[string]]::new()
foreach ($key in $required) {
    if (@($index | Where-Object Assertion -eq $key).Count -lt 1) { $issues.Add("$key has no index row") }
}
foreach ($row in $index) {
    if ($row.Assertion -notin $required) { $issues.Add("$($row.Assertion) is not required") }
}
foreach ($group in @($index | Group-Object Assertion, Method)) {
    if ($group.Count -gt 1) { $issues.Add("duplicate assertion/method claim: $($group.Name)") }
}
$auditRow = @($index | Where-Object Assertion -eq 'R-14/per-assertion-actual-run-index')
if ($auditRow.Count -ne 1 -or $auditRow[0].Row -ne 'AUDIT' -or $auditRow[0].Method -ne 'validated-index') {
    $issues.Add('R-14/per-assertion-actual-run-index: self-audit row must be unique and explicit')
}
$trxCache = @{}
$checked = 0
$expectedCases = @{
    'ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary' = 5
    'ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death' = 1
    'ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries' = 6
    'ChannelOutboundDeliveryTests.Gates_precede_admission_and_a_matched_companion_still_converts' = 3
    'ChannelOutboundDeadlineTests.Real_create_refusals_keep_the_original_without_provider_reroute' = 3
    'ChannelOutboundDeliveryTests.Only_acceptance_stamps_complete_actual_payload' = 4
    'ChannelOutboundDeliveryTests.Source_publication_requires_all_four_members_or_a_complete_zip' = 4
}
foreach ($row in $index) {
    if ($row.Row -eq 'AUDIT') {
        if ($row.Assertion -ne 'R-14/per-assertion-actual-run-index') {
            $issues.Add("$($row.Assertion): only the index self-audit may use AUDIT")
        }
        continue
    }
    if ($row.Row -eq 'CP-12') {
        if ($row.Method -notin @('ChannelsPage.test.tsx', 'attentionVisuals.test.ts')) {
            $issues.Add("$($row.Assertion): CP-12 claim names an unselected client file")
        }
        $clientLog = Join-Path $Root 'CP-12.log'
        if (-not (Test-Path $clientLog) -or
            -not (Select-String -Path $clientLog -Pattern '26 passed' -Quiet) -or
            -not (Select-String -Path $clientLog -SimpleMatch 'CLIENT TESTS EXIT CODE: 0' -Quiet)) {
            $issues.Add("$($row.Assertion): CP-12 native client log lacks the 26-case green receipt")
        } else { $checked++ }
        continue
    }
    if (-not $trxCache.ContainsKey($row.Row)) {
        $runs = @(Get-ChildItem $Root -Directory | Where-Object Name -Like "$($row.Row)-*" | ForEach-Object {
            Get-ChildItem $_.FullName -Filter 'run.trx' -File -ErrorAction SilentlyContinue
        })
        if ($runs.Count -ne 1) {
            $issues.Add("$($row.Row): expected one native run.trx, found $($runs.Count)")
            $trxCache[$row.Row] = @{}
        } else {
            [xml]$trx = Get-Content $runs[0].FullName -Raw
            $defs = @{}
            foreach ($def in $trx.SelectNodes('//*[local-name()="TestDefinitions"]/*[local-name()="UnitTest"]')) {
                $method = $def.SelectSingleNode('./*[local-name()="TestMethod"]')
                if ($null -ne $method) { $defs[$def.GetAttribute('id')] = $method.GetAttribute('className') + '.' + $method.GetAttribute('name') }
            }
            $outcomes = @{}
            foreach ($result in $trx.SelectNodes('//*[local-name()="Results"]/*[local-name()="UnitTestResult"]')) {
                $name = $defs[$result.GetAttribute('testId')]
                if ($null -eq $name) { continue }
                if (-not $outcomes.ContainsKey($name)) { $outcomes[$name] = @() }
                $outcomes[$name] += $result.GetAttribute('outcome')
            }
            $trxCache[$row.Row] = $outcomes
        }
    }
    $matches = @($trxCache[$row.Row].Keys | Where-Object { $_.EndsWith('.' + $row.Method, [StringComparison]::Ordinal) -or $_.EndsWith($row.Method, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) {
        $issues.Add("$($row.Assertion): expected one executed method $($row.Method) in $($row.Row), found $($matches.Count)")
        continue
    }
    $outcomes = @($trxCache[$row.Row][$matches[0]])
    if ($outcomes.Count -eq 0 -or @($outcomes | Where-Object { $_ -ne 'Passed' }).Count -gt 0) {
        $issues.Add("$($row.Assertion): non-green method $($matches[0]) ($($outcomes -join ','))")
    } elseif ($expectedCases.ContainsKey($row.Method) -and $outcomes.Count -ne $expectedCases[$row.Method]) {
        $issues.Add("$($row.Assertion): $($row.Method) executed $($outcomes.Count) cases; expected $($expectedCases[$row.Method])")
    } else { $checked++ }
}
if ($issues.Count -eq 0) {
    Write-Host "R-14 LOCAL INDEX PASS: $checked native/log claims plus self-audited index; $($required.Count) required keys present"
    exit 0
}
foreach ($issue in $issues) { Write-Host "R-14 INDEX GAP: $issue" }
Write-Host "R-14 LOCAL INDEX FAIL: $($issues.Count) gaps; $checked green native/log claims"
exit 1
