# CARD-0599 portable release-gate preflight

Status: **UNQUALIFIED; activation binding and live acceptance pending.** Original
Code/landing owner: `18880da1-ceca-4132-b085-75d512bed1b8` (CARD-1281 D-5).
Source coordinates and execution provenance are in the
[nightly qualification record](2026-10-15-card-0487-nightly-qualification.md).

CP-7 covers V-19/R-4 through `ReleaseGateActivationTests.C599_Admission` and
`C599_QueuedReadiness`: valid control, disabled/missing/wrong qualification identity,
recipient/outage/watchdog evidence, and stale/future/unreadable monitor refusal.
CP-8 covers V-20/R-4 through `ReleaseGateRunTests.C599_CreditVerdicts`: one TUnit
execution with 24 named script-harness assertions. RC credit cannot satisfy master
readiness; master credit cannot authorize release. False/string booleans, missing
identity and wrong candidate SHA earn no credit. Fixture files are not live receipts.

The [activation plan](../superpowers/plans/2026-09-22-card-0599-release-gate-activation-plan.md)
still requires its upstream settings/entrypoint binding (A-1). Its Q-1/Q-3
enable/trigger commands cannot be supplied until that seam exists. The CARD-1281
binding follow-up does not implement it or authorize switching schedulers.
Checked-in Windmill definitions and legacy owner prose are not deployment readbacks.

CARD-1281 Q-2, including activation-plan Q-1..Q-4, remains pending:

| Activation gate | Required evidence; current outcome |
|---|---|
| Q-1 | Deployed entrypoint/settings and exact server SHA; registration twice, one disabled recurrence, cron/timezone and no competing schedule. Not run; upstream binding required. |
| Q-2 | Real queue/process-adapter rehearsal at landed source, restart/contention/readback negatives and recipient receipts. Not run; upstream implementation required. |
| Q-3 | Actual manual entrypoint, full Windows RC including E2E, candidate/tag remote readback, downloaded manifest/summary digests and complete board GET body/revision. Not run. |
| Q-4 | Both real schedule slots and at least one actual scheduled complete-green published release; job/native/recipient identity and master capacity. Not run. |

The budget remains 20+20+313+318 = 671 active/run minutes plus slot waits.
No schedule, candidate, tag, release, settings or external message is changed by
this preflight. Interim remains unqualified; no savings are measured.

PC-41 (RC -> master), PC-42 (master -> release), PC-43 (coverage completeness),
PC-44 (recipient evidence), and PC-45 (freshness boundary) remain **pending** for
method-scoped SourceLanding Mutation. Every original CARD-0599 control/variant
also remains pending; this narrow preflight discharges none. Restart: none.
