# Why missed in rc run 419ed669 small assemblies

Windows Full at `5c0883bf203d122fdc7d4556e79087af2a683669` failed
two cases in `session-runner-all-group-1` and one in `pty-host-all-group-1`.
The previous Windows run, `1fe763a9`, was pinned at
`0cfc4ac1ffb41f5af159a0c1db423fe119950267`. The sequencer test and the
custody identity change were added between the two pins. The Herdr witness
and its snapshot code were the same at both pins.

## Git timeout census

`93e24eac5050f9e0ed1ef8c439c098140ccb25f5` added
`C0824_ResetClearsMergeRebaseAndSequencerState` for the slot reset change
in `2d44a3d5036591d5693f354827c393b68b781fb2` and
`6f7cdfa03c0ff0de3eda0a7746ce55d5d2f5bb11`. The fixture uses real Git.
`Budget_table_covers_every_git_method` scans the compiled method inventory
without running the cases it checks. The exact census failed on Linux master
`561591c562f31dd4fe0fc8f940f2ea7e947ce965` with the same single missing row.

Five separate runs of the exact slot reset method on loaded server2 passed
in 1.7547427, 3.0457065, 3.2261554, 2.8375796 and 2.3762847 seconds according
to TRX. On the 24-logical-CPU host, one-minute load was 55.23 to 85.93 at dispatch.
The five-sample nearest-rank p95, rounded up to milliseconds, is 3.227
seconds. This is a provisional Linux estimate. The inherited 60-second
timeout is the existing CARD-1722 loaded-Windows exception. The next
CARD-1722 Windows timing collection should include the sequencer case.
The census now tells authors the measurement and row format and where to
classify a Git-free case. [Performance standard](../test-performance-standards.md#rules) ·
[Previous missing row](2026-11-21-why-missed-rc-run-3528e6b1-session-runner-group-1.md)

CARD-2082 inventories this exact census and marks its class **SPLIT**.
The proposed project-scoped invariant selection would catch the omission
once active. The active guard union compiles in `Antiphon.Tests`; the census
compiles in `Antiphon.SessionRunner.Tests`. On the introducing range the
ordinary impact selector returned the C1722 reverse edge. The short Merge
selector omitted it, consistent with selecting narrow subject rows and
using fixed and hand-table selections. The cheapest current check is to add
`/*/*/C1722GitTimeoutBudgetTests/Budget_table_covers_every_git_method` to the
closed selection whenever a fixture in the reflected roster gains a test.
[CARD-2082 inventory](../superpowers/plans/2026-11-26-card-2082-invariant-inventory.md) ·
[Short Merge contract](../orchestration-loop.md#merge-verification-is-short-2026-11-18)

## Herdr idle-label scheduling

The Windows stack points to the poll of `f.Saved.TabLabel` after advancing
the fake clock. The fixture resolves `Saved` from disk every time.
`TryLoad` returns null on an I/O or access failure, and the null-forgiving
operator left the poll to dereference that result. The test already saw a
live subscription and the baseline's saved label before the failure.
The likely Windows-only race is between an unlocked read and the timer's
atomic replacement. Windows file sharing can reject either side of that
overlap. Unix rename permits the old file to stay open. The fixture now
takes the writer's existing per-path mutex and asserts that the snapshot is
readable. The idle witness still requires the renamed label and additionally
checks a validated sequence-2 observation, the baseline and timer getter
calls, and a live session. A timer that fails to run still fails the witness.
[Herdr label ownership](../herdr-sessions.md#following-live-placement-labels-card-0462) ·
[.NET StreamReader](https://github.com/dotnet/runtime/blob/v9.0.23/src/libraries/System.Private.CoreLib/src/System/IO/StreamReader.cs) ·
[Windows file-sharing rules](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew)

On Linux master all 20 exact attempts passed and zero skipped. The one-minute
load at invocation was 29.15 to 126.99. The Windows exception remains the
only observed red for this fixture. Windows confirmation is still pending.

The two rc pins carry the same CARD-0462 witness introduced at
`bebd14dbadada9770fc76476e00f74a00eb30a5f`. It exercises an owned fake
server, fake clock and temporary snapshot. The small-assembly runtime
witness belongs in the owning test selection. The CARD-2082 inventory and
the repository guard union serve different coverage here. An exact Windows
idle test using the shared reader/writer lock verifies the original race
path during the next qualification.
[CARD-0462 test plan](../superpowers/plans/2026-09-15-card-0462-follow-live-herdr-labels-plan.md)

## PtyHost foreign platform admission

The landed CARD-2067 port `0ab54f1cc35cd90b8c4f4eccbdb8f7c1a3b41ae6`
added a required Linux kernel generation to the host journal for a Linux
binding. At the rc pin, `HostSession` creates the journal before checking
whether the OS admits the configured backend. On Windows the foreign
binding is Linux. The kernel identity probe returns null there and the
constructor throws, so `LaunchAsync` returns `launchFailed` and the
follow-up custody read throws `verification_custody_identity_mismatch`.
On Linux the actual foreign binding is Windows, which bypasses the Linux
probe. The original test passed 1/1 on Linux master because it checked
only that launch replied with some error and a custody receipt was absent.

CARD-1614 requires platform refusal without native intent or a receipt.
CARD-2067's strict generation check applies to admitted Linux custody.
The fix evaluates platform admission before selecting the journal identity
requirements, retains the observable UnsupportedBackend disposition, and
keeps kernel identity mandatory for an admitted Linux binding. The two
witness arms use the actual opposite backend and a forced Linux-backend
refusal, respectively. The forced arm runs on Linux, where a premature
kernel probe would otherwise succeed. Both require the exact launch error
and custody state, zero payload/native-start/producer-receipt side effects,
and no Linux kernel identity in the refused journal. On Linux at
`38d4fb810b5e7136a79ec0ac6fa26d0be12655b3`, the actual opposite-backend case
passed and the forced arm failed on the unexpected kernel identity. The
checkpoint ran both cases with clean source and verified build provenance.
[CARD-1614 contract](../superpowers/plans/2026-10-29-card-1614-linux-custody-repairs-plan.md) ·
[CARD-2067 provenance](2026-11-26-card-2067.md)

CARD-2067's recorded native selection covered Linux receipt and release behavior,
with a separate `Antiphon.Tests` guard row. Replaying the port's ordinary
impact selection included `HostCustodyPlatformTests`. Replaying short Merge
omitted it. The exact foreign-platform witness on both backends would
exercise the missing admission branch. It is a direct host behavior test
outside CARD-2082's current-tree invariant inventory.

Master baselines, selector replays, load samples, TRX and repair checkpoint
receipts are retained under `/work/evidence/task-1e4dbb6e`.
