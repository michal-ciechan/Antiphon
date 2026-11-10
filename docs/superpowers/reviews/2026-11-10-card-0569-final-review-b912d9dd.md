# CARD-0569 Final Review round 1 (task b912d9dd)

Verdict: CLEAN. No blockers. Positive controls stay pending for SourceLanding Mutation.

Reviewed source task: `f0176553-92ed-4455-85ef-25bbd140876c` (Code).
Reviewed tip: `bca2fa8c4bbafe7d2a0792ad6f85bc4615935357`.
Range: `a69789c77865b2db502500ebd26d58f72a8013c0..bca2fa8c4bbafe7d2a0792ad6f85bc4615935357` (test commit `2ba544aaaeca3c57389ec5d7ae57a05eb7cfd78c`, fix commit `bca2fa8c4bbafe7d2a0792ad6f85bc4615935357`).
Receipt: `.antiphon/checkpoints/20261110-204746-91d6/report.json`, `CHECKPOINT SOURCE VALID` for that SHA, rows CP-1, CP-2, CP-3.

## Wording

The four named surfaces now state the caller rule. `-IgnoreConcurrencyLimit` is only for an open population with `canOverride`, axis `absolute`, and no listed occupant in the stage being dispatched. A same-stage collision defers.

- `ConcurrencyLimitException.Coda` is that axis sentence. `FormatDetail` still appends it only when `CanOverride` is true. `DecideCreate` is unchanged: a queued refusal stays `canOverride=false`; any other exceeded open population admits when the flag is set, including a role axis. The coda is caller guidance, not a new server rejection.
- `docs/antiphon-api.md` retry sentence names `population=open`, `canOverride`, axis `absolute`, and the same-stage deferral, and still says the retry records a Warning without raising `MaxConcurrentTasks` (`AgentTaskService` writes `FormatOverrideWarning` only).
- `scripts/delegate.ps1` header matches that rule. The `-IgnoreConcurrencyLimit` parameter comment already said `A same-stage collision still defers.` The file is ASCII (0 bytes at or above 128).
- `server/Bundles/orchestrator.md` replaces only `Delegates writing the same files at once: pass -Worktree so they cannot overwrite each other.` with `Fresh workers already use a worktree; -Shared opts out of that default.` The stage line `each in its own -Worktree` and the 409 axis paragraph are unchanged. `EffectiveRequestedWorkspace` gives a fresh worker with no `-Agent` a worktree, and an explicit workspace (including `-Shared`) opts out.

`CheckpointRepeatDocumentationTests` hash is `c1ee24b37dd9497ebe099045791dfcff2101b1a46191111540885161ad524b45` over the raw bundle bytes. LF-normalised trimmed body is 11,472 UTF-16 units of the 11,500 allocation.

The unconditional `if the user asked for parallel work this turn` sentence and `create is sequential-by-default` are gone from those surfaces. `docs/orchestration-loop.md` still has CARD-0533's scoped sentence (`Dispatch is sequential-by-default at the gate` and `Elsewhere, only when the user asked for parallel work this turn`). That is the owner rule for boards outside the standing pipeline policy, not a copy of the coda this card removed. Historical plans and the 2026-10-08 triage quote the old sentence as history. `docs/features/007-multi-agent-orchestration/proposal.md` still says delegates run in the working directory by default. Advisory only; it does not hold the land.

No changed input wait. Parking stays disabled, so there is no automatic release deadline. No OS-selected production branch: the changed `.cs` and `delegate.ps1` have no OS-pattern candidate. Downgrade selector count is 0 (`impacted-classes.ps1` kinds were `spawn-limit-rosters` and `test-lane-category`; the migrations row did not match).

## Verification

Code left no `### Checkpoints` plan. This review ran the closed table below at the reviewed SHA, one leased build, slot granted, `buildSource=verified`, dirty 0.

`check-evidence-diff.ps1` over the range: commits=2 entries=0 violations=0.
`git merge-tree --write-tree origin/master HEAD` at the reviewed tip: tree `9bad4ad099a383dc3316fc0ff7976982d51ec710`, exit 0. `origin/master` is `84bc17576287fb43cf2dc04395970a8bc52e2d76`.

CHECKPOINT CP-1 commit=bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 build=ok filter=/*/*/(SequentialOverrideWordingTests*)|(CheckpointRepeatDocumentationTests*)|(OrchestratorBundleOwnershipTests*)|(StandingPipelinePolicyDocumentationTests*)/* executed=28 passed=28 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 sourceState=clean buildSource=verified

CHECKPOINT CP-2 commit=bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=78 passed=78 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 sourceState=clean buildSource=verified

CHECKPOINT CP-3 commit=bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=57 passed=57 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=bca2fa8c4bbafe7d2a0792ad6f85bc4615935357 sourceState=clean buildSource=verified

CP-2 is the nine-class guard batch (78 results). CP-3 is the impacted `ProcessSpawnLimitTests` roster; `TestLaneCategoryGuardTests` is inside CP-2. The new wording tests read files and carry an in-memory infection that must throw; that infection ran inside the 28 green CP-1 results. No separate SourceLanding positive control was run. `TestClassificationPolicyTests` took 164s inside CP-2. The run wall was 13m29s. No new timeout.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | wording | tests/Antiphon.Tests -> bin-c0569-wording/ | c0569 | `/*/*/(SequentialOverrideWordingTests*)\|(CheckpointRepeatDocumentationTests*)\|(OrchestratorBundleOwnershipTests*)\|(StandingPipelinePolicyDocumentationTests*)/*` | axis coda, API sentence, delegate header, worktree default, orchestrator hash and 11500 body | 0 failed | 19 | 15 |
| CP-2 | wording | tests/Antiphon.Tests -> bin-c0569-wording/ | c0569 | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | Code/Review guard batch including the lane census | 0 failed | 78 | 15 |
| CP-3 | wording | tests/Antiphon.Tests -> bin-c0569-wording/ | c0569 | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit roster selected by the test-file diff | 0 failed | 33 | 15 |
