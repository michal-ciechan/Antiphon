# CARD-0649 review repair

## Decision and scope

Keep the runner brief path relative to its session cwd. The queue gives the staged
brief a row-owned inbox path, so the dispatcher sizes the wire pointer after that
replacement. Keep the CARD-0647 repeated task marker as a defensive correlation
measure. The original `aff1b9c6` loss was repaired by `7851254ea`; this repair
does not attribute that loss to a measured pointer length. The boundary length in
the regression test is deliberately constructed to exercise the 1,024-byte limit.

## Verification design

- V-1: `PtyDeliveryCeilingsTests` checks the dispatcher's runner ceiling and
  compact pointer behavior.
- V-2: `PhoneHomeTaskDispatchProjectionTests` checks that runner brief pointers
  name the row-owned relative path, with no desktop or runner absolute path.
- V-3: `DurableRunnerSpillReceiptTests` checks a boundary-sized dispatcher
  pointer through queue binding and a complete recorded UserPrompt.
- V-4: `PhoneHomeSpillTransportTests` checks the staged file through the runner
  Input frame and retry path.
- R-1: the whole Unit lane checks shared formatter and ceiling behavior.
- PC-1: method-scoped SourceLanding Mutation removes the post-binding sizing;
  V-3 must fail on a double spill.
- PC-2: method-scoped SourceLanding Mutation restores absolute runner paths;
  V-2 must fail.
- PC-3: method-scoped SourceLanding Mutation removes marker repetition;
  the join-safe marker assertion must fail. All PCs remain pending for Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
| --- | --- | --- | --- | --- | --- | --- | ---: | ---: |
| CP-1 | repair | `tests/Antiphon.Tests -> bin-c649-review/` | unit | `/*/*/*/*[Category=Unit]` | V-1, R-1 | >= 1 executed, 0 failed | 1 | 6 |
| CP-2 | repair | CP-1 | spill-integration | `/*/Antiphon.Tests.Application/(PhoneHomeTaskDispatchProjectionTests*)\|(DurableRunnerSpillReceiptTests*)\|(PhoneHomeSpillTransportTests*)/*` | V-2, V-3, V-4 | all three classes, 0 failed | 20 | 6 |

### Cost

Ordinary checkpoint floor: 12 minutes. CP-1's Unit lane and CP-2's three
named integration classes are the complete ordinary scope for this repair.

## Code checkpoint report

The first run at `b34afea4233c5bdb0fbba8e9b2d849ef9623a2ca` failed during
the CP-1 build because the new test lacked the `PtyBackend` namespace import;
both rows executed zero tests. After the import repair, the planned rows passed:

```text
CHECKPOINT CP-1 commit=74126f6bba3829d51a242057e941241c016796b5 build=ok filter=/*/*/*/*[Category=Unit] executed=3423 passed=3423 failed=0 skipped=34 trx=/work/worktrees/task-be4f9e5f/.antiphon/checkpoints/20260928-102636-c442/rows/CP-1/run.trx slot=granted waited=0s reruns=1
CHECKPOINT CP-2 commit=74126f6bba3829d51a242057e941241c016796b5 build=reused filter=/*/Antiphon.Tests.Application/(PhoneHomeTaskDispatchProjectionTests*)|(DurableRunnerSpillReceiptTests*)|(PhoneHomeSpillTransportTests*)/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/task-be4f9e5f/.antiphon/checkpoints/20260928-102636-c442/rows/CP-2/run.trx slot=granted waited=0s reruns=1
```

The runner transcript side is already pinned by the Windows-only
`SessionMessageQueueGrokPtyIntegrationTests.Multiline_delivery_is_transcript_confirmed_through_the_real_grok_tailer`:
its real fake-Grok tailer records the whole submitted multi-line prompt with
the measured newline join. The new V-3 test covers the specific CARD-0649
dispatcher and queue pointer boundary. No 1,547-byte root-cause measurement is
claimed; the regression length is constructed for the boundary.
