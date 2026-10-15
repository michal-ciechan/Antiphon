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

The committed preflight record precedes execution. Results, actual tested SHA,
unedited CHECKPOINT lines, slot/wait values and qualified receipt paths will be
recorded after terminal completion. Generated evidence remains ignored under
`.antiphon/checkpoints/`; the stored task report is `.antiphon/task-18880da1.md`.

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
