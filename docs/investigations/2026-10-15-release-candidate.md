# Release candidate 2026-10-15 — CARD-1281 pilot

Status: in progress; no release qualification or publication credit claimed.

Pinned candidate: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
Morning baseline: `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.
Task branch: `feat/card-task-1c14bf28`. Raw generated evidence stays outside Git at
`/work/evidence/task-1c14bf28/raw/`.

## Required coverage and available evidence

The owner `docs/release-gates.md` and schema-v2 `tests/test-execution-policy.json`
(policy hash `922cce2a0064bebfc90e211fe33db10dc74cb61ecb5d9f050bd00ec669e10ada`)
require the full automated **Windows** profile: `antiphon`, `session-runner`,
`pty-host`, `agents-pty`, `messaging`, `client`, `scripts`, and `e2e`.
This includes eligible Unit, Integration and Slow cases, native suites, messaging
against a disposable broker, fresh client build/lint/Vitest, the script census,
and automated E2E. OptIn alone does not exclude a case; only the policy's owned,
reasoned class/method exclusions apply. Live providers, headed canaries,
separately approved distiller tests, QEMU SourceLanding and Linux-only lanes are
explicitly outside that Windows profile, under their named owners.

Every required discovered UID needs one disposition, fresh source-bound execution
and TRX. Missing/duplicate UIDs, required skips, unknown outcomes, zero required
execution or stale evidence block release. E2E prerequisites are npm ci/build/lint,
the E2E build, generated Playwright Chromium installation and a fresh bundle;
missing Docker/browser/owned runner or fixture failures are incomplete/red.
E2E must use its disposable database and loopback random-port runner, with live,
headed, task-token and production override inputs cleared.

RC release credit additionally needs true boolean coverageComplete/testsPassed/
reportDelivered, exitCode 0, runId, matching policy hash, candidate id, valid
`release/rc-YYYYMMDDTHHMMSSZ` ref and matching full SHA/expectedSha. Publication
requires the producer journal, repeated exact remote identity validation, complete
required suite results, delivered report, annotated CalVer tag, GitHub Release,
read-back manifest/assets/digests and build-probed capabilities. No such release
credit or publication is commissioned here. The caller explicitly requested
`release/2026-10-15`; this immutable pilot branch is not the standard cutter's
accepted candidate-ref shape and cannot earn formal rc-release credit.

This native Linux host can produce a new pinned-source build and execution of
the explicit CARD-1287 373-class roster (expected 4,516 cases), inspect/attempt the
documented stack smoke safely, and attempt isolated automated E2E and its
prerequisites. It cannot produce the full Windows/native eight-suite RC profile,
Windows qualification, live Windmill registration/readbacks and two real schedule
slots, publication/tag/manifest/status receipts, or accepted master scheduled
readiness/watchdog/outage/notification receipts and an Interim admission/refusal
pilot. Those pieces remain missing, even if all local tests pass.

Interim stays disabled; no configuration or schedule is changed. Before activation
the owner additionally demands accepted CARD-0545/0487 nightly qualification,
independent watchdog deployment/custody, readiness configuration, the committed
qualification-bound Interim receipt, scheduled master complete-green and fresh
monitor, CARD-0544 pilot with deferred cases run in Final/exact-SHA land/rollback/
costs, and CARD-0599 full release-gate qualification. RC green never supplies
master daily readiness.

D-6 context: daily admission is only for opted-in, bounded, nonurgent cards, up to
10 independent groups per 16:00 Europe/London cut, with urgent Full work retained.
A branch cut is not approval of a train, member acceptance, Full Review, landing,
or activation. Required missing evidence stays an incomplete gate under D-2;
historical outcomes/costs are preserved.

## Execution results

Pending. Production AppHost/server/runner on 17202/17204 will not be stopped or
restarted. No product/test fixes, timeout relaxation or fabricated receipts.

Initial isolated Linux build: PASS, source HEAD `067b47e2dc903247f1a7c8aa539032cc4266e4ad`, 123.498s wall, 1,057 warnings, 0 errors, granted slot with 0s wait. Main assembly SHA-256 `3f92aaf671875db0b613e83d60f55d324655748d2febbdbd557a58acb5421290`; pinned informational-version bytes verified. The next documentation-only commit records this result before the long no-build test run; no product/test input changes. Build argv/log/timing/provenance are retained under the raw evidence root.

## Refinement and immutable ref correction

The caller's refinement arrived after the originally requested `release/2026-10-15`
was already pushed. That ref is retained at the pinned SHA; it was not deleted or
moved. The corrected authoritative pilot ref is
`release/rc-20261015T215926Z`, created by a plain non-force push of the pinned SHA
after an absence check and verified with `git ls-remote`. The cutter was not used:
its source rejects linked worktrees, and this assigned checkout is one. No owned
producer clone or candidate journal was fabricated. Remote readback is retained
in `raw/corrected-release-ref.txt`.

No tag or `publish-release.ps1` invocation is authorized or performed. The refined
release-note draft will be `2026-10-15-release-notes-v2026.10.15.1.md`; the proposed
version is not a published release.

## Completed Linux roster and smoke

| Piece | Verdict | Total | Passed | Failed | Skipped | Outer wall | Exit |
|---|---|---:|---:|---:|---:|---:|---:|
| Isolated pinned-source Linux build | PASS | — | — | 0 errors | — | 123.498s | 0 |
| Frozen Linux roster, one run | FAIL | 4,516 | 4,515 | 1 | 0 | 2,699.430s (44m59.430s) | 2 |
| Failed class, one isolated rerun | FAIL | 4 | 3 | 1 | 0 | 24.027s | 2 |
| Documented stack smoke | FAIL / health checks incomplete | — | Docker + owned network | missing dev Postgres + Windows-only cmdlet | — | 4.120s | 1 |

The roster's native TUnit duration is 44m52.566s; outer wall includes driver/slot
handling. Its slot was granted with 0s wait; the rerun waited 15s; smoke waited 0s.
Fresh TRX has exactly all 373 frozen roster classes, no missing/extra classes, and
2,866 distinct observed methods. The main assembly hash remained byte-identical
to the pinned-source build. Runtime checkout HEAD was the evidence-only commit
`cef5e26487da2b2d87fc4aaa9a62365632b74c71`; its only delta from the candidate is
this evidence note. No product/test input was modified or rebuilt for the run.

The single roster failure and its reproduced assertion are detailed in
[the defect fan-out](2026-10-15-release-candidate-defects.md). The missing Aspire
stack and Windows-only listener command leave smoke incomplete on this Linux
host. No production process was changed. E2E remains a separate pending piece.

Raw TRX: `/work/evidence/task-1c14bf28/raw/roster/run.trx` and
`/work/evidence/task-1c14bf28/raw/rerun-project-contract/run.trx`.
Exact argv, consoles, timings and machine-readable audits are beside those files.
