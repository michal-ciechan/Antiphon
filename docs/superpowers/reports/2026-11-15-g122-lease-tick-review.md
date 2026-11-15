# Final review: g122 Hangfire lease single-flight tick

CARD-1514, review round 1, Final. Subject `feat/card-task-7ac938e9` at `b9cf498c77d40bf14260cb6003ba0a8c5610cf76` (Debug task `7ac938e9-a45b-48e2-a65c-0088f3d5ec79`). Base `64e3d78c487d51570f708492ab67b5db6d0de9a9`. No plan `### Checkpoints` table exists for this repair; the brief's named classes are the closed selection. Verdict: no product defect.

## Single-flight

`OnTick` takes `_ticking` with `Interlocked.Exchange` and clears it with `Volatile.Write` in `finally`. A `return` from the catch (disposed, or still inside the crash-expiry window) still runs `finally`, so an exception or a stop during a tick does not leave supervision stuck off. A tick that never returns keeps the flag, which is the same stuck call the old lock pile-up could not get past.

Crash expiry stays `TimeProvider.GetElapsedTime` from the last confirmed hold (`CrashExpirySeconds` = 60), not a tick count. The in-flight tick checks that window in its own catch, so a skipped period does not push the host stop past the end of the tick that is running when the window expires. Production period is 15s and command timeout is 5s, so a normal refused connect does not overlap the next period. The 50ms test period does, which is what the new witness holds open.

`Dispose` drops the timer without waiting, then takes `_gate` and releases the connection. `EnsureHeld` rechecks `_disposed` under that lock. `StopHost` runs only after `EnsureHeld` has left the lock. `ProbeEachTick` is internal, null unless a test sets it, and production has no caller.

The red commit `015f20f0ee737ba291f361948c4c30e8621ca406` adds the probe and not the exchange, so the witness's `maxInside == 1` assertion can fail. This review did not re-execute that red commit. The Windows Debug run is the red evidence.

## Linux execution at b9cf498c

Build `tests/Antiphon.Tests` via `scripts/build-slot.ps1`, `UseAppHost=false`, slot waited 0s, 0 errors. Each run below is its own lease, `--no-build`.

| Selection | Result |
|---|---|
| `A_slow_tick_does_not_overlap_the_next_tick` x5 | 1/1, 1/1, 1/1, 1/1, 1/1 (6.4s, 9.8s, 5.7s, 7.2s, 5.0s) |
| `HangfireStorageSelectionTests` | 13/13 in 7.3s |
| `HangfireLeaseDocumentationTests` | 2/2 in 4.9s |
| `HangfirePostgreSqlStorageTests` | 22/22 in 35.1s |
| `TestClassificationPolicyTests` | 42/42 in 1m19s (slot waited 90s) |
| `TestClassificationGuardTests` | 1/1 in 5.6s (slot waited 60s) |
| `SlowTestRegistryMergeTests` | 5/5 in 14.5s |
| `SlowTestTripwireTests` | 2/2 in 4.5s |
| `NativeLinuxPolicyWitnessTests` | 3/3 in 6.8s |

There is no class `HangfireDurableStorageTests`. Those three classes are the types in `HangfireDurableStorageTests.cs`.

## Compose on origin/master 4ce55f36794f3e2fa904d56ba980f4182d89ab3a

Cherry-pick of the three subject commits was clean (`9a178c393e1184f9c3459b6ef190d602f2139587`). `regenerate-derived-impact-index.ps1` twice: unchanged, then unchanged. Same on the reviewed tip. policyHash `b610138753e25545cda6f208abe19ebd507cdc77afc288c9888b778ad1ff6b9d`. PinnedInputDigest `26d90df028d4e2358ed43b02aa7ef4ad688224c5470aba371d2fd566ff4a663e`. `NativeLinuxPolicyWitnessTests` recomputed `LegacyExclusionDigest` and passed.

Compose guard union, all exit 0:

| Class | Result |
|---|---|
| `TestClassificationPolicyTests` | 42/42, 1m19s, slot waited 15s |
| `TestClassificationGuardTests` | 1/1, 11s, slot waited 15s |
| `SlowTestRegistryMergeTests` | 5/5, 13s |
| `SlowTestTripwireTests` | 2/2, 11s |
| `NativeLinuxPolicyWitnessTests` | 3/3, 16s |
| `DerivedImpactIndexTests` | 27/27, 4m06s |
| `MechanicalPcContractTests` | 11/11, 4.7s |
| `TaskPlatformGuidanceTests` | 5/5, 5.0s |
| `C1729LayeringGuardTests` | 11/11, 14s, slot waited 45s |
| `TestLaneCategoryGuardTests` | 1/1, 11s |
| `WindowsUnitPlatformAdmissionTests` | 6 passed, 1 skipped, 7 total, 27s |

The skip is `C1210_Windows_linux_only_outcomes_are_exact` (Windows placement). It is not a Linux pass.

## Other checks

- `check-evidence-diff.ps1` base `64e3d78c487d51570f708492ab67b5db6d0de9a9` head `b9cf498c77d40bf14260cb6003ba0a8c5610cf76`: 3 commits, 0 violations.
- `git merge-tree --write-tree origin/master b9cf498c77d40bf14260cb6003ba0a8c5610cf76` exit 0, tree `9f6f72186692700a5d7fef915b99929070968b37`.
- `impacted-classes.ps1 -MergeSubset`: `migrations=False` (downgrade selector count 0). Direct derived classes include `HangfireStorageSelectionTests`.
- OS diff: no OS-gated branch. The portable witness is `A_slow_tick_does_not_overlap_the_next_tick`. Introducing commit `cbc08c44f6d43b8f0c9017878749d89cdd09e059` is an ancestor and contains the 50ms supervision start.

## Owed

Windows re-run of the three group-122 victims stays owed. The Debug Windows run is the evidence (4/4 after the fix, then 15/15). Mutation positive controls stay pending (operator waiver 2026-11-12). `reviewedSourceClean` is false: this repair has no checkpoint manifest, and these runs are build-slot leases rather than checkpoint-tool receipts.
