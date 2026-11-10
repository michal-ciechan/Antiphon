# CARD-1831 round 2 Final Review

CLEAN. No blocker. D-1 and D-2 from Review 65820e79 hold on the repaired tip. Land the repair. `reviewedSourceClean` stays false: this card has no checkpoint receipt, and declaring true would be fabricated. Evidence-backed land of this outcome returns `review_evidence_source_not_clean` unless the caller uses the separate authorized SHA path.

Reviewed source: `83064051c1d7aa3227d84fc84d4a4d3cabc44f05` on `feat/card-task-b2a93bc5`. Repair task `b2a93bc5-99bd-49cf-9019-b713facbaca0`. Original Code landing owner `300d724e-7453-4600-bd22-1199e12adffb`. Range `d6f4584376cd3fa7fca1b86fb9ded06eb9b09838..83064051c1d7aa3227d84fc84d4a4d3cabc44f05` (red `24a7887b5`, fix `83064051c`). Reviewer `cfc6b8b4-edfb-4569-8b7e-10aa844fbe26` on `feat/card-task-cfc6b8b4`. Product files were not edited. Positive controls stay pending. No plan file and no `### Checkpoints` table, so the checkpoint tool was not run. The commissioned Linux selection is the scope of this round.

Nothing in this review ran on the desktop, queried live Windmill, or touched `C:\Users\lndco\antiphon-snapshot-runner`. The live job keeps today's bytes until the desktop copy below.

## D-1 holds

`Test-SnapshotPlainForbiddenLink` (`scripts/postgres-snapshot.ps1` 1392-1399) refuses `LinkType` exactly `SymbolicLink` or `Junction`. An empty or whitespace `LinkType` with the reparse attribute set returns false (accepted). That is the cloud-filter path.

`Invoke-SnapshotPlainOneDriveCopy` refuses a directory link on the OneDrive root (1575), on `Backups` (1582), and on the destination child (1600, 1611). `Test-SnapshotPlainChainLinks` walks each path part. `Test-SnapshotPlainFileEscapes` refuses a forbidden link or a resolved path outside the destination. `Update-SnapshotPlainRetention` returns `outside-destination` before any delete if any victim escapes, then rechecks each victim immediately before `Remove-Item` (1507-1519).

`Test-SnapshotPathInside` (153-159) appends a trailing separator before `StartsWith` (`OrdinalIgnoreCase`). `antiphon-postgres-evil`, a `..` name, and an 8.3-style short name against the long name all return false.

Round-1 reproduction on the repaired script, Linux, pwsh 7.5.4, probe lease `cb6f8b11-83c0-4a4c-b304-91b495c8d3d0`:

- Symlink of `Backups`, including an empty target: `state=failed`, `reason=link`, loud `ONEDRIVE COPY oneDrive=failed reason=link`, outside tree unchanged, no new write.
- Symlink of the OneDrive root, of the child `antiphon-postgres`, and of a dump file: same refusal. The dump-file symlink remains. Outside bytes unchanged.
- Missing `Backups`: `state=missing`, nothing created.

The suite's `PlainOneDriveLink` cases passed inside the 764.

Mutant D-1 (forbidden-link body replaced with `return $false`, copies under `/tmp` only): `PlainOneDriveLink` 11 passed, 19 failed, exit 1. Lease `a9a87a7a-4767-4444-a41d-bbd6a6ad498e`. The claim was 13/17. The measured witness is red, and redder: the mutant wrote the new dump through the Backups, parent-chain, child, and file symlinks (`oneDrive=ok bytes=9`). The repaired refusal is what stops that write. Claimed 13/17 is not the count on this witness.

## D-2 holds

`Invoke-SnapshotPlainPublish` calls `Update-SnapshotPlainRetention` at line 1771, after the second `pg_restore --list` and `Test-SnapshotPlainVerified` (1759-1768). A failing verify returns at 1728, 1737, 1756, or 1767 and does not call retention. The early returns before the file is published also skip retention.

`Test-PlainPublishPruneOrder` executes `Invoke-SnapshotPlainPublish`. On the repaired script, inside the 764: failed verify does not prune; prune runs only after verify and the hash match; 14 local and 7 cloud kept.

Mutant D-2 (retention call moved to before `$ready`, late call replaced with a success object, copies under `/tmp` only): `PlainPublishPruneOrder` 13 passed, 5 failed, exit 1. Lease `3405d1e0-6dad-4b51-b8de-56dc3702c68d`. Failures: failed verify pruned (`count=1`), oldest and next local dumps missing, local folder `count=13`, and `prune runs only after verify` with `early=True count=2`. The claim was 12/6. The order witness goes red. Cloud unpruned asserts on that mutant still passed.

## Attacks that did not become blockers

- Pattern remains `^antiphon-[0-9]{8}T[0-9]{6}Z\.dump$` (line 79). Hermes and openclaw sibling folders holding matching names, plus `notes.txt` and a `.dump.age`, stayed at 9 files each. Cloud destination kept 7 matching dumps.
- Hard link (`LinkType=HardLink`, reparse false) is not a forbidden link. Prune unlinked the inside name. Outside bytes stayed the same. A hard link is not followed by `Resolve-SnapshotRealPath`.
- A pre-existing `.partial` symlink to an outside file, and a `.partial` symlink to an outside directory, were removed and replaced by an inside file. Outside bytes unchanged. `Remove-Item` at line 1639 is not preceded by `Test-SnapshotPlainFileEscapes`. On this Linux probe it unlinked the symlink.
- Case-different sibling `ANTIPHON-POSTGRES` returns inside=true because the compare is `OrdinalIgnoreCase`. On Windows that is the same directory. Prune selects `Join-Path` of the destination plus a pattern name, so it does not walk a sibling. Not a demonstrated outside delete.
- The repair diff is ASCII (non-ASCII count 0).

## SOURCE-SHA

`Resolve-PlainSnapshotScript` reads `SOURCE-SHA`, requires `^[0-9a-f]{64}$` after trim, and `Get-FileHash` of each candidate at runtime (invoke script lines 20-41). A missing pin, a non-hex pin, or no matching hash returns empty. The main path then prints `SNAPSHOT fail exit=1 reason=missing-script`, writes a fail job-status record, and still calls `Send-JobStatusLine` (lines 84-133). That main path was not executed here: it would create `C:\Users\lndco\antiphon-backups\job-status` and send Telegram. The pin behavior is the job-status suite: skip an earlier copy with a different hash, refuse when nothing matches, refuse a missing pin, keep an earlier copy when the hash matches.

`Test-SnapshotStatusFresh` requires `mode=plain`. Age more than 1 hour in the future is stale, so 30 minutes ahead is fresh and 2 hours ahead is stale (job-status.ps1 117-123). Suite lines: non-plain stale, 30m future fresh, 2h future stale.

sha256 of `scripts/postgres-snapshot.ps1` at the reviewed tip: `8ec5a0e61f7aebfcbd7945cf38546413d91d91b3f51b0e574b0a4dee4b41c414` (100366 bytes).

## Advisories (do not hold the land)

A-1. Empty `LinkType` plus a reparse point is accepted on purpose. A junction whose `LinkType` is exactly `Junction` is refused (classifier and the suite). A junction that PowerShell 5.1 reports with an empty `LinkType` would be accepted. This review did not run Windows PowerShell 5.1 and did not create a Windows junction. The desktop task still has to show a real junction of `Backups` refused, and a real OneDrive Files On-Demand directory accepted. File a card if a live junction comes back with an empty `LinkType`.

A-2. `Copy-Item` and the `.partial` `Remove-Item` are not no-follow opens. Retention rechecks immediately before each delete and is not atomic with that delete. A link swapped in after the check was not raced. No outside write was demonstrated.

A-3. `AppExecLink` and whitespace `LinkType` are accepted. Hard links are accepted and only the inside name is unlinked.

## Verification at 83064051c1d7aa3227d84fc84d4a4d3cabc44f05

Leased, native default output, `UseAppHost=false`, `TEMP=/tmp`. No `-o` or `OutputPath`. No plan rows. Downgrade selector (`impacted-classes.ps1` on the range): kinds empty, treenode filter empty, count 0.

- `scripts/test-postgres-snapshot.ps1`: 764 passed, 0 failed, exit 0. Lease `84567bef-718a-46fa-909e-b5d78f19cb73`, held 440s.
- `scripts/test-job-status.ps1`: 70 passed, 0 failed, exit 0. Lease `7f168944-c9d7-4911-93ac-647a86feb246`, held 13s.
- D-1 mutant: 11 passed, 19 failed, exit 1. D-2 mutant: 13 passed, 5 failed, exit 1. Both under `/tmp` only.
- Guard build of `tests/Antiphon.Tests`: 0 errors. Lease `e5a02c23-57e4-45ed-8ea4-3ef732eaef89`, waited 45s, held 344s.
- Guard run `--no-build`, filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/*`. Lease `1dbaec7b-cafc-4ac1-867a-042ca0805123`, held 159s. 78 passed, 0 failed, 0 skipped. TRX `/tmp/card1831-r2-guards/card1831-r2-guards.trx`. Class tallies: policy 41, classification guard 1, slow registry 5, tripwire 2, NativeLinuxPolicyWitness 3, mechanical 11, TaskPlatformGuidance 5, C1729 9, lane category 1.

`check-evidence-diff.ps1` over the range: commits=2, entries=0, violations=0. `git merge-tree --write-tree origin/master HEAD` exited 0, tree `43e031a6f4ffc4ec834ce5d558a90845d05f1c21`. `origin/master` is `057bf1f0361bb507dd11a818763e9e094fe1d192`, newer than the brief's `b8812bcb12ba309092007e216251284466181b5d`.

`ordinaryScopeCompleted` is Full for the commissioned Linux selection. Round 1's unrun Windows items are the post-land desktop task in this brief, not an open Linux row. `reviewedSourceClean` is false. There is no `source.json` and `validate-checkpoint-receipt.ps1` was not passed. PCs remain pending.

## Desktop task after landing

Copy these four files into `C:\Users\lndco\antiphon-snapshot-runner`:

- `scripts/postgres-snapshot.ps1`
- `scripts/windmill/invoke-postgres-snapshot.ps1`
- `scripts/windmill/invoke-postgres-snapshot-stale.ps1` (unchanged in this range; it dot-sources job-status)
- `scripts/windmill/job-status.ps1`

Write `SOURCE-SHA` beside them as the 64 hex digits below, ASCII, no BOM, optional trailing newline. PowerShell 5.1 `Set-Content` writes UTF-16 by default; that fails `^[0-9a-f]{64}$` and the runner fails closed.

`8ec5a0e61f7aebfcbd7945cf38546413d91d91b3f51b0e574b0a4dee4b41c414`

Confirm the copied `postgres-snapshot.ps1` hashes to that value. Then, on the desktop, show a real junction of `Backups` refused with nothing written or deleted outside, and a real OneDrive Files On-Demand directory accepted. Live ACL and the next 07:50 run stay human-owed. They do not hold this land.
