# Review: daily Postgres backup follows the database (CARD-1831 plan)

Round 1, Final. Subject `feat/card-task-a1368228` tip `aff936777756af49d305ba76718458aca881eced` (plan task `a1368228-96ac-4a0e-9e5c-ba028045948e`). Range `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4..aff936777756af49d305ba76718458aca881eced` (two docs commits: `908a4e0a19`, `aff9367777`). The diff is only `docs/superpowers/plans/2026-11-18-postgres-backup-retarget-plan.md`. No product code or tests changed, so the named guard classes were not re-run. No PC-n is named. Mutation stays waived and every PC stays pending.

## Verdict

Not clean. One blocker. Advisories below do not add further hold conditions.

## Blocker

Where: `docs/superpowers/plans/2026-11-18-postgres-backup-retarget-plan.md`, cutover step W-5.

Failure: `snapshot-target` is written back to `desktop` only when W-3 or W-4 fails. A later phase 4 or phase 5 failure, or a phase 6 rollback after W-3 and W-4 already succeeded, leaves the word at `server2`.

Why: Cutover phase 3 is verify-only (`scripts/cutover-postgres-server2.ps1` WhatIf text: do not repoint; `antiphon-postgres` is still up). Phase 4 stops the desktop container and repoints AppHost. Phase 6 starts `antiphon-postgres` again and restarts AppHost against the desktop volume. W-3's flip is a persistent file write. W-5 does not run on that rollback, so the next 07:50 dumps server2 while production is the desktop volume again.

Fix: Amend the plan only. Whenever phase 6 runs, write `desktop` back into `snapshot-target`, including when W-3 and W-4 succeeded. Keep the early flip if the proof is meant to run after phase 3; the missing piece is the rollback write-back, not the proof itself.

## Reconciliation with Code task 801355c0

Task `801355c0-5e29-494b-b693-5781c74da718` (CARD-1844, Dispatched, `feat/card-task-801355c0` at `579980a07468a4858ee34cbe938a820381f29636`) is titled "Postgres move gaps A". Its pushed commits are G-1 (`ANTIPHON_SKIP_DEV_POSTGRES=1`) and G-2 (accept `postgres:16.N@sha256` and `postgres:16.N-trixie@sha256` in `scripts/deploy-server2-postgres.ps1`, plus the matching sentence in `docs/docker-stack.md`). It has not edited `scripts/postgres-snapshot.ps1` or `scripts/windmill/invoke-postgres-snapshot.ps1`.

Its goal still includes G-4, and the runbook it was told to follow (`2e8907ee07fe42e5baaf2fd726d998f655c73087`, `docs/investigations/2026-11-18-postgres-move-runbook.md` gap table) says to pass `-Container antiphon-server2-postgres-postgres-1` with `DOCKER_HOST=ssh://…` and `-AllowRemoteDocker`, or to run the job on a server2 worker.

This plan rejects that mechanism. D-1 keeps the desktop Windmill worker. D-2 refuses a free `-Container`. D-3 refuses any `DOCKER_HOST` and leaves `-AllowRemoteDocker` unused. The server2 path is docker context `antiphon-server2` with an `ssh://` endpoint only, writing into the same desktop folders.

One implementation owns those two scripts. Do not let 801355c0 implement the runbook's G-4 sentence. Either drop G-4 from that task, or retarget its remaining G-4 work onto this plan (after the W-5 amendment). S4's `docs/docker-stack.md` paragraph must be added beside the G-2 sentence, not by restoring the two-form-only image wording. This plan does not edit `scripts/deploy-server2-postgres.ps1`. The pin `postgres:16@sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b` still matches both the master acceptor and the G-2 acceptor.

The plan's collision note is true of pushed snapshot-script diffs and misses this dispatched G-4 goal, and it misses the `docs/docker-stack.md` overlap already on `579980a07`.

## What matched

- One-word `snapshot-target` (`desktop` or `server2`), missing or any other value fails closed, and the wrapper sends one FAIL Telegram line (D-2, V-7, V-8). Direct `-Target` defaults to `desktop`.
- Server2 dump uses the named ssh-only context into the existing desktop folders. Retention stays 14 dumps and 7 OneDrive copies (`Retain` default 14, `OneDriveRetain` 7). `snapshot-status.json`, the 08:00 stale check (`u/lndcobra/antiphon_postgres_snapshot_stale`, 26 hours), and the OneDrive copy stay on the desktop path (D-1, D-4, D-8).
- Plain `-RestoreDrill` for `-Target server2` only, scratch image is the source container's image id, desktop plain drill stays `plain-drill-unsupported` (D-6, D-7, V-11, V-12).
- Image pin matches the registry read during this review: `16.14-trixie` and `16.14` index digest `sha256:95206741a5b214807675e14165369d05b93a9cf692223b616d07cca227e74b0b`; amd64 manifest `sha256:670391653713782e51974845b217c56fed4dd8729142299c43c919a8d3e15e00`; config `sha256:8a4b045886180c64f518ca6a4b0791b15fb997f3b3b619bd5ee141914668db55`. Floating `postgres:16` is `sha256:dd2bf3ce2dc83a81674cf7f217fcfd8a5009a4b325dda5b843bfee82a06cff6f`. The config blob body did not return, so `PG_VERSION` was not re-read from the blob. `deploy-server2-postgres.ps1` on master accepts `postgres:16@sha256:<64>`.
- Fourteen short command rows CP-1..CP-14. `Min` is `n/a`. `EstimatedMinutes` sums to 15. Unknown `-Case` exits 2 (`scripts/test-postgres-snapshot.ps1`). O-1..O-5 are present. Ground-truth container, volume, plain-drill refusal, and remote-docker refusal match master `e229fb4d3`.
- `git merge-tree --write-tree origin/master HEAD` exit 0, tree `c2175295225c45b7f3cba88b03bb450af8742f66`. `origin/master` is `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`.
- `scripts/check-evidence-diff.ps1` over that range: commits=2, entries=0, violations=0.

## Advisories

These do not hold the land. File them as cards if they are not folded into the plan amendment.

- V-4 locks `DOCKER_HOST=ssh://x` only. D-3 says any `DOCKER_HOST` is refused, because it overrides `--context`. A local `npipe://` or `unix://` value is the desktop Docker Desktop case.
- S1 refuses `-Target server2` plus an explicit `-Container` (`ambiguous-mode`) and has no V-n row. That is the free-name hazard D-2 rejects.
- R-3 says `PosixHarnessToolTests` runs the R-1 case names. `SnapshotCases` is `StagingRefusesLink` and `WindowsAclRules` on Windows, and `StagingRefusesLink`, `StagingRefusesForeignRoot`, `DrillRefusesIdPrefix` on Linux (`tests/Antiphon.Tests/Scripts/PosixHarnessToolTests.cs`). The C# list can stay unchanged. The "same cases as R-1" sentence cannot.
- `Get-SnapshotContainerFact` today stores the drill label only, volume names without a destination, and no running state or image id. D-5 and D-7 need those fields on that fact read.
- D-6 should name the plain destination (`C:\Users\lndco\antiphon-backups`, pattern `antiphon-<utc>.dump`) as the drill's search directory. "Newest local dump" does not say which directory.
- A few citations are off by a few lines (child argument list is `invoke-postgres-snapshot.ps1` 67-69; the age `-v <stage>:/work:ro` mount is `postgres-snapshot.ps1` 1275-1279; the OneDrive default assignment is line 1812). The behaviors those sentences describe are on master.
