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
| CP-2 | S1 | CP-1 | c1449-publication | `/*/*/AgentTaskLandPublicationTests/(C494_*)\|(C448_*)\|(C665_*)\|(C642_*)\|(C688_LandNeverRunsGitInTheSourceWorktree*)\|(C688_RebaseAndVerifyRunInTheLandWorktree*)\|(C688_PushPrecedesCanonicalAdvance*)\|(C688_DirtyCanonicalCheckoutLandsWithResidue*)\|(C688_TargetCheckedOutElsewhereIsResidueNotRefusal*)\|(C688_Repair_AlreadyCanonicalRecordsLocalIdentity*)\|(C688_LocalMasterBehindOriginRebasesOntoOrigin*)\|(C688_LocalMasterAheadOfOriginRefuses*)` | V-2, R-2 | 23 methods (56 cases), 0 failed/skipped | 56 | 20 |
| CP-3 | S1 | CP-1 | c1449-publication-skip | `/*/*/AgentTaskLandPublicationTests/C688_Repair_UnknownLinkedHeadPreventsCanonicalAdvance` | R-2 | 7 cases: 6 passed, 1 skipped (`locked`, Windows-only); documented non-receipt row | 6 | 4 |
| CP-4 | S1 | CP-1 | c1449-refused-retry | `/*/*/AgentTaskLandRefusedRetryTests/(RR_V1*)\|(RR_V2*)\|(RR_V3*)\|(RR_V4_TargetStatusFailureRetriesFromFreshEvidence*)\|(RR_V4_RemoteContainmentAfterRefusalKeepsAlreadyPresentShortcut*)\|(RR_V5*)\|(RR_V6*)\|(C688_RealLandProfile*)` | V-3, R-3 | 11 methods (14 cases), 0 failed/skipped; `RR_V4_RemoteReadFailureRetriesUnchangedSource` is CP-5 | 14 | 12 |
| CP-5 | S1 | `tests/Antiphon.Tests -> bin-c1449-red/` | c1449-inherited-red | `/*/*/AgentTaskLandRefusedRetryTests/RR_V4_RemoteReadFailureRetriesUnchangedSource` | documents the inherited red below | 1 executed, 1 failed (`_pins should not be empty`, `Scenario.RefuseAsync` line 516); documented non-receipt row, red at base e1e3beb5c | 1 | 8 |

CP-1 to CP-4 are one checkpoint-tool run with one build; CP-5 runs alone in a second run, because a
red row keeps its build output and that refuses every receipt of its run. CP-2 and CP-3 together are the
whole `AgentTaskLandPublicationTests` class; CP-3 is separate only because its `locked` row calls
`Skip.Test` off Windows and a skipped case refuses the receipt. CP-4 and CP-5 together are the whole
`AgentTaskLandRefusedRetryTests` class. `Min` is a floor; confirm `executed=`
in each CHECKPOINT line.

## Inherited red found by the first CP-4 run (not this task's)

`RR_V4_RemoteReadFailureRetriesUnchangedSource` fails in its refusal setup, `Scenario.RefuseAsync`
(`_pins.ShouldNotBeEmpty()`, AgentTaskLandRefusedRetryTests.cs:516): after the `source_remote_unreadable`
refusal the operation's recovery pins are gone. That assertion precedes the mode assertion (line 597) that
has been red since f612cbe60, so no run on master had reached it. It fails identically at this task's base
e1e3beb5c (detached worktree, isolated `bin-c1449base/` build through `scripts/build-slot.ps1`, one method
filter), and it passed line 516 at 55b706761 (the investigation's base, where the case failed at line 597),
so it comes from a master commit between those two. The land-path commits in that range are CARD-1432 S2
(6cd9317a0 `ReleaseLandRefsAsync`/`LandRefRetention`, ee62ec013 keeps latest and Refused pins), CARD-1436
(5403b5f77, cd49eb98f) and the CARD-1421 lane move (6922ec628); the retention path is the first suspect.
Separate card; this task does not touch it.

### Cost

Ordinary floor 46 minutes (10 + 20 + 4 + 12, rows overlapping two at a time) plus the checkpoint
tool bootstrap build and the rc/2026-10-20 scratch cherry-pick check recorded in the Code report.

## rc/2026-10-20 check (Code report records the outcome)

In a discarded scratch worktree: cherry-pick the S1 commit onto `origin/rc/2026-10-20` and report
conflicts. The RR relabel is on rc as cherry-pick 20c38faa8, so the rc tip needs the product change.
