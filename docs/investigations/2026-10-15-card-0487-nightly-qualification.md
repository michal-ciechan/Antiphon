# CARD-0487 / CARD-0545 portable qualification preflight

Status: **UNQUALIFIED; live acceptance pending.** This record belongs to CARD-1281
D-5, original Code/landing owner `18880da1-ceca-4132-b085-75d512bed1b8`.
It is not a trusted readiness receipt and must not be used to enable Interim.

The authoritative scope is the [CARD-1281 plan](../superpowers/plans/2026-10-15-card-1281-pipeline-speed-plan.md),
including its verification design and binding follow-up. Source base:
`179044a46d1f20625a91a91af84615b45f81289e`; branch `feat/card-task-18880da1`;
worktree `/work/worktrees/task-18880da1`. Only these qualification Markdown records
change. Existing production code, scripts and tests are read-only inputs.

The portable guard selection is CP-7..CP-11 (`--after S5`) in the
[closed manifest](../superpowers/plans/2026-10-15-card-1281-cheap-slices-checkpoints.md).
CP-11 runs `NightlyWatchdogCoreTests.C545_IntentBeforeNetwork`,
`C545_ReceiptIdempotent` and `C545_ReaderHeldWithoutSession` for R-4. These tests use
real SQLite with substitute network/reader services; they establish protocol logic,
not live recipient delivery or scheduler availability. Existing D checks may already
pass, as the plan explicitly permits; this task introduces no tests or production fix
and runs no deliberate mutants.

## Execution record

The caller refinement arrived after the D-5 rows were green and validated. It
directed stopping Unit, recording Windows skips and the `ops-http.md` failure as
deferred disclosures, then committing and reporting. Unit had already reached its
unchanged 21-minute timeout; the remaining supplementary run was stopped and its
executor/children were confirmed absent before cleanup. No further tests ran.

CP-7..CP-11 passed at committed source
`19b25d22171fc2f1126a1e661fefab3678d34173`: **8 executed, 8 passed, 0 failed,
0 skipped**. Fresh TRX contained all eight intended class/method identities.
V-19 passed (2 executions); V-20 passed (1 execution, 24 harness assertions);
V-21 passed (2 executions); R-4 passed (those five plus 3 watchdog executions).
No tests were added; the plan's existing-D exception applies, with sensitivity
still pending for Mutation. No red-first production mutation was performed.

Run `20261015-110741-beae` ended exit 0. The validator returned
`CHECKPOINT SOURCE VALID source=19b25d22171fc2f1126a1e661fefab3678d34173 rows=5`
for `report.json` with explicit CP-7,CP-8,CP-9,CP-10,CP-11. Every row has
`dirty=0 sourceState=clean buildSource=verified slot=granted waited=0s`.
There was one selected test build, 455.0874368 seconds; the tool's summary
`builds: 5` includes four unused manifest builds, not five executed builds.
Reported run wall time was 10m01s. No reruns or loaded repetitions occurred.

Generated receipts/TRX/logs remain ignored under
`.antiphon/checkpoints/20261015-110741-beae/`; the retained report is `report.md`
and its structured counterpart is `report.json`. The stored task report is
`.antiphon/task-18880da1.md`. This results-only documentation commit follows the
tested commit; none of its later bytes is relabelled as tested source.

The supplementary run `20261015-111927-a6e5` is **incomplete**, not green:
CP-14 timed out with no TRX, CP-15 was stopped without TRX, and CP-16 never ran.
No reliable executed/pass/fail/skip totals exist for those rows. Its console
reported 35 skip events and one failing assertion, described in the
[deferred disclosures](2026-10-15-card-0544-verification-pilot.md#deferred-final-disclosures).
Its build, CP-14 and CP-15 each obtained `slot=granted waited=0s`; CP-16 obtained
no slot. No base comparison ran, so inheritance of the failure is **unresolved**.

Unlisted work is explicit: the required checkpoint-tool bootstrap build used
`bin-c1281-d-tool/` under `build-slot.ps1` (granted, waited 0s); the Final
supplement used a second isolated test build for the reason below. No direct
ungated driver, assembly sweep, live qualification or Mutation was run. The
preflight tool removed its test output; the stopped supplement's output and the
bootstrap output were removed after confirming owned processes had ended.

Manual inspection: all nine original local links resolve; only the three assigned
qualification records changed. Qualification and trusted-receipt status remain
pending. All CARD-1281 PCs (PC-1..PC-64) remain pending; D-5 specifically owns
PC-41..PC-47 and their variants. Other slices' V/R/Q obligations are not claimed.

Placement was read through `GET /api/runner-defaults` (revision 2) and
`GET /api/session-runners` on 2026-10-15. Both Windows and Linux were eligible;
one other inventory entry was unavailable. Portable runs pin neither host nor OS.
This observation is not qualification evidence.

The standing Final-stage contract requires Unit and full named integration classes
in addition to the narrower D manifest. Those supplementary runs are declared in
the [Final supplement](2026-10-15-card-0544-verification-pilot.md#final-verification-supplement).
No full assembly or native suite is selected. There is no unbounded source impact:
the change consists only of qualification records. The extra checks retain the
existing readiness, baseline, landing and watchdog invariants.

## Live gates still pending

CARD-1281 Q-1 remains pending: accepted CARD-0545 S6/F-1..F-6, manual full unattended
master green followed by actual scheduled full unattended master green, all seven
suite counts and script/policy/source/job/native identities. Failure and recovery
must be observed in the authorized recipient view, joined by notification id and
whole-body hash, including delayed/available reader, pre-send restart, lost send
acknowledgement and reader outage. Sender acknowledgement is insufficient.

The operator owns deployment, credentials, reader login and live notices under
[nightly-watchdog.md](../nightly-watchdog.md#custody). No such action or live receipt
is claimed here. Commit an accepted qualification artifact before referencing its
SHA in a trusted receipt; committing this pending record confers no readiness.
Budget remains 300-600 active/run minutes plus two overnight boundaries.

Qualification order remains 0545/0487 -> [0599](2026-10-15-card-0599-release-gate-qualification.md)
-> [0544](2026-10-15-card-0544-verification-pilot.md). Existing comprehensive owner-plan
positive controls remain pending under their original IDs. Restart: none.

## Unedited checkpoint receipts

```
CHECKPOINT CP-7 commit=19b25d22171fc2f1126a1e661fefab3678d34173 build=ok filter=/*/*/ReleaseGateActivationTests/(C599_Admission*)|(C599_QueuedReadiness*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-18880da1/.antiphon/checkpoints/20261015-110741-beae/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=19b25d22171fc2f1126a1e661fefab3678d34173 sourceState=clean buildSource=verified
PHASES CP-7 slotWait=0s build=455.0874368s startup=3.399564s testsWall=0.0698825s teardown=0.5908081s hostWall=4.060267s
CHECKPOINT CP-8 commit=19b25d22171fc2f1126a1e661fefab3678d34173 build=reused filter=/*/*/ReleaseGateRunTests/C599_CreditVerdicts executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-18880da1/.antiphon/checkpoints/20261015-110741-beae/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=19b25d22171fc2f1126a1e661fefab3678d34173 sourceState=clean buildSource=verified
PHASES CP-8 slotWait=0s build=0s startup=3.0633174s testsWall=1.2625496s teardown=0.7190138s hostWall=5.0448808s
CHECKPOINT CP-9 commit=19b25d22171fc2f1126a1e661fefab3678d34173 build=reused filter=/*/*/InterimVerificationPolicyTests/C544_BaselineCompletion executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-18880da1/.antiphon/checkpoints/20261015-110741-beae/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=19b25d22171fc2f1126a1e661fefab3678d34173 sourceState=clean buildSource=verified
PHASES CP-9 slotWait=0s build=0s startup=44.4117188s testsWall=9.1546059s teardown=2.6632278s hostWall=56.2295523s
CHECKPOINT CP-10 commit=19b25d22171fc2f1126a1e661fefab3678d34173 build=reused filter=/*/*/InterimVerificationLandGuardTests/C544_FinalFullRequired executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-18880da1/.antiphon/checkpoints/20261015-110741-beae/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=19b25d22171fc2f1126a1e661fefab3678d34173 sourceState=clean buildSource=verified
PHASES CP-10 slotWait=0s build=0s startup=56.5142301s testsWall=5.4038724s teardown=2.2763012s hostWall=64.1944035s
CHECKPOINT CP-11 commit=19b25d22171fc2f1126a1e661fefab3678d34173 build=reused filter=/*/*/NightlyWatchdogCoreTests/(C545_IntentBeforeNetwork*)|(C545_ReceiptIdempotent*)|(C545_ReaderHeldWithoutSession*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-18880da1/.antiphon/checkpoints/20261015-110741-beae/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=19b25d22171fc2f1126a1e661fefab3678d34173 sourceState=clean buildSource=verified
PHASES CP-11 slotWait=0s build=0s startup=2.9180916s testsWall=0.2064616s teardown=0.4037631s hostWall=3.5283161s
```

Supplementary timeout line (no counts or TRX):

```
CHECKPOINT CP-14 commit=19b25d22171fc2f1126a1e661fefab3678d34173 build=ok filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=21m slot=granted waited=0s dirty=0 source=19b25d22171fc2f1126a1e661fefab3678d34173 sourceState=clean buildSource=verified
```
