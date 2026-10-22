# CARD-0266 S1/S2 Code evidence — c2af4f70

Original Code task / landing owner: `c2af4f70-adb5-49de-a880-df9b27784498`.
Branch: `feat/card-task-c2af4f70`. Worktree: `/work/worktrees/task-c2af4f70`.
Task base: `06d5b0fdbe9c14ef5edc352d3a75370d5d02d964`.
Plan: `docs/superpowers/plans/2026-10-22-card-0266-plan.md` at the task base.
Round: Final, with the brief's explicit closed CP-1..CP-3 selection and minimal-testing
instruction (no whole Unit/whole-suite run). No Interim selection or deferred-to-Final IDs.
No endpoint change; `ExpectationHoldReleaseEndpointTests` remains Review's ordinary scope
as the plan explicitly assigns it. No live/manual acceptance was specified.

This report is committed before the final checkpoint run so the final pushed HEAD itself
is tested. Final CP-2/CP-3 outcomes, exact source SHA, unedited CHECKPOINT lines, receipt
qualification, merge-tree result and cleanup observations belong in the runtime-owned
supplement `/work/worktrees/task-c2af4f70/.antiphon/reports/task-c2af4f70-final.md` and
the caller's final report. They are not preclaimed here. No tracked file is to change
while those checkpoints run or after successful qualification.

## Implementation and scope

- S1: `927d65ef1f8ab0638b7819b9092e7c7e193d1a2f`, pushed. Two fused-prompt witnesses,
  four guards, rewritten sweep assertion and an empty-composer sweep twin. The runner
  helper gained a settable snapshot.
- S2: `b525ed56d8fae4d162a296c18b54acfa53ca9122`, pushed. Canceled/current-generation/
  attempted/unsent rows hold input until any later `UserPrompt` above the sequence or
  timestamp floor, a clear composer, or a new generation. The shared screen helper keeps
  CARD-0650's two-empty-reads, unreadable-Claude-box and non-Claude whole-head rules.
  The direct expectation refusal is `composer_orphan`; human Send-now is unchanged.
- The initial guard is immediately after F1. An additional call after saving in-flush
  check supersession is essential: that cancellation happens after the initial guard.
  The rewritten sweep witness proves this same-flush path, so merely adding the earlier
  guard would leave an ordinary red.
- `docs/session-runtime-invariants.md` now records cancellation as bookkeeping, never
  composer release. The hold logs the orphan ID, types no clearing keys, changes no row
  status, charges no attempt, parks nothing and kills nothing.

G4 clears the composer before requesting the next delivery, preserving the plan's
baseline-green guard expectation; R1 separately tests the occupied composer. Running
R1's failing first flush inside G4 at base would already fuse the message and contradict
the requested eight-green/three-red baseline. G3 additionally leaves an unreadable Claude
frame above an actually empty composer: transcript resolution, rather than screen release,
must prevent a resolved orphan from holding later input.

## S1 ordinary evidence

Checkpoint tool import succeeded (3 rows). The only unlisted build was the tool bootstrap
required for import, run through `scripts/build-slot.ps1 -Label c0266-checkpoint-import`:
slot=granted, waited=0s, held=8s. Later invocations execute the built tool DLL directly;
the tool acquires its own build/test slots. No ungated build/test driver was used.

Live GET `/api/runner-defaults` returned revision 2, global default `server2`; GET
`/api/session-runners` confirmed it available/eligible on Linux. No Runner/Platform pin.

Run: `.antiphon/checkpoints/20261022-190006-424a/`. Actual tested source is the S1 SHA.
The process completed with exit 1, as required for the red witness selection. Fresh TRX
identities were inspected: 11 executions, 8 passed, exactly the 3 intended assertion
failures, 0 skipped. One isolated build (350.724s); test host 100.288s; build slot waited
0s and test slot waited 45s; total run 8m34s. The tool's printed `builds: 3` counts all
manifest build definitions; the other two were `unused` in this S1 run.

```text
CHECKPOINT CP-1 commit=927d65ef1f8ab0638b7819b9092e7c7e193d1a2f build=ok filter=/*/Antiphon.Tests.Application/(SessionMessageQueueWedgedHeadTests*)|(AgentTaskCheckSweepTests*)/(C0266_*)|(Parked_head_*)|(Cleared_composer_*)|(the_flush_cancels_a_settled_check*) executed=11 passed=8 failed=3 skipped=0 trx=/work/worktrees/task-c2af4f70/.antiphon/checkpoints/20261022-190006-424a/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=927d65ef1f8ab0638b7819b9092e7c7e193d1a2f sourceState=clean buildSource=verified
```

| Plan ID | CP-1 actual outcome |
|---|---|
| R1 | Expected red: SubmittedBodies contained the orphan Body fused with the next live queue message |
| R2 | Expected red: SubmittedBodies contained the orphan Body fused with the completion note |
| Rewritten sweep witness | Expected red: Runner.Writes contained two input writes after canceling the check |
| G1 previous generation | Passed |
| G2 never typed | Passed |
| G3 later UserPrompt | Passed |
| G4 cleared composer | Passed |
| Empty-composer sweep twin | Passed |
| Parked_head_* (2) | Both passed |
| Cleared_composer_* (1) | Passed |

These are assertions on real fake-adapter submissions, persisted UserPrompt rows and runner
writes, not setup/build failures. Production at S1 is identical to the task base. No deliberate
mutants were run. There are no separately numbered V-n or R-n beyond the plan's R1/R2 and guards.

The CP-1 source snapshots are clean and the build binding verified. The green-receipt validator
was invoked and returned exit 2, `cleanup_refused`: the intentionally red run retained its
output and reported `still-present`. It cannot be a green certificate. After observing executor
completion and inspecting the owned, untracked, link-free 801 MB output directory, it was
removed manually; the historical receipt was not edited. The final green run will use
`--keep-outputs`, followed by explicit output cleanup, to keep qualification separate from
the tool's automatic cleanup decision.

## RC applicability and follow-ups

Current RC checked: `origin/rc/2026-10-20` = `0714a9a89e4bff65aae7e0b4b94098694aeb931c`.
Per-commit three-way pick proof, both exit 0, without switching or creating any branch:

1. `git merge-tree --write-tree --merge-base=06d5b0fdbe9c14ef5edc352d3a75370d5d02d964 origin/rc/2026-10-20 927d65ef1f8ab0638b7819b9092e7c7e193d1a2f`
   produced tree `745ad011fca4cdc766d6128e9b14ce67653b334a`. Synthetic unreferenced commit
   `17c4e6bfe53f3043c7734327326cf85074386494` binds that tree to the RC parent for the next pick.
2. `git merge-tree --write-tree --merge-base=927d65ef1f8ab0638b7819b9092e7c7e193d1a2f 17c4e6bfe53f3043c7734327326cf85074386494 b525ed56d8fae4d162a296c18b54acfa53ca9122`
   produced tree `130f321c2f6ee42287a76fd196d371340994ed66`.

Neither slice changes the plan document, so the pick does not introduce it on RC. This is
clean-pick applicability only; the reviewed RC port and CP-1..CP-3 there are a separate task.
Restart: none for this task. Eventual activation is server-side after a reviewed RC port.

The notification repair below also picks cleanly, with both additional merge-tree calls
returning exit 0. Continue from S2 tree `130f321c2f6ee42287a76fd196d371340994ed66` via
synthetic commit `aa27e8df8fb49314159692562fc536887f27d00c` (parent is the S1 synthetic):

3. Merge-base `3ad732422687d6ff7c2441cef6df5903e864ab9b`, left `aa27e8df8fb49314159692562fc536887f27d00c`,
   right test repair `19f73e0e74eaa3ea7cb3cf82adaf5c3b54e01fe7` gives tree
   `7ccb78af782e3e6d369e9c814af643652e144eec`, wrapped as synthetic commit
   `e78b62b4e19ccab23ecd02fe317c9db1c5ea6a71` for the next pick.
4. Merge-base `19f73e0e74eaa3ea7cb3cf82adaf5c3b54e01fe7`, left `e78b62b4e19ccab23ecd02fe317c9db1c5ea6a71`,
   right fix `4c26df01c8bb96a88f5ab72bc7a89d7a266790d5` gives final RC applicability tree
   `2e137ff937e9eb1779d64c853dfb169ee35f0643`. Report-only commits are excluded from this pick chain.
5. The assertion spelling correction also picks cleanly (exit 0): wrap the preceding tree
   as `0e0e6454ccac22105c4a86e6b29979e4af2d1515`, then merge-base
   `c3dd6faee97571187f0e00d5720eda95b7223812`, left that synthetic commit, right
   `0a049b6fd87e21075563f9b03195547e3e5321b5`. Final tree:
   `394bef87f676388e1345b11d3d5ca5a9fadd74b9`. The report and plan documents remain excluded.

Full card reads confirmed the plan's attention follow-up is already CARD-0267 (Done,
`fd8f7536-be19-44dd-9514-70f549d3982f`), and the separate Pending/collapsed-paste retry gap is
CARD-1505 (Backlog, `c063ceb8-6a5d-4759-984d-e745096bc1b4`). No duplicate card was created.

PC-n/variants: the plan defines zero numbered PCs. All SourceLanding Mutation discovery and
any resulting controls remain pending, including cancellation hold bypass, transcript floor
arms, generation/typedness exclusions, collapsed/unreadable composer and direct-send coverage.
Code ordinary green does not discharge these. After ordinary Review, the caller lands the
original Code task and commissions SourceLanding Mutation on the published source.

## Final qualification command

The first final attempt (`20261022-191304-8746`) tested source
`3ad732422687d6ff7c2441cef6df5903e864ab9b` only as far as an incomplete CP-2 build.
It was explicitly stopped while its first build was running (about 4.5 minutes,
slot=granted, waited=0s). CP-2/CP-3 executed zero tests and have no qualifying receipt
from that attempt. `stop` completed; the subsequent `wait` returned exit 6 with
`executor died without phase=done`, which is preserved as interrupted evidence, not
an infrastructure regression or a passing checkpoint. Its owned partial output was
inspected and removed after the stop, before editing or starting a fresh build.

Reason: code inspection found the same-flush hold returned `Nothing` after persisting
a cancellation, suppressing `SessionQueueChanged` in the outer flush. The existing
all-superseded branch returns `LateConfirmed` to publish bookkeeping without reporting
a delivery. The hold now preserves that behavior. The original rewritten sweep test
also asserts the emitted queue snapshot excludes the canceled check:

- Test assertion committed/pushed first: `19f73e0e74eaa3ea7cb3cf82adaf5c3b54e01fe7`.
- Fix committed/pushed next: `4c26df01c8bb96a88f5ab72bc7a89d7a266790d5`.

This notification assertion was found by inspection and added after the measured S1
red run; it has no separate measured pre-fix red claim. It is part of the unchanged
11-test CP-2 roster and the full CP-3 sweep class. No assertion was loosened. The
replacement CP-2/CP-3 run uses the next committed report tip as expected source SHA.
The interrupted attempt is retained; no green proof is being repeated.

The replacement attempt `20261022-191953-b8ec` at
`c3dd6faee97571187f0e00d5720eda95b7223812` was also stopped during its first CP-2
build (about two minutes, slot=granted, waited=0s; zero tests executed). Local Shouldly
assembly metadata confirmed its predicate overload takes `Expression<Func<T,bool>>`,
which does not accept the newly added pattern-variable expression. The assertion was
spelled with `OfType<SessionQueueDto>()` before `ShouldContain`, preserving its meaning,
in pushed commit `0a049b6fd87e21075563f9b03195547e3e5321b5`. This was a compile-safety
correction found before a compiler result, not a measured test failure. Stop completed,
wait returned exit 6 (stopped executor without phase=done), and inspected partial output
was removed before editing. Both stopped attempts are non-receipt rows; neither can
qualify any ordinary test. Final qualification starts fresh at the next report commit.

With this report committed and pushed, run the already-built checkpoint tool:

```sh
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-22-card-0266-plan.md --rows CP-2,CP-3 --expected-source-sha <committed-HEAD> --max-wait 50s --serial --keep-outputs
```

Wait by the printed run ID until exit is not 75, inspect each class/method in fresh TRX,
then validate its report.json with `scripts/validate-checkpoint-receipt.ps1` for that exact
SHA and rows CP-2,CP-3. CP-2 is the fixed rerun of CP-1's red selection. CP-3 runs all of
SessionMessageQueueWedgedHeadTests, AgentTaskCheckSweepTests and ExpectationDirectDeliveryTests.
No repeat proof is requested. No timeout or assertion has been loosened.
