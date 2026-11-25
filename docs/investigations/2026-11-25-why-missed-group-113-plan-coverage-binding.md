# Why Full group 113 found stale plan coverage bindings

The Windows Full group 113 TRX for `b8cbfb24` recorded two failures in
`PlanCoverageBindingTests` at `2fd61b643ea4231fa732cd4969b5284d43b33f39`.
Both methods failed at the same assertions on Linux at that exact pin.
The Linux replay traced each assertion to a class removed in unit 20 of
the checkpoint migration.

## Introducing commit and affected plans

`3937ffdb7f4502c1b08bcb97c83e805fd4f10b55` removed the old PowerShell
checkpoint fixtures. It belongs to unit 20 of the PowerShell-to-C# migration.
The commit has no card number. Both named classes existed at the previous
candidate pin, `6ed4cb58cd75fe386e94965165d96752452431a8`. The seven plans
read by the class, the coverage implementation and the advisory timeout
method were identical between the pins. The first failures came from the
selected class lists after the deletion.
[Unit 20](../superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md)

| Method and top test frame at the base pin | Assertion in both TRX records | Windows and Linux durations | Plan row and missing class |
| --- | --- | --- | --- |
| `historical_plans_bind_without_suppression`, line 178 | `CheckpointNativeDiagnostics.Coverage(firstExit, CheckpointFixtures.RepoRoot, first)`, `c1046-real-plans`, expected 0, actual 2 | 48.712 s and 76.878 s | CARD-1035 CP-2 selects `RunCheckpointScriptTests`, whose file was deleted. |
| `historical_row_timeout_checklist_binds_the_advisory_method`, local `Bind` line 259, caller line 237 | `CheckpointNativeDiagnostics.Coverage(exit, root, output)`, `c1055-1005`, expected 0, actual 2 | 84.433 s and 113.077 s | CARD-1005 CP-1 selects `CheckpointNamespaceCensusUsageTests`, removed from `CheckpointTempUsageTests.cs` with the old namespace census. |

`CoverageCommand.Run` enumerates the selected project and requires each
checkpoint class operand to resolve before analyzing obligations. Both
commands issued `INPUT_INVALID` with `unresolved selected class`. The
diagnostic's `testPath` is the last input visited during the scan. The
missing operands were identified by comparing the plans with the deletion.
The gated Linux run executed two tests with two failures and zero skips.
[Binding tests](../../tests/Antiphon.Tests/Checkpoints/PlanCoverageBindingTests.cs)
· [Coverage command](../../tools/Antiphon.Checkpoints/Coverage/CoverageCommand.cs)

The first repair checkpoint at `8b5a34e4a2daf1b7aed62eecd70a1ee023c52bc9`
passed the CARD-1005 method and then exposed a second failure inside
`historical_plans_bind_without_suppression`. After CARD-1035 resolved, that
method reached CARD-1037 and expected exit 0 but received exit 1.
Unit 28's `2159d023d917593a1cb1a5f96d4bcd7a82386bd8` changed the live
refusal hint and its test assertions from PowerShell parameter spellings to
CLI flags. The CARD-1037 verification row and its three literal value
bindings still named the PowerShell parameters. An isolated coverage
replay with the base-pin plan and sources matched 20 of 23 obligations.
The three `MISSING_VALUE` findings were `-Worktree`, `-StartRef` and
`without -OnAgent`. The first checkpoint recorded one pass, one failure
and zero skips. [Live refusal assertions](../../tests/Antiphon.Tests/Application/RemotePoolFollowUpAdmissionTests.cs)

## Windows evidence and prior run

The orchestrator extracted the Windows TRX assertions, stacks and stdout
tails from the desktop. Both stack locations match the pinned source.
The CARD-1005 stdout reports `INPUT_INVALID` with `unresolved selected class`.
Its plan hash, `7a61e016999ddda84678e90b25290fc6ff559e27eea47075681be932fb6f586d`,
matches the pinned plan after CRLF conversion. The accompanying verified read
of `CheckpointManifestTests.cs` reports 5,102 characters and 116 CRLF line
endings, also matching the pinned source after conversion.

The CARD-1035 stdout excerpt ends in the diagnostic probe of
`NativeLinuxWindowsReasonAudit.cs`. That probe runs after the coverage command
returns and uses its own 32 KiB read cap. The pinned file is 62,483 bytes
with CRLF, within the coverage command's 1 MiB source limit. The Linux
console contains the primary coverage report for the same assertion and
identifies the unresolved class before that probe runs.
[Diagnostic probe](../../tests/Antiphon.Tests/Checkpoints/CheckpointNativeDiagnostics.cs)

The prior `acca11ba` group 113 lookup returned no outcome for either method.
The supplied failed-first seed mentions `group-113`. That group identifier
does not identify the methods included in the seed. The prior source pin
shows that both selected classes still existed there.

## Approving reviews

The approving Review task, Full or Interim scope and executed selection for
units 20 and 28 were unavailable in the retained evidence. The recorded recut
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
receipts. CARD-1037 V-1 and its checklist now use `--worktree`,
`--start-ref` and `without --on-agent`. The isolated replay matched all
23 obligations and retained all three static positive-control bindings.

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
| `docs/superpowers/plans/2026-10-04-card-1037-remote-follow-up-launch-guard-plan.md` | `/*/*/PlanCoverageBindingTests/*` |
| `tests/Antiphon.Tests/Application/RemotePoolFollowUpAdmissionTests.cs` | `/*/*/PlanCoverageBindingTests/*` |

Ordinary `impacted-classes --path … --json` over those paths returned
`PlanCoverageBindingTests`. The short Merge subset for unit 20's deletion did
not select it. Two method operands, `historical_plans_bind_without_suppression`
and `historical_row_timeout_checklist_binds_the_advisory_method`, are sufficient
for the deletion case and form the native replay row after the active
qualification releases the desktop.

## Exemption

None. Group 113 in run `3528e6b1` referred to the checkpoint index encoding
fixture and a different failure. [Earlier group 113](2026-11-21-why-missed-rc-run-3528e6b1-group-113.md)
