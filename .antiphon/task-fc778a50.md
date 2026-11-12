# CARD-0747 D-1 — Code round 2 (task fc778a50)

Outcome: D-1 fixed. `POST /api/agent-tasks/land-notifications/expire` with `apply: true` now needs the
`X-Antiphon-Operator-Token` header (403 `operator_token_required` otherwise, a child task token included) and
is refused 409 `land_note_expiry_disabled` while `Supervision:DeliveryVerification:LandNoteReceiptExpiryDays`
is 0. The dry run stays open. Expiry semantics are unchanged (`LandNoteExpirySweepService` and
`AgentTaskLandNotificationService` untouched).

Branch `feat/card-task-fc778a50`, worktree `/work/worktrees/task-fc778a50`, round base
`117987081300698d8522099c24cb951ccdf400ea` (tip of feat/card-task-3593eb2f, Review a207ee5c round 1).

## Commits

| SHA | What |
|---|---|
| a3190d661713d1b0d314f13d2788714774e13343 | test: red witnesses (did not compile: `AgentTaskStatus.Running`) |
| bf90a5d1a5a822e3f7de6048fb8ec07233edb2af | test: child fixture uses Dispatched — **red SHA** |
| 8a931a10705a0dc1def2414464383de8c7c3c54b | fix: operator credential on apply, off switch; docs — **fix SHA** |
| 69bdd67202199fa6e30d77aa485b803b18efb0f9 | test: compare only notes present before the request — **tested SHA** |
| (report commit) | this file only |

## Files

- `server/Api/Endpoints/AgentTaskEndpoints.cs` — route lambda: `OperatorCredential.Require` + off-switch 409 when apply; `AgentTaskService`/`ResolveCallerAsync` dropped from this route.
- `server/Application/Settings/SupervisionSettings.cs` — doc comment names the token and the 409.
- `docs/ops-http.md` row "Settle aged land notes" — names the `X-Antiphon-Operator-Token` header, 403 and 409.
- `docs/investigations/2026-11-11-card-0747-receipt-replay.md` — operator sweep line updated.
- `tests/Antiphon.Tests/Api/C0747LandNoteExpireEndpointTests.cs` (new) — two classes over the real Program
  (`AntiphonWebAppFactory` subclasses that remove `AgentTaskLandNotificationHostedService`, one with the off switch).

## Red at bf90a5d1 (pre-fix server)

6 run, 3 failed: `(no-token-apply)` 200 and settled both aged notes; `(child-token-apply)` code `caller_scope`;
off-switch `(operator-apply)` 200 and settled. Green arms: bare-dry-run, operator-apply, off-switch dry run.

## Green

- 8a931a10: 6/6 (direct leased run). 69bdd672: 6/6 (direct leased run).
- Why 69bdd672: the M1 mutant run also failed `(bare-dry-run)` for a reason unrelated to the mutant. During
  host boot a background service inserted 4 `Queued` notes for the seeded tasks, and the whole-table "writes
  nothing" snapshot counted them. The snapshot now compares every note present before the request. The sweep
  only settles existing open notes, so the guarantee is unchanged. The assertion was tightened to the right
  population, not loosened. All mutants were then re-run on 69bdd672.

## Mutants (on 69bdd672 test bytes, each one build lease + one run lease, source restored after each)

| Mutant | Change | Killed by |
|---|---|---|
| M1 | delete `OperatorCredential.Require` | `(no-token-apply)` 403→200, `(child-token-apply)` 403→200 |
| M2 | `if (false && …ExpiryDays <= 0)` | off-switch `(operator-apply)` 409→200 |
| M3 | Require moved before `if (apply)` (dry run gated) | `(bare-dry-run)` 200→403 |
| M4 | apply always throws 403 operator_token_required | `(operator-apply)` 200→403, off-switch `(operator-apply)` 409→403 |

The M3 run's first attempt timed out at my 170 s outer limit while it was still queued for a slot (position 2,
4/4 occupied), so that attempt is "not run". It was re-run `--no-build` on the same M3 build: waited=150s, 1 failed / 5 passed.

## Checkpoint receipt (.antiphon/c0747-d1-plan.md extract, run 20261112-022514-d913)

```
source: 69bdd67202199fa6e30d77aa485b803b18efb0f9 state=clean buildSource=verified
CHECKPOINT CP-1 commit=69bdd67202199fa6e30d77aa485b803b18efb0f9 build=ok filter=/*/*/(C0747LandNoteExpireEndpointTests*)|(C0747LandNoteExpireOffSwitchEndpointTests*)|(C0747LandReceiptReplayTests*)/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-fc778a50/.antiphon/checkpoints/20261112-022514-d913/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=69bdd67202199fa6e30d77aa485b803b18efb0f9 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=69bdd67202199fa6e30d77aa485b803b18efb0f9 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=77 passed=77 failed=0 skipped=0 trx=/work/worktrees/task-fc778a50/.antiphon/checkpoints/20261112-022514-d913/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=69bdd67202199fa6e30d77aa485b803b18efb0f9 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=69bdd67202199fa6e30d77aa485b803b18efb0f9 rows=2
```

The fresh CP-1 TRX names all six new cases (4 + 2 arms) plus the 16 C0747LandReceiptReplayTests results.

## Other gates

- check-evidence-diff: 117987081..69bdd672 commits=4 violations=0; merge-base(origin/master)=3db1a43b..69bdd672 commits=11 violations=0.
- `git merge-tree --write-tree origin/master HEAD` (origin/master b02f110fb67be04a4b25e3712ab469b4bdced43b): clean, tree 490855403852f599a4db8a846059227263405a14.
- tests/test-execution-policy.json unchanged, so no policyHash or PinnedInputDigest refresh.
- No migration.

## Unlisted runs (reasons)

Direct leased builds/runs outside the checkpoint tool were for red, green and the four mutants (one lease per build and per run).
The first build attempt failed to compile (CS0117, fixed in bf90a5d1).

## Owed / pending

- PCs pending for SourceLanding Mutation: M1–M4 above (method `C0747_Expire_apply_needs_the_operator_token` arms
  no-token-apply, child-token-apply, bare-dry-run, operator-apply; `C0747_Expire_apply_honours_the_off_switch` operator-apply).
- Whole Unit lane: nightly and rc qualification own it, not this Final.
- The live apply is still owed from round 1, after a server build that contains this change. It now needs the operator token.
