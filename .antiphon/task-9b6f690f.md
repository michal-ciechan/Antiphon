# Code 9b6f690f: CARD-1681 Linux custody drain EIO after root exit

Outcome: no source change needed. The CARD-1681 fix and its witnesses are already on master at the
task base 868704eddf0022bd88329f445759274fc4bafff4, landed through CARD-1617 S3 (Code f4830a56):

- 30c83936887a09c8382363dd1f496f3c13f7ff8d test(pty): CARD-1681 S3a red-first witnesses + inert stubs
- 25f5d49762249c87cb07d350cedb37e87cbd9d20 fix(pty): CARD-1681 S3b EIO after root exit is end of output
- 8297ed9dd / b2ea37b4d CARD-1617 review D-1 S4a/S4b (RootExited composition), Review 7c02ed68 round 2 CLEAN

Plan: docs/superpowers/plans/2026-11-03-card-1617-s3-card-1681-amendment.md (and
2026-11-03-card-1617-review-d1-amendment.md).

## Seam

`PtyAgentRunner.ReadStreamAsync` `catch (IOException)` (src/Antiphon.Agents.Pty/PtyAgentRunner.cs:347):
on the Linux custody lane (`_custody` and `_containment` set) an EIO (exact `IOException`, HResult 5)
is flushed as end of output only when `RootExited(exitObserved, /proc/<pid>/stat)` is true (observed
exit, or stat gone/Z/X/PF_EXITING). An EIO with a live root, an EIO-shaped subclass, or any other
error keeps `_custodyReadFailure`. ReadActive (tree emptiness), the empty-tree receipt rule, the
CARD-1617 bound and the Windows job-object branch are untouched.

## Checkpoints (tool, server2 Linux)

Green at HEAD 868704eddf0022bd88329f445759274fc4bafff4, run 20261103-213404-89a8
(`start --plan <s3 amendment> --rows CP-3,CP-4,CP-7`), verdict GREEN exit=0, unlisted none,
`validate` SOURCE VALID rows=3:

    CHECKPOINT CP-3 commit=868704eddf0022bd88329f445759274fc4bafff4 build=ok filter=/*/*/LinuxCustodyReleaseHostTests/* executed=13 passed=13 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=868704eddf0022bd88329f445759274fc4bafff4 sourceState=clean buildSource=verified
    CHECKPOINT CP-4 commit=868704eddf0022bd88329f445759274fc4bafff4 build=reused filter=/*/*/(HostCustodyPlatformTests*)|(CustodyReceiptBackendTests*)/* executed=6 passed=6 failed=0 skipped=0 slot=granted waited=15s dirty=0 source=868704eddf0022bd88329f445759274fc4bafff4 sourceState=clean buildSource=verified
    CHECKPOINT CP-7 commit=868704eddf0022bd88329f445759274fc4bafff4 build=ok filter=/*/*/Card1681PtyHangupTests/* executed=30 passed=30 failed=0 skipped=0 slot=granted waited=75s dirty=0 source=868704eddf0022bd88329f445759274fc4bafff4 sourceState=clean buildSource=verified

Counts exceed the S3 table (9/15) because later CARD-1617 commits added tests to both classes
(Review 7c02ed68 recorded 9 and 30 at da8f1a9d0).

Red re-confirmed at the S3a commit 30c83936887a09c8382363dd1f496f3c13f7ff8d (detached scratch
worktree, run 20261103-214008-3105, `--rows CP-5,CP-6`), verdict RED exit=1 as the plan expects:

    CHECKPOINT CP-5 commit=30c83936887a09c8382363dd1f496f3c13f7ff8d build=ok filter=/*/*/LinuxCustodyReleaseHostTests/(C1681_*)|(C1617_Failed_termination_retries_on_capped_backoff*)|(C1617_Bounded_release_still_finalizes*) executed=4 passed=1 failed=3 skipped=0 slot=granted waited=15s dirty=0 source=30c83936887a09c8382363dd1f496f3c13f7ff8d sourceState=clean buildSource=verified
    CHECKPOINT CP-6 commit=30c83936887a09c8382363dd1f496f3c13f7ff8d build=ok filter=/*/*/Card1681PtyHangupTests/* executed=15 passed=10 failed=5 skipped=0 slot=granted waited=105s dirty=0 source=30c83936887a09c8382363dd1f496f3c13f7ff8d sourceState=clean buildSource=verified

CP-5 failures: C1681_Eio_after_root_exit_drains_to_receipt, C1617_Bounded_release_still_finalizes
(W3), C1617_Failed_termination_retries_on_capped_backoff (W2 last leg), all `Unknown`
(observation_failed); the guard C1681_Eio_while_root_alive_stays_drain_failure was green.
CP-6 failures: C1681_Eio_after_root_exit_is_end_of_output and 4 ProcStatShowsExit cases.

Unlisted builds: one build-slot build of tools/Antiphon.Checkpoints (bin-c1681tool/, deleted) to
run the tool; no other build or test.

## Not run (scope)

The verification profile names the whole Unit lane; the brief says minimal testing (one red witness
plus one narrow green, the rc roster is the gate) and this task changes no source, so the Unit lane
was not run. Windows-owed rows (parent plan PtyCustodyTests / HostCustodyTests) were not run;
nothing may run on the desktop.

## Pending PCs (SourceLanding Mutation)

PC-11..PC-14 (S3 amendment) and PC-15..PC-19 (review D-1 amendment) stay pending. Known survivor:
Review 7c02ed68 A-1, PC-19 first half: the production call site passing a null stat reader
(`RootExited(_exitTcs.Task.IsCompleted, null)`) survived CP-3; no deterministic instance-level
witness for the /proc wiring. An unlanded sweep exists at b8caea0c4 (feat/card-task-b9b25805,
"waived, not CARD-0478 evidence").
