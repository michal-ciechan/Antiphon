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
The delay is before `slot-held`. After `go`, both restores still finish together.

The fixture model resolves a container id by scanning `$C1192_ROOT/containers/*/id`.
The scan was `[ -f "$candidate/id" ] && [ "$(< "$candidate/id")" = "$name" ]`. The script
runs with `set -euo pipefail`. A sibling `docker run --rm` unlinks that id after the `-f`
test and before the read. Bash then aborts the inspect and writes
`No such file or directory`. `c849_fixture_resource_state` accepts only an exact Docker
absence line, so an unknown result (state 2) makes `c849_fixture_launch` return 2. Both
`ready` files already exist, and the diagnosis is `FixtureConcurrentRestoreFailed`.

A tight loop of that scan while another process removed and recreated the id aborted
653 of 20000 times. One traced failure of the cleanup-hardened fixture captured the
inspect result exactly: exit 1, and the output was the bash line
`No such file or directory` for `containers/c849-...-restore-p2/id` followed by
`Error: No such container: <64-hex>`. `c849_fixture_resource_state` rejects the
extra line, so the launch state is 2. The trace was `PLANT_OK` for both restores,
then `LAUNCH_STATE` state=2 for one and `LAUNCH_OK` state=1 for the other. Same
facts as the rc row: barrier code 2, `FixtureConcurrentRestoreFailed`,
`late-slot-go-observed=0`, fixture exit 0, about 9 s.
On that hardened fixture, Linux TUnit of the four C1192/C1514 methods was 19/20
(iteration 12, 9.253 s) and a direct bash sweep was 2/168. The id read now treats a
vanished candidate as not this container and discards the diagnostic. The missing
restore id still produces `Error: No such container: <id>` and state 1.

The same bash model runs under WSL on the Windows rc host. This session cannot run
that Windows test. A slower unlink widens the same window.

A failed post-ready unlink can produce the same facts on its own. The cid line was
`rm -f -- "$cid_dir/id"; rmdir -- "$cid_dir" || true`: under `set -e` the `|| true`
covers only `rmdir`. The model's `rm -rf` of the `--rm` directory could replace the
payload status. Both unlinks are now best-effort. A forced non-zero unlink then still
reports `late-slot-go-observed=1`. A real restore failure (`C1192_FAULT=restore-p2`)
still reports `FixtureConcurrentRestoreFailed` and `late-slot-go-observed=0`. The
traced Linux failures were the id scan, not that unlink: the plant had succeeded and
the launch failed in `c849_fixture_resource_state`.

An inherited `C589_SLOT_WAIT_SECONDS` of 5 or 8 does not produce this diagnosis. The
late-slot mode sets the slot deadline to the injected delay plus 120 s. Those
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
Linux green on one pass does not exercise the id-scan race. The cleanup-hardened
fixture still failed 1 of 20 TUnit runs and 2 of 168 direct runs with the rc facts.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/fixtures/c1192-workspace-contract.sh` -> `/*/*/RemoteScriptContractTests/C1514_Late_second_slot_still_observes_volume_go`

That filter on Linux catches this when it is repeated. One pass does not. The id scan
failed about once in 20 TUnit runs and twice in 168 direct runs. Any new witness for a
Windows-native failure still needs a Windows-native run of that method before the rc.
The next rc can run just that method on the desktop between runs.

The guard for the scan is `c1192_read_container_id`: a vanished id is not this
container, and the bash diagnostic is discarded. The guard for a short inherited slot
bound is the late-slot deadline, the injected delay plus 120 s. The guard for a
cleanup unlink replacing a finished restore is the best-effort cid unlink in
`c849_fixture_launch` and the `--rm` unlink in the fixture model.

## Exemption

none
