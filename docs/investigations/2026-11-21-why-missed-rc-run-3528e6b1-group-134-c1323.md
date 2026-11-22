# Why Windows rc group 134 missed a setup timing write failure

## Introducing commit and observed failure

Windows Full run `3528e6b1` at `bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`
reported two failures in `antiphon-all-group-134.trx`. In C1323, the
`offline-windows` fixture reached final state with exit 1. Its sole verdict
reason was `deferred-evidence-write-failed` for
`timing-offline-windows/setup-phases.json`, with access denied. The missing
fixture Git floor file only emits a warning.

CARD-1514's `48f31c81e0a1b96b44aa6ca3d599cee0308e7b4f` introduced the
setup-phase sidecar. `218a6dbd9d9898b95fd92e304961035b4a68b99b` moved
the writer to `QualificationTiming.WriteRunPhase`. The write-behind
integration in `68e276a9b3c22ffe0955520f38dc0032c293b311` uses the shared
queue introduced by `6a371bc38888a77325fbdc24e55e61850ed05e68`. The queue
on the failed rc SHA attempted the replacement once.

## Failure path

`nightly-run-impl.ps1` creates a run directory under the fixture's private
`execution/run-windows` root. The child resolves a blank sealed timing root
under that directory as `timing-offline-windows`. Manifest and atomic-write
helpers create directories with inherited permissions. The fixture runs its
scenarios sequentially with distinct roots. Its child seam writes command
logs, TRX, and discovery and execution artifacts. The explicit run and journal
locks use separate files.

The nightly executor records setup and planning phases through
`QualificationTiming.WriteRunPhase`. That method reads earlier rows through
`EvidenceFiles`, then queues the replacement via `EvidenceFiles.WriteAtomic`.
The queue serializes writes and serves pending bytes to in-process readers.
The writer closes the GUID-suffixed temporary file before moving it over the
target. Repeated setup updates expose a short window where an external
reader can hold the target or temporary file without delete sharing.

This is consistent with an intermittent production writer race that surfaced
in the fixture. The TRX reason identifies the destination and error but does
not capture the failed filesystem call or the process holding a handle. The
healthy progress of the enclosing Windows run and the other C1323 checks
argue against a systematic permission failure. A real nightly shares the
writer and can still fail closed if a deferred write ultimately fails.

## Existing master repair

Master already contains `6972fca9efc9e62318bc3bbb62937c934aa03765`, which
is absent from the failed rc SHA. Its commit records the same
`setup-phases.json` denial in `RcFailedFirstExecutionTests` on Windows,
followed by a clean rerun. The commit routes both the buffered rename and the
direct atomic rename through `AtomicRename.MoveOver`. On Windows it retries
access denied and sharing or lock violations, with six attempts and 620 ms
total backoff. An unrelated error or exhaustion still propagates. The
deferred-write verdict continues to fail coverage and tests, recording the
final path and error. The retry covers the rename, and the original TRX
reason leaves a failure creating the temporary file possible.

The separate 35-versus-37 roster failure was already repaired in the inspected
master history by `20d801e8a003d7026dfc31e77d933ad39533705a` for CARD-2006.

## Linux verification

Checkpoint run `20261121-235214-f0c8` tested clean master
`daac1174b0ad3d45dcecd1444332adaa2cf98002` with one leased build.

| Row | Exact method | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| CP-1 | `ReleaseGatePlatformTests.C1199_WindowsRequiredRoster` | 1 | 0 | 0 |
| CP-2 | `ReleaseGatePlatformTests.C1323_PlatformExecution` | 1 | 0 | 0 |

The methods enforce six roster assertions and twelve platform-execution
assertions respectively. Native Windows verification remains owed.

## Review selection and why it missed

CARD-1514 Interim review `a45b2ddc` recorded its Linux selections as
`RcFullTimingContractTests`, `TestTimingRecorderTests`,
`TestPhaseSummaryTests`, `RcRosterRepairTests`,
`WindowsUnitPlatformAdmissionTests` and `C1514LandingSplitBudgetTests`.
It left native rows pending. The [November 19 nightly performance investigation](2026-11-19-operator-nightly-perf-commits-review.md)
covered the write-behind range on Linux, with a core case, nine guard
classes, the derived index, two exact `RcFailedFirstExecutionTests` methods
and the platform exclusion-policy harness. These are recorded verification
selections. An approving Full Review and its selection for the exact write-behind
change could not be verified from the repository record.

`C1323_PlatformExecution` is a script Integration method in
`ReleaseGatePlatformTests`. It was outside both recorded selections.
Running its simulated Windows case on Linux exercises coverage and credits,
but cannot exercise Windows delete sharing or the OS-gated rename retry.

## Cheapest pre-land check

Proposed native selections for the impacted-classes map:

| Changed path | Class filter |
|---|---|
| `scripts/lib/Antiphon.Nightly/QualificationTiming.cs` | `/*/Antiphon.Tests.Scripts/ReleaseGatePlatformTests/*` |
| `scripts/lib/Antiphon.Nightly/EvidenceFiles.cs` | `/*/Antiphon.Tests.Scripts/ReleaseGatePlatformTests/*` |
| `scripts/lib/nightly-tests-impl.ps1` | `/*/Antiphon.Tests.Scripts/ReleaseGatePlatformTests/*` |
| `src/Antiphon.IO.WriteBehind/*.cs` | `/*/Antiphon.Tests.Scripts/ReleaseGatePlatformTests/*` and `/*/Antiphon.IO.WriteBehind.Tests/WriteBehindFileSystemTests/*` |

Use the two exact methods below for the shortest native check.

Run both on Windows after the live qualification releases its lock, from a
checkout containing `6972fca9efc9e62318bc3bbb62937c934aa03765`. Both are
compiled into `tests/Antiphon.Tests`:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1323-native-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1323-native -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter "/*/Antiphon.Tests.Scripts/ReleaseGatePlatformTests/C1323_PlatformExecution" --timeout 570s
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1323-native-rename -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter "/*/Antiphon.IO.WriteBehind.Tests/WriteBehindFileSystemTests/Atomic_rename_waits_out_a_brief_open_of_the_target" --timeout 570s
```

## Exemption

None. The same failure signature predates this chunk in the master repair's
commit message. That record supplies the existing repair, and this note traces
the group-134 fixture and native coverage gap.
