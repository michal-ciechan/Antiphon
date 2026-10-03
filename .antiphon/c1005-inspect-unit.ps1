param([Parameter(Mandatory)][string]$Run)
$ErrorActionPreference='Stop'
$trx=".antiphon/checkpoints/$Run/rows/CP-2/run.trx"
[xml]$doc=Get-Content $trx -Raw
$defs=@{}; foreach($d in $doc.TestRun.TestDefinitions.UnitTest){$defs[$d.id]=$d.TestMethod.className+'.'+$d.TestMethod.name}
$actual=@{}; $outcomes=@{}; foreach($r in $doc.TestRun.Results.UnitTestResult){$key=$defs[$r.testId]; if(!$key){throw "no definition $($r.testId)"}; $actual[$key]++; $outcomes[$key] += @($r.outcome)}
$excluded=@('windows_quick_row_finishes_beside_a_slow_row','windows_row_arguments_round_trip_intact','windows_chatty_row_drains_interleaved_stdout_and_stderr','windows_row_timeout_kills_the_start_b_grandchild','C665_LockedFileMidDeleteResumesOnLaterPass','C721_HeldHandleDuringCleanupStaysRegisteredOrRecorded')
$expected=@{}; foreach($r in (Import-Csv .antiphon/c1005-unit-roster.csv)){if($r.Method -notin $excluded){$expected[$r.Class+'.'+$r.Method]=[int]$r.Cases}}
$bad=@(); foreach($key in $expected.Keys){if($actual[$key] -ne $expected[$key]){$bad += "expected $key $($expected[$key]) actual $($actual[$key])"}};foreach($key in $actual.Keys){if(!$expected.ContainsKey($key)){$bad += "unexpected $key $($actual[$key])"}}
'COUNTERS'; $doc.TestRun.ResultSummary.Counters | Format-List
"ROSTER expectedIdentities=$($expected.Count) actualIdentities=$($actual.Count) expectedCases=$(($expected.Values|Measure-Object -Sum).Sum) actualCases=$(($actual.Values|Measure-Object -Sum).Sum) mismatches=$($bad.Count)"
$bad
[xml]$focus=Get-Content .antiphon/checkpoints/20261003-174048-d24f/rows/CP-1/run.trx -Raw
'FOCUSED FULL CLASSES / METHODS'
foreach($group in ($focus.TestRun.TestDefinitions.UnitTest | Group-Object {$_.TestMethod.className})){
$keys=@($group.Group|ForEach-Object {$_.TestMethod.className+'.'+$_.TestMethod.name}|Sort-Object -Unique)
$expectedCases=$group.Count; $cases=0; $passed=0
foreach($key in $keys){$cases += $actual[$key]; $passed += @($outcomes[$key] | Where-Object {$_ -eq 'Passed'}).Count; if(!$actual[$key]){$bad += "missing focused $key"}}
"$($group.Name) cases=$cases passed=$passed expected=$expectedCases identities=$($keys.Count)"
if($cases -ne $expectedCases){$bad += "focused case count $($group.Name)"}
}
'V/R METHODS'
foreach($key in ($actual.Keys | Where-Object {$_ -like '*PlanCoverageCensusTests.*'} | Sort-Object)){"$key cases=$($actual[$key]) outcomes=$($outcomes[$key] -join ',')"}
'NON-PASS RESULTS'
foreach($r in $doc.TestRun.Results.UnitTestResult){if($r.outcome -ne 'Passed'){"$($defs[$r.testId]) $($r.outcome): $($r.Output.ErrorInfo.Message)"}}
if($bad.Count -gt 0){throw ($bad -join "`n")}
if($doc.TestRun.ResultSummary.Counters.executed -ne 4000 -or $doc.TestRun.ResultSummary.Counters.passed -ne 4000 -or $doc.TestRun.ResultSummary.Counters.failed -ne 0 -or $doc.TestRun.ResultSummary.Counters.notExecuted -ne 0){exit 1}
