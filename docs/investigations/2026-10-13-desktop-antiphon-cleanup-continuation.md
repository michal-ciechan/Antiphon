# Desktop Antiphon cleanup continuation - guard repairs

Code owner: dfaf5740-9fa0-4a75-912f-e665f8fbca55. Branch: feat/card-task-dfaf5740. Base: c0abb4faca41f7cdf3e80a54af421d5b7cac5421. Worktree: C:\Antiphon\worktrees\card-task-dfaf5740.

Operator-authorized cleanup. Execution pending. No builds/tests or checkpoint/V/R/PC IDs are assigned by the brief. Final profile manual acceptance applies; no test-green claim. Build slot=n/a; waited=0. Restart: none.

The external helper was repaired before deletion: fresh per-item age cutoff; audit cache reuse removed; deleted-row provenance preserved; root/ancestor reparse checks before metadata scans and recursive removal; exact Temp verifier naming repeated at execution; own-task exclusion; task/path identity; unknown/active landing retention; fresh logical byte sizes; unrelated global worktree prune removed. Terminal status, completion age, descendant age, tracked changes, HEAD/branch/common-directory identity, live process/native handle and live origin-containment guards remain mandatory. Branches are retained. Deletion is sequential because safe partitioning is unsupported.

The inherited blanket residue hold was explicitly superseded for eligible A by the operator refinement documented in the prior task. New automatic policy rejections will be recorded and left.

External helper: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1. Original backup: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d-before-dfaf5740.ps1. Manifest baseline: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-manifest-before-dfaf5740.json.

The brief requests a background log; delegate instructions require foreground ownership and awaiting every command. Runs use foreground execution sessions, with incremental logs and bounded polling, and will be awaited before settlement.

## Helper diff

```diff
diff --git "a/C:\\Users\\lndco\\Documents\\antiphon-reports\\desktop-cleanup-29f3946d-before-dfaf5740.ps1" "b/C:\\Users\\lndco\\Documents\\antiphon-reports\\desktop-cleanup-29f3946d.ps1"
index 6263e7454..be5665cf8 100644
--- "a/C:\\Users\\lndco\\Documents\\antiphon-reports\\desktop-cleanup-29f3946d-before-dfaf5740.ps1"
+++ "b/C:\\Users\\lndco\\Documents\\antiphon-reports\\desktop-cleanup-29f3946d.ps1"
@@ -3,7 +3,7 @@ $ErrorActionPreference='Stop'
 $root='C:\Users\lndco\Documents\antiphon-reports'
 $json=Join-Path $root 'desktop-cleanup-manifest-2026-10-13.json'
 $md=Join-Path $root 'desktop-cleanup-manifest-2026-10-13.md'
-$own='C:\Antiphon\worktrees\card-task-29f3946d'
+$own='C:\Antiphon\worktrees\card-task-dfaf5740'
 Add-Type -TypeDefinition @'
 using System;
 using System.IO;
@@ -11,6 +11,11 @@ using System.Collections.Generic;
 using System.Runtime.InteropServices;
 using Microsoft.Win32.SafeHandles;
 public class ArtifactScan {
+ public static void AssertNoReparseAncestors(string path){
+  for(var d=new DirectoryInfo(Path.GetFullPath(path));d!=null;d=d.Parent){
+   if(!d.Exists || (d.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Missing or reparse ancestor: "+d.FullName);
+  }
+ }
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true, ExactSpelling=true)]
  static extern SafeFileHandle CreateFileW(string name,uint access,uint share,IntPtr security,uint creation,uint flags,IntPtr template);
  public static bool DirectoryUnlocked(string p){
@@ -29,8 +34,10 @@ public class ArtifactScan {
   }}catch{}return found.ToArray();
  }
  public static ArtifactScan Read(string path,bool locks) {
+  AssertNoReparseAncestors(path);
   var r=new ArtifactScan(); var q=new Stack<string>();q.Push(path);
   while(q.Count>0){ var p=q.Pop();try{
+   AssertNoReparseAncestors(p);
    if(locks&&!DirectoryUnlocked(p)){r.Locked++;continue;}
    foreach(var e in new DirectoryInfo(p).EnumerateFileSystemInfos()) {
     try{ if(e.LastWriteTimeUtc>r.Newest)r.Newest=e.LastWriteTimeUtc;
@@ -43,6 +50,7 @@ public class ArtifactScan {
   return r;
  }
  public static void RemoveTree(string p) {
+  AssertNoReparseAncestors(p);
   var d=new DirectoryInfo(p);
   if((d.Attributes&FileAttributes.ReparsePoint)!=0){d.Delete();return;}
   foreach(var e in d.EnumerateFileSystemInfos()){
@@ -53,6 +61,7 @@ public class ArtifactScan {
   d.Attributes=FileAttributes.Normal;d.Delete();
  }
  public static void UnlinkReparsePoints(string p){
+  AssertNoReparseAncestors(p);
   var root=new DirectoryInfo(p);if((root.Attributes&FileAttributes.ReparsePoint)!=0){root.Delete();return;}
   foreach(var e in root.EnumerateFileSystemInfos()){
    if((e.Attributes&FileAttributes.ReparsePoint)!=0){if((e.Attributes&FileAttributes.Directory)!=0)Directory.Delete(e.FullName);else File.Delete(e.FullName);}
@@ -67,7 +76,7 @@ function Save($m) {
  $m | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding utf8
  $lines=[Collections.Generic.List[string]]::new()
  $lines.Add('# Desktop Antiphon cleanup manifest - 2026-10-13')
- $lines.Add('');$lines.Add("Task: 29f3946d. Action: $($m.phase). Cutoff UTC: $($m.cutoff). GB means 1,000,000,000 bytes; sizes are logical, not allocated; links excluded. No branch deletion. No builds or tests.")
+ $lines.Add('');$lines.Add("Tasks: 29f3946d, continuation dfaf5740. Action: $($m.phase). Last cutoff UTC: $($m.cutoff). Every execution item recalculates the 24-hour cutoff. GB means 1,000,000,000 bytes; sizes are logical, not allocated; links excluded. No branch deletion. No builds or tests.")
  $lines.Add('');$lines.Add("C: free before=$($m.freeBefore) bytes; after=$($m.freeAfter) bytes. Owner preview run=$($m.previewRun). Caller refinement supersedes residue holds for eligible A: clean tracked files, published commits, age and live-use guards govern removal. Worktree cutoff UTC=$($m.refinedCutoff). B/C file-lock checks open files exclusively without reading payloads; A checks include native directory handles, Git source identity, tracked state and live origin containment. Process command lines are checked but not saved. Metadata/lock uncertainty retains the directory.")
  $lines.Add('');$lines.Add('| Kind | Outcome | Count | Logical GB |');$lines.Add('| --- | --- | ---: | ---: |')
  foreach($g in ($m.items | Group-Object kind,outcome)) { $lines.Add("| $($g.Group[0].kind) | $($g.Group[0].outcome) | $($g.Count) | $([math]::Round(($g.Group | Measure-Object bytes -Sum).Sum/1e9,3)) |") }
@@ -84,20 +93,21 @@ if($Action -eq 'AuditWorktrees'){
  if($LASTEXITCODE-ne 0){throw 'Origin observation unavailable; retain all worktrees'}
  $registered=@{};foreach($line in (& git -C C:\src\Antiphon worktree list --porcelain)){if($line.StartsWith('worktree ')){$registered[$line.Substring(9).Replace('/','\')]=$true}}
  $procs=Processes
- $paths=@($m.items | Where-Object kind -eq 'A' | Select-Object -ExpandProperty path)
+ $paths=@($m.items | Where-Object { $_.kind -eq 'A' -and $_.outcome -notin @('deleted','already-absent') } | Select-Object -ExpandProperty path)
  $facts=@($paths | ForEach-Object -Parallel {
   $path=$_;$short=[IO.Path]::GetFileName($path).Substring(10);$cache=Join-Path $using:auditRoot "$short.json"
-  if(Test-Path -LiteralPath $cache){Get-Content -LiteralPath $cache -Raw | ConvertFrom-Json;return}
   if(-not(Test-Path -LiteralPath $path)){
    $absent=[pscustomobject]@{path=$path;taskId=$short;status='unknown';completed=$null;head=$null;branch=$null;remote=$null;cleanTracked=$null;tracked=@();registered=$null;bytes=0;newest='unknown';creation='unknown';reasons=@('already absent during refresh; no deletion by this task');outcome='already-absent'}
    $absent | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $cache -Encoding utf8;$absent;return
   }
+  if($path -notmatch '^C:\\Antiphon\\worktrees\\card-task-[0-9a-f]{8}$'){throw 'Audit path outside exact A scope'}
+  [ArtifactScan]::AssertNoReparseAncestors($path)
   $d=Get-Item -LiteralPath $path;$reasons=[Collections.Generic.List[string]]::new()
   $status='unknown';$taskId=$short;$completed=$null;$landState=$null
   try{$t=Invoke-RestMethod "http://localhost:17202/api/agent-tasks/$short";$status=$t.summary.status;$taskId=$t.summary.id;$completed=$t.summary.completedAt
    if($status-notin @('Succeeded','Failed','Canceled')){$reasons.Add('task not terminal')}
    if(-not $completed -or ([DateTimeOffset]$completed).UtcDateTime-ge $using:cut){$reasons.Add('task completion within 24h or unknown')}
-   if($t.landRequest){$landState=if($t.landRequest.state){$t.landRequest.state}else{$t.landRequest.status};if($landState-in @('Queued','Held','Running','Starting')){$reasons.Add("unfinished active land: $landState")}}
+   if($t.landRequest){$landState=if($t.landRequest.state){$t.landRequest.state}else{$t.landRequest.status};if($landState-notin @('Completed','Canceled','Failed','Succeeded')){$reasons.Add("unfinished or unknown land: $landState")}}
   }catch{$reasons.Add('task ownership unavailable')}
   $head=$null;$branch=$null;$origin=$null;$clean=$false;$tracked=@();$common=$null
   if(Test-Path -LiteralPath (Join-Path $path '.git')){
@@ -114,7 +124,7 @@ if($Action -eq 'AuditWorktrees'){
   if($isLink){$reasons.Add('reparse root: retained')}
   $scan=$null
   if(-not $isLink){$scan=[ArtifactScan]::Read($path,$false);if($scan.Errors){$reasons.Add("metadata uncertainty: $($scan.Errors) errors")};if($d.CreationTimeUtc-ge $using:cut -or $d.LastWriteTimeUtc-ge $using:cut -or $scan.Newest-ge $using:cut){$reasons.Add('creation or descendant modified within 24h')}}
-  if($path-eq 'C:\Antiphon\worktrees\card-task-29f3946d'){$reasons.Add('own live worktree')}
+  if($path-eq $using:own){$reasons.Add('own live worktree')}
   foreach($p in $using:procs){if(($p.CommandLine -and $p.CommandLine.IndexOf($path,[StringComparison]::OrdinalIgnoreCase)-ge 0)-or($p.ExecutablePath -and $p.ExecutablePath.StartsWith($path,[StringComparison]::OrdinalIgnoreCase))){$reasons.Add("in-use process ID=$($p.ProcessId)")}}
   $reg=$using:registered
   $fact=[pscustomobject]@{path=$path;taskId=$taskId;status=$status;completed=$completed;head=$head;branch=$branch;remote=$origin;cleanTracked=$clean;tracked=$tracked;registered=$reg.ContainsKey($path);bytes=if($scan){$scan.Bytes}else{0};newest=if($scan){$scan.Newest.ToString('o')}else{'unknown'};creation=$d.CreationTimeUtc.ToString('o');reasons=@($reasons);outcome=if($reasons.Count){'kept'}else{'worktree-candidate'}}
@@ -128,19 +138,25 @@ if($Action -eq 'AuditWorktrees'){
  exit
 }
 if($Action -eq 'ExecuteWorktrees'){
- $m=Get-Content -LiteralPath $json -Raw | ConvertFrom-Json;$cut=([DateTimeOffset]$m.refinedCutoff).UtcDateTime
+ $m=Get-Content -LiteralPath $json -Raw | ConvertFrom-Json;$cut=[DateTime]::UtcNow.AddHours(-24)
+ $m.refinedCutoff=$cut.ToString('o')
  $m.phase='Refinement worktree removal';Save $m
  foreach($i in @($m.items | Where-Object outcome -eq 'worktree-candidate' | Sort-Object bytes -Descending)){
   if([DateTime]::UtcNow-ge $StopAt.UtcDateTime){Write-Output 'STOP cleanly: refined budget cutoff';break}
   try{
    $path=[IO.Path]::GetFullPath($i.path)
    if($path-notmatch '^C:\\Antiphon\\worktrees\\card-task-[0-9a-f]{8}$' -or $path-eq $own){throw 'Exact target outside allowed A scope'}
+   [ArtifactScan]::AssertNoReparseAncestors($path)
+   $cut=[DateTime]::UtcNow.AddHours(-24)
+   $short=[IO.Path]::GetFileName($path).Substring(10)
    $d=Get-Item -LiteralPath $path;if(($d.Attributes-band [IO.FileAttributes]::ReparsePoint)-ne 0){throw 'Root replaced by reparse point'}
-   $t=Invoke-RestMethod "http://localhost:17202/api/agent-tasks/$($i.taskId)"
+   $t=Invoke-RestMethod "http://localhost:17202/api/agent-tasks/$short"
+   if(-not $t.summary.id -or -not $t.summary.id.StartsWith($short,[StringComparison]::OrdinalIgnoreCase)){throw 'Fresh task/path identity mismatch'}
    if($t.summary.status-notin @('Succeeded','Failed','Canceled')-or -not $t.summary.completedAt -or ([DateTimeOffset]$t.summary.completedAt).UtcDateTime-ge $cut){$i.outcome='kept';$i.reason='fresh terminal/completion guard failed';continue}
-   $ls=if($t.landRequest.state){$t.landRequest.state}else{$t.landRequest.status};if($ls-in @('Queued','Held','Running','Starting')){$i.outcome='kept';$i.reason="fresh active landing owner: $ls";continue}
+   $ls=if($t.landRequest.state){$t.landRequest.state}else{$t.landRequest.status};if($t.landRequest -and $ls-notin @('Completed','Canceled','Failed','Succeeded')){$i.outcome='kept';$i.reason="fresh active or unknown landing owner: $ls";continue}
    if((InUse $path (Processes)).Count -or -not [ArtifactScan]::DirectoryUnlocked($path)){$i.outcome='kept';$i.reason='fresh process or exclusive directory handle guard: in use/unknown';continue}
    $s=[ArtifactScan]::Read($path,$false);if($s.Errors -or $d.CreationTimeUtc-ge $cut -or $d.LastWriteTimeUtc-ge $cut -or $s.Newest-ge $cut){$i.outcome='kept';$i.reason='fresh metadata/24h guard failed';continue}
+   $i.bytes=$s.Bytes
    $refs=@(& git -C $path rev-parse HEAD --symbolic-full-name HEAD 2>$null);if($LASTEXITCODE-ne 0 -or $refs.Count-lt 2 -or $refs[0]-ne $i.head -or $refs[1]-ne $i.branch){$i.outcome='kept';$i.reason='fresh source HEAD/branch changed';continue}
    $common=(& git -C $path rev-parse --path-format=absolute --git-common-dir 2>$null | Select-Object -First 1);if($LASTEXITCODE-ne 0 -or -not $common -or $common.Replace('/','\').TrimEnd('\')-ne 'C:\src\Antiphon\.git'){$i.outcome='kept';$i.reason='fresh Git common directory identity failed';continue}
    $tracked=@(& git --no-optional-locks -C $path status --porcelain --untracked-files=no 2>$null);if($LASTEXITCODE-ne 0 -or $tracked.Count){$i.outcome='kept';$i.reason="fresh tracked changes/inspection failure: $($tracked -join ';')";continue}
@@ -158,7 +174,6 @@ if($Action -eq 'ExecuteWorktrees'){
   $m.freeAfter=(Get-PSDrive C).Free;Save $m
   Write-Output "REFINEMENT CLEANUP A $($i.outcome) $($i.path) logicalGB=$([math]::Round($i.bytes/1e9,3)) freeGB=$([math]::Round($m.freeAfter/1e9,3))"
  }
- & git -C C:\src\Antiphon worktree prune -v
  $m.freeAfter=(Get-PSDrive C).Free;$m.phase='Refinement stopped/finished; candidate rows pending';Save $m
  $m.items | Group-Object kind,outcome | Select-Object Name,Count | ConvertTo-Json
  exit
@@ -264,20 +279,23 @@ $registeredPaths=@(& git -C C:\src\Antiphon worktree list --porcelain | Where-Ob
 foreach($i in @($m.items | Where-Object outcome -eq 'candidate')){
  if(@($registeredPaths | Where-Object {$_-eq $i.path -or $_.StartsWith($i.path+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){$i.outcome='kept';$i.reason='registered worktree descendant: preserve metadata/source custody'}
 }
-$cut=([DateTimeOffset]$m.cutoff).UtcDateTime;$m.phase='Executing';Save $m
+$cut=[DateTime]::UtcNow.AddHours(-24);$m.cutoff=$cut.ToString('o');$m.phase='Executing';Save $m
 $deadline=$StopAt.UtcDateTime
 $n=0
 foreach($i in @($m.items | Where-Object outcome -eq 'candidate' | Sort-Object bytes -Descending)){
  if([DateTime]::UtcNow-ge $deadline){Write-Output 'STOP cleanly: budget cutoff';break}
  try{
   $resolved=[IO.Path]::GetFullPath($i.path)
-  $allowed=($i.kind-eq 'B' -and [IO.Path]::GetDirectoryName($resolved)-eq 'C:\Users\lndco\AppData\Local\Temp' -and [IO.Path]::GetFileName($resolved).StartsWith('antiphon-')) -or ($i.kind-eq 'C' -and [IO.Path]::GetDirectoryName($resolved)-in @('C:\Antiphon\review-evidence','C:\Antiphon\code-evidence'))
+  $allowed=($i.kind-eq 'B' -and [IO.Path]::GetDirectoryName($resolved)-eq 'C:\Users\lndco\AppData\Local\Temp' -and [IO.Path]::GetFileName($resolved) -match '^antiphon-(land-verify-|.*test|.*runner-|.*pty-|.*queue-|.*control-|.*seam-|.*stop-)') -or ($i.kind-eq 'C' -and [IO.Path]::GetDirectoryName($resolved)-in @('C:\Antiphon\review-evidence','C:\Antiphon\code-evidence'))
   if(-not $allowed -or $resolved-eq $own){throw 'Exact target escaped explicitly allowed scope'}
+  [ArtifactScan]::AssertNoReparseAncestors($resolved)
+  $cut=[DateTime]::UtcNow.AddHours(-24)
   $d=Get-Item -LiteralPath $resolved
   if($d.CreationTimeUtc-ge $cut -or $d.LastWriteTimeUtc-ge $cut){$i.outcome='kept';$i.reason='fresh age check failed';continue}
   if((InUse $resolved (Processes)).Count){$i.outcome='kept';$i.reason='fresh process ownership check: in use';continue}
   $s=[ArtifactScan]::Read($resolved,$true)
   if($s.Errors -or $s.Locked -or $s.Newest-ge $cut){$i.outcome='kept';$i.reason="fresh metadata/handle/age guard errors=$($s.Errors) locked=$($s.Locked) newest=$($s.Newest.ToString('o'))";continue}
+  $i.bytes=$s.Bytes
   [ArtifactScan]::RemoveTree($resolved)
   if(Test-Path -LiteralPath $resolved){throw 'Target remains after removal'}
   $i.outcome='deleted';$i.reason='exact path deleted; no links followed';$n++
```

## Worktree chunk result

The first continuation chunk completed with exit 0. Additional A removals: 434; logical bytes removed: 62528522239 (62.529 GB). Current A keeps: 284; A pending: 194; failures: 0. C: free at chunk completion: 436648968192 bytes. B/C execution remains pending. D counts overlap A and are never added to its bytes.

Execution log: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-dfaf5740-A1.log. Fresh audit log: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-dfaf5740-audit.log.
