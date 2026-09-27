# CARD-0550 Windows Code verification (task f5d7a783)

Continuation of Code task `ef7c4ee0` (Linux round: Unit lane, CP-2..CP-8) on this Windows/ConPTY
host (`DESKTOP-KTLKPIF`). Source under test: S1-S3 as committed in `8e680778`, rebased unchanged onto master
`39623574` (tree-identical, only the plan add/add resolved to the branch copy); native run at `339c9e74`.
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

## Native checkpoints CP-8..CP-24 (run `20260927-203501-9ef5`, commit `339c9e74`)

Tool deviation: master's checkpoint tool (`8822e114`) crashes with exit 6 before its first build on this
host. The runner broker's grant carries `"renewEverySeconds": null` and `BuildSlotClient.ReadInt` throws
on a JSON null (CARD-0781). The rows were run by the same tool built from `8822e114^` (`d129a25a`),
whose only difference is the missing renewal. Runs `20260927-203053-a5e9`, `-203227-44af` and a third
instrumented run are the crashes. Another task's native `AgentTaskLandDeliveryE2ETests` run
(`card-task-49e32a6e`) overlapped this whole run.

| CP | Rows | Result | Notes |
|---|---|---|---|
| CP-8 | V-9 probe | 1/1 | |
| CP-9 | V26 | 1/1 (1:51) | V-1 fixed on native |
| CP-10 | C488 V26 aliases | 2/2 | |
| CP-11 | V22, V28, V31 | 2/3 | V28 red at `:154` completion-scan evidence (INHERITED) |
| CP-12 | V23, V25 | 2/2 | |
| CP-13 | V24 | 1/1 | |
| CP-14 | V30 x2 | 2/2 | |
| CP-15 | V27, C488 lost-flush alias | 1/2 | alias of V28, same red (INHERITED) |
| CP-16 | V32 x2 | 2/2 | |
| CP-17 | V29 x6 | 6/6 | |
| CP-18 | C498 x2, C488 enqueue-failure | 3/3 | |
| CP-19 | three C488 rows | 3/3 | |
| CP-20 | two C488 rows | 2/2 | |
| CP-21 | two C488 rows | 2/2 | |
| CP-22 | five C540 rows | 4/5 | `C540_CollapsedWarningsWaitForBusyCaller` receipt timeout, quiet 60 s, protocol-git 28 constant (INHERITED) |
| CP-23 | four C540 rows | 3/4 | `C540_QueueInsertCrashReusesRows` receipt timeout, quiet 60 s, protocol-git 31 constant (INHERITED) |
| CP-24 | C540 post-prompt | 1/1 | |

Totals: 41 executed, 37 passed, 4 failed. No satisfied protocol wait renewed. The longest was 38.6 s,
and each land now makes 104 protocol git calls (master's CARD-0459 EvidenceGit cache), not 740. The two
`renewed: true` wait records are the two C540 timeouts, whose count never moved. The record's
`renewed = elapsed > quietSeconds` is also true for a plain timeout at 60.1 s.

Baseline and bisect (`C:\Antiphon\.antiphon\c550-baseline\`, one sample each, same host):

| Commit | V28 | C540 busy | C540 queue-insert |
|---|---|---|---|
| `39623574` master tip, no CARD-0550 diff | red, same `:154` assertion | red, receipt timeout | red, receipt timeout |
| `a16d82d7` completion backstop | red, receipt timeout | green | red |
| `c67eab7a` before the backstop series | green | green | green |

All four reds are inherited from master and are filed as CARD-0782. They are not caused by D-1 or D-4.
D-1 is not on V28's path, and D-4 can only lengthen a wait.
