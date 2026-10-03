$ErrorActionPreference='Stop'
function Load1005([string]$path){
[xml]$d=Get-Content $path -Raw; $defs=@{};foreach($t in $d.TestRun.TestDefinitions.UnitTest){$defs[$t.id]=$t.TestMethod.className+'.'+$t.TestMethod.name}
@($d.TestRun.Results.UnitTestResult | ForEach-Object {[pscustomobject]@{Key=$defs[$_.testId];Outcome=$_.outcome;TestId=$_.testId}})
}
$first=Load1005 .antiphon/checkpoints/20261003-181207-a884/rows/CP-2/run.trx
$last=Load1005 .antiphon/checkpoints/20261003-182649-9863/rows/CP-2/run.trx
$baseline=Load1005 .antiphon/c1005-baseline-os-proof-valid/run.trx
$fixed=@($first|Where-Object {$_.Outcome -eq 'NotExecuted' -and $_.Key -like '*RemoteScriptContractTests.*'})
foreach($g in ($fixed | Group-Object Key)){$rows=@($last|Where-Object {$_.Key -eq $g.Name}); "JQ $($g.Name) first=$($g.Count) finalPassed=$(@($rows|Where-Object Outcome -eq Passed).Count)"; if($rows.Count -ne $g.Count -or @($rows|Where-Object Outcome -ne Passed).Count -ne 0){throw 'jq cases not restored'}}
$skipped=@($last|Where-Object Outcome -eq NotExecuted | Group-Object Key)
foreach($g in $skipped){$b=@($baseline|Where-Object {$_.Key -eq $g.Name -and $_.Outcome -eq 'NotExecuted'}); "OS $($g.Name) final=$($g.Count) baseline=$($b.Count)";if($b.Count -ne $g.Count){throw 'baseline mismatch'}}
"JQ_RESTORED cases=$($fixed.Count); INHERITED_WINDOWS cases=$(($skipped|Measure-Object Count -Sum).Sum) methods=$($skipped.Count)"
