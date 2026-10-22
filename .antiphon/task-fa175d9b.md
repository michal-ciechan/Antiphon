# Code task fa175d9b: CARD-1449 / CARD-1447 land-protocol base reds

Branch `feat/card-task-fa175d9b`, worktree `/work/worktrees/task-fa175d9b` (server2 runner mirror), base
`e1e3beb5c60c44335c0cb4482d78e966adf1d1de` (master at dispatch). Tested SHA for every receipt below:
`7040c83fafaaeb54bbf26b59d04b10eb2009add8`. Plan:
`docs/superpowers/plans/2026-10-22-card-1449-1447-land-protocol-reds-plan.md`. Investigation:
`docs/investigations/2026-10-22-card-1449-land-protocol-base-reds.md` (task 1a07bb3b).

## Outcome

All three decisions implemented; 12 of the 13 base reds are green at the tested SHA with validated receipts.
The 13th, `RR_V4_RemoteReadFailureRetriesUnchangedSource`, is still red for a *different*, inherited reason
(recovery pins gone after refusal; red at this task's base e1e3beb5c, before any change here) and is recorded
as the documented non-receipt row CP-5. No `--no-optional-locks` change (separate card, as briefed).

## Commits (all pushed; fast-forward from e1e3beb5c)

| SHA | Content |
|---|---|
| `20fd814360d6d0a9ee18c571022dd1a2f2057624` | S1: protocol one-branch fix, C475 re-anchor, C688 exact inventory, plan, one docs sentence |
| `a4b5e15dc6ce6dcd71f21e8ad99795f1966f00a4` | compile fix found by CP-1's build: Shouldly string-collection `ShouldBe` takes `Case` before the message |
| `315572a75cec51bf6a50f61b20f9410670ec6b47` | plan amendment: CP-2 actual roster, CP-4 narrowed, CP-5 added for the inherited red |
| `7040c83fafaaeb54bbf26b59d04b10eb2009add8` | plan: inherited-red section with the base proof (tested SHA) |
| report commit (tip) | this file plus the CP-2 "23 methods" correction in the plan |

## What changed

1. **Product (CARD-1449, RR x8).** `server/Application/Services/AgentTaskLandingProtocol.cs` ~L190-195: the
   existing-operation branch relabels `Mode = ResumePublication` only when `ticket is null`. The ticketed
   resume after the verification window re-enters with the operation this run created, so a fresh land now
   settles `Fresh` on its durable `Mode`, terminal `mode=` detail and `LandingMode`. Intended relabels kept: a
   fresh entry (no ticket) that finds an operation mid-flight (SourcePinned or past Inspected) still relabels,
   and the ticketed re-entry cannot undo it. Mode readers audited (grep `LandOperationMode.`/`.Mode`): service
   L746/L776 test only `CleanupRetry`, L825 `mode=` detail, L1469 `terminal.LandingMode`; monitor L97;
   `LandingEvidenceDto.From`; client types list the enum names only; tests assert `ResumePublication` nowhere
   for a windowed fresh land (ResumePublication fixtures enter ticket-less after a PushStarted fault;
   `AgentTaskPipelineStatusTests` sets the enum directly). `docs/orchestration-loop.md` CARD-1379 paragraph:
   one sentence. rc/2026-10-20 carries the regression (20c38faa8) and needs this fix.
2. **C475 (CARD-1447, stale test).** `LandingProtocolHarnessTests.C475_LandingKeepsLeaseThroughProtocol`:
   only the protocol receives a forwarding `ProtocolLeaseObserver` (`h.ConfigureServices` +
   `ActivatorUtilities.CreateInstance<AgentTaskLandingProtocol>(sp, observer)`), which records the land
   common-directory query count at each protocol `Owns`. The contender probes at every land query until the
   protocol's first ownership check; the probe stored at the query that check reads (protocol L74, then
   `Owns` L75) must be refused, and an earlier probe must have taken the free lease (dead-probe guard). The
   source resolver also does query-then-`Owns` (AgentTaskLandSourceResolver.cs:53-54) before the protocol in
   Fresh/AlreadyPresent, which is why the wrapper is protocol-only rather than a first-`Owns` anchor. The two
   guarded-cleanup inspection probes are unchanged. No absolute query index remains.
3. **C688 (CARD-1449, contract change).** `AgentTaskLandPublicationTests.C688_LandNeverRunsGitInTheSourceWorktree`:
   exact ordered inventory `index.lock probe, rev-parse --absolute-git-dir, index.lock probe,
   rev-parse --absolute-git-dir, status --porcelain=v1 -z --untracked-files=no` (D-10, D-2, then CARD-1379
   `ValidateTicketAsync` L626-632), plus a read-only-verb guard; `Inspections == 2` kept and green.

## Checkpoint runs (checkpoint tool, server2, `--row-timeout 40m`, every build and row `slot=granted`)

| Run | Rows | SHA | Result |
|---|---|---|---|
| 20261022-035701-d6a8 | CP-1..4 | 20fd81436 | build failed: CS1503 in the C688 test (fixed by a4b5e15dc, same rows rerun); kept `bin-c1449` deleted by hand |
| 20261022-040139-37d6 | CP-1..4 (original CP-4 = whole RR class) | a4b5e15dc | CP-1 18/18, CP-2 56/56, CP-3 6+1 skip, CP-4 14/15 (RR_V4_RemoteReadFailure red at `_pins`); run RED, so no receipt |
| 20261022-042140-95e4 | CP-1..4 | 315572a75 | refused `pre-existing-build-source` (my cleanup chain stopped before deleting `bin-c1449`); no tests ran |
| **20261022-042255-56d5** | CP-1..4 | 7040c83fa | **GREEN**; `validate --rows CP-1,CP-2,CP-4` -> `CHECKPOINT SOURCE VALID source=7040c83fafaaeb54bbf26b59d04b10eb2009add8 rows=3`; CP-3 alone -> `row_failed` (skipped=1, Windows-only `locked` row), green-but-unqualified by design |
| **20261022-043254-7558** | CP-5 | 7040c83fa | RED as documented: 1 executed, 1 failed (`_pins should not be empty`); kept `bin-c1449-red` deleted by hand |

Unedited lines from `20261022-042255-56d5/report.md` and `20261022-043254-7558/report.md`:

```
CHECKPOINT CP-1 commit=7040c83fafaaeb54bbf26b59d04b10eb2009add8 build=ok filter=/*/*/LandingProtocolHarnessTests/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-fa175d9b/.antiphon/checkpoints/20261022-042255-56d5/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=7040c83fafaaeb54bbf26b59d04b10eb2009add8 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=7040c83fafaaeb54bbf26b59d04b10eb2009add8 build=reused filter=/*/*/AgentTaskLandPublicationTests/(C494_*)|(C448_*)|(C665_*)|(C642_*)|(C688_LandNeverRunsGitInTheSourceWorktree*)|(C688_RebaseAndVerifyRunInTheLandWorktree*)|(C688_PushPrecedesCanonicalAdvance*)|(C688_DirtyCanonicalCheckoutLandsWithResidue*)|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)|(C688_LocalMasterAheadOfOriginRefuses*) executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/task-fa175d9b/.antiphon/checkpoints/20261022-042255-56d5/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=7040c83fafaaeb54bbf26b59d04b10eb2009add8 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=7040c83fafaaeb54bbf26b59d04b10eb2009add8 build=reused filter=/*/*/AgentTaskLandPublicationTests/C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance executed=6 passed=6 failed=0 skipped=1 trx=/work/worktrees/task-fa175d9b/.antiphon/checkpoints/20261022-042255-56d5/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=7040c83fafaaeb54bbf26b59d04b10eb2009add8 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=7040c83fafaaeb54bbf26b59d04b10eb2009add8 build=reused filter=/*/*/AgentTaskLandRefusedRetryTests/(RR_V1*)|(RR_V2*)|(RR_V3*)|(RR_V4_TargetStatusFailureRetriesFromFreshEvidence*)|(RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut*)|(RR_V5*)|(RR_V6*)|(C688_RealLandProfile*) executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-fa175d9b/.antiphon/checkpoints/20261022-042255-56d5/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=7040c83fafaaeb54bbf26b59d04b10eb2009add8 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=7040c83fafaaeb54bbf26b59d04b10eb2009add8 build=ok filter=/*/*/AgentTaskLandRefusedRetryTests/RR_V4_RemoteReadFailureRetriesUnchangedSource executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-fa175d9b/.antiphon/checkpoints/20261022-043254-7558/rows/CP-5/run.trx slot=granted waited=30s dirty=0 source=7040c83fafaaeb54bbf26b59d04b10eb2009add8 sourceState=clean buildSource=verified
```

Rosters inspected from the fresh TRX: CP-1 all 9 methods / 18 cases (the four C475 lease modes included);
CP-2 23 methods / 56 cases (the plan's first census missed `C665_CleanupDetailSurvivesNotificationRecoveryAndCompleteCallerPrompt`,
12 cases; no roster miss; plan corrected); CP-3 + CP-2 = the whole `AgentTaskLandPublicationTests` class
(63 cases); CP-4 + CP-5 = the whole `AgentTaskLandRefusedRetryTests` class (15 cases). The eight RR mode
cases pass their replacement and success assertions (V-3); `C688_RealLandProfile` budget unchanged (R-3).

Repeat accounting: CP-1, CP-2, CP-3 executed green twice (run 040139 and 042255; the rerun was needed
because run 040139's red CP-4 kept its build output, which refuses every receipt of that run). Original
whole-class CP-4 once (14/15); narrowed CP-4 once; CP-5 once, plus two one-method diagnostic executions below.

## Unlisted builds and runs (each with its reason)

- Tool bootstrap: `build-slot.ps1 -Label c1449-tool-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1449-tool/` (granted, waited 0s, 6s).
- Classification of the CP-4 red at a4b5e15dc: `build-slot.ps1 -Label c1449-rrv4-diag -- dotnet exec tests/Antiphon.Tests/bin-c1449/Antiphon.Tests.dll --treenode-filter /*/*/AgentTaskLandRefusedRetryTests/RR_V4_RemoteReadFailureRetriesUnchangedSource` against the kept row build: 1 failed, same `_pins` assertion (deterministic, not an ordering flake).
- Inherited proof at the task base e1e3beb5c: detached worktree under the session scratchpad, `build-slot.ps1 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c1449base/` (326s) and the same one-method `dotnet exec`: 1 failed, same `_pins` assertion. Worktree and its output removed.
- Red witnesses for the 13 cases: the existing reds, one narrow unlisted run per class in the investigation (filters there) at 55b706761, ceb6e3d6b, e4da91c65 and f612cbe60; master e1e3beb5c differs from those commits on none of the cited lines. Not rerun here.

## Inherited red (not this task's; needs a card)

`AgentTaskLandRefusedRetryTests.RR_V4_RemoteReadFailureRetriesUnchangedSource` fails in `Scenario.RefuseAsync`
at `_pins.ShouldNotBeEmpty()` (AgentTaskLandRefusedRetryTests.cs:516): after the `source_remote_unreadable`
refusal, `for-each-ref A.RecoveryRefPrefix/` is empty. The assertion precedes the mode assertion (line 597)
that was red since f612cbe60, so nobody had reached it on master. It is red at e1e3beb5c (proof above) and
passed at 55b706761 (the investigation saw it fail at 597 there), so it comes from a master commit in
55b706761..e1e3beb5c. Land-path commits in that range: CARD-1432 S2 `6cd9317a0` (adds
`ReleaseLandRefsAsync` -> `LandRefRetention.ReleaseAsync` after every settled land) and `ee62ec013` (keeps
latest and Refused operations' pins), CARD-1436 `5403b5f77`/`cd49eb98f`, CARD-1421 lane move `6922ec628`.
`LandRefRetention.RetainedOperationsAsync` should retain A (Refused and latest), so either the retention
deletes it anyway or the third source `ls-remote` now fires before the pins exist; not investigated further.
The other RR refusal paths keep their pins (CP-4 green). Suggested card: "Base-red: RR_V4_RemoteReadFailure
recovery pins empty after source_remote_unreadable refusal since CARD-1432 S2 (red at e1e3beb5c, green at 55b706761)".

## Source checks

- `git merge-tree --write-tree origin/master HEAD` at 20fd81436 against origin/master aebf80885: clean (tree e432eac5d). Rerun at the tip is in the final message.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef e1e3beb5c -HeadRef HEAD`: `commits=1 entries=0 violations=0` at 20fd81436; rerun at the tip in the final message.
- rc/2026-10-20 (tip ceb6e3d6b78471db6831258699c493b8eb2d619b): 20fd81436 cherry-picked clean in a scratch worktree (d34571325, discarded). The full-range check is in the final message. The rc tip needs the product change (it carries the relabel as 20c38faa8).

## Positive controls pending for method-scoped SourceLanding Mutation (none run here)

PC-1 drop the `ticket is null` guard -> `RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation` red at
`b.Mode.ShouldBe(Fresh)`. PC-2 add a sixth pre-cleanup source command or a mutating one in
`ValidateTicketAsync` -> `C688_LandNeverRunsGitInTheSourceWorktree` red on the exact list. PC-3 release the
mutation lease before `PrepareAsync` (or let `Owns` pass without the OS lock) -> `C475_LandingKeepsLeaseThroughProtocol(Fresh)`
red at the protocol-query probe. Zero-PC discovery of missing controls stays with Mutation.

## Not done / deferred

No Unit lane (brief). Windows not run (brief; no platform-conditional code on the three paths). The source
`status` without `--no-optional-locks` stays as is (separate card named in the brief). The inherited RR_V4
pins red is left for its own card. `.antiphon/checkpoints/<run>/` evidence stays on disk, gitignored.

## Housekeeping

Deleted: `tests/Antiphon.Tests/bin-c1449` (twice, after the two red/refused runs), `tests/Antiphon.Tests/bin-c1449-red`,
`tools/Antiphon.Checkpoints/bin-c1449-tool`, `src/Antiphon.HostCleanup/bin-c1449-tool`; the tool reclaimed
`bin-c1449` itself after the green run. Scratch worktrees (rc cherry-pick, base proof) removed. No daemons touched.
