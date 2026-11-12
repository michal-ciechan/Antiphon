# Review 3cdf5799: CARD-0747 round 2 (D-1 operator token on expire apply), Final, diff only

**Verdict: CLEAN. D-1 is closed. There are no new blockers and no regressions.** The subject is `feat/card-task-fc778a50`. I reviewed tip `12d2604be53849ab1f868fb655e21604e4123154` over the range `117987081..12d2604be`. Code owner: task fc778a50 (Grok Code author).

## Rows run at 12d2604be (server2 Linux, checkpoint run 20261112-024110-72d4, VALID)

```
source: 12d2604be53849ab1f868fb655e21604e4123154 state=clean buildSource=verified
CHECKPOINT CP-1 commit=12d2604be53849ab1f868fb655e21604e4123154 build=ok filter=/*/*/(C0747LandNoteExpireEndpointTests*)|(C0747LandNoteExpireOffSwitchEndpointTests*)|(C0747LandReceiptReplayTests*)/* executed=22 passed=22 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=12d2604be53849ab1f868fb655e21604e4123154 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=77 passed=77 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
unlisted: none
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=12d2604be53849ab1f868fb655e21604e4123154 rows=2
```

The plan extract is in `.antiphon/c0747-r2-review-plan.md` (ignored). The CP-1 TRX names all six new arms as Passed: needs_the_operator_token with bare-dry-run, no-token-apply, child-token-apply and operator-apply, and honours_the_off_switch with operator-apply and bare-dry-run. It also names the 16 replay tests. Neither the TRX nor the console mentions the operator token header or its value.

- check-evidence-diff: `117987081..12d2604be` has 5 commits and 0 violations. `3db1a43b0..12d2604be` has 12 commits and 0 violations.
- merge-tree against origin/master `b02f110fb67be04a4b25e3712ab469b4bdced43b` is clean, giving tree `4722449033bff11df1b0c22b2aff6c8e64711499`.
- I ran no other builds or tests.

## D-1 closure checked

- `AgentTaskEndpoints.cs`: for `apply` the route calls `OperatorCredential.Require`, the same comparison the other operator surfaces use, which returns 403 `operator_token_required`. That check runs first. The route then returns 409 `land_note_expiry_disabled` when `LandNoteReceiptExpiryDays <= 0`, which matches the reconcile's "zero or negative disables". Both throws come before `SweepAsync`, so a refused request writes nothing. A dry run (apply false or absent) is still open.
- A child task token no longer gets special handling. It is simply not the operator token, so it gets a 403. The `child-token-apply` arm pins this.
- `LandNoteExpirySweepService` and `AgentTaskLandNotificationService` are not in the diff. The only other source change is a doc comment in `SupervisionSettings.cs`, so the semantics confirmed in round 1 are unchanged.
- `docs/ops-http.md` names `X-Antiphon-Operator-Token`, the 403 and the 409. No script or client calls the route, so no caller is broken.
- The witnesses can go red. They assert the status, the code and a per-note state snapshot from before the request; the snapshot tightened at 69bdd672 still covers every note present before the request. The test factory's token path is in its own workspace, not production. Red at bf90a5d1 and mutants M1–M4 are as the author reported; I did not re-run them.

## Pending, not discharged by this round

- The PCs for M1–M4 (methods `C0747_Expire_apply_needs_the_operator_token` and `C0747_Expire_apply_honours_the_off_switch`) stay pending for SourceLanding Mutation.
- The whole Unit lane is owned by nightly and rc qualification.
- The live apply and its counts are still owed after the change is activated on rc: rc port, server restart, `/api/version` check, then the dry run and the apply with the operator token.
- Advisories A-1 to A-4 from round 1 stand and go to cards.
