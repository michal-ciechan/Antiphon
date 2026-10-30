# CARD-1199 S-A Linux lane

Outcome: green. First slice only. Native Windows was not run. S-B1 was not started.

## Slice

S-A is first: twelve gate-only results, CP-1..CP-12, plus support row CP-36. These are runtime gate lifts, not policy exclusion removals. `tests/test-execution-policy.json` has no S/T/O exclusion and was not edited. No project, package, build target, or definition file changed.

Portable identities (RequireLinux lifted; bodies otherwise unchanged):

- S: `shape_staged_untracked`, `shape_clean_build`, `missing_receipt_is_not_negative_success`, `shape_existing_stamp`, `shape_compiler_collision`, `shape_existing_output_directory`, `shape_existing_output_symlink`
- T: `intermediate_output_path_tampering_is_reported`, `generated_target_file_tampering_is_reported`, `dirty_tracked_tree_is_refused_and_unmodified`, `tracked_snapshot_contains_committed_files`, `index_added_file_is_kept_and_indexed`

The sixteen `shape_conflict_rule` arguments are now sixteen named methods delegating to `AssertShapeAsync`. Ten other S methods, eight other T methods, and five O methods still call `RequireLinux`. The Windows confinement sibling stays Windows-only and was added to `linuxSkips`. Census windows skips are the six unrelated patterns plus those 23 methods (29). Linux skips are the previous six plus the sibling (7).

Collision shapes write `c1199-mutation-reached.txt` before the mutation Exec. Judge returns `mutation-marker-present` if that file exists after a named refusal. CP-4..CP-7 passed, so the marker stayed absent on this Linux preflight.

## Prerequisites

Base `130ea525aa1f25230123ee3584ea58dbf0604b95` is the rc top-up 38 tip and contains `LinuxOnlyWindowsExclusionPolicy` (six harness assertions). `scripts/lib/nightly-watchdog-admission.ps1` is absent. That blocks S-F-cost only, not S-A.

## Commits

- `09ddde919a0710b5e8ed3468a5d6cf4258c22917` docs: reviewed plan
- `3ef7737ea2cf035f195e6ac88abc2d69f8b10f8e` red witness. Compiled TUnit `C1199_WindowsRequiredRoster` failed: `c1199-roster-35` count=35 unique=20 `shape_conflict_rule`=16, and `c1199-residual-skips-exact` windows=9 linux=6. The other four C1323 rows passed.
- `1deb00ebd3bb34d3890d2e8b9080aab87c64aa1c` fix. This is the SHA the checkpoint receipts bind.

## Linux checkpoint

Run `20261030-074742-f60a`. Command, not wrapped in an outer build slot:

`dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-36 --serial`

Source clean, buildSource=verified, verdict GREEN exit=0. Wall 13m56s. Budget wall=836.317s execution=776.276s admissionOnly=60.041s. Shared build about 287s. CP-36 slot wait 60s, then green. Zero failed, zero skipped.

| CP | executed | passed | failed | skipped |
|---|---:|---:|---:|---:|
| CP-1 | 1 | 1 | 0 | 0 |
| CP-2 | 1 | 1 | 0 | 0 |
| CP-3 | 1 | 1 | 0 | 0 |
| CP-4 | 1 | 1 | 0 | 0 |
| CP-5 | 1 | 1 | 0 | 0 |
| CP-6 | 1 | 1 | 0 | 0 |
| CP-7 | 1 | 1 | 0 | 0 |
| CP-8 | 1 | 1 | 0 | 0 |
| CP-9 | 1 | 1 | 0 | 0 |
| CP-10 | 1 | 1 | 0 | 0 |
| CP-11 | 1 | 1 | 0 | 0 |
| CP-12 | 1 | 1 | 0 | 0 |
| CP-36 | 3 | 3 | 0 | 0 |

Unedited receipt lines are in `.antiphon/checkpoints/20261030-074742-f60a/report.md` on this worktree (gitignored). Manifest overlay changed only timeout fields and the phase-timing environment. Unedited import: `.antiphon/c1199-checkpoints.imported.yaml`. `timeouts.rowMinutes` 20. `build.slotWaitMinutes` 10. No blanket `--row-timeout`.

## Land graph

No definition file changed. `tests/test-execution-policy.json` did not change. `-Update` was not run.

`dotnet build tools/Antiphon.LandGraphAudit -c Release --property:OutputPath=bin-c1199auditcheck/ --property:UseAppHost=false -maxcpucount:1 -nodeReuse:false` through build-slot lease `cfcff19c-ef0b-464f-aa6f-dfe5d4482156`, then:

`pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin-c1199auditcheck/Antiphon.LandGraphAudit.dll`

Exit 0. `digest=7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99 count=68`. One `approved` line.

`scripts/check-evidence-diff.ps1` over base..HEAD before this report: commits=3 entries=0 violations=0 exit 0.

## Owed native Windows Verify

Same rows at evidence SHA `1deb00ebd3bb34d3890d2e8b9080aab87c64aa1c`. Dispatch `-Platform Windows`. Do not use a later report-only commit as the receipt source. After bootstrap, import, and the same deadline overlay:

`dotnet tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll run .antiphon/c1199-checkpoints.yaml --rows CP-1,CP-2,CP-3,CP-4,CP-5,CP-6,CP-7,CP-8,CP-9,CP-10,CP-11,CP-12,CP-36 --serial`

One-row form, after:

`$c1199Tool = Join-Path (Get-Location).Path 'tools/Antiphon.Checkpoints/bin-c1199-plan-tool/Antiphon.Checkpoints.dll'`

`$c1199Manifest = Join-Path (Get-Location).Path '.antiphon/c1199-checkpoints.yaml'`

`dotnet $c1199Tool run $c1199Manifest --rows CP-n --serial`

## Not done here

S-B1 and later slices. R-2 junction and custody qualification stays open. CARD-1613 master port and S3 admission stay later prerequisites.

--- next stage ---
next: review
handoff: S-A Linux CP-1..CP-12 and CP-36 are green at 1deb00ebd3bb34d3890d2e8b9080aab87c64aa1c. Native Windows Verify of those rows is owed. S-B1 was not started.
artifact: .antiphon/task-dd567e5f.md
