# Why missed: a new Checkpoints class kept the strict limiter without joining the shared-checkout roster

This note covers one new rc-chunk cause: rc run 231f573c, rc/2026-11-13 = `653f1145de5b72c2617a018cd606e0e8ba3cfe6e`,
chunk `antiphon-all-group-2.trx`, Windows. CARD-1781.
Debug task 84a19311 reproduced it on Linux (server2) at master `653f1145d`.
The Windows read-out is
`docs/investigations/2026-11-09-run-231f573c-process-spawn-limit-windows-failure.md` (task 36fc8553 branch).

## Failure

`Antiphon.Tests.ProcessSpawnLimitTests.Checkpoint_tool_classes_carry_their_own_limiter`
(`tests/Antiphon.Tests/ProcessSpawnLimitTests.cs:274`):

```
misses should be empty but had 2 items and was
["Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests must carry [ParallelLimiter<CheckpointToolSpawnLimit>]",
 "Antiphon.Tests.Checkpoints.PhaseJournalKillDiagnosticTests must not keep [ParallelLimiter<ProcessSpawnLimit>]"]
```

The test does no spawning. It reflects over the assembly's attributes, so it fails the same way on every platform and at
any load. On Linux at `653f1145d`, `/*/*/ProcessSpawnLimitTests/*` gave 37 run, 36 passed, 1 failed, 0 skipped. Windows gave
the same message. The XDG/pwsh-cache hypothesis (CARD-1790) does not apply: the failure has no environment input.

CARD-1747 (`292f49dfa` red, `e51d4375a` green) split the Checkpoints namespace into two groups:

- the six classes that start a child with the shared checkout as their working directory. These stay on
  `ProcessSpawnLimit` and are named in `SharedCheckoutCheckpointClasses` and `StrictClasses`;
- every other limited Checkpoints class, which must carry `CheckpointToolSpawnLimit`.

CARD-1748 then added `PhaseJournalKillDiagnosticTests` to `tests/Antiphon.Tests/Checkpoints/` with
`[ParallelLimiter<ProcessSpawnLimit>]` and did not add it to either roster. That attribute is the right lane:
`script_killed_row_prints_the_same_diagnostic` and `inherited_phase_root_uses_the_row_and_leaves_the_decoy_empty` start
`pwsh scripts/test-run-checkpoint.ps1` with `WorkingDirectory = CheckpointFixtures.RepoRoot`. Review 94181272 also
records harness stamp directories `tests/Antiphon.Tests/bin-c585h-c1748-*` left in the checkout. That is the CARD-1747
reason a class stays strict.

Fix (test-only, `58e11030b989620c2a30177c5eac5f8c158462e9`): name the class in `StrictClasses` and in
`SharedCheckoutCheckpointClasses`. The class and its attribute are unchanged, and no assertion was loosened. After the
fix, `(ProcessSpawnLimitTests*)|(PhaseJournalKillDiagnosticTests*)` gives 42 run, 42 passed.

## Introducing commit/card

introducing commit/card: be4c317a38568a37b15d41ae985fee6f8054ef1d (CARD-1748)

This commit is the rebased copy of Code 149a3cd3's red commit `f99dd0048e4f8851c96ee0774ad218e4e668c391`. It landed
on master at 2026-11-08 09:37Z with `8b7092b62`, `84298c40f`, `ccc472160` and `d0a7add59`. None of these change the
attribute.

This was not a merge collision. The CARD-1748 Code base `d0a3ee89e495baf364e8260abeab326db9c98f76` already
contained both CARD-1747 commits. The guard was red on the CARD-1748 branch from its first commit to its land.
rc/2026-11-12 (`5fef2c23b`) and rc/2026-11-13 both carry it. Run a6410818 on rc/2026-11-12 did not show it because that
run never wrote `antiphon-all-group-2.trx` before it was superseded (Windows read-out above).

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- Code 149a3cd3 (Grok): four build-slot leases of the card's own tests and no checkpoint receipt, as Review 95bbe60b
  records. No plan `### Checkpoints` table existed for the card. The owner repair, Code e1f0448b, was reviewed by
  diff only.
- Review 95bbe60b, round 1, scope Final (Full). It found a blocker unrelated to this cause. Selection:
  CP-1 `/*/*/PhaseJournalKillDiagnosticTests/*`; CP-2 `/*/*/CheckpointTerminalReceiptTests/*`;
  CP-3 `(CheckpointReceiptSafetyTests*)|(ReportWriterTests*)|(ReportMergerTests*)`;
  CP-4 `(RowRunnerTests*)|(TimingJournalTests*)|(RerunPolicyTests*)|(RunSchedulerTests*)|(CheckpointOrFilterTests*)`;
  CP-5 `(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestClassificationPolicyTests*)`.
  The whole Unit lane did not run (about 3 h against a 60-minute budget).
- Review 94181272, round 2, scope Final, diff only, CLEAN (`.antiphon/task-94181272.md`, commit `c331bc3ae`).
  Selection: CP-1 `/*/*/PhaseJournalKillDiagnosticTests/*`; CP-2 `/*/*/CheckpointTerminalReceiptTests/*`;
  CP-3 `(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestClassificationPolicyTests*)`.
- The land path: no fixed land subset names `ProcessSpawnLimitTests`, and no land receipt shows the class.

## Outside the selection

why the failing test was outside that selection (lane/category/path): Unit lane, `Unit`, `tests/Antiphon.Tests/ProcessSpawnLimitTests.cs`

Every selection listed the card's own classes plus the policy guard batch (`TestClassificationPolicyTests` and its
siblings). That batch checks lane categories, not limiter rosters. The limiter census lives only in
`ProcessSpawnLimitTests`. Reviewers add that class to a selection only when the diff touches the limiter types. Here the
diff added a new class that carried a limiter, which is the input the CARD-1747 census reads, so no reviewer added it.
The census runs only in the full Unit lane, which neither review ran. So it was first seen in rc qualification.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.Tests/**/*.cs`, `tests/Antiphon.Tests/TestHelpers/*SpawnLimit.cs`, `Directory.Build.targets` -> `/*/*/ProcessSpawnLimitTests/*`

As a CARD-1801 `tests/impacted-classes.json` kind:

```json
{
  "id": "spawn-limit-rosters",
  "globs": [
    "tests/Antiphon.Tests/TestHelpers/*SpawnLimit.cs",
    "tests/Antiphon.Tests/Checkpoints/**",
    "Directory.Build.targets"
  ],
  "added": {
    "glob": "tests/Antiphon.Tests/**",
    "literals": [
      "ParallelLimiter<"
    ]
  },
  "classes": [
    "ProcessSpawnLimitTests"
  ]
}
```

The `added` literal catches any new or changed class in `tests/Antiphon.Tests` that names a limiter, which is how this
cause entered. The `Checkpoints/**` glob covers the namespace the CARD-1747 census enumerates. The class takes about 6 s
after a build on server2 (37 tests). This note does not edit `tests/impacted-classes.json`. CARD-1801 owns that row, and
the file's witness suite pins its contents.

## Exemption

same cause as an already written note: none
