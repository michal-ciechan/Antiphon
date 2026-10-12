# CARD-1033 S1 repair

Outcome: the shipped JSON null cutoff starts, and an incomplete cutoff withholds destruction. Closed checkpoint `20261012-101010-a29a` is green: 99 executed, 99 passed, 0 failed, 0 skipped.

Landing owner is this task, fbe4b0e6. Original Code task: cee24c68-22d5-47ed-929c-0f9177577f9a. Branch: feat/card-task-fbe4b0e6. Worktree: /work/worktrees/task-fbe4b0e6. Code commit verified by the checkpoint: 70e0e3ab34d4e5771c1fe7d6390676d7aae30b53. Parent: 61dc9d67e2800189ed5ea5169c40fb5a5ace1a09. Task base: c1955dd7ce6bb0e30abb3b702fe813fc27d289b7. This report commit does not change production or tests.

Plan: docs/superpowers/plans/2026-10-11-card-1033-cleanup-scheduler-native-plan.md (not edited).
Design: docs/superpowers/plans/2026-10-12-card-1033-test-design.md.
Review evidence: /work/evidence/c962f32c/review.md.
Checkpoint evidence: /work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/report.md.

The task token was present. It is not printed. No runner or platform pin. No restart was performed. Activation of this server change needs a server restart after land. Restart: server.

## Fix

`WorktreeResidueSettings.TryReadCardDoneAfterUtc` treats null, empty, and whitespace as absence (cutoff null, inventory only). The JSON provider binds the shipped `CardDoneAfterUtc: null` to an empty string, so startup validation now succeeds with `Execute=false`. Any other text must match an exact invariant format: calendar date, time to the second, and `Z`, `+00:00`, or `+0000`, with an optional fraction of at most seven digits. Parsing uses `DateTime.TryParseExact` with `DateTimeStyles.None`. It does not use `DateTime.Parse` or `DateTimeOffset.TryParse`. A timestamp that is complete but unzoned, or that carries a non-zero or negative-zero offset, fails as `must be UTC.` Other non-empty text fails as `malformed.` Both failures withhold destruction (`cutoff_invalid`). Equality remains `historical_cohort`.

Shipped `server/appsettings.json` values were not changed. `WorktreeResidueSettingsValidator.cs` was not changed; an empty value now passes because `TryRead` accepts it, and a non-empty invalid value still fails validation.

## Tests

New tests in `tests/Antiphon.Tests/Application/CardWorktreeCleanupRecoveryTests.cs`:

- `C1033_ShippedJsonNullPassesStartup` loads `server/appsettings.json` with `ConfigurationBuilder.AddJsonFile`, checks the bound empty string, startup validation, and `cutoff_absent` admission.
- `C1033_CutoffGrammarWithholdsDestruction` is one table of 27 hand-written rows through validation, exact parse, and `DestructiveAdmissionRefusal`. Complete UTC values with a later Done instant admit. Equal boundary is `historical_cohort`. Absence is `cutoff_absent`. Incomplete, ambiguous, local, and out-of-range values are `cutoff_invalid`.
- `C1033_TimeOnlyCutoffAdmitsNothing` sets `Execute=true` and raw `00:00Z` on the cleanup fixture and requires `cutoff_invalid`, the tree preserved, and no intent.

Before the production edit, those three tests ran red against the previous parser: total 3, executed 3, passed 0, failed 3, skipped 0. TRX: `.antiphon/c1033-s1-red/red.trx`. The grammar failure records empty and whitespace as `malformed.` and `00:00Z` parsed as `2026-10-12T00:00:00.0000000Z` and admitted. Slot lease fde59987-1b48-44c1-a376-545385e37e7e, waited=0s. After the parser edit, the same three passed: total 3, executed 3, passed 3, failed 0, skipped 0. TRX: `.antiphon/c1033-s1-fix/fix.trx`. Slot lease 1fd533c3-bc06-4872-a539-d5e22ce854f8, waited=0s. Those two runs are the red confirmation and the pre-commit check. They are not the closed checkpoint.

## Design table

38 OR rows in the `### Checkpoints` table of the design now end every operand with `*`. CP-1 and CP-2 were already starred. CP-3 remains the single exact method `Registry_matches_compiled_metadata` with no star and no OR. Expect, Min, Build, and minutes were not changed. One sentence in that section now says later OR rows carry the suffix on every operand. It previously said those rows still use the bare form and select zero tests. The plan file was not edited.

## Closed checkpoint

Run `20261012-101010-a29a`, exit 0, wall 20m56s. One isolated subject build `bin-c1033-s1r/` (`UseAppHost=false`, forward slash, trailing slash). The build id is not `tests`. Rows share that build because the repair brief requires one isolated build; the design's separate bins were not used. Extra rows are the brief's closed list beyond CP-1..CP-3. No whole Unit lane. `--keep-outputs` was set for the run. Project `bin-c1033-*` directories were removed after the counts below were taken. The checkpoint directory remains.

Tool bootstrap, disclosed and not a subject build: `scripts/build-slot.ps1 -Label c1033-tool`, lease 1258444b-2b8c-4570-a745-ff5d3c6ec43a, waited=0s, held=7s, output `tools/Antiphon.Checkpoints/bin-c1033-tool/`. The run then used `dotnet exec` of that DLL. The subject driver took its own slots. No driver used NoSlot.

Build slot: lease 431752e2-fedc-41e2-a8f0-bc470c09697f, label `build:bin-c1033-s1r`, waited=0s, maxcpucount=6. Every row slot=granted waited=0s.

`C459_OutageKeepsFence` was excluded by filter. It is the known inherited red at base c1955dd7c (Directory.Exists named worktree expected true was false). It was not fixed and not executed in ADJ-REC (the name is absent from that TRX). ADJ-REC is the other eight recovery methods plus the 13 `C459_WorkerDeathAtEveryRetirementHandoff` arguments: 21 results.

Receipt: `dotnet exec` validate `--evidence` report.json `--expected-source-sha 70e0e3ab34d4e5771c1fe7d6390676d7aae30b53` printed `CHECKPOINT SOURCE VALID` for 7 rows.

Unedited CHECKPOINT lines:

CHECKPOINT CP-1 commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=ok filter=/*/*/CardWorktreeCleanupRecoveryTests/(C1017_ExecuteFalseCannotDelete*)|(C1017_HistoricalCutoffCannotDelete*)|(C1017_SettlingFloorCannotDelete*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=reused filter=/*/*/(CardDoneWorktreeCleanupTests*)|(CompletedCardWorktreeRemovalTests*)/(C1017_OnlyDoneAuthorizes*)|(C1017_GenerationMustMatch*)|(C1017_TerminalTaskRequired*)|(C1017_UniqueOrdinaryOwner*)|(C1017_ReopenBeforeIntentRevokes*)|(C1017_IntentBeforeReopenKeepsFence*)|(C1017_ActiveConsumersHold*)|(C1017_RecoveryDebtHolds*)|(C1017_SnapshotAttemptIsFresh*)|(C1017_SnapshotReportIsFresh*)|(C1017_NoDbLockAcrossIo*)|(C1017_SharedAndBorrowedExcluded*)|(C1017_SourceLandingExcluded*)|(C1017_SlotAndCanonicalExcluded*)|(C1017_DiscoverPostDoneAttempts*)|(C1017_ExactCardBinding*)|(C1017_DuplicateDoneIsIdempotent*)|(C1017_DoneSaveIsAtomic*)|(C1017_EvidenceNeedsDoneDisposition*)|(C1017_DisposalFlagIsNotAuthority*)|(C1017_LeaseMustBeGenuine*)|(C1017_SourceShaMustMatch*)|(C1017_RepositoryMustMatch*)|(C1017_PathMustMatch*)|(C1017_BranchMustMatch*)|(C1017_CommonDirectoryMustMatch*)|(C1017_AdminDirectoryMustMatch*)|(C1017_DirtyTrackedIsHeld*)|(C1017_UntrackedSourceIsHeld*)|(C1017_UnpushedCommitIsHeld*)|(C1017_TaskBranchOnlyIsHeld*)|(C1017_RebaseMappingMustBeBound*)|(C1017_RemoteProofMustBeFresh*)|(C1017_ProtectedIgnoredIsHeld*)|(C1017_FinalContentIsRechecked*)|(C1017_EvidenceChurnIsHeld*)|(C1017_LinksNeverFollowed*)|(C1017_RootConfinementIsFresh*)|(C1017_NestedRegistrationIsHeld*)|(C1017_RegistrationMustExist*)|(C1017_LockedRegistrationIsHeld*)|(C1017_PrunableRegistrationIsHeld*)|(C1017_ReportMustBeExternal*)|(C1017_ReportDigestMustMatch*)|(C1017_TaskReportDetailMustSurvive*)|(C1017_DeliverablesMustBeExternal*)|(C1017_DeliverablesMustExist*)|(C1017_FinalAuthorityIsFresh*)|(C1017_BranchCasPreservesConcurrentRef*)|(C1017_OriginRefsSurvive*)|(C1017_NoImplicitResetOrForce*)|(C1017_MissingReportNeedsDisposition*)|(C1017_FirstIgnoredLinkIsHeld*)|(C1017_FinalIgnoredLinkIsHeld*) executed=54 passed=54 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=reused filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified
CHECKPOINT CP-NEW commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=reused filter=/*/*/CardWorktreeCleanupRecoveryTests/(C1033_ShippedJsonNullPassesStartup*)|(C1033_CutoffGrammarWithholdsDestruction*)|(C1033_TimeOnlyCutoffAdmitsNothing*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/CP-NEW/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified
CHECKPOINT ADJ-REG commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=reused filter=/*/*/WorktreeResidueRegistrationTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/ADJ-REG/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified
CHECKPOINT ADJ-SWEEP commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=reused filter=/*/*/WorktreeResidueSweepTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/ADJ-SWEEP/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified
CHECKPOINT ADJ-REC commit=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 build=reused filter=/*/*/WorktreeResidueRecoveryTests/(C459_SpentSlotSurvivesRestart*)|(C459_ClaimCommittedBeforeIo*)|(C459_IntentCommittedBeforeGit*)|(C459_ComponentsAreIndependent*)|(C459_RunIntentPrecedesEnqueue*)|(C459_IncompletePinsRetained*)|(C459_PinRetirementUsesCas*)|(C459_TerminalProjectionRecovers*)|(C459_WorkerDeathAtEveryRetirementHandoff*) executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-fbe4b0e6/.antiphon/checkpoints/20261012-101010-a29a/rows/ADJ-REC/run.trx slot=granted waited=0s dirty=0 source=70e0e3ab34d4e5771c1fe7d6390676d7aae30b53 sourceState=clean buildSource=verified

ADJ-REG includes `C459_OneScheduler` and `C459_DisabledSchedulerStaysOff`, the two host startup tests that failed in review, plus `C459_ScheduledWorkerExecutesBothLanes`. All three passed.

| Row | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-1 | 3 | 3 | 0 | 0 |
| CP-2 | 54 | 54 | 0 | 0 |
| CP-3 | 1 | 1 | 0 | 0 |
| CP-NEW | 3 | 3 | 0 | 0 |
| ADJ-REG | 3 | 3 | 0 | 0 |
| ADJ-SWEEP | 14 | 14 | 0 | 0 |
| ADJ-REC | 21 | 21 | 0 | 0 |
| Total | 99 | 99 | 0 | 0 |

## Publication

`scripts/check-evidence-diff.ps1 -BaseRef c1955dd7c -HeadRef 70e0e3ab34d4e5771c1fe7d6390676d7aae30b53` exited 0: commits=4 entries=0 violations=0.

`git fetch origin master` updated origin/master to d89e508b63b785470cb46b9fbf03522a74d4fd6b. `git merge-tree --write-tree HEAD origin/master` at the code commit exited 0, tree e4d1a61b324c6dd1cc36b257204fcb76be88053c.

Post-land Mutation was NOT run. All 204 PCs remain pending. No V/R outside this closed list was run. No nightly result is claimed.
