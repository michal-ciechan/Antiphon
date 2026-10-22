# CARD-1449 / CARD-1447: land-protocol base reds (RR mode relabel, C475 lease anchor, C688 inventory)

Date: 2026-10-22. Code task fa175d9b (Final round; minimal testing: no Unit lane; the brief's
decisions are operator-standing recommended actions). Investigation:
[2026-10-22-card-1449-land-protocol-base-reds.md](../../investigations/2026-10-22-card-1449-land-protocol-base-reds.md)
(Investigate task 1a07bb3b, landed on master e1e3beb5c60c44335c0cb4482d78e966adf1d1de, this task's base).

Thirteen cases are red on master and on rc/2026-10-20 (tip ceb6e3d6b78471db6831258699c493b8eb2d619b):
two stale tests (C475 x4, C688 x1) and one low-severity product regression in land reporting (RR x8).

## Slice S1 (one commit group)

1. **Product, one branch.** `server/Application/Services/AgentTaskLandingProtocol.cs`, `RunCoreAsync`,
   the existing-operation branch: relabel `Mode = ResumePublication` only when `ticket is null`. The
   ticketed resume pass after the verification window re-enters with the operation this same run
   created (Fresh) or already relabelled on its fresh entry, so it keeps that mode. Every intended
   relabel stays: a fresh entry (`RunAsync`/`PrepareAsync`, no ticket) that finds an operation
   mid-flight (SourcePinned or past Inspected) still relabels before the window, and the window
   re-entry cannot undo it. Readers audited before the change: the service tests only
   `CleanupRetry`, writes `mode=` into the terminal detail and copies it to `LandingMode`; the monitor
   copies `operation?.Mode`; no test asserts `ResumePublication` for a windowed fresh land (every
   ResumePublication fixture enters without a ticket after a PushStarted fault, and
   `AgentTaskPipelineStatusTests` sets the enum directly); the client types carry the enum names only.
   `docs/orchestration-loop.md` gains one sentence in the CARD-1379 paragraph.
2. **Test.** `tests/Antiphon.Tests/Application/LandingProtocolHarnessTests.cs`
   `C475_LandingKeepsLeaseThroughProtocol`: the protocol alone receives an observing
   `IRepositoryMutationLease` wrapper (registered through `h.ConfigureServices` with
   `ActivatorUtilities.CreateInstance`) that records the land common-directory query count at each
   protocol `Owns` check. The contender probes at every land query until the protocol's first
   ownership check; the probe stored at the query that check reads is the protocol's own query
   (`CommonDirectoryAsync` then `Owns` at protocol entry) and must see the lease held. A second
   assertion requires that an earlier probe took the free lease, so a dead probe cannot pass. The two
   guarded-cleanup inspection probes are unchanged. No absolute query index remains.
3. **Test.** `tests/Antiphon.Tests/Application/AgentTaskLandPublicationTests.cs`
   `C688_LandNeverRunsGitInTheSourceWorktree`: the pre-cleanup source inventory is the exact ordered
   list admission index-lock probe, git-directory read, then CARD-1379 ticket validation (index-lock
   probe, sequencer probe, `status --porcelain=v1 -z --untracked-files=no`), plus a read-only-verb
   guard; the `Inspections == 2` profile assertion stays. No `--no-optional-locks` change here
   (separate card).

## Verification design

Red witnesses: the existing reds. One unlisted narrow run per class in the investigation (its
filters), red at 55b706761 (CARD-1432 base), at rc tip ceb6e3d6b and at the first-bad commits
e4da91c65 (C475) and f612cbe60 (C688, RR); master e1e3beb5c differs from those commits on none of
the cited lines. This task does not rerun them.

| ID | Invariant | Evidence |
|---|---|---|
| V-1 | The land holds the repository mutation lease at the protocol's own first query and through both guarded-cleanup inspections, in all four modes; a contender can take the lease before the land acquires it. | CP-1 (`C475_LandingKeepsLeaseThroughProtocol` x4) |
| R-1 | No other `LandingProtocolHarnessTests` case changes outcome (the wrapper is registered only inside V-1's test). | CP-1 (whole class) |
| V-2 | Before cleanup the task worktree sees exactly the five read-only commands above, in order, and nothing mutating; the guarded cleanup still makes the only two inspections. | CP-2 (`C688_LandNeverRunsGitInTheSourceWorktree`) |
| R-2 | Every other `AgentTaskLandPublicationTests` case keeps its outcome; `C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance(locked)` self-skips off Windows and is a documented non-receipt row. | CP-2, CP-3 |
| V-3 | A fresh land that passes through the verification window settles with `Mode = Fresh` and `LandingMode = Fresh` on the terminal event; the eight RR cases pass their replacement and later success assertions. | CP-4 (RR_V1, RR_V3, RR_V4 x3, RR_V6 x4) |
| R-3 | Resumed operations keep `ResumePublication`; CleanupRetry and the profile budget are unchanged. | CP-1 (ResumePublication/CleanupRetry rows of the C475 matrices), CP-4 (`C688_RealLandProfile`, RR_V2, RR_V5) |

Positive controls, all pending for method-scoped SourceLanding Mutation (none run here):

| PC | Mutant | Red test |
|---|---|---|
| PC-1 | Drop the `ticket is null` guard (restore the unconditional relabel) | `AgentTaskLandRefusedRetryTests.RR_V1_TargetRepairWithUnchangedSourceCreatesFreshOperation` |
| PC-2 | Add a sixth source-worktree command before cleanup (a second `status` in `ValidateTicketAsync`) or make one mutating | `AgentTaskLandPublicationTests.C688_LandNeverRunsGitInTheSourceWorktree` |
| PC-3 | Release the mutation lease before `PrepareAsync` (or let `Owns` succeed without the OS lock) | `LandingProtocolHarnessTests.C475_LandingKeepsLeaseThroughProtocol(Fresh)` |

Lane: portable .NET/PostgreSQL on the eligible default runner (server2). No `-Runner`, no `-Platform`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1449/` | c1449-harness | `/*/*/LandingProtocolHarnessTests/*` | V-1, R-1, R-3 | all 9 methods (18 cases), 0 failed/skipped | 18 | 10 |
| CP-2 | S1 | CP-1 | c1449-publication | `/*/*/AgentTaskLandPublicationTests/(C494_*)\|(C448_*)\|(C665_*)\|(C642_*)\|(C688_LandNeverRunsGitInTheSourceWorktree*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)\|(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)\|(C688_LocalMasterAheadOfOriginRefuses*)` | V-2, R-2 | 22 methods (44 cases), 0 failed/skipped | 44 | 20 |
| CP-3 | S1 | CP-1 | c1449-publication-skip | `/*/*/AgentTaskLandPublicationTests/C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance` | R-2 | 7 cases: 6 passed, 1 skipped (`locked`, Windows-only); documented non-receipt row | 6 | 4 |
| CP-4 | S1 | CP-1 | c1449-refused-retry | `/*/*/AgentTaskLandRefusedRetryTests/*` | V-3, R-3 | all 12 methods (15 cases), 0 failed/skipped | 15 | 12 |

The four rows are one checkpoint-tool run with one build. CP-2 and CP-3 together are the whole
`AgentTaskLandPublicationTests` class; CP-3 is separate only because its `locked` row calls
`Skip.Test` off Windows and a skipped case refuses the receipt. `Min` is a floor; confirm `executed=`
in each CHECKPOINT line.

### Cost

Ordinary floor 46 minutes (10 + 20 + 4 + 12, rows overlapping two at a time) plus the checkpoint
tool bootstrap build and the rc/2026-10-20 scratch cherry-pick check recorded in the Code report.

## rc/2026-10-20 check (Code report records the outcome)

In a discarded scratch worktree: cherry-pick the S1 commit onto `origin/rc/2026-10-20` and report
conflicts. The RR relabel is on rc as cherry-pick 20c38faa8, so the rc tip needs the product change.
