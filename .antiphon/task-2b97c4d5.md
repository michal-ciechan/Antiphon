# Review 2b97c4d5 — round 1 (diff-only), group 126 tz resolution and pipe drain

Subject: feat/card-task-c7a43e0c tip ce291773d354e150de324ca80460804ba0098c42 (base 679b696bd178e3c8bb14b32fcd04a1c26b148435), Code owner c7a43e0c-ba27-4dd0-9f61-a10726e15695.
Verdict: one defect (weakened oracle). Pipe drain is sound.

## Defects

### D-1 The zone precondition no longer observes the process zone (weakened oracle)
- Where: tests/Antiphon.Tests/TestHelpers/ManifestPublicationHarness.ps1:38-41 (`Assert-MpOffset` now reads `Get-TestPhasePinnedTimeZone`); scripts/lib/test-phase-evidence.ps1:1482-1488, 1499.
- Failure: on Linux, `TZ='India Standard Time'` reproduces what Windows does. The process Local zone stays UTC, so `ConvertFrom-Json` turns `2026-10-30T03:02:00+05:00` into `22:02+00:00` with Kind=Local. The pinned zone, by contrast, says Kolkata +05:30. Harness `v3-write` with `MP_EXPECT_OFFSET=330`:
  - tip: `ok:true`
  - base: `timezone-offset expected=330 actual=0 id=UTC`

  On Windows, `TZ` never changes .NET Local. So every cell of the 3x3 ProjectionPreservesLegacyBytes matrix (3 writes + 9 reduces) now runs in the host zone and passes. The live red went green because the precondition changed, not because the process ran in the requested zone. A Windows green no longer proves the legacy bytes are zone-independent.
- Why: the guard exists to prove that the requested zone is the one the code under test uses. That code converts dates with ConvertFrom-Json and DateTime Local. `Get-TestPhasePinnedTimeZone` only feeds `writerEnvironment`. On Windows the assertion therefore reads back its own `TZ` input, and it can only go red if resolution fails.

  The same split damages the writer identity. Two hosts with different real zones but the same `TZ` (say GMT and IST hosts, both with `TZ=Asia/Kolkata`) now produce the same `writerEnvironment` (`...|Asia/Kolkata|+05:30`). The `$same` resume check at test-phase-evidence.ps1:2065 then treats them as one environment. Before this change the strings were different.
- Fix (Code owner c7a43e0c): keep `Assert-MpOffset` on `[TimeZoneInfo]::Local`, or on any zone the code under test really uses. On Windows, do one of two things:
  - put the zone into effect for the code under test, for example by passing the pinned zone to every date conversion in the projection; or
  - report the non-host cells as visibly not applicable on Windows (an OS-branch witness, with the Linux lane owning the cross-zone proof) instead of passing them.

  Keep `writerEnvironment` equal to the zone that is actually in effect.

## Evidence (server2 Linux, reviewer-run)
- Build: Antiphon.Tests through the build slot (lease 7666d1a3, 567 s), exit 0.
- Classes at tip: RcManifestJournalTests + RcRosterRepairTests, one run under lease ba8256cf, total 9, failed 0, skipped 0. That is 6 + 3, both new witnesses included, from fresh TRX.
- Red at base and under mutants, run as scratch git-archive trees with harness pwsh, no build:
  - zone-map with the base lib: red (`Resolve-TestPhaseTimeZone` not recognized).
  - zone-map with the IANA→Windows converter branch removed: red `windows-shaped-iana-unmapped`.
  - successor-drain with the Start-MpCase drain removed: red `successor-drain-timeout`.
  - Tip zone-map and successor-drain: green again.
- Zone probe (TZ → Local / pinned / writerEnvironment):
  - `India Standard Time`: Local=UTC, pinned=IST +05:30. This is the divergence.
  - `Asia/Kolkata` and `:Asia/Kolkata`: both Kolkata.
  - `UTC`, empty, `Bogus/Zone`, `+05:30`: all UTC.
  - Every form except Windows ids gives the same result as base.
- check-evidence-diff over base..tip: commits=2 entries=0 violations=0.
- merge-tree onto origin/master and onto origin/rc/2026-11-01 (both 679b696bd): clean.
- Land graph -Check: printed `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`, which matches the expected values. The run then refused with `evaluation-failed: evaluating Antiphon.sln copy exceeded 5 s` (exit 1, server2 load). It was not re-run with a widened budget, and -Update was not run. The diff touches no project files.
- Cleanup: every bin-rv2b97 and bin-land-audit directory was removed by literal path. The canary was gone.

## Advisories
- A-1: an unknown `TZ` (`Bogus/Zone`, `+05:30`) still falls back to Local without any notice. The brief's "visible fallback" invariant is not met, but behaviour is unchanged from base.
- A-2: the drained child stdout and stderr are thrown away, never awaited and never attached to Fail-Mp reasons. Including them would make `successor-timeout` diagnosable.
- A-3: no witness proves that a successor which really stalls still fails `successor-timeout`. `WaitForExit(60000)` is unchanged, and the lock is non-blocking: a held lock is refused, and v7 in RcManifestRecoveryTests witnesses that. So the drain cannot mask a lock fault.
- A-4: no test reds if `Get-TestPhasePinnedTimeZone` ignores `TZ`. zone-map covers only `Resolve-TestPhaseTimeZone`.
- A-5: the worker report `.antiphon/task-c7a43e0c.md` exists in neither the branch nor the mirror. Its Windows claims, including the Windows -Check approval, were read from the API result only and were not checked here.
- A-6: the whole Unit lane was not run. The commissioned diff-only selection was run in full.
