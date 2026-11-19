# CARD-1744 S5a checkpoints

Docs slice for the G1-G3 runbook amendment. No new test. The closed list is the one row below.

`DockerStackDocumentationTests` has 20 `[Test]` methods and no `[Arguments]` in `tests/Antiphon.Tests/Infrastructure/DockerStackDocumentationTests.cs` on master `a789c87770d31a77687f75a619c5af646d1ce91f`. The G1-G3 plan's CP-9 Min of 19 was the count at `997f9b8d9`. S5a adds no test, so Min is 20. `EstimatedMinutes` 15 is the wall budget for one cold `tests/Antiphon.Tests` build plus this class. The class itself only reads files.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-14 | S5a | tests/Antiphon.Tests -> bin-c1744doc/ | docs | `/*/*/DockerStackDocumentationTests/*` | S5a doc edits keep the pinned strings | 0 failed | 20 | 15 |
