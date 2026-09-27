# CARD-0550 Windows Code verification (task f5d7a783)

Continuation of Code task `ef7c4ee0` (Linux round: Unit lane, CP-2..CP-8) on this Windows/ConPTY
host (`DESKTOP-KTLKPIF`). Source under test: `8e680778` (S1-S3, unchanged by this round).
Plan: [2026-09-17-card-0550-native-land-check-v26-evidence-and-barrier-plan.md](../superpowers/plans/2026-09-17-card-0550-native-land-check-v26-evidence-and-barrier-plan.md).

## Red-first evidence for the new Linux-round cases

Scratch mutations in the working tree, never committed; each build was
`tests/Antiphon.Tests -> bin-c550rf/` through `scripts/build-slot.ps1`, each run method- or
class-scoped. This proves the new cases can go red against the production line they guard. It does
not discharge any PC: every PC-1..PC-15 stays pending for SourceLanding Mutation.

| Batch | Scratch mutation | Filter | Result |
|---|---|---|---|
| R | `AgentTaskLandNotificationService` pre-link: delete the D-1 `queue-existing-key` `ReachedAsync` (today's pre-fix tip); `SessionMessageQueueService` keyed match arm: `onCreated?.Invoke(existing.Id);` replaced by `throw new ConflictException(...)` | `/*/*/AgentTaskLandNotificationRecoveryTests/C550*` | 3 executed, 3 failed: `C550_Keyed_enqueue_…` at `Should.NotThrowAsync` (threw); `C550_Recovery_prelink_…(False)` at `boundary.Reached.ShouldBe(…)`; `(True)` at `saved.EnqueueAttempts` (observer never ran, so no failure was recorded) |
| R restored | none (server restored) | same | 3 executed, 3 passed |
| A | `ProgressAwareWait`: delete `deadline = renewed < cap ? renewed : cap;` and delete `if (progress is null) throw new TimeoutException(evidence);` | `/*/*/ProgressAwareWaitTests/*` | 6 executed, 4 failed: `Growing_…` at `steps` (600 not 6000), `Progress_that_stops_…` at `RunAsync` count, `A_slow_but_progressing_…` at `IsCompletedSuccessfully`, `Without_a_progress_probe_…` at `error.Message`; `Constant_…` and `A_satisfied_…` green |
| B | `ProgressAwareWait`: `var deadline = progress is null ? start.AddSeconds(seconds) : cap;` and the predicate check moved after the poll delay | same | 6 executed, 3 failed: `Constant_…` at `RunAsync` count, `A_satisfied_…` at `RunAsync` count (5 not 4), `A_slow_but_progressing_…` at `steps`; the other three green |
| restored | none | same | 6 executed, 6 passed |

Every one of the six V-5 cases and all three V-2/V-7 cases went red under at least one mutant.
TRX: `C:\Antiphon\.antiphon\c550-redfirst\{recovery-red,waitA,waitB,restored}\`.
