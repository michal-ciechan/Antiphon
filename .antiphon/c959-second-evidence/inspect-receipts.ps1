$ErrorActionPreference='Stop'
$repo='/work/worktrees/task-818582a5'
$roots=@('.antiphon/c959-second','.antiphon/c959-second-final')
$roster=New-Object System.Collections.Generic.List[string]
$roster.Add("run	cp	class	method	outcome	testId	reason")
$summary=New-Object System.Collections.Generic.List[string]
foreach($root in $roots){foreach($run in Get-ChildItem -Directory (Join-Path $repo $root)){if(!(Test-Path (Join-Path $run.FullName 'report.json'))){continue}; foreach($p in Get-ChildItem (Join-Path $run.FullName 'rows/CP-*/run.trx') -ErrorAction SilentlyContinue){
 [xml]$x=Get-Content -Raw $p; $d=@{}; foreach($v in $x.TestRun.TestDefinitions.UnitTest){$d[$v.id]=$v.TestMethod}
 $r=@($x.TestRun.Results.UnitTestResult)
 foreach($v in $r){if(!$d.ContainsKey($v.testId)){throw 'TRX has unresolved test definition'}; $def=$d[$v.testId]; $reason=(($v.Output.ErrorInfo.Message + $v.Output.DebugTrace) -replace '[
	]+',' ');if([string]::IsNullOrEmpty($reason)){$reason='-'};$roster.Add(($run.Name,$p.Directory.Name,$def.className,$def.name,$v.outcome,$v.testId,$reason -join "	"))}
 $counts=($r|Group-Object outcome|ForEach-Object {"$($_.Name)=$($_.Count)"})-join ','
 $summary.Add("$($run.Name) $($p.Directory.Name) total=$($r.Count) $counts")
}}}
$roster|Set-Content -Encoding utf8 (Join-Path $repo '.antiphon/c959-second-evidence/trx-roster.tsv')
$summary|Set-Content -Encoding utf8 (Join-Path $repo '.antiphon/c959-second-evidence/trx-counts.txt')
$summary|Write-Output
