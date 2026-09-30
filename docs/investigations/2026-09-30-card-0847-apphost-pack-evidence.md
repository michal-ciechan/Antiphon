# CARD-0847 Linux apphost pack proof

Source change: `fc65b582cc4e5e2d609c5f05886dda75553363f5` on
`feat/card-task-d417892b`, based on `1bcb939a25b3f3af91bd8625855f7a741d492959`.
`CopyOwnedPtyHostAppHost` now restores `Antiphon.PtyHost` with `UseAppHost=true`
before its inner build. The target condition is unchanged before and after:
`Condition="'$(OS)' != 'Windows_NT'"`; Windows remains skipped.

Each proof was a separate `dotnet build tests/Antiphon.Tests` under
`scripts/build-slot.ps1`, with `--property:UseAppHost=false` and a distinct
`OutputPath=bin-c847-.../`. No pre-seeded cache or `UseAppHost=true` outer build
was used to make the proof pass.

| Proof | NuGet cache | Result |
|---|---|---|
| Start ref | Fresh empty `/tmp/antiphon-c847-red-9156` | Exit 1, one MSB3030: `src/Antiphon.PtyHost/obj/Debug/net9.0/apphost` not found. No `microsoft.netcore.app.host.*` directory downloaded. |
| Fixed ref | Fresh empty `/tmp/antiphon-c847-green-9156` | Exit 0; restore downloaded `microsoft.netcore.app.host.linux-x64/9.0.20`, and `tests/Antiphon.Tests/bin-c847-green/Antiphon.PtyHost` exists (75,368 bytes). |
| Fixed ref | Same cache, now warm | Exit 0 with independent `bin-c847-warm/` output. |

The local build logs are `.antiphon/card0847/red.log`, `green.log`, and
`warm.log`. They are producer-owned ignored evidence, not committed artifacts.

The complete CARD-0418 Final `CP-1`–`CP-13` manifest ran at
`.antiphon/checkpoints/20260930-103448-c3bc/` on the fixed source commit.
Ten rows were green. `CP-1` was 3,557 passed, one failed, 33 skipped; its
unchanged `CheckpointTaskOwnershipTests.late_settlement_after_owner_unverified_cancels_the_running_row`
hit the same six-second timeout documented in round 33. An exact whole-lane
rerun at `.antiphon/checkpoints/20260930-112510-defb/` passed 3,558 of 3,558
executed tests, with 33 platform skips.

`CP-6` was 30 passed and one failed in both the full sweep and the exact row
rerun. The failure is
`TaskPlatformDispatchTests.C772_Allowed_hosts_and_kinds_still_dispatch`:
the task stayed `Queued` where the test expected `Dispatched`. An unlisted
diagnostic class run, justified by that repeated failure, used
`run-checkpoint.ps1 -NoBuild` and produced 10 passed, one failed at
`.antiphon/card0847/diag-c772/DIAG-C772-20260930-114450-4392/run.trx`.
That test and its production dispatch sources are unchanged by CARD-0847;
the cause of the repeated failure remains undetermined. Round 38 had
passed the same full CP-6 selection (31/31).

`CP-8` was 69 passed, two failed: the inherited
`PinnedAgentKindTests.T1/T2` `codex_desktop_unqualified` refusals documented
in earlier CARD-0418 rounds. Its plan expectation is zero *introduced*
failures. All other test rows passed: CP-2 381/381, CP-3 34/34, CP-4 64/64,
CP-5 66/66, CP-7 321/321, CP-9 19/19, CP-10 1/1 real-browser PDF, and
CP-11 125/125. CP-12 client tests and CP-13 production client bundle exited
zero. Positive controls and PC-1–PC-30 remain pending for SourceLanding
Mutation; this build fix does not discharge them.
