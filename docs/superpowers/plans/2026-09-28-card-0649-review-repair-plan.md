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
