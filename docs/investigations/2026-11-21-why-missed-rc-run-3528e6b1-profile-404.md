# Why Windows rc run 3528e6b1 failed profile-404

Windows Full on `rc/2026-11-30`, source
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0`, reported one failure in
`antiphon-all-profile-404.trx`. `C0824DesktopLegacyReleasePolicyTests.Double_run_is_idempotent(concurrent)`
failed after 13.5 s because one of the original checkout paths still existed.
The supplied extract omits the candidate rows and the retirement reason. The
desktop TRX was unavailable from the Linux mirror, so the current chunk's
sequential case membership and verdict are unverified.

## What can leave the original checkout in place

`DesktopLegacyWorld` uses `git worktree add` and commits to the linked worktree.
It has a `.git` pointer file. Its loose objects and registration live in the
fixture repository's common Git directory. Group 137 exercised standalone
scratch repositories that contained their own loose-object store.

The guarded removal first moves the checkout aside, then moves its registration
out of the common Git directory's `worktrees/`, then unlinks the set-aside
checkout. The Windows delete already opens entries without following reparse
points, uses extended disposition with `IGNORE_READONLY_ATTRIBUTE`, and retries
bounded sharing failures. The classic disposition fallback lacks that flag,
but a delete failure at either disposition leaves the checkout under the
set-aside name. The existing path-length gate refuses before move-aside. This
fixture creates only short leaf names and ordinary files.

The concurrent case invokes the service directly, outside Hangfire's
`DisableConcurrentExecution` on the periodic job. The repository lease excludes
another mutation, while the other policy scope can still read Git metadata.
A Windows handle that blocks the original checkout move, or one that blocks
the registration move after set-aside, can leave the original path in place.
For a failed registration move, the guard reads the still-live registration
and restores the checkout. The checkout move already retries sharing failures.
The registration move made one `Directory.Move` call. A sharing error there
matches the assertion. The original Windows reason is still needed
to distinguish it from another pre-move refusal, a lease deferral, or a
different checkout restore.

## Why earlier checks missed it

- CARD-0824 S4a introduced V-22 in
  `d883c22e92bbd5d6d5333d9d7fab670f3f1c200b`. The test requires one release, one
  attempt, one command intent and both trees retired after the two ticks.
  Its sequential case repeats the tick only after the first tick completes.
- The single registration rename dates to CARD-0665
  `5eb15073c4d55caf4e66db32340c250323c146ed`. Later Windows sharing coverage in
  CARD-0448 exercised the worktree move, receipt replacement and set-aside
  delete. It had no registration-move sharing case.
- The previous Windows group-77 investigation at
  `546921b0dc33e32c46d96e7f75bcdfcb2039f9d0` records concurrent and sequential
  as passing in run `766e3df5`. The previous completed Windows Full
  `e7f92f66` does not list this method as a failure. The relevant fixture,
  policy and Git removal files are identical between its source
  `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248` and the failing cut.
  Earlier Linux review receipts also passed all three modes.
- The retained evidence establishes an intermittent Windows observation
  across runs. It does not establish which operation failed on the original
  Windows host or an introducing commit for that observation.

## Repair and verification

`LandingGit.UnregisterWorktreeAsync` now uses the existing Windows sharing
classification and retry budget for the registration move. It tries at most
five times, 100 ms apart. Before a retry it checks the registration pointer,
lock, original checkout path and live caller authorization. It only prepares
the retired destination once. A persistent conflict still follows the
existing restoration path. A switch flipped during the pause keeps the
registration and restores the original checkout.

The portable regression injects Win32 sharing error 32 at the registration move
inside the real retirement path. It verifies a transient failure retires the
checkout, a persistent failure restores it after exactly five attempts, and a
switch flipped during the pause forbids a second move. The positive control
includes a tracked read-only file. On Linux that attribute removes write bits,
and unlink depends on the parent directory. The Linux control establishes
retirement with that fixture and the injected Win32 result. Native Windows
sharing and read-only disposition remain owed.

The confinement cases verify an absolute path outside the configured root
is held without examining the tree or filing a release, and that a directory
link swapped into the set-aside checkout leaves the outside target's marker
unchanged. The concurrent case now emits both ticks' candidate outcomes and
reason codes if a checkout survives.

The Linux class checkpoint on pre-fix source
`bb905d240412a7ab648c72bc3ff7bd12e15f79ed` built with a granted slot, build
start load 13.77, `ANTIPHON_LINUX_COMPILER_POLICY=0` and a 570 s test timeout.
It executed 141 cases in 2m 24s, with 137 passed, 4 failed and 0 skipped. The
original concurrent, sequential and residue-sweep-first cases all passed,
as did the confinement cases. The three injected sharing cases failed as
designed. The transient and switch-off cases got `worktree_remove_failed`
and the persistent case got one move instead of five. The fourth failure
was the unchanged Linux-only
`Status_that_cannot_read_a_posix_directory_holds_it`, whose row was
`Refused` where it expected `Held`. This checkpoint did not establish
the original rc Windows cause or reproduce the distinct POSIX failure at
the unchanged rc SHA.

The focused Linux checkpoint on `37853f19ace54b8e7d03f102153c8532974bb294`
passed all 15 cases in 34 s. It covered the three new retry cases, the
three original idempotence cases, the path and late-link controls, and
`Late_switch_inside_unregister_keeps_the_registration`. Its build started
at load 13.17 on a granted slot, and the checkpoint tool validated the
report against the clean source SHA.

Two preliminary checkpoint runs were stopped. One was waiting for
a build slot when load exceeded the limit. In the other the checkpoint
child bypassed a PATH-based launch guard and started compiling without a
below-limit sample. A subsequent row refused to start compilation at load
15.34. These attempts executed zero tests. The completed foreground rows
used an explicit launcher that checked load immediately before compilation
inside the granted slot.

```text
CHECKPOINT CP-1 commit=bb905d240412a7ab648c72bc3ff7bd12e15f79ed build=ok filter=/*/Antiphon.Tests.Application/C0824DesktopLegacyReleasePolicyTests/* executed=141 passed=137 failed=4 skipped=0 trx=/work/worktrees/ws-15/.antiphon/checkpoints/CP-1-20261121-140054-f8a0/run.trx slot=granted waited=0s dirty=0 source=bb905d240412a7ab648c72bc3ff7bd12e15f79ed sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=37853f19ace54b8e7d03f102153c8532974bb294 build=ok filter=/*/Antiphon.Tests.Application/(C0824DesktopLegacyReleasePolicyTests*)|(C0824DesktopLegacyReleaseSwitchTests*)/(Double_run_is_idempotent*)|(Path_escapes_are_refused*)|(Registration_move_*)|(Late_switch_inside_unregister_keeps_the_registration*) executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/ws-15/.antiphon/checkpoints/CP-2-20261121-141053-1c1c/run.trx slot=granted waited=0s dirty=0 source=37853f19ace54b8e7d03f102153c8532974bb294 sourceState=clean buildSource=verified
```

## Windows qualification owed

Include the profile-404 cases in the shared Windows pass with C475,
`CheckpointIndexPathsTests`, the shadow tests and group 138's two cases.
Use the existing profile-404 TRX to confirm the sequential arm's membership
and verdict, and retain the next run's failure reason if the concurrent arm
leaves a checkout. The narrow Windows selection is
`C0824DesktopLegacyReleasePolicyTests` methods `Double_run_is_idempotent`,
`Registration_move_retries_only_while_authorized`,
`Registration_move_leaves_a_late_directory_links_target_intact` and
`Path_escapes_are_refused`. The native
`C0824DesktopLegacyReleaseNativeTests.Held_directory_defers_never_forces`
control exercises a real open handle.

The cheapest pre-land check for edits to
`server/Infrastructure/Git/LandingGit.cs` is the exact
`/*/*/C0824DesktopLegacyReleasePolicyTests/Registration_move_retries_only_while_authorized`
filter, paired with V-22 on Windows. The qualification gate remains incomplete
until native results are available.
