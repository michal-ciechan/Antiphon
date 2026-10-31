# Review b5c88cd9 — Final review of CARD-1514 failed-first ordering S2 + S3 + S3b

Outcome: one defect (D-1, missing producer-to-recipient witness). The ordering itself is sound at the tip. The property probe, chain probes and all 13 checkpoint rows are green. Return to Code for D-1 before merging to rc.

Subject: origin `feat/card-task-8d80dbe9`, tip `e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3` (tested source `593f4a08f49b9ae6ed6bfd71b5ac96aa94514fe1` plus a report-only commit). Reviewed range: `3fb3c113e8b2554ccd5789e00375dbfd69697981..e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3`, 36 commits, 18 files. Standing caller authority covered this review; no other approval was needed.

## Defects

### D-1 — No producer-to-recipient witness for previous-run ordering or the regrouped-plan fallback (test coverage)

- **Where:**
  - Producer: `scripts/lib/nightly-tests-impl.ps1:1786` writes `summary.dispatchProgress`.
  - Ingress: `scripts/lib/nightly-failure-history.ps1:504` (`Initialize-NightlyDispatchHistory`).
  - Recipient: `scripts/lib/nightly-dispatch.ps1:176` passes `-PreviousRun $DispatchPreviousRun`; `scripts/lib/nightly-dispatch.ps1:93` holds the `canonicalPlanDigest` mismatch fallback.
  - Committed fixtures:
    - `scripts/test-nightly-failed-first.ps1:911`: `New-FFLoopHistory` writes a summary with no `dispatchProgress`.
    - `scripts/test-nightly-failed-first.ps1:1187`: the only `dispatchProgress` fixture carries `liveness=unknown`, so ingress refuses it.
  - `Test-FFPreviousOrder` (`:1038`) calls `Resolve-NightlyPreviousRunOrder` directly and never goes through the schedule or coordinator.
- **Failure:** two mutants run in a scratch copy of the tip. Each stays green on every committed cheap case:
  - **M13:** the coordinator drops `-PreviousRun $DispatchPreviousRun`, so operator rule (2), resume after the last pass, never reaches a real run.
  - **M9:** the `:93` plan-digest guard becomes `if($false)`, so a regrouped plan would be ordered from a stale record.
  - Cases run against each mutant: CP-6 `ActualLoopFollowsSchedule -SmallLoopProbe` 13/13, `PreviousOrder` 1/1, CP-7 `JournalCustodySurvivesInterleaving` 27/27.
  - The full CP-6 replay uses the same history shape without `dispatchProgress`, so it cannot catch them either.
- **Why it matters:** the whole S3 previous-run path (rule 2, plus the regroup and stale-plan fallback) could be disconnected or deleted with every gate green. The behaviour is correct at the tip; my probes below prove that. What is missing is the test that guards it.
- **Fix (test-only, Code):** add to CP-6, or as a new exact method with its own plan row, a two-run chain through the real public path:
  1. Run 1 has no history and one failing chunk, using `Invoke-FFActualLoop ... -Variant failure`.
  2. Export run 1's real summary with `Export-NightlyPriorFailures`.
  3. Run 2 uses that history. Assert `orderingReason=previous-run-order`, the failed chunk first, passed chunks last, and every chunk launched exactly once.
  4. Add a regrouped run 2: change one suite's chunk membership. Assert `previous-run-plan-mismatch`, the canonical order and every current chunk launched once.

  Measured cost in the small fixture: about 35 s per chain. It must go red under M13 and M9.

## Central-danger probes (tip `e805b784`)

1. **Property probe.** It runs the real `Resolve-NightlyPreviousRunOrder` on random chunk lists of 1–4 suites with 1–8 chunks each, plus random previous records.
   - Each record holds a random subset of rows in random order: pass, FAIL, TIMEOUT, unstarted, pass with `coverageComplete=false`, or missing.
   - Each record has random `priorFailure` flags.
   - Injected faults: candidate mismatch, stale, `liveness=unknown`, an unknown chunk (removed or regrouped), a duplicate row, a malformed result, a bad date.
   - **Result: 3 seeds x 200 = 600 cases, 0 failures.**
     - Every output is a permutation that keeps object identity: same count, no duplicates, same set, and the very same objects.
     - Every fallback returns the exact canonical order with a stated reason.
     - Every ordered case (93/77/81) equals an independent oracle.
     - The first block is exactly the failed set (failed rows plus history `priorFailure`), unfinished rows next, passed rows last in canonical order.
     - Each lane resumes after its last passed chunk and wraps earlier non-passed chunks back in, so a chunk that did not pass is never skipped.
   - Script: scratch `prop.ps1`, not committed.
   - A row with a nonzero exit code and an unrecognised result classifies as failed. That is conservative and correct.
2. **Chain probe (real producer to recipient).**
   - Run 1, lazy: `antiphon/Priority` FAIL; `dispatchProgress` written; digest `65013ad3…`.
   - Run 2 with the exported history: launches `antiphon/Priority, antiphon/Ordinary, session-runner/Ordinary, session-runner/Priority`, `orderingReason=previous-run-order`, same digest, green.
3. **Regroup probe.** Merging two chunks' membership between runs gives:
   - `orderingReason=previous-run-plan-mismatch` (digests `65013ad3` vs `9d1b12ca`);
   - all 5 current chunks launched once, `testsPassed` and `coverageComplete` both true.
4. **Unknown liveness (S3b) — confirmed safe.**
   - At ingress, `Initialize-NightlyDispatchHistory` clears both the history and the previous record (`history-liveness-unknown`).
   - `Resolve-NightlyPreviousRunOrder` throws `previous-run-liveness-unknown` before it classifies anything and returns the canonical order with no lane cursors.
   - The old record is byte-identical afterwards (witnessed by `heartbeatLiveness`).
   - Nothing concludes the run died, and no chunk is marked unfinished on that basis.
   - The heartbeat library returns null for an unproven journal, which selects the existing 20-minute default-unknown idle budget. It is detection only and never stops a process. Nothing releases a waiting session, and there is no automatic deadline (CARD-1083 unaffected).
5. **Other cases.**
   - A missing or absent record keeps the S2 stable partition.
   - Different candidate, stale, malformed, duplicate rows, a removed chunk or an unknown state each fall back to the original order. None refuses.
   - Added suites or chunks are classed unfinished.
   - An empty previous `suites` array puts everything unfinished, giving the canonical order with prior failures first.

## Mutants (scratch copy of the tip, worktree untouched; files restored and hashes re-checked equal to tip)

| Mutant | Witness | Result |
|---|---|---|
| M1 drop failed-first partition (unfinished before failed) | PreviousOrder | killed (`progressOrder`) |
| M2 duplicate the failed block | PreviousOrder | killed |
| M3 swap pass/unfinished partitions | PreviousOrder | killed |
| M4 restore the old answer (missing journal generation → proceed → 1800 s) | HeartbeatBindings | killed (`heartbeatLiveness`) |
| M6 drop the unfinished partition | PreviousOrder | killed |
| M7 no lane rotation | PreviousOrder | killed |
| M8 ignore unknown liveness in Resolve | HeartbeatBindings | killed (`heartbeatLiveness`) |
| M10 duplicate a chunk in the schedule | ActualLoop small | killed (`dispatch-not-permutation`, harness exit 1) |
| M11 drop a chunk from the schedule | ActualLoop small | killed (`dispatch-not-permutation`) |
| M12 schedule without the failed-first partition | ActualLoop small | killed (`actualTrace`) |
| M5 remove Resolve's own permutation guard | PreviousOrder | survives — equivalent: the partition is a permutation by construction, and M2/M6 show the guards still catch it |
| M14 remove `Invoke-NightlyPreparedSchedule` set-equality guard | ActualLoop small | survives — defence in depth behind the `New-NightlyDispatchSchedule` guard (M10/M11) |
| M9 remove plan-digest fallback | ActualLoop small, PreviousOrder, CP-7 | **survives → D-1** |
| M13 coordinator ignores the previous record | ActualLoop small, PreviousOrder, CP-7 | **survives → D-1** (killed by my chain probe) |

On the lazy path (no history) a schedule-construction error is deliberately diagnostic-only. Chunks still execute from suite state, so the HeartbeatBindings controls do not see M10/M11. The barrier path does.

## Execution integration

The chain is `scripts/nightly-run.ps1` → `Invoke-AntiphonNightlyRun`/`Core`. When `-PriorFailuresPath` is given, Core copies and verifies the history under `StateRoot/history/<RunId>`, outside the checkout, before sync. It forwards the path and digest across the self-reexec and to `scripts/nightly-tests.ps1`. The receiver `Invoke-AntiphonNightlyTests` re-verifies with `Initialize-NightlyDispatchHistory` (`-CandidateSha`), then `Invoke-NightlyDispatchCoordinator` → `New-NightlyDispatchSchedule` → `Invoke-NightlyPreparedSchedule`, run serially.

- **Opt-in.** Without the flag the order is unchanged and lazy; the only new outputs are diagnostic `dispatch-schedule.json` and `summary.dispatchProgress`. CP-8 shows ledger and receipt bytes unchanged.
- **Fallback only.** Ingress and Resolve are both try/catch fallbacks with a stated reason. A bad, missing, tampered, stale or other-candidate record never refuses a run. The only throws are the internal permutation guards, which correct code never trips.
- **Future runs only.** The history must be a closed run of the same candidate SHA within 7 days. It affects only the next new complete attempt and never credits or skips a chunk.
- **No live state touched.** Every diff path is in-repo. No change touches `C:\Antiphon`, the live run or a private supervisor; the `C:\Antiphon` strings are unchanged pre-existing defaults. There is no supervisor in this repository.
- **History source.** It is the CARD-1514 `prior-failures.json` (`Export-NightlyPriorFailures` from S1, already on rc) plus the run's own `summary.json`.

## Pure function and PowerShell 5.1

- `Resolve-NightlyPreviousRunOrder` (`scripts/lib/nightly-dispatch.ps1:3`) does no I/O.
  - Time comes from the caller (`$NowUtc`), and it does not mutate its inputs (the test asserts record bytes are unchanged).
  - Ties keep canonical order within each partition and lane, so the result is deterministic.
- 5.1 scan of every added line in `scripts/lib/*.ps1`, `nightly-run.ps1` and `nightly-tests.ps1`: no `??`, `?.`, ternary, `&&`/`||` pipeline chains, `-Parallel`, `-AsHashtable`, `GetRelativePath`, or 3-argument `Join-Path`; all ASCII. `::new()` and `-cin` are 5.0-safe. **No 5.1 risks found.**

## Final sweep (checkpoint tool, one isolated build, TEMP=/tmp, UseAppHost=false)

Run `20261031-084428-83ed`, manifest `.antiphon/rv-b5c8/plan.md`. It holds the plan's CP-3..CP-8 exact filters plus RV rows.

- Verdict: GREEN, exit 0, 13 rows green, 0 red, 0 skipped.
- `unlisted: none`; builds: 1; `outputs: deleted bin-rvb5c8/`.
- `CHECKPOINT SOURCE VALID source=e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3 rows=13`.

| Row | Filter | Executed | Passed | Failed | Skipped | Test body |
|---|---|---:|---:|---:|---:|---:|
| CP-3 | RcFailedFirstPlannerTests/GlobalReplayPriority | 1 | 1 | 0 | 0 | 26 s |
| CP-4 | RcFailedFirstPlannerTests/FullBytesAndCoverageConserved | 1 | 1 | 0 | 0 | 682 s |
| CP-5 | RcFullWatchdogTests/CapFitsAdmittedPlannedCost | 1 | 1 | 0 | 0 | 5 s |
| CP-6 | RcFailedFirstExecutionTests/ActualLoopFollowsSchedule | 1 | 1 | 0 | 0 | 562 s |
| CP-7 | RcFailedFirstExecutionTests/JournalCustodySurvivesInterleaving | 1 | 1 | 0 | 0 | 126 s |
| CP-8 | RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged | 1 | 1 | 0 | 0 | 78 s |
| RV-1 | RcFailureHistoryTests/* | 2 | 2 | 0 | 0 | 114 s |
| RV-2 | RcFullWatchdogHarnessTests/* | 2 | 2 | 0 | 0 | 20 s |
| RV-3 | C1514LandingSplitBudgetTests/* (C1404, C1577, C1584, C1410, G2865) | 5 | 5 | 0 | 0 | 56 s |
| RV-4 | RcRosterRepairTests/* | 3 | 3 | 0 | 0 | 14 s |
| RV-5 | ScriptHarnessProcessContractTests/* | 30 | 30 | 0 | 0 | 0.2 s |
| RV-6 | Antiphon.Tests ProcessSpawnLimitTests/* | 21 | 21 | 0 | 0 | 3 s |
| RV-7 | SlowTestTripwireTests, TestLaneCategoryGuardTests, TestClassificationPolicyTests | 42 | 42 | 0 | 0 | 84 s |

- Build: 345 s, slot waited 0 s.
- Wall: 40m42s, of which 165 s was admission and slot waiting. The host load average was about 30 from other tenants.
- Evidence (ignored): `.antiphon/checkpoints/20261031-084428-83ed/report.md`.

Other checks:

- **Land graph:** `scripts/audit-land-verification-graph.ps1 -Check` with an isolated Release build of LandGraphAudit (through the slot) printed `digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69 phase=pass2 approved`, with the same 5 known not-built projects. That matches Code. No `-Update` was run, and the canary was removed.
- **Evidence diff:** `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 3fb3c113e -HeadRef e805b784` printed `commits=36 entries=4 violations=0`.
- **Merge-tree onto origin/rc/2026-10-27 `53835c8c2033618a76e6cb512f386e31c11e4ed7`:** clean, tree `c72247c8395c1a7730f7bae81f889de6ee1df238`. The base `3fb3c113e` is on rc.
- **Merge-tree onto origin/master `96715b56623d57a16c6788838bd18281a40e768d`:** 48 conflicts (plan add/add, `nightly-failure-history`, `nightly-tests-impl`, heartbeat, `test-phase-evidence`, harness scripts …). This rc-based branch is not master-landable; see A-1.
- **Native:** Windows CP-15/16 remain owed. S4a/S4b/S5 rows CP-9..CP-14 are not implemented yet and are out of scope.
- **Mutation:** every PC stays pending for method-scoped SourceLanding Mutation. My mutants are review probes, not PC credit.

## Advisories (non-blocking)

- **A-1 (master port):** master needs a separate port of S1–S3b. Landing on rc alone is clean.
- **A-2:** `scripts/lib/nightly-dispatch.ps1:93` skips the plan guard when a record has no `canonicalPlanDigest`. The roster check still stops drops and duplicates, and the producer always writes the field. Treating an absent digest as malformed would be the stricter, fail-safe reading.
- **A-3 (perf):** both rows are n=1 on a host at load average about 30. Both are inside their bounds; no budget change was requested.
  - CP-4: 682 s test body against a 9-minute estimate, 63% of the 1080 s owned bound.
  - CP-6: 562 s against an 8.0-minute measurement by Code (2.3 minutes faster) and a 12-minute estimate.
- **A-4:** in the hermetic chain, exporting a real run's summary warned `UNRESOLVED PRIOR FAILURE … historical-membership-unavailable (short-trx)` for every chunk. Ordering still came from `dispatchProgress`. This is S1 export behaviour on seam TRX, not changed here. The D-1 witness should check that history members resolve from a real run's TRX.
- **A-5:** M5 and M14 are equivalent or defence-in-depth guards; note this for the Mutation pack so they are not counted as survivors.

## Review evidence

```
--- review evidence ---
subjectTaskId: 8d80dbe9-bcb4-46a5-a996-56027ab83ecb
reviewedSourceSha: e805b7848ae1cc1fe00c9d6f7ccb6e21b0ababc3
reviewedSourceClean: true
ordinaryScopeCompleted: Full
```

**"Full" covers** the commissioned final sweep: the plan's closed ordinary rows CP-3..CP-8 and every class the brief named. It does **not** cover the whole Antiphon.Tests Unit lane (about 70 minutes of shards), because the plan's closed selection rule says not to run the rc roster. Windows CP-15/16 remain owed.
