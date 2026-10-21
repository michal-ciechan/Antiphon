# CARD-1405 snapshot script checkpoints

Date: 2026-10-21. Code task `7d4b009e`. Design: `docs/superpowers/plans/2026-10-21-card-1401-postgres-to-server2-plan.md` stage B / B1 at `946344bf635f8576f878c8ef2199135af8f6a43f`. This file is the closed checkpoint list for the script slice. It does not enable a schedule and it does not run against production Postgres.

## Verification design

V-1. `scripts/test-postgres-snapshot.ps1` with no `-Case`: `-WhatIf` lists the dump, encrypt, copy, and retention steps, exits 0, does not invoke docker, and writes nothing; a missing recipient reference exits non-zero with `reason=missing-recipient`; a recipient path inside the destination exits non-zero with `reason=recipient-inside-sync` and writes no dump; `-RestoreDrill -Execute` aimed at container `antiphon-postgres` or volume `antiphon_pgdata` exits non-zero with `reason=drill-refused` before docker; a scratch `-WhatIf` drill names `antiphon-postgres-drill` and `pg_restore`; `Get-SnapshotPruneSet` keeps 14, skips an unverified file, and refuses to drop the new file. The same run refuses an id prefix, an alias, an unlabelled container, a labelled container mounting the source volume, a labelled container mounting the source container's actual non-denylist volume, `antiphon-server2-postgres-postgres-1`, and `-ScratchDatabase antiphon` with exit 9, `reason=drill-refused`, and a named guard before `pg_restore`. A scratch volume that is the source mount, a volume mounted by another container, an existing volume without the drill label, or a volume the reused container does not mount is refused the same way, and a trailing slash on those production names is exit 9 rather than exit 2. A labelled container with `PGHOST` or `PGPORT` set is refused before `createdb`. A dump under 1048576 bytes or without `TABLE DATA` exits 5 and publishes nothing. A non-matching conflict copy is not deleted and does not fail retention. Execute keeps the staging root and any pre-existing notes or `dev-backup-*.dump`, works in an owner-only `run-<id>` directory (0700 on Linux), and deletes only that directory. A staging path that is a link, or that sits inside the destination even when the destination is outside the sync root, is refused before docker. A drill creates an absent scratch volume with label `antiphon.snapshot-drill=1`, labels its scratch container, and removes that container and volume unless `-KeepScratch`. No production database, volume, or OneDrive directory.

No R-n. No PC-n. Mutation owns positive controls; this slice declares none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | n/a | snapshot-script | `pwsh -NoProfile -File scripts/test-postgres-snapshot.ps1` | V-1 | 24 cases, 0 failed, exit 0 | n/a | 2 |

### Cost

Ordinary floor: 2 minutes.
