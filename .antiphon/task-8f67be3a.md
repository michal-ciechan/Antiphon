# CARD-0669 Review 8f67be3a round 1

Do not land `c0abb4faca41f7cdf3e80a54af421d5b7cac5421` as CARD-0669. The tip is a finished desktop worktree-cleanup report. It does not add a land-residue sweeper, and the committed diff cannot delete a git ref.

## Subject

- Card: CARD-0669 `70ba1e19-9ccb-40f0-8a19-e7e4ab8f2e5b` (InProgress). Ask: a lock-free sweeper for terminal land refs and pins older than a retention window, verify-temp directories for terminal lands, counts, and a git-spawn measurement.
- Code owner: `29f3946d-24ca-453b-b6ef-991c8fbf74f3`, title "Desktop Antiphon artifact cleanup >24h", role Code, status Succeeded, `cardId` equal to CARD-0669, branch `feat/card-task-29f3946d`.
- Reviewed tip: `c0abb4faca41f7cdf3e80a54af421d5b7cac5421` (remote `refs/heads/feat/card-task-29f3946d` matches).
- Base: `63b2e70479c846cacd01af6b20190e845f8258e4`. Unique range versus `origin/master` `bc71a95e7710532a9a68416f1d7b9e158fe96a15`:

| SHA | Subject |
|---|---|
| `633df73ce6aba647b1077e82f4713b2a75ece5c4` | docs: report partial desktop cleanup and retained policy holds |
| `49d18e75543f319d5d93920510c2efa0ff8776fc` | docs: record refined cleanup authority and fresh worktree guards |
| `c0abb4faca41f7cdf3e80a54af421d5b7cac5421` | docs: record 31 GB partial cleanup and verified branch retention |

Net diff versus merge-base and versus the merge-tree with current master: one file, `docs/investigations/2026-10-13-desktop-antiphon-cleanup.md`, +55 lines. No script, test, or production file changes. `tests/`, `server/`, `src/`, and `scripts/` are empty versus the base.

## Finding

Where: `docs/investigations/2026-10-13-desktop-antiphon-cleanup.md` (the only change).

Failure: The tip does not implement CARD-0669. It records an already-executed desktop cleanup (118 worktrees and 3 Temp directories, branches retained, `git worktree remove --force` on registered worktrees). It states that no builds or tests ran, that no plan, checkpoint, V/R, or PC ids exist, and that its own next stage is decide. There is no dry-run product gate, no retention check against a live or pending land, a review-evidence binding, or a retention window, and no scratch-repo test of keep versus delete sets.

Why: Landing-queue audit 991aa3f4 classified this unlanded docs tip as the CARD-0669 ref deleter. The bound task is a desktop artifact cleanup. A Clean Final would authorize landing that prose as the card.

Fix: Leave `c0abb4faca41f7cdf3e80a54af421d5b7cac5421` unlanded. Do not treat it as CARD-0669. Send the card back to plan for the lock-free sweeper. The investigation's own continuation (more desktop deletions) stays a separate operator decision. This review did not run the external script `C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1`.

Introduced by this range. Not inherited.

## Safety of what the bytes can delete

The git change is markdown. Landing it installs no deleter and cannot delete `refs/antiphon/land`, `source-recheck`, or any other ref.

The report's historical operation says branches were retained, missing or changed refs were 0, reparse points were unlinked without following targets, and a manifest existed before deletion. That is not a product dry-run, and this review cannot re-check the desktop. The keep and delete sets are not proven by any test in the repo.

`origin/master` already has CARD-1432 `LandRefRetention`, which deletes superseded `refs/antiphon/land/<task>/` and `refs/antiphon/progress/<task>/` refs after a land while keeping the latest and Refused operation pins, and which treats `source-recheck` as an observation pin. The 2026-11-08 housekeeping note still records CARD-0669 as having no product commit. This tip does not change that class. merge-tree does not touch it.

## Verification

No `### Checkpoints` table exists. The author says no plan was assigned. Closed checkpoint rows: none. Unlisted guard run reason: the brief names one leased guard batch, and there is no plan row to import. Invariant: classification, lane, slow-test, mechanical-PC, and platform-guidance guards at this SHA. Cost: one default-output build and one filtered run. Not a full-assembly run.

Build, slot `0438cb52-0872-4582-b60d-ce1ef76482ca`, `dotnet build tests/Antiphon.Tests --property:UseAppHost=false`, native output, `TEMP=/tmp`: 0 errors, 712 warnings, 3m00s, at `c0abb4fac`.

Guard run, slot `ade08b24-bbe9-41e3-a9f4-c7b616483982`, `--no-build`, same property, filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestTripwireTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(TestLaneCategoryGuardTests*)/*`, TRX `.antiphon/card-0669-review-8f67be3a/guards.trx`: 45 total, 44 passed, 1 failed, 0 skipped, 1m47s. Exit 2.

Failed: `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration`. The scanner reports ten untagged `DispatchBaseWarningDeliveryE2ETests` classes. Those lines are inside the raw string at `tests/Antiphon.Tests/Application/DispatchBaseWarningRosterTests.cs:107`. `git diff` of `tests/` against the base is empty, so this red is inherited. The merge with `origin/master` does not change that file. It is not a regression of this tip and is not a reason to patch the investigation.

Absent at the reviewed SHA, so not executed and not counted as passes: `SlowTestRegistryMergeTests`, `NativeLinuxPolicyWitnessTests`, `C1729LayeringGuardTests`, `DerivedImpactIndexTests`, `C1514LandingSplitBudgetTests.C1584_E2EExpandedBudget`. `scripts/impacted-classes.ps1` and `scripts/check-os-witnesses.ps1` are also absent.

`scripts/check-evidence-diff.ps1` base `63b2e70479c846cacd01af6b20190e845f8258e4` head `c0abb4faca41f7cdf3e80a54af421d5b7cac5421`: commits=3 entries=0 violations=0, exit 0.

`git merge-tree --write-tree origin/master HEAD`: exit 0, tree `f6389c43168c74c92704474d3bb0c9dcda753206`. That tree adds only the investigation file.

Downgrade selector: the range matches no `server/Migrations/**`, `AppDbContext.cs`, or `*Model.cs`. Changed `MigrateAsync` / `IMigrator` / `MigrationTestDatabase` / `.Down(` callers: 0. Impacted product rows: 0.

OS branch witnesses: the only changed path is investigation markdown. No production factory, OS branch, or startup path. No witness declaration. Input-wait behavior is unchanged.

Performance: no new test. Guard wall time 1m47s. No positive controls were defined or run. PCs stay pending.

`ordinaryScopeCompleted` is None: there is no checkpoint list, four named guard types are absent, and the executed guard batch is not green. `reviewedSourceClean` is false: there is no SHA-validated checkpoint receipt, and the guard TRX is not clean.

## Advisory

The investigation embeds two `pwsh` commands that resume deletion through the external script. They are not product code and were not run. They do not hold a separate code repair of this doc. The inherited lane-guard red is likewise not a CARD-0669 repair.

Proceeded under the brief's 2026-11-10 instruction that reviews run on Grok.
