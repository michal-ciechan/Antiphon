# Why group 137 failed in Windows Full run 3528e6b1

The Windows candidate for `rc/2026-11-30` was
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`. The caller reported six failures from
`C:\Antiphon\releases\candidates\rc-2026-11-30-bd34a7ff6da9\logs\2026-11-21-0735-3528e6b1\antiphon-all-group-137.trx`.
The Linux delegate received the failure extracts. The completed TRX and the Windows
checkout are on the desktop.

## Introducing commits

- CARD-1836 S6c added `RcDirtyOrderCSharpTests` and its repo replay in
  `a5fd2a1982e14f75572000bfa2fb81adb2496398`. `RcPortCorpus.ReplayDirty` ran
  the corpus verdict and then called `Directory.Delete(repo, true)` in
  `finally`. Git's Windows loose objects are read-only. The filename
  `39a3c67d8f41e40282ec5ae0b16385b3dc6589` matches the tree object
  `2c39a3c67d8f41e40282ec5ae0b16385b3dc6589` for the fixture's `other`
  commit. The cleanup exception becomes a corpus mismatch because replay
  catches exceptions around the whole case. The scratch repo now has one
  owner from creation through cleanup and uses `GitFixtureCleanup.Delete`
  on both setup failure and normal completion.
- CARD-1836 S6b introduced `RcRunEvidenceTests` in
  `567477ec6dd59ae6d38e7fed0d3b1724a8504850`. The follow-up added
  `OneEvidenceRootPerRun` in `03b6cf752be8adaed28fd6078250e33c5861abd5`
  and added `LaunchStampIsCheaper` and the Git stub in
  `bc55d2ddf05e4d409a76a4e6b9cd328c87036f87`.
  `OneEvidenceRootPerRun` prepended a `git.cmd` stub for its third run.
  `RcRunEvidence.GitRaw` asks `ProcessStartInfo` to run `git` with
  `UseShellExecute=false`. On Windows that selects an executable and bypasses
  the command script. The fault probe can reach its `fault-one-warning`
  assertion without having triggered a Git fault. The fixture now makes the
  real Git binary reject invalid config in the fault-only child, retaining the
  assertions on the warning and the child verdict. [Existing checkpoint witness for command resolution](../../scripts/test-checkpoint-git-encoding.ps1)
- For `LaunchStampIsCheaper`, the extract contains the outer exit-code
  assertion only. The child prints an `A6` timing row and the specific
  `FAIL RE LaunchStampIsCheaper.contract` reason. The timing comparison is
  sensitive to process startup. The timed stamp launch includes
  `AssertCurrent`, which runs `git rev-parse` and `git status` for the clean
  fixture. The timed full build checks the source digest and hashes the
  outputs. The other assertions cover
  join correctness, changed output refusal and phase placement. The child
  failure reason is needed to attribute the Windows exit to one of them.
  Both cases run in PowerShell 7. Group 136 repairs the separate assembly
  loader used by the in-process C# corpus.

The previous completed Windows Full, `e7f92f66`, used
`26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`. Its tree contains neither
test file. Its summary gives no earlier Windows evidence for these cases.
[Previous run summary](2026-11-20-run-e7f92f66-ended.md)

## Approving reviews and selection

| Review | Scope and observed selection |
| --- | --- |
| S6c `bb05708f` | Final, Full ordinary scope on Linux. CP-1 ran `RcShadowSampleCSharpTests`, `RcDirtyOrderCSharpTests` and `RcShadowDirtyDifferentialTests`, 19 passed. The wrapper and guard rows were also Linux. [Review](https://github.com/michal-ciechan/Antiphon/commit/6ecb2fbb3e53184dc0f632ee4a200739c1884747) |
| S6b follow-up round 1 | Final, Full on Linux. CP-12 ran `RcRunEvidenceTests`, `NightlyCoreTests` and `LiveLaneRunnerTests`, 13 passed. Both reported methods ran, including the Linux executable Git stub. [Review](https://github.com/michal-ciechan/Antiphon/commit/0bfb4059b5c8649a8244924251bd9c2d7d16b526) |
| S6b AD-1 `8db2556c` and CARD-2008 `1c3d8851` | Linux `RcRunEvidenceTests` passes of 9 and 10 respectively. CARD-2008 ran the class in its Final guard batch. [S6b AD-1 review](https://github.com/michal-ciechan/Antiphon/commit/3e111eb7005b1920fcd7b7989e18805eb4b184dd) · [CARD-2008 review](https://github.com/michal-ciechan/Antiphon/commit/6c7807feb3b6533bbe4d33b4196997c8bc54aa13) |

## Outside the selection

Both classes are Integration tests under `tests/Antiphon.Tests/Scripts`.
The reviews exercised the class logic and recorded Windows UNOBSERVED. Short
Merge verification uses the subject's narrow witness rows, which in these
plans ran on Linux. Linux can unlink a read-only file from a writable
directory, and native Linux Git resolution ran the executable shell stub.
A Linux wall-clock comparison also cannot establish the relative cost of
Windows Git process startup. [Short Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18)

## Linux witness

On `a7e64d91596b55ab015f6df2c74a0977f5a38a11`, the six existing dirty-order
methods and all ten run-evidence methods passed. Only the new cleanup witness
failed. Linux maps `File.SetAttributes(ReadOnly)` to file permissions and can
still unlink the file. The witness uses a second hard link to the same
fixture-owned inode to observe that the original cleanup left the read-only
bit set. The Windows branch of the witness checks that the directory
containing the read-only object was removed.

The fixed source `e3cc0d84d7e4fa3302802762cf8a21d8687ae13c` passed all
seven dirty-order methods and all ten run-evidence methods, with zero skips.
Each revision used one leased Linux build and one leased two-class test run
with the native build output, `UseAppHost=false`,
`ANTIPHON_LINUX_COMPILER_POLICY=0` and `--timeout 570s`. The one-minute loads
at build and test starts were 12.57 and 12.62 for the witness revision, then
9.82 and 13.30 for the fixed revision. The test runs took 1m50s and 1m57s.

## Cheapest pre-land check

These path-to-class selections add a native Windows counterpart when a change
touches scratch Git lifetime, the run-evidence fixture, or its launch seal.

| Path glob | TUnit class filter |
| --- | --- |
| `tests/Antiphon.Tests/Scripts/RcPortCorpus.cs` | `/*/*/RcDirtyOrderCSharpTests/*` |
| `tests/Antiphon.Tests/Scripts/RcDirtyOrderCSharpTests.cs` | `/*/*/RcDirtyOrderCSharpTests/*` |
| `scripts/test-rc-run-evidence.ps1` | `/*/*/RcRunEvidenceTests/*` |
| `scripts/lib/Antiphon.Nightly/RcRunEvidence.cs` | `/*/*/RcRunEvidenceTests/*` |

## Native verification owed

After the live run releases its native lock, use a fresh build with both the
group-136 assembly-loader repair and these fixture changes. Include the group
137 filter in the same Windows pass as CARD-2048 C475, `CheckpointIndexPathsTests`
and `RcShadowSampleCSharpTests`. Retain the TRX and built source SHA.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-g137-win-build -- dotnet build tests/Antiphon.Tests --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-g137-win-tests -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/Antiphon.Tests.Scripts/(RcDirtyOrderCSharpTests*)|(RcRunEvidenceTests*)/*' --timeout 570s --report-trx --report-trx-filename group-137-windows.trx --results-directory .antiphon/rc-g137-windows
```

Confirm execution of the four dirty-order methods
`SameTipPassCannotClearFailure`, `CaseDistinctKeysStaySeparate`,
`CarryWithOpenFailureRefuses`, and `UnknownShaRefuses`, plus
`OneEvidenceRootPerRun` and `LaunchStampIsCheaper`. Include the new
`ScratchRepoCleanupClearsReadOnlyObjects` test. Read the original completed
TRX's full `Output/ErrorInfo/Message` and `Output/StdOut` for the two
run-evidence methods. Preserve the `FAIL RE` reasons and, where emitted, the
`A6` and `fault=... warnings=...` rows. `fault-one-warning` with zero warnings
confirms the stub failure. `stamp-cheaper-than-full-read` with an `A6` row
where `stampMs >= fullMs` identifies the timing comparison.

## Exemption

None for this fixture. The group-136 PowerShell loader defect is separate.
An earlier Windows failure in a different Git fixture also involved
read-only loose objects. [Prior cleanup case](2026-11-20-why-missed-run-e7f92f66-split-8-and-land-graph.md)
