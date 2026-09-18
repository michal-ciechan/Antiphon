# CARD-0560: tag `HerdrPaneDisposalEndpointTests` as Integration

Plan task `c8477934`, 2026-09-18, inspected checkout `de6eef47` (`feat/card-task-c8477934`).
Read-only against code; no fix was built. The verification design is included because the fix is
one attribute line, so Build can execute it without a separate TestDesign stage.

Owners: [testing](../../testing-and-build.md) (§ Fast lane, CARD-0110 / CARD-0475 S5),
[conventions](../../project-context.md).

## Disposition in three lines

1. **One attribute line.** `[Category("Integration")]` goes directly above
   `public sealed class HerdrPaneDisposalEndpointTests` in
   `tests/Antiphon.Tests/Application/HerdrPaneDisposalEndpointTests.cs` (D-1). Nothing else changes:
   no allowlist row (D-2), no `ParallelLimiter` (D-3), no doc edit.
2. **The three guards go green in the Unit lane** because the class stops being the only
   `Antiphon.Tests` class with neither lane category. All three read the same fact from different
   sources (compiled metadata twice, source text once), so one line fixes all three.
3. **The class itself is verified by a named class filter, not by the Unit lane** (D-4). The card's
   phrase "plus the class itself go green on a fresh Unit-lane run" cannot hold once the class is
   Integration; the Fast-lane recipe is Unit lane plus named affected integration classes.

## Ground truth

Verified on `de6eef47` on 2026-09-18.

| Card assumption | Observed | Consequence |
|---|---|---|
| The class has no `[Category("Unit")]` or `[Category("Integration")]`. | Confirmed. The file has `[Test]` methods (14 cases: G001 x4, G002 x3, G009 x2, G010, G013, G106, G116, `Routes_validate_preview_execute_and_status`) and no class-level or method-level `Category` attribute. Created in `c03809e8c` (CARD-0461) without one. | The fix site is the class declaration line. |
| Three guard tests fail because of it. | `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` scans `tests/Antiphon.Tests/**/*.cs` source text and lists any `public [sealed] class` with `[Test]` members and neither attribute (`missing`). `TestClassificationGuardTests.Registry_matches_compiled_metadata` (tests/Shared, compiled into every test assembly) and `TestClassificationPolicyTests.C487_G068` both call `TestClassificationMetadata.AssertRegistryMatches` on the compiled `Antiphon.Tests` assembly, which emits `lane-xor <FullName>` for any class where `unit == integ` (neither or both). The first asserts the whole error list is empty; G068 asserts no `lane-xor` entry. | The census guard requires the attribute on the line(s) immediately above the class declaration (it walks back over `[...]` lines and comments only). The two metadata guards need the attribute compiled onto the type. A class-level attribute satisfies both. |
| Endpoint tests are typically Integration in this project. | Every other `*EndpointTests` class under `tests/Antiphon.Tests/Application` is `[Category("Integration")]` (19 files; the two that show no attribute are `partial` parts of `AgentTaskCommitEndpointTests`, whose primary part is tagged). | Convention points to Integration. |
| The class is Integration by behaviour, not just by name. | Each test constructs `HerdrDisposalHttpFixture` (`tests/Antiphon.Tests/TestHelpers/HerdrDisposalHttpFixture.cs`), which starts **two real Kestrel `WebApplication` hosts** on random loopback ports (a runner app and a server app), wires `SessionRunnerHttpClient` over real HTTP between them, and starts `HerdrPaneDisposalFixture` (`tests/Antiphon.SessionRunner.Tests`), whose `FakeHerdrServer` listens on a `NamedPipeServerStream`. No Program, no Postgres, no child process. Three of the tests delegate by `new HerdrPaneDisposalHttpWireTests().<method>()` to the sibling class in `tests/Antiphon.Tests/Agents`, which is `[Category("Integration")]`. | Integration (D-1). Real sockets and pipes are exactly what the Unit lane excludes, and the sibling that shares the fixture is already Integration. |
| Once tagged, "the class itself goes green on a fresh Unit-lane run". | The Unit lane is `--treenode-filter '/*/*/*/*[Category=Unit]'`. An Integration class is not selected by it. | The class is verified by its own class filter (D-4, V-4). The Unit lane verifies the guards (V-1..V-3). |
| The registry (`slow-tests-allowlist.txt`) needs a row. | The registry is the Slow allowlist; rows are required only for classes carrying `[Category("Slow")]`, and a row for a non-Slow class is itself an error (`unmarked-registered`). Neither this class nor its sibling is Slow or listed. | No registry edit (D-2). |
| A process-spawning class needs `[ParallelLimiter<ProcessSpawnLimit>]`. | The fixture spawns no child process (in-process Kestrel plus a named pipe). The sibling class carries no limiter. | No limiter (D-3). |

## Decisions

### D-1. `[Category("Integration")]` on the class

Add the attribute line directly above the class declaration, matching the sibling
`HerdrPaneDisposalHttpWireTests` and every other endpoint test in the directory. Reason: two Kestrel
hosts and a named-pipe fake per test are real transport, and the class reuses the sibling's tests by
direct call, so it belongs in the same lane as the sibling.

Rejected:

- **`[Category("Unit")]`.** Would make the census and metadata guards pass just as well, but it would
  put 14 socket-and-pipe tests into the 70-second fast lane and contradict the lane's definition. It
  would also let a Unit class call methods of an Integration class, which the census guard flags when
  the categories are method-level and which is misleading even when they are not.
- **Method-level attributes.** The census guard treats a class with no class-level lane as `missing`
  regardless of method-level tags; the metadata guards read the class. Class-level is the only form
  that satisfies all three.
- **Moving the class next to its sibling in `Agents/`.** Out of scope; the file location is not what
  the guards check.

### D-2. No `slow-tests-allowlist.txt` row

The class is not Slow and a row for a non-Slow class is an error. If the tripwire (V-5) reports a
new test at or above 5 s, that is a separate decision (Slow plus a row with a CARD reason), not
part of this card.

### D-3. No `[ParallelLimiter<ProcessSpawnLimit>]`

Nothing in the fixture chain starts a child process. Same as the sibling.

### D-4. Verification is Unit lane plus a named class filter

The card's verification sentence assumed the class would run in the Unit lane; it will not. Build
runs the Unit lane once (guards) and the class filter once (the class), both from one isolated build
output, and reads a fresh TRX for each.

## Slices

### S1. Tag the class

- `tests/Antiphon.Tests/Application/HerdrPaneDisposalEndpointTests.cs`: insert
  `[Category("Integration")]` on the line above `public sealed class HerdrPaneDisposalEndpointTests`.
  The `using TUnit.Core;` already present supplies `CategoryAttribute`.
- No other file.

Commit message shape: `test(CARD-0560): tag HerdrPaneDisposalEndpointTests Integration; lane guards green`.

## Verification design

Build once into an isolated output (forward slash), then run from it. Use a fresh empty results
directory per invocation.

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c560/ --nologo
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c560/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename unit.trx --results-directory .antiphon/c560-unit
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c560/ -- --treenode-filter '/*/*/HerdrPaneDisposalEndpointTests/*' --report-trx --report-trx-filename class.trx --results-directory .antiphon/c560-class
pwsh -File scripts/test-duration-tripwire.ps1 -Trx .antiphon/c560-class/class.trx
```

| Id | What | Filter / source | Pre-fix | Post-fix |
|---|---|---|---|---|
| V-1 | `TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration` | in `unit.trx` | fails, `untagged test classes: tests/Antiphon.Tests/Application/HerdrPaneDisposalEndpointTests.cs::HerdrPaneDisposalEndpointTests` | passes |
| V-2 | `TestClassificationGuardTests.Registry_matches_compiled_metadata` | in `unit.trx` | fails, error list contains `lane-xor Antiphon.Tests.Application.HerdrPaneDisposalEndpointTests` | passes |
| V-3 | `TestClassificationPolicyTests.C487_G068` | in `unit.trx` | fails, same `lane-xor` entry | passes |
| V-4 | `HerdrPaneDisposalEndpointTests`, all 14 cases | `class.trx` | 14 pass (behaviour unchanged) | 14 pass, nonzero count in a fresh TRX |
| V-5 | Duration tripwire on `class.trx` | script output | n/a | no new test at or above 5 s; if one appears, report it and stop (D-2) |
| V-6 | Unit lane total | `unit.trx` | 3 failures attributable to this class (plus any unrelated pre-existing red, which Build names and checks at the base commit by targeted rerun) | 0 failures attributable to this class |

The pre-fix column is the red-then-green control: Build may run V-1..V-3 as a targeted filter
(`/*/*/(TestLaneCategoryGuardTests*)|(TestClassificationGuardTests*)|(TestClassificationPolicyTests*)/*`)
at the base commit if it wants direct evidence, but the failure is already recorded on 10+ cards
(CARD-0443/0462/0492/0501/0527/0543/0546/0561/0562 verification reports), so the Unit lane after the
fix is sufficient.

Any Unit-lane failure not in V-1..V-3 is pre-existing by definition of this change (one attribute on
an Integration class cannot alter a Unit test) and is reported, not repaired here.

Cleanup: delete every `bin-c560` directory the build dropped (about a dozen, one per project) before
finishing.
