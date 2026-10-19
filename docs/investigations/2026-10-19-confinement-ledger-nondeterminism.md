# Confinement-ledger tests flip when the task token is unset

Status: **confirmed**. The five `CheckpointConfinementLedgerTests` methods fail together when the test process is bound to `ANTIPHON_TASK_ID` and `ANTIPHON_TASK_TOKEN` is absent. `CheckpointApp.ExecuteAsync` returns `owner-unverified` (exit 7) before the scheduler starts. The slot callback never rewrites the fixture file, and the ledger driver is never called. This is a test environment leak. The owner guard is doing what it is written to do. It is not a filesystem, clock, or ledger-stat bug.

The same test blob is green when those four owner variables are unset.

## What is the same

Test file blob `e4e39e582ba67b1c6fc80c9c122069ca55e34d1b` at `1f72e20158b99434cb6398fb7efbbd133d1c5569`, `42cbbd0417a2611f3a690e6671b13c9ba51fbf34`, `61117dd1663d2f47ce4708c18b609b8936491986`, and this checkout `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0`.

`tools/Antiphon.Checkpoints/Execution/TaskOwnerGuard.cs` blob `a29c2f94f6029b98de3e598687980e46d7646e1a` at all four. `tools/Antiphon.Checkpoints/CheckpointApp.cs` blob `b921c6b240416b62fdc95d20a84df6ac0b8b2871` at `42cbbd04` and at this checkout. The pass at `42cbbd04` (task 541c1ad7) and the fail at `42cbbd04` (task ede44ef3) are the same product.

Host for the stored rosters: container `a6457bc89508`, Debian 12, `/tmp` is ext4 `rw,relatime,data=ordered`, umask `0022`, timezone `Etc/UTC`, `TZ` unset. The reproduction ran on that same host.

## The five methods, and only those

`CheckpointConfinementLedgerTests` has 16 methods. Eleven call `BuildConfinement` directly. Five call `RunScheduledAsync`, which calls `CheckpointApp.ExecuteAsync` with a runtime that sets `Driver`, `Slots`, and `SourceCapture` and does not set `EnvironmentLookup` (`CheckpointConfinementLedgerTests.cs:439-442`). The default lookup is `Environment.GetEnvironmentVariable` (`CheckpointApp.cs:17`).

Roster TRX outcomes for those five:

| Method | ed312042 pass `1f72e201` | 541c1ad7 pass `42cbbd04` | ede44ef3 fail `42cbbd04` |
|---|---:|---:|---|
| `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch` | 5.288s | 8.378s | failed 3.385s, file was `module.exports = 12;` (`:265`) |
| `portable_stat_rewrite_keeping_size_and_mtime_during_the_slot_wait_is_refused_before_launch` | 6.081s | 8.549s | failed 3.853s, file was `module.exports = 11;` (`:240`) |
| `file_changed_during_the_slot_wait_and_restored_by_the_build_is_refused_before_launch` | 3.694s | 5.385s | failed 1.216s, `LedgerCaptures` was `[]` (`:159`) |
| `scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release` | 3.335s | 4.880s | failed 1.352s, `LedgerCaptures` was `[]` (`:131`) |
| `strict_admission_hash_in_the_manifest_refuses_the_build_before_launch` | 10.345s | 12.531s | failed 3.293s, `BuildLedgers` was `[]` (`:356`) |

The other eleven methods passed in the failing roster (`/tmp/rc-linux-ede44ef3/roster/run.trx`). Isolated reruns of the five, at both `42cbbd04` and `61117dd1`, failed the same assertions (`/tmp/rc-linux-ede44ef3/isolate-candidate` and `isolate-baseline`). `owned-directory` in that same isolate script passed; it is not one of these five.

## Why the bytes look like a missed rewrite

`BeginRepo` writes `node_modules/pkg/f0012.js` as `module.exports = 12;` and `f0011.js` as `module.exports = 11;` (`CheckpointConfinementLedgerTests.cs:475-476`). The build callback writes those same strings (`:261`, `:236`). The value the test expects, `98` or `99`, is written only from `RecordingSlotClient.AcquireAsync` (`:258-260`, `:233-235`, `:521-523`).

The rewrite failure is at line 265. Line 264, `BuildLedgers.ShouldBeEmpty()`, had already passed, so `RunAsync` did not run the build. The file still holds the seed because `AcquireAsync` never ran either. `LedgerCaptures` stays `[]` because `CaptureLedger` is only called from `RunScheduler.BuildOneAsync` (`RunScheduler.cs:341-344`), which does not start.

`AgeWorktree` sleeps `RacyWindow / 1_000_000 + 250` ms, and `RacyWindow` is `2_000_000_000` (`BuildConfinement.cs:48`), so about 2.25s. The rewrite and portable failures are one sleep plus a fast return. The scheduler and restored failures have no sleep and return in about 1.2–1.4s. The strict failure stops on the first of two runs (`:356`), so it pays one sleep, not two. The green durations include the ledger walk of the 2,000-file tree.

## The gate

`TaskOwnerGuard` is bound when `ANTIPHON_TASK_ID` is non-empty (`TaskOwnerGuard.cs:39-42`, `:59`). `EnsureLiveAsync` returns true immediately when unbound (`:70-71`). When bound, a missing API, a missing token, or a non-guid id logs `owner read failed: missing task API, token, or valid task id` and ends as `owner-unverified` (`:79-85`). That path does not wait out the three-minute uncertainty budget.

`CheckpointApp.ExecuteAsync` runs the scheduler only when that check and the source guard both succeed (`CheckpointApp.cs:199-201`). Otherwise it records the owner reason and returns `ExitCodes.OwnerEnded` (7) without calling the driver (`:239-247`, `ExitCodes.cs:12`).

Sibling checkpoint tests that call `ExecuteAsync` pass `EnvironmentLookup = _ => null` (for example `CheckpointSourceExecutionTests`, `CheckpointExecutorLogTests`, `CheckpointLaunchCleanupTests`). These five do not, so they see the process environment.

## The variable between the stored runs

Passing drivers `/tmp/rc-linux-ed312042/run-roster.sh` and `/tmp/rc-linux-541c1ad7/run-roster.sh` unset headed, distiller, broker, and card-0133 variables. They do not unset `ANTIPHON_TASK_TOKEN`.

Failing driver `/tmp/rc-linux-ede44ef3/run-all.sh` and `run-isolate.sh` also unset `ANTIPHON_TASK_TOKEN` and `TUNIT_MAX_PARALLEL_TESTS`. They leave `ANTIPHON_TASK_ID`, `ANTIPHON_API`, and `ANTIPHON_SESSION_ID` in place. This session's process has all four set (id 36 characters, token 64, API 37, session 36). The re-gate agent was the same shape of bound task.

A bound id with the token removed is `owner-unverified` before any ledger code. A live token, or no task id, lets the scheduler run. `TUNIT_MAX_PARALLEL_TESTS` is not required: the isolating run below did not unset it and still failed.

Slot waits on the failing roster builds were 0s (`docs/investigations/2026-10-19-rc-linux-regate2.md`). The broker was not the difference.

## Reproduction

Checkout `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0`, blobs above. One build, then one method, `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch`, through `scripts/build-slot.ps1`.

Build: lease `cf38edb4-4cde-4f75-9921-a935a66b9222`, waited 0s, held 119s, 1083 warnings, 0 errors, `UseAppHost=false`, output `bin-c5151-ledger/`. Broker at start: enabled, budget 4, occupied 0, no waiters. Evidence under `/tmp/c5151-ledger-repro/` (not in git). The `bin-c5151-ledger` directories were removed after the runs.

| Case | Environment | Result |
|---|---|---|
| `fresh-unbound` | empty `TMPDIR` `/tmp/c5151-empty-fresh`; task id, token, API, and session unset | passed, 4.749s. Lease `d7375190-b26a-4197-82be-eb28e1c6a3c5`, held 9s. Captured report `20261019-081008-af32`: build `slotReason=refused: tampered`, which is the refusal this test wants. Checkpoint exit 2, test passed. |
| `fresh-token-unset` | empty `TMPDIR` `/tmp/c5151-empty-token`; task id, API, and session kept; token unset | failed 3.834s at `:265`, file still `module.exports = 12;`. Lease `aa7ea8a6-1268-4784-b8df-0925dfd95f08`, held 8s. Report `20261019-081018-e73b`: `reason=owner-unverified`, exit 7, row `CP-1` state `owner-unverified`. `executor.log`: `owner read failed: missing task API, token, or valid task id`. |
| `failing-script` | default `/tmp`; the ede44ef3 `unset` list, including the token | failed 3.639s at `:265`, same bytes. Lease `1136c40f-9ed2-4072-b403-7d0f9d0b4c89`, held 7s. The run directory was deleted before the copy; the assertion matches `fresh-token-unset`. |

The empty temp directory still failed once the token was the only owner variable removed. Leftover files under `/tmp`, mount options, umask, and timezone do not separate the two results.

## What else was on the host

At the process snapshot just before the build (about 08:07Z) another agent was building `tests/Antiphon.Tests` to `bin-c1340` (label `c1340-c599-build`). Several other grok sessions were live. By 08:08Z the build-slot broker had occupied 0 and this build waited 0s. The reproduction failure does not depend on that neighbor: the token-unset case failed with a free slot and an empty temp directory.

## Ruled out

- Different test or owner-guard source. Blobs match, including the pass and the fail at `42cbbd04`.
- statx, ctime, or a timestamp-preserving rewrite. Admission never ran. The `12` / `11` bytes are the seed from `BeginRepo`.
- A stale ledger or seal file, or dirt under `/tmp`. An empty `TMPDIR` reproduced the failure.
- Build-slot wait or `TUNIT_MAX_PARALLEL_TESTS`. Waits were 0s, and unsetting only the token was enough.
- The unrelated `/proc/<pid>/stat` race in the same roster. That was `CheckpointOwnedOutputTests.changed_inventory_is_refused(directory)`, and its isolate passed.

## Remaining uncertainty

The other four methods were not executed again in this session. They share `RunScheduledAsync`, and their roster failures are the first assertion after `ExecuteAsync` returns (empty captures, or the strict test's default build never launching). The historical passing processes did not record their environment. Their scripts left `ANTIPHON_TASK_TOKEN` set, so either an unbound parent or a bound parent with a live token is consistent with those greens. Both pass this gate. The failing script is the one that removes the token.

## Not done, noted

Smallest fix: unset `ANTIPHON_TASK_ID` and `ANTIPHON_SESSION_ID` in the same driver statement as `ANTIPHON_TASK_TOKEN`, or pass `EnvironmentLookup = _ => null` from `RunScheduledAsync`. Red witness, already reproduced: `rewrite_keeping_size_and_mtime_on_the_real_file_system_is_refused_before_launch` with a guid `ANTIPHON_TASK_ID` and no token fails at `CheckpointConfinementLedgerTests.cs:265` with report reason `owner-unverified`; the same method passes when the task id is unset.
