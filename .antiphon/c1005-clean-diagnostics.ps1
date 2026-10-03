$ErrorActionPreference='Stop'
$root='/work/worktrees/task-535715ec'
$owned=@(
 '/work/worktrees/task-535715ec/tools/Antiphon.Checkpoints/bin-c1005-tool',
 '/work/worktrees/task-535715ec/.antiphon/c1005-filter-probe/bin-probe',
 '/work/worktrees/task-535715ec/.antiphon/c1005-filter-probe/obj',
 '/work/worktrees/task-535715ec/.antiphon/c1005-diagnostic-tools',
 '/work/worktrees/task-535715ec/.antiphon/c1005-jq/jq'
)
$baseline='/work/worktrees/task-535715ec/.antiphon/c1005-current-master'
$owned += @(Get-ChildItem -LiteralPath $baseline -Directory -Recurse -Filter bin-c1005-master-discovery | ForEach-Object FullName)
foreach($p in $owned){
 if(!$p.StartsWith($root+'/',[StringComparison]::Ordinal)){throw 'outside owned root'}
 if(!(Test-Path -LiteralPath $p)){continue}
 $item=Get-Item -LiteralPath $p
 if($item.LinkType -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw "linked root $p"}
 if($item.PSIsContainer){
  $links=@(Get-ChildItem -LiteralPath $p -Recurse -Force | Where-Object {$_.LinkType -or ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)})
  if($links.Count){throw "linked descendants $p"}
 }
 Remove-Item -LiteralPath $p -Recurse -Force
 "REMOVED $p"
}
