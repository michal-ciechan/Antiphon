$tool='tools/Antiphon.Checkpoints/bin-c1005-tool/Antiphon.Checkpoints.dll'
$plan='docs/superpowers/plans/2026-10-03-card-1005-opt-in-class-census-plan.md'
$old=Get-Content .antiphon/c1005-plan-coverage.json -Raw | ConvertFrom-Json
$args1005=@($tool,'coverage','--repo-root',"$PWD",'--plan',$plan)
foreach($s in $old.sources){$args1005 += @('--tests',$s.path)}
& dotnet @args1005 --format text > .antiphon/c1005-continuation-coverage.text
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet @args1005 --format json > .antiphon/c1005-continuation-coverage.json
if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}
& dotnet $tool import --plan $plan --out .antiphon/c1005-continuation-import.yml
exit $LASTEXITCODE
