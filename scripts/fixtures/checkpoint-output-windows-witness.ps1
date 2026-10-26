param([string]$Source='scripts/test-run-checkpoint.ps1')
$ErrorActionPreference='Stop'
$ast=[Management.Automation.Language.Parser]::ParseFile((Resolve-Path $Source),[ref]$null,[ref]$null)
$function=$ast.Find({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Start-C578Runner'},$true).Extent.Text
# Observe the actual argv builder without starting a native child.
$cut='$proc = [System.Diagnostics.Process]::Start($psi)'
if(-not $function.Contains($cut)){throw 'process start cut missing'}
Invoke-Expression $function.Replace($cut,'return $psi')
$script:RepoRoot='C:\repo with spaces'
$script:Runner='C:\repo with spaces\scripts\run-checkpoint.ps1'
$script:Fixtures='.'
$script:GreenTrx='C:\repo with spaces\scripts\fixtures\c585-green.trx'
$seen=@()
foreach($name in @('failed','interrupted')) {
    $fx=[pscustomobject]@{OutputPath="bin-c585h-c578-$name-unique/";ResultsRoot="C:\Users\fixture\Temp\$name\results";SlotShim='C:\fixtures\slot.ps1';Root="/inert-root-$name";Stamp='fixed';PhaseDir="C:\Users\fixture\Temp\$name\results\CP-1-fixed";Nonce=$name;Hold='build';FailBuild=($name -eq 'failed')}
    $psi=Start-C578Runner $fx $null
    $tokens=@($psi.ArgumentList)
    $output=$tokens[[Array]::IndexOf($tokens,'-OutputPath')+1]
    if($output -cne $fx.OutputPath){throw "forced-Windows $name uses shared output '$output' instead of '$($fx.OutputPath)'"}
    if($tokens[[Array]::IndexOf($tokens,'-ResultsRoot')+1] -cne $fx.ResultsRoot -or $psi.WorkingDirectory -cne $script:RepoRoot){throw 'Windows literal paths changed'}
    $seen+=$output
}
if($seen[0] -ceq $seen[1]){throw 'C578 invocations share an output'}
Write-Host 'Two forced-Windows invocations retain distinct fixture outputs and literal Windows paths.'
