# Why missed: Windows native ownership fixture marker sharing

## Introducing commit/card

`407afd5992c0272ad146179c8ee51a25be633fcf` (W2-nightly S04, porting the CARD-1039 ownership tests) replaced the PowerShell test driver with in-process C# reads. At rc source `ac8339c754a84ab53ca07708c091d4e79bd1fb5b`, `C1039_DescendantExit` and `C1039_RetainedWriterDrainDeadline` failed with sharing violations in Windows Full run `c9816b02`, `antiphon-all-group-134.trx`. The reader and fixture files are identical at the master start `f6c3563dd62b4cc970608dcc60bab3f9a8a80f91`.

The `OwnedChild` fixture writes `child.pid` and `writer.handle` with short `File.WriteAllText` calls. A file can already exist while the writer is still open or its content is empty. `File.ReadAllText` requests `FileShare.Read`, which conflicts with the existing write access on Windows. The intentionally retained writer is the duplicated stdout pipe handle recorded in `writer.handle`. `NativeProcessOwner` does not publish either marker. The test harness now shares write and delete access when reading live markers and polls for usable content for up to five seconds. Process identity, job containment, cleanup and drain assertions retain their original checks.

## Approving reviews

The CARD-1039 stage history records Full Clean reviews `81dea5db-f223-4430-8899-bdaba3c5303b` at `145544e68b5564aa8d4c7d140765e760951c6a32` and `f19803a5-5745-48d8-ab77-0c7ce0a8391d` at `33b5ee95901b5ce01a9e1019d722ae9a472d3959`. Both reviewed sources predate the C# port. Their executed filters and any approving S04 review, scope and executed filter could not be verified. The S04 plan lists `NightlyNativeOwnershipTests`, `RcFullTimingContractTests` and `TestPhaseSupervisorTests`. [W2-nightly port plan](../superpowers/plans/2026-11-21-w2-nightly-port-slices.md#5-test-classes-per-slice)

## Outside the selection

`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs` is an Integration class whose native entry requires Windows and the staged `OwnedChild.exe`. A Unit-only selection omits the methods, and a Linux selection skips their bodies. The portable `LiveFixtureTextTests` regression exercises the sharing flags and retry behavior through a synthetic opener. Native Windows confirmation of the original two methods is owed.

## Cheapest pre-land check

`tests/Antiphon.Tests/Scripts/NightlyNativeOwnershipTests.cs` → `/*/*/NightlyNativeOwnershipTests/*` on native Windows. The narrow incident rerun is `C1039_DescendantExit` and `C1039_RetainedWriterDrainDeadline`.

## Exemption

None.
