# CARD-1379 Code evidence

Implemented all four behaviors; all four commissioned narrow witnesses have a committed
red and a subsequent green. Final CP-2 is 3/3 green on
`e41346058bbedd6720556527fda0c4c9b1855eaa`; replay CP-8 is 1/1 green on its
earlier implementation slice. Ready for ordinary Review under the explicit trim.

Original Code task / landing owner: `e1e0eae5-b846-4761-8e10-1c7c5756bddb`.
Branch: `feat/card-task-e1e0eae5`.
Worktree: `/work/worktrees/task-e1e0eae5` (the assigned desktop checkout was not accessed).
Base: `8c70d23802bf989e38ed353adb5116b35ed5158a`.
Latest implementation: `7d1e1bc481c732e31bebc47147c381e4a2bcaad3`.
Latest tested source including the corrected fixture: `e41346058bbedd6720556527fda0c4c9b1855eaa`.
Plan: `docs/superpowers/plans/2026-10-20-card-1379-land-lease-replay-plan.md`.
The single supplied plan/design commit `291efd9729fb3a225385394c34dffaf765c1f3bd`
was cherry-picked first; its incoming plan was retained in the modify/delete conflict.

## Implementation and safety boundary

- S1: a flushed, atomically published verification marker is a fence, never authority by itself.
  The concrete lease provider admits only typed full-SHA fetches and disjoint, task-owned
  ordinary worktree creation through its own live capability, correlated to L, process/start,
  window nonce, request, attempt, operation and candidate. Untagged, unknown, foreign-provider,
  malformed, dead/unjoined child evidence does not acquire the compatibility exception.
  All admitted preflight operations still acquire the real repository mutation handle R.
- S2: immutable Prepared tickets separate leased preparation from verification. L remains held
  throughout verification, publication and cleanup, protecting the shared land checkout and
  artifacts. R covers source resolution, preparation/rebase/pins, then is released together with
  the Git read-cache scope. Slot acquisition, compiler/test children and their drain occur
  outside R. The window closes after the joined verifier and build-slot release; the service
  reacquires a genuine R, starts a fresh read scope, reloads task/request/operation, checks
  writers, and validates identity, candidate, source registrations, tracked cleanliness,
  protected land lock, index locks, sequencers, approval, filter, destination and pins.
  A changed candidate is refused rather than reset underneath an old passing result.
- R remains held for ordinary worktree provisioning through the committed claim, then is
  released before remote preparation/session handoff. Shared, repair/adoption, SourceLanding,
  Interim and parked paths retain ordinary admission. Existing prepared-mirror bypasses remain.
- A second land cannot reset the checkout during verification because L is still owned.
  Ordinary writers are fenced while the marker is active. Any mutation between window close
  and reacquisition is subject to fresh validation. R covers subsequent publication/cleanup;
  the existing remote target-baseline comparison, non-forced push, remote re-observation and
  confirmation remain authoritative. A moved target follows the existing bounded reprepare
  path and obtains a new verification result. This change adds no force push.
- S3: repository release and window opening signal a coalesced dispatcher wakeup. The serial
  hosted loop and safe boundaries service at most eight ready IDs through normal admission,
  capacity/reservation/claim checks. Ephemeral priority expires; the existing land yield
  budget and zero-disabled setting remain. Start-ref fetch retains its total deadline.
- S4: the single land reader reconciles the complete durable pending set before startup and
  every selection, ordered by original RequestedAt then ordinal lowercase request ID.
  Wakeups are hints; failed scans close selection. Legacy normalization, request eligibility,
  max attempts and recovery remain. Held requests are attempted once per sweep generation;
  stale entry completion cannot release a replacement claim.

CARD-1380 `LandExecutionLease.cs` and `LandVerificationArtifacts.cs` are unchanged.
The non-code skip decision, ForceVerificationBuild and audited build-graph fingerprint
are preserved. No migration, client change, live deployment or runner restart was made.
Restart requirement: **server**, after ordinary Review/landing and separately authorized
activation; performed: **none**. Target is master only. Post-activation batch timing,
loaded-version verification and authorized restart measurement remain with the caller.

## Commissioned verification scope and limitations

The brief explicitly overrides the generic Final profile: one committed red witness before
the fix and one narrow green per behavior; no Unit lane or full-class runs. CP-3/CP-5 merge
into CP-1, and CP-4/CP-6 into CP-2. CP-7/CP-8 select only Q1. All filters have trailing
wildcards and all checkpoint invocations use `--row-timeout 40m`, committed HEAD as expected
source, and the checkpoint tool's own host build-slot gate. No platform/runner pin was used.
GET `/api/runner-defaults` and `/api/session-runners` were read before execution.

The sole unlisted build was the necessary checkpoint-tool bootstrap through
`scripts/build-slot.ps1`, output `bin-c1379-tool/`: slot=granted, waited=45s,
build about 8s, zero errors. There were no other unlisted test drivers, full assemblies,
load repetitions, deliberate mutants or timeout/assertion relaxations.

The real-child helper replaces only compiler/test argv with an owned PowerShell process.
It traverses the production observer/journal/process exit and stream-drain path. Its
controlled verifier success is fixture input; the test oracles are actual lock acquisition,
Git fetch/worktree state and separately committed database claims/publication receipts.

Coverage is deliberately narrower than the complete design:

- W1 proves typed admission during a live verifier, ordinary-tag and foreign-provider
  refusal, second-L exclusion and post-join ordinary admission.
- L1 proves actual origin-only fetch and committed worktree/session claim before verifier
  release, then durable publication confirmation. It does not qualify all slot-wait,
  slow-remote, changed-ticket, crash or target-race variants.
- H1 proves the production targeted recipient performs an actual queued claim after the
  owned lease releases. It calls that recipient directly; it does not prove every hosted
  idle/busy boundary or record a competing later land. Those remain deferred.
- Q1 runs the actual restarted hosted reader and observes durable attempt and publication
  order for an older Held request versus a newly accepted request. Equal-time, crash,
  lost-wakeup and all legacy variants are not claimed.

Deferred, not passed: W2-W4, L2-L6, H2-H4, Q2-Q4 and the unexercised arms of W1/L1/H1/Q1;
the full named integration classes, Unit lane and adjacent sweep/queue suites. Two existing
sweep expectations were updated for durable snapshot authority/barrier semantics but were
not executed under the trim. Native N1-N3 were written, compiled, Windows-gated and not run
(CP-9 belongs to the separate Windows qualification). No native result is inferred from Linux.

## Historical outcomes

- CP-7 at `115cada284d2b3a845a24efeb8e94b8290f21bb4`: 1 executed, 1 expected failure
  at the replay-order assertion; newer request overtook the older durable request.
  Wall 6m03s, build 267.22s, startup 64.94s, test 9.49s.
- CP-8 at `9847576ff5b501d515940c49b602d8982949f5c2`: 1/1 passed, no skips.
  Wall 9m18s, build 493.25s, startup 42.37s, test 7.02s. Its receipt was independently
  validated. This is the replay slice's actual tested SHA, not the final tip.
- CP-1 at `2e60158488c7202309a916d49bf532992c883357`: 3 executed, 3 expected
  assertion failures: compatible admission null, R still held during verification,
  and ready task still Queued. No compile/fixture/timeout failure counted as a red witness.
  Wall 4m25s, build 192.35s, startup 43.77s, tests 12.26s.
- First CP-2 at `ddb74def03d48ba383379a472de6381fab00e929`: 3 executed,
  W1/H1 passed and L1 failed its final publication assertion after overlap succeeded.
  Resume wrongly required an unlocked land registration. The persistent land checkout
  is deliberately locked against pruning; the fix requires that lock and exact
  `antiphon-land` reason. Wall 9m55s; build 479.52s, startup 70.87s, tests 19.21s.
- CP-2 at `44b0ebc7f1c346febde8af893b6cdfda1105eb0b`: build failed CS1061 because
  the `LandWorkspace` property hid the class constant. Zero tests executed; not a red
  witness or pass. The next commit qualifies the type. Wall 1m29s. Row slot=skipped,
  waited=0s; the build itself held its granted slot, waited=0s.

No inherited-red claim is made for these newly introduced defects. Both repairs were
committed/pushed after their actual failures. CP-2 is rerun as the same closed manifest row;
the already passing two methods repeat only because that combined row remained red.

## Reproduction

After a gated bootstrap of `tools/Antiphon.Checkpoints`, use its built DLL:

```text
run --plan docs/superpowers/plans/2026-10-20-card-1379-land-lease-replay-plan.md --rows CP-2 --expected-source-sha <committed-HEAD> --row-timeout 40m --keep-outputs --max-wait 45s
wait --run <run-id> --max-wait 45s
```

Wait until terminal; inspect fresh TRX for all intended exact methods and nonzero counts.
Owned leftover output was removed before each repair retry. No receipt refusal occurred.
Generated checkpoint/TRX/JSON/log payloads remain ignored; only this individual Markdown
report is committed. Essential unedited receipts and the per-PC pending inventory follow.

## Pending Mutation inventory

All listed variants are unbound and pending; Mutation is paused. No PC was run or discharged by ordinary green. The complete payload recipes and detecting method names remain in the plan. No bound pack was created under docs/superpowers/mutations/.

| PC | Variants (each pending) | Status |
|---|---|---|
| PC-1 | second-land | Pending, unbound |
| PC-2 | lock-order | Pending, unbound |
| PC-3 | marker-only | Pending, unbound |
| PC-4 | common | Pending, unbound |
| PC-5 | process-start | Pending, unbound |
| PC-6 | nonce | Pending, unbound |
| PC-7 | request | Pending, unbound |
| PC-8 | attempt | Pending, unbound |
| PC-9 | operation | Pending, unbound |
| PC-10 | candidate | Pending, unbound |
| PC-11 | slot-wait/between-children | Pending, unbound |
| PC-12 | marker-write-fault | Pending, unbound |
| PC-13 | malformed | Pending, unbound |
| PC-14 | unknown-owner | Pending, unbound |
| PC-15 | dead-owner-no-child | Pending, unbound |
| PC-16 | replacement-generation | Pending, unbound |
| PC-17 | close/cancel | Pending, unbound |
| PC-18 | Shared/repair/adoption/SourceLanding/Interim/park-repair/settlement/cleanup/canonical-reset | Pending, unbound |
| PC-19 | source-path | Pending, unbound |
| PC-20 | land-path | Pending, unbound |
| PC-21 | admin-alias | Pending, unbound |
| PC-22 | protected-ref | Pending, unbound |
| PC-23 | unrelated/wrong-binding | Pending, unbound |
| PC-24 | dead/reused/unknown | Pending, unbound |
| PC-25 | malformed | Pending, unbound |
| PC-26 | start-intent | Pending, unbound |
| PC-27 | completed | Pending, unbound |
| PC-28 | stdout-only | Pending, unbound |
| PC-29 | stderr-only | Pending, unbound |
| PC-30 | claim-commit | Pending, unbound |
| PC-31 | slot/live-child | Pending, unbound |
| PC-32 | remote-handoff | Pending, unbound |
| PC-33 | slot-order | Pending, unbound |
| PC-34 | fresh-lease | Pending, unbound |
| PC-35 | row-lock | Pending, unbound |
| PC-36 | registration | Pending, unbound |
| PC-37 | request | Pending, unbound |
| PC-38 | attempt | Pending, unbound |
| PC-39 | active-operation | Pending, unbound |
| PC-40 | eligibility | Pending, unbound |
| PC-41 | approval | Pending, unbound |
| PC-42 | filter | Pending, unbound |
| PC-43 | destination | Pending, unbound |
| PC-44 | source-ref | Pending, unbound |
| PC-45 | source-SHA | Pending, unbound |
| PC-46 | source-pin/target-pin/prepared-pin | Pending, unbound |
| PC-47 | land-registration | Pending, unbound |
| PC-48 | admin | Pending, unbound |
| PC-49 | HEAD | Pending, unbound |
| PC-50 | tracked-dirt | Pending, unbound |
| PC-51 | index | Pending, unbound |
| PC-52 | sequencer | Pending, unbound |
| PC-53 | source-writer/repository-writer | Pending, unbound |
| PC-54 | remote-source | Pending, unbound |
| PC-55 | target-moved | Pending, unbound |
| PC-56 | budget-exhausted | Pending, unbound |
| PC-57 | build-fail/test-fail | Pending, unbound |
| PC-58 | cancel | Pending, unbound |
| PC-59 | cancel-child | Pending, unbound |
| PC-60 | restart-after-join | Pending, unbound |
| PC-61 | dead-owner-live-child | Pending, unbound |
| PC-62 | cap | Pending, unbound |
| PC-63 | eligibility | Pending, unbound |
| PC-64 | reservation | Pending, unbound |
| PC-65 | competing-claim | Pending, unbound |
| PC-66 | prepared-mirror/prepared-rearm | Pending, unbound |
| PC-67 | oldest-ready | Pending, unbound |
| PC-68 | busy-tick | Pending, unbound |
| PC-69 | idle | Pending, unbound |
| PC-70 | busy-boundary | Pending, unbound |
| PC-71 | expired | Pending, unbound |
| PC-72 | canceled | Pending, unbound |
| PC-73 | ineligible | Pending, unbound |
| PC-74 | same-attempt | Pending, unbound |
| PC-75 | interrupted-attempt | Pending, unbound |
| PC-76 | zero-disabled | Pending, unbound |
| PC-77 | start-ref-deadline | Pending, unbound |
| PC-78 | nine-waiters | Pending, unbound |
| PC-79 | lost-signal/restart | Pending, unbound |
| PC-80 | partial | Pending, unbound |
| PC-81 | failure | Pending, unbound |
| PC-82 | reverse-insert | Pending, unbound |
| PC-83 | tied-ID | Pending, unbound |
| PC-84 | legacy | Pending, unbound |
| PC-85 | dropped-wake/older-commit/cleanup-retry | Pending, unbound |
| PC-86 | superseded | Pending, unbound |
| PC-87 | terminal | Pending, unbound |
| PC-88 | NeedsResolution | Pending, unbound |
| PC-89 | ineligible | Pending, unbound |
| PC-90 | reviewed-recovery | Pending, unbound |
| PC-91 | max-attempt | Pending, unbound |
| PC-92 | duplicate | Pending, unbound |
| PC-93 | stale-completion | Pending, unbound |
| PC-94 | held-head | Pending, unbound |
| PC-95 | reeligible | Pending, unbound |
| PC-96 | snapshot | Pending, unbound |
| PC-97 | commit-after-selection | Pending, unbound |
| PC-98 | native-release | Pending, unbound |
| PC-99 | native-exclusion | Pending, unbound |
| PC-100 | active-preflight-close | Pending, unbound |
| PC-101 | cancel-reacquire | Pending, unbound |
| PC-102 | reacquire-polls | Pending, unbound |
| PC-103 | busy-reader | Pending, unbound |
| PC-104 | request-commit/cleanup-commit | Pending, unbound |
| PC-105 | dequeue-crash | Pending, unbound |
| PC-106 | attempt-commit-crash | Pending, unbound |

## Unedited checkpoint receipts

    CHECKPOINT CP-7 commit=115cada284d2b3a845a24efeb8e94b8290f21bb4 build=ok filter=/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-e1e0eae5/.antiphon/checkpoints/20261020-181129-b8f2/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=115cada284d2b3a845a24efeb8e94b8290f21bb4 sourceState=clean buildSource=verified

    CHECKPOINT CP-8 commit=9847576ff5b501d515940c49b602d8982949f5c2 build=ok filter=/*/*/LandRestartReplayTests/C1379_OldHeldReplaysBeforeConcurrentNewRequest* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-e1e0eae5/.antiphon/checkpoints/20261020-182027-f29a/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=9847576ff5b501d515940c49b602d8982949f5c2 sourceState=clean buildSource=verified

    CHECKPOINT CP-1 commit=2e60158488c7202309a916d49bf532992c883357 build=ok filter=/*/*/(LandVerificationWindowTests*)|(LandVerificationLeaseWindowTests*)|(LandDispatchHandoffTests*)/C1379_* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-e1e0eae5/.antiphon/checkpoints/20261020-183352-4cad/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=2e60158488c7202309a916d49bf532992c883357 sourceState=clean buildSource=verified

    CHECKPOINT CP-2 commit=ddb74def03d48ba383379a472de6381fab00e929 build=ok filter=/*/*/(LandVerificationWindowTests*)|(LandVerificationLeaseWindowTests*)|(LandDispatchHandoffTests*)/C1379_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-e1e0eae5/.antiphon/checkpoints/20261020-185315-5729/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=ddb74def03d48ba383379a472de6381fab00e929 sourceState=clean buildSource=verified

    CHECKPOINT CP-2 commit=44b0ebc7f1c346febde8af893b6cdfda1105eb0b build=failed filter=/*/*/(LandVerificationWindowTests*)|(LandVerificationLeaseWindowTests*)|(LandDispatchHandoffTests*)/C1379_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=44b0ebc7f1c346febde8af893b6cdfda1105eb0b sourceState=clean buildSource=unknown



CHECKPOINT CP-2 commit=7d1e1bc481c732e31bebc47147c381e4a2bcaad3 build=ok filter=/*/*/(LandVerificationWindowTests*)|(LandVerificationLeaseWindowTests*)|(LandDispatchHandoffTests*)/C1379_* executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-e1e0eae5/.antiphon/checkpoints/20261020-190804-bbd5/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7d1e1bc481c732e31bebc47147c381e4a2bcaad3 sourceState=clean buildSource=verified

Repair evidence at 7d1e1bc481c732e31bebc47147c381e4a2bcaad3: CP-2 again executed 3, passed 2, failed 1, skipped 0. L1 reached publication validation but correctly refused verification_failed: the controlled helper emitted run.trx while the unchanged S1 verifier requires landing-verification.trx. The helper filename was corrected in e41346058bbedd6720556527fda0c4c9b1855eaa; no production verifier, timeout or assertion was relaxed. This changes the proof fixture rather than repeating an unchanged proof. Wall 4m33s, build 197.29s, startup 43.44s, tests 16.46s.

    CHECKPOINT CP-2 commit=e41346058bbedd6720556527fda0c4c9b1855eaa build=ok filter=/*/*/(LandVerificationWindowTests*)|(LandVerificationLeaseWindowTests*)|(LandDispatchHandoffTests*)/C1379_* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-e1e0eae5/.antiphon/checkpoints/20261020-191336-fc72/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e41346058bbedd6720556527fda0c4c9b1855eaa sourceState=clean buildSource=verified


## Final ordinary results

| ID | Actual outcome |
|---|---|
| V-1 | W1 passed in final CP-2; expected red in CP-1. |
| R-1 | W1 trimmed regression witness passed; remaining arms deferred. |
| V-2 | L1 passed in final CP-2, including resumed publication; expected red in CP-1. |
| R-2 | L1 trimmed regression witness passed; remaining arms deferred. |
| V-3 | H1 passed in final CP-2; expected red in CP-1. |
| R-3 | H1 targeted-recipient witness passed; hosted/bounded-turn variants deferred. |
| V-4 | Q1 passed in CP-8; expected red in CP-7. |
| R-4 | Q1 trimmed regression witness passed; remaining replay variants deferred. |

Final CP-2 wall 4m37s, build 196.90s, startup 46.68s, tests 17.42s; slot=granted waited=0s. Fresh TRX individually inspected: all three exact intended classes/methods, executed=3 passed=3 failed=0 skipped=0, no timeout/error/aborted rows. Offline receipt validation returned CHECKPOINT SOURCE VALID source=e41346058bbedd6720556527fda0c4c9b1855eaa rows=1. CP-8 source validation previously returned valid for 9847576ff5b501d515940c49b602d8982949f5c2. No green run was repeated after the combined row became green.

Fetched origin/master=d1cffdb2f88e1b72e55ad0044331b9f6e105e087; merge-tree at tested source succeeded without conflicts, tree=e018aea4801928b4eb3b1325e4dda2067b8ef529. Full task evidence-history guard is run again after this report-only commit; generated payloads remain ignored.

## Pushed implementation commits

e0d4a4bd3e401c0c795f88057107871358584e70 docs(CARD-1379): bind lease and replay verification witnesses
115cada284d2b3a845a24efeb8e94b8290f21bb4 test(CARD-1379): witness durable land replay overtaking
9847576ff5b501d515940c49b602d8982949f5c2 fix(CARD-1379): reconcile durable land order before reader selection
2e60158488c7202309a916d49bf532992c883357 test(CARD-1379): add real-child lease and committed-dispatch witnesses
ddb74def03d48ba383379a472de6381fab00e929 feat: release repository lease for correlated landing verification
44b0ebc7f1c346febde8af893b6cdfda1105eb0b fix: preserve protected land worktree identity on verification resume
7d1e1bc481c732e31bebc47147c381e4a2bcaad3 fix: qualify land worktree lock reason constant
e41346058bbedd6720556527fda0c4c9b1855eaa test: emit the verifier's required fresh report filename

All task-owned bin-c1379-* outputs were removed after terminal checkpoint completion. No child/test run remains active. Next: Review; original Code task remains the landing owner; PCs stay pending for separately commissioned SourceLanding Mutation.
