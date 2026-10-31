# CARD-1545 S1a fix round 1

Outcome: D-4, D-5, and D-6 are fixed on `feat/card-task-5dd200c3`. CP-25, CP-1, and CP-30 are green on `dfbd13826d8927535aa4fa7a88d0218589097c66`. The seven census mutants and the three new mutants went red, then the restored tree went green. Land-graph `-Check` approved. `git merge-tree` against `origin/master` has one add/add on the plan file.

Code tip: `dfbd13826d8927535aa4fa7a88d0218589097c66`.
Prior tip: `860bfca3e4b0d53d2b2456654c72ea3b21142445`.
Start: `9a05dbfcd4b49fdbed098a520b28c7e7d91cc46c`.

The failing witnesses and the fix were written in one edit, so there is no separate red commit. Mutants were applied on the working tree, observed red, and restored before either commit.

## D-4 producer receipt

`Write-LiveLaneExportFile` leaves `terminal` false. `Complete-LiveLaneExport` sets it true only after discovery succeeded and the roster matrix reported no failures. `Invoke-LiveLaneCensus` calls `Test-LiveLaneProducerReceipt` before the empty-seed return. A row state other than `green` refuses with `producer-row-not-green`. A missing run or report refuses with `producer-receipt-missing`. A commit or `buildSource` mismatch refuses with `producer-receipt-foreign`. The latest run directory that contains each of CP-25 and CP-1 wins. CP-30 itself is not required to be green. Group writes `checkpointResults` into the portable context before CP-25, CP-1, and CP-30 run.

## D-5 scratch

Census, gate, policy, reader, fixture, and dangling-seed scratch is created under `.antiphon/live-lane-capture/<leaf>` and removed by exact ledger path in a finally. A loose `git commit-tree` object is deleted only when the path is `objects/<sha[0:2]>/<sha[2:]>` for a SHA this process created. `phase.log` stays, because the parent reads it after the child exits.

Historical `/tmp/antiphon-live-lane-*` entries from before this round are still on the machine. This round's green ScratchResidue, failing ScratchResidue, and green FullCensus runs added none.

## D-6 roster wall

Three `ExactCoreRoster` samples of `tests/Antiphon.Tests/bin-c1545-measure/Antiphon.Tests.dll`, each under its own build slot, on this host (24 CPUs, load 17-35, slots full):

| Sample | Discovery ms | Matrix ms | Total ms | Load at start |
|---|---:|---:|---:|---|
| 1 | 71966 | 50604 | 122603 | 16.88 34.12 38.49 |
| 2 | 74696 | 84383 | 159105 | 19.22 26.97 34.23 (ended 33.44) |
| 3 | 79685 | 48275 | 127989 | 34.69 28.86 32.79 |

p95 of the three totals is the max, 159105 ms. Twice that is 318210 ms. The parent wall is 320000 ms in both `LiveLaneInventoryTests` and `LiveClaudeInventoryTests`. Discovery p95 is 79685 ms. Twice that is 159370 ms. The discovery budget is 160000 ms. The parent wall stays above the discovery budget plus the slowest matrix (84383 ms).

Discovery is a fresh `dotnet exec <dll> --list-tests` on every call. It is not shared across tests. Sample 1 (123 s) matches the review's loaded 124 s rerun. Sample 2's extra time was the in-process matrix (84 s against 51 s) while load rose. That is why a 150 s wall killed the review CP-1 after discovery and export had already finished, and why the isolated 124 s rerun passed. The Pty roster uses the same parent wall. It is the smaller discovery (five UIDs) and was not the sample that hit the old wall.

Official CP-1 on the new wall: both tests passed, tests wall 202.8 s, inside the 320 s per-child limit.

## Checkpoints

One checkpoint tool, `tools/Antiphon.Checkpoints/bin-c1545-measure/Antiphon.Checkpoints.dll`, results root `.antiphon/live-lane-runs/ffda9d857b4b4838a5529f1b0e688e07`, source `dfbd13826d8927535aa4fa7a88d0218589097c66`, `--total-timeout 90m --hard-wall-timeout 100m`. Census base `556a20e65ca59c04b205e6e8dfe2f6ae1d0e1dad`.

| Row | Result | Counts |
|---|---|---|
| CP-25 | GREEN exit 0 | executed=1 passed=1 failed=0 skipped=0; buildSource=verified; dirty=0; slot waited=15s; tests wall 38s |
| CP-1 | GREEN exit 0 | executed=2 passed=2 failed=0 skipped=0; buildSource=verified; dirty=0; slot waited=135s; tests wall 203s |
| CP-30 | GREEN exit 0 | command FullCensusUnchanged; buildSource=notApplicable; executed 106s |

Runs: `20261031-183358-f31d`, `20261031-183831-b254`, `20261031-185307-aa0d`. Unlisted script witnesses, not new manifest rows: ExportPending accepted, ProducerReceipt accepted, ScratchResidue accepted.

## Mutants

Each census mutant was one exact edit, then `FullCensusUnchanged` with `ANTIPHON_LIVE_PORTABLE_CONTEXT` pointed at the green Group context. Restored afterward. Final restored census printed `accepted` and exited 0.

| ID | Edit | Red result |
|---|---|---|
| SM-A1 | seed-not-ancestor not recorded | expected-refusal-missing seed-nonancestor |
| SM-A2 | per-project producer-provenance not recorded | expected-refusal-missing producer-provenance |
| SM-A3b | undeclared-new-uid not added | expected-refusal-missing unknown-new-uid |
| SM-A4 | removed-category category-delta not added | expected-refusal-missing remove-Integration |
| SM-A5 | non-terminal export no longer refuses | expected-refusal-missing pending-producer |
| SM-A6 | seed-range not recorded | expected-refusal-missing seed-unlisted-edit |
| SM-A7b | ExpansionDiffers does not add extra-expansion | expected-refusal-missing extra-expansion |
| D-4 early final | Write sets terminal true | ExportPending: finalized-early, finalized-on-failure |
| D-4 skip receipt | census does not call the receipt check | ProducerReceipt: census-skipped-receipt |
| D-5 cleanup removed | Clear-LiveLaneOwnedScratch returns immediately | ScratchResidue: residue-fail x3, dangling-commit-fail, residue-green x3, dangling-commit-green |

## Evidence, land graph, merge-tree

`scripts/check-evidence-diff.ps1`:

- `9a05dbfcd..dfbd13826`: commits=2 entries=0 violations=0 exit 0
- merge-base `cbe3e6b42401db766f4cee38bc9b34f5b9d314af..dfbd13826`: commits=28 entries=3 violations=0 exit 0

The mirror is not shallow (`--is-shallow-repository` is false).

`scripts/audit-land-verification-graph.ps1 -Check` with the Release DLL at `tools/Antiphon.LandGraphAudit/bin-land-audit/Release/net10.0/Antiphon.LandGraphAudit.dll`: `digest=7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99 count=68`, `approved`, exit 0. `-Update` was not run.

`git fetch origin master` left `origin/master` at `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`. `git merge-tree --write-tree origin/master HEAD` exits 1 with one add/add conflict in `docs/superpowers/plans/2026-10-31-card-1545-rc-scoped-lanes-live-claude-lane-plan.md`. Master blob `413074d8f03fa2a40c6432aa2c1d16c8881f6a65`. This branch and `4e1e6106d` both have blob `542b9380cdd724fb8811d9812b8d0a0dd7e2f6de`. No merge was made.

Advisories A-1 through A-8 were not implemented. No live Claude run. `~/.claude` was not read.

## Commits

- `860bfca3e4b0d53d2b2456654c72ea3b21142445` test(card-1545): refuse a live census whose producer row is not green
- `dfbd13826d8927535aa4fa7a88d0218589097c66` test(card-1545): set the live roster wall from the loaded-host p95

--- next stage ---
next: Final review
handoff: Unit lane on feat/card-task-5dd200c3. After review, merge chain `2c550140e`, then sanitization, then S1a. Resolve the plan add/add once with blob `542b9380cdd724fb8811d9812b8d0a0dd7e2f6de`.
artifact: .antiphon/task-5dd200c3.md
