# CARD-1032 S5 Final supplement

The higher-priority Code stage contract requires Unit and full affected classes
for Final. These supplement the caller's CP-7..10 selection; they do not add
implementation slices. The caller asked to omit these runs; that conflicts with
the stage's explicit Final/SCOPE requirements, so this supplement records why
these additional selections run. No whole-assembly or namespace run is selected.

Invariant: S5 journal progress/receipt reading and the shared admission fence
preserve earlier inspection, ownership and legacy removal/creation behavior.
Bounded affected classes: CompletedCardWorkspaceRemovalTests (89 results),
LegacyWorkspaceRemovalGateTests (11), RunnerWorkspaceServiceTests (33).
No affected class is declared unbounded. One full-class invocation covers 133
results and only those classes. Full Unit categories run separately from class
selection in the two relevant test projects. Estimated additional cost: two
isolated builds plus approximately five minutes of tests (eight minutes total),
excluding build-slot wait. No PTY assembly is co-scheduled.

The S5 exact CP-7..10 roster already ran green at e5d8efc179f9a0ed083f534396606f36e10e216f.
This documentation-only committed group supplies Final coverage. No additional
unchanged S5 proof repetition is required after that green. Fresh TRX class and
method rosters/counts must be inspected; Min=1 for Unit is a floor, not a census.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-28 | S5-Final | `tests/Antiphon.SessionRunner.Tests -> bin-c1032-s5-final-runner/` | affected-full-classes | `/*/*/(CompletedCardWorkspaceRemovalTests*)\|(LegacyWorkspaceRemovalGateTests*)\|(RunnerWorkspaceServiceTests*)/*` | V-3, V-4, V-5, R-1, R-2, R-3 | all 133 full-class results; 0 failed/skipped | 133 | 4 | true |
| CP-29 | S5-Final | CP-28 | runner-unit | `/*/*/*/*[Category=Unit]` | Final Unit | full current runner Unit category; fresh census; 0 failed | 1 | 3 | true |
| CP-30 | S5-Final | `tests/Antiphon.Tests -> bin-c1032-s5-final-unit/` | unit-lane | `/*/*/*/*[Category=Unit]` | Final Unit | full current server Unit category; fresh census; 0 failed | 1 | 5 | true |
