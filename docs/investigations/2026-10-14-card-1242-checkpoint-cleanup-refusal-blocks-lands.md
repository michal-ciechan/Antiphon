# CARD-1242 (4): checkpoint cleanup refusal blocks lands

Investigate task 6f2b7762, 2026-10-14, read-only on server2 (Linux). Sources read at master 15a9071fa.
Evidence: `.antiphon/checkpoints/<run>/` folders of the named tasks, `/work/review-evidence/*`, git history.

## Outcome

Confirmed. Since the CARD-1199 land (`b3cdc5db8`, committer time 2026-10-13 23:13 UTC) every
checkpoint run on this host that performs a real `dotnet build` and does not pass `--keep-outputs`
has ended `exit 2` on cleanup, whatever its rows did. The cleanup path has two gates and a real build
output can pass neither:

1. `SafeDelete.TreeReason` refuses any output tree over 10,000 entries as `unsafe-tree`
   (`tools/Antiphon.Checkpoints/Cleanup/SafeDelete.cs:38`, `:181-215`).
2. `OwnedOutputLedger.Check` refuses the first on-disk entry that has no **in-process** creation
   record as `output-ledger-mismatch` (`Cleanup/OwnedOutputLedger.cs:27-31`). The record is
   `SafeWrite.CreatedContent`, filled only by `SafeWrite.*` writes (`Cleanup/SafeWrite.cs:30-47`).
   Files written by the child `dotnet build` process are never recorded, by design (CARD-1199 plan
   invariant V-4: "External compiler payloads and separate-invocation outputs require manual removal",
   `docs/superpowers/plans/2026-10-12-card-1199-detect-report-plan.md`).

A refusal on a green run becomes `exitCode 2`, `verdict RED`, `reason "refused: ..."` and
`cleanupRefusals` in `report.json` (`CheckpointApp.cs:350-366`). The tool validator then refuses the
receipt before looking at any row (`Report/ReportValidator.cs:26-27`, `cleanup_refused`, added in the
same card, `5baee470c`). `scripts/validate-checkpoint-receipt.ps1` never reads `cleanupRefusals`, so it
prints `VALID`. Reviews follow the tool verdict, write `reviewedSourceClean: false`, settlement stores
it on the StageOutcome row, and `-Land` reads that row: `server/Application/Services/LandApproval.cs:77-79`
throws 409 `review_evidence_source_not_clean`.

It is neither host-specific nor load-dependent. The 10,000 cap is a constant; the creation record is
process-local on every OS.

## What changed this week

`git log -S` dates both gates to CARD-1199 commits (`5baee470c` adds `MaxEntries`/`unsafe-tree` and
`cleanup_refused`; `ef04d4a38` replaces the old content-based disk-ledger comparison in `Check` with
the per-entry `MatchesCreation` loop and adds `output-memory-authority-missing`). Before `ef04d4a38`,
`Check` compared the sealed `.checkpoint-output-ledger.json` inventory with the on-disk inventory,
which is process-independent and holds the build's files.

Every run on this host correlates exactly with whether its source carries `b3cdc5db8`:

| Source carries b3cdc5db8 | Example runs (UTC 2026-10-14) | Cleanup |
|---|---|---|
| no (`e07168db4`, `e38c1cb17`, `42e3e2ae8`, `ba189827b`) | CARD-1032 S8 `bin-c1032-s8-server` 00:20, 04:05, 04:32; CARD-1213 cp9/10/13 00:42, 03:06 | `cleanedOutputs true`, exit 0 |
| yes (`255f30d54` and every later source) | first at 04:05 (`20261014-035424-d688`, unsafe-tree); all later build runs | exit 2 unless `--keep-outputs` |

CARD-1199's own verification ran with `--keep-outputs` (`.antiphon/task-2cf84f6c.md`, `task-1baae4ee.md`
in task-68c0f557), so its Final never exercised the cleanup it changed.

## Per-case table

All output directories were already removed by literal path when read, so sizes come from logs and
the CARD-1238 measurements (`/work/review-evidence/08de9c8f/base-absolute-size.json`: 97,625 files,
40.1 GB, 288 test-assembly copies on the pre-fix layout; `tip-absolute-size.json`: 2,233 files after
`f5979a733`).

| Case | Run | Refusal | Path refused | Who created it | Why the ledger does not hold it |
|---|---|---|---|---|---|
| CARD-1213 S3 review 78af2eac, rows 92/78/174/1/2 green | `20261014-064832-62de` at `468411a40` | `unsafe-tree` | `tests/Antiphon.Tests/bin-c1213-cp11` (38 GB per author report) | `dotnet build` of Antiphon.Tests on the pre-CARD-1238 csproj (12 shared-OutDir helper copies, `git show 468411a40:tests/Antiphon.Tests/Antiphon.Tests.csproj`) | `TreeReason` cap hit before `Check` ran; build time 11m46s vs 4m18s for the post-fix layout |
| CARD-1213 repair a6d0f871 | `20261014-061030-a732` at `468411a40` | `unsafe-tree`; separate `wait` adds `output-memory-authority-missing` | same `bin-c1213-cp11`; `.antiphon/checkpoints/<run>/tool` | build; shadow tool copy created by the `run` starter process (`CheckpointApp.cs:554`) | `wait` is another process, so `SafeWrite.WasCreated(tool)` is false (`WaitCommand.cs:94-102`); a non-removed tool copy turns a GREEN executor into exit 2 (`WaitCommand.cs:101-102`) |
| CARD-0100 review 3501caaa, rows 3/12/19/30 green | `20261014-064352-b422` at `79fb16126` | `output-ledger-mismatch` | `tests/Antiphon.Tests/bin-c0100-s12/fakegrok.dll` | `dotnet build` (helper `fakegrok` project output, build.log) | child-process write; post-fix layout is under 10,000 entries so gate 1 passed and gate 2 failed on the first enumerated unrecorded file |
| CARD-1240 task 68c0f557 two-row plan | `20261014-051908-b07a` (`keepOutputs:false`) vs `20261014-052838-341d` (`keepOutputs:true`), same source `cc295e3f6` | `output-ledger-mismatch` vs none | `tests/Antiphon.Checkpoints.FilterHost/bin-c1240/TUnit.Core.dll` | `dotnet build` of FilterHost (NuGet copy-local) | same; the keep-outputs run exits 0 with `cleanupRefusals []`, so `ReportValidator` accepts it |
| CARD-1238 review 5b6ae153 | review scratch run | `output-ledger-mismatch` | `.../bin-review-5b6ae153/fakegrok.dll` (`review-notes.md:58`) | build | same; reviewer removed by literal path afterwards |
| CARD-1234 round 1 d147acf9 | `20261014-063839-ef6b` | build `bin-c1234-fix` `refused: tampered` then `still-present` | two hard-linked esbuild binaries | not cleanup; CARD-1241 (task 586e863b) owns it | n/a |

Only the four tool-written entries ever match a creation record: the root, `.checkpoint-output-owner.json`
(`SafeWrite.cs:239`), `checkpoint-build-source.json` (`Evidence/SourceRunGuard.cs:322`) and the sealed
ledger (`OwnedOutputLedger.cs:14`). The sealed ledger is written after the build and inventories every
build file, but `Check` only tests its existence (`OwnedOutputLedger.cs:26`); its contents are
"diagnostic only" (`SafeWrite.cs:29`).

## Root cause

The CARD-1199 operator decision made the build's own output unremovable by the tool (V-4) while the
same card made any cleanup refusal invalidate the receipt (`ReportValidator.cs:26`) and the standing
review rule requires a validated receipt for `reviewedSourceClean: true`
(`docs/orchestration-loop.md:2029-2032`, `server/Bundles/delegate-basics.md`). Those three rules cannot
all hold for a run that builds. The 10,000-entry cap is a second, independent gate that the
pre-CARD-1238 Antiphon.Tests layout always trips; branches cut before `f5979a733` still carry it.

The memory-authority refusal on the shadow tool copy is the same design applied across processes:
the starter creates the copy, the executor runs from it, and a later `wait` can never hold the record.

## Minimal safe fix (for Plan/Code; not implemented here)

Record custody for the build's output at seal time, in the executor process that created the fresh
root and ran the build, instead of reading authority back from disk:

- `Cleanup/OwnedOutputLedger.cs:8-18` `Seal(root)`: after `Inventory(root)` succeeds, and only when
  `SafeWrite.WasCreated(root)` is true, call a new `SafeWrite.AdoptCreated(fullPath)` for each
  inventoried entry, which does what `RecordContent` does (`SafeWrite.cs:32-40`: size plus SHA-256 now)
  and adds the path to `Created`. `Inventory` already throws on any link or reparse point
  (`OwnedOutputLedger.cs:70-71`) and stays inside the root by construction; `Check` already runs
  `SafeWrite.Preflight` (tracked-path refusal) on the root (`:24`).
- Call site is unchanged: `Evidence/SourceRunGuard.cs:323` seals immediately after a green build and
  before any row runs (`Execution/RunScheduler.cs:395`).

Invariants preserved: a disk ledger still authorizes nothing (authority stays the in-process record);
a file changed after the seal fails the size/hash comparison and refuses; a file or directory added
after the seal has no record and refuses; links refuse in `Inventory`; tracked paths refuse in
`Preflight`; paths outside the root cannot be inventoried; a separate invocation still cannot delete.
What changes is only *when* the record is taken: at seal, by the process that proved the root fresh.

Red-first tests (`tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs`, next to
`refuses_unsafe_build_ids_and_keeps_tracked_files` at `:95`, whose comment states the current rule):

1. A fresh owned root plus a file written by an external process (`File.WriteAllText`, or a child
   `dotnet` process) before `Seal`; `CleanOwnedOutputs` on exit 0 deletes the tree, no refusals.
2. Same, but the file is modified after `Seal`: refused, bytes retained.
3. Same, but a new file is added after `Seal`: refused, directory retained.
4. `Seal` on a root this process did not create records nothing and `Check` still returns
   `output-memory-authority-missing`.
5. `ReportValidator.Validate` on a green report with empty `cleanupRefusals` and `cleanedOutputs true`
   passes (`OutputCleanupTests.cs:327` pins the refusal case).

Residual risk to measure in Code with one real run: a test row that writes under the owned output root
after the seal still refuses (correct per V-4, but it would keep the run red). The two
`output-ledger-mismatch` cases named a build file first, so this was not observed.

The `unsafe-tree` cap needs no change once a branch carries `f5979a733`; rebasing onto master removes
the 97,625-file layout. The shadow-tool refusal is separate: `WaitCommand.cs:101-102` should not map
`output-memory-authority-missing` on a separate `wait` to exit 2 when the executor exit is 0; the copy
is disclosed and retained, not a cleanup defect.

CARD-1241 (`feat/card-task-586e863b` at `f3197abc9`) touches `BuildConfinement.cs`, `RunScheduler.cs:328-450`
(including the `binding.Write()` line), `ProcessDriver.cs`, `IDriver.cs` and adds a differently named
`BuildConfinement.Ledger`. The fix above touches none of those files, so there is no textual collision;
a fix placed in `RunScheduler` after the build would collide.

## Operational workaround today (CARD-1213 S3, owner 8c30e908)

What the gate reads: `LandApproval.LoadUsableEvidenceAsync` loads the StageOutcome row named by
`reviewEvidenceId` and refuses unless `row.ReviewedSourceClean == true`
(`server/Application/Services/LandApproval.cs:70-79`). That column is written in two ways:

- Review settlement parses the report's `reviewedSourceClean:` line
  (`server/Application/Services/ReviewEvidence.cs:78-86`) and binds it
  (`ReviewEvidenceBindingService.cs:63`).
- The orchestrator override `POST /api/agent-tasks/{reviewTaskId}/finding` with
  `stage=Review, found=false, reviewedSourceSha, reviewedSourceClean=true`
  (`StageOutcomeService.RecordFindingAsync`, `StageOutcomeService.cs:152-235`;
  `scripts/delegate.ps1 -Finding <reviewTask> -Stage Review -Clean -ReviewedSourceSha <sha>
  -ReviewedSourceClean $true`, `delegate.ps1:937-944`). The row gets Source=Orchestrator, the subject
  from the predecessor row or the review's follow-up owner (`:193-205`), and null
  CommissionedRound/OrdinaryScopeCompleted. `LandApproval.cs:84-90` accepts nulls only when the owner has
  no `RequiresFinalVerificationReview`; owner 8c30e908 reports `ownerRequiresFinalReview: false`.

`-RecoverReviewedSource` cannot help: `ReviewEvidenceBindingService.cs:83-84` refuses when the stored
report says false.

Manual cleanup by literal path then re-validating the same run does not help either: `cleanupRefusals`
is persisted in `report.json` at run end, and the tool validator reads only that.

Two honest ways to obtain clean evidence:

1. Run the checkpoint with `--keep-outputs`. Proven by run `20261014-052838-341d`: same source and rows
   as the refused run, `exitCode 0`, `cleanupRefusals []`, so both validators accept it. Archive, then
   remove the owned `bin-*` root and the shadow tool copy by literal path (the pattern in
   `/work/review-evidence/task-78af2eac/cleanup-owned.ps1`). A separate `wait` still prints
   `TOOL CLEANUP Refused ... output-memory-authority-missing` and returns 2; read `report.json`
   `exitCode` and the validator instead.
2. For the existing CARD-1213 S3 evidence (rows green, source clean, `buildSource verified`, PowerShell
   validator `VALID` at `468411a40`): the documented rule says "passes `validate-checkpoint-receipt.ps1`
   (or the tool's equivalent)" (`docs/orchestration-loop.md:2029-2032`), which the receipt satisfies.
   The orchestrator override above produces a usable row without a new review; then
   `delegate.ps1 -Land 8c30e908 -FromTask a6d0f871 -ExpectedSourceSha 468411a40feca9037f533913191a8a8a2ad8c2a1
   -ReviewEvidenceId <new row>`. I did not exercise this path; `LandApproval.cs:106-115` additionally
   requires the row's subject, ref and repository to equal the adoption source.

## Remaining uncertainties

- Whether test rows add files under the owned root after the seal (would still refuse after the fix).
- The exact entry count of `bin-c1213-cp11` was not measured (directory already removed); the 10,000
  cap is inferred from the 97,625-file CARD-1238 measurement, the 38 GB author note, and the 11m46s
  build time on the pre-fix csproj.
- The override land path was reasoned from code, not run.

## Not done, noted

- Fix idea for the shadow tool copy: let `wait` accept `output-memory-authority-missing` on a GREEN
  executor as a disclosed retention, not exit 2 (`WaitCommand.cs:101-102`).
- V-4 of the CARD-1199 plan needs an operator amendment before Code; the fix contradicts its
  "external compiler payloads require manual removal" sentence.
