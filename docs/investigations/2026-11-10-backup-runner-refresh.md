# Backup runner refresh, 2026-11-10

Desktop refresh of `C:\Users\lndco\antiphon-snapshot-runner` from master
`f125755c1648533c473ce46311c307788b4a9b65` (CARD-1831). No `-Execute`, no
Telegram, no live dump. The archive object of `scripts/postgres-snapshot.ps1`
is `7f6f7e5cd38bd7674ff1f4e63c760f93c46231ed`, the same id as that commit.

The invoke script has no `-WhatIf`. Its main path always passes `-Execute` and
posts Telegram, so it was not launched. `Resolve-PlainSnapshotScript` was
called after a dot-source, which is the check the main path prints before it
would start the child.

## Runner directory

Before, the directory had no `SOURCE-SHA`. `SOURCE-SHA.txt` was 40 bytes,
`d6f4584376cd3fa7fca1b86fb9ded06eb9b09838`. That file is still there and is
not read. The resolver reads `SOURCE-SHA` and requires 64 lowercase hex digits.

| File | Before bytes | Before sha256 | After bytes | After sha256 |
|---|---:|---|---:|---|
| postgres-snapshot.ps1 | 93414 | `cc029abd12dbe5643fa4c461c3e591e268459c4b048ae46784d058a3a2c8ccce` | 100366 | `8ec5a0e61f7aebfcbd7945cf38546413d91d91b3f51b0e574b0a4dee4b41c414` |
| invoke-postgres-snapshot.ps1 | 4799 | `339cf6d8d0d951d13fa791f956c8104a98f0fa2951867de1ff61f4646f344e10` | 5941 | `c6e58ad01e4cacb057e7c7fe2ac9e6692ea3bc8721ccc09db7c795e2cdce2667` |
| invoke-postgres-snapshot-stale.ps1 | 1072 | `2577122f9b068819ab6687286e5dd25e4746b9f800135f0e93f33a15ebf0e036` | 1072 | `2577122f9b068819ab6687286e5dd25e4746b9f800135f0e93f33a15ebf0e036` |
| job-status.ps1 | 9538 | `36dc4f0fe32ebd4c33a45a68b068d18be608d17681eaadd26bb8dc4f925adff4` | 9660 | `f8f840efb33c884df3953a04e749ada93fac15ef5debc046eacc6f0327af1123` |
| SOURCE-SHA | absent | | 64 | `1ce5d59d2c6daab6953c922bb24ddeaff7e3510eb0193fffcb0027d2443e0222` |

`SOURCE-SHA` is the 64 hex digits of the copied `postgres-snapshot.ps1`, ASCII,
no BOM, no trailing newline. It matches the Review value
`8ec5a0e61f7aebfcbd7945cf38546413d91d91b3f51b0e574b0a4dee4b41c414`.

Both invoke scripts dot-source only `job-status.ps1`. No other file was copied.

## Pin

Default resolve, with no dump:

```
snapshot-script=C:\Users\lndco\antiphon-snapshot-runner\postgres-snapshot.ps1
source-sha=8ec5a0e61f7aebfcbd7945cf38546413d91d91b3f51b0e574b0a4dee4b41c414
```

The chosen file's sha256 equals that pin. `C:\src\Antiphon\scripts\postgres-snapshot.ps1`
is `5510080e4d0bc2585111b29147ec48b49ac1682deb1212d68a12fb68583284c1` and is
skipped. `C:\Antiphon\worktrees\card-task-300d724e\scripts\postgres-snapshot.ps1`
is the old runner hash and is skipped.

A temp copy of the runner (not the live directory) with pin
`0000…0000` returned empty for an explicit candidate and for the default
candidate list. The old 40-hex git id in that copy also returned empty.

## Windows suites

From the clean worktree at `f125755c1648533c473ce46311c307788b4a9b65`:

- `scripts/test-postgres-snapshot.ps1`: 376 passed, 10 failed, exit 1.
- `scripts/test-job-status.ps1`: 70 passed, 0 failed, exit 0.

The 10 failures are all in `Test-PlainPublishPruneOrder`:

- failed verify exits 5 — got `4 dump-failed`
- failed verify reason — got `dump-failed`
- verified publish exits 0 — got `4 dump-failed`
- verified publish calls retention — count=0
- verified publish keeps 14 local dumps — count=15
- verified publish drops the oldest local dump — still present
- verified publish lists the dump twice — lists=1
- verified publish keeps 7 cloud dumps — count=8
- verified publish drops the oldest cloud dump — still present
- verified publish drops the next cloud dump — still present

The witness replaces `Invoke-SnapshotNative` and writes the fake dump only when
the `cp` destination does not contain `:`. On Windows that destination is a
`C:\` path, so the file is never created. `Invoke-SnapshotPlainPublish` then
returns `dump-failed` (exit 4) at the missing host file, before the second
`pg_restore --list`. Linux recorded 764 and 70 at the reviewed tip. This
Windows run was not given a product change.

## Link proof

Temp root `C:\Users\lndco\AppData\Local\Temp\card-b28fd575-link`. The same
result on Windows PowerShell 5.1.19041.6456 and pwsh 7.6.9. Junction and
symlink `LinkType` values were `Junction` and `SymbolicLink`, not empty.

| Case | Result |
|---|---|
| `mklink /J` on `Backups` | `state=failed reason=link`, outside bytes unchanged |
| `mklink /J` on `Backups\antiphon-postgres` | `state=failed reason=link`, outside bytes unchanged |
| `mklink /D` on `Backups` | created, `state=failed reason=link`, outside bytes unchanged |
| normal `Backups\antiphon-postgres` | `state=ok`, copied 10 bytes, sha256 matched, no `.partial` left |

`-Plain -WhatIf -OneDriveCopy` against that temp root printed `SNAPSHOT plan exit=0 reason=ok` and wrote no dump.

Real `C:\Users\lndco\OneDrive`, `Backups`, and `Backups\antiphon-postgres` were
read only. Each has an empty `LinkType` and the reparse attribute set.
`Test-SnapshotPlainDirectoryLink` is false and `Test-SnapshotPlainChainLinks`
is `ok`, so the production check accepts the cloud-filter directory. A live
junction did not come back with an empty `LinkType`.

The real folder has 3 dumps, 1594125176 bytes:

- `antiphon-20261110T104159Z.dump` 530395357
- `antiphon-20261110T114120Z.dump` 531543585
- `antiphon-20261110T121911Z.dump` 532186234

## ACL

`C:\Users\lndco\antiphon-backups` was not modified. `icacls` shows one entry:
`DESKTOP-KTLKPIF\lndco:(OI)(CI)(F)`. `Test-SnapshotOwnedDirectory` is true and
`Test-SnapshotWindowsDirectory` returns ok.

## Windmill

Read-only GET of the live schedules. A short-lived token was minted on server2
and deleted in the same script. The token was not printed.

| Path | Live | Committed |
|---|---|---|
| `u/lndcobra/antiphon_postgres_snapshot` | `0 50 7 * * *`, Europe/London, enabled true, tag desktop, is_flow false | same |
| `u/lndcobra/antiphon_postgres_snapshot_stale` | `0 0 8 * * *`, Europe/London, enabled true, tag desktop, is_flow false | same |

## What 07:50 should show

The child is Windows PowerShell 5.1 with `-Plain -Execute -OneDriveCopy`. The
lines before the dump should be:

```
snapshot-script=C:\Users\lndco\antiphon-snapshot-runner\postgres-snapshot.ps1
source-sha=8ec5a0e61f7aebfcbd7945cf38546413d91d91b3f51b0e574b0a4dee4b41c414
```

A successful copy should then end with `ONEDRIVE COPY oneDrive=ok`, a
`DB backup: OK <size> +cloud` line, and `telegram=0`. The real cloud-filter
folder is accepted, so a link refusal is not the expected cloud result. The
08:00 stale job should print `JOB STATUS fresh` and send nothing when that
success is newer than 26 hours.
