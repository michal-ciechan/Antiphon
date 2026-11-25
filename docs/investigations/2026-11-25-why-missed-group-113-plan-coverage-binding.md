# Why Full group 113 found stale plan coverage bindings

Windows Full watcher for `b8cbfb24` reported two failures in
`PlanCoverageBindingTests` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`.
The two repository-backed methods both failed on Linux at that exact pin.
Each coverage command refused a class removed in unit 20 of the checkpoint
migration.

## Introducing commit and affected plans

`3937ffdb7f4502c1b08bcb97c83e805fd4f10b55` removed the old PowerShell
checkpoint fixtures. It belongs to unit 20 of the PowerShell-to-C# migration.
The commit has no card number. Both named classes existed at the previous
candidate pin, `6ed4cb58cd75fe386e94965165d96752452431a8`. The seven plans
read by the class, the coverage implementation and the advisory timeout
method were identical between the pins. The stale dependency is the selected
class list after the deletion.
[Unit 20](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)

| Linux method and top test frame at the base pin | Assertion | Plan row and missing class |
| --- | --- | --- |
| `historical_plans_bind_without_suppression`, line 178, 76.878 s | `c1046-real-plans`, expected 0, actual 2 | CARD-1035 CP-2 selects `RunCheckpointScriptTests`, whose file was deleted. |
| `historical_row_timeout_checklist_binds_the_advisory_method`, local `Bind` line 259, caller line 237, 113.077 s | `c1055-1005`, expected 0, actual 2 | CARD-1005 CP-1 selects `CheckpointNamespaceCensusUsageTests`, removed from `CheckpointTempUsageTests.cs` with the old namespace census. |

`CoverageCommand.Run` enumerates the selected project and requires each
checkpoint class operand to resolve before analyzing obligations. Both
commands issued `INPUT_INVALID` with `unresolved selected class`. The
diagnostic's `testPath` is the last input visited during the scan. The
missing operands were identified by comparing the plans with the deletion.
The gated Linux run executed two tests with two failures and zero skips.
[Binding tests](../../tests/Antiphon.Tests/Checkpoints/PlanCoverageBindingTests.cs)
· [Coverage command](../../tools/Antiphon.Checkpoints/Coverage/CoverageCommand.cs)

The Windows TRX, test log and execution record were unavailable from the Linux
mirror. The two assertions above come from the Linux TRX and console. The
method-level outcome in Windows run `acca11ba` and group 113's failed-first
membership remain unverified. The prior candidate's source establishes class
availability only.

## Approving reviews

The approving Review task, Full or Interim scope and executed selection for
unit 20 were unavailable in the retained evidence. The recorded recut
selections CP-1 through CP-4 omit `PlanCoverageBindingTests`. The graph and
derived-index gates in that record check their respective build and selector
inputs. [Recut gate evidence](2026-11-25-rc-cut-gates-at-4022b30f6.md)

## Outside the selection

This Unit class exercises the real plans at runtime. Compilation accepts the
deletions because the class names in the Markdown are data. Short Merge
verification selects narrow witnesses, and a replay of `impacted-classes
--merge-subset` for the introducing commit omitted this class. Both failures
reproduce under the corresponding narrow Linux selection. The earlier Windows
coverage-intake incident recorded isolation passes and supplies no attribution
for this deletion. [Short Merge policy](../orchestration-loop.md#merge-verification-is-short-2026-11-18)
· [Earlier intake incident](2026-10-15-card-1287-windows-unit-lane-5ca376de.md)

## Repair and other bindings

CARD-1035 CP-2 selects the current no-build, MSBuild-forwarding and launcher
ASCII witnesses. R-3 preserves the original selection for its historical
receipts. The CARD-1035 and CARD-1037 positive controls still bind to their
current assertion sources.

The old CARD-1005 census's fixed namespace count was retired along with its
PowerShell consumer. The binding test stages the current plan and the thirteen
current source files it already selects. It adds the original retired census
declaration as a syntax-only fixture so R-5 and PC-80 can still resolve inside
that isolated tree. The fixture comes from `CheckpointTempUsageTests.cs` at
`6ed4cb58cd75fe386e94965165d96752452431a8`. The test still requires all
177 CARD-1005 obligations, all 72 CARD-0891 obligations and the exact live
advisory-timeout method. CARD-1005's historical checkpoint table and prior
outcomes stay in the record.

The same stale selector pattern appears in CP rows in 29 archived plan
documents at the failing pin, including CARD-0585, CARD-0723, CARD-1046 and
CARD-1199. Those historical rows need their corresponding sources when
replayed. `PlanCoverageCommandTests.WriteFrozenWorld` also combines a frozen
plan with current test files, so future removals of its eight named sources
can break that test. The C835 and C866 golden checks already supply pinned
test sources with their historical plan projections.
[Frozen-world test](../../tests/Antiphon.Tests/Checkpoints/PlanCoverageCommandTests.cs)
· [Golden checks](../../tests/Antiphon.Tests/Checkpoints/PlanCoverageGoldenTests.cs)

## Cheapest pre-land check

| Changed path | Impacted class filter |
| --- | --- |
| `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs` | `/*/*/PlanCoverageBindingTests/*` |
| `tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs` | `/*/*/PlanCoverageBindingTests/*` |
| `docs/superpowers/plans/2026-10-04-card-1035-checkpoint-child-cwd-plan.md` | `/*/*/PlanCoverageBindingTests/*` |
| `docs/superpowers/plans/2026-10-03-card-1005-opt-in-class-census-plan.md` | `/*/*/PlanCoverageBindingTests/*` |

Ordinary `impacted-classes --path … --json` over those paths returned
`PlanCoverageBindingTests`. The short Merge subset for unit 20's deletion did
not select it. Two method operands, `historical_plans_bind_without_suppression`
and `historical_row_timeout_checklist_binds_the_advisory_method`, are sufficient
for the deletion case and form the native replay row after the active
qualification releases the desktop.

## Exemption

None. Group 113 in run `3528e6b1` referred to the checkpoint index encoding
fixture and a different failure. [Earlier group 113](2026-11-21-why-missed-rc-run-3528e6b1-group-113.md)
