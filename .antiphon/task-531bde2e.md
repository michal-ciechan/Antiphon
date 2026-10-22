# CARD-1462 S4 (G1 + G2) Final Review: task 531bde2e

Subject: Code task 2620772f-e433-4658-8337-bebaa522d05b, `feat/card-task-2620772f` at
`2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a` (code tip `776148cdd`, red `0dab512d8`, base `a38e1e1bd`).
Verdict: **clean**. No regression of land behaviour, no fail-open, every new test can go red.

## Receipts at the reviewed SHA (checkpoint tool, server2, run `20261022-095829-fbb4`)

Manifest `.antiphon/r531/review.yaml` (ignored): the plan's CP-12 row byte-for-byte, CP-13 split by method exactly as the
Code task did (CP-13a = the 11 guard methods not red at base, CP-13b = the other three classes), one build `bin-c1432-s5a`.

    CHECKPOINT CP-12 commit=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a build=ok filter=/*/Antiphon.Tests.Infrastructure/(LandingGitBatchingTests*)|(LandingGitTests*)|(LandingGitProfileTests*)/* executed=90 passed=90 failed=0 skipped=0 ... slot=granted waited=135s dirty=0 source=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a sourceState=clean buildSource=verified
    CHECKPOINT CP-13a commit=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a build=reused filter=/*/Antiphon.Tests.Application/LandingProtocolGuardTests/(C475_Task_verification_filter*)|(C475_Source_head*)|(C475_Source_common*)|(C475_Source_admin*)|(C475_Source_registered_path*)|(C475_Source_rejected*)|(C475_TargetSymbolicQueryFailureRefuses*)|(C475_TargetRegistrationAuthority*)|(C475_RemoteErrorDoesNotPublish*)|(C475_PushExitDoesNotConfirmPublication*)|(C475_CleanupPinsAreFresh*) executed=17 passed=17 failed=0 skipped=0 ... waited=195s dirty=0 source=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a sourceState=clean buildSource=verified
    CHECKPOINT CP-13b commit=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a build=reused filter=/*/Antiphon.Tests.Application/(LandingProtocolHarnessTests*)|(LandRefRetentionTests*)|(TaskProgressGitTests*)/* executed=66 passed=66 failed=0 skipped=0 ... waited=180s dirty=0 source=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a sourceState=clean buildSource=verified
    verdict: GREEN exit=0   unlisted: none   outputs: deleted bin-c1432-s5a/

- `validate --rows CP-12,CP-13a,CP-13b` (dll) and `scripts/validate-checkpoint-receipt.ps1`: both
  `CHECKPOINT SOURCE VALID source=2cc9ee2bb9040e99f3ef8b87f2a0b5659378c55a rows=3`, exit 0. Tree clean.
- CP-11 red not rerun (recorded by the Code task at `0dab512d8`: `show-ref --verify` 4 vs 1); the test body's assertions
  (histogram == 1, processes == 1, exact values, partial-read keys) are concrete and went red for the stated reason.
- The six `LandingProtocolGuardTests.C475_Task_*` methods were excluded by method, not rerun here. The Code task's
  base proof (6/6 red at `a38e1e1bd`, same actual reasons as the tip) is credible: the coordinate change during the
  verifier barrier is caught by checks before the recheck, which S4 does not touch. CARD-1463 merge check: not run
  (optional; budget).

## Unlisted builds/runs (reasons)

1. `build-slot.ps1 -Label r531-cptool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-r531/`: the tool
   for `import`, `start`, `wait`, `validate`. Deleted.
2. Throwaway mutation, separate detached worktree (scratchpad, removed): `build-slot.ps1 -Label r531-mut -- dotnet build
   tests/Antiphon.Tests --property:OutputPath=bin-r531mut/`, then `-Label r531-mutrun -- dotnet run --no-build ...
   --treenode-filter "/*/Antiphon.Tests.Infrastructure/(LandingGitBatchingTests*)|(LandingGitTests*)/(C1432_pin_create*)|(C1432_moved_or_missing*)|(C448_V30_RecoveryPinCannotOverwriteAnotherCommit*)"`.
   Mutations: (M1) `PinWithPrefixAsync` read-back returns success when the ref exists at any sha; (M2) the protocol
   treats a branch absent from the batch as equal to the expected sha (no single-ref re-read).
   Result `total: 4 failed: 3 succeeded: 1`: `C448_V30_RecoveryPinCannotOverwriteAnotherCommit` and
   `C1432_pin_create_refuses_existing_ref_with_other_sha` (expected `recovery_ref_collision`), and
   `C1432_moved_or_missing_branch_keeps_its_reason(missing, source_ref_missing)` (got `verification_tracked_tree_changed`:
   a later check still refuses, so M2 is not fail-open, but the reason changes and the test catches it). The `moved`
   case stayed green, as expected (M2 does not touch it). Green counterpart = CP-12 at the same SHA.

## (1) Recheck semantics: truth table, base vs tip

Base: per pin in order `show-ref --verify --hash <pin>` -> `recovery_pin_changed` on any failure or mismatch; then
`ReadBranchAsync` (`show-ref --verify --hash`, on failure `show-ref --exists`: 2 -> `source_ref_missing`, else
`source_ref_error`); a different sha -> `source_changed`.
Tip: one `show-ref --verify <pins...> <branch>`, pins first; any pin absent or different -> `recovery_pin_changed`;
branch absent -> the same `ReadBranchAsync` alone; different -> `source_changed`.

| Case | Base | Tip |
|---|---|---|
| all good | pass | pass |
| any pin missing / broken / corrupt / other sha (any position) | recovery_pin_changed | recovery_pin_changed (batch stops at it; later entries absent too) |
| pin bad and branch missing/moved | recovery_pin_changed | recovery_pin_changed (pins judged first) |
| pins good, branch missing | source_ref_missing | source_ref_missing (re-read alone) |
| pins good, branch broken/unreadable | source_ref_error | source_ref_error (re-read alone) |
| pins good, branch moved | source_changed | source_changed |
| identity recheck (PushStarted, no branch) with 0 pins | 0 processes | 0 processes (empty request spawns nothing) |

git 2.47.3 probe (scratch repo): `show-ref --verify a b c master` with b missing, b pointing at a missing object, and b
corrupt: each prints only `a`'s line and exits 128 (`not a valid ref` / `bad ref`). So the batch identifies the failing
ref by absence and nothing after it is reported. The parser keeps only lines whose sha is an oid and whose name is one
of the requested refs. Production has no other `ILandingGit` implementation (only `LandingGit` and its subclass
`TaskProgressGit`); fakes get the per-ref default, which keeps their command shapes.

## (2) Create-first pinning (`LandingGit.PinWithPrefixAsync`)

Precedence is unchanged: `invalid_recovery_identity` -> `invalid_recovery_ref` -> (on create refusal) same sha = success,
other sha = `recovery_ref_collision` -> `recovery_ref_query_error` (not absent) -> rethrown commit fault ->
`invalid_recovery_commit` -> git's own refusal. The commit-check fault is `IOException` (incl. `LandingGitCommandException`)
and is deferred past the read-back, so a same-sha repeat with a faulting commit check still succeeds, as at base.
The expected-old-zero `update-ref` never overwrites: it refuses an existing ref ("reference already exists") and a broken
ref ("reference broken"), both probed. A lock failure on an absent ref returns git's result, as at base. The 4-argument
`update-ref <ref> <sha> <zeros>` shape is unchanged (it was base's last step), so `BeforeCommand` fault hooks and the
journal/profile seams see the same command; `C1432_pin_create_refuses_existing_ref_with_other_sha` pins the exact args.

## (3) `TaskProgressGit.PinBaselineAsync`

Same create-first shape: create ok -> pinned; refused -> read-back same = pinned, other = `recovery_ref_collision`,
unreadable = `pin_failed`. Base mapped every case to the same result. Still runs under its own repository lease when the
dispatcher no longer holds R (CARD-1430 S3). CARD-1446 retention deletes progress pins only for terminal, unprotected
tasks with an old-value-checked transaction. A delete landing between a refused create and the read-back gives
`pin_failed` (fail-closed). Base had the reverse window: a read-back success followed by the delete reported a pin
that no longer existed. The window is not reachable for a task being dispatched. Callers ignore the result
(`AgentTaskDispatcher.cs:7666-7673`).

## (5) Process-count claim

"About -29 per land" against the plan's -42 is reported honestly and explained: the branch read was already one
process on success (the plan counted 2), and a pin is 5 -> 3, not 4 -> 1, because `check-ref-format` stays until S6 and
the commit check stays (`update-ref` accepts a blob for a non-branch ref). Both deltas are measured by the tests
(G1 4 -> 1, G2 trace of 3 commands).

## Advisory (not defects under the verdict policy)

- A1: no test covers `PinBaselineAsync`'s same-sha repeat or `recovery_ref_collision` path (untested at base too;
  `TaskProgressGitTests` only pins the success path). A cheap follow-up test would close it.
- A2: the plan's test name `C1432_missing_branch_reports_source_changed` was renamed to
  `C1432_moved_or_missing_branch_keeps_its_reason`, correctly, because base says `source_ref_missing`. A later plan
  touch should align V-6's wording.

## Landability

`git fetch origin master` -> `aa7a691ba961f8d4e7eea2562f6dfacfcf1b235e`; `git merge-tree --write-tree origin/master
2cc9ee2bb` exit 0 (clean). `scripts/check-evidence-diff.ps1 -BaseRef a38e1e1bd -HeadRef 2cc9ee2bb`: `violations=0`.
Master only; the rc pick waits for the CARD-1427/1428/1432 chain plus 6ee09ad07/03686bed4 (per the Code report).
