# CARD-1813 round 2 Code report (task 0e3ac6a5)

Outcome: done. Blocker D-1 and advisories A-1, A-2, and A-3 are fixed on Linux. Both previously red tests pass with only `TMPDIR` set and with an existing absolute `ANTIPHON_PWSH_CACHE_ROOT`. Official SourceLanding positive controls stay pending. Nothing was landed or deployed. Restart: none.

Worktree: `/work/worktrees/task-0e3ac6a5`. Branch: `feat/card-task-0e3ac6a5` (fast-forward from `origin/feat/card-task-2dd1792b` at `85815b63300cb4228331cc629ec30e48c7d27aa2`). Desktop checkout was not used.

## SHAs

| Role | SHA |
|---|---|
| Base | `85815b63300cb4228331cc629ec30e48c7d27aa2` |
| Non-compiling witness draft (do not cite as red) | `e348997dad8b5969bdc386bc62aa3176b7fecda4` |
| Behavioral red (seven witnesses) | `02d5e21735e6ea1cd70625df107cbd5f5f3898a7` |
| EPERM red | `1f5a05cb075a54e2258ce0f9a3af6f0befbc5e4b` |
| Fix and tested SHA | `de82fddcd1c178476e8fd40389b8b1628565afec` |

`e348997dad` did not compile (`ShouldContain` bound the extra message to `IEnumerable<char>`, and the three seams were unassigned). `02d5e21735` is the behavioral red.

Name-only range `85815b63300c..de82fddcd1` (five files, policy file absent):

- `docs/testing-and-build.md`
- `src/Antiphon.Hosting/PwshPrivateCache.cs`
- `tests/Antiphon.SessionRunner.Tests/C1793SessionPwshCacheTests.cs`
- `tests/Antiphon.Tests/Scripts/PwshPrivateCacheTests.cs`
- `tests/Antiphon.Tests/Scripts/ScriptHarnessLinuxOwnershipTests.cs`

`tests/test-execution-policy.json` is unchanged, so the nightly policyHash and the live-lane PinnedInputDigest were not refreshed. `docs/herdr-sessions.md` was not edited. No `docs/superpowers` plan or mutation file was added.

## What changed

`PwshPrivateCache.ConfiguredCacheParent()` is the parent the three test classes assert. It is independent of `ChooseRoot`: a non-whitespace `ANTIPHON_PWSH_CACHE_ROOT` is `GetFullPath` plus separator trim; otherwise the same normalization of `Path.GetTempPath()`. `Supervisor_removes_its_private_cache_before_the_group_kill` and `Each_pty_session_gets_a_host_owned_cache_that_its_exit_deletes` use that helper. `Cache_parent_lookups_use_the_shared_helper` requires the helper call in all three files and rejects a cache lookup written as `GetDirectories(Path.GetTempPath())` or `GetFullPath(Path.GetTempPath())`.

`ChooseRoot`: whitespace still uses the process temp root and sweeps. A relative path uses the temp root, does not sweep, and is not created. After `GetFullPath`, `IsUnusableRoot` (symlink, missing directory, file, reparse) uses the temp root and does not sweep. A missing absolute override is not `Directory.CreateDirectory`'d by the sweep. An existing ordinary absolute directory is used and swept. Access, argument, and Win32 exceptions fall back to temp and skip the sweep.

Missing `/proc` stat (`FileNotFoundException` or `DirectoryNotFoundException`) goes through `ProcessMayExist`. The presence hook wins when set. Otherwise Linux `kill(pid, 0)`: success means alive; any errno other than `ESRCH` (3), including `EPERM` (1), means alive and the cache is kept; `ESRCH` means gone and the cache is swept. `pid <= 0` or non-Linux with no hook is absent. Other IO and unauthorized failures stay Unknown and are kept.

`TryParseProcStartTicks` still uses `LastIndexOf(')')`. Stat `(a) b) (c)` yields start ticks 888. `IndexOf(')')` stops inside the comm and the parse returns false.

## Red, then green

All runs used a build-slot lease, native default output, `UseAppHost=false`, and `dotnet run --project tests/... --no-build`. Private dirs: `TMPDIR=/tmp/c1813r/t`. Root runs also set `ANTIPHON_PWSH_CACHE_ROOT=/tmp/c1813r/r` after that directory existed. Green rows were not rerun.

Red at `02d5e21735`, filter the seven new witnesses. Lease `26f7383e` held 9s, waited 0s. TRX `.antiphon/c1813-red/c1813-red.trx`. 7 total, 4 failed, 3 passed. Failed: `A_relative_root_override_is_unusable` (cache deleted), `A_hidden_proc_stat_keeps_a_live_owner` (cache deleted), `A_missing_stat_for_a_live_process_keeps_the_cache` (cache deleted), `A_missing_root_override_is_not_created` (`Directory.Exists` true at `/tmp/c1813r/t/c1813-miss-61KxFB/missing-root`). Passed: parenthesis parse, dead-stat sweep, helper scan.

EPERM red at `1f5a05cb07`. Build lease `2202aad3` waited 0s, held 162s, 0 errors. Test lease `8a9a8ead` held 9s, exit 2. TRX `.antiphon/c1813-eperm-red/c1813-eperm-red.trx`. 1 failed, 0 passed: `A_eperm_for_a_live_foreign_pid_keeps_the_cache` deleted the cache. `RequireEperm` reached the assertion, so `kill(1, 0)` returned EPERM on this host (uid 1654).

Fix builds at `de82fddcd1`, 0 errors. Antiphon.Tests lease `6ae5d85e-6d24-477c-8ec8-57bb128f9474` waited 0s, held 148s, 1984 warnings (pre-existing). SessionRunner.Tests lease `6537e065-fa49-4444-83de-9d6cfdf249a9` waited 0s, held 23s, 145 warnings. CS0436 on C1793 is the linked `PwshPrivateCache` copy winning over the imported SessionRunner type. Both copies read the same environment.

Green at `de82fddcd1`, before any mutant:

| Run | Result | Lease | TRX |
|---|---|---|---|
| `PwshPrivateCacheTests` + `ScriptHarnessLinuxOwnershipTests`, TMPDIR only | 43/43, 1m 17s | `fff85248-29b0-4045-98fb-21b41e3add36` waited 0s, held 80s | `.antiphon/c1813-green-tmp/c1813-classes-tmpdir.trx` |
| Same classes, TMPDIR and existing ROOT | 43/43, 1m 20s | `a92ac332-3dbc-420a-9817-c59d61aa5cd2` waited 0s, held 84s | `.antiphon/c1813-green-root/c1813-classes-root.trx` |
| `C1793SessionPwshCacheTests`, TMPDIR only | 2/2, 6s 952ms | `febc1841-bcf4-41d3-9964-e6ff85b2fce2` waited 0s, held 10s | `.antiphon/c1813-c1793-tmp/c1813-c1793-tmpdir.trx` |
| `C1793SessionPwshCacheTests`, TMPDIR and existing ROOT | 2/2, 5s 538ms | `42eb5138-11fe-498a-9787-f2b948a3e50d` waited 0s, held 8s | `.antiphon/c1813-c1793-root/c1813-c1793-root.trx` |
| Batched guards, TMPDIR and existing ROOT | 77/77, 1m 42s | `bb4514ec-11af-4076-89be-1235cc0a1d83` waited 0s, held 106s | `.antiphon/c1813-guards/c1813-guards.trx` |

Guard filter: `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/*`. Class split of that TRX: TestClassificationPolicyTests 41, MechanicalPcContractTests 11, C1729LayeringGuardTests 9, TaskPlatformGuidanceTests 5, SlowTestRegistryMergeTests 5, NativeLinuxPolicyWitnessTests 3, SlowTestTripwireTests 2, TestClassificationGuardTests 1. The review's earlier guard total was 52; MechanicalPcContractTests has grown. All eight required prefixes are in this run.

## Witness mutants

Each production mutant was its own leased build of the touched tree, then one leased `--no-build` run with `TMPDIR=/tmp/c1813r/t` and existing `ANTIPHON_PWSH_CACHE_ROOT=/tmp/c1813r/r`. Source was restored with `git checkout -- /work/worktrees/task-0e3ac6a5/src/Antiphon.Hosting/PwshPrivateCache.cs` (and the scan test file) after each kill. No mutant was committed. These runs are witness proof only. They do not discharge a SourceLanding PC.

| Mutant | Killed assertion | Build lease | Test lease | TRX |
|---|---|---|---|---|
| M1 `LastIndexOf(')')` to `IndexOf(')')` | `A_parenthesis_in_comm_keeps_the_start_field` returned false (1/1) | `8598a996` held 109s, 0 errors | `418da5ed` held 12s, exit 2 | `.antiphon/c1813-mut-indexof/c1813-mut-indexof.trx` |
| M2 ENOENT always `Absent` | hidden, live missing-stat, and EPERM caches deleted (3/3) | `568d5279` held 98s, 0 errors | `15f9b6cd` held 8s, exit 2 | `.antiphon/c1813-mut-absent/c1813-mut-absent.trx` |
| M3 ENOENT always `Unknown` | `A_missing_stat_for_a_dead_process_is_swept` left the cache (1/1) | `83f69c8f` held 114s, 0 errors | `efe62a3e` held 8s, exit 2 | `.antiphon/c1813-mut-unknown/c1813-mut-unknown.trx` |
| Scan: supervisor lookup reverted to `GetDirectories(Path.GetTempPath())` | `Cache_parent_lookups_use_the_shared_helper` (1/1). No rebuild; production source already restored | none | `dbf4a087` held 8s, exit 2 | `.antiphon/c1813-mut-scan/c1813-mut-scan.trx` |
| M4 failed `kill` returns false | `A_eperm_for_a_live_foreign_pid_keeps_the_cache` deleted the cache (1/1). Live `kill==0` and the hook path still keep | `25c3319a` held 99s, 0 errors | `64ec913d` held 7s, exit 2 | `.antiphon/c1813-mut-eperm/c1813-mut-eperm.trx` |
| M5 relative-path guard removed | `A_relative_root_override_is_unusable` deleted the relative cache (1/1) | `d8b8fac0-44c8-482a-b260-0a5b0a1291ed` waited 0s, held 101s, 0 errors | `badae73b-0a0e-4878-86ee-50502d1b96b7` waited 0s, held 11s, exit 2 | `.antiphon/c1813-mut-relative/c1813-mut-relative.trx` |
| M6 `Directory.CreateDirectory(full)` before `IsUnusableRoot` | `A_missing_root_override_is_not_created` saw the directory (1/1) | `2097ab8f-79df-4ba1-a3dc-1fa358570560` waited 0s, held 97s, 0 errors | `a3b65e06-aed5-4f2f-868c-89a5bd93ae51` waited 0s, held 10s, exit 2 | `.antiphon/c1813-mut-missing/c1813-mut-missing.trx` |

Deleting only `if (!Directory.Exists(full)) return true` does not fail the missing-root witness: `GetAttributes` throws, `ChooseRoot` catches it, and the path is never created. M6 is the mutant that creates the missing override and then uses it.

After M6 the on-disk test DLLs were the mutant. Source was restored (`git status` clean at `de82fddcd1`) and both test projects were rebuilt from that fixed source. This rebuild did not rerun tests. Antiphon.Tests lease `8b007b55-d69f-4fc2-b940-1515792434a8` waited 0s, 0 errors. SessionRunner.Tests lease `69a9e0ca-1311-403c-a7d9-1c77f350dfb1` waited 0s, held 23s, 0 errors, 145 warnings. Combined command exit 0.

## Evidence and merge at the code tip

`pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 85815b63300cb4228331cc629ec30e48c7d27aa2 -HeadRef HEAD` at `de82fddcd1` exited 0. `EVIDENCE result commits=4 entries=0 violations=0`. This was not the shallow-history exit 2.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` at `de82fddcd1` exited 0. Tree `50403ae9266f02553acf31984ff28e6f3a1b1f0f`. `origin/master` was `0cb7bfa7fcbd98e1fb537aa85dbf669395fe51a7`. No conflicts. Nothing was merged. `land-graph -Update` was not run.

This markdown file is a later commit. Evidence-diff and merge-tree are re-run against the pushed tip after it; those results are in the caller message, because they measure the commit that contains this file.

## Owed list (all still pending)

SourceLanding Mutation owns these. This round's mutant runs do not discharge them. Prior K, F, U, N, and P were killed in review `6008f08d` and were not re-run here. L survived that review; A-1 now kills the same `IndexOf` change, and the official PC remains pending.

- A-1 / L: `IndexOf(')')` instead of `LastIndexOf(')')` on a comm that contains `)` and spaces.
- A-2 live: missing stat for a live pid (`kill` returns 0) must keep.
- A-2 hook: a presence hook that returns true must keep even when the real pid is already dead.
- A-2 EPERM: `kill` failure with errno other than `ESRCH` (pid 1, EPERM) must keep.
- A-2 dead: missing stat and `ESRCH` must sweep. Always-Unknown must fail this witness.
- A-3 relative: a relative override must not be swept or used.
- A-3 missing: the sweep must not create a missing absolute override. The proving mutant is `Directory.CreateDirectory` before `IsUnusableRoot`.
- D-1 scan: cache-parent lookups in the three classes go through `ConfiguredCacheParent()`.
- K: kernel-ticks compare forced true.
- F: a future stamp counted as current.
- U: `IsUnusableRoot` forced false.
- N: an unknown start read treated as dead.
- P: parse reads `fields[20]` (vsize) instead of `fields[19]` (start ticks).

A-4, Windows execution of the new tests, stays owed to rc qualification. It is not a code change in this task. hidepid cannot be remounted here (empty capabilities). The EPERM, live-pid, and hook witnesses cover that keep path on this host.

No checkpoint table was in this repair brief. Ordinary verification is the closed class runs, the batched guards, evidence-diff, and merge-tree above. The Unit lane was not run.
