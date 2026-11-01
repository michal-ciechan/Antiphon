# Review 89df5cef — CARD-1609 retirement tests

Verdict: clean at `fbd95993f51ad438df98686a891248a4e48fe49f`. Stale tests, not a product regression. The temp-volume apply gate is closed at the base. The four rewritten tests still guard that gate, the Ready preview, and the current-tooling image. No numbered defect.

Subject task `5c28c416-84a0-426b-a9cb-a7650a069e75`. Diff `0096f7c5adcedc64fba0f2190d7c3f8bd09c0e74..fbd95993f51ad438df98686a891248a4e48fe49f`. Test behavior is `36c094029e59abe5f2fa9104c7097817c67936d8`; the tip commit only adds `.antiphon/task-5c28c416.md`. Scripts are unchanged: `scripts/c590-remote.sh` `6139891e6be104414932143fced197103ec2223c`, `scripts/c1008-retirement.sh` `1192163c68ec119cfd268dd84ffa8394117e9942`.

Positive controls stay pending. This diff does not change a waiting-session release. Parking stays disabled, so there is no automatic release deadline (CARD-1083). All four methods are `[NativeLinuxOnly]` and remain on `WindowsUnitSkipRoster` (lines 220, 223-225). Windows was not run.

## (a) Gate at the base

`c1609_apply_gate` (`scripts/c590-remote.sh:9159-9163`) requires `C1008_OPERATION` matching `^c1008[0-9a-f]{32}$` and `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-$C1008_OPERATION`. Otherwise `write_result false 'apply-gate-off' 2`, and `write_result` exits. The call is at 9206-9208, only when not a dry-run, before targets (9209), protected-target refusal (9214), the temp lock (9215), the journal, stop, `docker rm`, volume rm, and preview consume.

A fresh temp apply requires a Ready preview. `scripts/c590-remote.sh:9408-9409` refuses `preview-required` when `receipt.json` is missing or `c1609_require_ready_preview admit` fails, before stop. Consume is at 9483-9484, before volume rm, and sets the receipt consumed. `c1609_require_ready_preview` (`scripts/c1008-retirement.sh:512`) demands schema 2, policy 1, state Ready, consumed false, this operation id and project, the readyAt window, matching `retiredAt`, daemon id, and live fingerprints.

An older C994 receipt cannot choose the temp image or grant apply. Authority is the operation token plus this operation's Ready preview. The temp image is set only by `c1609_resolve_current_audit` (`scripts/c1008-retirement.sh:79-93`: custody of `audit-current.json`, size at most 8192, canonical JSON, schema 1, protocol 2, policyVersion 1, installed `sha256:` id). Recycle calls it at `scripts/c590-remote.sh:9253-9257`. `c994_lookup_image` (9976) is called from `case_retire_temp_containers` (10153) as best-effort diagnostics. The comment there says history cannot admit or refuse container cleanup. `retire-temp-runner` goes through `c1008_recycle` and does not call it.

The fixture sets `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-$C1008_OPERATION` unless `C1609_FIXTURE_APPLY_GATE=off`.

Mutants were applied to the real `scripts/c590-remote.sh`, which the fixture reads from the repo, then restored. Blob after both restores: `6139891e6be104414932143fced197103ec2223c`. `git diff` for scripts is empty.

- M1, the `c1609_apply_gate` call at 9207 replaced with a no-op. Four rewritten tests, TRX `/tmp/r89df-m1/r89df-m1.trx`: executed 4, passed 0, failed 4. Each failed `blocked.Exit` expected 2, actual 0, with `outcome=completed` and `removed=4`. Lease `a44fe88b-934d-4c08-9004-402590623d0a`.
- M2, the Ready-preview admit pair at 9408-9409 replaced with a no-op. Consume at 9484 stayed. TRX `/tmp/r89df-m2/r89df-m2.trx`: executed 4, passed 3, failed 1. `C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt` failed because `AssertApplyMatchesPreview` (`RollingVolumeRecycleScriptTests.cs:1581`) expected `RecycleGitAuditUnknown reason=preview-required` and the apply replayed `DIAGNOSIS=RecycleWorktreeDirty`. The other three passed. They assert gate-off, which refuses before this check. Lease `2b26903a-901b-418a-b8fe-25f384a3d24a`.

## (b) Same hazards under the new contract

`C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt` (line 826). The old apply-after-dry-run expected exit 2 `RecycleGitAuditUnknown` because a dry-run C994 receipt was not apply authority. The new test expects gate-off exit 2 `DIAGNOSIS=apply-gate-off`, no preview, no recycle, no create/stop, nothing removed, no journal (866-875), then a token apply that completes with `C1609_TOOLING` and `outcome=completed` (877-882). Dirty and unpublished previews still refuse `RecycleWorktreeDirty` / `RecycleUnpublishedWork` with no `C1008_PREVIEW` (889-934). `AssertApplyMatchesPreview` (1576-1586) then expects exit 2 `reason=preview-required`, no `outcome=completed`, nothing removed, no create/run/stop. M2 made that assertion fail. Unknown git still refuses `RecycleGitAuditUnknown` (941-952). The dry-run receipt still does not open the gate.

`C1594_Cross_sha_cleanup_image_binds_the_retired_temp` (line 988). The old test treated the cross-SHA cleanup image as the helper image, including `--user 1654:1654`. The new test requires `C1609_TOOLING` for `SessionImage` and forbids each historical image in the output and the docker trace (`AssertCurrentTooling`, 1230-1238). Gate-off refuses with no create/run/stop and nothing removed (1010-1015). Token apply completes on current tooling (1017-1021). Dirty and unpublished still refuse with no preview (1024-1070). A missing current tooling image refuses apply with `reason=current-tooling-unavailable`, no `C1609_TOOLING`, nothing removed (1110-1123). Foreign, missing, and no-record shapes preview on current tooling and remove nothing (1077-1116). They do not run gate-off.

`C1594_Older_retirement_receipt_is_ignored` (line 1134). Older, bound, partial, and dry shapes each preview Ready on current tooling, forbid the historical images, use a readonly `SessionImage` helper, and gate-off refuses with no create/run/stop and nothing removed (1174-1188). The shape that used to bind a same-retirement cross-SHA image now forbids that image.

`C1594_Disagreeing_cross_sha_images_refuse` (line 1198). Exit 0, Ready marker, neither image in the output or the docker trace, no create, nothing removed (1211-1217), then gate-off refuses (1219-1225). The old warning that both images disagreed is gone because disagreement is history. The delete is still refused without the token.

No assertion was dropped to make a red test pass. The contract change is that historical receipts are not image authority and are not apply authority.

## (c) Class run at the tip

Command bound to `fbd95993f51ad438df98686a891248a4e48fe49f` with an empty `git status --porcelain` before the slot. Reused `tests/Antiphon.Tests/bin-r89df/Antiphon.Tests.dll` (built at this SHA earlier in this review; the tree stayed clean). Lease `e4ebb2f1-edfa-4ce4-98e4-c1a4156152d1`, held 3226s. TUnit exit 2. Duration 53m 44s. TRX `/tmp/r89df-classes2/r89df-classes.trx`.

| Class | Executed | Passed | Failed |
|---|---:|---:|---:|
| RollingVolumeRecycleScriptTests | 77 | 73 | 4 |
| VolumeRetirementApplyGateTests | 12 | 12 | 0 |
| VolumeRetirementReceiptTests | 11 | 11 | 0 |
| VolumeRetirementContentTests | 15 | 15 | 0 |
| Total | 115 | 111 | 4 |

The four rewritten methods passed (durations 2m 20s, 1m 09s, 2m 36s, 18s). The four failures are `C1008_Legacy_rolling_and_jq_rosters_remain` for failing-shell, present, absent, and missing-shell, each `FAIL T-1 host order/targets` at `RollingVolumeRecycleScriptTests.cs:697`. That matches the report. The report's 105 of 109 is six below this full discovery of the same four classes at the same test commit (`36c094029` vs tip differs only by the docs report). The extra six passed. The failure set matches.

`validate-checkpoint-receipt.ps1` was not run. This brief has no checkpoint plan, and that validator requires `failed=0`. The commissioned result includes these four inherited failures, so a GREEN checkpoint receipt cannot exist. The TRX above is the receipt: dirty=0, source SHA `fbd95993f51ad438df98686a891248a4e48fe49f`, build-slot lease, nonzero counts.

## (d) Land gates

Land graph `-Check` (Release evaluator built through build-slot, lease `b380070d-a500-4545-9970-572ae26f94b7`, `UseAppHost=false`, output `bin-land-audit/Release/net10.0/`). No `-Update`. Exit 0. `approved`, count=69, digest `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`.

`check-evidence-diff.ps1` `0096f7c5adcedc64fba0f2190d7c3f8bd09c0e74..fbd95993f51ad438df98686a891248a4e48fe49f`: commits=6, entries=1, violations=0. The entry is the tracked code report.

`git fetch` on this review: `origin/master` and `origin/rc/2026-11-01` are both `e0a563b3d96ccd74b63f098f7460010fc4a99c2e`. Merge-base with the tip is still `0096f7c5adcedc64fba0f2190d7c3f8bd09c0e74`. Neither side contains the other. `git merge-tree --write-tree` exit 0 for both, tree `a8c27ba5dde39e44468ed09e7cf1555df43fd3c1`. Clean merge, not a fast-forward.

## Advisories

- The code report says every historical shape, including foreign, missing, no-record, and preferred, runs a gate-off apply. Preferred was removed. Foreign, missing, and no-record do not run gate-off. Gate-off is asserted on the clean C1008 path, the clean cross-SHA path, all four older shapes, and the disagreement case. M1 reddened all four methods, so the gate assertion is live on each.
- Cross-SHA dirty and unpublished previews refuse and do not call `AssertApplyMatchesPreview`. The token-apply `preview-required` check is in the C1008 dirty and unpublished blocks, and M2 made it fail.
- The cross-SHA apply no longer asserts `--user 1654:1654` or an exact create count. `VolumeRetirementContentTests.cs:484` still asserts `1654:1654`. `AssertReadonlyPreview` still checks the readonly helper flags.
- Class discovery here is 111 of 115, against the report's 105 of 109. Same four failure names.
- The code report records one `rm -rf` on the literal path `tests/Antiphon.Tests/bin-c5c28fix` in that worktree. Informational.

--- review evidence ---
subjectTaskId: 5c28c416-84a0-426b-a9cb-a7650a069e75
reviewedSourceSha: fbd95993f51ad438df98686a891248a4e48fe49f
reviewedSourceClean: true
ordinaryScopeCompleted: Full
