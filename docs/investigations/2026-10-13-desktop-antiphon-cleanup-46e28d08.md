# Desktop Antiphon cleanup - third continuation (46e28d08)

Code owner: 46e28d08-6db8-42ca-96e3-3979f4297e34. Branch: feat/card-task-46e28d08 (uncommitted per brief). Base: 1553325cc8a50486422bf640d6f023ab07e23854. Operator authority: "clean it all up anything older than 24 hours goes". No builds/tests (none required by the brief); build slot n/a, waited=0. Restart: none.

Outcome: 232 directories removed (202 worktrees, 29 Temp verifier dirs, 1 evidence dir), 90.505 GB logical, 0 failures; C: free 477.758 GB -> 569.575 GB (+91.8 GB). Stopped at the 100-minute budget with 329 B + 27 C candidates (125.08 GB logical) still pending; no A candidates pending.

## Guard review

Every brief guard was confirmed in desktop-cleanup-29f3946d.ps1 before running it. A deletes only exact `C:\Antiphon\worktrees\card-task-<8hex>` paths with no reparse root or ancestors. At execution it freshly re-checks: a terminal task (Succeeded/Failed/Canceled), task/path identity, completedAt and creation/descendant mtime older than a freshly computed 24 h cutoff, a land state that is either terminal or absent, no process command line or exe under the path, an exclusive native directory handle, HEAD/branch equal to the audit, the git common dir equal to C:\src\Antiphon\.git, no tracked changes, and HEAD contained in a live `ls-remote` origin branch. Junctions/symlinks are unlinked without traversal, and `git worktree remove --force` never deletes branches. B accepts only direct Temp children named `antiphon-(land-verify-|*test|*runner-|*pty-|*queue-|*control-|*seam-|*stop-)`. C accepts only direct children of review-evidence/code-evidence, with fresh age, process and exclusive-file-lock checks, and registered-worktree descendants kept. The canonical checkout, Docker/WSL, caches, Dropbox/OneDrive, C:\logs and Documents (beyond the script, manifest, logs and backups) were not touched.

Fixed before running (backup: desktop-cleanup-29f3946d-before-46e28d08.ps1; manifest backups: desktop-cleanup-manifest-before-46e28d08.json/.md):
1. `$own` still named the previous task's worktree; it now names card-task-46e28d08. (The age and nonterminal guards would have kept it regardless.)
2. `-Action Execute` never re-planned B/C rows that an earlier run had kept, for example for age, so they could never be retried. It now resets in-scope, still-existing kept/failed B/C rows to candidate, and the full execution guard set re-runs on each one. Rows outside the naming scope stay kept.

Parallel deleters were **not** used. Every run loads and rewrites the single manifest JSON, so concurrent partitions would race on it. The script has no safe partitioning, so deletion stayed sequential, as the brief requires.

Run note: passing `-StopAt ((Get-Date).ToUniversalTime().AddMinutes(50))` through `pwsh -File` drops the UTC kind. The child reads it as local BST, so the stop time is already in the past and the run stops immediately without touching any rows. Pass an ISO string such as `-StopAt 2026-10-13T18:41:45Z`.

Runs: AuditWorktrees (478 paths -> 195 candidates), then ExecuteWorktrees (194 deleted; 1 kept as in use: card-task-3013296e), then AuditWorktrees (284 paths -> 9 candidates), then ExecuteWorktrees (8 deleted), then Execute B/C (20 deleted, budget stop), then Execute B/C (10 deleted, budget stop). Logs: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-46e28d08-{audit1,execwt1,audit2,execwt2,execbc1,execbc2}.log.

Policy blocks: none occurred. No permission-policy rejection was hit, so no paths were left for that reason.

Bootstrap/build (bin-*) D rows: none of the 202 worktrees deleted here had D rows, so deleted-with-parent stayed at 72. All 352 kept D rows sit inside kept worktrees or non-task parents and still exist.

## Script diff

```diff
@@ -3,7 +3,8 @@ $ErrorActionPreference='Stop'
 $root='C:\Users\lndco\Documents\antiphon-reports'
 $json=Join-Path $root 'desktop-cleanup-manifest-2026-10-13.json'
 $md=Join-Path $root 'desktop-cleanup-manifest-2026-10-13.md'
-$own='C:\Antiphon\worktrees\card-task-dfaf5740'
+$own='C:\Antiphon\worktrees\card-task-46e28d08'
+function AllowedBC($kind,$resolved){ ($kind-eq 'B' -and [IO.Path]::GetDirectoryName($resolved)-eq 'C:\Users\lndco\AppData\Local\Temp' -and [IO.Path]::GetFileName($resolved) -match '^antiphon-(land-verify-|.*test|.*runner-|.*pty-|.*queue-|.*control-|.*seam-|.*stop-)') -or ($kind-eq 'C' -and [IO.Path]::GetDirectoryName($resolved)-in @('C:\Antiphon\review-evidence','C:\Antiphon\code-evidence')) }
 Add-Type -TypeDefinition @'
 using System;
 using System.IO;
@@ -76,7 +77,7 @@ function Save($m) {
  $m | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $json -Encoding utf8
  $lines=[Collections.Generic.List[string]]::new()
  $lines.Add('# Desktop Antiphon cleanup manifest - 2026-10-13')
- $lines.Add('');$lines.Add("Tasks: 29f3946d, continuation dfaf5740. Action: $($m.phase). Last cutoff UTC: $($m.cutoff). Every execution item recalculates the 24-hour cutoff. GB means 1,000,000,000 bytes; sizes are logical, not allocated; links excluded. No branch deletion. No builds or tests.")
+ $lines.Add('');$lines.Add("Tasks: 29f3946d, continuations dfaf5740 and 46e28d08. Action: $($m.phase). Last cutoff UTC: $($m.cutoff). Every execution item recalculates the 24-hour cutoff. GB means 1,000,000,000 bytes; sizes are logical, not allocated; links excluded. No branch deletion. No builds or tests.")
  $lines.Add('');$lines.Add("C: free before=$($m.freeBefore) bytes; after=$($m.freeAfter) bytes. Owner preview run=$($m.previewRun). Caller refinement supersedes residue holds for eligible A: clean tracked files, published commits, age and live-use guards govern removal. Worktree cutoff UTC=$($m.refinedCutoff). B/C file-lock checks open files exclusively without reading payloads; A checks include native directory handles, Git source identity, tracked state and live origin containment. Process command lines are checked but not saved. Metadata/lock uncertainty retains the directory.")
  $lines.Add('');$lines.Add('| Kind | Outcome | Count | Logical GB |');$lines.Add('| --- | --- | ---: | ---: |')
  foreach($g in ($m.items | Group-Object kind,outcome)) { $lines.Add("| $($g.Group[0].kind) | $($g.Group[0].outcome) | $($g.Count) | $([math]::Round(($g.Group | Measure-Object bytes -Sum).Sum/1e9,3)) |") }
@@ -276,6 +277,12 @@ foreach($extra in (Get-ChildItem C:\Antiphon\worktrees -Directory | Where-Object
  }
 }
 $registeredPaths=@(& git -C C:\src\Antiphon worktree list --porcelain | Where-Object {$_ -like 'worktree *'} | ForEach-Object {$_.Substring(9).Replace('/','\')})
+# 46e28d08 re-plan: B/C rows kept or failed by an earlier run go back to candidate only when they are in the exact
+# allowed scope and still exist; every execution guard below (scope, reparse, 24h age, process, exclusive locks) re-runs.
+foreach($i in @($m.items | Where-Object { $_.kind -in @('B','C') -and $_.outcome -in @('kept','failed') })){
+ if(-not (AllowedBC $i.kind ([IO.Path]::GetFullPath($i.path))) -or -not (Test-Path -LiteralPath $i.path)){continue}
+ $i.outcome='candidate';$i.reason="re-planned by 46e28d08 (previous: $($i.reason))"
+}
 foreach($i in @($m.items | Where-Object outcome -eq 'candidate')){
  if(@($registeredPaths | Where-Object {$_-eq $i.path -or $_.StartsWith($i.path+'\',[StringComparison]::OrdinalIgnoreCase)}).Count){$i.outcome='kept';$i.reason='registered worktree descendant: preserve metadata/source custody'}
 }
@@ -286,7 +293,7 @@ foreach($i in @($m.items | Where-Object outcome -eq 'candidate' | Sort-Object by
  if([DateTime]::UtcNow-ge $deadline){Write-Output 'STOP cleanly: budget cutoff';break}
  try{
   $resolved=[IO.Path]::GetFullPath($i.path)
-  $allowed=($i.kind-eq 'B' -and [IO.Path]::GetDirectoryName($resolved)-eq 'C:\Users\lndco\AppData\Local\Temp' -and [IO.Path]::GetFileName($resolved) -match '^antiphon-(land-verify-|.*test|.*runner-|.*pty-|.*queue-|.*control-|.*seam-|.*stop-)') -or ($i.kind-eq 'C' -and [IO.Path]::GetDirectoryName($resolved)-in @('C:\Antiphon\review-evidence','C:\Antiphon\code-evidence'))
+  $allowed=AllowedBC $i.kind $resolved
   if(-not $allowed -or $resolved-eq $own){throw 'Exact target escaped explicitly allowed scope'}
   [ArtifactScan]::AssertNoReparseAncestors($resolved)
   $cut=[DateTime]::UtcNow.AddHours(-24)
```
## Totals (task 46e28d08)

C: free at task start (pre-delete) = 477758345216 bytes (477.758 GB); now = 569574936576 bytes (569.575 GB); delta = 91.817 GB. Original freeBefore (29f3946d) = 339225063424 bytes (339.225 GB).

| Kind | Deleted by 46e28d08 | Logical GB |
| --- | ---: | ---: |
| A | 202 | 26.383 |
| B | 29 | 63.359 |
| C | 1 | 0.763 |
| total | 232 | 90.505 |

Cumulative manifest state (all three tasks):

| Kind | Outcome | Count | Logical GB |
| --- | --- | ---: | ---: |
| A | already-absent | 1 | 0 |
| A | deleted | 754 | 112.828 |
| A | kept | 276 | 70.586 |
| B | candidate | 329 | 124.959 |
| B | deleted | 50 | 113.395 |
| B | kept | 45 | 69.805 |
| C | candidate | 27 | 0.121 |
| C | deleted | 1 | 0.763 |
| C | kept | 3 | 1.666 |
| D | deleted-with-parent | 72 | 0 |
| D | kept | 352 | 0 |

## Kept A worktrees by primary reason

| Primary reason | Count | Logical GB |
| --- | ---: | ---: |
| within 24h only | 150 | 23.906 |
| unpublished HEAD: branch absent from origin | 116 | 45.241 |
| tracked changes | 3 | 0.846 |
| in use (process/handle) | 2 | 0.205 |
| task not terminal | 2 | 0.003 |
| unpublished HEAD: not contained on origin branch | 2 | 0.247 |
| land unfinished/unknown state | 1 | 0.138 |

Rows also carrying 'Git common directory identity unproven': 13.

### Kept for reasons other than age/no-origin-branch

| Path | Branch | Reason (truncated) |
| --- | --- | --- |
| C:\Antiphon\worktrees\card-task-02c16198 | refs/heads/feat/card-task-02c16198 | unfinished or unknown land: Superseded |
| C:\Antiphon\worktrees\card-task-0f9c42e4 | refs/heads/feat/card-task-0f9c42e4 | kept: tracked changes or failed tracked inspection:  M tests/Antiphon.Tests/TestHelpers/HerdrLabelFollowHttpFixture.cs; kept: unpushed work - no corresponding origin branch |
| C:\Antiphon\worktrees\card-task-1d5f5588 | refs/heads/feat/card-task-1d5f5588 | task not terminal; task completion within 24h or unknown; kept: unpushed work - no corresponding origin branch; Git common directory identity unproven |
| C:\Antiphon\worktrees\card-task-3013296e | refs/heads/feat/card-task-3013296e | fresh process or exclusive directory handle guard: in use/unknown |
| C:\Antiphon\worktrees\card-task-55662594 | refs/heads/feat/card-task-55662594 | kept: unpushed work - HEAD not contained on origin branch |
| C:\Antiphon\worktrees\card-task-570fd325 | refs/heads/feat/card-task-570fd325 | kept: tracked changes or failed tracked inspection: D  .antiphon/c1008-evidence-round2/cp3-repaired/report.md;D  .antiphon/c1008-evidence-round3/cp3-missing-jq/failures.md;D  .antiphon/c1008-evidence-round3/cp3-missing-j |
| C:\Antiphon\worktrees\card-task-73233c64 | refs/heads/feat/card-task-73233c64 | task not terminal; task completion within 24h or unknown; kept: unpushed work - no corresponding origin branch; Git common directory identity unproven |
| C:\Antiphon\worktrees\card-task-8ec6d6ff | refs/heads/feat/card-task-8ec6d6ff | kept: unpushed work - HEAD not contained on origin branch |
| C:\Antiphon\worktrees\card-task-cd2866b8 | refs/heads/feat/card-task-cd2866b8 | kept: tracked changes or failed tracked inspection:  M docs/superpowers/plans/2026-09-16-card-0544-interim-final-verification-plan.md; kept: unpushed work - no corresponding origin branch |
| C:\Antiphon\worktrees\card-task-dfc6f878 | refs/heads/feat/card-task-dfc6f878-implementation | kept: tracked changes or failed tracked inspection:  M server/Application/Services/CardService.cs; M src/Antiphon.SessionRunner/HerdrPaneChild.cs; M src/Antiphon.SessionRunner/SessionRunnerRuntime.cs; M tests/Antiphon.Se |

### Kept unpublished worktrees whose branch is not on origin (121)

Operator may want to inspect these; no branch or worktree was deleted. "also <24h" marks rows still inside the 24 h window.

| Path | Branch | HEAD | Task status | Logical GB | Also |
| --- | --- | --- | --- | ---: | --- |
| C:\Antiphon\worktrees\card-task-0025fbfd | refs/heads/feat/card-task-0025fbfd | 53e165503eab0d063502c053fdb0904b72301ffd | Failed | 0.259 |  |
| C:\Antiphon\worktrees\card-task-00c23442 | refs/heads/feat/card-task-00c23442 | d7937e73c78fa06c006aad0a7f55e4661391fcdc | Succeeded | 0.315 |  |
| C:\Antiphon\worktrees\card-task-0192f22e | refs/heads/feat/card-task-0192f22e | 4d9cd57b4923f9d6d9b76cb452a23a006092ef55 | Failed | 0.268 |  |
| C:\Antiphon\worktrees\card-task-022ccaf0 | refs/heads/feat/card-task-022ccaf0 | c246d43986b52f7ca4a847175f895f055e38cb07 | Succeeded | 0.26 |  |
| C:\Antiphon\worktrees\card-task-02e5b9b7 | refs/heads/feat/card-task-02e5b9b7 | 2d3c582416c5610d72e53df3e790bc114d87ad82 | Succeeded | 0.117 |  |
| C:\Antiphon\worktrees\card-task-06e30249 | refs/heads/feat/card-task-06e30249 | 5475ddee667c1150a38186f6685d0e31820a54bb | Succeeded | 0.226 |  |
| C:\Antiphon\worktrees\card-task-0726e42d | refs/heads/feat/card-task-0726e42d | fb06b0ae3caa06ce28d16757a9ad1d6472b49eca | Canceled | 5.389 | identity unproven |
| C:\Antiphon\worktrees\card-task-091ad0a4 | refs/heads/feat/card-task-091ad0a4 | 9e0e5d62945d267520709970091b6192fbc02260 | Succeeded | 0.298 |  |
| C:\Antiphon\worktrees\card-task-0a79bc28 | HEAD | 8cc17f518765943295f3517e6ebd279ab6d77a5c | Succeeded | 0.142 |  |
| C:\Antiphon\worktrees\card-task-0d499136 | refs/heads/feat/card-task-0d499136 | a127c2700c80ebfec48cb3487c8b1d25e8f6ea03 | Succeeded | 0.291 |  |
| C:\Antiphon\worktrees\card-task-0f9c42e4 | refs/heads/feat/card-task-0f9c42e4 | d6726dff09b1e14576cb8a5d55f718f4060523dc | Canceled | 0.073 | tracked changes |
| C:\Antiphon\worktrees\card-task-0fc542b5 | refs/heads/feat/card-task-0fc542b5 | f329f5d0c0641117abf556b6bee0d9e8966ba9ff | Succeeded | 0.137 |  |
| C:\Antiphon\worktrees\card-task-0fd2a915 | refs/heads/feat/card-task-0fd2a915 | 33b5ee95901b5ce01a9e1019d722ae9a472d3959 | Failed | 0.259 |  |
| C:\Antiphon\worktrees\card-task-123a6683 | refs/heads/feat/card-task-123a6683 | adffaf9f75fa61d48e8af9c4d1d7fa95732b194d | Succeeded | 0.149 |  |
| C:\Antiphon\worktrees\card-task-133888ed | refs/heads/feat/card-task-133888ed | b57842686c29cee4a48579705e8ab356653bc8ad | Succeeded | 0.26 |  |
| C:\Antiphon\worktrees\card-task-157eb737 | refs/heads/feat/card-task-157eb737 | 18c502e1c26e9594d01d1f8a8d1cd1e5b0ac8cc8 | Canceled | 0.009 | identity unproven |
| C:\Antiphon\worktrees\card-task-187ba700 | refs/heads/feat/card-task-187ba700 | 2c3902ec78836f5226660c5d3ab2b83d113c2356 | Succeeded | 0.291 |  |
| C:\Antiphon\worktrees\card-task-1893101c | refs/heads/feat/card-task-1893101c | 1d08ff42db131d7385695be792b487e797f0667a | Succeeded | 0.071 | identity unproven |
| C:\Antiphon\worktrees\card-task-18d59e3e | refs/heads/feat/card-task-18d59e3e | ea77c7dbbf78e3afa7d711b8aced3f7e5590fe77 | Succeeded | 0.035 | identity unproven |
| C:\Antiphon\worktrees\card-task-1d5f5588 | refs/heads/feat/card-task-1d5f5588 | 61972be866182eb9cec1867596dd95b600fb84e1 | Queued | 0.002 | <24h, identity unproven, not terminal |
| C:\Antiphon\worktrees\card-task-1fc4f3dd | refs/heads/feat/card-task-1fc4f3dd | f41748999c285a6ea00a7b8bf486ec51ee92fa16 | Succeeded | 0.136 |  |
| C:\Antiphon\worktrees\card-task-2299fe11 | refs/heads/feat/card-task-2299fe11 | 749e73f1c743737d551b677bf8a6d78ff109a3a1 | Succeeded | 0.26 |  |
| C:\Antiphon\worktrees\card-task-22e766b4 | refs/heads/feat/card-task-22e766b4 | 2c310b370194d834a7457037b61bb006356b151d | Succeeded | 0.264 |  |
| C:\Antiphon\worktrees\card-task-2618ecd0 | refs/heads/feat/card-task-2618ecd0 | bfbbbaae78292129fef941d2405a9f9cddeba23e | Succeeded | 0.266 |  |
| C:\Antiphon\worktrees\card-task-272701b6 | refs/heads/feat/card-task-272701b6 | 1dcc03c0a596dbd1fa857ee93293c6dfbf05828a | Failed | 0.415 |  |
| C:\Antiphon\worktrees\card-task-28a1714e | refs/heads/feat/card-task-28a1714e | 4e865e05d288374c0d61d8599080a5c399db5f4e | Succeeded | 0.276 |  |
| C:\Antiphon\worktrees\card-task-2db75657 | refs/heads/feat/card-task-2db75657 | 5475ddee667c1150a38186f6685d0e31820a54bb | Succeeded | 0.222 |  |
| C:\Antiphon\worktrees\card-task-2fdae085 | refs/heads/feat/card-task-2fdae085 | ac8fb106f6e75f6659bdb74447c842e1c1c7c8e7 | Succeeded | 0.325 | <24h |
| C:\Antiphon\worktrees\card-task-3516100f | refs/heads/feat/card-task-3516100f | 554519b37c55b24672df0818f3ed1603f3e3fc0e | Succeeded | 0.266 |  |
| C:\Antiphon\worktrees\card-task-37267e24 | refs/heads/feat/card-task-37267e24 | 5475ddee667c1150a38186f6685d0e31820a54bb | Failed | 5.84 |  |
| C:\Antiphon\worktrees\card-task-3a6a7498 | refs/heads/feat/card-task-3a6a7498 | 96107f8ceeeade848422bd4a75263476fa203547 | Succeeded | 0.261 |  |
| C:\Antiphon\worktrees\card-task-3e2722af | refs/heads/feat/card-task-3e2722af | 962075470e4d0dcfcb890c58f331b046abf4b48d | Succeeded | 0.291 |  |
| C:\Antiphon\worktrees\card-task-4009b45a | refs/heads/feat/card-task-4009b45a | a517c354fb07b5420788c063e6545e1185510c7d | Succeeded | 0.259 |  |
| C:\Antiphon\worktrees\card-task-44a8eca2 | refs/heads/feat/card-task-44a8eca2 | 7683aa85332521886bb1d496a45b87b701858e5e | Canceled | 0.149 | <24h |
| C:\Antiphon\worktrees\card-task-4608a549 | refs/heads/feat/card-task-4608a549 | 65a78f3b1f88c213bbb4ac595681212476447130 | Succeeded | 0.261 |  |
| C:\Antiphon\worktrees\card-task-46398720 | refs/heads/feat/card-task-46398720 | b2f2107fbcc04eaf5e5174836725c0a143daeb2d | Succeeded | 0.141 |  |
| C:\Antiphon\worktrees\card-task-4af9428c | refs/heads/feat/card-task-4af9428c | 6b97485644d44fce71e02d595735f11a01b639ae | Succeeded | 0.265 |  |
| C:\Antiphon\worktrees\card-task-4bc8a7a2 | refs/heads/feat/card-task-4bc8a7a2 | 3564a9c205c6549194fe69626ce98a7524c5eafc | Succeeded | 0.135 |  |
| C:\Antiphon\worktrees\card-task-4de0a86d | refs/heads/feat/card-task-4de0a86d | 3b7830b1f276dbabc4b7402bdb169663cbd05b95 | Canceled | 0.137 |  |
| C:\Antiphon\worktrees\card-task-50c43b3a | refs/heads/feat/card-task-50c43b3a | 5475ddee667c1150a38186f6685d0e31820a54bb | Failed | 0.223 |  |
| C:\Antiphon\worktrees\card-task-5128ef1f | refs/heads/feat/card-task-5128ef1f | 749e73f1c743737d551b677bf8a6d78ff109a3a1 | Succeeded | 0.259 |  |
| C:\Antiphon\worktrees\card-task-527a8982 | refs/heads/feat/card-task-527a8982 | 9d2ce18ed6a7c837320a0bc9f0faf1e3f8c55b61 | Succeeded | 0.264 |  |
| C:\Antiphon\worktrees\card-task-5582555c | refs/heads/feat/card-task-5582555c | 9b78712f6671d7ccb98bcf4a5ebea8f080d7cecd | Canceled | 0.131 |  |
| C:\Antiphon\worktrees\card-task-5638a6b5 | refs/heads/feat/card-task-5638a6b5 | bac3ddeb988bb43ee26c4117aba2ef545bd8433c | Succeeded | 0.236 |  |
| C:\Antiphon\worktrees\card-task-58c3df41 | refs/heads/feat/card-task-58c3df41 | 9e0e5d62945d267520709970091b6192fbc02260 | Failed | 0.15 |  |
| C:\Antiphon\worktrees\card-task-5914f175 | refs/heads/feat/card-task-5914f175 | 498004717e761614d04388bdba287c6097961e46 | Succeeded | 0.323 | <24h |
| C:\Antiphon\worktrees\card-task-5bc00d31 | HEAD | 284d9cc1f151cb2c87857902bc7debc69332c0f1 | Canceled | 0.514 |  |
| C:\Antiphon\worktrees\card-task-5e929ac1 | refs/heads/feat/card-task-5e929ac1 | d34058b3d4e4d7da4d44dc1853ccfcccc8763cee | Succeeded | 1.576 |  |
| C:\Antiphon\worktrees\card-task-60ccb73c | refs/heads/feat/card-task-60ccb73c | e7e5fb196d2614a68edcceed8e5963d990d77e06 | Succeeded | 0.325 | <24h |
| C:\Antiphon\worktrees\card-task-616e0c1c | refs/heads/feat/card-task-616e0c1c | c568aacbc64f33c85e8b5dddd8ca0444b3fe1abd | Succeeded | 0.262 |  |
| C:\Antiphon\worktrees\card-task-66704481 | refs/heads/feat/card-task-66704481 | b2f2107fbcc04eaf5e5174836725c0a143daeb2d | Succeeded | 0.162 |  |
| C:\Antiphon\worktrees\card-task-66eec4dd | refs/heads/feat/card-task-66eec4dd | 3ab4e4fa022d921fb0534a9fad88d1419a02c670 | Succeeded | 0.006 | identity unproven |
| C:\Antiphon\worktrees\card-task-6766751c | refs/heads/feat/card-task-6766751c | da2b605bf59d529e4debc9a4c25b7f207fa54b01 | Succeeded | 0.149 |  |
| C:\Antiphon\worktrees\card-task-68855ec6 | refs/heads/feat/card-task-68855ec6 | 9e482f37de08b3bbf1f607aa27a5dba6e093535b | Succeeded | 0.169 |  |
| C:\Antiphon\worktrees\card-task-6dcdf2c2 | refs/heads/feat/card-task-6dcdf2c2 | 36404fee1662de80f3c8fef1e8fb6659f0973d4b | Succeeded | 0.265 |  |
| C:\Antiphon\worktrees\card-task-6e5693b9 | refs/heads/feat/card-task-6e5693b9 | 9011b62ca7b5548eeeaa61261b1533a364c6c583 | Succeeded | 0.156 |  |
| C:\Antiphon\worktrees\card-task-6f4c2f0d | refs/heads/feat/card-task-6f4c2f0d | 47f5c8a2ab65c6542b38b5979bf51df19ade5403 | Succeeded | 0.326 | <24h |
| C:\Antiphon\worktrees\card-task-704c7117 | refs/heads/feat/card-task-704c7117 | ea970d249cb7ff22e434774181d05bb4af2ae583 | Succeeded | 0.006 | identity unproven |
| C:\Antiphon\worktrees\card-task-722d7d90 | refs/heads/feat/card-task-722d7d90 | fa70e660fc7b922552cf2bf7f9c82fe3efedce5e | Succeeded | 0.328 | <24h |
| C:\Antiphon\worktrees\card-task-73233c64 | refs/heads/feat/card-task-73233c64 | 61972be866182eb9cec1867596dd95b600fb84e1 | Queued | 0.002 | <24h, identity unproven, not terminal |
| C:\Antiphon\worktrees\card-task-752514ff | refs/heads/feat/card-task-752514ff | 6917f9e70211f69bf1c8cab66d450d24c4f86181 | Succeeded | 0.316 |  |
| C:\Antiphon\worktrees\card-task-781e698a | refs/heads/feat/card-task-781e698a | ca25400dafdb3e925306f43e01c5469fccb0813b | Succeeded | 0.311 |  |
| C:\Antiphon\worktrees\card-task-783cba50 | refs/heads/feat/card-task-783cba50 | 9dd710fceb3ba266c1d9f45cb08c4cb93eacf7d0 | Succeeded | 0.131 |  |
| C:\Antiphon\worktrees\card-task-7ce891eb | refs/heads/feat/card-task-7ce891eb | 8289c947ede1816f6b647555c66318de8221d427 | Succeeded | 0.267 |  |
| C:\Antiphon\worktrees\card-task-80e6d59c | refs/heads/feat/card-task-80e6d59c | 8bb0cea045f5a89359b0ba55712417f02e31a42a | Succeeded | 0.138 |  |
| C:\Antiphon\worktrees\card-task-80ff713f | refs/heads/feat/card-task-80ff713f | cb95a4c3c171df7a55bfd06266e17980712c68fb | Succeeded | 0.267 |  |
| C:\Antiphon\worktrees\card-task-81dea5db | refs/heads/feat/card-task-81dea5db | 145544e68b5564aa8d4c7d140765e760951c6a32 | Succeeded | 0.26 |  |
| C:\Antiphon\worktrees\card-task-83407ae4 | refs/heads/feat/card-task-83407ae4 | f7367c798c1d4260dd069e57930064f95b62c221 | Succeeded | 0.295 |  |
| C:\Antiphon\worktrees\card-task-86e61604 | refs/heads/feat/card-task-86e61604 | f11d2715f340c7caa03343862cb8dd73cc27f66b | Succeeded | 0.285 |  |
| C:\Antiphon\worktrees\card-task-88db4c4b | refs/heads/feat/card-task-88db4c4b | 3860c0fb19667e26ca6b797871388d4233472c3c | Succeeded | 0.324 | <24h |
| C:\Antiphon\worktrees\card-task-8bd36a07 | refs/heads/feat/card-task-8bd36a07 | 9567afa32de09cb956ed478b612dcffbd1af8772 | Succeeded | 0.259 |  |
| C:\Antiphon\worktrees\card-task-90025dd6 | refs/heads/feat/card-task-90025dd6 | f885b680552407e9f4d28261edfccd03d4259f50 | Succeeded | 0.149 |  |
| C:\Antiphon\worktrees\card-task-92c73242 | refs/heads/feat/card-task-92c73242 | 937246ed8e1fd88b6ced60316498dbc8f898fea1 | Succeeded | 0.244 |  |
| C:\Antiphon\worktrees\card-task-9efba31e | refs/heads/feat/card-task-9efba31e | 1ca05321119da6d6d42d78c46cca64bc1d18dbf9 | Succeeded | 0.244 |  |
| C:\Antiphon\worktrees\card-task-a02625fa | refs/heads/feat/card-task-a02625fa | 05987232edfead39672e89a44fff4af56b890020 | Succeeded | 0.26 |  |
| C:\Antiphon\worktrees\card-task-a6fe70f2 | refs/heads/feat/card-task-a6fe70f2 | fbca3189e6cdf9d833da2d1a88f1d2ad083afd5b | Succeeded | 1.578 | identity unproven |
| C:\Antiphon\worktrees\card-task-a89289fd | refs/heads/feat/card-task-a89289fd | 5801800811033ea2652e3afa2142dff911812fba | Succeeded | 0.263 |  |
| C:\Antiphon\worktrees\card-task-a926b0dd | refs/heads/feat/card-task-a926b0dd | 7a09439f0da78308862bdc6f830aaf65d2b0c1ec | Succeeded | 0.26 |  |
| C:\Antiphon\worktrees\card-task-ab8a329e | refs/heads/feat/card-task-ab8a329e | c2987ceb4210b267099d65711654c4c823601958 | Succeeded | 0.267 |  |
| C:\Antiphon\worktrees\card-task-ad33afac | refs/heads/feat/card-task-ad33afac | d817d30cdf7cab0542980aa686196df9048923ce | Succeeded | 0.266 |  |
| C:\Antiphon\worktrees\card-task-b1bf4404 | refs/heads/feat/card-task-b1bf4404 | e65d7119985b7993762bd69bee9706a6c05c760e | Succeeded | 0.264 |  |
| C:\Antiphon\worktrees\card-task-b9c5b5e6 | refs/heads/feat/card-task-b9c5b5e6 | c648f83a4a6d61875032acc9690bbef7785150a7 | Failed | 0.176 |  |
| C:\Antiphon\worktrees\card-task-ba64b974 | refs/heads/feat/card-task-ba64b974 | 5475ddee667c1150a38186f6685d0e31820a54bb | Succeeded | 0.105 |  |
| C:\Antiphon\worktrees\card-task-c0b9f2f7 | refs/heads/feat/card-task-c0b9f2f7 | 18c502e1c26e9594d01d1f8a8d1cd1e5b0ac8cc8 | Canceled | 0.009 | identity unproven |
| C:\Antiphon\worktrees\card-task-c1a38371 | refs/heads/feat/card-task-c1a38371 | 6a019da5f3c8240b7ca9ffee21423fe5ba81bcf8 | Succeeded | 0.244 |  |
| C:\Antiphon\worktrees\card-task-c55b6c31 | refs/heads/feat/card-task-c55b6c31 | 5475ddee667c1150a38186f6685d0e31820a54bb | Failed | 2.311 |  |
| C:\Antiphon\worktrees\card-task-c742d854 | refs/heads/feat/card-task-c742d854 | 325454aeeb0be7a33d115eb38abe7589a7a4a12a | Succeeded | 0.278 |  |
| C:\Antiphon\worktrees\card-task-c94cfd82 | refs/heads/feat/card-task-c94cfd82 | 36d19595f81e4241ec2d4932c5e6829148b0e582 | Succeeded | 0.131 |  |
| C:\Antiphon\worktrees\card-task-c9a3d250 | HEAD | 284d9cc1f151cb2c87857902bc7debc69332c0f1 | Canceled | 1.071 |  |
| C:\Antiphon\worktrees\card-task-ca7c02fa | refs/heads/feat/card-task-ca7c02fa | bac3ddeb988bb43ee26c4117aba2ef545bd8433c | Succeeded | 0.221 |  |
| C:\Antiphon\worktrees\card-task-cb100e90 | refs/heads/feat/card-task-cb100e90 | 0de12dac930ad52a235604c566ee643e71b21daa | Succeeded | 0.135 |  |
| C:\Antiphon\worktrees\card-task-cc5e0224 | refs/heads/feat/card-task-cc5e0224 | bfbbbaae78292129fef941d2405a9f9cddeba23e | Failed | 0.266 |  |
| C:\Antiphon\worktrees\card-task-cd2866b8 | refs/heads/feat/card-task-cd2866b8 | 0c99f7e601e149e15ba210d8589dc580218a7c0a | Canceled | 0.072 | tracked changes |
| C:\Antiphon\worktrees\card-task-ce64becf | refs/heads/feat/card-task-ce64becf | 9a65ac0086bc19965a7691d6527a51bd70c7921e | Succeeded | 0.295 |  |
| C:\Antiphon\worktrees\card-task-ce92d660 | refs/heads/feat/card-task-ce92d660 | 725c575dc2ca0c8832e1e5881ac2f6be17d7e98f | Succeeded | 0.259 |  |
| C:\Antiphon\worktrees\card-task-cea60832 | refs/heads/feat/card-task-cea60832 | 9e482f37de08b3bbf1f607aa27a5dba6e093535b | Succeeded | 0.169 | <24h |
| C:\Antiphon\worktrees\card-task-cf18faac | refs/heads/feat/card-task-cf18faac | fbca3189e6cdf9d833da2d1a88f1d2ad083afd5b | Succeeded | 0.071 | identity unproven |
| C:\Antiphon\worktrees\card-task-cff0cd4f | refs/heads/feat/card-task-cff0cd4f | bc2364102be954e98c0f084739a8f7665899b1ee | Succeeded | 0.325 | <24h |
| C:\Antiphon\worktrees\card-task-d07857d1 | refs/heads/feat/card-task-d07857d1 | a708378969b973a9f33a2e664af52ebe6e27d1f2 | Succeeded | 0.319 |  |
| C:\Antiphon\worktrees\card-task-d2a56eff | refs/heads/feat/card-task-d2a56eff | 453810412e14e2aa4967a0407b5bfbf8aaad0ab8 | Succeeded | 0.263 |  |
| C:\Antiphon\worktrees\card-task-d32fc8af | refs/heads/feat/card-task-d32fc8af | 693dc4795fd6bb77d649d9e9d02208234e5142b0 | Succeeded | 0.266 |  |
| C:\Antiphon\worktrees\card-task-d63c1fcc | refs/heads/feat/card-task-d63c1fcc | 45812fc1f6ab8af6f541c28abee18181d07ea9b1 | Succeeded | 0.271 |  |
| C:\Antiphon\worktrees\card-task-d6e4138e | refs/heads/feat/card-task-d6e4138e | 5f214b0c1daef4d6d7fbbbfbebd14deb83636639 | Succeeded | 0.134 |  |
| C:\Antiphon\worktrees\card-task-d7e561a7 | refs/heads/feat/card-task-d7e561a7 | 189042dae13c605ed70d8fe9d6f94923ac1ae747 | Succeeded | 0.258 |  |
| C:\Antiphon\worktrees\card-task-d9b74136 | refs/heads/feat/card-task-d9b74136 | bac3ddeb988bb43ee26c4117aba2ef545bd8433c | Succeeded | 0.105 |  |
| C:\Antiphon\worktrees\card-task-d9db421d | refs/heads/feat/card-task-d9db421d | b5e78700ae9a76430c13d75cc03399055dd82e59 | Succeeded | 0.141 |  |
| C:\Antiphon\worktrees\card-task-ddd16c07 | refs/heads/feat/card-task-ddd16c07 | 62a987b6711d1157aac478f15e3c2036ae197432 | Succeeded | 0.295 |  |
| C:\Antiphon\worktrees\card-task-defe909e | refs/heads/feat/card-task-defe909e | 9e482f37de08b3bbf1f607aa27a5dba6e093535b | Canceled | 0.149 | <24h |
| C:\Antiphon\worktrees\card-task-dfc6f878 | refs/heads/feat/card-task-dfc6f878-implementation | f94aa256ece250ddb7d2c3511f7cbfc6627bb823 | Succeeded | 0.701 | tracked changes |
| C:\Antiphon\worktrees\card-task-e0239b6f | refs/heads/feat/card-task-e0239b6f | 9f1cd6d398f572f179c9983b78d52e987b9d6663 | Succeeded | 0.279 |  |
| C:\Antiphon\worktrees\card-task-e2eb87b6 | refs/heads/feat/card-task-e2eb87b6 | 0e4c26f74c41d6cdd792e01b73f2390d5e726298 | Succeeded | 0.135 |  |
| C:\Antiphon\worktrees\card-task-e3922461 | refs/heads/feat/card-task-e3922461 | db19017209a36f2643b9a3c6c921c760e0a8cf9f | Succeeded | 3.601 | identity unproven |
| C:\Antiphon\worktrees\card-task-e3c11469 | refs/heads/feat/card-task-e3c11469 | be91f127cf480ca94835740b7b197d7d75b2ab75 | Succeeded | 0.149 | <24h |
| C:\Antiphon\worktrees\card-task-e6f03caf | refs/heads/feat/card-task-e6f03caf | 1d08ff42db131d7385695be792b487e797f0667a | Succeeded | 0.071 | identity unproven |
| C:\Antiphon\worktrees\card-task-e778e0b8 | refs/heads/feat/card-task-e778e0b8 | f062ff2e3ffa31a7fb39262cf79de49cceb6c364 | Succeeded | 0.319 |  |
| C:\Antiphon\worktrees\card-task-e7d7abed | refs/heads/feat/card-task-e7d7abed | a20b9e7de1d0fd59e9e1ac7beee0125ace87c5a5 | Succeeded | 0.149 |  |
| C:\Antiphon\worktrees\card-task-e94700c3 | refs/heads/feat/card-task-e94700c3 | 94216c6cfd5b60f17ec962e4bf4068198f983a3a | Succeeded | 0.149 |  |
| C:\Antiphon\worktrees\card-task-efc760e0 | refs/heads/feat/card-task-efc760e0 | d45a7915d2c9fa718363db0b0faeb522b08b95e3 | Succeeded | 0.156 |  |
| C:\Antiphon\worktrees\card-task-f23af0f1 | refs/heads/feat/card-task-f23af0f1 | 9224f549cb7e011ce51f32d6e767fb5f4cbc9d9c | Succeeded | 0.134 |  |
| C:\Antiphon\worktrees\card-task-fd9653b8 | refs/heads/feat/card-task-fd9653b8 | 4d9cd57b4923f9d6d9b76cb452a23a006092ef55 | Succeeded | 0.267 |  |
| C:\Antiphon\worktrees\card-task-fe7ceece | refs/heads/feat/card-task-fe7ceece | 27b0701a345dd5d6340cd4ff87f8d84dae92fb75 | Succeeded | 0.149 |  |

## Kept/failed B/C (48)

| Path | Kind | Outcome | Logical GB | Reason (truncated) |
| --- | --- | --- | ---: | --- |
| C:\Antiphon\review-evidence\cea60832 | C | kept | 0.59 | fresh age check failed |
| C:\Antiphon\review-evidence\e3c11469 | C | kept | 0.316 | registered worktree descendant: preserve metadata/source custody |
| C:\Antiphon\review-evidence\task-60ccb73c | C | kept | 0.76 | registered worktree descendant: preserve metadata/source custody |
| C:\Users\lndco\AppData\Local\Temp\antiphon-c415-launch | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope |
| C:\Users\lndco\AppData\Local\Temp\antiphon-c502-v26-ec3d8d68294d47f3a8e9c5e249a9f572 | B | kept | 0.001 | scratch ownership/naming outside proven verifier/test scope |
| C:\Users\lndco\AppData\Local\Temp\antiphon-card37-ceilings | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-card84-grok-shape | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-checkpoints-baseline | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-codex-startup | B | kept | 0 | scratch ownership/naming outside proven verifier/test scope |
| C:\Users\lndco\AppData\Local\Temp\antiphon-disk-4e06595f | B | kept | 0.29 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-grok-startup | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-hooks | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-085aad5499ff4ce2a3cd39496177fc1e | B | kept | 2.393 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-17fd38b7805a4e94823cf2ad5ea94fab | B | kept | 2.39 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-26d3325bdb774b54882cd6a8a846fcd1 | B | kept | 2.391 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-37afb65749d7413b817581c09f2fbf54 | B | kept | 2.393 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-3aaf3eeef685420fa03cd6625932c96e | B | kept | 2.391 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-4a2d60c5880a4b21a01712f0c024a4df | B | kept | 2.393 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-4be78e50691a4e9683788502e6dd4835 | B | kept | 2.387 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-57a3d3b31523464c921a0210247b9f7d | B | kept | 2.391 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-596a66c2179d4fd081b5300cdc0e4991 | B | kept | 2.393 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-5ac8b883f7be459aaf514b6dae4a336b | B | kept | 2.389 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-901564b33eeb4d709f27e7fac744b170 | B | kept | 2.391 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-96997faaaf65430d8b92d6bccf1a4317 | B | kept | 2.387 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-9bf95b88462a4fd9a2ae711bb7a104fe | B | kept | 2.389 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-a18884163de34f618cc1c0a67683ed7d | B | kept | 2.388 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-a51864b028de420d8ad9f31d79fb345d | B | kept | 2.394 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-a95498bfa80846d68a2d00690e8daefa | B | kept | 2.394 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-ae1e8dde884a4d648fd70c03f7152315 | B | kept | 2.393 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-b672c3d8572a42d69d279ae9e1b4bb00 | B | kept | 2.388 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-b67f449e50a14128a2e45036d2ab16fc | B | kept | 2.391 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-b70bc45a87cd45fd9896bd327aa9efd0 | B | kept | 2.388 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-c4c8b6d2b0e14767af448fc1a1708afe | B | kept | 2.392 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-d10daa07ca364051896a548bbe4d93cc | B | kept | 2.387 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-d4fd81eee534475a9f37432cc04747cf | B | kept | 2.388 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-e133d84503424b83820caa2ae2fcab9d | B | kept | 2.39 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-eb32fbf42e50441baf63bc66e3a08621 | B | kept | 2.387 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-f1a113d7d8a141f18ce24c056dbd05c0 | B | kept | 2.387 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-f7720840067043a6a845f4aca5e68d14 | B | kept | 2.389 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-fd0889cebfd34312883249df437de01e | B | kept | 2.389 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-land-verify-ff65b5faabfe431ea281a0ed7ddff372 | B | kept | 2.387 | fresh age check failed |
| C:\Users\lndco\AppData\Local\Temp\antiphon-logs | B | kept | 0.202 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-plans-3803d6805ffd434d921e55cd90b09f69 | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-plans-64426fc0a37c43879578b0345e357544 | B | kept | 0 | scratch ownership/naming outside proven verifier/test scope |
| C:\Users\lndco\AppData\Local\Temp\antiphon-plans-a1518dba90de4733bcbd7c0cfb6053f3 | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-plans-bc2f553c70d7489ca25f117402387ecb | B | kept | 0 | scratch ownership/naming outside proven verifier/test scope |
| C:\Users\lndco\AppData\Local\Temp\antiphon-plans-c93223bb706549258855d7047ae14053 | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |
| C:\Users\lndco\AppData\Local\Temp\antiphon-plans-d385b45f39864e58891ed875a413a669 | B | kept | 0 | modified/created within 24h; scratch ownership/naming outside proven verifier/test scope; descendant modified within 24h |

Failed rows (any kind): 0.
Pending candidates at stop: 356 (125.08 GB logical) - B=329, C=27.

