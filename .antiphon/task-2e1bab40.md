# CARD-1661 (task 2e1bab40): C1364 Host_recycle temp V-5/V-6 vectors: Debug report

Branch `feat/card-task-2e1bab40`, base `afa651cf274b889b8daee7a9c8f9e7c0e1b58eac` (CARD-1659 tip), Linux (server2). The change is test-only: `tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs`. No product file changed. The product scripts were tagged temporarily for diagnosis and restored byte-identical (sha256 5ccacd03...).

## Commits
- `8193aba6d98c0f5f2bbbd9326a667ac616f44df7` fix: the observer resets once per run; the temp resume seed is an admitted apply
- `0d5000fa41315c08b6af576105d7119119cbb23b` fix: temp V-6/V-7 follow CARD-1609 single inventory and exact per-volume deletion
- `14db0bea85db881ed3b803d0fb1f5410e7fe2734` fix: the observer attributes sleeps; the land wait stays exactly one poll

## Root causes and decisions
1. **Fixture: proof counter reset (V-6/temp, phase `removing`).** `C1008HostFixture.Run` injects its prelude with `source.Replace("trap 'ec=$?;", ...)`, which replaces EVERY occurrence. The script now has four such sites: top level plus 5787, 8704 `c1008_preview_restore_traps` and 8848 (the preview helper run). The temp content audit (`c1008_audit_readonly`) therefore re-ran the `ObserveHost` prelude mid-run, and that prelude truncated `proofs.jsonl`, `land-reads.jsonl` and `sleeps`. The land injected for "proof 3" actually landed at proof 5: phase `removing`, one volume removed. Traced: observed-run proofs went 1, 2, then 1 again at containersRemoved. **Fix:** the observer resets those files once per run (`C1364_OBSERVER_ARMED`). Its function overrides are still re-applied.
2. **Fixture: stale V-5/temp/resume-removing seed (RecycleResumeMismatch).** The refusal is `c1609_prepare_resume || c1008_refuse RecycleResumeMismatch` (c590-remote.sh:9264). The seed exited 77 on the journal's `removing` save, which happens before `c1609_require_ready_preview` consumes the Ready receipt (c590-remote.sh:9480). That left `state=Ready, consumed=false`, and CARD-1609's temp resume contract refuses that state: it requires a consumed receipt in Applying, PartialRefused, Refused or Completed. The real supported interruption is after admission, which is what the J8 VolumeRetirementReceiptTests cuts use. **Fix:** the temp seed exits at the first `c1609_target_intent`, after admission and before any volume effect. The seed now asserts journal `removing`, receipt `Applying`, `consumed` true, 0 intents and 0 volumes removed. The vector still asserts the same refusal (RecycleLandInFlight at proof 2, waited=1polls) and no destructive continuation.
3. **Stale expectations: CARD-1609 product changes, temp only.** Once (1) was fixed, V-6/V-7 temp reached assertions that described the pre-CARD-1609 temp path:
   - The temp audit is the read-only, self-removing `docker run` preview-audit helper (c1008_audit temp branch), not create/start. With no temp container removed, the post-removal scan is omitted on purpose: 88ab37c77, J4 single inventory. The vector now requires **exactly one** `run` audit labelled `io.antiphon.preview-audit=<op>:`, carrying `--rm` and `--read-only`, with 0 create/start. It counts only the observed run's trace, because the Ready preview shares docker-trace.jsonl.
   - Temp deletion is exact per-volume `docker volume rm` in C1008_TARGETS order (runner-state, dind-data, runner-tmp, work) since 8536d74cf, not `compose down -v`. V-7/temp now expects `targets.Take(proof-4)` removed and 0 `down`. That is the same journal-progress rule the main vectors use.
   - With the reset gone, the `sleep` stub also sees the audit cidfile poll (`c1008_audit_readonly`, 0.2). Sleeps are now recorded as `caller args`. Apart from that poll, exactly `["c1008_tasks_wait 1"]` is required. This is stricter than the old `["1"]`, because the caller must be the land wait.
   The main vectors are unchanged: two attached audits, both helpers removed, main container rm, main targets. The no-sudo guard in RollingProductionMountTests is unchanged.
4. **Product:** not wrong; no product change. Observation only, not filed: a temp apply refused between the journal's `removing` save and the receipt consume leaves a journal that resume refuses (RecycleResumeMismatch) and that a fresh apply also refuses (journal exists). It is fail-closed by design. Operator recovery is the CARD-1609 close path.
5. **Fixture smell (no card filed):** `C1008HostFixture.Run` replacing all `trap 'ec=$?;` sites re-runs the whole prelude inside functions, and this affects every C1008HostFixture user. I scoped the fix to C1364 because the shared fixture also serves RollingVolumeRecycleScriptTests (26+ min, not run).

## Runs (native output, UseAppHost=false, one build-slot lease each)
- RED at tip afa651cf2: C1364 class 4/6 passed, 2 failed. V-5/temp/resume-removing `boundaries [1] expected [1,2]` with DIAGNOSIS=RecycleResumeMismatch. V-6/temp phase `removing`, expected `containersRemoved`.
- At 8193aba6: admission green; post_removal red at `auditRuns 0 expected 2` (V-6/temp, stale expectation).
- At 0d5000fa: post_removal red at the sleeps assertion (`0.2` x25 + `1`).
- **GREEN at 14db0bea: C1364BoundLandWaitProofTests 6/6 passed, 0 skipped, 94 vector lines passed.** Durations: post_removal 5m10s, admission 4m26s.
- **Neighbours at 14db0bea: RollingProductionMountTests + HostJq\* 53/53 passed (12 + 41).**
- Witness M1 (script-only, restored): forcing the post-removal audit for an empty temp project (`if true ||`) turns V-6/temp red with `scans.Length 2 expected 1`.
- Unlisted runs: three diagnostic runs with a temporarily instrumented c590-remote.sh (refusal, proof and trace logging to scratchpad), and one run cut at my 590 s outer timeout (5/6 passed; the admission test was still running; no orphans). RollingVolumeRecycleScriptTests was not run, per the brief.

## Gates
- check-evidence-diff afa651cf2..14db0bea: commits=3 entries=0 violations=0.
- Land graph -Check (native Release evaluator via -ToolPath): approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE. -Update was not run.
- merge-tree origin/master 18bc8a191: the whole branch conflicts in the C1364 file, only because CARD-1659 landed rebased (6d50f55b9 and siblings; identical patch-id af77fa0b). The range afa651cf2..HEAD applies clean (`--merge-base afa651cf2`), so the landing rebase drops the carried CARD-1659 commits.
- merge-tree origin/rc/2026-11-01 10a826b9d: the whole branch is clean. rc has CARD-1609 (91f3e7966, 88ab37c77) but not CARD-1659, so an rc port needs bc2e4dca8 (C1364 part of CARD-1659) before these three commits.

## Pending for Mutation (PCs)
- PC-1: product, restore the post-removal audit for an empty temp project -> V-6/temp `scans.Length` red (shown).
- PC-2: test, revert the observer guard (unconditional truncation) -> V-6/temp phase `removing` red (shown at tip).
- PC-3: test, revert the temp seed to the c1008_save exit-77 hook -> V-5/temp/resume-removing RecycleResumeMismatch red (shown at tip).
- PC-4: product, reverse the temp C1008_TARGETS deletion order -> V-7/temp Removed red.
- PC-5: product, add a second land-wait sleep -> sleeps assertion red.
