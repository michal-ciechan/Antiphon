# Why C0824WorkspaceSlotRoleGateTests missed rc/2026-11-11

Debug task bd07f5fe. Qualification run 4e0d1159, chunk
`antiphon-all-group-88.trx`, rc cut
`3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e` (equal to master at the cut).
The desktop paths
`C:\Antiphon\releases\candidates\rc-2026-11-11-3364197bf1cb\logs\2026-11-08-0936-4e0d1159\antiphon-all-group-88.trx`
and `antiphon-all-group-88-tests.log` are not mounted on this runner.
The chunk summary supplied with the brief is 15 failed in
`C0824WorkspaceSlotRoleGateTests`. The per-test message and stack for
run 4e0d1159 were not readable here.

A leased Linux run of the class at that SHA passed. Build
`tests/Antiphon.Tests` with `UseAppHost=false`, slot waited 91s, lease
`ece68f92-b75d-4790-8203-463b3767b23f`, 0 errors. Test filter
`/*/*/C0824WorkspaceSlotRoleGateTests*/*`, slot waited 45s, lease
`be1d7374-0f02-437a-b4ab-1ebd39c38831`: total 20, failed 0, succeeded
20, skipped 0, duration 37s 584ms. No product or test change.

## What the 15 are

The class at this SHA has 20 cases. Fifteen construct
`C0824SlotTransportWorld` and therefore the process-wide scratch
repository:

- `Case_reuses_one_scratch_repository_and_spawns_no_git`
- `Listed_role_leases_a_slot_on_the_real_runner`
- `Unlisted_role_gets_the_legacy_mirror_and_never_holds` (4 arguments)
- `None_stops_new_leases_while_releases_continue` (2 arguments)
- `Removed_role_slot_is_released_normally`
- `Hot_reload_applies_to_the_next_lease_without_restart`
- `Retry_keeps_the_first_decision_of_its_preparation` (4 arguments)
- `Decision_is_recorded_before_the_mirror_is_sent`

Five cases use `RemoteWorkspacePreparerTests.Rig` and never enter that
world: `Flag_off_or_no_feature_ignores_the_list` (4 arguments) and
`Flag_off_hosted_loop_sends_no_release`. The previous native Windows
run of the older 19-case class (task b5e998ee, 2026-11-06) was 5
passed and 14 failed, and the 14 names were exactly the world cases
that existed then. `Case_reuses_one_scratch_repository_and_spawns_no_git`
is the fifteenth world case, added with the shared scratch. A chunk
count of 15 matches every world case failing and the five Rig cases
passing. That match is from the class shape and the earlier name list.
It is not a read of this run's TRX.

## Classification

Windows-only shared-fixture failure on the fixed master. Linux at the
rc SHA is green, so this is not the pre-fix per-case scratch-git storm
reappearing as fifteen independent inits. The shared repository is one
`Lazy` (`C0824SlotTransportWorld.BuildSharedScratch`). One throw there
fails every later world case with the same exception and leaves the
five Rig cases alone.

The last message actually measured on Windows for this class is
`TimeoutException: scratch git exceeded its operation deadline` from
`C0824SlotTransportWorld.RunGit`, at the unchanged one-second
`GitBudget`. Native task b5e998ee on `DESKTOP-KTLKPIF` (git
2.50.1.windows.1, 8 processors, qualification load, mean CPU 89%)
recorded 26 scratch-git calls, 12 succeeded, 14 timed out, in `init`,
`config` and `add`. Successful-only p95 was 688 ms. `commit`,
`rev-parse` and `clone` were never observed because the cases died
before them. The shared build still runs those commands, including
`commit` and `clone`, under the same one-second budget
(`C0824SlotTransportWorld.cs`, `GitBudget`). A single timeout is cached
by `Lazy` and fails all 15 world cases.

A Windows origin-url form that `EncodeOriginUrl` does not accept would
produce the same 15-and-5 split (`InvalidOperationException: shared
scratch origin url is not the template path`). Run 4e0d1159's stack
was not available, so that alternative is open. The one-second budget
was not widened. No assertion was changed.

## Introducing change

CARD-0824 S1c introduced the class. Tests
`ec01445ab2f75dacd7f76b88aac45ff3ead007ed`, product
`2561829864d86fd8db54c44c86f772ad09489f74` (`feat(card-0824): S1c role
allow-list for slot leases`). The in-source note records the first
Linux green at that SHA on server2, 2026-11-03.

The Windows failure of the class was first recorded on qualification
run 472c7495 (rc/2026-11-07, group 88): 14 world cases, all
`TimeoutException` at `RunGit`. Two later test-only repairs landed on
master and left the deadline in place:

- Limiter. Source `b8ded161bcc186fc5a94f7db7550a55d9e3c5dfe`, red guard
  `eda2c8bb11a5d6225561f733ee97d8134bda398a`, master cherry-pick
  `44db908e4bf5592434b942be8cd24cfc5349677f` (2026-11-06 21:36:44
  +0000). Adds `[ParallelLimiter<GitProcessSpawnLimit>]`. Keeps
  `GitBudget` at one second.
- Shared scratch. Red
  `559711c5e4cf36c9103921c1401d6a29b56cba3c` (master cherry-pick
  `b34a16d0b5d884a5f346354d4c14a2b1edc6f1c5`). Green
  `741016a6cffa7ab72a07e3975afac0045bc29d4d`, master cherry-pick
  `e02965b22564280eddc657f7385ccae00dcb9c16` (2026-11-07 17:52:48
  +0000). Each case copies one process-wide origin and clone. The
  commit message says `GitBudget` and the 80 s class timeout are
  unchanged. This is the tree run 4e0d1159 built.

## Reviews that approved it

No Review row exists for either repair. Both were commissioned as
test-side lands.

- Code be08aac7, base `5abd946dd2c235b6e873b0cbb95c738f35216dcf`. The
  brief said test-side red/green needs no separate Review, next land.
  Selection it reported: the role class 19 passed, limiter guards 21
  passed, on Linux. Plan
  `docs/superpowers/plans/2026-11-06-c0824-role-git-limiter.md` CP-1
  filter `/*/*/(C0824WorkspaceSlotRoleGateTests*)|(ProcessSpawnLimitTests*)/*`.
  The plan states comparable-load native Windows acceptance is owed to
  the next qualification and that the task must not run on the desktop.
  Scope is not Full or Interim: no Review was run. Merge 93225381
  cherry-picked the tip onto master.
- Debug 36f1c763, tip `741016a6cffa7ab72a07e3975afac0045bc29d4d`. Merge
  batch 115 (df5a54fd) names it "test-only, red 559711c5e green
  741016a6c; no separate review" and owes a Windows native rerun of
  the group-88 class. The batch's Linux final-tree filter included
  `C0824WorkspaceSlotRoleGateTests*|ProcessSpawnLimitTests*` plus the
  guard batch (policy sweep, `MechanicalPcContractTests`,
  `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`,
  `CheckpointToolProjectContractTests`). That selection cannot see a
  one-second Windows git deadline.

The original S1c test design
(`docs/superpowers/plans/2026-11-03-card-0824-s1c-test-design.md`)
CP-21 filter is
`/*/*/(C0824WorkspaceSlotRoleSettingsTests*)|(C0824WorkspaceSlotRoleGateTests*)/*`,
portable, estimated 12 minutes. That row is how the class was first
accepted, on Linux, before any Windows deadline sample existed.

## Why Linux could not see it

The class is `[Category("Integration")]`. It is outside the Unit lane.
Linux Debug 680b704d measured 158 scratch-git calls, maximum under
300 ms, against the same one-second budget, so a Linux run of the
class is green both before and after the shared scratch. The native
Windows lane that catches it is one leased run of

```text
/*/*/C0824WorkspaceSlotRoleGateTests*/*
```

on the qualification host, with `UseAppHost=false` and the unchanged
one-second `GitBudget`. b5e998ee ran that filter once, before either
repair, and recorded the deadline. The rerun owed after the limiter
land and again after batch 115 was not run before this cut. Run
4e0d1159 is the first native execution of the shared-scratch tree, and
it is the run that failed.

## Cheapest pre-land check (CARD-1782)

CARD-1782's own acceptance says a Linux impacted-class selector does
not catch Windows-only scratch-git timing; that stays on the native rc
run. The cheapest row that still forces the class back into a Code,
Review or Merge selection when the fixture changes:

- glob: `tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs`
- filter: `/*/*/C0824WorkspaceSlotRoleGateTests*/*`

A Linux execution of that filter at `3364197bf1` is 20 passed, 0
failed. It would not have turned this rc red. The check that would
have is the owed native Windows run of the same filter, which batch
115 named and which was not executed before run 4e0d1159.
