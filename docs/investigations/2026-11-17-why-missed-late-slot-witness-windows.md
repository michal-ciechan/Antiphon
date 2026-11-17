# Why missed: C1514 late-slot witness on Windows

rc run f2de5b30 (Windows Full, rc/2026-11-26 = 29ba0c5002e642c4f4f91805be7e17660a40906d), chunk
antiphon-all-profile-2156: `RemoteScriptContractTests.C1514_Late_second_slot_still_observes_volume_go`
failed at RemoteScriptContractTests.cs:1103 (9.8 s). `late-slot-go-observed` was `0`.
Additional facts: `late-slot-barrier-code=2`, `late-slot-diagnosis=FixtureConcurrentRestoreFailed`,
`C1192_EXIT=0`. CARD-1514 Debug task e187afde.

The original `C1192_Race_barriers_observe_volume_state` passed natively in the same run (1/1).
The 4 s poll-count fix is doing its job. This failure is the late-slot witness itself.

## Cause

`FixtureConcurrentRestoreFailed` is raised only after both `slot-held` markers and both
`ready` files have been observed, when `wait` on either restore probe is non-zero
(`c849_fixture_race_restore_barrier` in `scripts/c590-remote.sh`). A deadline miss before
`ready` is `FixtureRestoreBarrierFailed` or `FixtureBuildSlotTimeout`. The 9.8 s duration
contains the witness's real 8 s delay, so the delay finished and the probe then exited
non-zero. A 2700 s go deadline and the 180 s WSL harness cap do not fit that duration.

The probe writes `ready` and then still has to unlink its container directory (`docker run
--rm` in the fixture model) and its cid directory (`c849_fixture_launch`). The cid line was
`rm -f -- "$cid_dir/id"; rmdir -- "$cid_dir" || true`: under `set -e` the `|| true` covers
only `rmdir`. A failed `rm -f` aborts the launch after `ready` exists. The model's `rm -rf`
of the `--rm` container had the same effect: it replaced the payload status.

On the unfixed fixture, a container cleanup that returns 1 after the payload reproduces the
rc facts exactly: barrier code 2, `FixtureConcurrentRestoreFailed`, `late-slot-go-observed=0`,
fixture exit 0, about 10 s, probe status 1. With the fix, the same forced failure of the
container unlink and the cid unlink still reports `late-slot-go-observed=1`. A real restore
failure (`C1192_FAULT=restore-p2`) still reports `FixtureConcurrentRestoreFailed` and
`late-slot-go-observed=0`.

An inherited `C589_SLOT_WAIT_SECONDS` of 5 or 8 does not produce this diagnosis (it refuses
before `ready`, or as `FixtureBuildSlotTimeout`). The late-slot mode now sets the slot
deadline to the injected delay plus 120 s, from the same delay the probe sleeps. Those
inherited values then observe go.

## Introducing commit/card

introducing commit/card: 7d48ad791 (CARD-1514 witness; the C1192 race fixture is
b9b0fa6aeb8d8d0d90de67ac984fa24bfe27b9d4)

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- not found. The witness was added by Debug fde20c05 and landed through Merge 262.
  Test-only rc repairs skip Review. Merge verification waives the whole
  `RemoteScriptContractTests` class. The witness was run by exact method on Linux only.

## Outside the selection

why the failing test was outside that selection (lane/category/path): the class is waived
for the 570 s cap, and `tests/impacted-classes.json` `remote-scripts` does not list
`scripts/fixtures/c1192-workspace-contract.sh` or `C1514_Late_second_slot_still_observes_volume_go`.
Linux green does not exercise a WSL unlink of a cid file and a `--rm` directory that have
lived for the injected delay.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/fixtures/c1192-workspace-contract.sh` -> `/*/*/RemoteScriptContractTests/C1514_Late_second_slot_still_observes_volume_go`

That filter on Linux does not catch this. Any new test in `RemoteScriptContractTests`, and
any new witness for a Windows-native failure, needs a Windows-native run of that method
before the rc. The next rc can run just that method on the desktop between runs.

The guard that catches the short-bound variant is the late-slot deadline, which is the
injected delay plus 120 s. The guard that catches a cleanup unlink replacing a finished
restore is the best-effort cid unlink in `c849_fixture_launch` and the `--rm` unlink in
the fixture model. A Linux-only exact-method run will not.

## Exemption

none
