# Why the live-lane budget lint read deleted drivers

Windows Full run `acca11ba`, group `antiphon-all-group-112`, reported four
`FileNotFoundException` failures at rc pin `6ed4cb58cd75fe386e94965165d96752452431a8`.
The same four startup and delivery cases failed on server2 Linux at master
`a8982a509d823ffe1d8639838747ea6dc3f42bc6`. The class executed 13 results with
9 passed and 4 failed. Both pins carry the same test blob and lack both drivers.

## Introducing commit

`f47da556e86ac6c5419cee7c3a07b3d3bbb64d06` deleted
`scripts/test-live-lane-startup.ps1` and `scripts/test-live-lane-delivery.ps1`
in the [unreferenced-script cleanup](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md#unreferenced-scripts).
The [CARD-1545 plan](../superpowers/plans/2026-10-31-card-1545-s1c-test-design.md)
already defined CP-3 and CP-19 against the startup and delivery wiring test
classes before the cleanup. The cleanup retired the checkpoint wrappers.
The [CARD-1720 budget lint](../superpowers/plans/2026-11-05-card-1720-round1-repair.md)
still read the wrapper files for its base and controls variants.

## Approval and test selection

The cleanup record documents operator approval and a reference census.
The available record gives no Full or Interim Review receipt or executed test
selection for the deletion. The census listed zero C# references because
`CheckpointCallerBudgetTests` assembled the filenames from the case argument.
Neither deleted path had an entry in the pre-deletion derived impact index.

Replaying the Merge subset selector over `f47da556^..f47da556` omitted
`CheckpointCallerBudgetTests`, a Unit class in `tests/Antiphon.Tests/Checkpoints`.
The fixed Merge/rc-cut guard classes also omit it. The Full roster reached it
after the cut. Running the impacted reader class against the staged deletion
would have exposed the mismatch before publication.

## Cheapest pre-land check

Proposed CARD-1782 mapping in `tests/Antiphon.Tests`, including deleted or
renamed paths in the diff.

`scripts/test-live-lane-*.ps1` → `/*/*/CheckpointCallerBudgetTests/*`

Exemption for a previously documented cause: none.

## Repair

The test roster now names the two surviving callers by literal path. The four
isolation variants still lint producer and consumer rows with and without
controls. The two evidence variants still lint the real totals and also check
that the wrapper forwards `--total-timeout` to the checkpoint command. All six
cases run the extracted totals through the real checkpoint selection code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-e2507f23/` | live-lane-caller-budget | `/*/*/CheckpointCallerBudgetTests/*` | Surviving caller budgets and explicit-total admission | 9 passed, 0 failed | 9 | 8 |
