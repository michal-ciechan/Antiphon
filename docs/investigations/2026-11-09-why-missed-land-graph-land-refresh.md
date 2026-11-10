# Why missed: land-graph refresh commit-failed

Debug task 04a52369. Qualification run dce78ee0, chunk `antiphon-all-group-87.trx`, rc/2026-11-14 = `7bee43614495293afbae1b0f4add019d11dacb9c`. Windows read-out: `docs/investigations/2026-11-09-run-231f573c-land-graph-land-refresh-windows-failure.md` on `origin/feat/card-task-d80b30f8` at `070b4dfee57b9be87482762039cc8ab3cb67121c`. That filename says run 231f573c; the body is run dce78ee0 (checkout `C:\Antiphon\releases\checkout-42be26dd`, DESKTOP-KTLKPIF).

## Failure

Chunk of 391 tests, 388 passed, 3 failed. All three are `LandGraphLandRefreshTests`. No timeout, and no git stderr was stored. The script replaces a failed `git commit` with one notice line (`scripts/land-graph-land-refresh.ps1` commit of the manifest, evaluation and pins; non-zero becomes `action=refuse reason=commit-failed`).

| Test | Line | Duration | Message |
|---|---:|---:|---|
| `failed_card_note_does_not_block_the_refresh` | 285 | 4.860s | `result.Code` should be 0 but was 1 |
| `check_still_failing_after_refresh_fails_the_land` | 232 | 4.292s | `result.Text` should contain `action=refuse reason=check-after-refresh` but was `LAND-GRAPH-NOTICE action=refuse reason=commit-failed` |
| `new_project_refreshes_and_reports_the_new_count` | 93 | 3.978s | `result.Code` should be 0 but was 1 |

The first and third assert `result.Code` before `result.Text`, so their notice text was not stored. They are the same refresh-and-commit path as the second. In the same minute the same stub committed successfully for `changed_csproj_refreshes_commits_only_land_graph_files_and_notifies` (5.155s) and `changed_pin_on_a_batch_file_is_rewritten_and_committed` (5.320s). Both real-audit methods passed. The three failures ran one at a time after the rest of the chunk had drained.

## Classification

Windows-only intermittent test-fixture defect. Not a product defect, and not a change between the two rc tips.

The audit stub at `7bee43614` copies the prepared manifest and evaluation with `Copy-Item -Force`. Copy-Item keeps the source file's modification time. The prepared records are the same size as the committed ones (64-hex digest, one-digit count, fixed evaluation shape). Git then trusts size plus timestamp and `git commit -- <those paths>` fails with nothing to commit. The refresh script reports that as `reason=commit-failed`. `changed_pin` also rewrites the pins file with `WriteAllText`, so that commit still has a fresh timestamp and passed. A same-size Copy-Item commit is intermittent: `changed_csproj` passed beside the three failures.

`ed162195eb07f0be8ae79868a9119266467ea6f4` (2026-11-09 14:03Z, after the 11:22Z rc cut) replaces those two `Copy-Item` calls with `File.WriteAllBytes`, which stamps a fresh time, as the real `-Update` does. That commit is on master `da4f168de5a771adf5dab22336826d19b4ec613b` and is not an ancestor of `7bee43614`.

These later master commits are not the difference for these three tests. They do not change the stub:

- `bc96c71bef39761b4dbc5c05e1030312396a0e16` ports the emulation pre-check to C#. The stub tests pass `-PinScript` and never run that pre-check. Both real-audit methods passed on this Windows chunk.
- `65d94c686bbd3347623e42f6e8138288e7e33150` builds LandGraphAudit once (batch 144). The stub does not build that tool.
- `0cb7bfa7fcbd98e1fb537aa85dbf669395fe51a7` refreshes the committed land-graph digest. The stub uses its own fixture repo.
- `965ab080395310c957b04e3ea9088c5f04534670` rebuilds a stale audit DLL. The stub path returns at `-PinScript` before `Resolve-LandAuditTool`.

The class file and `scripts/land-graph-land-refresh.ps1` are the same blob at rc/2026-11-13 `653f1145de5b72c2617a018cd606e0e8ba3cfe6e` and at `7bee43614` (class `4434b857da3c7151117c6ec4e2f919c092527f2f`, script `2bef47c79f90a3dd8a176a7410425c8bd8dbafac`). Run 231f573c's dispatched failures were groups 2, 4, 71, 96 and 117. This class was not among them. The sibling extract read the dce78ee0 TRX, not the 231f573c TRX, so there is no per-method row for 231f573c here. The same bytes did not produce a dispatched failure on that run, which fits an intermittent miss.

Linux cannot show it. Git on this host stores nanosecond timestamps, so a newer Copy-Item is a visible change. At `7bee43614`: the class was 15 passed, 0 failed; the three methods were 20/20 alone (60 passed) and 5/5 while four `sha256sum /dev/zero` processes ran (15 passed). At master `da4f168de` (with `ed162195e`): the class was 15 passed, 0 failed. No assertion was loosened. No second fix was added. The repair is already on master.

## Introducing commit/card

introducing commit/card: `e2358f2ddc375bca3bb6a0e883895e31be9c7b8d` (CARD-1753)

That commit adds the stub, including `Copy-Item` of `C1753_NEW_MANIFEST` and `C1753_NEW_EVAL`. `26ddc026df82e094db176788fcb75ab5f8d898ba` later fixed the read-only dispose and the autocrlf fixture and is on both rc tips. It left `Copy-Item` in place, so the next Windows run could see `reason=commit-failed` instead of the dispose exception that had replaced the assertion.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Review `af70c827-2d79-49b5-8767-cdd1708fbd3a`, scope Full (recorded `ordinaryScopeCompleted=Full`, round Final, outcome Clean), subject `a4d84751-bbd6-4f8c-91da-2ddb8c55d7c7`, reviewed source `6d6700b5d33101edfd7e500b012cca67618abb51`, runner server2, platform linux. Checkpoint run 20261108-042252-c6c6: CP-1 `LandGraphLandRefreshTests` plus four instruction classes (116 passed, 0 failed). Recorded in `docs/investigations/2026-11-08-why-missed-land-graph-land-refresh.md`.

`ed162195e` is an operator commit on master after the cut. It names no card and no review selection. The rc was already cut from `7bee43614`.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was not outside the class filter. The class is Unit, path `tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs`, and it was inside CP-1. That selection ran on Linux and passed. Linux `Copy-Item` plus nanosecond git timestamps does not miss a same-size rewrite. The failing lane is native Windows.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

The CARD-1801 row is already present (`c1f28848f159fdb152656ed72e069691625265c6`, on this rc cut). This note does not edit `tests/impacted-classes.json`.

| path | class filter | windows |
|---|---|---|
| `tests/Antiphon.Tests/Application/LandGraphLandRefreshTests.cs` | `/*/*/LandGraphLandRefreshTests/*` | required |
| `scripts/land-graph-land-refresh.ps1` | `/*/*/LandGraphLandRefreshTests/*` | required |

A Linux run of that filter is green at the failing tip (15/15, and the three methods 20 times alone and 5 times loaded). It cannot see this cause. The Windows lane of the same filter is the check. One Windows pass is not proof: `changed_csproj` passed in the failing chunk.

## Exemption

same cause as an already written note: none. The dispose and autocrlf failures are a different cause, written in `docs/investigations/2026-11-08-why-missed-land-graph-land-refresh.md`.
