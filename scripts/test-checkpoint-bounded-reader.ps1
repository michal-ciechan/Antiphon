param([string]$Repository,[string]$ToolDll,[string]$Mode,[string]$Fixture,[switch]$Held)
$ErrorActionPreference='Stop'
if ($ToolDll) { [void][Reflection.Assembly]::LoadFrom($ToolDll) }
. "$Repository/scripts/lib/checkpoint-file-reader.ps1"
if (Test-Path "$Repository/scripts/lib/checkpoint-process-runner.ps1") { . "$Repository/scripts/lib/checkpoint-process-runner.ps1" }
function Import-Function([string]$Name) {
    $tokens=$null; $errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile("$Repository/scripts/run-checkpoint.ps1",[ref]$tokens,[ref]$errors)
    $found=@($ast.FindAll({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name},$true))
    if ($found.Count -ne 1) { throw 'function count' }
    return [scriptblock]::Create($found[0].Extent.Text)
}
$env:PATH="$Fixture/bin:"+$env:PATH
$empty=[string[]]@()
$root=$Fixture
switch($Mode) {
 'project' {
    . (Import-Function 'Read-CheckpointProjectReferences')
    try { $v=Read-CheckpointProjectReferences "$Fixture/fixture.csproj"; if($Held){throw 'unexpected admission'}; if($v.Count -ne 1 -or $v[0] -cne 'child.csproj'){throw 'value mismatch'} }
    catch { if(-not $Held -or $_.Exception.Message -notmatch 'not regular'){throw} }
 }
 'tool-git' {
    $m=[Antiphon.Checkpoints.BuildConfinement].GetMethod('Git',[Reflection.BindingFlags]'Static,NonPublic')
    $v=$m.Invoke($null,[object[]]@($Fixture,[string[]]@('status')))
    if($Held){if($v.Error -cne 'git-timeout'){throw "expected timeout, got $($v.Error)"}}
    elseif($v.Exit -ne 0 -or $v.Error -or $v.Stdout.Trim() -cne 'ordinary-control'){throw 'healthy Git mismatch'}
 }
 'script-git' {
    . (Import-Function 'Invoke-CheckpointGit'); $v=Invoke-CheckpointGit $Fixture @('status')
    if($Held){if($v.Reason -cne 'git-timeout'){throw "expected timeout, got $($v.Reason)"}}
    elseif($v.Exit -ne 0 -or $v.Reason -or $v.Stdout.Trim() -cne 'ordinary-control'){throw 'healthy Git mismatch'}
 }
 'tool-eval' {
    $m=[Antiphon.Checkpoints.BuildConfinement].GetMethod('Evaluate',[Reflection.BindingFlags]'Static,NonPublic')
    $v=$m.Invoke($null,[object[]]@("$Fixture/fixture.csproj","$Fixture/bin-out","$Fixture/artifacts",$empty,$Fixture,$null,[Threading.CancellationToken]::None))
    if($Held){if($v.Error -cne 'msbuild evaluation timed out'){throw "expected timeout, got $($v.Error)"}}
    elseif($v.Error -or $v.References.Count -ne 0){throw "healthy eval mismatch $($v.Error)"}
 }
 'script-eval' {
    . (Import-Function 'Invoke-CheckpointGraphEval'); $v=Invoke-CheckpointGraphEval "$Fixture/fixture.csproj" "$Fixture/bin-out" "$Fixture/artifacts" $empty
    if($Held){if($v.Error -cne 'msbuild evaluation timed out'){throw "expected timeout, got $($v.Error)"}}
    elseif($v.Error -or $v.References.Count -ne 0){throw "healthy eval mismatch $($v.Error)"}
 }
 default { throw 'unknown mode' }
}
"PASS $Mode held=$Held"
