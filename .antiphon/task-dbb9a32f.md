Five added readers repaired, six inherited reader gaps documented, and effective Final closed verification qualified: 255 passed, zero unresolved failed, one planned Windows skip.

Landing owner/original Code task for this repair: dbb9a32f-0026-4af8-ab19-1f4baae9d28b, superseding a8b33bf7-3165-49e8-8151-2e217d7f6b16. Branch feat/card-task-dbb9a32f; worktree /work/worktrees/task-dbb9a32f; assigned base d13387548b88ac3a6b37dc046370f40d880cae32. Tests-first ed65aaea62895de19666580d70f7503e5ab79763; reader implementation b4aebc7aeab47b9c491fa02248e5c520e78e5d10; fixture dependency repair dbe003007fdeffffaa76da9b1e80ab47b07fbd04. Every slice was committed and pushed without rebase, amend, reset or force push. A report-only publication follows these actual tested SHAs.

Plan: /work/worktrees/task-dbb9a32f/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md. Scoped inherited-reader inventory: /work/worktrees/task-dbb9a32f/docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md. Evidence: /work/worktrees/task-dbb9a32f/.antiphon/c1199r13i; original checkpoint runs /work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7 and /work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-161717-e715.

Implementation and scope

The project-reference XML preflight now reads bounded regular-file bytes/text before parsing memory. BuildConfinement.Git/Evaluate and Invoke-CheckpointGit/Invoke-CheckpointGraphEval share scripts/lib/CheckpointProcessRunner.cs, compiled by the tool and loaded by PowerShell through the existing regular-file reader. The existing 15/30/120-second operation limits cover config inspection, direct-child exit and both output drains. Expiry kills the owned Linux process group and abandons drains. Git overrides fsmonitor/hooks, optional locks, replace refs, external diff, global/system config and configured clean/smudge/process/textconv drivers. The installed-Git status probe verified fsmonitor/filter command suppression in both entry points; textconv is disabled by the config override but no diff/textconv operation was functionally exercised by that status probe. Native Windows custody/race behavior remains unqualified R-2, explicitly stated in the docs.

Git diff against the fetched-master merge-base and blame identify all eleven flagged sites: five added readers fixed; six unchanged-master expressions retained. The six are verify-checkpoint-temp-usage.ps1:22/:109 DLL hash reads, test-run-checkpoint.ps1:353/:356 repository ASCII reads, SourceSnapshot.cs:198-206 and checkpoint-source.ps1:19-26 inherited process-only deadlines with unbounded drains. Exact origins and ordinary/FIFO/held-pipe reproductions are in the short committed inventory and reader-origin.txt/master-expression-comparison.json. Existing accepted diagnostic, staging and execution-loader exclusions remain disclosed. Whitespace tool identities refuse; inherited script Name admits whitespace because Name@Project is nonblank. Review's actual broker admission evidence is cited in the inventory, rather than claiming that broker admission refuses a whitespace Name.

Tests first and ordinary outcomes

The handwritten native supervisor uses FIFO blocking for synchronization and hard child watchdogs, with no synchronization sleeps. Production readers at tests-first commit ed65aaea62895de19666580d70f7503e5ab79763 are unchanged from d13387548. Five ordinary controls passed; all five defect controls failed on unchanged production: project FIFO killed at 4.025s, script Git retained pipe at 20.033s, tool Git at 35.015s, tool graph at 125.103s and script graph at 125.117s. All four added F3 rows are genuine guarded hangs. The same ten controls passed after repair, with no surviving pipe-holder; graph child walls were 121.847/122.611s including startup around their unchanged 120-second operation bounds. No deliberate source mutant or Mutation PC was run.

Full CP-1..CP-21 at b4aebc7aeab47b9c491fa02248e5c520e78e5d10: 255 executed, 254 passed, one fixture failure, one planned Windows skip; 256 unique fresh TRX identities. The tracked_snapshot_contains_committed_files probe extracted the Git function without loading the new helper. Its unchanged-base native control passed; repair production reproduced git-toplevel. An initial diagnostic attempt used the wrong culture-sorted expected order; the corrected exact five-member set control passed. Both diagnostic outcomes are retained. The fixture now loads the shared dependency, preserving every existing assertion and its 60-second timeout. No production tools/scripts changed in this fixture slice.

Exact red-row CP-1 requalification at dbe003007fdeffffaa76da9b1e80ab47b07fbd04: all 133 passed, zero failed/skipped; fresh full 13-case affected class and repaired method verified. Effective original 21-row selection: 255 passed, zero unresolved failed, one planned Windows skip. Actual ordinary executions across both groups: 388 executed, 387 passed, one historical fixture failure, one planned skip; 389 unique TRX identities including the skip. No receipt is edited, merged or relabeled across SHAs.

| CP | Executed | Passed | Failed | Skipped | Actual source | slot | waited |
|---|---:|---:|---:|---:|---|---|---|
| CP-1 | 133 | 133 | 0 | 0 | dbe003007 | granted | 0s |
| CP-2 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-3 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-4 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-5 | 17 | 17 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-6 | 10 | 10 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-7 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-8 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-9 | 13 | 13 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-10 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-11 | 25 | 25 | 0 | 1 | b4aebc7ae | granted | 0s |
| CP-12 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-13 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-14 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-15 | 16 | 16 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-16 | 3 | 3 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-17 | 4 | 4 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-18 | 10 | 10 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-19 | 4 | 4 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-20 | 1 | 1 | 0 | 0 | b4aebc7ae | granted | 0s |
| CP-21 | 10 | 10 | 0 | 0 | b4aebc7ae | granted | 0s |

Every intended class/method has nonzero fresh TRX results. The compiled namespace census 536 passed. Both source groups have dirtyFiles=0, sourceState=clean, buildSource=verified and matching source fingerprints/build stamps. First fingerprint ec8d62663114cb9741bd87e05186f5c3201c4fdd1eba82395251c3e00a114e7a; repair fingerprint 932eda1b23bc05f06b7e98e44a1057c902a37ce8c7fbd1a259d413a03d95b49d. Both unedited original build stamps are retained in ignored evidence.

Both exact-SHA validators pass the first run's nineteen eligible rows (excluding CP-1 and CP-11) and the repaired CP-1. They correctly reject the historical CP-1 red and CP-11 planned skip with row_failed. The repair stored verdict is GREEN/exit 0; separate terminal wait returned 2 solely for retained shadow-copy cleanup. The first stored verdict/wait is RED/exit 1. Full zero-skip certification is not claimed.

V-1 PASS detection/no recovery; V-2 PASS fresh-output refusal; V-3 PASS required Linux receipts with the disclosed Windows sharing skip; V-4 PASS in-memory deletion authority; V-5 PASS bounded admitted regular-file/type/shape controls; V-6 PASS scoped identities/verb outcomes; V-7 PASS five added reader FIFO/exit-and-drain controls. R-1 PASS every named full affected class and census; R-2 PENDING native Windows junction/handle/sharing/custody qualification; R-3 PASS three initial chronological hanging-Git methods in CP-1/7/8, plus one genuine failure-driven full CP-1 rerun after a fixture source change; R-4 PASS sixteen shared-file-reader cases. No loaded stability repetitions or ordinary IDs deferred to another Final round. R-2 is the explicit native qualification limit, not passed.

Commands, cost and cleanup

All builds/test drivers used the host slot gate, were owned and awaited, and source stayed frozen during long runs. Tool bootstrap builds were necessary to execute unchanged/repaired production: first gate waited=0s, held=12s, one warning/zero errors; repaired bootstrap waited=45s, held=8s, two warnings/zero errors. Native tests-first gate waited=0s/held=126s; repaired native waited=121s/held=123s; installed-Git control waited=0s/held=2s. Membership diagnostics used granted gates with waited=0s (initial setup attempt held=3s, corrected held=5s). These native drivers built no test assembly and do not replace ordinary rows.

Checkpoint commands used the committed tool DLL run --plan docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md --expected-source-sha <full SHA> --keep-outputs --serial --max-wait 55s, followed by wait until terminal. Fixture requalification added --rows CP-1 with the new source and isolated bin-c1199r13i-fix/. Both test builds used UseAppHost=false, a forward-slash trailing OutputPath, and granted slots waited=0s. Each compiler reported 713 warnings, zero errors; compiler clocks 6m36.11s and 8m02.23s. The second build was required by a real compiled-fixture repair; the original one-build and 75-minute targets were exceeded. The 240-minute hard ceiling was not approached. No Unit lane, full assembly, unbounded class selection, deadline widening, assertion weakening, unleased retry or extra stability loop was used.

The gate-wrapped native commands were node .antiphon/c1199r13i/native-suite.mjs, native-green-suite.mjs, git-hardening.mjs and membership-base-proof.mjs; exact stdout/outcomes remain in adjacent logs. Run the current full manifest through the checkpoint tool for Final Review; use the current committed HEAD as expected SHA. Bootstrap through scripts/build-slot.ps1 to a fresh producer-owned bin-<name>/ output before invoking the DLL.

Both ~39.8 GB compiler roots and both task-created bootstrap roots were removed only after their users/executors ended. Exact allowlist, resolved containment, nonlinked ancestors/descendants, zero Git-tracked members and regular-file identity/size/time inventory were checked; files were unlinked individually and empty directories removed nonrecursively. Cleanup receipts:

```json
{"target":"/work/worktrees/task-dbb9a32f/tests/Antiphon.Tests/bin-c1199r13","files":97867,"directories":10145,"bytes":39828796996,"absent":true}
{"target":"/work/worktrees/task-dbb9a32f/tests/Antiphon.Tests/bin-c1199r13i-fix","files":97867,"directories":10145,"bytes":39831853980,"absent":true}
{"target":"/work/worktrees/task-dbb9a32f/tools/Antiphon.Checkpoints/bin-c1199r13i-base","files":35,"directories":14,"bytes":19610395,"absent":true}
{"target":"/work/worktrees/task-dbb9a32f/tools/Antiphon.Checkpoints/bin-c1199r13i-tool","files":35,"directories":14,"bytes":19617067,"absent":true}
```

Original ignored receipts/TRX/logs and retained executor shadows remain. No tracked source, unrelated output or daemon bin directory was deleted. GET /api/runner-defaults and /api/session-runners were read; no Runner/Platform pin or fleet location is embedded. Restart: none.

Publication and pending Mutation

Fetched origin/master 00fa6b884a5f127982cff26a677aae74e6666650; merge-tree --write-tree origin/master HEAD exited 0 before the report-only publication (tree 59a9fea243b452a2ae444de00595342d7cf8a21e). The full task base..HEAD evidence guard passed before the report; it is rerun after the report commit. All assigned work is pushed to feat/card-task-dbb9a32f; the final SHA/progress line is in the returned summary.

PC-1 nine FIFO verb/path variants; PC-2 eighteen symlink/directory verb/path variants; PC-3 required-fact/collection/element/empty-shape variants; PC-4 row/build empty/whitespace/null identity guard and admitted verb variants; PC-5 source-stamp type/open/cap/cancellation variants; PC-6 import/coverage and derived-build identity variants; PC-7 malformed-state/verb exception variants; PC-8 project/tool-Git/script-Git/tool-graph/script-graph file/exit/drain/descendant-custody variants and Git config-driver control discovery ALL PENDING method-scoped SourceLanding Mutation. All earlier forged-ledger/output/receipt/owner/master-path variants and missing-control discovery remain pending. Post-land Mutation was NOT run. Candidate docs are unbound under docs/superpowers/mutations/. Caller commissions ordinary Review, lands original Code owner dbb9a32f after Review, then commissions SourceLanding Mutation.

Changed round files

.antiphon/task-a8b33bf7.md
docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md
docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md
docs/testing-and-build.md
scripts/lib/CheckpointProcessRunner.cs
scripts/lib/checkpoint-process-runner.ps1
scripts/lib/checkpoint-usage.ps1
scripts/run-checkpoint.ps1
scripts/test-checkpoint-bounded-reader.ps1
scripts/test-checkpoint-bounded-readers.mjs
tests/Antiphon.Tests/Checkpoints/CheckpointBoundedReaderTests.cs
tests/Antiphon.Tests/Checkpoints/CheckpointTrackedBaselineTests.cs
tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj
tools/Antiphon.Checkpoints/BuildConfinement.cs

Systematic sentence sweep

Whole CARD-1199 diff from merge-base 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2 was searched across comments/docs/plan/test names for every/all/always/never/only/bounded/refuses. Final broad keyword census before adding this report: 749 added matching lines, including non-prose occurrences. Historical SHA-qualified outcomes/before quotations remain historical; local invariant/test assertions are scoped to their named operation. Thirty-four existing reader/identity audit sentences were corrected, plus the plan/current selection, explicit scoped guide/inventory wording, fixture requalification and Windows qualification wording. No existing test was renamed; the new bounded-reader method was added. Exact before/after for every changed documentation sentence (minus = before, plus = after; additions have no prior sentence):

```diff
diff --git a/.antiphon/task-a8b33bf7.md b/.antiphon/task-a8b33bf7.md
index 5420ced82..024899fa6 100644
--- a/.antiphon/task-a8b33bf7.md
+++ b/.antiphon/task-a8b33bf7.md
@@ -6,7 +6,7 @@ Landing owner/original Code task: a8b33bf7-3165-49e8-8151-2e217d7f6b16, supersed
 
 Plan: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md. Evidence: .antiphon/c1199r13h/ and .antiphon/checkpoints/20261013-134107-9c67/. The explicit repair brief overrides the generic Final Unit profile: CP-1..CP-20 only, serial, all named full selected classes plus required ordinary V/R. No Unit or full-assembly run, no unbounded class selection. Checkpoints have exact wildcard filters and census 526 (511 inherited + 15 handwritten new cases). No test timeout, assertion or rerun policy was weakened.
 
-The shared reader classifies Linux paths via statx(AT_SYMLINK_NOFOLLOW) before opening, uses O_NOFOLLOW/O_NONBLOCK, verifies the retained descriptor, caps input and applies a cancellable three-second read deadline. Windows metadata is checked before its reparse-point handle open and the retained handle is verified. Tool source stamps, untracked source hashing, manifest/plan/project/TRX reads and PowerShell counterparts use this boundary. Metadata-only symlink/directory/absent stamps do not open targets; unsupported special files and unreadable regular contents become unprovable. Settlement reports changed/unprovable paths and never restores/deletes resulting source/index entries. Source stamps/receipts cap at 64 MiB; coverage may use its smaller document/source caps. Current-process deletion authority remains unchanged.
+The shared reader classifies Linux paths via statx(AT_SYMLINK_NOFOLLOW) before opening, uses O_NOFOLLOW/O_NONBLOCK, verifies the retained descriptor, caps input and applies a cancellable three-second read deadline. Windows metadata is checked before its reparse-point handle open and the retained handle is verified. Tool source stamps, untracked source hashing, manifest/plan/project/TRX reads and script Repeat XML use this boundary. Repair 13h left the script project-reference preflight unbounded; repair 13i routes that reader through the same file boundary. Inherited exceptions are listed in docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md. Metadata-only symlink/directory/absent stamps do not open targets; unsupported special files and unreadable regular contents become unprovable. Settlement reports changed/unprovable paths and never restores/deletes resulting source/index entries. Source stamps/receipts cap at 64 MiB; coverage may use its smaller document/source caps. Current-process deletion authority remains unchanged.
 
 F1 uses PlanTableImporter before import writes or coverage reports clean. Build identities derive from nonblank isolated output syntax. The tool's direct row name also has an explicit whitespace check. The complete entry-point inventory below names existing exception/legacy boundaries rather than claiming universal guards.
 
@@ -99,40 +99,40 @@ Entry-point census, repeated for repair 13h (row/build identities, unless stated
 - Tool validate: report only, strict JSON duplicate-property/required-fact/identity validation plus source/count/token validation. It does not consume state.json; CP-20's validate control therefore succeeds with an intact report and malformed state.
 - Tool hold, smoke-write, smoke-detach, help/version: operational/diagnostic helpers, no checkpoint row/build identity model. Supplied process/path selectors have their existing contracts.
 - Shared ManifestValidator, PlanTableImporter, ReceiptJson Read/Parse/Validate, RunStateStore TryRead overloads and ReportWriter.RequiredReceiptRefusal are the admission boundaries. Build-source binding models carry directory/project/source facts, not row/build IDs; ReportMerger receives prevalidated models. Run/executor ownership stores validate process/run-directory bindings; repeat/root markers contain their own nonce/process identities.
-- Script run-checkpoint: -Name is mandatory; empty values have PowerShell binding refusal. It has no new static whitespace-name guard; the enabled broker's label admission is its existing whitespace refusal, after the script's result-directory setup. Output identity derives from the isolated OutputPath syntax. This script is not covered by the new import/coverage no-write identity guarantee.
+- Script run-checkpoint: -Name is mandatory; empty values have PowerShell binding refusal. It has no new static whitespace-name guard; whitespace-only Name remains admitted because the constructed Name@Project broker label is nonblank, after the script's result-directory setup. Output identity derives from the isolated OutputPath syntax. This script is not covered by the new import/coverage no-write identity guarantee.
 - Script validate-checkpoint-receipt: offline source/count/token qualification, not the tool's ReceiptJson identity model. It has no explicit build-ID admission check; row/name identity is compared with its receipt line. No universal blank-identity refusal is claimed for coherently forged legacy script inputs. Its path reads now use the bounded native reader. Inspector/temp-usage/orphan harnesses have marker/roster/process identities, not importable checkpoint row/build IDs. checkpoint-task manages task GUID/publication, not checkpoint admission.
 
 The repair's ENFORCE promise is specifically import/coverage plus the already guarded tool receipt consumers. The legacy script boundaries above are listed to avoid repeating the previous universal entry-point claim; they are not silently counted as controls for the new guarantee.
 
 
-## Complete reader census and dispositions
+## Audited reader census and scoped dispositions
 
-Reader census (current source, file:line; each occurrence has a disposition). Exact source and run-path readers use the verified regular handle, 64 MiB maximum (coverage may select smaller limits), three-second cancellable read deadline, Linux pre-open statx/no-follow/nonblocking open and descriptor verification. Symlink/directory/absent content stamps are metadata-only, never target-content reads. No deletion or recovery was added.
+Reader census (repair 13h source, file:line; corrections below disclose inherited exceptions). The admitted readers added or changed by CARD-1199 are bounded after repair 13i; pre-existing unbounded readers and accepted exclusions are listed in docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md. Exact source and run-path readers use the verified regular handle, 64 MiB maximum (coverage may select smaller limits), three-second cancellable read deadline, Linux pre-open statx/no-follow/nonblocking open and descriptor verification. Symlink/directory/absent content stamps are metadata-only, never target-content reads. No deletion or recovery was added.
 
 - scripts/validate-checkpoint-receipt.ps1:50:    [xml]$trx = Read-CheckpointText $TrxPath — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/validate-checkpoint-receipt.ps1:60:    $raw = (Read-CheckpointText ([System.IO.Path]::GetFullPath($Evidence))) — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
-- scripts/run-checkpoint.ps1:133:        $outTask = $proc.StandardOutput.ReadToEndAsync() — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- scripts/run-checkpoint.ps1:134:        $errTask = $proc.StandardError.ReadToEndAsync() — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
+- scripts/run-checkpoint.ps1:133:        $outTask = $proc.StandardOutput.ReadToEndAsync() — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
+- scripts/run-checkpoint.ps1:134:        $errTask = $proc.StandardError.ReadToEndAsync() — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
 - scripts/run-checkpoint.ps1:453:            $presentStamp = Read-CheckpointText $script:stampPath \| ConvertFrom-Json — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/run-checkpoint.ps1:492:        try { $readBack = Read-CheckpointText (Join-Path $script:resultsDirectory 'source.json') \| ConvertFrom-Json; if ($readBack.receipt -cne $line) { throw 'receipt-mismatch' } } — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/run-checkpoint.ps1:542:    [xml]$repeatProjectXml = (Read-CheckpointText $repeatProject) — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/run-checkpoint.ps1:602:    try { $stampValue = Read-CheckpointText $stampPath \| ConvertFrom-Json } — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
-- scripts/run-checkpoint.ps1:759:        $outTask = $proc.StandardOutput.ReadToEndAsync() — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- scripts/run-checkpoint.ps1:760:        $errTask = $proc.StandardError.ReadToEndAsync() — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
+- scripts/run-checkpoint.ps1:759:        $outTask = $proc.StandardOutput.ReadToEndAsync() — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
+- scripts/run-checkpoint.ps1:760:        $errTask = $proc.StandardError.ReadToEndAsync() — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
 - scripts/run-checkpoint.ps1:882:        $bytes = Read-CheckpointBytes $Full — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/run-checkpoint.ps1:1189:    [xml]$doc = Read-CheckpointText $trxPath — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
-- scripts/lib/checkpoint-source.ps1:19:            $copy = $process.StandardOutput.BaseStream.CopyToAsync($stream) — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- scripts/lib/checkpoint-source.ps1:20:            $errorRead = $process.StandardError.ReadToEndAsync() — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
+- scripts/lib/checkpoint-source.ps1:19:            $copy = $process.StandardOutput.BaseStream.CopyToAsync($stream) — inherited process-only timeout; subsequent output drain remains unbounded.
+- scripts/lib/checkpoint-source.ps1:20:            $errorRead = $process.StandardError.ReadToEndAsync() — inherited process-only timeout; subsequent output drain remains unbounded.
 - scripts/lib/checkpoint-source.ps1:101:                [byte[]]$digest = [System.Security.Cryptography.SHA256]::HashData([byte[]](Read-CheckpointBytes $full)) — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/lib/checkpoint-usage.ps1:149:        try { $selected = @(Read-CheckpointText $RosterPath \| ConvertFrom-Json) } — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/lib/checkpoint-usage.ps1:156:            [xml]$doc = Read-CheckpointText $TrxPath — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/lib/checkpoint-usage.ps1:209:        $raw = (Read-CheckpointText $EventsPath) — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/lib/checkpoint-usage.ps1:262:        $prior = Read-CheckpointText $PriorPath \| ConvertFrom-Json — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - tools/Antiphon.Checkpoints/BuildConfinement.cs:197:            var bytes = ReceiptFile.ReadBytes(full); — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
-- tools/Antiphon.Checkpoints/BuildConfinement.cs:410:        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken); — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- tools/Antiphon.Checkpoints/BuildConfinement.cs:411:        var stderr = process.StandardError.ReadToEndAsync(cancellationToken); — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- tools/Antiphon.Checkpoints/BuildConfinement.cs:604:        var stdout = process.StandardOutput.ReadToEndAsync(); — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- tools/Antiphon.Checkpoints/BuildConfinement.cs:605:        var stderr = process.StandardError.ReadToEndAsync(); — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
+- tools/Antiphon.Checkpoints/BuildConfinement.cs:410:        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken); — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
+- tools/Antiphon.Checkpoints/BuildConfinement.cs:411:        var stderr = process.StandardError.ReadToEndAsync(cancellationToken); — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
+- tools/Antiphon.Checkpoints/BuildConfinement.cs:604:        var stdout = process.StandardOutput.ReadToEndAsync(); — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
+- tools/Antiphon.Checkpoints/BuildConfinement.cs:605:        var stderr = process.StandardError.ReadToEndAsync(); — added Git/graph output reader: process-only deadline at repair 13h, shared whole-operation exit/drain deadline after repair 13i.
 - tools/Antiphon.Checkpoints/State/RunStateStore.cs:43:            var json = ReceiptFile.ReadText(path); — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - tools/Antiphon.Checkpoints/Trx/TrxReport.cs:65:            doc = XDocument.Parse(ReceiptFile.ReadText(path)); — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/lib/checkpoint-file-reader.ps1:38:        var bytes = ReadBytes(root, path, limit, afterVerification); — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
@@ -195,11 +195,11 @@ Reader census (current source, file:line; each occurrence has a disposition). Ex
 - tools/Antiphon.Checkpoints/CheckpointApp.cs:697:            ManifestHash = Hash(ReceiptFile.ReadText(Path.Combine(runDirectory, "manifest.resolved.yaml"))), — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/verify-checkpoint-temp-usage.ps1:28:        $binding = Read-CheckpointText $bindingPath \| ConvertFrom-Json — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - scripts/verify-checkpoint-temp-usage.ps1:41:    $binding = Read-CheckpointText $bindingPath \| ConvertFrom-Json — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
-- scripts/verify-checkpoint-temp-usage.ps1:85:$outCopy = $child.StandardOutput.BaseStream.CopyToAsync($outStream) — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- scripts/verify-checkpoint-temp-usage.ps1:86:$errCopy = $child.StandardError.BaseStream.CopyToAsync($errStream) — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
+- scripts/verify-checkpoint-temp-usage.ps1:85:$outCopy = $child.StandardOutput.BaseStream.CopyToAsync($outStream) — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
+- scripts/verify-checkpoint-temp-usage.ps1:86:$errCopy = $child.StandardError.BaseStream.CopyToAsync($errStream) — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
 - tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:116:                    digest = SHA256.HashData(ReceiptFile.ReadBytes(full)); — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
-- tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:198:        var copy = process.StandardOutput.BaseStream.CopyToAsync(output); — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
-- tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:199:        var error = process.StandardError.ReadToEndAsync(); — subprocess output pipe or in-memory reader. Runtime git/MSBuild drivers use their existing process timeout/kill paths; no new local-file open (legacy diagnostic exceptions listed separately).
+- tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:198:        var copy = process.StandardOutput.BaseStream.CopyToAsync(output); — inherited process-only timeout; subsequent output drain remains unbounded.
+- tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:199:        var error = process.StandardError.ReadToEndAsync(); — inherited process-only timeout; subsequent output drain remains unbounded.
 - tools/Antiphon.Checkpoints/Evidence/ReceiptFile.cs:12:    public static byte[] ReadBytes(string path, CancellationToken cancellationToken = default) — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - tools/Antiphon.Checkpoints/Evidence/ReceiptFile.cs:22:            return ConfinedFileReader.ReadBytes(Path.GetDirectoryName(Path.GetFullPath(path))!, path, 64 * 1024 * 1024, cancellationToken: cancellationToken); — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
 - tools/Antiphon.Checkpoints/Evidence/ReceiptFile.cs:28:    public static string ReadText(string path) — bounded regular-file reader (direct or shared ReceiptFile/Read-CheckpointText/Bytes); parsing/hash of its memory bytes is not another path open.
@@ -211,25 +211,25 @@ Reader census (current source, file:line; each occurrence has a disposition). Ex
 - tools/Antiphon.Checkpoints/Evidence/HostSnapshot.cs:55:            var output = process.StandardOutput.ReadToEnd().Trim(); — OUT OF SCOPE: inherited dotnet --version host diagnostic pipe; not a repository/run-derived file and no source eligibility authority.
 - tools/Antiphon.Checkpoints/Evidence/GitSnapshot.cs:44:            var stdout = process.StandardOutput.ReadToEnd(); — OUT OF SCOPE: inherited human diagnostic git.txt collector; synchronous pipe read precedes its nominal timeout and is not qualified as bounded. It does not stamp/certify source or consume a persisted receipt.
 
-Git worktree reads: BuildConfinement.Git status/index/ls-files are child-process bounded at 30s; Evaluate project graph at 120s; SourceSnapshot.RunGit and Get-CheckpointGitBytes status/diff are bounded at 30s. SafeDelete git preflight is bounded at its existing configured deadline; baseline worktree add/remove use ProcessDriver deadlines. Git's internal worktree reads cannot be handle-verified here; timeout/owned-child kill is the boundary. No bash cat/sha256sum/read repository-content reader was found in the tool or production checkpoint scripts. PowerShell/.NET assembly and script loaders are execution bootstrap mechanisms, explicitly outside the data-reader promise.
+Git worktree reads: repair 13h bounded direct-child exit at 15/30/120 seconds, but the six drains listed by Review could exceed those deadlines. Repair 13i bounds the four added Git/graph readers; inherited SourceSnapshot.RunGit and Get-CheckpointGitBytes remain process-only deadlines. SafeDelete retains its distinct bounded Drain. Kernel/diagnostic, staging and execution-loader exclusions are listed in docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md.
 
 Additional reader sites (bounded shared readers/memory parsing unless explicitly excluded):
-- scripts/lib/checkpoint-source.ps1:19:            $copy = $process.StandardOutput.BaseStream.CopyToAsync($stream) — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
+- scripts/lib/checkpoint-source.ps1:19:            $copy = $process.StandardOutput.BaseStream.CopyToAsync($stream) — inherited process-only timeout; subsequent output drain remains unbounded.
 - scripts/checkpoint-orphan-owner.ps1:6:$stat = [IO.File]::ReadAllText("/proc/$($current.Id)/stat") — OUT OF SCOPE fixed kernel metadata.
 - scripts/checkpoint-orphan-owner.ps1:12:    Boot = ([IO.File]::ReadAllText('/proc/sys/kernel/random/boot_id')).Trim() — OUT OF SCOPE fixed kernel metadata.
 - scripts/lib/checkpoint-file-reader.ps1:56:        using var stream = new FileStream(handle, FileAccess.Read); — verified native bounded file reader, or parse/hash of its memory bytes.
 - scripts/lib/checkpoint-file-reader.ps1:60:        while ((count = stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit - (int)content.Length + 1)), deadline.Token).AsTask().WaitAsync(deadline.Token).GetAwaiter().GetResult()) != 0) — verified native bounded file reader, or parse/hash of its memory bytes.
-- scripts/verify-checkpoint-temp-usage.ps1:85:$outCopy = $child.StandardOutput.BaseStream.CopyToAsync($outStream) — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
-- scripts/verify-checkpoint-temp-usage.ps1:86:$errCopy = $child.StandardError.BaseStream.CopyToAsync($errStream) — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
+- scripts/verify-checkpoint-temp-usage.ps1:85:$outCopy = $child.StandardOutput.BaseStream.CopyToAsync($outStream) — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
+- scripts/verify-checkpoint-temp-usage.ps1:86:$errCopy = $child.StandardError.BaseStream.CopyToAsync($errStream) — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
 - scripts/verify-checkpoint-orphan-usage.ps1:41:    $stat = [IO.File]::ReadAllText("/proc/$($process.Id)/stat") — OUT OF SCOPE fixed kernel metadata.
 - scripts/verify-checkpoint-orphan-usage.ps1:46:        Boot = ([IO.File]::ReadAllText('/proc/sys/kernel/random/boot_id')).Trim() — OUT OF SCOPE fixed kernel metadata.
 - scripts/test-checkpoint-stamp.ps1:53:if ($Shape -eq 'symlink' -and [IO.File]::ReadAllText("$Root/target") -cne 'target bytes') { throw 'link target changed' } — OUT OF SCOPE known-created fixture/control reader.
-- scripts/run-checkpoint.ps1:671:            $stdoutTask = $proc.StandardOutput.ReadLineAsync() — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
-- scripts/run-checkpoint.ps1:672:            $stderrTask = $proc.StandardError.ReadLineAsync() — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
-- scripts/run-checkpoint.ps1:680:                    $stdoutTask = if ($null -eq $line) { $null } else { $proc.StandardOutput.ReadLineAsync() } — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
-- scripts/run-checkpoint.ps1:683:                    $stderrTask = if ($null -eq $line) { $null } else { $proc.StandardError.ReadLineAsync() } — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
-- scripts/test-run-checkpoint.ps1:353:    $runnerBytes = @([IO.File]::ReadAllBytes($script:Runner) | Where-Object { $_ -gt 127 }) — OUT OF SCOPE known-created fixture/control reader.
-- scripts/test-run-checkpoint.ps1:356:    $harnessBytes = @([IO.File]::ReadAllBytes($harness) | Where-Object { $_ -gt 127 }) — OUT OF SCOPE known-created fixture/control reader.
+- scripts/run-checkpoint.ps1:671:            $stdoutTask = $proc.StandardOutput.ReadLineAsync() — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
+- scripts/run-checkpoint.ps1:672:            $stderrTask = $proc.StandardError.ReadLineAsync() — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
+- scripts/run-checkpoint.ps1:680:                    $stdoutTask = if ($null -eq $line) { $null } else { $proc.StandardOutput.ReadLineAsync() } — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
+- scripts/run-checkpoint.ps1:683:                    $stderrTask = if ($null -eq $line) { $null } else { $proc.StandardError.ReadLineAsync() } — legacy subprocess output loop without a local time/cancellation deadline; an enclosing caller must supply one.
+- scripts/test-run-checkpoint.ps1:353:    $runnerBytes = @([IO.File]::ReadAllBytes($script:Runner) | Where-Object { $_ -gt 127 }) — pre-existing repository-path ASCII content reader, unbounded on FIFO; not a known-created fixture.
+- scripts/test-run-checkpoint.ps1:356:    $harnessBytes = @([IO.File]::ReadAllBytes($harness) | Where-Object { $_ -gt 127 }) — pre-existing repository-path ASCII content reader, unbounded on FIFO; not a known-created fixture.
 - scripts/test-run-checkpoint.ps1:629:            $nonce = ([System.IO.File]::ReadAllText($noncePath)).Trim() — OUT OF SCOPE known-created fixture/control reader.
 - scripts/test-run-checkpoint.ps1:646:            $nonce = ([System.IO.File]::ReadAllText($noncePath)).Trim() — OUT OF SCOPE known-created fixture/control reader.
 - scripts/test-run-checkpoint.ps1:712:    $parts = ([System.IO.File]::ReadAllText($entry)).Split('|') — OUT OF SCOPE known-created fixture/control reader.
@@ -239,13 +239,13 @@ Additional reader sites (bounded shared readers/memory parsing unless explicitly
 - tools/Antiphon.Checkpoints/State/RunStateStore.cs:46:            return ReceiptJson.Parse<RunState>(json, Json); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Program.cs:377:            return ReceiptJson.Read<ReportModel>(Path.Combine(run, "report.json"), ReportWriter.Json); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Program.cs:443:            var report = ReceiptJson.Parse<ReportModel>(raw, ReportWriter.Json); — verified native bounded file reader, or parse/hash of its memory bytes.
-- tools/Antiphon.Checkpoints/CheckpointApp.cs:28:        public Action<string, string>? CopyToolOutput { get; init; } — verified native bounded file reader, or parse/hash of its memory bytes.
+- tools/Antiphon.Checkpoints/CheckpointApp.cs:28:        public Action<string, string>? CopyToolOutput { get; init; } — callback declaration; no read by itself.
 - tools/Antiphon.Checkpoints/CheckpointApp.cs:118:        var request = ReceiptJson.Read<RunRequest>(Path.Combine(runDirectory, "request.json"), Json) — verified native bounded file reader, or parse/hash of its memory bytes.
-- tools/Antiphon.Checkpoints/CheckpointApp.cs:554:            (runtime.CopyToolOutput ?? ShadowCopy.CopyToolOutput)(ToolSource(runtime), Path.Combine(runDirectory, "tool")); — verified native bounded file reader, or parse/hash of its memory bytes.
+- tools/Antiphon.Checkpoints/CheckpointApp.cs:554:            (runtime.CopyToolOutput ?? ShadowCopy.CopyToolOutput)(ToolSource(runtime), Path.Combine(runDirectory, "tool")); — executing-tool staging via SafeWrite.CopyOverwrite/File.Copy; accepted unbounded staging exclusion.
 - tools/Antiphon.Checkpoints/ProjectWriteRoots.cs:138:        using var reader = XmlReader.Create(new StringReader(ReceiptFile.ReadText(projectFile)), settings); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Cleanup/ProcessIdentity.cs:63:        var stat = File.ReadAllText($"/proc/{pid}/stat"); — OUT OF SCOPE fixed kernel metadata.
 - tools/Antiphon.Checkpoints/Cleanup/ProcessIdentity.cs:96:            return File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim(); — OUT OF SCOPE fixed kernel metadata.
-- tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:198:        var copy = process.StandardOutput.BaseStream.CopyToAsync(output); — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
+- tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:198:        var copy = process.StandardOutput.BaseStream.CopyToAsync(output); — inherited process-only timeout; subsequent output drain remains unbounded.
 - tools/Antiphon.Checkpoints/Cleanup/RunOwnership.cs:25:            var record = ReceiptJson.Read<RunOwnership>(path, Json); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Evidence/ReceiptFile.cs:22:            return ConfinedFileReader.ReadBytes(Path.GetDirectoryName(Path.GetFullPath(path))!, path, 64 * 1024 * 1024, cancellationToken: cancellationToken); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Cleanup/ToolCopyCleanup.cs:148:            var record = ReceiptJson.Read<ExecutorOwnership>(Path.Combine(runDirectory, FileName), new System.Text.Json.JsonSerializerOptions()); — verified native bounded file reader, or parse/hash of its memory bytes.
@@ -259,7 +259,7 @@ Additional reader sites (bounded shared readers/memory parsing unless explicitly
 - tools/Antiphon.Checkpoints/Cleanup/SafeWrite.cs:257:        stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None); — OUT OF SCOPE write/exclusive gate operation, no content-read API.
 - tools/Antiphon.Checkpoints/Cleanup/SafeWrite.cs:280:        stream = new FileStream(full, FileMode.Append, FileAccess.Write, share); — OUT OF SCOPE write/exclusive gate operation, no content-read API.
 - tools/Antiphon.Checkpoints/Cleanup/SafeWrite.cs:302:        stream = new FileStream(full, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); — OUT OF SCOPE write/exclusive gate operation, no content-read API.
-- tools/Antiphon.Checkpoints/Execution/ShadowCopy.cs:5:    public static void CopyToolOutput(string toolDirectory, string destination) — verified native bounded file reader, or parse/hash of its memory bytes.
+- tools/Antiphon.Checkpoints/Execution/ShadowCopy.cs:5:    public static void CopyToolOutput(string toolDirectory, string destination) — executing-tool staging via SafeWrite.CopyOverwrite/File.Copy; accepted unbounded staging exclusion.
 - tools/Antiphon.Checkpoints/Report/ReportWriter.cs:97:            var report = ReceiptJson.Read<ReportModel>(jsonPath, Json); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Report/ReportWriter.cs:111:                    var binding = ReceiptJson.Read<CheckpointBuildBinding>(path, CheckpointApp.Json); — verified native bounded file reader, or parse/hash of its memory bytes.
 - tools/Antiphon.Checkpoints/Execution/ProcessDriver.cs:33:            _process.BeginOutputReadLine(); — subprocess/HTTP stream under existing driver/deadline, not a local source/receipt path open.
@@ -382,3 +382,5 @@ handoff: Review the pushed a8b33bf7 landing owner; F3 bounded stamping, F1 impor
 artifact: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md
 
 [antiphon-report:a8b33bf7 done]
+
+Reader omissions corrected by repair 13i: scripts/run-checkpoint.ps1:335 was an added unbounded project reader and now parses bounded memory. scripts/verify-checkpoint-temp-usage.ps1:22/:109 are inherited unbounded repository DLL hash reads; scripts/test-run-checkpoint.ps1:353/:356 are inherited unbounded repository ASCII reads. SourceSnapshot.RunGit and Get-CheckpointGitBytes retain inherited unbounded output drains. The full origins/reproductions and unchanged-master comparison are in docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md.
diff --git a/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md b/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md
index 3d0d3ed9e..8e46a6873 100644
--- a/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md
+++ b/docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md
@@ -20,7 +20,7 @@ Closed scope overrides broad Unit profile per repair brief. Linux only. One isol
 
 | CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
 |---|---|---|---|---|---|---|---:|---:|
-| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1199r13/` | closed-checkpoints | `/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*\|BaselineComparerTests*\|RunSchedulerTests*\|CheckpointManifestTests*\|CheckpointTaskOwnershipTests*\|CheckpointToolCopyCleanupTests*\|WaitCommandTests*\|RunStateStoreTests*\|CheckpointAppTests*\|CheckpointNamespaceCensusUsageTests*\|CheckpointOutputConfinementTests*\|CheckpointTrackedBaselineTests*)/*` | R-1 R-3 | all listed, 0 failed/skipped | 133 | 15 |
+| CP-1 | S2 | `tests/Antiphon.Tests -> bin-c1199r13i-fix/` | closed-checkpoints | `/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*\|BaselineComparerTests*\|RunSchedulerTests*\|CheckpointManifestTests*\|CheckpointTaskOwnershipTests*\|CheckpointToolCopyCleanupTests*\|WaitCommandTests*\|RunStateStoreTests*\|CheckpointAppTests*\|CheckpointNamespaceCensusUsageTests*\|CheckpointOutputConfinementTests*\|CheckpointTrackedBaselineTests*)/*` | R-1 R-3 | all listed, 0 failed/skipped | 133 | 15 |
 | CP-2 | S2 | CP-1 | staged-untracked | `/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
 | CP-3 | S2 | CP-1 | staged-ignored | `/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
 | CP-4 | S2 | CP-1 | index-added | `/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed*` | V-1 | bytes and index kept, invalid receipt | 1 | 2 |
@@ -40,6 +40,7 @@ Closed scope overrides broad Unit profile per repair brief. Linux only. One isol
 | CP-18 | S2 | CP-1 | source-stamp-shapes | `/*/*/CheckpointStampSafetyTests/tracked_stamp_shapes_are_bounded_and_leave_results_in_place*` | V-1 V-5 F3 | ten tool/script shape rows, four-second child watchdog | 10 | 1 |
 | CP-19 | S2 | CP-1 | importer-identities | `/*/*/CheckpointStampSafetyTests/import_and_coverage_refuse_blank_identities_without_writes*` | V-6 F1 | four handwritten identities, import/coverage no-write refusals | 4 | 1 |
 | CP-20 | S2 | CP-1 | malformed-state-verbs | `/*/*/CheckpointReceiptSafetyTests/malformed_state_shape_by_verb_table_matches_documented_exits*` | V-6 F2 | five shapes x two collections x nine verbs with documented reasons | 1 | 1 |
+| CP-21 | S2 | CP-1 | changed-reader-deadlines | `/*/*/CheckpointBoundedReaderTests/*` | V-7 F1 F3 | ten ordinary/FIFO/held-pipe controls, no surviving descendant | 10 | 6 |
 
 ### Mutation handoff
 
@@ -49,7 +50,7 @@ No deliberate mutants run by Code. All PCs/variants and missing-control discover
 
 Repair 13g estimated ordinary floor 44 minutes plus implementation (42 inherited minutes plus CP-17's two minutes). Authoritative repair 13g task budget is 50 minutes. No broad assembly or unbounded classes selected.
 
-The continuation sections below record historical source-qualified outcomes and obligations. Repair 13g is the current selection; earlier unresolved states and budgets describe their named historical sources.
+The continuation sections below record historical source-qualified outcomes and obligations. Repair 13i is the current selection; earlier unresolved states and budgets describe their named historical sources.
 
 ### Historical repair 12c result (superseded; not repair 13 evidence)
 
@@ -126,3 +127,19 @@ The complete CP-1..CP-20 run at 0ad1c58b4aaf234f399e7a545b470f9e41229b05 execute
 Required repair verification: CP-18 and CP-19 at the committed fixture SHA, covering the full fourteen-case CheckpointStampSafetyTests class with one fresh isolated build. Unaffected green rows retain their actual 0ad1c58b4aaf234f399e7a545b470f9e41229b05 provenance; no receipt merge/relabeling across SHAs. This genuine fixture repair requires an additional build beyond the original one-build/60-minute target. All ordinary IDs remain pending until the red row is requalified; R-2 and every PC remain pending as before.
 
 Repair 13h ordinary qualification completed: full CP-1..CP-20 at 0ad1c58b4aaf234f399e7a545b470f9e41229b05 retained 238 passes, seven CP-18 fixture failures and the planned Windows skip; corrected full fourteen-case class CP-18/CP-19 passed at ddcb90c76bce034b83e41ef2c0e7bce099d780b2. Effective closed selection: 245 passed, zero unresolved failed, one planned native-Windows skip. Source/test production is unchanged by the fixture-only slice; no receipt SHA was relabeled or merged. Both validators pass their respective eligible selections and reject the original CP-11/CP-18 red/skip rows. All Linux V/R complete; R-2 and PC-1..PC-7/all inherited variants remain pending. Full report and unedited CHECKPOINT lines: .antiphon/task-a8b33bf7.md. Restart none; next ordinary Review, then caller landing and SourceLanding Mutation.
+
+### Repair 13i authoritative scope
+
+Landing owner Code dbb9a32f-0026-4af8-ab19-1f4baae9d28b, branch feat/card-task-dbb9a32f, base d13387548b88ac3a6b37dc046370f40d880cae32; add commits, no rebase. V-7: the admitted file/process readers added or changed by CARD-1199 refuse nonregular files before open and bound process exit plus output drain at their existing 3/15/30/120-second limits. Inherited readers and accepted diagnostic/staging/execution-loader exclusions are listed in 2026-10-13-card-1199-inherited-readers.md and retain their limitations. CP-21 adds ten cases; native tests-first execution uses the identical handwritten child supervisor against unchanged base production, without a test-assembly build. Final closed scope CP-1..CP-21 expects 255 passes plus one disclosed Windows skip; namespace census 536. No Unit lane or full assembly. Budget 75 minutes; one isolated test build plus small gated tool bootstraps. All previous PCs and PC-8 (project/tool-git/script-git/tool-eval/script-eval FIFO or pipe deadline bypass variants) remain pending method-scoped SourceLanding Mutation. Restart: none.
+
+### Repair 13i fixture dependency requalification
+
+Full CP-1..CP-21 at b4aebc7aeab47b9c491fa02248e5c520e78e5d10 recorded 254 passes, one tracked_snapshot_contains_committed_files fixture failure, and one planned Windows skip. CP-21 passed all ten cases; compiled census 536 passed. The probe extracts functions without the new shared runner bootstrap, so Invoke-CheckpointGit cannot resolve its type and returns git-unavailable / git-toplevel. A native membership control passes on unchanged d13387548 and reproduces this new fixture failure on repair production. The fixture now dot-sources the shared runner before extracting the functions, preserving its five membership assertions and 60-second timeout. No production reader changed.
+
+Required requalification is the exact red CP-1 row, all 133 cases, including the full affected 13-case CheckpointTrackedBaselineTests class. Run --rows CP-1 at the committed fixture SHA using the newly named isolated bin-c1199r13i-fix/ output. CP-2..CP-21 retain their actual b4aebc7aeab47b9c491fa02248e5c520e78e5d10 provenance. The extra isolated test build is necessary because compiled test source changed; no build stamp or receipt is relabeled. This is a failure-driven red-row rerun, not an additional stability proof; the three initial CP-1/7/8 hanging-Git passes remain distinct. The first compiler output was removed only after the full run ended, using an exact producer allowlist, checked regular-file inventory and zero tracked members; its unedited build stamp is preserved externally in ignored evidence.
+
+| PC | Exact method | Variants | Status |
+|---|---|---|---|
+| PC-8 | `/*/*/CheckpointBoundedReaderTests/changed_readers_bound_fifo_and_exited_child_pipes` | project precheck/nonblocking-open bypass; tool Git/script Git/tool graph/script graph exit-and-drain deadline or descendant-custody bypass, each with ordinary control; Git config-driver control discovery | pending method-scoped SourceLanding Mutation |
+
+Repair 13i ordinary qualification completed: CP-1..CP-21 at b4aebc7aeab47b9c491fa02248e5c520e78e5d10 recorded 254 passes, one fixture failure and the planned Windows skip; the exact full 133-case CP-1 rerun at dbe003007fdeffffaa76da9b1e80ab47b07fbd04 passed. Effective closed selection: 255 passed, zero unresolved failed, one planned skip. Fresh TRX inspection covers every intended class/method and census 536. Both validators accept the first run's nineteen eligible rows and the repaired CP-1 at their actual SHAs, and reject the historical red/skip rows. No receipt was merged, edited or relabeled. All Linux V/R are complete; R-2 and PC-1..PC-8/all variants remain pending. This second isolated build was required for the fixture-only dependency fix and exceeded the original one-build/75-minute target. Report: .antiphon/task-dbb9a32f.md. Restart: none; next ordinary Review, caller landing of Code dbb9a32f, then SourceLanding Mutation.
diff --git a/docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md b/docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md
new file mode 100644
index 000000000..66588c184
--- /dev/null
+++ b/docs/superpowers/plans/2026-10-13-card-1199-inherited-readers.md
@@ -0,0 +1,20 @@
+# CARD-1199 repair 13i reader scope
+
+The admitted readers added or changed by CARD-1199 are bounded; the following pre-existing readers are not. This means the admitted content/Git/graph readers, with the diagnostic, staging and execution-loader exclusions below. It is not a certificate for every repository operation. Lines refer to d13387548b88ac3a6b37dc046370f40d880cae32, so the inventory remains stable after repair.
+
+Comparison: merge-base with fetched origin/master is 6e36b695f9ead5f16e9bb05a7f4033cd0c432cf2. git diff merge-base..d13387548 and git blame identify the reader expressions, rather than treating a file with other changes as a wholly new reader. The six inherited expressions below are identical on master. Provenance and native outcomes are retained in .antiphon/c1199r13i/reader-origin.txt and the Final Review evidence /work/review-evidence/task-9f9613bd/.
+
+| Reader | Origin | Reproduction / limitation |
+| --- | --- | --- |
+| scripts/verify-checkpoint-temp-usage.ps1:22 | ca876f92cd, unchanged | Get-FileHash of repository-derived DLL blocks opening a FIFO; ordinary hash passes, five-second external watchdog kills and awaits the child. |
+| scripts/verify-checkpoint-temp-usage.ps1:109 | ca876f92cd, unchanged | Same after-run DLL hash: ordinary passes, FIFO exceeds five seconds. |
+| scripts/test-run-checkpoint.ps1:353 | 00fdfed4a7, unchanged | ASCII check reads the repository script path; ordinary passes, FIFO exceeds five seconds. This is not a known-created fixture. |
+| scripts/test-run-checkpoint.ps1:356 | 00fdfed4a7, unchanged | ASCII check reads its own repository harness path; ordinary passes, FIFO exceeds five seconds. |
+| tools/Antiphon.Checkpoints/Evidence/SourceSnapshot.cs:198-206 | 31411ea310, unchanged | RunGit bounds direct-child exit at 30 seconds but not subsequent output drain. An exited child leaving a descendant holding stderr exceeds the 35-second watchdog; installed Git with core.fsmonitor hook reproduces. |
+| scripts/lib/checkpoint-source.ps1:19-26 | 31411ea310, unchanged | Get-CheckpointGitBytes has the same process-only 30-second deadline and unbounded drain; held-pipe and installed-Git controls exceed 35 seconds. |
+
+Fixed admitted readers: scripts/run-checkpoint.ps1:335 (new project XML reader, 2c531276f0), :133-141 (new Git reader, 8017666882), :759-767 (new graph reader, c761d418b5), and tools/Antiphon.Checkpoints/BuildConfinement.cs:604-616/:410-421 (new Git/graph readers, c761d418b5). The project reader uses Read-CheckpointText and parses memory; the four process readers use the same CheckpointProcessRunner compiled for the tool and loaded by PowerShell. Its deadline includes config inspection, process exit and both output drains. Expiry kills the owned Linux process group and abandons pending drains. Windows attempts job assignment and tree/job termination; its pre-assignment child race remains unqualified under R-2. Git overrides fsmonitor/hooks, optional locks, replace refs, external diff, global/system config and named clean/smudge/process/textconv drivers. Linux FIFO/held-pipe cases have ordinary controls and hard child watchdogs; Windows remains pending R-2.
+
+Accepted exclusions retained from Review: fixed kernel metadata (/proc identity and host metrics), legacy human diagnostic GitSnapshot/HostSnapshot collectors, executing-tool staging via SafeWrite.CopyOverwrite/File.Copy and ShadowCopy.CopyFiles, script/assembly execution loading, known-created test fixture content, and checkpoint-task publication helpers. CopyToolOutput is a callback property, not a reader. These staging/diagnostic paths are not certified as bounded. The legacy verify-checkpoint-temp-usage output stream loop and standalone run-checkpoint Invoke-Dotnet line loop have no local deadline; an enclosing caller may bound them, but this document does not claim they do.
+
+Whitespace identity scope: tool import/coverage and strict receipt consumers refuse null/empty/whitespace row/build IDs, as CP-17/CP-19 prove. Script run-checkpoint -Name retains PowerShell's empty-string binding refusal and admits whitespace-only names; its composite Name@Project broker label is nonblank. This parameter and behavior pre-exist unchanged on master. Review's real broker control was granted and released after a 180-second wait (whitespace-label.log), disproving a broker whitespace-name guard. Legacy script validation also has no universal whitespace build-ID guard. No inherited identity behavior is changed by this repair.
diff --git a/docs/testing-and-build.md b/docs/testing-and-build.md
index 3a8537df3..c4789dd99 100644
--- a/docs/testing-and-build.md
+++ b/docs/testing-and-build.md
@@ -1156,3 +1156,9 @@ unconfirmed it continues until its wait limit. Nullable diagnostic fields may be
 when no entries are required. Receipt reads have a 64 MiB bound. Markdown digests hash exact bytes.
 
 CARD-1199 source/content stamping opens regular files only through the verified native reader, with a 64 MiB cap and a cancellable three-second read deadline. Links/directories/absent paths contribute metadata-only shape stamps; unsupported special files and unreadable regular contents are unprovable. Settlement lists changed/unprovable paths and leaves the resulting filesystem shape and index untouched. Import and coverage refuse blank checkpoint identities before writing a manifest or reporting clean coverage; build identities are derived from nonblank isolated output syntax.
+
+### CARD-1199 repair 13i reader qualification
+
+The admitted content/Git/graph readers added or changed by CARD-1199 are bounded; the pre-existing unbounded readers, unchanged-master origins, reproductions, identity exceptions and accepted diagnostic/staging/execution-loader exclusions are listed in [the scoped reader inventory](superpowers/plans/2026-10-13-card-1199-inherited-readers.md). In particular, inherited SourceSnapshot.RunGit and Get-CheckpointGitBytes bound process exit but retain unbounded output drains. The new project preflight uses the existing regular-file/size/cancellation boundary; the four added Git/graph readers share CheckpointProcessRunner, whose 15/30/120-second deadline covers config inspection, exit and both drains. Linux process groups retain descendant custody after direct-child exit. Windows job assignment is attempted after launch; native qualification, including the pre-assignment child race, remains R-2.
+
+Tool import/coverage and strict receipt admission refuse whitespace row/build IDs. The inherited PowerShell -Name boundary admits whitespace because Name@Project is nonblank; broker label admission does not supply a whitespace-name guard. The standalone Invoke-Dotnet and legacy acceptance-harness output loops have no local deadline.

```

Unedited tests-first failure output

```text
BUILD SLOT granted lease=c29a9470-528a-4a06-9036-817d7b8cafe2 waited=0s maxcpucount=6
{"mode":"tool-eval","held":false,"bound":125000,"elapsed":1001,"expired":false,"survivor":false,"code":0,"signal":null,"stdout":"PASS tool-eval held=False\n","stderr":""}
{"mode":"tool-git","held":false,"bound":35000,"elapsed":1065,"expired":false,"survivor":false,"code":0,"signal":null,"stdout":"PASS tool-git held=False\n","stderr":""}
{"mode":"script-git","held":false,"bound":20000,"elapsed":1400,"expired":false,"survivor":false,"code":0,"signal":null,"stdout":"PASS script-git held=False\n","stderr":""}
{"mode":"script-eval","held":false,"bound":125000,"elapsed":1375,"expired":false,"survivor":false,"code":0,"signal":null,"stdout":"PASS script-eval held=False\n","stderr":""}
{"mode":"project","held":false,"bound":4000,"elapsed":1447,"expired":false,"survivor":false,"code":0,"signal":null,"stdout":"PASS project held=False\n","stderr":""}
{"mode":"project","held":true,"bound":4000,"elapsed":4025,"expired":true,"survivor":false,"code":null,"signal":"SIGKILL","stdout":"","stderr":""}
{"mode":"script-git","held":true,"bound":20000,"elapsed":20033,"expired":true,"survivor":false,"code":null,"signal":"SIGKILL","stdout":"","stderr":""}
{"mode":"tool-git","held":true,"bound":35000,"elapsed":35015,"expired":true,"survivor":false,"code":null,"signal":"SIGKILL","stdout":"","stderr":""}
{"mode":"tool-eval","held":true,"bound":125000,"elapsed":125103,"expired":true,"survivor":false,"code":null,"signal":"SIGKILL","stdout":"","stderr":""}
{"mode":"script-eval","held":true,"bound":125000,"elapsed":125117,"expired":true,"survivor":false,"code":null,"signal":"SIGKILL","stdout":"","stderr":""}
[{"mode":"project","shape":"ordinary","code":0},{"mode":"project","shape":"held","code":1},{"mode":"tool-git","shape":"ordinary","code":0},{"mode":"tool-git","shape":"held","code":1},{"mode":"script-git","shape":"ordinary","code":0},{"mode":"script-git","shape":"held","code":1},{"mode":"tool-eval","shape":"ordinary","code":0},{"mode":"tool-eval","shape":"held","code":1},{"mode":"script-eval","shape":"ordinary","code":0},{"mode":"script-eval","shape":"held","code":1}]
BUILD SLOT released lease=c29a9470-528a-4a06-9036-817d7b8cafe2 held=126s

```

Unedited CHECKPOINT lines, first full run

```text
CHECKPOINT CP-1 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*|BaselineComparerTests*|RunSchedulerTests*|CheckpointManifestTests*|CheckpointTaskOwnershipTests*|CheckpointToolCopyCleanupTests*|WaitCommandTests*|RunStateStoreTests*|CheckpointAppTests*|CheckpointNamespaceCensusUsageTests*|CheckpointOutputConfinementTests*|CheckpointTrackedBaselineTests*)/* executed=133 passed=132 failed=1 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-1/run.trx slot=granted waited=30s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_untracked_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointTrackedBaselineTests/staged_ignored_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointTrackedBaselineTests/index_added_file_is_kept_and_indexed* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointShapeConflictTests/* executed=17 passed=17 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointOwnedOutputTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/OutputCleanupTests/hanging_git_is_refused_and_killed_within_the_bound* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/RowRunnerTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-10 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointTrackedBaselineTests/passing_script_requires_writable_source_receipt* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-11 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/(ReportWriterTests*|CheckpointSourceStateTests*|CheckpointSourceExecutionTests*|EvidenceFolderTests*)/* executed=25 passed=25 failed=0 skipped=1 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointReceiptSafetyTests/linux_fifo_receipts_refuse_within_four_seconds* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointReceiptSafetyTests/symlink_and_directory_receipts_refuse_within_four_seconds* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointReceiptSafetyTests/malformed_collection_and_string_shapes_return_named_refusals* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/PlanCoverageHandleTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointBuildBindingPathTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointReceiptSafetyTests/receipt_row_and_build_identities_refuse_blank_without_writing* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointStampSafetyTests/tracked_stamp_shapes_are_bounded_and_leave_results_in_place* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointStampSafetyTests/import_and_coverage_refuse_blank_identities_without_writes* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointReceiptSafetyTests/malformed_state_shape_by_verb_table_matches_documented_exits* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 build=reused filter=/*/*/CheckpointBoundedReaderTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-154005-dbf7/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=b4aebc7aeab47b9c491fa02248e5c520e78e5d10 sourceState=clean buildSource=verified
```

Unedited CHECKPOINT line, exact red-row repair

```text
CHECKPOINT CP-1 commit=dbe003007fdeffffaa76da9b1e80ab47b07fbd04 build=ok filter=/*/Antiphon.Tests.Checkpoints/(OutputCleanupTests*|BaselineComparerTests*|RunSchedulerTests*|CheckpointManifestTests*|CheckpointTaskOwnershipTests*|CheckpointToolCopyCleanupTests*|WaitCommandTests*|RunStateStoreTests*|CheckpointAppTests*|CheckpointNamespaceCensusUsageTests*|CheckpointOutputConfinementTests*|CheckpointTrackedBaselineTests*)/* executed=133 passed=133 failed=0 skipped=0 trx=/work/worktrees/task-dbb9a32f/.antiphon/checkpoints/20261013-161717-e715/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=dbe003007fdeffffaa76da9b1e80ab47b07fbd04 sourceState=clean buildSource=verified
```

--- next stage ---
next: review
handoff: Review the pushed dbb9a32f Code owner with fresh CP-1..CP-21. Five added readers are repaired; six inherited gaps and script whitespace admission are documented. Recorded ordinary results retain their actual SHAs. R-2 and PC-1..PC-8/all variants remain pending. After Review, caller lands dbb9a32f and commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md
[antiphon-report:dbb9a32f done]
