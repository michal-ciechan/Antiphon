# CARD-1603 Final Review (8ef7bb9e)

**Verdict: defects found. D-1 is a product regression that can stall a provider; D-2..D-4 are witnesses that cannot go red for their own named controls. Unit lane: Full, every red explained, no regression. CP-1..CP-8: 261/261 green.**

- Subject: `feat/card-task-1d5d34a3` at `a05fa77c05e759a0024a99b411fde005aed235ce` (product verified at `63ed1c99c`; the tip only adds the author's report). Code owner: task 1d5d34a3. Base `e65343132`.
- `git merge-tree --write-tree origin/master(0dbbbe490) a05fa77c0` exits 0, tree `e02375f6d`.
- `check-evidence-diff.ps1 -BaseRef e65343132 -HeadRef a05fa77c0`: commits=11, entries=1, violations=0.
- Runner: server2 Linux. One leased build `tests/Antiphon.Tests -> bin-r8ef7bb9/` (`UseAppHost=false`, 241 s, 0 errors). Shards ran as `dotnet exec --no-build`, each through `scripts/build-slot.ps1`. No native or Windows runs.

## Defects

### D-1 (product, regression): a granted wait that finishes with a canceled or parked selection holds the provider grant forever

- **Where:**
  - `server/Application/Services/CapacityRecoveryService.cs:156-160`: the outstanding-grant branch tests `ObsoleteSelectionAttachedAsync(current)` first and sets `skipGrant = true`. It never reaches `StillHoldsGrant`/`GrantExpired` (:177).
  - `ObsoleteSelectionAttachedAsync` (`CapacityRecoveryService.ObsoleteConsumers.cs:543-562`) has no unfinished-state guard and no expiry guard.
  - The only path that would release that grant is cleanup. Its discovery reads only `Unfinished` waits (`ObsoleteConsumers.cs:376`), so it never sees a finished one.
- **Failure (interleaving):**
  1. LiveSession wait W is granted (ActionPending). The flush selects Channel X (keyed W/K1) but does not redeem it (a non-held Ui head).
  2. A successful turn ends. `ObserveTranscriptAsync` (`CapacityRecoveryService.cs:1027-1036`) sets W to Progressed and does not clear the grant.
  3. A human Drop cancels X (`CancelAsync`). X keeps W/K1. A parked X (Pending at the delivery cap) behaves the same way.
  4. From then on, every `ReconcileAsync` behaves the same way. Cleanup skips W because it is finished. `GrantReadyAsync` finds the obsolete selection still attached and skips the grant. No D-12 expiry runs, and no other waiter of that kind is admitted until a new wall wave (`BumpWaveOnAsync`) happens to clear the grant.
  - Steps 2 and 3 only need to land between two reconcile ticks, or in either order while cleanup is busy on the queue gate.
- **Evidence:** a scratch test, `R8ef7_finished_wait_with_obsolete_selection_releases_grant`, drives steps 1-4 through the real queue, `ObserveTranscriptAsync` and `CancelAsync`.
  - At the tip it is red. It prints `R8EF7_D1 window=0..2 grantedWait=W(progressed) grantedAt=<unchanged>` across three 121 s windows, and the due competitor is never granted.
  - With every CARD-1603 branch disabled (pre-fix behaviour) it is green.
- **Related gap:** for an unfinished W, the same branch also drops the D-12 bound while cleanup returns Busy or Deferred, for example while the session's queue gate is held. Plan Risk 5 says "D-12 still bounds individual stale grants". The demand-loss branch has the same shape: when `GrantedActionKey != current.ActionKey` it sets `skipGrant` and has no expiry fallback (:164-176).
- **Why it matters:** base revoked a finished wait's grant on the next tick. The tip can stop all capacity admissions for an AgentKind, fleet-wide. That is a dropped live grant.
- **Fix:**
  - Apply the obsolete-selection hold only to an unfinished grant candidate whose grant has not expired. Otherwise clear the grant with `ClearGrant` only, with no re-arm or queue re-key, so no queue row is rewritten under the provider lock.
  - Give the key-mismatch path in the demand-loss branch the same expiry fallback.
  - Add the repro (finished wait plus Drop, and a parked variant) and a gate-busy-past-expiry arm as witnesses.

### D-2 (witness cannot go red): V-2 `C1603_queue_cleanup_preserves_real_consumers` does not cover the exemptions its named control removes

- **Where:**
  - The cleanup exemptions are `ObsoleteConsumers.cs:380-387` in discovery and `:141` and `:155` in the recheck: `HasIndependentConsumer` and `HasRedeemableTaskAsync`.
  - The seeding is `SeedPreservedConsumerAsync` (`tests/Antiphon.Tests/Application/CapacityObsoleteQueueTests.cs:967-1046`).
- **Failure:** I disabled those exemptions (mutation `noexempt`). CP-1 stayed 35/35 green.
  - No V-2 arm has a Canceled or parked selected row together with a launch or dispatch receipt, a confirmed prompt, or a Queued or Working+retained task.
  - In the receipt and task arms, the selection is either absent or a Pending row below the cap, so cleanup never considers them.
- **Why it matters:** the plan's V-2 control is "remove independent-receipt exemptions; real consumer data is detached and the method fails". As written, V-2 cannot fail for that control. A retained or dispatcher wait with a dropped selection could lose its revalidation flag and action key without any test noticing.
- **Fix:** add arms where a canceled X and a parked X are selected and keyed to W alongside each receipt or task kind. Assert no detach, and that state, version, action and flag are unchanged.

### D-3 (witness cannot go red): V-10 `C1603_obsolete_cleanup_loses_compare_and_set_safely` witnesses the reread, not the CAS

- **Where:**
  - The boundary `capacity-obsolete-before-finish` sits before a fresh reread: `ObsoleteConsumers.cs:147-159` (queue) and `:324-334` (standing).
  - The CAS writes are `:174-190` and `:337-346`.
- **Failure:** results from my mutations:

  | Mutation | CP-1 | CP-2 |
  |---|---|---|
  | `nocas` (CAS removed in both paths) | 35/35 green | 18/18 green |
  | `noreread` alone | 35/35 green | 18/18 green |
  | `nocas,noreread` | 35/35 green | V-10 red, nothing else |

  - So the CAS has no witness of its own.
  - The queue path has no CAS race witness at all. `ObserveTranscriptAsync` writes the wait without the provider lock, so a Progressed write between the reread and the update is the race the queue CAS exists for.
- **Why it matters:** the plan's control for V-10 is "PC removing the state/version CAS overwrites Progressed and fails". With the CAS removed, V-10 stays green.
- **Fix:** move or add the boundary between the reread and `ExecuteUpdate`, or drop the redundant reread. Add a queue-side V-10 arm.

### D-4 (witness gap, lower severity): the legacy null-SessionId grant rule is unwitnessed

- **Where:**
  - The SQL filter admits `w.SessionId == null` (`ObsoleteConsumers.cs:504`).
  - The only resolution through the `session:<N>` consumer key is in the in-loop checks (`CapacityRecoveryService.cs:230-232`, via `ResolveQueueSessionId`).
- **Failure:** removing the in-loop checks (mutation `noloop`) leaves CP-1 35/35 green.
  - Commit `a0f1b5e6d` also converted four `CapacityRecoveryAcceptanceTests` waits from key-only to stamped `sessionId`. The plan allowed only "add a real Pending held row … retain setup", so this goes further than it.
- **Why it matters:** the plan requires "Resolve a legacy missing SessionId through the exact session consumer key". Nothing would catch an empty legacy wait cycling again.
- **Fix:** add a V-12 arm with a null-SessionId `session:<N>` wait: not granted while empty, granted with held demand. Alternatively, restore the key-only shape in those fixtures.

## Plan conformance and advisories (A-1..A-4, deviations)

- **Implemented as planned:**
  - Detach under gate, then message `FOR UPDATE NOWAIT`, then provider try-lock, with conditional writes and one transaction.
  - Parked rows stay Pending with their attempts.
  - Demand predicate:
    - It mirrors flush rules: attempts below `Math.Max(1, MaxDeliveryAttempts)`, `HoldUntil` due, not `LegacyUnclassified`, and deferred-run generation.
    - It is applied before `Take` and to the outstanding grant.
  - Demand loss sets Ready/`queue-demand-lost` and keeps key, ordinal, selection, count, flag and clock.
  - Standing supersede follows lock order session(4), agent(5), provider(7), with ownership proof, the generation table and `standing-consumer-replaced`.
- **Deviations judged:**
  - **EF try-lock:** OK.
  - **Commit-fault rollback:** OK.
  - **Witness timing:** OK.
  - **Fixture identity conversion:** see D-4.
  - **Obsolete-grant hold with no expiry:** not in the plan, and it is the D-1 cause.
- **A-1** (re-anchor citations) and **A-4** (composer guard in V-11 parked arms): done.
- **A-2:** Code moves an Admitted+`NeedsRevalidationGrant` wait whose demand is proven absent to Ready. This is safe:
  - The flag is kept.
  - `GrantReady` sets Ready→ActionPending and keeps the flag.
  - `RedeemOnAsync` with the flag does not increment `AdmissionCount` (`CapacityRecoveryService.cs:394-415`).
  - `ConfirmPromptOnAsync` ignores Ready. But a report-boundary turn still finishes the wait, because that path checks no state.
  - In production this state is reachable only through the seeded future-`HoldUntil` shape.
- **A-3:** no new arm was added. The exclusion is enforced by `WhereQueueDemandAllowsGrant` (an attached obsolete selection is excluded before `Take`), so it is safe by inspection, but there is still no arm for "Y waiting while X is attached and the gate is busy".
- **Races (constructed against the code):**
  - **A row about to be consumed:** a Pending row below the cap is not obsolete, and a flush in flight holds the gate, so cleanup gets Busy.
  - **Human Drop or `CancelAsync` at the same moment:** serialized on the same gate.
  - **`DeferResumeAbsence` re-Pending:** handled by the locked recheck plus the conditional update (the `norecheck` mutation turns resurrected, send-now and late-confirm red).
  - **New held Y while no grant is held:** stays unkeyed demand and the same wait is granted next (V-11).
  - **Flush selects Y while `GrantReady` decides demand is absent:**
    - The version CAS passes, because selection does not bump the version.
    - W becomes Ready with the same key, and the redeem is refused.
    - The next tick re-grants W with the same key, and Y is redeemed.
    - Result: a one-tick delay, and no loss.
  - **Restart mid-cleanup:** one transaction, so it rolls back.
  - **Lock order:** provider lock last everywhere, no queue gate taken while holding a DB lock, and the try-lock and NOWAIT forms cannot wait.
  - **Exceptions:** an exception other than 55P03 in the new pass aborts that tick's `GrantReady`. This is the same pattern as the base steps, and transient.
- **Exactly-once (S3-A):** `RedeemOnAsync` and the consume paths are unchanged. CP-4 24/24 and CP-5 52/52.
- **No cycling:** V-1, V-12, the `nofilter`/`noattempts`/`nohold`/`blanket` mutations and the pre-fix run all show the following:
  - An empty, parked-only or future-held wait is not granted.
  - A wait with deliverable demand is granted as before.
  - The exception is D-1.
- **Changed input waits:** none. There is no new session wait or park. With parking disabled, an input-waiting session still has no automatic release deadline (CARD-1083).
- **Flaky witness under load (advisory):** `C1603_human_start_supersedes_standing_wait(resume-stamped-generation)` failed once at the tip build under three-lane load (`replaced.State` was ActionPending: the single reconcile deferred). It then passed 5/5 alone, and passed in CP-2. A one-reconcile assertion with try-locks is load-sensitive; consider reconciling until stable within a bound.

## Mutation witnesses (scratch copy, env-gated, one build, method classes CP-1/CP-2)

- **Pre-fix** (all new branches off): CP-1 26/35 red, including V-1 4/4; CP-2 9/18 red, including V-6 4/4. This is the plan's RED.
- **Red, as the plan's controls expect** (failed count in brackets):
  - `noqueueclean` (19), `nostanding` (9), `noobsgrant` (2), `nodemandloss` (3), `demandloss-deferred` (3)
  - `nofilter` (3), `noattempts` (3), `nohold` (1), `broadstatus` (8), `noparked` (4), `norecheck` (3)
  - `notake` (1), `blanket` (29), `standingagentexists` (7), `providerfirst` (1), `blockinglock` (1), `blockingprovider` (3)
- **Green, so the witness cannot go red:**
  - `noexempt`: D-2.
  - `nocas`, `noreread`, `nocas+noreread` on the queue side: D-3.
  - `noloop`: D-4.
- PCs stay pending for method-scoped SourceLanding Mutation. These scratch runs discharge none of them.

## Performance (R1)

- CP rows: per-row test durations were 30 s to 86 s, and the eight rows summed to 6.8 min of test time on a loaded host.
- The duration tripwire flags only:
  - `CapacityRecoveryTranscriptTests.C1322_Cancelled_boundary_does_not_progress_recovery` at 12.9 s;
  - `CapacityRecoverySupervisionTests.C1593_S3_supervisor_skip_does_not_spend(compaction-block)` at 7.7 s.
  - Each was the first test in its host, so the time is the shared Postgres template startup, which is documented at about 10-11 s. It is not CARD-1603 work.
- The new classes are Integration+Slow, `[NotInParallel]` and allowlisted. The roster is untouched, which is correct.
- The author's 14m52s wall for 261 tests is consistent.

## Unit lane (Full)

- One build. 21 Unit shards plus one Matrix method row, each `dotnet exec` under `build-slot.ps1` with `timeout 45m`.
- Lanes: three at first; after lanes A and B finished, I moved shards c8, c9 and c10/c11 into new lanes D, E and F. Lane C then logged `missing-filter` for those names; this is expected, and each shard ran exactly once.
- Wall: 18:21 to 20:17 UTC. Slot waits were up to 615 s per shard, because the scratch mutation build and runs shared the host's 4 slots.
- **Completeness:** a `System.Reflection.Metadata` scan of the built dll finds 3,919 `Category("Unit")` methods (class or method level). The union of shard TRX holds 3,919 (class, method) pairs: missing 0, extra 0.
  - The class-filter shard c6 did not select the method-level `MatrixDefaultComplexityCreateTests` Unit row, which is the documented miss. Its own row ran 8/8.
- **Results:** 6,271 total: 6,224 passed, 44 NotExecuted, 3 failed.
  - The 44 are all runtime Windows-only skips: Grok argv guard 12, drive-letter MockFileSystem 5, Windows delivery profile 4, ConPTY 2, and others.
- **Reds, each compared alone at origin/master `0dbbbe490` (scratch build) and at the tip:**

  | Test | Shard | origin/master alone | Tip alone | Verdict |
  |---|---|---|---|---|
  | `C1364BoundLandWaitProofTests.C1364_Wrapper_retire_volumes_reports_waited` | c1, script exit 139 under load | 6/6 green | 6/6 green | Load flake |
  | `RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain(failing-shell)` | b1, under load | 4/4 green | 4/4 green | Load flake |
  | `RemoteScriptContractTests.C1105_Git_audit_resume_preserves_proof` (empty node fixture output) | a-rsc2 | red | red | Inherited, not CARD-1603 |

  - None of the three can be reached by this server-only change, so there is no regression.
- **Integration rows (CP-1..CP-8), fresh, same build:**

  | Row | Result |
  |---|---|
  | CP-1 | 35/35 |
  | CP-2 | 18/18 |
  | CP-3 | 47/47 |
  | CP-4 | 24/24 |
  | CP-5 | 52/52 |
  | CP-6 | 28/28 |
  | CP-7 | 28/28 |
  | CP-8 | 29/29 |
  | Total | 261/261 |

- **Receipts:** there are no sealed checkpoint-tool receipts, because the brief prescribes `dotnet exec` shards. So `reviewedSourceClean` is false.

### Raw shard summary
```
SHARD i-cp1 end rc=0 wall=43s waited=0s  total: 35  failed: 0  succeeded: 35  skipped: 0  duration: 40s 577ms 
SHARD i-cp2 end rc=0 wall=32s waited=0s  total: 18  failed: 0  succeeded: 18  skipped: 0  duration: 30s 300ms 
SHARD i-cp3 end rc=0 wall=230s waited=180s  total: 47  failed: 0  succeeded: 47  skipped: 0  duration: 46s 931ms 
SHARD i-cp4 end rc=0 wall=112s waited=75s  total: 24  failed: 0  succeeded: 24  skipped: 0  duration: 35s 537ms 
SHARD i-cp5 end rc=0 wall=230s waited=165s  total: 52  failed: 0  succeeded: 52  skipped: 0  duration: 1m 01s 826ms 
SHARD c1-C1 end rc=2 wall=957s waited=0s  total: 173  failed: 1  succeeded: 169  skipped: 3  duration: 15m 52s 975ms 
SHARD i-cp6 end rc=0 wall=665s waited=615s  total: 28  failed: 0  succeeded: 28  skipped: 0  duration: 47s 447ms 
SHARD c2-C2 end rc=0 wall=652s waited=390s  total: 201  failed: 0  succeeded: 201  skipped: 0  duration: 4m 20s 386ms 
SHARD i-cp7 end rc=0 wall=388s waited=300s  total: 28  failed: 0  succeeded: 28  skipped: 0  duration: 1m 25s 674ms 
SHARD b1-RollingVRS end rc=2 wall=1725s waited=0s  total: 77  failed: 1  succeeded: 76  skipped: 0  duration: 28m 42s 468ms 
SHARD i-cp8 end rc=0 wall=78s waited=15s  total: 29  failed: 0  succeeded: 29  skipped: 0  duration: 1m 00s 771ms 
SHARD b2-RollingOther end rc=0 wall=189s waited=30s  total: 13  failed: 0  succeeded: 13  skipped: 0  duration: 2m 35s 747ms 
SHARD a-rsc1 end rc=0 wall=413s waited=150s  total: 40  failed: 0  succeeded: 40  skipped: 0  duration: 4m 20s 893ms 
SHARD c3-AB end rc=0 wall=887s waited=60s  total: 553  failed: 0  succeeded: 550  skipped: 3  duration: 13m 43s 661ms 
SHARD b3-Retired end rc=0 wall=913s waited=30s  total: 38  failed: 0  succeeded: 38  skipped: 0  duration: 14m 40s 150ms 
SHARD a-rsc2 end rc=2 wall=881s waited=120s  total: 216  failed: 1  succeeded: 215  skipped: 0  duration: 12m 38s 563ms 
SHARD c4-D end rc=0 wall=860s waited=210s  total: 580  failed: 0  succeeded: 574  skipped: 6  duration: 10m 46s 744ms 
SHARD b4-Ch1 end rc=0 wall=1080s waited=135s  total: 255  failed: 0  succeeded: 255  skipped: 0  duration: 15m 42s 320ms 
SHARD c5-EFGIJK end rc=0 wall=784s waited=30s  total: 492  failed: 0  succeeded: 479  skipped: 13  duration: 12m 31s 371ms 
SHARD a-rsc3 end rc=0 wall=1715s waited=270s  total: 39  failed: 0  succeeded: 39  skipped: 0  duration: 24m 03s 678ms 
SHARD a-rsc4 end rc=0 wall=184s waited=30s  total: 83  failed: 0  succeeded: 83  skipped: 0  duration: 2m 31s 066ms 
SHARD b5-Ch2 end rc=0 wall=1510s waited=285s  total: 302  failed: 0  succeeded: 301  skipped: 1  duration: 20m 21s 921ms 
LANE B done 2026-10-29T19:51:39Z
SHARD a-rsc5 end rc=0 wall=465s waited=180s  total: 121  failed: 0  succeeded: 121  skipped: 0  duration: 4m 43s 015ms 
LANE A done 2026-10-29T19:51:57Z
SHARD c6-HLM end rc=0 wall=1479s waited=330s  total: 740  failed: 0  succeeded: 738  skipped: 2  duration: 19m 06s 270ms 
SHARD x-c11-W end rc=0 wall=276s waited=0s  total: 299  failed: 0  succeeded: 298  skipped: 1  duration: 4m 33s 198ms 
SHARD x-c9-S end rc=0 wall=571s waited=0s  total: 356  failed: 0  succeeded: 354  skipped: 2  duration: 9m 28s 160ms 
LANE E done 2026-10-29T20:01:49Z
SHARD x-c10-TUV end rc=0 wall=854s waited=30s  total: 423  failed: 0  succeeded: 418  skipped: 5  duration: 13m 40s 415ms 
LANE D done 2026-10-29T20:11:08Z
SHARD c7-OPQ end rc=0 wall=1074s waited=0s  total: 763  failed: 0  succeeded: 755  skipped: 8  duration: 17m 51s 566ms 
SHARD c8-R missing-filter
SHARD c9-S missing-filter
SHARD c10-TUV missing-filter
SHARD c11-W missing-filter
LANE C done 2026-10-29T20:12:56Z
SHARD x-c8-R end rc=0 wall=881s waited=120s  total: 499  failed: 0  succeeded: 499  skipped: 0  duration: 12m 37s 003ms 
LANE F done 2026-10-29T20:17:07Z
SHARD m-matrix end rc=0 wall=3s waited=0s  total: 8  failed: 0  succeeded: 8  skipped: 0  duration: 1s 247ms 
LANE G done 2026-10-29T20:17:33Z
```

### Red comparisons
```
RED c1364-master rc=0 wall=377s  total: 6  failed: 0  succeeded: 6  skipped: 0 
RED c1364-tip rc=0 wall=371s  total: 6  failed: 0  succeeded: 6  skipped: 0 
RED c1008legacy-master rc=0 wall=701s  total: 4  failed: 0  succeeded: 4  skipped: 0 
RED c1008legacy-tip rc=0 wall=624s  total: 4  failed: 0  succeeded: 4  skipped: 0 
RED v6stamped-tip-1 rc=0 wall=28s  total: 4  failed: 0  succeeded: 4  skipped: 0 
RED v6stamped-tip-2 rc=0 wall=27s  total: 4  failed: 0  succeeded: 4  skipped: 0 
RED v6stamped-tip-3 rc=0 wall=28s  total: 4  failed: 0  succeeded: 4  skipped: 0 
RED v6stamped-tip-4 rc=0 wall=24s  total: 4  failed: 0  succeeded: 4  skipped: 0 
RED v6stamped-tip-5 rc=0 wall=27s  total: 4  failed: 0  succeeded: 4  skipped: 0 
REDBATCH done 2026-10-29T19:35:02Z
RED c1105resume-master rc=2 wall=3s  total: 1  failed: 1  succeeded: 0  skipped: 0 
RED c1105resume-tip rc=2 wall=3s  total: 1  failed: 1  succeeded: 0  skipped: 0 
RED2BATCH done 2026-10-29T19:35:19Z
```

### Mutation runs
```
MUT none-Q rc=0 wall=96s  total: 35  failed: 0  succeeded: 35  skipped: 0 
MUT none-ST rc=2 wall=43s  total: 18  failed: 1  succeeded: 17  skipped: 0 
MUT ALL-Q rc=2 wall=54s  total: 35  failed: 26  succeeded: 9  skipped: 0 
MUT ALL-ST rc=2 wall=60s  total: 18  failed: 9  succeeded: 9  skipped: 0 
MUT noqueueclean-Q rc=2 wall=60s  total: 35  failed: 19  succeeded: 16  skipped: 0 
MUT nostanding-ST rc=2 wall=58s  total: 18  failed: 9  succeeded: 9  skipped: 0 
MUT noobsgrant-Q rc=2 wall=60s  total: 35  failed: 2  succeeded: 33  skipped: 0 
MUT nodemandloss-Q rc=2 wall=77s  total: 35  failed: 3  succeeded: 32  skipped: 0 
MUT demandloss-deferred-Q rc=2 wall=74s  total: 35  failed: 3  succeeded: 32  skipped: 0 
MUT nofilter-Q rc=2 wall=54s  total: 35  failed: 3  succeeded: 32  skipped: 0 
MUT noloop-Q rc=0 wall=59s  total: 35  failed: 0  succeeded: 35  skipped: 0 
MUT noattempts-Q rc=2 wall=51s  total: 35  failed: 3  succeeded: 32  skipped: 0 
MUT nohold-Q rc=2 wall=53s  total: 35  failed: 1  succeeded: 34  skipped: 0 
MUT broadstatus-Q rc=2 wall=56s  total: 35  failed: 8  succeeded: 27  skipped: 0 
MUT noparked-Q rc=2 wall=54s  total: 35  failed: 4  succeeded: 31  skipped: 0 
MUT noexempt-Q rc=0 wall=57s  total: 35  failed: 0  succeeded: 35  skipped: 0 
MUT nocas-Q rc=0 wall=58s  total: 35  failed: 0  succeeded: 35  skipped: 0 
MUT nocas-ST rc=0 wall=41s  total: 18  failed: 0  succeeded: 18  skipped: 0 
MUT noreread-Q rc=0 wall=50s  total: 35  failed: 0  succeeded: 35  skipped: 0 
MUT noreread-ST rc=0 wall=39s  total: 18  failed: 0  succeeded: 18  skipped: 0 
MUT nocas+noreread-Q rc=0 wall=78s  total: 35  failed: 0  succeeded: 35  skipped: 0 
MUT nocas+noreread-ST rc=2 wall=55s  total: 18  failed: 1  succeeded: 17  skipped: 0 
MUT norecheck-Q rc=2 wall=46s  total: 35  failed: 3  succeeded: 32  skipped: 0 
MUT notake-Q rc=2 wall=52s  total: 35  failed: 1  succeeded: 34  skipped: 0 
MUT blanket-Q rc=2 wall=39s  total: 35  failed: 29  succeeded: 6  skipped: 0 
MUT standingagentexists-ST rc=2 wall=42s  total: 18  failed: 7  succeeded: 11  skipped: 0 
MUT providerfirst-ST rc=2 wall=40s  total: 18  failed: 1  succeeded: 17  skipped: 0 
MUT blockinglock-Q rc=2 wall=87s  total: 35  failed: 1  succeeded: 34  skipped: 0 
MUT blockingprovider-ST rc=2 wall=59s  total: 18  failed: 3  succeeded: 15  skipped: 0 
BATCH done 2026-10-29T18:59:48Z
    R8EF7_D1 window=0 grantedWait=W(progressed) grantedAt=2026-10-29T18:48:01.3293900Z
    R8EF7_D1 window=1 grantedWait=W(progressed) grantedAt=2026-10-29T18:48:01.3293900Z
    R8EF7_D1 window=2 grantedWait=W(progressed) grantedAt=2026-10-29T18:48:01.3293900Z
  total: 1
  failed: 1
D-1 repro pre-fix (ALL):
  total: 1
  failed: 0
```

## Cleanup

- I removed all 33 `bin-r8ef7bb9` directories in the worktree, one literal path at a time.
- The scratch copies (env-gated mutation tree with outputs `bin-r8ef7mut/` and `bin-r8ef7d1/`, and the origin/master tree with `bin-r8ef7mst/`) stay in the session scratchpad. They are outside the repo and I did not delete them.
- Generated TRX and logs stay under the gitignored `.antiphon/rv/`.
- The subject branch was not touched.
